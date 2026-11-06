"""Training-time exploration for rare decision kinds (``--explore-kinds K1,K2 --explore-eps E``, wave 9, 2026-11-05).

Why. The behaviour audit of line A u5064 (docs/diag-2026-11-05/behaviour-audit-a-u5064.md) found the policy at
P(HEAL) = 1.000 at every rest site (the SMITH:<card> macro rows at 0.000) and P(card removal) ~ 0 in every shop. The
rollout samples from the policy, so a row with ~0 probability is never taken and PPO never sees its advantage.

What. During the ROLLOUT only (``ppo.train`` and ``overlap.OverlapRollout``; never in ``sts2train.evaluate``, the
periodic / ``--eval-at`` evaluations or the loop breaker's greedy path), at a decision whose candidate table has at least
one row of an explore item, the action is drawn with probability E uniformly from the explore rows present (the union
of every item's rows: e.g. HEAL and every SMITH:<card> row for ``ChooseRestOption``), otherwise from the policy as
before. The behaviour policy is the mixture

    b(a|s) = (1 - E) pi(a|s) + E u(a|s),   u = uniform over the explore rows present (0 elsewhere)

at such decisions and b = pi everywhere else. ``pi`` is the distribution the rollout sampled from (after the rollout
loop breaker's mask, when on); explore rows are restricted to the rows that distribution can take (j < cand_n, within
``max_rows``, not masked to ``NEG``), so the breaker's bans are never explored around.

Items (``parse_kinds``): ``Kind`` = every row of that RunActionKind (by name, as the library's enum spells it), or
``Kind:sel`` with a selector:

- ``macro`` / ``plain``: the room-macro rows (B >= 0: ``SMITH:<card>``, ``Remove:<card>``; sim/VEC.md "Room macros") /
  the game's own rows (B = -1).
- ``remove`` (alias of ``card_removal``) or any ShopCategory name, on ``BuyEntry``: the rows whose shop entry (A) has
  that category, macro or plain: ``BuyEntry:remove`` = the card-removal rows only (a removal row is a ``BuyEntry`` like
  any purchase, so the kind alone cannot tell them apart).
- any RestOption name (``SMITH``, ``HEAL``, ...) on ``ChooseRestOption``: the rows of that rest option (``A``'s
  ``rest.option_id``), macro or plain: ``ChooseRestOption:SMITH`` = every SMITH:<card> row (or the plain SMITH row
  without macros), without HEAL.
- any MapPointType name (``Elite``, ``RestSite``, ``Unknown``, ``Shop``, ``Treasure``, ``Monster``, ...) on ``Travel``
  (wave 10): the Travel rows whose destination node has that point type. A Travel row's A (``option_index``) is the
  rank of the destination among the legal points (``map_nodes.legal_next``) sorted by (col, row) (sim/VEC.md; the
  model's own map pointer, ``model.py``), so the destination's type is ``map_nodes.point_type`` of the A-th legal cell
  in column-major order. ``Travel:Elite`` at E: at a Travel decision offering >= 1 Elite destination, with probability
  E the action is uniform among the Elite destinations (P(an elite) >= E in the rollouts); a decision without an Elite
  destination is untouched.
- ``skipcard`` on ``ProceedRewards`` (wave 10): the ProceedRewards row only at decisions that also list a card row
  (``TakeRewardCard`` = the rewards-screen card macro, or ``PickRewardCard``): leaving the rewards screen there skips the
  unclaimed card. The bare ``ProceedRewards`` item also explores the decisions where nothing (or only gold / potions /
  relics) is left to claim, where proceeding is no skip. Proceeding forfeits every unclaimed reward on the screen (the
  game's ``OnSkipped``), not only the card: with gold still unclaimed a ``skipcard`` draw skips the gold too.

Log-prob stored for PPO (``logp_mode``). What every mode corrects is the one-step policy ratio only: the advantages
(GAE) and the value targets are computed from the b-trajectories with no off-policy correction (no V-trace / Retrace),
so the critic learns V^b and the advantages are b's (e.g. a forced SMITH at 30% of rest sites lowers V upstream of rest
sites relative to V^pi; small, because rest and shop decisions are rare). None of the modes is "unbiased" in that sense.

- ``decoupled`` (default): store log pi_old(a|s) and multiply the normalised advantage by w = pi_old / b (decoupled
  PPO, Hilton et al. 2021: the proximal policy pi_old is separated from the behaviour policy b). The ratio starts at 1
  for every sample, so the clip window is centred on pi_old and w is the importance weight of the sample actually
  drawn: the first-step surrogate gradient is the on-policy policy gradient of pi (from b's samples). An explored row
  the policy gives ~0 gets w ~ 0: its gradient is the (small) true one, grad pi = pi grad log pi.
- ``mixture``: store log b(a|s), ratio pi_new / b. At an explore decision the ratio starts at pi / b, not 1:
  1 / (1 - E) for a policy-drawn non-explore row and ~ pi / ((1 - E) pi + E / cnt) for the explore rows. Under the
  clip this is NOT the on-policy gradient: once 1 / (1 - E) > 1 + clip (E > clip / (1 + clip) = 0.167 at clip 0.2)
  every row the policy drew starts outside the window, so it only gets the negative-advantage gradient (positive ones
  are clipped flat), while an explored ~0-probability row starts below 1 - clip and only gets the positive-advantage
  gradient: an advantage-independent drift away from the policy's own choices (e.g. P(leave shop) and P(HEAL) pushed
  down whatever their value). `resolve_config` therefore refuses ``mixture`` at E >= clip / (1 + clip); below that the
  explored low-probability rows still start clipped from below. ``explore_mix_clip0`` reports the share of the explore
  decisions' samples whose mixture start ratio pi / b lies outside [1 - clip, 1 + clip] (in every mode; under
  ``decoupled`` / ``policy`` the actual start ratio is 1).
- ``policy``: store log pi_old and ignore the mixture (ratio = pi_new / pi_old as if pi had drawn every action): biased
  off-policy; the explored rows get the full A x grad log pi push (fast recovery of the rare rows' probability, also for
  rows whose advantage is only noise).

Cost. Steps without any explore row in the candidate table (all combat steps for ``ChooseRestOption,BuyEntry``) cost
only the numpy row scan of the observation: no device-to-host copy. A step with an explore row copies the [n, w]
normalised log-probs and the [n] actions to the host once each (two syncs on CUDA). ``t_explore_s`` (per update) is
the wall time spent in the explorer; on CUDA it includes waiting for the forward pass that the rollout would otherwise
wait for at its own action copy, so compare ``infer_s`` explorer on / off rather than reading it as pure overhead.

Randomness. The draws of slot s at rollout step t of update u (the update the rollout belongs to, absolute) come from
``np.random.default_rng([purpose_seed(sample_seed, "explore"), u, t])`` (two uniforms per slot of the N-slot batch: the
coin and the pick): a pure function of the sample seed and (u, t, s). No state to checkpoint; a resume continues the
same stream at its update, ``--overlap double`` draws slot s as ``--overlap off`` does (each lane takes its rows of the
N-slot draw), and E = 0 builds no explorer at all (the trainer computes exactly what it computed before; the torch
sample RNG is never touched by the explorer either way).

Metrics per update (``pop_stats``): ``explore_eps``, ``explore_decisions`` (decisions with >= 1 explore row),
``explore_share`` (of the valid decisions), ``explore_steps`` (mixture draws), ``explore_policy_mass`` (mean of pi's
summed probability on the explore rows at those decisions), ``explore_mix_clip0`` (above), ``t_explore_s``, and per item ``explore_present.<item>``,
``explore_pmass.<item>`` (mean pi mass on the item's rows where present), ``explore_taken.<item>`` /
``explore_drawn.<item>`` (actions taken on the item's rows / of them by a mixture draw), plus ``..._macro.<item>``
variants over the item's macro rows (B >= 0): ``explore_pmass_macro.ChooseRestOption`` is P(SMITH:<card>) at rest sites
with macros on.
"""

