from __future__ import annotations

import hashlib
import json
from pathlib import Path
from typing import Any

from sts2rec.benchmark import BenchmarkCase, BenchmarkSpec
from sts2rec.train_cli import main


def _write_jsonl(path: Path, records: list[dict[str, Any]]) -> Path:
    path.write_text(
        "".join(json.dumps(record, separators=(",", ":")) + "\n" for record in records),
        encoding="utf-8",
    )
    return path


def _transition(*, episode: str, won: bool, choice: int = 0) -> dict[str, Any]:
    nodes = [{"index": 1}, {"index": 2}]
    legal = [
        {
            "action_index": 0,
            "action": "choose_map_node",
            "index": 1,
            "candidate": nodes[0],
        },
        {
            "action_index": 1,
            "action": "choose_map_node",
            "index": 2,
            "candidate": nodes[1],
        },
    ]
    return {
        "schema_version": 2,
        "sequence": 1,
        "worker_id": 1,
        "episode_id": episode,
        "information_mode": "limited",
        "observation": {
            "state_type": "map",
            "run": {"act": 1, "floor": 2},
            "player": {"hp": 50 if choice == 0 else 20, "max_hp": 70},
            "map": {"next_options": nodes},
        },
        "legal_actions": legal,
        "action_mask": [1, 1],
        "chosen_action_index": choice,
        "action": legal[choice],
        "wire_action": {"action": "choose_map_node", "index": choice + 1},
        "reward": 1.0 if won else 0.0,
        "next_observation": {
            "state_type": "game_over",
            "game_over": {"win": won},
        },
        "next_legal_actions": [],
        "next_action_mask": [],
        "terminated": True,
        "truncated": False,
        "action_space_audit": {
            "state_type": "map",
            "supported": True,
            "complete": True,
            "action_count": 2,
            "candidate_action_count": 2,
            "issues": [],
        },
        "info": {"win": won, "privileged": None},
        "metadata": {"seed_group": episode},
    }


def _decision(index: int) -> dict[str, Any]:
    choice = index % 2
    won = choice == 0
    nodes = [{"index": 1}, {"index": 2}]
    legal = [
        {
            "action_index": 0,
            "action": "choose_map_node",
            "index": 1,
            "candidate": nodes[0],
        },
        {
            "action_index": 1,
            "action": "choose_map_node",
            "index": 2,
            "candidate": nodes[1],
        },
    ]
    return {
        "dataset_schema_version": 1,
        "decision_id": f"d-{index}",
        "group_id": f"g-{index // 2}",
        "source_schema_version": 2,
        "sequence": 0,
        "worker_id": 0,
        "episode_id": f"episode-{index}",
        "observation": {
            "state_type": "map",
            "signal": choice,
            "map": {"next_options": nodes},
        },
        "legal_actions": legal,
        "action_mask": [1, 1],
        "chosen_action_index": choice,
        "chosen_action": legal[choice],
        "wire_action": legal[choice],
        "next_observation": {"state_type": "game_over"},
        "next_legal_actions": [],
        "next_action_mask": [],
        "privileged_observation": None,
        "next_privileged_observation": None,
        "terminated": True,
        "truncated": False,
        "outcome": won,
        "information_mode": "limited",
        "eligible": True,
        "bc_eligible": True,
        "value_eligible": True,
        "ineligibility_reasons": [],
        "leakage_paths": [],
        "action_space_audit": {
            "state_type": "map",
            "supported": True,
            "complete": True,
            "action_count": 2,
            "candidate_action_count": 2,
            "issues": [],
        },
    }


