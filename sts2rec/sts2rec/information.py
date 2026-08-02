"""Explicit information boundaries for human-equivalent and omniscient evaluation."""

from __future__ import annotations

from copy import deepcopy
from enum import Enum
from typing import Any

from .legal_actions import audit_action_space, derive_legal_actions


class InformationMode(str, Enum):
    LIMITED = "limited"
    OMNISCIENT = "omniscient"


_HIDDEN_KEYS = frozenset(
    {
        "seed",
        "rng",
        "rng_state",
        "true_draw_order",
        "future_rooms",
        "future_shops",
        "future_rewards",
    }
)


def _strip_hidden(value: Any) -> Any:
    if isinstance(value, dict):
        return {
            key: _strip_hidden(item)
            for key, item in value.items()
            if str(key).lower() not in _HIDDEN_KEYS
            and not str(key).lower().startswith("privileged_")
        }
    if isinstance(value, list):
        return [_strip_hidden(item) for item in value]
    return deepcopy(value)


def build_information_view(
    state: dict[str, Any],
    mode: InformationMode | str,
    *,
    privileged: dict[str, Any] | None = None,
) -> dict[str, Any]:
    """Create a tagged evaluator input without cross-mode leakage.

    ``state`` is always treated as public observation.  Hidden engine/code/RNG
    facts must be supplied separately through ``privileged`` and are exposed
    only in omniscient mode.  This makes accidental training contamination
    testable instead of relying on naming conventions inside a large blob.
    """
    selected = InformationMode(mode)
    observation = _strip_hidden(state)
    view: dict[str, Any] = {
        "mode": selected.value,
        "observation": observation,
        "legal_actions": derive_legal_actions(observation),
        "action_space_audit": audit_action_space(observation),
        "privileged": None,
    }
    if selected is InformationMode.OMNISCIENT:
        view["privileged"] = deepcopy(privileged or {})
    return view
