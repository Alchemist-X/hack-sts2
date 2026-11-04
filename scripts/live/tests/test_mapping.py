"""Action mapping, one test per candidate kind: a twin candidate (as ``Twin.acts`` builds it) is matched by meaning onto
the real game's candidates (``mcp.acts_of`` over STS2_MCP states shaped like the live v0.111.0 MCP) and must yield the
right wire body. Also: view decoding / diffing, the UI glue rules, the play-server token decoder. No game, no sim --
except `test_every_library_action_kind_is_mapped`, which enumerates the library's RunActionKind (`LIBRARY_KINDS`: each
kind's semantic kind and its test here) and skips without the Release build."""

from __future__ import annotations

import pytest
from nn_twin import mcp, standin
from nn_twin.runner import Runner, canonical_seed
from nn_twin.semantics import Act, View, compare_views, match_act, screens_compatible


# ------------------------------------------------------------------------------------------------ MCP state builders

def card(cid, index, target="Self", upgraded=False, can_play=True, **kw):
    return {"id": cid, "name": cid.title(), "type": "Attack", "cost": "1", "is_upgraded": upgraded, "index": index,
            "target_type": target, "can_play": can_play, "keywords": [], **kw}


def enemy(mid, cid, hp, block=0, alive=True):
    return {"entity_id": f"{mid}_{cid - 1}", "id": mid, "combat_id": cid, "hp": hp, "max_hp": 30, "block": block,
            "is_alive": alive, "is_targetable": alive, "intents": []}


def player(**kw):
    p = {"character": "静默猎手", "hp": 60, "max_hp": 70, "block": 0, "gold": 99, "potions": [],
         "max_potion_slots": 2, "relics": [{"id": "RING_OF_THE_SNAKE"}]}
    p.update(kw)
    return p


def state(st, floor=2, **blocks):
    s = {"state_type": st, "run": {"act": 1, "floor": floor, "ascension": 10}, "player": player()}
    for k, v in blocks.items():
        if k == "player":
            s["player"].update(v)
        else:
            s[k] = v
    return s


def combat_state():
    return state("monster", battle={"round": 2, "turn": "player", "is_play_phase": True,
                                    "is_over_or_ending": False,
                                    "enemies": [enemy("CORPSE_SLUG", 1, 20), enemy("CORPSE_SLUG", 2, 25, block=3),
                                                enemy("TOADPOLE", 3, 0, alive=False)]},
                 player={"energy": 3, "max_energy": 3, "block": 4,
                         "hand": [card("STRIKE_SILENT", 0, "AnyEnemy"), card("DEFEND_SILENT", 1),
                                  card("STRIKE_SILENT", 2, "AnyEnemy"),
                                  card("NEUTRALIZE", 3, "AnyEnemy", upgraded=True),
                                  card("ASCENDERS_BANE", 4, "None", can_play=False)],
                         "potions": [{"id": "FIRE_POTION", "slot": 0, "can_use_in_combat": True,
                                      "target_type": "AnyEnemy"},
                                     {"id": "BLOCK_POTION", "slot": 1, "can_use_in_combat": True,
                                      "target_type": "AnyPlayer"}]},
                 execution={"queue_idle": True})


def twin(kind, key=(), pos=None, row=0, plan=()):
    return Act(kind, tuple(key), pos, row, f"twin {kind}", tuple(plan))


def wire_for(t, s, twin_acts=None, relaxed=False):
    m = match_act(t, mcp.acts_of(s), twin_acts, relaxed=relaxed)
    return None if m is None else m.native


# ------------------------------------------------------------------------------------------------ combat kinds

def test_play_targeted_goes_out_as_combat_id():
    s = combat_state()
    w = wire_for(twin("play", ("NEUTRALIZE", 1, 2), 3), s)
    assert w == {"action": "play_card", "card_index": 3, "target": "2"}


def test_play_duplicate_copies_pair_by_occurrence_rank():
    s = combat_state()
    t1 = twin("play", ("STRIKE_SILENT", 0, 1), 0, row=0)
    t2 = twin("play", ("STRIKE_SILENT", 0, 1), 2, row=5)
    assert wire_for(t1, s, [t1, t2])["card_index"] == 0
    assert wire_for(t2, s, [t1, t2])["card_index"] == 2


def test_play_untargeted_and_unplayable():
    s = combat_state()
    assert wire_for(twin("play", ("DEFEND_SILENT", 0, None), 1), s) == {"action": "play_card", "card_index": 1}
    assert wire_for(twin("play", ("ASCENDERS_BANE", 0, None), 4), s) is None      # can_play false: not offered


def test_play_dead_enemy_is_not_a_target():
    assert wire_for(twin("play", ("STRIKE_SILENT", 0, 3), 0), combat_state()) is None


def test_play_upgrade_mismatch_only_maps_relaxed():
    s = combat_state()
    t = twin("play", ("NEUTRALIZE", 0, 1), 3)
    assert wire_for(t, s) is None
    assert wire_for(t, s, relaxed=True) == {"action": "play_card", "card_index": 3, "target": "1"}


def test_end_turn():
    assert wire_for(twin("end_turn"), combat_state()) == {"action": "end_turn"}


