"""`sts2rec report` structure tests (golden-ish: sections and facts, not exact text)."""

from __future__ import annotations

import json
from pathlib import Path

from sts2rec.report import build_report

from .conftest import (
    SEED,
    START_TIME,
    compress_streams,
    make_action,
    make_event,
    make_manifest,
    make_state,
    write_session,
)

SECTION_HEADINGS = (
    "## Floor timeline",
    "## Combats",
    "## Acquisitions",
    "## Deck evolution",
    "## Counts & storage",
)


def rich_records() -> dict[str, list[dict]]:
    """A two-floor mini-run with a combat, a shop, and acquisitions."""
    t = START_TIME
    return {
        "states": [
            make_state(2, t + 2.0, "h1", floor=1, player={"hp": 68, "gold": 99}),
            make_state(8, t + 8.0, "h2", floor=1, player={"hp": 61, "gold": 99}, energy=0),
            make_state(11, t + 11.0, "h3", floor=2, gold=249),
        ],
        "actions": [
            make_action(3, t + 3.0, 2, card="CARD.ZAP", status="executed"),
            make_action(5, t + 5.0, 2, kind="end_turn", status="executed"),
            make_action(
                10, t + 10.0, 8, kind="map_choice", status="committed", node="x1y2"
            ),
            make_action(
                12, t + 12.0, 11, kind="shop_purchase", status="committed",
                relic="RELIC.ANCHOR", cost=120,
            ),
        ],
        "events": [
            make_event(1, t + 1.0, "room_entered", room_type="MONSTER", floor=1, act=1),
            make_event(4, t + 4.0, "damage_dealt", amount=8, target="ENEMY.CULTIST"),
            make_event(6, t + 6.0, "damage_received", amount=7, actor="ENEMY.CULTIST"),
            make_event(7, t + 7.0, "card_reward_obtained", card="CARD.DUALCAST"),
            make_event(9, t + 9.0, "room_entered", room_type="SHOP", floor=2, act=1),
            make_event(13, t + 13.0, "relic_obtained", relic="RELIC.ANCHOR"),
        ],
    }


def write_rich_session(base: Path) -> Path:
    return write_session(base / "sessions" / f"1773034386-{SEED}", records=rich_records())


def test_report_has_all_sections_and_header_facts(tmp_path: Path) -> None:
    report = build_report(write_rich_session(tmp_path))
    for heading in SECTION_HEADINGS:
        assert heading in report
    assert report.startswith("# STS2 run report")
    assert "CHARACTER.DEFECT" in report
    assert SEED in report
    assert "WIN" in report
    assert "0.108.0" in report


def test_report_combat_summary_facts(tmp_path: Path) -> None:
    report = build_report(write_rich_session(tmp_path))
    assert "ENEMY.CULTIST" in report
    assert "CARD.ZAP" in report
    assert "8/7" in report  # damage dealt/taken
    # hp/gold pulled from the first snapshot after the combat
    assert "HP 61" in report
    assert "gold 99" in report


def test_report_floor_timeline_and_acquisitions(tmp_path: Path) -> None:
    report = build_report(write_rich_session(tmp_path))
    assert "MONSTER" in report
    assert "SHOP" in report
    assert "RELIC.ANCHOR" in report
    assert "CARD.DUALCAST" in report  # deck evolution gain
    assert "+ CARD.DUALCAST" in report


def test_report_reads_compressed_sessions(tmp_path: Path) -> None:
    session = compress_streams(write_rich_session(tmp_path))
    report = build_report(session)
    assert "## Counts & storage" in report
    assert "gz" in report
    assert "CARD.ZAP" in report


def test_report_survives_incomplete_crash_session(tmp_path: Path) -> None:
    records = rich_records()
    session = write_session(
        tmp_path / "sessions" / "crashed",
        manifest=make_manifest(incomplete=True, result=None),
        records=records,
        with_native=False,
    )
    # simulate a torn final write
    with (session / "events.jsonl").open("a", encoding="utf-8") as handle:
        handle.write('{"seq": 14, "t": 1.0, "type": "ev')
    report = build_report(session)
    assert "in progress" in report
    assert "incomplete" in report
    assert "## Combats" in report
    assert "CARD.ZAP" in report
    # Torn-final-line contract: the crash-terminated session's torn in-flight
    # line is skipped with a specific note, not a generic truncation warning.
    assert "torn final line" in report
    assert "stream truncated/unreadable" not in report


def test_report_handles_empty_streams(tmp_path: Path) -> None:
    session = write_session(
        tmp_path / "sessions" / "empty",
        manifest=make_manifest(counts={"states": 0, "actions": 0, "events": 0}),
        records={"states": [], "actions": [], "events": []},
    )
    report = build_report(session)
    assert "(no combat activity recorded)" in report
    assert "(no acquisitions recorded)" in report
    assert "(no deck change events recorded)" in report


def test_report_default_session_structure(session_dir: Path) -> None:
    # The plain conftest session (no room events) must still render.
    report = build_report(session_dir)
    for heading in SECTION_HEADINGS:
        assert heading in report
    assert "CARD.ZAP" in report
