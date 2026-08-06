"""Leakage-audited decision rows from compact transition JSONL.

The loader is intentionally dependency-free and path based.  It reads a file
twice: pass one collects terminal outcomes per episode; pass two streams each
decision with that outcome attached.  Only one small episode-summary object is
kept in memory, so both ``.jsonl`` and ``.jsonl.gz`` inputs can be much larger
than RAM.

Supported source schemas:

``v1``
    The original :class:`JsonlTrajectoryWriter` shape where ``action`` is an
    integer or dictionary and timestep metadata lives in ``info``.

``v2``
    The resolved-action shape with ``chosen_action_index``, structured
    ``action``, ``wire_action``, next legal actions/masks, explicit information
    mode, source metadata, and a top-level current-state action-space audit.
"""

from __future__ import annotations

import gzip
import hashlib
import json
import math
from collections import Counter
from copy import deepcopy
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Iterable, Iterator, Literal, Mapping, Sequence

from .action_normalization import (
    ActionNormalizationError,
    NormalizedAction,
    normalize_action,
    normalize_action_pair,
    semantic_action,
)
from .legal_actions import audit_action_space, derive_legal_actions


SUPPORTED_TRANSITION_SCHEMAS = frozenset({1, 2})
DATASET_SCHEMA_VERSION = 1
DEFAULT_SPLIT_SALT = "hack-sts2/decision-split/v1"

_HIDDEN_KEYS = frozenset(
    {
        "seed",
        "engine_seed",
        "random_seed",
        "rng_seed",
        "rng",
        "rng_state",
        "rng_position",
        "rng_stream",
        "true_draw_order",
        "draw_order",
        "future_rooms",
        "future_map",
        "future_shops",
        "future_rewards",
        "future_events",
        "hidden_state",
        "privileged",
    }
)
_HIDDEN_KEY_FRAGMENTS = (
    "future",
    "hidden",
    "privileged",
    "rng",
    "random_seed",
    "draw_order",
)


class DecisionDatasetError(ValueError):
    """A transition file violates the dataset input contract."""


class IneligibleDecisionError(DecisionDatasetError):
    """Raised by ``on_ineligible='raise'`` with the rejected decision."""

    def __init__(self, decision_id: str, reasons: Sequence[str]) -> None:
        self.decision_id = decision_id
        self.reasons = tuple(reasons)
        super().__init__(
            f"decision {decision_id} is ineligible: {', '.join(self.reasons)}"
        )


@dataclass(frozen=True)
class EpisodeKey:
    """Worker-qualified episode identity.

    Worker qualification avoids accidental outcome propagation when callers
    reuse human-friendly episode IDs across concurrent workers.
    """

    worker_id: int | str | None
    episode_id: int | str

    def canonical(self) -> str:
        return _canonical_json([self.worker_id, self.episode_id])


@dataclass(frozen=True)
class EpisodeOutcome:
    """Pass-one terminal summary for one episode."""

    key: EpisodeKey
    transition_count: int
    terminal_records: int
    outcome: bool | None
    truncated: bool
    conflicting_outcomes: bool
    terminal_without_outcome: bool
    sequence_complete: bool
    sequence_monotonic: bool
    terminal_is_last: bool
    group_consistent: bool
    terminal_state_consistent: bool

    @property
    def complete(self) -> bool:
        return (
            self.terminal_records == 1
            and self.outcome is not None
            and not self.truncated
            and not self.conflicting_outcomes
            and not self.terminal_without_outcome
            and self.sequence_complete
            and self.sequence_monotonic
            and self.terminal_is_last
            and self.group_consistent
            and self.terminal_state_consistent
        )


