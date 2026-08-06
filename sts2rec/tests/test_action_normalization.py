from __future__ import annotations

import pytest

from sts2rec.action_normalization import (
    ActionNormalizationError,
    actions_equivalent,
    normalize_action,
    normalize_action_pair,
)


def legal_actions() -> list[dict[str, object]]:
    return [
        {
            "action_index": 0,
            "action": "play_card",
            "card_index": 2,
            "target": "LOUSE_0",
            "candidate": {"id": "STRIKE", "description": "Deal 6 damage."},
            "target_candidate": {"entity_id": "LOUSE_0", "hp": 12},
        },
        {"action_index": 1, "action": "end_turn", "label": "End turn"},
    ]


def test_integer_action_resolves_to_full_structured_legal_action() -> None:
    resolved = normalize_action(0, legal_actions())
    assert resolved.index == 0
    assert resolved.action["candidate"] == {
        "id": "STRIKE",
        "description": "Deal 6 damage.",
    }


def test_wire_dictionary_matches_action_with_evaluator_metadata() -> None:
    resolved = normalize_action(
        {"action": "play_card", "card_index": 2, "target": "LOUSE_0"},
        legal_actions(),
    )
    assert resolved.index == 0
    assert resolved.action["action_index"] == 0


def test_embedded_index_must_agree_with_structured_command() -> None:
    with pytest.raises(ActionNormalizationError, match="disagree"):
        normalize_action(
            {"action_index": 0, "action": "end_turn"},
            legal_actions(),
        )


def test_v2_pair_rejects_conflicting_index_and_action() -> None:
    with pytest.raises(ActionNormalizationError, match="different legal actions"):
        normalize_action_pair(
            chosen_action_index=1,
            chosen_action={"action": "play_card", "card_index": 2, "target": "LOUSE_0"},
            legal_actions=legal_actions(),
        )


def test_v2_pair_respects_embedded_index_for_duplicate_wire_actions() -> None:
    duplicate_wire = [
        {
            "action_index": 0,
            "action": "choose",
            "index": 7,
            "candidate": {"id": "first"},
        },
        {
            "action_index": 1,
            "action": "choose",
            "index": 7,
            "candidate": {"id": "second"},
        },
    ]
    with pytest.raises(ActionNormalizationError, match="different legal actions"):
        normalize_action_pair(
            chosen_action_index=0,
            chosen_action=duplicate_wire[1],
            legal_actions=duplicate_wire,
        )
    resolved = normalize_action_pair(
        chosen_action_index=1,
        chosen_action=duplicate_wire[1],
        legal_actions=duplicate_wire,
    )
    assert resolved.index == 1
    assert resolved.action["candidate"] == {"id": "second"}


def test_non_semantic_candidate_payload_does_not_change_action_identity() -> None:
    assert actions_equivalent(
        {"action": "end_turn", "candidate": {"debug": 1}},
        {"action_index": 9, "action": "end_turn", "q_value": 0.75},
    )


def test_out_of_range_and_ambiguous_actions_are_rejected() -> None:
    with pytest.raises(ActionNormalizationError, match="outside"):
        normalize_action(2, legal_actions())
    with pytest.raises(ActionNormalizationError, match="multiple legal actions"):
        normalize_action(
            {"action": "end_turn"},
            [{"action": "end_turn"}, {"action": "end_turn", "label": "duplicate"}],
        )
