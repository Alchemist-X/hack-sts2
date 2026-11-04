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
from sts2rec.diff_export import ExportError, PartStreams, export_bundles
from sts2rec.diff_export_v2 import (
    RUN_LAYER_SOURCES,
    ContentRegistry,
    DeckTracker,
    RoomWindow,
    _event_option_index,
    _shop_slot,
    LineageContext,
    build_run_decisions,
    card_reward_closed_by_skip,
    deck_edit_positions,
    export_bundles_v2,
    hoist_combat_starting_event_options,
    resolve_deck_picks_from_saves,
    prompt_open_state,
    reason_class_v2,
    resolve_card_picks,
    scan_content_registry,
)
from sts2rec.session import ActionRecord, EventRecord, StateRecord

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


def v2_lineage(root: Path, *, with_mcr: bool = True, with_saves: bool = False, monster_screen_pick: bool = False) -> None:
    """``with_saves``: R1 room_entry saves before floors 3 and 5 (E3); ``monster_screen_pick``: the card pick's
    last snapshot is the combat room's and the reward_taken carries the R4 offer (E3 GREMLIN_MERC_NORMAL)."""
    s = Stream()
    saves: list[int] = []

    def room_entry_save(floor: int) -> None:
        seq = s.seq + 1
        s.event("run_saved", file=f"native/saves/{seq:06d}.save", anchor_kind="room_entry", act=0, floor=floor, pre_finished=False)
        saves.append(seq)

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
    if monster_screen_pick:
        cr = s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": player_block()})
    else:
        cr = s.state({"state_type": "card_reward", "card_reward": {"cards": offers, "can_skip": True}, "player": player_block()})
    s.action("player_choice", cr, choice_type="Index", indexes=[1], index=1, picked=True)
    offer = {"reward_type": "card_reward", "cards": [{"id": f"CARD.{c['id']}", "upgrade_level": 0} for c in offers]} if monster_screen_pick else None
    s.action("reward_taken", cr, reward_type="card_reward", set_index=1, human=True, **({"offer": offer} if offer else {}))
    m2 = s.state(map_state(1, 1, [(1, 2, "RestSite")]))
    move(s, m2, 1, 2)
    s.event(
        "floor_summary",
        map_point_type="monster",
        rooms=[{"model_id": "ENCOUNTER.NIBBITS_WEAK"}],
        player_stats=[{"card_choices": [{"card": {"id": f"CARD.{c}"}, "was_picked": c == ANGER} for c in (INFLAME, ANGER, CLASH)], "cards_gained": [{"id": "CARD.ANGER"}]}],
    )
    # Floor 3: rest site, smith Bash (the deck choice is logged before the option).
    if with_saves:
        room_entry_save(2)
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
    if with_saves:
        room_entry_save(4)
    s.event("room_entered", room_type="Monster", room_model_id="ENCOUNTER.NIBBITS_WEAK", act=0, floor=5)
    hand5 = [hand_card(ANGER, 0), hand_card(CLASH, 1, cost="0"), hand_card(STRIKE, 2, upgraded=True), hand_card(DEFEND, 3, upgraded=True), hand_card(BASH, 4, upgraded=True, cost="2")]
    t5 = s.state(battle(hand=hand5, draw=[pile_card(STRIKE)] * 4 + [pile_card(DEFEND)] * 3))
    s.action("end_turn", t5, turn_number=1)
    s.action("ready_to_begin_enemy_turn", t5, action_type="ReadyToBeginEnemyTurnAction")
    s.state({"state_type": "monster", "message": "Combat ended. Waiting for rewards...", "player": player_block()})
    path = write_part(root, "1773200000-V2TESTSEED01", s, RUN, part=1, version="v0.111.0")
    for seq in saves:
        (path / "native" / "saves").mkdir(parents=True, exist_ok=True)
        (path / "native" / "saves" / f"{seq:06d}.save").write_bytes(b"{}")
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
            # E3: no save anchor in this corpus; the harness chains run segments or skips them as anchor_missing.
            assert "anchor_missing" not in segment["eligibility"]["reasons"]
            assert segment["start_anchor"] is None


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


