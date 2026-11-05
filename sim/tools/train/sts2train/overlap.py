"""Rollout overlap (``sts2train train --overlap double``): the env's CPU stepping of one half of the slots runs while the
policy forward of the other half runs, so the two phases of the rollout no longer add up.

Layout (no C# change). The ``N`` env slots are split into two lanes, ``[0, N//2)`` and ``[N//2, N)``. Each lane is its
own native ``VecEnv`` (``VecPair.lanes``; a ``VecEnv`` steps all of its slots in one call and the native library
serialises calls per env, so one env per lane is the way to step half the slots). Both lanes write into ONE set of
``N``-slot buffers (``VecPair.buffers``; each lane's pointer table points at its rows), so the observation / signal
arrays the rest of the trainer reads are exactly the ``N``-slot arrays of one env. ``VecPair`` is a drop-in ``N``-slot
env for everything outside the rollout (reset, info, pool, restore_many, prefetch, checkpoints): pool calls go to both
lanes' pools in the same order, so entry ids agree; ``info()["episode_id"]`` is mapped to the id one ``N``-slot env
would give (``(k - 1) * N + slot + 1`` for a slot's k-th episode).

Schedule per rollout step t (``OverlapRollout.rollout``; ``pre`` = obs -> device, forward, sampling, store writes;
``post`` = rewards, episode / cycle / shaping bookkeeping)::

    worker thread:   step A(t)               | step B(t)              | step A(t+1)            | ...
    main thread:     post B(t-1), pre B(t)   | post A(t), pre A(t+1)  | post B(t), pre B(t+1)  | ...

The native step releases the GIL, so the main thread's forward (GPU on CUDA, torch threads on CPU) and the worker's
env stepping run at the same time. Steps of the two lanes never run at the same time (step B(t) starts after step
A(t) returned), so every native env is driven from one thread at a time, as before.

What stays the same as ``--overlap off``. The store holds the same [T, N] batch (lane rows written in place); GAE, the
update, the bootstrap forward (all N slots, one call) and every metric are computed exactly as before. Per lane, the
per-slot bookkeeping objects (``EpisodeRewards``, ``AcqShaping``, ``LoopBreaker``, ``CycleDetector``) are own instances
of lane size (they are per-slot independent); ``AuxTargets`` writes through column views into the trainer's full
object. Episode dicts, cycle events and error texts carry global slot numbers; the finished episodes of an update come
in the same order as with one env (step t: lane A's slots, then lane B's = ascending slot order), and the seed stream
(``LaneStreamEnv``) hands seeds out in that same order, so slot i's start sequence is the one ``--overlap off`` gives
it. With a deterministic policy the rollouts are identical to ``--overlap off`` (tests/test_overlap.py); with sampling
the actions differ only because the sampler draws per lane (two ``multinomial`` calls per step instead of one): a
different, equally distributed consumption of the same RNG.

Pool mode (``--start-mode pool``, fresh pool only): both lanes hold the same pool; lane B's slot start offsets come
from the seed text ``<seed_text>~L1`` (VecEnv slot seeds hash the local slot index), so lane B's slots start other pool
entries than slots N//2.. of one env would: the same start distribution, not the same assignment. ``--start-saves``
with ``--overlap double`` is refused (save copies are made through slot restores / clones of one env). A per-slot start
hook that clones slots into the pool (e.g. a self-anchor harvester) runs per lane: one instance per lane on
``VecPair.lanes[k]``, fed the lane's rows in ``post``.

Timing (metrics.jsonl, only with ``--overlap double``): ``t_env_s`` = the summed native step time of both lanes (worker
thread, overlapped), ``t_infer_s`` = the main thread's pre phases + the bootstrap forward, ``t_overlap_wait_s`` = the
main thread blocked on the worker, ``t_rollout_s`` = the rollout's wall time. No overlap gain means ``t_rollout_s ~
t_env_s + t_infer_s + post``; full overlap ``t_rollout_s ~ max(t_env_s, t_infer_s + post)``.
"""

from __future__ import annotations

import copy
import ctypes
import time
from collections import Counter
from concurrent.futures import ThreadPoolExecutor

import numpy as np
from sts2vec import STATUS, VecEnv
from sts2vec.schema import Buffers

from .features import obs_to_device
from .seedstream import ERR_BITS, StreamEnv

