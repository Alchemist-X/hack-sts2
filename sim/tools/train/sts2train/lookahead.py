"""Decision-time combat lookahead for an sts2train checkpoint: an honest, declared search-augmented evaluation row.

The policy is an ordinary ``sts2vec`` policy ``(obs, ctx) -> actions``, so the unmodified protocol evaluator
(``sts2vec.evaluate.evaluate``) plays the games, scores them and replay-audits them; the search is the policy's own
business and sees nothing the evaluator does not hand to any policy.

Mirror env. ``ctx`` carries no env handle, so the policy keeps a PRIVATE ``VecEnv`` (``auto_reset=False``, no step or
floor cap): slots ``[0, M)`` are shadows of the M games, built from ``ctx.keys`` (one ``pool.add_fresh`` per key, the
evaluator's own entries) and stepped with exactly the actions the policy returns. Every call compares the shadows'
observation buffers byte for byte with the evaluator's (all buffers, live games); a difference raises ``Desync``.
Slots ``[M, M + M*K*R)`` are rollout slots, handed out per search round.

Search (``LookaheadConfig``). Only where a game is live, in combat (``global_i.is_combat``) and has ``cand_n >= 2``:

- candidates: the greedy choice, EndTurn, then the remaining candidates by descending logit, at most K, never
  DiscardPotion; fewer than 2 -> no search;
- roots: R determinized clones of the shadow (``clone_determinized``: the same player view, draw order and every RNG
  stream resampled), seed ``u64(sha256(f"{seed}:{key}:{n}:{r}"))`` with ``n`` the game's decision index (actions taken
  so far), shared by all candidates of that ``r`` (common random numbers). Exact ``snapshot`` is never used: it would
  read the true draw order;
- the rollout slots of a round start from those roots in one go. ABI 2 libraries (``env.supports_branch``): one
  determinized ``branch`` per root (seed = ``inv_branch_seed`` of the determinization seed, so the root is exactly
  ``clone_determinized(shadow, seed)``), then one exact ``branch`` fan-out from each root slot to its other candidate
  slots; no pool entry. Exact branches only ever copy a rollout root, never a shadow (checked on every call). Older
  libraries: ``clone_determinized`` pool entries and one batched ``reset(mask, start_ids)``. Both paths give the same
  states (test); step 0 applies the candidate, then
  the model's greedy action on the rows still running (one batched forward per step over all games; byte-identical
  model inputs are evaluated once; trailing hand / enemy / potion rows that no row of the batch occupies or points
  at are cut (``trim_padding``): the model masks them, so only float rounding changes, as with any batch change);
- latch per slot (checked after every step, in this order): a SIM_ERROR / CONTENT_GAP / illegal-action status or a
  truncation -> invalid (logged); terminated -> victory / defeat; ``is_combat`` 1 -> 0 -> combat won (leaf); turn
  horizon: ``turn`` above the root's or ``cand_n == 0`` -> leaf; H steps -> leaf. A rollout never continues past the
  end of its combat;
- Q: defeat 0 (``score='hp'``: -1); victory ``1 - P(root) + win_bonus``; any other leaf ``P(leaf) - P(root)`` plus
  ``V(leaf)`` (``value``, ``value+hp``) plus ``0.10 * hp / max_hp - 0.03 * sum(enemy hp) / sum(enemy max hp)``
  (``value+hp``, ``hp``); P = protocol v0.2 progress of the state (``objective.state_progress``), V = the value head;
  mean over the valid determinizations; argmax over the candidates, ties (within ``TIE_EPS``) -> the greedy choice,
  no valid candidate -> greedy;
- the shadows then advance with the chosen actions. On the pool path every clone is a pool entry (~22-33 KB) that only
  closing the env frees: when the pool exceeds ``pool_budget`` the search env is rebuilt and every game's history
  replayed (state hash and observation of every shadow checked against the old env); the branch path never grows it.

ExIt hook: ``record`` (a callable, or ``evaluate_lookahead(record=True)`` -> ``searches.jsonl`` in the run directory)
receives one dict per searched decision: game ``key``, decision index ``n`` (the state is the game's start replayed
through ``history_actions[:n]`` of its summary), the candidate indices, their mean Q (None = no valid rollout), the
valid rollout count, the choice and the greedy choice. Recording never changes a decision.

Entry points: ``evaluate_lookahead`` (a normal protocol run directory, policy name ``nn:<ckpt>+la(K,R,horizon,honest)``,
``stats.json`` next to it), ``evaluate_greedy`` (the plain greedy baseline under the same torch thread count),
``paired`` (per-seed progress differences of two run directories), and the CLI:

    python -m sts2train.lookahead eval --seedset silent-a10-v1 --checkpoint best.pt --out RUN [--greedy] [--K 8 ...]
    python -m sts2train.lookahead paired GREEDY_RUN LOOKAHEAD_RUN [--out paired.json]
    STS2LA_CHECKPOINT=best.pt STS2LA_CONFIG='{"K": 8}' sts2vec eval --policy sts2train.lookahead:EnvLookahead ...
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import platform
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Callable

import numpy as np
import torch
from sts2vec import STATUS, VecEnv, load, schema_of
from sts2vec import protocol as PG
from sts2vec.evaluate import FLOOR_STEP_CAP, STEP_CAP
from sts2vec.evaluate import evaluate as vec_evaluate

from .features import USED_BUFFERS, ModelSpec, bind_spec, check_spec_compat, describe_widened, obs_to_torch
from .objective import state_progress

ALGO = "la-v1"                       # bump when the search semantics change (part of the policy hash)
HORIZONS = ("turn", "combat")
SCORES = ("value+hp", "value", "hp")
MAX_H = 40
HP_W, ENEMY_W = 0.10, 0.03           # value+hp / hp leaf terms
TIE_EPS = 1e-6                       # Q differences below this are ties (float32 value head)
FWD_CHUNK = 1024                     # rows per batched rollout forward
SEARCH_STEP_CAP = 1 << 30            # the mirror env never truncates (the evaluator's env enforces the protocol limits)
BAD_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"] | STATUS["ILLEGAL_ACTION"]
RUNNING, DEFEAT, VICTORY, WON, TURN, HORIZON, ERROR = range(7)
OUTCOMES = ("running", "defeat", "victory", "won", "turn", "horizon", "error")


class Desync(RuntimeError):
    """A shadow's observation differs from the evaluator's (or a rebuild did not reproduce the shadows)."""


@dataclass(frozen=True)
class LookaheadConfig:
    """K candidates, R determinizations, H steps per rollout, horizon ``turn`` | ``combat``, leaf score
    ``value+hp`` | ``value`` | ``hp``; ``threads`` = mirror env worker threads and torch threads; ``seed`` = the
    determinization seed; ``pool_budget`` = pool entries before the mirror env is rebuilt; ``device`` of the model."""

    K: int = 8
    R: int = 1
    H: int = 12
    horizon: str = "turn"
    score: str = "value+hp"
    threads: int = 4
    seed: int = 0
    pool_budget: int = 50_000
    device: str = "cpu"

    def __post_init__(self):
        if self.K < 1 or self.R < 1:
            raise ValueError(f"K and R must be >= 1 (got K={self.K}, R={self.R})")
        if not 1 <= self.H <= MAX_H:
            raise ValueError(f"H must be in 1..{MAX_H} (got {self.H})")
        if self.horizon not in HORIZONS:
            raise ValueError(f"horizon must be one of {HORIZONS} (got {self.horizon!r})")
        if self.score not in SCORES:
            raise ValueError(f"score must be one of {SCORES} (got {self.score!r})")
        if self.threads < 1 or self.pool_budget < 1:
            raise ValueError("threads and pool_budget must be >= 1")

    def search(self) -> dict:
        """The fields that define the search (hashed into the policy hash); threads / pool_budget / device are
        execution details."""
        return {"algo": ALGO, "K": self.K, "R": self.R, "H": self.H, "horizon": self.horizon, "score": self.score,
                "seed": self.seed, "information": "determinized"}

    @property
    def sha8(self) -> str:
        return hashlib.sha256(json.dumps(self.search(), sort_keys=True, separators=(",", ":")).encode()).hexdigest()[:8]

    @property
    def label(self) -> str:
        return f"la({self.K},{self.R},{self.horizon},honest)"


def det_seed(seed: int, key: str, n: int, r: int) -> int:
    """The determinization seed of game `key` at its decision `n`, sample `r` (uint64; batch- and slot-independent)."""
    return int.from_bytes(hashlib.sha256(f"{int(seed)}:{key}:{int(n)}:{int(r)}".encode()).digest()[:8], "little")


_M64 = (1 << 64) - 1
_GOLDEN, _MIX1, _MIX2 = 0x9E3779B97F4A7C15, 0xBF58476D1CE4E5B9, 0x94D049BB133111EB


def _unxorshift(y: int, k: int) -> int:
    x = y
    for _ in range(64 // k + 1):
        x = y ^ (x >> k)
    return x & _M64


def inv_branch_seed(target: int) -> int:
    """The `seed` with ``sts2vec.env.branch_seed(seed, 0) == target``: ``env.branch(src, [d], determinized=True,
    seed=inv_branch_seed(t))`` puts into ``d`` exactly ``clone_determinized(src, t)`` (the SplitMix64 finaliser is a
    bijection; this undoes it and the position-0 offset)."""
    z = _unxorshift(int(target) & _M64, 31)
    z = (z * pow(_MIX2, -1, 1 << 64)) & _M64
    z = _unxorshift(z, 27)
    z = (z * pow(_MIX1, -1, 1 << 64)) & _M64
    z = _unxorshift(z, 30)
    return (z - _GOLDEN) & _M64


def _new_stats() -> dict:
    return {"rounds": 0, "live_decisions": 0, "combat_decisions": 0, "searched": 0, "changed": 0, "candidates": 0,
            "clones": 0, "restores": 0, "forwards": 0, "forward_calls": 0, "forward_rows_requested": 0,
            "env_steps": 0, "env_calls": 0, "rollout_errors": 0, "no_valid_candidate": 0,
            "outcomes": {k: 0 for k in OUTCOMES[1:]}, "changed_to": {}, "pool_size": 0, "pool_max": 0,
            "rebuilds": 0, "sync_checks": 0, "desyncs": 0,
            "t": {k: 0.0 for k in ("policy", "sync", "greedy", "search", "clone", "restore", "env", "forward",
                                   "dedup", "to_torch", "model", "classify", "shadow", "rebuild", "init")}}


TRIM_TABLES = (("hand", "card_id", "hand_slot", ("hand", "hand_f", "hand_dmg")),
               ("enemies", "present", "target_slot", ("enemies", "enemy_intents", "enemy_powers")),
               ("potions", "potion_id", "potion_slot", ("potions",)))


def trim_padding(x: dict[str, torch.Tensor], spec: ModelSpec) -> dict[str, torch.Tensor]:
    """Model inputs with the hand / enemy / potion row axes cut after the last row any sample of the batch occupies
    or a candidate points at (at least one row). The model masks unoccupied rows (attention keys, pooling) and every
    pointer stays in range, so the outputs equal the untrimmed ones up to float rounding; the trunk runs on fewer
    tokens (43 -> ~30 in combat)."""
    C = spec.cols
    cand, cc = x["cand"], C["cand"]
    y = dict(x)
    for table, occ_col, ptr_col, names in TRIM_TABLES:
        occ = (x[table][..., C[table][occ_col]] != 0).any(0)
        n = int(torch.nonzero(occ).max()) + 1 if bool(occ.any()) else 0
        ptr = cand[..., cc[ptr_col]]
        if ptr.numel() and bool((ptr >= 0).any()):
            n = max(n, int(ptr.max()) + 1)
        n = max(1, min(n, x[table].shape[1]))
        for name in names:
            y[name] = x[name][:, :n]
    return y


class LookaheadPolicy:
    """The lookahead as an ``sts2vec`` policy callable (see the module docstring). ``model(x) -> (logits, value)``
    with ``spec`` bound (``features.bind_spec``); ``content`` = the seed set's content (``ctx`` does not carry it);
    ``win_bonus`` = the objective's bonus at a win (the checkpoint's ``config.win_bonus``). Call ``close()`` when done.
    """

    def __init__(self, model, spec: ModelSpec, cfg: LookaheadConfig | None = None, *, content: str = "ported_only",
                 win_bonus: float = 1.0, dll=None, log: Callable[[str], None] | None = None,
                 record: Callable[[dict], None] | None = None):
        self.model = model
        self.spec = spec
        self.cfg = cfg or LookaheadConfig()
        self.content = content
        self.win_bonus = float(win_bonus)
        self.dll = dll
        self.log = log or (lambda m: None)
        self.record = record
        self.device = torch.device(self.cfg.device)
        self.env: VecEnv | None = None
        self.stats = _new_stats()
        self._pcache: dict[tuple, float] = {}
        self._err_logged = 0
        bind_spec(spec)

    # ------------------------------------------------------------------------------------------- mirror env

    def _init(self, ctx) -> None:
        t0 = time.perf_counter()
        if not ctx.keys or len(ctx.keys) != ctx.num_envs:
            raise ValueError("LookaheadPolicy needs ctx.keys ('<CHARACTER>-<SEED>' per slot), as sts2vec.evaluate "
                             "provides")
        self.keys = list(ctx.keys)
        self.M = len(self.keys)
        self.ascension = int(ctx.ascension)
        self.N = self.M * (1 + (self.cfg.K * self.cfg.R if self.cfg.K >= 2 else 0))
        self.hist: list[list[int]] = [[] for _ in range(self.M)]
        self.env = self._build_env()
        self._branch = bool(getattr(self.env, "supports_branch", False))
        self.stats["api"] = "branch" if self._branch else "pool"
        S = self.env.schema
        self.G, self.CC, self.E, self.AX = S.cols("global_i"), S.cols("cand"), S.cols("enemies"), S.cols("aux")
        kinds = S.vocab.enums["RunActionKind"]
        self.END_TURN, self.DISCARD = kinds.index_of("EndTurn"), kinds.index_of("DiscardPotion")
        self.kind_names = [kinds.name(i) for i in range(len(kinds))]
        self.stats["t"]["init"] += time.perf_counter() - t0

    def _build_env(self) -> VecEnv:
        env = VecEnv(self.N, threads=self.cfg.threads, step_cap=SEARCH_STEP_CAP, floor_step_cap=0, auto_reset=False,
                     seed_text="EVAL", content=self.content, pool=None, dll=self.dll)
        try:
            for key in self.keys:
                ch, sd = key.split("-", 1)
                env.pool.add_fresh(ch, seed=sd, ascension=self.ascension, content=self.content)
            mask = np.zeros(self.N, np.uint8)
            mask[:self.M] = 1
            start = np.full(self.N, -1, np.int32)
            start[:self.M] = np.arange(self.M)
            env.reset(mask=mask, start_ids=start)
        except BaseException:
            env.close()
            raise
        return env

    def _rebuild(self) -> None:
        """Close the mirror env (frees every clone) and replay each game's history on a new one; the shadows' state
        hashes and observations must equal the old env's."""
        t0 = time.perf_counter()
        old = self.env
        M = self.M
        h_old = old.state_hash(np.arange(M))
        o_old = {k: v[:M].copy() for k, v in old.obs.items()}
        old.close()
        self.env = env = self._build_env()
        lens = [len(h) for h in self.hist]
        a = np.full(self.N, -1, np.int32)
        for t in range(max(lens, default=0)):
            a[:M] = [h[t] if t < n else -1 for h, n in zip(self.hist, lens)]
            env.step(a)
        h_new = env.state_hash(np.arange(M))
        bad = np.flatnonzero(h_new != h_old).tolist()
        bad_obs = [k for k, v in env.obs.items() if not np.array_equal(v[:M], o_old[k])]
        if bad or bad_obs:
            raise Desync(f"rebuild did not reproduce the shadows: state hash differs for games {bad[:8]}, "
                         f"observation buffers {bad_obs[:8]}")
        self.stats["rebuilds"] += 1
        self.stats["t"]["rebuild"] += time.perf_counter() - t0

    def _check_sync(self, obs: dict, live: np.ndarray) -> None:
        sh = self.env.obs
        M = self.M
        every = bool(live.all())
        bad = []
        for k, v in obs.items():
            mine = sh[k][:M]
            if every:
                if not np.array_equal(v, mine):
                    bad.append(k)
            elif not np.array_equal(v[live], mine[live]):
                bad.append(k)
        self.stats["sync_checks"] += int(live.sum())
        if bad:
            self.stats["desyncs"] += 1
            rows = np.flatnonzero(live & np.any([(obs[k] != sh[k][:M]).reshape(M, -1).any(1) for k in bad], axis=0))
            raise Desync(f"shadow differs from the evaluator in buffers {bad[:8]} for games "
                         f"{[self.keys[i] for i in rows[:8]]}")

    def close(self) -> None:
        if self.env is not None:
            self.stats["pool_size"] = len(self.env.pool)
            self.env.close()

    def __del__(self):
        try:
            if self.env is not None:
                self.env.close()
        except Exception:
            pass

    # ------------------------------------------------------------------------------------------- policy

    def __call__(self, obs: dict, ctx) -> np.ndarray:
        t_call = time.perf_counter()
        if self.env is None:
            self._init(ctx)
        st, T = self.stats, self.stats["t"]
        live = np.asarray(ctx.live, dtype=bool)
        t0 = time.perf_counter()
        self._check_sync(obs, live)
        T["sync"] += time.perf_counter() - t0
        t0 = time.perf_counter()
        with torch.no_grad():                 # the greedy choice exactly as sts2train.evaluate.greedy_policy
            logits, _ = self.model(obs_to_torch(obs, self.device))
        greedy = logits.argmax(-1).cpu().numpy().astype(np.int32)
        T["greedy"] += time.perf_counter() - t0
        a = greedy.copy()
        st["rounds"] += 1
        st["live_decisions"] += int(live.sum())
        incombat = live & (obs["global_i"][:, self.G.is_combat] == 1)
        st["combat_decisions"] += int(incombat.sum())
        games = np.flatnonzero(incombat & (obs["cand_n"] >= 2))
        if self.cfg.K >= 2 and len(games):
            t0 = time.perf_counter()
            a = self._search(obs, games, greedy, logits.float().cpu().numpy())
            T["search"] += time.perf_counter() - t0
        t0 = time.perf_counter()
        sa = np.full(self.N, -1, np.int32)
        sa[:self.M] = np.where(live, a, -1)
        self.env.step(sa)                     # the shadows take the evaluator's next step
        for g in np.flatnonzero(live):
            self.hist[g].append(int(a[g]))
        T["shadow"] += time.perf_counter() - t0
        n_pool = len(self.env.pool)
        st["pool_size"] = n_pool
        st["pool_max"] = max(st["pool_max"], n_pool)
        if n_pool > self.cfg.pool_budget:
            self._rebuild()
        T["policy"] += time.perf_counter() - t_call
        return a

    def _candidates(self, obs: dict, g: int, greedy: int, logits: np.ndarray) -> list[int]:
        n = int(obs["cand_n"][g])
        kinds = obs["cand"][g, :n, self.CC.kind]
        order = [int(greedy)]
        ends = np.flatnonzero(kinds == self.END_TURN)
        if len(ends) and int(ends[0]) != order[0]:
            order.append(int(ends[0]))
        rest = [j for j in range(n) if kinds[j] != self.DISCARD and j not in order]
        rest.sort(key=lambda j: (-float(logits[g, j]), j))
        return (order + rest)[:self.cfg.K]

    def _progress(self, aux_rows: np.ndarray) -> np.ndarray:
        AX = self.AX
        out = np.empty(len(aux_rows))
        for i, r in enumerate(aux_rows):
            key = (int(r[AX.act_index]), int(r[AX.total_floor]), int(r[AX.act_floor]), int(r[AX.act_rooms]))
            p = self._pcache.get(key)
            if p is None:
                p = self._pcache[key] = state_progress(*key, ascension=self.ascension)
            out[i] = p
        return out

    def _leaf_terms(self, o: dict, slots: np.ndarray) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
        """(progress, hp / max_hp, sum enemy hp / sum enemy max hp) of env rows `slots`."""
        G, E = self.G, self.E
        gi = o["global_i"][slots]
        hpf = gi[:, G.hp] / np.maximum(gi[:, G.max_hp], 1)
        en = o["enemies"][slots]
        pres = en[:, :, E.present] > 0
        ehp = (np.maximum(en[:, :, E.hp], 0) * pres).sum(1)
        emax = (en[:, :, E.max_hp] * pres).sum(1)
        ef = np.where(emax > 0, ehp / np.maximum(emax, 1), 0.0)
        return self._progress(o["aux"][slots]), hpf.astype(np.float64), ef.astype(np.float64)

    def _forward(self, o: dict, rows: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
        """Greedy action and value of env rows `rows` (byte-identical model inputs are evaluated once)."""
        st, T = self.stats, self.stats["t"]
        st["forward_rows_requested"] += len(rows)
        t0 = time.perf_counter()
        if len(rows) > 1:
            mat = np.concatenate([np.ascontiguousarray(o[b][rows]).reshape(len(rows), -1).view(np.uint8)
                                  for b in USED_BUFFERS], axis=1)
            v = np.ascontiguousarray(mat).view(np.dtype((np.void, mat.shape[1]))).ravel()
            _, first, inv = np.unique(v, return_index=True, return_inverse=True)
            perm = np.argsort(first, kind="stable")          # unique rows in order of first appearance
            rank = np.empty_like(perm)
            rank[perm] = np.arange(len(perm))
            uniq = rows[first[perm]]
            back = rank[inv.ravel()]
        else:
            uniq, back = rows, np.arange(len(rows))
        acts = np.empty(len(uniq), np.int32)
        vals = np.empty(len(uniq), np.float64)
        T["dedup"] += time.perf_counter() - t0
        with torch.inference_mode():
            for c in range(0, len(uniq), FWD_CHUNK):
                part = uniq[c:c + FWD_CHUNK]
                t0 = time.perf_counter()
                x = trim_padding(obs_to_torch(o, self.device, index=part), self.spec)
                t1 = time.perf_counter()
                logits, value = self.model(x)
                acts[c:c + len(part)] = logits.argmax(-1).cpu().numpy()
                vals[c:c + len(part)] = value.float().cpu().numpy()
                T["to_torch"] += t1 - t0
                T["model"] += time.perf_counter() - t1
                st["forward_calls"] += 1
        st["forwards"] += len(uniq)
        return acts[back], vals[back]

    def _search(self, obs: dict, games: np.ndarray, greedy: np.ndarray, logits: np.ndarray) -> np.ndarray:
        cfg, env, st, T = self.cfg, self.env, self.stats, self.stats["t"]
        M, R, N = self.M, cfg.R, self.N
        a = greedy.copy()
        plans = [(int(g), self._candidates(obs, int(g), int(greedy[g]), logits)) for g in games]
        plans = [(g, c) for g, c in plans if len(c) >= 2]
        if not plans:
            return a
        # R determinized roots per game, every rollout slot of the round started from its root
        slot_game, first, seeds, root_pos = [], [], [], []
        for g, cands in plans:
            n_dec = len(self.hist[g])
            for r in range(R):
                root_pos.append(len(first))                 # the root's own slot = its first candidate's
                seeds.append((g, det_seed(cfg.seed, self.keys[g], n_dec, r), len(cands)))
                slot_game += [g] * len(cands)
                first += cands
        U = len(first)
        used = np.arange(M, M + U)
        slot_game = np.asarray(slot_game)
        first = np.asarray(first, np.int32)
        if self._branch:
            t0 = time.perf_counter()
            for (g, sd, _), pos in zip(seeds, root_pos):
                env.branch(g, [M + pos], determinized=True, seed=inv_branch_seed(sd))
            T["clone"] += time.perf_counter() - t0
            t0 = time.perf_counter()
            src = np.concatenate([np.full(n - 1, M + pos) for (_, _, n), pos in zip(seeds, root_pos)])
            dst = np.concatenate([np.arange(M + pos + 1, M + pos + n) for (_, _, n), pos in zip(seeds, root_pos)])
            if not (src >= M).all():                        # an exact copy of a shadow would read the true hidden state
                raise RuntimeError("lookahead: exact branch from a shadow slot")
            o = env.branch(src, dst, determinized=False)
            T["restore"] += time.perf_counter() - t0
        else:
            t0 = time.perf_counter()
            roots = np.concatenate([np.full(n, env.clone_determinized(g, sd)) for g, sd, n in seeds])
            T["clone"] += time.perf_counter() - t0
            mask = np.zeros(N, np.uint8)
            mask[used] = 1
            start = np.full(N, -1, np.int32)
            start[used] = roots
            t0 = time.perf_counter()
            o = env.reset(mask=mask, start_ids=start)
            T["restore"] += time.perf_counter() - t0
        st["searched"] += len(plans)
        st["candidates"] += sum(len(c) for _, c in plans)
        st["clones"] += len(plans) * R
        st["restores"] += U
        if not (np.array_equal(o["cand_n"][used], obs["cand_n"][slot_game])
                and np.array_equal(o["cand"][used], obs["cand"][slot_game])):
            raise Desync("a restored clone's candidate rows differ from its game's (clone_determinized contract)")
        root_turn = obs["global_i"][slot_game, self.G.turn].astype(np.int64)
        p_root = self._progress(obs["aux"][slot_game])
        outcome, pleaf, vleaf, hpf, ef = self._rollouts(used, first, root_turn)
        # Q per rollout, mean over the valid determinizations, argmax with ties -> greedy (position 0)
        use_v = cfg.score in ("value", "value+hp")
        use_hp = cfg.score in ("value+hp", "hp")
        q = np.full(U, np.nan)
        q[outcome == DEFEAT] = -1.0 if cfg.score == "hp" else 0.0
        vic = outcome == VICTORY
        q[vic] = 1.0 - p_root[vic] + self.win_bonus
        leaf = np.isin(outcome, (WON, TURN, HORIZON))
        q[leaf] = (pleaf[leaf] - p_root[leaf] + (vleaf[leaf] if use_v else 0.0)
                   + ((HP_W * hpf[leaf] - ENEMY_W * ef[leaf]) if use_hp else 0.0))
        k = 0
        for g, cands in plans:
            n = len(cands)
            block = q[k:k + n * R].reshape(R, n)            # slots of game g: r-major, then candidate position
            k += n * R
            valid = ~np.isnan(block)
            cnt = valid.sum(0)
            if not cnt.any():
                st["no_valid_candidate"] += 1
                continue
            mean = np.where(cnt > 0, np.where(valid, block, 0.0).sum(0) / np.maximum(cnt, 1), -np.inf)
            p = int(np.flatnonzero(mean >= mean.max() - TIE_EPS)[0])
            a[g] = cands[p]
            if self.record is not None:
                self.record({"key": self.keys[g], "n": len(self.hist[g]), "cands": list(cands),
                             "q": [round(float(x), 6) if c else None for x, c in zip(mean, cnt)],
                             "valid": cnt.tolist(), "choice": int(cands[p]), "greedy": int(cands[0])})
            if p != 0:
                st["changed"] += 1
                kn = self.kind_names[int(obs["cand"][g, cands[p], self.CC.kind])]
                st["changed_to"][kn] = st["changed_to"].get(kn, 0) + 1
        for i, name in enumerate(OUTCOMES[1:], start=1):
            st["outcomes"][name] += int((outcome == i).sum())
        return a

    def _rollouts(self, used: np.ndarray, first: np.ndarray, root_turn: np.ndarray):
        """Run the restored slots `used` (step 0 = `first`, then the model's greedy actions) until each latches.
        Returns per slot (outcome, P(leaf), V(leaf), hp / max_hp, enemy hp fraction)."""
        cfg, env, st, T = self.cfg, self.env, self.stats, self.stats["t"]
        G = self.G
        U = len(used)
        outcome = np.zeros(U, np.int8)
        pleaf = np.zeros(U)
        vleaf = np.zeros(U)
        hpf = np.zeros(U)
        ef = np.zeros(U)
        running = np.ones(U, bool)
        act = np.full(self.N, -1, np.int32)
        act[used] = first
        need_v = cfg.score in ("value", "value+hp")
        turn_h = cfg.horizon == "turn"
        for h in range(cfg.H):
            t0 = time.perf_counter()
            o, rew, term, trunc, status, _ = env.step(act)
            T["env"] += time.perf_counter() - t0
            t0 = time.perf_counter()
            idx = np.flatnonzero(running)
            s = used[idx]
            st["env_calls"] += 1
            st["env_steps"] += len(idx)
            bad = ((status[s] & BAD_BITS) != 0) | ((trunc[s] != 0) & (term[s] == 0))
            done = (term[s] != 0) & ~bad
            gi = o["global_i"][s]
            over = ~bad & ~done & (gi[:, G.is_combat] == 0)
            rest = ~bad & ~done & ~over
            leafy = rest & ((o["cand_n"][s] <= 0) | (turn_h & (gi[:, G.turn] > root_turn[idx])))
            hz = rest & ~leafy & (h == cfg.H - 1)
            win = rew[s] > 0
            outcome[idx[bad]] = ERROR
            outcome[idx[done & win]] = VICTORY
            outcome[idx[done & ~win]] = DEFEAT
            outcome[idx[over]] = WON
            outcome[idx[leafy]] = TURN
            outcome[idx[hz]] = HORIZON
            if bad.any():
                st["rollout_errors"] += int(bad.sum())
                for slot in s[bad][:max(0, 5 - self._err_logged)]:
                    self._err_logged += 1
                    txt = (env.last_error(int(slot)) or f"status {int(status[slot])}").strip().splitlines()
                    self.log(f"lookahead: rollout error in slot {int(slot)}: {txt[0] if txt else '?'}")
            newleaf = idx[over | leafy | hz]
            if len(newleaf):
                pleaf[newleaf], hpf[newleaf], ef[newleaf] = self._leaf_terms(o, used[newleaf])
            running[idx[bad | done | over | leafy | hz]] = False
            still = np.flatnonzero(running)
            T["classify"] += time.perf_counter() - t0
            vrows = newleaf if need_v else newleaf[:0]
            act[:] = -1
            if len(still) or len(vrows):
                t0 = time.perf_counter()
                acts, vals = self._forward(o, used[np.concatenate([still, vrows])])
                T["forward"] += time.perf_counter() - t0
                act[used[still]] = acts[:len(still)]
                vleaf[vrows] = vals[len(still):]
            if not len(still):
                break
        return outcome, pleaf, vleaf, hpf, ef

    # ------------------------------------------------------------------------------------------- reporting

    def summary(self) -> dict:
        st = json.loads(json.dumps(self.stats))
        n = max(st["searched"], 1)
        T = st["t"]
        st["derived"] = {
            "ms_per_searched_decision": round(1000 * T["search"] / n, 3),
            "ms_per_live_decision": round(1000 * T["policy"] / max(st["live_decisions"], 1), 3),
            "candidates_per_search": round(st["candidates"] / n, 3),
            "rollouts_per_search": round(st["restores"] / n, 3),
            "env_steps_per_search": round(st["env_steps"] / n, 3),
            "forwards_per_search": round(st["forwards"] / n, 3),
            "forward_dedup_saved": round(1 - st["forwards"] / max(st["forward_rows_requested"], 1), 4),
            "changed_rate": round(st["changed"] / n, 4),
            "searched_share_of_live": round(st["searched"] / max(st["live_decisions"], 1), 4),
        }
        st["t"] = {k: round(v, 3) for k, v in T.items()}
        return st


# ------------------------------------------------------------------------------------------------ entry points


def _load(checkpoint, cfg: LookaheadConfig, dll=None, log=print):
    """The checkpoint's model for the loaded library (``ppo.load_checkpoint(library=...)``: refused with SpecMismatch
    unless the model inputs are identical or only widened, as ``sts2train eval``)."""
    from .ppo import load_checkpoint

    model, spec, ck = load_checkpoint(checkpoint, cfg.device, library=ModelSpec.from_schema(schema_of(load(dll))))
    compat = ck.get("spec_compat") or check_spec_compat(spec, ModelSpec.from_schema(schema_of(load(dll))))
    if compat["other_differences"] or compat.get("widened_enums"):
        log(f"note: checkpoint encoder_version {compat['checkpoint_encoder_version']}, library "
            f"{compat['library_encoder_version']}: model inputs {compat.get('model_inputs', 'identical')}"
            + (f" ({describe_widened(compat['widened_enums'])}, new rows zero)" if compat.get("widened_enums") else ""))
    bind_spec(spec)
    return model, spec, ck, compat


def _repo_sha() -> str | None:
    from sts2vec.evaluate import repo_sha

    return repo_sha()


def _win_bonus(ck: dict) -> float:
    return float((ck.get("config") or {}).get("win_bonus", 1.0))


def evaluate_lookahead(seedset, checkpoint, out, cfg: LookaheadConfig | None = None, *, dll=None,
                       step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP, record: bool = False,
                       log: Callable[[str], None] = print) -> dict:
    """Play `seedset` with the checkpoint's lookahead policy and write the protocol run directory `out` (plus
    `stats.json`, and `searches.jsonl` with `record`). Torch runs on `cfg.threads` threads; the evaluator's env uses
    the same count."""
    cfg = cfg or LookaheadConfig()
    torch.set_num_threads(cfg.threads)
    ss = PG.validate_seedset(dict(seedset)) if isinstance(seedset, dict) else PG.load_seedset(str(seedset))
    model, spec, ck, compat = _load(checkpoint, cfg, dll, log)
    wh = ck.get("weights_hash") or "?"
    Path(out).mkdir(parents=True, exist_ok=True)
    rec_f = open(Path(out) / "searches.jsonl", "w", encoding="utf-8") if record else None
    hook = (lambda d: rec_f.write(json.dumps(d, separators=(",", ":")) + "\n")) if rec_f else None
    pol = LookaheadPolicy(model, spec, cfg, content=ss["content"], win_bonus=_win_bonus(ck), dll=dll, log=log,
                          record=hook)
    name = f"nn:{Path(checkpoint).name}+{cfg.label}"
    search = dict(cfg.search(), threads=cfg.threads, pool_budget=cfg.pool_budget, device=cfg.device,
                  win_bonus=pol.win_bonus, tie_eps=TIE_EPS, hp_w=HP_W, enemy_w=ENEMY_W, sha8=cfg.sha8)
    t0 = time.monotonic()
    try:
        rep = vec_evaluate(
            ss, pol, out, threads=cfg.threads, step_cap=step_cap, floor_step_cap=floor_step_cap, policy_name=name,
            policy_hash=f"weights:{wh}+la:{cfg.sha8}", log=log, dll=dll,
            summary_extra={"weights_hash": wh, "lookahead": cfg.search()},
            config_extra={"model": "sts2train entity-encoder PPO", "checkpoint": str(checkpoint), "weights_hash": wh,
                          "params": sum(p.numel() for p in model.parameters()), "arch": getattr(model, "arch", None),
                          "device": cfg.device, "greedy": False, "checkpoint_encoder_version": spec.encoder_version,
                          "resume_compat": compat, "search": search, "information": "determinized, combat-only",
                          "mirror_env": True})
    finally:
        pol.close()
        if rec_f:
            rec_f.close()
    stats = pol.summary()
    h = rep["headline"]
    stats.update(config=asdict(cfg), search=search, policy_name=name, weights_hash=wh, checkpoint=str(checkpoint),
                 code={"lookahead_py_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest()[:16],
                       "repo_sha": rep.get("repo_sha") or _repo_sha()},
                 seedset=ss.get("id") or ss.get("name"), games=h["games"], replayable=h["replayable"],
                 progress_mean=h["progress_mean"], wall_seconds=round(time.monotonic() - t0, 2),
                 torch_threads=torch.get_num_threads(), torch=torch.__version__, host=platform.node(),
                 machine=platform.machine(), python=platform.python_version())
    with open(Path(out) / "stats.json", "w", encoding="utf-8") as f:
        json.dump(stats, f, indent=1)
    d = stats["derived"]
    log(f"lookahead {cfg.label} H={cfg.H} score={cfg.score}: searched {stats['searched']:,} decisions, changed "
        f"{stats['changed']:,} ({d['changed_rate']:.1%}), {d['ms_per_searched_decision']:.1f} ms/decision, forwards "
        f"{stats['forwards']:,}, env-steps {stats['env_steps']:,}, rollout errors {stats['rollout_errors']}, "
        f"rebuilds {stats['rebuilds']}, desyncs {stats['desyncs']}, wall {stats['wall_seconds']:.0f} s")
    return rep


def evaluate_greedy(seedset, checkpoint, out, *, threads: int = 4, device: str = "cpu", dll=None,
                    log: Callable[[str], None] = print) -> dict:
    """The plain greedy baseline (``sts2train.evaluate.evaluate``, the ``sts2train eval`` code path) under the same
    torch thread count as a lookahead run, so the paired comparison differs only by the search."""
    from .evaluate import evaluate

    torch.set_num_threads(threads)
    t0 = time.monotonic()
    rep = evaluate(str(seedset) if not isinstance(seedset, dict) else seedset, str(checkpoint), str(out),
                   threads=threads, device=device, log=log)
    stats = {"greedy": True, "checkpoint": str(checkpoint), "threads": threads,
             "torch_threads": torch.get_num_threads(), "wall_seconds": round(time.monotonic() - t0, 2),
             "games": rep["headline"]["games"], "replayable": rep["headline"]["replayable"],
             "progress_mean": rep["headline"]["progress_mean"], "torch": torch.__version__, "host": platform.node(),
             "machine": platform.machine()}
    with open(Path(out) / "stats.json", "w", encoding="utf-8") as f:
        json.dump(stats, f, indent=1)
    return rep


class EnvLookahead(LookaheadPolicy):
    """``sts2vec eval --policy sts2train.lookahead:EnvLookahead`` (constructed without arguments): the checkpoint from
    ``$STS2LA_CHECKPOINT``, the config from ``$STS2LA_CONFIG`` (JSON object of ``LookaheadConfig`` fields), content
    ``$STS2LA_CONTENT`` (default ported_only). The reported row is ``python -m sts2train.lookahead eval``."""

    def __init__(self):
        cfg = LookaheadConfig(**json.loads(os.environ.get("STS2LA_CONFIG") or "{}"))
        torch.set_num_threads(cfg.threads)
        model, spec, ck, _ = _load(os.environ["STS2LA_CHECKPOINT"], cfg)
        super().__init__(model, spec, cfg, content=os.environ.get("STS2LA_CONTENT", "ported_only"),
                         win_bonus=_win_bonus(ck))


# ------------------------------------------------------------------------------------------------ paired analysis


def _betacf(a: float, b: float, x: float) -> float:
    """Continued fraction of the regularized incomplete beta function (modified Lentz)."""
    tiny, qab, qap, qam = 1e-300, a + b, a + 1.0, a - 1.0
    c, d = 1.0, 1.0 - qab * x / qap
    d = 1.0 / (d if abs(d) > tiny else tiny)
    h = d
    for m in range(1, 300):
        m2 = 2 * m
        aa = m * (b - m) * x / ((qam + m2) * (a + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        h *= d * c
        aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2))
        d = 1.0 + aa * d
        d = 1.0 / (d if abs(d) > tiny else tiny)
        c = 1.0 + aa / c
        c = c if abs(c) > tiny else tiny
        delta = d * c
        h *= delta
        if abs(delta - 1.0) < 1e-14:
            break
    return h


def _betainc(a: float, b: float, x: float) -> float:
    if x <= 0.0:
        return 0.0
    if x >= 1.0:
        return 1.0
    lb = math.lgamma(a + b) - math.lgamma(a) - math.lgamma(b) + a * math.log(x) + b * math.log1p(-x)
    if x < (a + 1.0) / (a + b + 2.0):
        return math.exp(lb) * _betacf(a, b, x) / a
    return 1.0 - math.exp(lb) * _betacf(b, a, 1.0 - x) / b


def t_cdf(t: float, df: float) -> float:
    tail = 0.5 * _betainc(df / 2.0, 0.5, df / (df + t * t))
    return 1.0 - tail if t >= 0 else tail


def t_ppf(p: float, df: float) -> float:
    """Student t quantile (bisection on the cdf)."""
    lo, hi = -1e3, 1e3
    for _ in range(200):
        mid = 0.5 * (lo + hi)
        if t_cdf(mid, df) < p:
            lo = mid
        else:
            hi = mid
    return 0.5 * (lo + hi)


def _summaries(run: Path) -> dict[str, dict]:
    out = {}
    for p in sorted((run / "games").glob("*.summary.json")):
        s = json.loads(p.read_text(encoding="utf-8"))
        out[s["key"]] = s
    if not out:
        raise FileNotFoundError(f"{run}: no games/*.summary.json")
    return out


def paired(dir_a, dir_b, *, boot: int = 10_000, seed: int = 0) -> dict:
    """Per-seed protocol progress difference B - A of two run directories over the same games: mean, 95% t interval,
    95% percentile-bootstrap interval of the mean, better / worse / same counts, wins of each side."""
    A, B = Path(dir_a), Path(dir_b)
    sa, sb = _summaries(A), _summaries(B)
    if set(sa) != set(sb):
        raise ValueError(f"the run directories hold different games ({len(set(sa) ^ set(sb))} keys differ)")
    ca = json.loads((A / "config.json").read_text(encoding="utf-8"))
    cb = json.loads((B / "config.json").read_text(encoding="utf-8"))
    sha_a, sha_b = PG.checksum(ca["seedset"]), PG.checksum(cb["seedset"])
    if sha_a != sha_b:
        raise ValueError(f"different seed sets ({sha_a[:12]} vs {sha_b[:12]})")
    keys = sorted(sa)
    d = np.array([float(sb[k]["progress"]) - float(sa[k]["progress"]) for k in keys])
    n = len(d)
    mean = float(d.mean())
    sd = float(d.std(ddof=1)) if n > 1 else 0.0
    half = t_ppf(0.975, n - 1) * sd / math.sqrt(n) if n > 1 else 0.0
    bm = d[np.random.default_rng(seed).integers(0, n, size=(boot, n))].mean(1)
    lo, hi = np.percentile(bm, [2.5, 97.5])

    def side(s: dict, cfg: dict, run: Path) -> dict:
        res = [x["result"] for x in s.values()]
        return {"dir": str(run), "policy": cfg.get("policy"), "policy_hash": cfg.get("prompt_hash"),
                "progress_mean": round(float(np.mean([float(x["progress"]) for x in s.values()])), 6),
                "wins": res.count("win"), "results": {r: res.count(r) for r in sorted(set(res), key=str)},
                "replayable": sum(bool(x.get("replayable")) for x in s.values()),
                "floor_mean": round(float(np.mean([x["floor"] or 0 for x in s.values()])), 3)}

    return {"games": n, "seedset": ca["seedset"].get("id") or ca["seedset"].get("name"), "seedset_sha256": sha_a,
            "a": side(sa, ca, A), "b": side(sb, cb, B), "delta_mean": round(mean, 6), "delta_sd": round(sd, 6),
            "t_ci95": [round(mean - half, 6), round(mean + half, 6)],
            "bootstrap_ci95": [round(float(lo), 6), round(float(hi), 6)], "bootstrap_resamples": boot,
            "better": int((d > 1e-9).sum()), "worse": int((d < -1e-9).sum()), "same": int((np.abs(d) <= 1e-9).sum()),
            "per_game": {k: round(float(x), 6) for k, x in zip(keys, d)}}


# ------------------------------------------------------------------------------------------------ CLI


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python -m sts2train.lookahead", description=__doc__.split("\n\n")[0])
    sub = ap.add_subparsers(dest="cmd", required=True)
    ev = sub.add_parser("eval", help="play a seed set with the lookahead (or --greedy) policy of a checkpoint")
    ev.add_argument("--seedset", required=True)
    ev.add_argument("--checkpoint", required=True)
    ev.add_argument("--out", required=True)
    ev.add_argument("--greedy", action="store_true", help="the plain greedy baseline (same torch threads)")
    D = LookaheadConfig()
    ev.add_argument("--K", type=int, default=D.K)
    ev.add_argument("--R", type=int, default=D.R)
    ev.add_argument("--H", type=int, default=None, help="rollout steps (default 12 for turn, 40 for combat)")
    ev.add_argument("--horizon", choices=HORIZONS, default=D.horizon)
    ev.add_argument("--score", choices=SCORES, default=D.score)
    ev.add_argument("--threads", type=int, default=D.threads)
    ev.add_argument("--seed", type=int, default=D.seed)
    ev.add_argument("--pool-budget", dest="pool_budget", type=int, default=D.pool_budget)
    ev.add_argument("--device", default=D.device)
    ev.add_argument("--record", action="store_true", help="write searches.jsonl (per-search Q values; ExIt targets)")
    pa = sub.add_parser("paired", help="per-seed progress difference B - A of two run directories")
    pa.add_argument("a")
    pa.add_argument("b")
    pa.add_argument("--out", default=None, help="also write the JSON here")
    a = ap.parse_args(argv)
    if a.cmd == "paired":
        r = paired(a.a, a.b)
        if a.out:
            Path(a.out).write_text(json.dumps(r, indent=1) + "\n", encoding="utf-8")
        print(json.dumps({k: v for k, v in r.items() if k != "per_game"}, indent=1))
        return 0
    log = lambda m: print(m, flush=True)  # noqa: E731
    if a.greedy:
        rep = evaluate_greedy(a.seedset, a.checkpoint, a.out, threads=a.threads, device=a.device, log=log)
    else:
        cfg = LookaheadConfig(K=a.K, R=a.R, H=a.H or (12 if a.horizon == "turn" else MAX_H), horizon=a.horizon,
                              score=a.score, threads=a.threads, seed=a.seed, pool_budget=a.pool_budget,
                              device=a.device)
        rep = evaluate_lookahead(a.seedset, a.checkpoint, a.out, cfg, record=a.record, log=log)
    h = rep["headline"]
    print(json.dumps({"out": a.out, "progress_mean": h["progress_mean"], "progress_ci95": h["progress_ci95"],
                      "wins": h["wins"], "games": h["games"], "results": {k: v for k, v in h["results"].items() if v},
                      "floor_mean": h["floor_mean"], "replayable": h["replayable"]}, indent=1))
    return 0 if h["replayable"] == h["games"] else 1


if __name__ == "__main__":
    sys.exit(main())
