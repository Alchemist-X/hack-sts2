"""Concurrent game runner: one asyncio task per game, bounded by a semaphore."""

from __future__ import annotations

import asyncio
import hashlib
import json
import random
import sys
import time
from collections import deque
from dataclasses import dataclass, field
from pathlib import Path

from . import prices as P
from .policies import BudgetExceeded, FatalApiError, empty_meta
from .seedsets import game_key
from .simclient import SimClient, SimCrash, SimError, SimIllegal
from .tokens import illegal_message, resolve

LOG_LINES = 12
LAST_ACTIONS = 6
CONSECUTIVE_API_ERROR_ABORT = 25


def full_run_floors(ascension: int) -> int:
    return 48 if ascension < 10 else 49


def game_seed(seed: str, character: str) -> int:
    """Deterministic per-game rng seed for the random policy and fallbacks."""
    return int.from_bytes(hashlib.sha256(f"{character}:{seed}".encode()).digest()[:8], "big")


@dataclass
class RunLimits:
    step_cap: int = 1200
    game_minutes: float = 60.0
    step_timeout_s: float = 300.0
    max_minutes: float | None = None


@dataclass
class RunState:
    """Shared across games: abort flag, progress counters, budget."""
    tracker: P.CostTracker
    started: float = field(default_factory=time.monotonic)
    deadline: float | None = None
    abort_reason: str | None = None
    done: int = 0
    running: int = 0
    steps: int = 0
    total: int = 0
    consecutive_api_errors: int = 0

    def abort(self, reason: str) -> None:
        if self.abort_reason is None:
            self.abort_reason = reason

    def check_deadline(self) -> None:
        if self.deadline is not None and time.monotonic() >= self.deadline:
            self.abort("time")


def _hp(obs: dict) -> tuple[int | None, int | None]:
    p = obs.get("player") or {}
    return p.get("hp"), p.get("max_hp")


def _legal(obs: dict) -> list[str]:
    return [a["token"] for a in obs.get("legal_actions") or [] if a.get("token")]


