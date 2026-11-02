"""VecEnv: the batched Python API of CONTRACTS.md §D6 over the sts2vec C ABI."""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field

import numpy as np

from .loader import Library, load
from .schema import SIGNAL_BUFFERS, Buffers, Schema

ALL_CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")


class Sts2VecError(RuntimeError):
    pass


@dataclass(frozen=True)
class PoolSpec:
    """config.pool.fresh: ``count`` entries, character = characters[i % len], seed '<seed_prefix>-<character>-<i>'."""

    characters: tuple[str, ...] = ALL_CHARACTERS
    ascension: int = 0
    count: int = 4096
    seed_prefix: str = "TRAIN"
    act1: str = "random"

    def to_json(self) -> dict:
        return {"fresh": {"characters": list(self.characters), "ascension": self.ascension, "count": self.count,
                          "seed_prefix": self.seed_prefix, "act1": self.act1}}


_schema_cache: dict[int, Schema] = {}


def schema_of(lib: Library) -> Schema:
    s = _schema_cache.get(id(lib))
    if s is None:
        s = _schema_cache[id(lib)] = Schema.from_json(lib.schema_json())
    return s


class Pool:
    """``env.pool``: start entries (ids 0..len-1 in insertion order)."""

    def __init__(self, env: "VecEnv"):
        self._env = env

    def add_fresh(self, character: str, *, seed: str, ascension: int = 0, act1: str = "random",
                  content: str | None = None) -> int:
        spec = {"character": character, "ascension": ascension, "seed": seed, "act1": act1}
        if content is not None:
            spec["content"] = content
        b = json.dumps(spec).encode()
        return self._env._check(self._env._lib.sts2vec_pool_add_fresh(self._env._h, b, len(b)))

    def add_save(self, path: str | os.PathLike) -> int:
        b = os.fsencode(os.path.abspath(path))
        return self._env._check(self._env._lib.sts2vec_pool_add_save(self._env._h, b, len(b)))

    def __len__(self) -> int:
        return self._env._check(self._env._lib.sts2vec_pool_count(self._env._h))


class VecEnv:
    """N simulator slots stepped in one native call; observations are written in place into env-owned numpy arrays.

    ``reset``/``step`` return views valid until the next call (``copy=True`` returns copies).
    """

    def __init__(self, num_envs: int, *, threads: int = 8, step_cap: int = 3000, auto_reset: bool = True,
                 seed_text: str = "VEC", content: str = "ported_only", pool: PoolSpec | None = PoolSpec(),
                 pool_cache: int = 4096, dll: str | os.PathLike | None = None):
        self._lib = load(dll)
        self.schema = schema_of(self._lib)
        self.num_envs = int(num_envs)
        self.config = {"num_envs": self.num_envs, "threads": threads, "step_cap": step_cap, "auto_reset": auto_reset,
                       "seed_text": seed_text, "content": content, "pool_cache": pool_cache}
        if pool is not None:
            self.config["pool"] = pool.to_json()
        cfg = json.dumps(self.config).encode()
        self._h = 0
        h = self._lib.sts2vec_create(cfg, len(cfg))
        if h <= 0:
            raise Sts2VecError(f"sts2vec_create failed: {self._lib.last_error(0)}")
        self._h = h
        self.buffers: Buffers = self.schema.allocate(self.num_envs)
        self._actions = np.full(self.num_envs, -1, dtype=np.int32)
        self._mask = np.ones(self.num_envs, dtype=np.uint8)
        self._starts = np.full(self.num_envs, -1, dtype=np.int32)
        self.pool = Pool(self)

    # ---- plumbing

    @property
    def handle(self) -> int:
        return self._h

    @property
    def lib(self) -> Library:
        return self._lib

    def _check(self, rc: int) -> int:
        if rc < 0:
            raise Sts2VecError(self._lib.last_error(self._h, -1) or self._lib.last_error(0))
        return rc

    def _obs(self, copy: bool) -> dict[str, np.ndarray]:
        a = self.buffers.arrays
        return {k: (v.copy() if copy else v) for k, v in a.items() if k not in SIGNAL_BUFFERS}

    def _info(self, copy: bool) -> dict:
        ep = self.buffers["episode"]
        f = self.schema["episode"].fields
        le = self.buffers["last_episode"]
        out = {"episode_id": ep[:, f.index("episode_id")], "step_index": ep[:, f.index("step_index")],
               "start_entry": ep[:, f.index("start_entry")], "last_episode": le}
        return {k: (v.copy() if copy else v) for k, v in out.items()}

    # ---- API

    def reset(self, mask=None, start_ids=None, *, copy: bool = False) -> dict[str, np.ndarray]:
        if mask is None:
            self._mask[:] = 1
        else:
            self._mask[:] = np.asarray(mask, dtype=np.uint8)
        if start_ids is None:
            self._starts[:] = -1
        else:
            self._starts[:] = np.asarray(start_ids, dtype=np.int32)
        self._check(self._lib.sts2vec_reset(self._h, self._mask.ctypes.data, self._starts.ctypes.data, self.buffers.ptr))
        return self._obs(copy)

    def step(self, actions, *, copy: bool = False):
        self._actions[:] = np.asarray(actions, dtype=np.int32)
        self._check(self._lib.sts2vec_step(self._h, self._actions.ctypes.data, self.buffers.ptr))
        b = self.buffers
        sig = (b["reward"], b["terminated"], b["truncated"], b["status"])
        if copy:
            sig = tuple(x.copy() for x in sig)
        return (self._obs(copy), *sig, self._info(copy))

    def snapshot(self, slot: int) -> int:
        return self._check(self._lib.sts2vec_snapshot(self._h, int(slot)))

    def restore(self, slot: int, entry: int, *, copy: bool = False) -> dict[str, np.ndarray]:
        self._check(self._lib.sts2vec_restore(self._h, int(slot), int(entry), self.buffers.ptr))
        return self._obs(copy)

    def state_hash(self, slots=None) -> np.ndarray:
        s = np.arange(self.num_envs, dtype=np.int32) if slots is None else np.asarray(slots, dtype=np.int32)
        out = np.zeros(len(s), dtype=np.uint64)
        self._check(self._lib.sts2vec_state_hash(self._h, s.ctypes.data, len(s), out.ctypes.data))
        return out

    def set_threads(self, threads: int) -> None:
        self._check(self._lib.sts2vec_set_threads(self._h, int(threads)))

    def last_error(self, slot: int = -1) -> str:
        return self._lib.last_error(self._h, slot)

    def candidates(self, i: int) -> list[dict]:
        """Slot i's candidate rows decoded for debugging (kind as a RunActionKind name)."""
        spec = self.schema["cand"]
        kinds = self.schema.vocab.enums["RunActionKind"]
        n = int(self.buffers["cand_n"][i])
        rows = []
        for r in self.buffers["cand"][i, :n]:
            d = dict(zip(spec.fields, (int(x) for x in r)))
            d["kind"] = kinds[d["kind"]] if 0 <= d["kind"] < len(kinds) else d["kind"]
            rows.append(d)
        return rows

    def close(self) -> None:
        if self._h:
            self._lib.sts2vec_destroy(self._h)
            self._h = 0

    def __enter__(self) -> "VecEnv":
        return self

    def __exit__(self, *exc) -> None:
        self.close()

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass
