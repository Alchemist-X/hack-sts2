"""UI revisit cycles: verified zero-progress rest-site / shop prompt loops. Counted in training rollouts and in
evaluation (counting only); ``--cycle-cost c`` charges the trainer's reward of the transition that closes one.

Why (2026-11-04 loop-breaker diagnosis, u2225 on silent-a10-dev-v1): 176 of the 177 eval loop-breaker interventions
were RestSite ``SMITH`` -> ``PromptSelect`` -> ``PromptCancel`` back to the byte-identical rest-site observation, one
was Shop card removal -> ``PromptSelect`` -> ``PromptCancel``. The policy has no memory, the revisit's input is the
same, so a greedy policy repeats the loop until the floor step cap ends the game ``stuck``; in training the loop costs
nothing (the progress reward and the acquisition shaping are 0 on every step of it).

Definition. A *UI revisit cycle* is completed at a decision whose observation

1. is a RestSite or Shop decision (``global_i.decision_kind``, not in combat; ``ANCHOR_PHASES``) whose digest of the
   27 model-read buffers (``loopbreak.ObsDigest``, the loop breaker's digest) equals that of an earlier RestSite /
   Shop decision of the same episode and floor at most ``window`` (64) decisions back, and
2. every decision in between (at least one) was a prompt screen of that room: ``decision_kind`` Prompt with
   ``prompt_kind`` in ``PROMPT_KINDS`` (DeckUpgrade, DeckRemove), out of combat, entered from that room observation
   (``global_i.phase`` is Prompt as well while a prompt is pending; the room's tables stay in the observation), and
3. none of those prompt screens carried new information (``CycleDetector.verify_prompt``): the prompt observation
   equals the room observation in every model-read buffer except the prompt UI (``PROMPT_UI_COLS`` of global_i,
   ``prompt_options``, ``cand``, ``cand_n``), so it is the same room (floor, map position, rest / shop tables, HP,
   gold, deck, relics, potions); its options are cards of the deck that the room observation already shows
   ((item_id, upgrade) of every option a sub-multiset of the deck's (card_id, upgrade) rows, kind ``card``, no table
   overflow); its candidates are only ``PromptSelect`` of a listed option (same card id), ``PromptConfirm`` and
   ``PromptCancel``.

Equal digests imply no room progress and no HP / gold / deck / relic / potion change (all of them are model inputs);
the prompt-only path implies no fight. Each completed cycle is counted once, at the decision that closes it (the
revisit); the chain is cut back to the revisited entry, so going round again is a second cycle. Not cycles: a no-op
(the same room observation right after an action at it), a path through any other screen (map, combat, rewards, event,
another room observation: the excursion is reset), an event cancel, a confirmed upgrade or removal (the deck changes,
so the digest does), a prompt kind outside ``PROMPT_KINDS`` or a prompt screen that fails the check
(``rejected`` counters: ``no_op``, ``unverified_prompt``, ``room_change``).

``CycleDetector`` per env slot: ``observe(obs, live)`` at every observation a slot decides at (-> bool [N]: the slots
whose arrival here closed a cycle), ``commit(actions, obs)`` with the actions taken there (labels for the path),
``reset(slots)`` / ``finish(done)`` at episode ends. Trainer hook: ``after_step(obs, done, finished)`` right after
``env.step`` (ends the finished episodes, puts each one's count in its ``finished`` dict as ``cycles``, observes the
new observations) -> the closing mask of that transition. ``pop()`` -> counts and events since the last call (per
update), ``slot_totals()`` (per game in an evaluation). No masking: the detector never changes an action.
"""

from __future__ import annotations

import hashlib
from collections import Counter

import numpy as np

from .features import USED_BUFFERS, ModelSpec
from .loopbreak import ObsDigest

