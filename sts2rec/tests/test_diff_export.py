"""End-to-end tests for `sts2rec export-bundles` on a tiny synthetic corpus.

Two lineages, built record by record in the recorder's shapes:
- an Ironclad A10 v0.107.1 run (Neow's Talisman, NIBBITS_WEAK first combat)
  whose part 2 is a post-combat resume (no combat, must be skipped);
- a Silent custom-mode v0.111.0 run whose first combat is retried after a
  save&quit (two attempts, both ineligible for the sim).
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

from sts2rec.cli import main
from sts2rec.diff_bundle import SimRegistry
from sts2rec.diff_export import CorpusVocabulary, ExportError, _combat_end, export_bundles, extract_decisions
from sts2rec.session import ActionRecord, StateRecord

from .conftest import make_manifest, write_session
from .test_diff_bundle import (
    BANE,
    BASH,
    DEFEND,
    STRIKE,
    combat_state,
    enemy,
    hand_card,
    pile_card,
)

REGISTRY = {
    "characters": ["IRONCLAD"],
    "encounters": ["NIBBITS_WEAK"],
    "cards": [STRIKE, DEFEND, BASH, BANE],
    "upgradable_cards": [STRIKE, DEFEND, BASH],
}
PLAYER = {"is_player": True, "combat_id": 0, "hp": 64, "max_hp": 80, "block": 0}


class Stream:
    """Collects one part's records with a shared, increasing seq."""

    def __init__(self) -> None:
        self.records: dict[str, list[dict[str, Any]]] = {"states": [], "actions": [], "events": []}
        self.seq = 0

    def _next(self) -> int:
        self.seq += 1
        return self.seq

    def state(self, state: dict[str, Any], trigger: str = "phase") -> int:
        seq = self._next()
        self.records["states"].append(
            {"seq": seq, "t": float(seq), "type": "state", "trigger": trigger, "screen": state.get("state_type", "x"), "hash": f"h{seq}", "state": state}
        )
        return seq

    def action(self, kind: str, state_seq: int | None, **params: Any) -> int:
        seq = self._next()
        self.records["actions"].append(
            {"seq": seq, "t": float(seq), "type": "action", "source": "hook:Test", "action": {"kind": kind, "params": params}, "state_seq": state_seq, "status": "executed"}
        )
        return seq

    def event(self, entry: str, **data: Any) -> int:
        seq = self._next()
        self.records["events"].append({"seq": seq, "t": float(seq), "type": "event", "entry": entry, "data": data})
        return seq


def nibbit(hp: int, **kwargs: Any) -> dict[str, Any]:
    return {"is_player": False, "combat_id": 1, "model_id": "MONSTER.NIBBIT", "hp": hp, "max_hp": 46, "block": 0, **kwargs}


def card_event(card_id: str, entry: str, pile: str = "Discard") -> dict[str, Any]:
    return {"actor": PLAYER, "card_play": {"card": {"id": f"CARD.{card_id}"}, "is_auto_play": False, "result_pile": pile}}


def write_part(root: Path, name: str, stream: Stream, run: dict[str, Any], *, part: int, version: str, result=None, incomplete=True) -> Path:
    manifest = make_manifest(
        game={"version": version, "commit": "x", "build_id": ""},
        run=run,
        part=part,
        result=result,
        incomplete=incomplete,
        counts={k: len(v) for k, v in stream.records.items()},
    )
    return write_session(root / name, manifest, stream.records, with_native=False)


