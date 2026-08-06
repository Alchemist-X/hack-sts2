from __future__ import annotations

import gzip
import json
from copy import deepcopy
from pathlib import Path
from typing import Any

import pytest

from sts2rec.decision_dataset import (
    DecisionDatasetError,
    IneligibleDecisionError,
    deterministic_group_split,
    iter_decision_dataset,
    iter_transition_jsonl,
    load_decision_dataset,
    scan_episode_outcomes,
    split_decisions,
)


def map_state(*, hidden: bool = False) -> dict[str, Any]:
    state: dict[str, Any] = {
        "state_type": "map",
        "run": {"act": 1, "floor": 3},
        "player": {"hp": 60, "max_hp": 70, "gold": 99},
        "map": {"next_options": [{"index": 4, "room_type": "monster"}]},
    }
    if hidden:
        state["rng_state"] = "LEAK"
    return state


def legal_actions() -> list[dict[str, Any]]:
    node = {"index": 4, "room_type": "monster"}
    return [
        {
            "action_index": 0,
            "action": "choose_map_node",
            "index": 4,
            "candidate": node,
        }
    ]


def terminal_state(win: bool) -> dict[str, Any]:
    return {"state_type": "game_over", "game_over": {"win": win}}


def v1_record(
    *,
    sequence: int,
    episode_id: str = "episode-v1",
    action: int | dict[str, Any] = 0,
    terminated: bool = False,
    truncated: bool = False,
    win: bool | None = None,
) -> dict[str, Any]:
    info: dict[str, Any] = {
        "information_mode": "limited",
        "privileged": None,
    }
    if win is not None:
        info["win"] = win
    return {
        "schema_version": 1,
        "sequence": sequence,
        "time": 1000.0 + sequence,
        "worker_id": 1,
        "episode_id": episode_id,
        "observation": map_state(),
        "legal_actions": legal_actions(),
        "action": action,
        "reward": 1.0 if win else 0.0,
        "next_observation": terminal_state(win) if terminated and win is not None else map_state(),
        "terminated": terminated,
        "truncated": truncated,
        "info": info,
    }


def v2_record(
    *,
    sequence: int,
    episode_id: str,
    worker_id: int = 2,
    terminated: bool = True,
    truncated: bool = False,
    win: bool | None = True,
    action_index: int = 0,
    audit_complete: bool = True,
    hidden: bool = False,
    metadata: dict[str, Any] | None = None,
) -> dict[str, Any]:
    selected = legal_actions()[0]
    info: dict[str, Any] = {"privileged": None}
    if win is not None:
        info["win"] = win
    return {
        "schema_version": 2,
        "sequence": sequence,
        "time": 2000.0 + sequence,
        "worker_id": worker_id,
        "episode_id": episode_id,
        "observation": map_state(hidden=hidden),
        "legal_actions": legal_actions(),
        "action_mask": [1],
        "chosen_action_index": action_index,
        "action": deepcopy(selected),
        "wire_action": {"action": "choose_map_node", "index": 4},
        "reward": 1.0 if win else 0.0,
        "next_observation": terminal_state(bool(win)) if terminated else map_state(),
        "next_legal_actions": [] if terminated else legal_actions(),
        "next_action_mask": [] if terminated else [1],
        "terminated": terminated,
        "truncated": truncated,
        "information_mode": "limited",
        "action_space_audit": {
            "state_type": "map",
            "supported": True,
            "complete": audit_complete,
            "action_count": 1,
            "candidate_action_count": 1,
            "issues": [] if audit_complete else ["candidate_snapshot_incomplete"],
        },
        "info": info,
        "metadata": metadata or {},
    }


def write_jsonl(path: Path, records: list[dict[str, Any]]) -> Path:
    payload = "".join(
        json.dumps(record, ensure_ascii=False, separators=(",", ":")) + "\n"
        for record in records
    )
    if path.suffix == ".gz":
        with gzip.open(path, "wt", encoding="utf-8") as handle:
            handle.write(payload)
    else:
        path.write_text(payload, encoding="utf-8")
    return path


