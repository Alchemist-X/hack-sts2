"""Forced-choice what-if evaluation (``sts2train eval --force SELECTORS [--force-mode greedy|uniform]``, wave 16,
2026-11-06). Evaluation only: the trainer never builds one.

Question it answers. The policy never upgrades at rest sites, never removes cards and avoids elites (behaviour audits
of waves 9-15). Is that "rational" for its current combat strength (forcing the choice hurts the score) or a collapse
of rare rows that PPO never saw the advantage of (forcing helps, or is neutral)? A paired what-if eval on the same seed
set answers it per checkpoint.

Selectors. The grammar of the training explorer (``sts2train.explore.parse_kinds`` / ``KindExplorer``): ``Kind`` = every
row of that RunActionKind, ``Kind:macro`` / ``Kind:plain``, ``ChooseRestOption:SMITH`` (rows of that RestOption, macro or
plain), ``BuyEntry:remove`` (card-removal rows), ``Travel:Elite`` (Travel rows whose destination has that MapPointType),
``ProceedRewards:skipcard`` (the proceed row only where a card row is listed). The row masks are the explorer's own
``item_rows`` (an explorer built with a dummy eps whose sampler is never called), so a selector selects here exactly
the rows it selects in training.

Semantics. At a decision of a live game (``cand_n > 0``) where at least one selector has >= 1 present row (legal,
within the logits' width, logit above ``NEG_LIVE``, i.e. not masked by the model), the action is restricted to the
union of the selectors' rows: ``greedy`` (default) takes the union row with the highest policy logit, ``uniform`` one
uniformly at random from the play's own generator (sha256 of ("sts2train-force", seed, game key), one draw per forced
decision only: independent of the batch). Implementation: every other row of that slot gets the model's masked logit
(``NEG``), so the evaluator's greedy argmax / sampled draw / loop breaker see a one-row distribution there. Every other
decision is untouched (the same logits tensor). ``observe`` (python API only) restricts nothing and only counts (the
baseline's availability and the policy's own take rate of each selector).

Counts (per game into ``games/<KEY>.summary.json`` under ``force``, totals into report.json / the returned report under
``force``): per selector ``available`` (decisions where its rows were present), ``forced_decisions`` (forced decisions
whose chosen row belongs to the selector; = available for a single selector), ``changed`` (... where the chosen row
differs from the policy's own argmax: how often forcing actually changed the action), ``policy_took`` (decisions
where the unforced argmax already lay on the selector's rows), plus ``forced_total`` / ``changed_total`` (decisions,
each counted once) and ``games_forced``. A loop breaker (eval default on) may still override a forced row at a
revisited observation; the counts are of the forced choice before it (the wave-16 readouts run with it off).
"""

from __future__ import annotations

import hashlib

import numpy as np
import torch

from .explore import NEG_LIVE, KindExplorer, format_kinds, parse_kinds
from .features import ModelSpec

FORCE_MODES = ("greedy", "uniform")        # [0] = the default; "observe" (API only) counts without forcing
NEG = -1e9                                 # = model.NEG


def force_rng(seed: int, key: str) -> np.random.Generator:
    """The uniform forced draw's generator of one play (independent of slot order and batch)."""
    h = hashlib.sha256(f"sts2train-force:{int(seed)}:{key}".encode("utf-8")).digest()
    return np.random.default_rng(int.from_bytes(h[:8], "little"))


def parse_force(text) -> str:
    """Normalised selector string ("" = off) of ``--force``; ValueError on bad syntax."""
    return format_kinds(parse_kinds(text))


