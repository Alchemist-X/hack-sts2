"""Session validation: structural integrity checks that never raise on bad data.

Errors  = data-integrity violations (corrupt line, seq regression, dangling
          or future-pointing state_seq, consecutive duplicate state hashes,
          timestamp regression beyond tolerance, bad manifest).
Warnings = recoverable gaps (missing native artifacts — the `archive`
          subcommand exists for exactly that — and manifest inconsistencies
          such as result set while incomplete=true).

Manifest-count contract: when manifest.incomplete=false (cleanly finalized),
counts must match the JSONL line counts exactly — a mismatch is an error.
When incomplete=true (crash-terminated session), the manifest on disk is a
periodic checkpoint while the OS may have flushed more (or fewer) buffered
JSONL lines than the checkpoint recorded, so count mismatches degrade to
warnings; line-level integrity violations (seq regressions, dangling
state_seq, malformed NON-final lines) remain errors either way.

Torn-final-line contract: when incomplete=true, a malformed FINAL line of a
stream is the recorder's single in-flight write at SIGKILL — expected crash
debris, not corruption — so it degrades to a warning and the intact records
are retained. A malformed final line in a cleanly finalized session
(incomplete=false), or any malformed non-final line, stays an error.

state_seq contract: an action's state_seq is null (legacy: 0, normalized at
parse time) when the action fired before the first snapshot; that is valid
data, not a dangling reference — `canonical` degrades gracefully.
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
    resolve_stream_path,
)

TIMESTAMP_TOLERANCE_SECONDS = 0.05
ACTION_STATUSES = frozenset({"committed", "executed", "cancelled"})

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
    warnings: tuple[str, ...] = ()


def _read_stream(
    session_dir: Path, stream: str, *, incomplete: bool
) -> _StreamResult:
    """Parse one stream (plain or .gz), collecting per-line integrity errors.

    When `incomplete` is true (crash-terminated session), a malformed FINAL
    line is tolerated as the recorder's torn in-flight write at SIGKILL and
    reported as a warning; otherwise it stays an error like any other
    malformed line.
    """
    path = resolve_stream_path(session_dir, stream)
    parser = _PARSERS[stream]
    records: list[Any] = []
    errors: list[str] = []
    torn_final_lines: list[int] = []

    def _note_torn_final(line_no: int, _error: JsonlError) -> None:
        torn_final_lines.append(line_no)

    line_count = 0
    prev_seq: int | None = None
    prev_t: float | None = None
    try:
        for line_no, raw in iter_jsonl(
            path, on_torn_final=_note_torn_final if incomplete else None
        ):
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
    warnings = tuple(
        f"{path.name}:{line_no}: torn final line (crash-terminated session); "
        f"{line_count} intact records retained"
        for line_no in torn_final_lines
    )
    return _StreamResult(
        records=tuple(records),
        line_count=line_count,
        errors=tuple(errors),
        warnings=warnings,
    )


def _cross_stream_errors(
    states: _StreamResult, actions: _StreamResult
) -> tuple[str, ...]:
    errors: list[str] = []
    state_seqs = frozenset(record.seq for record in states.records)
    for action in actions.records:
        if action.state_seq is None:
            # Contract: the action fired before any snapshot existed
            # (recorder writes null; legacy 0 is normalized at parse time).
            continue
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
    for action in actions.records:
        if action.status is not None and action.status not in ACTION_STATUSES:
            errors.append(
                f"actions.jsonl: action seq={action.seq} has unknown "
                f"status {action.status!r} (expected one of "
                f"{sorted(ACTION_STATUSES)})"
            )
    for previous, current in zip(states.records, states.records[1:]):
        if previous.hash == current.hash:
            errors.append(
                f"states.jsonl: consecutive identical state hashes at "
                f"seq={previous.seq} and seq={current.seq} "
                f"(hash={current.hash!r}); recorder must hash-dedup"
            )
    return tuple(errors)


def _count_mismatches(
    manifest: Manifest, results: Mapping[str, _StreamResult]
) -> tuple[str, ...]:
    """Count mismatches; errors when incomplete=false, warnings otherwise."""
    mismatches: list[str] = []
    for stream, result in results.items():
        expected = manifest.counts.for_stream(stream)
        if expected != result.line_count:
            message = (
                f"manifest counts.{stream}={expected} but {stream}.jsonl "
                f"has {result.line_count} lines"
            )
            if manifest.incomplete:
                message += (
                    " (tolerated: incomplete=true, manifest counts are a "
                    "checkpoint for crash-terminated sessions)"
                )
            mismatches.append(message)
    return tuple(mismatches)


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
        stream: _read_stream(
            session_dir, stream, incomplete=manifest.incomplete
        )
        for stream in ("states", "actions", "events")
    }
    errors: list[str] = []
    warnings: list[str] = []
    for result in results.values():
        errors.extend(result.errors)
        warnings.extend(result.warnings)
    errors.extend(_cross_stream_errors(results["states"], results["actions"]))
    # Contract: count mismatches are errors only for cleanly finalized
    # sessions; a crash-terminated (incomplete=true) session's manifest is a
    # periodic checkpoint, so mismatches degrade to warnings there.
    count_mismatches = _count_mismatches(manifest, results)
    if manifest.incomplete:
        warnings.extend(count_mismatches)
    else:
        errors.extend(count_mismatches)
    warnings.extend(_native_warnings(session_dir, manifest))
    return ValidationReport(
        session_dir=str(session_dir),
        errors=tuple(errors),
        warnings=tuple(warnings),
        counts={
            stream: result.line_count for stream, result in results.items()
        },
    )
