#!/usr/bin/env python3
"""Export versioned STS2 facts from the live model registry and local game source.

The active profile supplies canonical, currently registered card/relic/potion IDs.
The MCP wiki supplies localized, fully resolved card/relic rules text. The local
PCK supplies English and Simplified Chinese localization templates. Decompiled
source supplies model classes, numeric expressions, upgrades, and enemy move
state machines. The game version and commit come from the installed build's
release_info.json. `--offline` skips the live MCP and exports source plus PCK
facts only. Community opinions intentionally live elsewhere.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import mmap
import os
import re
import struct
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request
from collections.abc import Iterable
from pathlib import Path
from typing import Any


# Defaults for callers that do not pass the installed build's identity.
GAME_VERSION = "0.107.1"
GAME_COMMIT = "59260271"
SCHEMA_VERSION = 1
# ILSpy writes one of two project layouts: `--nested-directories` nests every
# namespace segment (MegaCrit/sts2/Core/Models/Cards/), while plain `-p` writes
# one folder per namespace (MegaCrit.Sts2.Core.Models.Cards/).
NESTED_CORE = "MegaCrit/sts2/Core"
FLAT_CORE = "MegaCrit.Sts2.Core"
# scripts/export_sts2_localization.py writes cards/relics/potions/keywords/enemies
# (plus powers/events) into the same knowledge/source/v<version> directories and
# stamps itself as the manifest's "generator". This exporter writes the same
# file names with a different row set and manifest, so a rerun into such a
# directory would silently replace the files the simulator's RulesText and the
# Docker image load. export() refuses unless --overwrite-localization-export.
LOCALIZATION_GENERATOR = "scripts/export_sts2_localization.py"
LOCALIZATION_ONLY_FILES = ("events.jsonl", "powers.jsonl")
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
        default=None,
        help="Default: artifacts/knowledge/decompiled-v<installed version>.",
    )
    parser.add_argument(
        "--output",
        type=Path,
        default=None,
        help="Default: knowledge/source/v<installed version>.",
    )
    parser.add_argument(
        "--skip-decompile",
        action="store_true",
        help="Require an existing decompiled tree instead of regenerating it.",
    )
    parser.add_argument(
        "--offline",
        action="store_true",
        help="Do not query a live MCP. Runtime text is omitted and every "
        "record has profile_discovered=false.",
    )
    parser.add_argument(
        "--release-channel",
        default="stable",
        help="Steam branch of the installed build, e.g. public-beta.",
    )
    parser.add_argument(
        "--overwrite-localization-export",
        action="store_true",
        help="Allow writing into an --output directory that holds files "
        f"produced by {LOCALIZATION_GENERATOR} (refused by default).",
    )
    return parser.parse_args()


def localization_export_markers(output: Path) -> list[str]:
    """Evidence that export_sts2_localization.py produced files in `output`."""

    markers: list[str] = []
    manifest = output / "manifest.json"
    if manifest.is_file():
        try:
            generator = json.loads(manifest.read_text(encoding="utf-8")).get("generator")
        except (OSError, ValueError, AttributeError):
            generator = None
        if generator == LOCALIZATION_GENERATOR:
            markers.append(f"manifest.json generator {generator}")
    markers += [name for name in LOCALIZATION_ONLY_FILES if (output / name).is_file()]
    return markers


def guard_output(output: Path, overwrite_localization_export: bool = False) -> None:
    """Refuse to clobber a directory written by export_sts2_localization.py."""

    markers = localization_export_markers(output)
    if markers and not overwrite_localization_export:
        raise RuntimeError(
            f"refusing to write into {output}: it holds files produced by "
            f"{LOCALIZATION_GENERATOR} ({', '.join(markers)}) that this exporter "
            "would overwrite with a different row set and manifest. Export to a "
            "scratch --output and merge fields by id (knowledge/README.md), or "
            "pass --overwrite-localization-export to replace them on purpose."
        )


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


def pck_locale_ranges(mapped: mmap.mmap) -> dict[str, list[tuple[int, int]]]:
    """Map each locale code to the byte ranges of its localization/<code>/*.json.

    Reads the Godot PCK directory (format 2 or 3, unencrypted): u32 path length,
    zero-padded path, u64 offset, u64 size, 16-byte MD5, u32 flags per entry.
    """

    if bytes(mapped[:4]) != b"GDPC":
        raise RuntimeError("not a Godot PCK file")
    pack_version, _major, _minor, _patch, pack_flags = struct.unpack_from(
        "<5I", mapped, 4
    )
    if pack_flags & 1:
        raise RuntimeError("PCK directory is encrypted")
    (file_base,) = struct.unpack_from("<Q", mapped, 24)
    if pack_version == 2:
        position = 96
    elif pack_version == 3:
        (position,) = struct.unpack_from("<Q", mapped, 32)
    else:
        raise RuntimeError(f"unsupported PCK format version {pack_version}")
    (count,) = struct.unpack_from("<I", mapped, position)
    position += 4
    ranges: dict[str, list[tuple[int, int]]] = {}
    for _ in range(count):
        (length,) = struct.unpack_from("<I", mapped, position)
        raw_path = bytes(mapped[position + 4 : position + 4 + length])
        offset, size = struct.unpack_from("<QQ", mapped, position + 4 + length)
        position += 4 + length + 16 + 16 + 4
        parts = raw_path.rstrip(b"\0").decode("utf-8").removeprefix("res://").split("/")
        if len(parts) == 3 and parts[0] == "localization" and parts[2].endswith(".json"):
            start = file_base + offset
            ranges.setdefault(parts[1], []).append((start, start + size))
    return ranges


def extract_locale_map(pck_path: Path, occurrence_index: int | str) -> dict[str, str]:
    """Merge the relevant localization objects for one locale.

    The PCK stores uncompressed JSON objects, but their physical order is not a
    single locale block. Each stable sentinel occurs once per supported locale.
    A locale code ("eng", "zhs") selects the occurrence inside that locale's
    localization/<code>/ files according to the PCK directory. An integer keeps
    the legacy positional choice; positions shift when the game adds a locale
    (v0.111.0 added zht after zhs, so -1 is no longer Simplified Chinese).
    """

    with pck_path.open("rb") as handle:
        mapped = mmap.mmap(handle.fileno(), 0, access=mmap.ACCESS_READ)
        try:
            merged: dict[str, str] = {}
            decoder = json.JSONDecoder()
            locale_ranges: list[tuple[int, int]] | None = None
            if isinstance(occurrence_index, str):
                locale_ranges = pck_locale_ranges(mapped).get(occurrence_index)
                if not locale_ranges:
                    raise RuntimeError(
                        f"PCK has no localization/{occurrence_index}/ files"
                    )
            for sentinel in LOCALIZATION_OBJECT_SENTINELS:
                occurrences = find_all(mapped, f'"{sentinel}"'.encode())
                if locale_ranges is not None:
                    inside = [
                        offset
                        for offset in occurrences
                        if any(start <= offset < end for start, end in locale_ranges)
                    ]
                    if len(inside) != 1:
                        raise RuntimeError(
                            f"localization sentinel {sentinel} appeared "
                            f"{len(inside)} times in locale {occurrence_index}"
                        )
                    center = inside[0]
                else:
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


def core_file(decompiled_root: Path, relative: str) -> Path:
    """Locate a Core source file ("Commands/OstyCmd.cs") in either ILSpy layout."""

    nested = decompiled_root / NESTED_CORE / relative
    if nested.is_file():
        return nested
    folder, _, name = relative.rpartition("/")
    return decompiled_root / f"{FLAT_CORE}.{folder.replace('/', '.')}" / name


def core_files(
    decompiled_root: Path, namespace: str, *, recursive: bool = True
) -> list[Path]:
    """Sorted .cs files of a Core namespace ("Models/Cards"), in either layout.

    With recursive=True sub-namespaces such as Models/Cards/Mocks are included.
    """

    nested = decompiled_root / NESTED_CORE / namespace
    if nested.is_dir():
        return sorted(nested.rglob("*.cs") if recursive else nested.glob("*.cs"))
    flat = f"{FLAT_CORE}.{namespace.replace('/', '.')}"
    folders = [decompiled_root / flat]
    if recursive:
        folders += [
            folder
            for folder in decompiled_root.glob(f"{flat}.*")
            if folder.is_dir()
        ]
    return sorted(path for folder in folders for path in folder.glob("*.cs"))


def is_mock_source(path: Path) -> bool:
    return "Mocks" in path.parts or "Mocks" in path.parent.name.split(".")


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


def verified_mechanics(
    decompiled_root: Path,
    game_version: str = GAME_VERSION,
    game_commit: str = GAME_COMMIT,
) -> list[dict[str, Any]]:
    """Extract source-backed cross-model contracts that affect decisions.

    These records are intentionally small and fail closed on version drift.  A
    prose invariant is emitted only while all of its required implementation
    fragments are still present in the pinned game's decompiled source.
    """

    records: list[dict[str, Any]] = []
    for spec in MECHANIC_SPECS:
        path = core_file(
            decompiled_root,
            str(spec["relative_path"]).removeprefix(NESTED_CORE + "/"),
        )
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
                "game_version": game_version,
                "game_commit": game_commit,
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
                    "decompiled_path": str(path.relative_to(decompiled_root)),
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
    release = json.loads(release_info.read_text(encoding="utf-8"))
    game_version = str(release["version"]).lstrip("v")
    game_commit = str(release["commit"])
    if args.decompiled_root is None:
        args.decompiled_root = Path(f"artifacts/knowledge/decompiled-v{game_version}")
    if args.output is None:
        args.output = Path(f"knowledge/source/v{game_version}")
    if args.output.name.startswith("v") and args.output.name != f"v{game_version}":
        raise RuntimeError(
            f"output {args.output} does not match installed build v{game_version}"
        )
    guard_output(args.output, args.overwrite_localization_export)

    if not args.skip_decompile:
        decompile(game_data, args.decompiled_root)
    if not core_files(args.decompiled_root, "Models/Cards"):
        raise RuntimeError(f"decompiled model tree is missing: {args.decompiled_root}")

    print("[knowledge] extracting English and Simplified Chinese localization")
    locales = {
        "eng": extract_locale_map(pck, "eng"),
        "zhs": extract_locale_map(pck, "zhs"),
    }

    compendium: dict[str, Any] = {}
    card_ids: list[str] = []
    relic_ids: list[str] = []
    potion_ids: list[str] = []
    if not args.offline:
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
        runtime_by_name = {
            normalized_name(model_id): model_id for model_id in runtime
        }
        records: list[dict[str, Any]] = []
        for path in core_files(args.decompiled_root, f"Models/{namespace}"):
            model_id = resolve_source_model_id(
                path, runtime_by_name, localized_by_name
            )
            runtime_value = runtime.get(model_id)
            records.append(
                {
                    "schema_version": SCHEMA_VERSION,
                    "game_version": game_version,
                    "game_commit": game_commit,
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
    for path in core_files(args.decompiled_root, "Models/Monsters"):
        inferred = pascal_to_model_id(path.stem)
        model_id = monster_names.get(normalized_name(inferred), inferred)
        localized = localize(locales, model_id)
        enemies.append(
            {
                "schema_version": SCHEMA_VERSION,
                "game_version": game_version,
                "game_commit": game_commit,
                "kind": "enemy",
                "model_id": f"MONSTER.{model_id}",
                "id": model_id,
                "localized": localized,
                "internal_or_test_model": (
                    is_mock_source(path)
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
            "game_version": game_version,
            "game_commit": game_commit,
            "kind": "keyword",
            "id": keyword_id,
            "localization_id": localization_id,
            "localized": localize(locales, localization_id),
        }
        for keyword_id, localization_id in keyword_specs
        if localize(locales, localization_id)
    ]
    mechanics = verified_mechanics(args.decompiled_root, game_version, game_commit)

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
    runtime_source: dict[str, Any] = {
        "endpoint": f"http://127.0.0.1:{args.port}/api/v1/wiki",
        "scope": "active_profile_discovered_cards_and_relics",
        "profile_id": compendium.get("profile_id"),
        "note": "Exact per-ID lookup for the active profile subset. Every decompiled model is still indexed; profile_discovered marks which records received runtime-resolved text.",
    }
    if args.offline:
        runtime_source = {
            "scope": "none",
            "note": "Offline export without a live MCP: runtime is null and profile_discovered is false for every record. Localized templates come from the PCK; source facts from the decompiled assembly.",
        }
    manifest = {
        "schema_version": SCHEMA_VERSION,
        "game_version": game_version,
        "game_commit": game_commit,
        "release_channel": args.release_channel,
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
            "runtime": runtime_source,
        },
        "counts": counts,
        "coverage": coverage,
    }
    write_json(args.output / "manifest.json", manifest)
    print(json.dumps(manifest, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    export()