def test_potion_use_targeted_and_self():
    s = combat_state()
    assert wire_for(twin("potion", ("FIRE_POTION", 0, 1), 0), s) == {"action": "use_potion", "slot": 0,
                                                                     "target": "1"}
    assert wire_for(twin("potion", ("BLOCK_POTION", 1, None), 1), s) == {"action": "use_potion", "slot": 1}


def test_potion_discard():
    assert wire_for(twin("discard_potion", ("BLOCK_POTION", 1), 1), combat_state()) == \
        {"action": "discard_potion", "slot": 1}


def test_combat_view_fields():
    v = mcp.view_of(combat_state())
    assert v.screen == "combat" and v.energy == 3 and v.block == 4 and v.floor == 2
    assert v.hand[0] == ("STRIKE_SILENT", 0) and v.hand[3] == ("NEUTRALIZE", 1)
    assert v.enemies == ((1, "CORPSE_SLUG", 20, 0), (2, "CORPSE_SLUG", 25, 3))      # dead TOADPOLE dropped
    assert v.potions == ((0, "FIRE_POTION"), (1, "BLOCK_POTION")) and v.relics == ("RING_OF_THE_SNAKE",)


# ------------------------------------------------------------------------------------------------ map / rewards

def test_travel_by_coordinate():
    s = state("map", map={"next_options": [{"index": 0, "col": 0, "row": 1, "type": "Monster"},
                                           {"index": 1, "col": 3, "row": 1, "type": "Unknown"}]})
    assert wire_for(twin("travel", (3, 1), 1), s) == {"action": "choose_map_node", "index": 1}
    assert wire_for(twin("travel", (4, 1), 2), s) is None
    assert mcp.view_of(s).detail["map_options"] == ((0, 1, "Monster"), (3, 1, "Unknown"))


def rewards_state(potions=()):
    return state("rewards", rewards={"items": [{"index": 0, "type": "gold", "gold_amount": 12},
                                               {"index": 1, "type": "potion", "potion_id": "FIRE_POTION"},
                                               {"index": 2, "type": "card"},
                                               {"index": 3, "type": "relic", "relic_id": "ANCHOR"},
                                               {"index": 4, "type": "card"}],
                                     "can_proceed": True},
                 player={"potions": list(potions)})


def test_claim_rewards_by_identity():
    s = rewards_state()
    assert wire_for(twin("claim_reward", ("gold", 12), 0), s) == {"action": "claim_reward", "index": 0}
    assert wire_for(twin("claim_reward", ("potion", "FIRE_POTION"), 1), s) == {"action": "claim_reward", "index": 1}
    assert wire_for(twin("claim_reward", ("relic", "ANCHOR"), 3), s) == {"action": "claim_reward", "index": 3}
    assert wire_for(twin("claim_reward", ("gold", 13), 0), s) is None


def test_two_card_rewards_pair_by_rank():
    s = rewards_state()
    a = twin("claim_reward", ("card", None), 2, row=2)
    b = twin("claim_reward", ("card", None), 4, row=4)
    assert wire_for(a, s, [a, b])["index"] == 2 and wire_for(b, s, [a, b])["index"] == 4


def test_potion_reward_with_full_belt_is_not_offered():
    full = [{"id": "X", "slot": 0}, {"id": "Y", "slot": 1}]
    assert wire_for(twin("claim_reward", ("potion", "FIRE_POTION"), 1), rewards_state(full)) is None


def test_leave_rewards():
    assert wire_for(twin("leave", ("rewards",)), rewards_state()) == {"action": "proceed"}


def test_card_reward_pick_skip_reroll():
    s = state("card_reward", card_reward={"cards": [card("PREPARED", 0), card("ACCURACY", 1, upgraded=True)],
                                          "can_skip": True})
    assert wire_for(twin("pick_card", ("ACCURACY", 1), 1), s) == {"action": "select_card_reward", "card_index": 1}
    assert wire_for(twin("card_alt", ("skip",)), s) == {"action": "skip_card_reward"}
    s2 = state("card_reward", card_reward={"cards": [card("PREPARED", 0)],
                                           "alternatives": [{"id": "REROLL", "choice_index": 0},
                                                            {"id": "Skip", "choice_index": 1}]})
    assert wire_for(twin("card_alt", ("reroll",), 0), s2) == {"action": "select_card_reward_alternative",
                                                              "choice_index": 0}
    assert wire_for(twin("card_alt", ("skip",)), s2) == {"action": "skip_card_reward", "choice_index": 1}


def test_prompt_pick_matches_a_card_reward_screen():
    """A relic's card reward: Prompt(CardReward) in the sim, a card_reward screen in the game."""
    s = state("card_reward", card_reward={"cards": [card("PREPARED", 0), card("ACCURACY", 1)], "can_skip": True})
    assert wire_for(twin("select", ("PREPARED", 0), 0), s) == {"action": "select_card_reward", "card_index": 0}
    assert wire_for(twin("cancel"), s) == {"action": "skip_card_reward"}