# ---- E3 (2026-11-02): REC recorder anchors and farm findings ----------------------------


def test_save_anchors_bind_to_the_room_they_precede(tmp_path: Path) -> None:
    root = tmp_path / "sessions"
    v2_lineage(root, with_mcr=False, with_saves=True)
    out = tmp_path / "out"
    export_bundles_v2(root, out, full_registry())
    bundle = load(out)
    saves = [a for a in bundle["anchors"] if a["kind"] == "room_entry"]
    assert len(saves) == 2 and all(a["seq"] is not None for a in saves), "keyed by the record's own seq, kind from anchor_kind"
    rest_room = next(s for s in bundle["segments"] if s["kind"] == "room" and s["room_type"] == "RestSite")
    assert rest_room["start_anchor"] == saves[0]["id"] and rest_room["start"]["from"] == "anchor"
    combat5 = bundle["segments"][9]
    assert combat5["kind"] == "combat" and combat5["floor"] == 5
    assert combat5["start_anchor"] == saves[1]["id"] == combat5["start"]["anchor"]
    assert combat5["start"]["from"] == "anchor" and combat5["start"]["deck"], "v1 fields kept for the record"
    assert not any(r.startswith(("anchor_", "recorder_gap:deck_order")) for r in combat5["eligibility"]["reasons"])
    # Refs are relative to the bundle dir: the save is byte-copied next to the bundle, never moved.
    for anchor in saves:
        assert (out / anchor["ref"]).read_bytes() == b"{}"
        assert (root / anchor["ref"]).is_file()


def test_card_pick_after_a_combat_screen_uses_the_r4_offer(tmp_path: Path) -> None:
    root = tmp_path / "sessions"
    v2_lineage(root, monster_screen_pick=True)
    out = tmp_path / "out"
    export_bundles_v2(root, out, full_registry())
    rewards = load(out)["segments"][3]
    kinds = [d["action"]["type"] for d in rewards["decisions"]]
    assert kinds == ["reward_take", "reward_take", "card_reward_pick"], kinds
    assert rewards["decisions"][2]["action"] == {"type": "card_reward_pick", "index": 1, "card_id": ANGER}
    assert "recorder_gap:card_reward_offer" not in rewards["eligibility"]["reasons"]


def test_shop_purchase_with_twin_prices_takes_the_slot_that_emptied() -> None:
    items = [
        {"index": 0, "category": "card", "price": 76, "is_stocked": True, "on_sale": False, "card_id": "DISMANTLE"},
        {"index": 4, "category": "card", "price": 76, "is_stocked": True, "on_sale": False, "card_id": "INFERNO"},
    ]
    params = {"cost": 76, "item": "card", "item_id": None, "on_sale": False}
    before = {"shop": {"items": items}}
    assert _shop_slot(params, before) == (0, "DISMANTLE", False)
    after = {"shop": {"items": [items[0], {"index": 4, "category": "card", "price": 76, "is_stocked": False, "on_sale": False}]}}
    assert _shop_slot(params, before, after) == (4, "INFERNO", True)


def test_event_option_prefers_the_recorded_option_key() -> None:
    params = {"title_key": "SEA_GLASS.IRONCLAD.title", "text_key": "OROBAS.pages.INITIAL.options.SEA_GLASS", "index": 0}
    assert _event_option_index(params, None) == ("OROBAS.pages.INITIAL.options.SEA_GLASS", 0)
    legacy = {"title_key": "NEOWS_TALISMAN.title"}
    assert _event_option_index(legacy, None)[0] == "NEOWS_TALISMAN"


