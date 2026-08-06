import dataclasses
import json
from pathlib import Path

import pytest

from sts2rec.errors import NativeRunError
from sts2rec.native_run import (
    NATIVE_RUN_SCHEMA_VERSION,
    SUPPORTED_NATIVE_RUN_SCHEMA_VERSIONS,
    load_run_file,
    parse_run_summary,
)


class TestRealFixtures:
    """Parse real .run files captured from the game (schema_version 8)."""

    def test_all_fixtures_parse(self, real_run_paths: list[Path]) -> None:
        assert len(real_run_paths) >= 2, "expected real .run fixtures checked in"
        for path in real_run_paths:
            summary = load_run_file(path)
            assert summary.schema_version == 8
            assert summary.seed
            assert summary.build_id.startswith("v")
            assert int(path.stem) == summary.start_time
            assert len(summary.players) >= 1
            assert len(summary.acts) == 3

    def test_small_abandoned_run(self, real_run_paths: list[Path]) -> None:
        summary = load_run_file(
            next(path for path in real_run_paths if path.stem == "1773034386")
        )
        assert summary.was_abandoned is True
        assert summary.win is False
        assert summary.seed == "TXRREQH1QY"
        assert summary.players[0].character == "CHARACTER.DEFECT"
        assert summary.players[0].deck[0].id == "CARD.STRIKE_DEFECT"
        assert summary.floors_visited == 1
        first_point = summary.map_point_history[0][0]
        assert first_point.map_point_type == "ancient"
        assert first_point.rooms[0].room_type == "event"

    def test_large_multiplayer_win(self, real_run_paths: list[Path]) -> None:
        summary = load_run_file(
            next(path for path in real_run_paths if path.stem == "1773471970")
        )
        assert summary.win is True
        assert len(summary.players) == 3
        assert summary.floors_visited > 10

    def test_summary_is_immutable(self, real_run_paths: list[Path]) -> None:
        summary = load_run_file(real_run_paths[0])
        with pytest.raises(dataclasses.FrozenInstanceError):
            summary.seed = "HACKED"  # type: ignore[misc]
        assert isinstance(summary.players, tuple)
        assert isinstance(summary.players[0].deck, tuple)

    def test_schema_constants_preserve_the_fixture_backed_alias(self) -> None:
        assert NATIVE_RUN_SCHEMA_VERSION == 8
        assert SUPPORTED_NATIVE_RUN_SCHEMA_VERSIONS == frozenset({8, 9})


class TestErrorHandling:
    def test_missing_file(self, tmp_path: Path) -> None:
        with pytest.raises(NativeRunError, match="not found"):
            load_run_file(tmp_path / "nope.run")

    def test_invalid_json(self, tmp_path: Path) -> None:
        path = tmp_path / "bad.run"
        path.write_text("{oops", encoding="utf-8")
        with pytest.raises(NativeRunError, match="cannot read"):
            load_run_file(path)

    def test_schema_version_9_best_effort_compatibility(
        self, real_run_paths: list[Path]
    ) -> None:
        # This verifies compatibility with the common v8 field set, not a real
        # v9 fixture. A checked-in native v9 run is still needed for full coverage.
        raw = json.loads(real_run_paths[0].read_text(encoding="utf-8"))
        raw["schema_version"] = 9
        assert parse_run_summary(raw).schema_version == 9

    def test_unknown_schema_version(self, real_run_paths: list[Path]) -> None:
        raw = json.loads(real_run_paths[0].read_text(encoding="utf-8"))
        raw["schema_version"] = 10
        with pytest.raises(NativeRunError, match="unsupported native schema_version 10"):
            parse_run_summary(raw)

    def test_ascension_must_be_between_a0_and_a10(
        self, real_run_paths: list[Path]
    ) -> None:
        raw = json.loads(real_run_paths[0].read_text(encoding="utf-8"))
        raw["ascension"] = 11
        with pytest.raises(NativeRunError, match="0 through 10"):
            parse_run_summary(raw)

    def test_missing_required_keys(self) -> None:
        with pytest.raises(NativeRunError, match="missing required keys"):
            parse_run_summary({"schema_version": 8, "seed": "X"})

    def test_non_object_top_level(self) -> None:
        with pytest.raises(NativeRunError, match="must be a JSON object"):
            parse_run_summary("not a dict")

    def test_malformed_player(self, real_run_paths: list[Path]) -> None:
        raw = json.loads(real_run_paths[0].read_text(encoding="utf-8"))
        raw["players"] = [{"character": "X"}]  # no id
        with pytest.raises(NativeRunError, match="players"):
            parse_run_summary(raw)