from __future__ import annotations

import math
import time
from dataclasses import dataclass

import numpy as np
import torch

from .features import ModelSpec

LOGP_MODES = ("decoupled", "mixture", "policy")       # [0] = the default
GENERIC_SELECTORS = ("macro", "plain")
SELECTOR_ALIASES = {"remove": "card_removal"}
NEG_LIVE = -1e8                     # logits above this are live rows (masked ones are model.NEG = -1e9)


def parse_kinds(spec_text) -> tuple[tuple[str, str | None], ...]:
    """``"ChooseRestOption,BuyEntry:remove"`` -> ((kind, selector or None), ...): syntax only (names are checked against
    the library's enums by `KindExplorer`). Accepts a comma string or a sequence; "" / "none" / "off" = ()."""
    if spec_text is None:
        return ()
    parts = spec_text.split(",") if isinstance(spec_text, str) else list(spec_text)
    out: list[tuple[str, str | None]] = []
    for p in parts:
        p = str(p).strip()
        if not p or (len(parts) == 1 and p.lower() in ("none", "off")):
            continue
        kind, colon, sel = p.partition(":")
        kind, sel = kind.strip(), sel.strip()
        if not kind or (colon and not sel) or ":" in sel:
            raise ValueError(f"explore kind {p!r}: expected Kind or Kind:selector")
        item = (kind, SELECTOR_ALIASES.get(sel, sel) if sel else None)
        if item not in out:
            out.append(item)
    return tuple(out)


