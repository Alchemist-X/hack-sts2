"""Tail a live session directory.

Polls the three JSONL streams by byte offset and yields each newly completed
record as a plain dict. No threads, no inotify — a simple generator suitable
for `sts2rec watch` and for tests. Partial trailing lines (a write in
progress) are left in the buffer until their newline arrives.

Compressed sessions: when a stream exists only as <stream>.jsonl.gz (the
recorder compresses at run completion, `sts2rec pack` compresses later), the
compressed file is immutable, so it is read exactly once. If a live session
completes mid-watch (plain file replaced by .gz), records already emitted
from the plain file are skipped by count — no duplicates.
"""

from __future__ import annotations

import json
import time
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from .errors import JsonlError
from .session import COMPRESSED_SUFFIX, STREAM_NAMES, iter_jsonl, stream_path

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


def _drain_compressed(path: Path, skip: int) -> list[dict[str, Any]]:
    """All records of an immutable .gz stream after the first `skip` ones."""
    records: list[dict[str, Any]] = []
    try:
        for index, (_line_no, record) in enumerate(iter_jsonl(path)):
            if index >= skip:
                records.append(record)
    except JsonlError:
        # A live watcher tolerates unreadable data (same policy as malformed
        # plain lines); `sts2rec validate` is the integrity tool.
        pass
    return records


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
    Reads .jsonl.gz streams transparently (see module docstring).
    """
    session_dir = Path(session_dir)
    offsets = {stream: 0 for stream in STREAM_NAMES}
    emitted = {stream: 0 for stream in STREAM_NAMES}
    compressed_done: set[str] = set()
    last_progress = time.monotonic()
    while True:
        if should_stop is not None and should_stop():
            return
        got_any = False
        for stream in STREAM_NAMES:
            plain = stream_path(session_dir, stream)
            if plain.is_file():
                lines, offsets[stream] = _drain_new_lines(plain, offsets[stream])
                for line in lines:
                    emitted[stream] += 1
                    try:
                        record = json.loads(line)
                    except json.JSONDecodeError:
                        continue
                    if isinstance(record, dict):
                        got_any = True
                        yield WatchRecord(stream=stream, record=record)
                continue
            if stream in compressed_done:
                continue
            compressed = plain.with_name(plain.name + COMPRESSED_SUFFIX)
            if not compressed.is_file():
                continue
            # The .gz appears exactly once (at completion) and never grows:
            # emit everything past what the plain-file phase already yielded.
            compressed_done.add(stream)
            for record in _drain_compressed(compressed, skip=emitted[stream]):
                emitted[stream] += 1
                if isinstance(record, dict):
                    got_any = True
                    yield WatchRecord(stream=stream, record=record)
        now = time.monotonic()
        if got_any:
            last_progress = now
        elif idle_timeout is not None and now - last_progress >= idle_timeout:
            return
        sleep(poll_interval)
