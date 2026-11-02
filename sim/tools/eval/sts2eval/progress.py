"""STS2-Bench protocol v0.2 progress metric (docs/sts2-bench-protocol-v0.2-2026-11-01.md, "Progress").

A run has ACTS = 3 acts. Act a's map has rows 0..B_a: row 0 is the act's start room (the Ancient), the
last row B_a its final boss (the second boss at A10 in act 3, rows N+2; a GOLDEN_COMPASS act is a
16-room golden path, rows 0..17). Every room of row r is the (r + 1)-th room of the act on any path, so
an act has L_a = B_a + 1 rooms and the run's `floor` counts the rooms entered so far.

    rooms_reached  k  = floor - (floor at the act's first observation)   (act 1: k = floor)
    rooms_cleared  c  = L_a on a win, else max(0, k - 1)                 (the room the run ended in is not cleared)
    progress          = 1.0 on a win, else ((a - 1) + min(c, L_a - 1) / L_a) / ACTS

so each act weighs 1/3 whatever its length, a death in the last boss room scores below a win, and an
act-start observation (k = 0) scores exactly (a - 1) / 3. L_a comes from the act map the agent saw
(`map.nodes`, or the act-map grid of a Map-phase `obs_text`); without one, the standard lengths below
are used (`act_rooms_source: "default"`).
"""

from __future__ import annotations

import json
import re
from pathlib import Path

PROTOCOL_VERSION = "0.2"
ACTS = 3
# Rooms per act incl. start (Ancient) and boss rows: Overgrowth/Underdocks 15 + 2, Hive 14 + 2, Glory 13 + 2;
# DoubleBoss (A10+) adds a second boss row to the last act.
DEFAULT_ACT_ROOMS = {1: 17, 2: 16, 3: 15}
DOUBLE_BOSS_ASCENSION = 10

_GRID_ROW = re.compile(r"^ r(\d+):(.*)$")
_HEADER = re.compile(r"\| act (\d+) floor (\d+)")


def default_act_rooms(act: int, ascension: int) -> int:
    n = DEFAULT_ACT_ROOMS.get(act, DEFAULT_ACT_ROOMS[ACTS])
    if act == ACTS and ascension >= DOUBLE_BOSS_ASCENSION:
        n += 1
    return n


def full_run_floors(ascension: int) -> int:
    """Floors of a standard full run (no GOLDEN_COMPASS): 48 at A0, 49 at A10+ (second boss)."""
    return sum(default_act_rooms(a, ascension) for a in range(1, ACTS + 1))


def map_info(obs_map: dict | None) -> tuple[int | None, int | None]:
    """(current row or None, rooms in the act = max row + 1 or None) from an observation's `map`."""
    nodes = (obs_map or {}).get("nodes") or []
    if not nodes:
        return None, None
    rows = [n.get("row") for n in nodes if isinstance(n.get("row"), int)]
    cur = next((n.get("row") for n in nodes if n.get("current")), None)
    return cur, (max(rows) + 1 if rows else None)


def grid_info(obs_text: str | None) -> tuple[int | None, int | None]:
    """(current row, rooms in the act) from the act-map grid of a Map-phase `obs_text` (None, None without one).

    The grid shows at most 19 rows (sim/Sts2Sim.Server ObservationBuilder.AppendMapGrid); every act map
    so far has at most 18, so its last row is the final boss row.
    """
    if not obs_text or "-- act map" not in obs_text:
        return None, None
    cur = None
    last = None
    in_grid = False
    for line in obs_text.splitlines():
        if line.startswith("-- act map"):
            in_grid = True
            continue
        if not in_grid:
            continue
        m = _GRID_ROW.match(line)
        if not m:
            if last is not None:
                break
            continue
        last = int(m.group(1))
        if "*" in m.group(2):
            cur = last
    return cur, (last + 1 if last is not None else None)


def header_act_floor(obs_text: str | None) -> tuple[int | None, int | None]:
    m = _HEADER.search(obs_text or "")
    return (int(m.group(1)), int(m.group(2))) if m else (None, None)


