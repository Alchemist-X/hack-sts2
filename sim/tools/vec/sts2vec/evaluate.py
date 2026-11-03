"""Policy-agnostic protocol evaluator: ``sts2vec eval`` (STS2-Bench protocol v0.2, NN / programmatic track).

Plays every game of a seed set with one policy: one pool entry and one env slot per game
(``pool.add_fresh(character, seed, ascension, content)``), ``auto_reset=False``, every slot run to its end, all slots
stepped together in one batched call. Standard limits (protocol §5): ``step_cap`` 1200 decisions (``capped``) and
``floor_step_cap`` 80 non-combat decisions on one floor (``stuck``). Both are enforced by the env itself
(``VecEnv(step_cap=..., floor_step_cap=...)``; the floor cap ends the game with status ``FLOOR_STALL``), so training
envs and the evaluator share one implementation and one default (``sts2vec.FLOOR_STEP_CAP``). Every live game decides
every step: a negative action from the policy for a live game is sent as an illegal index (counted in ``illegal`` and,
at a non-combat point, toward the floor cap), so a no-op loop ends ``stuck`` like any other stall.

Results per game: ``win`` / ``loss`` (terminal), ``capped`` (step cap), ``stuck`` (floor step cap), ``crashed``
(SIM_ERROR / CONTENT_GAP), ``error`` (a live slot without candidates). Scoring is ``protocol.compute`` on the final
(act, floor) with the act's start floor (``aux.total_floor - aux.act_floor`` at the act's first observation) and room
count (``aux.act_rooms``).

Output (the LLM runner's layout, so ``sts2eval report DIR`` re-renders it): ``config.json``, ``run.json``,
``games/<CHARACTER>-<SEED>.summary.json`` (with ``history_actions`` = candidate indices), ``report.json``, ``report.md``.

Replay audit: every game's recorded candidate indices are replayed on a fresh env; result, act, floor and the final
``StateHash`` must match (``replayable``). This is the determinism guarantee checked on every evaluation.
"""

from __future__ import annotations

import json
import subprocess
import time
from pathlib import Path
from typing import Callable

import numpy as np

from . import protocol as PG
from .env import FLOOR_STEP_CAP, VecEnv
from .policies import PolicyContext, game_rng, resolve
from .schema import STATUS

STEP_CAP = 1200
ERROR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
# what a live game's negative (no-op) action is sent as: an index outside [0, cand_n), i.e. ILLEGAL_ACTION
ILLEGAL_INDEX = 2**31 - 1

__all__ = ["FLOOR_STEP_CAP", "STEP_CAP", "evaluate", "replay"]


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


def _make_env(ss: dict, threads: int, step_cap: int, floor_step_cap: int, dll=None) -> VecEnv:
    n = len(ss["games"])
    env = VecEnv(n, threads=threads, step_cap=step_cap, floor_step_cap=floor_step_cap, auto_reset=False,
                 seed_text="EVAL", content=ss["content"], pool=None, dll=dll)
    try:
        for g in ss["games"]:
            env.pool.add_fresh(g["character"], seed=g["seed"], ascension=ss["ascension"], content=ss["content"])
    except BaseException:
        env.close()
        raise
    return env


def _end_result(term: int, reward: float, status: int) -> str:
    if term:
        return "win" if reward > 0 else "loss"
    if status & ERROR_BITS:
        return "crashed"
    return "stuck" if status & STATUS["FLOOR_STALL"] else "capped"


def replay(ss: dict, actions_log: list[list[int]], *, step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP,
           threads: int = 8, dll=None) -> list[dict]:
    """Re-play each game's recorded candidate indices on a fresh env (same limits as the evaluation).

    Returns per game ``{"ended", "result", "act", "floor", "state_hash"}`` where ``state_hash`` is taken right after
    the game's last recorded action (or after its terminal / truncating step)."""
    n = len(ss["games"])
    env = _make_env(ss, threads, step_cap, floor_step_cap, dll)
    try:
        obs = env.reset(start_ids=np.arange(n, dtype=np.int32))
        le = env.schema.cols("last_episode")
        ax = env.schema.cols("aux")
        out: list[dict | None] = [None] * n
        lens = [len(a) for a in actions_log]
        for i in range(n):          # games with no recorded action end where they started
            if lens[i] == 0:
                out[i] = {"ended": False, "result": None, "act": int(obs["aux"][i, ax.act_index]) + 1,
                          "floor": int(obs["aux"][i, ax.total_floor]), "state_hash": int(env.state_hash([i])[0])}
        for t in range(max(lens, default=0)):
            a = np.array([acts[t] if t < len(acts) and out[i] is None else -1 for i, acts in enumerate(actions_log)],
                         dtype=np.int32)
            obs, rew, term, trunc, status, info = env.step(a)
            for i in range(n):
                if out[i] is not None or t >= lens[i]:
                    continue
                if term[i] or trunc[i]:
                    row = info["last_episode"][i]
                    out[i] = {"ended": True, "result": _end_result(int(term[i]), float(rew[i]), int(status[i])),
                              "act": int(row[le.act]) + 1, "floor": int(row[le.floor]),
                              "state_hash": int(env.state_hash([i])[0])}
                elif t == lens[i] - 1:
                    out[i] = {"ended": False, "result": None, "act": int(obs["aux"][i, ax.act_index]) + 1,
                              "floor": int(obs["aux"][i, ax.total_floor]), "state_hash": int(env.state_hash([i])[0])}
        return out
    finally:
        env.close()


