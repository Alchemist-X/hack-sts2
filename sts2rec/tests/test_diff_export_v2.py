"""Tests for `sts2rec export-bundles-v2` (bundle schema v2) on synthetic recorder corpora.

The main fixture is one v0.111.0 Ironclad run, built record by record in the recorder's
shapes: Neow (event option + map move), a floor-2 combat with a potion, an in-combat hand
selection and a card play, its rewards (gold + a card-reward pick), a rest-site smith, a
shop purchase and a floor-5 combat whose deck comes from the floor-summary tracker. The
v1 corpus of test_diff_export is reused to check that combat decisions match v1.
"""

from __future__ import annotations

import json
from collections import Counter
from pathlib import Path
from typing import Any

import pytest

from sts2rec.cli import main
from sts2rec.diff_bundle import CardNameMap, SimRegistry
from sts2rec.diff_export import ExportError, export_bundles
from sts2rec.diff_export_v2 import (
    RUN_LAYER_SOURCES,
    ContentRegistry,
    DeckTracker,
    export_bundles_v2,
    prompt_open_state,
    reason_class_v2,
    resolve_card_picks,
    scan_content_registry,
)
from sts2rec.session import StateRecord

from .test_diff_bundle import BASH, DEFEND, STRIKE, combat_state, enemy, hand_card, pile_card
from .test_diff_export import REGISTRY, Stream, card_event, ironclad_lineage, silent_lineage, write_part

RUN = {"seed": "V2TESTSEED01", "character": "IRONCLAD", "ascension": 0, "game_mode": "standard", "start_time": 1773200000.0}
LINEAGE = "1773200000-V2TESTSEED01-IRONCLAD"
ANGER, CLASH, INFLAME = "ANGER", "CLASH", "INFLAME"
FULL = {
    "characters": ["IRONCLAD"],
    "encounters": ["NIBBITS_WEAK"],
    "cards": [STRIKE, DEFEND, BASH, ANGER, CLASH, INFLAME, "ASCENDERS_BANE"],
    "upgradable_cards": [STRIKE, DEFEND, BASH],
    "relics": ["BURNING_BLOOD", "NEOWS_TALISMAN"],
    "potions": ["FIRE_POTION"],
    "powers": ["VULNERABLE_POWER"],
    "monsters": ["NIBBIT"],
    "events": ["NEOW"],
    "run_layer": sorted(RUN_LAYER_SOURCES),
}


def player_block(**extra: Any) -> dict[str, Any]:
    return {
        "hp": 80,
        "max_hp": 80,
        "gold": 99,
        "relics": [{"id": "BURNING_BLOOD", "counter": None}, {"id": "NEOWS_TALISMAN", "counter": None}],
        "potions": [{"id": "FIRE_POTION", "slot": 0}],
        "max_potion_slots": 3,
        **extra,
    }


def battle(**kwargs: Any) -> dict[str, Any]:
    state = combat_state(**kwargs)
    state["player"].update(
        relics=[{"id": "BURNING_BLOOD", "counter": None}, {"id": "NEOWS_TALISMAN", "counter": None}],
        potions=[{"id": "FIRE_POTION", "slot": 0}],
        max_potion_slots=3,
        gold=99,
    )
    return state


def map_state(col: int, row: int, options: list[tuple[int, int, str]]) -> dict[str, Any]:
    return {
        "state_type": "map",
        "map": {
            "current_position": {"col": col, "row": row, "type": "Monster"},
            "next_options": [{"index": i, "col": c, "row": r, "type": t} for i, (c, r, t) in enumerate(options)],
            "nodes": [{"col": 1, "row": 1, "type": "Monster", "children": [[1, 2]]}],
        },
        "player": player_block(),
    }


def move(s: Stream, at: int, col: int, row: int) -> None:
    s.action("vote_for_map_coord", at, destination={"col": col, "row": row}, human=True)
    s.action("move_to_map_coord", at, destination={"col": col, "row": row}, human=True)


