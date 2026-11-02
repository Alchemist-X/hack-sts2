"""Collect run-layer oracle inputs for `Sts2Sim.Diff run` (sim/specs/run-oracles.md).

Read-only over the game's user-data tree and the recorder corpus. Copies, never
modifies:

- `current_run.save` of every single-player profile (schema 20, standard mode);
- recorder `<session>/native/*.save` copies of the same format, when present;
- `.run` histories (schema 10) of the oracle build (`v0.111.0` by default).

Output layout (never inside the corpus, the game's user-data tree or the repo):

    <out>/saves/<n>-<name>.save
    <out>/runs/<name>.run
    <out>/manifest.json   # kept files + excluded files with reasons
"""

from __future__ import annotations

import json
import shutil
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Callable, Iterable

from .errors import Sts2RecError
from .paths import history_dir, profile_saves_dirs
from .run_save import RunSaveError, find_run_saves, load_run_save, strip_category, unsupported_reasons

RUN_ORACLE_INPUTS_VERSION = 1
RUN_HISTORY_SCHEMA_VERSION = 10
ORACLE_BUILD_ID = "v0.111.0"
STANDARD_GAME_MODE = "standard"


class RunOracleExportError(Sts2RecError):
    """Raised for unusable output locations."""


@dataclass
class RunOracleInputs:
    kept: list[dict[str, Any]] = field(default_factory=list)
    excluded: list[dict[str, str]] = field(default_factory=list)

    def manifest(self, build_id: str) -> dict[str, Any]:
        return {
            "run_oracle_inputs_version": RUN_ORACLE_INPUTS_VERSION,
            "build_id": build_id,
            "summary": {
                "saves": sum(1 for k in self.kept if k["kind"] == "save"),
                "runs": sum(1 for k in self.kept if k["kind"] == "run"),
                "excluded": len(self.excluded),
            },
            "files": self.kept,
            "excluded": self.excluded,
        }


def history_reasons(data: Any, build_id: str) -> list[str]:
    """Why a `.run` history is not an oracle input (empty list = keep)."""
    if not isinstance(data, dict):
        return ["not a JSON object"]
    reasons: list[str] = []
    if data.get("schema_version") != RUN_HISTORY_SCHEMA_VERSION:
        reasons.append(f"schema_version {data.get('schema_version')!r} (only {RUN_HISTORY_SCHEMA_VERSION})")
    if data.get("build_id") != build_id:
        reasons.append(f"build {data.get('build_id')!r} (oracle build {build_id})")
    if data.get("game_mode", STANDARD_GAME_MODE) != STANDARD_GAME_MODE:
        reasons.append(f"game_mode {data.get('game_mode')}")
    if data.get("modifiers"):
        reasons.append("modifiers")
    players = data.get("players")
    if not isinstance(players, list) or len(players) != 1:
        reasons.append("not single-player")
    if not data.get("seed"):
        reasons.append("no seed")
    return reasons


def _history_files(user_data: Path) -> Iterable[Path]:
    for saves in profile_saves_dirs(user_data):
        directory = history_dir(saves)
        if directory.is_dir():
            yield from sorted(directory.glob("*.run"))


def _session_saves(sessions: Path | None) -> Iterable[Path]:
    if sessions is None or not sessions.is_dir():
        return ()
    return sorted(sessions.glob("*/native/*.save"))


def collect(
    user_data: Path,
    sessions: Path | None,
    *,
    build_id: str = ORACLE_BUILD_ID,
) -> tuple[RunOracleInputs, list[tuple[Path, str, str]]]:
    """Return the manifest data and the (source, kind, target name) copies to make."""
    inputs = RunOracleInputs()
    copies: list[tuple[Path, str, str]] = []
    save_paths = list(find_run_saves(user_data)) + list(_session_saves(sessions))
    for n, path in enumerate(save_paths):
        try:
            save = load_run_save(path)
        except RunSaveError as error:
            inputs.excluded.append({"source": str(path), "reason": str(error)})
            continue
        reasons = unsupported_reasons(save)
        if reasons:
            inputs.excluded.append({"source": str(path), "reason": "; ".join(reasons)})
            continue
        try:
            entry_point = save.entry_point
        except RunSaveError as error:
            inputs.excluded.append({"source": str(path), "reason": str(error)})
            continue
        name = f"{n:03d}-{path.parent.parent.name}-{path.name}"
        player = save.players[0]
        inputs.kept.append(
            {
                "kind": "save",
                "path": f"saves/{name}",
                "source": str(path),
                "seed": save.seed,
                "character": player.character_id,
                "ascension": save.ascension,
                "entry_point": entry_point,
            }
        )
        copies.append((path, "saves", name))
    for path in _history_files(user_data):
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            inputs.excluded.append({"source": str(path), "reason": f"unreadable: {error}"})
            continue
        reasons = history_reasons(data, build_id)
        if reasons:
            inputs.excluded.append({"source": str(path), "reason": "; ".join(reasons)})
            continue
        player = data["players"][0]
        inputs.kept.append(
            {
                "kind": "run",
                "path": f"runs/{path.name}",
                "source": str(path),
                "seed": data["seed"],
                "character": strip_category(str(player.get("character", ""))),
                "ascension": data.get("ascension", 0),
            }
        )
        copies.append((path, "runs", path.name))
    return inputs, copies


def _inside(path: Path, root: Path) -> bool:
    try:
        path.resolve().relative_to(root.resolve())
    except ValueError:
        return False
    return True


def export_run_oracles(
    user_data: Path,
    sessions: Path | None,
    out_dir: Path,
    *,
    build_id: str = ORACLE_BUILD_ID,
    forbidden_roots: Iterable[Path] = (),
    log: Callable[[str], None] = lambda _message: None,
) -> dict[str, Any]:
    for root in [user_data, *([sessions] if sessions else []), *forbidden_roots]:
        if _inside(out_dir, root) or _inside(root, out_dir):
            raise RunOracleExportError(f"refusing to write {out_dir}: it overlaps {root}")
    inputs, copies = collect(user_data, sessions, build_id=build_id)
    for sub in ("saves", "runs"):
        target = out_dir / sub
        if target.is_dir():
            shutil.rmtree(target)
    for source, sub, name in copies:
        target = out_dir / sub
        target.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, target / name)
        log(f"copied {source} -> {target / name}")
    manifest = inputs.manifest(build_id)
    out_dir.mkdir(parents=True, exist_ok=True)
    (out_dir / "manifest.json").write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return manifest
