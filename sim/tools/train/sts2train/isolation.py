"""Isolation of the training starts from the frozen evaluation seed sets (holdouts) and the blind-set gate.

A seed text names a game through its 64-bit run seed (``sts2vec.protocol.run_seed``: XxHash64 of the UTF-8 text,
``RngSeeds.RunSeed``; every run RNG stream, the act selection and the player streams derive from it), so isolation is
checked on the run seed as well as on the (character, seed text, ascension) triple: a training seed whose run seed
equals a holdout game's plays that game's map and RNG whatever its character or ascension.

``IsolationGuard.from_seedsets()`` loads every frozen seed set under ``sts2vec.protocol.seedset_dirs()`` (the
committed ``sim/tools/eval/seedsets`` plus ``$STS2VEC_SEEDSETS``) whose role is in ``HOLDOUT_ROLES``, validated (a
frozen set's sha256 must match its content; a file that does not validate is an error, never skipped), plus the
run's own eval seed set. Then:

- ``--start-mode stream`` (``sts2train.seedstream``): the generator skips every stream index whose seed collides (run
  seed or triple); the skips are counted (``stream_skipped_total`` in metrics.jsonl, ``isolation.collisions_skipped``
  in config.json / summary.json). Stream seed texts carry '-' (``SP0-SILENT-17``), which no game-form seed set seed
  has, so a skip needs a 64-bit hash collision (or a seed set built on the stream's own texts).
- ``--start-mode pool`` (legacy): the fresh pool's seed texts (``TRAIN<env_seed>-<CHARACTER>-<i>``, i < --pool) are
  checked before the env is built and the run is REFUSED (``IsolationError``) when any of them collides: ``--seed 0
  --pool 4608`` would swallow silent-a10-ab-v1 (TRAIN0-SILENT-4096..4607). Start saves are human games with their own
  12-character run seeds and are not checked here (they are not part of the new mainline: --start-mode stream refuses
  them).

config.json records ``isolation``: the checked sets (id, name, role, games, sha256), the number of holdout games and
distinct run seeds, the mode, the number of start seeds checked (pool) or the skip count (stream), ``refused`` false
(a refused run raises before the env or config.json exists; the error names the colliding seeds).

Blind sets (role ``blind``, e.g. silent-a10-blind-v1): every evaluation path ends in ``sts2vec.evaluate.evaluate``,
which refuses a seed set holding a blind game unless ``allow_blind`` and a ``purpose`` are given, and then appends
{utc, weights_hash, seedset, purpose, blind_sets} to ``<out>/blind-access.jsonl`` (``protocol.require_blind_access``,
re-exported here). The trainer and report-card check before any game is played and log into their own directory too.

Exposure of the older sets (Q3 audit, 2026-11-04): silent-a10-test-v1 (H1A10SI001..100) was computed and seen next to
every dev-128 selection and used in post-cycle analysis, so its role is now ``dev2`` (the checksum covers only
ascension / content / games, so the frozen sha256 is unchanged); silent-a10-ab-v1 (TRAIN0-SILENT-4096..4607) was
evaluated on u2225 and stays an A/B development set (role ``ab``). Both remain holdouts for training. ``EXPOSED``
lists them for reports.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from sts2vec import protocol as PG
from sts2vec.protocol import BlindSetError, blind_overlap, require_blind_access, run_seed  # noqa: F401 (re-exported)

# Every role of an evaluation seed set: nothing that scores a policy may be trained on. A role outside this list (a
# diagnostic set drawn from the training pool on purpose) is not a holdout.
HOLDOUT_ROLES = ("target", "dev", "test", "dev2", "ab", "blind", "official", "smoke")
EXPOSED = {
    "silent-a10-test-v1": "role test -> dev2 on 2026-11-04: seen next to every dev-128 selection and in the "
                          "post-cycle analysis (Q3 audit); a second dev set, not a one-shot holdout",
    "silent-a10-ab-v1": "evaluated on u2225 (report-card, 2026-11-04 11:15 EST): an A/B development set",
}


class IsolationError(ValueError):
    """The legacy start pool shares games with a frozen evaluation seed set (or a holdout set does not load)."""


@dataclass
class IsolationGuard:
    """Run seeds and (character, seed text, ascension) triples of the holdout games (``from_seedsets``)."""

    sets: list[dict] = field(default_factory=list)            # {"id", "name", "role", "games", "sha256", "path"}
    by_seed: dict[int, tuple[str, str]] = field(default_factory=dict)   # run seed -> (set id, seed text)
    triples: dict[tuple[str, str, int], str] = field(default_factory=dict)

    @classmethod
    def from_seedsets(cls, roles=HOLDOUT_ROLES, extra=()) -> "IsolationGuard":
        """Every frozen seed set under ``seedset_dirs()`` with a role in ``roles``, plus ``extra`` seed sets (names,
        ids, paths or dicts; e.g. the run's eval seed set, whatever its role)."""
        g = cls()
        try:
            found = PG.frozen_seedsets(roles)
        except PG.SeedSetError as e:
            raise IsolationError(f"a seed set under {[str(d) for d in PG.seedset_dirs()]} does not validate, so "
                                 f"isolation cannot be checked: {e}") from None
        if not found:                   # a box without the seed sets must not train "isolated" from nothing
            dirs = [str(d) for d in PG.seedset_dirs()]
            raise IsolationError(f"no frozen seed set with a role in {roles} under {dirs} (the Colab bundle ships "
                                 "them in seedsets/ via STS2VEC_SEEDSETS): isolation cannot be checked")
        for path, data in found:
            g.add(data, path=str(path))
        for x in extra or ():
            if not x:
                continue
            data = PG.validate_seedset(dict(x)) if isinstance(x, dict) else PG.load_seedset(str(x))
            if all(s["sha256"] != PG.checksum(data) for s in g.sets):
                g.add(data, path=None if isinstance(x, dict) else str(x))
        return g

    def add(self, data: dict, path: str | None = None) -> None:
        sid = data.get("id") or data.get("name") or "<inline>"
        asc = int(data["ascension"])
        for game in data["games"]:
            self.by_seed.setdefault(run_seed(game["seed"]), (sid, game["seed"]))
            self.triples.setdefault((game["character"], game["seed"], asc), sid)
        self.sets.append({"id": sid, "name": data.get("name"), "role": data.get("role"), "games": len(data["games"]),
                          "sha256": PG.checksum(data), "path": path})

    def collision(self, character: str, seed_text: str, ascension: int) -> str | None:
        """Why a training start (character, seed text, ascension) may not be used: None when it is no holdout game."""
        sid = self.triples.get((character, seed_text, int(ascension)))
        if sid is not None:
            return f"{character} {seed_text} A{ascension} is a game of {sid}"
        rs = run_seed(seed_text)
        hit = self.by_seed.get(rs)
        if hit is not None:
            return f"{seed_text} has the run seed {rs:#018x} of {hit[0]} game {hit[1]}"
        return None

    def check(self, starts) -> list[dict]:
        """Collisions of (character, seed text, ascension) starts: [{"character", "seed", "ascension", "reason",
        "set"}]."""
        out = []
        for ch, text, asc in starts:
            why = self.collision(ch, text, asc)
            if why is not None:
                sid = self.triples.get((ch, text, int(asc))) or self.by_seed[run_seed(text)][0]
                out.append({"character": ch, "seed": text, "ascension": int(asc), "reason": why, "set": sid})
        return out

    def record(self) -> dict:
        """The config.json ``isolation`` block's common part."""
        return {"checked_sets": [dict(s) for s in self.sets], "holdout_games": len(self.triples),
                "holdout_run_seeds": len(self.by_seed), "roles": list(HOLDOUT_ROLES), "exposed": dict(EXPOSED)}


def legacy_pool_starts(cfg) -> list[tuple[str, str, int]]:
    """(character, seed text, ascension) of the fresh entries a legacy (``--start-mode pool``) run puts into its
    start pool: ``TRAIN<env_seed>-<characters[i % n]>-<i>`` for i < pool (none at ``save_mix`` 1 with start saves)."""
    from .startpool import uses_start_saves

    n = 0 if (uses_start_saves(cfg) and float(cfg.save_mix) >= 1.0) else int(cfg.pool)
    chars = tuple(cfg.characters)
    prefix = f"TRAIN{cfg.env_seed}"
    return [(chars[i % len(chars)], f"{prefix}-{chars[i % len(chars)]}-{i}", int(cfg.ascension)) for i in range(n)]


def check_legacy_pool(cfg, guard: IsolationGuard) -> dict:
    """Refuse a legacy pool that holds a holdout game (``IsolationError`` naming them); -> the config.json record."""
    starts = legacy_pool_starts(cfg)
    hits = guard.check(starts)
    if hits:
        sets = sorted({h["set"] for h in hits})
        first = "; ".join(h["reason"] for h in hits[:3])
        raise IsolationError(
            f"refusing to train: {len(hits)} of the {len(starts)} start-pool seeds ({starts[0][1]} .. "
            f"{starts[-1][1]}) are games of frozen evaluation seed sets ({', '.join(sets)}): {first}"
            + (" ..." if len(hits) > 3 else "") + ". Use a smaller --pool, another --env-seed, or --start-mode "
            "stream (fresh seeds that skip every holdout)")
    return {**guard.record(), "mode": "pool", "start_seeds_checked": len(starts), "collisions": 0,
            "collisions_skipped": 0, "refused": False}


def check_blind_eval(seedset, *, allow_blind: bool, purpose: str | None, log_dir=None,
                     weights_hash: str | None = None) -> dict | None:
    """``protocol.require_blind_access`` on a seed set given by name / id / path / dict (before any game is played);
    the access line goes to ``log_dir/blind-access.jsonl`` when allowed."""
    data = PG.validate_seedset(dict(seedset)) if isinstance(seedset, dict) else PG.load_seedset(str(seedset))
    return require_blind_access(data, allow_blind=allow_blind, purpose=purpose,
                                out_dir=None if log_dir is None else Path(log_dir), weights_hash=weights_hash)