async def play_game(client: SimClient, policy, game: dict, seedset: dict, out_dir: Path,
                    limits: RunLimits, state: RunState, server_version: str | None = None) -> dict:
    """Play one full run; write games/<KEY>.jsonl and games/<KEY>.summary.json; return the summary."""
    key = game_key(game)
    games_dir = out_dir / "games"
    games_dir.mkdir(parents=True, exist_ok=True)
    transcript_path = games_dir / f"{key}.jsonl"
    rseed = game_seed(game["seed"], game["character"])
    fallback_rng = random.Random(rseed ^ 0x5EED)
    gpolicy = policy.for_game(rseed)
    asc = seedset["ascension"]
    total_floors = full_run_floors(asc)

    s = {"key": key, "seed": game["seed"], "character": game["character"], "ascension": asc,
         "content": seedset["content"], "policy": policy.name, "model": getattr(policy, "model", None),
         "game_id": None, "result": None, "cap_reason": None, "abort_reason": None, "error": None,
         "floor": None, "act": None, "progress_pct": None, "steps": 0, "turns": 0,
         "illegal": 0, "illegal_replies": 0, "retries": 0, "api_errors": 0, "refusals": 0,
         "timeouts": 0, "max_tokens_stops": 0, "fallbacks": 0, "api_calls": 0,
         "tokens": P.usage_dict(None), "cost_usd": 0.0, "seconds": None, "final_hp": None, "max_hp": None,
         "death": None, "history_tokens": None, "history_steps": None, "replayable": None,
         "server_version": server_version}
    t0 = time.monotonic()
    log_lines: deque[str] = deque(maxlen=LOG_LINES)
    actions: deque[str] = deque(maxlen=LAST_ACTIONS)
    last_turn_key = None
    last_combat = None
    last_room = None
    obs = None
    with open(transcript_path, "w", encoding="utf-8") as tf:
        def write(rec: dict) -> None:
            tf.write(json.dumps(rec, ensure_ascii=False) + "\n")
            tf.flush()

        try:
            created = await client.new_run(game["seed"], game["character"], asc, seedset["content"])
            gid = created["id"]
            s["game_id"] = gid
            obs = created["observation"]
            write({"type": "start", "key": key, "game_id": gid, "seed": game["seed"],
                   "character": game["character"], "ascension": asc, "content": seedset["content"]})
            while True:
                if obs.get("terminal"):
                    s["result"] = obs.get("result") or "loss"
                    break
                state.check_deadline()
                if state.abort_reason:
                    s["result"], s["abort_reason"] = "aborted", state.abort_reason
                    break
                if s["steps"] >= limits.step_cap:
                    s["result"], s["cap_reason"] = "capped", "steps"
                    break
                if time.monotonic() - t0 >= limits.game_minutes * 60:
                    s["result"], s["cap_reason"] = "capped", "wall"
                    break
                legal = _legal(obs)
                if not legal:
                    s["result"], s["error"] = "error", "non-terminal observation with no legal actions"
                    break
                if obs.get("phase") == "Combat" and obs.get("combat"):
                    last_combat = obs["combat"]
                    tk = (obs.get("floor"), obs.get("turn"))
                    if obs.get("turn") is not None and tk != last_turn_key:
                        s["turns"] += 1
                        last_turn_key = tk
                hist = {"log": list(log_lines), "actions": list(actions)}
                attempts = []

                async def ask(feedback=None, obs=obs, legal=legal, hist=hist, attempts=attempts):
                    try:
                        cand, meta = await asyncio.wait_for(
                            gpolicy.choose(obs["text"], legal, hist, feedback), timeout=limits.step_timeout_s)
                    except asyncio.TimeoutError:
                        cand, meta = None, empty_meta(error="timeout")
                    attempts.append(meta)
                    if gpolicy.name != "random":
                        if meta.get("stop_reason") is not None:
                            s["api_calls"] += 1
                        P.add_usage(s["tokens"], meta["usage"])
                        s["cost_usd"] += meta["cost_usd"]
                        if meta.get("stop_reason") == "max_tokens":
                            s["max_tokens_stops"] += 1
                    return cand, meta

                cand, meta = await ask()
                token = None
                fallback = None
                err = meta.get("error")
                if err is None:
                    token = resolve(cand, legal)
                    if token is None:
                        s["illegal_replies"] += 1
                        s["retries"] += 1
                        cand2, meta2 = await ask(illegal_message(cand, legal))
                        err = meta2.get("error")
                        if err is None:
                            token = resolve(cand2, legal)
                            if token is None:
                                s["illegal_replies"] += 1
                                s["illegal"] += 1
                                fallback = "illegal"
                if err is not None:
                    fallback = err
                    if err == "refusal":
                        s["refusals"] += 1
                    elif err == "timeout":
                        s["timeouts"] += 1
                        s["api_errors"] += 1
                    else:
                        s["api_errors"] += 1
                    state.consecutive_api_errors += 1
                    if state.consecutive_api_errors >= CONSECUTIVE_API_ERROR_ABORT:
                        state.abort("api_errors")
                elif gpolicy.name != "random":
                    state.consecutive_api_errors = 0
                if token is None:
                    token = fallback_rng.choice(legal)
                    s["fallbacks"] += 1

                hp, max_hp = _hp(obs)
                rec = {"type": "step", "step": s["steps"], "phase": obs.get("phase"), "act": obs.get("act"),
                       "floor": obs.get("floor"), "turn": obs.get("turn"), "hp": hp, "max_hp": max_hp,
                       "obs_text": obs.get("text"), "legal": legal, "token": token, "fallback": fallback,
                       "retries": len(attempts) - 1,
                       "attempts": [{"raw": m.get("raw"), "stop_reason": m.get("stop_reason"),
                                     "error": m.get("error"), "usage": m.get("usage"),
                                     "latency_ms": m.get("latency_ms"), "cost_usd": m.get("cost_usd"),
                                     **{k: m[k] for k in ("exception", "status", "refusal_category", "request_id")
                                        if m.get(k) is not None}} for m in attempts]}
                if obs.get("phase") == "Map" and token.startswith("travel"):
                    try:
                        idx = int(token.split()[1])
                        opt = next((o for o in (obs.get("map") or {}).get("options") or [] if o.get("index") == idx), None)
                        last_room = opt.get("type") if opt else None
                    except (IndexError, ValueError):
                        pass
                try:
                    stepped = await client.step(gid, token)
                except SimIllegal as e:
                    # cannot happen for a token taken from legal_actions; record and fall back once
                    rec["server_reject"] = e.body.get("error")
                    s["illegal"] += 1
                    token = fallback_rng.choice(_legal(e.body) or legal)
                    rec["token"] = token
                    stepped = await client.step(gid, token)
                s["steps"] += 1
                state.steps += 1
                rec["log"] = stepped.get("log") or []
                write(rec)
                for line in rec["log"]:
                    log_lines.append(line)
                actions.append(f"floor {obs.get('floor')} {obs.get('phase')}: {token}")
                obs = stepped["observation"]
        except SimCrash as e:
            s["result"], s["error"] = "crashed", e.body.get("error") or str(e)
        except (BudgetExceeded, FatalApiError) as e:
            reason = "budget" if isinstance(e, BudgetExceeded) else "api_fatal"
            state.abort(reason)
            s["result"], s["abort_reason"], s["error"] = "aborted", reason, str(e)
        except SimError as e:
            s["result"], s["error"] = "error", str(e)
            if e.status is None:
                state.abort("server_down")
        except asyncio.CancelledError:
            s["result"], s["abort_reason"] = "aborted", "cancelled"
            raise
        finally:
            s["seconds"] = round(time.monotonic() - t0, 2)
            if obs is not None:
                s["floor"], s["act"] = obs.get("floor"), obs.get("act")
                s["final_hp"], s["max_hp"] = _hp(obs)
                if s["floor"] is not None:
                    s["progress_pct"] = round(100.0 * s["floor"] / total_floors, 1)
            if s["result"] == "loss":
                enemies = (last_combat or {}).get("enemies") or []
                s["death"] = {"floor": s["floor"], "act": s["act"], "room_type": last_room,
                              "phase": obs.get("phase") if obs else None,
                              "enemies": [e.get("id") for e in enemies]}
            s["cost_usd"] = round(s["cost_usd"], 6)
            write({"type": "end", "result": s["result"], "steps": s["steps"], "floor": s["floor"],
                   "error": s["error"]})
    if s["game_id"]:
        try:
            h = await client.history(s["game_id"])
            s["history_tokens"] = h.get("tokens")
            s["history_steps"] = h.get("steps")
            s["replayable"] = len(h.get("tokens") or []) == s["steps"]
            await client.delete(s["game_id"])
        except SimError as e:
            s["error"] = s["error"] or f"history: {e}"
    with open(games_dir / f"{key}.summary.json", "w", encoding="utf-8") as f:
        json.dump(s, f, ensure_ascii=False, indent=1)
    return s


