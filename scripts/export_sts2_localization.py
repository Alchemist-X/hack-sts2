#!/usr/bin/env python3
"""Export localisation templates (rules text) from the installed game's PCK.

Localisation only: no running game, no wiki port, no decompile. The Godot PCK
stores each localisation table as an uncompressed JSON object, once per
supported locale. For each table we find every copy by a stable sentinel key,
identify the English and Simplified Chinese copies, and write one JSON object
per line (sorted by id) to knowledge/source/<installed version>/:

  cards.jsonl, relics.jsonl, potions.jsonl, keywords.jsonl:
      {id, kind, game_version, game_commit, schema_version,
       localized:{eng:{title, description_template}, zhs:{...}}}
  enemies.jsonl:
      {id, kind:"enemy", ..., localized:{eng:{title, move_titles:{MOVE: title}}, zhs:{...}}}
  powers.jsonl:
      {id (without _POWER), model_id, kind:"power", ...,
       localized:{eng:{title, description_template, smart_template}, zhs:{...}}}
      description_template is the static `.description`; smart_template is
      `.smartDescription` (uses {Amount} etc.).
  events.jsonl:
      {id, kind:"event"|"ancient", ...,
       localized:{eng:{title, pages:{PAGE: description},
                       options:{"PAGE.options.KEY": {title, description}}}, zhs:{...}}}
      Option keys are the sim's EventOption.TextKey minus "<EVENT_ID>.pages.".

Locale selection. Tables with a v0.107.1 counterpart (cards, relics, potions,
enemies, keywords) pick the copy whose titles best match the committed
v0.107.1 eng / zhs titles. Power and event tables have no counterpart; they
use the relic table's copy indices (all three tables have the same number of
copies, in the same locale order) and the zhs power copy must contain
Simplified-only characters.

Usage: python3 scripts/export_sts2_localization.py [--game-app PATH]
"""

from __future__ import annotations

import argparse
import json
import mmap
import re
import sys
from pathlib import Path
from typing import Any

SCHEMA_VERSION = 1
WINDOW = 4 * 1024 * 1024
REFERENCE_VERSION = "v0.107.1"

# kind -> (sentinel key, reference jsonl in REFERENCE_VERSION or None, output file, title suffix)
TABLES: dict[str, tuple[str, str | None, str, str]] = {
    "card": ("SLEIGHT_OF_FLESH.description", "cards.jsonl", "cards.jsonl", "title"),
    "relic": ("AKABEKO.description", "relics.jsonl", "relics.jsonl", "title"),
    "potion": ("FIRE_POTION.description", "potions.jsonl", "potions.jsonl", "title"),
    "enemy": ("INFESTED_PRISM.name", "enemies.jsonl", "enemies.jsonl", "name"),
    "keyword": ("EXHAUST.description", "keywords.jsonl", "keywords.jsonl", "title"),
    "power": ("VULNERABLE_POWER.description", None, "powers.jsonl", "title"),
    "event": ("ABYSSAL_BATHS.title", None, "events.jsonl", "title"),
    "ancient": ("NEOW.title", None, "events.jsonl", "title"),
}


def parse_args() -> argparse.Namespace:
    game_root = Path.home() / (
        "Library/Application Support/Steam/steamapps/common/"
        "Slay the Spire 2/SlayTheSpire2.app"
    )
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--game-app", type=Path, default=game_root)
    parser.add_argument("--knowledge", type=Path, default=Path("knowledge/source"))
    parser.add_argument("--output", type=Path, default=None, help="default: <knowledge>/<installed version>")
    return parser.parse_args()


def find_all(mapped: mmap.mmap, needle: bytes) -> list[int]:
    offsets: list[int] = []
    position = 0
    while True:
        position = mapped.find(needle, position)
        if position < 0:
            return offsets
        offsets.append(position)
        position += len(needle)


def table_copies(mapped: mmap.mmap, sentinel: str) -> list[dict[str, str]]:
    """Every copy (one per locale, in PCK order) of the localisation table holding `sentinel`."""
    decoder = json.JSONDecoder()
    copies: list[dict[str, str]] = []
    for center in find_all(mapped, f'"{sentinel}"'.encode()):
        start = mapped.rfind(b"{\n", max(0, center - WINDOW), center)
        if start < 0:
            continue
        text = mapped[start : start + WINDOW].decode("utf-8", errors="replace")
        try:
            value, _ = decoder.raw_decode(text)
        except json.JSONDecodeError:
            continue
        if isinstance(value, dict) and sentinel in value:
            copies.append({k: v for k, v in value.items() if isinstance(k, str) and isinstance(v, str)})
    if len(copies) < 2:
        raise RuntimeError(f"localisation table for {sentinel} found {len(copies)} times")
    return copies


def reference_titles(path: Path, locale: str) -> dict[str, str]:
    titles: dict[str, str] = {}
    with path.open(encoding="utf-8") as handle:
        for line in handle:
            record = json.loads(line)
            title = ((record.get("localized") or {}).get(locale) or {}).get("title")
            if title:
                titles[record["id"]] = title
    return titles


