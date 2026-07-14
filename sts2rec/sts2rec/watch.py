"""Tail a live session directory.

Polls the three JSONL streams by byte offset and yields each newly completed
record as a plain dict. No threads, no inotify — a simple generator suitable
for `sts2rec watch` and for tests. Partial trailing lines (a write in
progress) are left in the buffer until their newline arrives.
"""

from __future__ import annotations

import json
import time
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .session import STREAM_NAMES, stream_path

DEFAULT_POLL_INTERVAL_SECONDS = 0.5


@dataclass(frozen=True, slots=True)
class WatchRecord:
    stream: str
    record: dict[str, Any]


def _drain_new_lines(path: Path, offset: int) -> tuple[list[str], int]:
    """Complete new lines after byte `offset`, and the new offset."""
    try:
        with path.open("rb") as handle:
            handle.seek(offset)
            chunk = handle.read()
    except OSError:
        return [], offset
    if not chunk:
        return [], offset
    end = chunk.rfind(b"\n")
    if end < 0:
        return [], offset  # only a partial line so far
    complete = chunk[: end + 1]
    lines = [
        line.decode("utf-8", errors="replace")
        for line in complete.splitlines()
        if line.strip()
    ]
    return lines, offset + len(complete)


def watch_session(
    session_dir: Path | str,
    *,
    poll_interval: float = DEFAULT_POLL_INTERVAL_SECONDS,
    idle_timeout: float | None = None,
    should_stop: Callable[[], bool] | None = None,
    sleep: Callable[[float], None] = time.sleep,
) -> Iterator[WatchRecord]:
    """Yield new stream records as they are appended.

    Stops when `should_stop()` returns true, or after `idle_timeout` seconds
    without any new record (None = watch forever). Malformed lines are
    skipped (a live writer is allowed to be mid-flight) and retried never —
    use `sts2rec validate` on the finished session for integrity checks.
    """
    session_dir = Path(session_dir)
    offsets = {stream: 0 for stream in STREAM_NAMES}
    last_progress = time.monotonic()
    while True:
        if should_stop is not None and should_stop():
            return
        got_any = False
        for stream in STREAM_NAMES:
            path = stream_path(session_dir, stream)
            if not path.is_file():
                continue
            lines, offsets[stream] = _drain_new_lines(path, offsets[stream])
            for line in lines:
                try:
                    record = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if isinstance(record, dict):
                    got_any = True
                    yield WatchRecord(stream=stream, record=record)
        now = time.monotonic()
        if got_any:
            last_progress = now
        elif idle_timeout is not None and now - last_progress >= idle_timeout:
            return
        sleep(poll_interval)
