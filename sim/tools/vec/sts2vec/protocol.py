"""STS2-Bench protocol v0.2 for sts2vec: progress metric, seed sets, run report (docs/sts2-bench-protocol-v0.2-2026-11-01.md).

Vendored from ``sim/tools/eval`` (``sts2eval.progress`` / ``seedsets`` / ``report``) so that ``sts2vec`` stays a
self-contained uv project: nothing here imports ``sts2eval``. ``tests/test_protocol.py`` loads
``sim/tools/eval/sts2eval/progress.py`` by file path and asserts that ``compute`` returns exactly the same fields on a
fixture grid, so the two copies cannot drift silently.

Progress (§4.2): an act ``a`` with ``L`` rooms (its map's last row + 1, start and boss rows included);
``k`` = rooms entered in the act = ``floor - act_start_floor`` (act 1: ``k = floor``); ``c = max(0, k - 1)`` rooms
cleared (the room the run ended in does not count); ``progress = 1.0`` on a win, else ``((a - 1) + min(c, L - 1) / L) / 3``.
"""

from __future__ import annotations

import hashlib
import json
import math
import os
import re
import statistics
from collections import Counter
from pathlib import Path

PROTOCOL_VERSION = "0.2"
ACTS = 3
# Rooms per act incl. the start (Ancient) and boss rows; A10 (DoubleBoss) adds a second boss row to act 3.
DEFAULT_ACT_ROOMS = {1: 17, 2: 16, 3: 15}
DOUBLE_BOSS_ASCENSION = 10
CHARACTERS = ("IRONCLAD", "SILENT", "DEFECT", "REGENT", "NECROBINDER")
RESULTS = ("win", "loss", "capped", "stuck", "crashed", "aborted", "error")

# ------------------------------------------------------------------------------------------------ progress


def default_act_rooms(act: int, ascension: int) -> int:
    n = DEFAULT_ACT_ROOMS.get(act, DEFAULT_ACT_ROOMS[ACTS])
    if act == ACTS and ascension >= DOUBLE_BOSS_ASCENSION:
        n += 1
    return n


def full_run_floors(ascension: int) -> int:
    """Floors of a standard full run (no GOLDEN_COMPASS): 48 at A0, 49 at A10+ (second boss)."""
    return sum(default_act_rooms(a, ascension) for a in range(1, ACTS + 1))


def _int_keys(d: dict | None) -> dict[int, int]:
    return {int(k): v for k, v in (d or {}).items()}


def compute(result: str | None, act: int | None, floor: int | None, ascension: int,
            act_start_floor: dict | None = None, act_rooms: dict | None = None) -> dict:
    """The v0.2 progress fields of one game from its final (result, act 1-based, floor) and what it saw per act
    (``act_start_floor[act]`` = total floor at the act's first observation, ``act_rooms[act]`` = L)."""
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


# ------------------------------------------------------------------------------------------------ seed sets

_SEED_RE = re.compile(r"^[A-Z0-9]{1,10}$")


class SeedSetError(ValueError):
    pass


def seedset_dirs() -> list[Path]:
    """Where named seed sets are looked up: ``$STS2VEC_SEEDSETS`` (os.pathsep-separated), then the committed protocol
    sets in ``sim/tools/eval/seedsets`` (read as data files; the eval package is not imported)."""
    dirs = [Path(p) for p in os.environ.get("STS2VEC_SEEDSETS", "").split(os.pathsep) if p]
    dirs.append(Path(__file__).resolve().parents[2] / "eval" / "seedsets")
    return [d for d in dirs if d.is_dir()]


def checksum(data: dict) -> str:
    """sha256 of the canonical content {"ascension","content","games":[{"character","seed"}]} (sorted-key compact
    JSON, games in file order) — the protocol's seed-set identity."""
    canon = {"ascension": data["ascension"], "content": data["content"],
             "games": [{"character": g["character"], "seed": g["seed"]} for g in data["games"]]}
    return hashlib.sha256(json.dumps(canon, sort_keys=True, separators=(",", ":")).encode("utf-8")).hexdigest()


