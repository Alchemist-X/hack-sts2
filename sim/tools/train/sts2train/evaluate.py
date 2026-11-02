"""NN entry point of the STS2-Bench protocol v0.2 (docs/sts2-bench-protocol-v0.2-2026-11-01.md §7 "NN").

Plays a seed set with a checkpoint's greedy policy through sts2vec: one pool entry and one env slot per game
(`pool.add_fresh(character, seed, ascension, content)`), no auto-reset, every slot run to its end. Scoring is the
protocol's own (`sts2eval.progress.compute` from the final act/floor plus the act start floor and act room count read
from the aux block), and the run directory has the LLM runner's layout (`config.json`, `run.json`,
`games/<CHARACTER>-<SEED>.summary.json`) so `sts2eval report DIR` and this command write the same `report.md`.

Limits follow the protocol's standard values: step_cap 1200 (`capped`) and floor_step_cap 80 non-combat steps on one
floor (`stuck`). The LLM-only limits (loop hints, wall/step timeouts, cost) do not apply to a local NN policy.
"""

from __future__ import annotations

import json
import subprocess
import time
from pathlib import Path

import numpy as np
import torch
from sts2eval import progress as PG
from sts2eval import report as R
from sts2eval import seedsets as SS
from sts2vec import STATUS, VecEnv

from .features import bind_spec, obs_to_torch
from .ppo import load_checkpoint
from .progress import ProgressTracker

STEP_CAP = 1200
FLOOR_STEP_CAP = 80


def repo_sha() -> str | None:
    try:
        root = Path(__file__).resolve().parents[4]
        sha = subprocess.run(["git", "-C", str(root), "rev-parse", "--short", "HEAD"], capture_output=True, text=True,
                             check=True).stdout.strip()
        dirty = subprocess.run(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=no"],
                               capture_output=True, text=True, check=True).stdout.strip()
        return sha + ("-dirty" if dirty else "")
    except (OSError, subprocess.CalledProcessError, IndexError):
        return None


def replay(ss: dict, actions_log: list[list[int]], step_cap: int, threads: int = 8) -> list[tuple]:
    """Re-play each game's recorded candidate indices on a fresh env; -> (ended, result, act, floor) per game."""
    games = ss["games"]
    n = len(games)
    env = VecEnv(n, threads=threads, step_cap=step_cap, auto_reset=False, seed_text="REPLAY", content=ss["content"],
                 pool=None)
    try:
        for g in games:
            env.pool.add_fresh(g["character"], seed=g["seed"], ascension=ss["ascension"], content=ss["content"])
        obs = env.reset(start_ids=np.arange(n, dtype=np.int32))
        le_cols = {f: j for j, f in enumerate(env.schema["last_episode"].fields)}
        aux_cols = {f: j for j, f in enumerate(env.schema["aux"].fields)}
        out: list[tuple | None] = [None] * n
        for t in range(max((len(a) for a in actions_log), default=0)):
            a = np.array([acts[t] if t < len(acts) and out[i] is None else -1 for i, acts in enumerate(actions_log)],
                         dtype=np.int32)
            obs, rew, term, trunc, status, info = env.step(a)
            for i in np.flatnonzero((term | trunc).astype(bool)):
                if out[i] is None:
                    le = info["last_episode"][i]
                    res = ("win" if rew[i] > 0 else "loss") if term[i] else (
                        "crashed" if status[i] & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]) else "capped")
                    out[i] = (True, res, int(le[le_cols["act"]]) + 1, int(le[le_cols["floor"]]))
        for i in range(n):
            if out[i] is None:
                out[i] = (False, None, int(obs["aux"][i, aux_cols["act_index"]]) + 1,
                          int(obs["aux"][i, aux_cols["total_floor"]]))
        return out
    finally:
        env.close()


