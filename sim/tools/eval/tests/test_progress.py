"""Protocol v0.2 progress metric: unit tests, the runner's per-act tracking, and re-aggregating old runs."""

import asyncio
import json
import math

import pytest

from sts2eval import cli
from sts2eval import prices as P
from sts2eval import progress as PG
from sts2eval import seedsets as S
from sts2eval.policies import RandomPolicy
from sts2eval.report import build_report, mean_ci95
from sts2eval.runner import RunLimits, RunState, play_game

A0 = {"1": 17, "2": 16, "3": 15}          # standard act lengths (rooms incl. Ancient and boss rows)
A0_START = {"1": 1, "2": 17, "3": 33}


def test_full_run_floors_and_defaults():
    assert PG.full_run_floors(0) == 48 and PG.full_run_floors(10) == 49 and PG.full_run_floors(20) == 49
    assert [PG.default_act_rooms(a, 10) for a in (1, 2, 3)] == [17, 16, 16]


@pytest.mark.parametrize("result,act,floor,asc,starts,rooms,expected", [
    ("win", 3, 48, 0, A0_START, A0, 1.0),
    ("win", 3, 49, 10, None, None, 1.0),
    ("loss", 1, 1, 0, A0_START, A0, 0.0),                               # died in the first room
    ("loss", 1, 2, 0, A0_START, A0, (1 / 17) / 3),                     # cleared the Ancient room
    ("loss", 1, 17, 0, A0_START, A0, (16 / 17) / 3),                   # died at the act 1 boss
    ("capped", 2, 17, 0, A0_START, A0, 1 / 3),                         # act 2 start, no room entered yet
    ("loss", 2, 33, 0, A0_START, A0, (1 + 15 / 16) / 3),               # act 2 boss
    ("loss", 3, 48, 0, A0_START, A0, (2 + 14 / 15) / 3),               # A0 final boss: below a win
    ("loss", 3, 48, 10, A0_START, {"1": 17, "2": 16, "3": 16}, (2 + 14 / 16) / 3),  # A10 first boss of act 3
    ("loss", 3, 49, 10, A0_START, {"1": 17, "2": 16, "3": 16}, (2 + 15 / 16) / 3),  # A10 second boss
    # GOLDEN_COMPASS taken in act 2: a 16-room golden path, 18 rows; boss at floor 17 + 18 = 35
    ("loss", 2, 35, 0, A0_START, {"1": 17, "2": 18}, (1 + 17 / 18) / 3),
    ("loss", 3, 36, 0, {"1": 1, "2": 17, "3": 35}, {"1": 17, "2": 18, "3": 15}, (2 + 0 / 15) / 3),
    ("loss", 3, 40, 0, {"1": 1, "2": 17, "3": 35}, {"1": 17, "2": 18, "3": 15}, (2 + 4 / 15) / 3),
])
def test_compute(result, act, floor, asc, starts, rooms, expected):
    out = PG.compute(result, act, floor, asc, starts, rooms)
    assert math.isclose(out["progress"], expected, abs_tol=1e-6)
    assert 0.0 <= out["progress"] <= 1.0
    if result != "win":
        assert out["progress"] < 1.0
    assert out["protocol_version"] == "0.2"


def test_compute_defaults_and_missing():
    out = PG.compute("loss", 3, 40, 10, None, None)               # no maps seen: standard table, base 17 + 16
    assert out["act_rooms_source"] == "default" and out["act_rooms_current"] == 16
    assert out["rooms_reached"] == 7 and out["rooms_cleared"] == 6
    assert math.isclose(out["progress"], (2 + 6 / 16) / 3, abs_tol=1e-6)
    assert PG.compute("error", None, None, 0)["progress"] is None


def test_progress_is_monotone_along_a_standard_run():
    """Non-decreasing over the run; it steps up once per cleared room (48 rooms: 47 below a win, then the win).
    Entering a room adds nothing until it is cleared, so an act's start observation and its first room tie."""
    starts = {"1": 1, "2": 17, "3": 33}
    seq = []
    for floor in range(1, 49):
        act = 1 if floor <= 17 else 2 if floor <= 33 else 3
        seq.append(PG.compute("loss", act, floor, 0, starts, A0)["progress"])
        if floor in (17, 33):   # the next act's start observation, same floor
            seq.append(PG.compute("loss", act + 1, floor, 0, starts, A0)["progress"])
    seq.append(PG.compute("win", 3, 48, 0, starts, A0)["progress"])
    assert all(b >= a for a, b in zip(seq, seq[1:]))
    assert len(set(seq)) == 49 and seq[0] == 0.0 and seq[-1] == 1.0