def validate_seedset(data: dict, origin: str = "<inline>") -> dict:
    for key in ("name", "ascension", "content", "games"):
        if key not in data:
            raise SeedSetError(f"{origin}: missing key {key!r}")
    if not isinstance(data["ascension"], int) or not 0 <= data["ascension"] <= 10:
        raise SeedSetError(f"{origin}: ascension must be an int in 0..10")
    if data["content"] not in ("ported_only", "all"):
        raise SeedSetError(f"{origin}: content must be 'ported_only' or 'all'")
    games = data["games"]
    if not isinstance(games, list) or not games:
        raise SeedSetError(f"{origin}: games must be a non-empty list")
    seen = set()
    for i, g in enumerate(games):
        seed, ch = g.get("seed"), g.get("character")
        if not isinstance(seed, str) or not _SEED_RE.match(seed):
            raise SeedSetError(f"{origin}: games[{i}].seed {seed!r} must be 1-10 uppercase alphanumerics")
        if ch not in CHARACTERS:
            raise SeedSetError(f"{origin}: games[{i}].character {ch!r} not in {CHARACTERS}")
        if (ch, seed) in seen:
            raise SeedSetError(f"{origin}: duplicate game {ch} {seed}")
        seen.add((ch, seed))
    if data.get("sha256") is not None and data["sha256"] != checksum(data):
        raise SeedSetError(f"{origin}: sha256 {data['sha256']} does not match the content ({checksum(data)})")
    return data


def load_seedset(name_or_path: str) -> dict:
    """A seed set by file name stem (``a0-s20``), id (``std-a0-v1``) or path to a JSON file."""
    p = Path(name_or_path)
    if p.exists():
        with open(p, encoding="utf-8") as f:
            return validate_seedset(json.load(f), str(p))
    for d in seedset_dirs():
        q = d / f"{name_or_path}.json"
        if q.exists():
            return load_seedset(str(q))
        for q in sorted(d.glob("*.json")):
            try:
                with open(q, encoding="utf-8") as f:
                    if json.load(f).get("id") == name_or_path:
                        return load_seedset(str(q))
            except (OSError, ValueError):
                continue
    avail = sorted({x.stem for d in seedset_dirs() for x in d.glob("*.json")})
    raise SeedSetError(f"seed set {name_or_path!r} not found (known: {', '.join(avail) or 'none'})")


def seedset_identity(data: dict | None) -> dict:
    """id / version / sha256 of a seed set and whether its content matches a frozen (sha256-carrying) committed set."""
    none = {"seedset_id": None, "seedset_version": None, "seedset_sha256": None, "seedset_frozen": False}
    if not data or not data.get("games"):
        return none
    sha = checksum(data)
    frozen = None
    for d in seedset_dirs():
        for q in sorted(d.glob("*.json")):
            try:
                with open(q, encoding="utf-8") as f:
                    x = json.load(f)
            except (OSError, ValueError):
                continue
            if x.get("sha256") == sha:
                frozen = x
                break
        if frozen:
            break
    return {"seedset_id": (frozen or {}).get("id") or data.get("id") or data.get("name"),
            "seedset_version": (frozen or {}).get("version") or data.get("version"),
            "seedset_sha256": sha, "seedset_frozen": frozen is not None}


def game_key(game: dict) -> str:
    return f"{game['character']}-{game['seed']}"


# ------------------------------------------------------------------------------------------------ report


def wilson(wins: int, n: int, z: float = 1.96) -> tuple[float, float]:
    if n == 0:
        return (0.0, 0.0)
    p = wins / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return (max(0.0, centre - half), min(1.0, centre + half))


def mean_ci95(xs: list[float], z: float = 1.96) -> tuple[float, float]:
    """Normal-approximation 95% interval of a mean, clipped to [0, 1]; (mean, mean) for n < 2."""
    if not xs:
        return (0.0, 0.0)
    m = statistics.fmean(xs)
    if len(xs) < 2:
        return (m, m)
    half = z * statistics.stdev(xs) / math.sqrt(len(xs))
    return (max(0.0, m - half), min(1.0, m + half))


def _mean(xs):
    return statistics.fmean(xs) if xs else None


