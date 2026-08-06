"""Portable, offline-only dataset, baseline, and benchmark commands.

Unlike :mod:`sts2rec.text_cli`, this module never imports or launches the game
environment.  Every command operates on JSONL records and small JSON
checkpoints, so it is safe to run while a human is playing STS2.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import sys
import tempfile
from collections import Counter
from dataclasses import asdict
from pathlib import Path
from typing import Any, Iterable, Iterator, Mapping, Sequence, TextIO

from .action_normalization import actions_equivalent
from .baseline import (
    BinaryWinValueModel,
    HashedStateActionFeatures,
    LinearBehaviorCloningPolicy,
    evaluate_behavior_cloning,
    evaluate_win_value,
)
from .benchmark import (
    BenchmarkCase,
    BenchmarkSpec,
    EpisodeOutcome,
    summarize_action_coverage,
    summarize_nosl,
    summarize_sl,
)
from .decision_dataset import (
    DATASET_SCHEMA_VERSION,
    deterministic_group_split,
    find_limited_leakage_paths,
    iter_decision_dataset,
)
from .human_dataset import iter_human_decision_rows
from .legal_actions import audit_action_space, derive_legal_actions


EXIT_OK = 0
EXIT_DATA_ERROR = 1


class TrainCliError(ValueError):
    """A user-facing data or command error."""


def _explicit_outcomes(block: Any) -> set[bool]:
    """Extract only outcome fields present in an engine-facing snapshot."""

    if not isinstance(block, Mapping):
        return set()
    found: set[bool] = set()
    for key in ("outcome", "win", "won", "victory", "is_victory"):
        value = block.get(key)
        if isinstance(value, bool):
            found.add(value)
    result = block.get("result")
    if isinstance(result, str):
        lowered = result.casefold()
        if lowered in {"win", "won", "victory"}:
            found.add(True)
        elif lowered in {"loss", "lost", "defeat", "defeated"}:
            found.add(False)
    for key in ("game_over", "result", "run"):
        nested = block.get(key)
        if isinstance(nested, Mapping):
            found.update(_explicit_outcomes(nested))
    return found


def _print_json(value: Any) -> None:
    print(json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True, allow_nan=False))


def _open_jsonl(path: Path) -> TextIO:
    if path.suffix.lower() == ".gz":
        return gzip.open(path, "rt", encoding="utf-8")
    return path.open("r", encoding="utf-8")


def _iter_jsonl(path: str | Path) -> Iterator[dict[str, Any]]:
    source = Path(path)
    if not source.is_file():
        raise TrainCliError(f"JSONL file not found: {source}")
    try:
        with _open_jsonl(source) as handle:
            for line_number, line in enumerate(handle, start=1):
                if not line.strip():
                    continue
                try:
                    value = json.loads(line)
                except json.JSONDecodeError as error:
                    raise TrainCliError(
                        f"{source}:{line_number}: invalid JSON: {error.msg}"
                    ) from error
                if not isinstance(value, dict):
                    raise TrainCliError(
                        f"{source}:{line_number}: record must be a JSON object"
                    )
                yield value
    except TrainCliError:
        raise
    except (OSError, EOFError, UnicodeError) as error:
        raise TrainCliError(f"cannot read {source}: {error}") from error


class _EpisodeAuditAccumulator:
    """Streaming cross-row checks for normalized decision datasets."""

    def __init__(self) -> None:
        self._episodes: dict[str, dict[str, Any]] = {}
        self._split_identity_groups: dict[str, set[str]] = {}

    def update(self, row: Mapping[str, Any]) -> None:
        episode_id = row.get("episode_id")
        worker_id = row.get("worker_id")
        if (
            isinstance(episode_id, bool)
            or not isinstance(episode_id, (str, int))
            or not str(episode_id)
            or isinstance(worker_id, bool)
            or worker_id is not None
            and not isinstance(worker_id, (str, int))
        ):
            return
        key = json.dumps(
            [worker_id, episode_id],
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
        state = self._episodes.setdefault(
            key,
            {
                "last_sequence": None,
                "non_monotonic": False,
                "terminal_records": 0,
                "seen_terminal": False,
                "record_after_terminal": False,
                "outcomes": set(),
                "group_ids": set(),
                "human": set(),
                "session_complete": set(),
                "terminal_outcomes": set(),
                "engine_terminal_outcomes": set(),
                "truncated": False,
                "terminal_state_mismatch": False,
            },
        )
        metadata = row.get("metadata")
        source = metadata.get("source") if isinstance(metadata, Mapping) else None
        is_human = (
            row.get("source_schema_version") == "human-canonical-v1"
            and source == "sts2-human-canonical"
        )

        sequence = row.get("sequence")
        if type(sequence) is int:
            previous = state["last_sequence"]
            if previous is not None and sequence <= previous:
                state["non_monotonic"] = True
            state["last_sequence"] = sequence
        group_id = row.get("group_id")
        if isinstance(group_id, str) and group_id:
            state["group_ids"].add(group_id)
            split_identity = _split_identity(row)
            if split_identity is not None:
                self._split_identity_groups.setdefault(split_identity, set()).add(
                    group_id
                )
        if state["seen_terminal"]:
            state["record_after_terminal"] = True
        if row.get("terminated") is True:
            state["terminal_records"] += 1
            state["seen_terminal"] = True
            state["engine_terminal_outcomes"].update(
                _explicit_outcomes(row.get("next_observation"))
            )
        next_observation = row.get("next_observation")
        next_state_type = (
            next_observation.get("state_type")
            if isinstance(next_observation, Mapping)
            else None
        )
        terminal_state_mismatch = False
        if row.get("terminated") is True:
            if isinstance(next_observation, Mapping):
                terminal_state_mismatch = next_state_type != "game_over"
            elif not is_human:
                terminal_state_mismatch = True
        elif row.get("terminated") is False and next_state_type == "game_over":
            terminal_state_mismatch = True
        if terminal_state_mismatch:
            state["terminal_state_mismatch"] = True
        if row.get("truncated") is True:
            state["truncated"] = True
        outcome = row.get("outcome")
        if isinstance(outcome, bool):
            state["outcomes"].add(outcome)

        state["human"].add(is_human)
        if isinstance(metadata, Mapping):
            session_complete = metadata.get("session_complete")
            if isinstance(session_complete, bool):
                state["session_complete"].add(session_complete)
            terminal_outcome = metadata.get("terminal_outcome")
            if isinstance(terminal_outcome, bool):
                state["terminal_outcomes"].add(terminal_outcome)

    def issues(self, *, task: str) -> Counter[str]:
        issues: Counter[str] = Counter()
        for groups in self._split_identity_groups.values():
            if len(groups) > 1:
                issues["split_identity_spans_multiple_groups"] += 1
        for state in self._episodes.values():
            if state["non_monotonic"]:
                issues["episode_sequence_non_monotonic"] += 1
            if len(state["outcomes"]) > 1:
                issues["episode_outcome_inconsistent"] += 1
            if len(state["group_ids"]) != 1:
                issues["episode_group_id_inconsistent"] += 1
            if state["record_after_terminal"]:
                issues["episode_terminal_not_last"] += 1
            if state["terminal_state_mismatch"]:
                issues["episode_terminal_state_mismatch"] += 1
            if len(state["human"]) != 1:
                issues["episode_source_mixed"] += 1
                continue

            is_human = next(iter(state["human"]))
            if is_human:
                if task == "value":
                    if state["truncated"]:
                        issues["human_session_truncated"] += 1
                    if state["session_complete"] != {True}:
                        issues["human_session_not_complete"] += 1
                    if (
                        len(state["terminal_outcomes"]) != 1
                        or state["terminal_outcomes"] != state["outcomes"]
                    ):
                        issues["human_terminal_outcome_inconsistent"] += 1
                    engine_outcomes = state["engine_terminal_outcomes"]
                    if engine_outcomes and engine_outcomes != state["outcomes"]:
                        issues["human_engine_outcome_inconsistent"] += 1
                # A strict human adapter may omit the canonical terminal action
                # after filtering an automatic/unmappable step.  Session-level
                # completion metadata is the authoritative value-data proof.
                continue

            if state["terminal_records"] == 0:
                issues["episode_terminal_missing"] += 1
            elif state["terminal_records"] > 1:
                issues["episode_terminal_multiple"] += 1
            if state["truncated"]:
                issues["episode_truncated"] += 1
            if task == "value" and len(state["outcomes"]) != 1:
                issues["episode_outcome_missing"] += 1
            if task == "value":
                engine_outcomes = state["engine_terminal_outcomes"]
                if len(engine_outcomes) != 1:
                    issues["episode_terminal_outcome_unverified"] += 1
                elif engine_outcomes != state["outcomes"]:
                    issues["episode_terminal_outcome_mismatch"] += 1
        return issues


def _split_identity(row: Mapping[str, Any]) -> str | None:
    """Return a stable provenance identity used to audit split grouping."""

    blocks = (row, row.get("metadata"), row.get("info"))
    for key in ("seed_fingerprint", "seed_group", "seed"):
        for block in blocks:
            if isinstance(block, Mapping) and block.get(key) is not None:
                return json.dumps(
                    [key, block[key]],
                    ensure_ascii=False,
                    sort_keys=True,
                    separators=(",", ":"),
                    allow_nan=False,
                )
    for key in ("lineage_id", "run_id"):
        for block in blocks:
            if isinstance(block, Mapping) and block.get(key) is not None:
                return json.dumps(
                    [key, block[key]],
                    ensure_ascii=False,
                    sort_keys=True,
                    separators=(",", ":"),
                    allow_nan=False,
                )
    return None


def _load_audited_rows(path: str | Path, *, task: str) -> list[dict[str, Any]]:
    rows: list[dict[str, Any]] = []
    episodes = _EpisodeAuditAccumulator()
    decision_ids: set[str] = set()
    for row_number, row in enumerate(_iter_jsonl(path), start=1):
        issues = _row_audit_issues(row, task=task)
        if issues:
            raise TrainCliError(
                f"dataset row {row_number} failed the {task} quality gate: "
                + ", ".join(issues)
            )
        decision_id = str(row["decision_id"])
        if decision_id in decision_ids:
            raise TrainCliError(
                f"dataset row {row_number} failed the {task} quality gate: "
                f"duplicate_decision_id:{decision_id}"
            )
        decision_ids.add(decision_id)
        rows.append(row)
        episodes.update(row)
    if not rows:
        raise TrainCliError("dataset is empty")
    episode_issues = episodes.issues(task=task)
    if episode_issues:
        raise TrainCliError(
            f"dataset failed the {task} episode quality gate: "
            + ", ".join(sorted(episode_issues))
        )
    return rows


def _sha256_file(path: str | Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return f"sha256:{digest.hexdigest()}"


def _prepare_target(path: Path, *, force: bool) -> None:
    if path.exists() and not force:
        raise TrainCliError(f"refusing to overwrite {path}; pass --force to replace it")
    path.parent.mkdir(parents=True, exist_ok=True)


def _temporary_target(target: Path) -> Path:
    descriptor, name = tempfile.mkstemp(
        prefix=f".{target.name}.", suffix=".tmp", dir=target.parent
    )
    os.close(descriptor)
    return Path(name)


def _commit_target(temporary: Path, target: Path, *, force: bool) -> None:
    """Atomically publish a file, preserving no-overwrite semantics."""

    if force:
        os.replace(temporary, target)
        return
    try:
        os.link(temporary, target)
    except FileExistsError as error:
        raise TrainCliError(
            f"refusing to overwrite {target}; pass --force to replace it"
        ) from error
    temporary.unlink()


def _open_output(path: Path, *, compressed: bool) -> TextIO:
    if compressed:
        return gzip.open(path, "wt", encoding="utf-8")
    return path.open("w", encoding="utf-8")


def _write_json_line(handle: TextIO, value: Mapping[str, Any]) -> None:
    handle.write(
        json.dumps(
            value,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
        + "\n"
    )


def _cmd_dataset_build(args: argparse.Namespace) -> int:
    target = Path(args.output).resolve()
    sources = [Path(path).resolve() for path in args.inputs]
    if target in sources:
        raise TrainCliError("dataset output must be different from every input file")
    _prepare_target(target, force=args.force)
    temporary = _temporary_target(target)
    counts: Counter[str] = Counter()
    seen_decisions: set[str] = set()
    try:
        with _open_output(temporary, compressed=target.suffix.lower() == ".gz") as handle:
            for source in sources:
                for decision in iter_decision_dataset(
                    source,
                    information_mode=args.information_mode,
                    on_ineligible=args.on_ineligible,
                ):
                    row = decision.as_dict()
                    if decision.decision_id in seen_decisions:
                        raise TrainCliError(
                            f"duplicate decision_id across inputs: {decision.decision_id}"
                        )
                    seen_decisions.add(decision.decision_id)
                    _write_json_line(handle, row)
                    counts["records"] += 1
                    counts[
                        "bc_eligible" if decision.bc_eligible else "bc_ineligible"
                    ] += 1
                    counts[
                        "value_eligible"
                        if decision.value_eligible
                        else "value_ineligible"
                    ] += 1
                    for reason in decision.bc_ineligibility_reasons:
                        counts[f"bc_reason:{reason}"] += 1
                    for reason in decision.value_ineligibility_reasons:
                        counts[f"value_reason:{reason}"] += 1
        _commit_target(temporary, target, force=args.force)
    finally:
        if temporary.exists():
            temporary.unlink()
    _print_json(
        {
            "command": "dataset build",
            "dataset_schema_version": DATASET_SCHEMA_VERSION,
            "inputs": [str(path) for path in sources],
            "output": str(target),
            "output_sha256": _sha256_file(target),
            "records": counts["records"],
            "eligible": counts["bc_eligible"],
            "ineligible": counts["bc_ineligible"],
            "bc_eligible": counts["bc_eligible"],
            "bc_ineligible": counts["bc_ineligible"],
            "value_eligible": counts["value_eligible"],
            "value_ineligible": counts["value_ineligible"],
            "bc_ineligibility_reasons": {
                key.removeprefix("bc_reason:"): value
                for key, value in sorted(counts.items())
                if key.startswith("bc_reason:")
            },
            "value_ineligibility_reasons": {
                key.removeprefix("value_reason:"): value
                for key, value in sorted(counts.items())
                if key.startswith("value_reason:")
            },
        }
    )
    return EXIT_OK


def _cmd_dataset_build_human(args: argparse.Namespace) -> int:
    target = Path(args.output).resolve()
    sources = [Path(path).resolve() for path in args.sessions]
    _prepare_target(target, force=args.force)
    temporary = _temporary_target(target)
    counts: Counter[str] = Counter()
    seen_decisions: set[str] = set()
    try:
        with _open_output(temporary, compressed=target.suffix.lower() == ".gz") as handle:
            for source in sources:
                for row in iter_human_decision_rows(
                    source, on_ineligible=args.on_ineligible
                ):
                    decision_id = str(row["decision_id"])
                    if decision_id in seen_decisions:
                        raise TrainCliError(
                            f"duplicate human decision_id across sessions: {decision_id}"
                        )
                    seen_decisions.add(decision_id)
                    _write_json_line(handle, row)
                    counts["records"] += 1
                    counts["bc_eligible" if row["bc_eligible"] else "bc_ineligible"] += 1
                    counts[
                        "value_eligible" if row["value_eligible"] else "value_ineligible"
                    ] += 1
        _commit_target(temporary, target, force=args.force)
    finally:
        temporary.unlink(missing_ok=True)
    _print_json(
        {
            "command": "dataset build-human",
            "sessions": [str(path) for path in sources],
            "output": str(target),
            "output_sha256": _sha256_file(target),
            **counts,
        }
    )
    return EXIT_OK


def _action_counter(actions: Iterable[Mapping[str, Any]]) -> Counter[str]:
    return Counter(
        json.dumps(
            {
                str(key): value
                for key, value in action.items()
                if str(key) not in {"action_index", "label", "q_value"}
            },
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
        for action in actions
    )


def _row_audit_issues(
    row: Mapping[str, Any], *, task: str = "bc"
) -> list[str]:
    issues: list[str] = []
    if row.get("dataset_schema_version") != DATASET_SCHEMA_VERSION:
        issues.append("unsupported_dataset_schema_version")
    for key in ("decision_id", "group_id"):
        if not isinstance(row.get(key), str) or not row.get(key):
            issues.append(f"missing_{key}")
    if type(row.get("sequence")) is not int:
        issues.append("sequence_not_integer")
    episode_id = row.get("episode_id")
    if (
        isinstance(episode_id, bool)
        or not isinstance(episode_id, (str, int))
        or not str(episode_id)
    ):
        issues.append("episode_id_missing_or_invalid")
    worker_id = row.get("worker_id")
    if isinstance(worker_id, bool) or (
        worker_id is not None and not isinstance(worker_id, (str, int))
    ):
        issues.append("worker_id_invalid")
    if not isinstance(row.get("terminated"), bool):
        issues.append("terminated_not_boolean")
    if not isinstance(row.get("truncated"), bool):
        issues.append("truncated_not_boolean")
    source_schema = row.get("source_schema_version")
    metadata = row.get("metadata")
    source = metadata.get("source") if isinstance(metadata, Mapping) else None
    is_human = (
        source_schema == "human-canonical-v1" and source == "sts2-human-canonical"
    )

    observation = row.get("observation")
    legal_actions = row.get("legal_actions")
    action_mask = row.get("action_mask")
    chosen_index = row.get("chosen_action_index")
    chosen_action = row.get("chosen_action")
    if not isinstance(observation, Mapping):
        issues.append("observation_not_object")
    if not isinstance(legal_actions, list) or any(
        not isinstance(action, Mapping) for action in legal_actions
    ):
        issues.append("legal_actions_malformed")
        legal_actions = []
    if not isinstance(action_mask, list) or len(action_mask) != len(legal_actions):
        issues.append("action_mask_mismatch")
        action_mask = []
    elif any(type(value) is not int or value not in (0, 1) for value in action_mask):
        issues.append("action_mask_not_binary")

    next_legal_actions = row.get("next_legal_actions")
    next_action_mask = row.get("next_action_mask")
    if not isinstance(next_legal_actions, list) or any(
        not isinstance(action, Mapping) for action in next_legal_actions
    ):
        issues.append("next_legal_actions_malformed")
        next_legal_actions = []
    if not isinstance(next_action_mask, list) or len(next_action_mask) != len(
        next_legal_actions
    ):
        issues.append("next_action_mask_mismatch")
    elif any(
        type(value) is not int or value not in (0, 1)
        for value in next_action_mask
    ):
        issues.append("next_action_mask_not_binary")

    if isinstance(chosen_index, bool) or not isinstance(chosen_index, int):
        issues.append("chosen_action_index_missing")
    elif chosen_index < 0 or chosen_index >= len(legal_actions):
        issues.append("chosen_action_index_out_of_range")
    else:
        if len(action_mask) == len(legal_actions) and action_mask[chosen_index] != 1:
            issues.append("chosen_action_masked")
        if not isinstance(chosen_action, Mapping) or not actions_equivalent(
            chosen_action, legal_actions[chosen_index]
        ):
            issues.append("chosen_action_mismatch")
    wire_action = row.get("wire_action")
    wire_proof_required = source_schema in {2, "human-canonical-v1"}
    if wire_proof_required and not isinstance(wire_action, Mapping):
        issues.append("wire_action_missing_or_malformed")
    elif isinstance(wire_action, Mapping) and isinstance(
        chosen_action, Mapping
    ) and not actions_equivalent(
        wire_action, chosen_action
    ):
        issues.append("wire_action_mismatch")

    if isinstance(observation, Mapping):
        derived = derive_legal_actions(dict(observation))
        if _action_counter(derived) != _action_counter(legal_actions):
            issues.append("legal_actions_do_not_match_public_observation")
    next_observation = row.get("next_observation")
    if next_observation is None:
        if next_legal_actions:
            issues.append("next_actions_without_observation")
        if row.get("terminated") is True and not is_human:
            issues.append("terminal_state_mismatch")
    elif not isinstance(next_observation, Mapping):
        issues.append("next_observation_not_object")
    else:
        if (
            row.get("terminated") is True
            and next_observation.get("state_type") != "game_over"
        ) or (
            row.get("terminated") is False
            and next_observation.get("state_type") == "game_over"
        ):
            issues.append("terminal_state_mismatch")
        if _action_counter(
            derive_legal_actions(dict(next_observation))
        ) != _action_counter(next_legal_actions):
            issues.append("next_legal_actions_do_not_match_public_observation")

    generic_eligible = row.get("eligible")
    bc_eligible = row.get("bc_eligible", generic_eligible)
    value_eligible = row.get("value_eligible", generic_eligible)
    eligibility_key = "value_eligible" if task == "value" else "bc_eligible"
    eligible = value_eligible if task == "value" else bc_eligible
    if not isinstance(eligible, bool):
        issues.append("eligible_not_boolean")
    elif not eligible:
        issues.append("record_marked_ineligible")
    if isinstance(generic_eligible, bool) and isinstance(bc_eligible, bool):
        if generic_eligible != bc_eligible:
            issues.append("eligibility_flags_inconsistent")
    if value_eligible is True and bc_eligible is not True:
        issues.append("eligibility_flags_inconsistent")

    leakage_paths = row.get("leakage_paths", [])
    if not isinstance(leakage_paths, list):
        issues.append("leakage_paths_not_list")
        leakage_paths = []

    audit = row.get("action_space_audit")
    if not isinstance(audit, Mapping):
        issues.append("action_space_incomplete")
    else:
        audit_issues = audit.get("issues")
        if audit.get("supported") is not True or audit.get("complete") is not True:
            issues.append("action_space_incomplete")
        if not isinstance(audit_issues, list) or audit_issues:
            issues.append("action_space_audit_issues")
        if type(audit.get("action_count")) is not int or audit.get(
            "action_count"
        ) != len(legal_actions):
            issues.append("action_space_count_mismatch")
        expected_candidates = sum(1 for action in legal_actions if "candidate" in action)
        if type(audit.get("candidate_action_count")) is not int or audit.get(
            "candidate_action_count"
        ) != expected_candidates:
            issues.append("action_space_candidate_count_mismatch")
        if isinstance(observation, Mapping) and audit.get(
            "state_type"
        ) != observation.get("state_type"):
            issues.append("action_space_state_type_mismatch")

    if task == "value" and not isinstance(row.get("outcome"), bool):
        issues.append("outcome_not_boolean")
    if task == "value" and row.get("truncated") is not False:
        issues.append("value_record_truncated")
    mode = row.get("information_mode")
    if mode not in {"limited", "omniscient"}:
        issues.append("unknown_information_mode")
    if mode == "limited":
        recomputed_leakage = find_limited_leakage_paths(row)
        if recomputed_leakage:
            issues.append("limited_information_leakage")
        declared_leakage = tuple(sorted(set(str(path) for path in leakage_paths)))
        if declared_leakage != recomputed_leakage:
            issues.append("leakage_declaration_mismatch")
        if row.get("privileged_observation") is not None:
            issues.append("limited_information_leakage")
        if row.get("next_privileged_observation") is not None:
            issues.append("limited_information_leakage")
    if mode == "omniscient" and not isinstance(
        row.get("privileged_observation"), Mapping
    ):
        issues.append("omniscient_privileged_observation_missing")
    if mode == "omniscient" and not isinstance(
        row.get("next_privileged_observation"), Mapping
    ):
        issues.append("omniscient_next_privileged_observation_missing")

    reason_key = (
        "value_ineligibility_reasons"
        if task == "value"
        else "bc_ineligibility_reasons"
    )
    ineligibility_reasons = row.get(
        reason_key, row.get("ineligibility_reasons")
    )
    if not isinstance(ineligibility_reasons, list):
        issues.append("ineligibility_reasons_not_list")
    elif ineligibility_reasons:
        issues.append("record_has_ineligibility_reasons")
    return list(dict.fromkeys(issues))


def _cmd_dataset_audit(args: argparse.Namespace) -> int:
    issue_counts: Counter[str] = Counter()
    episodes = _EpisodeAuditAccumulator()
    decision_ids: set[str] = set()
    group_ids: set[str] = set()
    records = 0
    for row in _iter_jsonl(args.dataset):
        records += 1
        decision_id = row.get("decision_id")
        if isinstance(decision_id, str):
            if decision_id in decision_ids:
                issue_counts["duplicate_decision_id"] += 1
            decision_ids.add(decision_id)
        group_id = row.get("group_id")
        if isinstance(group_id, str):
            group_ids.add(group_id)
        issue_counts.update(_row_audit_issues(row, task=args.task))
        episodes.update(row)
        reason_key = (
            "value_ineligibility_reasons"
            if args.task == "value"
            else "bc_ineligibility_reasons"
        )
        reasons = row.get(reason_key, row.get("ineligibility_reasons"))
        if isinstance(reasons, list):
            issue_counts.update(
                f"source:{reason}" for reason in reasons if isinstance(reason, str)
            )
    if records == 0:
        issue_counts["empty_dataset"] += 1
    issue_counts.update(episodes.issues(task=args.task))
    report = {
        "command": "dataset audit",
        "dataset": str(Path(args.dataset).resolve()),
        "dataset_sha256": _sha256_file(args.dataset),
        "task": args.task,
        "records": records,
        "unique_decisions": len(decision_ids),
        "groups": len(group_ids),
        "passed": not issue_counts,
        "issues": dict(sorted(issue_counts.items())),
    }
    _print_json(report)
    if issue_counts and not args.allow_issues:
        return EXIT_DATA_ERROR
    return EXIT_OK


def _cmd_dataset_split(args: argparse.Namespace) -> int:
    output_dir = Path(args.output_dir).resolve()
    targets = {
        "train": output_dir / "train.jsonl",
        "val": output_dir / "validation.jsonl",
        "test": output_dir / "test.jsonl",
    }
    # Validate ratios before creating any output files.
    deterministic_group_split(
        "ratio-validation",
        train=args.train_ratio,
        val=args.validation_ratio,
        test=args.test_ratio,
        salt=args.salt,
    )
    for target in targets.values():
        _prepare_target(target, force=args.force)
    temporary = {name: _temporary_target(target) for name, target in targets.items()}
    handles = {name: path.open("w", encoding="utf-8") for name, path in temporary.items()}
    counts: Counter[str] = Counter()
    episodes = _EpisodeAuditAccumulator()
    groups_by_split: dict[str, set[str]] = {name: set() for name in targets}
    try:
        for row in _iter_jsonl(args.dataset):
            issues = _row_audit_issues(row, task=args.task)
            if issues:
                if not args.drop_ineligible:
                    raise TrainCliError(
                        "dataset contains a row that failed audit: " + ", ".join(issues)
                    )
                counts["dropped"] += 1
                continue
            episodes.update(row)
            group_id = str(row["group_id"])
            split = deterministic_group_split(
                group_id,
                train=args.train_ratio,
                val=args.validation_ratio,
                test=args.test_ratio,
                salt=args.salt,
            )
            _write_json_line(handles[split], row)
            counts[split] += 1
            groups_by_split[split].add(group_id)
        episode_issues = episodes.issues(task=args.task)
        if episode_issues:
            raise TrainCliError(
                "dataset failed the episode quality gate: "
                + ", ".join(sorted(episode_issues))
            )
    except Exception:
        for path in temporary.values():
            path.unlink(missing_ok=True)
        raise
    finally:
        for handle in handles.values():
            handle.close()
    if sum(counts[name] for name in targets) == 0:
        for path in temporary.values():
            path.unlink(missing_ok=True)
        raise TrainCliError("split produced no eligible records")
    for name, target in targets.items():
        _commit_target(temporary[name], target, force=args.force)
    _print_json(
        {
            "command": "dataset split",
            "input": str(Path(args.dataset).resolve()),
            "output_dir": str(output_dir),
            "salt": args.salt,
            "task": args.task,
            "records": {name: counts[name] for name in targets},
            "groups": {name: len(groups_by_split[name]) for name in targets},
            "dropped": counts["dropped"],
            "files": {
                name: {"path": str(path), "sha256": _sha256_file(path)}
                for name, path in targets.items()
            },
        }
    )
    return EXIT_OK


def _new_encoder(args: argparse.Namespace) -> HashedStateActionFeatures:
    return HashedStateActionFeatures(dimension=args.dimension)


def _training_metadata(args: argparse.Namespace, metrics: Mapping[str, Any]) -> dict[str, Any]:
    return {
        "dataset_sha256": _sha256_file(args.dataset),
        "training": {
            "epochs": args.epochs,
            "learning_rate": args.learning_rate,
            "l2": args.l2,
            "seed": args.seed,
            "shuffle": not args.no_shuffle,
            "use_privileged_observation": args.use_privileged,
            "metrics": dict(metrics),
        },
    }


def _cmd_train_bc(args: argparse.Namespace) -> int:
    rows = _load_audited_rows(args.dataset, task="bc")
    model = LinearBehaviorCloningPolicy(
        encoder=_new_encoder(args),
        use_privileged_observation=args.use_privileged,
    )
    metrics = asdict(
        model.fit(
            rows,
            epochs=args.epochs,
            learning_rate=args.learning_rate,
            l2=args.l2,
            seed=args.seed,
            shuffle=not args.no_shuffle,
        )
    )
    if metrics["examples"] == 0:
        raise TrainCliError("no eligible behavior-cloning examples")
    target = Path(args.output).resolve()
    _prepare_target(target, force=args.force)
    temporary = _temporary_target(target)
    try:
        model.save(temporary, metadata=_training_metadata(args, metrics))
        _commit_target(temporary, target, force=args.force)
    finally:
        temporary.unlink(missing_ok=True)
    _print_json(
        {
            "command": "train bc",
            "model": str(target),
            "checkpoint_sha256": _sha256_file(target),
            "metrics": metrics,
        }
    )
    return EXIT_OK


def _cmd_train_value(args: argparse.Namespace) -> int:
    rows = _load_audited_rows(args.dataset, task="value")
    model = BinaryWinValueModel(
        encoder=_new_encoder(args),
        use_privileged_observation=args.use_privileged,
    )
    metrics = asdict(
        model.fit(
            rows,
            epochs=args.epochs,
            learning_rate=args.learning_rate,
            l2=args.l2,
            seed=args.seed,
            shuffle=not args.no_shuffle,
        )
    )
    if metrics["examples"] == 0:
        raise TrainCliError("no eligible value-training examples")
    target = Path(args.output).resolve()
    _prepare_target(target, force=args.force)
    temporary = _temporary_target(target)
    try:
        model.save(temporary, metadata=_training_metadata(args, metrics))
        _commit_target(temporary, target, force=args.force)
    finally:
        temporary.unlink(missing_ok=True)
    _print_json(
        {
            "command": "train value",
            "model": str(target),
            "checkpoint_sha256": _sha256_file(target),
            "metrics": metrics,
        }
    )
    return EXIT_OK


def _cmd_evaluate_bc(args: argparse.Namespace) -> int:
    model = LinearBehaviorCloningPolicy.load(args.model)
    metrics = asdict(
        evaluate_behavior_cloning(
            model, _load_audited_rows(args.dataset, task="bc")
        )
    )
    if metrics["examples"] == 0:
        raise TrainCliError("no eligible behavior-cloning evaluation examples")
    _print_json(
        {
            "command": "evaluate bc",
            "dataset_sha256": _sha256_file(args.dataset),
            "checkpoint_sha256": _sha256_file(args.model),
            "metrics": metrics,
        }
    )
    return EXIT_OK


def _cmd_evaluate_value(args: argparse.Namespace) -> int:
    model = BinaryWinValueModel.load(args.model)
    metrics = asdict(
        evaluate_win_value(
            model, _load_audited_rows(args.dataset, task="value")
        )
    )
    if metrics["examples"] == 0:
        raise TrainCliError("no eligible value-model evaluation examples")
    _print_json(
        {
            "command": "evaluate value",
            "dataset_sha256": _sha256_file(args.dataset),
            "checkpoint_sha256": _sha256_file(args.model),
            "metrics": metrics,
        }
    )
    return EXIT_OK


def _load_outcomes(path: str | Path) -> tuple[list[EpisodeOutcome], dict[str, Any]]:
    outcomes: list[EpisodeOutcome] = []
    source = Path(path).resolve()
    fingerprints: set[str] = set()
    missing_fingerprints = 0
    missing_trajectory_paths = 0
    verified_trajectory_files = 0
    for row in _iter_jsonl(path):
        fingerprint = row.get("spec_fingerprint")
        if isinstance(fingerprint, str) and fingerprint:
            fingerprints.add(fingerprint)
        else:
            missing_fingerprints += 1
        trajectory_path = row.get("trajectory_path")
        trajectory_hash = row.get("trajectory_hash")
        if not isinstance(trajectory_path, str) or not trajectory_path:
            missing_trajectory_paths += 1
        else:
            artifact = Path(trajectory_path)
            if not artifact.is_absolute():
                artifact = source.parent / artifact
            if not artifact.is_file():
                raise TrainCliError(f"trajectory artifact not found: {artifact}")
            actual_hash = _sha256_file(artifact)
            if actual_hash != trajectory_hash:
                raise TrainCliError(
                    f"trajectory hash mismatch for {artifact}: "
                    f"declared {trajectory_hash!r}, actual {actual_hash}"
                )
            verified_trajectory_files += 1
        try:
            outcomes.append(
                EpisodeOutcome(
                    episode_id=row["episode_id"],
                    case_id=row["case_id"],
                    seed=row["seed"],
                    ascension=row["ascension"],
                    won=row["won"],
                    terminal_reason=row["terminal_reason"],
                    sequence_index=row["sequence_index"],
                    attempt=row.get("attempt", 1),
                    reload_count=row.get("reload_count"),
                    trajectory_complete=row.get("trajectory_complete"),
                    trajectory_hash=trajectory_hash,
                    steps=row.get("steps"),
                    elapsed_s=row.get("elapsed_s"),
                )
            )
        except KeyError as error:
            raise TrainCliError(f"outcome record is missing {error.args[0]}") from error
        except (TypeError, ValueError) as error:
            raise TrainCliError(f"invalid outcome record: {error}") from error
    if not outcomes:
        raise TrainCliError("outcome JSONL is empty")
    provenance = {
        "spec_fingerprint": next(iter(fingerprints)) if len(fingerprints) == 1 else None,
        "provenance_complete": missing_fingerprints == 0 and len(fingerprints) == 1,
        "missing_spec_fingerprints": missing_fingerprints,
        "distinct_spec_fingerprints": len(fingerprints),
        "trajectory_artifacts_verified": (
            missing_trajectory_paths == 0
            and verified_trajectory_files == len(outcomes)
        ),
        "verified_trajectory_files": verified_trajectory_files,
        "missing_trajectory_paths": missing_trajectory_paths,
    }
    return outcomes, provenance


def _load_benchmark_spec(path: str | Path | None) -> BenchmarkSpec | None:
    if path is None:
        return None
    source = Path(path)
    try:
        payload = json.loads(source.read_text(encoding="utf-8"))
    except json.JSONDecodeError as error:
        raise TrainCliError(f"{source}: invalid benchmark spec JSON: {error.msg}") from error
    if not isinstance(payload, dict):
        raise TrainCliError("benchmark spec root must be a JSON object")
    raw_cases = payload.get("cases")
    if not isinstance(raw_cases, list):
        raise TrainCliError("benchmark spec cases must be a list")
    try:
        cases = tuple(BenchmarkCase(**case) for case in raw_cases)
        values = dict(payload)
        values["cases"] = cases
        for key in ("ascensions", "sl_budgets"):
            if key in values:
                values[key] = tuple(values[key])
        return BenchmarkSpec(**values)
    except (TypeError, ValueError) as error:
        raise TrainCliError(f"invalid benchmark spec: {error}") from error


def _require_provenance(args: argparse.Namespace, provenance: Mapping[str, Any]) -> None:
    if args.require_spec_fingerprint and not provenance["provenance_complete"]:
        raise TrainCliError(
            "formal benchmark aggregation requires one identical spec_fingerprint on every outcome"
        )


def _verify_spec_provenance(
    spec: BenchmarkSpec | None, provenance: Mapping[str, Any]
) -> None:
    if spec is None:
        return
    if not provenance["provenance_complete"]:
        raise TrainCliError(
            "a spec-bound benchmark requires its fingerprint on every outcome"
        )
    if provenance["spec_fingerprint"] != spec.fingerprint():
        raise TrainCliError(
            "outcome spec_fingerprint does not match the supplied benchmark spec"
        )


def _verify_trajectory_artifacts(
    spec: BenchmarkSpec | None, provenance: Mapping[str, Any]
) -> None:
    if spec is None:
        return
    if not provenance["trajectory_artifacts_verified"]:
        raise TrainCliError(
            "a spec-bound benchmark requires a verified trajectory_path/hash "
            "artifact for every outcome"
        )


def _cmd_benchmark_nosl(args: argparse.Namespace) -> int:
    outcomes, provenance = _load_outcomes(args.outcomes)
    spec = _load_benchmark_spec(args.spec)
    _require_provenance(args, provenance)
    _verify_spec_provenance(spec, provenance)
    summary = summarize_nosl(outcomes, spec=spec)
    _verify_trajectory_artifacts(spec, provenance)
    _print_json(
        {
            "command": "benchmark nosl",
            "input_sha256": _sha256_file(args.outcomes),
            **provenance,
            "spec_verified": spec is not None,
            "summary": summary.as_dict(),
        }
    )
    return EXIT_OK


def _cmd_benchmark_sl(args: argparse.Namespace) -> int:
    outcomes, provenance = _load_outcomes(args.outcomes)
    spec = _load_benchmark_spec(args.spec)
    _require_provenance(args, provenance)
    _verify_spec_provenance(spec, provenance)
    summary = summarize_sl(outcomes, budgets=args.budget, spec=spec)
    _verify_trajectory_artifacts(spec, provenance)
    _print_json(
        {
            "command": "benchmark sl",
            "input_sha256": _sha256_file(args.outcomes),
            **provenance,
            "spec_verified": spec is not None,
            "summary": summary.as_dict(),
        }
    )
    return EXIT_OK


def _chosen_action_legal(row: Mapping[str, Any]) -> bool | None:
    legal = row.get("legal_actions")
    mask = row.get("action_mask")
    index = row.get("chosen_action_index")
    chosen = row.get("chosen_action")
    if (
        not isinstance(legal, list)
        or not isinstance(mask, list)
        or len(mask) != len(legal)
        or any(type(value) is not int or value not in (0, 1) for value in mask)
        or isinstance(index, bool)
        or not isinstance(index, int)
        or index < 0
        or index >= len(legal)
        or not isinstance(legal[index], Mapping)
        or not isinstance(chosen, Mapping)
    ):
        return False
    if mask[index] != 1:
        return False
    return actions_equivalent(chosen, legal[index])


def _verified_coverage_audit(row: Mapping[str, Any]) -> dict[str, Any] | None:
    """Recompute coverage from the public snapshot and cross-check the recorder."""

    observation = row.get("observation")
    recorded_audit = row.get("action_space_audit")
    if not isinstance(observation, Mapping) or not isinstance(
        recorded_audit, Mapping
    ):
        return None
    legal = row.get("legal_actions")
    legal_actions = (
        legal
        if isinstance(legal, list)
        and all(isinstance(action, Mapping) for action in legal)
        else []
    )
    derived_actions = derive_legal_actions(dict(observation))
    derived_audit = audit_action_space(dict(observation))
    issues = [str(issue) for issue in derived_audit.get("issues", [])]
    if _action_counter(legal_actions) != _action_counter(derived_actions):
        issues.append("recorded_legal_actions_do_not_match_public_snapshot")

    expected = {
        "state_type": derived_audit.get("state_type"),
        "supported": derived_audit.get("supported"),
        "action_count": len(legal_actions),
        "candidate_action_count": sum(
            1 for action in legal_actions if "candidate" in action
        ),
    }
    for key, value in expected.items():
        if recorded_audit.get(key) != value:
            issues.append(f"recorded_audit_{key}_mismatch")
    raw_issues = recorded_audit.get("issues")
    if not isinstance(raw_issues, list):
        issues.append("recorded_audit_issues_malformed")
    elif raw_issues:
        issues.extend(f"recorded:{issue}" for issue in raw_issues)
    if recorded_audit.get("complete") is not True:
        issues.append("recorded_audit_incomplete")

    return {
        **expected,
        "complete": derived_audit.get("complete") is True and not issues,
        "issues": list(dict.fromkeys(issues)),
    }


def _cmd_benchmark_coverage(args: argparse.Namespace) -> int:
    audits: list[dict[str, Any]] = []
    for row in _iter_jsonl(args.dataset):
        audits.append(
            {
                "action_space_audit": _verified_coverage_audit(row),
                "observation": row.get("observation"),
                "legal_actions": row.get("legal_actions"),
                "chosen_action_legal": _chosen_action_legal(row),
            }
        )
    _print_json(
        {
            "command": "benchmark coverage",
            "dataset_sha256": _sha256_file(args.dataset),
            "summary": summarize_action_coverage(audits).as_dict(),
        }
    )
    return EXIT_OK


def _add_training_options(parser: argparse.ArgumentParser) -> None:
    parser.add_argument("dataset")
    parser.add_argument("-o", "--output", required=True)
    parser.add_argument("--epochs", type=int, default=3)
    parser.add_argument("--learning-rate", type=float, default=0.05)
    parser.add_argument("--l2", type=float, default=0.0)
    parser.add_argument("--dimension", type=int, default=1 << 15)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--no-shuffle", action="store_true")
    parser.add_argument(
        "--use-privileged",
        action="store_true",
        help="train the separate omniscient channel; rejects limited rows",
    )
    parser.add_argument("--force", action="store_true")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="sts2train",
        description="Offline STS2 dataset, baseline-model, and benchmark tools",
    )
    sub = parser.add_subparsers(dest="command", required=True)

    dataset = sub.add_parser("dataset", help="build and validate decision datasets")
    dataset_sub = dataset.add_subparsers(dest="dataset_command", required=True)
    build = dataset_sub.add_parser("build", help="normalize compact transition JSONL")
    build.add_argument("inputs", nargs="+")
    build.add_argument("-o", "--output", required=True)
    build.add_argument(
        "--information-mode", choices=("limited", "omniscient"), default="limited"
    )
    build.add_argument(
        "--on-ineligible", choices=("mark", "skip", "raise"), default="mark"
    )
    build.add_argument("--force", action="store_true")
    build.set_defaults(handler=_cmd_dataset_build)

    build_human = dataset_sub.add_parser(
        "build-human",
        help="strictly align passive recorder sessions for behavior cloning",
    )
    build_human.add_argument("sessions", nargs="+")
    build_human.add_argument("-o", "--output", required=True)
    build_human.add_argument(
        "--on-ineligible", choices=("mark", "skip", "raise"), default="mark"
    )
    build_human.add_argument("--force", action="store_true")
    build_human.set_defaults(handler=_cmd_dataset_build_human)

    audit = dataset_sub.add_parser("audit", help="fail on malformed or ineligible rows")
    audit.add_argument("dataset")
    audit.add_argument("--task", choices=("bc", "value"), default="bc")
    audit.add_argument(
        "--allow-issues", action="store_true", help="report issues without a failing exit code"
    )
    audit.set_defaults(handler=_cmd_dataset_audit)

    split = dataset_sub.add_parser("split", help="make deterministic run-level splits")
    split.add_argument("dataset")
    split.add_argument("--output-dir", required=True)
    split.add_argument("--train-ratio", type=float, default=0.8)
    split.add_argument("--validation-ratio", type=float, default=0.1)
    split.add_argument("--test-ratio", type=float, default=0.1)
    split.add_argument("--salt", default="hack-sts2/decision-split/v1")
    split.add_argument("--task", choices=("bc", "value"), default="bc")
    split.add_argument("--drop-ineligible", action="store_true")
    split.add_argument("--force", action="store_true")
    split.set_defaults(handler=_cmd_dataset_split)

    train = sub.add_parser("train", help="train portable baseline models")
    train_sub = train.add_subparsers(dest="train_command", required=True)
    train_bc = train_sub.add_parser("bc", help="train masked behavior cloning")
    _add_training_options(train_bc)
    train_bc.set_defaults(handler=_cmd_train_bc)
    train_value = train_sub.add_parser("value", help="train terminal win-value model")
    _add_training_options(train_value)
    train_value.set_defaults(handler=_cmd_train_value)

    evaluate = sub.add_parser("evaluate", help="evaluate a frozen baseline checkpoint")
    evaluate_sub = evaluate.add_subparsers(dest="evaluate_command", required=True)
    evaluate_bc = evaluate_sub.add_parser("bc")
    evaluate_bc.add_argument("dataset")
    evaluate_bc.add_argument("--model", required=True)
    evaluate_bc.set_defaults(handler=_cmd_evaluate_bc)
    evaluate_value = evaluate_sub.add_parser("value")
    evaluate_value.add_argument("dataset")
    evaluate_value.add_argument("--model", required=True)
    evaluate_value.set_defaults(handler=_cmd_evaluate_value)

    benchmark = sub.add_parser("benchmark", help="aggregate recorded benchmark artifacts")
    benchmark_sub = benchmark.add_subparsers(dest="benchmark_command", required=True)
    nosl = benchmark_sub.add_parser("nosl", help="NoSL pass rate, Wilson CI, and streak")
    nosl.add_argument("outcomes")
    nosl.add_argument("--spec", help="frozen BenchmarkSpec JSON for strict validation")
    nosl.add_argument("--require-spec-fingerprint", action="store_true")
    nosl.set_defaults(handler=_cmd_benchmark_nosl)
    sl = benchmark_sub.add_parser("sl", help="SL solve rate under fixed attempt budgets")
    sl.add_argument("outcomes")
    sl.add_argument("--spec", help="frozen BenchmarkSpec JSON for strict validation")
    sl.add_argument("--budget", type=int, action="append")
    sl.add_argument("--require-spec-fingerprint", action="store_true")
    sl.set_defaults(handler=_cmd_benchmark_sl)
    coverage = benchmark_sub.add_parser("coverage", help="legal-action coverage report")
    coverage.add_argument("dataset")
    coverage.set_defaults(handler=_cmd_benchmark_coverage)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    try:
        return int(args.handler(args))
    except (TrainCliError, OSError, ValueError, json.JSONDecodeError) as error:
        print(f"error: {error}", file=sys.stderr)
        return EXIT_DATA_ERROR


if __name__ == "__main__":
    raise SystemExit(main())
