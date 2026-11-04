"""VecEnv: the batched Python API of CONTRACTS.md §D6 over the sts2vec C ABI (§D5).

One ``VecEnv`` = one native env handle with ``num_envs`` slots. Every call resets or steps all selected slots in one
native call (worker threads inside, GIL released); observations are written in place into env-owned numpy arrays
allocated from the schema. ``reset``/``step``/``restore`` return views of those arrays, valid until the next call on the
env (``copy=True`` returns copies).

Return codes are never swallowed: a non-zero return raises ``Sts2VecError`` with the export's last_error text. Per-slot
problems are not exceptions — they are the ``status`` bits (``sts2vec.Status``), which ``step`` returns and which the
caller must look at (``ILLEGAL_ACTION``, ``CONTENT_GAP``, ``SIM_ERROR``, ``POOL_EMPTY`` …; ``slot_errors()`` gives the
native exception text of failed slots).

Threads: the native library serialises every call on one env (a lock per env, held for the whole call; the GIL is
released during it). ``branch`` / ``restore_many`` / ``pool.release`` keep no Python-side scratch, so they may be called
from another thread while one thread steps; they then wait for the step (and vice versa). ``reset`` / ``step`` /
``restore`` share per-env scratch arrays: drive them from one thread.

Search (ABI 2, sim/VEC.md "Search"): ``branch(src, dsts)`` copies a live slot into other slots in one call (exact or
determinized, no pool entry), ``restore_many(slots, entries)`` restores many slots at once, ``pool.release(ids)`` frees
pool entries.
"""

from __future__ import annotations

import json
import os

import numpy as np

from .errors import SERVER_GC_MARKER, ServerGCError, Sts2VecError
from .loader import Library, load
from .pool import ALL_CHARACTERS, Entry, FreshEntry, Pool, PoolSpec, SaveEntry, int32_list, make_config
from .schema import SIGNAL_BUFFERS, STATUS, Buffers, Schema, status_counts

__all__ = ["ALL_CHARACTERS", "Entry", "FLOOR_STEP_CAP", "FreshEntry", "PoolSpec", "SaveEntry", "Sts2VecError",
           "VecEnv", "branch_seed", "random_actions", "schema_of"]

_M64 = (1 << 64) - 1

# Default floor stall cap: truncate an episode after this many non-combat decisions on one floor without the floor
# advancing (status FLOOR_STALL, last_episode.end_kind FloorStall). The protocol's limit (v0.2 §5, `stuck`); the C#
# default (VecConfig.DefaultFloorStepCap) and the evaluator (`sts2vec eval`) use the same value.
FLOOR_STEP_CAP = 80

_schema_cache: dict[int, Schema] = {}


def schema_of(lib: Library) -> Schema:
    """The parsed schema of a loaded library (cached per library)."""
    s = _schema_cache.get(id(lib))
    if s is None:
        s = _schema_cache[id(lib)] = Schema.from_json(lib.schema_json())
    return s


def branch_seed(seed: int, index: int) -> int:
    """The determinization seed of destination position ``index`` in ``env.branch(src, dsts, seed=seed)``
    (``VecEnv.BranchSeed``): the SplitMix64 finaliser of ``seed + (index + 1) * 0x9E3779B97F4A7C15`` mod 2**64. So
    ``branch(src, [d], seed=s)`` puts into ``d`` exactly what ``restore(d, clone_determinized(src, branch_seed(s, 0)))``
    would, and destination j of any branch call is a function of (state, seed, j) only."""
    z = (int(seed) + 0x9E3779B97F4A7C15 * (int(index) + 1)) & _M64
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & _M64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & _M64
    return z ^ (z >> 31)


def random_actions(rng: np.random.Generator, cand_n: np.ndarray, out: np.ndarray | None = None) -> np.ndarray:
    """Uniform random candidate index per slot over ``[0, cand_n)``; ``-1`` (no-op) where ``cand_n == 0``.

    Draws exactly ``len(cand_n)`` uniforms per call regardless of the values, so a seeded generator yields the same
    action sequence for any env whose ``cand_n`` sequence is the same (determinism checks rely on this)."""
    cand_n = np.asarray(cand_n)
    u = rng.random(len(cand_n))
    acts = np.floor(u * np.maximum(cand_n, 1)).astype(np.int32)
    acts[cand_n <= 0] = -1
    if out is not None:
        out[:] = acts
        return out
    return acts


