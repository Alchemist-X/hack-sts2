import json
import math

from sts2eval.report import aggregate, build_report, render_md, wilson, write_report


def summary(**kw):
    s = {"key": f"{kw['character']}-{kw['seed']}", "ascension": 0, "content": "ported_only", "policy": "claude",
         "model": "claude-sonnet-5-5", "result": "loss", "floor": 5, "act": 1, "steps": 100, "turns": 20,
         "illegal": 0, "illegal_replies": 0, "retries": 0, "api_errors": 0, "refusals": 0, "timeouts": 0,
         "max_tokens_stops": 0, "fallbacks": 0, "api_calls": 100,
         "tokens": {"input_tokens": 1000, "cache_creation_input_tokens": 0, "cache_read_input_tokens": 5000,
                    "output_tokens": 300},
         "cost_usd": 0.01, "seconds": 60.0, "replayable": True, "death": None}
    s.update(kw)
    return s


GAMES = [
    summary(seed="S1", character="IRONCLAD", result="win", floor=48, act=3, steps=900, cost_usd=0.5, seconds=600.0,
            illegal=2, illegal_replies=3, retries=3),
    summary(seed="S2", character="IRONCLAD", floor=10, act=1, steps=200, cost_usd=0.1, seconds=120.0,
            death={"floor": 10, "act": 1, "room_type": "Elite", "phase": "Defeat", "enemies": ["BYRDONIS"]}),
    summary(seed="S3", character="SILENT", result="crashed", floor=20, act=2, steps=400, cost_usd=0.2,
            seconds=240.0, error="NotImplementedException: X", replayable=True, api_errors=1, fallbacks=1),
]


def test_wilson():
    lo, hi = wilson(0, 10)
    assert lo == 0.0 and math.isclose(hi, 0.2775, abs_tol=1e-3)
    lo, hi = wilson(5, 10)
    assert math.isclose(lo, 0.2366, abs_tol=1e-3) and math.isclose(hi, 0.7634, abs_tol=1e-3)
    assert wilson(0, 0) == (0.0, 0.0)


def test_aggregate():
    h = aggregate(GAMES, 0)
    assert h["games"] == 3 and h["wins"] == 1
    assert math.isclose(h["win_rate"], 1 / 3)
    assert h["results"]["win"] == 1 and h["results"]["loss"] == 1 and h["results"]["crashed"] == 1
    assert h["crashes"] == 1
    assert h["floor_mean"] == (48 + 10 + 20) / 3 and h["floor_median"] == 20 and h["floor_max"] == 48
    assert math.isclose(h["progress_mean_pct"], 100 * 26 / 48)
    assert h["act_reached"] == {1: 1, 2: 1, 3: 1}
    assert h["steps_total"] == 1500
    assert math.isclose(h["illegal_per_game"], 2 / 3)
    assert h["api_errors"] == 1 and h["fallbacks"] == 1
    assert h["tokens"]["cache_read_input_tokens"] == 15000
    assert math.isclose(h["cost_usd"], 0.8)
    assert math.isclose(h["minutes_per_game_mean"], (600 + 120 + 240) / 3 / 60)
    assert aggregate(GAMES, 10)["full_run_floors"] == 49


def test_report_files(tmp_path):
    (tmp_path / "games").mkdir()
    for g in GAMES:
        (tmp_path / "games" / f"{g['key']}.summary.json").write_text(json.dumps(g))
    cfg = {"run_id": "t-claude", "seedset": {"name": "t", "ascension": 0, "games": [{}] * 4},
           "policy": "claude", "model": "claude-sonnet-5-5", "effort": "low", "thinking": "adaptive",
           "server_version": "abc1234", "repo_sha": "deadbeef", "prompt_hash": "0123",
           "prices": {"models": {"claude-sonnet-5-5": {"input": 2.0, "output": 10.0, "cache_read": 0.2,
                                                       "cache_write": 2.5}}}}
    (tmp_path / "config.json").write_text(json.dumps(cfg))
    (tmp_path / "run.json").write_text(json.dumps({"wall_seconds": 700.0, "abort_reason": None}))
    rep = write_report(tmp_path)
    assert (tmp_path / "report.json").exists() and (tmp_path / "report.md").exists()
    assert rep["expected_games"] == 4 and rep["headline"]["games"] == 3
    assert set(rep["per_character"]) == {"IRONCLAD", "SILENT"}
    assert rep["per_character"]["IRONCLAD"]["wins"] == 1
    md = (tmp_path / "report.md").read_text()
    assert "| **win rate** (Wilson 95%) | **33.3%** [" in md
    assert "| **progress v0.2** (mean [95% CI], median) |" in md
    assert "protocol v0.2 metric (recomputed; run written before v0.2)" in md
    assert "Elite: BYRDONIS" in md
    assert "NotImplementedException" in md
    assert "games summarised 3/4" in md
    assert "Prices used" in md and "11.67 min" in md
    assert build_report(tmp_path)["headline"] == rep["headline"]
    assert render_md(rep) == md


def test_report_stuck_and_loop_rows(tmp_path):
    games = [
        summary(seed="S4", character="SILENT", result="stuck", floor=31, act=2, steps=580, loop_hints=1,
                stuck={"phase": "CardReward", "floor": 31, "noncombat_steps": 80}, truncated=True, reward=0),
        summary(seed="S5", character="SILENT", floor=24, act=2, steps=406, loop_hints=2,
                death={"floor": 24, "act": 2, "room_type": "Monster", "phase": "Defeat", "enemies": ["MYTE"]}),
        summary(seed="S6", character="DEFECT", result="capped", cap_reason="steps", floor=30, act=2, steps=1200),
    ]
    h = aggregate(games, 0)
    assert h["results"]["stuck"] == 1 and h["results"]["capped"] == 1 and h["results"]["loss"] == 1
    assert h["stuck_games"] == 1 and h["loop_hints"] == 3 and h["wins"] == 0
    (tmp_path / "games").mkdir()
    for g in games:
        (tmp_path / "games" / f"{g['key']}.summary.json").write_text(json.dumps(g))
    (tmp_path / "config.json").write_text(json.dumps({"run_id": "t-stuck", "policy": "claude", "reveal_moves": False,
                                                      "seedset": {"name": "t", "ascension": 0, "games": [{}] * 3}}))
    rep = write_report(tmp_path)
    md = (tmp_path / "report.md").read_text()
    assert "| results | loss 1, capped 1, stuck 1 |" in md
    assert "| stuck games | 1 |" in md and "| loop hints | 3 |" in md
    assert "| stuck: CardReward floor 31 |" in md
    # capped is not stuck; no transcript -> act 2 length from the standard table (16 rooms, act 1 = 17 floors)
    assert "| S6 | DEFECT | capped | 0.583 | 12/16* | 30 | 2 | 1200 | 0 | 0.0100 | 1.00 | steps |" in md
    assert "enemy moves `hidden`" in md
    rows = {g["seed"]: g for g in rep["games"]}
    assert rows["S4"]["cause"] == "stuck: CardReward floor 31" and rows["S6"]["cause"] == "steps"


def test_report_old_runs_without_new_fields(tmp_path):
    """Summaries written before the loop guard (no loop_hints / stuck keys) still aggregate."""
    old = summary(seed="S7", character="REGENT")
    h = aggregate([old], 0)
    assert h["stuck_games"] == 0 and h["loop_hints"] == 0
    (tmp_path / "games").mkdir()
    (tmp_path / "games" / "REGENT-S7.summary.json").write_text(json.dumps(old))
    assert "enemy moves `?` (not recorded)" in render_md(build_report(tmp_path))