def pick_locale(copies: list[dict[str, str]], titles: dict[str, str], suffix: str) -> tuple[int, float]:
    best, best_score = -1, -1.0
    for index, table in enumerate(copies):
        # Score over the reference ids this table actually holds (the keyword table is a subset).
        present = [(model_id, title) for model_id, title in titles.items() if f"{model_id}.{suffix}" in table]
        hits = sum(1 for model_id, title in present if table[f"{model_id}.{suffix}"] == title)
        score = hits / max(1, len(present)) if len(present) >= 3 else 0.0
        if score > best_score:
            best, best_score = index, score
    return best, best_score


def base_record(model_id: str, kind: str, version: str, commit: str) -> dict[str, Any]:
    return {
        "game_commit": commit,
        "game_version": version,
        "id": model_id,
        "kind": kind,
        "schema_version": SCHEMA_VERSION,
    }


def simple_records(kind: str, eng: dict[str, str], zhs: dict[str, str], version: str, commit: str) -> list[dict[str, Any]]:
    ids = sorted({k.rsplit(".", 1)[0] for k in eng if k.count(".") == 1 and k.endswith((".title", ".description"))})
    records = []
    for model_id in ids:
        localized: dict[str, Any] = {}
        for locale, table in (("eng", eng), ("zhs", zhs)):
            entry = {
                "title": table.get(f"{model_id}.title"),
                "description_template": table.get(f"{model_id}.description"),
            }
            entry = {k: v for k, v in entry.items() if v is not None}
            if entry:
                localized[locale] = entry
        record = base_record(model_id, kind, version, commit)
        record["localized"] = localized
        records.append(record)
    return records


MOVE_RE = re.compile(r"^([A-Z0-9_]+)\.moves\.([A-Za-z0-9_]+)\.title$")


def enemy_records(eng: dict[str, str], zhs: dict[str, str], version: str, commit: str) -> list[dict[str, Any]]:
    def parse(table: dict[str, str]) -> dict[str, dict[str, Any]]:
        out: dict[str, dict[str, Any]] = {}
        for key, value in table.items():
            if key.count(".") == 1 and key.endswith(".name"):
                out.setdefault(key[: -len(".name")], {})["title"] = value
                continue
            m = MOVE_RE.match(key)
            if m:
                out.setdefault(m.group(1), {}).setdefault("move_titles", {})[m.group(2)] = value
        return out

    eng_map, zhs_map = parse(eng), parse(zhs)
    records = []
    for model_id in sorted(eng_map):
        localized = {"eng": eng_map[model_id]}
        if model_id in zhs_map:
            localized["zhs"] = zhs_map[model_id]
        record = base_record(model_id, "enemy", version, commit)
        record["localized"] = localized
        records.append(record)
    return records


def power_records(eng: dict[str, str], zhs: dict[str, str], version: str, commit: str) -> list[dict[str, Any]]:
    ids = sorted(
        {
            k.rsplit(".", 1)[0]
            for k in eng
            if k.count(".") == 1 and k.rsplit(".", 1)[1] in ("title", "description", "smartDescription")
        }
    )
    records = []
    for model_id in ids:
        localized: dict[str, Any] = {}
        for locale, table in (("eng", eng), ("zhs", zhs)):
            entry = {
                "title": table.get(f"{model_id}.title"),
                "description_template": table.get(f"{model_id}.description"),
                "smart_template": table.get(f"{model_id}.smartDescription"),
            }
            entry = {k: v for k, v in entry.items() if v is not None}
            if entry:
                localized[locale] = entry
        short_id = model_id[: -len("_POWER")] if model_id.endswith("_POWER") else model_id
        record = base_record(short_id, "power", version, commit)
        record["localized"] = localized
        record["model_id"] = model_id
        records.append(record)
    return records


PAGE_RE = re.compile(r"^([A-Z0-9_]+)\.pages\.([A-Za-z0-9_]+)\.description$")
OPTION_RE = re.compile(r"^([A-Z0-9_]+)\.pages\.([A-Za-z0-9_]+)\.options\.([A-Za-z0-9_]+)\.(title|description)$")


def event_localized(table: dict[str, str]) -> dict[str, dict[str, Any]]:
    events: dict[str, dict[str, Any]] = {}
    for key, value in table.items():
        if key.endswith(".title") and key.count(".") == 1:
            events.setdefault(key[: -len(".title")], {})["title"] = value
            continue
        m = OPTION_RE.match(key)
        if m:
            ev, page, option, field = m.groups()
            opts = events.setdefault(ev, {}).setdefault("options", {})
            opts.setdefault(f"{page}.options.{option}", {})[field] = value
            continue
        m = PAGE_RE.match(key)
        if m:
            ev, page = m.groups()
            events.setdefault(ev, {}).setdefault("pages", {})[page] = value
            continue
        if key.endswith(".epithet") and key.count(".") == 1:
            events.setdefault(key[: -len(".epithet")], {})["epithet"] = value
    return events