def test_macro_take_reward_card_is_a_two_step_plan():
    plan = (twin("claim_reward", ("card", None), 4, row=None), twin("pick_card", ("ACCURACY", 0), 1, row=None))
    m = twin("macro", ("TakeRewardCard", 4, 1), 1, row=9, plan=plan)
    r = Runner.__new__(Runner)
    first = r.map_row(m, [m], mcp.acts_of(rewards_state()), relaxed=False)
    assert first.native == {"action": "claim_reward", "index": 2}      # first card reward: rank 0 of ("card", None)
    s2 = state("card_reward", card_reward={"cards": [card("PREPARED", 0), card("ACCURACY", 1)], "can_skip": True})
    assert match_act(plan[1], mcp.acts_of(s2)).native == {"action": "select_card_reward", "card_index": 1}


def macro(kind, a, b, second, row):
    return twin("macro", (kind, a, b), b, row=row, plan=(twin("claim_reward", ("card", None), a, row=None), second))


def test_macro_take_reward_alternative_is_a_two_step_plan():
    """TakeRewardAlternative(A, B) = ClaimReward(A), RewardAlternative(B): REROLL (Driftwood) / SACRIFICE (Pael's Wing),
    never Skip (declining is ProceedRewards)."""
    r = Runner.__new__(Runner)
    reroll = macro("TakeRewardAlternative", 2, 0, twin("card_alt", ("reroll",), 0, row=None), row=4)
    assert r.map_row(reroll, [reroll], mcp.acts_of(rewards_state()), relaxed=False).native == \
        {"action": "claim_reward", "index": 2}
    opened = state("card_reward", card_reward={"cards": [card("PREPARED", 0)],
                                               "alternatives": [{"id": "REROLL", "choice_index": 0},
                                                                {"id": "SACRIFICE", "choice_index": 1},
                                                                {"id": "Skip", "choice_index": 2}]})
    assert match_act(reroll.plan[1], mcp.acts_of(opened)).native == {"action": "select_card_reward_alternative",
                                                                      "choice_index": 0}
    sacrifice = twin("card_alt", ("sacrifice",), 1, row=None)
    assert match_act(sacrifice, mcp.acts_of(opened)).native == {"action": "select_card_reward_alternative",
                                                                "choice_index": 1}


def test_macro_open_step_pairs_card_rewards_by_rank():
    """Two unclaimed card rewards: each macro opens the game's card reward of the same rank, whatever the indices
    (the game's reward list here has lost its claimed gold, so its indices are one lower than the twin's)."""
    first = macro("TakeRewardCard", 1, 0, twin("pick_card", ("PREPARED", 0), 0, row=None), row=1)
    second = [macro("TakeRewardCard", 3, i, twin("pick_card", (c, 0), i, row=None), row=4 + i)
              for i, c in enumerate(("ACCURACY", "DEFLECT"))]
    tacts = [first, twin("claim_reward", ("potion", "FIRE_POTION"), 2, row=1), *second,
             twin("leave", ("rewards",), row=6)]
    s = state("rewards", rewards={"items": [{"index": 0, "type": "card"}, {"index": 1, "type": "potion",
                                                                           "potion_id": "FIRE_POTION"},
                                            {"index": 2, "type": "card"}], "can_proceed": True})
    r = Runner.__new__(Runner)
    racts = mcp.acts_of(s)
    assert r.map_row(first, tacts, racts, relaxed=False).native == {"action": "claim_reward", "index": 0}
    assert {r.map_row(m, tacts, racts, relaxed=False).native["index"] for m in second} == {2}


def test_standin_macro_steps():
    """The play server has no macro rows: the plan's acts are its 'reward', 'card' and 'reroll' / 'alt' tokens."""
    base = {"phase": "Rewards", "act": 1, "floor": 2, "terminal": False, "player": {"hp": 1, "max_hp": 1, "gold": 0},
            "rewards": [{"index": 0, "type": "Gold", "detail": "12 gold"}, {"index": 1, "type": "Card"}],
            "legal_actions": [{"token": "reward 0"}, {"token": "reward 1"}, {"token": "proceed"}]}
    m = macro("TakeRewardCard", 1, 1, twin("pick_card", ("ACCURACY", 1), 1, row=None), row=2)
    r = Runner.__new__(Runner)
    assert r.map_row(m, [m], standin.acts_of(base), relaxed=False).native == "reward 1"
    opened = {"phase": "CardReward", "act": 1, "floor": 2, "terminal": False,
              "player": {"hp": 1, "max_hp": 1, "gold": 0},
              "card_reward": {"cards": [{"index": 0, "id": "PREPARED", "upgraded": False},
                                        {"index": 1, "id": "ACCURACY", "upgraded": True}],
                              "alternatives": [{"index": 0, "id": "REROLL"}, {"index": 1, "id": "SACRIFICE"},
                                               {"index": 2, "id": "Skip"}]},
              "legal_actions": [{"token": "card 0"}, {"token": "card 1"}, {"token": "reroll"}, {"token": "alt 1"},
                                {"token": "skip"}]}
    acts = standin.acts_of(opened)
    assert match_act(m.plan[1], acts).native == "card 1"
    assert match_act(twin("card_alt", ("reroll",)), acts).native == "reroll"
    assert match_act(twin("card_alt", ("sacrifice",)), acts).native == "alt 1"


# ------------------------------------------------------------------------------------------------ shop / rest / chest