def test_dataset_build_audit_and_group_split(tmp_path: Path, capsys) -> None:
    transitions = _write_jsonl(
        tmp_path / "transitions.jsonl",
        [
            _transition(episode="episode-win", won=True, choice=0),
            _transition(episode="episode-loss", won=False, choice=1),
        ],
    )
    dataset = tmp_path / "dataset.jsonl"

    assert main(["dataset", "build", str(transitions), "-o", str(dataset)]) == 0
    rows = [json.loads(line) for line in dataset.read_text().splitlines()]
    assert [row["outcome"] for row in rows] == [True, False]
    assert all(row["dataset_schema_version"] == 1 for row in rows)
    assert main(["dataset", "audit", str(dataset)]) == 0

    split_dir = tmp_path / "splits"
    assert main(
        [
            "dataset",
            "split",
            str(dataset),
            "--output-dir",
            str(split_dir),
            "--train-ratio",
            "1",
            "--validation-ratio",
            "0",
            "--test-ratio",
            "0",
        ]
    ) == 0
    train_rows = [json.loads(line) for line in (split_dir / "train.jsonl").read_text().splitlines()]
    assert {row["group_id"] for row in train_rows} == {row["group_id"] for row in rows}
    assert (split_dir / "validation.jsonl").read_text() == ""
    assert capsys.readouterr().err == ""


def test_dataset_build_human_uses_strict_offline_adapter(
    tmp_path: Path, capsys, monkeypatch
) -> None:
    human_row = {
        **_decision(0),
        "bc_eligible": True,
        "value_eligible": False,
        "outcome": None,
        "next_observation": None,
        "next_legal_actions": [],
        "next_action_mask": [],
        "privileged_observation": None,
        "next_privileged_observation": None,
    }
    monkeypatch.setattr(
        "sts2rec.train_cli.iter_human_decision_rows",
        lambda source, on_ineligible: iter([human_row]),
    )
    output = tmp_path / "human.jsonl"
    assert main(
        ["dataset", "build-human", str(tmp_path / "session"), "-o", str(output)]
    ) == 0
    assert json.loads(output.read_text(encoding="utf-8"))["bc_eligible"] is True
    report = json.loads(capsys.readouterr().out)
    assert report["bc_eligible"] == 1
    assert report["value_ineligible"] == 1
    assert main(["dataset", "audit", str(output), "--task", "bc"]) == 0
    capsys.readouterr()
    assert main(["dataset", "audit", str(output), "--task", "value"]) == 1


def test_dataset_audit_is_a_strict_quality_gate(tmp_path: Path, capsys) -> None:
    broken = _decision(1)
    broken["eligible"] = False
    broken["ineligibility_reasons"] = ["limited_information_leakage"]
    broken["leakage_paths"] = ["observation.rng_state"]
    broken["observation"]["rng_state"] = "secret"
    path = _write_jsonl(tmp_path / "broken.jsonl", [broken, broken])

    assert main(["dataset", "audit", str(path)]) == 1
    report = json.loads(capsys.readouterr().out)
    assert report["passed"] is False
    assert report["issues"]["duplicate_decision_id"] == 1
    assert report["issues"]["limited_information_leakage"] == 2


def test_bc_and_value_train_evaluate_round_trip(tmp_path: Path, capsys) -> None:
    dataset = _write_jsonl(
        tmp_path / "decisions.jsonl", [_decision(index) for index in range(20)]
    )
    bc = tmp_path / "bc.json"
    value = tmp_path / "value.json"

    assert main(
        ["train", "bc", str(dataset), "-o", str(bc), "--epochs", "8", "--dimension", "256"]
    ) == 0
    bc_train = json.loads(capsys.readouterr().out)
    assert main(["evaluate", "bc", str(dataset), "--model", str(bc)]) == 0
    bc_eval = json.loads(capsys.readouterr().out)
    assert main(
        [
            "train",
            "value",
            str(dataset),
            "-o",
            str(value),
            "--epochs",
            "8",
            "--dimension",
            "256",
        ]
    ) == 0
    value_train = json.loads(capsys.readouterr().out)
    assert main(["evaluate", "value", str(dataset), "--model", str(value)]) == 0
    value_eval = json.loads(capsys.readouterr().out)
    assert bc.is_file() and value.is_file()
    assert bc_train["metrics"]["examples"] == 20
    assert bc_eval["metrics"]["examples"] == 20
    assert value_train["metrics"]["examples"] == 20
    assert value_eval["metrics"]["examples"] == 20


