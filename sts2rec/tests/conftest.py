"""Shared fixtures: synthetic session builders exactly per docs/design.md."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import pytest

FIXTURES_DIR = Path(__file__).parent / "fixtures"

START_TIME = 1773034386.0
SEED = "TESTSEED12"


def make_manifest(**overrides: Any) -> dict[str, Any]:
    manifest: dict[str, Any] = {
        "schema_version": 1,
        "recorder_version": "0.1.0",
        "game": {"version": "0.108.0", "commit": "abc123", "build_id": "20250101"},
        "platform": "macos",
        "profile": "profile1",
        "run": {
            "seed": SEED,
            "character": "CHARACTER.DEFECT",
            "ascension": 1,
            "game_mode": "standard",
            "start_time": START_TIME,
        },
        "result": {"win": True, "abandoned": False, "end_time": START_TIME + 100.0},
        "counts": {"states": 3, "actions": 2, "events": 3},
        "incomplete": False,
    }
    return {**manifest, **overrides}


def make_state(seq: int, t: float, hash_: str, **payload: Any) -> dict[str, Any]:
    return {
        "seq": seq,
        "t": t,
        "type": "state",
        "trigger": "action",
        "screen": "combat",
        "hash": hash_,
        "state": {"screen": "combat", **payload},
    }


def make_action(
    seq: int,
    t: float,
    state_seq: int | None,
    kind: str = "play_card",
    *,
    status: str | None = None,
    **params: Any,
) -> dict[str, Any]:
    record: dict[str, Any] = {
        "seq": seq,
        "t": t,
        "type": "action",
        "source": "hook:PlayCardPatch",
        "action": {"kind": kind, **params},
        "state_seq": state_seq,
    }
    if status is not None:
        record["status"] = status
    return record


def make_event(seq: int, t: float, entry: str = "card_drawn", **data: Any) -> dict[str, Any]:
    return {"seq": seq, "t": t, "type": "event", "entry": entry, "data": data}


def default_records() -> dict[str, list[dict[str, Any]]]:
    """A tiny but complete run: 3 states, 2 actions, 3 events, global seq order."""
    t = START_TIME
    return {
        "states": [
            make_state(1, t + 1.0, "h1", floor=1),
            make_state(4, t + 4.0, "h2", floor=1, energy=2),
            make_state(7, t + 7.0, "h3", floor=1, energy=1),
        ],
        "actions": [
            make_action(2, t + 2.0, 1, card="CARD.ZAP"),
            make_action(5, t + 5.0, 4, kind="end_turn"),
        ],
        "events": [
            make_event(3, t + 3.0, "card_drawn", card="CARD.STRIKE_DEFECT"),
            make_event(6, t + 6.0, "damage_received", amount=5),
            make_event(8, t + 8.0, "turn_ended", turn=1),
        ],
    }


def write_session(
    session_dir: Path,
    manifest: dict[str, Any] | None = None,
    records: dict[str, list[dict[str, Any]]] | None = None,
    *,
    with_native: bool = True,
) -> Path:
    session_dir.mkdir(parents=True, exist_ok=True)
    resolved_records = records if records is not None else default_records()
    resolved_manifest = manifest if manifest is not None else make_manifest(
        counts={
            stream: len(lines) for stream, lines in resolved_records.items()
        }
    )
    (session_dir / "manifest.json").write_text(
        json.dumps(resolved_manifest), encoding="utf-8"
    )
    for stream, lines in resolved_records.items():
        content = "".join(json.dumps(line) + "\n" for line in lines)
        (session_dir / f"{stream}.jsonl").write_text(content, encoding="utf-8")
    if with_native:
        native = session_dir / "native"
        native.mkdir(exist_ok=True)
        (native / "run_history.run").write_text("{}", encoding="utf-8")
        (native / "replay.mcr").write_bytes(b"\x00MCR")
    return session_dir


@pytest.fixture
def session_dir(tmp_path: Path) -> Path:
    return write_session(tmp_path / "sessions" / f"1773034386-{SEED}")


@pytest.fixture
def real_run_paths() -> list[Path]:
    return sorted(FIXTURES_DIR.glob("*.run"))
