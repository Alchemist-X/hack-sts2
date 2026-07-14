import json
from pathlib import Path

from sts2rec.validate import validate_session
from tests.conftest import (
    START_TIME,
    default_records,
    make_action,
    make_manifest,
    write_session,
)


def test_happy_path(session_dir: Path) -> None:
    report = validate_session(session_dir)
    assert report.errors == ()
    assert report.warnings == ()
    assert report.ok
    assert report.counts == {"states": 3, "actions": 2, "events": 3}


def test_missing_manifest(tmp_path: Path) -> None:
    report = validate_session(tmp_path)
    assert not report.ok
    assert "manifest not found" in report.errors[0]


def test_unknown_schema_version(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s", manifest=make_manifest(schema_version=99))
    report = validate_session(session)
    assert any("unsupported schema_version" in error for error in report.errors)


def test_truncated_jsonl_line(session_dir: Path) -> None:
    with (session_dir / "events.jsonl").open("a", encoding="utf-8") as handle:
        handle.write('{"seq": 9, "t": 177')
    report = validate_session(session_dir)
    assert not report.ok
    assert any("malformed JSON line" in error for error in report.errors)


def test_seq_regression(tmp_path: Path) -> None:
    records = default_records()
    records["states"][2]["seq"] = 2  # regresses after seq 4
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert any("seq regression" in error for error in report.errors)


def test_timestamp_regression_beyond_tolerance(tmp_path: Path) -> None:
    records = default_records()
    records["states"][2]["t"] = records["states"][1]["t"] - 5.0
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert any("timestamp regression" in error for error in report.errors)


def test_timestamp_jitter_within_tolerance_ok(tmp_path: Path) -> None:
    records = default_records()
    records["states"][2]["t"] = records["states"][1]["t"] - 0.01
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert not any("timestamp" in error for error in report.errors)


def test_dangling_state_seq(tmp_path: Path) -> None:
    records = default_records()
    records["actions"].append(make_action(9, 1773034395.0, state_seq=999))
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert any("state_seq=999" in error for error in report.errors)


def test_state_seq_at_or_after_action_seq(tmp_path: Path) -> None:
    """Regression: state_seq must point BEFORE the action; a state_seq that
    exists but is >= the action's own seq is an integrity error."""
    records = default_records()
    # action seq=2 pointing at snapshot seq=4, which exists but is later
    records["actions"][0] = make_action(
        2, START_TIME + 2.0, state_seq=4, card="CARD.ZAP"
    )
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert any(
        "state_seq=4 at or after its own seq" in error for error in report.errors
    )


def test_consecutive_duplicate_state_hashes(tmp_path: Path) -> None:
    records = default_records()
    records["states"][1]["hash"] = records["states"][0]["hash"]
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert any("consecutive identical state hashes" in error for error in report.errors)


def test_count_mismatch(tmp_path: Path) -> None:
    manifest = make_manifest(counts={"states": 99, "actions": 2, "events": 3})
    session = write_session(tmp_path / "s", manifest=manifest)
    report = validate_session(session)
    assert any("counts.states=99" in error for error in report.errors)


def test_missing_native_artifacts_warn_when_result_set(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s", with_native=False)
    report = validate_session(session)
    assert report.ok  # warnings only
    assert any("run_history.run" in warning for warning in report.warnings)
    assert any("replay.mcr" in warning for warning in report.warnings)


def test_no_native_warning_for_in_progress_session(tmp_path: Path) -> None:
    manifest = make_manifest(result=None, incomplete=True)
    session = write_session(tmp_path / "s", manifest=manifest, with_native=False)
    report = validate_session(session)
    assert report.ok
    assert report.warnings == ()


def test_finalized_without_result_warns(tmp_path: Path) -> None:
    manifest = make_manifest(result=None, incomplete=False)
    session = write_session(tmp_path / "s", manifest=manifest, with_native=False)
    report = validate_session(session)
    assert any("result is null but incomplete=false" in w for w in report.warnings)


def test_missing_stream_file_is_error(session_dir: Path) -> None:
    (session_dir / "actions.jsonl").unlink()
    report = validate_session(session_dir)
    assert any("actions.jsonl" in error and "not found" in error for error in report.errors)


def test_never_raises_on_garbage(tmp_path: Path) -> None:
    session = tmp_path / "s"
    session.mkdir()
    (session / "manifest.json").write_text(
        json.dumps(make_manifest()), encoding="utf-8"
    )
    (session / "states.jsonl").write_bytes(b"\x00\xff garbage")
    report = validate_session(session)  # must not raise
    assert not report.ok


def test_unknown_action_status_is_error(tmp_path: Path) -> None:
    records = default_records()
    records["actions"][0]["status"] = "pending"
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert not report.ok
    assert any("unknown" in error and "status" in error for error in report.errors)


def test_known_action_statuses_are_accepted(tmp_path: Path) -> None:
    records = default_records()
    records["actions"][0]["status"] = "cancelled"
    records["actions"][1]["status"] = "committed"
    session = write_session(tmp_path / "s", records=records)
    report = validate_session(session)
    assert report.ok
