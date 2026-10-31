"""Unit tests for the diff-bundle building blocks (sim/specs/diff-bundle.md).

All inputs are small hand-built recorder snapshots / events in the shapes the
recorder writes; no real trajectories.
"""

from __future__ import annotations

from collections import Counter
from pathlib import Path

from sts2rec.diff_bundle import (
    KALEIDOSCOPE_NICHE_DRAWS,
    CardNameMap,
    SimRegistry,
    card_key,
    gap_keeps_hand,
    niche_offset,
    parse_attack_label,
    parse_cost,
    project_state,
    reconstruct_deck,
    relic_reasons,
    resolve_hand_index,
    roll_forward,
    scan_sim_registry,
    seeding_for,
    sim_coverage_reasons,
    snapshot_deck_multiset,
    visible_card_total,
)

STRIKE, DEFEND, BASH, BANE = "STRIKE_IRONCLAD", "DEFEND_IRONCLAD", "BASH", "ASCENDERS_BANE"
ZH = {STRIKE: "打击", DEFEND: "防御", BASH: "痛击", BANE: "进阶之灾", "STRIKE_SILENT": "打击"}


def hand_card(card_id: str, index: int, *, upgraded: bool = False, cost: str = "1", can_play: bool = True) -> dict:
    name = ZH.get(card_id, card_id.lower()) + ("+" if upgraded else "")
    return {
        "id": card_id,
        "name": name,
        "cost": cost,
        "is_upgraded": upgraded,
        "index": index,
        "can_play": can_play,
        "target_type": "AnyEnemy" if card_id in (STRIKE, BASH) else "Self",
    }


def pile_card(card_id: str, *, upgraded: bool = False) -> dict:
    return {"name": ZH.get(card_id, card_id.lower()) + ("+" if upgraded else ""), "cost": "1"}


def enemy(combat_id: int, hp: int, *, block: int = 0, powers=(), intents=(("Attack", "6"),), entity: str = "NIBBIT") -> dict:
    return {
        "entity_id": f"{entity}_{combat_id - 1}",
        "combat_id": combat_id,
        "hp": hp,
        "max_hp": 46,
        "block": block,
        "status": [{"id": pid, "amount": amount} for pid, amount in powers],
        "intents": [{"type": kind, "label": label} for kind, label in intents],
    }


def combat_state(
    *,
    hand,
    draw=(),
    discard=(),
    exhaust=(),
    enemies=None,
    energy: int = 3,
    hp: int = 64,
    block: int = 0,
    turn: int = 1,
    play: bool = True,
    powers=(),
) -> dict:
    return {
        "state_type": "monster",
        "battle": {
            "round": turn,
            "turn": "player",
            "is_play_phase": play,
            "player_turn_number": turn,
            "enemies": enemies if enemies is not None else [enemy(1, 46)],
        },
        "player": {
            "hp": hp,
            "max_hp": 80,
            "block": block,
            "energy": energy,
            "max_energy": 3,
            "hand": list(hand),
            "draw_pile_count": len(draw),
            "discard_pile_count": len(discard),
            "exhaust_pile_count": len(exhaust),
            "draw_pile": list(draw),
            "discard_pile": list(discard),
            "exhaust_pile": list(exhaust),
            "status": [{"id": pid, "amount": amount} for pid, amount in powers],
            "relics": [{"id": "BURNING_BLOOD"}],
        },
    }


def ironclad_names() -> CardNameMap:
    names = CardNameMap()
    names.observe("IRONCLAD", [hand_card(STRIKE, 0), hand_card(DEFEND, 1), hand_card(BASH, 2), hand_card(BANE, 3)])
    names.observe("IRONCLAD", [hand_card(STRIKE, 0, upgraded=True)])
    names.observe("SILENT", [hand_card("STRIKE_SILENT", 0)])
    return names


# --- small parsers -----------------------------------------------------------


def test_attack_labels_use_the_multiplication_sign() -> None:
    assert parse_attack_label("13") == (13, 1)
    assert parse_attack_label("6×2") == (6, 2)
    assert parse_attack_label("6x2") is None
    assert parse_attack_label("") is None


