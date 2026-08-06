"""Strict adapter from passive human recordings to decision rows.

The recorder's canonical trajectory stores the action vocabulary emitted by
the game hooks, while :mod:`sts2rec.legal_actions` exposes the command
vocabulary accepted by the text environment.  The two vocabularies overlap,
but are not identical.  This module deliberately aligns only mappings that
are documented and unambiguous; it never picks the "closest" legal action.

Incomplete recordings remain useful for behaviour cloning (BC).  They cannot
provide a win/loss target, so ``value_outcome`` is explicitly ``None`` and
``value_eligible`` is false even when ``bc_eligible`` is true.
"""

from __future__ import annotations

import hashlib
import json
import re
from collections import Counter
from collections.abc import Iterator, Mapping, Sequence
from copy import deepcopy
from pathlib import Path
from typing import Any, Literal

from .action_normalization import (
    ActionNormalizationError,
    normalize_action,
)
from .canonical import build_canonical
from .information import InformationMode, build_information_view


HUMAN_DATASET_SCHEMA_VERSION = 1

_AUTOMATIC_ACTION_TYPES = frozenset(
    {
        "move_to_map_coord",
        "ready_to_begin_enemy_turn",
    }
)
_FINAL_ACTION_STATUSES = frozenset({"committed", "executed"})
_INDEX_KEYS = ("index", "option_index", "choice_index", "node_index")
_IDENTIFIER_KEYS = (
    "option_id",
    "id",
    "model_id",
    "key",
    "option",
    "name",
)


class HumanDatasetError(ValueError):
    """A canonical human trajectory violates the adapter input contract."""


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
        raise HumanDatasetError(
            f"value is not deterministically JSON-serializable: {error}"
        ) from error


def _opaque_hash(prefix: str, value: Any, *, length: int) -> str:
    digest = hashlib.sha256(_canonical_json(value).encode("utf-8")).hexdigest()
    return f"{prefix}-{digest[:length]}"


def _as_mapping(value: Any) -> dict[str, Any]:
    if not isinstance(value, Mapping):
        return {}
    return deepcopy(dict(value))


def _as_action_list(value: Any) -> list[dict[str, Any]]:
    if not isinstance(value, list):
        return []
    return [deepcopy(dict(item)) for item in value if isinstance(item, Mapping)]


def _normalised_identifier(value: Any) -> str:
    """Normalize recorder/model identifier spelling without fuzzy matching."""

    return re.sub(r"[^A-Z0-9]+", "_", str(value).upper()).strip("_")


def _model_identifier(value: Any, namespace: str) -> str:
    normalized = _normalised_identifier(value)
    prefix = f"{namespace}_"
    return normalized[len(prefix) :] if normalized.startswith(prefix) else normalized


def _candidate_sources(action: Mapping[str, Any]) -> tuple[Mapping[str, Any], ...]:
    candidate = action.get("candidate")
    if isinstance(candidate, Mapping):
        return action, candidate
    return (action,)


def _first_value(
    sources: Sequence[Mapping[str, Any]], keys: Sequence[str]
) -> Any | None:
    for source in sources:
        for key in keys:
            if source.get(key) is not None:
                return source[key]
    return None


def _recorded_coordinates(params: Mapping[str, Any]) -> tuple[Any, Any] | None:
    col = _first_value((params,), ("col", "column", "x"))
    row = _first_value((params,), ("row", "y"))
    coord = params.get(
        "destination",
        params.get("coord", params.get("map_coord", params.get("node"))),
    )
    if isinstance(coord, Mapping):
        col = col if col is not None else _first_value((coord,), ("col", "column", "x"))
        row = row if row is not None else _first_value((coord,), ("row", "y"))
    elif isinstance(coord, (list, tuple)) and len(coord) == 2:
        col = col if col is not None else coord[0]
        row = row if row is not None else coord[1]
    elif isinstance(coord, str):
        match = re.fullmatch(r"\s*x?(-?\d+)\D+y?(-?\d+)\s*", coord, re.IGNORECASE)
        if match is not None:
            col = col if col is not None else int(match.group(1))
            row = row if row is not None else int(match.group(2))
    if col is None or row is None:
        return None
    return col, row


