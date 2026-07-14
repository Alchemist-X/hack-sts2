from pathlib import Path

import pytest

from sts2rec import paths


def test_user_data_dir_macos() -> None:
    home = Path("/Users/alice")
    assert paths.user_data_dir("darwin", home=home) == home / (
        "Library/Application Support/SlayTheSpire2"
    )


def test_user_data_dir_windows() -> None:
    result = paths.user_data_dir("win32", appdata=r"C:\Users\alice\AppData\Roaming")
    assert result == Path(r"C:\Users\alice\AppData\Roaming") / "SlayTheSpire2"


def test_user_data_dir_windows_without_appdata_raises() -> None:
    import os

    old = os.environ.pop("APPDATA", None)
    try:
        with pytest.raises(ValueError, match="APPDATA"):
            paths.user_data_dir("win32")
    finally:
        if old is not None:
            os.environ["APPDATA"] = old


def test_user_data_dir_linux() -> None:
    home = Path("/home/alice")
    assert paths.user_data_dir("linux", home=home) == home / (
        ".local/share/SlayTheSpire2"
    )


def test_recorder_output_root_macos() -> None:
    home = Path("/Users/alice")
    assert paths.recorder_output_root("darwin", home=home) == home / (
        "Library/Application Support/Sts2Recorder"
    )


def test_sessions_root() -> None:
    assert paths.sessions_root(Path("/x")) == Path("/x/sessions")


def test_profile_saves_dirs_discovery(tmp_path: Path) -> None:
    saves = tmp_path / "steam" / "76500000000000001" / "profile1" / "saves"
    saves.mkdir(parents=True)
    (tmp_path / "steam" / "76500000000000001" / "profile2").mkdir()  # no saves/
    (tmp_path / "steam" / "not-a-dir.txt").write_text("x")
    assert paths.profile_saves_dirs(tmp_path) == (saves,)


def test_profile_saves_dirs_empty_when_no_steam_dir(tmp_path: Path) -> None:
    assert paths.profile_saves_dirs(tmp_path) == ()


def test_history_and_replays_dirs(tmp_path: Path) -> None:
    saves = tmp_path / "profile1" / "saves"
    assert paths.history_dir(saves) == saves / "history"
    # replays/ is a sibling of saves/ (observed real layout)
    assert paths.replays_dir(saves) == tmp_path / "profile1" / "replays"


def test_profile_dirs_include_modded_tree(tmp_path: Path) -> None:
    """Modded game runs relocate saves to steam/<id>/modded/profileN/saves."""
    steam_id = tmp_path / "steam" / "76500000000000001"
    vanilla = steam_id / "profile1"
    modded = steam_id / "modded" / "profile1"
    (vanilla / "saves").mkdir(parents=True)
    (modded / "saves").mkdir(parents=True)
    assert paths.profile_dirs(tmp_path) == (vanilla, modded)
    assert paths.profile_saves_dirs(tmp_path) == (
        vanilla / "saves",
        modded / "saves",
    )


def test_modded_only_profile_discovered(tmp_path: Path) -> None:
    modded_saves = (
        tmp_path / "steam" / "76500000000000001" / "modded" / "profile2" / "saves"
    )
    modded_saves.mkdir(parents=True)
    assert paths.profile_saves_dirs(tmp_path) == (modded_saves,)