def v2_lineage(root: Path, *, with_mcr: bool = True) -> None:
    s = Stream()
    s.event("recorder_config")
    s.state({"state_type": "unknown", "player": player_block()})
    # Floor 1: Neow.
    s.event("room_entered", room_type="Event", room_model_id="EVENT.NEOW", act=0, floor=1)
    neow = s.state(
        {
            "state_type": "event",
            "event": {"event_id": "NEOW", "is_ancient": True, "options": [{"index": 0, "title": "护符", "text_key": "NEOWS_TALISMAN", "is_locked": False, "is_proceed": False}]},
            "player": player_block(),
        }
    )
    s.action("event_option", neow, title_key="NEOWS_TALISMAN.title", title="护符")
    s.action("event_proceed", neow, index=0, text_key="PROCEED")
    m1 = s.state(map_state(3, 0, [(1, 1, "Monster")]))
    move(s, m1, 1, 1)
    s.event(
        "floor_summary",
        map_point_type="ancient",
        rooms=[{"model_id": "EVENT.NEOW"}],
        player_stats=[{"relic_choices": [{"choice": "RELIC.NEOWS_TALISMAN", "was_picked": True}], "upgraded_cards": ["CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD"]}],
    )
    # Floor 2: combat with a potion, a hand selection and a card play.
    s.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=2)
    deck_piles = [pile_card(STRIKE)] * 4 + [pile_card(STRIKE, upgraded=True)] + [pile_card(DEFEND)] * 3
    deck_piles += [pile_card(DEFEND, upgraded=True), pile_card(BASH)]
    s.state(battle(hand=[], draw=deck_piles, energy=0, play=False))
    hand = [hand_card(STRIKE, 0), hand_card(BASH, 1, cost="2"), hand_card(DEFEND, 2), hand_card(STRIKE, 3), hand_card(STRIKE, 4)]
    rest = [pile_card(STRIKE), pile_card(STRIKE, upgraded=True), pile_card(DEFEND), pile_card(DEFEND), pile_card(DEFEND, upgraded=True)]
    turn1 = s.state(battle(hand=hand, draw=rest))
    s.action("use_potion", turn1, potion_index=0, potion_id="POTION.FIRE_POTION", target_id=1, in_combat=True, human=True)
    s.event("potion_used", potion="POTION.FIRE_POTION")
    select = battle(hand=hand, draw=rest, enemies=[enemy(1, 26)])
    select["state_type"] = "hand_select"
    select["hand_select"] = {"mode": "simple_select", "cards": [hand_card(STRIKE, 0), hand_card(DEFEND, 1)], "can_confirm": False}
    hs = s.state(select)
    s.action("player_choice", hs, choice_type="CombatCard", cards=["CARD.DEFEND_IRONCLAD"], picked=True)
    b2 = s.state(battle(hand=hand, draw=rest, enemies=[enemy(1, 26)]))
    s.action("play_card", b2, card_model_id="CARD.BASH", combat_card_index=1, target_id=1)
    s.event("card_play_started", **card_event(BASH, "start"))
    s.event("card_play_finished", **card_event(BASH, "finish"))
    s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": player_block()})
    rewards = s.state({"state_type": "rewards", "rewards": {"items": [{"index": 0, "type": "gold", "gold_amount": 12}, {"index": 1, "type": "card"}]}, "player": player_block()})
    s.action("reward_taken", rewards, reward_type="gold_reward", set_index=0, amount=12, human=True)
    offers = [{"id": INFLAME, "index": 0, "is_upgraded": False}, {"id": ANGER, "index": 1, "is_upgraded": False}, {"id": CLASH, "index": 2, "is_upgraded": False}]
    cr = s.state({"state_type": "card_reward", "card_reward": {"cards": offers, "can_skip": True}, "player": player_block()})
    s.action("player_choice", cr, choice_type="Index", indexes=[1], index=1, picked=True)
    s.action("reward_taken", cr, reward_type="card_reward", set_index=1, human=True)
    m2 = s.state(map_state(1, 1, [(1, 2, "RestSite")]))
    move(s, m2, 1, 2)
    s.event(
        "floor_summary",
        map_point_type="monster",
        rooms=[{"model_id": "ENCOUNTER.NIBBITS_WEAK"}],
        player_stats=[{"card_choices": [{"card": {"id": f"CARD.{c}"}, "was_picked": c == ANGER} for c in (INFLAME, ANGER, CLASH)], "cards_gained": [{"id": "CARD.ANGER"}]}],
    )
    # Floor 3: rest site, smith Bash (the deck choice is logged before the option).
    s.event("room_entered", room_type="RestSite", room_model_id=None, act=0, floor=3)
    rest_state = s.state({"state_type": "rest_site", "rest_site": {"options": [{"index": 0, "id": "HEAL", "is_enabled": True}, {"index": 1, "id": "SMITH", "is_enabled": True}]}, "player": player_block()})
    s.action("player_choice", rest_state, choice_type="DeckCard", cards=["CARD.BASH"], picked=True)
    # The opener hook fires after its deck choice resolved (cf. MerchantCardRemovalEntry.OnTryPurchaseWrapper paying
    # first): its own state is post-resolution, so the exporter gives it the choice's pre_state.
    rest_done = s.state({"state_type": "rest_site", "rest_site": {"options": []}, "player": player_block(gold=98)})
    s.action("rest_site_option", rest_done, option_id="SMITH")
    m3 = s.state(map_state(1, 2, [(1, 3, "Shop")]))
    move(s, m3, 1, 3)
    s.event("floor_summary", map_point_type="rest", rooms=[{"model_id": None}], player_stats=[{"upgraded_cards": ["CARD.BASH"], "rest_site_choices": ["SMITH"]}])
    # Floor 4: shop, buy Clash.
    s.event("room_entered", room_type="Shop", room_model_id=None, act=0, floor=4)
    shop = s.state(
        {
            "state_type": "shop",
            "shop": {"inventory_open": True, "items": [
                {"index": 0, "category": "card", "price": 50, "is_stocked": True, "on_sale": False, "card_id": CLASH},
                {"index": 1, "category": "card_removal", "price": 75, "is_stocked": True},
            ]},
            "player": player_block(),
        }
    )
    s.action("shop_purchase", shop, cost=50, item="card", item_id=None, on_sale=False)
    m4 = s.state(map_state(1, 3, [(1, 4, "Monster")]))
    move(s, m4, 1, 4)
    s.event("floor_summary", map_point_type="shop", rooms=[{"model_id": None}], player_stats=[{"cards_gained": [{"id": "CARD.CLASH"}]}])
    # Floor 5: a later combat; its deck comes from the tracker.
    s.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=5)
    hand5 = [hand_card(ANGER, 0), hand_card(CLASH, 1, cost="0"), hand_card(STRIKE, 2, upgraded=True), hand_card(DEFEND, 3, upgraded=True), hand_card(BASH, 4, upgraded=True, cost="2")]
    t5 = s.state(battle(hand=hand5, draw=[pile_card(STRIKE)] * 4 + [pile_card(DEFEND)] * 3))
    s.action("end_turn", t5, turn_number=1)
    s.action("ready_to_begin_enemy_turn", t5, action_type="ReadyToBeginEnemyTurnAction")
    s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": player_block()})
    path = write_part(root, "1773200000-V2TESTSEED01", s, RUN, part=1, version="v0.111.0")
    if with_mcr:
        (path / "native").mkdir(exist_ok=True)
        (path / "native" / "replay.mcr").write_bytes(b"\x00MCR")


