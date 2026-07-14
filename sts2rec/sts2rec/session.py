"""Session-format (schema_version 1) primitives.

Immutable dataclasses for manifest.json and the JSONL envelope records, plus a
streaming JSONL reader. Contract: docs/design.md, "Session format".
"""

from __future__ import annotations

import gzip
import json
from collections.abc import Iterator, Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import IO, Any

from .errors import JsonlError, ManifestError, RecordError

SESSION_SCHEMA_VERSION = 1
MANIFEST_NAME = "manifest.json"
STREAM_NAMES = ("states", "actions", "events")
# Streams may be gzip-compressed at run completion (recorder >= 0.2 writes
# <stream>.jsonl.gz; `sts2rec pack` compresses older sessions). gzip was
# chosen over Brotli so both sides stay stdlib-only (docs/design.md).
COMPRESSED_SUFFIX = ".gz"


@dataclass(frozen=True, slots=True)
class GameInfo:
    version: str
    commit: str | None
    build_id: str | None
    untested: bool


@dataclass(frozen=True, slots=True)
class RunInfo:
    seed: str
    character: str
    ascension: int
    game_mode: str
    start_time: float


@dataclass(frozen=True, slots=True)
class RunResult:
    win: bool
    abandoned: bool
    end_time: float


@dataclass(frozen=True, slots=True)
class StreamCounts:
    states: int
    actions: int
    events: int

    def for_stream(self, stream: str) -> int:
        if stream not in STREAM_NAMES:
            raise ValueError(f"unknown stream name: {stream!r}")
        return int(getattr(self, stream))


@dataclass(frozen=True, slots=True)
class Manifest:
    schema_version: int
    recorder_version: str
    game: GameInfo
    platform: str
    profile: str
    run: RunInfo
    result: RunResult | None
    counts: StreamCounts
    incomplete: bool
    degraded_hooks: tuple[str, ...]
    # Resumed runs recorded across game restarts open a new session directory
    # with a -partN suffix; the manifest carries the part number (default 1).
    part: int = 1
    # "gz" when the JSONL streams were compressed at completion (or by
    # `sts2rec pack`); None/absent = plain streams (crash sessions stay plain).
    compression: str | None = None


@dataclass(frozen=True, slots=True)
class StateRecord:
    seq: int
    t: float
    trigger: str
    screen: str
    hash: str
    state: Mapping[str, Any]


@dataclass(frozen=True, slots=True)
class ActionRecord:
    seq: int
    t: float
    source: str
    action: Mapping[str, Any]
    # Seq of the latest snapshot before the action. None means the action fired
    # before any snapshot existed (recorder >= 0.1 writes JSON null; older
    # recorders wrote 0, which is normalized to None at parse time).
    state_seq: int | None
    # Optional GameAction lifecycle marker written by recorder >= 0.1:
    # "committed" (already-final decisions) | "executed" | "cancelled".
    # None for sessions recorded before the field existed.
    status: str | None = None


@dataclass(frozen=True, slots=True)
class EventRecord:
    seq: int
    t: float
    entry: str
    data: Mapping[str, Any]


def _require(mapping: Mapping[str, Any], key: str, context: str) -> Any:
    if key not in mapping:
        raise ManifestError(f"{context}: missing required key {key!r}")
    return mapping[key]


def _parse_game(raw: Any) -> GameInfo:
    if not isinstance(raw, Mapping):
        raise ManifestError("manifest 'game' must be an object")
    return GameInfo(
        version=str(_require(raw, "version", "manifest.game")),
        commit=raw.get("commit"),
        build_id=raw.get("build_id"),
        untested=bool(raw.get("untested", False)),
    )


def _parse_run(raw: Any) -> RunInfo:
    if not isinstance(raw, Mapping):
        raise ManifestError("manifest 'run' must be an object")
    return RunInfo(
        seed=str(_require(raw, "seed", "manifest.run")),
        character=str(_require(raw, "character", "manifest.run")),
        ascension=int(_require(raw, "ascension", "manifest.run")),
        game_mode=str(_require(raw, "game_mode", "manifest.run")),
        start_time=float(_require(raw, "start_time", "manifest.run")),
    )


