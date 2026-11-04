"""Start pools (CONTRACTS.md §D4): what a VecEnv's slots start their episodes from.

A pool is a list of entries in insertion order (entry ids 0, 1, …). ``PoolSpec`` describes the pool a ``VecEnv`` is
created with:

- the ``config.pool.fresh`` block (``count`` fresh runs: character ``characters[i % len]``, seed text
  ``"<seed_prefix>-<character>-<i>"``) — entries ``0 .. count-1``, created by ``sts2vec_create``;
- then the explicit ``entries`` (``FreshEntry`` / ``SaveEntry``), added in order right after create with
  ``sts2vec_pool_add_fresh`` / ``sts2vec_pool_add_save`` — entries ``count ..``.

At run time ``env.pool.add_fresh(...)`` / ``env.pool.add_save(path)`` append more, and ``env.snapshot(slot)`` appends a
clone of a live slot. Fresh templates are created lazily on first use (``RunSim.NewRun``, ~10–20 ms) and cached up to
``config.pool_cache``; every reset clones a template (~15 µs). ``env.pool.warm()`` creates them ahead of time.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import TYPE_CHECKING, Union

if TYPE_CHECKING:
    from .env import VecEnv

ALL_CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")
CONTENT = ("ported_only", "all")


@dataclass(frozen=True)
class FreshEntry:
    """One fresh run: ``RunSim.NewRun(character, ascension, seed, act1)``; ``content=None`` = the env's content."""

    character: str
    seed: str
    ascension: int = 0
    act1: str = "random"
    content: str | None = None

    def to_json(self) -> dict:
        d = {"character": self.character, "seed": str(self.seed), "ascension": int(self.ascension), "act1": self.act1}
        if self.content is not None:
            d["content"] = self.content
        return d

    def validate(self) -> None:
        if not self.character:
            raise ValueError("FreshEntry.character is required")
        if not 0 <= int(self.ascension) <= 10:
            raise ValueError(f"ascension must be 0..10 (got {self.ascension})")
        if self.content is not None and self.content not in CONTENT:
            raise ValueError(f"content must be one of {CONTENT} (got {self.content!r})")


@dataclass(frozen=True)
class SaveEntry:
    """An imported ``.save`` file (``RunImporter.FromSave``, checked; unsupported saves fail with the reasons)."""

    path: str | os.PathLike

    def validate(self) -> None:
        if not os.path.exists(self.path):
            raise FileNotFoundError(f"save file {self.path} does not exist")


Entry = Union[FreshEntry, SaveEntry]


@dataclass(frozen=True)
class PoolSpec:
    """The start pool of a new VecEnv (see the module docstring). ``count=0`` = no ``config.pool`` block."""

    characters: tuple[str, ...] = ALL_CHARACTERS
    ascension: int = 0
    count: int = 4096
    seed_prefix: str = "TRAIN"
    act1: str = "random"
    entries: tuple[Entry, ...] = field(default_factory=tuple)

    def __post_init__(self):
        # accept lists for convenience; keep the dataclass hashable
        object.__setattr__(self, "characters", tuple(self.characters))
        object.__setattr__(self, "entries", tuple(self.entries))

    @staticmethod
    def fresh(characters=ALL_CHARACTERS, *, count: int = 4096, ascension: int = 0, seed_prefix: str = "TRAIN",
              act1: str = "random") -> "PoolSpec":
        return PoolSpec(tuple(characters), ascension, count, seed_prefix, act1)

    @staticmethod
    def of(*entries: Entry) -> "PoolSpec":
        """A pool of explicit entries only."""
        return PoolSpec(count=0, entries=tuple(entries))

    @staticmethod
    def saves(*paths: str | os.PathLike) -> "PoolSpec":
        return PoolSpec.of(*(SaveEntry(p) for p in paths))

    def with_entries(self, *entries: Entry) -> "PoolSpec":
        return PoolSpec(self.characters, self.ascension, self.count, self.seed_prefix, self.act1,
                        self.entries + tuple(entries))

    @property
    def size(self) -> int:
        """Number of entries the env will hold right after creation."""
        return self.count + len(self.entries)

    def validate(self) -> None:
        if self.count < 0:
            raise ValueError(f"count must be >= 0 (got {self.count})")
        if self.count > 0:
            if not self.characters:
                raise ValueError("characters is empty")
            if not 0 <= int(self.ascension) <= 10:
                raise ValueError(f"ascension must be 0..10 (got {self.ascension})")
        for e in self.entries:
            if not isinstance(e, (FreshEntry, SaveEntry)):
                raise TypeError(f"pool entries must be FreshEntry or SaveEntry, not {type(e).__name__}")
            e.validate()

    def to_json(self) -> dict | None:
        """The ``config.pool`` value (None when ``count == 0``: the config then carries no pool block)."""
        if self.count == 0:
            return None
        return {"fresh": {"characters": list(self.characters), "ascension": int(self.ascension),
                          "count": int(self.count), "seed_prefix": self.seed_prefix, "act1": self.act1}}

    def seed_texts(self) -> list[tuple[str, str]]:
        """(character, seed text) of the fresh block's entries, in entry-id order (mirrors VecEnv's constructor)."""
        return [(self.characters[i % len(self.characters)], f"{self.seed_prefix}-{self.characters[i % len(self.characters)]}-{i}")
                for i in range(self.count)]


