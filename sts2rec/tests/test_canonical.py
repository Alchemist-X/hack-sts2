import json
from pathlib import Path

import pytest

from sts2rec.canonical import build_canonical
from sts2rec.errors import CanonicalError
from tests.conftest import (
    START_TIME,
    default_records,
    make_action,
    make_event,
    make_manifest,
    make_state,
    write_session,
)


def test_meta_fields(session_dir: Path) -> None:
    trajectory = build_canonical(session_dir)
    meta = trajectory["meta"]
    assert meta["source"] == "sts2-real-client"
    assert meta["seed"] == "TESTSEED12"
    assert meta["game_version"] == "0.108.0"
    assert meta["run_id"] == session_dir.name
    assert meta["incomplete"] is False
    assert "reward" in meta["reward_note"]


def test_one_step_per_action(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    assert len(steps) == 2
    assert [step["step_idx"] for step in steps] == [0, 1]


def test_state_before_and_after_linking(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    # action seq=2 references state seq=1; next snapshot is seq=4
    assert steps[0]["state_before"]["floor"] == 1
    assert steps[0]["info"]["state_before_seq"] == 1
    assert steps[0]["info"]["state_after_seq"] == 4
    assert steps[0]["state_after"]["energy"] == 2
    # action seq=5 references state seq=4; next snapshot is seq=7
    assert steps[1]["info"]["state_before_seq"] == 4
    assert steps[1]["info"]["state_after_seq"] == 7


def test_action_shape(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    assert steps[0]["action"] == {"type": "play_card", "params": {"card": "CARD.ZAP"}}
    assert steps[1]["action"] == {"type": "end_turn", "params": {}}
    assert steps[0]["info"]["action_source"] == "hook:PlayCardPatch"


def test_events_partition_by_action_boundaries(session_dir: Path) -> None:
    trajectory = build_canonical(session_dir)
    steps = trajectory["steps"]
    # no events before the first action (seq=2)
    assert trajectory["prelude_events"] == []
    # event seq=3 falls between actions 2 and 5 -> step 0
    assert [event["seq"] for event in steps[0]["info"]["events"]] == [3]
    # last step: events after action seq=5, unbounded (seq 6 and trailing 8)
    assert [event["seq"] for event in steps[1]["info"]["events"]] == [6, 8]


def test_reward_is_none_everywhere(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    assert all(step["reward"] is None for step in steps)


def test_terminal_from_manifest_result(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    assert [step["terminal"] for step in steps] == [False, True]
    assert steps[-1]["info"]["result"] == {
        "win": True,
        "abandoned": False,
        "end_time": 1773034486.0,
    }


def test_no_terminal_when_result_null(tmp_path: Path) -> None:
    records = default_records()
    manifest = make_manifest(
        result=None,
        incomplete=True,
        counts={stream: len(lines) for stream, lines in records.items()},
    )
    session = write_session(tmp_path / "s", manifest=manifest, records=records)
    trajectory = build_canonical(session)
    steps = trajectory["steps"]
    assert all(step["terminal"] is False for step in steps)
    assert "result" not in steps[-1]["info"]
    assert trajectory["meta"]["result"] is None


def test_dangling_state_seq_raises(tmp_path: Path) -> None:
    records = default_records()
    records["actions"].append(make_action(9, 1773034395.0, state_seq=999))
    session = write_session(tmp_path / "s", records=records)
    with pytest.raises(CanonicalError, match="state_seq=999"):
        build_canonical(session)


def test_corrupt_stream_raises_canonical_error(session_dir: Path) -> None:
    with (session_dir / "states.jsonl").open("a", encoding="utf-8") as handle:
        handle.write("{truncated")
    with pytest.raises(CanonicalError, match="malformed JSON line"):
        build_canonical(session_dir)


def test_deterministic_output(session_dir: Path) -> None:
    first = json.dumps(build_canonical(session_dir), sort_keys=True)
    second = json.dumps(build_canonical(session_dir), sort_keys=True)
    assert first == second


def test_last_action_without_following_snapshot(tmp_path: Path) -> None:
    records = default_records()
    records["states"] = records["states"][:2]  # drop snapshot seq=7
    session = write_session(tmp_path / "s", records=records)
    steps = build_canonical(session)["steps"]
    assert steps[1]["state_after"] is None
    assert steps[1]["info"]["state_after_seq"] is None


def test_no_duplicate_events_when_actions_share_state_seq(tmp_path: Path) -> None:
    """Regression: throttled/deduped snapshots make consecutive actions share
    a state_seq; each event must still land on exactly one step."""
    t = START_TIME
    records = {
        "states": [make_state(1, t + 1.0, "h1", floor=1), make_state(6, t + 6.0, "h2", floor=1)],
        "actions": [
            make_action(2, t + 2.0, 1, card="CARD.ZAP"),
            make_action(4, t + 4.0, 1, kind="end_turn"),
        ],
        "events": [
            make_event(3, t + 3.0, "damage_received", amount=5),
            make_event(5, t + 5.0, "turn_ended", turn=1),
        ],
    }
    session = write_session(tmp_path / "s", records=records)
    trajectory = build_canonical(session)
    steps = trajectory["steps"]
    assert [event["seq"] for event in steps[0]["info"]["events"]] == [3]
    assert [event["seq"] for event in steps[1]["info"]["events"]] == [5]
    all_seqs = [
        event["seq"]
        for step in steps
        for event in step["info"]["events"]
    ] + [event["seq"] for event in trajectory["prelude_events"]]
    assert sorted(all_seqs) == [3, 5]  # each event exactly once


def test_event_between_state_windows_not_dropped(tmp_path: Path) -> None:
    """Regression: event seq=5 sits between step 0's state_after (seq=4) and
    step 1's state_before (seq=6); it must attach to step 0, not vanish."""
    t = START_TIME
    records = {
        "states": [
            make_state(1, t + 1.0, "h1", floor=1),
            make_state(4, t + 4.0, "h2", floor=1),
            make_state(6, t + 6.0, "h3", floor=1),
        ],
        "actions": [
            make_action(2, t + 2.0, 1, card="CARD.ZAP"),
            make_action(7, t + 7.0, 6, kind="end_turn"),
        ],
        "events": [
            make_event(3, t + 3.0, "card_drawn", card="CARD.STRIKE_DEFECT"),
            make_event(5, t + 5.0, "damage_received", amount=5),
            make_event(8, t + 8.0, "turn_ended", turn=1),
        ],
    }
    session = write_session(tmp_path / "s", records=records)
    steps = build_canonical(session)["steps"]
    assert [event["seq"] for event in steps[0]["info"]["events"]] == [3, 5]
    assert [event["seq"] for event in steps[1]["info"]["events"]] == [8]


def test_prelude_events_before_first_action(tmp_path: Path) -> None:
    """Events before the first hooked action (e.g. combat-start draws) go to
    the top-level prelude_events list."""
    t = START_TIME
    records = {
        "states": [make_state(1, t + 1.0, "h1", floor=1)],
        "actions": [make_action(3, t + 3.0, 1, card="CARD.ZAP")],
        "events": [
            make_event(2, t + 2.0, "card_drawn", card="CARD.STRIKE_DEFECT"),
            make_event(4, t + 4.0, "damage_received", amount=5),
        ],
    }
    session = write_session(tmp_path / "s", records=records)
    trajectory = build_canonical(session)
    assert [event["seq"] for event in trajectory["prelude_events"]] == [2]
    assert [
        event["seq"] for event in trajectory["steps"][0]["info"]["events"]
    ] == [4]


def test_meta_result_mirrors_manifest(session_dir: Path) -> None:
    meta = build_canonical(session_dir)["meta"]
    assert meta["result"] == {
        "win": True,
        "abandoned": False,
        "end_time": 1773034486.0,
    }


def test_zero_action_session_keeps_result(tmp_path: Path) -> None:
    """Regression: a session with a result but no hooked actions must keep
    its outcome in meta.result (steps is empty, events land in prelude)."""
    records = {
        "states": [],
        "actions": [],
        "events": [make_event(1, START_TIME + 1.0, "turn_ended", turn=1)],
    }
    session = write_session(tmp_path / "s", records=records)
    trajectory = build_canonical(session)
    assert trajectory["steps"] == []
    assert trajectory["meta"]["result"] == {
        "win": True,
        "abandoned": False,
        "end_time": 1773034486.0,
    }
    assert [event["seq"] for event in trajectory["prelude_events"]] == [1]


def test_nested_params_object_is_unwrapped(tmp_path: Path) -> None:
    """Recorder >= 0.1 writes action:{kind, params:{...}}; canonical must not
    double-nest ({"params": {"params": ...}})."""
    t = START_TIME
    records = {
        "states": [make_state(1, t + 1.0, "h1", floor=1)],
        "actions": [
            {
                "seq": 2,
                "t": t + 2.0,
                "type": "action",
                "source": "hook:ActionQueuePatch",
                "action": {"kind": "play_card", "params": {"card": "CARD.ZAP"}},
                "status": "executed",
                "state_seq": 1,
            }
        ],
        "events": [],
    }
    session = write_session(tmp_path / "s", records=records)
    steps = build_canonical(session)["steps"]
    assert steps[0]["action"] == {"type": "play_card", "params": {"card": "CARD.ZAP"}}
    assert steps[0]["info"]["action_status"] == "executed"


def test_action_status_absent_from_info_when_not_recorded(session_dir: Path) -> None:
    steps = build_canonical(session_dir)["steps"]
    assert all("action_status" not in step["info"] for step in steps)


def test_meta_carries_part(tmp_path: Path) -> None:
    records = default_records()
    manifest = make_manifest(
        part=2,
        counts={stream: len(lines) for stream, lines in records.items()},
    )
    session = write_session(tmp_path / "s-part2", manifest=manifest, records=records)
    meta = build_canonical(session)["meta"]
    assert meta["part"] == 2


def test_meta_part_defaults_to_one(session_dir: Path) -> None:
    assert build_canonical(session_dir)["meta"]["part"] == 1
