from __future__ import annotations

import gzip
import json
from pathlib import Path

import pytest

from sts2rec.canonical import build_canonical
from sts2rec.cli import main
from sts2rec.errors import CanonicalError
from sts2rec.indexed_reader import IndexedCanonicalReader
from .conftest import (START_TIME, default_records, make_action, make_event,
                       make_manifest, make_state, write_session)


@pytest.mark.parametrize("compressed", [False, True])
def test_full_and_targeted_reads_equal_scan_and_keep_event_boundaries(tmp_path, compressed):
    records = default_records()
    records["events"].insert(0, make_event(0, START_TIME, "prelude"))
    session = write_session(tmp_path / "s", records=records)
    if compressed:
        for path in session.glob("*.jsonl"):
            path.with_suffix(".jsonl.gz").write_bytes(gzip.compress(path.read_bytes()))
            path.unlink()
    expected = build_canonical(session)
    cache = tmp_path / "cache"
    assert build_canonical(session, cache_dir=cache) == expected
    with IndexedCanonicalReader(session, cache) as reader:
        assert reader.cache_hit
        assert list(reader.steps({2})) == expected["steps"][:1]
        assert reader.prelude_events == expected["prelude_events"]
    selected = build_canonical(session, cache_dir=cache, action_seqs={5})
    assert build_canonical(session, action_seqs={5}) == selected
    assert build_canonical(session, action_seqs=set())["steps"] == []
    assert selected["steps"] == expected["steps"][1:]
    assert selected["steps"][0]["step_idx"] == 1
    assert selected["steps"][0]["terminal"]


def test_delayed_action_reads_exact_old_state_with_all_200_candidates(tmp_path):
    cards = [{"id": f"CARD.{i}", "choice_index": i, "instance_id": i + 1,
              "description": "完整候选", "selected": i == 199} for i in range(200)]
    before = make_state(1, START_TIME, "old", state_type="card_select",
                        card_select={"cards": cards, "min_select": 1, "max_select": 1})
    before["screen"] = "card_select"
    records = {"states": [before, make_state(3, START_TIME + 3, "new", state_type="map", map={})],
               "actions": [make_action(5, START_TIME + 5, 1, kind="player_choice",
                                       index=199, card_instances=[{"id": "CARD.199", "instance_id": 200}],
                                       _decision={"status": "captured", "stage": "before_input"})],
               "events": []}
    session = write_session(tmp_path / "s", records=records)
    expected = build_canonical(session)
    for _ in range(2):
        actual = build_canonical(session, cache_dir=tmp_path / "cache")
        assert actual == expected
        step = actual["steps"][0]
        assert step["info"]["state_before_seq"] == 1
        assert step["state_before"]["card_select"]["cards"] == cards
        assert len(step["info"]["legal_actions"]) >= 200


def test_explicit_missing_capture_is_never_filled_from_future(tmp_path):
    records = {"states": [make_state(3, START_TIME + 3, "future", state_type="map")],
               "actions": [make_action(2, START_TIME + 2, None, kind="player_choice",
                                       _decision={"status": "missing", "stage": "unbound_choice"})],
               "events": []}
    session = write_session(tmp_path / "s", records=records)
    step = build_canonical(session, cache_dir=tmp_path / "cache")["steps"][0]
    assert step["state_before"] is None
    assert step["info"]["state_before_seq"] is None
    assert "state_before_estimated" not in step["info"]


@pytest.mark.parametrize("change", ["append", "replace", "manifest", "compress"])
def test_cache_invalidates_on_source_changes(tmp_path, change):
    session = write_session(tmp_path / "s")
    cache = tmp_path / "cache"
    build_canonical(session, cache_dir=cache)
    states = session / "states.jsonl"
    if change == "append":
        with states.open("a") as file:
            file.write(json.dumps(make_state(99, START_TIME + 99, "added")) + "\n")
    elif change == "replace":
        # Same-length replacement still invalidates via nanosecond mtime/ctime/inode.
        content = states.read_text().replace('"floor": 1', '"floor": 9')
        replacement = session / "replacement"
        replacement.write_text(content)
        replacement.replace(states)
    elif change == "manifest":
        path = session / "manifest.json"
        raw = json.loads(path.read_text()); raw["part"] = 4
        path.write_text(json.dumps(raw))
    else:
        states.with_suffix(".jsonl.gz").write_bytes(gzip.compress(states.read_bytes()))
        states.unlink()
    with IndexedCanonicalReader(session, cache) as reader:
        assert not reader.cache_hit
        assert list(reader.steps()) == build_canonical(session)["steps"]