def evaluate(seedset: str | dict, policy, out_dir: str | Path, *, threads: int = 8, step_cap: int = STEP_CAP,
             floor_step_cap: int = FLOOR_STEP_CAP, seed: int = 0, policy_name: str | None = None,
             policy_hash: str | None = None, config_extra: dict | None = None, summary_extra: dict | None = None,
             log: Callable[[str], None] = print, dll=None) -> dict:
    """Play ``seedset`` (name, id, path or dict) with ``policy`` (built-in name, ``module:callable``,
    ``file.py:callable`` or a callable; see ``sts2vec.policies``) and write the protocol run directory ``out_dir``.
    Returns the report dict (``report["headline"]["progress_mean"]`` is the protocol metric)."""
    ss = PG.validate_seedset(dict(seedset)) if isinstance(seedset, dict) else PG.load_seedset(seedset)
    fn, label, phash = resolve(policy)
    policy_name = policy_name or label
    policy_hash = policy_hash or phash
    games = ss["games"]
    n = len(games)
    keys = [PG.game_key(g) for g in games]
    out = Path(out_dir)
    (out / "games").mkdir(parents=True, exist_ok=True)
    t_start = time.monotonic()
    env = _make_env(ss, threads, step_cap, floor_step_cap, dll)
    schema = env.schema
    try:
        obs = env.reset(start_ids=np.arange(n, dtype=np.int32))
        G, AX, LE, CC = schema.cols("global_i"), schema.cols("aux"), schema.cols("last_episode"), schema.cols("cand")
        phase_names = schema.vocab.enums["RunPhase"]
        end_turn = schema.vocab.enums["RunActionKind"].index_of("EndTurn")
        ctx = PolicyContext(schema=schema, num_envs=n, live=np.ones(n, dtype=bool), keys=keys,
                            ascension=ss["ascension"], rngs=[game_rng(seed, k) for k in keys])
        starts: list[dict[int, int]] = [{} for _ in range(n)]
        rooms: list[dict[int, int]] = [{} for _ in range(n)]
        done = np.zeros(n, dtype=bool)
        result: list[dict | None] = [None] * n
        steps = np.zeros(n, dtype=np.int64)
        turns = np.zeros(n, dtype=np.int64)
        illegal = np.zeros(n, dtype=np.int64)
        actions_log: list[list[int]] = [[] for _ in range(n)]
        t_games = np.zeros(n)
        end_hash = np.zeros(n, dtype=np.uint64)

        def finish(i: int, res: str, act0: int, floor: int, **extra) -> None:
            d = PG.compute(res, act0 + 1, floor, ss["ascension"], starts[i], rooms[i])
            result[i] = dict(d, result=res, act=act0 + 1, floor=floor,
                             act_start_floor={str(k): v for k, v in sorted(starts[i].items())},
                             act_rooms={str(k): v for k, v in sorted(rooms[i].items())}, **extra)
            done[i] = True
            t_games[i] = time.monotonic() - t_start
            end_hash[i] = env.state_hash([i])[0]

        while not done.all():
            gi, aux = obs["global_i"], obs["aux"]
            floor = gi[:, G.total_floor].astype(np.int64)
            for i in np.flatnonzero(~done):          # per-act bookkeeping for the progress metric
                act = int(aux[i, AX.act_index]) + 1
                starts[i].setdefault(act, int(aux[i, AX.total_floor]) - int(aux[i, AX.act_floor]))
                if int(aux[i, AX.act_rooms]) > 0:
                    rooms[i][act] = int(aux[i, AX.act_rooms])
            for i in np.flatnonzero((~done) & (obs["cand_n"] <= 0)):
                finish(int(i), "error", int(aux[i, AX.act_index]), int(floor[i]),
                       error="non-terminal slot with cand_n = 0")
            if done.all():
                break
            live = ~done
            ctx.live = live
            a = np.asarray(fn(obs, ctx))
            if a.shape != (n,):
                raise ValueError(f"policy {policy_name} returned shape {a.shape}, expected ({n},)")
            a = a.astype(np.int32, copy=True)
            a[live & (a < 0)] = ILLEGAL_INDEX      # a live game must decide: a no-op is an illegal decision
            a[~live] = -1
            cand = obs["cand"]
            cn = obs["cand_n"]
            for i in np.flatnonzero(live):
                actions_log[i].append(int(a[i]))
                if 0 <= a[i] < cn[i] and cand[i, a[i], CC.kind] == end_turn:
                    turns[i] += 1
            steps[live] += 1
            ctx.step += 1
            obs, rew, term, trunc, status, info = env.step(a)
            illegal[(status & STATUS["ILLEGAL_ACTION"]) != 0] += 1
            for i in np.flatnonzero(live & (term | trunc).astype(bool)):
                le = info["last_episode"][i]
                res = _end_result(int(term[i]), float(rew[i]), int(status[i]))
                extra = {"final_hp": int(le[LE.final_hp]), "max_hp": int(le[LE.final_max_hp]),
                         "steps_env": int(le[LE.steps])}
                if res == "crashed":
                    extra["error"] = env.last_error(int(i))[:2000]
                if res == "capped":
                    extra["cap_reason"] = "steps"
                if res == "stuck":                  # auto_reset=False: obs still shows the final state
                    extra["stuck"] = {"phase": phase_names.name(int(obs["global_i"][i, G.decision_kind])),
                                      "floor": int(le[LE.floor])}
                if res == "loss":
                    enc = int(le[LE.last_encounter_id])
                    extra["death"] = {"floor": int(le[LE.floor]), "act": int(le[LE.act]) + 1, "room_type": None,
                                      "enemies": [schema.vocab.encounters.name(enc)] if enc > 0 else []}
                finish(int(i), res, int(le[LE.act]), int(le[LE.floor]), **extra)
        wall = time.monotonic() - t_start
        encoder_version = schema.encoder_version
    finally:
        env.close()

    rep = replay(ss, actions_log, step_cap=step_cap, floor_step_cap=floor_step_cap, threads=threads, dll=dll)
    replay_ok = []
    for i in range(n):
        r, p = result[i] or {}, rep[i]
        ended_expected = r.get("result") != "error"
        replay_ok.append(p["ended"] == ended_expected and (p["result"] == r.get("result") or not ended_expected)
                         and p["act"] == r.get("act") and p["floor"] == r.get("floor")
                         and p["state_hash"] == int(end_hash[i]))
    log(f"replay audit: {sum(replay_ok)}/{n} games reproduce their end (result, act, floor, state hash)")

    env_version = f"sts2vec/{encoder_version}"
    for i, g in enumerate(games):
        r = result[i] or {}
        s = {"key": keys[i], "seed": g["seed"], "character": g["character"], "ascension": ss["ascension"],
             "content": ss["content"], "policy": policy_name, "result": r.get("result"), "act": r.get("act"),
             "floor": r.get("floor"), "steps": int(steps[i]), "turns": int(turns[i]), "illegal": int(illegal[i]),
             "illegal_replies": 0, "retries": 0, "fallbacks": 0, "loop_hints": 0, "api_calls": 0, "api_errors": 0,
             "refusals": 0, "timeouts": 0, "max_tokens_stops": 0, "cost_usd": 0.0,
             "seconds": round(float(t_games[i]), 3), "final_hp": r.get("final_hp"), "max_hp": r.get("max_hp"),
             "death": r.get("death"), "error": r.get("error"), "cap_reason": r.get("cap_reason"),
             "stuck": r.get("stuck"), "truncated": r.get("result") in ("capped", "stuck"),
             "reward": 1 if r.get("result") == "win" else 0,
             "act_start_floor": r.get("act_start_floor"), "act_rooms": r.get("act_rooms"),
             "progress_pct": round(100.0 * (r.get("floor") or 0) / PG.full_run_floors(ss["ascension"]), 2),
             "history_actions": actions_log[i], "final_state_hash": f"{int(end_hash[i]):016x}",
             "replayable": bool(replay_ok[i]), "server_version": env_version, **(summary_extra or {})}
        s.update(PG.compute(s["result"], s["act"], s["floor"], ss["ascension"], s["act_start_floor"], s["act_rooms"]))
        with open(out / "games" / f"{keys[i]}.summary.json", "w", encoding="utf-8") as f:
            json.dump(s, f, ensure_ascii=False, indent=1)
    cfg = {"run_id": out.name, "protocol_version": PG.PROTOCOL_VERSION, "seedset": ss, "policy": policy_name,
           "model": None, "effort": None, "thinking": None, "server_version": env_version, "repo_sha": repo_sha(),
           "prompt_hash": policy_hash, "reveal_moves": False, "obs_mode": "sts2vec",
           "limits": {"step_cap": step_cap, "floor_step_cap": floor_step_cap}, "eval_seed": seed, "threads": threads,
           "evaluator": "sts2vec eval", **(config_extra or {})}
    with open(out / "config.json", "w", encoding="utf-8") as f:
        json.dump(cfg, f, ensure_ascii=False, indent=1)
    with open(out / "run.json", "w", encoding="utf-8") as f:
        json.dump({"wall_seconds": round(wall, 2), "abort_reason": None}, f, indent=1)
    report = PG.write_report(out)
    h = report["headline"]
    log(f"wrote {out / 'report.md'}: progress {h['progress_mean']:.3f} [{h['progress_ci95'][0]:.3f}, "
        f"{h['progress_ci95'][1]:.3f}], wins {h['wins']}/{n}, results "
        + ", ".join(f"{k} {v}" for k, v in h["results"].items() if v))
    return report
