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

Log-prob stored for PPO (``logp_mode``):

- ``mixture`` (default): store log b(a|s). The update's ratio is pi_new / b, the importance ratio of the samples
  actually drawn, so the surrogate's gradient at pi_new = pi_old is the unbiased on-policy gradient of pi. Caveat (by
  construction): at an explore decision the ratio starts at pi / b, not 1: 1 / (1 - E) for a policy-drawn row (1.43 at
  E = 0.3, outside the 0.2 clip, so a positive advantage there gives no gradient while a negative one does), and
  ~ pi / (E u) ~ 0 for an explored row the policy gives ~0 to, whose gradient is then ~ ratio x A x grad log pi:
  correct but small, so pi on the rare rows grows only as fast as the true policy gradient lets it (approx_kl /
  clipfrac include those samples).
- ``decoupled``: store log pi_old(a|s) and multiply the normalised advantage by w = pi_old / b (decoupled PPO, Hilton
  et al. 2021: the proximal policy pi_old is separated from the behaviour policy b). Same expected gradient as
  ``mixture`` at the start of the update, but the clip window is centred on pi_old (the ratio starts at 1 everywhere).
- ``policy``: store log pi_old and ignore the mixture (ratio = pi_new / pi_old as if pi had drawn every action): biased
  off-policy; the explored rows get the full A x grad log pi push (fast recovery of the rare rows' probability, also for
  rows whose advantage is only noise).

Randomness. The draws of slot s at rollout step t of update u (the update the rollout belongs to, absolute) come from
``np.random.default_rng([purpose_seed(sample_seed, "explore"), u, t])`` (two uniforms per slot of the N-slot batch: the
coin and the pick): a pure function of the sample seed and (u, t, s). No state to checkpoint; a resume continues the
same stream at its update, ``--overlap double`` draws slot s as ``--overlap off`` does (each lane takes its rows of the
N-slot draw), and E = 0 builds no explorer at all (the trainer computes exactly what it computed before; the torch
sample RNG is never touched by the explorer either way).