def test_v1_two_pass_propagates_terminal_outcome_and_normalizes_actions(
    tmp_path: Path,
) -> None:
    records = [
        v1_record(sequence=1, action=0),
        v1_record(
            sequence=2,
            action={"action": "choose_map_node", "index": 4},
            terminated=True,
            win=True,
        ),
    ]
    path = write_jsonl(tmp_path / "v1.jsonl", records)
    decisions = load_decision_dataset(path)

    assert [decision.outcome for decision in decisions] == [True, True]
    assert [decision.chosen_action_index for decision in decisions] == [0, 0]
    assert decisions[0].chosen_action == legal_actions()[0]
    assert all(decision.eligible for decision in decisions)
    assert decisions[0].action_mask == [1]  # defaulted for schema v1


def test_v2_contract_preserves_resolved_fields_and_group_identity(tmp_path: Path) -> None:
    metadata = {"seed_group": "SEED-X", "lineage_id": "lineage-7"}
    records = [
        v2_record(
            sequence=1,
            episode_id="v2",
            terminated=False,
            win=None,
            metadata=metadata,
        ),
        v2_record(sequence=2, episode_id="v2", metadata=metadata),
    ]
    path = write_jsonl(tmp_path / "v2.jsonl", records)
    decisions = list(iter_decision_dataset(path))

    assert len(decisions) == 2
    assert all(decision.source_schema_version == 2 for decision in decisions)
    assert all(decision.outcome is True for decision in decisions)
    assert decisions[0].wire_action == {"action": "choose_map_node", "index": 4}
    assert decisions[0].group_id == decisions[1].group_id
    assert decisions[0].metadata["seed_group"] == "SEED-X"


def test_plain_and_gzip_streams_produce_identical_decision_ids(tmp_path: Path) -> None:
    records = [v2_record(sequence=1, episode_id="compressed")]
    plain = write_jsonl(tmp_path / "transitions.jsonl", records)
    compressed = write_jsonl(tmp_path / "transitions.jsonl.gz", records)

    assert list(iter_transition_jsonl(plain)) == list(iter_transition_jsonl(compressed))
    plain_decisions = load_decision_dataset(plain)
    gzip_decisions = load_decision_dataset(compressed)
    assert plain_decisions[0].decision_id == gzip_decisions[0].decision_id
    assert plain_decisions[0].as_dict() == gzip_decisions[0].as_dict()


def test_quality_contract_marks_each_required_ineligibility_reason(
    tmp_path: Path,
) -> None:
    records = [
        v2_record(
            sequence=1,
            episode_id="missing",
            terminated=False,
            win=None,
        ),
        v2_record(
            sequence=2,
            episode_id="truncated",
            terminated=False,
            truncated=True,
            win=None,
        ),
        v2_record(
            sequence=3,
            episode_id="audit",
            audit_complete=False,
        ),
        v2_record(
            sequence=4,
            episode_id="illegal",
            action_index=9,
        ),
        v2_record(
            sequence=5,
            episode_id="leak",
            hidden=True,
        ),
    ]
    path = write_jsonl(tmp_path / "quality.jsonl", records)
    by_episode = {
        decision.episode_id: decision for decision in load_decision_dataset(path)
    }

    assert "missing_terminal_outcome" in by_episode[
        "missing"
    ].value_ineligibility_reasons
    assert by_episode["missing"].bc_eligible is True
    assert by_episode["missing"].value_eligible is False
    assert "episode_truncated" in by_episode["truncated"].ineligibility_reasons
    assert "action_space_incomplete" in by_episode["audit"].ineligibility_reasons
    assert "action_not_legal" in by_episode["illegal"].ineligibility_reasons
    assert "limited_information_leakage" in by_episode["leak"].ineligibility_reasons
    assert by_episode["leak"].leakage_paths == ("observation.rng_state",)
    assert by_episode["missing"].eligible is True
    assert not any(
        decision.eligible
        for episode_id, decision in by_episode.items()
        if episode_id != "missing"
    )


def test_skip_and_raise_ineligible_modes(tmp_path: Path) -> None:
    path = write_jsonl(
        tmp_path / "reject.jsonl",
        [v2_record(sequence=1, episode_id="bad", audit_complete=False)],
    )
    assert list(iter_decision_dataset(path, on_ineligible="skip")) == []
    with pytest.raises(IneligibleDecisionError, match="action_space_incomplete"):
        list(iter_decision_dataset(path, on_ineligible="raise"))