def aggregate(summaries: list[dict], ascension: int = 0) -> dict:
    """Headline numbers of a set of per-game summaries (same keys as ``sts2eval.report.aggregate`` for the fields a
    local policy produces)."""
    n = len(summaries)
    results = Counter(s.get("result") for s in summaries)
    wins = results.get("win", 0)
    lo, hi = wilson(wins, n)
    floors = [s["floor"] for s in summaries if s.get("floor") is not None]
    prog = [s["progress"] for s in summaries if s.get("progress") is not None]
    plo, phi = mean_ci95(prog)
    total = full_run_floors(ascension)
    secs = [s["seconds"] for s in summaries if s.get("seconds") is not None]
    return {
        "games": n, "results": {r: results.get(r, 0) for r in RESULTS}, "wins": wins,
        "win_rate": wins / n if n else None, "win_rate_ci95": [lo, hi],
        "progress_mean": _mean(prog), "progress_ci95": [plo, phi] if prog else [None, None],
        "progress_median": statistics.median(prog) if prog else None, "progress_games": len(prog),
        "act_rooms_default": sum(1 for s in summaries if s.get("act_rooms_source") == "default"),
        "floor_mean": _mean(floors), "floor_median": statistics.median(floors) if floors else None,
        "floor_max": max(floors) if floors else None,
        "progress_mean_pct": (100.0 * _mean(floors) / total) if floors else None, "full_run_floors": total,
        "act_reached": dict(sorted(Counter(s.get("act") for s in summaries if s.get("act") is not None).items())),
        "steps_mean": _mean([s.get("steps", 0) for s in summaries]),
        "steps_total": sum(s.get("steps", 0) for s in summaries),
        "turns_mean": _mean([s.get("turns", 0) for s in summaries]),
        "illegal_total": sum(s.get("illegal", 0) for s in summaries),
        "illegal_per_game": (sum(s.get("illegal", 0) for s in summaries) / n) if n else None,
        "crashes": results.get("crashed", 0), "stuck_games": results.get("stuck", 0),
        "capped_games": results.get("capped", 0),
        "minutes_per_game_mean": (_mean(secs) / 60.0) if secs else None,
        "replayable": sum(1 for s in summaries if s.get("replayable")),
    }


def load_summaries(out_dir: Path) -> list[dict]:
    out = []
    for p in sorted((Path(out_dir) / "games").glob("*.summary.json")):
        with open(p, encoding="utf-8") as f:
            s = json.load(f)
        s.update(compute(s.get("result"), s.get("act"), s.get("floor"), int(s.get("ascension") or 0),
                         s.get("act_start_floor"), s.get("act_rooms")))
        out.append(s)
    return out


def _read_json(p: Path):
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def build_report(out_dir: Path) -> dict:
    out_dir = Path(out_dir)
    cfg = _read_json(out_dir / "config.json") or {}
    run = _read_json(out_dir / "run.json") or {}
    summaries = load_summaries(out_dir)
    ss = cfg.get("seedset") or {}
    asc = ss.get("ascension", summaries[0].get("ascension", 0) if summaries else 0)
    per_char = {ch: aggregate([s for s in summaries if s.get("character") == ch], asc)
                for ch in CHARACTERS if any(s.get("character") == ch for s in summaries)}
    return {
        "protocol_version": PROTOCOL_VERSION, "run_protocol_version": cfg.get("protocol_version"),
        "run_id": cfg.get("run_id"), "out_dir": str(out_dir), "seedset": ss.get("name"), **seedset_identity(ss),
        "ascension": asc, "policy": cfg.get("policy"), "model": cfg.get("model"), "effort": cfg.get("effort"),
        "thinking": cfg.get("thinking"), "server_version": cfg.get("server_version"), "repo_sha": cfg.get("repo_sha"),
        "prompt_hash": cfg.get("prompt_hash"), "obs_mode": cfg.get("obs_mode"), "reveal_moves": cfg.get("reveal_moves"),
        "expected_games": len(ss.get("games") or []) or None, "aborted": run.get("abort_reason"),
        "wall_seconds": run.get("wall_seconds"), "headline": aggregate(summaries, asc), "per_character": per_char,
        "games": [_game_row(s) for s in summaries],
    }


def _game_row(s: dict) -> dict:
    death = s.get("death") or {}
    cause = None
    if s.get("result") == "loss":
        cause = "/".join(death.get("enemies") or []) or "?"
    elif s.get("result") == "crashed":
        cause = (s.get("error") or "")[:120]
    elif s.get("result") == "stuck":
        st = s.get("stuck") or {}
        cause = f"stuck: {st.get('phase') or '?'} floor {st.get('floor', s.get('floor'))}"
    elif s.get("result") in ("capped", "error"):
        cause = s.get("cap_reason") or (s.get("error") or "")[:120]
    return {"seed": s.get("seed"), "character": s.get("character"), "result": s.get("result"), "floor": s.get("floor"),
            "act": s.get("act"), "progress": s.get("progress"), "rooms_cleared": s.get("rooms_cleared"),
            "act_rooms": s.get("act_rooms_current"), "act_rooms_source": s.get("act_rooms_source"),
            "steps": s.get("steps"), "turns": s.get("turns"), "illegal": s.get("illegal"),
            "minutes": round((s.get("seconds") or 0) / 60.0, 2), "cause": cause, "replayable": s.get("replayable")}


def _f(x, nd=1, pct=False):
    if x is None:
        return "-"
    if pct:
        return f"{100 * x:.{nd}f}%"
    if isinstance(x, float):
        return f"{x:.{nd}f}"
    return str(x)