def test_cost_parsing_keeps_x() -> None:
    assert parse_cost("1") == 1
    assert parse_cost("X") == "X"
    assert parse_cost("?") is None
    assert card_key(STRIKE, True) == "STRIKE_IRONCLAD+"


def test_seeding_by_version_and_old_prefix() -> None:
    assert seeding_for("v0.107.1", "ABC") == ("legacy_v18", [])
    assert seeding_for("v0.111.0", "ABC") == ("xxh64", [])
    assert seeding_for("v0.110.1", "oldABC") == (None, ["seed_old_prefix"])
    assert seeding_for("v0.108.0", "ABC") == (None, ["seeding_unknown_version:v0.108.0"])


def test_niche_offset_formula_and_relic_whitelist() -> None:
    # Kaleidoscope: 2 card rewards x StableShuffle of the 4 other character pools (3 draws each).
    assert KALEIDOSCOPE_NICHE_DRAWS == 6
    assert niche_offset(["BURNING_BLOOD", "KALEIDOSCOPE", "NEW_LEAF", "NEOWS_BONES"]) == 8
    assert niche_offset(["BURNING_BLOOD", "NEOWS_TALISMAN"]) == 0
    assert relic_reasons(["BURNING_BLOOD", "BOOMING_CONCH", "RING_OF_THE_SNAKE"]) == [
        "relic_excluded:BOOMING_CONCH",
        "relic_not_whitelisted:RING_OF_THE_SNAKE",
    ]


# --- name map ----------------------------------------------------------------


def test_name_map_disambiguates_strike_per_character_and_derives_plus() -> None:
    names = ironclad_names()
    assert names.resolve("IRONCLAD", "打击") == (STRIKE, False)
    assert names.resolve("SILENT", "打击") == ("STRIKE_SILENT", False)
    # Seen upgraded in an Ironclad hand.
    assert names.resolve("IRONCLAD", "打击+") == (STRIKE, True)
    # Never seen upgraded: the "+" suffix falls back to the base name.
    assert names.resolve("IRONCLAD", "防御+") == (DEFEND, True)
    # A character that never held it falls back to the global table, which
    # is ambiguous for Strike, and unknown names stay unresolved.
    assert names.resolve("DEFECT", "打击") is None
    assert names.resolve("IRONCLAD", "无名") is None


def test_name_map_keeps_dynamic_plus_names_that_are_not_upgrades() -> None:
    names = CardNameMap()
    names.observe("NECROBINDER", [{"id": "WITHER", "name": "凋萎+2", "is_upgraded": False}])
    assert names.resolve("NECROBINDER", "凋萎+2") == ("WITHER", False)


# --- projection --------------------------------------------------------------


def test_projection_maps_snapshot_fields_to_contract_keys() -> None:
    state = combat_state(
        hand=[hand_card(STRIKE, 0), hand_card(BANE, 1, cost="0", can_play=False)],
        draw=[pile_card(DEFEND), pile_card(STRIKE, upgraded=True)],
        discard=[pile_card(BASH)],
        enemies=[
            enemy(2, 20, intents=(("StatusCard", "3"), ("Buff", ""))),
            enemy(1, 30, powers=(("VULNERABLE_POWER", 2),), intents=(("Attack", "6×2"),)),
            enemy(3, 0),
        ],
        powers=(("STRENGTH_POWER", 1),),
    )
    projection, issues = project_state(state, character="IRONCLAD", names=ironclad_names(), monster_ids={1: "NIBBIT"})
    assert issues == []
    assert projection["turn"] == {"player_turn": 1, "phase": "player_play"}
    player = projection["player"]
    assert player["hand"] == [
        {"id": STRIKE, "upgraded": False, "cost": 1, "can_play": True},
        {"id": BANE, "upgraded": False, "cost": 0, "can_play": False},
    ]
    assert player["draw_multiset"] == ["DEFEND_IRONCLAD", "STRIKE_IRONCLAD+"]
    assert player["discard"] == ["BASH"] and player["exhaust"] == []
    assert (player["draw_count"], player["discard_count"], player["exhaust_count"]) == (2, 1, 0)
    assert player["powers"] == [{"id": "STRENGTH_POWER", "amount": 1}]
    assert [e["combat_id"] for e in projection["enemies"]] == [1, 2]  # sorted, dead dropped
    first, second = projection["enemies"]
    assert first["monster_id"] == "NIBBIT"
    assert first["powers"] == [{"id": "VULNERABLE_POWER", "amount": 2}]
    assert first["intents"] == [{"type": "Attack", "attack_value": 6, "attack_times": 2}]
    assert second["intents"] == [{"type": "StatusCard", "card_count": 3}, {"type": "Buff"}]


