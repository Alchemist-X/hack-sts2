"""`sts2rec pack` semantics: verify-then-delete, incomplete refusal, idempotence."""

from __future__ import annotations

import gzip
import json
from pathlib import Path

import pytest

from sts2rec.errors import PackError
from sts2rec.pack import pack_session
from sts2rec.session import load_manifest
from sts2rec.validate import validate_session

from .conftest import make_manifest, write_session


def test_pack_compresses_and_deletes_plain(session_dir: Path) -> None:
    plain_bytes = {
        stream: (session_dir / f"{stream}.jsonl").read_bytes()
        for stream in ("states", "actions", "events")
    }
    result = pack_session(session_dir)

    assert set(result.packed) == {"states", "actions", "events"}
    assert result.bytes_before > 0
    for stream, expected in plain_bytes.items():
        plain = session_dir / f"{stream}.jsonl"
        compressed = session_dir / f"{stream}.jsonl.gz"
        assert not plain.exists()
        assert gzip.decompress(compressed.read_bytes()) == expected
    # manifest and native/ stay plain
    assert (session_dir / "manifest.json").is_file()
    assert (session_dir / "native" / "run_history.run").is_file()
    # manifest marks the compression, and the session still validates clean
    assert load_manifest(session_dir).compression == "gz"
    assert validate_session(session_dir).ok


def test_pack_is_idempotent(session_dir: Path) -> None:
    pack_session(session_dir)
    result = pack_session(session_dir)
    assert result.packed == ()
    assert set(result.already_packed) == {"states", "actions", "events"}


def test_pack_refuses_incomplete_sessions(tmp_path: Path) -> None:
    session = write_session(
        tmp_path / "sessions" / "live",
        manifest=make_manifest(incomplete=True, result=None),
    )
    with pytest.raises(PackError, match="incomplete"):
        pack_session(session)
    assert (session / "states.jsonl").is_file()  # untouched


def test_pack_force_packs_incomplete_sessions(tmp_path: Path) -> None:
    session = write_session(
        tmp_path / "sessions" / "crashed",
        manifest=make_manifest(incomplete=True, result=None),
    )
    result = pack_session(session, force=True)
    assert set(result.packed) == {"states", "actions", "events"}
    assert not (session / "states.jsonl").exists()


def test_pack_failure_keeps_plain_files(session_dir: Path) -> None:
    # A directory squats on one .gz target: that stream cannot be written.
    (session_dir / "events.jsonl.gz").mkdir()
    with pytest.raises(PackError):
        pack_session(session_dir)
    for stream in ("states", "actions", "events"):
        assert (session_dir / f"{stream}.jsonl").is_file()
    # no half-packed leftovers from this call
    assert not (session_dir / "states.jsonl.gz").exists()
    assert not (session_dir / "actions.jsonl.gz").exists()
    # manifest untouched on failure
    assert load_manifest(session_dir).compression is None


def test_pack_missing_manifest_raises(tmp_path: Path) -> None:
    empty = tmp_path / "not-a-session"
    empty.mkdir()
    with pytest.raises(PackError, match="manifest"):
        pack_session(empty)