OVERLAP_MODES = ("off", "double")
LANE1_SUFFIX = "~L1"
FLOOR_STALL = STATUS.get("FLOOR_STALL", 0)


def lane_bounds(n: int) -> list[tuple[int, int]]:
    n = int(n)
    if n < 2:
        raise ValueError(f"--overlap double needs envs >= 2 (got {n})")
    h = n // 2
    return [(0, h), (h, n)]


def view_buffers(full: Buffers, lo: int, hi: int) -> Buffers:
    """A ``Buffers`` over rows [lo, hi) of `full` (views; the pointer table points at those rows)."""
    v = Buffers.__new__(Buffers)
    v.schema = full.schema
    v.num_envs = int(hi - lo)
    v.arrays = {}
    v.table = (ctypes.c_void_p * len(full.schema.buffers))()
    for b in full.schema.buffers:
        a = full.arrays[b.name][lo:hi]
        assert a.flags.c_contiguous and a.flags.writeable
        v.arrays[b.name] = a
        v.table[b.index] = a.ctypes.data
    v._full = full                      # keeps the storage alive
    return v


class PairPool:
    """``VecPair.pool``: every call on both lanes' pools in the same order (so entry ids agree)."""

    def __init__(self, pair: "VecPair"):
        self._pair = pair

    @property
    def added(self):
        return self._pair.lanes[0].pool.added

    def _both(self, name: str, *args, **kw) -> list:
        return [getattr(lane.pool, name)(*args, **kw) for lane in self._pair.lanes]

    def _same(self, name: str, *args, **kw) -> int:
        ids = self._both(name, *args, **kw)
        if len(set(ids)) != 1:
            raise RuntimeError(f"overlap lanes' pools diverged: {name} gave ids {ids}")
        return ids[0]

    def add(self, entry) -> int:
        return self._same("add", entry)

    def add_fresh(self, character: str, **kw) -> int:
        return self._same("add_fresh", character, **kw)

    def add_save(self, path) -> int:
        return self._same("add_save", path)

    def warm(self, max_entries: int | None = None) -> int:
        """Both lanes' pools create the same templates, in parallel (one thread per lane: the native warm releases the
        GIL), so the wall time stays one pool's while the CPU work doubles; -> the count of lane 0's pool."""
        lanes = self._pair.lanes
        with ThreadPoolExecutor(max_workers=len(lanes), thread_name_prefix="sts2train-overlap-warm") as ex:
            futs = [ex.submit(lane.pool.warm, max_entries) for lane in lanes]
            return [f.result() for f in futs][0]

    def release(self, ids) -> int:
        return self._both("release", ids)[0]

    def __len__(self) -> int:
        return len(self._pair.lanes[0].pool)

    count = property(__len__)


