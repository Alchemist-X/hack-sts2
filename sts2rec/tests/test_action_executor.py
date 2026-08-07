from __future__ import annotations

import importlib.util
import inspect
import json
from pathlib import Path


SCRIPT = Path(__file__).resolve().parents[2] / "scripts/sts2_action_executor.py"
SPEC = importlib.util.spec_from_file_location("sts2_action_executor", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def _combat_state(*, card_ids: list[str], energy: int = 3) -> dict:
    return {
        "state_type": "monster",
        "battle": {
            "round": 1,
            "turn": "player",
            "is_play_phase": True,
            "enemies": [{"entity_id": "ENEMY_0", "hp": 30}],
        },
        "player": {
            "energy": energy,
            "hand": [
                {
                    "id": card_id,
                    "index": index,
                    "description": "造成6点伤害。",
                    "keywords": [],
                }
                for index, card_id in enumerate(card_ids)
            ],
            "discard_pile_count": 0,
            "exhaust_pile_count": 0,
        },
    }


def test_executor_exposes_no_gameplay_decision_policy() -> None:
    assert not hasattr(MODULE, "decide")
    assert "quiet_window" in inspect.signature(MODULE.settled_state).parameters
    assert (
        inspect.signature(MODULE.settled_state)
        .parameters["quiet_window"]
        .default
        == 2.5
    )


def test_play_card_commit_requires_the_selected_card_to_move() -> None:
    before = _combat_state(card_ids=["BLIGHT_STRIKE", "SCOURGE"])
    unrelated_change = _combat_state(card_ids=["BLIGHT_STRIKE", "SCOURGE"], energy=2)
    unrelated_change["battle"]["enemies"][0]["hp"] = 20
    committed = _combat_state(card_ids=["SCOURGE"], energy=2)
    action = {"action": "play_card", "card_index": 0, "target": "ENEMY_0"}

    assert MODULE._play_card_committed(before, unrelated_change, action) is False
    assert MODULE._play_card_committed(before, committed, action) is True


def test_append_jsonl_writes_linkable_audit_events(tmp_path: Path) -> None:
    path = tmp_path / "decisions.jsonl"
    MODULE.append_jsonl(path, {"type": "decision_intent", "decision_id": "d1"})
    MODULE.append_jsonl(path, {"type": "decision_outcome", "decision_id": "d1"})

    rows = [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines()]
    assert [row["type"] for row in rows] == [
        "decision_intent",
        "decision_outcome",
    ]
    assert {row["decision_id"] for row in rows} == {"d1"}
