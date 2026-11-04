"""Loop breaker: the greedy evaluation policy (and optionally the rollout sampling) does not repeat a no-op or a cycle.

A no-op is an action after which the slot's next observation is identical, in every buffer the model reads
(``features.USED_BUFFERS``), to the one it was taken at: e.g. ``BuyEntry`` of a potion with a full potion belt, or an
illegal index. A cycle returns to an earlier observation through others: Rewards -> ``ClaimReward`` (card) ->
CardReward -> skip -> the same Rewards view, or shop -> card removal -> prompt -> cancel -> the same shop. Either way
the model sees the same input again and a greedy policy repeats itself until the env's ``floor_step_cap`` ends the
game ``stuck`` (2026-11-04 evals, library 5dbfa09dd9dcb5ed: up to 15 of 20 games on silent-a0-v1 / silent-a10-v1).
Library 3b73317739c650bd removes the potion no-op and the card-reward cycle at the source (a potion purchase or claim
the game would refuse is not listed; a card reward is taken in one ``TakeRewardCard`` decision); illegal indices and
the shop removal / cancel cycle remain.

Per env slot `LoopBreaker` keeps a 64-bit digest of the model-read buffers (`ObsDigest`) for each of its last
``window`` decisions (default 64) and the action taken there. At an observation whose digest is in the window, every
action already taken at it led back to it, so those candidate rows are banned (logit set to ``model.NEG``) and the
policy takes the best remaining row. When the slot has been at this observation ``k`` times before (default 3: e.g.
three no-ops in a row) only the rows that leave the screen (`LEAVE_KINDS`: proceed / leave / skip relic / end turn /
prompt confirm or cancel) stay unmasked when one of them is legal and not banned (the best of them is taken), else the
best remaining row. When every legal row is banned nothing is masked (the env's caps end the game as before).

The leave kinds are looked up by name in the RunActionKind members of the spec the breaker is built with, which is the
library's (``ModelSpec.from_schema(env.schema)``: the kind ids the observations' ``cand`` rows carry), so an enum that
gains members never shifts them. The Vec layer's rewards-screen macros (``TakeRewardCard``, ``TakeRewardAlternative``,
appended to RunActionKind as 21 / 22 in 3b73317739c650bd) are real actions, like ``ClaimReward``: they take a card, or
reroll / sacrifice a card reward, and are never leave rows (declining the cards is ``ProceedRewards``, a leave row).

Counters per slot: ``no_op_actions`` (actions after which the observation was unchanged), ``cycle_revisits`` (returns
to an observation of the window through other observations; both counted with the breaker on or off) and
``loop_breaks`` (decisions the breaker changed: greedy, the chosen row is not the model's argmax; sampling, the
distribution was masked). The chosen actions are what the env receives, so the evaluator's replay audit replays them
like any other policy's.
"""

from __future__ import annotations

import numpy as np
import torch

from .features import USED_BUFFERS, ModelSpec

LEAVE_KINDS = ("ProceedRewards", "LeaveShop", "LeaveRestSite", "LeaveEvent", "SkipTreasureRelic", "EndTurn",
               "PromptConfirm", "PromptCancel")
DEFAULT_K = 3
DEFAULT_WINDOW = 64
NEG = -1e9                          # = model.NEG (the model's masked-row logit)