def test_outcomes_are_worker_qualified_and_conflicts_are_rejected(tmp_path: Path) -> None:
    records = [
        v2_record(sequence=1, episode_id="same", worker_id=1, win=True),
        v2_record(sequence=2, episode_id="same", worker_id=2, win=False),
        v2_record(sequence=3, episode_id="conflict", worker_id=3, win=True),
        v2_record(sequence=4, episode_id="conflict", worker_id=3, win=False),
    ]
    path = write_jsonl(tmp_path / "workers.jsonl", records)
    decisions = load_decision_dataset(path)
    assert [decision.outcome for decision in decisions[:2]] == [True, False]
    assert decisions[2].outcome is None
    assert "conflicting_terminal_outcomes" in decisions[2].ineligibility_reasons
    summaries = scan_episode_outcomes(path)
    assert len(summaries) == 3


def test_generic_metadata_cannot_supply_or_conflict_with_terminal_outcome(
    tmp_path: Path,
) -> None:
    unknown = v2_record(
        sequence=1,
        episode_id="metadata-is-not-a-label",
        win=None,
        metadata={"result": "win"},
    )
    unknown["next_observation"] = {
        "state_type": "game_over",
        "game_over": {"message": "Run ended."},
    }
    # Old text writers inferred this False value from the message-only state.
    # It is not valid evidence without the explicit provenance marker.
    unknown["info"]["win"] = False
    unknown["outcome"] = True
    explicit = v2_record(
        sequence=2,
        episode_id="explicit-engine-label-wins",
        win=True,
        metadata={"result": "defeat"},
    )
    decisions = load_decision_dataset(
        write_jsonl(tmp_path / "metadata-outcomes.jsonl", [unknown, explicit])
    )

    assert decisions[0].outcome is None
    assert decisions[0].bc_eligible is True
    assert decisions[0].value_eligible is False
    assert "missing_terminal_outcome" in decisions[0].value_ineligibility_reasons
    assert decisions[1].outcome is True
    assert "conflicting_terminal_outcomes" not in decisions[1].ineligibility_reasons
    assert decisions[1].eligible is True


def test_terminal_flag_and_next_state_must_agree_for_the_entire_episode(
    tmp_path: Path,
) -> None:
    first = v2_record(
        sequence=1,
        episode_id="terminal-map",
        terminated=False,
        win=None,
    )
    terminal = v2_record(sequence=2, episode_id="terminal-map", win=True)
    terminal["next_observation"] = map_state()
    terminal["next_legal_actions"] = legal_actions()
    terminal["next_action_mask"] = [1]
    path = write_jsonl(tmp_path / "terminal-map.jsonl", [first, terminal])

    decisions = load_decision_dataset(path)
    assert all(
        "terminal_state_mismatch" in decision.ineligibility_reasons
        for decision in decisions
    )
    summary = next(iter(scan_episode_outcomes(path).values()))
    assert summary.terminal_state_consistent is False
    assert summary.complete is False


def test_split_is_deterministic_and_never_separates_a_group(tmp_path: Path) -> None:
    metadata = {"seed_group": "ONE-SEED", "lineage_id": "ONE-LINEAGE"}
    path = write_jsonl(
        tmp_path / "split.jsonl",
        [
            v2_record(
                sequence=1,
                episode_id="split",
                terminated=False,
                win=None,
                metadata=metadata,
            ),
            v2_record(sequence=2, episode_id="split", metadata=metadata),
        ],
    )
    decisions = load_decision_dataset(path)
    first = deterministic_group_split(decisions[0].group_id, salt="fixed")
    second = deterministic_group_split(decisions[0].group_id, salt="fixed")
    assert first == second
    partitions = split_decisions(decisions, salt="fixed")
    populated = [name for name, rows in partitions.items() if rows]
    assert populated == [first]
    assert partitions[first] == decisions
    assert (
        deterministic_group_split("anything", train=0, val=0, test=1)
        == "test"
    )


