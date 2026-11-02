"""Aggregate games/*.summary.json into report.json and report.md."""

from __future__ import annotations

import json
import math
import statistics
from collections import Counter
from pathlib import Path

from .prices import USAGE_KEYS
from .runner import full_run_floors
from .seedsets import CHARACTERS

RESULTS = ("win", "loss", "capped", "stuck", "crashed", "aborted", "error")


def wilson(wins: int, n: int, z: float = 1.96) -> tuple[float, float]:
    if n == 0:
        return (0.0, 0.0)
    p = wins / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def load_summaries(out_dir: Path) -> list[dict]:
    out = []
    for p in sorted((Path(out_dir) / "games").glob("*.summary.json")):
        with open(p, encoding="utf-8") as f:
            out.append(json.load(f))
    return out


def _mean(xs):
    return statistics.fmean(xs) if xs else None


def aggregate(summaries: list[dict], ascension: int = 0) -> dict:
    n = len(summaries)
    results = Counter(s.get("result") for s in summaries)
    wins = results.get("win", 0)
    lo, hi = wilson(wins, n)
    floors = [s["floor"] for s in summaries if s.get("floor") is not None]
    total_floors = full_run_floors(ascension)
    tokens = {k: sum((s.get("tokens") or {}).get(k, 0) for s in summaries) for k in USAGE_KEYS}
    secs = [s["seconds"] for s in summaries if s.get("seconds") is not None]
    return {
        "games": n,
        "results": {r: results.get(r, 0) for r in RESULTS},
        "wins": wins,
        "win_rate": wins / n if n else None,
        "win_rate_ci95": [lo, hi],
        "floor_mean": _mean(floors),
        "floor_median": statistics.median(floors) if floors else None,
        "floor_max": max(floors) if floors else None,
        "progress_mean_pct": (100.0 * _mean(floors) / total_floors) if floors else None,
        "full_run_floors": total_floors,
        "act_reached": dict(sorted(Counter(s.get("act") for s in summaries if s.get("act") is not None).items())),
        "steps_mean": _mean([s.get("steps", 0) for s in summaries]),
        "steps_total": sum(s.get("steps", 0) for s in summaries),
        "turns_mean": _mean([s.get("turns", 0) for s in summaries]),
        "illegal_total": sum(s.get("illegal", 0) for s in summaries),
        "illegal_per_game": (sum(s.get("illegal", 0) for s in summaries) / n) if n else None,
        "illegal_replies_total": sum(s.get("illegal_replies", 0) for s in summaries),
        "retries_total": sum(s.get("retries", 0) for s in summaries),
        "crashes": results.get("crashed", 0),
        "stuck_games": results.get("stuck", 0),
        "loop_hints": sum(s.get("loop_hints", 0) for s in summaries),
        "api_errors": sum(s.get("api_errors", 0) for s in summaries),
        "refusals": sum(s.get("refusals", 0) for s in summaries),
        "timeouts": sum(s.get("timeouts", 0) for s in summaries),
        "max_tokens_stops": sum(s.get("max_tokens_stops", 0) for s in summaries),
        "fallbacks": sum(s.get("fallbacks", 0) for s in summaries),
        "late_line_tokens": sum(s.get("late_line_tokens", 0) for s in summaries),
        "api_calls": sum(s.get("api_calls", 0) for s in summaries),
        "tokens": tokens,
        "cost_usd": round(sum(s.get("cost_usd", 0.0) for s in summaries), 6),
        "cost_per_game_usd": (sum(s.get("cost_usd", 0.0) for s in summaries) / n) if n else None,
        "minutes_per_game_mean": (_mean(secs) / 60.0) if secs else None,
        "replayable": sum(1 for s in summaries if s.get("replayable")),
    }