ANCHOR_PHASES = ("RestSite", "Shop")
PROMPT_KINDS = ("DeckUpgrade", "DeckRemove")      # verified to carry no new information (docs: the q-modes memo)
PROMPT_ACTIONS = ("PromptSelect", "PromptConfirm", "PromptCancel")
# global_i columns that describe the pending prompt (phase / decision_kind are Prompt while one is pending): prompt UI
PROMPT_UI_COLS = ("phase", "decision_kind", "prompt_kind", "prompt_min", "prompt_max", "prompt_picked",
                  "prompt_needs_confirm", "prompt_can_cancel", "prompt_can_skip", "prompt_ordered", "prompt_option_n",
                  "prompt_pile", "prompt_site_hash")
UI_BUFFERS = ("prompt_options", "cand", "cand_n")
WINDOW = 64
REJECT_REASONS = ("no_op", "unverified_prompt", "room_change")
COUNT_KEYS = ("cycles", "cycles_rest", "cycles_shop", "cycle_steps") + tuple(f"rejected_{r}" for r in REJECT_REASONS)
ANCHOR, PROMPT = 1, 2
# chain entry fields
_D, _CORE, _CLS, _OK, _ACT, _LAB, _PH, _PK, _STEP = range(9)


class PartDigest(ObsDigest):
    """`ObsDigest` split by buffer: ``parts(obs, idx)`` -> uint64 [m, buffers] whose row sums (mod 2^64) are exactly
    the digests ``ObsDigest()(obs, idx)`` (same keys); ``masked`` = one buffer's term with some columns zeroed."""

    def parts(self, obs: dict, idx: np.ndarray) -> np.ndarray:
        m = len(idx)
        out = np.zeros((m, len(self.names)), dtype=np.uint64)
        if m == 0:
            return out
        for j, name in enumerate(self.names):
            b = np.ascontiguousarray(obs[name][idx]).reshape(m, -1).view(np.uint8)
            w = b.view(np.uint32) if b.shape[1] % 4 == 0 else b
            out[:, j] = w.astype(np.uint64) @ self._key(j, w.shape[1])
        return out

    def masked(self, j: int, a: np.ndarray, cols) -> np.ndarray:
        """Digest term of buffer `j` (rows `a`, [m, ...]) with the columns `cols` of its last axis zeroed."""
        a = np.array(a, copy=True)
        a[..., list(cols)] = 0
        m = len(a)
        b = np.ascontiguousarray(a).reshape(m, -1).view(np.uint8)
        w = b.view(np.uint32) if b.shape[1] % 4 == 0 else b
        return w.astype(np.uint64) @ self._key(j, w.shape[1])


def sample_rng(seed: int, key: str, repeat: int = 0) -> np.random.Generator:
    """The action RNG of a sampled evaluation play: sha256("sts2train-sample:<seed>:<game key>:<repeat>"), independent
    of slot order and batch (the per-game generator rule of ``sts2vec.policies.game_rng``)."""
    h = hashlib.sha256(f"sts2train-sample:{int(seed)}:{key}:{int(repeat)}".encode("utf-8")).digest()
    return np.random.default_rng(int.from_bytes(h[:8], "little"))