def ironclad_lineage(root: Path) -> None:
    run = {"seed": "IRONTEST01", "character": "IRONCLAD", "ascension": 10, "game_mode": "standard", "start_time": 1773000000.0}
    s = Stream()
    s.event("recorder_config")
    s.state({"state_type": "unknown", "player": {"relics": [{"id": "BURNING_BLOOD"}]}})
    s.event("room_entered", room_type="Event", room_model_id="EVENT.NEOW", act=0, floor=1)
    s.event(
        "floor_summary",
        map_point_type="ancient",
        rooms=[{"model_id": "EVENT.NEOW"}],
        player_stats=[
            {
                "relic_choices": [{"choice": "RELIC.NEOWS_TALISMAN", "was_picked": True}],
                "upgraded_cards": ["CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD"],
            }
        ],
    )
    s.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=2)
    relics = [{"id": "BURNING_BLOOD"}, {"id": "NEOWS_TALISMAN"}]
    deck_piles = [pile_card(c) for c in [STRIKE] * 4] + [pile_card(STRIKE, upgraded=True)] + [pile_card(DEFEND)] * 3
    deck_piles += [pile_card(DEFEND, upgraded=True), pile_card(BASH), pile_card(BANE)]
    start = combat_state(hand=[], draw=deck_piles, energy=0, play=False, enemies=[enemy(1, 46, intents=(("Attack", "13"),))])
    start["player"]["relics"] = relics
    s.state(start)
    draw6 = [pile_card(DEFEND)] * 3 + [pile_card(DEFEND, upgraded=True), pile_card(STRIKE), pile_card(STRIKE, upgraded=True)]
    bane = hand_card(BANE, 2, cost="0", can_play=False)
    turn1 = s.state(
        combat_state(
            hand=[hand_card(STRIKE, 0), hand_card(BASH, 1, cost="2"), bane, hand_card(STRIKE, 3), hand_card(STRIKE, 4)],
            draw=draw6,
            enemies=[enemy(1, 46, intents=(("Attack", "13"),))],
        )
    )
    s.action("play_card", turn1, card_model_id="CARD.BASH", combat_card_index=1, target_id=1)
    s.event("energy_spent", actor=PLAYER, amount=2)
    s.event("card_play_started", **card_event(BASH, "start"))
    mid = s.state(
        combat_state(
            hand=[hand_card(STRIKE, 0), {**bane, "index": 1}, hand_card(STRIKE, 2), hand_card(STRIKE, 3)],
            draw=draw6,
            energy=1,
            enemies=[enemy(1, 46, intents=(("Attack", "13"),))],
        ),
        trigger="action",
    )
    s.event("damage_received", actor=nibbit(38), result={"receiver": nibbit(38)}, dealer=PLAYER)
    s.event("power_received", actor=nibbit(38), power="POWER.VULNERABLE_POWER", amount=2)
    s.event("card_play_finished", **card_event(BASH, "finish"))
    s.action("play_card", mid, card_model_id="CARD.STRIKE_IRONCLAD", combat_card_index=3, target_id=1)
    s.event("energy_spent", actor=PLAYER, amount=1)
    s.event("card_play_started", **card_event(STRIKE, "start"))
    s.state(
        combat_state(
            hand=[hand_card(STRIKE, 0, can_play=False), {**bane, "index": 1}, hand_card(STRIKE, 2, can_play=False)],
            draw=draw6,
            discard=[pile_card(BASH)],
            energy=0,
            enemies=[enemy(1, 38, powers=(("VULNERABLE_POWER", 2),), intents=(("Attack", "13"),))],
        ),
        trigger="action",
    )
    s.event("damage_received", actor=nibbit(29), result={"receiver": nibbit(29)}, dealer=PLAYER)
    s.event("card_play_finished", **card_event(STRIKE, "finish"))
    settled = s.state(
        combat_state(
            hand=[hand_card(STRIKE, 0, can_play=False), {**bane, "index": 1}, hand_card(STRIKE, 2, can_play=False)],
            draw=draw6,
            discard=[pile_card(BASH), pile_card(STRIKE)],
            energy=0,
            enemies=[enemy(1, 29, powers=(("VULNERABLE_POWER", 2),), intents=(("Attack", "13"),))],
        ),
        trigger="poll",
    )
    s.action("end_turn", settled, turn_number=1)
    s.action("ready_to_begin_enemy_turn", settled, action_type="ReadyToBeginEnemyTurnAction")
    s.event("card_exhausted", actor=PLAYER, card={"id": "CARD.ASCENDERS_BANE"})
    s.event("damage_received", actor={**PLAYER, "hp": 51}, result={"receiver": {**PLAYER, "hp": 51}})
    turn2 = s.state(
        combat_state(
            hand=[
                hand_card(STRIKE, 0, upgraded=True),
                hand_card(DEFEND, 1, upgraded=True),
                hand_card(DEFEND, 2),
                hand_card(DEFEND, 3),
                hand_card(STRIKE, 4),
            ],
            draw=[pile_card(DEFEND)],
            discard=[pile_card(BASH)] + [pile_card(STRIKE)] * 3,
            exhaust=[pile_card(BANE)],
            hp=51,
            turn=2,
            enemies=[enemy(1, 29, powers=(("VULNERABLE_POWER", 1),), intents=(("Attack", "7"), ("Defend", "")))],
        )
    )
    s.action("play_card", turn2, card_model_id="CARD.STRIKE_IRONCLAD", combat_card_index=6, target_id=1)
    s.event("energy_spent", actor={**PLAYER, "hp": 51}, amount=1)
    s.event("card_play_started", **card_event(STRIKE, "start"))
    s.state(
        combat_state(
            hand=[hand_card(DEFEND, 0, upgraded=True), hand_card(DEFEND, 1), hand_card(DEFEND, 2), hand_card(STRIKE, 3)],
            draw=[pile_card(DEFEND)],
            discard=[pile_card(BASH)] + [pile_card(STRIKE)] * 3,
            exhaust=[pile_card(BANE)],
            hp=51,
            energy=2,
            turn=2,
            enemies=[enemy(1, 29, powers=(("VULNERABLE_POWER", 1),), intents=(("Attack", "7"), ("Defend", "")))],
        ),
        trigger="action",
    )
    s.event("damage_received", actor=nibbit(0), result={"receiver": nibbit(0), "was_target_killed": True})
    s.event("card_play_finished", **{**card_event(STRIKE, "finish"), "actor": {**PLAYER, "hp": 51}})
    s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": {"hp": 57}})
    rewards = s.state({"state_type": "rewards", "rewards": {"items": []}, "player": {"hp": 57}})
    s.action("reward_taken", rewards, reward_type="gold_reward")
    # The game is quit on the rewards screen: part 2 resumes there.
    write_part(root, "1773000000-IRONTEST01", s, run, part=1, version="v0.107.1")

    resume = Stream()
    resume.event("recorder_config")
    resume.state({"state_type": "unknown", "player": {}})
    resume.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=2)
    resume.state({"state_type": "rewards", "rewards": {"items": []}, "player": {"hp": 57}})
    resume.event("room_entered", room_type="Treasure", room_model_id=None, act=0, floor=3)
    write_part(
        root,
        "1773000000-IRONTEST01-part2",
        resume,
        run,
        part=2,
        version="v0.107.1",
        result={"win": False, "abandoned": True, "end_time": 1773000500.0},
        incomplete=False,
    )


