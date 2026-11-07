"""Optional, disposable SQLite index of *raw* records, outside session directories.

First read validates every record and indexes it atomically. Warm reads decode
only requested actions, their EXACT referenced states, next states and events.
Candidate arrays are never trimmed. Derived decisions/audits are not cached.
Active/incomplete sessions use a temporary index, never a reusable one.
"""
from __future__ import annotations

import hashlib
import json
import os
import sqlite3
import tempfile
from collections import OrderedDict
from pathlib import Path
from typing import Any, Iterator

from .canonical import _build_meta, _discard_torn_final, _event_payload, _resolved_step
from .errors import CanonicalError, JsonlError, RecordError
from .session import (STREAM_NAMES, iter_jsonl, load_manifest, parse_action_record,
                      parse_event_record, parse_state_record, resolve_stream_path)

INDEX_VERSION = 1
PARSERS = {"states": parse_state_record, "actions": parse_action_record, "events": parse_event_record}


def _fingerprint(session: Path) -> str:
    paths = [session / "manifest.json", *(resolve_stream_path(session, s) for s in STREAM_NAMES)]
    entries = []
    for path in paths:
        stat = path.stat()
        entries.append((str(path), stat.st_dev, stat.st_ino, stat.st_size,
                        stat.st_mtime_ns, stat.st_ctime_ns))
    return json.dumps([INDEX_VERSION, entries], separators=(",", ":"))


def _connect(path: Path) -> sqlite3.Connection:
    connection = sqlite3.connect(path.as_uri() + "?mode=ro", uri=True)
    connection.execute("PRAGMA query_only=ON")
    connection.execute("PRAGMA cache_size=-2048")  # bounded page cache, KiB
    return connection


