from sts2rec.information import InformationMode, build_information_view
from sts2rec.legal_actions import audit_action_space, derive_legal_actions


def test_event_does_not_offer_a_button_disabled_during_choice_resolution():
    state={"state_type":"event","event":{"options":[
        {"index":0,"is_locked":False,"was_chosen":True,"is_enabled":False},
        {"index":1,"is_locked":False,"was_chosen":False,"is_enabled":False},
    ]}}
    assert derive_legal_actions(state)==[]
    state["event"]["options"][1]["is_enabled"]=True
    assert [a["index"] for a in derive_legal_actions(state)]==[1]


def test_shop_enumerates_every_affordable_stocked_item_with_full_payload() -> None:
    state = {
        "state_type": "shop",
        "shop": {
            "items": [
                {
                    "index": 0,
                    "category": "card",
                    "card_id": "WISP",
                    "card_description": "Gain energy.",
                    "price": 50,
                    "is_stocked": True,
                    "can_afford": True,
                },
                {
                    "index": 1,
                    "category": "relic",
                    "relic_id": "PEN_NIB",
                    "relic_description": "Every tenth attack deals double damage.",
                    "price": 200,
                    "is_stocked": True,
                    "can_afford": False,
                },
            ]
        },
    }
    actions = derive_legal_actions(state)
    assert [action["action"] for action in actions] == ["shop_purchase", "proceed"]
    assert actions[0]["candidate"]["card_description"] == "Gain energy."
    assert audit_action_space(state)["complete"] is True


def test_combat_expands_each_enemy_target() -> None:
    state = {
        "state_type": "monster",
        "battle": {
            "is_play_phase": True,
            "enemies": [
                {"entity_id": "a_0", "hp": 10},
                {"entity_id": "b_0", "hp": 20},
            ],
        },
        "player": {
            "hand": [
                {"index": 0, "id": "STRIKE", "can_play": True, "target_type": "AnyEnemy"},
                {"index": 1, "id": "DEFEND", "can_play": True, "target_type": "Self"},
            ],
            "potions": [],
        },
    }
    actions = derive_legal_actions(state)
    assert [action.get("target") for action in actions if action["action"] == "play_card"] == [
        "a_0",
        "b_0",
        None,
    ]
    assert actions[-1]["action"] == "end_turn"


def test_limited_view_cannot_receive_privileged_data() -> None:
    state = {"state_type": "game_over", "seed": "SECRET", "privileged_rng": 7}
    secret = {"true_draw_order": ["A", "B"], "future_shops": ["PEN_NIB"]}
    limited = build_information_view(state, InformationMode.LIMITED, privileged=secret)
    omniscient = build_information_view(state, InformationMode.OMNISCIENT, privileged=secret)
    assert limited["privileged"] is None
    assert "seed" not in limited["observation"]
    assert "privileged_rng" not in limited["observation"]
    assert omniscient["privileged"] == secret


def test_unknown_state_is_audited_not_silently_claimed_complete() -> None:
    audit = audit_action_space({"state_type": "overlay"})
    assert audit["complete"] is False
    assert audit["supported"] is False


def test_bundle_preview_exposes_only_its_confirm_and_cancel_controls() -> None:
    state={"state_type":"bundle_select","bundle_select":{
        "bundles":[{"index":0},{"index":1}],"preview_showing":True,
        "can_confirm":True,"can_cancel":True}}
    assert [a["action"] for a in derive_legal_actions(state)]==[
        "confirm_bundle_selection","cancel_bundle_selection"]
    state["bundle_select"].update(preview_showing=False,can_confirm=False,can_cancel=False)
    assert [a["index"] for a in derive_legal_actions(state)]==[0,1]


def test_capped_grid_selection_keeps_deselection_without_ignored_extra_picks() -> None:
    for screen in ["NCombatPileCardSelectScreen", "NSimpleCardSelectScreen", "simple_select"]:
        state={"state_type":"card_select","card_select":{
            "screen_type":screen,"selected_count":2,"max_select":2,"can_confirm":True,
            "cards":[{"index":0,"is_selected":False},{"index":1,"is_selected":True},
                     {"index":2,"is_selected":True}]}}
        actions=derive_legal_actions(state)
        assert [(a['action'],a.get('index')) for a in actions]==[
            ('select_card',1),('select_card',2),('confirm_selection',None)]
        state['card_select'].update(selected_count=1)
        state['card_select']['cards'][1]['is_selected']=False
        assert [a['index'] for a in derive_legal_actions(state) if a['action']=='select_card']==[0,1,2]


def test_empty_enchant_preview_is_not_offered_but_real_optional_grid_confirm_is() -> None:
    state={"state_type":"card_select","card_select":{
        "screen_type":"NDeckEnchantSelectScreen","min_select":0,"max_select":3,
        "selected_count":0,"can_confirm":True,"cards":[{"index":0,"is_selected":False}]}}
    assert [a['action'] for a in derive_legal_actions(state)]==['select_card']
    state['card_select'].update(selected_count=1)
    assert [a['action'] for a in derive_legal_actions(state)]==['select_card','confirm_selection']
    state['card_select'].update(screen_type='NCombatPileCardSelectScreen',selected_count=0)
    assert [a['action'] for a in derive_legal_actions(state)]==['select_card','confirm_selection']


def test_full_potion_belt_can_be_freed_before_claiming_a_reward() -> None:
    state={"state_type":"rewards","player":{"max_potion_slots":1,"potions":[{
        "slot":0,"can_discard_out_of_combat":True,"can_use_out_of_combat":False}]},
        "rewards":{"items":[{"index":0,"type":"potion"},{"index":1,"type":"gold"}],"can_proceed":True}}
    actions=derive_legal_actions(state)
    assert [(a["action"],a.get("index",a.get("slot"))) for a in actions]==[
        ("claim_reward",1),("proceed",None),("discard_potion",0)]
    state["player"]["potions"]=[]
    assert [a["index"] for a in derive_legal_actions(state) if a["action"]=="claim_reward"]==[0,1]
    state.update(state_type="shop",shop={"items":[{
        "index":0,"category":"potion","can_afford":True,"is_stocked":True}]})
    assert any(a["action"]=="shop_purchase" for a in derive_legal_actions(state))
    state["player"]["potions"]=[{"slot":0,"can_discard_out_of_combat":True}]
    assert [a["action"] for a in derive_legal_actions(state)]==["proceed","discard_potion"]
    state["player"]["potions"]=[]
    state["shop"]["items"][0]["can_purchase"]=False
    assert [a["action"] for a in derive_legal_actions(state)]==["proceed"]


def test_automatic_potion_can_be_discarded_without_being_manually_used() -> None:
    state={"state_type":"monster","battle":{"is_play_phase":True},
        "player":{"hand":[],"potions":[{"slot":0,"can_use_in_combat":False,"can_discard":True}]}}
    assert [a["action"] for a in derive_legal_actions(state)]==["discard_potion","end_turn"]