def make_config(num_envs: int, *, threads: int = 8, step_cap: int = 3000, auto_reset: bool = True,
                seed_text: str = "VEC", content: str = "ported_only", pool: PoolSpec | None = None,
                pool_cache: int = 4096, fast_path: bool = True, floor_step_cap: int | None = None) -> dict:
    """The ``config_json`` object of ``sts2vec_create`` (§D5); snake_case keys. Values are validated natively (unknown
    content / num_envs < 1 … make ``sts2vec_create`` fail with the reason). ``floor_step_cap=None`` leaves the key out
    (the library's default, 80; a library built before the key existed rejects it as unknown)."""
    cfg = {"num_envs": int(num_envs), "threads": int(threads), "step_cap": int(step_cap), "auto_reset": bool(auto_reset),
           "seed_text": str(seed_text), "content": content, "pool_cache": int(pool_cache), "fast_path": bool(fast_path)}
    if floor_step_cap is not None:
        cfg["floor_step_cap"] = int(floor_step_cap)
    if pool is not None:
        pool.validate()
        p = pool.to_json()
        if p is not None:
            cfg["pool"] = p
    return cfg


class Pool:
    """``env.pool``: the env's start entries (ids ``0 .. len-1`` in insertion order)."""

    def __init__(self, env: "VecEnv"):
        self._env = env
        self.added: list[tuple[int, Entry]] = []  # entries added from Python (spec.entries + add_fresh/add_save)

    def add(self, entry: Entry) -> int:
        if isinstance(entry, FreshEntry):
            return self.add_fresh(entry.character, seed=entry.seed, ascension=entry.ascension, act1=entry.act1,
                                  content=entry.content)
        if isinstance(entry, SaveEntry):
            return self.add_save(entry.path)
        raise TypeError(f"pool entries must be FreshEntry or SaveEntry, not {type(entry).__name__}")

    def add_fresh(self, character: str, *, seed: str, ascension: int = 0, act1: str = "random",
                  content: str | None = None) -> int:
        """Append a fresh-run entry; returns its id. Raises Sts2VecError (unknown character, bad ascension …)."""
        e = FreshEntry(character, str(seed), ascension, act1, content)
        b = json.dumps(e.to_json()).encode("utf-8")
        env = self._env
        rid = env._check(env._lib.sts2vec_pool_add_fresh(env._live_handle(), b, len(b)), "pool_add_fresh")
        self.added.append((rid, e))
        return rid

    def add_save(self, path: str | os.PathLike) -> int:
        """Append an imported save; returns its id. Unsupported saves raise Sts2VecError with the importer's reasons."""
        p = os.path.abspath(os.fspath(path))
        b = p.encode("utf-8")
        env = self._env
        rid = env._check(env._lib.sts2vec_pool_add_save(env._live_handle(), b, len(b)), "pool_add_save")
        self.added.append((rid, SaveEntry(p)))
        return rid

    def warm(self, max_entries: int | None = None) -> int:
        """Create fresh entries' start templates now rather than at their first reset (``sts2vec_pool_warm``).

        Warms up to ``max_entries`` fresh entries that have no template yet (None = all of them), in id order and only
        while the template cache (``pool_cache``) has room, on the env's worker threads. Returns how many templates this
        call created (0 when everything is warm or the cache is full). Starts do not change: a warm template is the
        ``RunSim.NewRun`` the entry's first reset would create; only that cold cost (~16 ms per entry) moves out of
        reset/step."""
        if max_entries is not None and int(max_entries) < 0:
            raise ValueError(f"max_entries must be >= 0 or None for all (got {max_entries})")
        env = self._env
        h = env._live_handle()
        fn = env._lib.require("sts2vec_pool_warm")
        return env._check(fn(h, -1 if max_entries is None else int(max_entries)), "pool_warm")

    def __len__(self) -> int:
        env = self._env
        return env._check(env._lib.sts2vec_pool_count(env._live_handle()), "pool_count")

    count = property(__len__)

    def __repr__(self) -> str:
        try:
            return f"<sts2vec.Pool {len(self)} entries>"
        except Exception:
            return "<sts2vec.Pool (closed)>"
