"""Derive the complete *visible* action set from a passive STS2 snapshot.

The recorder snapshot is the contract: this module never queries the game and
never invents hidden outcomes.  Every enumerated action carries the source
candidate payload so evaluators retain price, description, keywords and other
decision-relevant fields instead of reducing a choice to an opaque index.
"""

from __future__ import annotations

from copy import deepcopy
from typing import Any


COMBAT_TYPES = frozenset({"monster", "elite", "boss"})
SUPPORTED_TYPES = frozenset(
    {
        *COMBAT_TYPES,
        "hand_select",
        "rewards",
        "card_reward",
        "map",
        "event",
        "rest_site",
        "shop",
        "fake_merchant",
        "treasure",
        "card_select",
        "bundle_select",
        "relic_select",
        "crystal_sphere",
        "game_over",
        "menu",
    }
)


def _candidate(action: str, item: dict[str, Any] | None = None, **params: Any) -> dict[str, Any]:
    result: dict[str, Any] = {"action": action, **params}
    if item is not None:
        result["candidate"] = deepcopy(item)
    return result


def _items(block: Any, key: str) -> list[dict[str, Any]]:
    if not isinstance(block, dict) or not isinstance(block.get(key), list):
        return []
    return [item for item in block[key] if isinstance(item, dict)]


def _combat_actions(state: dict[str, Any]) -> list[dict[str, Any]]:
    player = state.get("player") if isinstance(state.get("player"), dict) else {}
    battle = state.get("battle") if isinstance(state.get("battle"), dict) else {}
    enemies = [
        enemy
        for enemy in _items(battle, "enemies")
        if int(enemy.get("hp", 0) or 0) > 0 and enemy.get("is_targetable", True)
    ]
    actions: list[dict[str, Any]] = []
    if battle.get("is_play_phase"):
        for card in _items(player, "hand"):
            if not card.get("can_play", False):
                continue
            params = {"card_index": card.get("index")}
            target_type = str(card.get("target_type", "None")).lower()
            if "anyenemy" in target_type or target_type in {"enemy", "singleenemy"}:
                for enemy in enemies:
                    actions.append(
                        _candidate(
                            "play_card",
                            card,
                            **params,
                            target=enemy.get("entity_id", enemy.get("combat_id")),
                            target_candidate=deepcopy(enemy),
                        )
                    )
            else:
                actions.append(_candidate("play_card", card, **params))
        for potion in _items(player, "potions"):
            if not potion.get("can_use_in_combat", False):
                continue
            target_type = str(potion.get("target_type", "None")).lower()
            if "anyenemy" in target_type or target_type in {"enemy", "singleenemy"}:
                for enemy in enemies:
                    actions.append(
                        _candidate(
                            "use_potion",
                            potion,
                            slot=potion.get("slot"),
                            target=enemy.get("entity_id", enemy.get("combat_id")),
                            target_candidate=deepcopy(enemy),
                        )
                    )
            else:
                actions.append(_candidate("use_potion", potion, slot=potion.get("slot")))
            actions.append(_candidate("discard_potion", potion, slot=potion.get("slot")))
        actions.append(_candidate("end_turn"))
    return actions


