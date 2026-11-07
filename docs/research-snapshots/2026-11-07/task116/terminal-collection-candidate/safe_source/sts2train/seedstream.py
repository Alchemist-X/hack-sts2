"""Continuously generated training seed stream (``sts2train train --start-mode stream``) and the start bookkeeping
of both start modes.

Stream mode: every episode of every slot starts from a seed text that no episode of the run started from before,
``f"{prefix}-{character}-{n}"`` (prefix ``--seed-stream-prefix``, default ``SP<env_seed>``; character
``characters[n % len(characters)]``; ``--ascension``), with ``n`` taken from a monotonically increasing counter. The
counter is part of the checkpoint (``seed_stream``) and a resume continues at the stored value, so a resume neither
changes nor replays the start sequence. Indices whose seed collides with a frozen holdout game (run seed or (character,
seed text, ascension); ``sts2train.isolation``) are skipped and counted.

Implementation on the existing ABI (no C# change): the VecEnv is created with ``auto_reset=False`` and an empty pool,
so it never starts an episode by itself. ``StreamEnv`` wraps it; after every ``step`` the slots whose episode ended
(terminated or truncated: terminal, step cap, floor stall, SIM_ERROR / CONTENT_GAP) are restarted at once with
``restore_many`` onto the next stream entries, which are then released (``pool.release``: a stream entry starts exactly
one episode, so its template is dropped right away). The step's signals (reward, terminated, truncated, status) are
copied before the restart, because a restore writes RESET signals for its slots; ``last_episode`` is left alone by a
restore and still describes the ended episode, and the observation views / ``info()`` (``start_entry``, ``episode_id``,
``step_index``) show the new episode, exactly as with auto-reset. Every episode the trainer sees therefore starts from a
stream entry, and the env's own episode counter advances once per stream start: slot i's k-th episode (k = 1, 2, ...)
has ``episode_id = (k - 1) * N + i + 1`` (VecEnv.StartSlot), which ``tests/test_seed_stream.py`` checks against the
starts the stream made. A start whose template or first observation fails (status SIM_ERROR / CONTENT_GAP or no legal
action) consumes its seed (``stream_failed_total``, an ``episodes.jsonl`` row ``start_failed``) and the slot takes the
next seed (at most ``MAX_START_TRIES`` in a row).

Lookahead (``--stream-lookahead``; default auto = ``max(64, 2 * envs)``, grown to 1.25 x the starts of an update when
an update starts more episodes, at most ``16 * envs``): that many entries are generated ahead (FIFO, in counter order)
and their templates (``RunSim.NewRun``, ~10-20 ms of CPU each) are created by ``pool.warm()`` once per update, after
the rollout: in a background thread while the PPO update runs with ``--stream-prefetch`` (default; the env is idle
then, the native library serialises calls per env and the next ``step`` joins the thread first), else synchronously.
A restart in the rollout then clones a warm template (~0.1 ms with the release and the next add_fresh). With
``--stream-lookahead 0`` every restart runs NewRun inside ``restore_many`` (measured in the CPU smoke: 15-20 ms per
start, 84-88 % of the env time; with the lookahead: 3-4 %). Live pool size = the lookahead.

Counters (all sessions; checkpoint ``seed_stream``): ``next`` = the first index no start consumed (the resume point),
``started`` = seeds consumed by a start (= ``seeds_used_total``), ``failed`` (of those), ``skipped`` = indices below
``next`` skipped by the isolation guard, so ``next == started + skipped``. Lookahead entries that were generated but
not started when a session ends are not counted anywhere: they are generated again (same indices, same texts) after
the resume. In-flight episodes at a checkpoint are recorded with it (``inflight``: slot, seed, steps so far); a resume
from that checkpoint logs them as ``dropped`` rows in episodes.jsonl (they never finish; their indices are below
``next``, so they are never reused) and adds them to ``dropped_total``.

A fork of a checkpoint into a new run directory (e.g. two arms of an A/B from one common start) continues that
checkpoint's stream: the arms start the same seed sequence (common random numbers); give an arm its own family with
``--seed-stream-prefix`` / ``--env-seed`` (the counter continues, the texts differ).

Slot assignment: no RNG. At a reset slot i takes the i-th seed of the FIFO; after a step the slots whose episodes
ended take the next seeds in ascending slot order.

Pool mode (``PoolStarts``): the legacy fixed pool; entry names (fresh seed text, or ``<save file>#copy<c>``) and the
per-entry start counts (``seeds_used_total`` = entries started at least once; checkpoint ``start_counts``, kept across
a resume while the pool size is the same).
"""