class VecEnv:
    """N simulator slots stepped in one native call (CONTRACTS.md §D2/§D6).

    Parameters mirror ``config_json`` (§D5): ``threads`` worker threads, ``step_cap`` decisions per episode before
    truncation, ``auto_reset`` (finished slots restart in the same step), ``seed_text`` (per-slot start sequences),
    ``content`` ("ported_only" | "all"), ``pool`` (``PoolSpec``; None = empty pool), ``pool_cache`` (template cache size),
    ``fast_path`` (clone-free stepping when the static prompt-site predicate proves no prompt can occur; same outputs),
    ``floor_step_cap`` (truncate an episode after this many non-combat decisions — legal or illegal, no-ops excluded —
    on one floor without the floor advancing: ``truncated``, status ``FLOOR_STALL``, ``end_kind`` FloorStall; 0 = off;
    default ``FLOOR_STEP_CAP`` = 80, the protocol evaluator's ``stuck`` rule; combat decisions never count).
    ``dll`` picks the library (default: ``STS2VEC_AOT_LIB`` / ``STS2VEC_DLL`` / the Release build; see ``sts2vec.loader``).
    """

    def __init__(self, num_envs: int, *, threads: int = 8, step_cap: int = 3000, auto_reset: bool = True,
                 seed_text: str = "VEC", content: str = "ported_only", pool: PoolSpec | None = PoolSpec(),
                 pool_cache: int = 4096, fast_path: bool = True, floor_step_cap: int = FLOOR_STEP_CAP,
                 dll: str | os.PathLike | None = None):
        self._h = 0
        self._lib = load(dll)
        self.schema = schema_of(self._lib)
        self.num_envs = int(num_envs)
        self.pool_spec = pool
        if int(floor_step_cap) < 0:
            raise ValueError(f"floor_step_cap must be >= 0 (0 = off), got {floor_step_cap}")
        cap: int | None = int(floor_step_cap)
        if not self.schema.supports_floor_step_cap:
            # a library built before floor_step_cap: it has no cap and rejects the config key
            if cap:
                raise Sts2VecError(f"floor_step_cap={cap}: {self._lib.path} was built before floor_step_cap existed; "
                                   "rebuild it (dotnet build sim/Sts2Sim.sln -c Release) or pass floor_step_cap=0")
            cap = None
        self.floor_step_cap = int(floor_step_cap)
        self.config = make_config(self.num_envs, threads=threads, step_cap=step_cap, auto_reset=auto_reset,
                                  seed_text=seed_text, content=content, pool=pool, pool_cache=pool_cache,
                                  fast_path=fast_path, floor_step_cap=cap)
        cfg = json.dumps(self.config).encode("utf-8")
        h = self._lib.sts2vec_create(cfg, len(cfg))
        if h <= 0:
            text = self._lib.last_error(0)
            if SERVER_GC_MARKER in text:
                overrides = ", ".join(f"{k}={os.environ[k]}" for k in ("DOTNET_gcServer", "COMPlus_gcServer")
                                      if k in os.environ)
                raise ServerGCError(f"sts2vec_create: {text} [loader: {self._lib.mode}, {self._lib.gc_mode}, "
                                    f"runtimeconfig {self._lib.runtimeconfig}"
                                    + (f"; environment overrides {overrides}" if overrides else "") + "]")
            raise Sts2VecError(f"sts2vec_create: {text}")
        self._h = int(h)
        self.pool = Pool(self)
        try:
            for e in (pool.entries if pool is not None else ()):
                self.pool.add(e)
        except BaseException:
            self.close()
            raise
        self.buffers: Buffers = self.schema.allocate(self.num_envs)
        self._actions = np.full(self.num_envs, -1, dtype=np.int32)
        self._mask = np.ones(self.num_envs, dtype=np.uint8)
        self._starts = np.full(self.num_envs, -1, dtype=np.int32)
        self._threads = int(threads)

    # ------------------------------------------------------------------------------------------------ plumbing

    @property
    def handle(self) -> int:
        return self._h

    @property
    def lib(self) -> Library:
        return self._lib

    @property
    def closed(self) -> bool:
        return self._h == 0

    def _live_handle(self) -> int:
        if self._h == 0:
            raise Sts2VecError("VecEnv is closed")
        return self._h

    def _check(self, rc: int, what: str = "call") -> int:
        if rc < 0:
            text = (self._lib.last_error(self._h, -1) if self._h else "") or self._lib.last_error(0)
            raise Sts2VecError(f"sts2vec_{what}: {text or 'failed (no error text)'}")
        return int(rc)

    def _vector(self, values, dtype, name: str, dst: np.ndarray) -> np.ndarray:
        a = np.asarray(values)
        if a.shape != (self.num_envs,):
            raise ValueError(f"{name} must have shape ({self.num_envs},), got {a.shape}")
        if dtype is np.int32 and a.dtype.kind not in "iub":
            raise TypeError(f"{name} must be an integer array, got dtype {a.dtype}")
        dst[:] = a
        return dst

    def _obs(self, copy: bool) -> dict[str, np.ndarray]:
        a = self.buffers.arrays
        return {k: (v.copy() if copy else v) for k, v in a.items() if k not in SIGNAL_BUFFERS}

    # ------------------------------------------------------------------------------------------------ API

    @property
    def obs(self) -> dict[str, np.ndarray]:
        """Views of the current observation buffers (as last written)."""
        return self._obs(False)

    def info(self, copy: bool = False) -> dict[str, np.ndarray]:
        """``{"episode_id", "step_index", "start_entry"}`` (int64 [N]) and ``"last_episode"`` (int32 [N, 8])."""
        ep = self.buffers["episode"]
        f = self.schema.cols("episode")
        out = {"episode_id": ep[:, f["episode_id"]], "step_index": ep[:, f["step_index"]],
               "start_entry": ep[:, f["start_entry"]], "last_episode": self.buffers["last_episode"]}
        return {k: (v.copy() if copy else v) for k, v in out.items()}

    @property
    def status(self) -> np.ndarray:
        """The status bits of the last call (int32 [N])."""
        return self.buffers["status"]

    def reset(self, mask=None, start_ids=None, *, copy: bool = False) -> dict[str, np.ndarray]:
        """(Re)start every slot with ``mask[i] != 0`` (None = all) from pool entry ``start_ids[i]`` (None or ``< 0`` = the
        slot's deterministic start sequence). Writes those slots' observations and signals (status ``RESET``); other
        slots' buffers are left untouched. Raises if a start id is ``>= len(pool)`` (nothing changes then)."""
        h = self._live_handle()
        mp = None if mask is None else self._vector(np.asarray(mask) != 0, np.uint8, "mask", self._mask).ctypes.data
        sp = None if start_ids is None else self._vector(start_ids, np.int32, "start_ids", self._starts).ctypes.data
        self._check(self._lib.sts2vec_reset(h, mp, sp, self.buffers.ptr), "reset")
        return self._obs(copy)

    def step(self, actions, *, copy: bool = False):
        """Apply ``actions[i]`` (candidate index into slot i's ``cand`` rows; ``< 0`` = no-op) to every slot.

        Returns ``(obs, reward, terminated, truncated, status, info)``: ``reward`` float32 [N] (1 at a Victory
        terminal), ``terminated``/``truncated`` uint8 [N], ``status`` int32 [N] bit flags, ``info`` as ``info()``.
        With auto-reset, a slot whose episode ended this step already shows the *next* episode's first observation;
        the finished episode is described by ``info["last_episode"]`` and the signals."""
        h = self._live_handle()
        self._vector(actions, np.int32, "actions", self._actions)
        self._check(self._lib.sts2vec_step(h, self._actions.ctypes.data, self.buffers.ptr), "step")
        b = self.buffers
        sig = (b["reward"], b["terminated"], b["truncated"], b["status"])
        if copy:
            sig = tuple(x.copy() for x in sig)
        return (self._obs(copy), *sig, self.info(copy))

    def snapshot(self, slot: int) -> int:
        """Append a pool entry holding a clone of the slot's current state (works at pending prompts); returns its id."""
        return self._check(self._lib.sts2vec_snapshot(self._live_handle(), int(slot)), "snapshot")

    def clone_determinized(self, slot: int, seed: int) -> int:
        """Append a pool entry holding a *determinized* clone of the slot's current state and return its id.

        The clone shows the same player view (identical observation bytes) while its hidden state is resampled from
        ``seed`` (uint64): draw-pile order, every RNG stream (so future monster moves, card generation, rewards, ...),
        relic grab-bag order and unvisited event order. Works at pending prompts. Same state and seed give the same
        clone; ``restore(slot, entry)`` starts a slot from it (information-set search). See sim/VEC.md
        "Determinized clones"."""
        fn = self._lib.require("sts2vec_clone_determinized")
        return self._check(fn(self._live_handle(), int(slot), int(seed) & 0xFFFF_FFFF_FFFF_FFFF), "clone_determinized")

    def restore(self, slot: int, entry: int, *, copy: bool = False) -> dict[str, np.ndarray]:
        """Restart one slot from one pool entry and write that slot's observation (E4 search)."""
        self._check(self._lib.sts2vec_restore(self._live_handle(), int(slot), int(entry), self.buffers.ptr), "restore")
        return self._obs(copy)

    def branch(self, src, dsts, determinized: bool = True, seed: int = 0, *,
               copy: bool = False) -> dict[str, np.ndarray]:
        """Copy a live slot's state into every slot of ``dsts`` in one native call (ABI 2).

        ``src`` is one slot (``sts2vec_branch``) or one source slot per destination (``sts2vec_branch_many``: many roots in
        one call, one parallel region and one observation encode per source; the cheaper form for batched search).
        ``determinized=True`` (honest search): the destination at position j of the call gets
        ``CloneDeterminized(branch_seed(seed, j))`` of its source, the same player view (identical observation bytes) with
        the hidden state resampled (draw order, RNG streams, future monster moves, run queues); ``determinized=False``
        (oracle): an exact clone (same ``state_hash``, steps exactly like the source). Pending prompts are kept. No pool
        entry is created, so nothing accumulates.

        Every destination starts a new episode: status ``RESET``, a new ``episode_id``, ``step_index`` 0,
        ``start_entry`` -1, floor stall count 0; ``last_episode`` is left as it was. Its observation, candidates and
        signals are written in this call (like ``reset``); the sources and every other slot are untouched. Raises
        ``Sts2VecError`` (nothing changed) when a source holds no run or ``dsts`` has a slot out of range, a source of the
        call, or a duplicate. Returns the observation views (``copy=True``: copies). Serialised with other calls on this
        env."""
        d = int32_list(dsts, "dsts")
        mode, s64 = 1 if determinized else 0, int(seed) & _M64
        if np.ndim(src) == 0:
            fn = self._lib.require("sts2vec_branch")
            rc = fn(self._live_handle(), int(src), d.ctypes.data if len(d) else None, len(d), mode, s64, self.buffers.ptr)
            self._check(rc, "branch")
        else:
            fn = self._lib.require("sts2vec_branch_many")
            srcs = int32_list(src, "src")
            if len(srcs) != len(d):
                raise ValueError(f"src has {len(srcs)} entries but dsts has {len(d)} (one source per destination)")
            rc = fn(self._live_handle(), srcs.ctypes.data if len(d) else None, d.ctypes.data if len(d) else None, len(d),
                    mode, s64, self.buffers.ptr)
            self._check(rc, "branch_many")
        return self._obs(copy)

    def restore_many(self, slots, entries, *, copy: bool = False) -> dict[str, np.ndarray]:
        """``restore(slots[j], entries[j])`` for every j in one native call, in parallel (``sts2vec_restore_many``, ABI 2).

        Same per-slot result as ``restore`` (status ``RESET``, new ``episode_id``, ``step_index`` 0, ``start_entry`` = the
        entry; ``last_episode`` untouched); other slots are untouched. Several slots may start from one entry. Raises
        ``Sts2VecError`` (nothing changed) for a slot out of range or listed twice, a length mismatch, or an entry that is
        not live (never issued or released). Returns the observation views (``copy=True``: copies)."""
        fn = self._lib.require("sts2vec_restore_many")
        s = int32_list(slots, "slots")
        e = int32_list(entries, "entries")
        if len(s) != len(e):
            raise ValueError(f"slots has {len(s)} entries but entries has {len(e)}")
        self._check(fn(self._live_handle(), s.ctypes.data if len(s) else None, e.ctypes.data if len(e) else None, len(s),
                       self.buffers.ptr), "restore_many")
        return self._obs(copy)

    @property
    def abi_version(self) -> int:
        """The loaded library's ABI version (``Library.abi_version``; 1 = built before ``sts2vec_abi_version``)."""
        return self._lib.abi_version

    @property
    def supports_branch(self) -> bool:
        """``branch`` is available in the loaded library (ABI >= 2)."""
        return self._lib.supports_branch

    @property
    def supports_restore_many(self) -> bool:
        """``restore_many`` is available in the loaded library (ABI >= 2)."""
        return self._lib.supports_restore_many

    @property
    def supports_pool_release(self) -> bool:
        """``pool.release`` is available in the loaded library (ABI >= 2)."""
        return self._lib.supports_pool_release

    def state_hash(self, slots=None) -> np.ndarray:
        """``RunSim.StateHash`` per slot (uint64; 0 for a slot without a run). Debug/test only (14–131 µs each)."""
        h = self._live_handle()
        s = np.arange(self.num_envs, dtype=np.int32) if slots is None else np.ascontiguousarray(slots, dtype=np.int32).ravel()
        out = np.zeros(len(s), dtype=np.uint64)
        if len(s):
            self._check(self._lib.sts2vec_state_hash(h, s.ctypes.data, len(s), out.ctypes.data), "state_hash")
        return out

    @property
    def threads(self) -> int:
        return self._threads

    def set_threads(self, threads: int) -> None:
        self._check(self._lib.sts2vec_set_threads(self._live_handle(), int(threads)), "set_threads")
        self._threads = int(threads)

    def last_error(self, slot: int = -1) -> str:
        """Env-level error text (``slot=-1``) or a slot's last native exception (full ``ToString()``)."""
        return self._lib.last_error(self._live_handle(), int(slot))

    def slot_errors(self, bits: int = STATUS["CONTENT_GAP"] | STATUS["SIM_ERROR"]) -> dict[int, str]:
        """Slot -> last error text for the slots whose current status has any of ``bits``."""
        idx = np.flatnonzero(self.buffers["status"] & bits)
        return {int(i): self.last_error(int(i)) for i in idx}

    def status_counts(self) -> dict[str, int]:
        return status_counts(self.buffers["status"])

    def legal_mask(self) -> np.ndarray:
        """bool [N, CAP_CANDIDATES]: ``True`` for selectable candidate indices (``j < cand_n[i]``)."""
        cap = self.schema["cand"].shape[0]
        return np.arange(cap)[None, :] < self.buffers["cand_n"][:, None]

    def candidates(self, i: int) -> list[dict]:
        """Slot i's candidate rows decoded for debugging: every ``cand`` field, ``kind`` as a RunActionKind name
        (``kind_id`` keeps the value) and ``card`` = the card id's vocabulary name when ``card_id > 0``."""
        spec = self.schema["cand"]
        kinds = self.schema.vocab.enums["RunActionKind"]
        cards = self.schema.vocab.cards
        n = int(self.buffers["cand_n"][i])
        rows = []
        for j, r in enumerate(self.buffers["cand"][i, :n]):
            d = {"index": j, **dict(zip(spec.fields, (int(x) for x in r)))}
            d["kind_id"] = d["kind"]
            d["kind"] = kinds.name(d["kind"])
            if d.get("card_id", 0) > 0:
                d["card"] = cards.name(d["card_id"])
            rows.append(d)
        return rows

    def last_episode(self, i: int) -> dict:
        """Slot i's ``last_episode`` row decoded (``end_kind`` as an EndKind name)."""
        f = self.schema["last_episode"].fields
        d = dict(zip(f, (int(x) for x in self.buffers["last_episode"][i])))
        if "end_kind" in d and "EndKind" in self.schema.vocab.enums:
            d["end_kind"] = self.schema.vocab.enums["EndKind"].name(d["end_kind"])
        if d.get("last_encounter_id", 0) > 0 and "encounters" in self.schema.vocab.lists:
            d["last_encounter"] = self.schema.vocab.encounters.name(d["last_encounter_id"])
        return d

    def close(self) -> None:
        """Destroy the native env (idempotent). The buffers stay readable; every other call raises afterwards."""
        h, self._h = self._h, 0
        if h:
            self._lib.sts2vec_destroy(h)

    def __enter__(self) -> "VecEnv":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass

    def __repr__(self) -> str:
        state = "closed" if self.closed else f"handle {self._h}"
        return f"<sts2vec.VecEnv N={self.num_envs} threads={self._threads} {state}>"