def test_projection_reports_unresolved_names_and_non_play_phase() -> None:
    state = combat_state(hand=[], draw=[{"name": "无名"}], play=False)
    _projection, issues = project_state(state, character="IRONCLAD", names=ironclad_names())
    assert "unresolved_pile_name:无名" in issues
    assert "pre_state_not_player_play:player_not_play" in issues


def test_snapshot_deck_multiset_counts_hand_and_piles() -> None:
    state = combat_state(hand=[hand_card(BASH, 0)], draw=[pile_card(STRIKE), pile_card(STRIKE, upgraded=True)])
    observed, issues = snapshot_deck_multiset(state, character="IRONCLAD", names=ironclad_names())
    assert issues == []
    assert observed == Counter({"BASH": 1, "STRIKE_IRONCLAD": 1, "STRIKE_IRONCLAD+": 1})


# --- deck reconstruction -----------------------------------------------------


def neow(*picks: str, **lists) -> dict:
    stats = {"relic_choices": [{"choice": f"RELIC.{p}", "was_picked": True} for p in picks]}
    stats.update(lists)
    return stats


def ids(deck) -> list[str]:
    return [card_key(card_id, upgraded) for card_id, upgraded in deck]


def test_talisman_upgrades_last_strike_and_defend_and_bane_is_appended_at_a5() -> None:
    deck, issues = reconstruct_deck(
        "IRONCLAD", 10, neow("NEOWS_TALISMAN", upgraded_cards=["CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD"])
    )
    assert issues == []
    assert ids(deck) == [STRIKE] * 4 + [STRIKE + "+"] + [DEFEND] * 3 + [DEFEND + "+", BASH, BANE]
    deck, _ = reconstruct_deck("IRONCLAD", 4, neow("GOLDEN_PEARL"))
    assert ids(deck) == [STRIKE] * 5 + [DEFEND] * 4 + [BASH]


def test_gained_cards_append_in_gain_order_and_bones_curse_comes_last() -> None:
    deck, issues = reconstruct_deck("IRONCLAD", 0, neow("CURSED_PEARL", cards_gained=[{"id": "CARD.GREED"}]))
    assert issues == [] and ids(deck)[-1] == "GREED"
    stats = neow(
        "NEOWS_BONES",
        "HEFTY_TABLET",
        "POMANDER",
        cards_gained=[{"id": "CARD.REAPER_FORM"}, {"id": "CARD.INJURY"}, {"id": "CARD.REGRET"}],
        upgraded_cards=["CARD.REAPER_FORM"],
    )
    deck, issues = reconstruct_deck("IRONCLAD", 0, stats)
    assert issues == []
    assert ids(deck)[-3:] == ["REAPER_FORM+", "INJURY", "REGRET"]


def test_leafy_poultice_removes_first_strike_and_defend_and_appends_replacements() -> None:
    stats = neow(
        "LEAFY_POULTICE",
        cards_transformed=[
            {"original_card": {"id": "CARD.DEFEND_IRONCLAD"}, "final_card": {"id": "CARD.OFFERING"}},
            {"original_card": {"id": "CARD.STRIKE_IRONCLAD"}, "final_card": {"id": "CARD.DARK_EMBRACE"}},
        ],
    )
    deck, issues = reconstruct_deck("IRONCLAD", 0, stats)
    assert issues == []
    assert ids(deck) == [STRIKE] * 4 + [DEFEND] * 3 + [BASH, "DARK_EMBRACE", "OFFERING"]