@dataclass(frozen=True)
class DecisionRecord:
    """Canonical training/evaluation decision contract."""

    decision_id: str
    group_id: str
    source_schema_version: int
    sequence: int | None
    time: float | int | None
    worker_id: int | str | None
    episode_id: int | str
    observation: dict[str, Any]
    privileged_observation: dict[str, Any] | None
    legal_actions: list[dict[str, Any]]
    action_mask: list[int]
    chosen_action_index: int | None
    chosen_action: dict[str, Any] | None
    wire_action: dict[str, Any] | None
    reward: float | int | None
    next_observation: dict[str, Any] | None
    next_privileged_observation: dict[str, Any] | None
    next_legal_actions: list[dict[str, Any]]
    next_action_mask: list[int]
    terminated: bool
    truncated: bool
    outcome: bool | None
    information_mode: str
    action_space_audit: dict[str, Any]
    metadata: dict[str, Any]
    eligible: bool
    bc_eligible: bool
    value_eligible: bool
    ineligibility_reasons: tuple[str, ...]
    bc_ineligibility_reasons: tuple[str, ...]
    value_ineligibility_reasons: tuple[str, ...]
    leakage_paths: tuple[str, ...]

    @property
    def win(self) -> bool | None:
        """Alias matching evaluation terminology."""

        return self.outcome

    def as_dict(self) -> dict[str, Any]:
        """Return a detached JSON-serializable row."""

        return {
            "dataset_schema_version": DATASET_SCHEMA_VERSION,
            "decision_id": self.decision_id,
            "group_id": self.group_id,
            "source_schema_version": self.source_schema_version,
            "sequence": self.sequence,
            "time": self.time,
            "worker_id": self.worker_id,
            "episode_id": self.episode_id,
            "observation": deepcopy(self.observation),
            "privileged_observation": deepcopy(self.privileged_observation),
            "legal_actions": deepcopy(self.legal_actions),
            "action_mask": list(self.action_mask),
            "chosen_action_index": self.chosen_action_index,
            "chosen_action": deepcopy(self.chosen_action),
            "wire_action": deepcopy(self.wire_action),
            "reward": self.reward,
            "next_observation": deepcopy(self.next_observation),
            "next_privileged_observation": deepcopy(
                self.next_privileged_observation
            ),
            "next_legal_actions": deepcopy(self.next_legal_actions),
            "next_action_mask": list(self.next_action_mask),
            "terminated": self.terminated,
            "truncated": self.truncated,
            "outcome": self.outcome,
            "information_mode": self.information_mode,
            "action_space_audit": deepcopy(self.action_space_audit),
            "metadata": deepcopy(self.metadata),
            "eligible": self.eligible,
            "bc_eligible": self.bc_eligible,
            "value_eligible": self.value_eligible,
            "ineligibility_reasons": list(self.ineligibility_reasons),
            "bc_ineligibility_reasons": list(self.bc_ineligibility_reasons),
            "value_ineligibility_reasons": list(
                self.value_ineligibility_reasons
            ),
            "leakage_paths": list(self.leakage_paths),
        }


@dataclass
class _MutableEpisodeOutcome:
    transition_count: int = 0
    terminal_records: int = 0
    outcomes: set[bool] | None = None
    truncated: bool = False
    terminal_without_outcome: bool = False
    sequence_complete: bool = True
    sequence_monotonic: bool = True
    terminal_is_last: bool = True
    last_sequence: int | None = None
    group_ids: set[str] | None = None
    terminal_state_consistent: bool = True

    def __post_init__(self) -> None:
        if self.outcomes is None:
            self.outcomes = set()
        if self.group_ids is None:
            self.group_ids = set()


