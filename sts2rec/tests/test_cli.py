import json
from pathlib import Path

import pytest

from sts2rec.cli import main
from tests.conftest import make_manifest, write_session


class TestSessions:
    def test_lists_sessions_with_result(
        self, tmp_path: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        root = tmp_path / "sessions"
        write_session(root / "1773034386-TESTSEED12")
        write_session(
            root / "1773034999-OTHERSEED1",
            manifest=make_manifest(result=None, incomplete=True),
        )
        assert main(["sessions", str(root)]) == 0
        out = capsys.readouterr().out
        assert "RUN_ID" in out
        assert "1773034386-TESTSEED12" in out and "win" in out
        assert "1773034999-OTHERSEED1" in out and "in-progress" in out

    def test_invalid_manifest_row(
        self, tmp_path: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        root = tmp_path / "sessions"
        bad = root / "bad-session"
        bad.mkdir(parents=True)
        (bad / "manifest.json").write_text("{broken", encoding="utf-8")
        assert main(["sessions", str(root)]) == 0
        assert "invalid manifest" in capsys.readouterr().out

    def test_missing_root(self, tmp_path: Path) -> None:
        assert main(["sessions", str(tmp_path / "nope")]) == 1

    def test_empty_root(self, tmp_path: Path, capsys: pytest.CaptureFixture[str]) -> None:
        (tmp_path / "sessions").mkdir()
        assert main(["sessions", str(tmp_path / "sessions")]) == 0
        assert "no sessions found" in capsys.readouterr().out


class TestValidate:
    def test_ok_session_exits_zero(
        self, session_dir: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        assert main(["validate", str(session_dir)]) == 0
        assert "OK" in capsys.readouterr().out

    def test_bad_session_exits_one(
        self, tmp_path: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        assert main(["validate", str(tmp_path)]) == 1
        out = capsys.readouterr().out
        assert "ERROR" in out and "FAILED" in out


class TestCanonical:
    def test_writes_to_stdout(
        self, session_dir: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        assert main(["canonical", str(session_dir)]) == 0
        trajectory = json.loads(capsys.readouterr().out)
        assert trajectory["meta"]["source"] == "sts2-real-client"
        assert len(trajectory["steps"]) == 2

    def test_writes_to_file(self, session_dir: Path, tmp_path: Path) -> None:
        out_path = tmp_path / "out.json"
        assert main(["canonical", str(session_dir), "-o", str(out_path)]) == 0
        trajectory = json.loads(out_path.read_text(encoding="utf-8"))
        assert len(trajectory["steps"]) == 2

    def test_bad_session_exits_one(self, tmp_path: Path) -> None:
        assert main(["canonical", str(tmp_path)]) == 1


class TestWatch:
    def test_watch_prints_records(
        self, session_dir: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        assert main(["watch", str(session_dir), "--idle-timeout", "0", "--interval", "0"]) == 0
        out = capsys.readouterr().out
        assert "[states]" in out and "[actions]" in out and "[events]" in out

    def test_watch_missing_dir(self, tmp_path: Path) -> None:
        assert main(["watch", str(tmp_path / "nope")]) == 1


class TestArchive:
    def test_archive_with_explicit_saves(
        self, tmp_path: Path, capsys: pytest.CaptureFixture[str]
    ) -> None:
        profile = tmp_path / "profile1"
        history = profile / "saves" / "history"
        history.mkdir(parents=True)
        (history / "1773034386.run").write_text("{}", encoding="utf-8")
        (profile / "replays").mkdir()
        (profile / "replays" / "latest.mcr").write_bytes(b"\x01")
        session = write_session(tmp_path / "session", with_native=False)
        code = main(["archive", str(session), "--saves", str(profile / "saves")])
        assert code == 0
        assert (session / "native" / "run_history.run").is_file()
        assert (session / "native" / "replay.mcr").is_file()

    def test_archive_incomplete_exits_one(self, tmp_path: Path) -> None:
        saves = tmp_path / "profile1" / "saves"
        saves.mkdir(parents=True)
        session = write_session(tmp_path / "session", with_native=False)
        assert main(["archive", str(session), "--saves", str(saves)]) == 1

    def test_archive_bad_session_exits_one(self, tmp_path: Path) -> None:
        saves = tmp_path / "saves"
        saves.mkdir()
        assert main(["archive", str(tmp_path / "nope"), "--saves", str(saves)]) == 1


class TestUsage:
    def test_no_command_exits_two(self) -> None:
        with pytest.raises(SystemExit) as excinfo:
            main([])
        assert excinfo.value.code == 2

    def test_unknown_command_exits_two(self) -> None:
        with pytest.raises(SystemExit) as excinfo:
            main(["frobnicate"])
        assert excinfo.value.code == 2

    def test_version_flag(self, capsys: pytest.CaptureFixture[str]) -> None:
        with pytest.raises(SystemExit) as excinfo:
            main(["--version"])
        assert excinfo.value.code == 0
        assert "sts2rec 0.1.0" in capsys.readouterr().out