@pytest.fixture
def v2_corpus(tmp_path: Path) -> Path:
    root = tmp_path / "sessions"
    v2_lineage(root)
    return root


def full_registry() -> ContentRegistry:
    return ContentRegistry.from_json(FULL, source="test")


def load(out: Path) -> dict[str, Any]:
    return json.loads((out / f"{LINEAGE}.json").read_text(encoding="utf-8"))


def test_run_is_segmented_per_room(v2_corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    index = export_bundles_v2(v2_corpus, out, full_registry())
    bundle = load(out)
    assert (bundle["bundle_version"], bundle["kind"], bundle["rules_version"], bundle["seeding"]) == (2, "run", "v0.111.0", "xxh64")
    assert bundle["source"]["lineage"] == LINEAGE
    assert bundle["source"]["provenance"]["driver"] == "human"
    kinds = [(s["kind"], s["floor"], s["phase"]) for s in bundle["segments"]]
    assert kinds == [
        ("room", 1, None), ("map", 1, None),
        ("combat", 2, None), ("room", 2, "post_combat"), ("map", 2, None),
        ("room", 3, None), ("map", 3, None),
        ("room", 4, None), ("map", 4, None),
        ("combat", 5, None),
    ]
    assert [s["id"] for s in bundle["segments"]] == [f"s{i}" for i in range(10)]
    run_indexes = [d["run_index"] for s in bundle["segments"] for d in s["decisions"]]
    assert run_indexes == list(range(len(run_indexes)))
    summary = index["summary"]
    assert summary["runs"] == 1 and summary["segments_by_kind"] == {"combat": 2, "map": 4, "room": 4}
    assert (out / "reasons.json").is_file() and (out / "name_map.json").is_file()


def test_first_combat_decisions_v2(v2_corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    export_bundles_v2(v2_corpus, out, full_registry())
    combat = load(out)["segments"][2]
    start = combat["start"]
    assert start["rng_offsets"] == {"Niche": 0} and "anchor" not in start
    assert [(c["id"], c["upgraded"]) for c in start["deck"]] == (
        [(STRIKE, False)] * 4 + [(STRIKE, True)] + [(DEFEND, False)] * 3 + [(DEFEND, True), (BASH, False)]
    )
    assert start["potions"] == [{"slot": 0, "id": "FIRE_POTION"}]
    actions = [d["action"] for d in combat["decisions"]]
    assert actions[0] == {"type": "use_potion", "slot": 0, "potion_id": "FIRE_POTION", "target_combat_id": 1, "in_combat": True}
    assert actions[1]["type"] == "choose" and actions[1]["context"] == "combat" and actions[1]["kind"] == "hand"
    assert actions[1]["picks"] == [{"index": 1, "card_id": DEFEND, "upgraded": False, "combat_card_index": None}]
    assert actions[2] == {"type": "play_card", "hand_index": 1, "card_id": BASH, "upgraded": False, "target_combat_id": 1}
    potion = combat["decisions"][0]
    assert potion["pre_state"]["player"]["potions"] == [{"slot": 0, "id": "FIRE_POTION"}]
    assert potion["oracle"]["source_hook"] == "hook:Test" and potion["oracle"]["human"] is True
    assert combat["decisions"][1]["pre_state"]["select"]["candidates"] == [
        {"index": 0, "id": STRIKE, "upgraded": False}, {"index": 1, "id": DEFEND, "upgraded": False}
    ]
    assert combat["outcome"]["result"] == "win"
    assert combat["eligibility"] == {"eligible": True, "reasons": []}


def test_run_level_decisions_in_logical_order(v2_corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    export_bundles_v2(v2_corpus, out, full_registry())
    segments = load(out)["segments"]
    neow = [d["action"] for d in segments[0]["decisions"]]
    assert neow == [
        {"type": "event_option", "text_key": "NEOWS_TALISMAN", "index": 0},
        {"type": "event_proceed", "text_key": "PROCEED", "index": 0},
    ]
    first_map = segments[1]["decisions"][0]
    assert first_map["action"] == {"type": "map_move", "col": 1, "row": 1}
    assert first_map["pre_state"]["map"]["nodes"]  # full graph once per act
    assert "nodes" not in segments[4]["decisions"][0]["pre_state"]["map"]
    rewards = segments[3]["decisions"]
    assert [d["action"]["type"] for d in rewards] == ["reward_take", "reward_take", "card_reward_pick"]
    assert rewards[1]["action"] == {"type": "reward_take", "set_index": 1, "reward_type": "card_reward", "item_id": None}
    assert rewards[1]["oracle"]["recorded_order"] == "after_pick"
    assert rewards[2]["action"] == {"type": "card_reward_pick", "index": 1, "card_id": ANGER}
    assert rewards[2]["pre_state"]["card_reward"]["cards"][1]["id"] == ANGER
    rest = segments[5]["decisions"]
    assert rest[0]["action"] == {"type": "rest_option", "option_id": "SMITH"}
    assert rest[1]["action"]["type"] == "choose" and rest[1]["action"]["context"] == "rest"
    assert rest[1]["action"]["picks"][0]["card_id"] == BASH and rest[1]["action"]["picks"][0]["resolved_by"] == "deck_multiset"
    assert rest[1]["oracle"]["recorded_order"] == "before_opener"
    assert rest[0]["oracle"]["pre_state_seq"] == rest[1]["oracle"]["state_seq"]
    assert rest[0]["pre_state"]["player"]["gold"] == 99, "the opener's pre_state is the state before its deck choice"
    assert rest[0]["pre_state"]["rest_site"]["options"], "not the post-resolution snapshot"
    assert segments[7]["decisions"][0]["action"] == {"type": "shop_buy", "category": "card", "item_id": CLASH, "price": 50, "slot": 0}
    for segment in segments:
        if segment["kind"] != "combat":
            assert "anchor_missing" in segment["eligibility"]["reasons"]
            assert not segment["eligibility"]["eligible"]


def test_later_combat_deck_and_mcr_anchor(v2_corpus: Path, tmp_path: Path) -> None:
    out = tmp_path / "out"
    export_bundles_v2(v2_corpus, out, full_registry())
    bundle = load(out)
    combat = bundle["segments"][9]
    assert [(c["id"], c["upgraded"]) for c in combat["start"]["deck"]] == (
        [(STRIKE, False)] * 4 + [(STRIKE, True)] + [(DEFEND, False)] * 3 + [(DEFEND, True), (BASH, True), (ANGER, False), (CLASH, False)]
    )
    assert "rng_offsets" not in combat["start"]
    assert bundle["anchors"][0]["kind"] == "mcr_initial" and bundle["anchors"][0]["floor"] == 5
    assert combat["start_anchor"] == bundle["anchors"][0]["id"] == combat["start"]["anchor"]
    assert combat["eligibility"]["reasons"] == ["anchor_unreadable:mcr_decoder_missing"]
    assert bundle["segments"][2]["start_anchor"] is None
    out2 = tmp_path / "out2"
    export_bundles_v2(v2_corpus, out2, full_registry(), mcr_readable=True)
    assert load(out2)["segments"][9]["eligibility"] == {"eligible": True, "reasons": []}


def test_later_combat_without_mcr_is_anchor_missing(tmp_path: Path) -> None:
    root = tmp_path / "sessions"
    v2_lineage(root, with_mcr=False)
    out = tmp_path / "out"
    export_bundles_v2(root, out, full_registry())
    assert load(out)["segments"][9]["eligibility"]["reasons"] == ["anchor_missing"]


def test_content_and_run_layer_reasons(v2_corpus: Path, tmp_path: Path) -> None:
    raw = {**FULL, "relics": ["BURNING_BLOOD"], "potions": [], "run_layer": ["map"], "events": []}
    out = tmp_path / "out"
    export_bundles_v2(v2_corpus, out, ContentRegistry.from_json(raw, source="test"))
    segments = load(out)["segments"]
    assert "content_not_ported:relic:NEOWS_TALISMAN" in segments[2]["eligibility"]["reasons"]
    assert "content_not_ported:potion:FIRE_POTION" in segments[2]["eligibility"]["reasons"]
    assert {"run_layer_not_ported:neow", "run_layer_not_ported:events", "content_not_ported:event:NEOW"} <= set(segments[0]["eligibility"]["reasons"])
    assert "run_layer_not_ported:rest" in segments[5]["eligibility"]["reasons"]
    assert not any(r.startswith("run_layer_not_ported") for r in segments[1]["eligibility"]["reasons"])
    assert reason_class_v2("content_not_ported:relic:X") == "content_not_ported:relic"
    assert reason_class_v2("deck_reconstruction_failed:missing=Ax1") == "deck_reconstruction_failed"


def test_v1_combat_decisions_are_reused_unchanged(tmp_path: Path) -> None:
    root = tmp_path / "sessions"
    ironclad_lineage(root)
    silent_lineage(root)
    v1_out = tmp_path / "v1"
    export_bundles(root, v1_out, SimRegistry.from_json(REGISTRY, source="test"))
    v1 = json.loads((v1_out / "bundles" / "v0.107.1" / "1773000000-IRONTEST01.attempt1.json").read_text(encoding="utf-8"))
    v2_out = tmp_path / "v2"
    index = export_bundles_v2(root, v2_out, ContentRegistry.from_json(REGISTRY, source="test"), versions=None)
    assert index["summary"]["runs"] == 2
    bundle = json.loads((v2_out / "1773000000-IRONTEST01-IRONCLAD.json").read_text(encoding="utf-8"))
    combat = next(s for s in bundle["segments"] if s["kind"] == "combat")
    assert [d["action"] for d in combat["decisions"]] == [d["action"] for d in v1["decisions"]]
    for mine, theirs in zip(combat["decisions"], v1["decisions"]):
        player = {k: v for k, v in mine["pre_state"]["player"].items() if k not in {"potions", "relics"}}
        assert {**mine["pre_state"], "player": player} == theirs["pre_state"]
    assert [(c["id"], c["upgraded"]) for c in combat["start"]["deck"]] == [(c["id"], c["upgraded"]) for c in v1["deck"]]
    # v0.107.1 is not the rules version: the first-combat recipe does not apply.
    assert "anchor_missing" in combat["eligibility"]["reasons"]
    # The part-2 resume on the rewards screen (no battle snapshot, no action) is no attempt.
    assert [s["kind"] for s in bundle["segments"]].count("combat") == 1
    assert not any(s["phase"] == "post_combat_resume" for s in bundle["segments"])
    silent = json.loads((v2_out / "1773100000-SILTEST001AB-SILENT.json").read_text(encoding="utf-8"))
    attempts = [s["attempt"] for s in silent["segments"] if s["kind"] == "combat"]
    assert attempts == [1, 2]


def test_deck_tracker_order_and_ambiguity() -> None:
    tracker = DeckTracker.from_deck([("A", False), ("B", False), ("A", False)])
    tracker.apply_summary({"cards_gained": [{"id": "CARD.C", "current_upgrade_level": 1}]})
    assert tracker.order == [("A", False), ("B", False), ("A", False), ("C", True)]
    tracker.apply_summary({"upgraded_cards": ["CARD.B"]})
    assert tracker.order == [("A", False), ("B", True), ("A", False), ("C", True)]
    tracker.apply_summary({"cards_removed": [{"id": "CARD.A"}]})
    assert tracker.order is None
    assert tracker.multiset == Counter({"A": 1, "B+": 1, "C+": 1})


def test_choice_resolution() -> None:
    candidates = [{"id": "X", "is_upgraded": False, "index": 0}, {"id": "X", "is_upgraded": True, "index": 1}]
    picks, reasons = resolve_card_picks(["CARD.X"], candidates)
    assert picks[0]["index"] == 0 and reasons == ["recorder_gap:choice_identity"]
    picks, reasons = resolve_card_picks(["CARD.Y"], None, Counter({"Y+": 2}))
    assert picks[0] == {"index": None, "card_id": "Y", "upgraded": True, "resolved_by": "deck_multiset"} and reasons == []
    names = CardNameMap()
    names.observe("IRONCLAD", [{"id": "INJURY", "name": "受伤"}])
    hand = [{"id": "S", "index": 0}, {"id": "INJURY", "index": 1}, {"id": "S", "index": 2}]
    picked = {
        "state_type": "hand_select",
        "player": {"hand": hand},
        "hand_select": {"cards": [{"id": "S", "index": 0}, {"id": "S", "index": 1}], "selected_cards": [{"index": 0, "name": "受伤"}]},
    }
    record = StateRecord(seq=5, t=5.0, trigger="action", screen="hand_select", hash="h", state=picked)
    state, reasons = prompt_open_state([record], record, character="IRONCLAD", names=names)
    assert reasons == []
    assert [c["id"] for c in state["hand_select"]["cards"]] == ["S", "INJURY", "S"]


def test_output_dir_rules_and_cli(v2_corpus: Path, tmp_path: Path) -> None:
    with pytest.raises(ExportError):
        export_bundles_v2(v2_corpus, v2_corpus / "out", full_registry())
    with pytest.raises(ExportError):
        export_bundles_v2(v2_corpus, tmp_path / "repo" / "x", full_registry(), repo_root=tmp_path / "repo")
    export_bundles_v2(v2_corpus, tmp_path / "repo" / "tmp" / "x", full_registry(), repo_root=tmp_path / "repo")
    registry_file = tmp_path / "registry.json"
    registry_file.write_text(json.dumps(FULL), encoding="utf-8")
    out = tmp_path / "cli-out"
    code = main(["export-bundles-v2", str(out), "--root", str(v2_corpus), "--sim-registry", str(registry_file), "--versions", "all"])
    assert code == 0
    assert (out / f"{LINEAGE}.json").is_file()


def test_scan_content_registry(tmp_path: Path) -> None:
    core = tmp_path / "Core"
    (core / "Relics").mkdir(parents=True)
    (core / "Relics" / "Akabeko.cs").write_text(
        'public sealed class Akabeko : RelicModel\n{\n    public const string RelicId = "AKABEKO";\n    public override string Id => RelicId;\n}\n',
        encoding="utf-8",
    )
    (core / "Relics" / "Stub.cs").write_text(
        'public sealed class Stub : RelicModel\n{\n    public override string Id => "STUB";\n    public void X() { throw new NotImplementedException(); }\n}\n',
        encoding="utf-8",
    )
    (core / "Run" / "Map").mkdir(parents=True)
    (core / "Run" / "Map" / "StandardActMap.cs").write_text("class StandardActMap {}\n", encoding="utf-8")
    registry = scan_content_registry(core)
    assert registry.ported("relics", "AKABEKO") and not registry.ported("relics", "STUB")
    assert registry.run_layer == frozenset({"map"})
