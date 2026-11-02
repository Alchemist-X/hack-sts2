from __future__ import annotations

import json
import shutil
from pathlib import Path

import pytest

from sts2rec.cli import main
from sts2rec.run_oracles import RunOracleExportError, export_run_oracles, history_reasons

FIXTURE_SAVE = Path(__file__).resolve().parents[2] / "sim" / "Sts2Sim.Tests" / "Fixtures" / "run_save_a8_silent_boss1.save"


def _history(build: str = "v0.111.0", **extra) -> dict:
    data = {
        "schema_version": 10,
        "build_id": build,
        "game_mode": "standard",
        "seed": "ABC",
        "ascension": 3,
        "modifiers": [],
        "players": [{"character": "CHARACTER.SILENT"}],
        "map_point_history": [],
    }
    data.update(extra)
    return data


def _user_data(tmp_path: Path) -> Path:
    base = tmp_path / "game"
    saves = base / "steam" / "123" / "profile1" / "saves"
    (saves / "history").mkdir(parents=True)
    shutil.copyfile(FIXTURE_SAVE, saves / "current_run.save")
    (saves / "history" / "1.run").write_text(json.dumps(_history()), encoding="utf-8")
    (saves / "history" / "2.run").write_text(json.dumps(_history("v0.110.1")), encoding="utf-8")
    (saves / "history" / "3.run").write_text(json.dumps(_history(game_mode="custom")), encoding="utf-8")
    return base


def test_history_reasons_gate_build_mode_and_players():
    assert history_reasons(_history(), "v0.111.0") == []
    assert history_reasons(_history("v0.110.1"), "v0.111.0") == ["build 'v0.110.1' (oracle build v0.111.0)"]
    assert "not single-player" in history_reasons(_history(players=[{}, {}]), "v0.111.0")


def test_export_copies_kept_files_and_writes_manifest(tmp_path: Path):
    base = _user_data(tmp_path)
    out = tmp_path / "out"
    manifest = export_run_oracles(base, None, out)
    assert manifest["summary"] == {"saves": 1, "runs": 1, "excluded": 2}
    kinds = sorted((f["kind"], f["path"]) for f in manifest["files"])
    assert kinds == [("run", "runs/1.run"), ("save", "saves/000-profile1-current_run.save")]
    assert (out / "saves" / "000-profile1-current_run.save").read_bytes() == FIXTURE_SAVE.read_bytes()
    assert json.loads((out / "manifest.json").read_text())["build_id"] == "v0.111.0"
    save_entry = next(f for f in manifest["files"] if f["kind"] == "save")
    assert save_entry["character"] == "SILENT" and save_entry["entry_point"] == "post_combat_rewards"


def test_export_refuses_output_inside_the_game_tree(tmp_path: Path):
    base = _user_data(tmp_path)
    with pytest.raises(RunOracleExportError):
        export_run_oracles(base, None, base / "steam" / "out")


def test_cli_export_run_oracles(tmp_path: Path, capsys):
    base = _user_data(tmp_path)
    out = tmp_path / "cli-out"
    code = main(["export-run-oracles", str(out), "--user-data", str(base), "--root", str(tmp_path / "none")])
    assert code == 0
    assert "1 saves, 1 .run histories kept; 2 excluded" in capsys.readouterr().out