def shop_state():
    return state("shop", shop={"items": [
        {"index": 0, "category": "card", "price": 48, "is_stocked": True, "can_afford": True, "card_id": "PREPARED"},
        {"index": 1, "category": "card", "price": 90, "is_stocked": True, "can_afford": True, "card_id": "APOTHEOSIS"},
        {"index": 7, "category": "relic", "price": 150, "is_stocked": True, "can_afford": False, "relic_id": "ANCHOR"},
        {"index": 10, "category": "potion", "price": 51, "is_stocked": True, "can_afford": True,
         "potion_id": "FIRE_POTION"},
        {"index": 13, "category": "card_removal", "price": 75, "is_stocked": True, "can_afford": True},
        {"index": 2, "category": "card", "price": 49, "is_stocked": False, "can_afford": True, "card_id": "DEFLECT"}],
        "can_proceed": False}, player={"gold": 100})


def test_buy_card_colorless_potion_removal():
    s = shop_state()
    assert wire_for(twin("buy", ("card", "PREPARED"), 0), s) == {"action": "shop_purchase", "index": 0}
    # the sim's colorless_card category is a plain "card" on both sides
    assert wire_for(twin("buy", ("card", "APOTHEOSIS"), 1), s) == {"action": "shop_purchase", "index": 1}
    assert wire_for(twin("buy", ("potion", "FIRE_POTION"), 10), s) == {"action": "shop_purchase", "index": 10}
    assert wire_for(twin("buy", ("card_removal", None), 13), s) == {"action": "shop_purchase", "index": 13}
    assert wire_for(twin("buy", ("relic", "ANCHOR"), 7), s) is None              # cannot afford: not offered
    assert wire_for(twin("buy", ("card", "DEFLECT"), 2), s) is None              # sold out


def test_leave_shop_and_shop_view():
    s = shop_state()
    assert wire_for(twin("leave", ("shop",)), s) == {"action": "proceed"}
    assert ("card_removal", None, 75) in mcp.view_of(s).detail["shop"]
    assert all(item[1] != "DEFLECT" for item in mcp.view_of(s).detail["shop"])


def test_rest_options_and_leave():
    s = state("rest_site", rest_site={"options": [{"index": 0, "id": "HEAL", "is_enabled": True},
                                                  {"index": 1, "id": "SMITH", "is_enabled": True}],
                                      "can_proceed": False})
    assert wire_for(twin("rest", ("SMITH",), 1), s) == {"action": "choose_rest_option", "index": 1}
    done = state("rest_site", rest_site={"options": [], "can_proceed": True})
    assert wire_for(twin("leave", ("rest",)), done) == {"action": "proceed"}


def test_treasure_open_take_skip():
    closed = state("treasure", treasure={"chest_opened": False, "relics": [], "can_proceed": False})
    assert wire_for(twin("open_chest"), closed) == {"action": "open_chest"}
    opened = state("treasure", treasure={"relics": [{"index": 0, "id": "ANCHOR"}], "can_proceed": True})
    assert wire_for(twin("take_relic", ("ANCHOR",)), opened) == {"action": "claim_treasure_relic", "index": 0}
    assert wire_for(twin("leave", ("treasure",)), opened) == {"action": "proceed"}
    assert mcp.view_of(opened).detail["treasure_relic"] == "ANCHOR"


# ------------------------------------------------------------------------------------------------ events

def event_state(options, dialogue=False, eid="BRAIN_LEECH"):
    return state("event", event={"event_id": eid, "in_dialogue": dialogue, "options": options})


def test_event_option_by_index_and_proceed_option():
    s = event_state([{"index": 0, "title": "A", "is_locked": False, "is_proceed": False},
                     {"index": 1, "title": "B", "is_locked": True, "is_proceed": False},
                     {"index": 2, "title": "C", "is_locked": False, "is_proceed": False}])
    assert wire_for(twin("event_option", (2,), 2), s) == {"action": "choose_event_option", "index": 2}
    assert wire_for(twin("event_option", (1,), 1), s) is None                   # locked
    assert mcp.view_of(s).detail == {"event": "BRAIN_LEECH", "event_options_n": 3}
    fin = event_state([{"index": 0, "title": "继续", "is_locked": False, "is_proceed": True}])
    # the sim's LeaveEvent and a ChooseEventOption on a proceed option both find the game's proceed button
    assert wire_for(twin("leave", ("event",)), fin) == {"action": "choose_event_option", "index": 0}
    assert wire_for(twin("event_option", (0,), 0), fin) == {"action": "choose_event_option", "index": 0}
    assert mcp.view_of(fin).detail["event_options_n"] == 0


def test_event_dialogue_is_real_only_glue():
    s = event_state([], dialogue=True)
    obs = _obs(s)
    r = Runner.__new__(Runner)
    assert r.real_glue(obs, View("event"))[0] == "dialogue"
    assert r.real_glue(obs, View("event"))[1].native == {"action": "advance_dialogue"}


# ------------------------------------------------------------------------------------------------ selections