def test_chosen_removal_of_interchangeable_copies_is_exact_but_split_upgrade_is_ambiguous() -> None:
    deck, issues = reconstruct_deck("IRONCLAD", 0, neow("PRECISE_SCISSORS", cards_removed=[{"id": "CARD.STRIKE_IRONCLAD"}]))
    assert issues == [] and ids(deck) == [STRIKE] * 4 + [DEFEND] * 4 + [BASH]
    # Upgrading one of five identical Strikes lands in a different deck slot
    # depending on the (unrecorded) choice.
    deck, issues = reconstruct_deck("IRONCLAD", 0, neow("POMANDER", upgraded_cards=["CARD.STRIKE_IRONCLAD"]))
    assert "deck_order_ambiguous:upgrade:STRIKE_IRONCLAD" in issues


def test_missing_neow_and_unknown_character_are_reported() -> None:
    deck, issues = reconstruct_deck("IRONCLAD", 0, None)
    assert issues == ["neow_summary_missing"] and len(deck) == 10
    assert reconstruct_deck("WATCHER", 0, None) == (None, ["character_unknown:WATCHER"])
    _deck, issues = reconstruct_deck("IRONCLAD", 0, neow("GOLDEN_PEARL", cards_gained=[{"id": "CARD.X"}]))
    assert "neow_gain_count_mismatch:1" in issues


# --- decisions ---------------------------------------------------------------


def test_hand_index_for_unique_and_interchangeable_copies() -> None:
    hand = [hand_card(STRIKE, 0), hand_card(STRIKE, 1), hand_card(BASH, 2)]
    assert resolve_hand_index(hand, BASH, None) == (2, None)
    # Removing either adjacent Strike leaves the same visible hand.
    assert resolve_hand_index(hand, STRIKE, None) == (0, None)
    assert resolve_hand_index(hand, DEFEND, None) == (None, "played_card_not_in_pre_hand:DEFEND_IRONCLAD")


def test_hand_index_uses_post_action_hand_for_separated_copies() -> None:
    hand = [hand_card(STRIKE, 0), hand_card(BASH, 1), hand_card(STRIKE, 2)]
    after_last = [hand_card(STRIKE, 0), hand_card(BASH, 1)]
    after_first = [hand_card(BASH, 0), hand_card(STRIKE, 1)]
    assert resolve_hand_index(hand, STRIKE, after_last) == (2, None)
    assert resolve_hand_index(hand, STRIKE, after_first) == (0, None)
    assert resolve_hand_index(hand, STRIKE, None) == (None, "hand_index_ambiguous:STRIKE_IRONCLAD")


def test_hand_index_tells_copies_apart_by_cost() -> None:
    hand = [hand_card(DEFEND, 0, cost="2"), hand_card(DEFEND, 1, cost="1")]
    assert resolve_hand_index(hand, DEFEND, [hand_card(DEFEND, 0, cost="1")]) == (0, None)


def test_hand_index_copies_differing_in_another_field_are_not_interchangeable() -> None:
    # Same (id, upgraded, cost), but one copy carries an enchantment hover tip (corpus:
    # "本能" Strikes): picking "the first" could play the wrong instance.
    plain = hand_card(STRIKE, 0)
    enchanted = {**hand_card(STRIKE, 1), "keywords": [{"name": "本能", "description": "这张牌的攻击伤害翻倍。"}]}
    bash = hand_card(BASH, 2, cost="2")
    hand = [plain, enchanted, bash]
    assert resolve_hand_index(hand, STRIKE, None) == (None, "hand_index_ambiguous:STRIKE_IRONCLAD")
    # The post-action hand shows which instance left.
    assert resolve_hand_index(hand, STRIKE, [{**enchanted, "index": 0}, {**bash, "index": 1}]) == (0, None)
    assert resolve_hand_index(hand, STRIKE, [{**plain, "index": 0}, {**bash, "index": 1}]) == (1, None)
    # A post-action hand that only the (id, upgraded) key matches cannot tell them apart.
    cheaper = {**plain, "cost": "0"}
    assert resolve_hand_index(hand, STRIKE, [cheaper, {**bash, "index": 1}]) == (None, "hand_index_ambiguous:STRIKE_IRONCLAD")


