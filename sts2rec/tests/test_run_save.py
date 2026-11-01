import copy
import json
import struct
from pathlib import Path

import pytest

from sts2rec.run_save import (
    ANCIENT_FINISHED,
    ENTERING_MAP_POINT,
    NO_VISITED_COORD,
    POST_COMBAT_REWARDS,
    RunSaveError,
    find_run_saves,
    float32,
    load_run_save,
    parse_run_save,
    strip_category,
    unsupported_reasons,
)

FIXTURE = (
    Path(__file__).resolve().parents[2]
    / "sim"
    / "Sts2Sim.Tests"
    / "Fixtures"
    / "run_save_a8_silent_boss1.save"
)


@pytest.fixture()
def raw() -> dict:
    return json.loads(FIXTURE.read_text(encoding="utf-8"))


def test_fixture_fields() -> None:
    save = load_run_save(FIXTURE)
    assert save.schema_version == 20
    assert save.seed == "G1YFC196B2H2"
    assert save.ascension == 8
    assert [a.id for a in save.acts] == ["UNDERDOCKS", "HIVE", "GLORY"]
    assert save.acts[0].has_saved_map and not save.acts[1].has_saved_map
    assert save.acts[0].boss_id == "SOUL_FYSH_BOSS"
    assert save.acts[0].ancient_id == "NEOW"
    assert len(save.acts[0].normal_encounter_ids) == 15
    assert save.acts[0].visited["normal_encounters_visited"] == 4
    assert len(save.visited_map_coords) == 17
    assert save.floors == 17
    assert save.rngs["up_front"].counter == 407
    assert save.rngs["up_front"].s0 == 13230861342643808336
    assert save.unknown_map_point_odds["shop"] == float32(0.14999999)
    assert save.unknown_map_point_odds["elite"] == -5.0
    assert len(save.shared_relic_grab_bag["uncommon"]) == 30
    assert save.entry_point == POST_COMBAT_REWARDS
    assert unsupported_reasons(save) == []
    player = save.players[0]
    assert player.character_id == "SILENT"
    assert (player.current_hp, player.max_hp, player.gold, player.max_potion_slot_count) == (28, 75, 278, 2)
    assert len(player.deck) == 22
    taunt = next(c for c in player.deck if c.id == "TAUNT")
    assert (taunt.upgrade_level, taunt.enchantment_id, taunt.enchantment_amount) == (1, "NIMBLE", 2)
    assert [r.id for r in player.relics if r.has_props] == ["GIRYA"]
    assert player.rng_seed == 7378453647942262644
    assert player.rngs["rewards"].counter == 96
    assert player.card_rarity_odds == float32(-0.05)
    assert player.potion_reward_odds == float32(0.20000002)


def test_float32_rounds_to_single() -> None:
    assert float32(0.1) == struct.unpack("<f", struct.pack("<f", 0.1))[0]
    assert float32(0.1) != 0.1


def test_entry_points(raw: dict) -> None:
    no_room = copy.deepcopy(raw)
    del no_room["pre_finished_room"]
    assert parse_run_save(no_room).entry_point == ENTERING_MAP_POINT
    no_room["visited_map_coords"] = []
    assert parse_run_save(no_room).entry_point == NO_VISITED_COORD
    ancient = copy.deepcopy(raw)
    ancient["pre_finished_room"] = {"room_type": "event", "event_id": "EVENT.PAEL", "is_pre_finished": True}
    assert parse_run_save(ancient).entry_point == ANCIENT_FINISHED


def test_rejects_other_schema_and_reports_modes(raw: dict) -> None:
    old = copy.deepcopy(raw)
    old["schema_version"] = 19
    with pytest.raises(RunSaveError, match="schema_version 19"):
        parse_run_save(old)
    custom = copy.deepcopy(raw)
    custom["game_mode"] = "custom"
    custom["modifiers"] = [{"id": "MODIFIER.DRAFT"}]
    assert unsupported_reasons(parse_run_save(custom)) == [
        "game_mode custom is not standard",
        "modifier DRAFT is not supported",
    ]


def test_find_run_saves_covers_vanilla_and_modded(tmp_path: Path) -> None:
    vanilla = tmp_path / "steam" / "123" / "profile1" / "saves"
    modded = tmp_path / "steam" / "123" / "modded" / "profile2" / "saves"
    for saves in (vanilla, modded):
        saves.mkdir(parents=True)
        (saves / "current_run.save").write_text("{}", encoding="utf-8")
    (modded / "current_run_mp.save").write_text("{}", encoding="utf-8")
    assert find_run_saves(tmp_path) == (vanilla / "current_run.save", modded / "current_run.save")
    assert len(find_run_saves(tmp_path, include_multiplayer=True)) == 3


def test_strip_category() -> None:
    assert strip_category("CARD.STRIKE_SILENT") == "STRIKE_SILENT"
    assert strip_category("SILENT6_EPOCH") == "SILENT6_EPOCH"
