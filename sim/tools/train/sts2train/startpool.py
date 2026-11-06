"""Start pools that mix real game saves with fresh starts (``sts2train train --start-saves DIR --save-mix R``).

A save (the game's ``current_run.save``, schema 20, v0.111.0; e.g. the recorder's copies collected by
``scripts/train/collect_start_saves.py``) starts an episode mid-run: Act 2/3 rooms, a human-built deck. sts2vec imports
it with ``env.pool.add_save`` (``RunImporter.FromSave``, checked: an unsupported save fails with the reasons). The
three save points of the game import as: entering a map point (the room's start, e.g. a combat's first turn; the sim's
``total_floor`` = the save's history + 1), after a won combat (the Rewards screen of that combat) and after the act's
Ancient (the event's last page). A save keeps its own character and ascension; ``--ascension`` only applies to fresh
entries.

Mixing (deterministic given ``seed`` and the saves' contents): a slot's k-th episode starts from pool entry
``(slot_seed + k) mod len(pool)`` (sim/VEC.md, start sequence), so an entry's weight is its multiplicity and the
fraction of episode starts from saves is the fraction of save entries. ``plan_layout`` puts ``fresh`` fresh entries
(the same characters and seed texts as ``config.pool.fresh``: ``"<seed_prefix>-<character>-<i>"``) and ``c`` copies of
every usable save, ``c = max(1, round(fresh * R / (1 - R) / saves))``, into one pool and interleaves them evenly
(save at position j iff ``floor((j + 1) * S / P) > floor(j * S / P)``), so every window of consecutive entries, and so
every slot's run of episodes, holds the ratio. Copy 0 of a save is the imported save itself; further copies are
``env.clone_determinized`` of it (``save_copies="determinized"``: the same player view, hidden state - draw order,
every RNG stream, relic bag and unvisited event order - resampled from a seed derived from (seed, save sha256, copy))
or plain re-imports (``"exact"``: every start of that save replays the same RNG). Rounds of copies visit the saves in
a seeded order (sha256 of seed, round and the save's sha256), not in file order.

Bookkeeping: ``EpisodeRewards`` records the pool entry, floor, act and protocol progress of each episode's first
observation; ``split_stats`` reports fresh- and save-started episodes separately (``fresh_*`` / ``save_*`` keys of
``metrics.jsonl``), and with ``--harvest`` (``sts2train.harvest``) the episodes started from self-anchors (pool ids
``>= harvest.base``) as a third group (``harvest_*``), so ``fresh_*`` keeps meaning "started from a fresh entry". A
save-started episode's protocol ``progress`` is the run's absolute progress at its end (what the evaluator would
score); its return is ``progress - start_progress`` (+ the win bonus, + ``--boss-bonus`` per act-2+ boss kill),
reported as ``save_progress_gain_mean``.

Stream mode (wave 17, ``--start-mode stream --start-saves DIR --save-mix M``): the pool holds no fresh entries; the
saves and their copies (as many save entries as pool mode would make for ``--pool`` fresh entries at mix M:
``c = max(1, round(pool * M / (1 - M) / saves))`` copies of each) are built into the empty pool first (ids 0 .. K-1;
``StartPool.stream_share`` = M) and ``seedstream.StreamEnv.attach_saves`` starts a finished slot from a uniformly
drawn save entry with probability M (so M is the fraction of episode starts), else from an anchor / the stream.
Evaluation never uses saves (fresh seed sets only), so the eval readout is unaffected; a save start's protocol
progress starts at its save's ``start_progress`` (the run's absolute progress there).
"""

from __future__ import annotations

import hashlib
import json
import math
import os
from collections import Counter
from dataclasses import asdict, dataclass, field
from pathlib import Path

import numpy as np
from sts2vec import STATUS, VecEnv
from sts2vec.errors import Sts2VecError
from sts2vec.pool import ALL_CHARACTERS

from .harvest import KINDS
from .objective import state_progress

FRESH, SAVE = 0, 1
COPY_MODES = ("determinized", "exact")
ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
MANIFEST = "manifest.json"          # written by scripts/train/collect_start_saves.py (optional; provenance only)