def _act(seq: int, kind: str, state_seq: int | None, source: str = "hook:test", **params: Any) -> ActionRecord:
    return ActionRecord(seq=seq, t=float(seq), source=source, action={"kind": kind, "params": params}, state_seq=state_seq, status="committed")


def _snap(seq: int, state_type: str) -> StateRecord:
    return StateRecord(seq=seq, t=float(seq), trigger="action", screen=state_type, hash="", state={"state_type": state_type})


def test_event_option_that_starts_a_combat_moves_back_to_the_event() -> None:
    # Farm VSQP6ESN1F f7: PUNCH_OFF FIGHT enters the combat inside its handler (Models.Events/PunchOff.cs:522-535); the
    # SaveEventOptionToHistory hook logs it after the combat's room_entered, with a battle snapshot as its state.
    save_option = "hook:EventSynchronizer.SaveEventOptionToHistory"
    event_room = EventRecord(seq=1144, t=0.0, entry="room_entered", data={"room_type": "Event", "room_model_id": "EVENT.PUNCH_OFF", "act": 0, "floor": 7})
    combat_room = EventRecord(seq=1152, t=0.0, entry="room_entered", data={"room_type": "Monster", "room_model_id": "ENCOUNTER.PUNCH_OFF_EVENT_ENCOUNTER", "act": 0, "floor": 7})
    take_them = _act(1146, "event_option", 1145, save_option, text_key="PUNCH_OFF.pages.INITIAL.options.I_CAN_TAKE_THEM")
    fight = _act(1154, "event_option", 1153, save_option, text_key="PUNCH_OFF.pages.I_CAN_TAKE_THEM.options.FIGHT")
    play = _act(1167, "play_card", 1166, card_model_id="CARD.DEFEND_REGENT")
    event = RoomWindow(part_index=0, room=event_room, end_seq=1152, states=[_snap(1145, "event"), _snap(1147, "event"), _snap(1148, "monster")], actions=[take_them], events=[])
    combat = RoomWindow(part_index=0, room=combat_room, end_seq=None, states=[_snap(1153, "monster"), _snap(1166, "monster")], actions=[fight, play], events=[])
    notes = hoist_combat_starting_event_options([event, combat])
    assert [a.seq for a in event.actions] == [1146, 1154]
    assert event.actions[1].state_seq == 1147, "the event window's last event snapshot (the FIGHT page), not the combat message"
    assert [a.seq for a in combat.actions] == [1167]
    assert len(notes) == 1 and "1154" in notes[0]
    # A combat on another floor, or after a room that is not an event, keeps its records.
    other = RoomWindow(part_index=0, room=EventRecord(seq=1200, t=0.0, entry="room_entered", data={"room_type": "Monster", "room_model_id": "ENCOUNTER.X", "act": 0, "floor": 8}),
                       end_seq=None, states=[], actions=[_act(1201, "event_option", 1200)], events=[])
    assert hoist_combat_starting_event_options([combat, other]) == []
    assert [a.seq for a in other.actions] == [1201]