SILENT_HAND = ["STRIKE_SILENT", "DEFEND_SILENT", "NEUTRALIZE", "SURVIVOR", "STRIKE_SILENT"]
SILENT_DECK = ["STRIKE_SILENT"] * 5 + ["DEFEND_SILENT"] * 5 + ["NEUTRALIZE", "SURVIVOR"]


def silent_attempt(stream: Stream, *, win: bool) -> None:
    names = {"STRIKE_SILENT": "打击", "DEFEND_SILENT": "防御", "NEUTRALIZE": "中和", "SURVIVOR": "生存者"}

    def card(card_id: str, index: int) -> dict[str, Any]:
        return {"id": card_id, "name": names[card_id], "cost": "1", "is_upgraded": False, "index": index, "can_play": True}

    stream.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.SLIMES_WEAK", act=0, floor=2)
    start = combat_state(hand=[], draw=[{"name": names[c]} for c in SILENT_DECK], energy=0, play=False)
    start["player"]["relics"] = [{"id": "RING_OF_THE_SNAKE"}]
    stream.state(start)
    turn1 = stream.state(
        combat_state(hand=[card(c, i) for i, c in enumerate(SILENT_HAND)], draw=[{"name": "防御"}] * 4 + [{"name": "打击"}] * 3)
    )
    stream.action("play_card", turn1, card_model_id="CARD.NEUTRALIZE", combat_card_index=2, target_id=1)
    if win:
        stream.event("card_play_finished", **card_event("NEUTRALIZE", "finish"))
        stream.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": {"hp": 70}})