def test_hand_index_ignores_the_target_damage_preview() -> None:
    # The targeted copy's description shows the damage preview (6 vs 9 against a
    # Vulnerable enemy); the copies are still interchangeable.
    hand = [
        {**hand_card(STRIKE, 0), "description": "造成9点伤害。"},
        {**hand_card(STRIKE, 1), "description": "造成6点伤害。"},
        hand_card(BASH, 2, cost="2"),
    ]
    assert resolve_hand_index(hand, STRIKE, None) == (0, None)


def finish(card_id: str, pile: str = "Discard") -> tuple[str, dict]:
    return ("card_play_finished", {"card_play": {"card": {"id": f"CARD.{card_id}"}, "is_auto_play": False, "result_pile": pile}})


def damaged(combat_id: int, hp: int, block: int = 0) -> tuple[str, dict]:
    creature = {"is_player": False, "combat_id": combat_id, "hp": hp, "max_hp": 46, "block": block}
    return ("damage_received", {"actor": creature, "result": {"receiver": creature}})


def power(combat_id: int, power_id: str, amount: int) -> tuple[str, dict]:
    actor = {"is_player": False, "combat_id": combat_id, "hp": 38, "max_hp": 46, "block": 0}
    return ("power_received", {"actor": actor, "power": f"POWER.{power_id}", "amount": amount})


def mid_resolution_projection():
    state = combat_state(hand=[hand_card(STRIKE, 0), hand_card(STRIKE, 1)], draw=[pile_card(DEFEND)], energy=1)
    projection, issues = project_state(state, character="IRONCLAD", names=ironclad_names(), monster_ids={1: "NIBBIT"})
    assert issues == []
    return projection


def test_roll_forward_applies_damage_powers_and_the_discard_move() -> None:
    rolled, problem = roll_forward(
        mid_resolution_projection(),
        [damaged(1, 38), power(1, "VULNERABLE_POWER", 2), finish(BASH), ("checksum", {})],
        played_card=(BASH, False),
        visible_powers={"VULNERABLE_POWER"},
        played_in_limbo=True,
        played_destination="Discard",
    )
    assert problem is None
    assert rolled["enemies"][0]["hp"] == 38
    assert rolled["enemies"][0]["powers"] == [{"id": "VULNERABLE_POWER", "amount": 2}]
    assert rolled["player"]["discard"] == ["BASH"] and rolled["player"]["discard_count"] == 1
    assert rolled["player"]["energy"] == 1 and len(rolled["player"]["hand"]) == 2


def test_roll_forward_trusts_counts_over_event_order() -> None:
    # The snapshot already shows the card in the discard pile (its history
    # event was written later): no second append.
    rolled, problem = roll_forward(
        mid_resolution_projection(),
        [finish(BASH)],
        played_card=(BASH, False),
        visible_powers=set(),
        played_in_limbo=False,
        played_destination="Discard",
    )
    assert problem is None and rolled["player"]["discard"] == []
    _rolled, problem = roll_forward(
        mid_resolution_projection(), [finish(BASH)], played_card=(BASH, False), visible_powers=set()
    )
    assert problem == "pending_pile_move_unresolved"


def test_roll_forward_drops_killed_enemies_and_skips_invisible_powers() -> None:
    state = combat_state(hand=[], enemies=[enemy(1, 46, intents=(("Buff", ""),)), enemy(2, 46)])
    projection, _ = project_state(state, character="IRONCLAD", names=ironclad_names())
    rolled, problem = roll_forward(
        projection,
        [power(1, "HIDDEN_POWER", 1), damaged(2, 0)],
        played_card=None,
        visible_powers=set(),
    )
    assert problem is None
    assert [e["combat_id"] for e in rolled["enemies"]] == [1]
    assert rolled["enemies"][0]["powers"] == []