def test_hand_select_pick_and_confirm():
    s = state("hand_select", hand_select={"mode": "simple_select", "cards": [card("DEFEND_SILENT", 0),
                                                                              card("STRIKE_SILENT", 2)],
                                          "can_confirm": False})
    assert wire_for(twin("select", ("STRIKE_SILENT", 0), 1), s) == {"action": "combat_select_card", "card_index": 2}
    s2 = state("hand_select", hand_select={"cards": [card("DEFEND_SILENT", 0)], "selected_cards": [{"index": 2}],
                                           "can_confirm": True})
    assert wire_for(twin("confirm"), s2) == {"action": "combat_confirm_selection"}


def grid(screen_type, cards, **kw):
    block = {"screen_type": screen_type, "selection_tracking": "available", "min_select": 1, "max_select": 1,
             "cards": cards, "selected_indices": [], "selected_count": 0, "preview_showing": False,
             "can_cancel": False, "can_confirm": False}
    block.update(kw)
    return state("card_select", card_select=block)


def test_deck_grid_duplicates_by_rank_and_confirm_cancel():
    cards = [card("STRIKE_SILENT", 0), card("STRIKE_SILENT", 1), card("DEFEND_SILENT", 2)]
    s = grid("upgrade", cards, can_cancel=True)
    a = twin("select", ("STRIKE_SILENT", 0), 0, row=0)
    b = twin("select", ("STRIKE_SILENT", 0), 1, row=1)
    assert wire_for(b, s, [a, b]) == {"action": "select_card", "index": 1}
    assert wire_for(twin("cancel"), s) == {"action": "cancel_selection"}
    preview = grid("upgrade", cards, preview_showing=True, can_confirm=True, can_cancel=True, selected_count=1,
                   selected_indices=[1])
    acts = mcp.acts_of(preview)
    assert [a.kind for a in acts] == ["confirm", "cancel"]                         # no picks while previewing


def test_choose_a_card_skip():
    s = grid("choose", [card("PRODUCTION", 0), card("ULTIMATE_DEFEND", 1)], can_skip=True)
    assert wire_for(twin("select", ("ULTIMATE_DEFEND", 0), 1), s) == {"action": "select_card", "index": 1}
    assert wire_for(twin("cancel"), s) == {"action": "skip_card_selection"}
    # the sim declines a min-0 choose-a-card prompt by confirming nothing; the game has a skip button for it
    assert wire_for(twin("confirm", ("empty",)), s) == {"action": "skip_card_selection"}
    assert wire_for(twin("confirm"), s) is None                    # a confirm after picks never becomes a skip
    s2 = grid("simple_select", [card("PRODUCTION", 0)], min_select=0, can_confirm=True)
    assert wire_for(twin("confirm", ("empty",)), s2) == {"action": "confirm_selection"}


def test_bundle_and_relic_selection():
    b = state("bundle_select", bundle_select={"bundles": [{"index": 0, "cards": []}, {"index": 1, "cards": []}],
                                              "preview_showing": False, "can_confirm": False})
    t0, t1 = twin("select", ("bundle",), 0, row=0), twin("select", ("bundle",), 1, row=1)
    assert wire_for(t1, b, [t0, t1]) == {"action": "select_bundle", "index": 1}
    r = state("relic_select", relic_select={"relics": [{"index": 0, "id": "ANCHOR"}, {"index": 1, "id": "LANTERN"}],
                                            "can_skip": True})
    assert wire_for(twin("select", ("LANTERN", 0), 1), r) == {"action": "select_relic", "index": 1}
    assert wire_for(twin("cancel"), r) == {"action": "skip_relic_selection"}


# ------------------------------------------------------------------------------------------------ views, glue

def _obs(s):
    from nn_twin.game import Observation
    return Observation(raw=s, view=mcp.view_of(s), acts=mcp.acts_of(s))


def test_compare_views_hard_soft_and_screen_compat():
    a = View("combat", act=1, floor=3, hp=50, max_hp=70, gold=99, energy=3, block=0,
             hand=(("A", 0), ("B", 0)), enemies=((1, "X", 10, 0),), potions=(), relics=("R",))
    b = View("combat", act=1, floor=3, hp=50, max_hp=70, gold=99, energy=3, block=0,
             hand=(("B", 0), ("A", 0)), enemies=((1, "X", 10, 0),), potions=(), relics=("R",))
    hard, soft = compare_views(a, b)
    assert hard == [] and [d["field"] for d in soft] == ["hand_order"]
    b.hp, b.enemies = 49, ((1, "X", 9, 0),)
    assert {d["field"] for d in compare_views(a, b)[0]} == {"hp", "enemies"}
    assert screens_compatible("prompt", "card_reward") and not screens_compatible("prompt", "combat")
    assert compare_views(View("prompt", floor=2), View("card_reward", floor=2)) == ([], [])


def test_glue_confirm_after_the_twin_settled_its_pick():
    s = grid("upgrade", [card("STRIKE_SILENT", 0)], preview_showing=True, can_confirm=True, selected_count=1)
    r = Runner.__new__(Runner)
    assert r.real_glue(_obs(s), View("rest"))[0] == "confirm"
    assert r.real_glue(_obs(s), View("prompt")) is None                         # twin still picking: lockstep