def _parse_result(raw: Any) -> RunResult | None:
    if raw is None:
        return None
    if not isinstance(raw, Mapping):
        raise ManifestError("manifest 'result' must be an object or null")
    return RunResult(
        win=bool(_require(raw, "win", "manifest.result")),
        abandoned=bool(_require(raw, "abandoned", "manifest.result")),
        end_time=float(_require(raw, "end_time", "manifest.result")),
    )


def _parse_counts(raw: Any) -> StreamCounts:
    if not isinstance(raw, Mapping):
        raise ManifestError("manifest 'counts' must be an object")
    return StreamCounts(
        states=int(_require(raw, "states", "manifest.counts")),
        actions=int(_require(raw, "actions", "manifest.counts")),
        events=int(_require(raw, "events", "manifest.counts")),
    )


def parse_manifest(raw: Any, *, source: str = MANIFEST_NAME) -> Manifest:
    """Validate a decoded manifest.json object into a Manifest."""
    if not isinstance(raw, Mapping):
        raise ManifestError(f"{source}: top level must be a JSON object")
    schema_version = _require(raw, "schema_version", source)
    if schema_version != SESSION_SCHEMA_VERSION:
        raise ManifestError(
            f"{source}: unsupported schema_version {schema_version!r} "
            f"(this tool supports {SESSION_SCHEMA_VERSION})"
        )
    try:
        return Manifest(
            schema_version=int(schema_version),
            recorder_version=str(_require(raw, "recorder_version", source)),
            game=_parse_game(_require(raw, "game", source)),
            platform=str(_require(raw, "platform", source)),
            profile=str(_require(raw, "profile", source)),
            run=_parse_run(_require(raw, "run", source)),
            result=_parse_result(_require(raw, "result", source)),
            counts=_parse_counts(_require(raw, "counts", source)),
            incomplete=bool(_require(raw, "incomplete", source)),
            degraded_hooks=tuple(raw.get("degraded_hooks", ())),
            part=int(raw.get("part", 1)),
            compression=_parse_compression(raw.get("compression"), source),
        )
    except (TypeError, ValueError) as error:
        raise ManifestError(f"{source}: malformed field value: {error}") from error


def _parse_compression(raw: Any, source: str) -> str | None:
    if raw is None:
        return None
    if not isinstance(raw, str):
        raise ManifestError(f"{source}: 'compression' must be a string or null")
    return raw


def load_manifest(session_dir: Path) -> Manifest:
    """Load and validate <session_dir>/manifest.json."""
    path = Path(session_dir) / MANIFEST_NAME
    if not path.is_file():
        raise ManifestError(f"manifest not found: {path}")
    try:
        raw = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as error:
        raise ManifestError(f"cannot read {path}: {error}") from error
    return parse_manifest(raw, source=str(path))


def _open_jsonl_text(path: Path) -> IO[str]:
    """Text handle for a plain or gzip-compressed JSONL file."""
    if path.name.endswith(COMPRESSED_SUFFIX):
        return gzip.open(path, "rt", encoding="utf-8")
    return path.open("r", encoding="utf-8")


def _resolve_jsonl_path(path: Path) -> Path:
    """Transparent compressed twin: given foo.jsonl, fall back to foo.jsonl.gz
    when the plain file is missing (and vice versa is a no-op — a .gz path is
    used as given)."""
    if path.is_file():
        return path
    if not path.name.endswith(COMPRESSED_SUFFIX):
        twin = path.with_name(path.name + COMPRESSED_SUFFIX)
        if twin.is_file():
            return twin
    return path


def iter_jsonl(path: Path) -> Iterator[tuple[int, dict[str, Any]]]:
    """Stream (line_number, record) pairs from a JSONL file.

    Accepts plain .jsonl and gzip-compressed .jsonl.gz transparently; given a
    plain path whose file is missing, the .gz twin is read instead.
    Raises JsonlError naming the file and 1-based line on the first malformed
    line (including a truncated final line). Blank lines are skipped.
    """
    path = _resolve_jsonl_path(Path(path))
    if not path.is_file():
        raise JsonlError(f"stream file not found: {path}")
    try:
        with _open_jsonl_text(path) as handle:
            for line_no, line in enumerate(handle, start=1):
                stripped = line.strip()
                if not stripped:
                    continue
                try:
                    record = json.loads(stripped)
                except json.JSONDecodeError as error:
                    raise JsonlError(
                        f"{path}:{line_no}: malformed JSON line "
                        f"(truncated write?): {error}"
                    ) from error
                if not isinstance(record, dict):
                    raise JsonlError(
                        f"{path}:{line_no}: expected a JSON object, "
                        f"got {type(record).__name__}"
                    )
                yield line_no, record
    except (OSError, EOFError) as error:
        # gzip.BadGzipFile is an OSError; EOFError = truncated gzip stream.
        raise JsonlError(f"cannot read {path}: {error}") from error
    except UnicodeDecodeError as error:
        raise JsonlError(
            f"{path}: not valid UTF-8 (binary garbage?): {error}"
        ) from error