from __future__ import annotations

import re
import threading
import time
from collections import deque
from dataclasses import asdict, dataclass

import numpy as np
from sts2vec import STATUS
from sts2vec.errors import Sts2VecError

ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
NON_BOOTSTRAP_BITS = ERR_BITS | STATUS["ILLEGAL_ACTION"] | STATUS.get("FLOOR_STALL", 0)
PREFIX_RE = re.compile(r"^[A-Z][A-Z0-9_]{0,23}$")
LEGACY_PREFIX = "TRAIN"             # the legacy pool's family (TRAIN<seed>-<CHARACTER>-<i>, silent-a10-ab-v1)
MAX_START_TRIES = 8
START_MODES = ("pool", "stream")


def default_prefix(env_seed: int) -> str:
    if int(env_seed) < 0:
        raise ValueError(f"env_seed {env_seed} < 0: give --seed-stream-prefix explicitly")
    return f"SP{int(env_seed)}"


def check_prefix(prefix: str) -> str:
    """A stream prefix: 1-24 characters [A-Z0-9_] starting with a letter (so never the game's legacy ``old`` form),
    not the legacy pool's ``TRAIN`` family."""
    if not isinstance(prefix, str) or not PREFIX_RE.match(prefix):
        raise ValueError(f"seed stream prefix {prefix!r}: expected 1-24 characters [A-Z0-9_] starting with a letter")
    if prefix.startswith(LEGACY_PREFIX):
        raise ValueError(f"seed stream prefix {prefix!r} is refused: TRAIN* is the legacy start pool's seed family "
                         "(its continuation TRAIN0-SILENT-4096.. is the frozen silent-a10-ab-v1 set)")
    return prefix


def stream_prefix(cfg) -> str:
    """The effective prefix of a TrainConfig: ``seed_stream_prefix``, else ``SP<env_seed>``."""
    p = getattr(cfg, "seed_stream_prefix", None)
    env_seed = cfg.env_seed if getattr(cfg, "env_seed", None) is not None else cfg.seed
    return check_prefix(p) if p else default_prefix(env_seed)


def stream_text(prefix: str, character: str, n: int) -> str:
    return f"{prefix}-{character}-{int(n)}"


def auto_lookahead(envs: int) -> int:
    return max(64, 2 * int(envs))


def max_lookahead(envs: int) -> int:
    return max(1024, 16 * int(envs))


def lookahead_of(cfg) -> int:
    la = getattr(cfg, "stream_lookahead", None)
    return auto_lookahead(cfg.envs) if la is None else int(la)


@dataclass
class StreamState:
    """The persisted counters of a seed stream (checkpoint ``seed_stream``; module docstring)."""

    prefix: str
    next: int = 0
    started: int = 0
    failed: int = 0
    skipped: int = 0

    @classmethod
    def from_dict(cls, d: dict | None, prefix: str) -> "StreamState":
        d = d or {}
        return cls(prefix=prefix, next=int(d.get("next", 0)), started=int(d.get("started", 0)),
                   failed=int(d.get("failed", 0)), skipped=int(d.get("skipped", 0)))


class SeedGrantExhausted(RuntimeError):
    """No new seed may be issued outside this worker's reserved range."""


