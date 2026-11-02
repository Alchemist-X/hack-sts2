"""Concurrent game runner: one asyncio task per game, bounded by a semaphore."""

from __future__ import annotations

import asyncio
import hashlib
import json
import random
import sys
import time
from collections import Counter, deque
from dataclasses import dataclass, field
from pathlib import Path

from . import prices as P
from . import progress as PG
from .compact import CompactView, sha16
from .policies import BudgetExceeded, FatalApiError, empty_meta
from .prompt import loop_hint
from .seedsets import game_key
from .simclient import SimClient, SimCrash, SimError, SimIllegal
from .tokens import any_line_token, illegal_message, resolve

LOG_LINES = 12
LAST_ACTIONS = 6
CONSECUTIVE_API_ERROR_ABORT = 25
LOOP_REPEAT = 6
FLOOR_STEP_CAP = 80


def full_run_floors(ascension: int) -> int:
    """Floors of a standard full run: 48 at A0, 49 at A10+ (second boss). Secondary metric only (v0.2)."""
    return PG.full_run_floors(ascension)


def game_seed(seed: str, character: str) -> int:
    """Deterministic per-game rng seed for the random policy and fallbacks."""
    return int.from_bytes(hashlib.sha256(f"{character}:{seed}".encode()).digest()[:8], "big")


@dataclass
class RunLimits:
    step_cap: int = 1200
    game_minutes: float = 60.0
    step_timeout_s: float = 300.0
    max_minutes: float | None = None
    loop_repeat: int = LOOP_REPEAT        # same observation K times on a floor -> one loop hint (0 = off)
    floor_step_cap: int = FLOOR_STEP_CAP  # non-combat steps per floor, then the game ends 'stuck' (0 = off)