def render_md(report: dict) -> str:
    h = report["headline"]
    sha = report.get("seedset_sha256")
    lo, hi = h["win_rate_ci95"]
    plo, phi = h.get("progress_ci95") or (None, None)
    lines = [f"# Eval report: {report.get('run_id') or report['out_dir']}", "",
             f"- protocol v{report.get('protocol_version')} metric (sts2vec evaluator); seed set id "
             f"`{report.get('seedset_id')}` v{report.get('seedset_version') or '?'}, "
             + (f"sha256 `{sha[:16]}`" if sha else "sha256 `?`")
             + (", frozen" if report.get("seedset_frozen") else ", not a frozen set"),
             f"- seed set `{report.get('seedset')}` (A{report.get('ascension')}), policy `{report.get('policy')}`",
             f"- env `{report.get('server_version')}`, repo `{report.get('repo_sha')}`, policy hash "
             f"`{report.get('prompt_hash')}`, enemy moves hidden",
             f"- total wall time: {_f((report.get('wall_seconds') or 0) / 60.0, 2)} min; games summarised "
             f"{h['games']}/{report.get('expected_games') or '?'}", "",
             "## Headline", "", "| metric | value |", "| --- | --- |",
             f"| games | {h['games']} |",
             f"| **progress v0.2** (mean [95% CI], median) | **{_f(h.get('progress_mean'), 3)}** [{_f(plo, 3)}, "
             f"{_f(phi, 3)}], {_f(h.get('progress_median'), 3)} |",
             f"| **win rate** (Wilson 95%) | **{_f(h['win_rate'], 1, True)}** [{_f(lo, 1, True)}, {_f(hi, 1, True)}] |",
             f"| results | {', '.join(f'{k} {v}' for k, v in h['results'].items() if v) or '-'} |",
             f"| floor mean / median / max | {_f(h['floor_mean'])} / {_f(h['floor_median'])} / {_f(h['floor_max'])} |",
             f"| floor / {h['full_run_floors']} (v0.1 progress, secondary) | {_f(h['progress_mean_pct'])}% |",
             f"| act reached | {', '.join(f'act {a}: {c}' for a, c in h['act_reached'].items()) or '-'} |",
             f"| steps mean (total) | {_f(h['steps_mean'])} ({h['steps_total']}) |",
             f"| turns mean | {_f(h['turns_mean'])} |",
             f"| illegal actions per game | {_f(h['illegal_per_game'], 2)} |",
             f"| crashes / stuck / capped | {h['crashes']} / {h['stuck_games']} / {h['capped_games']} |",
             f"| replayable histories (replay audit) | {h['replayable']}/{h['games']} |",
             "", "## Per character", "",
             "| character | games | progress | wins | floor mean | max | act reached | steps mean |",
             "| --- | --- | --- | --- | --- | --- | --- | --- |"]
    for ch, a in report["per_character"].items():
        acts = ", ".join(f"{k}:{v}" for k, v in a["act_reached"].items())
        lines.append(f"| {ch} | {a['games']} | {_f(a.get('progress_mean'), 3)} | {a['wins']} | {_f(a['floor_mean'])} "
                     f"| {_f(a['floor_max'])} | {acts} | {_f(a['steps_mean'])} |")
    lines += ["", "## Games", "", "| seed | character | result | progress | rooms cleared | floor | act | steps | illegal "
              "| replay | cause |", "| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |"]
    for g in sorted(report["games"], key=lambda g: (g["character"] or "", g["seed"] or "")):
        rooms = (f"{g['rooms_cleared']}/{g['act_rooms']}" + ("*" if g.get("act_rooms_source") == "default" else "")
                 if g.get("act_rooms") else "-")
        cause = (g.get("cause") or "").replace("|", "/")
        lines.append(f"| {g['seed']} | {g['character']} | {g['result']} | {_f(g.get('progress'), 3)} | {rooms} "
                     f"| {_f(g['floor'])} | {_f(g['act'])} | {g['steps']} | {g['illegal']} "
                     f"| {'ok' if g.get('replayable') else 'FAIL'} | {cause} |")
    lines += ["", "Progress v0.2 = ((act - 1) + rooms cleared in the act / rooms in the act) / 3, 1.0 for a win; "
                  "rooms cleared = rooms entered in the act - 1. `*` = act length from the standard table.", ""]
    return "\n".join(lines)


def write_report(out_dir: Path) -> dict:
    out_dir = Path(out_dir)
    report = build_report(out_dir)
    with open(out_dir / "report.json", "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False, indent=1)
    (out_dir / "report.md").write_text(render_md(report), encoding="utf-8")
    return report
