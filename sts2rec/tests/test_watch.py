import json
from pathlib import Path

from sts2rec.watch import watch_session
from tests.conftest import make_event, write_session


def _append(path: Path, record: dict) -> None:
    with path.open("a", encoding="utf-8") as handle:
        handle.write(json.dumps(record) + "\n")


def test_yields_existing_records_then_stops_on_idle(session_dir: Path) -> None:
    records = list(watch_session(session_dir, poll_interval=0.0, idle_timeout=0.0))
    assert len(records) == 8  # 3 states + 2 actions + 3 events
    streams = {item.stream for item in records}
    assert streams == {"states", "actions", "events"}


def test_picks_up_appended_records(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s")
    appended = {"done": False}

    def sleep(_: float) -> None:
        if not appended["done"]:
            _append(session / "events.jsonl", make_event(9, 1773034396.0, "late"))
            appended["done"] = True

    seen: list[str] = []
    for item in watch_session(
        session, poll_interval=0.0, idle_timeout=0.0, sleep=sleep
    ):
        if item.record.get("entry") == "late":
            seen.append(item.stream)
    assert seen == ["events"]


def test_partial_line_not_yielded_until_complete(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s")
    path = session / "states.jsonl"
    with path.open("a", encoding="utf-8") as handle:
        handle.write('{"seq": 10, "t": 1')  # no newline: write in flight
    records = list(watch_session(session, poll_interval=0.0, idle_timeout=0.0))
    assert all(item.record.get("seq") != 10 for item in records)


def test_malformed_complete_line_skipped(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s")
    with (session / "events.jsonl").open("a", encoding="utf-8") as handle:
        handle.write("{broken}\n")
    records = list(watch_session(session, poll_interval=0.0, idle_timeout=0.0))
    assert len(records) == 8  # broken line silently skipped


def test_should_stop_halts_immediately(session_dir: Path) -> None:
    records = list(
        watch_session(session_dir, poll_interval=0.0, should_stop=lambda: True)
    )
    assert records == []


def test_missing_stream_files_tolerated(tmp_path: Path) -> None:
    session = write_session(tmp_path / "s")
    (session / "actions.jsonl").unlink()
    records = list(watch_session(session, poll_interval=0.0, idle_timeout=0.0))
    assert len(records) == 6  # states + events only
