"""Semantic checks for a recorded decision's candidates and pre-input observation.

This checks evidence, not whether the game engine would accept every proposed move.
Historical floor summaries may recover missing offers, but are never silently used
as an earlier observation here.
"""
from collections.abc import Mapping
from typing import Any


def _records(value: Any) -> list[Mapping[str, Any]]:
    return [item for item in value if isinstance(item, Mapping)] if isinstance(value, list) else []


def audit_decision(state: Mapping[str, Any] | None, action: Mapping[str, Any]) -> dict[str, Any]:
    kind = action.get("kind", action.get("type"))
    kind = kind if isinstance(kind, str) else None
    params = action.get("params", action)
    params = params if isinstance(params, Mapping) else {}
    expected = {
        "player_choice": {"card_reward", "card_select", "hand_select", "bundle_select", "relic_select"},
        "vote_for_map_coord": {"map"},
        "event_option": {"event"},
        "event_proceed": {"event"},
        "rest_site_option": {"rest_site"},
    }.get(kind)
    provenance = params.get("_decision")
    issues: list[str] = []
    checked = expected is not None
    if not checked:
        return {"checked": False, "complete": None, "issues": [], "scope": "decision_snapshot_consistency"}
    if isinstance(provenance, Mapping):
        if provenance.get("status") != "captured":
            issues.append("pre_input_capture_missing")
        elif provenance.get("stage") != "before_input":
            issues.append("pre_input_capture_not_observed")
    state = state or {}
    screen = state.get("state_type")
    screen = screen if isinstance(screen, str) else None
    if screen not in expected:
        issues.append(f"wrong_decision_screen:{screen}")
    else:
        block = state.get(screen, {})
        if not isinstance(block, Mapping) or "section_error" in block or "error" in block:
            issues.append("candidate_section_failed")
        else:
            key = {"map": "next_options", "rest_site": "options", "event": "options",
                   "bundle_select": "bundles", "relic_select": "relics"}.get(screen, "cards")
            candidates = _records(block.get(key))
            if screen == "hand_select":
                candidates += _records(block.get("selected_cards"))
            if screen == "card_reward":
                candidates += _records(block.get("alternatives"))
            if not candidates:
                issues.append("candidate_set_missing_or_empty")
            elif kind == "vote_for_map_coord" and isinstance(params.get("destination"), Mapping):
                dest = params["destination"]
                if not any((c.get("col"), c.get("row")) == (dest.get("col"), dest.get("row")) for c in candidates):
                    issues.append("chosen_destination_not_offered")
            elif kind == "rest_site_option" and "option_id" in params:
                if not any(c.get("id") == params["option_id"] and c.get("is_enabled", False) for c in candidates):
                    issues.append("chosen_rest_option_not_enabled")
            elif kind in {"event_option", "event_proceed"} and params.get("text_key"):
                if any("text_key" in c for c in candidates) and not any(c.get("text_key") == params["text_key"] for c in candidates):
                    issues.append("chosen_event_option_not_offered")
            elif kind == "player_choice":
                if params.get("choice_type") == "Index" and screen in {"card_reward", "card_select", "bundle_select", "relic_select"}:
                    indexes = params.get("indexes", [params.get("index")])
                    offered = [c.get("choice_index", c.get("index")) for c in candidates]
                    # Legacy rewards represented skip as an extra index but omitted
                    # the alternatives. Flag that uncertainty instead of guessing.
                    if not isinstance(indexes, list) or any(i is not None and (not isinstance(i, int) or isinstance(i, bool)) for i in indexes):
                        issues.append("invalid_choice_indexes")
                    elif any(i is not None and i >= 0 and i not in offered for i in indexes):
                        issues.append("chosen_index_not_in_recorded_candidates")
                if params.get("card_instances") and screen in {"card_select", "hand_select"}:
                    offered_ids = [c.get("instance_id") for c in candidates if c.get("instance_id") is not None]
                    selected_instances = _records(params["card_instances"])
                    if not selected_instances or any(c.get("instance_id") not in offered_ids for c in selected_instances):
                        issues.append("chosen_card_instance_not_in_recorded_candidates")
                elif params.get("cards") and screen in {"card_select", "hand_select"}:
                    offered = [str(c.get("id", "")).removeprefix("CARD.") for c in candidates]
                    chosen = params["cards"] if isinstance(params["cards"], list) else [params["cards"]]
                    for card in chosen:
                        card_id = str(card).removeprefix("CARD.")
                        if card_id not in offered:
                            issues.append("chosen_card_not_in_recorded_candidates")
                            break
                        offered.remove(card_id)  # repeated copies require repeated candidates
    return {"checked": True, "complete": not issues, "issues": issues,
            "capture_provenance": "recorded" if provenance is not None else "legacy_unverified",
            "scope": "decision_snapshot_consistency; not proof of all game UI coverage"}