def test_seed_group_ignores_lineage_but_no_seed_prefers_lineage(tmp_path: Path) -> None:
    records = [
        v2_record(
            sequence=1,
            episode_id="seed-a",
            metadata={"seed": "SAME", "lineage_id": "branch-a"},
        ),
        v2_record(
            sequence=2,
            episode_id="seed-b",
            metadata={"seed": "SAME", "lineage_id": "branch-b"},
        ),
        v2_record(
            sequence=3,
            episode_id="lineage-a",
            metadata={"lineage_id": "shared"},
        ),
        v2_record(
            sequence=4,
            episode_id="lineage-b",
            metadata={"lineage_id": "shared"},
        ),
    ]
    decisions = load_decision_dataset(write_jsonl(tmp_path / "groups.jsonl", records))
    assert decisions[0].group_id == decisions[1].group_id
    assert decisions[2].group_id == decisions[3].group_id


def test_episode_sequence_and_single_terminal_are_quality_gates(tmp_path: Path) -> None:
    records = [
        v2_record(sequence=2, episode_id="backwards", terminated=False, win=None),
        v2_record(sequence=1, episode_id="backwards"),
        v2_record(sequence=3, episode_id="double-terminal"),
        v2_record(sequence=4, episode_id="double-terminal"),
    ]
    decisions = load_decision_dataset(write_jsonl(tmp_path / "sequence.jsonl", records))
    by_episode = {decision.episode_id: decision for decision in decisions}
    assert "episode_sequence_non_monotonic" in by_episode["backwards"].ineligibility_reasons
    assert "multiple_terminal_records" in by_episode["double-terminal"].ineligibility_reasons
    summaries = scan_episode_outcomes(tmp_path / "sequence.jsonl")
    assert not all(summary.complete for summary in summaries.values())


def test_terminal_transition_must_be_the_last_record_in_episode(tmp_path: Path) -> None:
    records = [
        v2_record(sequence=1, episode_id="after-terminal", win=True),
        v2_record(
            sequence=2,
            episode_id="after-terminal",
            terminated=False,
            win=None,
        ),
    ]
    decisions = load_decision_dataset(
        write_jsonl(tmp_path / "after-terminal.jsonl", records)
    )

    assert all("terminal_not_last" in row.ineligibility_reasons for row in decisions)
    summary = next(iter(scan_episode_outcomes(tmp_path / "after-terminal.jsonl").values()))
    assert summary.terminal_is_last is False
    assert summary.complete is False


def test_one_episode_cannot_change_its_split_identity(tmp_path: Path) -> None:
    records = [
        v2_record(
            sequence=1,
            episode_id="group-drift",
            terminated=False,
            win=None,
            metadata={"seed_group": "SEED-A"},
        ),
        v2_record(
            sequence=2,
            episode_id="group-drift",
            metadata={"seed_group": "SEED-B"},
        ),
    ]
    decisions = load_decision_dataset(
        write_jsonl(tmp_path / "group-drift.jsonl", records)
    )

    assert all(
        "episode_group_identity_inconsistent" in row.ineligibility_reasons
        for row in decisions
    )
    summary = next(iter(scan_episode_outcomes(tmp_path / "group-drift.jsonl").values()))
    assert summary.group_consistent is False


def test_current_and_next_public_candidate_payloads_are_reconciled(
    tmp_path: Path,
) -> None:
    v1 = v1_record(sequence=1, terminated=True, win=True)
    v1["legal_actions"][0]["candidate"]["room_type"] = "boss"

    v2 = v2_record(
        sequence=1,
        episode_id="bad-next",
        terminated=False,
        win=None,
    )
    v2["next_legal_actions"][0]["candidate"]["room_type"] = "elite"

    first = load_decision_dataset(write_jsonl(tmp_path / "bad-v1.jsonl", [v1]))[0]
    second = load_decision_dataset(write_jsonl(tmp_path / "bad-v2.jsonl", [v2]))[0]
    assert "action_space_incomplete" in first.ineligibility_reasons
    assert (
        "next_legal_actions_do_not_match_public_snapshot"
        in second.ineligibility_reasons
    )