def silent_lineage(root: Path) -> None:
    run = {"seed": "SILTEST001AB", "character": "SILENT", "ascension": 0, "game_mode": "custom", "start_time": 1773100000.0}
    first = Stream()
    first.event("room_entered", room_type="Event", room_model_id="EVENT.NEOW", act=0, floor=1)
    first.event(
        "floor_summary",
        map_point_type="ancient",
        rooms=[{"model_id": "EVENT.NEOW"}],
        player_stats=[{"relic_choices": [{"choice": "RELIC.GOLDEN_PEARL", "was_picked": True}]}],
    )
    silent_attempt(first, win=False)
    write_part(root, "1773100000-SILTEST001AB", first, run, part=1, version="v0.111.0")
    retry = Stream()
    silent_attempt(retry, win=True)
    write_part(root, "1773100000-SILTEST001AB-part2", retry, run, part=2, version="v0.111.0")


@pytest.fixture
def corpus(tmp_path: Path) -> Path:
    root = tmp_path / "sessions"
    ironclad_lineage(root)
    silent_lineage(root)
    return root


def registry() -> SimRegistry:
    return SimRegistry.from_json(REGISTRY, source="test")


def test_export_writes_the_eligible_first_combat_bundle(corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    index = export_bundles(corpus, out, registry())
    summary = index["summary"]
    assert summary["attempts"] == 3
    assert summary["eligible"] == 1 and summary["eligible_by_version"] == {"v0.107.1": 1}
    assert summary["blocked_only_by_sim_coverage"] == 0

    path = out / "bundles" / "v0.107.1" / "1773000000-IRONTEST01.attempt1.json"
    bundle = json.loads(path.read_text(encoding="utf-8"))
    assert bundle["bundle_version"] == 1
    assert bundle["source"] == {"session": "1773000000-IRONTEST01", "part": "1773000000-IRONTEST01", "attempt": 1, "act": 1, "floor": 2}
    assert (bundle["game_version"], bundle["seeding"], bundle["seed"]) == ("v0.107.1", "legacy_v18", "IRONTEST01")
    assert (bundle["character"], bundle["ascension"], bundle["encounter_id"]) == ("IRONCLAD", 10, "NIBBITS_WEAK")
    assert bundle["player"] == {"hp": 64, "max_hp": 80}
    assert [(c["id"], c["upgraded"]) for c in bundle["deck"]] == (
        [(STRIKE, False)] * 4 + [(STRIKE, True)] + [(DEFEND, False)] * 3 + [(DEFEND, True), (BASH, False), (BANE, False)]
    )
    assert bundle["relics"] == ["BURNING_BLOOD", "NEOWS_TALISMAN"]
    assert bundle["rng_offsets"] == {"Niche": 0}
    assert (bundle["outcome"], bundle["final_hp"]) == ("win", 51)

    decisions = bundle["decisions"]
    assert [d["action"] for d in decisions] == [
        {"type": "play_card", "hand_index": 1, "card_id": BASH, "upgraded": False, "target_combat_id": 1},
        {"type": "play_card", "hand_index": 2, "card_id": STRIKE, "upgraded": False, "target_combat_id": 1},
        {"type": "end_turn"},
        {"type": "play_card", "hand_index": 0, "card_id": STRIKE, "upgraded": True, "target_combat_id": 1},
    ]
    assert [d["index"] for d in decisions] == [0, 1, 2, 3]
    # Decision 1's snapshot was taken mid-Bash; it is rolled forward.
    rolled = decisions[1]
    assert rolled["oracle"]["settled"] is False and rolled["oracle"]["rolled_forward"] is True
    assert rolled["pre_state"]["enemies"][0]["hp"] == 38
    assert rolled["pre_state"]["enemies"][0]["powers"] == [{"id": "VULNERABLE_POWER", "amount": 2}]
    assert rolled["pre_state"]["player"]["discard"] == ["BASH"]
    assert rolled["oracle"]["snapshot_pre_state"]["enemies"][0]["hp"] == 46
    assert decisions[2]["oracle"]["settled"] is True
    assert decisions[2]["pre_state"]["player"]["discard"] == ["BASH", STRIKE]
    opening = decisions[0]["pre_state"]
    assert opening["turn"] == {"player_turn": 1, "phase": "player_play"}
    assert opening["player"]["draw_multiset"] == sorted(
        [DEFEND] * 3 + [DEFEND + "+", STRIKE, STRIKE + "+"]
    )
    assert opening["enemies"] == [
        {"combat_id": 1, "monster_id": "NIBBIT", "hp": 46, "max_hp": 46, "block": 0, "powers": [], "intents": [{"type": "Attack", "attack_value": 13, "attack_times": 1}]}
    ]


def test_reasons_index_accounts_for_every_attempt(corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    export_bundles(corpus, out, registry())
    index = json.loads((out / "reasons.json").read_text(encoding="utf-8"))
    by_key = {(c["session"], c["attempt"]): c for c in index["combats"]}
    iron = by_key[("1773000000-IRONTEST01", 1)]
    assert iron["eligible"] and iron["reasons"] == [] and iron["bundle"].endswith("IRONTEST01.attempt1.json")
    lineage = next(item for item in index["lineages"] if item["session"] == "1773000000-IRONTEST01")
    assert lineage["attempts"] == 1 and lineage["post_combat_resumes_skipped"] == 1

    first, second = by_key[("1773100000-SILTEST001AB", 1)], by_key[("1773100000-SILTEST001AB", 2)]
    assert first["part"] == "1773100000-SILTEST001AB" and second["part"] == "1773100000-SILTEST001AB-part2"
    assert (first["outcome"], second["outcome"]) == ("abandoned", "win")
    for record in (first, second):
        assert not record["eligible"] and record["bundle"] is None
        assert "character_not_ported:SILENT" in record["reasons"]
        assert "encounter_not_ported:SLIMES_WEAK" in record["reasons"]
        assert "card_not_ported:STRIKE_SILENT" in record["reasons"]
        assert "relic_not_whitelisted:RING_OF_THE_SNAKE" in record["reasons"]
        assert "game_mode_modifiers_unknown:custom" in record["reasons"]
        assert not any(r.startswith(("deck_mismatch", "unresolved_pile_name")) for r in record["reasons"])
    classes = index["summary"]["reason_classes"]
    assert classes["character_not_ported"] == 2 and classes["game_mode_modifiers_unknown"] == 2
    assert (out / "name_map.json").is_file()
    # The diff harness reads every *.json with a top-level bundle_version as a
    # bundle; the index and the name map must not look like one.
    assert "bundle_version" not in index
    assert "bundle_version" not in json.loads((out / "name_map.json").read_text(encoding="utf-8"))


def test_include_ineligible_and_snapshot_mode(corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    export_bundles(corpus, out, registry(), include_ineligible=True, pre_state_mode="snapshot")
    assert sorted(p.name for p in (out / "ineligible" / "v0.111.0").iterdir()) == [
        "1773100000-SILTEST001AB.attempt1.json",
        "1773100000-SILTEST001AB.attempt2.json",
    ]
    bundle = json.loads((out / "bundles" / "v0.107.1" / "1773000000-IRONTEST01.attempt1.json").read_text(encoding="utf-8"))
    # snapshot mode keeps the raw mid-resolution projection.
    assert bundle["decisions"][1]["pre_state"]["enemies"][0]["hp"] == 46
    # Only ineligible bundles carry the exporter's verdict (the harness skips them by default).
    assert "ineligible_reasons" not in bundle
    index = json.loads((out / "reasons.json").read_text(encoding="utf-8"))
    by_key = {(c["session"], c["attempt"]): c for c in index["combats"]}
    for attempt in (1, 2):
        path = out / "ineligible" / "v0.111.0" / f"1773100000-SILTEST001AB.attempt{attempt}.json"
        ineligible = json.loads(path.read_text(encoding="utf-8"))
        assert ineligible["ineligible_reasons"] == by_key[("1773100000-SILTEST001AB", attempt)]["reasons"]
        assert "character_not_ported:SILENT" in ineligible["ineligible_reasons"]

    unobserved = tmp_path / "unobserved"
    export_bundles(corpus, unobserved, registry(), pre_state_mode="unobserved")
    bundle = json.loads((unobserved / "bundles" / "v0.107.1" / "1773000000-IRONTEST01.attempt1.json").read_text(encoding="utf-8"))
    assert bundle["decisions"][1]["pre_state"] is None
    assert bundle["decisions"][1]["oracle"]["unobserved_reason"] == "mid_resolution_snapshot"


def test_export_refuses_to_write_into_the_corpus(corpus: Path, tmp_path: Path) -> None:
    with pytest.raises(ExportError):
        export_bundles(corpus, corpus / "bundles", registry())
    with pytest.raises(ExportError):
        export_bundles(corpus, tmp_path / "out", registry(), forbidden_roots=[tmp_path])


def map_view_lineage(root: Path) -> tuple[int, int]:
    """Ironclad A10 v0.107.1, first combat in which the player opens the map view between
    two plays: the second play's state_seq is the map snapshot (corpus: 1792615807-4RHDW1LUA9)."""
    run = {"seed": "MAPVIEW001", "character": "IRONCLAD", "ascension": 10, "game_mode": "standard", "start_time": 1773200000.0}
    s = Stream()
    s.event("room_entered", room_type="Event", room_model_id="EVENT.NEOW", act=0, floor=1)
    s.event(
        "floor_summary",
        map_point_type="ancient",
        rooms=[{"model_id": "EVENT.NEOW"}],
        player_stats=[{"relic_choices": [{"choice": "RELIC.GOLDEN_PEARL", "was_picked": True}]}],
    )
    s.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=2)
    deck = [STRIKE] * 5 + [DEFEND] * 4 + [BASH, BANE]
    start = combat_state(hand=[], draw=[pile_card(c) for c in deck], energy=0, play=False, enemies=[enemy(1, 12)])
    start["player"]["relics"] = [{"id": "BURNING_BLOOD"}, {"id": "GOLDEN_PEARL"}]
    s.state(start)
    bane = hand_card(BANE, 4, cost="0", can_play=False)
    draw = [pile_card(c) for c in [STRIKE] * 3 + [DEFEND] * 3]
    turn1 = s.state(
        combat_state(
            hand=[hand_card(STRIKE, 0), hand_card(DEFEND, 1), hand_card(BASH, 2, cost="2"), hand_card(STRIKE, 3), bane],
            draw=draw,
            enemies=[enemy(1, 12)],
        )
    )
    s.action("play_card", turn1, card_model_id="CARD.STRIKE_IRONCLAD", combat_card_index=0, target_id=1)
    s.event("card_play_started", **card_event(STRIKE, "start"))
    s.event("damage_received", actor=nibbit(6), result={"receiver": nibbit(6)}, dealer=PLAYER)
    s.event("card_play_finished", **card_event(STRIKE, "finish"))
    settled = s.state(
        combat_state(
            hand=[hand_card(DEFEND, 0), hand_card(BASH, 1, cost="2"), hand_card(STRIKE, 2), {**bane, "index": 3}],
            draw=draw,
            discard=[pile_card(STRIKE)],
            energy=2,
            enemies=[enemy(1, 6)],
        ),
        trigger="poll",
    )
    view = s.state({"state_type": "map", "map": {}}, trigger="poll")
    s.action("play_card", view, card_model_id="CARD.STRIKE_IRONCLAD", combat_card_index=3, target_id=1)
    s.event("card_play_started", **card_event(STRIKE, "start"))
    s.event("damage_received", actor=nibbit(0), result={"receiver": nibbit(0), "was_target_killed": True}, dealer=PLAYER)
    s.event("card_play_finished", **card_event(STRIKE, "finish"))
    s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": {"hp": 70}})
    s.state({"state_type": "rewards", "rewards": {"items": []}, "player": {"hp": 70}})
    s.event("room_entered", room_type="Treasure", room_model_id=None, act=0, floor=3)
    write_part(root, "1773200000-MAPVIEW001", s, run, part=1, version="v0.107.1")
    return settled, view


def test_map_view_mid_combat_neither_ends_the_combat_nor_hides_the_pre_state(tmp_path: Path) -> None:
    root = tmp_path / "sessions"
    settled, view = map_view_lineage(root)
    out = tmp_path / "out"
    index = export_bundles(root, out, registry())
    record = index["combats"][0]
    assert record["eligible"], record["reasons"]
    assert (record["outcome"], record["outcome_evidence"], record["final_hp"]) == ("win", "combat_ended_message", 64)
    bundle = json.loads((out / "bundles" / "v0.107.1" / "1773200000-MAPVIEW001.attempt1.json").read_text(encoding="utf-8"))
    decisions = bundle["decisions"]
    assert [d["action"]["hand_index"] for d in decisions] == [0, 2]
    # The play made right after closing the map view uses the latest battle snapshot.
    oracle = decisions[1]["oracle"]
    assert (oracle["state_seq"], oracle["view_state_seq"], oracle["settled"]) == (settled, view, True)
    assert decisions[1]["pre_state"]["enemies"][0]["hp"] == 6
    assert "view_state_seq" not in decisions[0]["oracle"]


def test_combat_end_ignores_screens_between_battle_snapshots() -> None:
    battle = combat_state(hand=[hand_card(STRIKE, 0)])

    def snap(seq: int, state: dict[str, Any]) -> StateRecord:
        return StateRecord(seq, float(seq), "poll", str(state.get("state_type")), f"h{seq}", state)

    ended = {"state_type": "monster", "message": "Combat ended. Waiting for rewards..."}
    states = [snap(1, battle), snap(2, {"state_type": "map"}), snap(3, {"state_type": "rewards"}), snap(4, battle), snap(5, ended)]
    assert _combat_end(states, []) == (5, "win", "combat_ended_message")
    # Save & quit from the map view: the recording stops with no end marker.
    assert _combat_end(states[:2], []) == (None, None, "open")


def test_in_combat_choices_and_unrecorded_turn_ends_are_reasons() -> None:
    vocabulary = CorpusVocabulary()
    hand = [hand_card(STRIKE, 0), hand_card(DEFEND, 1)]
    vocabulary.observe_state("IRONCLAD", {"player": {"hand": hand}})
    state = StateRecord(1, 1.0, "phase", "monster", "h1", combat_state(hand=hand))

    def act(seq: int, kind: str, status: str = "executed", **params: Any) -> ActionRecord:
        return ActionRecord(seq, float(seq), "hook:Test", {"kind": kind, "params": params}, 1, status)

    actions = [
        # A real discard pick that carries the recorder's sticky singleton label.
        act(2, "player_choice", "committed", choice_type="CombatCard", context_model="SINGLETON.MULTIPLAYER_SCALING_MODEL", cards=["CARD.DEFEND_IRONCLAD"], picked=True),
        # Turn end logged only as the follow-up (STS2_MCP-driven recorder 0.2.0 parts).
        act(3, "ready_to_begin_enemy_turn"),
        act(4, "end_turn", turn_number=2),
        act(5, "ready_to_begin_enemy_turn"),
    ]
    decisions, reasons, notes, _counts = extract_decisions(
        states=[state], actions=actions, events=[], character="IRONCLAD", vocabulary=vocabulary, monster_ids={}
    )
    assert reasons == ["unsupported_action:player_choice", "turn_end_unrecorded:seq3"]
    assert [d["action"] for d in decisions] == [{"type": "end_turn"}]
    assert any("sticky recorder label" in note for note in notes)


def test_cli_export_bundles(corpus: Path, tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
    registry_file = tmp_path / "registry.json"
    registry_file.write_text(json.dumps(REGISTRY), encoding="utf-8")
    out = tmp_path / "cli-out"
    code = main(["export-bundles", str(out), "--root", str(corpus), "--sim-registry", str(registry_file)])
    assert code == 0
    assert "1 eligible bundles" in capsys.readouterr().out
    assert (out / "reasons.json").is_file()
    assert main(["export-bundles", str(corpus / "x"), "--root", str(corpus), "--sim-registry", str(registry_file)]) == 2