def _candidate_coordinates(action: Mapping[str, Any]) -> tuple[Any, Any] | None:
    sources = _candidate_sources(action)
    col = _first_value(sources, ("col", "column", "x"))
    row = _first_value(sources, ("row", "y"))
    if col is None or row is None:
        return None
    return col, row


def _mapped_candidates(
    legal_actions: Sequence[Mapping[str, Any]], target_action: str
) -> list[tuple[int, Mapping[str, Any]]]:
    return [
        (index, action)
        for index, action in enumerate(legal_actions)
        if action.get("action") == target_action
    ]


def _filter_by_discriminators(
    candidates: list[tuple[int, Mapping[str, Any]]],
    params: Mapping[str, Any],
    *,
    coordinate: bool = False,
    require_proceed: bool = False,
) -> list[tuple[int, Mapping[str, Any]]]:
    """Apply every available deterministic discriminator to candidates."""

    result = candidates
    recorded_index = _first_value((params,), _INDEX_KEYS)
    if recorded_index is not None:
        result = [
            pair
            for pair in result
            if _first_value(_candidate_sources(pair[1]), _INDEX_KEYS)
            == recorded_index
        ]

    recorded_identifier = _first_value((params,), _IDENTIFIER_KEYS)
    if recorded_identifier is not None:
        wanted = _normalised_identifier(recorded_identifier)
        result = [
            pair
            for pair in result
            if (
                candidate_id := _first_value(
                    _candidate_sources(pair[1]), _IDENTIFIER_KEYS
                )
            )
            is not None
            and _normalised_identifier(candidate_id) == wanted
        ]

    recorded_title = params.get("title")
    if recorded_title is not None:
        wanted_title = _normalised_identifier(recorded_title)
        result = [
            pair
            for pair in result
            if (
                candidate_title := _first_value(
                    _candidate_sources(pair[1]), ("title",)
                )
            )
            is not None
            and _normalised_identifier(candidate_title) == wanted_title
        ]

    if coordinate:
        recorded_coord = _recorded_coordinates(params)
        if recorded_coord is not None:
            result = [
                pair
                for pair in result
                if _candidate_coordinates(pair[1]) == recorded_coord
            ]

    if require_proceed:
        marked = [
            pair
            for pair in result
            if bool(
                _first_value(
                    _candidate_sources(pair[1]),
                    ("is_proceed", "proceed", "is_continue"),
                )
            )
        ]
        # An explicit proceed marker is authoritative.  Some snapshot versions
        # expose no such flag; in that case a sole legal event option is still
        # unambiguous under the confirmed event_proceed mapping.
        if marked:
            result = marked
    return result


def _unique_index(candidates: Sequence[tuple[int, Mapping[str, Any]]]) -> int:
    if len(candidates) != 1:
        if not candidates:
            raise ActionNormalizationError("recorded action has no legal match")
        raise ActionNormalizationError("recorded action has multiple legal matches")
    return candidates[0][0]


def _action_counter(actions: Sequence[Mapping[str, Any]]) -> Counter[str]:
    return Counter(
        _canonical_json(
            {
                str(key): value
                for key, value in action.items()
                if str(key) not in {"action_index", "label", "q_value"}
            }
        )
        for action in actions
    )