class CycleDetector:
    """Per-slot detection of completed UI revisit cycles (module docstring). Built from the library's spec (the
    column maps and enum members the observations follow; enums resolved by name). `card_name` (id -> name) only
    makes the logged paths readable. `keep_events`: also keep every slot's events (``slot_totals``; evaluation)."""

    def __init__(self, spec: ModelSpec, num_envs: int | None = None, *, window: int = WINDOW, card_name=None,
                 keep_events: bool = False):
        if int(window) < 2:
            raise ValueError(f"cycle window {window} must be >= 2")
        G = spec.cols["global_i"]
        self._dk, self._floor = G["decision_kind"], G["total_floor"]
        self._act, self._pk, self._combat = G["act_index"], G["prompt_kind"], G["is_combat"]
        self._opt_n, self._deck_n = G["prompt_option_n"], G["deck_n"]
        self._ui_cols = tuple(G[c] for c in PROMPT_UI_COLS if c in G)
        en = spec.enum_names
        self.phase_names = list(en["RunPhase"])
        self.pkind_names = list(en["ChoicePromptKind"])
        self.kind_names = list(en["RunActionKind"])
        self.rest_names = list(en.get("RestOption") or [])
        self.shop_names = list(en.get("ShopCategory") or [])
        self.anchor_ids = np.array([self.phase_names.index(p) for p in ANCHOR_PHASES])
        self.prompt_phase = self.phase_names.index("Prompt")
        self.prompt_kind_ids = np.array([self.pkind_names.index(k) for k in PROMPT_KINDS if k in self.pkind_names])
        self.prompt_action_ids = np.array([self.kind_names.index(k) for k in PROMPT_ACTIONS])
        self.select_id = self.kind_names.index("PromptSelect")
        self.card_item = list(en["PromptItemKind"]).index("card")
        self.C, self.PO, self.D = spec.cols["cand"], spec.cols["prompt_options"], spec.cols["deck"]
        self.R, self.S = spec.cols["rest"], spec.cols["shop"]
        self.window = int(window)
        self.card_name = card_name
        self.keep_events = bool(keep_events)
        self.digest = PartDigest()
        self._jg = USED_BUFFERS.index("global_i")
        self._core_j = np.array([j for j, b in enumerate(USED_BUFFERS) if b not in UI_BUFFERS and b != "global_i"])
        self.n = 0
        self.totals: Counter = Counter()
        self._popped: Counter = Counter()
        self.events: list[dict] = []
        if num_envs:
            self._alloc(int(num_envs))

    # ------------------------------------------------------------------------------------------- state

    def _alloc(self, n: int) -> None:
        self.n = n
        self.chains: list[list[list]] = [[] for _ in range(n)]
        self.floor = np.full(n, -1, dtype=np.int64)
        self.active = np.zeros(n, dtype=bool)          # the slot's chain is not empty
        self.pending = np.zeros(n, dtype=bool)         # observed at the last observe(), action not committed yet
        self.decisions = np.zeros(n, dtype=np.int64)   # decisions observed in the slot's current episode
        self.ep_cycles = np.zeros(n, dtype=np.int64)   # cycles in the slot's current episode
        self.slot_cycles = np.zeros(n, dtype=np.int64)  # cycles over every episode of the slot (eval: the game)
        self.slot_rest = np.zeros(n, dtype=np.int64)
        self.slot_shop = np.zeros(n, dtype=np.int64)
        self.slot_events: list[list[dict]] = [[] for _ in range(n)]

    def _clear(self, s: int) -> None:
        self.chains[s].clear()
        self.active[s] = False
        self.floor[s] = -1

    def reset(self, slots) -> None:
        """New episodes on `slots` (bool mask or indices): forget their chains and episode counts."""
        if self.n == 0:
            return
        sl = np.asarray(slots)
        sl = np.flatnonzero(sl) if sl.dtype == bool else sl.astype(np.int64)
        for s in sl.tolist():
            self._clear(int(s))
        self.pending[sl] = False
        self.decisions[sl] = 0
        self.ep_cycles[sl] = 0

    def finish(self, done) -> dict[int, int]:
        """End the episodes of the `done` slots: {slot: cycles in the ended episode}, then `reset` them."""
        done = np.asarray(done, dtype=bool)
        if self.n == 0 or not done.any():
            return {}
        out = {int(s): int(self.ep_cycles[s]) for s in np.flatnonzero(done)}
        self.reset(done)
        return out

    # ------------------------------------------------------------------------------------------- per step

    def classify(self, obs: dict, live=None) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """(deciding, room, prompt) bool [N]: slots that decide now; at a RestSite / Shop decision; at a prompt of
        `PROMPT_KINDS` (out of combat; whether it belongs to the room is checked against the room observation)."""
        cand_n = np.asarray(obs["cand_n"])
        gi = obs["global_i"]
        dec = cand_n > 0
        if live is not None:
            dec &= np.asarray(live, dtype=bool)
        calm = gi[:, self._combat] == 0
        dk = gi[:, self._dk]
        room = dec & calm & np.isin(dk, self.anchor_ids)
        prompt = dec & calm & (dk == self.prompt_phase) & np.isin(gi[:, self._pk], self.prompt_kind_ids)
        return dec, room, prompt

    def core(self, obs: dict, idx: np.ndarray, parts: np.ndarray) -> np.ndarray:
        """Digest of the slots `idx` without the prompt UI (global_i's PROMPT_UI_COLS zeroed, UI_BUFFERS left out)."""
        if len(idx) == 0:
            return np.zeros(0, dtype=np.uint64)
        c = parts[:, self._core_j].sum(1, dtype=np.uint64)
        return c + self.digest.masked(self._jg, obs["global_i"][idx], self._ui_cols)

    def observe(self, obs: dict, live=None, *, episode_ids=None, **tag) -> np.ndarray:
        """Process the observation every deciding slot is at (``cand_n > 0`` and `live`): -> bool [N], True where
        arriving at it completed a cycle. `episode_ids` ([N]) and `tag` (e.g. the rollout step) go into the events."""
        cand_n = np.asarray(obs["cand_n"])
        n = len(cand_n)
        if self.n == 0:
            self._alloc(n)
        elif self.n != n:
            raise ValueError(f"cycle detector sized for {self.n} slots, called with {n}")
        closed = np.zeros(n, dtype=bool)
        self.pending[:] = False
        dec, room, prompt = self.classify(obs, live)
        rel = room | prompt
        for s in np.flatnonzero(dec & ~rel & self.active).tolist():
            self._clear(int(s))                       # any other screen ends the excursion
        idx = np.flatnonzero(rel)
        if len(idx):
            gi = obs["global_i"]
            parts = self.digest.parts(obs, idx)
            full = parts.sum(1, dtype=np.uint64)
            core = self.core(obs, idx, parts)
            for r, s in enumerate(idx.tolist()):
                closed[s] = self._visit(obs, s, int(full[r]), int(core[r]), bool(room[s]), gi, episode_ids, tag)
        self.decisions[dec] += 1
        return closed

    def _visit(self, obs, s: int, d: int, c: int, is_room: bool, gi, episode_ids, tag) -> bool:
        ch = self.chains[s]
        fl = int(gi[s, self._floor])
        if ch and self.floor[s] != fl:
            ch.clear()
        self.floor[s] = fl
        self.active[s] = True
        self.pending[s] = True
        step = int(self.decisions[s])
        if not is_room:
            anchor = next((e for e in reversed(ch) if e[_CLS] == ANCHOR), None)
            ok = anchor is not None and anchor[_CORE] == c and self.verify_prompt(obs, s)
            ch.append([d, c, PROMPT, ok, -1, None, "Prompt", self.pkind_names[int(gi[s, self._pk])], step])
            if len(ch) > self.window:
                del ch[: len(ch) - self.window]
            return False
        k = next((j for j in range(len(ch) - 1, -1, -1) if ch[j][_CLS] == ANCHOR and ch[j][_D] == d), None)
        closed = False
        if k is not None:
            between = ch[k + 1:]
            if not between:
                reason = "no_op"
            elif any(e[_CLS] != PROMPT for e in between):
                reason = "room_change"
            elif not all(e[_OK] for e in between):
                reason = "unverified_prompt"
            else:
                reason = None
            if reason is None:
                closed = True
                self._record(s, ch[k:], fl, int(gi[s, self._act]) + 1, step, episode_ids, tag)
            else:
                self.totals[f"rejected_{reason}"] += 1
            del ch[k:]
        ch.append([d, c, ANCHOR, True, -1, None, self.phase_names[int(gi[s, self._dk])], None, step])
        if len(ch) > self.window:
            del ch[: len(ch) - self.window]
        return closed

    def _record(self, s: int, seg: list, floor: int, act: int, step: int, episode_ids, tag) -> None:
        phase = seg[0][_PH]
        rest = phase == "RestSite"
        ev = {**tag, "slot": s, "phase": phase,
              "prompt": sorted({e[_PK] for e in seg[1:] if e[_PK]}), "act": act, "floor": floor, "len": len(seg),
              "step": step, "path": [e[_LAB] if e[_LAB] is not None else str(e[_ACT]) for e in seg],
              "actions": [int(e[_ACT]) for e in seg]}
        if episode_ids is not None:
            ev["episode"] = int(np.asarray(episode_ids)[s])
        self.totals["cycles"] += 1
        self.totals["cycles_rest" if rest else "cycles_shop"] += 1
        self.totals["cycle_steps"] += len(seg)
        self.ep_cycles[s] += 1
        self.slot_cycles[s] += 1
        (self.slot_rest if rest else self.slot_shop)[s] += 1
        self.events.append(ev)
        if self.keep_events:
            self.slot_events[s].append(ev)

    def commit(self, actions, obs: dict | None = None) -> None:
        """The actions taken at the observations of the last `observe` (-1 = none); `obs` (still that observation)
        labels them for the logged path."""
        a = np.asarray(actions)
        for s in np.flatnonzero(self.pending).tolist():
            e = self.chains[s][-1]
            e[_ACT] = int(a[s])
            if obs is not None:
                e[_LAB] = self.label(obs, s, int(a[s]))
        self.pending[:] = False

    def after_step(self, obs: dict, done, finished: list[dict] | None = None, *, episode_ids=None,
                   **tag) -> np.ndarray:
        """Trainer hook right after ``env.step`` (auto-reset): end the `done` slots' episodes (their counts go into
        the `finished` dicts as ``cycles``), then `observe` the new observations -> float32 [N], 1.0 where the
        transition just taken closed a cycle."""
        ended = self.finish(done)
        for e in finished or ():
            e["cycles"] = ended.get(int(e["slot"]), 0)
        return self.observe(obs, episode_ids=episode_ids, **tag).astype(np.float32)

    # ------------------------------------------------------------------------------------------- checks / labels

    def verify_prompt(self, obs: dict, s: int) -> bool:
        """Condition 3 of the module docstring for slot `s` at a prompt (the core-digest half is in `_visit`): the
        options are deck cards (no overflow), the candidates only select a listed option, confirm or cancel."""
        gi = obs["global_i"][s]
        po = obs["prompt_options"][s]
        n_opt = int(gi[self._opt_n])
        deck = obs["deck"][s]
        if n_opt > po.shape[0] or int(gi[self._deck_n]) > deck.shape[0]:
            return False
        rows = po[:n_opt]
        if n_opt and (rows[:, self.PO["kind_of_item"]] != self.card_item).any():
            return False
        have = Counter(zip(deck[:, self.D["card_id"]].tolist(), deck[:, self.D["upgrade"]].tolist()))
        have.pop((0, 0), None)
        need = Counter(zip(rows[:, self.PO["item_id"]].tolist(), rows[:, self.PO["upgrade"]].tolist()))
        if any(v > have.get(k, 0) for k, v in need.items()):
            return False
        cn = int(obs["cand_n"][s])
        cand = obs["cand"][s, :cn]
        kinds = cand[:, self.C["kind"]]
        if not np.isin(kinds, self.prompt_action_ids).all():
            return False
        sel = cand[kinds == self.select_id]
        oi = sel[:, self.C["option_index"]]
        if ((oi < 0) | (oi >= n_opt)).any():
            return False
        return bool((sel[:, self.C["card_id"]] == rows[oi, self.PO["item_id"]]).all())

    def _card(self, cid: int) -> str:
        if self.card_name is not None:
            try:
                return str(self.card_name(int(cid)))
            except Exception:
                pass
        return f"card{int(cid)}"

    def label(self, obs: dict, s: int, a: int) -> str:
        """Short name of action `a` of slot `s`: ``rest:SMITH``, ``buy:card_removal``, ``select:<card>[+]``,
        ``confirm``, ``cancel``, else the RunActionKind name."""
        if a < 0 or a >= int(obs["cand_n"][s]):
            return f"illegal:{a}"
        row = obs["cand"][s, a]
        k = self.kind_names[int(row[self.C["kind"]])]
        oi = int(row[self.C["option_index"]])
        try:
            # a room macro row (env macros: B = the deck row >= 0, card_id = its card) ends in ":<card>"; a game
            # ChooseRestOption / BuyEntry row has B = -1 and card_id 0, so its label is the one of earlier builds
            cid = int(row[self.C["card_id"]])
            card = (":" + self._card(cid)) if cid > 0 and int(row[self.C["B"]]) >= 0 else ""
            if k == "ChooseRestOption" and 0 <= oi < obs["rest"].shape[1] and self.rest_names:
                return "rest:" + self.rest_names[int(obs["rest"][s, oi, self.R["option_id"]])] + card
            if k == "BuyEntry" and 0 <= oi < obs["shop"].shape[1] and self.shop_names:
                cat = self.shop_names[int(obs["shop"][s, oi, self.S["category"]])]
                item = int(obs["shop"][s, oi, self.S["item_id"]])
                return f"buy:{cat}{card}" if cat == "card_removal" else f"buy:{cat}:{item}"
            if k == "PromptSelect" and 0 <= oi < obs["prompt_options"].shape[1]:
                po = obs["prompt_options"][s, oi]
                return "select:" + self._card(int(po[self.PO["item_id"]])) + "+" * int(po[self.PO["upgrade"]])
        except (IndexError, ValueError):
            pass
        return {"PromptConfirm": "confirm", "PromptCancel": "cancel"}.get(k, k)

    # ------------------------------------------------------------------------------------------- reporting

    def pop(self) -> tuple[dict, list[dict]]:
        """(counts since the previous call: COUNT_KEYS, the events since the previous call)."""
        cur = Counter(self.totals)
        d = {k: int(cur.get(k, 0) - self._popped.get(k, 0)) for k in COUNT_KEYS}
        self._popped = cur
        ev, self.events = self.events, []
        return d, ev

    def totals_dict(self) -> dict:
        return {k: int(self.totals.get(k, 0)) for k in COUNT_KEYS}

    def slot_totals(self) -> list[dict]:
        """Per slot (an evaluation's game): cycles, by room, and the events (with `keep_events`)."""
        if self.n == 0:
            return []
        return [{"cycles": int(self.slot_cycles[s]), "cycles_rest": int(self.slot_rest[s]),
                 "cycles_shop": int(self.slot_shop[s]), "cycle_events": list(self.slot_events[s])}
                for s in range(self.n)]


def update_stats(counts: dict, finished: list[dict], cost: float = 0.0) -> dict:
    """The per-update metrics of the trainer: closed cycles in this update's transitions (`counts` from
    ``CycleDetector.pop``), the finished episodes with >= 1 cycle and the most in one episode, the cost charged."""
    per = [int(e.get("cycles", 0)) for e in finished]
    return {"cycles_total": counts["cycles"], "cycles_rest": counts["cycles_rest"],
            "cycles_shop": counts["cycles_shop"], "cycle_steps": counts["cycle_steps"],
            "cycles_rejected": sum(counts[f"rejected_{r}"] for r in REJECT_REASONS),
            "episodes_with_cycle": sum(1 for x in per if x > 0), "cycle_max_per_episode": max(per, default=0),
            "cycle_cost_total": round(float(cost) * counts["cycles"], 9)}