def format_kinds(items) -> str:
    return ",".join(k if s is None else f"{k}:{s}" for k, s in items)


@dataclass
class _Item:
    label: str
    kind_id: int
    sel: str | None         # None | "macro" | "plain" | "shop" | "rest" | "travel" | "skipcard"
    sel_id: int = -1        # ShopCategory / RestOption / MapPointType member id for "shop" / "rest" / "travel"


SKIPCARD = "skipcard"
CARD_KINDS = ("TakeRewardCard", "PickRewardCard")    # the card rows whose presence makes ProceedRewards a skip


class KindExplorer:
    """The mixture sampler of the module docstring. `spec` = the library's (``ModelSpec.from_schema(env.schema)``);
    `kinds` = ``parse_kinds`` input; raises ValueError on a kind or selector the library does not have."""

    def __init__(self, spec: ModelSpec, kinds, eps: float, seed: int, *, logp_mode: str = LOGP_MODES[0],
                 clip: float = 0.2):
        items = parse_kinds(kinds)
        if not items:
            raise ValueError("explore: no kinds given")
        eps = float(eps)
        if not 0.0 < eps < 1.0:
            raise ValueError(f"explore_eps {eps} must be in (0, 1) for an explorer")
        if logp_mode not in LOGP_MODES:
            raise ValueError(f"explore_logp {logp_mode!r}: expected one of {LOGP_MODES}")
        en = spec.enum_names
        names = list(en["RunActionKind"])
        shop_names = list(en.get("ShopCategory") or [])
        rest_names = list(en.get("RestOption") or [])
        point_names = [p for p in (en.get("MapPointType") or []) if p != "Unassigned"]     # 0 = no node
        self.card_kind_ids = [names.index(k) for k in CARD_KINDS if k in names]
        self.items: list[_Item] = []
        for kind, sel in items:
            if kind not in names:
                raise ValueError(f"explore kind {kind!r} is not a RunActionKind of this library ({', '.join(names)})")
            label = kind if sel is None else f"{kind}:{sel}"
            it = _Item(label, names.index(kind), None)
            if sel is not None:
                if sel in GENERIC_SELECTORS:
                    it.sel = sel
                elif kind == "BuyEntry" and sel in shop_names:
                    it.sel, it.sel_id = "shop", shop_names.index(sel)
                elif kind == "ChooseRestOption" and sel in rest_names:
                    it.sel, it.sel_id = "rest", rest_names.index(sel)
                elif kind == "Travel" and sel in point_names:
                    it.sel, it.sel_id = "travel", list(en["MapPointType"]).index(sel)
                elif kind == "ProceedRewards" and sel == SKIPCARD and self.card_kind_ids:
                    it.sel = SKIPCARD
                else:
                    ok = list(GENERIC_SELECTORS) + (["remove"] + shop_names if kind == "BuyEntry" else
                                                    rest_names if kind == "ChooseRestOption" else
                                                    [f"{p} (MapPointType)" for p in point_names] if kind == "Travel"
                                                    else [SKIPCARD] if kind == "ProceedRewards" else [])
                    raise ValueError(f"explore selector {label!r}: unknown for {kind} (expected one of {ok})")
            self.items.append(it)
        self.kinds = format_kinds(items)
        self.eps = eps
        self.logp_mode = logp_mode
        self.seed = int(seed)
        self.clip = float(clip)
        self._u_key, self._u = None, None
        C = spec.cols["cand"]
        self.c_kind, self.c_b, self.c_oi = C["kind"], C["B"], C["option_index"]
        self.shop_cat = spec.cols["shop"]["category"]
        self.rest_opt = spec.cols["rest"]["option_id"]
        M = spec.cols.get("map_nodes") or {}
        self.m_pt, self.m_legal = M.get("point_type", -1), M.get("legal_next", -1)
        self.travel_id = names.index("Travel") if "Travel" in names else -1
        if any(it.sel == "travel" for it in self.items) and min(self.m_pt, self.m_legal) < 0:
            raise ValueError("explore Travel:<MapPointType>: this library's map_nodes has no point_type / legal_next")
        self._log1m = math.log1p(-eps)
        self._loge = math.log(eps)
        self._zero()
        self.run = {"explore_steps": 0, "explore_decisions": 0, "decisions": 0}

    # ------------------------------------------------------------------------------------------- rows

    def _zero(self) -> None:
        L = len(self.items)
        self.acc = {"decisions": 0, "present": 0, "fired": 0, "mass": 0.0, "clip0": 0, "t": 0.0}
        self.per = {k: np.zeros(L, dtype=np.float64) for k in
                    ("present", "pmass", "taken", "drawn", "present_macro", "pmass_macro", "taken_macro",
                     "drawn_macro")}

    def item_rows(self, obs: dict, w: int) -> tuple[np.ndarray, np.ndarray]:
        """-> (rows [I, n, w] bool: row j of slot s belongs to item i (j < cand_n), macro [n, w] bool: B >= 0)."""
        cand = np.asarray(obs["cand"])
        n = cand.shape[0]
        w_c = min(int(w), cand.shape[1])
        rows = np.zeros((len(self.items), n, int(w)), dtype=bool)
        macro_full = np.zeros((n, int(w)), dtype=bool)
        cand = cand[:, :w_c]
        cn = np.asarray(obs["cand_n"]).reshape(n, 1)
        legal = np.arange(w_c)[None, :] < cn
        kind = cand[:, :, self.c_kind]
        macro = cand[:, :, self.c_b] >= 0
        macro_full[:, :w_c] = macro
        oi = dest = has_card = None
        for i, it in enumerate(self.items):
            r = legal & (kind == it.kind_id)
            if it.sel == "macro":
                r &= macro
            elif it.sel == "plain":
                r &= ~macro
            elif it.sel in ("shop", "rest"):
                if oi is None:
                    oi = cand[:, :, self.c_oi].astype(np.int64)
                buf, col = (obs["shop"], self.shop_cat) if it.sel == "shop" else (obs["rest"], self.rest_opt)
                buf = np.asarray(buf)
                ok = (oi >= 0) & (oi < buf.shape[1])
                val = np.take_along_axis(buf[:, :, col], np.clip(oi, 0, buf.shape[1] - 1), axis=1)
                r &= ok & (val == it.sel_id)
            elif it.sel == "travel":
                if dest is None:
                    if oi is None:
                        oi = cand[:, :, self.c_oi].astype(np.int64)
                    dest = self.travel_dest(obs, oi, legal & (kind == self.travel_id))
                r &= dest == it.sel_id
            elif it.sel == SKIPCARD:
                if has_card is None:
                    has_card = (legal & np.isin(kind, self.card_kind_ids)).any(1, keepdims=True)
                r &= has_card
            rows[i, :, :w_c] = r
        return rows, macro_full

    def travel_dest(self, obs: dict, oi: np.ndarray, travel: np.ndarray) -> np.ndarray:
        """[n, w] MapPointType id of the destination of each Travel row (``travel`` [n, w] bool), -1 elsewhere: row A
        (``oi``) = rank of the destination among the legal points sorted by (col, row) (sim/VEC.md; ``model.py``'s
        map pointer). Reads ``map_nodes`` only for the slots with a Travel row (none on combat steps)."""
        out = np.full(oi.shape, -1, dtype=np.int64)
        slots = np.flatnonzero(travel.any(1))
        if slots.size == 0:
            return out
        mp = np.asarray(obs["map_nodes"])[slots]                    # [k, R, C, f]
        k = slots.size
        legal_cm = (mp[..., self.m_legal] != 0).transpose(0, 2, 1).reshape(k, -1)      # column-major: (col, row)
        pt_cm = mp[..., self.m_pt].astype(np.int64).transpose(0, 2, 1).reshape(k, -1)
        order = np.argsort(~legal_cm, axis=1, kind="stable")       # legal cells first, in (col, row) order
        pt_rank = np.take_along_axis(pt_cm, order, 1)               # [k, R * C]: type of the r-th legal point
        o = oi[slots]
        ok = travel[slots] & (o >= 0) & (o < legal_cm.sum(1, keepdims=True))
        val = np.take_along_axis(pt_rank, np.clip(o, 0, pt_rank.shape[1] - 1), 1)
        out[slots] = np.where(ok, val, -1)
        return out

    def uniforms(self, update: int, t: int, n_total: int) -> np.ndarray:
        """[2, n_total] float64 in [0, 1): coin and pick of every slot of the N-slot batch at (update, t)."""
        key = (int(update), int(t), int(n_total))
        if key != self._u_key:              # --overlap double: both lanes of a step share one draw
            self._u_key, self._u = key, np.random.default_rng([self.seed, key[0], key[1]]).random((2, key[2]))
        return self._u

    # ------------------------------------------------------------------------------------------- sampling

    def __call__(self, obs: dict, logits: torch.Tensor, dist, a: torch.Tensor, logp: torch.Tensor, *, update: int,
                 t: int, lo: int = 0, n_total: int | None = None):
        """One rollout step of slots [lo, lo + n) of an `n_total`-slot batch: `logits` [n, w] (what `dist` was built
        from), `a` / `logp` the policy's draw. -> (actions, stored log-probs, advantage weights or None): the actions
        with the mixture draws substituted, the log-prob per `logp_mode`, w = pi_old / b for ``decoupled``. Slots
        without an explore row keep the policy's action and log-prob unchanged (the same tensors' values)."""
        t0 = time.perf_counter()
        try:
            return self._step(obs, logits, dist, a, logp, update, t, lo, n_total)
        finally:
            self.acc["t"] += time.perf_counter() - t0

    def _step(self, obs, logits, dist, a, logp, update, t, lo, n_total):
        n, w = int(logits.shape[0]), int(logits.shape[1])
        n_total = n if n_total is None else int(n_total)
        valid = np.asarray(obs["cand_n"]) > 0
        self.acc["decisions"] += int(valid.sum())
        self.run["decisions"] += int(valid.sum())
        rows_i, macro = self.item_rows(obs, w)      # host-side scan of the observation: no device sync
        if not rows_i.any():                        # no explore row at all (e.g. every combat step): untouched
            return a, logp, (np.ones(n, dtype=np.float32) if self.logp_mode == "decoupled" else None)
        lp_all = dist.logits                        # normalised log-probs of pi (Categorical); masked rows ~ -1e9
        lp_np = lp_all.float().cpu().numpy()        # the one [n, w] copy: live mask, probabilities, log pi
        rows_i &= (lp_np > NEG_LIVE)[None]
        rows = rows_i.any(0)                                # [n, w]: the union = the explore rows
        cnt = rows.sum(1)
        present = cnt > 0
        if not present.any():
            return a, logp, (np.ones(n, dtype=np.float32) if self.logp_mode == "decoupled" else None)
        a_np = a.cpu().numpy().astype(np.int64)
        u = self.uniforms(update, t, n_total)[:, lo:lo + n]
        fire = present & (u[0] < self.eps)
        k = np.minimum((u[1] * np.maximum(cnt, 1)).astype(np.int64), np.maximum(cnt - 1, 0))
        csum = np.cumsum(rows, 1)                           # the k-th explore row (0-based) of each slot
        pick = np.argmax(csum > k[:, None], 1)
        a_new_np = np.where(fire, pick, a_np)
        dev = a.device
        a_new = torch.from_numpy(a_new_np).to(dev, a.dtype)
        lp_pi = lp_all.gather(1, a_new.unsqueeze(1)).squeeze(1)
        lp_pi = torch.where(torch.from_numpy(fire).to(dev), lp_pi, logp)    # unchanged where pi drew
        p_np = np.exp(lp_np)
        self._count(rows_i, macro, p_np, a_new_np, fire)
        mass = (p_np * rows).sum(1)
        self.acc["present"] += int(present.sum())
        self.acc["fired"] += int(fire.sum())
        self.acc["mass"] += float(mass[present].sum())
        self.run["explore_steps"] += int(fire.sum())
        self.run["explore_decisions"] += int(present.sum())
        ar = np.arange(n)
        in_np = rows[ar, a_new_np] & present
        lu_np = self._loge - np.log(np.maximum(cnt, 1))
        lpi_np = lp_np[ar, a_new_np].astype(np.float64)    # host copy of log pi(a) (w, explore_mix_clip0)
        lb_np = np.where(in_np, np.logaddexp(self._log1m + lpi_np, lu_np), self._log1m + lpi_np)
        lb_np = np.where(present, lb_np, lpi_np)
        r0 = np.exp(lpi_np - lb_np)                         # the mixture mode's start ratio pi / b
        self.acc["clip0"] += int((present & (np.abs(r0 - 1.0) > self.clip)).sum())
        if self.logp_mode == "decoupled":
            return a_new, lp_pi, r0.astype(np.float32)
        if self.logp_mode == "policy":
            return a_new, lp_pi, None
        in_rows = torch.from_numpy(in_np).to(dev)
        pres = torch.from_numpy(present).to(dev)
        lu = torch.from_numpy(lu_np).to(dev, lp_pi.dtype)
        lp_b = torch.where(in_rows, torch.logaddexp(self._log1m + lp_pi, lu), self._log1m + lp_pi)
        return a_new, torch.where(pres, lp_b, lp_pi), None

    def _count(self, rows_i, macro, p_np, a_np, fire) -> None:
        n = rows_i.shape[1]
        ar = np.arange(n)
        a_c = np.clip(a_np, 0, rows_i.shape[2] - 1)
        P = self.per
        for i in range(len(self.items)):
            r = rows_i[i]
            rm = r & macro
            on, onm = r.any(1), rm.any(1)
            took = r[ar, a_c] & (a_np >= 0)
            tookm = rm[ar, a_c] & (a_np >= 0)
            P["present"][i] += on.sum()
            P["present_macro"][i] += onm.sum()
            P["taken"][i] += took.sum()
            P["taken_macro"][i] += tookm.sum()
            P["drawn"][i] += (took & fire).sum()
            P["drawn_macro"][i] += (tookm & fire).sum()
            if p_np is not None:
                P["pmass"][i] += float((p_np * r).sum(1)[on].sum())
                P["pmass_macro"][i] += float((p_np * rm).sum(1)[onm].sum())

    # ------------------------------------------------------------------------------------------- reporting

    def pop_stats(self) -> dict:
        """This update's metrics (module docstring); resets the counters."""
        A, P = self.acc, self.per
        out = {"explore_eps": self.eps, "explore_decisions": A["present"], "explore_steps": A["fired"],
               "explore_share": A["present"] / A["decisions"] if A["decisions"] else None,
               "explore_policy_mass": A["mass"] / A["present"] if A["present"] else None,
               "explore_mix_clip0": A["clip0"] / A["present"] if A["present"] else None,
               "t_explore_s": round(A["t"], 4)}
        for i, it in enumerate(self.items):
            lb = it.label
            out[f"explore_present.{lb}"] = int(P["present"][i])
            out[f"explore_pmass.{lb}"] = float(P["pmass"][i] / P["present"][i]) if P["present"][i] else None
            out[f"explore_taken.{lb}"] = int(P["taken"][i])
            out[f"explore_drawn.{lb}"] = int(P["drawn"][i])
            out[f"explore_present_macro.{lb}"] = int(P["present_macro"][i])
            out[f"explore_pmass_macro.{lb}"] = (float(P["pmass_macro"][i] / P["present_macro"][i])
                                                if P["present_macro"][i] else None)
            out[f"explore_taken_macro.{lb}"] = int(P["taken_macro"][i])
            out[f"explore_drawn_macro.{lb}"] = int(P["drawn_macro"][i])
        self._zero()
        return out

    def describe(self) -> str:
        return (f"explore: kinds {self.kinds}, eps {self.eps:g} (rollout only; mixture draws uniform over the explore "
                f"rows present), stored log-prob: {self.logp_mode}"
                + (" (advantage x pi_old / b)" if self.logp_mode == "decoupled" else ""))

    def totals(self) -> dict:
        return {"kinds": self.kinds, "eps": self.eps, "logp_mode": self.logp_mode, **self.run}