def summary_path(out_dir: Path, game: dict) -> Path:
    return out_dir / "games" / f"{game_key(game)}.summary.json"


def completed(out_dir: Path, game: dict) -> bool:
    """A finished game for --resume: a summary exists and the game was not aborted mid-run."""
    p = summary_path(out_dir, game)
    if not p.exists():
        return False
    try:
        with open(p, encoding="utf-8") as f:
            return json.load(f).get("result") not in (None, "aborted")
    except (OSError, ValueError):
        return False


async def progress_loop(state: RunState, interval: float = 10.0, stream=sys.stderr) -> None:
    while True:
        await asyncio.sleep(interval)
        el = time.monotonic() - state.started
        print(f"[{int(el // 60):02d}:{int(el % 60):02d}] done {state.done}/{state.total} running {state.running} "
              f"steps {state.steps} api_calls {state.tracker.calls} cost ${state.tracker.spent_usd:.4f}",
              file=stream, flush=True)


async def run_all(client: SimClient, policy, seedset: dict, out_dir: Path, limits: RunLimits,
                  state: RunState, concurrency: int = 10, resume: bool = False,
                  server_version: str | None = None, progress_interval: float = 10.0) -> list[dict]:
    games = list(seedset["games"])
    todo = [g for g in games if not (resume and completed(out_dir, g))]
    state.total = len(games)
    state.done = len(games) - len(todo)
    if limits.max_minutes:
        state.deadline = state.started + limits.max_minutes * 60
    sem = asyncio.Semaphore(max(1, concurrency))
    results: list[dict] = []

    async def one(g):
        async with sem:
            if state.abort_reason:
                return None
            state.running += 1
            try:
                r = await play_game(client, policy, g, seedset, out_dir, limits, state, server_version)
                results.append(r)
                return r
            finally:
                state.running -= 1
                state.done += 1

    prog = asyncio.create_task(progress_loop(state, progress_interval)) if progress_interval else None
    try:
        await asyncio.gather(*(one(g) for g in todo))
    finally:
        if prog:
            prog.cancel()
    return results
