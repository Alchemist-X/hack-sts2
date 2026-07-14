import json
from pathlib import Path

import pytest

from sts2rec.archive import archive_native, find_matching_run_file
from sts2rec.errors import ManifestError
from tests.conftest import START_TIME, write_session


@pytest.fixture
def profile(tmp_path: Path) -> Path:
    """A fake steam profile dir with saves/history and replays/."""
    profile = tmp_path / "steam" / "765001" / "profile1"
    history = profile / "saves" / "history"
    history.mkdir(parents=True)
    (history / f"{int(START_TIME)}.run").write_text(
        json.dumps({"schema_version": 8, "seed": "X"}), encoding="utf-8"
    )
    (history / "1700000000.run").write_text("{}", encoding="utf-8")
    (history / "notanumber.run").write_text("{}", encoding="utf-8")
    replays = profile / "replays"
    replays.mkdir()
    (replays / "latest.mcr").write_bytes(b"\x01MCRDATA")
    return profile


def test_archives_both_artifacts(tmp_path: Path, profile: Path) -> None:
    session = write_session(tmp_path / "session", with_native=False)
    result = archive_native(profile / "saves", session)
    assert result.complete
    assert result.run_history == session / "native" / "run_history.run"
    assert result.replay == session / "native" / "replay.mcr"
    assert (session / "native" / "replay.mcr").read_bytes() == b"\x01MCRDATA"
    # originals untouched
    assert (profile / "saves" / "history" / f"{int(START_TIME)}.run").is_file()
    assert (profile / "replays" / "latest.mcr").is_file()


def test_matches_within_tolerance(tmp_path: Path, profile: Path) -> None:
    history = profile / "saves" / "history"
    exact = history / f"{int(START_TIME)}.run"
    exact.rename(history / f"{int(START_TIME) + 30}.run")  # 30s off, within 120s
    session = write_session(tmp_path / "session", with_native=False)
    result = archive_native(profile / "saves", session)
    assert result.run_history is not None


def test_no_match_outside_tolerance(tmp_path: Path, profile: Path) -> None:
    history = profile / "saves" / "history"
    (history / f"{int(START_TIME)}.run").unlink()
    session = write_session(tmp_path / "session", with_native=False)
    result = archive_native(profile / "saves", session)
    assert result.run_history is None
    assert any("no .run file" in warning for warning in result.warnings)


def test_missing_replay_warns(tmp_path: Path, profile: Path) -> None:
    (profile / "replays" / "latest.mcr").unlink()
    session = write_session(tmp_path / "session", with_native=False)
    result = archive_native(profile / "saves", session)
    assert result.replay is None
    assert not result.complete
    assert any("replay not found" in warning for warning in result.warnings)


def test_existing_copies_kept_without_overwrite(tmp_path: Path, profile: Path) -> None:
    session = write_session(tmp_path / "session", with_native=True)
    original = (session / "native" / "replay.mcr").read_bytes()
    result = archive_native(profile / "saves", session)
    assert result.complete
    assert (session / "native" / "replay.mcr").read_bytes() == original


def test_overwrite_replaces_copies(tmp_path: Path, profile: Path) -> None:
    session = write_session(tmp_path / "session", with_native=True)
    archive_native(profile / "saves", session, overwrite=True)
    assert (session / "native" / "replay.mcr").read_bytes() == b"\x01MCRDATA"


def test_bad_session_raises_manifest_error(tmp_path: Path, profile: Path) -> None:
    with pytest.raises(ManifestError):
        archive_native(profile / "saves", tmp_path / "nonexistent-session")


def test_find_matching_prefers_closest(tmp_path: Path) -> None:
    history = tmp_path / "history"
    history.mkdir()
    (history / "1000.run").write_text("{}")
    (history / "1050.run").write_text("{}")
    match = find_matching_run_file(history, 1040.0, tolerance=120.0)
    assert match is not None and match.name == "1050.run"


def test_find_matching_missing_dir(tmp_path: Path) -> None:
    assert find_matching_run_file(tmp_path / "nope", 0.0, tolerance=1.0) is None