def test_glue_leave_treasure_after_the_claim():
    s = state("treasure", treasure={"can_proceed": True})
    r = Runner.__new__(Runner)
    why, act = r.real_glue(_obs(s), View("map"))
    assert why == "proceed" and act.native == {"action": "proceed"}
    pending = state("treasure", treasure={"relics": [{"index": 0, "id": "ANCHOR"}], "can_proceed": True})
    assert r.real_glue(_obs(pending), View("map")) is None                      # relic not taken yet: no glue


def test_twin_only_steps():
    r = Runner.__new__(Runner)
    opened = state("treasure", treasure={"relics": [{"index": 0, "id": "ANCHOR"}], "can_proceed": True})
    t_open = twin("open_chest", row=0)
    assert r.twin_glue(_obs(opened), View("treasure"), [t_open]) == ("open_chest", t_open)
    t_conf = twin("confirm", row=3)
    assert r.twin_glue(_obs(combat_state()), View("prompt"), [t_conf]) == ("confirm", t_conf)
    assert r.twin_glue(_obs(combat_state()), View("prompt"), [twin("select", ("X", 0), 0)]) == \
        ("twin_only_prompt", None)


def test_fallback_is_deterministic_and_real_side_only():
    r = Runner.__new__(Runner)
    a = r.fallback(_obs(combat_state()))
    assert a.kind == "play" and a.native["card_index"] == 1        # untargeted first, then lowest-hp target
    assert r.fallback(_obs(rewards_state())).native == {"action": "claim_reward", "index": 0}


def test_potion_discard_outside_combat():
    """derive_legal_actions lists it only with can_discard_out_of_combat; the MCP accepts it on non-combat screens."""
    s = event_state([{"index": 0, "title": "A", "is_locked": False, "is_proceed": False}])
    s["player"]["potions"] = [{"id": "FIRE_POTION", "slot": 0, "can_use_in_combat": True}]
    assert wire_for(twin("discard_potion", ("FIRE_POTION", 0), 0), s) == {"action": "discard_potion", "slot": 0}
    assert wire_for(twin("potion", ("FIRE_POTION", 0, None), 0), s) is None        # drinking it: not offered


def test_unoffered_choice_is_substituted_without_leaving_lockstep(tmp_path):
    import numpy as np
    from nn_twin.twin import Decision

    s = event_state([{"index": 0, "title": "A", "is_locked": False, "is_proceed": False}])
    tacts = [twin("potion", ("FIRE_POTION", 0, None), 0, row=0), twin("event_option", (0,), 0, row=1)]
    dec = Decision(row=0, probs=np.array([0.7, 0.3]), value=0.0, win_prob=None, combat_hp=None)
    r = Runner.__new__(Runner)
    r.mode, r.first_desyncs, r.i = "lockstep", [], 0
    from collections import Counter
    r.stats = Counter()
    row, act, how = r.choose(_obs(s), tacts, dec, in_sync=True)
    assert (row, how, r.mode) == (1, "substitute", "lockstep") and act.native["action"] == "choose_event_option"
    row, act, how = r.choose(_obs(s), tacts, dec, in_sync=False)
    assert (row, how, r.mode) == (1, "follow", "follow")


def test_canonical_seed():
    assert canonical_seed("c1a10si01") == "C1A10S101"
    with pytest.raises(ValueError):
        canonical_seed("bad seed!")


# ------------------------------------------------------------------------------------------------ play-server tokens

def test_standin_tokens_decode_to_the_same_keys():
    o = {"phase": "Combat", "act": 1, "floor": 2, "terminal": False,
         "player": {"hp": 50, "max_hp": 70, "gold": 99, "energy": 3, "block": 0, "relics": [{"id": "R"}],
                    "potions": [{"slot": 0, "id": "FIRE_POTION"}, None]},
         "combat": {"hand": [{"index": 0, "id": "STRIKE_SILENT", "upgraded": False},
                             {"index": 1, "id": "SURVIVOR", "upgraded": True}],
                    "enemies": [{"index": 0, "uid": "u1", "id": "SLIME", "hp": 10, "block": 0},
                                {"index": 2, "uid": "u3", "id": "SLIME", "hp": 12, "block": 0}]},
         "legal_actions": [{"token": "play 0 -> 2"}, {"token": "play 1"}, {"token": "potion 0 -> 0"},
                           {"token": "discard 0"}, {"token": "end"}]}
    acts = standin.acts_of(o)
    assert [(a.kind, a.key) for a in acts] == [("play", ("STRIKE_SILENT", 0, 3)), ("play", ("SURVIVOR", 1, None)),
                                               ("potion", ("FIRE_POTION", 0, 1)),
                                               ("discard_potion", ("FIRE_POTION", 0)), ("end_turn", ())]
    v = standin.view_of(o)
    assert v.enemies == ((1, "SLIME", 10, 0), (3, "SLIME", 12, 0)) and v.potions == ((0, "FIRE_POTION"),)