def build_report(out_dir: Path) -> dict:
    out_dir = Path(out_dir)
    cfg = _read_json(out_dir / "config.json") or {}
    run = _read_json(out_dir / "run.json") or {}
    summaries = load_summaries(out_dir)
    asc = (cfg.get("seedset") or {}).get("ascension", summaries[0]["ascension"] if summaries else 0)
    expected = len((cfg.get("seedset") or {}).get("games") or []) or None
    per_char = {}
    for ch in CHARACTERS:
        sub = [s for s in summaries if s.get("character") == ch]
        if sub:
            per_char[ch] = aggregate(sub, asc)
    report = {
        "run_id": cfg.get("run_id"),
        "out_dir": str(out_dir),
        "seedset": (cfg.get("seedset") or {}).get("name"),
        "ascension": asc,
        "policy": cfg.get("policy"),
        "model": cfg.get("model"),
        "effort": cfg.get("effort"),
        "thinking": cfg.get("thinking"),
        "server_version": cfg.get("server_version"),
        "repo_sha": cfg.get("repo_sha"),
        "prompt_hash": cfg.get("prompt_hash"),
        "reveal_moves": cfg.get("reveal_moves"),
        "prices": cfg.get("prices"),
        "expected_games": expected,
        "aborted": run.get("abort_reason"),
        "wall_seconds": run.get("wall_seconds"),
        "headline": aggregate(summaries, asc),
        "per_character": per_char,
        "games": [_game_row(s) for s in summaries],
    }
    return report


def _game_row(s: dict) -> dict:
    death = s.get("death") or {}
    cause = None
    if s.get("result") == "loss":
        cause = "/".join(death.get("enemies") or []) or (death.get("phase") or "?")
        if death.get("room_type"):
            cause = f"{death['room_type']}: {cause}"
    elif s.get("result") == "crashed":
        cause = (s.get("error") or "")[:120]
    elif s.get("result") == "stuck":
        stuck = s.get("stuck") or {}
        cause = f"stuck: {stuck.get('phase') or '?'} floor {_f(stuck.get('floor', s.get('floor')))}"
    elif s.get("result") in ("capped", "aborted", "error"):
        cause = s.get("cap_reason") or s.get("abort_reason") or (s.get("error") or "")[:120]
    return {"seed": s.get("seed"), "character": s.get("character"), "result": s.get("result"),
            "floor": s.get("floor"), "act": s.get("act"), "steps": s.get("steps"), "turns": s.get("turns"),
            "illegal": s.get("illegal"), "api_errors": s.get("api_errors"), "cost_usd": s.get("cost_usd"),
            "minutes": round((s.get("seconds") or 0) / 60.0, 2), "cause": cause,
            "replayable": s.get("replayable")}


def _read_json(p: Path):
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def _f(x, nd=1, pct=False):
    if x is None:
        return "-"
    if pct:
        return f"{100 * x:.{nd}f}%"
    if isinstance(x, float):
        return f"{x:.{nd}f}"
    return str(x)


def _headline_rows(h: dict) -> list[tuple[str, str]]:
    t = h["tokens"]
    lo, hi = h["win_rate_ci95"]
    acts = ", ".join(f"act {a}: {c}" for a, c in h["act_reached"].items()) or "-"
    res = ", ".join(f"{k} {v}" for k, v in h["results"].items() if v) or "-"
    return [
        ("games", str(h["games"])),
        ("results", res),
        ("wins", str(h["wins"])),
        ("win rate (Wilson 95%)", f"{_f(h['win_rate'], 1, True)} [{_f(lo, 1, True)}, {_f(hi, 1, True)}]"),
        ("floor mean / median / max", f"{_f(h['floor_mean'])} / {_f(h['floor_median'])} / {_f(h['floor_max'])}"),
        (f"progress (floor / {h['full_run_floors']})", f"{_f(h['progress_mean_pct'])}%"
         if h["progress_mean_pct"] is not None else "-"),
        ("act reached", acts),
        ("steps mean (total)", f"{_f(h['steps_mean'])} ({h['steps_total']})"),
        ("illegal actions per game (replies rejected, re-asks)",
         f"{_f(h['illegal_per_game'], 2)} ({h['illegal_replies_total']}, {h['retries_total']})"),
        ("crashes", str(h["crashes"])),
        ("stuck games", str(h.get("stuck_games", 0))),
        ("loop hints", str(h.get("loop_hints", 0))),
        ("api errors / refusals / timeouts / max_tokens stops",
         f"{h['api_errors']} / {h['refusals']} / {h['timeouts']} / {h['max_tokens_stops']}"),
        ("random fallbacks", str(h["fallbacks"])),
        ("tokens taken from a later reply line", str(h.get("late_line_tokens", 0))),
        ("api calls", str(h["api_calls"])),
        ("tokens input / cache write / cache read / output",
         f"{t['input_tokens']} / {t['cache_creation_input_tokens']} / {t['cache_read_input_tokens']} / {t['output_tokens']}"),
        ("cost USD (per game)", f"${h['cost_usd']:.4f} (${(h['cost_per_game_usd'] or 0):.4f})"),
        ("minutes per game (mean)", _f(h["minutes_per_game_mean"], 2)),
        ("replayable histories", f"{h['replayable']}/{h['games']}"),
    ]


