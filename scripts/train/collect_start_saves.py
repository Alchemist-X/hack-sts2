#!/usr/bin/env python3
"""Collect the recorder's copies of the game's run saves as training start states (``sts2train train --start-saves``).

    python3 scripts/train/collect_start_saves.py --out tmp/train/saves-silent-a9 --character SILENT --ascension 9
    uv run --project sim/tools/vec python scripts/train/collect_start_saves.py --out DIR --character SILENT --check

Source, read only (files are opened for reading and byte-copied; nothing under it is written, renamed or deleted):
``<sessions>/<session>/native/saves/*.save``, ``<sessions>`` = ``--sessions`` or
``~/Library/Application Support/Sts2Recorder/sessions``. Each file is a copy of the game's ``current_run.save``
(schema 20) that the recorder took when the game saved: entering a map point, after a won combat
(``pre_finished_room`` = that combat) or after the act's Ancient. Saves are parsed like ``sts2rec.run_save`` (no
dependency on it): character, ascension, act, the floor the sim starts on (the save's map-point history, + 1 for
"entering a map point": the sim enters that room), the room type, HP, gold, deck, relics; the session's
``manifest.json`` adds the game version and the run seed.

Filters: ``--character`` (e.g. SILENT), ``--ascension N``, ``--min-floor N``, ``--act N`` (1-based act of the saved
state), ``--max-floors-to-boss N`` (keep saves whose act floor is at most N floors before the act's first boss floor;
the boss floors themselves, the A10 second boss (-1) and boss Rewards screens (0) included); saves the sim's importer rejects
regardless of content (another schema, non-standard mode, modifiers, multiplayer) are skipped. ``--check`` also imports
every candidate with sts2vec (``env.pool.add_save`` + the first observation, content ``ported_only`` like the trainer)
and skips the ones that fail, with the importer's reasons; it needs the sts2vec package and a built library (run it
with ``uv run --project sim/tools/vec``).

Output: ``DIR/<CHARACTER>_A<asc>_f<floor>_<run seed>_<sha256[:8]>.save`` (byte copies, de-duplicated by sha256) and
``DIR/manifest.json`` (filters, counts, total bytes, floor / act / room histograms, skipped and duplicate files, and
one row per save: file, session, source, sha256, bytes, character, ascension, act, floor, act floor, floors to boss,
boss ids, entry point, room type, deck size, upgraded cards, ...).

Act floor = the save's map-point count in the current act (the act's Ancient start point counts as floor 1), + 1 when
entering a map point, so it equals the current map row + 1; the act's first boss sits on act floor ``boss row + 1``
(``acts[i].saved_map.boss.coord.row``), and ``floors_to_boss`` = boss act floor - act floor (0 on the first boss and its
Rewards screen, -1 on the A10 second boss). ``--sessions`` may also be a staging tree of symlinks laid out as
``<session>/native/saves/*.save`` (e.g. to re-filter an existing pool); the copies are made from the link targets.
``--out`` must be empty or new unless ``--clean`` (removes its ``*.save`` files and manifest first).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import time
from collections import Counter
from pathlib import Path

DEFAULT_SESSIONS = Path.home() / "Library" / "Application Support" / "Sts2Recorder" / "sessions"
SCHEMA = 20
COMBAT_ROOMS = {"monster", "elite", "boss"}


def strip(model_id) -> str:
    s = str(model_id or "")
    return s.split(".", 1)[1] if "." in s else s


def _coord(c) -> tuple[int, int] | None:
    return (int(c["col"]), int(c["row"])) if isinstance(c, dict) and "col" in c and "row" in c else None


def map_point_type(act: dict, coord) -> str | None:
    """Type of the saved map point at ``coord`` in an act's ``saved_map`` (points, start, boss, second boss)."""
    sm = act.get("saved_map") if isinstance(act, dict) else None
    if not isinstance(sm, dict) or coord is None:
        return None
    pts = list(sm.get("points") or [])
    pts += [sm[k] for k in ("start", "boss", "second_boss") if isinstance(sm.get(k), dict)]
    for pt in pts:
        if _coord(pt.get("coord")) == coord:
            return pt.get("type")
    return None