GRID_TEXT = """== r4 Map | act 1 floor 2 | IRONCLAD HP 80/80 gold 99
-- map: next rooms
 (0) Monster col 2 row 2 -> Unknown
-- act map (r<row>: <col><type>[*current|x visited]><next-row cols>; M monster E elite R rest $ shop T treasure ? unknown A ancient B boss)
 r0: 3Ax>2,3,6
 r1: 2M*>2 3M>3 6M>5,6
 r2: 2?>2 3$>2,4
 r3: 3B
-- legal actions
 travel 0  : travel to Monster (col 2, row 2)
"""


def test_grid_and_map_info():
    assert PG.grid_info(GRID_TEXT) == (1, 4)
    assert PG.grid_info("== r1 Combat | act 1 floor 2") == (None, None)
    assert PG.header_act_floor(GRID_TEXT) == (1, 2)
    nodes = [{"row": 0, "col": 3, "current": False}, {"row": 1, "col": 2, "current": True},
             {"row": 2, "col": 3, "current": False}, {"row": 3, "col": 3, "current": False}]
    assert PG.map_info({"options": [], "nodes": nodes}) == (1, 4)
    assert PG.map_info(None) == (None, None)


def test_mean_ci95():
    assert mean_ci95([]) == (0.0, 0.0) and mean_ci95([0.4]) == (0.4, 0.4)
    lo, hi = mean_ci95([0.2, 0.4, 0.6])
    assert math.isclose(lo, 0.4 - 1.96 * 0.2 / math.sqrt(3)) and math.isclose(hi, 0.4 + 1.96 * 0.2 / math.sqrt(3))
    assert mean_ci95([0.0, 1.0, 1.0, 1.0]) [1] == 1.0


def _nodes(rows: int, current: int | None):
    return [{"row": r, "col": 3, "type": "Monster", "edges": [3] if r < rows - 1 else [], "visited": False,
             "current": r == current} for r in range(rows)]


class FakeMapSim:
    """A tiny two-act run: act 1 has 4 rooms (rows 0..3), act 2 has 3; the run dies in act 2's second room."""

    SEQ = [  # (act, floor, phase, map rows or None, current row)
        (1, 1, "Map", 4, 0), (1, 2, "Combat", None, None), (1, 2, "Map", 4, 1), (1, 3, "Combat", None, None),
        (1, 3, "Map", 4, 2), (1, 4, "Combat", None, None), (2, 4, "Map", 3, None), (2, 5, "Map", 3, 0),
        (2, 6, "Combat", None, None), (2, 6, "Defeat", None, None),
    ]

    def __init__(self):
        self.i = 0
        self.tokens = []

    def _obs(self):
        act, floor, phase, rows, cur = self.SEQ[self.i]
        terminal = phase == "Defeat"
        legal = [] if terminal else [{"token": "travel 0" if phase == "Map" else "end", "desc": "go"}]
        text = f"== r1 {phase} | act {act} floor {floor}\n-- legal actions\n" + "\n".join(
            f" {a['token']}  : {a['desc']}" for a in legal)
        return {"game_id": "r1", "kind": "run", "phase": phase, "terminal": terminal,
                "result": "loss" if terminal else None, "act": act, "floor": floor, "turn": 1 if phase == "Combat" else None,
                "player": {"hp": 0 if terminal else 50, "max_hp": 80},
                "combat": {"enemies": [{"id": "NIBBIT"}]} if phase == "Combat" else None,
                "map": {"options": [{"index": 0, "type": "Monster"}], "nodes": _nodes(rows, cur)} if rows else None,
                "legal_actions": legal, "text": text}

    async def health(self):
        return {"ok": True, "version": "fake"}

    async def new_run(self, seed, character, ascension, content, **kw):
        return {"id": "r1", "observation": self._obs()}

    async def step(self, gid, token):
        self.tokens.append(token)
        self.i += 1
        return {"observation": self._obs(), "log": [], "ok": True}

    async def history(self, gid):
        return {"id": gid, "tokens": list(self.tokens), "steps": len(self.tokens)}

    async def delete(self, gid):
        return {"ok": True}


SEEDSET = {"name": "t", "ascension": 0, "content": "ported_only", "games": [{"seed": "T1", "character": "IRONCLAD"}]}