def test_corrupt_index_rebuilds_and_original_files_are_never_modified(tmp_path):
    session = write_session(tmp_path / "s")
    originals = {p.name: p.read_bytes() for p in session.iterdir() if p.is_file()}
    cache = tmp_path / "cache"
    expected = build_canonical(session, cache_dir=cache)
    index = next(cache.glob("*.sqlite3")); index.write_bytes(b"broken sqlite")
    assert build_canonical(session, cache_dir=cache) == expected
    assert originals == {p.name: p.read_bytes() for p in session.iterdir() if p.is_file()}


def test_incomplete_is_ephemeral_and_tolerates_only_torn_final_line(tmp_path):
    session = write_session(tmp_path / "s", manifest=make_manifest(incomplete=True, result=None))
    path = session / "states.jsonl"
    original = path.read_text()
    path.write_text(original + '{"torn"')
    cache = tmp_path / "cache"
    for _ in range(2):
        with IndexedCanonicalReader(session, cache) as reader:
            assert not reader.cache_hit
            assert list(reader.steps()) == build_canonical(session)["steps"]
        assert not list(cache.glob("*.sqlite3"))
    path.write_text('{"torn"\n' + original)
    with pytest.raises(CanonicalError, match="malformed JSON"):
        build_canonical(session, cache_dir=cache)


def test_missing_reference_and_unsorted_records_match_scan(tmp_path):
    records = default_records()
    for values in records.values():
        values.reverse()
    session = write_session(tmp_path / "s", records=records)
    cache = tmp_path / "cache"
    assert build_canonical(session, cache_dir=cache) == build_canonical(session)
    records["actions"].append(make_action(99, START_TIME + 99, 999))
    write_session(session, records=records)
    with pytest.raises(CanonicalError, match="state_seq=999"):
        build_canonical(session, cache_dir=cache)


def test_source_changing_mid_read_is_detected_and_not_mixed_silently(tmp_path):
    session = write_session(tmp_path / "s")
    with pytest.raises(CanonicalError, match="changed during"):
        with IndexedCanonicalReader(session, tmp_path / "cache") as reader:
            iterator = reader.steps()
            next(iterator)
            with (session / "actions.jsonl").open("a") as file:
                file.write(json.dumps(make_action(99, START_TIME + 99, 1)) + "\n")
            list(iterator)


def test_cli_targeted_read(tmp_path):
    session = write_session(tmp_path / "s")
    output = tmp_path / "selected.json"
    assert main(["canonical", str(session), "--cache-dir", str(tmp_path / "cache"),
                 "--action-seq", "5", "-o", str(output)]) == 0
    assert json.loads(output.read_text())["steps"] == build_canonical(session)["steps"][1:]


def test_cache_must_not_be_written_into_source_session(tmp_path):
    session = write_session(tmp_path / "s")
    with pytest.raises(CanonicalError, match="outside"):
        build_canonical(session, cache_dir=session / "cache")


@pytest.mark.parametrize("records", [
    {"states": [], "actions": [], "events": []},
    {"states": [], "actions": [], "events": [make_event(1, START_TIME)]},
    {"states": [], "actions": [make_action(1, START_TIME, None)], "events": []},
])
def test_empty_streams_and_legacy_missing_reference_match_scan(tmp_path, records):
    session = write_session(tmp_path / "s", records=records)
    assert build_canonical(session, cache_dir=tmp_path / "cache") == build_canonical(session)
