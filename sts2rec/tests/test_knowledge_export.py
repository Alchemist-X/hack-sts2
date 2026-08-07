from __future__ import annotations

import importlib.util
import json
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
