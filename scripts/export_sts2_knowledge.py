#!/usr/bin/env python3
"""Export versioned STS2 facts from the live model registry and local game source.

The active profile supplies canonical, currently registered card/relic/potion IDs.
The MCP wiki supplies localized, fully resolved card/relic rules text. The local
PCK supplies English and Simplified Chinese localization templates. Decompiled
v0.107.1 source supplies model classes, numeric expressions, upgrades, and enemy
move state machines. Community opinions intentionally live elsewhere.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import mmap
import os
import re
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterable
from pathlib import Path
from typing import Any


GAME_VERSION = "0.107.1"
GAME_COMMIT = "59260271"
SCHEMA_VERSION = 1
LOCALIZATION_OBJECT_SENTINELS = (
    "SLEIGHT_OF_FLESH.description",
    "AKABEKO.description",
    "INFESTED_PRISM.name",
    "FIRE_POTION.description",
    "FATAL.description",
    "BOUND.description",
    "ETHEREAL.description",
    "EXHAUST.description",
    "INNATE.description",
    "RETAIN.description",
    "SLY.description",
    "UNPLAYABLE.description",
    "TAINTED_POWER.description",
    "VULNERABLE_POWER.description",
    "WEAK_POWER.description",
    "STRENGTH_POWER.description",
)
MODEL_NAMESPACES = {
    "card": "Cards",
    "relic": "Relics",
    "potion": "Potions",
    "enemy": "Monsters",
}
MECHANIC_SPECS = (
    {
        "id": "OSTY_SUMMON_LIFECYCLE",
        "title": "Osty summon, growth, death, and revival",
        "summary": (
            "Summoning while Osty is alive adds to that creature's max HP. "
            "Summoning while Osty is dead or absent sets the revived/new "
            "creature's max HP to the summon amount and heals it by that amount; "
            "previous in-combat max-HP growth is therefore not retained."
        ),
        "relative_path": "MegaCrit/sts2/Core/Commands/OstyCmd.cs",
        "type": "MegaCrit.Sts2.Core.Commands.OstyCmd",
        "method": "Summon",
        "signature": "public static async Task<SummonResult> Summon(",
        "apis": ["OstyCmd.Summon"],
        "related_models": [
            "CARD.AFTERLIFE",
            "CARD.BODYGUARD",
            "CARD.BONE_SHARDS",
            "CARD.NECRO_MASTERY",
            "CARD.REANIMATE",
            "CARD.SPUR",
            "RELIC.BOUND_PHYLACTERY",
        ],
        "invariants": [
            {
                "when": "summoner.IsOstyAlive",
                "effect": "CreatureCmd.GainMaxHp(summoner.Osty, amount)",
            },
            {
                "when": "Osty is dead or absent",
                "effect": (
                    "CreatureCmd.SetMaxHp(osty, amount), then "
                    "CreatureCmd.Heal(osty, amount, isReviving)"
                ),
            },
        ],
        "required_fragments": [
            "if (summoner.IsOstyAlive)",
            "await CreatureCmd.GainMaxHp(summoner.Osty, amount);",
            "await CreatureCmd.SetMaxHp(osty, amount);",
            "await CreatureCmd.Heal(osty, amount, isReviving);",
        ],
    },
)


def parse_args() -> argparse.Namespace:
    game_root = Path.home() / (
        "Library/Application Support/Steam/steamapps/common/"
        "Slay the Spire 2/SlayTheSpire2.app"
    )
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=15601)
    parser.add_argument("--game-app", type=Path, default=game_root)
    parser.add_argument(
        "--decompiled-root",
        type=Path,
        default=Path("artifacts/knowledge/decompiled-v0.107.1"),
    )
    parser.add_argument(
        "--output", type=Path, default=Path("knowledge/source/v0.107.1")
    )
    parser.add_argument(
        "--skip-decompile",
        action="store_true",
        help="Require an existing decompiled tree instead of regenerating it.",
    )
    return parser.parse_args()


def sha256_bytes(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def http_json(url: str, *, attempts: int = 3) -> dict[str, Any]:
    last_error: Exception | None = None
    for _ in range(attempts):
        try:
            with urllib.request.urlopen(url, timeout=20) as response:
                value = json.load(response)
            if not isinstance(value, dict):
                raise ValueError(f"expected JSON object from {url}")
            return value
        except (urllib.error.URLError, TimeoutError, OSError, ValueError) as error:
            last_error = error
    raise RuntimeError(f"failed to read {url}: {last_error}")


def wiki_exact(port: int, item_type: str, model_id: str) -> dict[str, Any]:
    query = urllib.parse.urlencode(
        {"query": model_id, "type": item_type, "limit": 1}
    )
    data = http_json(f"http://127.0.0.1:{port}/api/v1/wiki?{query}")
    matches = [
        item
        for item in data.get("results", [])
        if isinstance(item, dict) and item.get("id") == model_id
    ]
    if len(matches) != 1:
        raise RuntimeError(
            f"wiki exact lookup failed for {item_type} {model_id}: "
            f"{[item.get('id') for item in data.get('results', [])]}"
        )
    return matches[0]


def find_all(mapped: mmap.mmap, needle: bytes) -> list[int]:
    offsets: list[int] = []
    position = 0
    while True:
        position = mapped.find(needle, position)
        if position < 0:
            return offsets
        offsets.append(position)
        position += len(needle)


def extract_locale_map(pck_path: Path, occurrence_index: int) -> dict[str, str]:
    """Merge the relevant localization objects for one locale.

    The PCK stores uncompressed JSON objects, but their physical order is not a
    single locale block. Each stable sentinel occurs once per supported locale;
    selecting the same occurrence index per object avoids mixing neighboring
    languages while remaining independent of private PCK directory metadata.
    """

    with pck_path.open("rb") as handle:
        mapped = mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ)
        try:
            merged: dict[str, str] = {}
            decoder = json.JSONDecoder()
            for sentinel in LOCALIZATION_OBJECT_SENTINELS:
                occurrences = find_all(mapped, f'"{sentinel}"'.encode())
                if len(occurrences) < 2:
                    raise RuntimeError(
                        f"localization sentinel {sentinel} appeared "
                        f"{len(occurrences)} times"
                    )
                index = (
                    occurrence_index
                    if occurrence_index >= 0
                    else len(occurrences) + occurrence_index
                )
                if not 0 <= index < len(occurrences):
                    raise RuntimeError(
                        f"invalid locale occurrence index {occurrence_index}"
                    )
                center = occurrences[index]
                start = mapped.rfind(
                    b"{\n", max(0, center - 2 * 1024 * 1024), center
                )
                if start < 0:
                    raise RuntimeError(
                        f"could not find enclosing JSON object for {sentinel}"
                    )
                text = mapped[start : start + 2 * 1024 * 1024].decode(
                    "utf-8", errors="replace"
                )
                try:
                    value, _ = decoder.raw_decode(text)
                except json.JSONDecodeError as error:
                    raise RuntimeError(
                        f"failed to parse localization object for {sentinel}: {error}"
                    ) from error
                if not isinstance(value, dict) or sentinel not in value:
                    raise RuntimeError(
                        f"enclosing localization object did not contain {sentinel}"
                    )
                for key, localized in value.items():
                    if isinstance(key, str) and isinstance(localized, str):
                        merged[key] = localized
            return merged
        finally:
            mapped.close()


def normalized_name(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower())


def pascal_to_model_id(value: str) -> str:
    first = re.sub(r"(.)([A-Z][a-z]+)", r"\1_\2", value)
    second = re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", first)
    return second.upper()


def localized_model_ids(locales: dict[str, dict[str, str]]) -> dict[str, str]:
    result: dict[str, str] = {}
    for values in locales.values():
        for key in values:
            if key.endswith(".title"):
                model_id = key[:-6]
            elif key.endswith(".name"):
                model_id = key[:-5]
            else:
                continue
            result.setdefault(normalized_name(model_id), model_id)
    return result


def resolve_source_model_id(
    path: Path,
    runtime_by_name: dict[str, str],
    localized_by_name: dict[str, str],
) -> str:
    inferred = pascal_to_model_id(path.stem)
    key = normalized_name(inferred)
    return runtime_by_name.get(key) or localized_by_name.get(key) or inferred


def extract_block(source: str, signature: str) -> str | None:
    start = source.find(signature)
    if start < 0:
        return None
    opening = source.find("{", start)
    if opening < 0:
        return None
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start : index + 1].strip()
    return None


def source_facts(path: Path | None, decompiled_root: Path, kind: str) -> dict[str, Any]:
    if path is None:
        return {"matched": False}
    raw = path.read_bytes()
    source = raw.decode("utf-8")
    expressions: list[str] = []
    mechanics: list[str] = []
    expression_pattern = re.compile(
        r"\b(?:Hp|Damage|Block|Repeat|Amount|Count|Cost|CanonicalVars|Strength|Dexterity)\b"
        r"|AscensionHelper|new (?:MoveState|WeightedEntry|DynamicVar)"
    )
    mechanic_pattern = re.compile(
        r"(?:DamageCmd|PowerCmd|CreatureCmd|CardPileCmd|CardCmd|PlayerCmd|"
        r"GainBlock|Draw|Discard|Exhaust|Summon|Doom|Tainted|Pollution|"
        r"MoveState|Intent|FollowUpState)"
    )
    for line in source.splitlines():
        compact = line.strip()
        if not compact or compact.startswith("using "):
            continue
        if expression_pattern.search(compact) and len(expressions) < 80:
            expressions.append(compact)
        if mechanic_pattern.search(compact) and len(mechanics) < 160:
            mechanics.append(compact)
    dependencies = [
        {"type": command_type, "method": method}
        for command_type, method in sorted(
            set(
                re.findall(
                    r"\b([A-Z][A-Za-z0-9]*Cmd)\.([A-Z][A-Za-z0-9_]*)\s*\(",
                    source,
                )
            )
        )
    ]
    relative = path.relative_to(decompiled_root)
    facts: dict[str, Any] = {
        "matched": True,
        "type": f"MegaCrit.Sts2.Core.Models.{MODEL_NAMESPACES[kind]}.{path.stem}",
        "assembly": "sts2.dll",
        "decompiled_path": str(relative),
        "sha256": sha256_bytes(raw),
        "numeric_expressions": expressions,
        "mechanic_lines": mechanics,
        "dependencies": dependencies,
    }
    constructor = re.search(
        rf"public {re.escape(path.stem)}\(\)\s*\n?\s*: base\((.*?)\)",
        source,
        flags=re.DOTALL,
    )
    if constructor:
        facts["base_constructor"] = " ".join(constructor.group(1).split())
    if kind == "card":
        upgrade = extract_block(source, "protected override void OnUpgrade()")
        if upgrade:
            facts["upgrade_source"] = upgrade
    if kind == "enemy":
        move_machine = extract_block(
            source, "protected override MonsterMoveStateMachine GenerateMoveStateMachine()"
        )
        if move_machine:
            facts["move_state_machine_source"] = move_machine
    return facts


def verified_mechanics(decompiled_root: Path) -> list[dict[str, Any]]:
    """Extract source-backed cross-model contracts that affect decisions.

    These records are intentionally small and fail closed on version drift.  A
    prose invariant is emitted only while all of its required implementation
    fragments are still present in the pinned game's decompiled source.
    """

    records: list[dict[str, Any]] = []
    for spec in MECHANIC_SPECS:
        path = decompiled_root / str(spec["relative_path"])
        if not path.is_file():
            raise RuntimeError(f"mechanic source is missing: {path}")
        raw = path.read_bytes()
        source = raw.decode("utf-8")
        missing = [
            fragment
            for fragment in spec["required_fragments"]
            if fragment not in source
        ]
        if missing:
            raise RuntimeError(
                f"mechanic {spec['id']} no longer matches source; "
                f"missing fragments: {missing}"
            )
        method_source = extract_block(source, str(spec["signature"]))
        if method_source is None:
            raise RuntimeError(
                f"mechanic {spec['id']} method was not extractable"
            )
        records.append(
            {
                "schema_version": SCHEMA_VERSION,
                "game_version": GAME_VERSION,
                "game_commit": GAME_COMMIT,
                "kind": "mechanic",
                "id": spec["id"],
                "title": spec["title"],
                "summary": spec["summary"],
                "apis": spec["apis"],
                "related_models": spec["related_models"],
                "invariants": spec["invariants"],
                "source": {
                    "assembly": "sts2.dll",
                    "type": spec["type"],
                    "method": spec["method"],
                    "decompiled_path": spec["relative_path"],
                    "sha256": sha256_bytes(raw),
                    "method_source": method_source,
                },
            }
        )
    return records


def localize(locales: dict[str, dict[str, str]], model_id: str) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for locale, values in locales.items():
        title = values.get(f"{model_id}.title") or values.get(f"{model_id}.name")
        description = values.get(f"{model_id}.description")
        moves = {
            key[len(model_id) + 7 : -6]: value
            for key, value in values.items()
            if key.startswith(f"{model_id}.moves.") and key.endswith(".title")
        }
        localized: dict[str, Any] = {}
        if title is not None:
            localized["title"] = title
        if description is not None:
            localized["description_template"] = description
        if moves:
            localized["move_titles"] = dict(sorted(moves.items()))
        if localized:
            result[locale] = localized
    return result


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )


def write_jsonl(path: Path, values: Iterable[dict[str, Any]]) -> int:
    rows = list(values)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as handle:
        for row in rows:
            handle.write(json.dumps(row, ensure_ascii=False, sort_keys=True) + "\n")
    return len(rows)


def decompile(game_data: Path, output: Path) -> None:
    ilspy = Path.home() / ".dotnet/tools/ilspycmd"
    if not ilspy.is_file():
        raise RuntimeError(f"ilspycmd is missing: {ilspy}")
    output.mkdir(parents=True, exist_ok=True)
    command = [
        str(ilspy),
        "--disable-updatecheck",
        "--nested-directories",
        "-p",
        "-o",
        str(output),
        "-r",
        str(game_data),
        str(game_data / "sts2.dll"),
    ]
    environment = os.environ | {
        "DOTNET_ROOT": "/opt/homebrew/opt/dotnet@9/libexec",
        "DOTNET_ROLL_FORWARD": "Major",
    }
    subprocess.run(command, check=True, env=environment)


def export() -> None:
    args = parse_args()
    game_data = args.game_app / "Contents/Resources/data_sts2_macos_arm64"
    assembly = game_data / "sts2.dll"
    pck = args.game_app / "Contents/Resources/Slay the Spire 2.pck"
    release_info = args.game_app / "Contents/Resources/release_info.json"
    for required in (assembly, pck, release_info):
        if not required.is_file():
            raise RuntimeError(f"required game file is missing: {required}")

    if not args.skip_decompile:
        decompile(game_data, args.decompiled_root)
    if not (args.decompiled_root / "MegaCrit/sts2/Core/Models").is_dir():
        raise RuntimeError(f"decompiled model tree is missing: {args.decompiled_root}")

    print("[knowledge] extracting English and Simplified Chinese localization")
    locales = {
        "eng": extract_locale_map(pck, 1),
        "zhs": extract_locale_map(pck, -1),
    }

    compendium = http_json(f"http://127.0.0.1:{args.port}/api/v1/compendium")
    sections = compendium.get("sections", {})
    card_ids = sorted(sections["card_library"]["discovered_ids"])
    relic_ids = sorted(sections["relic_collection"]["discovered_ids"])
    potion_ids = sorted(sections["potion_lab"]["discovered_ids"])
    card_runtime: dict[str, dict[str, Any]] = {}
    for number, model_id in enumerate(card_ids, start=1):
        if number % 50 == 0 or number == len(card_ids):
            print(f"[knowledge] cards {number}/{len(card_ids)}")
        card_runtime[model_id] = wiki_exact(args.port, "card", model_id)

    relic_runtime: dict[str, dict[str, Any]] = {}
    for number, model_id in enumerate(relic_ids, start=1):
        if number % 50 == 0 or number == len(relic_ids):
            print(f"[knowledge] relics {number}/{len(relic_ids)}")
        relic_runtime[model_id] = wiki_exact(args.port, "relic", model_id)

    localized_by_name = localized_model_ids(locales)

    def source_catalog(
        kind: str,
        runtime: dict[str, dict[str, Any]],
    ) -> list[dict[str, Any]]:
        namespace = MODEL_NAMESPACES[kind]
        model_root = args.decompiled_root / "MegaCrit/sts2/Core/Models" / namespace
        runtime_by_name = {
            normalized_name(model_id): model_id for model_id in runtime
        }
        records: list[dict[str, Any]] = []
        for path in sorted(model_root.rglob("*.cs")):
            model_id = resolve_source_model_id(
                path, runtime_by_name, localized_by_name
            )
            runtime_value = runtime.get(model_id)
            records.append(
                {
                    "schema_version": SCHEMA_VERSION,
                    "game_version": GAME_VERSION,
                    "game_commit": GAME_COMMIT,
                    "kind": kind,
                    "model_id": f"{kind.upper()}.{model_id}",
                    "id": model_id,
                    "localized": localize(locales, model_id),
                    "profile_discovered": runtime_value is not None,
                    "internal_or_undiscovered": runtime_value is None,
                    "runtime": runtime_value,
                    "source": source_facts(
                        path, args.decompiled_root, kind
                    ),
                }
            )
        return records

    cards = source_catalog("card", card_runtime)
    relics = source_catalog("relic", relic_runtime)

    potion_runtime = {model_id: {} for model_id in potion_ids}
    potions = source_catalog("potion", potion_runtime)

    monster_names = {
        normalized_name(key[:-5]): key[:-5]
        for values in locales.values()
        for key in values
        if key.endswith(".name")
    }
    enemies: list[dict[str, Any]] = []
    monster_root = args.decompiled_root / "MegaCrit/sts2/Core/Models/Monsters"
    for path in sorted(monster_root.rglob("*.cs")):
        inferred = pascal_to_model_id(path.stem)
        model_id = monster_names.get(normalized_name(inferred), inferred)
        localized = localize(locales, model_id)
        enemies.append(
            {
                "schema_version": SCHEMA_VERSION,
                "game_version": GAME_VERSION,
                "game_commit": GAME_COMMIT,
                "kind": "enemy",
                "model_id": f"MONSTER.{model_id}",
                "id": model_id,
                "localized": localized,
                "internal_or_test_model": (
                    "Mocks" in path.parts
                    or "Deprecated" in path.stem
                    or not localized
                ),
                "source": source_facts(path, args.decompiled_root, "enemy"),
            }
        )

    keyword_specs = (
        ("BOUND", "BOUND"),
        ("ETHEREAL", "ETHEREAL"),
        ("EXHAUST", "EXHAUST"),
        ("FATAL", "FATAL"),
        ("INNATE", "INNATE"),
        ("RETAIN", "RETAIN"),
        ("SLY", "SLY"),
        ("SOUL", "SOUL"),
        ("TAINTED", "TAINTED"),
        ("UNPLAYABLE", "UNPLAYABLE"),
        ("VULNERABLE", "VULNERABLE_POWER"),
        ("WEAK", "WEAK_POWER"),
        ("STRENGTH", "STRENGTH_POWER"),
    )
    keywords = [
        {
            "schema_version": SCHEMA_VERSION,
            "game_version": GAME_VERSION,
            "game_commit": GAME_COMMIT,
            "kind": "keyword",
            "id": keyword_id,
            "localization_id": localization_id,
            "localized": localize(locales, localization_id),
        }
        for keyword_id, localization_id in keyword_specs
        if localize(locales, localization_id)
    ]
    mechanics = verified_mechanics(args.decompiled_root)

    counts = {
        "cards": write_jsonl(args.output / "cards.jsonl", cards),
        "relics": write_jsonl(args.output / "relics.jsonl", relics),
        "potions": write_jsonl(args.output / "potions.jsonl", potions),
        "enemies": write_jsonl(args.output / "enemies.jsonl", enemies),
        "keywords": write_jsonl(args.output / "keywords.jsonl", keywords),
        "mechanics": write_jsonl(args.output / "mechanics.jsonl", mechanics),
    }
    coverage = {
        "profile_discovered_cards": len(card_runtime),
        "source_card_models": len(cards),
        "cards_with_source": sum(item["source"]["matched"] for item in cards),
        "cards_with_runtime_text": sum(item["profile_discovered"] for item in cards),
        "cards_with_eng_text": sum("eng" in item["localized"] for item in cards),
        "cards_with_zhs_text": sum("zhs" in item["localized"] for item in cards),
        "profile_discovered_relics": len(relic_runtime),
        "source_relic_models": len(relics),
        "relics_with_source": sum(item["source"]["matched"] for item in relics),
        "relics_with_runtime_text": sum(item["profile_discovered"] for item in relics),
        "profile_discovered_potions": len(potion_ids),
        "source_potion_models": len(potions),
        "potions_with_source": sum(item["source"]["matched"] for item in potions),
        "enemy_public_models": sum(not item["internal_or_test_model"] for item in enemies),
        "enemy_internal_or_test_models": sum(item["internal_or_test_model"] for item in enemies),
        "verified_cross_model_mechanics": len(mechanics),
    }
    manifest = {
        "schema_version": SCHEMA_VERSION,
        "game_version": GAME_VERSION,
        "game_commit": GAME_COMMIT,
        "release_channel": "stable",
        "sources": {
            "assembly": {
                "name": "sts2.dll",
                "sha256": sha256_file(assembly),
                "method": "ILSpy 9.1 decompilation and normalized source facts",
            },
            "localization": {
                "name": "Slay the Spire 2.pck",
                "sha256": sha256_file(pck),
                "locales": ["eng", "zhs"],
            },
            "runtime": {
                "endpoint": f"http://127.0.0.1:{args.port}/api/v1/wiki",
                "scope": "active_profile_discovered_cards_and_relics",
                "profile_id": compendium.get("profile_id"),
                "note": "Exact per-ID lookup for the active profile subset. Every decompiled model is still indexed; profile_discovered marks which records received runtime-resolved text.",
            },
        },
        "counts": counts,
        "coverage": coverage,
    }
    write_json(args.output / "manifest.json", manifest)
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    export()
