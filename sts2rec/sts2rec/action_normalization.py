"""Normalize compact-trajectory actions against their legal action set.

The text environment accepts either a zero-based integer action index or a
structured action dictionary.  Training code should not have to care which
representation the policy used, so this module resolves both forms to one
stable ``(index, structured action)`` contract.

Only evaluator annotations are ignored while matching.  Parameters that are
sent to the game (for example ``card_index`` and ``target``) remain part of the
action identity.
"""

from __future__ import annotations

import json
from copy import deepcopy
from dataclasses import dataclass
from typing import Any, Mapping, Sequence


# These keys annotate an action for humans/evaluators but are not part of the
# command posted to STS2MCP.  Keep this list in sync with text_env._NON_WIRE_KEYS
# without importing text_env (which would pull runtime environment types into
# the offline data layer).
NON_SEMANTIC_ACTION_KEYS = frozenset(
    {"action_index", "candidate", "target_candidate", "label", "q_value"}
)


class ActionNormalizationError(ValueError):
    """Raised when a chosen action cannot be resolved unambiguously."""


@dataclass(frozen=True)
class NormalizedAction:
    """A chosen action resolved to the current legal-action list."""

    index: int
    action: dict[str, Any]

    def as_dict(self) -> dict[str, Any]:
        return {"index": self.index, "action": deepcopy(self.action)}


def semantic_action(action: Mapping[str, Any]) -> dict[str, Any]:
    """Return the command-bearing portion of an action.

    Candidate payloads remain available on :class:`NormalizedAction`; they are
    ignored only for equality because the environment strips them before
    submitting a command.
    """

    return {
        str(key): deepcopy(value)
        for key, value in action.items()
        if str(key) not in NON_SEMANTIC_ACTION_KEYS
    }


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
        raise ActionNormalizationError(
            f"action is not deterministically JSON-serializable: {error}"
        ) from error


def actions_equivalent(left: Mapping[str, Any], right: Mapping[str, Any]) -> bool:
    """Whether two actions submit the same structured command."""

    return _canonical_json(semantic_action(left)) == _canonical_json(
        semantic_action(right)
    )


def _validated_legal_actions(
    legal_actions: Sequence[Mapping[str, Any]],
) -> list[dict[str, Any]]:
    if isinstance(legal_actions, (str, bytes, bytearray)):
        raise ActionNormalizationError("legal_actions must be a sequence of objects")
    result: list[dict[str, Any]] = []
    for index, action in enumerate(legal_actions):
        if not isinstance(action, Mapping):
            raise ActionNormalizationError(
                f"legal_actions[{index}] must be an object, got "
                f"{type(action).__name__}"
            )
        result.append(deepcopy(dict(action)))
    return result


def _resolve_index(index: int, legal_actions: list[dict[str, Any]]) -> NormalizedAction:
    if index < 0 or index >= len(legal_actions):
        raise ActionNormalizationError(
            f"chosen action index {index} outside [0, {len(legal_actions)})"
        )
    return NormalizedAction(index=index, action=deepcopy(legal_actions[index]))


def normalize_action(
    chosen_action: int | Mapping[str, Any],
    legal_actions: Sequence[Mapping[str, Any]],
) -> NormalizedAction:
    """Resolve an integer or structured choice against ``legal_actions``.

    Integer choices use list position, matching :class:`Sts2TextEnv`.  A
    structured choice may carry ``action_index``; if so both its index and
    command fields are validated.  Without an index, its command-bearing fields
    must match exactly one legal action.
    """

    legal = _validated_legal_actions(legal_actions)
    if isinstance(chosen_action, bool):
        raise ActionNormalizationError("boolean is not a valid action index")
    if isinstance(chosen_action, int):
        return _resolve_index(chosen_action, legal)
    if not isinstance(chosen_action, Mapping):
        raise ActionNormalizationError(
            "chosen action must be an integer index or structured object"
        )

    chosen = dict(chosen_action)
    embedded_index = chosen.get("action_index")
    if embedded_index is not None:
        if isinstance(embedded_index, bool) or not isinstance(embedded_index, int):
            raise ActionNormalizationError("action_index must be an integer")
        resolved = _resolve_index(embedded_index, legal)
        # ``{"action_index": n}`` is a useful compact structured form.  If
        # command fields are present, require them to agree with the indexed
        # candidate instead of silently trusting one representation.
        if semantic_action(chosen) and not actions_equivalent(chosen, resolved.action):
            raise ActionNormalizationError(
                "chosen action_index and structured action disagree"
            )
        return resolved

    matches = [
        index
        for index, candidate in enumerate(legal)
        if actions_equivalent(chosen, candidate)
    ]
    if not matches:
        raise ActionNormalizationError(
            "structured action is not in the current legal-action set"
        )
    if len(matches) > 1:
        raise ActionNormalizationError(
            "structured action matches multiple legal actions; provide action_index"
        )
    return _resolve_index(matches[0], legal)


def normalize_action_pair(
    *,
    chosen_action_index: int | None,
    chosen_action: Mapping[str, Any] | None,
    legal_actions: Sequence[Mapping[str, Any]],
) -> NormalizedAction:
    """Resolve the explicit index + action pair used by transition schema v2.

    At least one representation is required.  When both are present they must
    identify the same legal command.
    """

    if chosen_action_index is None and chosen_action is None:
        raise ActionNormalizationError("transition has no chosen action")
    if chosen_action_index is not None:
        resolved = normalize_action(chosen_action_index, legal_actions)
        if chosen_action is not None:
            # Schema v2 normally stores the full selected candidate, including
            # its embedded action_index.  That index is identity-bearing here:
            # two candidates may intentionally submit the same wire command.
            # Comparing only semantic wire fields would silently pair the
            # structured candidate with the wrong list entry in that case.
            embedded_index = chosen_action.get("action_index")
            if embedded_index is not None:
                structured = normalize_action(chosen_action, legal_actions)
                if structured.index != resolved.index:
                    raise ActionNormalizationError(
                        "chosen_action_index and action identify different legal actions"
                    )
            elif not actions_equivalent(chosen_action, resolved.action):
                raise ActionNormalizationError(
                    "chosen_action_index and action identify different legal actions"
                )
        return resolved
    assert chosen_action is not None
    return normalize_action(chosen_action, legal_actions)
