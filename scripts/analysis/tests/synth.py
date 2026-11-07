"""Synthetic protocol eval directories for the scripts/analysis tests (stdlib only)."""

from __future__ import annotations

import hashlib
import json
import os
import random
import statistics

HERE = os.path.dirname(os.path.abspath(__file__))
ANALYSIS = os.path.dirname(HERE)
SCRATCH = os.environ.get("STS2_ANALYSIS_DATA", "/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-"
                         "worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad")
ACT_ROOMS = {"1": 17, "2": 16, "3": 16}
ACT_START = {"1": 0, "2": 17, "3": 33}


def seedset(n: int, role: str = "dev", tag: str = "SYN") -> dict:
    return {"name": f"silent-a10-{role}-s{n}", "id": f"silent-a10-{role}-v1", "version": 1, "role": role,
            "protocol": "0.2", "ascension": 10, "content": "ported_only",
            "games": [{"seed": f"{tag}{i:05d}", "character": "SILENT"} for i in range(n)]}


def game(seed: str, total_floor: int, won: bool = False, stuck: bool = False) -> dict:
    """A game summary ending at total floor `total_floor` (act from the standard act lengths)."""
    total_floor = max(1, min(49, int(total_floor)))
    act = 1 if total_floor <= 17 else 2 if total_floor <= 33 else 3
    s = {"key": f"SILENT-{seed}", "seed": seed, "character": "SILENT", "ascension": 10,
         "result": "win" if won else "stuck" if stuck else "loss", "act": act, "floor": total_floor,
         "act_start_floor": {k: v for k, v in ACT_START.items() if int(k) <= act},
         "act_rooms": {k: v for k, v in ACT_ROOMS.items() if int(k) <= act}, "seconds": 1.0, "steps": 10}
    # stored progress as sts2vec writes it (eval_summary recomputes it; tests check they agree)
    import eval_summary as ES
    s["progress"] = ES.compute_progress(s["result"], act, total_floor, 10, s["act_start_floor"], s["act_rooms"])
    return s


def write_eval(root: str, run: str, update: int, floors: list[int], *, role: str = "dev", mode: str = "greedy-off",
               endpoint: bool = False, report: bool = True, tag: str = "SYN") -> str:
    n = len(floors)
    ss = seedset(n, role, tag)
    if endpoint:
        d = os.path.join(root, run, "evals", "endpoint", f"u{update:07d}-{role}{n}-{mode}")
    else:
        d = os.path.join(root, run, "evals", f"u{update:07d}", mode)
    os.makedirs(os.path.join(d, "games"), exist_ok=True)
    pol, _, br = mode.partition("-")
    cfg = {"run_id": mode, "protocol_version": "0.2", "seedset": ss, "policy": f"nn:pinned-{update:07d}.pt",
           "checkpoint": f"/content/sts2/runs/{run}/checkpoints/pinned-{update:07d}.pt",
           "weights_hash": hashlib.sha256(f"{run}:{update}".encode()).hexdigest()[:16], "greedy": pol == "greedy",
           "loop_breaker": {"mode": br}, "eval_mode": {"policy": pol, "breaker": br, "name": mode,
                                                       "sample_seed": None, "repeat": None},
           "limits": {"step_cap": 1200, "floor_step_cap": 80, "macros": ["rest", "shop", "prompt"]}}
    with open(os.path.join(d, "config.json"), "w") as f:
        json.dump(cfg, f)
    games = []
    for g, fl in zip(ss["games"], floors):
        s = game(g["seed"], fl, won=fl >= 49)
        games.append(s)
        with open(os.path.join(d, "games", f"{s['key']}.summary.json"), "w") as f:
            json.dump(s, f)
    with open(os.path.join(d, "run.json"), "w") as f:
        json.dump({"wall_seconds": 12.5, "abort_reason": None}, f)
    if report:
        prog = [s["progress"] for s in sorted(games, key=lambda s: s["key"])]
        with open(os.path.join(d, "report.json"), "w") as f:
            json.dump({"out_dir": f"/content/sts2/runs/{run}/evals/u{update:07d}/{mode}",
                       "prompt_hash": f"weights:{cfg['weights_hash']}",
                       "headline": {"games": n, "progress_mean": statistics.fmean(prog)}}, f)
    return d


def base_floors(n: int, seed: int = 1) -> list[int]:
    rng = random.Random(seed)
    return [rng.choice([9, 14, 17, 17, 20, 24, 28, 33, 33, 36]) for _ in range(n)]


def jitter(floors: list[int], rng: random.Random, shift: int = 0, sd: float = 3.0) -> list[int]:
    return [max(1, min(48, int(round(f + shift + rng.gauss(0, sd))))) for f in floors]


def make_line(root: str, run: str, updates: list[int], n: int = 128, shift: int = 0, seed: int = 0,
              role: str = "dev", **kw) -> None:
    """One run: a per-100 eval of the same seed set at each update; per-seed base + shift + checkpoint jitter."""
    base = base_floors(n)
    rng = random.Random(seed)
    for u in updates:
        write_eval(root, run, u, jitter(base, rng, shift), role=role, **kw)


def rj(path: str):
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def wj(path: str, obj) -> None:
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f)
