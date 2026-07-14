"""Platform-aware discovery of STS2 and recorder directories.

All functions are pure: platform / home / APPDATA are injectable so tests never
touch the real filesystem layout. Observed real layout (macOS, 2026-07):

    <user_data>/steam/<steamid>/profileN/
        saves/history/<start_time>.run   # native run summaries
        replays/latest.mcr               # native command replay (sibling of saves/)
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

GAME_DIR_NAME = "SlayTheSpire2"
RECORDER_DIR_NAME = "Sts2Recorder"


def _app_support_dir(
    dir_name: str,
    platform: str | None,
    home: Path | None,
    appdata: str | None,
) -> Path:
    resolved_platform = platform if platform is not None else sys.platform
    resolved_home = home if home is not None else Path.home()
    if resolved_platform == "darwin":
        return resolved_home / "Library" / "Application Support" / dir_name
    if resolved_platform.startswith("win"):
        resolved_appdata = (
            appdata if appdata is not None else os.environ.get("APPDATA")
        )
        if not resolved_appdata:
            raise ValueError(
                "APPDATA is not set; cannot locate the "
                f"{dir_name} directory on Windows"
            )
        return Path(resolved_appdata) / dir_name
    return resolved_home / ".local" / "share" / dir_name


def user_data_dir(
    platform: str | None = None,
    *,
    home: Path | None = None,
    appdata: str | None = None,
) -> Path:
    """The game's user-data root (contains steam/, logs/, ...)."""
    return _app_support_dir(GAME_DIR_NAME, platform, home, appdata)


def recorder_output_root(
    platform: str | None = None,
    *,
    home: Path | None = None,
    appdata: str | None = None,
) -> Path:
    """Root under which the Sts2Recorder mod writes its output."""
    return _app_support_dir(RECORDER_DIR_NAME, platform, home, appdata)


def sessions_root(output_root: Path) -> Path:
    """Directory holding one subdirectory per recorded session."""
    return output_root / "sessions"


def profile_dirs(base: Path) -> tuple[Path, ...]:
    """All steam/<steamid>/profileN directories under the user-data root."""
    steam_root = base / "steam"
    if not steam_root.is_dir():
        return ()
    found = (
        profile
        for steam_id in sorted(steam_root.iterdir())
        if steam_id.is_dir()
        for profile in sorted(steam_id.glob("profile*"))
        if profile.is_dir()
    )
    return tuple(found)


def profile_saves_dirs(base: Path) -> tuple[Path, ...]:
    """All steam/<steamid>/profileN/saves directories that exist."""
    return tuple(
        profile / "saves"
        for profile in profile_dirs(base)
        if (profile / "saves").is_dir()
    )


def history_dir(saves_dir: Path) -> Path:
    """Native .run history directory for a profile's saves dir."""
    return saves_dir / "history"


def replays_dir(saves_dir: Path) -> Path:
    """Native replay directory (latest.mcr) — sibling of saves/ on disk."""
    return saves_dir.parent / "replays"