def _canonical_json(value: Any) -> str:
    try:
        return json.dumps(
            value,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
    except (TypeError, ValueError) as error:
        raise DecisionDatasetError(
            f"value is not deterministically JSON-serializable: {error}"
        ) from error


def _open_text(path: Path):
    if path.suffix.lower() == ".gz":
        return gzip.open(path, "rt", encoding="utf-8")
    return path.open("r", encoding="utf-8")


def _iter_located_transitions(
    path: str | Path,
) -> Iterator[tuple[int, dict[str, Any]]]:
    source = Path(path)
    if not source.is_file():
        raise DecisionDatasetError(f"transition JSONL not found: {source}")
    try:
        with _open_text(source) as handle:
            for line_number, line in enumerate(handle, start=1):
                if not line.strip():
                    continue
                try:
                    raw = json.loads(line)
                except json.JSONDecodeError as error:
                    raise DecisionDatasetError(
                        f"{source}:{line_number}: invalid JSON: {error.msg}"
                    ) from error
                if not isinstance(raw, dict):
                    raise DecisionDatasetError(
                        f"{source}:{line_number}: transition must be a JSON object"
                    )
                schema = raw.get("schema_version")
                if isinstance(schema, bool) or schema not in SUPPORTED_TRANSITION_SCHEMAS:
                    raise DecisionDatasetError(
                        f"{source}:{line_number}: unsupported transition "
                        f"schema_version {schema!r}; expected 1 or 2"
                    )
                _episode_key(raw, where=f"{source}:{line_number}")
                yield line_number, raw
    except DecisionDatasetError:
        raise
    except (OSError, EOFError, UnicodeError) as error:
        raise DecisionDatasetError(f"cannot read {source}: {error}") from error


def iter_transition_jsonl(path: str | Path) -> Iterator[dict[str, Any]]:
    """Stream validated v1/v2 transition objects from plain or gzip JSONL."""

    for _line_number, record in _iter_located_transitions(path):
        yield record


def _episode_key(record: Mapping[str, Any], *, where: str) -> EpisodeKey:
    episode_id = record.get("episode_id")
    worker_id = record.get("worker_id")
    scalar_types = (int, str)
    if isinstance(episode_id, bool) or not isinstance(episode_id, scalar_types):
        raise DecisionDatasetError(
            f"{where}: episode_id must be a string or integer"
        )
    if isinstance(worker_id, bool) or (
        worker_id is not None and not isinstance(worker_id, scalar_types)
    ):
        raise DecisionDatasetError(
            f"{where}: worker_id must be null, a string, or an integer"
        )
    return EpisodeKey(worker_id=worker_id, episode_id=episode_id)


def _boolean_field(record: Mapping[str, Any], key: str, *, where: str) -> bool:
    value = record.get(key, False)
    if not isinstance(value, bool):
        raise DecisionDatasetError(f"{where}: {key} must be boolean")
    return value


def _outcome_from_block(block: Any) -> set[bool]:
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
    return found


def _terminal_outcomes(record: Mapping[str, Any]) -> set[bool]:
    """Collect explicit terminal labels without guessing from shaped reward."""

    # Caller-provided top-level labels and generic experiment metadata are
    # deliberately excluded.  They are provenance, not authoritative engine
    # outcomes.
    # Legacy text writers also materialized unknown terminal states as
    # ``info.win=false``.  Trust that field only when the producer explicitly
    # proves that the terminal outcome was observed; an outcome embedded in the
    # next engine snapshot remains independently authoritative below.
    blocks: list[Any] = []
    info = record.get("info")
    if isinstance(info, Mapping) and info.get("terminal_outcome_known") is True:
        blocks.append(info)
    next_observation = record.get("next_observation")
    blocks.append(next_observation)
    if isinstance(next_observation, Mapping):
        blocks.extend(
            next_observation.get(key) for key in ("game_over", "result", "run")
        )
    found: set[bool] = set()
    for block in blocks:
        found.update(_outcome_from_block(block))
    return found


def scan_episode_outcomes(
    path: str | Path,
) -> dict[EpisodeKey, EpisodeOutcome]:
    """First pass: collect one terminal outcome summary per episode."""

    mutable: dict[EpisodeKey, _MutableEpisodeOutcome] = {}
    source = Path(path)
    for line_number, record in _iter_located_transitions(source):
        where = f"{source}:{line_number}"
        key = _episode_key(record, where=where)
        summary = mutable.setdefault(key, _MutableEpisodeOutcome())
        assert summary.group_ids is not None
        summary.group_ids.add(decision_group_id(record, key))
        if summary.terminal_records:
            summary.terminal_is_last = False
        summary.transition_count += 1
        sequence = record.get("sequence")
        if isinstance(sequence, bool) or not isinstance(sequence, int):
            summary.sequence_complete = False
        else:
            if summary.last_sequence is not None and sequence <= summary.last_sequence:
                summary.sequence_monotonic = False
            summary.last_sequence = sequence
        truncated = _boolean_field(record, "truncated", where=where)
        terminated = _boolean_field(record, "terminated", where=where)
        next_observation = record.get("next_observation")
        next_state_type = (
            next_observation.get("state_type")
            if isinstance(next_observation, Mapping)
            else None
        )
        if (terminated and next_state_type != "game_over") or (
            not terminated and next_state_type == "game_over"
        ):
            summary.terminal_state_consistent = False
        summary.truncated = summary.truncated or truncated
        if terminated:
            summary.terminal_records += 1
            outcomes = _terminal_outcomes(record)
            if not outcomes:
                summary.terminal_without_outcome = True
            assert summary.outcomes is not None
            summary.outcomes.update(outcomes)

    result: dict[EpisodeKey, EpisodeOutcome] = {}
    for key, summary in mutable.items():
        outcomes = summary.outcomes or set()
        result[key] = EpisodeOutcome(
            key=key,
            transition_count=summary.transition_count,
            terminal_records=summary.terminal_records,
            outcome=next(iter(outcomes)) if len(outcomes) == 1 else None,
            truncated=summary.truncated,
            conflicting_outcomes=len(outcomes) > 1,
            terminal_without_outcome=summary.terminal_without_outcome,
            sequence_complete=summary.sequence_complete,
            sequence_monotonic=summary.sequence_monotonic,
            terminal_is_last=summary.terminal_is_last,
            group_consistent=len(summary.group_ids or ()) == 1,
            terminal_state_consistent=summary.terminal_state_consistent,
        )
    return result


def _dict_or_empty(value: Any) -> dict[str, Any]:
    return deepcopy(dict(value)) if isinstance(value, Mapping) else {}


def _dict_or_none(value: Any) -> dict[str, Any] | None:
    return deepcopy(dict(value)) if isinstance(value, Mapping) else None


def _action_list(value: Any) -> list[dict[str, Any]]:
    if not isinstance(value, list):
        return []
    return [deepcopy(dict(item)) for item in value if isinstance(item, Mapping)]


def _mask(
    value: Any,
    size: int,
    *,
    allow_missing: bool,
) -> tuple[list[int], bool, bool]:
    """Return a safe mask plus independent length/value validity flags.

    Schema v1 did not write masks, so its missing value means all recorded
    actions were enabled.  Schema v2 is explicit: a missing mask is invalid,
    including the otherwise ambiguous empty-mask/empty-action case.
    """

    if value is None:
        if allow_missing:
            return [1] * size, True, True
        return [], size == 0, False
    if not isinstance(value, list):
        return [], False, False
    length_valid = len(value) == size
    values_valid = all(type(item) is int and item in (0, 1) for item in value)
    # Invalid source rows are never eligible.  Still expose a well-formed,
    # fail-closed mask to downstream diagnostics instead of leaking bool/2/etc.
    safe_mask = [item if type(item) is int and item in (0, 1) else 0 for item in value]
    return safe_mask, length_valid, values_valid


def _action_key(action: Mapping[str, Any]) -> str:
    return _canonical_json(semantic_action(action))


def _public_action_key(action: Mapping[str, Any]) -> str:
    """Canonical public candidate payload, excluding evaluator annotations."""

    return _canonical_json(
        {
            str(key): deepcopy(value)
            for key, value in action.items()
            if str(key) not in {"action_index", "label", "q_value"}
        }
    )


def _derive_v1_audit(
    observation: dict[str, Any], legal_actions: list[dict[str, Any]]
) -> dict[str, Any]:
    audit = deepcopy(audit_action_space(observation))
    derived = derive_legal_actions(observation)
    recorded_keys = Counter(_public_action_key(item) for item in legal_actions)
    derived_keys = Counter(_public_action_key(item) for item in derived)
    if recorded_keys != derived_keys:
        issues = list(audit.get("issues", []))
        issues.append("recorded_legal_actions_do_not_match_public_snapshot")
        audit["issues"] = issues
        audit["complete"] = False
    audit["recorded_action_count"] = len(legal_actions)
    return audit


def _action_space_audit(
    record: Mapping[str, Any],
    *,
    schema_version: int,
    observation: dict[str, Any],
    legal_actions: list[dict[str, Any]],
) -> dict[str, Any]:
    if schema_version == 2:
        direct = record.get("action_space_audit")
        if isinstance(direct, Mapping):
            audit = deepcopy(dict(direct))
            audit = _validate_recorded_audit(audit, legal_actions)
            return _reconcile_public_actions(audit, observation, legal_actions)
        metadata = record.get("metadata")
        if isinstance(metadata, Mapping) and isinstance(
            metadata.get("action_space_audit"), Mapping
        ):
            audit = deepcopy(dict(metadata["action_space_audit"]))
            audit = _validate_recorded_audit(audit, legal_actions)
            return _reconcile_public_actions(audit, observation, legal_actions)
        return {
            "complete": False,
            "supported": False,
            "state_type": observation.get("state_type"),
            "action_count": len(legal_actions),
            "issues": ["missing_current_state_action_space_audit"],
        }
    return _derive_v1_audit(observation, legal_actions)


def _reconcile_public_actions(
    audit: dict[str, Any],
    observation: Mapping[str, Any],
    legal_actions: Sequence[Mapping[str, Any]],
) -> dict[str, Any]:
    """Fail closed when a v2 candidate set disagrees with its public state."""

    derived = derive_legal_actions(dict(observation))
    recorded_keys = Counter(_action_key(item) for item in legal_actions)
    derived_keys = Counter(_action_key(item) for item in derived)
    recorded_public = Counter(_public_action_key(item) for item in legal_actions)
    derived_public = Counter(_public_action_key(item) for item in derived)
    derived_audit = audit_action_space(dict(observation))
    if recorded_keys != derived_keys or recorded_public != derived_public:
        issues = list(audit.get("issues", []))
        issues.append("recorded_legal_actions_do_not_match_public_snapshot")
        audit["issues"] = list(dict.fromkeys(issues))
        audit["complete"] = False
    for key in ("state_type", "supported", "action_count", "candidate_action_count"):
        if audit.get(key) != derived_audit.get(key):
            issues = list(audit.get("issues", []))
            issues.append(f"recorded_audit_{key}_does_not_match_public_snapshot")
            audit["issues"] = list(dict.fromkeys(issues))
            audit["complete"] = False
    if derived_audit.get("complete") is not True:
        issues = list(audit.get("issues", []))
        issues.extend(str(issue) for issue in derived_audit.get("issues", []))
        audit["issues"] = list(dict.fromkeys(issues))
        audit["complete"] = False
    return audit


def _validate_recorded_audit(
    audit: dict[str, Any], legal_actions: Sequence[Mapping[str, Any]]
) -> dict[str, Any]:
    """Turn internally inconsistent v2 audits into explicit failures."""

    recorded_action_count = len(legal_actions)
    recorded_candidate_count = sum(
        1 for action in legal_actions if "candidate" in action
    )
    raw_issues = audit.get("issues")
    issues: list[str] = []
    valid = True
    if not isinstance(raw_issues, list):
        issues.append("audit_issues_missing_or_not_list")
        valid = False
    else:
        for issue in raw_issues:
            if not isinstance(issue, str) or not issue:
                issues.append("audit_issue_not_nonempty_string")
                valid = False
            else:
                issues.append(issue)
        if raw_issues:
            valid = False

    for key in ("supported", "complete"):
        if not isinstance(audit.get(key), bool):
            issues.append(f"audit_{key}_missing_or_not_boolean")
            valid = False
    if audit.get("supported") is not True or audit.get("complete") is not True:
        valid = False

    action_count = audit.get("action_count")
    if type(action_count) is not int or action_count < 0:
        issues.append("audit_action_count_missing_or_invalid")
        valid = False
    elif action_count != recorded_action_count:
        issues.append("audit_action_count_mismatch")
        valid = False

    candidate_count = audit.get("candidate_action_count")
    if type(candidate_count) is not int or candidate_count < 0:
        issues.append("audit_candidate_action_count_missing_or_invalid")
        valid = False
    else:
        if type(action_count) is int and candidate_count > action_count:
            issues.append("audit_candidate_action_count_exceeds_action_count")
            valid = False
        if candidate_count != recorded_candidate_count:
            issues.append("audit_candidate_action_count_mismatch")
            valid = False

    for key in ("candidate_complete", "state_complete"):
        if key not in audit:
            continue
        if not isinstance(audit.get(key), bool):
            issues.append(f"audit_{key}_not_boolean")
            valid = False
        elif audit.get(key) is False:
            issues.append(f"{key}:false")
            valid = False
    audit["complete"] = bool(valid)
    audit["issues"] = list(dict.fromkeys(str(issue) for issue in issues))
    audit["recorded_action_count"] = recorded_action_count
    audit["recorded_candidate_action_count"] = recorded_candidate_count
    return audit


def _normalise_record_action(
    record: Mapping[str, Any],
    schema_version: int,
    legal_actions: list[dict[str, Any]],
) -> NormalizedAction:
    if schema_version == 1:
        return normalize_action(record.get("action"), legal_actions)  # type: ignore[arg-type]

    chosen_index = record.get("chosen_action_index")
    if isinstance(chosen_index, bool) or (
        chosen_index is not None and not isinstance(chosen_index, int)
    ):
        raise ActionNormalizationError("chosen_action_index must be an integer")
    chosen = record.get("action")
    if chosen is not None and not isinstance(chosen, Mapping):
        raise ActionNormalizationError("schema v2 action must be a structured object")
    return normalize_action_pair(
        chosen_action_index=chosen_index,
        chosen_action=chosen,
        legal_actions=legal_actions,
    )


def _information_mode(
    record: Mapping[str, Any], required_mode: str | None
) -> tuple[str, bool]:
    candidates: list[Any] = [record.get("information_mode")]
    for key in ("info", "metadata"):
        block = record.get(key)
        if isinstance(block, Mapping):
            candidates.append(block.get("information_mode"))
    for candidate in candidates:
        if isinstance(candidate, str):
            return candidate.casefold(), False
    return required_mode or "limited", True


def _find_hidden_paths(value: Any, path: str) -> list[str]:
    found: list[str] = []
    if isinstance(value, Mapping):
        for raw_key, item in value.items():
            key = str(raw_key)
            lowered = key.casefold()
            item_path = f"{path}.{key}" if path else key
            is_hidden = (
                lowered in _HIDDEN_KEYS
                or lowered.startswith("privileged_")
                or lowered.startswith("hidden_")
                or any(fragment in lowered for fragment in _HIDDEN_KEY_FRAGMENTS)
            )
            # A null privileged channel is the expected limited-mode marker.
            if is_hidden and item is not None:
                found.append(item_path)
            found.extend(_find_hidden_paths(item, item_path))
    elif isinstance(value, list):
        for index, item in enumerate(value):
            found.extend(_find_hidden_paths(item, f"{path}[{index}]"))
    return found


def _limited_leakage_paths(
    record: Mapping[str, Any],
    *,
    observation: dict[str, Any],
    legal_actions: list[dict[str, Any]],
    chosen_action: dict[str, Any] | None,
    next_observation: dict[str, Any] | None,
    next_legal_actions: list[dict[str, Any]],
) -> tuple[str, ...]:
    roots = {
        "observation": observation,
        "privileged_observation": record.get("privileged_observation"),
        "legal_actions": legal_actions,
        "chosen_action": chosen_action,
        "next_observation": next_observation,
        "next_privileged_observation": record.get(
            "next_privileged_observation"
        ),
        "next_legal_actions": next_legal_actions,
    }
    found: list[str] = []
    for name, value in roots.items():
        found.extend(_find_hidden_paths(value, name))
    # Metadata is not consumed by the baseline, but hidden runtime payloads in
    # it are still provenance contamination.  A top-level seed is the one
    # exception: it is retained solely for hashing whole runs into one split
    # group and is never copied into observation/model features.
    metadata = record.get("metadata")
    if isinstance(metadata, Mapping):
        metadata_for_audit = {
            key: value
            for key, value in metadata.items()
            if str(key).casefold() not in {"seed", "seed_group"}
        }
        found.extend(_find_hidden_paths(metadata_for_audit, "metadata"))
    # ``info`` is not a model feature, except a non-null privileged channel is
    # an explicit contamination signal worth rejecting.
    for container_name in ("info", "metadata"):
        block = record.get(container_name)
        if isinstance(block, Mapping) and block.get("privileged") is not None:
            found.append(f"{container_name}.privileged")
    if record.get("privileged") is not None:
        found.append("privileged")
    return tuple(sorted(set(found)))


def find_limited_leakage_paths(record: Mapping[str, Any]) -> tuple[str, ...]:
    """Recompute limited-channel leakage from a normalized or transition row.

    Callers must not trust a serialized ``leakage_paths`` declaration: this
    function scans every model-facing field and provenance metadata again.
    Top-level seed metadata is exempt only because it is hashed for group
    splitting and is never passed to a policy.
    """

    observation = _dict_or_empty(record.get("observation"))
    legal_actions = _action_list(record.get("legal_actions"))
    chosen = record.get("chosen_action", record.get("action"))
    chosen_action = _dict_or_none(chosen)
    next_observation = _dict_or_none(record.get("next_observation"))
    next_legal_actions = _action_list(record.get("next_legal_actions"))
    return _limited_leakage_paths(
        record,
        observation=observation,
        legal_actions=legal_actions,
        chosen_action=chosen_action,
        next_observation=next_observation,
        next_legal_actions=next_legal_actions,
    )


def _source_value(record: Mapping[str, Any], key: str) -> Any:
    for block in (record, record.get("metadata"), record.get("info")):
        if isinstance(block, Mapping) and block.get(key) is not None:
            return block.get(key)
    return None


def decision_group_id(record: Mapping[str, Any], episode_key: EpisodeKey) -> str:
    """Derive a seed/lineage-preserving opaque group ID.

    Source seed values are useful for leakage-free splitting but should not be
    handed to a limited policy, so the returned identifier is a one-way hash.
    """

    seed_group = _source_value(record, "seed_group")
    if seed_group is None:
        seed_group = _source_value(record, "seed")
    identity: dict[str, Any] = {}
    if seed_group is not None:
        # Never add lineage/run IDs to a seed identity: SL/resume branches from
        # the same seed must remain in one dataset partition.  Version,
        # character, and ascension are intentionally omitted too: keeping an
        # identical seed together is the conservative choice for transfer
        # experiments. Callers should still audit version mixing separately.
        identity["seed"] = seed_group
    else:
        lineage_id = _source_value(record, "lineage_id")
        run_id = _source_value(record, "run_id")
        if lineage_id is not None:
            identity["lineage_id"] = lineage_id
        elif run_id is not None:
            identity["run_id"] = run_id
        else:
            identity["episode"] = [episode_key.worker_id, episode_key.episode_id]
    digest = hashlib.sha256(_canonical_json(identity).encode("utf-8")).hexdigest()
    return f"group-{digest[:24]}"


def decision_id_for(
    record: Mapping[str, Any],
    *,
    episode_key: EpisodeKey,
    observation: Mapping[str, Any],
    legal_actions: Sequence[Mapping[str, Any]],
    chosen_action_index: int | None,
    chosen_action: Mapping[str, Any] | None,
) -> str:
    """Build a deterministic ID from semantic decision identity, not file path."""

    identity = {
        "episode": [episode_key.worker_id, episode_key.episode_id],
        "sequence": record.get("sequence"),
        "observation": observation,
        "legal_actions": legal_actions,
        "chosen_action_index": chosen_action_index,
        "chosen_action": chosen_action,
    }
    digest = hashlib.sha256(_canonical_json(identity).encode("utf-8")).hexdigest()
    return f"decision-{digest[:32]}"


def _build_decision(
    record: Mapping[str, Any],
    *,
    episode: EpisodeOutcome,
    required_information_mode: str | None,
) -> DecisionRecord:
    schema_version = int(record["schema_version"])
    observation = _dict_or_empty(record.get("observation"))
    privileged_observation = _dict_or_none(record.get("privileged_observation"))
    legal_actions = _action_list(record.get("legal_actions"))
    next_observation = _dict_or_none(record.get("next_observation"))
    next_privileged_observation = _dict_or_none(
        record.get("next_privileged_observation")
    )
    next_legal_actions = _action_list(record.get("next_legal_actions"))
    if schema_version == 1 and not isinstance(record.get("next_legal_actions"), list):
        next_legal_actions = derive_legal_actions(next_observation)
    action_mask, action_mask_length_valid, action_mask_values_valid = _mask(
        record.get("action_mask"),
        len(legal_actions),
        allow_missing=schema_version == 1,
    )
    (
        next_action_mask,
        next_action_mask_length_valid,
        next_action_mask_values_valid,
    ) = _mask(
        record.get("next_action_mask"),
        len(next_legal_actions),
        allow_missing=schema_version == 1,
    )
    audit = _action_space_audit(
        record,
        schema_version=schema_version,
        observation=observation,
        legal_actions=legal_actions,
    )
    metadata = _dict_or_empty(record.get("metadata"))
    metadata.setdefault("source_schema_version", schema_version)

    reasons: list[str] = []
    if not observation:
        reasons.append("observation_missing")
    raw_legal_actions = record.get("legal_actions")
    if not isinstance(raw_legal_actions, list) or len(legal_actions) != len(
        raw_legal_actions
    ):
        reasons.append("legal_actions_malformed")
    if episode.truncated:
        reasons.append("episode_truncated")
    if episode.conflicting_outcomes:
        reasons.append("conflicting_terminal_outcomes")
    if episode.terminal_records > 1:
        reasons.append("multiple_terminal_records")
    if not episode.sequence_complete:
        reasons.append("episode_sequence_missing")
    if not episode.sequence_monotonic:
        reasons.append("episode_sequence_non_monotonic")
    if not episode.terminal_is_last:
        reasons.append("terminal_not_last")
    if not episode.group_consistent:
        reasons.append("episode_group_identity_inconsistent")
    if not episode.terminal_state_consistent:
        reasons.append("terminal_state_mismatch")
    if (
        episode.terminal_records == 0
        or episode.outcome is None
        or episode.terminal_without_outcome
    ):
        reasons.append("missing_terminal_outcome")
    if audit.get("complete") is not True:
        reasons.append("action_space_incomplete")

    if not action_mask_length_valid:
        reasons.append("action_mask_mismatch")
    if not action_mask_values_valid:
        reasons.append("action_mask_invalid")
    if not next_action_mask_length_valid:
        reasons.append("next_action_mask_mismatch")
    if not next_action_mask_values_valid:
        reasons.append("next_action_mask_invalid")
    if next_observation is not None:
        derived_next_actions = derive_legal_actions(next_observation)
        if Counter(_public_action_key(item) for item in next_legal_actions) != Counter(
            _public_action_key(item) for item in derived_next_actions
        ):
            reasons.append("next_legal_actions_do_not_match_public_snapshot")
    elif next_legal_actions:
        reasons.append("next_legal_actions_without_public_snapshot")

    normalized: NormalizedAction | None = None
    try:
        normalized = _normalise_record_action(
            record, schema_version, legal_actions
        )
    except ActionNormalizationError as error:
        reasons.append("action_not_legal")
        metadata["action_normalization_error"] = str(error)

    if normalized is not None and action_mask_length_valid and action_mask_values_valid and (
        normalized.index >= len(action_mask) or action_mask[normalized.index] != 1
    ):
        reasons.append("action_not_legal")
        metadata["action_normalization_error"] = (
            "chosen action is disabled by action_mask"
        )

    raw_wire_action = record.get("wire_action")
    if schema_version == 2:
        if not isinstance(raw_wire_action, Mapping):
            reasons.append("wire_action_missing_or_malformed")
        elif normalized is not None and _action_key(raw_wire_action) != _action_key(
            normalized.action
        ):
            reasons.append("wire_action_mismatch")

    chosen_index = normalized.index if normalized is not None else None
    chosen_action = normalized.action if normalized is not None else None
    mode, inferred_mode = _information_mode(record, required_information_mode)
    if inferred_mode:
        metadata["information_mode_inferred"] = True
        reasons.append("information_mode_missing")
    if mode not in {"limited", "omniscient"}:
        reasons.append("unknown_information_mode")
    if required_information_mode is not None and mode != required_information_mode:
        reasons.append("information_mode_mismatch")
    if mode == "limited":
        if record.get("privileged_observation") is not None:
            reasons.append("limited_privileged_observation_present")
        if record.get("next_privileged_observation") is not None:
            reasons.append("limited_next_privileged_observation_present")
        privileged_observation = None
        next_privileged_observation = None
    elif mode == "omniscient":
        if not isinstance(record.get("privileged_observation"), Mapping):
            reasons.append("omniscient_privileged_observation_missing")
        if not isinstance(record.get("next_privileged_observation"), Mapping):
            reasons.append("omniscient_next_privileged_observation_missing")

    leakage_paths: tuple[str, ...] = ()
    if mode == "limited" or required_information_mode == "limited":
        leakage_paths = _limited_leakage_paths(
            record,
            observation=observation,
            legal_actions=legal_actions,
            chosen_action=chosen_action,
            next_observation=next_observation,
            next_legal_actions=next_legal_actions,
        )
        if leakage_paths:
            reasons.append("limited_information_leakage")

    key = episode.key
    decision_id = decision_id_for(
        record,
        episode_key=key,
        observation=observation,
        legal_actions=legal_actions,
        chosen_action_index=chosen_index,
        chosen_action=chosen_action,
    )
    value_reasons = tuple(dict.fromkeys(reasons))
    # A missing terminal outcome invalidates the value target, not an otherwise
    # sound demonstrated action.  Other episode-integrity failures remain shared
    # gates because they can indicate merged/corrupted trajectories.
    bc_reasons = tuple(
        reason for reason in value_reasons if reason != "missing_terminal_outcome"
    )
    sequence = record.get("sequence")
    if isinstance(sequence, bool) or not isinstance(sequence, int):
        sequence = None
    wire_action = _dict_or_none(record.get("wire_action"))
    reward = record.get("reward")
    if isinstance(reward, bool) or not isinstance(reward, (int, float)):
        reward = None
    timestamp = record.get("time")
    if isinstance(timestamp, bool) or not isinstance(timestamp, (int, float)):
        timestamp = None
    return DecisionRecord(
        decision_id=decision_id,
        group_id=decision_group_id(record, key),
        source_schema_version=schema_version,
        sequence=sequence,
        time=timestamp,
        worker_id=key.worker_id,
        episode_id=key.episode_id,
        observation=observation,
        privileged_observation=privileged_observation,
        legal_actions=legal_actions,
        action_mask=action_mask,
        chosen_action_index=chosen_index,
        chosen_action=chosen_action,
        wire_action=wire_action,
        reward=reward,
        next_observation=next_observation,
        next_privileged_observation=next_privileged_observation,
        next_legal_actions=next_legal_actions,
        next_action_mask=next_action_mask,
        terminated=bool(record.get("terminated")),
        truncated=bool(record.get("truncated")),
        outcome=episode.outcome,
        information_mode=mode,
        action_space_audit=audit,
        metadata=metadata,
        eligible=not bc_reasons,
        bc_eligible=not bc_reasons,
        value_eligible=not value_reasons,
        ineligibility_reasons=bc_reasons,
        bc_ineligibility_reasons=bc_reasons,
        value_ineligibility_reasons=value_reasons,
        leakage_paths=leakage_paths,
    )


def iter_decision_dataset(
    path: str | Path,
    *,
    information_mode: Literal["limited", "omniscient"] | None = "limited",
    on_ineligible: Literal["mark", "skip", "raise"] = "mark",
) -> Iterator[DecisionRecord]:
    """Two-pass streaming conversion from transition rows to decision rows.

    ``mark`` yields all rows with explicit quality flags, ``skip`` yields only
    eligible rows, and ``raise`` stops at the first ineligible row.
    """

    if information_mode not in {None, "limited", "omniscient"}:
        raise ValueError("information_mode must be limited, omniscient, or None")
    if on_ineligible not in {"mark", "skip", "raise"}:
        raise ValueError("on_ineligible must be mark, skip, or raise")
    outcomes = scan_episode_outcomes(path)
    source = Path(path)
    for line_number, record in _iter_located_transitions(source):
        key = _episode_key(record, where=f"{source}:{line_number}")
        decision = _build_decision(
            record,
            episode=outcomes[key],
            required_information_mode=information_mode,
        )
        if decision.bc_eligible or on_ineligible == "mark":
            yield decision
        elif on_ineligible == "raise":
            raise IneligibleDecisionError(
                decision.decision_id, decision.bc_ineligibility_reasons
            )


def load_decision_dataset(
    path: str | Path,
    *,
    information_mode: Literal["limited", "omniscient"] | None = "limited",
    on_ineligible: Literal["mark", "skip", "raise"] = "mark",
) -> list[DecisionRecord]:
    """Materialize :func:`iter_decision_dataset` for small/offline jobs."""

    return list(
        iter_decision_dataset(
            path,
            information_mode=information_mode,
            on_ineligible=on_ineligible,
        )
    )


# Concise aliases for callers that prefer iterator/load terminology.
iter_decisions = iter_decision_dataset
load_decisions = load_decision_dataset
build_decision_dataset = load_decision_dataset


def deterministic_group_split(
    group_id: Any,
    *,
    train: float = 0.8,
    val: float = 0.1,
    test: float = 0.1,
    salt: str = DEFAULT_SPLIT_SALT,
) -> Literal["train", "val", "test"]:
    """Assign a whole group to one reproducible dataset split."""

    ratios = (train, val, test)
    if any(
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(value)
        or value < 0
        for value in ratios
    ):
        raise ValueError("split ratios must be finite non-negative numbers")
    total = float(sum(ratios))
    if total <= 0:
        raise ValueError("at least one split ratio must be positive")
    normalized_train = float(train) / total
    normalized_validation = float(val) / total
    payload = _canonical_json({"salt": salt, "group_id": group_id})
    bucket = int.from_bytes(hashlib.sha256(payload.encode("utf-8")).digest()[:8], "big")
    unit = bucket / float(1 << 64)
    if unit < normalized_train:
        return "train"
    if unit < normalized_train + normalized_validation:
        return "val"
    return "test"


def split_decisions(
    decisions: Iterable[DecisionRecord],
    *,
    train: float = 0.8,
    val: float = 0.1,
    test: float = 0.1,
    salt: str = DEFAULT_SPLIT_SALT,
    group_key: Callable[[DecisionRecord], Any] | None = None,
) -> dict[str, list[DecisionRecord]]:
    """Partition rows without ever splitting a group across partitions."""

    result: dict[str, list[DecisionRecord]] = {
        "train": [],
        "val": [],
        "test": [],
    }
    get_group = group_key or (lambda decision: decision.group_id)
    for decision in decisions:
        split = deterministic_group_split(
            get_group(decision),
            train=train,
            val=val,
            test=test,
            salt=salt,
        )
        result[split].append(decision)
    return result