def test_train_refuses_to_overwrite_checkpoint(tmp_path: Path, capsys) -> None:
    dataset = _write_jsonl(tmp_path / "decisions.jsonl", [_decision(0)])
    model = tmp_path / "model.json"
    model.write_text("keep", encoding="utf-8")

    assert main(["train", "bc", str(dataset), "-o", str(model)]) == 1
    assert model.read_text(encoding="utf-8") == "keep"
    assert "refusing to overwrite" in capsys.readouterr().err


def test_train_cannot_bypass_dataset_quality_gate(tmp_path: Path, capsys) -> None:
    contaminated = {
        **_decision(0),
        "eligible": "false",
        "bc_eligible": "false",
        "leakage_paths": ["observation.rng_state"],
    }
    dataset = _write_jsonl(tmp_path / "contaminated.jsonl", [contaminated])
    model = tmp_path / "must-not-exist.json"

    assert main(["train", "bc", str(dataset), "-o", str(model)]) == 1
    assert not model.exists()
    error = capsys.readouterr().err
    assert "quality gate" in error
    assert "eligible_not_boolean" in error


def test_train_rejects_duplicate_decision_ids_even_without_separate_audit(
    tmp_path: Path, capsys
) -> None:
    first = _decision(0)
    duplicate = _decision(0)
    duplicate["episode_id"] = "other-episode"
    duplicate["group_id"] = "other-group"
    dataset = _write_jsonl(tmp_path / "duplicates.jsonl", [first, duplicate])

    assert main(["train", "bc", str(dataset), "-o", str(tmp_path / "m.json")]) == 1
    assert "duplicate_decision_id" in capsys.readouterr().err


def test_quality_gate_recomputes_leakage_and_rejects_wire_mismatch(
    tmp_path: Path, capsys
) -> None:
    contaminated = _decision(0)
    contaminated["observation"]["futureCardReward"] = ["oracle-label"]
    contaminated["leakage_paths"] = []
    contaminated["wire_action"] = {"action": "choose_map_node", "index": 999}
    dataset = _write_jsonl(tmp_path / "contaminated.jsonl", [contaminated])

    assert main(["dataset", "audit", str(dataset)]) == 1
    report = json.loads(capsys.readouterr().out)
    assert report["issues"]["limited_information_leakage"] == 1
    assert report["issues"]["leakage_declaration_mismatch"] == 1
    assert report["issues"]["wire_action_mismatch"] == 1


def test_quality_gate_checks_cross_row_episode_invariants(
    tmp_path: Path, capsys
) -> None:
    later = _decision(0)
    earlier = _decision(1)
    for row in (later, earlier):
        row["episode_id"] = "same-episode"
        row["worker_id"] = 7
        row["outcome"] = True
    later["sequence"] = 2
    earlier["sequence"] = 1
    earlier["group_id"] = "different-group"
    path = _write_jsonl(tmp_path / "bad-episode.jsonl", [later, earlier])

    assert main(["dataset", "audit", str(path), "--task", "value"]) == 1
    report = json.loads(capsys.readouterr().out)
    assert report["issues"]["episode_sequence_non_monotonic"] == 1
    assert report["issues"]["episode_terminal_multiple"] == 1
    assert report["issues"]["episode_group_id_inconsistent"] == 1


def test_same_seed_cannot_claim_multiple_split_groups(tmp_path: Path, capsys) -> None:
    first = _decision(0)
    second = _decision(1)
    first["metadata"] = {"seed": "IDENTICAL-SEED"}
    second["metadata"] = {"seed": "IDENTICAL-SEED"}
    first["group_id"] = "group-a"
    second["group_id"] = "group-b"
    path = _write_jsonl(tmp_path / "seed-leak.jsonl", [first, second])

    assert main(["dataset", "audit", str(path), "--task", "value"]) == 1
    report = json.loads(capsys.readouterr().out)
    assert report["issues"]["split_identity_spans_multiple_groups"] == 1