def test_standin_bundle_prompt_uses_the_bundle_key():
    """A bundle option's id is its card list on the play server; all sides key bundles as ("bundle",) + rank."""
    o = {"phase": "Prompt", "act": 1, "floor": 1, "terminal": False, "player": {"hp": 1, "max_hp": 1, "gold": 0},
         "prompt": {"kind": "Bundle", "picked": [], "options": [{"index": 0, "id": "A,B,C", "upgraded": False},
                                                                {"index": 1, "id": "D,E,F", "upgraded": False}]},
         "legal_actions": [{"token": "pick 0"}, {"token": "pick 1"}]}
    assert [(a.key, a.pos) for a in standin.acts_of(o)] == [(("bundle",), 0), (("bundle",), 1)]
    assert "prompt_options" not in standin.view_of(o).detail
    t0, t1 = twin("select", ("bundle",), 0, row=0), twin("select", ("bundle",), 1, row=1)
    assert match_act(t1, standin.acts_of(o), [t0, t1]).native == "pick 1"


# ------------------------------------------------------------------------------------------------ the library's kinds

# Every RunActionKind of the library -> (the semantic kind Twin.acts decodes it to, the test above that maps that kind
# onto the game's candidates). test_every_library_action_kind_is_mapped fails when the library gains a kind not listed.
LIBRARY_KINDS = {
    "PlayCard": ("play", "test_play_targeted_goes_out_as_combat_id"),
    "EndTurn": ("end_turn", "test_end_turn"),
    "UsePotion": ("potion", "test_potion_use_targeted_and_self"),
    "DiscardPotion": ("discard_potion", "test_potion_discard"),
    "Travel": ("travel", "test_travel_by_coordinate"),
    "ClaimReward": ("claim_reward", "test_claim_rewards_by_identity"),
    "ProceedRewards": ("leave", "test_leave_rewards"),
    "PickRewardCard": ("pick_card", "test_card_reward_pick_skip_reroll"),
    "RewardAlternative": ("card_alt", "test_card_reward_pick_skip_reroll"),
    "BuyEntry": ("buy", "test_buy_card_colorless_potion_removal"),
    "LeaveShop": ("leave", "test_leave_shop_and_shop_view"),
    "ChooseRestOption": ("rest", "test_rest_options_and_leave"),
    "LeaveRestSite": ("leave", "test_rest_options_and_leave"),
    "OpenChest": ("open_chest", "test_treasure_open_take_skip"),
    "TakeTreasureRelic": ("take_relic", "test_treasure_open_take_skip"),
    "SkipTreasureRelic": ("leave", "test_treasure_open_take_skip"),
    "ChooseEventOption": ("event_option", "test_event_option_by_index_and_proceed_option"),
    "LeaveEvent": ("leave", "test_event_option_by_index_and_proceed_option"),
    "PromptSelect": ("select", "test_hand_select_pick_and_confirm"),
    "PromptConfirm": ("confirm", "test_hand_select_pick_and_confirm"),
    "PromptCancel": ("cancel", "test_deck_grid_duplicates_by_rank_and_confirm_cancel"),
    "TakeRewardCard": ("macro", "test_macro_take_reward_card_is_a_two_step_plan"),
    "TakeRewardAlternative": ("macro", "test_macro_take_reward_alternative_is_a_two_step_plan"),
}


def test_every_library_action_kind_is_mapped():
    """The library's RunActionKind, enumerated: every kind is listed above, decodes through ``Twin.acts`` to its
    semantic kind and formats as a play-server token (``Twin.row_to_token``); the macros read their card / alternative
    from ``reward_choices`` (synthetic candidate rows in a fresh twin's zeroed buffers; needs the Release build)."""
    pytest.importorskip("sts2vec")
    from nn_twin.twin import Twin

    try:
        tw = Twin(content="all")
    except Exception as e:  # noqa: BLE001  (LoaderError: no Release build of the library)
        pytest.skip(f"no sts2vec library: {e}")
    try:
        names = list(tw.kinds)
        assert sorted(names) == sorted(LIBRARY_KINDS), \
            f"map these RunActionKind in Twin._act / row_to_token and list them: {set(names) ^ set(LIBRARY_KINDS)}"
        for name, (_, test) in LIBRARY_KINDS.items():
            assert callable(globals().get(test)), f"{name}: no test {test}"
        obs, cc, rc = tw.obs, tw.c["cand"], tw.schema.cols("reward_choices")
        obs["cand_n"][0] = 1
        cand = obs["cand"][0]

        def row(kind, a=0, b=-1, k=0, card_id=0):
            cand[0, :] = 0
            cand[0, [cc["kind"], cc["A"], cc["B"], cc["option_index"], cc["card_id"]]] = (kind, a, b, k, card_id)
            return tw.acts()[0], tw.row_to_token(0)

        for i, name in enumerate(names):
            act, tok = row(i)
            assert act.kind == LIBRARY_KINDS[name][0], (name, act)
            assert not tok.startswith(name), f"{name}: no play-server token ({tok!r})"
        # reward_choices: reward 1 offers PREPARED+ as its card 2; reward 3 has REROLL (alternative 0), SACRIFICE (1)
        prepared, alts = tw.V.cards.index_of("PREPARED"), tw.E["CardRewardAlt"]
        rows = obs["reward_choices"][0]
        rows[0, [rc["reward_index"], rc["option"], rc["card_id"], rc["upgrade"]]] = (1, 2, prepared, 1)
        rows[1, [rc["reward_index"], rc["option"], rc["alt_id"]]] = (3, 0, alts.index_of("REROLL"))
        rows[2, [rc["reward_index"], rc["option"], rc["alt_id"]]] = (3, 1, alts.index_of("SACRIFICE"))
        take, alt = names.index("TakeRewardCard"), names.index("TakeRewardAlternative")
        m, tok = row(take, 1, 2, 0, prepared)
        assert [(p.kind, p.key, p.pos) for p in m.plan] == [("claim_reward", ("card", None), 1),
                                                            ("pick_card", ("PREPARED", 1), 2)]
        assert tok == "reward 1; card 2"
        m, tok = row(alt, 3, 0, 1)
        assert m.plan[1].key == ("reroll",) and tok == "reward 3; reroll"
        m, tok = row(alt, 3, 1, 2)
        assert m.plan[1].key == ("sacrifice",) and tok == "reward 3; alt 1"
    finally:
        tw.close()