class SeedStream:
    """The seed generator: ``generate()`` issues the next usable index (skipping holdout collisions), ``consume(n)``
    accounts a start of index n (in issuance order)."""

    def __init__(self, prefix: str, characters, ascension: int, *, guard=None, state: dict | None = None,
                 act1: str = "random", seed_range: tuple[int, int] | None = None):
        self.prefix = check_prefix(prefix)
        self.characters = tuple(characters)
        if not self.characters:
            raise ValueError("seed stream: characters is empty")
        self.ascension = int(ascension)
        self.act1 = act1
        self.guard = guard
        self.state = StreamState.from_dict(state, self.prefix)
        self.resumed_prefix = (state or {}).get("prefix")
        self.seed_range = seed_range
        self.grant_gap = None
        self.reserved_skipped = int((state or {}).get("reserved_skipped", 0))
        if seed_range is not None:
            begin, end = seed_range
            old = self.state.next
            if (type(begin) is not int or type(end) is not int or not old <= begin < end
                    or (self.resumed_prefix is not None and self.resumed_prefix != self.prefix)):
                raise ValueError("seed grant differs from the accepted stream")
            self.state.next = begin
            self.state.skipped += begin - old
            self.reserved_skipped += begin - old
            self.grant_gap = (old, begin)
        self.issued = self.state.next           # session-local issuance cursor (>= state.next)
        self.session_skips: list[dict] = []     # isolation skips issued this session (index, text, reason)

    def character(self, n: int) -> str:
        return self.characters[int(n) % len(self.characters)]

    def text(self, n: int) -> str:
        return stream_text(self.prefix, self.character(n), n)

    def generate(self) -> tuple[int, str, str]:
        """(n, character, seed text) of the next issued index whose seed is no holdout game."""
        while True:
            n = self.issued
            if self.seed_range is not None and n >= self.seed_range[1]:
                raise SeedGrantExhausted("worker seed range exhausted")
            self.issued += 1
            ch = self.character(n)
            text = stream_text(self.prefix, ch, n)
            why = self.guard.collision(ch, text, self.ascension) if self.guard is not None else None
            if why is None:
                return n, ch, text
            if len(self.session_skips) < 1000:
                self.session_skips.append({"n": n, "seed_text": text, "reason": why})

    def consume(self, n: int) -> None:
        st = self.state
        if self.seed_range is not None and not self.seed_range[0] <= n < self.seed_range[1]:
            raise SeedGrantExhausted("seed lies outside the worker reservation")
        if n < st.next:
            raise RuntimeError(f"seed stream: index {n} consumed after {st.next - 1} (out of order or reused)")
        st.skipped += n - st.next               # every index in [next, n) was skipped by the guard (FIFO order)
        st.next = n + 1
        st.started += 1

    def grant_record(self) -> dict:
        if self.seed_range is None:
            return {}
        return {"seed_range": list(self.seed_range), "reserved_skip_interval": list(self.grant_gap),
                "issued_until": self.issued,
                "reserved_skipped": self.reserved_skipped}

    def state_dict(self) -> dict:
        return {**asdict(self.state), "characters": list(self.characters), "ascension": self.ascension,
                **self.grant_record()}


class _Tracker:
    """Per-update start counters shared by the two modes."""

    def _reset_update(self) -> None:
        self.update = {"starts": 0, "start_failed": 0}
        self.t_restart = 0.0
        self.t_prefetch = 0.0
        self.t_prefetch_wait = 0.0

    def pop_update(self) -> dict:
        out = dict(self.update, t_restart=self.t_restart, t_prefetch=self.t_prefetch,
                   t_prefetch_wait=self.t_prefetch_wait)
        self._reset_update()
        return out

    def wait_prefetch(self) -> None:
        pass

    def pop_failed(self) -> list[dict]:
        return []


