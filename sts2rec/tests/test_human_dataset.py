from __future__ import annotations

from copy import deepcopy
import json
from pathlib import Path
from typing import Any

import pytest

from sts2rec.human_dataset import (
    HumanDatasetError,
    iter_human_decision_rows,
    load_human_decision_rows,
)
from sts2rec.legal_actions import audit_action_space, derive_legal_actions


START_TIME = 1773034386.0


def write_minimal_session(session: Path, state: dict[str, Any]) -> Path:
    session.mkdir(parents=True)
    manifest = {
        "schema_version": 1,
        "recorder_version": "0.1.0",
        "game": {"version": "0.107.1", "build_id": "test"},
        "platform": "macos",
        "profile": "test-profile",
        "run": {
            "seed": "SESSION-SEED",
            "character": "CHARACTER.DEFECT",
            "ascension": 13,
            "game_mode": "standard",
            "start_time": START_TIME,
        },
        "result": {
            "win": False,
            "abandoned": False,
            "end_time": START_TIME + 5,
        },
        "counts": {"states": 2, "actions": 1, "events": 0},
        "incomplete": False,
    }
    states = [
        {
            "seq": sequence,
            "t": START_TIME + sequence,
            "type": "state",
            "trigger": "action",
            "screen": "combat",
            "hash": hash_value,
            "state": state,
        }
        for sequence, hash_value in ((1, "before"), (3, "after"))
    ]
    actions = [
        {
            "seq": 2,
            "t": START_TIME + 2,
            "type": "action",
            "source": "hook:NEndTurnButton",
            "action": {"kind": "end_turn"},
            "status": "executed",
            "state_seq": 1,
        }
    ]
    (session / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
    for name, rows in (("states", states), ("actions", actions), ("events", [])):
        (session / f"{name}.jsonl").write_text(
            "".join(json.dumps(row) + "\n" for row in rows), encoding="utf-8"
        )
    return session


def combat_state() -> dict[str, Any]:
    return {
        "state_type": "monster",
        "seed": "MUST-NOT-BE-A-FEATURE",
        "player": {
            "energy": 2,
            "hand": [
                {
                    "index": 3,
                    "id": "CARD.ZAP",
                    "can_play": True,
                    "target_type": "None",
                }
            ],
            "potions": [],
        },
        "battle": {"is_play_phase": True, "enemies": []},
    }


def map_state(*, duplicate: bool = False) -> dict[str, Any]:
    options = [
        {"index": 0, "col": 1, "row": 6, "room_type": "rest"},
        {"index": 1, "col": 2, "row": 6, "room_type": "elite"},
    ]
    if duplicate:
        options[1] = {"index": 1, "col": 1, "row": 6, "room_type": "elite"}
    return {"state_type": "map", "map": {"next_options": options}}


def event_state() -> dict[str, Any]:
    return {
        "state_type": "event",
        "event": {
            "options": [
                {
                    "index": 0,
                    "id": "FIGHT",
                    "title": "Fight",
                    "is_locked": False,
                },
                {
                    "index": 1,
                    "id": "CONTINUE",
                    "title": "Continue",
                    "is_locked": False,
                    "is_proceed": True,
                },
            ]
        },
    }


def rest_state() -> dict[str, Any]:
    return {
        "state_type": "rest_site",
        "rest_site": {
            "options": [
                {"index": 0, "id": "REST", "is_enabled": True},
                {"index": 1, "id": "SMITH", "is_enabled": True},
            ]
        },
    }


def canonical_step(
    state: dict[str, Any],
    action_type: str,
    params: dict[str, Any] | None = None,
    *,
    status: str | None = "executed",
    terminal: bool = False,
) -> dict[str, Any]:
    legal = derive_legal_actions(state)
    info: dict[str, Any] = {
        "action_seq": 10,
        "state_before_seq": 9,
        "state_after_seq": 11,
        "legal_actions": legal,
        "action_space_audit": audit_action_space(state),
        "events": [],
    }
    if status is not None:
        info["action_status"] = status
    return {
        "step_idx": 0,
        "ts": 123.0,
        "state_before": deepcopy(state),
        "action": {"type": action_type, "params": deepcopy(params or {})},
        "state_after": {"state_type": "game_over"} if terminal else deepcopy(state),
        "reward": None,
        "terminal": terminal,
        "info": info,
    }


def trajectory(
    step: dict[str, Any],
    *,
    seed: str = "SEED-A",
    run_id: str = "run-a",
    incomplete: bool = False,
    result: dict[str, Any] | None = None,
) -> dict[str, Any]:
    if result is None and not incomplete:
        result = {"win": True, "abandoned": False, "end_time": 200.0}
    return {
        "meta": {
            "run_id": run_id,
            "seed": seed,
            "character": "CHARACTER.DEFECT",
            "ascension": 13,
            "game_version": "0.107.1",
            "incomplete": incomplete,
            "result": result,
        },
        "prelude_events": [],
        "steps": [step],
    }


def test_end_turn_is_aligned_and_complete_outcome_is_propagated() -> None:
    row = load_human_decision_rows(
        trajectory(canonical_step(combat_state(), "end_turn", terminal=True))
    )[0]

    expected = next(
        index
        for index, action in enumerate(row["legal_actions"])
        if action["action"] == "end_turn"
    )
    assert row["chosen_action_index"] == expected
    assert row["chosen_action"]["action"] == "end_turn"
    assert row["bc_eligible"] is True
    assert row["value_eligible"] is True
    assert row["value_outcome"] is True
    assert row["outcome"] is True
    assert row["terminated"] is True
    assert "seed" not in row["observation"]
    assert row["privileged_observation"] is None
    assert row["next_privileged_observation"] is None


def test_missing_action_status_is_not_assumed_committed() -> None:
    row = load_human_decision_rows(
        trajectory(canonical_step(combat_state(), "end_turn", status=None))
    )[0]
    assert row["bc_eligible"] is False
    assert "action_status_unconfirmed" in row["bc_ineligibility_reasons"]


def test_play_card_aligns_by_model_and_target_not_only_hand_position() -> None:
    state = {
        "state_type": "monster",
        "player": {
            "energy": 3,
            "hand": [
                {
                    "index": 0,
                    "id": "CARD.SEVERANCE",
                    "can_play": True,
                    "target_type": "AnyEnemy",
                },
                {
                    "index": 1,
                    "id": "CARD.STRIKE_NECROBINDER",
                    "can_play": True,
                    "target_type": "AnyEnemy",
                },
            ],
            "potions": [],
        },
        "battle": {
            "is_play_phase": True,
            "enemies": [
                {"entity_id": "ENEMY_0", "combat_id": 7, "hp": 20},
                {"entity_id": "ENEMY_1", "combat_id": 8, "hp": 20},
            ],
        },
    }
    row = load_human_decision_rows(
        trajectory(
            canonical_step(
                state,
                "play_card",
                {
                    "card_model_id": "CARD.SEVERANCE",
                    "combat_card_index": 0,
                    "target_id": 8,
                },
            )
        )
    )[0]
    assert row["bc_eligible"] is True
    assert row["chosen_action"]["candidate"]["id"] == "CARD.SEVERANCE"
    assert row["chosen_action"]["target_candidate"]["combat_id"] == 8


@pytest.mark.parametrize(
    ("state", "action_type", "params", "expected_action", "expected_index"),
    [
        (
            map_state(),
            "vote_for_map_coord",
            {"col": 2, "row": 6, "human": True},
            "choose_map_node",
            1,
        ),
        (
            event_state(),
            "event_option",
            {"option_id": "FIGHT"},
            "choose_event_option",
            0,
        ),
        (
            event_state(),
            "event_proceed",
            {},
            "choose_event_option",
            1,
        ),
        (
            rest_state(),
            "rest_site_option",
            {"option_id": "smith"},
            "choose_rest_option",
            1,
        ),
        (
            map_state(),
            "choose_map_node",
            {"index": 0},
            "choose_map_node",
            0,
        ),
    ],
)
def test_confirmed_vocabularies_align_only_the_unique_candidate(
    state: dict[str, Any],
    action_type: str,
    params: dict[str, Any],
    expected_action: str,
    expected_index: int,
) -> None:
    row = load_human_decision_rows(
        trajectory(canonical_step(state, action_type, params))
    )[0]

    assert row["bc_eligible"] is True
    assert row["chosen_action_index"] == expected_index
    assert row["chosen_action"]["action"] == expected_action


def test_existing_wire_semantics_require_an_exact_unique_match() -> None:
    row = load_human_decision_rows(
        trajectory(
            canonical_step(combat_state(), "play_card", {"card_index": 3})
        )
    )[0]

    assert row["bc_eligible"] is True
    assert row["chosen_action"]["action"] == "play_card"
    assert row["wire_action"] == {"action": "play_card", "card_index": 3}


def test_recorder_destination_coordinates_align_to_the_public_map_node() -> None:
    row = load_human_decision_rows(
        trajectory(
            canonical_step(
                map_state(),
                "vote_for_map_coord",
                {"destination": {"col": 2, "row": 6}},
            )
        )
    )[0]

    assert row["bc_eligible"] is True
    assert row["chosen_action"]["action"] == "choose_map_node"
    assert row["chosen_action"]["index"] == 1


def test_real_event_history_title_aligns_the_committed_option() -> None:
    row = load_human_decision_rows(
        trajectory(
            canonical_step(
                event_state(),
                "event_option",
                {
                    "player": 1,
                    "title_key": "EVENT.OPTION.FIGHT",
                    "title": "Fight",
                    "variables": {},
                },
            )
        )
    )[0]

    assert row["bc_eligible"] is True
    assert row["chosen_action"]["action"] == "choose_event_option"
    assert row["chosen_action"]["index"] == 0


def test_explicitly_non_human_action_never_enters_human_bc() -> None:
    row = load_human_decision_rows(
        trajectory(
            canonical_step(
                combat_state(),
                "end_turn",
                {"human": False, "can_back_out": False},
            )
        )
    )[0]

    assert row["bc_eligible"] is False
    assert "action_not_human" in row["bc_ineligibility_reasons"]


def test_ambiguous_or_unmapped_action_is_marked_ineligible_and_never_guessed() -> None:
    ambiguous = load_human_decision_rows(
        trajectory(
            canonical_step(
                map_state(duplicate=True),
                "vote_for_map_coord",
                {"col": 1, "row": 6},
            )
        )
    )[0]
    unmapped = load_human_decision_rows(
        trajectory(canonical_step(map_state(), "mystery_click", {}))
    )[0]

    for row in (ambiguous, unmapped):
        assert row["bc_eligible"] is False
        assert row["chosen_action_index"] is None
        assert row["chosen_action"] is None
        assert "action_not_uniquely_aligned" in row["bc_ineligibility_reasons"]


@pytest.mark.parametrize(
    ("mutation", "reason"),
    [
        ("estimated", "state_before_estimated"),
        ("cancelled", "action_cancelled"),
        ("automatic", "action_automatic"),
        ("incomplete_audit", "action_space_incomplete"),
    ],
)
def test_low_integrity_actions_are_explicitly_bc_ineligible(
    mutation: str, reason: str
) -> None:
    step = canonical_step(combat_state(), "end_turn")
    if mutation == "estimated":
        step["info"]["state_before_estimated"] = True
    elif mutation == "cancelled":
        step["info"]["action_status"] = "cancelled"
    elif mutation == "automatic":
        step["action"]["params"]["automatic"] = True
    else:
        step["info"]["action_space_audit"]["complete"] = False
        step["info"]["action_space_audit"]["issues"] = ["partial_snapshot"]

    row = load_human_decision_rows(trajectory(step))[0]
    assert row["bc_eligible"] is False
    assert reason in row["bc_ineligibility_reasons"]


def test_missing_audit_is_not_silently_replaced_with_an_eligible_one() -> None:
    step = canonical_step(combat_state(), "end_turn")
    del step["info"]["action_space_audit"]

    row = load_human_decision_rows(trajectory(step))[0]
    assert row["bc_eligible"] is False
    assert "missing_action_space_audit" in row["bc_ineligibility_reasons"]


def test_incomplete_session_keeps_sound_bc_label_without_inventing_value_target() -> None:
    row = load_human_decision_rows(
        trajectory(
            canonical_step(combat_state(), "end_turn"),
            incomplete=True,
            result=None,
        )
    )[0]

    assert row["eligible"] is True
    assert row["bc_eligible"] is True
    assert row["outcome"] is None
    assert row["value_outcome"] is None
    assert row["value_eligible"] is False
    assert row["value_ineligibility_reasons"] == ["session_incomplete"]


def test_group_is_seed_hash_not_raw_feature_and_same_seed_stays_together() -> None:
    first = load_human_decision_rows(
        trajectory(canonical_step(combat_state(), "end_turn"), run_id="branch-1")
    )[0]
    second = load_human_decision_rows(
        trajectory(canonical_step(combat_state(), "end_turn"), run_id="branch-2")
    )[0]
    other = load_human_decision_rows(
        trajectory(
            canonical_step(combat_state(), "end_turn"),
            seed="SEED-B",
            run_id="branch-3",
        )
    )[0]

    assert first["group_id"] == second["group_id"]
    assert first["group_id"] != other["group_id"]
    assert "SEED-A" not in str(first)
    assert "seed" not in first["observation"]


def test_session_directory_runs_through_build_canonical(tmp_path: Path) -> None:
    session = write_minimal_session(tmp_path / "human", combat_state())

    row = load_human_decision_rows(session)[0]
    assert row["bc_eligible"] is True
    assert row["value_eligible"] is True
    assert row["value_outcome"] is False


def test_skip_and_raise_are_defined_by_bc_eligibility() -> None:
    bad = trajectory(canonical_step(map_state(), "mystery_click", {}))

    assert list(iter_human_decision_rows(bad, on_ineligible="skip")) == []
    with pytest.raises(HumanDatasetError, match="BC-ineligible"):
        list(iter_human_decision_rows(bad, on_ineligible="raise"))
