"""Fallback archiver: copy native game artifacts into a session's native/ dir.

Used when the recorder mod missed run-end (crash, force-quit). Originals are
never moved or modified — copies only. The matching .run file is found by
comparing its filename stem (the run's start unix time) against
manifest.run.start_time within a tolerance.
"""

from __future__ import annotations

import shutil
from dataclasses import dataclass
from pathlib import Path

from .errors import ArchiveError
from .paths import history_dir, replays_dir
from .session import load_manifest

DEFAULT_START_TIME_TOLERANCE_SECONDS = 120.0
RUN_HISTORY_NAME = "run_history.run"
REPLAY_NAME = "replay.mcr"


@dataclass(frozen=True, slots=True)
class ArchiveResult:
    run_history: Path | None
    replay: Path | None
    warnings: tuple[str, ...]

    @property
    def complete(self) -> bool:
        return self.run_history is not None and self.replay is not None


def find_matching_run_file(
    history: Path, start_time: float, tolerance: float
) -> Path | None:
    """The .run file whose start-time stem is closest to start_time (within tolerance)."""
    if not history.is_dir():
        return None
    best: tuple[float, Path] | None = None
    for candidate in sorted(history.glob("*.run")):
        try:
            candidate_start = int(candidate.stem)
        except ValueError:
            continue
        distance = abs(candidate_start - start_time)
        if distance <= tolerance and (best is None or distance < best[0]):
            best = (distance, candidate)
    return best[1] if best is not None else None


def _copy_into_native(source: Path, native: Path, target_name: str) -> Path:
    target = native / target_name
    try:
        native.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
    except OSError as error:
        raise ArchiveError(f"failed to copy {source} -> {target}: {error}") from error
    return target


def archive_native(
    saves_dir: Path | str,
    session_dir: Path | str,
    *,
    tolerance: float = DEFAULT_START_TIME_TOLERANCE_SECONDS,
    overwrite: bool = False,
) -> ArchiveResult:
    """Copy the matching <start_time>.run and replays/latest.mcr into <session>/native/.

    Skips artifacts that already exist in the session unless overwrite=True.
    Raises ArchiveError only on unreadable manifest or failed copies; missing
    source artifacts are reported as warnings.
    """
    saves_dir = Path(saves_dir)
    session_dir = Path(session_dir)
    manifest = load_manifest(session_dir)
    native = session_dir / "native"
    warnings: list[str] = []

    run_target: Path | None = None
    existing_run = native / RUN_HISTORY_NAME
    if existing_run.is_file() and not overwrite:
        run_target = existing_run
    else:
        run_source = find_matching_run_file(
            history_dir(saves_dir), manifest.run.start_time, tolerance
        )
        if run_source is None:
            warnings.append(
                f"no .run file in {history_dir(saves_dir)} matches "
                f"start_time={manifest.run.start_time} "
                f"(tolerance {tolerance:.0f}s)"
            )
        else:
            run_target = _copy_into_native(run_source, native, RUN_HISTORY_NAME)

    replay_target: Path | None = None
    existing_replay = native / REPLAY_NAME
    if existing_replay.is_file() and not overwrite:
        replay_target = existing_replay
    else:
        replay_source = replays_dir(saves_dir) / "latest.mcr"
        if not replay_source.is_file():
            warnings.append(f"replay not found: {replay_source}")
        else:
            warnings.append(
                "copied latest.mcr; note it is overwritten by the game each "
                "run — only trust it if no newer run was played"
            )
            replay_target = _copy_into_native(replay_source, native, REPLAY_NAME)

    return ArchiveResult(
        run_history=run_target, replay=replay_target, warnings=tuple(warnings)
    )