def _align_action(
    recorded: Mapping[str, Any], legal_actions: Sequence[Mapping[str, Any]]
) -> int:
    action_type = recorded.get("type")
    params = recorded.get("params")
    if not isinstance(action_type, str):
        raise ActionNormalizationError("recorded action type is missing")
    if not isinstance(params, Mapping):
        raise ActionNormalizationError("recorded action params must be an object")

    if action_type == "end_turn":
        return _unique_index(_mapped_candidates(legal_actions, "end_turn"))
    if action_type == "play_card":
        card_model_id = params.get("card_model_id")
        if card_model_id is None:
            # Some canonical producers already emit the environment's exact
            # wire parameters.  Preserve that safe path; recorder hook payloads
            # with model IDs use the explicit translation below.
            return normalize_action(
                {"action": "play_card", **deepcopy(dict(params))}, legal_actions
            ).index
        wanted_card = _model_identifier(card_model_id, "CARD")
        candidates = []
        for index, candidate_action in _mapped_candidates(
            legal_actions, "play_card"
        ):
            candidate = candidate_action.get("candidate")
            if not isinstance(candidate, Mapping) or candidate.get("id") is None:
                continue
            if _model_identifier(candidate["id"], "CARD") == wanted_card:
                candidates.append((index, candidate_action))

        target_id = params.get("target_id")
        if target_id is not None:
            candidates = [
                pair
                for pair in candidates
                if isinstance(pair[1].get("target_candidate"), Mapping)
                and pair[1]["target_candidate"].get("combat_id") == target_id
            ]

        # Multiple copies of the same model can coexist in hand.  The hook's
        # combat_card_index is used only as a final exact discriminator; model
        # ID (and target, when present) must already agree.
        if len(candidates) > 1 and isinstance(params.get("combat_card_index"), int):
            recorded_index = params["combat_card_index"]
            candidates = [
                pair
                for pair in candidates
                if pair[1].get("card_index") == recorded_index
                or (
                    isinstance(pair[1].get("candidate"), Mapping)
                    and pair[1]["candidate"].get("index") == recorded_index
                )
            ]
        return _unique_index(candidates)
    if action_type == "vote_for_map_coord":
        return _unique_index(
            _filter_by_discriminators(
                _mapped_candidates(legal_actions, "choose_map_node"),
                params,
                coordinate=True,
            )
        )
    if action_type in {"event_option", "event_proceed"}:
        return _unique_index(
            _filter_by_discriminators(
                _mapped_candidates(legal_actions, "choose_event_option"),
                params,
                require_proceed=action_type == "event_proceed",
            )
        )
    if action_type == "rest_site_option":
        return _unique_index(
            _filter_by_discriminators(
                _mapped_candidates(legal_actions, "choose_rest_option"), params
            )
        )

    # For vocabulary that already agrees with the environment, require exact
    # wire semantics.  normalize_action also rejects duplicate semantic
    # candidates, which is essential: this adapter must never break ties.
    command = {"action": action_type, **deepcopy(dict(params))}
    return normalize_action(command, legal_actions).index


def _load_canonical(source: str | Path | Mapping[str, Any]) -> dict[str, Any]:
    if isinstance(source, Mapping):
        trajectory = deepcopy(dict(source))
    else:
        trajectory = build_canonical(source)
    if not isinstance(trajectory.get("meta"), Mapping):
        raise HumanDatasetError("canonical trajectory must contain meta object")
    if not isinstance(trajectory.get("steps"), list):
        raise HumanDatasetError("canonical trajectory must contain steps list")
    return trajectory


def _group_id(meta: Mapping[str, Any]) -> str:
    seed = meta.get("seed")
    if seed is not None:
        # Every branch/resume of one seed must remain in one split group,
        # irrespective of metadata drift elsewhere in the recording.
        identity = {"seed": seed}
    else:
        identity = {"run_id": meta.get("run_id")}
    return _opaque_hash("group", identity, length=24)


def _value_outcome(meta: Mapping[str, Any]) -> tuple[bool | None, str | None]:
    if meta.get("incomplete") is True:
        return None, "session_incomplete"
    result = meta.get("result")
    if not isinstance(result, Mapping) or not isinstance(result.get("win"), bool):
        return None, "terminal_outcome_missing"
    return bool(result["win"]), None


def _decision_id(
    *,
    run_id: Any,
    step_idx: Any,
    action_seq: Any,
    observation: Mapping[str, Any],
    legal_actions: Sequence[Mapping[str, Any]],
) -> str:
    return _opaque_hash(
        "decision",
        {
            "run_id": run_id,
            "step_idx": step_idx,
            "action_seq": action_seq,
            "observation": observation,
            "legal_actions": legal_actions,
        },
        length=32,
    )