class ObsDigest:
    """64-bit digest per env slot of the bytes of the model-read buffers: the sum over the slot's 4-byte words w_j
    (single bytes for a buffer whose per-slot size is not a multiple of 4) of w_j * K_j mod 2^64, with fixed odd
    pseudo-random K_j per buffer and position; one uint64 matmul per buffer for all slots. Equal observations give
    equal digests; two observations that differ in one word never collide (K_j odd), differing in several words they
    collide with probability <= 2^-33 over the keys."""

    def __init__(self, names=USED_BUFFERS, seed: int = 0x5EED100B):
        self.names = tuple(names)
        self.seed = int(seed)
        self._keys: dict[tuple[int, int], np.ndarray] = {}

    def _key(self, j: int, m: int) -> np.ndarray:
        k = self._keys.get((j, m))
        if k is None:
            k = np.random.default_rng([self.seed, j, m]).integers(0, 2**63, size=m, dtype=np.uint64)
            k = k * np.uint64(2) + np.uint64(1)
            self._keys[(j, m)] = k
        return k

    def __call__(self, obs: dict, idx: np.ndarray | None = None) -> np.ndarray:
        """Digests (uint64) of every slot of `obs`, or of the slots `idx`."""
        n = len(obs[self.names[0]]) if idx is None else len(idx)
        h = np.zeros(n, dtype=np.uint64)
        if n == 0:
            return h
        for j, name in enumerate(self.names):
            a = obs[name] if idx is None else obs[name][idx]
            b = np.ascontiguousarray(a).reshape(n, -1).view(np.uint8)
            w = b.view(np.uint32) if b.shape[1] % 4 == 0 else b
            h += w.astype(np.uint64) @ self._key(j, w.shape[1])
        return h