@pytest.mark.parametrize(
    ("field", "value", "reason"),
    [
        ("action_mask", [2], "action_mask_invalid"),
        ("action_mask", [1, 0], "action_mask_mismatch"),
        ("next_action_mask", [1], "next_action_mask_mismatch"),
        ("next_action_mask", [True], "next_action_mask_invalid"),
    ],
)
def test_masks_require_binary_values_and_matching_lengths(
    tmp_path: Path, field: str, value: object, reason: str
) -> None:
    record = v2_record(sequence=1, episode_id=f"mask-{field}-{reason}")
    record[field] = value
    decision = load_decision_dataset(
        write_jsonl(tmp_path / f"{field}-{reason}.jsonl", [record])
    )[0]
    assert reason in decision.ineligibility_reasons
    assert not decision.eligible


@pytest.mark.parametrize(
    "audit_update",
    [
        {"issues": ["contradiction"]},
        {"issues": "not-a-list"},
        {"supported": "yes"},
        {"complete": 1},
        {"action_count": True},
        {"candidate_action_count": 2},
        {"candidate_action_count": None},
    ],
)
def test_v2_complete_audit_must_be_internally_consistent(
    tmp_path: Path, audit_update: dict[str, Any]
) -> None:
    record = v2_record(sequence=1, episode_id="bad-audit")
    record["action_space_audit"].update(audit_update)
    decision = load_decision_dataset(
        write_jsonl(tmp_path / "bad-audit.jsonl", [record])
    )[0]
    assert "action_space_incomplete" in decision.ineligibility_reasons
    assert decision.action_space_audit["complete"] is False


def test_information_mode_missing_and_hidden_metadata_fail_closed(tmp_path: Path) -> None:
    missing = v2_record(sequence=1, episode_id="missing-mode")
    missing.pop("information_mode")
    hidden = v2_record(
        sequence=2,
        episode_id="metadata-leak",
        metadata={"seed": "GROUPING-ONLY", "rng_state": "LEAK"},
    )
    decisions = load_decision_dataset(
        write_jsonl(tmp_path / "mode-and-metadata.jsonl", [missing, hidden])
    )
    assert "information_mode_missing" in decisions[0].ineligibility_reasons
    assert decisions[1].leakage_paths == ("metadata.rng_state",)
    assert "metadata.seed" not in decisions[1].leakage_paths


def test_privileged_observations_follow_information_mode_contract(tmp_path: Path) -> None:
    limited = v2_record(sequence=1, episode_id="limited-privileged")
    limited["privileged_observation"] = {"engine_seed": "LEAK"}
    limited["next_privileged_observation"] = None

    omniscient = v2_record(sequence=2, episode_id="omniscient-valid")
    omniscient["information_mode"] = "omniscient"
    omniscient["privileged_observation"] = {"engine_seed": "VISIBLE"}
    omniscient["next_privileged_observation"] = {"rng_state": "VISIBLE"}

    omniscient_missing = v2_record(sequence=3, episode_id="omniscient-missing")
    omniscient_missing["information_mode"] = "omniscient"

    path = write_jsonl(
        tmp_path / "privileged.jsonl",
        [limited, omniscient, omniscient_missing],
    )
    decisions = load_decision_dataset(path, information_mode=None)
    assert "limited_privileged_observation_present" in decisions[0].ineligibility_reasons
    assert decisions[0].privileged_observation is None
    assert decisions[1].eligible
    assert decisions[1].privileged_observation == {"engine_seed": "VISIBLE"}
    assert decisions[1].next_privileged_observation == {"rng_state": "VISIBLE"}
    assert "omniscient_privileged_observation_missing" in decisions[2].ineligibility_reasons
    assert "omniscient_next_privileged_observation_missing" in decisions[2].ineligibility_reasons


def test_malformed_or_unknown_schema_is_a_data_error(tmp_path: Path) -> None:
    malformed = tmp_path / "malformed.jsonl"
    malformed.write_text("not-json\n", encoding="utf-8")
    with pytest.raises(DecisionDatasetError, match="invalid JSON"):
        list(iter_transition_jsonl(malformed))

    unknown = write_jsonl(
        tmp_path / "unknown.jsonl",
        [{"schema_version": 3, "worker_id": 1, "episode_id": "x"}],
    )
    with pytest.raises(DecisionDatasetError, match="expected 1 or 2"):
        list(iter_transition_jsonl(unknown))