def _step_row(
    step: Mapping[str, Any],
    *,
    meta: Mapping[str, Any],
    group_id: str,
    value_outcome: bool | None,
    value_reason: str | None,
) -> dict[str, Any]:
    info = _as_mapping(step.get("info"))
    state_before = _as_mapping(step.get("state_before"))
    state_after = _as_mapping(step.get("state_after"))
    view = build_information_view(state_before, InformationMode.LIMITED)
    next_view = (
        build_information_view(state_after, InformationMode.LIMITED)
        if state_after
        else None
    )
    observation = view["observation"]

    reasons: list[str] = []
    if not state_before:
        reasons.append("observation_missing")
    if info.get("state_before_estimated") is True:
        reasons.append("state_before_estimated")

    status = info.get("action_status")
    action = _as_mapping(step.get("action"))
    action_type = action.get("type")
    params = action.get("params") if isinstance(action.get("params"), Mapping) else {}
    if status == "cancelled":
        reasons.append("action_cancelled")
    elif status == "automatic":
        reasons.append("action_automatic")
    elif status not in _FINAL_ACTION_STATUSES:
        reasons.append("action_status_unconfirmed")
    if (
        action_type in _AUTOMATIC_ACTION_TYPES
        or params.get("automatic") is True
        or params.get("programmatic") is True
    ):
        reasons.append("action_automatic")
    if params.get("human") is False:
        reasons.append("action_not_human")

    raw_legal = info.get("legal_actions")
    legal_actions = _as_action_list(raw_legal)
    public_legal_actions = _as_action_list(view["legal_actions"])
    if not isinstance(raw_legal, list) or len(legal_actions) != len(raw_legal):
        reasons.append("legal_actions_missing_or_malformed")
        legal_actions = public_legal_actions
    if _action_counter(legal_actions) != _action_counter(public_legal_actions):
        reasons.append("legal_actions_do_not_match_public_observation")

    audit = _as_mapping(info.get("action_space_audit"))
    if not audit:
        reasons.append("missing_action_space_audit")
        audit = _as_mapping(view["action_space_audit"])
    if audit.get("supported") is not True or audit.get("complete") is not True:
        reasons.append("action_space_incomplete")
    if not isinstance(audit.get("issues"), list) or audit.get("issues"):
        reasons.append("action_space_audit_issues")
    if audit.get("action_count") != len(legal_actions):
        reasons.append("action_space_count_mismatch")
    expected_candidates = sum(1 for item in legal_actions if "candidate" in item)
    if audit.get("candidate_action_count") != expected_candidates:
        reasons.append("action_space_candidate_count_mismatch")
    if audit.get("state_type") != observation.get("state_type"):
        reasons.append("action_space_state_type_mismatch")

    chosen_index: int | None = None
    alignment_error: str | None = None
    try:
        chosen_index = _align_action(action, legal_actions)
    except ActionNormalizationError as error:
        reasons.append("action_not_uniquely_aligned")
        alignment_error = str(error)

    chosen_action = (
        deepcopy(legal_actions[chosen_index]) if chosen_index is not None else None
    )
    wire_action = None
    if chosen_action is not None:
        wire_action = {
            key: deepcopy(value)
            for key, value in chosen_action.items()
            if key
            not in {
                "action_index",
                "candidate",
                "target_candidate",
                "label",
                "q_value",
            }
        }

    unique_reasons = list(dict.fromkeys(reasons))
    bc_eligible = not unique_reasons
    value_reasons = list(unique_reasons)
    if value_reason is not None:
        value_reasons.append(value_reason)
    value_eligible = bc_eligible and value_outcome is not None

    metadata: dict[str, Any] = {
        "source": "sts2-human-canonical",
        "run_id": meta.get("run_id"),
        "game_version": meta.get("game_version"),
        "character": meta.get("character"),
        "ascension": meta.get("ascension"),
        "step_idx": step.get("step_idx"),
        "action_seq": info.get("action_seq"),
        "recorded_action": action,
        "session_complete": value_outcome is not None,
        "terminal_outcome": value_outcome,
    }
    if meta.get("seed") is not None:
        metadata["seed_fingerprint"] = _opaque_hash(
            "seed", {"seed": meta.get("seed")}, length=24
        )
    if alignment_error is not None:
        metadata["action_alignment_error"] = alignment_error

    return {
        "dataset_schema_version": HUMAN_DATASET_SCHEMA_VERSION,
        "source_schema_version": "human-canonical-v1",
        "decision_id": _decision_id(
            run_id=meta.get("run_id"),
            step_idx=step.get("step_idx"),
            action_seq=info.get("action_seq"),
            observation=observation,
            legal_actions=legal_actions,
        ),
        "group_id": group_id,
        "sequence": step.get("step_idx"),
        "time": step.get("ts"),
        "worker_id": None,
        "episode_id": meta.get("run_id"),
        "observation": observation,
        "privileged_observation": None,
        "legal_actions": legal_actions,
        "action_mask": [1] * len(legal_actions),
        "chosen_action_index": chosen_index,
        "chosen_action": chosen_action,
        "wire_action": wire_action,
        "reward": None,
        "next_observation": next_view["observation"] if next_view else None,
        "next_privileged_observation": None,
        "next_legal_actions": next_view["legal_actions"] if next_view else [],
        "next_action_mask": (
            [1] * len(next_view["legal_actions"]) if next_view else []
        ),
        "terminated": bool(step.get("terminal")),
        "truncated": bool(meta.get("incomplete")),
        "outcome": value_outcome,
        "value_outcome": value_outcome,
        "information_mode": "limited",
        "action_space_audit": audit,
        "metadata": metadata,
        # ``eligible`` intentionally follows BC eligibility so an unfinished
        # human session is not discarded by the existing BC baseline loader.
        "eligible": bc_eligible,
        "bc_eligible": bc_eligible,
        "value_eligible": value_eligible,
        "ineligibility_reasons": unique_reasons,
        "bc_ineligibility_reasons": unique_reasons,
        "value_ineligibility_reasons": list(dict.fromkeys(value_reasons)),
        "leakage_paths": [],
    }