def describe(data: dict) -> dict:
    """The fields of a parsed save that matter for a start pool, plus ``reasons`` the importer would reject it for."""
    reasons = []
    if data.get("schema_version") != SCHEMA:
        reasons.append(f"schema_version {data.get('schema_version')!r} (only {SCHEMA})")
    if data.get("game_mode", "standard") != "standard":
        reasons.append(f"game_mode {data.get('game_mode')}")
    if "dailyTime" in data:
        reasons.append("daily run")
    reasons += [f"modifier {strip(m.get('id'))}" for m in data.get("modifiers") or [] if isinstance(m, dict)]
    players = data.get("players") or []
    if len(players) != 1:
        reasons.append(f"{len(players)} players (single-player only)")
    p = players[0] if players and isinstance(players[0], dict) else {}
    acts = data.get("acts") or []
    ai = data.get("current_act_index")
    ai = ai if isinstance(ai, int) else -1
    if not 0 <= ai < len(acts):
        reasons.append(f"current_act_index {ai} outside {len(acts)} acts")
    history = data.get("map_point_history") or []
    floors = sum(len(a) for a in history if isinstance(a, list))
    visited = [c for c in (_coord(c) for c in data.get("visited_map_coords") or []) if c is not None]
    pre = data.get("pre_finished_room") if isinstance(data.get("pre_finished_room"), dict) else None
    if pre is not None:
        rt = pre.get("room_type")
        entry = "post_combat_rewards" if rt in COMBAT_ROOMS else ("ancient_finished" if rt == "event" else "?")
        room = rt
        if entry == "?":
            reasons.append(f"pre_finished_room room_type {rt!r} cannot be resumed")
        floor = floors
    elif visited:
        entry, floor = "entering_map_point", floors + 1
        room = map_point_type(acts[ai], visited[-1]) if 0 <= ai < len(acts) else None
    else:
        entry, room, floor = "no_visited_coord", "map", floors
    rng = data.get("rng") if isinstance(data.get("rng"), dict) else {}
    act = acts[ai] if 0 <= ai < len(acts) and isinstance(acts[ai], dict) else {}
    act_hist = history[ai] if 0 <= ai < len(history) and isinstance(history[ai], list) else []
    act_floor = len(act_hist) + (1 if entry == "entering_map_point" else 0)
    sm = act.get("saved_map") if isinstance(act.get("saved_map"), dict) else {}
    boss = _coord((sm.get("boss") or {}).get("coord")) if isinstance(sm.get("boss"), dict) else None
    rooms = act.get("rooms") if isinstance(act.get("rooms"), dict) else {}
    deck = [c for c in p.get("deck") or [] if isinstance(c, dict)]
    return {"character": strip(p.get("character_id")), "ascension": data.get("ascension"), "act": ai + 1,
            "floor": floor, "history_floors": floors, "act_floor": act_floor,
            "floors_to_boss": boss[1] + 1 - act_floor if boss is not None else None,
            "boss_id": strip(rooms.get("boss_id")) or None, "second_boss_id": strip(rooms.get("second_boss_id")) or None,
            "entry_point": entry, "room_type": room,
            "hp": p.get("current_hp"), "max_hp": p.get("max_hp"), "gold": p.get("gold"),
            "deck_n": len(p.get("deck") or []), "upgraded_n": sum(1 for c in deck if (c.get("current_upgrade_level") or 0) > 0),
            "relic_n": len(p.get("relics") or []), "run_seed": rng.get("seed"), "reasons": reasons}