def evaluate(seedset: str, checkpoint: str, out_dir: str, *, threads: int = 8, device: str = "cpu",
             step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP, log=print) -> dict:
    ss = SS.load(seedset)
    games = ss["games"]
    n = len(games)
    out = Path(out_dir)
    (out / "games").mkdir(parents=True, exist_ok=True)
    model, spec, ck = load_checkpoint(checkpoint, device)
    t_start = time.monotonic()
    env = VecEnv(n, threads=threads, step_cap=step_cap, auto_reset=False, seed_text="EVAL", content=ss["content"],
                 pool=None)
    if env.schema.encoder_version != spec.encoder_version:
        log(f"warning: checkpoint encoder_version {spec.encoder_version} != library {env.schema.encoder_version}")
    bind_spec(spec)
    for g in games:
        env.pool.add_fresh(g["character"], seed=g["seed"], ascension=ss["ascension"], content=ss["content"])
    obs = env.reset(start_ids=np.arange(n, dtype=np.int32))
    gi_cols = env.schema.cols("global_i")
    aux_cols = {f: i for i, f in enumerate(env.schema["aux"].fields)}
    tracker = ProgressTracker(env.schema["aux"].fields, env.schema["last_episode"].fields, n, ss["ascension"])
    tracker.observe(obs["aux"])
    kinds = env.schema.vocab.enums["RunActionKind"]
    phase_names = env.schema.vocab.enums["RunPhase"]
    end_turn = kinds.index_of("EndTurn") if hasattr(kinds, "index_of") else 1
    done = np.zeros(n, dtype=bool)
    result: list[dict | None] = [None] * n
    steps = np.zeros(n, dtype=np.int64)
    turns = np.zeros(n, dtype=np.int64)
    floor_seen = np.full(n, -1, dtype=np.int64)
    noncombat = np.zeros(n, dtype=np.int64)
    actions_log: list[list[int]] = [[] for _ in range(n)]
    t_games = np.zeros(n)
    illegal = np.zeros(n, dtype=np.int64)
    while not done.all():
        gi = obs["global_i"]
        floor = gi[:, gi_cols["total_floor"]].astype(np.int64)
        combat = gi[:, gi_cols["is_combat"]] != 0
        # floor_step_cap (protocol §5): the (cap + 1)-th non-combat decision on one floor ends the game as 'stuck'
        newf = floor != floor_seen
        floor_seen[newf] = floor[newf]
        noncombat[newf] = 0
        stuck = (~done) & (~combat) & (noncombat >= floor_step_cap) & (floor_step_cap > 0)
        for i in np.flatnonzero(stuck):
            a0 = int(obs["aux"][i, aux_cols["act_index"]])
            d = tracker.score(int(i), "stuck", a0, int(floor[i]))
            result[i] = dict(d, result="stuck", act=a0 + 1, floor=int(floor[i]),
                             stuck={"phase": phase_names.name(int(gi[i, gi_cols["decision_kind"]])), "floor": int(floor[i])},
                             final_hp=int(gi[i, gi_cols["hp"]]), max_hp=int(gi[i, gi_cols["max_hp"]]))
            done[i] = True
            t_games[i] = time.monotonic() - t_start
        # a live game with no candidate is an encoder/env error (the runner's 'error'), never a silent wait
        for i in np.flatnonzero((~done) & (obs["cand_n"] <= 0)):
            a0 = int(obs["aux"][i, aux_cols["act_index"]])
            result[i] = dict(tracker.score(int(i), "error", a0, int(floor[i])), result="error", act=a0 + 1,
                             floor=int(floor[i]), error="non-terminal slot with cand_n = 0")
            done[i] = True
            t_games[i] = time.monotonic() - t_start
        noncombat[(~combat) & (~done)] += 1
        if done.all():
            break
        x = obs_to_torch(obs, device)
        with torch.no_grad():
            logits, _ = model(x)
        a = logits.argmax(-1).cpu().numpy().astype(np.int32)
        live = (~done) & (obs["cand_n"] > 0)
        cand = obs["cand"]
        kind_col = env.schema.cols("cand")["kind"]
        for i in np.flatnonzero(live):
            actions_log[i].append(int(a[i]))
            if cand[i, a[i], kind_col] == end_turn:
                turns[i] += 1
        a[~live] = -1
        steps[live] += 1
        obs, rew, term, trunc, status, info = env.step(a)
        illegal[(status & STATUS["ILLEGAL_ACTION"]) != 0] += 1
        ended = (~done) & ((term | trunc).astype(bool))
        le_cols = {f: j for j, f in enumerate(env.schema["last_episode"].fields)}
        for i in np.flatnonzero(ended):
            le = info["last_episode"][i]
            st = int(status[i])
            if term[i]:
                res = "win" if rew[i] > 0 else "loss"
            elif st & (STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]):
                res = "crashed"
            else:
                res = "capped"
            a0 = int(le[le_cols["act"]])
            d = tracker.score(int(i), res, a0, int(le[le_cols["floor"]]))
            r = dict(d, result=res, act=a0 + 1, floor=int(le[le_cols["floor"]]), final_hp=int(le[le_cols["final_hp"]]),
                     max_hp=int(le[le_cols["final_max_hp"]]), steps_env=int(le[le_cols["steps"]]))
            if res == "crashed":
                r["error"] = env.last_error(int(i))[:2000]
            if res == "capped":
                r["cap_reason"] = "steps"
            if res == "loss":
                enc = int(le[le_cols["last_encounter_id"]])
                r["death"] = {"floor": r["floor"], "act": r["act"], "room_type": None,
                              "enemies": [env.schema.vocab.encounters.name(enc)] if enc > 0 else []}
            result[i] = r
            done[i] = True
            t_games[i] = time.monotonic() - t_start
        live_after = ~done
        tracker.observe(obs["aux"], live_after)
    wall = time.monotonic() - t_start
    env.close()
    # Audit (protocol §6.2 analogue): replay every game's candidate indices on a fresh env; the end must match.
    rep = replay(ss, actions_log, step_cap, threads)
    replay_ok = []
    for i in range(n):
        r = result[i] or {}
        ended, res, act, floor = rep[i]
        if r.get("result") in ("stuck", "error"):
            replay_ok.append((not ended) and act == r.get("act") and floor == r.get("floor"))
        else:
            replay_ok.append(ended and res == r.get("result") and act == r.get("act") and floor == r.get("floor"))
    log(f"replay check: {sum(replay_ok)}/{n} games reproduce their end")

    wh = ck.get("weights_hash") or "?"
    lib_version = f"sts2vec/{spec.encoder_version}"
    for i, g in enumerate(games):
        r = result[i] or {}
        key = SS.game_key(g)
        s = {"key": key, "seed": g["seed"], "character": g["character"], "ascension": ss["ascension"],
             "content": ss["content"], "policy": f"nn:{Path(checkpoint).name}", "result": r.get("result"),
             "act": r.get("act"), "floor": r.get("floor"), "steps": int(steps[i]), "turns": int(turns[i]),
             "illegal": int(illegal[i]), "illegal_replies": 0, "retries": 0, "fallbacks": 0, "loop_hints": 0, "api_calls": 0,
             "api_errors": 0, "refusals": 0, "timeouts": 0, "max_tokens_stops": 0, "cost_usd": 0.0,
             "seconds": round(float(t_games[i]), 3), "final_hp": r.get("final_hp"), "max_hp": r.get("max_hp"),
             "death": r.get("death"), "error": r.get("error"), "cap_reason": r.get("cap_reason"),
             "stuck": r.get("stuck"), "truncated": r.get("result") in ("capped", "stuck"),
             "reward": 1 if r.get("result") == "win" else 0,
             "act_start_floor": r.get("act_start_floor"), "act_rooms": r.get("act_rooms"),
             "progress_pct": round(100.0 * (r.get("floor") or 0) / PG.full_run_floors(ss["ascension"]), 2),
             "history_actions": actions_log[i],
             # Replay = same seed/character/ascension/content entry + these candidate indices through sts2vec.
             "replayable": bool(replay_ok[i]), "weights_hash": wh, "server_version": lib_version}
        s.update(PG.compute(s["result"], s["act"], s["floor"], ss["ascension"], s["act_start_floor"], s["act_rooms"]))
        with open(out / "games" / f"{key}.summary.json", "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1)
    cfg = {"run_id": out.name, "protocol_version": PG.PROTOCOL_VERSION, "seedset": ss, "policy": f"nn:{Path(checkpoint).name}",
           "model": "sts2train-smoke entity-encoder PPO", "effort": None, "thinking": None,
           "server_version": lib_version, "repo_sha": repo_sha(), "prompt_hash": f"weights:{wh}",
           "reveal_moves": False, "limits": {"step_cap": step_cap, "floor_step_cap": floor_step_cap},
           "checkpoint": str(checkpoint), "weights_hash": wh, "params": sum(p.numel() for p in model.parameters()),
           "device": device, "greedy": True}
    with open(out / "config.json", "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=1)
    with open(out / "run.json", "w", encoding="utf-8") as f:
        json.dump({"wall_seconds": round(wall, 2), "abort_reason": None}, f, indent=1)
    rep = R.write_report(out)
    log(f"wrote {out / 'report.md'}: progress {rep['headline']['progress_mean']}, wins {rep['headline']['wins']}/{n}")
    return rep