def test_card_reward_selection_closed_by_skip_before_the_pick() -> None:
    # Farm UUR25UNBL5 f5 BRAIN_LEECH RIP: Index 3 on a 3-card reward is the Skip alternative (Rewards/CardReward.cs:646-658,
    # Entities.CardRewardAlternatives/CardRewardAlternative.cs Generate), which ends the selection without completing the
    # reward; the later Index 2 completes it and has the reward_taken.
    def offer(can_skip: bool = True, can_reroll: bool = False) -> dict[str, Any]:
        return {"reward_type": "card_reward", "cards": [{"id": "CARD.DISCOVERY"}, {"id": "CARD.FLASH_OF_STEEL"}, {"id": "CARD.FISTICUFFS"}], "can_skip": can_skip, "can_reroll": can_reroll}

    def pick(seq: int, index: int) -> ActionRecord:
        return _act(seq, "player_choice", seq - 1, choice_type="Index", indexes=[index], index=index, picked=True)

    def taken(seq: int, **kw: Any) -> ActionRecord:
        return _act(seq, "reward_taken", seq - 1, reward_type="card_reward", set_index=5, offer=offer(**kw))

    proceed = _act(600, "event_proceed", 598, index=0, text_key="PROCEED")
    reward = taken(599)
    assert card_reward_closed_by_skip(pick(595, 3), [pick(597, 2), reward, proceed]) is reward
    assert card_reward_closed_by_skip(pick(595, 3), [pick(596, 3), pick(597, 2), reward]) is reward, "several closes before the pick"
    assert card_reward_closed_by_skip(pick(597, 2), [reward, proceed]) is None, "the completing pick itself"
    assert card_reward_closed_by_skip(pick(595, 1), [pick(597, 2), reward]) is None, "an offered card, not an alternative"
    assert card_reward_closed_by_skip(pick(595, 3), [pick(597, 2), taken(599, can_skip=False, can_reroll=True)]) is None, "REROLL"
    assert card_reward_closed_by_skip(pick(595, 3), [proceed, pick(597, 2), reward]) is None, "only Index choices in between"


def test_deck_edit_positions_follow_the_deck_operations() -> None:
    S, S1, D, J = ("STRIKE_IRONCLAD", 0, None), ("STRIKE_IRONCLAD", 1, None), ("DEFEND_IRONCLAD", 0, None), ("JUGGLING", 0, None)
    # An upgrade changes its card in place (farm K4P3FWUZYN f12 SMITH: the second Strike).
    assert deck_edit_positions([S, S, S, D], [S, S1, S, D]) == {1: "modified"}
    # A transform removes the original and appends the replacement (Commands/CardCmd.cs:2061-2064; 545XJP726S f7
    # AROMA_OF_CHAOS transformed the upgraded Strike).
    assert deck_edit_positions([S, S, S1, S, D], [S, S, S, D, J]) == {2: "removed"}
    # Removing one of identical copies: any copy gives the same deck.
    assert list(deck_edit_positions([S, S, D], [S, D]).values()) == ["removed"]
    assert deck_edit_positions([S, D], [S, D, J]) == {}
    assert deck_edit_positions([S], [D]) == {0: "removed"}


