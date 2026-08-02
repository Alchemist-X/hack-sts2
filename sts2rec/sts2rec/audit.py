"""Coverage audit for visible candidates and derived legal actions."""

from __future__ import annotations

from collections import Counter
from pathlib import Path
from typing import Any

from .lineage import build_lineage


_SHOP_REQUIRED = {
    "card": {"card_id", "card_name", "card_description", "card_cost", "card_rarity", "price"},
    "relic": {"relic_id", "relic_name", "relic_description", "price"},
    "potion": {"potion_id", "potion_name", "potion_description", "price"},
    "card_removal": {"price"},
}


def audit_lineage(session_dir: str | Path) -> dict[str, Any]:
    trajectory = build_lineage(session_dir)
    state_types: Counter[str] = Counter()
    issue_counts: Counter[str] = Counter()
    supported = complete = 0
    decision_snapshots = 0
    shop_rooms: dict[tuple[Any, Any], dict[str, Any]] = {}

    for step in trajectory["steps"]:
        state = step.get("state_before")
        info = step.get("info", {})
        if not isinstance(state, dict):
            continue
        decision_snapshots += 1
        state_type = str(state.get("state_type", "unknown"))
        state_types[state_type] += 1
        audit = info.get("action_space_audit", {})
        supported += bool(audit.get("supported"))
        complete += bool(audit.get("complete"))
        issue_counts.update(audit.get("issues", []))

        if state_type != "shop" or not isinstance(state.get("shop"), dict):
            continue
        run = state.get("run") if isinstance(state.get("run"), dict) else {}
        key = (run.get("act"), run.get("floor"))
        room = shop_rooms.setdefault(
            key,
            {"act": key[0], "floor": key[1], "max_slots": 0, "stocked_seen": {}, "issues": []},
        )
        items = [item for item in state["shop"].get("items", []) if isinstance(item, dict)]
        room["max_slots"] = max(room["max_slots"], len(items))
        for item in items:
            if not item.get("is_stocked", False):
                continue
            index = item.get("index")
            room["stocked_seen"][index] = item

    shop_results: list[dict[str, Any]] = []
    for room in shop_rooms.values():
        issues: list[str] = []
        for index, item in sorted(room.pop("stocked_seen").items()):
            category = str(item.get("category"))
            missing = sorted(_SHOP_REQUIRED.get(category, set()) - item.keys())
            if missing:
                issues.append(f"slot_{index}:{category}:missing_{','.join(missing)}")
        room["issues"] = issues
        room["complete"] = room["max_slots"] > 0 and not issues
        shop_results.append(room)

    return {
        "meta": trajectory["meta"],
        "decision_snapshots": decision_snapshots,
        "supported_snapshots": supported,
        "complete_snapshots": complete,
        "supported_rate": round(supported / max(1, decision_snapshots), 6),
        "complete_rate": round(complete / max(1, decision_snapshots), 6),
        "state_types": dict(state_types),
        "issues": dict(issue_counts),
        "shops": sorted(shop_results, key=lambda room: (room["act"], room["floor"])),
        "all_observed_shops_complete": bool(shop_results) and all(room["complete"] for room in shop_results),
        "scope_note": (
            "All items in each currently generated shop are audited. Future shops are hidden "
            "from limited mode and belong only in the omniscient privileged channel."
        ),
    }