class LoopBreaker:
    """Per-slot no-op / cycle detection and masking (module docstring). Greedy use: ``a = lb.greedy(obs, logits,
    live)``. Sampling use: ``logits = lb.apply(obs, logits)``, sample, ``lb.commit(actions)``, and ``lb.reset(done)``
    after the env step (auto-reset slots start new episodes). With ``enabled=False`` it only counts.

    ``num_envs`` may be left out: the arrays are sized at the first call. `spec` is the library's (the one the env's
    observations follow): the leave kinds resolve by name against its RunActionKind members (`leave_kinds`, in
    `LEAVE_KINDS` order; a name the library does not have is left out) and the kind column is its ``cand.kind``."""

    def __init__(self, spec: ModelSpec, num_envs: int | None = None, *, k: int = DEFAULT_K,
                 window: int = DEFAULT_WINDOW, enabled: bool = True):
        if int(k) < 1 or int(window) < 1:
            raise ValueError(f"loop breaker k {k} and window {window} must be >= 1")
        names = list(spec.enum_names["RunActionKind"])
        self.leave_kinds = tuple(x for x in LEAVE_KINDS if x in names)     # by name: appended kinds shift nothing
        self.leave_ids = frozenset(names.index(x) for x in self.leave_kinds)
        self.kind_col = int(spec.cols["cand"]["kind"])
        self.k = int(k)
        self.window = int(window)
        self.enabled = bool(enabled)
        self.digest = ObsDigest()
        self.keys: list[str] | None = None     # slot labels (the evaluator's game keys), set by the caller
        self.n = 0
        self._popped = (0, 0, 0)
        if num_envs:
            self._alloc(int(num_envs))

    @property
    def mode(self) -> str:
        return "on" if self.enabled else "off"

    def _alloc(self, n: int) -> None:
        self.n = n
        self.ring_h = np.zeros((n, self.window), dtype=np.uint64)   # digests of the slot's last decisions
        self.ring_a = np.full((n, self.window), -1, dtype=np.int64)  # the actions taken there (-1: empty)
        self.ptr = np.zeros(n, dtype=np.int64)           # next ring position
        self.cur_h = np.zeros(n, dtype=np.uint64)        # this call's digests
        self.acted = np.zeros(n, dtype=bool)             # slots deciding at this call
        self.masked = np.zeros(n, dtype=bool)            # ... under a mask
        self.no_op_actions = np.zeros(n, dtype=np.int64)
        self.cycle_revisits = np.zeros(n, dtype=np.int64)
        self.loop_breaks = np.zeros(n, dtype=np.int64)

    def apply(self, obs: dict, logits: torch.Tensor, live: np.ndarray | None = None) -> torch.Tensor:
        """Look this observation up in each deciding slot's window and return `logits` with the rows already taken at
        it set to NEG (the same tensor when nothing is masked). Deciding slots: ``cand_n > 0`` (and `live`)."""
        n, w = int(logits.shape[0]), int(logits.shape[1])
        if self.n == 0:
            self._alloc(n)
        elif self.n != n:
            raise ValueError(f"loop breaker sized for {self.n} slots, called with {n}")
        cand_n = np.asarray(obs["cand_n"])
        act = cand_n > 0
        if live is not None:
            act &= np.asarray(live, dtype=bool)
        self.acted = act
        self.masked = np.zeros(n, dtype=bool)
        idx = np.flatnonzero(act)
        if len(idx) == 0:
            return logits
        h = self.digest(obs, None if len(idx) == n else idx)
        self.cur_h[idx] = h
        ra = self.ring_a[idx]
        match = (self.ring_h[idx] == h[:, None]) & (ra >= 0)       # [m, window]: earlier decisions at this observation
        rev = match.any(1)
        if not rev.any():
            return logits
        last = (self.ptr[idx] - 1) % self.window
        imm = match[np.arange(len(idx)), last]                       # the previous action changed nothing
        self.no_op_actions[idx[imm]] += 1
        self.cycle_revisits[idx[rev & ~imm]] += 1
        if not self.enabled:
            return logits
        mask = None
        for r in np.flatnonzero(rev):
            s = int(idx[r])
            tried = ra[r][match[r]]
            lim = min(int(cand_n[s]), w)
            banned = set(tried.tolist())
            ok = [j for j in range(lim) if j not in banned]
            if not ok:              # every legal row already led back here: nothing to break
                continue
            if len(tried) >= self.k:
                kinds = obs["cand"][s, :lim, self.kind_col]
                ok = [j for j in ok if int(kinds[j]) in self.leave_ids] or ok
            if mask is None:
                mask = np.zeros((n, w), dtype=bool)
            mask[s] = True
            mask[s, ok] = False
            self.masked[s] = True
        if mask is None:
            return logits
        return logits.masked_fill(torch.from_numpy(mask).to(logits.device), NEG)

    def commit(self, actions, plain=None) -> None:
        """Record the actions taken at this call's observations (-1 = none). `plain` (greedy): the model's own argmax,
        a loop break is a decision that differs from it; without it (sampling) a decision taken under a mask."""
        a = np.asarray(actions).astype(np.int64)
        took = self.acted & (a >= 0)
        s = np.flatnonzero(took)
        p = self.ptr[s]
        self.ring_h[s, p] = self.cur_h[s]
        self.ring_a[s, p] = a[s]
        self.ptr[s] = (p + 1) % self.window
        broke = self.masked if plain is None else a != np.asarray(plain).astype(np.int64)
        self.loop_breaks += took & broke

    def greedy(self, obs: dict, logits: torch.Tensor, live: np.ndarray | None = None) -> np.ndarray:
        """The greedy action per slot (int32) with the loop breaker applied."""
        plain = logits.argmax(-1)
        lg = self.apply(obs, logits, live)
        a = (plain if lg is logits else lg.argmax(-1)).cpu().numpy().astype(np.int32)
        self.commit(a, plain.cpu().numpy())
        return a

    def reset(self, slots) -> None:
        """Forget the slots' windows (a bool mask or indices): new episodes after an auto-reset."""
        if self.n == 0:
            return
        sl = np.asarray(slots)
        sl = np.flatnonzero(sl) if sl.dtype == bool else sl.astype(np.int64)
        self.ring_a[sl] = -1

    def totals(self) -> dict:
        """Totals over every slot so far."""
        z = np.zeros(0, dtype=np.int64)
        lb, no, cy = (self.loop_breaks, self.no_op_actions, self.cycle_revisits) if self.n else (z, z, z)
        return {"mode": self.mode, "k": self.k, "window": self.window, "loop_breaks": int(lb.sum()),
                "no_op_actions": int(no.sum()), "cycle_revisits": int(cy.sum()),
                "slots_with_loop_breaks": int((lb > 0).sum()), "slots_with_no_ops": int((no > 0).sum()),
                "slots_with_cycles": int((cy > 0).sum())}

    def pop_counts(self) -> tuple[int, int, int]:
        """(loop breaks, no-op actions, cycle revisits) since the previous call (per-update metrics)."""
        t = self.totals()
        cur = (t["loop_breaks"], t["no_op_actions"], t["cycle_revisits"])
        d = tuple(c - p for c, p in zip(cur, self._popped))
        self._popped = cur
        return d
