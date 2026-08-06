import dataclasses
import json
from pathlib import Path

import pytest

from sts2rec.errors import JsonlError, ManifestError, RecordError
from sts2rec.session import (
    iter_jsonl,
    load_manifest,
    parse_action_record,
    parse_event_record,
    parse_manifest,
    parse_state_record,
    stream_path,
)
from tests.conftest import make_action, make_event, make_manifest, make_state


class TestLoadManifest:
    def test_happy_path(self, session_dir: Path) -> None:
        manifest = load_manifest(session_dir)
        assert manifest.schema_version == 1
        assert manifest.run.seed == "TESTSEED12"
        assert manifest.result is not None and manifest.result.win is True
        assert manifest.counts.states == 3
        assert manifest.incomplete is False

    def test_missing_manifest(self, tmp_path: Path) -> None:
        with pytest.raises(ManifestError, match="manifest not found"):
            load_manifest(tmp_path)

    def test_invalid_json(self, tmp_path: Path) -> None:
        (tmp_path / "manifest.json").write_text("{not json", encoding="utf-8")
        with pytest.raises(ManifestError, match="cannot read"):
            load_manifest(tmp_path)

    def test_unknown_schema_version(self) -> None:
        with pytest.raises(ManifestError, match="unsupported schema_version 99"):
            parse_manifest(make_manifest(schema_version=99))

    def test_missing_required_key(self) -> None:
        raw = make_manifest()
        del raw["recorder_version"]
        with pytest.raises(ManifestError, match="recorder_version"):
            parse_manifest(raw)

    def test_null_result_allowed(self) -> None:
        manifest = parse_manifest(make_manifest(result=None, incomplete=True))
        assert manifest.result is None
        assert manifest.incomplete is True

    def test_ascension_must_be_between_a0_and_a10(self) -> None:
        raw = make_manifest()
        raw["run"]["ascension"] = 11
        with pytest.raises(ManifestError, match="0 through 10"):
            parse_manifest(raw)

    def test_non_object_top_level(self) -> None:
        with pytest.raises(ManifestError, match="must be a JSON object"):
            parse_manifest([1, 2])

    def test_manifest_is_immutable(self) -> None:
        manifest = parse_manifest(make_manifest())
        with pytest.raises(dataclasses.FrozenInstanceError):
            manifest.platform = "windows"  # type: ignore[misc]


class TestIterJsonl:
    def test_reads_all_lines(self, session_dir: Path) -> None:
        records = list(iter_jsonl(session_dir / "states.jsonl"))
        assert [line_no for line_no, _ in records] == [1, 2, 3]
        assert all(record["type"] == "state" for _, record in records)

    def test_truncated_line_raises_with_location(self, tmp_path: Path) -> None:
        path = tmp_path / "states.jsonl"
        path.write_text('{"seq": 1, "t": 1.0}\n{"seq": 2, "t"', encoding="utf-8")
        with pytest.raises(JsonlError, match=r"states\.jsonl:2"):
            list(iter_jsonl(path))

    def test_missing_file_raises(self, tmp_path: Path) -> None:
        with pytest.raises(JsonlError, match="not found"):
            list(iter_jsonl(tmp_path / "nope.jsonl"))

    def test_non_object_line_raises(self, tmp_path: Path) -> None:
        path = tmp_path / "s.jsonl"
        path.write_text("[1, 2]\n", encoding="utf-8")
        with pytest.raises(JsonlError, match="expected a JSON object"):
            list(iter_jsonl(path))

    def test_blank_lines_skipped(self, tmp_path: Path) -> None:
        path = tmp_path / "s.jsonl"
        path.write_text('{"a": 1}\n\n{"a": 2}\n', encoding="utf-8")
        assert len(list(iter_jsonl(path))) == 2

    def test_torn_final_line_skipped_with_callback(self, tmp_path: Path) -> None:
        """on_torn_final: a malformed FINAL line (torn in-flight write at
        SIGKILL) is skipped, the intact records are yielded, and the callback
        receives the torn line number and the would-be error."""
        path = tmp_path / "states.jsonl"
        path.write_text('{"seq": 1, "t": 1.0}\n{"seq": 2, "t"', encoding="utf-8")
        torn: list[tuple[int, JsonlError]] = []
        records = list(
            iter_jsonl(path, on_torn_final=lambda n, e: torn.append((n, e)))
        )
        assert records == [(1, {"seq": 1, "t": 1.0})]
        assert len(torn) == 1
        assert torn[0][0] == 2
        assert "malformed JSON line" in str(torn[0][1])

    def test_torn_middle_line_raises_even_with_callback(self, tmp_path: Path) -> None:
        """A malformed NON-final line is corruption, never a torn write."""
        path = tmp_path / "states.jsonl"
        path.write_text(
            '{"seq": 1, "t": 1.0}\n{"seq": 2, "t"\n{"seq": 3, "t": 3.0}\n',
            encoding="utf-8",
        )
        with pytest.raises(JsonlError, match=r"states\.jsonl:2"):
            list(iter_jsonl(path, on_torn_final=lambda n, e: None))

    def test_callback_not_invoked_on_clean_file(self, session_dir: Path) -> None:
        torn: list[int] = []
        records = list(
            iter_jsonl(
                session_dir / "states.jsonl",
                on_torn_final=lambda n, e: torn.append(n),
            )
        )
        assert len(records) == 3
        assert torn == []