class IndexedCanonicalReader:
    """Context-managed iterator. Retains at most 8 decoded states plus one step.

    Iteration must finish (or call close) to check source stability. A concurrent
    source change raises; callers must discard partial output and retry. Like
    the scan reader, inputs are treated as immutable while a read is in progress.
    """

    def __init__(self, session_dir: str | Path, cache_dir: str | Path):
        self.session = Path(session_dir).resolve()
        self.connection: sqlite3.Connection | None = None
        self._states: OrderedDict[int, Any] = OrderedDict()
        self._temporary = False
        self._closed = False
        self.cache_hit = False
        try:
            self.fingerprint = _fingerprint(self.session)
            self.manifest = load_manifest(self.session)
            cache = Path(cache_dir).resolve()
            if cache == self.session or self.session in cache.parents:
                raise CanonicalError("cache directory must be outside the recorded session")
            cache.mkdir(parents=True, exist_ok=True)
            key = hashlib.sha256(str(self.session).encode()).hexdigest()
            self.path = cache / (key + ".sqlite3")
            if self.manifest.incomplete:
                fd, name = tempfile.mkstemp(prefix="active-", suffix=".sqlite3", dir=cache)
                os.close(fd)
                self.path = Path(name)
                self._temporary = True
            if not self._temporary and self.path.is_file():
                try:
                    self.connection = _connect(self.path)
                    meta = dict(self.connection.execute("SELECT key, value FROM meta"))
                    self.cache_hit = meta.get("fingerprint") == self.fingerprint
                    if self.cache_hit:
                        # Detect truncated/schema-corrupt indexes before yielding output.
                        self.connection.execute("SELECT seq FROM records LIMIT 1").fetchone()
                except sqlite3.DatabaseError:
                    self.cache_hit = False
                if not self.cache_hit and self.connection is not None:
                    self.connection.close()
                    self.connection = None
            if not self.cache_hit:
                self._build()
                self.connection = _connect(self.path)
            self._check_stable()
            self.meta = _build_meta(self.manifest, self.session)
            self.action_count = self.connection.execute(
                "SELECT count(*) FROM records WHERE stream='actions'").fetchone()[0]
        except (OSError, sqlite3.Error, JsonlError, RecordError) as error:
            self._cleanup()
            raise CanonicalError(f"cannot open session index: {error}") from error
        except BaseException:
            self._cleanup()
            raise

    def _build(self) -> None:
        fd, name = tempfile.mkstemp(prefix="building-", suffix=".sqlite3", dir=self.path.parent)
        os.close(fd)
        temporary = Path(name)
        connection = sqlite3.connect(temporary)
        try:
            connection.executescript("""
                CREATE TABLE meta(key TEXT PRIMARY KEY, value TEXT NOT NULL);
                CREATE TABLE records(stream TEXT, seq INTEGER, ordinal INTEGER, payload TEXT NOT NULL,
                    PRIMARY KEY(stream, seq)) WITHOUT ROWID;
            """)
            for stream, parser in PARSERS.items():
                path = resolve_stream_path(self.session, stream)
                for line, raw in iter_jsonl(path, on_torn_final=(
                        _discard_torn_final if self.manifest.incomplete else None)):
                    record = parser(raw, where=f"{path.name}:{line}")
                    connection.execute("INSERT INTO records VALUES (?, ?, NULL, ?)",
                                       (stream, record.seq, json.dumps(raw, ensure_ascii=False, separators=(",", ":"))))
            # Sort by global seq even for historical files that weren't ordered.
            # Keep ordinals to preserve step_idx and terminal on filtered reads.
            cursor = connection.execute("SELECT seq FROM records WHERE stream='actions' ORDER BY seq")
            for ordinal, (seq,) in enumerate(cursor):
                connection.execute("UPDATE records SET ordinal=? WHERE stream='actions' AND seq=?",
                                   (ordinal, seq))
            self._check_stable()
            connection.execute("INSERT INTO meta VALUES ('fingerprint', ?)", (self.fingerprint,))
            connection.commit()
            connection.close()
            os.replace(temporary, self.path)
        finally:
            connection.close()
            temporary.unlink(missing_ok=True)

    def _check_stable(self) -> None:
        if _fingerprint(self.session) != self.fingerprint:
            raise CanonicalError("session changed during indexed read; discard partial output and retry")

    def _state(self, seq: int | None):
        if seq is None:
            return None
        if seq in self._states:
            self._states.move_to_end(seq)
            return self._states[seq]
        row = self.connection.execute(
            "SELECT payload FROM records WHERE stream='states' AND seq=?", (seq,)).fetchone()
        result = parse_state_record(json.loads(row[0])) if row else None
        self._states[seq] = result
        if len(self._states) > 8:
            self._states.popitem(last=False)
        return result

    def _events(self, start: int | None, stop: int | None) -> list[dict[str, Any]]:
        conditions = ["stream='events'"]
        params = []
        if start is not None:
            conditions.append("seq>?"); params.append(start)
        if stop is not None:
            conditions.append("seq<?"); params.append(stop)
        return [_event_payload(parse_event_record(json.loads(row[0]))) for row in
                self.connection.execute("SELECT payload FROM records WHERE " +
                                        " AND ".join(conditions) + " ORDER BY seq", params)]

    @property
    def prelude_events(self) -> list[dict[str, Any]]:
        first = self.connection.execute("SELECT min(seq) FROM records WHERE stream='actions'").fetchone()[0]
        return self._events(None, first)

    def steps(self, action_seqs: set[int] | None = None) -> Iterator[dict[str, Any]]:
        try:
            if action_seqs is None:
                rows = self.connection.execute(
                    "SELECT ordinal, payload FROM records WHERE stream='actions' ORDER BY seq")
            else:
                # Individual indexed probes avoid SQLite's parameter limit and
                # never scan/decode all actions for a small selection.
                rows = (row for seq in sorted(action_seqs) for row in self.connection.execute(
                    "SELECT ordinal, payload FROM records WHERE stream='actions' AND seq=?", (seq,)))
            for ordinal, raw in rows:
                action = parse_action_record(json.loads(raw))
                after = self.connection.execute(
                    "SELECT seq FROM records WHERE stream='states' AND seq>? ORDER BY seq LIMIT 1",
                    (action.seq,)).fetchone()
                next_action = self.connection.execute(
                    "SELECT seq FROM records WHERE stream='actions' AND seq>? ORDER BY seq LIMIT 1",
                    (action.seq,)).fetchone()
                yield _resolved_step(ordinal, action, self._state(action.state_seq),
                                     self._state(after[0] if after else None),
                                     self._events(action.seq, next_action[0] if next_action else None),
                                     ordinal == self.action_count - 1, self.manifest)
            self._check_stable()
        except (sqlite3.Error, json.JSONDecodeError, RecordError) as error:
            # Corrupt cache is disposable. Do not return a partially mixed read;
            # next invocation rebuilds from source and audits it again.
            self.path.unlink(missing_ok=True)
            raise CanonicalError(f"invalid session index; retry to rebuild: {error}") from error

    def _cleanup(self) -> None:
        if self.connection is not None:
            self.connection.close()
        self._states.clear()
        if self._temporary:
            self.path.unlink(missing_ok=True)

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        try:
            self._check_stable()
        finally:
            self._cleanup()

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, tb):
        if exc_type is None:
            self.close()
        else:
            self._cleanup()
        return False