Metrics per update (``pop_stats``): ``explore_eps``, ``explore_decisions`` (decisions with >= 1 explore row),
``explore_share`` (of the valid decisions), ``explore_steps`` (mixture draws), ``explore_policy_mass`` (mean of pi's
summed probability on the explore rows at those decisions), and per item ``explore_present.<item>``,
``explore_pmass.<item>`` (mean pi mass on the item's rows where present), ``explore_taken.<item>`` /
``explore_drawn.<item>`` (actions taken on the item's rows / of them by a mixture draw), plus ``..._macro.<item>``
variants over the item's macro rows (B >= 0): ``explore_pmass_macro.ChooseRestOption`` is P(SMITH:<card>) at rest sites
with macros on.
"""

from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np
import torch

from .features import ModelSpec

LOGP_MODES = ("mixture", "decoupled", "policy")
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
    sel: str | None         # None | "macro" | "plain" | "shop" | "rest"
    sel_id: int = -1        # ShopCategory / RestOption member id for "shop" / "rest"


class KindExplorer:
    """The mixture sampler of the module docstring. `spec` = the library's (``ModelSpec.from_schema(env.schema)``);
    `kinds` = ``parse_kinds`` input; raises ValueError on a kind or selector the library does not have."""

    def __init__(self, spec: ModelSpec, kinds, eps: float, seed: int, *, logp_mode: str = "mixture"):
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
                else:
                    ok = list(GENERIC_SELECTORS) + (["remove"] + shop_names if kind == "BuyEntry" else
                                                    rest_names if kind == "ChooseRestOption" else [])
                    raise ValueError(f"explore selector {label!r}: unknown for {kind} (expected one of {ok})")
            self.items.append(it)
        self.kinds = format_kinds(items)
        self.eps = eps
        self.logp_mode = logp_mode
        self.seed = int(seed)
        C = spec.cols["cand"]
        self.c_kind, self.c_b, self.c_oi = C["kind"], C["B"], C["option_index"]
        self.shop_cat = spec.cols["shop"]["category"]
        self.rest_opt = spec.cols["rest"]["option_id"]
        self._log1m = math.log1p(-eps)
        self._loge = math.log(eps)
        self._zero()
        self.run = {"explore_steps": 0, "explore_decisions": 0, "decisions": 0}

    # ------------------------------------------------------------------------------------------- rows

    def _zero(self) -> None:
        L = len(self.items)
        self.acc = {"decisions": 0, "present": 0, "fired": 0, "mass": 0.0}
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
        oi = None
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
            rows[i, :, :w_c] = r
        return rows, macro_full

    def uniforms(self, update: int, t: int, n_total: int) -> np.ndarray:
        """[2, n_total] float64 in [0, 1): coin and pick of every slot of the N-slot batch at (update, t)."""
        rng = np.random.default_rng([self.seed, int(update), int(t)])
        return rng.random((2, int(n_total)))

    # ------------------------------------------------------------------------------------------- sampling

    def __call__(self, obs: dict, logits: torch.Tensor, dist, a: torch.Tensor, logp: torch.Tensor, *, update: int,
                 t: int, lo: int = 0, n_total: int | None = None):
        """One rollout step of slots [lo, lo + n) of an `n_total`-slot batch: `logits` [n, w] (what `dist` was built
        from), `a` / `logp` the policy's draw. -> (actions, stored log-probs, advantage weights or None): the actions
        with the mixture draws substituted, the log-prob per `logp_mode`, w = pi_old / b for ``decoupled``. Slots
        without an explore row keep the policy's action and log-prob unchanged (the same tensors' values)."""
        n, w = int(logits.shape[0]), int(logits.shape[1])
        n_total = n if n_total is None else int(n_total)
        rows_i, macro = self.item_rows(obs, w)
        live = (logits > NEG_LIVE).cpu().numpy()
        rows_i &= live[None]
        rows = rows_i.any(0)                                # [n, w]: the union = the explore rows
        cnt = rows.sum(1)
        present = cnt > 0
        valid = np.asarray(obs["cand_n"]) > 0
        self.acc["decisions"] += int(valid.sum())
        self.run["decisions"] += int(valid.sum())
        a_np = a.cpu().numpy().astype(np.int64)
        if not present.any():
            self._count(rows_i, macro, None, a_np, np.zeros(n, dtype=bool))
            w_out = np.ones(n, dtype=np.float32) if self.logp_mode == "decoupled" else None
            return a, logp, w_out
        u = self.uniforms(update, t, n_total)[:, lo:lo + n]
        fire = present & (u[0] < self.eps)
        k = np.minimum((u[1] * np.maximum(cnt, 1)).astype(np.int64), np.maximum(cnt - 1, 0))
        csum = np.cumsum(rows, 1)                           # the k-th explore row (0-based) of each slot
        pick = np.argmax(csum > k[:, None], 1)
        a_new_np = np.where(fire, pick, a_np)
        dev = a.device
        a_new = torch.from_numpy(a_new_np).to(dev, a.dtype)
        lp_all = dist.logits                                # normalised log-probs of pi (Categorical)
        lp_pi = lp_all.gather(1, a_new.unsqueeze(1)).squeeze(1)
        lp_pi = torch.where(torch.from_numpy(fire).to(dev), lp_pi, logp)    # unchanged where pi drew
        probs = lp_all.exp()
        p_np = probs.float().cpu().numpy()
        self._count(rows_i, macro, p_np, a_new_np, fire)
        mass = (p_np * rows).sum(1)
        self.acc["present"] += int(present.sum())
        self.acc["fired"] += int(fire.sum())
        self.acc["mass"] += float(mass[present].sum())
        self.run["explore_steps"] += int(fire.sum())
        self.run["explore_decisions"] += int(present.sum())
        in_rows = torch.from_numpy(rows[np.arange(n), a_new_np] & present).to(dev)
        pres = torch.from_numpy(present).to(dev)
        lu = torch.from_numpy(self._loge - np.log(np.maximum(cnt, 1))).to(dev, lp_pi.dtype)
        lp_b = torch.where(in_rows, torch.logaddexp(self._log1m + lp_pi, lu), self._log1m + lp_pi)
        lp_b = torch.where(pres, lp_b, lp_pi)
        if self.logp_mode == "mixture":
            return a_new, lp_b, None
        if self.logp_mode == "decoupled":
            return a_new, lp_pi, (lp_pi - lp_b).exp().cpu().numpy().astype(np.float32)
        return a_new, lp_pi, None

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
               "explore_policy_mass": A["mass"] / A["present"] if A["present"] else None}
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
                f"rows present), stored log-prob: {self.logp_mode}")

    def totals(self) -> dict:
        return {"kinds": self.kinds, "eps": self.eps, "logp_mode": self.logp_mode, **self.run}