class TestRecordParsing:
    def test_state_record(self) -> None:
        record = parse_state_record(make_state(1, 2.0, "h1", floor=3))
        assert (record.seq, record.t, record.hash) == (1, 2.0, "h1")
        assert record.state["floor"] == 3

    def test_action_record(self) -> None:
        record = parse_action_record(make_action(5, 6.0, 4, card="CARD.ZAP"))
        assert record.state_seq == 4
        assert record.action["kind"] == "play_card"

    def test_event_record(self) -> None:
        record = parse_event_record(make_event(7, 8.0, "card_drawn", card="X"))
        assert record.entry == "card_drawn"
        assert record.data == {"card": "X"}

    def test_wrong_type_field(self) -> None:
        with pytest.raises(RecordError, match="expected type='state'"):
            parse_state_record(make_action(1, 1.0, 0))

    def test_missing_envelope_key(self) -> None:
        with pytest.raises(RecordError, match="'seq'"):
            parse_event_record({"t": 1.0, "type": "event", "entry": "x", "data": {}})

    def test_action_without_kind(self) -> None:
        raw = make_action(1, 1.0, 0)
        raw["action"] = {"card": "X"}
        with pytest.raises(RecordError, match="'kind'"):
            parse_action_record(raw)

    def test_records_are_immutable(self) -> None:
        record = parse_event_record(make_event(1, 1.0))
        with pytest.raises(dataclasses.FrozenInstanceError):
            record.seq = 2  # type: ignore[misc]

    def test_action_status_parsed_when_present(self) -> None:
        record = parse_action_record(make_action(5, 6.0, 4, status="cancelled"))
        assert record.status == "cancelled"

    def test_action_status_defaults_to_none(self) -> None:
        record = parse_action_record(make_action(5, 6.0, 4))
        assert record.status is None


class TestManifestOptionalFields:
    def test_part_parsed_when_present(self) -> None:
        manifest = parse_manifest(make_manifest(part=3))
        assert manifest.part == 3

    def test_part_defaults_to_one(self) -> None:
        manifest = parse_manifest(make_manifest())
        assert manifest.part == 1

    def test_degraded_hooks_parsed(self) -> None:
        manifest = parse_manifest(make_manifest(degraded_hooks=["hook:MapPatch"]))
        assert manifest.degraded_hooks == ("hook:MapPatch",)


def test_stream_path_rejects_unknown_stream(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="unknown stream"):
        stream_path(tmp_path, "bogus")
