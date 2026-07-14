"""Transparent .jsonl.gz reading across iter_jsonl/validate/canonical/watch."""

from __future__ import annotations

import gzip
import json
from pathlib import Path

import pytest

from sts2rec.canonical import build_canonical
from sts2rec.errors import JsonlError
from sts2rec.session import (
    iter_jsonl,
    load_manifest,
    resolve_stream_path,
    stream_path,
)
from sts2rec.validate import validate_session
from sts2rec.watch import watch_session

from .conftest import compress_streams, default_records, write_session


class TestIterJsonlCompressed:
    def test_reads_gz_path_directly(self, compressed_session_dir: Path) -> None:
        records = list(iter_jsonl(compressed_session_dir / "states.jsonl.gz"))
        assert len(records) == 3
        assert records[0][1]["type"] == "state"

    def test_plain_path_falls_back_to_gz_twin(self, compressed_session_dir: Path) -> None:
        records = list(iter_jsonl(compressed_session_dir / "states.jsonl"))
        assert len(records) == 3

    def test_missing_both_raises(self, tmp_path: Path) -> None:
        with pytest.raises(JsonlError, match="not found"):
            list(iter_jsonl(tmp_path / "states.jsonl"))

    def test_corrupt_gz_raises_jsonl_error(self, tmp_path: Path) -> None:
        path = tmp_path / "states.jsonl.gz"
        path.write_bytes(b"\x1f\x8b not really gzip")
        with pytest.raises(JsonlError):
            list(iter_jsonl(path))

    def test_truncated_gz_raises_jsonl_error(self, tmp_path: Path) -> None:
        payload = gzip.compress(b'{"seq": 1}\n' * 100)
        path = tmp_path / "states.jsonl.gz"
        path.write_bytes(payload[: len(payload) // 2])
        with pytest.raises(JsonlError):
            list(iter_jsonl(path))


class TestResolveStreamPath:
    def test_prefers_gz_when_both_exist(self, session_dir: Path) -> None:
        gz = session_dir / "states.jsonl.gz"
        gz.write_bytes(gzip.compress((session_dir / "states.jsonl").read_bytes()))
        assert resolve_stream_path(session_dir, "states") == gz

    def test_plain_when_no_gz(self, session_dir: Path) -> None:
        assert resolve_stream_path(session_dir, "states") == stream_path(
            session_dir, "states"
        )

    def test_manifest_marks_compression(self, compressed_session_dir: Path) -> None:
        assert load_manifest(compressed_session_dir).compression == "gz"


class TestValidateCompressed:
    def test_compressed_session_validates_clean(self, compressed_session_dir: Path) -> None:
        report = validate_session(compressed_session_dir)
        assert report.ok, (report.errors, report.warnings)
        assert report.counts == {"states": 3, "actions": 2, "events": 3}

    def test_count_mismatch_still_detected(self, tmp_path: Path) -> None:
        records = default_records()
        records["events"] = records["events"][:1]
        session = write_session(tmp_path / "sessions" / "x", records=records)
        # counts in the manifest still claim 3 events
        manifest = json.loads((session / "manifest.json").read_text())
        manifest["counts"]["events"] = 3
        (session / "manifest.json").write_text(json.dumps(manifest))
        compress_streams(session)
        report = validate_session(session)
        assert not report.ok
        assert any("counts.events" in message for message in report.errors)


class TestCanonicalCompressed:
    def test_same_trajectory_as_plain(self, tmp_path: Path) -> None:
        plain = write_session(tmp_path / "sessions" / "plain")
        packed = compress_streams(write_session(tmp_path / "sessions" / "packed"))
        plain_traj = build_canonical(plain)
        packed_traj = build_canonical(packed)
        # meta.run_id differs (directory name); steps/events must be identical.
        assert plain_traj["steps"] == packed_traj["steps"]
        assert plain_traj["prelude_events"] == packed_traj["prelude_events"]


class TestWatchCompressed:
    def test_watch_emits_compressed_records_once(self, compressed_session_dir: Path) -> None:
        seen = list(
            watch_session(
                compressed_session_dir,
                poll_interval=0,
                idle_timeout=0,
                sleep=lambda _s: None,
            )
        )
        assert len(seen) == 8  # 3 states + 2 actions + 3 events
        streams = {item.stream for item in seen}
        assert streams == {"states", "actions", "events"}

    def test_watch_does_not_duplicate_after_live_compression(self, tmp_path: Path) -> None:
        session = write_session(tmp_path / "sessions" / "live")
        polls = {"count": 0}

        def stop() -> bool:
            return polls["count"] > 4

        def sleep(_seconds: float) -> None:
            polls["count"] += 1
            if polls["count"] == 2:
                # The recorder finishes the run: streams become .gz mid-watch.
                compress_streams(session)

        seen = list(watch_session(session, poll_interval=0, should_stop=stop, sleep=sleep))
        seqs = sorted(item.record["seq"] for item in seen)
        assert seqs == [1, 2, 3, 4, 5, 6, 7, 8]  # every record exactly once