class ForcedChoice:
    """The logits filter of the module docstring. `spec` = the library's (the env's observations); `selectors` =
    ``parse_kinds`` input (non-empty); raises ValueError on a kind / selector the library does not have."""

    def __init__(self, spec: ModelSpec, selectors, mode: str = FORCE_MODES[0], seed: int = 0):
        items = parse_kinds(selectors)
        if not items:
            raise ValueError("force: no selectors given")
        if mode not in FORCE_MODES + ("observe",):
            raise ValueError(f"force mode {mode!r}: expected one of {FORCE_MODES}")
        # the explorer's selector validation and row masks only; its sampler (and eps) is never used
        self._rows = KindExplorer(spec, format_kinds(items), 0.5, 0)
        self.labels = [it.label for it in self._rows.items]
        self.selectors = format_kinds(items)
        self.mode = mode
        self.seed = int(seed)
        self.keys: list[str] | None = None
        self._rngs: list | None = None
        self.n = 0

    def _alloc(self, n: int) -> None:
        L = len(self.labels)
        self.n = n
        self.c = {k: np.zeros((n, L), dtype=np.int64) for k in ("available", "forced_decisions", "changed",
                                                                 "policy_took")}
        self.forced = np.zeros(n, dtype=np.int64)
        self.changed_any = np.zeros(n, dtype=np.int64)

    def apply(self, obs: dict, logits: torch.Tensor, live=None, keys=None) -> torch.Tensor:
        """`logits` [n, w] with the forced slots restricted to their chosen row (the same tensor when no slot is
        forced, or in ``observe`` mode). `live` [n] bool (default all), `keys` the games' keys (uniform mode's
        generators; default slot indices)."""
        n, w = int(logits.shape[0]), int(logits.shape[1])
        if self.n == 0:
            self._alloc(n)
        elif self.n != n:
            raise ValueError(f"forced choice sized for {self.n} slots, called with {n}")
        if self.keys is None:
            self.keys = [str(k) for k in keys] if keys is not None else [str(i) for i in range(n)]
        rows_i, _ = self._rows.item_rows(obs, w)              # [I, n, w]: host-side scan, no device copy
        act = np.asarray(obs["cand_n"]) > 0
        if live is not None:
            act &= np.asarray(live, dtype=bool)
        rows_i &= act[None, :, None]
        if not rows_i.any():
            return logits
        z = logits.detach().float().cpu().numpy()
        rows_i &= (z > NEG_LIVE)[None]
        rows = rows_i.any(0)                                   # [n, w]: the union
        present = rows.any(1)
        if not present.any():
            return logits
        plain = z.argmax(1)
        zm = np.where(rows, z, -np.inf)
        choice = zm.argmax(1)                                  # greedy: the max-logit forced row
        if self.mode == "uniform":
            if self._rngs is None:
                self._rngs = [force_rng(self.seed, k) for k in self.keys]
            for s in np.flatnonzero(present).tolist():
                idx = np.flatnonzero(rows[s])
                choice[s] = int(idx[int(self._rngs[s].integers(len(idx)))])
        elif self.mode == "observe":
            choice = plain
        ar = np.arange(n)
        pres_i = rows_i.any(2)                                 # [I, n]
        on_choice = rows_i[:, ar, choice] & present[None]      # [I, n]
        on_plain = rows_i[:, ar, plain] & present[None]
        ch = present & (choice != plain)
        self.c["available"] += pres_i.T
        self.c["policy_took"] += on_plain.T
        if self.mode == "observe":
            return logits
        self.c["forced_decisions"] += on_choice.T
        self.c["changed"] += (on_choice & ch[None]).T
        self.forced += present
        self.changed_any += ch
        keep = np.zeros((n, w), dtype=bool)
        keep[~present] = True
        keep[np.flatnonzero(present), choice[present]] = True
        return logits.masked_fill(torch.from_numpy(~keep).to(logits.device), NEG)

    # ------------------------------------------------------------------------------------------- reporting

    def per_game(self, i: int) -> dict:
        return {"mode": self.mode, **{k: {lb: int(v[i, j]) for j, lb in enumerate(self.labels)}
                                      for k, v in self.c.items()},
                "forced_total": int(self.forced[i]), "changed_total": int(self.changed_any[i])}

    def totals(self) -> dict:
        if self.n == 0:
            self._alloc(0)
        out = {"selectors": self.labels, "mode": self.mode, "seed": self.seed if self.mode == "uniform" else None}
        for k, v in self.c.items():
            out[k] = {lb: int(v[:, j].sum()) for j, lb in enumerate(self.labels)}
        out.update(forced_total=int(self.forced.sum()), changed_total=int(self.changed_any.sum()),
                   games_forced=int((self.forced > 0).sum()), games_changed=int((self.changed_any > 0).sum()))
        for lb in self.labels:      # flat aliases: forced_decisions.<selector>, available.<selector>
            out[f"forced_decisions.{lb}"] = out["forced_decisions"][lb]
            out[f"available.{lb}"] = out["available"][lb]
        return out

    def describe(self) -> str:
        if self.mode == "observe":
            return f"forced choice (observe): {self.selectors}; counting only, no action is restricted"
        return (f"forced choice ({self.mode}): {self.selectors}; at a decision with a selector row present the action "
                "is restricted to those rows")


def write_force_stats(fc: ForcedChoice, out_dir, keys: list[str]) -> dict:
    """Add ``force`` (per-game counts) to every game summary and the totals to report.json; -> the totals."""
    import json
    from pathlib import Path

    out = Path(out_dir)
    for i, key in enumerate(keys):
        p = out / "games" / f"{key}.summary.json"
        s = json.loads(p.read_text(encoding="utf-8"))
        s["force"] = fc.per_game(i) if i < fc.n else None
        with open(p, "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1)
    t = fc.totals()
    rp = out / "report.json"
    if rp.exists():
        r = json.loads(rp.read_text(encoding="utf-8"))
        r["force"] = t
        with open(rp, "w", encoding="utf-8") as f:
            json.dump(r, f, ensure_ascii=False, indent=1)
    return t