def test_runner_records_rooms_and_progress(tmp_path):
    s = asyncio.run(play_game(FakeMapSim(), RandomPolicy(), SEEDSET["games"][0], SEEDSET, tmp_path, RunLimits(),
                              RunState(tracker=P.CostTracker()), "fake"))
    assert s["result"] == "loss" and s["act"] == 2 and s["floor"] == 6 and s["final_phase"] == "Defeat"
    assert s["act_start_floor"] == {"1": 1, "2": 4} and s["act_rooms"] == {"1": 4, "2": 3}
    assert s["rooms_reached"] == 2 and s["rooms_cleared"] == 1 and s["act_rooms_current"] == 3
    assert math.isclose(s["progress"], (1 + 1 / 3) / 3, abs_tol=1e-6)
    assert s["protocol_version"] == "0.2" and s["progress_pct"] == round(100 * 6 / 48, 1)
    recs = [json.loads(line) for line in (tmp_path / "games" / "IRONCLAD-T1.jsonl").read_text().splitlines()]
    steps = [r for r in recs if r["type"] == "step"]
    assert [(r.get("map_row"), r.get("act_rooms")) for r in steps if r["phase"] == "Map"] == \
        [(0, 4), (1, 4), (2, 4), (None, 3), (0, 3)]
    assert "act_rooms" not in steps[1]
    assert recs[-1]["type"] == "end" and math.isclose(recs[-1]["progress"], s["progress"])
    # the transcript alone gives the same answer (the path `sts2eval report` takes for older summaries)
    old = {k: v for k, v in s.items() if k not in ("act_start_floor", "act_rooms", "progress")}
    again = PG.summary_progress(old, tmp_path / "games" / "IRONCLAD-T1.jsonl")
    assert math.isclose(again["progress"], s["progress"]) and again["act_rooms_source"] == "map"


def _v01_transcript(path, act_maps, final):
    """A transcript as a pre-v0.2 runner wrote it: act/floor and obs_text only (grid on Map steps)."""
    lines = [json.dumps({"type": "start", "key": path.stem})]
    i = 0
    for act, floor, rows in act_maps:
        grid = "\n".join(f" r{r}: 3M>3" for r in range(rows))
        text = f"== r1 Map | act {act} floor {floor} | IRONCLAD\n-- act map (r<row>: ...)\n{grid}\n-- legal actions\n travel 0  : go"
        lines.append(json.dumps({"type": "step", "step": i, "phase": "Map", "act": act, "floor": floor,
                                 "obs_text": text, "legal": ["travel 0"], "token": "travel 0"}))
        i += 1
    lines.append(json.dumps({"type": "end", "result": final["result"], "steps": i, "floor": final["floor"]}))
    path.write_text("\n".join(lines) + "\n")


def test_report_recomputes_old_run(tmp_path, capsys):
    """`sts2eval report DIR` on a run written before v0.2: progress comes from the transcripts."""
    games = tmp_path / "games"
    games.mkdir()
    base = {"ascension": 10, "content": "ported_only", "policy": "claude", "model": "claude-sonnet-5-5",
            "steps": 100, "turns": 10, "illegal": 0, "tokens": {}, "cost_usd": 1.0, "seconds": 60.0,
            "progress_pct": None}
    rows = [  # key, result, act, floor, act maps seen
        ("IRONCLAD-E1A10IC01", "loss", 1, 9, [(1, 1, 17)]),
        ("SILENT-E1A10SI01", "loss", 3, 49, [(1, 1, 17), (2, 17, 16), (3, 33, 16)]),
        ("DEFECT-E1A10DE01", "win", 3, 49, [(1, 1, 17), (2, 17, 16), (3, 33, 16)]),
        ("REGENT-E1A10RE01", "capped", 2, 35, [(1, 1, 17), (2, 17, 18)]),   # GOLDEN_COMPASS act 2
    ]
    for key, result, act, floor, maps in rows:
        ch, seed = key.split("-")
        (games / f"{key}.summary.json").write_text(json.dumps({**base, "key": key, "seed": seed, "character": ch,
                                                               "result": result, "act": act, "floor": floor}))
        _v01_transcript(games / f"{key}.jsonl", maps, {"result": result, "floor": floor})
    seedset = S.load("std-a10-v1")
    (tmp_path / "config.json").write_text(json.dumps({"run_id": "old", "policy": "claude", "seedset": seedset}))
    assert cli.main(["report", str(tmp_path)]) == 0
    rep = json.loads((tmp_path / "report.json").read_text())
    expected = {"IRONCLAD": (8 / 17) / 3, "SILENT": (2 + 15 / 16) / 3, "DEFECT": 1.0, "REGENT": (1 + 17 / 18) / 3}
    got = {g["character"]: g["progress"] for g in rep["games"]}
    for ch, p in expected.items():
        assert math.isclose(got[ch], p, abs_tol=1e-6), ch
    h = rep["headline"]
    assert math.isclose(h["progress_mean"], sum(expected.values()) / 4, abs_tol=1e-6)
    assert h["progress_games"] == 4 and h["act_rooms_default"] == 0 and h["full_run_floors"] == 49
    assert rep["protocol_version"] == "0.2" and rep["run_protocol_version"] is None
    assert rep["seedset_id"] == "std-a10-v1" and rep["seedset_frozen"] and rep["seedset_version"] == 1
    md = (tmp_path / "report.md").read_text()
    assert "| E1A10RE01 | REGENT | capped | 0.648 | 17/18 | 35 | 2 |" in md
    assert "seed set id `std-a10-v1` v1" in md and "frozen" in md
    out = capsys.readouterr().out
    assert "progress v0.2" in out
    # idempotent: a second report over the same dir gives the same headline
    assert json.loads(json.dumps(build_report(tmp_path)["headline"])) == h