def test_roll_forward_refuses_what_it_cannot_replay() -> None:
    projection = mid_resolution_projection()
    assert roll_forward(projection, [("card_drawn", {})], played_card=None, visible_powers=set()) == (
        None,
        "pending_card_drawn",
    )
    # Weak on an attacking enemy changes its damage preview.
    assert roll_forward(projection, [power(1, "WEAK_POWER", 1)], played_card=None, visible_powers={"WEAK_POWER"}) == (
        None,
        "pending_power_changes_intent:WEAK_POWER",
    )


def test_gap_hand_safety() -> None:
    assert gap_keeps_hand([damaged(1, 30), finish(BASH)], played_card=(BASH, False))
    assert gap_keeps_hand([finish(BASH, "Exhaust"), ("card_exhausted", {"card": {"id": "CARD.BASH"}})], played_card=(BASH, False))
    assert not gap_keeps_hand([("card_drawn", {})], played_card=None)
    assert not gap_keeps_hand([finish(BASH, "Hand")], played_card=(BASH, False))
    assert not gap_keeps_hand([("card_exhausted", {"card": {"id": "CARD.BASH"}})], played_card=(BASH, False))


def test_visible_card_total() -> None:
    assert visible_card_total(mid_resolution_projection()) == 3


# --- sim registry ------------------------------------------------------------


def test_scan_sim_registry_reads_def_ids_from_sources(tmp_path: Path) -> None:
    core = tmp_path / "Core"
    (core / "Cards").mkdir(parents=True)
    (core / "Monsters").mkdir()
    (core / "obj").mkdir()
    (core / "Cards" / "Defs.cs").write_text(
        """
public abstract class StrikeBase : CardDef { protected StrikeBase(string id) : base(id, 1) { } }
public sealed class StrikeIronclad : StrikeBase
{
    public const string CardId = "STRIKE_IRONCLAD";
    public StrikeIronclad() : base(CardId) { }
    public override void OnUpgrade(CardInstance card) { }
}
public sealed class Greed : CardDef
{
    public Greed() : base("GREED", -1) { }
}
[SimExclude]
public sealed class TestOnly : CardDef { public TestOnly() : base("TEST_ONLY", 0) { } }
public static class StartingDecks { public const string IroncladCharacterId = "IRONCLAD"; }
""",
        encoding="utf-8",
    )
    (core / "Monsters" / "NibbitsWeak.cs").write_text(
        """
public sealed class NibbitsWeak : EncounterDef
{
    public const string EncounterIdEntry = "NIBBITS_WEAK";
    public NibbitsWeak() : base(EncounterIdEntry) { }
}
""",
        encoding="utf-8",
    )
    (core / "obj" / "Stale.cs").write_text(
        'public sealed class Stale : CardDef { public Stale() : base("STALE", 0) { } }', encoding="utf-8"
    )
    registry = scan_sim_registry(core)
    assert registry.cards == {"STRIKE_IRONCLAD", "GREED"}
    assert registry.upgradable_cards == {"STRIKE_IRONCLAD"}
    assert registry.encounters == {"NIBBITS_WEAK"}
    assert registry.characters == {"IRONCLAD"}


def test_sim_coverage_reasons() -> None:
    registry = SimRegistry.from_json(
        {"characters": ["IRONCLAD"], "encounters": ["NIBBITS_WEAK"], "cards": [STRIKE, "GREED"], "upgradable_cards": [STRIKE]},
        source="test",
    )
    assert sim_coverage_reasons(registry, character="IRONCLAD", encounter_id="NIBBITS_WEAK", deck=[(STRIKE, True), ("GREED", False)]) == []
    assert sim_coverage_reasons(
        registry, character="SILENT", encounter_id="SLIMES_WEAK", deck=[("GREED", True), ("ZAP", False)]
    ) == [
        "character_not_ported:SILENT",
        "encounter_not_ported:SLIMES_WEAK",
        "card_upgrade_not_ported:GREED",
        "card_not_ported:ZAP",
    ]