class ActTracker:
    """Learns, per act, the floor at the act's first observation and the act's room count (last map seen)."""

    def __init__(self):
        self.start_floor: dict[int, int] = {}
        self.rooms: dict[int, int] = {}

    def observe(self, act: int | None, floor: int | None, rooms: int | None = None) -> None:
        if act is None:
            return
        if floor is not None and act not in self.start_floor:
            self.start_floor[act] = floor
        if rooms:
            self.rooms[act] = rooms

    def observe_step(self, rec: dict) -> None:
        """A transcript step record: v0.2 records carry `act_rooms`; older ones only `obs_text`."""
        act, floor = rec.get("act"), rec.get("floor")
        if act is None or floor is None:
            act, floor = header_act_floor(rec.get("obs_text"))
        rooms = rec.get("act_rooms")
        if rooms is None:
            rooms = grid_info(rec.get("obs_text"))[1]
        self.observe(act, floor, rooms)

    def as_dict(self) -> dict:
        return {"act_start_floor": {str(k): v for k, v in sorted(self.start_floor.items())},
                "act_rooms": {str(k): v for k, v in sorted(self.rooms.items())}}


def _int_keys(d: dict | None) -> dict[int, int]:
    return {int(k): v for k, v in (d or {}).items()}


def compute(result: str | None, act: int | None, floor: int | None, ascension: int,
            act_start_floor: dict | None = None, act_rooms: dict | None = None) -> dict:
    """The v0.2 progress fields of one game from its final (result, act, floor) and what it saw per act."""
    starts = _int_keys(act_start_floor)
    rooms = _int_keys(act_rooms)
    out = {"progress": None, "rooms_reached": None, "rooms_cleared": None, "act_rooms_current": None,
           "act_rooms_source": None, "protocol_version": PROTOCOL_VERSION}
    if result == "win":
        out.update(progress=1.0, act_rooms_source="win")
        if act is not None:
            L = rooms.get(act) or default_act_rooms(act, ascension)
            out.update(rooms_reached=L, rooms_cleared=L, act_rooms_current=L)
        return out
    if act is None or floor is None:
        return out
    act = max(1, min(int(act), ACTS))
    if act in rooms:
        L, src = rooms[act], "map"
    else:
        L, src = default_act_rooms(act, ascension), "default"
    if act == 1:
        base = 0
    elif act in starts:
        base = starts[act]
    else:
        base = sum(rooms.get(b) or default_act_rooms(b, ascension) for b in range(1, act))
    k = max(0, int(floor) - base)
    c = max(0, k - 1)
    out.update(progress=round(((act - 1) + min(c, L - 1) / L) / ACTS, 6), rooms_reached=k, rooms_cleared=c,
               act_rooms_current=L, act_rooms_source=src)
    return out


def from_transcript(path: Path) -> ActTracker:
    """Rebuild the per-act tracker from a games/<KEY>.jsonl transcript (v0.1 or v0.2 records)."""
    t = ActTracker()
    try:
        with open(path, encoding="utf-8") as f:
            for line in f:
                try:
                    rec = json.loads(line)
                except ValueError:
                    continue
                if rec.get("type") == "step":
                    t.observe_step(rec)
    except OSError:
        pass
    return t


def summary_progress(s: dict, transcript: Path | None = None) -> dict:
    """v0.2 progress of a per-game summary. Uses the summary's own per-act fields when it has them
    (written by a v0.2 runner), else rebuilds them from the transcript next to it."""
    starts, rooms = s.get("act_start_floor"), s.get("act_rooms")
    if (starts is None or rooms is None) and transcript is not None and Path(transcript).exists():
        t = from_transcript(Path(transcript))
        d = t.as_dict()
        starts, rooms = d["act_start_floor"], d["act_rooms"]
    return compute(s.get("result"), s.get("act"), s.get("floor"), int(s.get("ascension") or 0), starts, rooms)
