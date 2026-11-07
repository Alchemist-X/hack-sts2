from pathlib import Path

from sts2rec.canonical import build_canonical
from sts2rec.decision_coverage import audit_decision
from sts2rec.legal_actions import audit_action_space, derive_legal_actions
from sts2rec.validate import validate_session
from tests.conftest import START_TIME, make_action, make_state, write_session


def test_reward_alternatives_preserve_real_indexes_without_inventing_skip():
    state = {"state_type": "card_reward", "card_reward": {
        "cards": [{"id": "A", "index": 0}, {"id": "B", "index": 1}],
        "alternatives": [{"id": "REROLL", "choice_index": 2, "title": "重掷"},
                         {"id": "Sacrifice", "choice_index": 3, "title": "献祭"}],
        "can_skip": False}}
    actions = derive_legal_actions(state)
    assert len(actions) == 4
    assert not any(a["action"] == "skip_card_reward" for a in actions)
    assert actions[-1]["choice_index"] == 3
    assert actions[-1]["candidate"]["title"] == "献祭"
    assert audit_decision(state, {"kind": "player_choice", "params": {
        "choice_type": "Index", "indexes": [3]}})["complete"]


def test_skip_not_duplicated_when_alternatives_are_explicit():
    actions = derive_legal_actions({"state_type": "card_reward", "card_reward": {
        "cards": [{"index": 0}], "alternatives": [{"id": "Skip", "choice_index": 1}], "can_skip": True}})
    assert [a["action"] for a in actions] == ["select_card_reward", "skip_card_reward"]


def test_valid_reference_to_wrong_screen_is_detected(tmp_path: Path):
    records = {"states": [make_state(1, START_TIME, "a", state_type="rewards", rewards={"items": [{"type": "card"}]})],
               "actions": [make_action(2, START_TIME + 1, 1, "player_choice",
                                       params={"choice_type": "Index", "indexes": [0]})], "events": []}
    directory = write_session(tmp_path / "session", records=records)
    step = build_canonical(directory)["steps"][0]
    assert step["info"]["decision_audit"]["issues"] == ["wrong_decision_screen:rewards"]
    assert any("wrong_decision_screen:rewards" in w for w in validate_session(directory).warnings)


def test_explicit_missing_capture_never_uses_future_snapshot(tmp_path: Path):
    records = {"states": [make_state(2, START_TIME + 1, "a", state_type="card_reward",
                                     card_reward={"cards": [{"id": "AFTER", "index": 0}]})],
               "actions": [make_action(1, START_TIME, None, "player_choice", params={
                   "_decision": {"status": "missing", "stage": "unbound_choice"}})], "events": []}
    step = build_canonical(write_session(tmp_path / "session", records=records))["steps"][0]
    assert step["state_before"] is None
    assert step["state_after"]["card_reward"]["cards"][0]["id"] == "AFTER"
    assert "state_before_estimated" not in step["info"]
    assert "pre_input_capture_missing" in step["info"]["decision_audit"]["issues"]


def test_delayed_upgrade_uses_pinned_candidate_state(tmp_path: Path):
    records = {"states": [
        make_state(1, START_TIME, "a", state_type="card_select", card_select={
            "screen_type": "upgrade", "preview_showing": True, "can_confirm": True,
            "cards": [{"id": "GUARDS", "selected": True}, {"id": "GLOW", "selected": False}]}),
        make_state(2, START_TIME + 1, "b", state_type="rest_site", rest_site={"options": []})],
        "actions": [make_action(3, START_TIME + 2, 1, "player_choice", params={
            "choice_type": "DeckCard", "cards": ["CARD.GUARDS"],
            "_decision": {"status": "captured", "stage": "before_input"}})], "events": []}
    step = build_canonical(write_session(tmp_path / "session", records=records))["steps"][0]
    assert step["info"]["decision_audit"]["complete"]
    assert step["state_before"]["card_select"]["cards"][1]["id"] == "GLOW"


def test_empty_event_options_and_absent_route_are_not_complete():
    assert not audit_decision({"state_type": "event", "event": {"options": []}},
                             {"kind": "event_option"})["complete"]
    audit = audit_decision({"state_type": "map", "map": {"next_options": [{"col": 1, "row": 3}]}},
                          {"kind": "vote_for_map_coord", "params": {"destination": {"col": 2, "row": 3}}})
    assert audit["issues"] == ["chosen_destination_not_offered"]


def test_card_multiselect_keeps_already_selected_candidates():
    state = {"state_type": "hand_select", "hand_select": {
        "cards": [{"id": "B"}], "selected_cards": [{"id": "A"}, {"id": "A"}]}}
    action = {"kind": "player_choice", "params": {"cards": ["CARD.A", "CARD.A"]}}
    assert audit_decision(state, action)["complete"]
    action["params"]["cards"].append("CARD.A")
    assert not audit_decision(state, action)["complete"]


def test_section_errors_cannot_be_hidden_by_a_proceed_action():
    state = {"state_type": "shop", "shop": {"section_error": "missing inventory"}}
    assert not audit_action_space(state)["complete"]


def test_opening_snapshot_alone_does_not_claim_pre_input_provenance():
    audit = audit_decision({"state_type": "card_select", "card_select": {"cards": [{"id": "A"}]}},
                          {"kind": "player_choice", "params": {
                              "_decision": {"status": "captured", "stage": "opened"}}})
    assert audit["issues"] == ["pre_input_capture_not_observed"]


def test_duplicate_card_models_are_disambiguated_by_instance():
    state = {"state_type": "card_select", "card_select": {"cards": [
        {"id": "GUARDS", "instance_id": 11}, {"id": "GUARDS", "instance_id": 12}]}}
    action = {"kind": "player_choice", "params": {"card_instances": [{"id": "CARD.GUARDS", "instance_id": 12}]}}
    assert audit_decision(state, action)["complete"]
    action["params"]["card_instances"][0]["instance_id"] = 13
    assert audit_decision(state, action)["issues"] == ["chosen_card_instance_not_in_recorded_candidates"]


def test_event_key_must_be_in_current_page():
    state = {"state_type": "event", "event": {"options": [{"text_key": "PAGE2.LEAVE"}]}}
    assert audit_decision(state, {"kind": "event_option", "params": {"text_key": "PAGE1.PAY"}})["issues"] == [
        "chosen_event_option_not_offered"]


def test_malformed_candidate_payloads_are_reported_without_crashing_validation():
    action = {"kind": "player_choice", "params": {"choice_type": "Index", "indexes": None}}
    state = {"state_type": "card_reward", "card_reward": {"cards": [{"index": 0}], "alternatives": None}}
    assert audit_decision(state, action)["issues"] == ["invalid_choice_indexes"]
    state["card_reward"]["cards"] = None
    assert audit_decision(state, action)["issues"] == ["candidate_set_missing_or_empty"]
    state["state_type"] = []
    assert audit_decision(state, action)["issues"] == ["wrong_decision_screen:None"]