def _envelope(record: Mapping[str, Any], expected_type: str, where: str) -> tuple[int, float]:
    for key in ("seq", "t", "type"):
        if key not in record:
            raise RecordError(f"{where}: missing envelope key {key!r}")
    if record["type"] != expected_type:
        raise RecordError(
            f"{where}: expected type={expected_type!r}, got {record['type']!r}"
        )
    try:
        return int(record["seq"]), float(record["t"])
    except (TypeError, ValueError) as error:
        raise RecordError(f"{where}: malformed seq/t: {error}") from error


def parse_state_record(record: Mapping[str, Any], *, where: str = "state record") -> StateRecord:
    seq, t = _envelope(record, "state", where)
    for key in ("trigger", "screen", "hash", "state"):
        if key not in record:
            raise RecordError(f"{where}: missing key {key!r}")
    return StateRecord(
        seq=seq,
        t=t,
        trigger=str(record["trigger"]),
        screen=str(record["screen"]),
        hash=str(record["hash"]),
        state=record["state"],
    )


def parse_action_record(record: Mapping[str, Any], *, where: str = "action record") -> ActionRecord:
    seq, t = _envelope(record, "action", where)
    for key in ("source", "action", "state_seq"):
        if key not in record:
            raise RecordError(f"{where}: missing key {key!r}")
    source = record["source"]
    if not isinstance(source, str):
        raise RecordError(
            f"{where}: 'source' must be a string, got {type(source).__name__}"
        )
    action = record["action"]
    if not isinstance(action, Mapping) or "kind" not in action:
        raise RecordError(f"{where}: 'action' must be an object with a 'kind'")
    if not isinstance(action["kind"], str):
        raise RecordError(
            f"{where}: action 'kind' must be a string, "
            f"got {type(action['kind']).__name__}"
        )
    status = record.get("status")
    return ActionRecord(
        seq=seq,
        t=t,
        source=source,
        action=action,
        state_seq=_parse_state_seq(record["state_seq"], where),
        status=str(status) if status is not None else None,
    )


def _parse_state_seq(raw: Any, where: str) -> int | None:
    """state_seq contract: null (or legacy 0) = no snapshot existed yet."""
    if raw is None:
        return None
    try:
        value = int(raw)
    except (TypeError, ValueError) as error:
        raise RecordError(f"{where}: malformed state_seq: {error}") from error
    return value if value != 0 else None


def parse_event_record(record: Mapping[str, Any], *, where: str = "event record") -> EventRecord:
    seq, t = _envelope(record, "event", where)
    for key in ("entry", "data"):
        if key not in record:
            raise RecordError(f"{where}: missing key {key!r}")
    entry = record["entry"]
    if not isinstance(entry, str):
        raise RecordError(
            f"{where}: 'entry' must be a string, got {type(entry).__name__}"
        )
    return EventRecord(seq=seq, t=t, entry=entry, data=record["data"])


def stream_path(session_dir: Path, stream: str) -> Path:
    """Path of the plain stream file (states/actions/events) in a session dir."""
    if stream not in STREAM_NAMES:
        raise ValueError(f"unknown stream name: {stream!r}")
    return Path(session_dir) / f"{stream}.jsonl"


def resolve_stream_path(session_dir: Path, stream: str) -> Path:
    """Actual on-disk stream file: prefers <stream>.jsonl.gz when it exists
    (the compressed copy is the verified, authoritative one), else the plain
    <stream>.jsonl path (which may or may not exist)."""
    plain = stream_path(session_dir, stream)
    compressed = plain.with_name(plain.name + COMPRESSED_SUFFIX)
    if compressed.is_file():
        return compressed
    return plain
