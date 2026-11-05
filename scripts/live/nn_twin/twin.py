"""The simulator twin: one ``sts2vec`` slot that mirrors the game, plus the checkpoint's policy on it.

The twin is the network's only input: the checkpoint reads the twin's observation buffers (exactly what it was trained
on), its chosen candidate row is translated to a semantic `Act` and matched against the game's candidates, and the twin
is stepped with the same row. Twin act keys (the other sides build the same keys):

  play           (card_id, upgraded, target_uid | None)      pos = hand slot
  end_turn       ()
  potion         (potion_id, slot, target_uid | None)        pos = slot
  discard_potion (potion_id, slot)
  travel         (col, row)
  claim_reward   (reward_type, item)   item = gold amount / potion id / relic id / card id (special card) / None
  leave          (screen,)             rewards / shop / rest / treasure (skip the relic) / event (proceed option)
  pick_card      (card_id, upgraded)                         pos = card index on the card screen
  card_alt       (alternative,)        skip / reroll / sacrifice
  buy            (category, item_id | None)   category card / relic / potion / card_removal
  rest           (option_id,)          HEAL / SMITH / ...
  open_chest     ()
  take_relic     (relic_id,)
  event_option   (index,)
  select         (item_id, upgraded) | ("bundle",) | ("index",)   pos = option index
  confirm / cancel  ()
  macro          (kind, A, B)          TakeRewardCard / TakeRewardAlternative (rewards screen, twin side only): plan =
                                       claim_reward ("card", None), then pick_card (card_id, upgraded) / card_alt (alt,)
                 ("SMITH" | "REMOVE", card_id, upgraded, d)   room macros (``Twin(macros="rest,shop")``; rows
                                       ChooseRestOption / BuyEntry with B = deck row d >= 0): plan = rest ("SMITH",) /
                                       buy ("card_removal", None), select (card_id, upgraded) pos = d, confirm; expect =
                                       every macro row's ((card_id, upgraded), d) of that screen (the game's grid)

Prompt macros (``Twin(macros="all")`` / ``"rest,shop,prompt"``): the prompt rows keep their kinds (select / confirm;
no cancel row is ever listed); whether a select also needs the game's confirm is read from the twin's env after its
step (``prompt_marker`` before / after, ``macros.prompt_committed``; the runner plays it, macros.py "Prompt macros").
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from .macros import PromptMarker
from .semantics import Act, View, norm_alt, norm_reward_type, norm_shop_category, norm_upgrade

SCREEN_OF_PHASE = {"Map": "map", "Combat": "combat", "Rewards": "rewards", "CardReward": "card_reward",
                   "Shop": "shop", "RestSite": "rest", "Treasure": "treasure", "Event": "event", "Prompt": "prompt",
                   "Victory": "end", "Defeat": "end"}


class TwinError(RuntimeError):
    """The twin cannot follow (illegal row, simulator error, unported content)."""


@dataclass
class Decision:
    row: int                     # the chosen row: the argmax, or with the loop breaker the best row it leaves unmasked
    probs: np.ndarray            # the model's softmax over the cand_n rows (unmasked)
    value: float                 # critic value (expected protocol return under the policy; not a probability)
    win_prob: float | None       # P(win) of the --aux-heads win head (None: the checkpoint has no aux heads)
    combat_hp: float | None      # predicted hp share at the end of the current combat (aux head), in combat only
    ms: float = 0.0
    order: np.ndarray | None = None   # the scores the choice ranks by (the loop breaker's masked logits), else probs
    plain: int | None = None          # the model's own argmax (differs from `row` when the loop breaker broke a loop)

    @property
    def loop_break(self) -> bool:
        return self.plain is not None and self.plain != self.row

    def ranked(self) -> list[int]:
        """Rows in the order a choice is made (the loop breaker's masked rows last)."""
        score = self.order if self.order is not None else self.probs
        return [int(i) for i in np.argsort(-score, kind="stable")]

    def top(self, k: int = 5) -> list[tuple[int, float]]:
        """The model's own top-k (unmasked probabilities)."""
        return [(int(i), float(self.probs[i])) for i in np.argsort(-self.probs, kind="stable")[:k]]


class Policy:
    """A trained sts2train checkpoint as a greedy policy over the twin's candidate rows (CPU)."""

    def __init__(self, checkpoint: str, *, device: str = "cpu", torch_threads: int = 4, loop_breaker: bool = True,
                 loop_breaker_k: int | None = None):
        import torch
        from sts2train.ppo import load_checkpoint

        torch.set_num_threads(max(1, int(torch_threads)))
        self.torch = torch
        self.device = device
        # sts2train.loopbreak.LoopBreaker, as `sts2train eval` plays greedy (on by default): at an observation the twin
        # was at before, the rows taken there are masked; from the k-th return only leave rows stay (built in bind()).
        self.loop_breaker_on = bool(loop_breaker)
        self.loop_breaker_k = loop_breaker_k
        self.breaker = None
        self._pending = False
        self.model, self.spec, ck = load_checkpoint(checkpoint, device)
        self.model.eval()
        self.checkpoint = str(checkpoint)
        self.aux = bool(getattr(self.model, "aux_heads", False))
        self.meta = {"checkpoint": str(checkpoint), "weights_hash": ck.get("weights_hash"),
                     "encoder_version": ck.get("encoder_version") or self.spec.encoder_version,
                     "params": ck.get("params"), "updates": ck.get("updates"), "env_steps": ck.get("env_steps"),
                     "arch": ck.get("arch"), "aux_heads": self.aux,
                     "characters": (ck.get("config") or {}).get("characters"),
                     "ascension": (ck.get("config") or {}).get("ascension"),
                     # the room macros of its training env (sts2train.evaluate.checkpoint_macros; "" = off)
                     "macros": str(((ck.get("config") or {}).get("macros")) or ""),
                     "best_eval": {k: (ck.get("best_eval") or {}).get(k) for k in
                                   ("seedset", "progress_mean", "win_rate", "floor_mean", "update")}
                     if ck.get("best_eval") else None}

    def bind(self, schema) -> dict:
        """Refuse a library whose observation differs from the checkpoint's inputs; -> the compatibility record.

        A checkpoint whose embedded enums only gained members appended at their end on this library (e.g. a pre-macro
        checkpoint: RunActionKind 21 -> 23, +TakeRewardCard, +TakeRewardAlternative) is reloaded widened, as
        ``sts2train eval`` does (``load_checkpoint(library=...)``: its embedding rows kept, the new members' rows zero).
        Unwidened, the model would clamp the macro kinds to its last kind (PromptCancel) and score each macro row as a
        cancel."""
        from sts2train.features import ModelSpec, bind_spec, check_spec_compat
        from sts2train.ppo import load_checkpoint

        library = ModelSpec.from_schema(schema)
        compat = check_spec_compat(self.spec, library, what="live twin")
        if compat["widened_enums"]:
            self.model, self.spec, _ = load_checkpoint(self.checkpoint, self.device, library=library)
            self.model.eval()
        bind_spec(self.spec)
        self._combat_col = self.spec.cols["global_i"]["is_combat"]
        try:
            from sts2train.loopbreak import DEFAULT_K, LoopBreaker
        except ImportError:      # a trainer tree without the loop breaker: plain argmax
            self.breaker = None
        else:
            self.breaker = LoopBreaker(library, num_envs=1, k=self.loop_breaker_k or DEFAULT_K,
                                       enabled=self.loop_breaker_on)
        self.meta["loop_breaker"] = ({"mode": self.breaker.mode, "k": self.breaker.k, "window": self.breaker.window,
                                      "leave_kinds": list(self.breaker.leave_kinds)} if self.breaker is not None
                                     else {"mode": "unavailable"})
        return compat

    def commit(self, row: int | None) -> None:
        """Record the row the twin actually took at the observation of the last `decide` (None / -1: none), so the
        loop breaker knows what led back to an observation. A decision that was not followed by a step is not
        recorded."""
        if self.breaker is not None and self._pending:
            r = -1 if row is None else int(row)
            self.breaker.commit(np.array([r], dtype=np.int64), np.array([self._plain], dtype=np.int64))
        self._pending = False

    def loop_totals(self) -> dict | None:
        return self.breaker.totals() if self.breaker is not None else None

    def decide(self, obs: dict) -> Decision:
        import time

        from sts2train.features import obs_to_torch

        torch = self.torch
        t0 = time.perf_counter()
        n = int(obs["cand_n"][0])
        with torch.no_grad():
            x = obs_to_torch(obs, self.device)
            if self.aux:
                logits, value, h = self.model(x, aux=True)
                win = float(torch.sigmoid(h["win_logit"])[0])
                chp = float(torch.sigmoid(h["combat_logit"])[0]) if obs["global_i"][0, self._combat_col] else None
            else:
                logits, value = self.model(x)
                win, chp = None, None
        lg = logits[0, :n].double().cpu().numpy()
        plain = int(np.argmax(lg))
        order = None
        row = plain
        if self.breaker is not None:
            masked = self.breaker.apply(obs, logits)
            self._plain, self._pending = plain, True
            if masked is not logits:
                order = masked[0, :n].double().cpu().numpy()
                row = int(np.argmax(order))
        lg = lg - lg.max()
        p = np.exp(lg)
        p /= p.sum()
        return Decision(row=row, probs=p, value=float(value[0]), win_prob=win, combat_hp=chp,
                        ms=(time.perf_counter() - t0) * 1000, order=order, plain=plain)


class RandomPolicy:
    """Seeded random choice over the twin's candidates, for coverage dry runs (tests): with probability `bias` it avoids
    ending the turn / leaving / skipping when anything else is legal, so runs reach shops, rest sites and prompts."""

    PASSIVE = ("EndTurn", "ProceedRewards", "LeaveShop", "LeaveRestSite", "LeaveEvent", "SkipTreasureRelic",
               "DiscardPotion", "PromptCancel", "RewardAlternative")

    def __init__(self, seed: int = 0, bias: float = 0.8):
        self.rng = np.random.default_rng(seed)
        self.bias = bias
        self.aux = False
        self.meta = {"checkpoint": None, "policy": f"random(seed={seed}, bias={bias})"}

    def commit(self, row) -> None:
        pass

    def loop_totals(self) -> dict | None:
        return None

    def bind(self, schema) -> dict:
        self._kind_col = schema.cols("cand")["kind"]
        self._passive = {i for i, name in enumerate(schema.vocab.enums["RunActionKind"]) if name in self.PASSIVE}
        return {}

    def decide(self, obs: dict) -> Decision:
        n = int(obs["cand_n"][0])
        kinds = obs["cand"][0][:n, self._kind_col]
        active = [i for i in range(n) if int(kinds[i]) not in self._passive]
        pool = active if active and self.rng.random() < self.bias else list(range(n))
        row = int(pool[int(self.rng.integers(len(pool)))])
        p = np.full(n, 0.5 / (n - 1) if n > 1 else 1.0)
        p[row] = 0.5 if n > 1 else 1.0
        return Decision(row=row, probs=p, value=0.0, win_prob=None, combat_hp=None)


class Twin:
    """One simulator slot (``auto_reset=False``, no step or stall caps) and its decoding into Views and Acts."""

    def __init__(self, *, content: str = "all", macros=None, dll=None):
        """``macros``: the macros of the checkpoint's training env (``"rest,shop"``, ``"all"`` = ``"rest,shop,prompt"``;
        None / "" = off, the candidate lists of every earlier build). A model trained with them must see them: its
        SMITH / removal rows mean "upgrade / remove this card" (sim/VEC.md "Room macros"), and with ``prompt`` a pick
        that completes a selection also confirms it (sim/VEC.md "Prompt macros v2")."""
        from sts2vec import STATUS, PoolSpec, VecEnv

        self.STATUS = STATUS
        extra = {"macros": macros} if macros else {}
        self.env = VecEnv(1, threads=1, step_cap=1_000_000, auto_reset=False, content=content,
                          pool=PoolSpec(count=0), floor_step_cap=0, dll=dll, **extra)
        self.macros = tuple(getattr(self.env, "macros", ()) or ())
        self.prompt_macros = "prompt" in self.macros
        self.last_status = 0
        self.content = content
        s = self.env.schema
        self.schema = s
        self.V = s.vocab
        self.E = s.vocab.enums
        self.c = {name: s.cols(name) for name in ("global_i", "hand", "enemies", "potions", "relics", "map_nodes",
                                                   "rewards", "card_reward", "shop", "rest", "event_options",
                                                   "prompt_options", "cand")}
        self.kinds = self.E["RunActionKind"]
        self.started = False
        self.origin: dict = {}
        self.steps = 0

    # ---------------------------------------------------------------------------------------- lifecycle

    def start_fresh(self, character: str, seed: str, ascension: int) -> int:
        e = self.env.pool.add_fresh(character, seed=seed, ascension=int(ascension))
        self.env.restore(0, e)
        self.started = True
        self.origin = {"kind": "fresh", "character": character, "seed": seed, "ascension": int(ascension),
                       "entry": e}
        self.steps = 0
        return e

    def start_save(self, path: str) -> int:
        """Re-seed the twin from a copy of the game's current_run.save (raises Sts2VecError if unsupported)."""
        e = self.env.pool.add_save(path)
        self.env.restore(0, e)
        self.started = True
        self.origin = {"kind": "save", "path": str(path), "entry": e}
        self.steps = 0
        return e

    def close(self) -> None:
        self.env.close()

    @property
    def obs(self) -> dict:
        return self.env.obs

    def g(self, name: str) -> int:
        return int(self.env.obs["global_i"][0, self.c["global_i"][name]])

    @property
    def cand_n(self) -> int:
        return int(self.env.obs["cand_n"][0])

    def state_hash(self) -> str:
        return f"{int(self.env.state_hash()[0]):016x}"

    def phase_name(self) -> str:
        return self.E["RunPhase"].name(self.g("decision_kind"))

    def step(self, row: int) -> int:
        """Apply candidate `row`; raises TwinError when the twin cannot take it."""
        if not 0 <= row < self.cand_n:
            raise TwinError(f"row {row} outside the {self.cand_n} candidates")
        _, _, _, _, status, _ = self.env.step(np.array([row], dtype=np.int32))
        st = int(status[0])
        S = self.STATUS
        if st & (S["ILLEGAL_ACTION"] | S["SIM_ERROR"] | S["CONTENT_GAP"]):
            names = [k for k, v in S.items() if v and st & v]
            raise TwinError(f"twin step {row} -> status {names}: {self.env.last_error(0)[:300]}")
        self.steps += 1
        self.last_status = st
        return st

    def prompt_marker(self) -> PromptMarker | None:
        """The pending prompt of the twin's env (None: no prompt pending), for ``macros.prompt_committed``."""
        if self.phase_name() != "Prompt":
            return None
        return PromptMarker(picked=self.g("prompt_picked"), site=self.g("prompt_site_hash"),
                            needs_confirm=bool(self.g("prompt_needs_confirm")), lo=self.g("prompt_min"),
                            hi=self.g("prompt_max"))

    # ---------------------------------------------------------------------------------------- names

    def card(self, cid: int) -> str:
        return self.V.cards.name(cid)

    def relic(self, rid: int) -> str:
        return self.V.relics.name(rid)

    def potion(self, pid: int) -> str:
        return self.V.potions.name(pid)

    def monster(self, mid: int) -> str:
        return self.V.monsters.name(mid)

    def _item(self, vocab_kind: str, item_id: int):
        if item_id <= 0:
            return None
        return getattr(self.V, vocab_kind).name(item_id)

    # ---------------------------------------------------------------------------------------- tables

    def _hand(self) -> list[tuple[str, int]]:
        hc = self.c["hand"]
        rows = self.obs["hand"][0]
        return [(self.card(int(r[hc["card_id"]])), norm_upgrade(r[hc["upgrade"]]))
                for r in rows[:self.g("hand_n")] if int(r[hc["card_id"]]) > 0]

    def _deck(self) -> list[tuple[str, int, int]]:
        """Master deck rows -> (card_id, upgraded 0/1, cards vocab id), in deck order (the ``deck`` buffer)."""
        dc = self.schema.cols("deck")
        rows = self.obs["deck"][0]
        return [(self.card(int(r[dc["card_id"]])), norm_upgrade(r[dc["upgrade"]]), int(r[dc["card_id"]]))
                for r in rows[:self.g("deck_n")]]

    def _is_curse(self, card_vocab_id: int) -> bool:
        s = self.schema
        if not getattr(s, "card_static_fields", ()) or not 0 <= card_vocab_id < s.card_static.shape[0]:
            return False
        try:
            return self.E["CardType"].name(int(s.card_static[card_vocab_id, s.card_static_cols()["type"]])) == "Curse"
        except (KeyError, IndexError):
            return False

    def _room_macro_rows(self, kind: str) -> list[int]:
        """The deck rows B of this screen's room macro rows of `kind` (ChooseRestOption / BuyEntry with B >= 0)."""
        cc = self.c["cand"]
        k = self.kinds.index_of(kind)
        return sorted(int(r[cc["B"]]) for r in self.obs["cand"][0][:self.cand_n]
                      if int(r[cc["kind"]]) == k and int(r[cc["B"]]) >= 0)

    def _prompt_order(self, kind: str, rows: list[int]) -> list[int]:
        """The order in which the game's DeckUpgrade / DeckRemove prompt lists the macro's deck rows: deck order;
        the removal puts curses first (``RoomPrompts.FromDeckForRemoval``)."""
        if kind != "BuyEntry":
            return rows
        deck = self._deck()
        return sorted(rows, key=lambda d: (not (d < len(deck) and self._is_curse(deck[d][2])), d))

    def _enemies(self) -> dict[int, tuple]:
        """enemies row -> (uid, monster_id, hp, block) for live enemies."""
        ec = self.c["enemies"]
        out = {}
        for j, r in enumerate(self.obs["enemies"][0]):
            if int(r[ec["present"]]):
                out[j] = (int(r[ec["uid"]]), self.monster(int(r[ec["monster_id"]])), int(r[ec["hp"]]),
                          int(r[ec["block"]]))
        return out

    def _potions(self) -> dict[int, str]:
        pc = self.c["potions"]
        return {i: self.potion(int(r[pc["potion_id"]])) for i, r in enumerate(self.obs["potions"][0])
                if int(r[pc["potion_id"]]) > 0}

    def _map_legal(self) -> list[tuple[int, int, str]]:
        """Legal destinations sorted by (col, row): Travel's option_index is the rank in this list."""
        mc = self.c["map_nodes"]
        nodes = self.obs["map_nodes"][0]
        pts = self.E["MapPointType"]
        out = []
        for row in range(nodes.shape[0]):
            for col in range(nodes.shape[1]):
                if int(nodes[row, col, mc["legal_next"]]):
                    out.append((col, row, pts.name(int(nodes[row, col, mc["point_type"]]))))
        return sorted(out)

    def _rewards(self) -> dict[int, tuple]:
        rc = self.c["rewards"]
        types = self.E["RewardType"]
        out = {}
        for i, r in enumerate(self.obs["rewards"][0]):
            t = int(r[rc["reward_type"]])
            if t <= 0:
                continue
            name = norm_reward_type(types.name(t))
            item_id = int(r[rc["item_id"]])
            if name == "gold":
                item = int(r[rc["amount"]])
            elif name == "potion":
                item = self._item("potions", item_id)
            elif name == "relic":
                item = self._item("relics", item_id)
            elif name == "special_card":
                item = self._item("cards", item_id)
            else:
                item = None
            out[i] = (name, item)
        return out

    def _card_reward(self) -> list[tuple[str, int]]:
        cc = self.c["card_reward"]
        return [(self.card(int(r[cc["card_id"]])), norm_upgrade(r[cc["upgrade"]]))
                for r in self.obs["card_reward"][0] if int(r[cc["card_id"]]) > 0]

    def _alts(self) -> list[str]:
        alts = self.E["CardRewardAlt"]
        return [norm_alt(alts.name(int(v))) for v in self.obs["card_reward_alts"][0] if int(v) > 0]

    def _shop(self) -> dict[int, tuple]:
        sc = self.c["shop"]
        cats = self.E["ShopCategory"]
        out = {}
        for i, r in enumerate(self.obs["shop"][0]):
            cat_raw = cats.name(int(r[sc["category"]]))
            if int(r[sc["category"]]) <= 1:
                continue
            cat = norm_shop_category(cat_raw)
            vocab = {"card": "cards", "relic": "relics", "potion": "potions"}.get(cat)
            item = self._item(vocab, int(r[sc["item_id"]])) if vocab else None
            out[i] = (cat, item, int(r[sc["price"]]), int(r[sc["stocked"]]), norm_upgrade(r[sc["upgrade"]]))
        return out

    def _rest(self) -> dict[int, tuple[str, int]]:
        rc = self.c["rest"]
        opts = self.E["RestOption"]
        return {i: (opts.name(int(r[rc["option_id"]])), int(r[rc["enabled"]]))
                for i, r in enumerate(self.obs["rest"][0]) if int(r[rc["option_id"]]) > 1}

    def _event_options(self) -> dict[int, dict]:
        ec = self.c["event_options"]
        n = self.g("event_option_n")
        out = {}
        for i, r in enumerate(self.obs["event_options"][0][:n]):
            out[i] = {"ordinal": int(r[ec["ordinal"]]), "enabled": int(r[ec["enabled"]]),
                      "locked": int(r[ec["locked"]]), "proceed": int(r[ec["proceed"]]),
                      "relic": self._item("relics", int(r[ec["relic_id"]]))}
        return out

    def _prompt_options(self) -> dict[int, dict]:
        pc = self.c["prompt_options"]
        kinds = self.E["PromptItemKind"]
        n = self.g("prompt_option_n")
        out = {}
        for i, r in enumerate(self.obs["prompt_options"][0][:n]):
            kind = kinds.name(int(r[pc["kind_of_item"]]))
            vocab = {"card": "cards", "relic": "relics", "potion": "potions"}.get(kind)
            item = self._item(vocab, int(r[pc["item_id"]])) if vocab else None
            out[i] = {"kind": kind, "item": item, "upgraded": norm_upgrade(r[pc["upgrade"]]),
                      "picked": int(r[pc["picked"]]), "ordinal": int(r[pc["ordinal"]])}
        return out

    # ---------------------------------------------------------------------------------------- view

    def view(self) -> View:
        screen = SCREEN_OF_PHASE.get(self.phase_name(), "other")
        v = View(screen=screen, act=self.g("act_index") + 1, floor=self.g("total_floor"), hp=self.g("hp"),
                 max_hp=self.g("max_hp"), gold=self.g("gold"))
        v.potions = tuple(sorted(self._potions().items()))
        rc = self.c["relics"]
        v.relics = tuple(sorted(self.relic(int(r[rc["relic_id"]])) for r in self.obs["relics"][0]
                                if int(r[rc["relic_id"]]) > 0))
        if self.g("is_combat"):
            v.energy, v.block = self.g("energy"), self.g("block")
            v.hand = tuple(self._hand())
            v.enemies = tuple(sorted(self._enemies().values()))
        if screen == "end":
            v.terminal = "victory" if self.phase_name() == "Victory" else "defeat"
        d = v.detail
        if screen == "map":
            d["map_options"] = tuple(self._map_legal())
        elif screen == "event":
            d["event"] = self.V.events.name(self.g("event_id"))
            d["event_options_n"] = sum(1 for o in self._event_options().values() if not o["proceed"])
        elif screen == "card_reward":
            d["card_reward"] = tuple(self._card_reward())
        elif screen == "rewards":
            d["rewards"] = tuple(sorted(self._rewards().values(), key=repr))
        elif screen == "shop":
            d["shop"] = tuple(sorted(((c, i, p) for c, i, p, s, _ in self._shop().values() if s), key=repr))
        elif screen == "rest":
            d["rest"] = tuple(name for name, en in self._rest().values() if en)
        elif screen == "treasure" and self.g("treasure_opened") and not self.g("treasure_claimed"):
            d["treasure_relic"] = self.relic(self.g("treasure_relic_id"))
        elif screen == "prompt" and self.g("prompt_picked") == 0:
            d["prompt_options"] = tuple(sorted((o["item"], o["upgraded"]) for o in self._prompt_options().values()
                                               if o["item"] is not None))
        return v

    # ---------------------------------------------------------------------------------------- candidates

    def acts(self) -> list[Act]:
        """One Act per candidate row (Act.native = the row)."""
        cc = self.c["cand"]
        rows = self.obs["cand"][0][:self.cand_n]
        cache: dict = {}

        def lazy(name, fn):
            if name not in cache:
                cache[name] = fn()
            return cache[name]

        out = []
        for j, r in enumerate(rows):
            kind = self.kinds.name(int(r[cc["kind"]]))
            a, b = int(r[cc["A"]]), int(r[cc["B"]])
            hs, ts, ps, oi = (int(r[cc["hand_slot"]]), int(r[cc["target_slot"]]), int(r[cc["potion_slot"]]),
                              int(r[cc["option_index"]]))
            uid = int(r[cc["target_uid"]]) or None
            out.append(self._act(j, kind, a, b, hs, ts, ps, oi, uid, int(r[cc["card_id"]]), lazy))
        return out

    def _act(self, j, kind, a, b, hs, ts, ps, oi, uid, card_id, lazy) -> Act:
        enemies = lambda: lazy("enemies", self._enemies)  # noqa: E731

        def tgt_label():
            if uid is None:
                return ""
            m = next((e for e in enemies().values() if e[0] == uid), None)
            return f" -> u{uid} {m[1] if m else '?'}"

        if kind == "PlayCard":
            hand = lazy("hand", self._hand)
            cid, up = hand[hs] if 0 <= hs < len(hand) else (self.card(card_id), 0)
            return Act("play", (cid, up, uid), hs, j, f"play {cid}{'+' if up else ''}{tgt_label()}")
        if kind == "EndTurn":
            return Act("end_turn", (), None, j, "end turn")
        if kind in ("UsePotion", "DiscardPotion"):
            pot = lazy("potions", self._potions).get(ps, "?")
            if kind == "UsePotion":
                return Act("potion", (pot, ps, uid), ps, j, f"potion {ps} {pot}{tgt_label()}")
            return Act("discard_potion", (pot, ps), ps, j, f"discard potion {ps} {pot}")
        if kind == "Travel":
            legal = lazy("map", self._map_legal)
            col, row, typ = legal[oi] if 0 <= oi < len(legal) else (-1, -1, "?")
            return Act("travel", (col, row), oi, j, f"travel ({col},{row}) {typ}")
        if kind == "ClaimReward":
            rw = lazy("rewards", self._rewards).get(oi, ("?", None))
            return Act("claim_reward", rw, oi, j, f"claim {rw[0]} {rw[1] if rw[1] is not None else ''}".strip())
        if kind == "ProceedRewards":
            return Act("leave", ("rewards",), None, j, "leave rewards")
        if kind == "PickRewardCard":
            cr = lazy("card_reward", self._card_reward)
            cid, up = cr[oi] if 0 <= oi < len(cr) else (self.card(card_id), 0)
            return Act("pick_card", (cid, up), oi, j, f"take card {cid}{'+' if up else ''}")
        if kind == "RewardAlternative":
            alts = lazy("alts", self._alts)
            alt = alts[oi] if 0 <= oi < len(alts) else f"#{oi}"
            return Act("card_alt", (alt,), oi, j, f"card reward: {alt}")
        if kind in ("ChooseRestOption", "BuyEntry") and b >= 0:
            return self._room_macro(j, kind, a, b, oi, lazy)
        if kind == "BuyEntry":
            cat, item, price, _, up = lazy("shop", self._shop).get(oi, ("?", None, 0, 0, 0))
            return Act("buy", (cat, item), oi, j, f"buy {cat} {item or ''} ({price}g)".replace("  ", " "))
        if kind == "LeaveShop":
            return Act("leave", ("shop",), None, j, "leave shop")
        if kind == "ChooseRestOption":
            name = lazy("rest", self._rest).get(oi, ("?", 0))[0]
            return Act("rest", (name,), oi, j, f"rest: {name}")
        if kind == "LeaveRestSite":
            return Act("leave", ("rest",), None, j, "leave rest site")
        if kind == "OpenChest":
            return Act("open_chest", (), None, j, "open chest")
        if kind == "TakeTreasureRelic":
            rel = self.relic(self.g("treasure_relic_id"))
            return Act("take_relic", (rel,), None, j, f"take relic {rel}")
        if kind == "SkipTreasureRelic":
            return Act("leave", ("treasure",), None, j, "skip the relic")
        if kind == "ChooseEventOption":
            # A proceed-type option stays an event_option here; the MCP side lists its proceed options both as
            # event_option(index) and as leave("event"), so either twin form finds it.
            opt = lazy("event", self._event_options).get(oi, {})
            rel = f" [{opt['relic']}]" if opt.get("relic") else ""
            tag = " (proceed)" if opt.get("proceed") else ""
            return Act("event_option", (oi,), oi, j, f"event option {oi}{rel}{tag}")
        if kind == "LeaveEvent":
            return Act("leave", ("event",), None, j, "leave event")
        if kind == "PromptSelect":
            o = lazy("prompt", self._prompt_options).get(oi, {"kind": "?", "item": None, "upgraded": 0})
            if o["kind"] in ("card", "relic", "potion") and o["item"] is not None:
                key = (o["item"], o["upgraded"])
                lab = f"select {o['item']}{'+' if o['upgraded'] else ''}"
            else:
                key = (o["kind"],)
                lab = f"select {o['kind']} {oi}"
            return Act("select", key, oi, j, lab)
        if kind == "PromptConfirm":
            if self.g("prompt_picked") == 0:
                # confirming nothing = declining (a choose-a-card prompt with min 0): the game shows a skip button
                return Act("confirm", ("empty",), None, j, "confirm an empty selection (skip)")
            return Act("confirm", (), None, j, "confirm selection")
        if kind == "PromptCancel":
            return Act("cancel", (), None, j, "cancel / skip selection")
        if kind in ("TakeRewardCard", "TakeRewardAlternative"):
            return self._macro(j, kind, a, b, oi, card_id)
        return Act("other", (kind, a, b), None, j, f"{kind} {a} {b}")

    def _reward_choice(self, kind: str, k: int, a: int, b: int) -> dict | None:
        """The ``reward_choices`` row of macro candidate (A=a, B=b): row k (the candidate's option_index) when it is
        that choice, else the row that is; None when no row is (an older library without the buffer)."""
        if "reward_choices" not in self.obs:
            return None
        rc = self.schema.cols("reward_choices")
        rows = self.obs["reward_choices"][0]
        filled = "card_id" if kind == "TakeRewardCard" else "alt_id"      # unused rows are all zero
        for r in ([rows[k]] if 0 <= k < len(rows) else []) + list(rows):
            if int(r[rc["reward_index"]]) == a and int(r[rc["option"]]) == b and int(r[rc[filled]]) > 0:
                return {name: int(r[i]) for name, i in rc.items()}
        return None

    def _alt_name(self, alt_id: int) -> str:
        return norm_alt(self.E["CardRewardAlt"].name(alt_id))

    def _macro(self, j, kind, a, b, oi, card_id) -> Act:
        """Rewards-screen macros (``RewardMacros.Expand``): ClaimReward(A) opens card reward A, then PickRewardCard(B)
        takes its card B / RewardAlternative(B) chooses its alternative B (REROLL, SACRIFICE; never Skip). On the game's
        side the macro is a plan of those two acts; the card or alternative is read from ``reward_choices`` row `oi`."""
        ch = self._reward_choice(kind, oi, a, b) or {}
        open_act = Act("claim_reward", ("card", None), a, None, f"open card reward {a}")
        if kind == "TakeRewardCard":
            cid = self.card(card_id or ch.get("card_id", 0))
            up = norm_upgrade(ch.get("upgrade", 0))
            pick = Act("pick_card", (cid, up), b, None, f"take card {cid}{'+' if up else ''}")
            return Act("macro", ("TakeRewardCard", a, b), b, j, f"reward {a}: take {cid}{'+' if up else ''}",
                       plan=(open_act, pick))
        alt = self._alt_name(ch["alt_id"]) if ch else f"#{b}"
        choose = Act("card_alt", (alt,), b, None, f"card reward: {alt}")
        return Act("macro", ("TakeRewardAlternative", a, b), b, j, f"reward {a}: {alt}", plan=(open_act, choose))

    def _room_macro(self, j, kind, a, b, oi, lazy) -> Act:
        """Room macros (``RoomMacros``, ``--macros rest,shop``): ChooseRestOption(A = SMITH, B = d) / BuyEntry(A = card
        removal, B = d) upgrade / remove deck card d in one decision. In the game: open the SMITH / removal screen,
        select the card, confirm. ``expect`` = the grid the game must open: every macro row's card of this screen
        (they are exactly the upgradable / removable deck cards, the filters of the game's prompt)."""
        deck = lazy("deck", self._deck)
        rows = lazy("room:" + kind, lambda: self._room_macro_rows(kind))
        cid, up, _ = deck[b] if 0 <= b < len(deck) else ("?", 0, 0)
        expect = tuple(((deck[d][0], deck[d][1]), d) for d in rows if d < len(deck))
        sel = Act("select", (cid, up), b, None, f"select {cid}{'+' if up else ''}")
        confirm = Act("confirm", (), None, None, "confirm selection")
        if kind == "ChooseRestOption":
            opt = lazy("rest", self._rest).get(oi, ("SMITH", 0))[0]
            open_act = Act("rest", (opt,), oi, None, f"rest: {opt}")
            return Act("macro", ("SMITH", cid, up, b), b, j, f"smith {cid}{'+' if up else ''} (deck {b})",
                       plan=(open_act, sel, confirm), expect=expect)
        open_act = Act("buy", ("card_removal", None), oi, None, "buy card_removal")
        return Act("macro", ("REMOVE", cid, up, b), b, j, f"remove {cid}{'+' if up else ''} (deck {b})",
                   plan=(open_act, sel, confirm), expect=expect)

    # ---------------------------------------------------------------------------------------- tokens

    def row_to_token(self, row: int) -> str:
        """The play-server token of candidate `row`, from the row's own arguments (``ActionTokens.Token`` in
        sim/Sts2Sim.Server): the dry-run audit compares it with the token the stand-in matched semantically."""
        cc = self.c["cand"]
        r = self.obs["cand"][0][row]
        kind = self.kinds.name(int(r[cc["kind"]]))
        a, b = int(r[cc["A"]]), int(r[cc["B"]])
        if kind == "PlayCard":
            return f"play {a}" if b == -1 else f"play {a} -> {b}"
        if kind == "EndTurn":
            return "end"
        if kind == "UsePotion":
            return f"potion {a}" if b == -1 else (f"potion {a} -> {b}" if b >= 0 else f"potion {a} -> ally {-2 - b}")
        if kind == "DiscardPotion":
            return f"discard {a}"
        if kind == "PromptSelect":
            return f"pick {a}"
        if kind == "PromptConfirm":
            return "confirm"
        if kind == "PromptCancel":
            return "cancel"
        if kind == "Travel":
            return f"travel {a}"
        if kind == "ClaimReward":
            return f"reward {a}"
        if kind == "ProceedRewards":
            return "proceed"
        if kind == "PickRewardCard":
            return f"card {a}"
        if kind == "RewardAlternative":
            alts = self._alts()
            alt = alts[a] if 0 <= a < len(alts) else ""
            return {"skip": "skip", "reroll": "reroll"}.get(alt, f"alt {a}")
        if kind in ("ChooseRestOption", "BuyEntry") and b >= 0:
            # a room macro: the play server has no macro rows, so it is the game's three clicks -- the prompt option
            # of deck card b is its rank in the prompt's order of the screen's macro rows
            order = self._prompt_order(kind, self._room_macro_rows(kind))
            pick = order.index(b) if b in order else -1
            return f"{'rest ' + str(a) if kind == 'ChooseRestOption' else 'remove'}; pick {pick}; confirm"
        if kind == "BuyEntry":
            return "remove" if self._shop().get(a, ("",))[0] == "card_removal" else f"buy {a}"
        if kind in ("LeaveShop", "LeaveRestSite", "LeaveEvent"):
            return "leave"
        if kind == "ChooseRestOption":
            return f"rest {a}"
        if kind == "OpenChest":
            return "open"
        if kind == "TakeTreasureRelic":
            return "take 0"
        if kind == "SkipTreasureRelic":
            return "skip"
        if kind == "ChooseEventOption":
            return f"event {a}"
        # the macros: the play server has no macro rows, so a macro is its two tokens (the runner joins its plan's
        # wires the same way)
        if kind == "TakeRewardCard":
            return f"reward {a}; card {b}"
        if kind == "TakeRewardAlternative":
            ch = self._reward_choice(kind, int(r[cc["option_index"]]), a, b)
            alt = self._alt_name(ch["alt_id"]) if ch else ""
            return f"reward {a}; " + {"skip": "skip", "reroll": "reroll"}.get(alt, f"alt {b}")
        return f"{kind} {a} {b}"