def derive_legal_actions(state: dict[str, Any] | None) -> list[dict[str, Any]]:
    """Return all actions exposed by the current public snapshot.

    An empty list can be legitimate for a transition frame.  Use
    :func:`audit_action_space` to distinguish that from an unsupported screen.
    """
    if not isinstance(state, dict):
        return []
    state_type = str(state.get("state_type", "unknown"))
    if state_type == "menu":
        actions = []
        for option in state.get("options", []):
            item = {'name':option, 'enabled':True} if isinstance(option,str) else option
            if isinstance(item,dict) and item.get('name') and item.get('enabled',True):
                actions.append(_candidate('menu_select',item,option=item['name']))
        return actions
    if state_type in COMBAT_TYPES:
        return _combat_actions(state)
    if state_type == "hand_select":
        block = state.get("hand_select")
        actions = [
            _candidate("combat_select_card", card, card_index=card.get("index"))
            for card in _items(block, "cards")
        ]
        if isinstance(block, dict) and block.get("can_confirm"):
            actions.append(_candidate("combat_confirm_selection"))
        return actions
    if state_type == "rewards":
        block = state.get("rewards")
        actions = [
            _candidate("claim_reward", item, index=item.get("index"))
            for item in _items(block, "items")
        ]
        if isinstance(block, dict) and block.get("can_proceed"):
            actions.append(_candidate("proceed"))
        return actions
    if state_type == "card_reward":
        block = state.get("card_reward")
        actions = [
            _candidate("select_card_reward", card, card_index=card.get("index"))
            for card in _items(block, "cards")
        ]
        if isinstance(block, dict) and block.get("can_skip"):
            actions.append(_candidate("skip_card_reward"))
        return actions
    if state_type == "map":
        return [
            _candidate("choose_map_node", node, index=node.get("index"))
            for node in _items(state.get("map"), "next_options")
        ]
    if state_type == "event":
        block = state.get("event")
        if isinstance(block, dict) and block.get("in_dialogue"):
            return [_candidate("advance_dialogue")]
        return [
            _candidate("choose_event_option", option, index=option.get("index"))
            for option in _items(block, "options")
            if not option.get("is_locked", False) and not option.get("was_chosen", False)
        ]
    if state_type == "rest_site":
        block = state.get("rest_site")
        actions = [
            _candidate("choose_rest_option", option, index=option.get("index"))
            for option in _items(block, "options")
            if option.get("is_enabled", False)
        ]
        if isinstance(block, dict) and block.get("can_proceed"):
            actions.append(_candidate("proceed"))
        return actions
    if state_type in {"shop", "fake_merchant"}:
        outer = state.get(state_type)
        block = outer.get("shop") if state_type == "fake_merchant" and isinstance(outer, dict) else outer
        actions = [
            _candidate("shop_purchase", item, index=item.get("index"))
            for item in _items(block, "items")
            if item.get("is_stocked", False) and item.get("can_afford", False)
        ]
        # A shop is always leaveable from the player's perspective.  The UI's
        # can_proceed flag can briefly be false while the inventory is open;
        # closing/leaving remains the same semantic option.
        actions.append(_candidate("proceed"))
        return actions
    if state_type == "treasure":
        block = state.get("treasure")
        actions = [
            _candidate("claim_treasure_relic", relic, index=relic.get("index"))
            for relic in _items(block, "relics")
        ]
        if isinstance(block, dict) and block.get("chest_opened") is False:
            actions.append(_candidate("open_chest"))
        if isinstance(block, dict) and block.get("can_proceed"):
            actions.append(_candidate("proceed"))
        return actions
    if state_type == "card_select":
        block = state.get("card_select")
        actions = [
            _candidate("select_card", card, index=card.get("index"))
            for card in _items(block, "cards")
            if not (isinstance(block, dict) and block.get("preview_showing"))
        ]
        if isinstance(block, dict) and block.get("can_confirm"):
            actions.append(_candidate("confirm_selection"))
        if isinstance(block, dict) and block.get("can_cancel"):
            actions.append(_candidate("cancel_selection"))
        if isinstance(block, dict) and block.get("can_skip"):
            actions.append(_candidate("skip_card_selection"))
        return actions
    if state_type == "bundle_select":
        block = state.get("bundle_select")
        actions = [
            _candidate("select_bundle", bundle, index=bundle.get("index"))
            for bundle in _items(block, "bundles")
        ]
        if isinstance(block, dict) and block.get("can_confirm"):
            actions.append(_candidate("confirm_bundle_selection"))
        if isinstance(block, dict) and block.get("can_cancel"):
            actions.append(_candidate("cancel_bundle_selection"))
        return actions
    if state_type == "relic_select":
        block = state.get("relic_select")
        actions = [
            _candidate("select_relic", relic, index=relic.get("index"))
            for relic in _items(block, "relics")
        ]
        if isinstance(block, dict) and block.get("can_skip"):
            actions.append(_candidate("skip_relic_selection"))
        return actions
    if state_type == "crystal_sphere":
        block = state.get("crystal_sphere")
        actions = [
            _candidate(
                "crystal_sphere_click_cell", cell, x=cell.get("x"), y=cell.get("y")
            )
            for cell in _items(block, "clickable_cells")
        ]
        if isinstance(block, dict) and block.get("can_proceed"):
            actions.append(_candidate("crystal_sphere_proceed"))
        return actions
    return []


def audit_action_space(state: dict[str, Any] | None) -> dict[str, Any]:
    """Describe action-set coverage without treating transition frames as legal moves."""
    state_type = state.get("state_type") if isinstance(state, dict) else None
    actions = derive_legal_actions(state)
    issues: list[str] = []
    if state_type not in SUPPORTED_TYPES:
        issues.append(f"unsupported_state_type:{state_type}")
    elif state_type != "game_over" and not actions:
        issues.append("no_visible_action:transition_or_incomplete_snapshot")
    candidate_actions = sum(1 for action in actions if "candidate" in action)
    return {
        "state_type": state_type,
        "supported": state_type in SUPPORTED_TYPES,
        "complete": not issues,
        "coverage_scope": "snapshot_derived_only; not proof of complete game UI coverage",
        "action_count": len(actions),
        "candidate_action_count": candidate_actions,
        "issues": issues,
    }