def _deck_save(path: Path, deck: list[tuple[str, int]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    cards = [{"id": f"CARD.{cid}", "floor_added_to_deck": 1, **({"current_upgrade_level": up} if up else {})} for cid, up in deck]
    path.write_text(json.dumps({"players": [{"deck": cards}]}), encoding="utf-8")


def test_ambiguous_deck_picks_are_resolved_from_the_saves_around_the_room(tmp_path: Path) -> None:
    part = tmp_path / "1793604345-K4P3FWUZYN"
    _deck_save(part / "native" / "saves" / "001544.save", [("STRIKE_IRONCLAD", 0)] * 3 + [("DEFEND_IRONCLAD", 0)])
    _deck_save(part / "native" / "saves" / "001556.save", [("STRIKE_IRONCLAD", 0), ("STRIKE_IRONCLAD", 1), ("STRIKE_IRONCLAD", 0), ("DEFEND_IRONCLAD", 0)])
    ctx = LineageContext(lineage="L", parts=[PartStreams(path=part, manifest=None)], raw_manifests=[], vocabulary=None, registry=None,  # type: ignore[arg-type]
                         options=None, character="IRONCLAD", ascension=0, game_version="v0.111.0")  # type: ignore[arg-type]
    for seq in (1544, 1556):
        ctx.anchors.append({"id": f"a{seq}", "kind": "room_entry", "seq": seq, "ref": f"{part.name}/native/saves/{seq:06d}.save", "part_index": 0})
    smith = {"index": None, "card_id": "STRIKE_IRONCLAD", "upgraded": False, "resolved_by": "deck_multiset", "deck_index": None}
    rest = {"id": "s27", "kind": "room", "part": part.name, "notes": [], "eligibility": {"eligible": True, "reasons": []}, "decisions": [
        {"oracle": {"action_seq": 1548}, "action": {"type": "rest_option", "option_id": "SMITH"}},
        {"oracle": {"action_seq": 1547}, "action": {"type": "choose", "context": "rest", "choice_type": "DeckCard", "picks": [smith]}},
    ]}
    walk = {"id": "s28", "kind": "map", "part": part.name, "notes": [], "eligibility": {"eligible": True, "reasons": []},
            "decisions": [{"oracle": {"action_seq": 1550}, "action": {"type": "map_move"}}]}
    ctx.segments += [rest, walk]
    resolve_deck_picks_from_saves(ctx)
    assert smith["deck_index"] == 1 and smith["upgraded"] is False and smith["resolved_by"] == "save_deck_diff"
    assert rest["notes"] and "STRIKE_IRONCLAD@1" in rest["notes"][0]
    # Another room's decision between the saves: the diff is not this room's alone.
    other = dict(smith, deck_index=None, resolved_by="deck_multiset")
    rest["decisions"][1]["action"]["picks"] = [other]
    ctx.segments.insert(1, {"id": "sX", "kind": "room", "part": part.name, "notes": [], "eligibility": {"eligible": True, "reasons": []},
                            "decisions": [{"oracle": {"action_seq": 1549}, "action": {"type": "shop_buy"}}]})
    resolve_deck_picks_from_saves(ctx)
    assert other["deck_index"] is None


def test_a_backed_out_deck_selection_is_not_paired_with_the_next_option(tmp_path: Path) -> None:
    # Human EC039PTFQWV5 f12: SMITH opened, the deck select closed with nothing picked (SmithRestSiteOption.cs:263-266 returns
    # false, no rest_site_option is logged), then HEAL. The empty choice must not become HEAL's deck choice.
    part = tmp_path / "1793384475-EC039PTFQWV5-part3"
    ctx = LineageContext(lineage="L", parts=[PartStreams(path=part, manifest=None)], raw_manifests=[], vocabulary=None, registry=None,  # type: ignore[arg-type]
                         options=None, character="SILENT", ascension=7, game_version="v0.111.0")  # type: ignore[arg-type]
    room = EventRecord(seq=700, t=0.0, entry="room_entered", data={"room_type": "RestSite", "room_model_id": None, "act": 0, "floor": 12})
    rest = StateRecord(seq=708, t=0.0, trigger="action", screen="rest_site", hash="",
                       state={"state_type": "rest_site", "rest_site": {"options": [{"index": 0, "id": "HEAL", "is_enabled": True}, {"index": 1, "id": "SMITH", "is_enabled": True}]}, "player": player_block()})
    cancel = _act(709, "player_choice", 708, choice_type="DeckCard", cards=[], picked=False)
    heal = _act(710, "rest_site_option", 708, option_id="HEAL")
    window = RoomWindow(part_index=0, room=room, end_seq=None, states=[rest], actions=[cancel, heal], events=[])
    decisions, reasons, _ = build_run_decisions(ctx, window, [cancel, heal], phase=None, summary=None)
    assert [d["action"] for d in decisions] == [{"type": "rest_option", "option_id": "HEAL"}]
    assert decisions[0]["oracle"]["backed_out_selection_seqs"] == [709]
    assert "choice_without_opener" not in reasons
    # A real deck choice still pairs with its opener.
    smith = _act(711, "player_choice", 708, choice_type="DeckCard", cards=["CARD.STRIKE_SILENT"], picked=True)
    opener = _act(712, "rest_site_option", 708, option_id="SMITH")
    decisions, _, _ = build_run_decisions(ctx, window, [smith, opener], phase=None, summary=None)
    assert [d["action"]["type"] for d in decisions] == ["rest_option", "choose"]