def session_info(session_dir: Path, cache: dict) -> dict:
    if session_dir not in cache:
        info = {}
        try:
            m = json.loads((session_dir / "manifest.json").read_text(encoding="utf-8"))
            info = {"game_version": (m.get("game") or {}).get("version"), "recorder_version": m.get("recorder_version")}
        except (OSError, ValueError, AttributeError):
            pass
        cache[session_dir] = info
    return cache[session_dir]


class SimCheck:
    """Import check through sts2vec (``--check``): add_save + the first observation in a 1-slot env."""

    def __init__(self, content: str = "ported_only"):
        try:
            from sts2vec import STATUS, VecEnv
            from sts2vec.errors import Sts2VecError
        except ImportError as e:
            sys.exit(f"--check needs the sts2vec package (uv run --project sim/tools/vec ...): {e}")
        self.err_bits = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
        self.error_type = Sts2VecError
        self.env = VecEnv(1, threads=1, pool=None, content=content, auto_reset=False, seed_text="COLLECT")
        s = self.env.schema
        self.cols = {f: s.col("global_i", f) for f in ("phase", "total_floor", "act_index")}
        self.phases = s.vocab.enums["RunPhase"]
        self.library = str(self.env.lib.path)

    def __call__(self, path: Path) -> tuple[str | None, dict]:
        try:
            entry = self.env.pool.add_save(path)
        except self.error_type as e:
            text = str(e)
            return text.split("is not supported: ", 1)[-1][:500], {}
        obs = self.env.restore(0, entry)
        if int(self.env.status[0]) & self.err_bits:
            return "first observation failed: " + (self.env.last_error(0).splitlines() or ["?"])[0][:300], {}
        if int(obs["cand_n"][0]) <= 0:
            return "no legal action at the first observation", {}
        g = {f: int(obs["global_i"][0, c]) for f, c in self.cols.items()}
        return None, {"sim_phase": self.phases.name(g["phase"]), "sim_floor": g["total_floor"],
                      "sim_act": g["act_index"] + 1}

    def close(self):
        self.env.close()


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", required=True, help="output directory (new or empty, or --clean)")
    ap.add_argument("--sessions", default=str(DEFAULT_SESSIONS), help=f"recorder sessions root (default {DEFAULT_SESSIONS})")
    ap.add_argument("--character", help="keep only this character (e.g. SILENT)")
    ap.add_argument("--ascension", type=int, help="keep only this ascension")
    ap.add_argument("--min-floor", dest="min_floor", type=int, default=0, help="keep saves starting on floor >= N")
    ap.add_argument("--act", type=int, help="keep only saves whose state is in this act (1-based)")
    ap.add_argument("--max-floors-to-boss", dest="max_floors_to_boss", type=int,
                    help="keep saves at most N floors before the act's first boss floor (boss floors included)")
    ap.add_argument("--check", action="store_true", help="import every candidate with sts2vec; skip the failures")
    ap.add_argument("--clean", action="store_true", help="remove --out's *.save files and manifest.json first")
    a = ap.parse_args(argv)

    root = Path(a.sessions).expanduser().resolve()
    out = Path(a.out).expanduser().resolve()
    if not root.is_dir():
        sys.exit(f"sessions root {root} does not exist")
    if out == root or root in out.parents:
        sys.exit(f"--out {out} is inside the recorder sessions root {root}: refusing (the source is read only)")
    if out.exists():
        old = [p for p in out.iterdir() if p.suffix == ".save" or p.name == "manifest.json"]
        others = [p for p in out.iterdir() if p not in old and not p.name.startswith(".")]
        if old and not a.clean:
            sys.exit(f"--out {out} already holds {len(old)} save / manifest files: pass --clean or a new directory")
        if others:
            sys.exit(f"--out {out} holds other files ({others[0].name}, ...): use a dedicated directory")
        for p in old:
            p.unlink()
    character = strip(a.character).upper() if a.character else None

    sources = sorted(root.glob("*/native/saves/*.save"))
    check = SimCheck() if a.check else None
    cache: dict = {}
    kept, skipped, duplicates, by_sha = [], [], [], {}
    filtered = Counter()
    for src in sources:
        rel = src.relative_to(root).as_posix()
        raw = src.read_bytes()
        sha = hashlib.sha256(raw).hexdigest()
        if sha in by_sha:
            duplicates.append({"source": rel, "same_as": by_sha[sha]})
            continue
        by_sha[sha] = rel
        try:
            data = json.loads(raw.decode("utf-8-sig"))
            if not isinstance(data, dict):
                raise ValueError("top level is not an object")
            d = describe(data)
        except (ValueError, UnicodeDecodeError, TypeError, KeyError) as e:
            skipped.append({"source": rel, "reason": f"unreadable: {type(e).__name__}: {e}"[:300]})
            continue
        if character and d["character"].upper() != character:
            filtered["character"] += 1
            continue
        if a.ascension is not None and d["ascension"] != a.ascension:
            filtered["ascension"] += 1
            continue
        if d["floor"] < a.min_floor:
            filtered["min_floor"] += 1
            continue
        if a.act is not None and d["act"] != a.act:
            filtered["act"] += 1
            continue
        if a.max_floors_to_boss is not None and (d["floors_to_boss"] is None or d["floors_to_boss"] > a.max_floors_to_boss):
            filtered["max_floors_to_boss"] += 1
            continue
        if d["reasons"]:
            skipped.append({"source": rel, "reason": "; ".join(d["reasons"])})
            continue
        sim = {}
        if check is not None:
            reason, sim = check(src)
            if reason:
                skipped.append({"source": rel, "reason": reason})
                continue
        sess = src.parents[2]
        name = f"{d['character']}_A{d['ascension']}_f{d['floor']:02d}_{d['run_seed'] or 'NOSEED'}_{sha[:8]}.save"
        row = {"file": name, "session": sess.name, "source": rel, "sha256": sha, "bytes": len(raw),
               **{k: v for k, v in d.items() if k != "reasons"}, **session_info(sess, cache), **sim}
        kept.append((src, row))
    if check is not None:
        check.close()

    out.mkdir(parents=True, exist_ok=True)
    for src, row in kept:
        shutil.copyfile(src, out / row["file"])          # read the source, write the copy; the source is untouched
    rows = [r for _, r in kept]
    total = sum(r["bytes"] for r in rows)
    num = ("floor", "floors_to_boss")
    hist = {k: dict(sorted(Counter(r[k] for r in rows).items(),
                           key=lambda kv: (kv[0] is None, kv[0] or 0) if k in num else (0, str(kv[0]))))
            for k in ("floor", "act", "floors_to_boss", "entry_point", "room_type", "ascension", "character")}
    manifest = {"created_utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "sessions_root": str(root),
                "filters": {"character": character, "ascension": a.ascension, "min_floor": a.min_floor,
                            "act": a.act, "max_floors_to_boss": a.max_floors_to_boss},
                "checked_with_sim": check is not None, "sim_library": check.library if check else None,
                "source_files": len(sources), "count": len(rows), "bytes_total": total, "histograms": hist,
                "filtered_out": dict(filtered), "skipped": skipped, "duplicates": duplicates, "saves": rows}
    (out / "manifest.json").write_text(json.dumps(manifest, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")

    print(f"{len(sources)} save files under {root}: {len(rows)} collected into {out} ({total:,} bytes), "
          f"{len(duplicates)} duplicates, {len(skipped)} skipped, filtered out {dict(filtered) or 0}")
    for r, n in Counter(s["reason"] for s in skipped).most_common():
        print(f"  skipped x{n}: {r}")
    print(f"  act {hist['act']}")
    print(f"  floor " + " ".join(f"{k}:{v}" for k, v in hist["floor"].items()))
    print(f"  floors to boss {hist['floors_to_boss']}")
    print(f"  entry point {hist['entry_point']}")
    print(f"  room type {hist['room_type']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
