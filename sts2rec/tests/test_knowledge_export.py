from __future__ import annotations

import importlib.util
import json
import mmap
import struct
from pathlib import Path

import pytest


SCRIPT = Path(__file__).resolve().parents[2] / "scripts/export_sts2_knowledge.py"
SPEC = importlib.util.spec_from_file_location("export_sts2_knowledge", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def test_pascal_to_model_id_handles_words_and_version_suffix() -> None:
    assert MODULE.pascal_to_model_id("SleightOfFlesh") == "SLEIGHT_OF_FLESH"
    assert MODULE.pascal_to_model_id("InfestedPrism") == "INFESTED_PRISM"
    assert MODULE.pascal_to_model_id("BattleFriendV1") == "BATTLE_FRIEND_V1"


def test_extract_block_balances_nested_braces() -> None:
    source = """
before
protected override void OnUpgrade()
{
    if (ready) { Upgrade(4); }
}
after
"""
    assert MODULE.extract_block(source, "protected override void OnUpgrade()") == (
        "protected override void OnUpgrade()\n"
        "{\n"
        "    if (ready) { Upgrade(4); }\n"
        "}"
    )


def test_localize_collects_names_descriptions_and_moves() -> None:
    locales = {
        "eng": {
            "INFESTED_PRISM.name": "Infested Prism",
            "INFESTED_PRISM.moves.JAB.title": "Jab",
        },
        "zhs": {
            "INFESTED_PRISM.name": "感染棱柱",
            "INFESTED_PRISM.moves.JAB.title": "刺击",
        },
    }
    localized = MODULE.localize(locales, "INFESTED_PRISM")
    assert localized["eng"]["title"] == "Infested Prism"
    assert localized["zhs"]["move_titles"] == {"JAB": "刺击"}


def test_source_facts_indexes_transitive_command_dependencies(
    tmp_path: Path,
) -> None:
    source = tmp_path / "MegaCrit/sts2/Core/Models/Cards/BoneShards.cs"
    source.parent.mkdir(parents=True)
    source.write_text(
        """
public class BoneShards
{
    public BoneShards() : base(1, CardType.Attack) {}
    public async Task Play()
    {
        await CreatureCmd.Kill(owner.Osty);
        await DamageCmd.Attack(9);
    }
}
""",
        encoding="utf-8",
    )

    facts = MODULE.source_facts(source, tmp_path, "card")
    assert facts["dependencies"] == [
        {"type": "CreatureCmd", "method": "Kill"},
        {"type": "DamageCmd", "method": "Attack"},
    ]


def test_verified_osty_mechanic_fails_closed_on_source_drift(
    tmp_path: Path,
) -> None:
    source = tmp_path / "MegaCrit/sts2/Core/Commands/OstyCmd.cs"
    source.parent.mkdir(parents=True)
    source.write_text(
        """
public static async Task<SummonResult> Summon(Player summoner, decimal amount)
{
    if (summoner.IsOstyAlive)
    {
        await CreatureCmd.GainMaxHp(summoner.Osty, amount);
    }
    else
    {
        await CreatureCmd.SetMaxHp(osty, amount);
        await CreatureCmd.Heal(osty, amount, isReviving);
    }
}
""",
        encoding="utf-8",
    )

    mechanic = MODULE.verified_mechanics(tmp_path)[0]
    assert mechanic["id"] == "OSTY_SUMMON_LIFECYCLE"
    assert "SetMaxHp(osty, amount)" in mechanic["source"]["method_source"]
    assert json.dumps(mechanic, ensure_ascii=False)

    source.write_text(source.read_text().replace("SetMaxHp", "GrowMaxHp"))
    with pytest.raises(RuntimeError, match="no longer matches source"):
        MODULE.verified_mechanics(tmp_path)


def test_core_paths_cover_nested_and_flat_ilspy_layouts(tmp_path: Path) -> None:
    nested = tmp_path / "nested"
    flat = tmp_path / "flat"
    for path in (
        nested / "MegaCrit/sts2/Core/Models/Monsters/Aeonglass.cs",
        nested / "MegaCrit/sts2/Core/Models/Monsters/Mocks/MockMonster.cs",
        nested / "MegaCrit/sts2/Core/Commands/OstyCmd.cs",
        flat / "MegaCrit.Sts2.Core.Models.Monsters/Aeonglass.cs",
        flat / "MegaCrit.Sts2.Core.Models.Monsters.Mocks/MockMonster.cs",
        flat / "MegaCrit.Sts2.Core.Models.MonstersExtra/NotAMonster.cs",
        flat / "MegaCrit.Sts2.Core.Commands/OstyCmd.cs",
    ):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("", encoding="utf-8")

    for root in (nested, flat):
        monsters = MODULE.core_files(root, "Models/Monsters")
        assert [path.stem for path in monsters] == ["Aeonglass", "MockMonster"]
        assert [MODULE.is_mock_source(path) for path in monsters] == [False, True]
        top = MODULE.core_files(root, "Models/Monsters", recursive=False)
        assert [path.stem for path in top] == ["Aeonglass"]
        assert MODULE.core_file(root, "Commands/OstyCmd.cs").is_file()
    assert str(MODULE.core_file(flat, "Commands/OstyCmd.cs").relative_to(flat)) == (
        "MegaCrit.Sts2.Core.Commands/OstyCmd.cs"
    )


def write_pck(path: Path, files: dict[str, bytes]) -> None:
    """Minimal Godot PCK format 3: header, file data, then the directory."""

    header_size = 4 + 5 * 4 + 8 + 8 + 16 * 4
    data = b"".join(files.values())
    directory = struct.pack("<I", len(files))
    offset = 0
    for name, content in files.items():
        encoded = name.encode()
        encoded += b"\0" * (-len(encoded) % 4)
        directory += struct.pack("<I", len(encoded)) + encoded
        directory += struct.pack("<QQ", offset, len(content)) + b"\0" * 16
        directory += struct.pack("<I", 0)
        offset += len(content)
    header = b"GDPC" + struct.pack("<5I", 3, 4, 5, 1, 2)
    header += struct.pack("<QQ", header_size, header_size + len(data))
    header += b"\0" * 64
    path.write_bytes(header + data + directory)


def test_locale_map_follows_pck_directory_not_position(tmp_path: Path) -> None:
    def table(text: str) -> bytes:
        values = {key: f"{text} {key}" for key in MODULE.LOCALIZATION_OBJECT_SENTINELS}
        return ("{\n" + json.dumps(values, ensure_ascii=False)[1:] + "\n").encode()

    pck = tmp_path / "game.pck"
    # zht sorts after zhs, so the legacy "last occurrence" is no longer zhs.
    write_pck(
        pck,
        {
            "localization/deu/all.json": table("Schlag"),
            "localization/eng/all.json": table("Strike"),
            "localization/zhs/all.json": table("打击"),
            "localization/zht/all.json": table("打擊"),
        },
    )
    with pck.open("rb") as handle, mmap.mmap(
        handle.fileno(), 0, access=mmap.ACCESS_READ
    ) as mapped:
        assert set(MODULE.pck_locale_ranges(mapped)) == {"deu", "eng", "zhs", "zht"}
    assert MODULE.extract_locale_map(pck, "eng")["FATAL.description"] == "Strike FATAL.description"
    assert MODULE.extract_locale_map(pck, "zhs")["FATAL.description"] == "打击 FATAL.description"
    assert MODULE.extract_locale_map(pck, -1)["FATAL.description"] == "打擊 FATAL.description"
    with pytest.raises(RuntimeError, match="no localization/jpn/"):
        MODULE.extract_locale_map(pck, "jpn")


def test_output_guard_protects_localization_exports(tmp_path: Path) -> None:
    MODULE.guard_output(tmp_path / "missing")  # a new directory is fine
    MODULE.guard_output(tmp_path)  # so is an empty one

    by_manifest = tmp_path / "by-manifest"
    by_manifest.mkdir()
    (by_manifest / "manifest.json").write_text(
        json.dumps({"generator": MODULE.LOCALIZATION_GENERATOR}), encoding="utf-8"
    )
    with pytest.raises(RuntimeError, match="refusing to write"):
        MODULE.guard_output(by_manifest)
    MODULE.guard_output(by_manifest, overwrite_localization_export=True)

    by_table = tmp_path / "by-table"
    by_table.mkdir()
    (by_table / "powers.jsonl").write_text("", encoding="utf-8")
    with pytest.raises(RuntimeError, match="powers.jsonl"):
        MODULE.guard_output(by_table)

    own = tmp_path / "own"
    own.mkdir()
    (own / "manifest.json").write_text(json.dumps({"schema_version": 1}), encoding="utf-8")
    (own / "cards.jsonl").write_text("", encoding="utf-8")
    MODULE.guard_output(own)  # this exporter's own output may be regenerated


def test_committed_localization_export_is_guarded() -> None:
    source = SCRIPT.parents[1] / "knowledge/source"
    assert MODULE.localization_export_markers(source / "v0.111.0")
    assert not MODULE.localization_export_markers(source / "v0.107.1")