def event_records(
    tables: list[tuple[str, dict[str, str], dict[str, str]]], version: str, commit: str
) -> list[dict[str, Any]]:
    merged: dict[str, dict[str, Any]] = {}
    for kind, eng, zhs in tables:
        eng_events = event_localized(eng)
        zhs_events = event_localized(zhs)
        for event_id, entry in eng_events.items():
            if "title" not in entry:
                continue
            localized = {"eng": entry}
            if event_id in zhs_events:
                localized["zhs"] = zhs_events[event_id]
            record = base_record(event_id, kind, version, commit)
            record["localized"] = localized
            merged[event_id] = record
    return [merged[k] for k in sorted(merged)]


def write_jsonl(path: Path, records: list[dict[str, Any]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        for record in records:
            handle.write(json.dumps(record, ensure_ascii=False, sort_keys=True) + "\n")


def has_simplified(table: dict[str, str]) -> bool:
    # Characters that differ between Simplified and Traditional (易伤/易傷, 虚弱/虛弱).
    text = (table.get("VULNERABLE_POWER.title") or "") + (table.get("WEAK_POWER.title") or "")
    return ("伤" in text or "虚" in text) and "傷" not in text


def main() -> int:
    args = parse_args()
    resources = args.game_app / "Contents/Resources"
    pck = resources / "Slay the Spire 2.pck"
    release_info_path = resources / "release_info.json"
    for required in (pck, release_info_path):
        if not required.is_file():
            print(f"required game file is missing: {required}", file=sys.stderr)
            return 1
    release_info = json.loads(release_info_path.read_text(encoding="utf-8"))
    version = str(release_info["version"])
    if not version.startswith("v"):
        version = "v" + version
    plain_version = version[1:]
    commit = str(release_info.get("commit", ""))
    output = args.output or (args.knowledge / version)
    reference_dir = args.knowledge / REFERENCE_VERSION

    chosen: dict[str, tuple[dict[str, str], dict[str, str]]] = {}
    with pck.open("rb") as handle:
        mapped = mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ)
        try:
            copies = {kind: table_copies(mapped, spec[0]) for kind, spec in TABLES.items()}
        finally:
            mapped.close()

    relic_indices: tuple[int, int] | None = None
    for kind, (sentinel, reference, _, suffix) in TABLES.items():
        if reference is None:
            continue
        ref = reference_dir / reference
        eng_index, eng_score = pick_locale(copies[kind], reference_titles(ref, "eng"), suffix)
        zhs_index, zhs_score = pick_locale(copies[kind], reference_titles(ref, "zhs"), suffix)
        print(
            f"[localization] {kind}: {len(copies[kind])} copies; eng=#{eng_index} ({eng_score:.0%} of {REFERENCE_VERSION} titles match), "
            f"zhs=#{zhs_index} ({zhs_score:.0%})"
        )
        if eng_score < 0.5 or zhs_score < 0.5 or eng_index == zhs_index:
            print(f"[localization] could not identify the eng/zhs copies of the {kind} table", file=sys.stderr)
            return 1
        chosen[kind] = (copies[kind][eng_index], copies[kind][zhs_index])
        if kind == "relic":
            relic_indices = (eng_index, zhs_index)
    assert relic_indices is not None
    for kind in ("power", "event", "ancient"):
        if len(copies[kind]) != len(copies["relic"]):
            print(f"[localization] {kind} table has {len(copies[kind])} copies, relic table {len(copies['relic'])}", file=sys.stderr)
            return 1
        chosen[kind] = (copies[kind][relic_indices[0]], copies[kind][relic_indices[1]])
    if not has_simplified(chosen["power"][1]):
        print("[localization] zhs copy of the power table is not Simplified Chinese", file=sys.stderr)
        return 1

    outputs: dict[str, list[dict[str, Any]]] = {}
    for kind in ("card", "relic", "potion", "keyword"):
        outputs[TABLES[kind][2]] = simple_records(kind, *chosen[kind], plain_version, commit)
    outputs["enemies.jsonl"] = enemy_records(*chosen["enemy"], plain_version, commit)
    outputs["powers.jsonl"] = power_records(*chosen["power"], plain_version, commit)
    outputs["events.jsonl"] = event_records(
        [("event", *chosen["event"]), ("ancient", *chosen["ancient"])], plain_version, commit
    )
    for name, records in outputs.items():
        write_jsonl(output / name, records)
    manifest = {
        "game_commit": commit,
        "game_version": plain_version,
        "generator": "scripts/export_sts2_localization.py",
        "schema_version": SCHEMA_VERSION,
        "counts": {name[: -len(".jsonl")]: len(records) for name, records in sorted(outputs.items())},
        "locales": ["eng", "zhs"],
        "note": "Localisation templates only (no runtime text, no decompiled facts); see knowledge/source/v0.107.1 for the full knowledge export.",
    }
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"[localization] wrote {', '.join(f'{len(r)} {n}' for n, r in outputs.items())} to {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