def render_md(report: dict) -> str:
    h = report["headline"]
    lines = [f"# Eval report: {report.get('run_id') or report['out_dir']}", ""]
    lines.append(f"- seed set `{report.get('seedset')}` (A{report.get('ascension')}), policy `{report.get('policy')}`"
                 + (f", model `{report['model']}`, effort `{report.get('effort') or 'default'}`, "
                    f"thinking `{report.get('thinking')}`" if report.get("model") else ""))
    reveal = report.get("reveal_moves")
    lines.append(f"- server `{report.get('server_version')}`, repo `{report.get('repo_sha')}`, "
                 f"prompt `{report.get('prompt_hash')}`, enemy moves "
                 + ("`revealed`" if reveal else "`hidden`" if reveal is False else "`?` (not recorded)"))
    wall = report.get("wall_seconds")
    lines.append(f"- total wall time: {_f(wall / 60.0, 2) + ' min' if wall is not None else '-'}"
                 + (f"; games summarised {h['games']}/{report['expected_games']}" if report.get("expected_games") else ""))
    if report.get("aborted"):
        lines.append(f"- **aborted: {report['aborted']}**")
    lines += ["", "## Headline", "", "| metric | value |", "| --- | --- |"]
    lines += [f"| {k} | {v} |" for k, v in _headline_rows(h)]
    lines += ["", "## Per character", "",
              "| character | games | wins | win rate [95%] | floor mean | max | act reached | steps mean | illegal/game | cost USD | min/game |",
              "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for ch, a in report["per_character"].items():
        lo, hi = a["win_rate_ci95"]
        acts = ", ".join(f"{k}:{v}" for k, v in a["act_reached"].items())
        lines.append(f"| {ch} | {a['games']} | {a['wins']} | {_f(a['win_rate'], 0, True)} [{_f(lo, 0, True)}, {_f(hi, 0, True)}] "
                     f"| {_f(a['floor_mean'])} | {_f(a['floor_max'])} | {acts} | {_f(a['steps_mean'])} "
                     f"| {_f(a['illegal_per_game'], 2)} | {a['cost_usd']:.4f} | {_f(a['minutes_per_game_mean'], 2)} |")
    lines += ["", "## Games", "",
              "| seed | character | result | floor | act | steps | illegal | cost USD | minutes | cause |",
              "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for g in sorted(report["games"], key=lambda g: (g["character"] or "", g["seed"] or "")):
        cause = (g.get("cause") or "").replace("|", "/")
        lines.append(f"| {g['seed']} | {g['character']} | {g['result']} | {_f(g['floor'])} | {_f(g['act'])} "
                     f"| {g['steps']} | {g['illegal']} | {(g['cost_usd'] or 0):.4f} | {g['minutes']:.2f} | {cause} |")
    prices = report.get("prices") or {}
    if report.get("model") and prices.get("models", {}).get(report["model"]):
        mp = prices["models"][report["model"]]
        lines += ["", f"Prices used (USD/MTok, {report['model']}): input {mp['input']}, output {mp['output']}, "
                      f"cache write {mp['cache_write']}, cache read {mp['cache_read']}."]
    lines.append("")
    return "\n".join(lines)


def write_report(out_dir: Path) -> dict:
    out_dir = Path(out_dir)
    report = build_report(out_dir)
    with open(out_dir / "report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    (out_dir / "report.md").write_text(render_md(report), encoding="utf-8")
    return report
