from sts2rec.information import InformationMode, build_information_view
from sts2rec.legal_actions import audit_action_space, derive_legal_actions


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