class LoopGuard:
    """Per-game loop guard over the observations a policy is asked about.

    Keeps, for the current floor, how often each (phase, sha256(obs_text)) was seen and how many
    non-combat steps the floor took. `observe` returns (hint, stuck): `hint` is the one-time loop hint
    for this prompt, given once per floor when one observation has come up `repeat` times; `stuck` is
    True when this observation would be the floor's (floor_step_cap + 1)-th non-combat step.
    """

    def __init__(self, repeat: int = LOOP_REPEAT, floor_step_cap: int = FLOOR_STEP_CAP):
        self.repeat = repeat
        self.floor_step_cap = floor_step_cap
        self.floor = None
        self.seen: Counter = Counter()
        self.hinted = False
        self.noncombat_steps = 0
        self.hints = 0

    def observe(self, phase: str | None, floor: int | None, obs_text: str) -> tuple[str | None, bool]:
        if floor != self.floor:
            self.floor, self.seen, self.hinted, self.noncombat_steps = floor, Counter(), False, 0
        if phase != "Combat":
            if self.floor_step_cap and self.noncombat_steps >= self.floor_step_cap:
                return None, True
            self.noncombat_steps += 1
        key = (phase, hashlib.sha256(obs_text.encode("utf-8")).hexdigest()[:16])
        self.seen[key] += 1
        if self.repeat and not self.hinted and self.seen[key] >= self.repeat:
            self.hinted = True
            self.hints += 1
            return loop_hint(self.repeat, phase), False
        return None, False


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
                    limits: RunLimits, state: RunState, server_version: str | None = None,
                    reveal_moves: bool = False, obs_mode: str = "full") -> dict:
    """Play one full run; write games/<KEY>.jsonl and games/<KEY>.summary.json; return the summary.

    obs_mode 'full' sends the server's `text`; 'compact' renders the step text and the cached run-context
    blocks from the observation JSON (sts2eval.compact). The loop guard always hashes the server text."""
    if obs_mode not in ("full", "compact"):
        raise ValueError(f"obs_mode must be 'full' or 'compact', not {obs_mode!r}")
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
         "reveal_moves": bool(reveal_moves),
         "game_id": None, "result": None, "cap_reason": None, "abort_reason": None, "error": None,
         "stuck": None, "truncated": False, "reward": 0,
         "floor": None, "act": None, "progress_pct": None, "progress": None, "rooms_reached": None,
         "rooms_cleared": None, "act_rooms_current": None, "act_rooms_source": None,
         "act_start_floor": None, "act_rooms": None, "protocol_version": PG.PROTOCOL_VERSION,
         "final_phase": None, "steps": 0, "turns": 0,
         "illegal": 0, "illegal_replies": 0, "retries": 0, "api_errors": 0, "refusals": 0,
         "timeouts": 0, "max_tokens_stops": 0, "fallbacks": 0, "api_calls": 0, "late_line_tokens": 0,
         "loop_hints": 0,
         "tokens": P.usage_dict(None), "cost_usd": 0.0, "seconds": None, "final_hp": None, "max_hp": None,
         "death": None, "history_tokens": None, "history_steps": None, "replayable": None,
         "server_version": server_version, "server_url": getattr(client, "url", None), "obs_mode": obs_mode}
    view = CompactView(asc) if obs_mode == "compact" else None
    t0 = time.monotonic()
    log_lines: deque[str] = deque(maxlen=LOG_LINES)
    actions: deque[str] = deque(maxlen=LAST_ACTIONS)
    guard = LoopGuard(limits.loop_repeat, limits.floor_step_cap)
    acts = PG.ActTracker()
    last_turn_key = None
    last_combat = None
    last_room = None
    obs = None
    with open(transcript_path, "w", encoding="utf-8") as tf:
        def write(rec: dict) -> None:
            tf.write(json.dumps(rec, ensure_ascii=False) + "\n")
            tf.flush()

        try:
            created = await client.new_run(game["seed"], game["character"], asc, seedset["content"],
                                           reveal_moves=bool(reveal_moves))
            gid = created["id"]
            s["game_id"] = gid
            obs = created["observation"]
            write({"type": "start", "key": key, "game_id": gid, "server_url": s["server_url"], "seed": game["seed"],
                   "character": game["character"], "ascension": asc, "content": seedset["content"],
                   "reveal_moves": bool(reveal_moves)})
            while True:
                map_row, act_rooms = PG.map_info(obs.get("map"))
                acts.observe(obs.get("act"), obs.get("floor"), act_rooms)
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
                hint, stuck = guard.observe(obs.get("phase"), obs.get("floor"), obs.get("text") or "")
                if stuck:
                    # a floor that takes this many non-combat steps is a loop: stop before paying for more
                    s["result"] = "stuck"
                    s["stuck"] = {"phase": obs.get("phase"), "floor": obs.get("floor"),
                                  "noncombat_steps": guard.noncombat_steps}
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
                if hint:
                    hist["loop_hint"] = hint
                    s["loop_hints"] += 1
                attempts = []
                prompt_text, ctx, ctx_first = obs["text"], None, None
                if view is not None:
                    if view.needs_piles(obs):
                        view.set_piles(obs, await client.piles(gid))
                    ctx, prompt_text, ctx_first = view.render(obs)
                ctx_kw = {} if ctx is None else {"context": ctx}

                async def ask(feedback=None, text=prompt_text, legal=legal, hist=hist, attempts=attempts,
                              ctx_kw=ctx_kw):
                    try:
                        cand, meta = await asyncio.wait_for(
                            gpolicy.choose(text, legal, hist, feedback, **ctx_kw), timeout=limits.step_timeout_s)
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
                    if token is None and (late := any_line_token(meta.get("raw"), legal)) is not None:
                        token, meta["token_line"] = late
                        s["late_line_tokens"] += 1
                    if token is None:
                        s["illegal_replies"] += 1
                        s["retries"] += 1
                        cand2, meta2 = await ask(illegal_message(cand, legal))
                        err = meta2.get("error")
                        if err is None:
                            token = resolve(cand2, legal)
                            if token is None and (late := any_line_token(meta2.get("raw"), legal)) is not None:
                                token, meta2["token_line"] = late
                                s["late_line_tokens"] += 1
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
                if ctx is not None:
                    rec["prompt_text"] = prompt_text
                    rec["context_sha"] = sha16("".join(ctx))
                    if ctx_first is not None:
                        rec["context_changed_from"] = ctx_first
                        rec["context"] = list(ctx)
                if act_rooms is not None:
                    rec["map_row"], rec["act_rooms"] = map_row, act_rooms
                if hint:
                    rec["loop_hint"] = hint
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
            # Gym-style episode end (sim/CONTRACTS.md D5): reward 1 only at a won terminal; a cap or a stuck
            # floor truncates the episode.
            s["truncated"] = s["result"] in ("capped", "stuck")
            s["reward"] = 1 if s["result"] == "win" else 0
            if obs is not None:
                s["floor"], s["act"] = obs.get("floor"), obs.get("act")
                s["final_phase"] = obs.get("phase")
                s["final_hp"], s["max_hp"] = _hp(obs)
                acts.observe(obs.get("act"), obs.get("floor"), PG.map_info(obs.get("map"))[1])
                if s["floor"] is not None:
                    s["progress_pct"] = round(100.0 * s["floor"] / total_floors, 1)
            if view is not None:
                s["context_rebuilds"] = view.rebuilds
                s["context_block_rebuilds"] = list(view.block_rebuilds)
                s["piles_fetches"] = view.piles_fetches
            s.update(acts.as_dict())
            s.update(PG.compute(s["result"], s["act"], s["floor"], asc, s["act_start_floor"], s["act_rooms"]))
            if s["result"] == "loss":
                enemies = (last_combat or {}).get("enemies") or []
                s["death"] = {"floor": s["floor"], "act": s["act"], "room_type": last_room,
                              "phase": obs.get("phase") if obs else None,
                              "enemies": [e.get("id") for e in enemies]}
            s["cost_usd"] = round(s["cost_usd"], 6)
            write({"type": "end", "result": s["result"], "steps": s["steps"], "act": s["act"], "floor": s["floor"],
                   "phase": s["final_phase"], "progress": s["progress"],
                   "error": s["error"], "stuck": s["stuck"], "loop_hints": s["loop_hints"]})
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