class VecPair:
    """N slots as two lane ``VecEnv``s writing into one N-slot buffer set (module docstring). Takes the ``VecEnv``
    keyword arguments; lane 1's ``seed_text`` gets the suffix ``~L1``."""

    def __init__(self, num_envs: int, *, seed_text: str = "VEC", dll=None, **kw):
        self.bounds = lane_bounds(num_envs)
        self.num_envs = int(num_envs)
        self.lanes: list[VecEnv] = []
        try:
            for k, (lo, hi) in enumerate(self.bounds):
                st = seed_text if k == 0 else f"{seed_text}{LANE1_SUFFIX}"
                self.lanes.append(VecEnv(hi - lo, seed_text=st, dll=dll, **kw))
        except BaseException:
            self.close()
            raise
        first = self.lanes[0]
        self.schema = first.schema
        self.pool_spec = first.pool_spec
        self.floor_step_cap = first.floor_step_cap
        self.macros = first.macros          # room macros (both lanes get the same VecEnv kwargs)
        self.config = dict(first.config, num_envs=self.num_envs, overlap_lanes=[lane.config for lane in self.lanes])
        self.buffers: Buffers = self.schema.allocate(self.num_envs)
        for lane, (lo, hi) in zip(self.lanes, self.bounds):
            lane.buffers = view_buffers(self.buffers, lo, hi)
        self.pool = PairPool(self)
        self._ecol = self.schema.cols("episode")
        self.lane_of = np.zeros(self.num_envs, dtype=np.int64)
        self.lane_of[self.bounds[1][0]:] = 1

    # ---------------------------------------------------------------------------------------------- plumbing

    lib = property(lambda self: self.lanes[0].lib)
    threads = property(lambda self: self.lanes[0].threads)
    abi_version = property(lambda self: self.lanes[0].abi_version)
    supports_branch = property(lambda self: self.lanes[0].supports_branch)
    supports_restore_many = property(lambda self: self.lanes[0].supports_restore_many)
    supports_pool_release = property(lambda self: self.lanes[0].supports_pool_release)
    handle = property(lambda self: self.lanes[0].handle)

    @property
    def closed(self) -> bool:
        return all(lane.closed for lane in self.lanes)

    def _split(self, slots):
        """Global slot indices -> [(lane k, local indices, positions in `slots`)]."""
        s = np.asarray(slots, dtype=np.int64).reshape(-1)
        if len(s) and (s.min() < 0 or s.max() >= self.num_envs):
            raise ValueError(f"slot out of range 0..{self.num_envs - 1}: {s.min()}..{s.max()}")
        out = []
        for k, (lo, hi) in enumerate(self.bounds):
            pos = np.flatnonzero((s >= lo) & (s < hi))
            if len(pos):
                out.append((k, s[pos] - lo, pos))
        return out

    def _lane(self, slot: int) -> tuple[VecEnv, int]:
        slot = int(slot)
        if not 0 <= slot < self.num_envs:
            raise ValueError(f"slot {slot} out of range 0..{self.num_envs - 1}")
        k = int(self.lane_of[slot])
        return self.lanes[k], slot - self.bounds[k][0]

    # ---------------------------------------------------------------------------------------------- env API

    _obs = VecEnv._obs

    @property
    def obs(self) -> dict[str, np.ndarray]:
        return self._obs(False)

    def episode_ids(self, lo: int = 0, hi: int | None = None) -> np.ndarray:
        """``episode_id`` of slots [lo, hi) as one N-slot env numbers them (``k * N + slot + 1``; 0 stays 0)."""
        hi = self.num_envs if hi is None else hi
        raw = self.buffers["episode"][:, self._ecol["episode_id"]]
        out = np.zeros(hi - lo, dtype=np.int64)
        for a, b in self.bounds:
            x0, x1 = max(lo, a), min(hi, b)
            if x0 >= x1:
                continue
            loc = raw[x0:x1].astype(np.int64)
            nk = b - a
            g = ((loc - 1) // nk) * self.num_envs + a + (loc - 1) % nk + 1
            out[x0 - lo:x1 - lo] = np.where(loc > 0, g, loc)
        return out

    def info(self, copy: bool = False) -> dict[str, np.ndarray]:
        ep = self.buffers["episode"]
        f = self._ecol
        out = {"episode_id": self.episode_ids(), "step_index": ep[:, f["step_index"]],
               "start_entry": ep[:, f["start_entry"]], "last_episode": self.buffers["last_episode"]}
        return {k: (v.copy() if copy else v) for k, v in out.items()}

    def lane_info(self, k: int) -> dict[str, np.ndarray]:
        lo, hi = self.bounds[k]
        ep = self.buffers["episode"]
        f = self._ecol
        return {"episode_id": self.episode_ids(lo, hi), "step_index": ep[lo:hi, f["step_index"]],
                "start_entry": ep[lo:hi, f["start_entry"]], "last_episode": self.buffers["last_episode"][lo:hi]}

    @property
    def status(self) -> np.ndarray:
        return self.buffers["status"]

    def reset(self, mask=None, start_ids=None, *, copy: bool = False):
        for lane, (lo, hi) in zip(self.lanes, self.bounds):
            lane.reset(None if mask is None else np.asarray(mask)[lo:hi],
                       None if start_ids is None else np.asarray(start_ids)[lo:hi])
        return self._obs(copy)

    def step_lane(self, k: int, actions) -> None:
        """Step lane k's slots (``actions`` = its rows); the results land in rows ``bounds[k]`` of the buffers."""
        self.lanes[k].step(actions)

    def step(self, actions, *, copy: bool = False):
        """Both lanes, one after the other: the ``VecEnv.step`` result over all N slots."""
        a = np.asarray(actions)
        for k, (lo, hi) in enumerate(self.bounds):
            self.step_lane(k, a[lo:hi])
        b = self.buffers
        sig = (b["reward"], b["terminated"], b["truncated"], b["status"])
        if copy:
            sig = tuple(x.copy() for x in sig)
        return (self._obs(copy), *sig, self.info(copy))

    def restore(self, slot: int, entry: int, *, copy: bool = False):
        lane, i = self._lane(slot)
        lane.restore(i, entry)
        return self._obs(copy)

    def restore_many(self, slots, entries, *, copy: bool = False):
        e = np.asarray(entries, dtype=np.int64).reshape(-1)
        if len(e) != len(np.asarray(slots).reshape(-1)):
            raise ValueError("slots and entries differ in length")
        for k, loc, pos in self._split(slots):
            self.lanes[k].restore_many(loc, e[pos])
        return self._obs(copy)

    def snapshot(self, slot: int) -> int:
        raise NotImplementedError("--overlap double: snapshots live in one lane's pool (use VecPair.lanes)")

    def clone_determinized(self, slot: int, seed: int) -> int:
        raise NotImplementedError("--overlap double: clones live in one lane's pool (use VecPair.lanes)")

    def state_hash(self, slots=None) -> np.ndarray:
        s = np.arange(self.num_envs) if slots is None else np.asarray(slots, dtype=np.int64).reshape(-1)
        out = np.zeros(len(s), dtype=np.uint64)
        for k, loc, pos in self._split(s):
            out[pos] = self.lanes[k].state_hash(loc)
        return out

    def set_threads(self, threads: int) -> None:
        for lane in self.lanes:
            lane.set_threads(threads)

    def last_error(self, slot: int = -1) -> str:
        if int(slot) < 0:
            return "\n".join(t for t in (lane.last_error(-1) for lane in self.lanes) if t)
        lane, i = self._lane(slot)
        return lane.last_error(i)

    def slot_errors(self, bits: int = ERR_BITS) -> dict[int, str]:
        idx = np.flatnonzero(self.buffers["status"] & bits)
        return {int(i): self.last_error(int(i)) for i in idx}

    def lane_slot_errors(self, k: int, bits: int = ERR_BITS) -> dict[int, str]:
        lo, hi = self.bounds[k]
        idx = np.flatnonzero(self.buffers["status"][lo:hi] & bits)
        return {int(lo + i): self.lanes[k].last_error(int(i)) for i in idx}

    status_counts = VecEnv.status_counts
    legal_mask = VecEnv.legal_mask
    candidates = VecEnv.candidates
    last_episode = VecEnv.last_episode

    def close(self) -> None:
        for lane in self.lanes:
            lane.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()

    def __repr__(self) -> str:
        return f"<sts2train.overlap.VecPair N={self.num_envs} lanes={[hi - lo for lo, hi in self.bounds]}>"


class LaneStreamEnv(StreamEnv):
    """``StreamEnv`` over a ``VecPair`` with ``step_lane``: one lane's step plus the stream restarts of its ended
    slots. One FIFO and one counter for both lanes, consumed in call order: with the trainer's order (A(t), B(t),
    A(t+1), ...) the seeds go to the slots in the order one N-slot env hands them out."""

    def __init__(self, env: VecPair, stream, **kw):
        super().__init__(env, stream, **kw)
        self.lane_ended: list[dict] = [{} for _ in env.bounds]
        self.lane_errors: list[dict] = [{} for _ in env.bounds]

    def step_lane(self, k: int, actions):
        """-> (reward, terminated, truncated, status) of lane k's rows (copies taken before the restarts) and the
        error texts {global slot: text} of its failed slots."""
        self._join()
        pair = self.env
        lo, hi = pair.bounds[k]
        pair.step_lane(k, actions)
        b = pair.buffers
        rew, term, trunc, status = (b[n][lo:hi].copy() for n in ("reward", "terminated", "truncated", "status"))
        done = (term != 0) | (trunc != 0)
        errs = pair.lane_slot_errors(k) if (status & ERR_BITS).any() else {}
        ended = {}
        if done.any():
            t = time.monotonic()
            slots = lo + np.flatnonzero(done)
            for i in slots:
                ended[int(i)] = (int(self.slot_n[i]), self.slot_text[i], int(self.slot_entry[i]))
            self._start(slots)
            dt = time.monotonic() - t
            self.t_restart += dt
            self.totals["t_restart"] += dt
        self.lane_ended[k] = ended
        self.lane_errors[k] = errs
        return rew, term, trunc, status, errs

    def step(self, actions, *, copy: bool = False):
        a = np.asarray(actions)
        sig = [self.step_lane(k, a[lo:hi]) for k, (lo, hi) in enumerate(self.env.bounds)]
        rew, term, trunc, status = (np.concatenate([s[j] for s in sig]) for j in range(4))
        self.ended = {k: v for d in self.lane_ended for k, v in d.items()}
        self.errors = {k: v for d in self.lane_errors for k, v in d.items()}
        info = self.env.info(copy)
        obs = self.env.obs
        return ({k: v.copy() for k, v in obs.items()} if copy else obs), rew, term, trunc, status, info

    def annotate(self, finished: list[dict]) -> None:
        for e in finished:
            s = int(e["slot"])
            n, text, _ = self.lane_ended[int(self.env.lane_of[s])].get(s, (-1, None, -1))
            e["seed_text"], e["seed_n"] = text, (n if n >= 0 else None)


def pool_step_lane(env: VecPair, k: int, actions):
    """Pool mode: lane k's step (auto-reset) -> the same tuple as ``LaneStreamEnv.step_lane``."""
    lo, hi = env.bounds[k]
    env.step_lane(k, actions)
    b = env.buffers
    rew, term, trunc, status = (b[n][lo:hi].copy() for n in ("reward", "terminated", "truncated", "status"))
    errs = env.lane_slot_errors(k) if (status & ERR_BITS).any() else {}
    return rew, term, trunc, status, errs


# ------------------------------------------------------------------------------------------------ lane trackers


AUX_ARRAYS = ("in_combat", "_won", "ends", "end_hp", "term", "trunc", "win")


def aux_view(full, lo: int, hi: int):
    """An ``AuxTargets`` whose [T, n] arrays are column views [:, lo:hi] of `full`'s (its writes land in `full`)."""
    v = copy.copy(full)
    for name in AUX_ARRAYS:
        setattr(v, name, getattr(full, name)[:, lo:hi])
    return v


class Lane:
    def __init__(self, k: int, lo: int, hi: int, trackers: dict, aux_full=None):
        self.k, self.lo, self.hi = k, lo, hi
        self.n = hi - lo
        self.rewards = trackers["rewards"]
        self.shaper = trackers.get("shaper")
        self.breaker = trackers.get("breaker")
        self.cycles = trackers["cycles"]
        self.aux_tg = aux_view(aux_full, lo, hi) if aux_full is not None else None
        self.cand_n = None
        self.valid = None


class LaneCycles:
    """The trainer's view of the lanes' ``CycleDetector``s: ``pop`` (events with global slots, in step / slot order,
    as one detector over N slots logs them) and ``totals_dict``."""

    def __init__(self, lanes: list[Lane]):
        self.lanes = lanes

    def _keys(self):
        return list(self.lanes[0].cycles.totals_dict())

    def pop(self) -> tuple[dict, list[dict]]:
        counts: Counter = Counter()
        events = []
        for L in self.lanes:
            c, ev = L.cycles.pop()
            counts.update(c)
            for e in ev:
                e["slot"] = int(e["slot"]) + L.lo
            events += ev
        events.sort(key=lambda e: (e.get("t", -1), e["slot"]))
        return {k: int(counts.get(k, 0)) for k in self._keys()}, events

    def totals_dict(self) -> dict:
        out: Counter = Counter()
        for L in self.lanes:
            out.update(L.cycles.totals_dict())
        return {k: int(out.get(k, 0)) for k in self._keys()}


class LaneBreakers:
    def __init__(self, lanes: list[Lane]):
        self.lanes = lanes

    def pop_counts(self) -> tuple[int, int, int]:
        tot = [L.breaker.pop_counts() for L in self.lanes]
        return tuple(sum(x[j] for x in tot) for j in range(3))

    def totals(self) -> dict:
        ts = [L.breaker.totals() for L in self.lanes]
        out = dict(ts[0])
        for k, v in ts[0].items():
            if isinstance(v, int) and k not in ("k", "window"):
                out[k] = sum(t[k] for t in ts)
        return out


class LaneShapers:
    def __init__(self, lanes: list[Lane]):
        self.lanes = lanes

    @property
    def phi(self) -> np.ndarray:
        return np.concatenate([L.shaper.phi for L in self.lanes])


# ------------------------------------------------------------------------------------------------ the rollout


class OverlapRollout:
    """The ``--overlap double`` rollout (module docstring). `env` = a ``VecPair`` or a ``LaneStreamEnv`` over one;
    `make_trackers(n)` -> {"rewards", "shaper", "breaker", "cycles"} for a lane of n slots; `aux_full` = the
    trainer's ``AuxTargets`` (or None)."""

    def __init__(self, env, make_trackers, *, aux_full=None):
        self.stream = isinstance(env, LaneStreamEnv)
        pair = env.env if self.stream else env
        if not isinstance(pair, VecPair):
            raise TypeError("OverlapRollout needs a VecPair (or a LaneStreamEnv over one)")
        self.env, self.pair = env, pair
        self.lanes = [Lane(k, lo, hi, make_trackers(hi - lo), aux_full) for k, (lo, hi) in enumerate(pair.bounds)]
        self.cycles = LaneCycles(self.lanes)
        self.breaker = LaneBreakers(self.lanes) if self.lanes[0].breaker is not None else None
        self.shaper = LaneShapers(self.lanes) if self.lanes[0].shaper is not None else None
        self.worker = ThreadPoolExecutor(max_workers=1, thread_name_prefix="sts2train-overlap")
        self.totals = {"t_overlap_wait": 0.0, "t_rollout": 0.0}

    def _views(self, L: Lane) -> dict[str, np.ndarray]:
        return {k: v[L.lo:L.hi] for k, v in self.pair.obs.items()}

    def reset(self) -> None:
        """After ``env.reset()``: every lane's trackers see the first observations (as ``train`` does for one env)."""
        for L in self.lanes:
            o, info = self._views(L), self.pair.lane_info(L.k)
            L.rewards.reset(o["aux"], start_entry=info["start_entry"])
            if L.shaper is not None:
                L.shaper.reset(o["aux"], o["deck"])
            L.cycles.observe(o, episode_ids=info["episode_id"])

    def _step_fn(self, k: int, a: np.ndarray):
        t = time.monotonic()
        res = self.env.step_lane(k, a) if self.stream else pool_step_lane(self.pair, k, a)
        return res, time.monotonic() - t

    def close(self) -> None:
        self.worker.shutdown(wait=True)

    def rollout(self, *, upd: int, store, runner, device, max_rows: int, static: bool, track, steps: int,
                cycle_cost: float, r_obj, cyc_r, hooks_on: bool, episode_hooks, episode_fields, errors_seen: dict,
                check_forward=None) -> dict:
        """One rollout window of `steps` into `store` (rows of both lanes). Returns the loop's counters as ``train``
        keeps them for one env: env_s, infer_s, wait_s, rollout_s, n_steps, se_n, cg_n, il_n, over_n, fs_n,
        ep_this."""
        T = int(steps)
        A, B = self.lanes
        acc = dict(env_s=0.0, infer_s=0.0, wait_s=0.0, n_steps=0, se_n=0, cg_n=0, il_n=0, over_n=0, fs_n=0)
        ep_this: list[dict] = []
        views = {L.k: self._views(L) for L in self.lanes}
        t_start = time.monotonic()

        def pre(L: Lane, t: int) -> np.ndarray:
            t0 = time.monotonic()
            o = views[L.k]
            cand_n = o["cand_n"].copy()
            if L.aux_tg is not None:
                L.aux_tg.before(t, o["aux"])
            x = obs_to_device(o, device, max_rows=max_rows, static=static)
            if check_forward is not None:
                check_forward(x)
            if L.breaker is None:
                a, logp, value = runner.sample(x)
            else:
                a, logp, value = runner.sample(x, adjust=lambda lg: L.breaker.apply(o, lg))
            store.put_rows(t, L.lo, L.hi, x, o)
            a_np = a.cpu().numpy().astype(np.int32)
            valid = cand_n > 0
            a_np[~valid] = -1
            if L.breaker is not None:
                L.breaker.commit(a_np)
            L.cycles.commit(a_np, o)
            store.actions[t, L.lo:L.hi] = np.maximum(a_np, 0)
            store.logp[t, L.lo:L.hi] = logp.cpu().numpy()
            store.values[t, L.lo:L.hi] = value.cpu().numpy()
            store.valid[t, L.lo:L.hi] = valid
            L.cand_n, L.valid = cand_n, valid
            acc["infer_s"] += time.monotonic() - t0
            return a_np

        def post(L: Lane, t: int, res) -> None:
            rew, term, trunc, status, errs = res
            o, info = views[L.k], self.pair.lane_info(L.k)
            lo, hi = L.lo, L.hi
            done = (term | trunc).astype(bool)
            if L.breaker is not None:
                L.breaker.reset(done)
            r, fin = L.rewards.step(rew, done, info["last_episode"], o["aux"], start_entry=info["start_entry"])
            for e in fin:
                e["slot"] = int(e["slot"]) + lo                 # global slot numbers from here on
            if done.any():
                track.count_starts(done, info)                  # pool mode: start_entry[done] of the auto-resets
            if fin:
                track.annotate(fin)
                if hooks_on:
                    for e in fin:
                        e["update"], e["t"] = upd + 1, t
                        e["fields"] = episode_fields(e, episode_hooks)
            if L.aux_tg is not None:
                L.aux_tg.after(t, term, trunc, info["last_episode"], o["aux"])
            if L.shaper is not None:
                f_acq, ended = L.shaper.step(done, o["aux"], o["deck"])
                for e in fin:
                    e["shaping"] = ended.get(e["slot"] - lo, 0.0)
                    e["shaped_return"] = e["return"] + e["shaping"]
                r_obj[t, lo:hi] = r
                r = r + f_acq
            ended_c = L.cycles.finish(done)                     # = CycleDetector.after_step, local slots
            for e in fin:
                e["cycles"] = ended_c.get(e["slot"] - lo, 0)
            closed = L.cycles.observe(o, episode_ids=info["episode_id"], t=t).astype(np.float32)
            if cyc_r is not None:
                if L.shaper is None:
                    r_obj[t, lo:hi] = r
                cyc_r[t, lo:hi] = np.float32(cycle_cost) * closed
                r = r - cyc_r[t, lo:hi]
            store.rewards[t, lo:hi] = r
            store.dones[t, lo:hi] = done
            se = int(np.count_nonzero(status & STATUS["SIM_ERROR"]))
            cg = int(np.count_nonzero(status & STATUS["CONTENT_GAP"]))
            il = int(np.count_nonzero(status & STATUS["ILLEGAL_ACTION"]))
            if se or cg:
                for text in errs.values():
                    key = text.splitlines()[0][:160] if text else "?"
                    errors_seen[key] = errors_seen.get(key, 0) + 1
            acc["se_n"] += se
            acc["cg_n"] += cg
            acc["il_n"] += il
            acc["fs_n"] += int(np.count_nonzero(status & FLOOR_STALL)) if FLOOR_STALL else 0
            acc["over_n"] += int(np.count_nonzero(L.cand_n > max_rows))
            acc["n_steps"] += int(np.count_nonzero(L.valid))
            ep_this.extend(fin)

        def wait(fut):
            t0 = time.monotonic()
            try:
                res, dt = fut.result()
            finally:
                acc["wait_s"] += time.monotonic() - t0
            acc["env_s"] += dt
            return res

        pending = None
        try:
            pending = self.worker.submit(self._step_fn, A.k, pre(A, 0))
            a_b = pre(B, 0)
            for t in range(T):
                res = wait(pending)                                         # step A(t) done
                pending = self.worker.submit(self._step_fn, B.k, a_b)       # step B(t) runs during A's post / pre
                post(A, t, res)
                a_a = pre(A, t + 1) if t + 1 < T else None
                res = wait(pending)                                         # step B(t) done
                pending = self.worker.submit(self._step_fn, A.k, a_a) if a_a is not None else None
                post(B, t, res)
                if t + 1 < T:
                    a_b = pre(B, t + 1)
            pending = None
        except BaseException:
            if pending is not None:                                         # never leave a native step running
                try:
                    pending.result()
                except BaseException:
                    pass
            raise
        rollout_s = time.monotonic() - t_start
        self.totals["t_overlap_wait"] += acc["wait_s"]
        self.totals["t_rollout"] += rollout_s
        return {**acc, "rollout_s": rollout_s, "ep_this": ep_this}