def test_truncated_human_session_cannot_train_terminal_value(
    tmp_path: Path, capsys
) -> None:
    row = _decision(0)
    row["source_schema_version"] = "human-canonical-v1"
    row["terminated"] = False
    row["truncated"] = True
    row["metadata"] = {
        "source": "sts2-human-canonical",
        "run_id": "human-run",
        "session_complete": True,
        "terminal_outcome": row["outcome"],
        "seed_fingerprint": "seed-fingerprint",
    }
    path = _write_jsonl(tmp_path / "truncated-human.jsonl", [row])

    assert main(["dataset", "audit", str(path), "--task", "value"]) == 1
    report = json.loads(capsys.readouterr().out)
    assert report["issues"]["value_record_truncated"] == 1
    assert report["issues"]["human_session_truncated"] == 1


def test_coverage_treats_a_masked_choice_as_illegal(tmp_path: Path, capsys) -> None:
    row = _decision(0)
    row["action_mask"] = [0, 1]
    dataset = _write_jsonl(tmp_path / "masked.jsonl", [row])

    assert main(["benchmark", "coverage", str(dataset)]) == 0
    report = json.loads(capsys.readouterr().out)
    overall = report["summary"]["overall"]
    assert overall["chosen_action_checks"] == 1
    assert overall["chosen_actions_covered"] == 0


def test_coverage_cannot_be_self_certified_without_a_public_observation(
    tmp_path: Path, capsys
) -> None:
    forged = {
        "action_space_audit": {
            "state_type": "combat",
            "supported": True,
            "complete": True,
            "action_count": 1,
            "candidate_action_count": 1,
            "issues": [],
        },
        "legal_actions": [{"action": "invented", "candidate": {}}],
        "action_mask": [1],
        "chosen_action_index": 0,
        "chosen_action": {"action": "invented", "candidate": {}},
    }
    dataset = _write_jsonl(tmp_path / "forged-coverage.jsonl", [forged])

    assert main(["benchmark", "coverage", str(dataset)]) == 0
    report = json.loads(capsys.readouterr().out)
    overall = report["summary"]["overall"]
    assert overall["audited_snapshots"] == 0
    assert overall["coverage_rate"] == 0.0


def test_nosl_and_sl_aggregation_keep_provenance_explicit(tmp_path: Path, capsys) -> None:
    fingerprint = "sha256:benchmark"
    nosl = _write_jsonl(
        tmp_path / "nosl.jsonl",
        [
            {
                "episode_id": "e2",
                "case_id": "c2",
                "seed": "s2",
                "ascension": 2,
                "won": False,
                "terminal_reason": "death",
                "sequence_index": 1,
                "spec_fingerprint": fingerprint,
            },
            {
                "episode_id": "e1",
                "case_id": "c1",
                "seed": "s1",
                "ascension": 1,
                "won": True,
                "terminal_reason": "victory",
                "sequence_index": 0,
                "spec_fingerprint": fingerprint,
            },
        ],
    )
    assert main(
        ["benchmark", "nosl", str(nosl), "--require-spec-fingerprint"]
    ) == 0
    nosl_report = json.loads(capsys.readouterr().out)
    assert nosl_report["provenance_complete"] is True
    assert nosl_report["summary"]["overall"]["pass_rate"] == 0.5

    sl = _write_jsonl(
        tmp_path / "sl.jsonl",
        [
            {
                "episode_id": "a1",
                "case_id": "a",
                "seed": "seed-a",
                "ascension": 1,
                "won": False,
                "terminal_reason": "death",
                "sequence_index": 0,
                "attempt": 1,
                "spec_fingerprint": fingerprint,
            },
            {
                "episode_id": "a2",
                "case_id": "a",
                "seed": "seed-a",
                "ascension": 1,
                "won": True,
                "terminal_reason": "victory",
                "sequence_index": 0,
                "attempt": 2,
                "spec_fingerprint": fingerprint,
            },
        ],
    )
    assert main(["benchmark", "sl", str(sl), "--budget", "2"]) == 0
    sl_report = json.loads(capsys.readouterr().out)
    assert sl_report["summary"]["overall"]["2"]["solve_rate"] == 1.0