class StreamEnv(_Tracker):
    """A VecEnv (auto_reset off, empty pool) whose episodes all start from a ``SeedStream`` (module docstring).
    Everything not overridden here is the wrapped env's."""

    mode = "stream"

    def __init__(self, env, stream: SeedStream, *, lookahead: int = 64, prefetch: bool = True,
                 adaptive: bool = False):
        if getattr(env, "config", {}).get("auto_reset", True):
            raise ValueError("StreamEnv needs a VecEnv created with auto_reset=False")
        for need in ("supports_restore_many", "supports_pool_release"):
            if not getattr(env, need, False):
                raise Sts2VecError(f"--start-mode stream needs restore_many and pool.release (ABI 2); "
                                   f"{env.lib.path} has ABI {env.abi_version}")
        if len(env.pool):
            raise ValueError(f"StreamEnv needs an env with an empty pool (it has {len(env.pool)} entries)")
        self.env = env
        self.stream = stream
        self.lookahead = max(0, int(lookahead))
        self.adaptive = bool(adaptive) and self.lookahead > 0
        self.max_lookahead = max(self.lookahead, max_lookahead(env.num_envs))
        self.prefetch_enabled = bool(prefetch) and self.lookahead > 0
        self.fifo: deque = deque()              # (n, character, text, entry id): issued, not started
        n = env.num_envs
        self.slot_n = np.full(n, -1, dtype=np.int64)
        self.slot_text: list[str | None] = [None] * n
        self.slot_entry = np.full(n, -1, dtype=np.int64)
        self.slot_starts = np.zeros(n, dtype=np.int64)       # successful stream starts per slot this session
        self.slot_attempts = np.zeros(n, dtype=np.int64)     # restores per slot this session (incl. failed starts)
        self.ended: dict[int, tuple[int, str, int]] = {}    # slot -> (n, text, entry) of the episodes that ended
        self.failed_rows: list[dict] = []                    # start failures not yet logged
        self.errors: dict[int, str] = {}
        self._thread: threading.Thread | None = None
        self._thread_exc: BaseException | None = None
        self.totals = {"t_restart": 0.0, "t_prefetch": 0.0, "t_prefetch_wait": 0.0, "prefetched": 0}
        self._reset_update()

    def __getattr__(self, name):
        if name == "env":
            raise AttributeError(name)
        return getattr(self.env, name)

    # ------------------------------------------------------------------------------------------------ the stream

    def _issue(self, k: int) -> None:
        pool = self.env.pool
        for _ in range(max(0, int(k))):
            n, ch, text = self.stream.generate()
            eid = pool.add_fresh(ch, seed=text, ascension=self.stream.ascension, act1=self.stream.act1)
            self.fifo.append((n, ch, text, eid))

    def _top_up(self) -> None:
        if len(self.fifo) < self.lookahead:
            self._issue(self.lookahead - len(self.fifo))

    def prime(self) -> int:
        """Generate the entries of the first reset plus the lookahead now (``pool.warm()`` then creates their
        templates in parallel, before training); -> how many the FIFO holds."""
        want = self.env.num_envs + self.lookahead
        if len(self.fifo) < want:
            self._issue(want - len(self.fifo))
        return len(self.fifo)

    def _start(self, slots) -> None:
        """One new stream seed on every slot of ``slots`` (restore_many), entries released at once; failed starts take
        the next seed; then the lookahead is topped up."""
        todo = np.asarray(slots, dtype=np.int64)
        tries = 0
        while len(todo):
            tries += 1
            if len(self.fifo) < len(todo):
                self._issue(len(todo) - len(self.fifo))
            seeds = [self.fifo.popleft() for _ in range(len(todo))]
            for s in seeds:
                self.stream.consume(s[0])
            entries = np.array([s[3] for s in seeds], dtype=np.int64)
            self.env.restore_many(todo, entries)
            self.env.pool.release(entries)
            self.slot_attempts[todo] += 1
            st = np.asarray(self.env.status)[todo]
            cn = np.asarray(self.env.buffers["cand_n"])[todo]
            bad = ((st & ERR_BITS) != 0) | (cn <= 0)
            for j, i in enumerate(todo):
                n, _, text, eid = seeds[j]
                if bad[j]:
                    self.stream.state.failed += 1
                    self.update["start_failed"] += 1
                    err = (self.env.last_error(int(i)) or "").splitlines()
                    self.failed_rows.append({"slot": int(i), "seed_text": text, "n": int(n),
                                             "reason": (err[0] if err else "no legal action")[:300]})
                    self.slot_n[i], self.slot_text[i], self.slot_entry[i] = -1, None, -1
                else:
                    self.slot_n[i], self.slot_text[i], self.slot_entry[i] = n, text, eid
                    self.slot_starts[i] += 1
                    self.update["starts"] += 1
            todo = todo[bad]
            if len(todo) and tries >= MAX_START_TRIES:
                raise Sts2VecError(f"seed stream: {len(todo)} slots failed {tries} starts in a row; last: "
                                   f"{self.failed_rows[-1]}")
        self._top_up()

    # ------------------------------------------------------------------------------------------------ env API

    def reset(self, mask=None, start_ids=None, *, copy: bool = False):
        """Start every slot from the stream (``mask`` / ``start_ids`` are not supported: the stream picks)."""
        if mask is not None or start_ids is not None:
            raise ValueError("stream mode: reset() starts every slot from the seed stream (no mask / start_ids)")
        self._join()
        t = time.monotonic()
        self._start(np.arange(self.env.num_envs))
        self.ended = {}
        self.t_restart += time.monotonic() - t
        self.totals["t_restart"] += time.monotonic() - t
        obs = self.env.obs
        return {k: v.copy() for k, v in obs.items()} if copy else obs

    def step(self, actions, *, copy: bool = False, capture_final: bool = False):
        """``VecEnv.step`` with the ended slots restarted from the stream before returning (module docstring); the
        returned signals are the step's (copies). With capture_final, info carries sparse copies of the valid
        step-cap observations, indexed by slot, before the reset writes to the same buffers."""
        self._join()
        obs, rew, term, trunc, status, info = self.env.step(actions)
        rew, term, trunc, status = rew.copy(), term.copy(), trunc.copy(), status.copy()
        done = (term != 0) | (trunc != 0)
        self.errors = self.env.slot_errors() if (status & ERR_BITS).any() else {}
        self.ended = {}
        if capture_final:
            final_slots = np.flatnonzero((trunc != 0) & (term == 0) & ((status & NON_BOOTSTRAP_BITS) == 0))
            final_obs = {key: value[final_slots] for key, value in obs.items()} if final_slots.size else {}
        if done.any():
            t = time.monotonic()
            slots = np.flatnonzero(done)
            for i in slots:
                self.ended[int(i)] = (int(self.slot_n[i]), self.slot_text[i], int(self.slot_entry[i]))
            self._start(slots)
            dt = time.monotonic() - t
            self.t_restart += dt
            self.totals["t_restart"] += dt
        info = self.env.info(copy)
        if capture_final:
            info["final_observation"] = (final_slots, final_obs)
        obs = self.env.obs
        return ({k: v.copy() for k, v in obs.items()} if copy else obs), rew, term, trunc, status, info

    def slot_errors(self, bits: int = ERR_BITS) -> dict[int, str]:
        """The error texts of the slots whose last step failed (captured before their restart)."""
        return {i: t for i, t in self.errors.items()}

    # ------------------------------------------------------------------------------------------------ prefetch

    def prefetch(self, background: bool | None = None) -> None:
        """Create the lookahead's templates (``pool.warm``), in a background thread with ``prefetch`` on. With the
        auto lookahead, first grow it to 1.25 x this update's starts when they exceeded it."""
        self._join()
        if self.adaptive and 5 * self.update["starts"] > 4 * self.lookahead:
            self.lookahead = min(self.max_lookahead, -(-5 * self.update["starts"] // 4))
            self._top_up()
        if not self.fifo:
            return
        if background is None:
            background = self.prefetch_enabled
        if background:
            self._thread = threading.Thread(target=self._warm, name="sts2train-stream-prefetch", daemon=True)
            self._thread.start()
        else:
            self._warm()

    def _warm(self) -> None:
        t = time.monotonic()
        try:
            self.totals["prefetched"] += int(self.env.pool.warm())
        except BaseException as e:      # re-raised in the main thread by _join
            self._thread_exc = e
        dt = time.monotonic() - t
        self.t_prefetch += dt
        self.totals["t_prefetch"] += dt

    def _join(self) -> None:
        th = self._thread
        if th is not None:
            t = time.monotonic()
            th.join()
            self._thread = None
            dt = time.monotonic() - t
            self.t_prefetch_wait += dt
            self.totals["t_prefetch_wait"] += dt
        if self._thread_exc is not None:
            e, self._thread_exc = self._thread_exc, None
            raise e

    def wait_prefetch(self) -> None:
        """Join the background prefetch (the trainer calls it before it reads the update's counters)."""
        self._join()

    def close(self) -> None:
        try:
            self._join()
        finally:
            self.env.close()

    # ------------------------------------------------------------------------------------------------ bookkeeping

    def annotate(self, finished: list[dict]) -> None:
        """Add ``seed_text`` / ``seed_n`` to the episodes that ended in the last step (``EpisodeRewards.step``)."""
        for e in finished:
            n, text, _ = self.ended.get(e["slot"], (-1, None, -1))
            e["seed_text"], e["seed_n"] = text, (n if n >= 0 else None)

    def count_starts(self, done, info) -> None:
        """Pool-mode hook; the stream counts its starts itself."""

    def pop_failed(self) -> list[dict]:
        """Start failures since the last call (episodes.jsonl rows ``start_failed``)."""
        rows, self.failed_rows = self.failed_rows, []
        return rows

    def inflight(self) -> dict:
        """The episodes running now (for the checkpoint): slot, stream index, seed text, steps so far."""
        live = np.flatnonzero(self.slot_n >= 0)
        steps = np.asarray(self.env.info()["step_index"])
        return {"slot": live.tolist(), "n": self.slot_n[live].tolist(), "seed_text": [self.slot_text[i] for i in live],
                "steps": steps[live].astype(np.int64).tolist()}

    def state_dict(self) -> dict:
        return self.stream.state_dict()

    def metrics(self) -> dict:
        st = self.stream.state
        return {"seeds_used_total": st.started, "seed_stream_next": st.next, "stream_skipped_total": st.skipped,
                "stream_failed_total": st.failed, "stream_lookahead": len(self.fifo)}

    def record(self) -> dict:
        """config.json / summary.json ``seed_stream`` block."""
        return {"prefix": self.stream.prefix, "characters": list(self.stream.characters),
                "ascension": self.stream.ascension, **{k: v for k, v in asdict(self.stream.state).items()
                                                        if k != "prefix"},
                "lookahead": self.lookahead, "lookahead_adaptive": self.adaptive, "prefetch": self.prefetch_enabled,
                "resumed_prefix": self.stream.resumed_prefix, **self.stream.grant_record(),
                "isolation_skips_this_session": self.stream.session_skips[:20]}


class PoolStarts(_Tracker):
    """Pool mode (legacy): names of the pool entries and how often each was started (module docstring)."""

    mode = "pool"

    def __init__(self, env, starts=None, prev_counts=None):
        self.env = env
        self.names = pool_entry_names(env, starts)
        self.counts = np.zeros(len(self.names), dtype=np.int64)
        self.counts_restored = prev_counts is not None and len(prev_counts) == len(self.counts)
        if self.counts_restored:
            self.counts += np.asarray(prev_counts, dtype=np.int64)
        self._reset_update()

    def name(self, entry: int) -> str | None:
        return self.names[entry] if 0 <= int(entry) < len(self.names) else None

    def count_starts(self, done, info) -> None:
        """Count the episodes started at ``done`` slots (all slots after a reset: ``done=None``)."""
        se = np.asarray(info["start_entry"], dtype=np.int64)
        new = se if done is None else se[np.asarray(done, dtype=bool)]
        new = new[(new >= 0) & (new < len(self.counts))]
        np.add.at(self.counts, new, 1)
        self.update["starts"] += len(new)

    def annotate(self, finished: list[dict]) -> None:
        for e in finished:
            e["seed_text"], e["seed_n"] = self.name(e.get("start_entry", -1)), None

    def inflight(self) -> dict:
        info = self.env.info()
        se = np.asarray(info["start_entry"], dtype=np.int64)
        live = np.flatnonzero(se >= 0)
        steps = np.asarray(info["step_index"])
        return {"slot": live.tolist(), "n": [None] * len(live), "seed_text": [self.name(int(e)) for e in se[live]],
                "steps": steps[live].astype(np.int64).tolist()}

    def state_dict(self) -> dict:
        return {"start_counts": self.counts.copy()}

    def metrics(self) -> dict:
        return {"seeds_used_total": int(np.count_nonzero(self.counts)), "seed_stream_next": None}

    def record(self) -> dict:
        return {"pool_entries": len(self.names), "start_counts_restored": self.counts_restored}


def pool_entry_names(env, starts=None) -> list[str]:
    """Name of every start-pool entry in id order: its fresh seed text, or ``<save file>#copy<c>`` for a save copy
    (``starts``, the trainer's StartPool, when the pool mixes saves)."""
    from .startpool import FRESH

    if starts is None:
        spec = getattr(env, "pool_spec", None)
        names = [t for _, t in spec.seed_texts()] if spec is not None else []
        for rid, e in getattr(env.pool, "added", []):
            while len(names) <= rid:
                names.append(None)
            names[rid] = getattr(e, "seed", None) or str(getattr(e, "path", "?"))
        return names
    fresh = {rid: e.seed for rid, e in env.pool.added if hasattr(e, "seed")}
    L = starts.layout
    return [fresh.get(j) if L.kinds[j] == FRESH else f"{starts.saves[int(L.index[j])].file}#copy{int(L.copy[j])}"
            for j in range(L.size)]