def save_seed(path) -> str | None:
    """The run seed text of a save file (its ``rng.seed``), or None when it cannot be read."""
    try:
        d = json.loads(Path(path).read_text(encoding="utf-8-sig"))
        seed = (d.get("rng") or {}).get("seed")
        return str(seed) if seed else None
    except (OSError, ValueError, AttributeError):
        return None


def find_saves(root: str | os.PathLike) -> list[Path]:
    """Every ``*.save`` file under ``root`` (recursively), sorted by relative path."""
    root = Path(root)
    if not root.is_dir():
        raise FileNotFoundError(f"--start-saves {root}: not a directory")
    return sorted((p for p in root.rglob("*.save") if p.is_file()), key=lambda p: p.relative_to(root).as_posix())


def _sha256(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def _key(*parts) -> bytes:
    return hashlib.sha256(":".join(str(p) for p in parts).encode("utf-8")).digest()


def copy_seed(seed: int, sha256: str, copy: int) -> int:
    """uint64 seed of a save's determinized copy (``env.clone_determinized``)."""
    return int.from_bytes(_key("sts2train-save-copy", seed, sha256, copy)[:8], "little")


@dataclass(frozen=True)
class SaveStart:
    """A usable save and its first observation in the sim."""

    file: str               # path relative to the saves directory
    path: str               # absolute path
    sha256: str
    bytes: int
    character: str
    ascension: int
    act: int                # 1-based
    floor: int              # total_floor of the first observation
    act_floor: int
    act_rooms: int
    phase: str              # RunPhase of the first observation (Combat, Rewards, Event, Shop, RestSite, Treasure, ...)
    hp: int
    max_hp: int
    gold: int
    deck_n: int
    relic_n: int
    start_progress: float   # protocol progress of the first observation
    pending_prompt: bool    # the import stopped at a prompt: copies are re-imports (sim/VEC.md, pending load)
    provenance: dict = field(default_factory=dict)   # session / seed / game version from the directory's manifest


def _manifest_rows(root: Path) -> dict[str, dict]:
    """sha256 -> provenance fields of ``root/manifest.json`` (collect_start_saves.py), if there is one."""
    p = root / MANIFEST
    if not p.is_file():
        return {}
    try:
        rows = json.loads(p.read_text(encoding="utf-8")).get("saves") or []
    except (OSError, ValueError, AttributeError):
        return {}
    keep = ("session", "source", "run_seed", "game_version", "entry_point", "room_type")
    return {r["sha256"]: {k: r[k] for k in keep if r.get(k) is not None} for r in rows if r.get("sha256")}


def probe_saves(root: str | os.PathLike, *, content: str = "ported_only", characters=None,
                dll=None) -> tuple[list[SaveStart], list[dict], int]:
    """Import every ``*.save`` under ``root`` once (a 1-slot env with the training env's content filter) and read the
    first observation. Returns (usable saves in path order, de-duplicated by content; skipped ``{"file", "reason"}``
    rows; number of files found). Skipped: unreadable / unsupported saves (the importer's reasons), a first observation
    with SIM_ERROR / CONTENT_GAP or no legal action, a character outside ``characters``, duplicates."""
    root = Path(root).resolve()
    files = find_saves(root)
    prov = _manifest_rows(root)
    keep_chars = {c.upper() for c in characters} if characters else None
    ok: list[SaveStart] = []
    skipped: list[dict] = []
    seen: dict[str, str] = {}
    with VecEnv(1, threads=1, pool=None, content=content, auto_reset=False, seed_text="SAVEPROBE", dll=dll) as env:
        S = env.schema
        gi_col = {f: S.col("global_i", f) for f in ("phase", "character", "ascension", "act_index", "total_floor",
                                                     "act_floor", "hp", "max_hp", "gold", "deck_n", "relic_n",
                                                     "decision_kind")}
        rooms_col = S.col("aux", "act_rooms")
        phases = S.vocab.enums["RunPhase"]
        chars = S.vocab.lists["characters"]
        prompt_phase = phases.index_of("Prompt")
        for p in files:
            rel = p.relative_to(root).as_posix()
            try:
                sha, size = _sha256(p), p.stat().st_size
            except OSError as e:
                skipped.append({"file": rel, "reason": f"unreadable: {e}"})
                continue
            if sha in seen:
                skipped.append({"file": rel, "reason": f"duplicate of {seen[sha]}"})
                continue
            seen[sha] = rel
            try:
                entry = env.pool.add_save(p)
            except Sts2VecError as e:
                reason = str(e).split("is not supported: ", 1)[-1] if "is not supported: " in str(e) else str(e)
                skipped.append({"file": rel, "reason": reason[:500]})
                continue
            obs = env.restore(0, entry)
            st = int(env.status[0])
            if st & ERR_BITS:
                skipped.append({"file": rel, "reason": "first observation failed: "
                                + (env.last_error(0).splitlines() or ["?"])[0][:300]})
                continue
            if int(obs["cand_n"][0]) <= 0:
                skipped.append({"file": rel, "reason": "no legal action at the first observation"})
                continue
            g = {f: int(obs["global_i"][0, c]) for f, c in gi_col.items()}
            character = chars.name(g["character"])
            if keep_chars is not None and character not in keep_chars:
                skipped.append({"file": rel, "reason": f"character {character} not in --characters"})
                continue
            act_rooms = int(obs["aux"][0, rooms_col])
            ok.append(SaveStart(
                file=rel, path=str(p), sha256=sha, bytes=size, character=character, ascension=g["ascension"],
                act=g["act_index"] + 1, floor=g["total_floor"], act_floor=g["act_floor"], act_rooms=act_rooms,
                phase=phases.name(g["phase"]), hp=g["hp"], max_hp=g["max_hp"], gold=g["gold"], deck_n=g["deck_n"],
                relic_n=g["relic_n"],
                start_progress=state_progress(g["act_index"], g["total_floor"], g["act_floor"], act_rooms, g["ascension"]),
                pending_prompt=g["decision_kind"] == prompt_phase, provenance=prov.get(sha, {})))
    return ok, skipped, len(files)


# ------------------------------------------------------------------------------------------------ layout


@dataclass
class Layout:
    """Pool entry ``j`` holds ``kinds[j]`` (FRESH / SAVE): fresh entry ``index[j]`` or copy ``copy[j]`` of save
    ``index[j]``."""

    kinds: np.ndarray
    index: np.ndarray
    copy: np.ndarray
    fresh: int
    saves: int
    copies: int
    mix: float

    @property
    def size(self) -> int:
        return len(self.kinds)

    @property
    def save_entries(self) -> int:
        return int(np.count_nonzero(self.kinds == SAVE))

    @property
    def mix_effective(self) -> float:
        return self.save_entries / self.size if self.size else 0.0


def plan_layout(fresh: int, saves: int, mix: float, seed: int = 0, sha256s=None) -> Layout:
    """The interleaved pool layout (module docstring). ``mix`` = requested fraction of episode starts from saves
    (0..1). At ``mix == 1`` there are no fresh entries and ``fresh`` sets the number of save entries instead (every
    save at least once). ``sha256s`` (one per save) orders each round of copies; default: the save indices."""
    if not 0.0 <= mix <= 1.0 or math.isnan(mix):
        raise ValueError(f"save_mix must be in [0, 1] (got {mix})")
    if fresh < 0 or saves < 0:
        raise ValueError("fresh and saves must be >= 0")
    if saves == 0 or mix == 0.0:
        n_fresh, copies = fresh, 0
    elif mix == 1.0:
        n_fresh, copies = 0, max(1, round(fresh / saves))
    else:
        n_fresh, copies = fresh, max(1, round(fresh * mix / (1.0 - mix) / saves))
    ns = copies * saves
    total = n_fresh + ns
    if total == 0:
        raise ValueError("the start pool would be empty (fresh = 0 and no saves)")
    j = np.arange(total, dtype=np.int64)
    is_save = ((j + 1) * ns) // total - (j * ns) // total > 0
    kinds = np.where(is_save, SAVE, FRESH).astype(np.int8)
    index = np.zeros(total, dtype=np.int32)
    copy = np.zeros(total, dtype=np.int32)
    index[~is_save] = np.arange(n_fresh, dtype=np.int32)
    keys = [str(i) for i in range(saves)] if sha256s is None else [str(s) for s in sha256s]
    if len(keys) != saves:
        raise ValueError("sha256s must have one entry per save")
    order = []
    for r in range(copies):
        perm = sorted(range(saves), key=lambda i: _key("sts2train-save-round", seed, r, keys[i]))
        order += [(i, r) for i in perm]
    if order:
        index[is_save] = [i for i, _ in order]
        copy[is_save] = [r for _, r in order]
    return Layout(kinds, index, copy, n_fresh, saves, copies, float(mix))


# ------------------------------------------------------------------------------------------------ the pool


@dataclass
class StartPool:
    """What ``build_start_pool`` put into the training env: the layout, the usable saves and the probe's report."""

    root: str
    layout: Layout
    saves: list[SaveStart]
    skipped: list[dict]
    files: int
    copies_mode: str
    build_s: float = 0.0
    stream_share: float | None = None   # stream mode: probability that a start is a save start (layout: saves only)
    kinds: np.ndarray = field(init=False)

    def __post_init__(self):
        self.kinds = self.layout.kinds

    def is_save(self, start_entry: np.ndarray | int) -> np.ndarray:
        e = np.asarray(start_entry, dtype=np.int64)
        ok = (e >= 0) & (e < len(self.kinds))
        return ok & (self.kinds[np.clip(e, 0, len(self.kinds) - 1)] == SAVE)

    def histograms(self) -> dict:
        def h(key):
            return dict(sorted(Counter(key(s) for s in self.saves).items()))
        return {"start_floor": h(lambda s: s.floor), "start_act": h(lambda s: s.act),
                "start_phase": h(lambda s: s.phase), "ascension": h(lambda s: s.ascension),
                "character": h(lambda s: s.character)}

    def summary(self, brief: bool = False) -> dict:
        L = self.layout
        out = {"saves_dir": self.root, "save_mix": L.mix, "save_mix_effective": round(L.mix_effective, 6),
               "save_copies": self.copies_mode, "pool_entries": L.size, "fresh_entries": L.fresh,
               "save_entries": L.save_entries, "unique_saves": len(self.saves), "copies_per_save": L.copies,
               "files_found": self.files, "skipped": len(self.skipped),
               "start_progress_mean": (round(float(np.mean([s.start_progress for s in self.saves])), 6)
                                       if self.saves else None),
               "histograms_unique_saves": self.histograms(), "build_s": round(self.build_s, 2)}
        if self.stream_share is not None:
            out.update(save_mix=self.stream_share, save_mix_effective=None, start_mode="stream",
                       save_start_rule="stream: a starting slot draws u ~ U(0,1); u < save_mix -> a uniformly drawn "
                                       "save entry, else an anchor (--harvest) / the next stream seed")
        if not brief:
            out["skipped_files"] = self.skipped
            out["saves"] = [{k: v for k, v in asdict(s).items() if k != "path"} for s in self.saves]
            out["layout"] = ("entry j is a save iff floor((j+1)*S/P) > floor(j*S/P); fresh entries keep the "
                             "config.pool.fresh characters and seed texts; save copies in seeded rounds")
        return out

    def describe(self) -> list[str]:
        L, h = self.layout, self.histograms()
        if self.stream_share is not None:
            lines = [f"start saves (stream mode): {L.save_entries} save entries ({len(self.saves)} saves x {L.copies} "
                     f"{'copy' if L.copies == 1 else 'copies'}, {self.copies_mode}); a starting slot takes a uniformly "
                     f"drawn save entry with probability {self.stream_share:g}, else an anchor / the next stream "
                     f"seed; built in {self.build_s:.1f} s"]
        else:
            lines = [f"start pool: {L.fresh} fresh + {L.save_entries} save entries = {L.size} ({len(self.saves)} saves "
                     f"x {L.copies} {'copy' if L.copies == 1 else 'copies'}, {self.copies_mode}); save starts "
                     f"{L.mix_effective:.4f} of episode starts (requested {L.mix:g}); built in {self.build_s:.1f} s"]
        lines.append(f"start saves: {self.files} files under {self.root}, {len(self.saves)} usable, "
                     f"{len(self.skipped)} skipped")
        reasons = Counter(r["reason"] for r in self.skipped)
        lines += [f"  skipped x{n}: {r}" for r, n in reasons.most_common()]
        lines.append("  start floor (unique saves): " + " ".join(f"{k}:{v}" for k, v in h["start_floor"].items()))
        lines.append(f"  start act {h['start_act']}, phase {h['start_phase']}, ascension {h['ascension']}, "
                     f"character {h['character']}")
        return lines


def build_start_pool(env: VecEnv, root, *, mix: float = 0.5, copies_mode: str = "determinized", fresh: int = 2048,
                     characters=ALL_CHARACTERS, ascension: int = 0,
                     seed_prefix: str = "TRAIN", act1: str = "random", seed: int = 0, probe=None) -> StartPool:
    """Fill the (empty) pool of ``env`` with the interleaved layout of fresh entries and the usable saves under
    ``root``. ``probe`` = a ``probe_saves`` result to reuse. Raises when no save is usable."""
    import time

    if copies_mode not in COPY_MODES:
        raise ValueError(f"save_copies {copies_mode!r}: expected one of {COPY_MODES}")
    if len(env.pool) != 0:
        raise ValueError(f"build_start_pool needs an env with an empty pool (it has {len(env.pool)} entries)")
    t0 = time.monotonic()
    saves, skipped, files = probe if probe is not None else probe_saves(
        root, content=env.config.get("content", "ported_only"), characters=characters)
    if not saves:
        reasons = Counter(r["reason"] for r in skipped).most_common(3)
        raise ValueError(f"--start-saves {root}: no usable save among {files} files (skipped: {reasons})")
    layout = plan_layout(fresh, len(saves), mix, seed, [s.sha256 for s in saves])
    chars = tuple(characters)
    base: dict[int, int] = {}
    pool = env.pool
    clone = None
    if copies_mode == "determinized" and layout.copies > 1:
        clone = env.clone_determinized
        try:
            env.lib.require("sts2vec_clone_determinized")
        except Sts2VecError as e:
            raise Sts2VecError(f"{e} (or pass --save-copies exact)") from None
    for j in range(layout.size):
        k, i, c = int(layout.kinds[j]), int(layout.index[j]), int(layout.copy[j])
        if k == FRESH:
            ch = chars[i % len(chars)]
            eid = pool.add_fresh(ch, seed=f"{seed_prefix}-{ch}-{i}", ascension=ascension, act1=act1)
        else:
            s = saves[i]
            if c == 0 or clone is None or s.pending_prompt:
                eid = pool.add_save(s.path)
                if c == 0:
                    base[i] = eid
            else:
                env.restore(0, base[i])
                eid = clone(0, copy_seed(seed, s.sha256, c))
        if eid != j:
            raise RuntimeError(f"start pool entry {j} got id {eid}: the env's pool was not empty or changed")
    return StartPool(str(Path(root).resolve()), layout, saves, skipped, files, copies_mode,
                     build_s=time.monotonic() - t0)


# ------------------------------------------------------------------------------------------------ metrics


def _m(xs) -> float | None:
    return float(np.mean(xs)) if len(xs) else None


def _group_stats(name: str, g: list[dict]) -> dict:
    n, wins = len(g), sum(1 for e in g if e["win"])
    return {f"{name}_episodes": n, f"{name}_wins": wins, f"{name}_win_rate": wins / n if n else None,
            f"{name}_progress_mean": _m([e["progress"] for e in g if e.get("progress") is not None]),
            f"{name}_floor_mean": _m([e["floor"] for e in g]),
            f"{name}_return_mean": _m([e["return"] for e in g if "return" in e]),
            f"{name}_steps_mean": _m([e["steps"] for e in g if "steps" in e])}


def _cleared(e: dict) -> bool:
    """The episode got past its first room: a win, or a final floor above its start floor (``harvest_clear_rate``)."""
    return bool(e["win"]) or ("start_floor" in e and int(e["floor"]) > int(e["start_floor"]))


def split_stats(eps: list[dict], pool: StartPool | None, harvest=None) -> dict:
    """``fresh_*`` / ``save_*`` (and with `harvest`, a ``harvest.Harvester``, ``harvest_*``) keys of a metrics row
    from finished-episode dicts (``EpisodeRewards.step``); {} when there are neither saves nor anchors. An episode is
    an anchor start when its ``start_entry`` is ``>= harvest.base``, a save start when the pool's layout says so, a
    fresh start otherwise (without saves: every entry below ``base``), so ``fresh_*`` stays comparable with a run
    without saves / anchors. ``save_progress_mean`` is the absolute protocol progress at the end of save-started
    episodes, ``save_progress_gain_mean`` = end - start progress (their return without the win bonus); the same for
    ``harvest_*``, plus ``harvest_start_share`` (anchor starts / finished episodes), and per ring kind
    ``harvest_episodes_<kind>`` / ``harvest_clear_rate_<kind>`` (share of those episodes that got past their first
    room: a win or a final floor above the start floor)."""
    if pool is None and harvest is None:
        return {}
    starts = np.asarray([e.get("start_entry", -1) for e in eps], dtype=np.int64)
    anchor = harvest.is_anchor(starts) if harvest is not None else np.zeros(len(eps), dtype=bool)
    is_save = (pool.is_save(starts) if pool is not None and len(eps) else np.zeros(len(eps), dtype=bool)) & ~anchor
    groups = {"fresh": [e for e, s, a in zip(eps, is_save, anchor) if not s and not a],
              "save": [e for e, s in zip(eps, is_save) if s]}
    if harvest is not None:
        groups["harvest"] = [e for e, a in zip(eps, anchor) if a]
    out: dict = {}
    for name, g in groups.items():
        out.update(_group_stats(name, g))
    s = groups["save"]
    acts: dict[int, list] = {}
    for e in s:
        acts.setdefault(int(e.get("start_act", 0)), []).append(e)
    out.update({
        "save_start_progress_mean": _m([e["start_progress"] for e in s if "start_progress" in e]),
        "save_progress_gain_mean": _m([e["progress_gain"] for e in s if e.get("progress_gain") is not None]),
        "save_start_floor_mean": _m([e["start_floor"] for e in s if "start_floor" in e]),
        "save_floors_gained_mean": _m([e["floor"] - e["start_floor"] for e in s if "start_floor" in e]),
        "save_act_reached": dict(sorted(Counter(e["act"] for e in s).items())),
        "save_episodes_by_start_act": {a: len(v) for a, v in sorted(acts.items())},
        "save_gain_by_start_act": {a: _m([e["progress_gain"] for e in v if e.get("progress_gain") is not None])
                                   for a, v in sorted(acts.items())},
    })
    if harvest is not None:
        h = groups["harvest"]
        out.update({
            "harvest_start_share": len(h) / len(eps) if eps else None,
            "harvest_start_progress_mean": _m([e["start_progress"] for e in h if "start_progress" in e]),
            "harvest_progress_gain_mean": _m([e["progress_gain"] for e in h if e.get("progress_gain") is not None]),
            "harvest_start_floor_mean": _m([e["start_floor"] for e in h if "start_floor" in e]),
            "harvest_floors_gained_mean": _m([e["floor"] - e["start_floor"] for e in h if "start_floor" in e]),
            "harvest_act_reached": dict(sorted(Counter(e["act"] for e in h).items())),
        })
        codes = harvest.kind_of([e["start_entry"] for e in h]) if h else np.zeros(0, dtype=np.int8)
        for k in harvest.kinds:
            sel = [e for e, c in zip(h, codes) if int(c) == KINDS.index(k)]
            out[f"harvest_episodes_{k}"] = len(sel)
            out[f"harvest_clear_rate_{k}"] = _m([float(_cleared(e)) for e in sel])
    return out


def recent_sums(eps: list[dict], pool: StartPool | None, harvest=None) -> dict:
    """Per-update sums for the status file's rolling window (``recent_view``)."""
    if pool is None and harvest is None:
        return {}
    st = split_stats(eps, pool, harvest)
    out = {}
    for name in ("fresh", "save") + (("harvest",) if harvest is not None else ()):
        n = st[f"{name}_episodes"]
        out[f"{name}_episodes"] = n
        out[f"{name}_wins"] = st[f"{name}_wins"]
        out[f"{name}_progress_sum"] = (st[f"{name}_progress_mean"] or 0.0) * n
    n = st["save_episodes"]
    out["save_gain_sum"] = (st["save_progress_gain_mean"] or 0.0) * n
    if harvest is not None:
        n = st["harvest_episodes"]
        out["harvest_gain_sum"] = (st["harvest_progress_gain_mean"] or 0.0) * n
    return out


def recent_view(recent) -> dict:
    """fresh / save (/ harvest) progress, win rate (and save / harvest progress gain, the anchor start share) over a
    window of ``recent_sums`` rows."""
    rows = [r for r in recent if "fresh_episodes" in r]
    if not rows:
        return {}
    names = ("fresh", "save") + (("harvest",) if any("harvest_episodes" in r for r in rows) else ())
    out = {}
    for name in names:
        n = sum(r.get(f"{name}_episodes", 0) for r in rows)
        out[f"{name}_episodes"] = n
        out[f"{name}_progress_mean"] = sum(r.get(f"{name}_progress_sum", 0.0) for r in rows) / n if n else None
        out[f"{name}_win_rate"] = sum(r.get(f"{name}_wins", 0) for r in rows) / n if n else None
    n = out["save_episodes"]
    out["save_progress_gain_mean"] = sum(r["save_gain_sum"] for r in rows) / n if n else None
    if "harvest_episodes" in out:
        n = out["harvest_episodes"]
        out["harvest_progress_gain_mean"] = sum(r.get("harvest_gain_sum", 0.0) for r in rows) / n if n else None
        tot = sum(out[f"{x}_episodes"] for x in names)
        out["harvest_start_share"] = n / tot if tot else None
    return out


# ------------------------------------------------------------------------------------------------ trainer hook


def uses_start_saves(cfg) -> bool:
    """True when a TrainConfig asks for save starts (``start_saves`` set and ``save_mix > 0``)."""
    return bool(getattr(cfg, "start_saves", None)) and float(getattr(cfg, "save_mix", 0.0)) > 0.0


def start_pool_from_config(env: VecEnv, cfg, log=print) -> StartPool | None:
    """The trainer's start pool: None (``config.pool.fresh``, already in ``env``) without ``start_saves``, else
    ``build_start_pool`` into ``env`` (created with an empty pool: ``ppo.env_kwargs``) with the config's fresh-entry
    characters, ascension and seed texts; logs the summary."""
    if not uses_start_saves(cfg):
        if getattr(cfg, "start_saves", None):
            log(f"--start-saves {cfg.start_saves} ignored: --save-mix 0 (fresh starts only)")
        return None
    spec = env.pool_spec
    env_seed = getattr(cfg, "env_seed", None)
    start_seed = getattr(cfg, "start_seed", None)
    mix, fresh, probe = float(cfg.save_mix), int(cfg.pool), None
    stream = getattr(cfg, "start_mode", "pool") == "stream"
    if stream:          # saves only in the pool (copies as pool mode would make them); the stream draws the share
        probe = probe_saves(cfg.start_saves, content=env.config.get("content", "ported_only"),
                            characters=tuple(cfg.characters))
        if not probe[0]:
            reasons = Counter(r["reason"] for r in probe[1]).most_common(3)
            raise ValueError(f"--start-saves {cfg.start_saves}: no usable save among {probe[2]} files (skipped: "
                             f"{reasons})")
        copies = plan_layout(fresh, len(probe[0]), mix).copies
        mix, fresh = 1.0, copies * len(probe[0])
    sp = build_start_pool(env, cfg.start_saves, mix=mix, copies_mode=cfg.save_copies,
                          fresh=fresh, characters=tuple(cfg.characters), ascension=int(cfg.ascension),
                          seed_prefix=spec.seed_prefix if spec is not None else
                          f"TRAIN{cfg.seed if env_seed is None else env_seed}",
                          act1=spec.act1 if spec is not None else "random",
                          seed=int(cfg.seed if start_seed is None else start_seed), probe=probe)
    if stream:
        sp.stream_share = float(cfg.save_mix)
    for line in sp.describe():
        log(line)
    other = sorted(a for a in sp.histograms()["ascension"] if a != int(cfg.ascension))
    if other:
        log(f"note: save-started episodes play at the save's ascension (A{', A'.join(map(str, other))}); "
            f"--ascension {cfg.ascension} applies to the fresh entries only")
    return sp