def assign_servers(games: list[dict], n: int) -> list[int]:
    """Round-robin server index per game, by the game's position in the seed set (stable under --resume)."""
    return [i % max(1, n) for i in range(len(games))]


async def run_all(client: SimClient | list[SimClient], policy, seedset: dict, out_dir: Path, limits: RunLimits,
                  state: RunState, concurrency: int = 10, resume: bool = False,
                  server_version: str | dict | None = None, progress_interval: float = 10.0,
                  reveal_moves: bool = False, obs_mode: str = "full") -> list[dict]:
    """Play the seed set. `client` is one SimClient, or a list of them (one per server, `--urls`): game i of the
    seed set then runs on server i mod len(clients) and its summary records that server's URL. A game always
    stays on the server that created it (game ids are per server process). `server_version` may be a dict
    {url: version} for several servers."""
    clients = list(client) if isinstance(client, (list, tuple)) else [client]
    if not clients:
        raise ValueError("run_all needs at least one client")
    games = list(seedset["games"])
    where = assign_servers(games, len(clients))
    todo = [(g, clients[where[i]]) for i, g in enumerate(games) if not (resume and completed(out_dir, g))]
    state.total = len(games)
    state.done = len(games) - len(todo)
    if limits.max_minutes:
        state.deadline = state.started + limits.max_minutes * 60
    sem = asyncio.Semaphore(max(1, concurrency))
    results: list[dict] = []

    def version_of(c) -> str | None:
        if isinstance(server_version, dict):
            return server_version.get(getattr(c, "url", None))
        return server_version

    async def one(g, c):
        async with sem:
            if state.abort_reason:
                return None
            state.running += 1
            try:
                r = await play_game(c, policy, g, seedset, out_dir, limits, state, version_of(c),
                                    reveal_moves=reveal_moves, obs_mode=obs_mode)
                results.append(r)
                return r
            finally:
                state.running -= 1
                state.done += 1

    prog = asyncio.create_task(progress_loop(state, progress_interval)) if progress_interval else None
    try:
        await asyncio.gather(*(one(g, c) for g, c in todo))
    finally:
        if prog:
            prog.cancel()
    return results