# ------------------------------------------------------------------------------------------------ 2026-11-04 real-run fixes

def test_relic_reward_without_an_id_matches_by_type():
    """STS2_MCP's rewards items carry no relic id (BuildRewardsState): ("relic", None) stands for the twin's relic."""
    s = state("rewards", rewards={"items": [{"index": 0, "type": "card"}, {"index": 1, "type": "gold", "gold_amount": 31},
                                            {"index": 2, "type": "relic", "description": "打击木人"}],
                                  "can_proceed": True})
    assert wire_for(twin("claim_reward", ("relic", "STRIKE_DUMMY"), 2), s) == {"action": "claim_reward", "index": 2}
    tv = View("rewards", floor=2, detail={"rewards": (("card", None), ("gold", 31), ("relic", "STRIKE_DUMMY"))})
    assert compare_views(tv, mcp.view_of(s))[0] == []
    tv.detail["rewards"] = (("card", None), ("gold", 30), ("relic", "STRIKE_DUMMY"))
    assert [d["field"] for d in compare_views(tv, mcp.view_of(s))[0]] == ["rewards"]


def test_event_rewards_screen_closed_by_the_game_is_left_by_the_twin():
    s = event_state([{"index": 0, "title": "继续", "is_locked": False, "is_proceed": True}])
    r = Runner.__new__(Runner)
    leave = twin("leave", ("rewards",), row=1)
    acts = [twin("discard_potion", ("DROPLET_OF_PRECOGNITION", 0), 0, row=0), leave]
    assert r.twin_glue(_obs(s), View("rewards"), acts) == ("leave_rewards", leave)
    pending = acts + [twin("claim_reward", ("gold", 10), 0, row=2)]
    assert r.twin_glue(_obs(s), View("rewards"), pending) is None       # something left to claim: not auto-closed


def test_follow_never_maps_across_screens_and_never_discards_as_a_second_choice():
    import numpy as np
    from collections import Counter
    from nn_twin.twin import Decision

    s = event_state([{"index": 0, "title": "继续", "is_locked": False, "is_proceed": True}])
    s["player"]["potions"] = [{"id": "DROPLET_OF_PRECOGNITION", "slot": 0, "can_use_in_combat": True}]
    tacts = [twin("leave", ("rewards",), row=0), twin("discard_potion", ("DROPLET_OF_PRECOGNITION", 0), 0, row=1)]
    dec = Decision(row=0, probs=np.array([0.9, 0.1]), value=0.0, win_prob=None, combat_hp=None)
    r = Runner.__new__(Runner)
    r.mode, r.first_desyncs, r.i, r.stats = "follow", [], 0, Counter()
    row, act, how = r.choose(_obs(s), tacts, dec, tv_screen="rewards")
    assert how == "fallback" and act.kind != "discard_potion"           # the twin is on another screen
    row, act, how = r.choose(_obs(s), tacts, dec, tv_screen="event")
    assert how == "fallback" or act.kind != "discard_potion"            # a discard is never the stand-in choice
    assert Runner.second_choices(dec, tacts) == [0]
    dec2 = Decision(row=1, probs=np.array([0.1, 0.9]), value=0.0, win_prob=None, combat_hp=None)
    assert Runner.second_choices(dec2, tacts) == [1, 0]                 # ... unless the model wants it most


def test_cancel_on_a_preview_is_followed_by_a_second_cancel():
    """Smith -> pick -> cancel: the sim's PromptCancel closes the selection, the game's cancel on the preview returns to
    the grid; the runner cancels again (never confirms an upgrade the network cancelled)."""
    grid_back = grid("upgrade", [card("STRIKE_SILENT", 0), card("FLICK_FLACK", 1)], can_cancel=True)
    r = Runner.__new__(Runner)
    r.last_twin_kind = "cancel"
    why, act = r.real_glue(_obs(grid_back), View("rest"))
    assert why == "cancel" and act.native == {"action": "cancel_selection"}
    stuck = grid("upgrade", [card("STRIKE_SILENT", 0)], preview_showing=True, can_confirm=True, can_cancel=False,
                 selected_count=1)
    assert r.real_glue(_obs(stuck), View("rest")) is None               # no cancel: never fall through to confirm
    r.last_twin_kind = "select"
    assert r.real_glue(_obs(stuck), View("rest"))[0] == "confirm"        # the twin settled on its pick