def test_benchmark_without_fingerprint_cannot_pass_strict_mode(
    tmp_path: Path, capsys
) -> None:
    path = _write_jsonl(
        tmp_path / "outcomes.jsonl",
        [
            {
                "episode_id": "e",
                "case_id": "c",
                "seed": "s",
                "ascension": 1,
                "won": False,
                "terminal_reason": "timeout",
                "sequence_index": 0,
            }
        ],
    )
    assert main(
        ["benchmark", "nosl", str(path), "--require-spec-fingerprint"]
    ) == 1
    assert "requires one identical spec_fingerprint" in capsys.readouterr().err


def test_spec_bound_benchmark_rejects_a_missing_declared_case(
    tmp_path: Path, capsys
) -> None:
    spec = BenchmarkSpec(
        name="fixed-nosl",
        game_version="0.107.1",
        game_build="build-1",
        character="NECROBINDER",
        ascensions=(1,),
        policy_id="sha256:policy",
        seed_suite_id="sha256:seeds",
        cases=(
            BenchmarkCase("case-1", "seed-1", 1, 0),
            BenchmarkCase("case-2", "seed-2", 1, 1),
        ),
        environment_id="sha256:environment",
        victory_condition="declared final boss defeated",
        timeout_s=60.0,
        max_steps=100_000,
        mod_set_id="sha256:mods",
        unlock_state_id="sha256:unlocks",
        controller_version="sha256:controller",
    )
    spec_path = tmp_path / "spec.json"
    spec_path.write_text(json.dumps(spec.as_dict()), encoding="utf-8")
    outcomes = _write_jsonl(
        tmp_path / "partial.jsonl",
        [
            {
                "episode_id": "e1",
                "case_id": "case-1",
                "seed": "seed-1",
                "ascension": 1,
                "won": True,
                "terminal_reason": "victory",
                "sequence_index": 0,
                "spec_fingerprint": spec.fingerprint(),
            }
        ],
    )

    assert main(
        ["benchmark", "nosl", str(outcomes), "--spec", str(spec_path)]
    ) == 1
    assert "missing cases" in capsys.readouterr().err


def test_spec_bound_nosl_requires_and_loads_trajectory_proof(
    tmp_path: Path, capsys
) -> None:
    spec = BenchmarkSpec(
        name="proof-nosl",
        game_version="0.107.1",
        game_build="build-1",
        character="NECROBINDER",
        ascensions=(13,),
        policy_id="sha256:policy",
        seed_suite_id="sha256:seeds",
        cases=(BenchmarkCase("case-1", "seed-1", 13, 0),),
        environment_id="sha256:environment",
        victory_condition="declared final boss defeated",
        timeout_s=60.0,
        max_steps=100_000,
        mod_set_id="sha256:mods",
        unlock_state_id="sha256:unlocks",
        controller_version="sha256:controller",
    )
    spec_path = tmp_path / "spec.json"
    spec_path.write_text(json.dumps(spec.as_dict()), encoding="utf-8")
    trajectory = tmp_path / "e1.trajectory.jsonl"
    trajectory.write_text('{"step":1}\n', encoding="utf-8")
    trajectory_hash = "sha256:" + hashlib.sha256(trajectory.read_bytes()).hexdigest()
    base = {
        "episode_id": "e1",
        "case_id": "case-1",
        "seed": "seed-1",
        "ascension": 13,
        "won": True,
        "terminal_reason": "victory",
        "sequence_index": 0,
        "spec_fingerprint": spec.fingerprint(),
        "trajectory_path": trajectory.name,
        "trajectory_hash": trajectory_hash,
        "steps": 1,
        "elapsed_s": 1.0,
    }
    outcomes = _write_jsonl(tmp_path / "outcomes.jsonl", [base])

    assert main(["benchmark", "nosl", str(outcomes), "--spec", str(spec_path)]) == 1
    assert "reload_count=0" in capsys.readouterr().err

    _write_jsonl(
        outcomes,
        [
            {
                **base,
                "reload_count": 0,
                "trajectory_complete": True,
            }
        ],
    )
    assert main(["benchmark", "nosl", str(outcomes), "--spec", str(spec_path)]) == 0
    report = json.loads(capsys.readouterr().out)
    assert report["spec_verified"] is True