def iter_human_decision_rows(
    source: str | Path | Mapping[str, Any],
    *,
    on_ineligible: Literal["mark", "skip", "raise"] = "mark",
) -> Iterator[dict[str, Any]]:
    """Yield BC/value rows from a session directory or canonical mapping.

    ``skip`` and ``raise`` use BC eligibility.  Missing terminal outcomes do
    not make a sound action label unusable for behaviour cloning.
    """

    if on_ineligible not in {"mark", "skip", "raise"}:
        raise ValueError("on_ineligible must be mark, skip, or raise")
    trajectory = _load_canonical(source)
    meta = _as_mapping(trajectory["meta"])
    group_id = _group_id(meta)
    outcome, outcome_reason = _value_outcome(meta)
    for raw_step in trajectory["steps"]:
        if not isinstance(raw_step, Mapping):
            raise HumanDatasetError("canonical steps must be objects")
        row = _step_row(
            raw_step,
            meta=meta,
            group_id=group_id,
            value_outcome=outcome,
            value_reason=outcome_reason,
        )
        if row["bc_eligible"]:
            yield row
        elif on_ineligible == "mark":
            yield row
        elif on_ineligible == "raise":
            raise HumanDatasetError(
                f"decision {row['decision_id']} is BC-ineligible: "
                + ", ".join(row["bc_ineligibility_reasons"])
            )


def load_human_decision_rows(
    source: str | Path | Mapping[str, Any],
    *,
    on_ineligible: Literal["mark", "skip", "raise"] = "mark",
) -> list[dict[str, Any]]:
    """Materialize :func:`iter_human_decision_rows`."""

    return list(iter_human_decision_rows(source, on_ineligible=on_ineligible))


# Short aliases for callers that treat all adapters as dataset builders.
iter_human_decisions = iter_human_decision_rows
load_human_decisions = load_human_decision_rows
build_human_dataset = load_human_decision_rows
