"""Session validation: structural integrity checks that never raise on bad data.

Errors  = data-integrity violations (corrupt line, seq regression, dangling
          or future-pointing state_seq, consecutive duplicate state hashes,
          count mismatches, timestamp regression beyond tolerance, bad
          manifest).
Warnings = recoverable gaps (missing native artifacts — the `archive`
          subcommand exists for exactly that — and manifest inconsistencies
          such as result set while incomplete=true).
"""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Mapping

from .errors import JsonlError, ManifestError, RecordError
from .session import (
    Manifest,
    iter_jsonl,
    load_manifest,
    parse_action_record,
    parse_event_record,
    parse_state_record,
    stream_path,
)

TIMESTAMP_TOLERANCE_SECONDS = 0.05

_PARSERS: dict[str, Callable[..., Any]] = {
    "states": parse_state_record,
    "actions": parse_action_record,
    "events": parse_event_record,
}


@dataclass(frozen=True, slots=True)
class ValidationReport:
    session_dir: str
    errors: tuple[str, ...]
    warnings: tuple[str, ...]
    counts: Mapping[str, int]

    @property
    def ok(self) -> bool:
        return not self.errors


@dataclass(frozen=True, slots=True)
class _StreamResult:
    records: tuple[Any, ...]
    line_count: int
    errors: tuple[str, ...]


def _read_stream(session_dir: Path, stream: str) -> _StreamResult:
    """Parse one stream, collecting per-line integrity errors."""
    path = stream_path(session_dir, stream)
    parser = _PARSERS[stream]
    records: list[Any] = []
    errors: list[str] = []
    line_count = 0
    prev_seq: int | None = None
    prev_t: float | None = None
    try:
        for line_no, raw in iter_jsonl(path):
            line_count += 1
            where = f"{path.name}:{line_no}"
            try:
                record = parser(raw, where=where)
            except RecordError as error:
                errors.append(str(error))
                continue
            if prev_seq is not None and record.seq <= prev_seq:
                errors.append(
                    f"{where}: seq regression: {record.seq} after {prev_seq} "
                    "(must be strictly increasing per stream)"
                )
            if (
                prev_t is not None
                and record.t < prev_t - TIMESTAMP_TOLERANCE_SECONDS
            ):
                errors.append(
                    f"{where}: timestamp regression: t={record.t} after "
                    f"t={prev_t} (tolerance {TIMESTAMP_TOLERANCE_SECONDS}s)"
                )
            prev_seq = record.seq
            prev_t = max(prev_t, record.t) if prev_t is not None else record.t
            records.append(record)
    except JsonlError as error:
        errors.append(str(error))
    return _StreamResult(
        records=tuple(records), line_count=line_count, errors=tuple(errors)
    )


def _cross_stream_errors(
    states: _StreamResult, actions: _StreamResult
) -> tuple[str, ...]:
    errors: list[str] = []
    state_seqs = frozenset(record.seq for record in states.records)
    for action in actions.records:
        if action.state_seq not in state_seqs:
            errors.append(
                f"actions.jsonl: action seq={action.seq} references "
                f"state_seq={action.state_seq}, which does not exist in "
                "states.jsonl"
            )
        elif action.state_seq >= action.seq:
            errors.append(
                f"actions.jsonl: action seq={action.seq} references "
                f"state_seq={action.state_seq} at or after its own seq "
                "(state_seq must be the latest snapshot before the action)"
            )
    for previous, current in zip(states.records, states.records[1:]):
        if previous.hash == current.hash:
            errors.append(
                f"states.jsonl: consecutive identical state hashes at "
                f"seq={previous.seq} and seq={current.seq} "
                f"(hash={current.hash!r}); recorder must hash-dedup"
            )
    return tuple(errors)


def _count_errors(
    manifest: Manifest, results: Mapping[str, _StreamResult]
) -> tuple[str, ...]:
    errors: list[str] = []
    for stream, result in results.items():
        expected = manifest.counts.for_stream(stream)
        if expected != result.line_count:
            errors.append(
                f"manifest counts.{stream}={expected} but {stream}.jsonl "
                f"has {result.line_count} lines"
            )
    return tuple(errors)


def _native_warnings(session_dir: Path, manifest: Manifest) -> tuple[str, ...]:
    warnings: list[str] = []
    if manifest.result is not None:
        native = session_dir / "native"
        for name in ("run_history.run", "replay.mcr"):
            if not (native / name).is_file():
                warnings.append(
                    f"manifest.result is set but native/{name} is missing "
                    "(run `sts2rec archive` to copy it from the game)"
                )
        if manifest.incomplete:
            warnings.append(
                "manifest.result is set but incomplete=true "
                "(manifest was not finalized cleanly)"
            )
    elif not manifest.incomplete:
        warnings.append(
            "manifest.result is null but incomplete=false "
            "(finalized session without a result)"
        )
    return tuple(warnings)


def validate_session(session_dir: Path | str) -> ValidationReport:
    """Validate a session directory. Never raises on bad data files."""
    session_dir = Path(session_dir)
    try:
        manifest = load_manifest(session_dir)
    except ManifestError as error:
        return ValidationReport(
            session_dir=str(session_dir),
            errors=(str(error),),
            warnings=(),
            counts={},
        )

    results = {
        stream: _read_stream(session_dir, stream)
        for stream in ("states", "actions", "events")
    }
    errors: list[str] = []
    for result in results.values():
        errors.extend(result.errors)
    errors.extend(_cross_stream_errors(results["states"], results["actions"]))
    errors.extend(_count_errors(manifest, results))
    return ValidationReport(
        session_dir=str(session_dir),
        errors=tuple(errors),
        warnings=_native_warnings(session_dir, manifest),
        counts={
            stream: result.line_count for stream, result in results.items()
        },
    )
