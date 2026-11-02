"""sts2eval command line: `run` (play a seed set) and `report` (re-aggregate a run directory)."""

from __future__ import annotations

import argparse
import asyncio
import datetime as dt
import json
import os
import sys
import time
from pathlib import Path

from . import __version__
from . import prices as P
from . import seedsets
from .policies import EFFORTS, MODELS, ClaudePolicy, RandomPolicy, RateLimiter, thinking_param
from .progress import PROTOCOL_VERSION
from .compact import COMPACT_SYSTEM_PROMPT, RENDER_VERSION, compact_prompt_hash
from .prompt import SYSTEM_PROMPT, prompt_hash
from .report import render_md, write_report
from .runner import FLOOR_STEP_CAP, LOOP_REPEAT, RunLimits, RunState, run_all
from .server import SpawnedServer, git_sha, repo_root
from .simclient import SimClient, SimError


def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(prog="sts2eval", description=__doc__)
    sub = ap.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("run", help="play every game of a seed set and write a report")
    r.add_argument("--seedset", required=True,
                   help="committed name or id (a0-s10 = smoke-a0-v1, a0-s20 = std-a0-v1, a10-s20 = std-a10-v1) "
                        "or a JSON path")
    r.add_argument("--policy", choices=("random", "claude"), default="random")
    r.add_argument("--model", choices=MODELS, default="claude-sonnet-5-5")
    r.add_argument("--effort", choices=EFFORTS, default=None, help="output_config.effort (default: model default)")
    r.add_argument("--thinking", choices=("adaptive", "off"), default="adaptive")
    r.add_argument("--max-tokens", type=int, default=1024)
    r.add_argument("--concurrency", type=int, default=10)
    r.add_argument("--rps-limit", type=float, default=None, help="max API request starts per second")
    r.add_argument("--step-cap", type=int, default=1200)
    r.add_argument("--loop-repeat", type=int, default=LOOP_REPEAT,
                   help="hint once when one observation has come up K times on a floor (0 = off)")
    r.add_argument("--floor-step-cap", type=int, default=FLOOR_STEP_CAP,
                   help="non-combat steps per floor before the game ends 'stuck' (0 = off)")
    r.add_argument("--obs-mode", choices=("full", "compact"), default="compact",
                   help="compact (default): runner-rendered step text plus cached run-context system blocks "
                        "(sts2eval/compact.py); full: the server's text observation, as in protocol v0.2")
    r.add_argument("--reveal-moves", action="store_true",
                   help="create games with reveal_moves (intents name the enemy move); default off = the game's view")
    r.add_argument("--game-minutes", type=float, default=60.0, help="wall-clock cap per game")
    r.add_argument("--step-timeout", type=float, default=300.0, help="seconds per policy decision (incl. SDK retries)")
    r.add_argument("--max-cost-usd", type=float, default=None)
    r.add_argument("--max-minutes", type=float, default=None)
    srv = r.add_mutually_exclusive_group()
    srv.add_argument("--url", default=None, help="use a running server (default http://127.0.0.1:8765)")
    srv.add_argument("--spawn", action="store_true", help="launch the built Release server on a free port")
    r.add_argument("--out", default=None, help="output dir (default tmp/eval/<run_id>)")
    r.add_argument("--resume", action="store_true", help="skip games whose summary exists (and is not aborted)")
    r.add_argument("--api-key-file", default=None, help="file holding the API key (read once, never logged)")
    r.add_argument("--prices", default=None, help="price table JSON (default sts2eval/prices.json)")
    r.add_argument("--progress-interval", type=float, default=10.0)
    rp = sub.add_parser("report", help="re-aggregate games/*.summary.json of a run directory (recomputes the "
                                       "protocol v0.2 progress from the transcripts of older runs)")
    rp.add_argument("dir")
    pa = sub.add_parser("parity", help="check that --obs-mode compact shows everything the server text shows "
                                       "(replays recorded games or plays seeded random ones)")
    pa.add_argument("--replay", default=None, help="run directory whose games/*.summary.json histories to replay")
    pa.add_argument("--keys", default=None, help="comma-separated game keys to replay (default: the longest games)")
    pa.add_argument("--games", type=int, default=3)
    pa.add_argument("--samples", type=int, default=200, help="observations checked (seeded sample; 0 = all)")
    pa.add_argument("--seed", type=int, default=0)
    psrv = pa.add_mutually_exclusive_group()
    psrv.add_argument("--url", default=None)
    psrv.add_argument("--spawn", action="store_true")
    return ap


def system_prompt(obs_mode: str) -> str:
    """The static (first, cached) system block: SYSTEM_PROMPT in full mode, byte-identical to protocol v0.2."""
    return COMPACT_SYSTEM_PROMPT if obs_mode == "compact" else SYSTEM_PROMPT


def run_prompt_hash(obs_mode: str) -> str:
    """config.json prompt_hash: unchanged for full mode; compact hashes its prompt and renderer version."""
    return compact_prompt_hash() if obs_mode == "compact" else prompt_hash()


def make_run_id(seedset_name: str, policy: str, model: str | None, now: dt.datetime | None = None) -> str:
    now = now or dt.datetime.now()
    return f"{seedset_name}-{policy}-{model or 'none'}-{now:%Y%m%d-%H%M}"


def make_anthropic_client(api_key_file: str | None):
    import anthropic
    kw = {"max_retries": 3}
    # Ask for gzip only: with brotlicffi 1.0.9.2 the SDK's brotli decoder is called with an
    # 'output_buffer_limit' argument it does not accept, and every response then fails with
    # APIConnectionError. gzip is decoded by the standard library. Override with STS2EVAL_ACCEPT_ENCODING.
    kw["default_headers"] = {"Accept-Encoding": os.environ.get("STS2EVAL_ACCEPT_ENCODING", "gzip")}
    if api_key_file:
        with open(api_key_file, encoding="utf-8") as f:
            kw["api_key"] = f.read().strip()
    elif not (os.environ.get("ANTHROPIC_API_KEY") or os.environ.get("ANTHROPIC_AUTH_TOKEN")):
        print("warning: ANTHROPIC_API_KEY is not set; the SDK will try its other credential sources",
              file=sys.stderr)
    return anthropic.AsyncAnthropic(**kw)


async def _run(args, policy, seedset: dict, out_dir: Path, url: str, prices: dict, tracker: P.CostTracker) -> dict:
    client = SimClient(url, max_connections=max(16, args.concurrency * 2))
    try:
        try:
            health = await client.health()
        except SimError as e:
            raise SystemExit(f"server not healthy at {url}: {e}") from None
        cfg = {
            "protocol_version": PROTOCOL_VERSION,
            "run_id": out_dir.name,
            "sts2eval_version": __version__,
            **seedsets.identity(seedset),
            "created": dt.datetime.now().isoformat(timespec="seconds"),
            "seedset": seedset,
            "policy": policy.name,
            "model": getattr(policy, "model", None),
            "effort": args.effort if policy.name == "claude" else None,
            "thinking": args.thinking if policy.name == "claude" else None,
            "thinking_param": getattr(policy, "thinking", None),
            "max_tokens": args.max_tokens if policy.name == "claude" else None,
            "obs_mode": args.obs_mode,
            "render_version": RENDER_VERSION if args.obs_mode == "compact" else None,
            "prompt_hash": run_prompt_hash(args.obs_mode) if policy.name == "claude" else None,
            "system_prompt_chars": len(system_prompt(args.obs_mode)),
            "reveal_moves": bool(args.reveal_moves),
            "server_url": url,
            "server_spawned": bool(args.spawn),
            "server_version": health.get("version"),
            "repo_sha": git_sha(),
            "prices": prices,
            "limits": {"step_cap": args.step_cap, "game_minutes": args.game_minutes,
                       "step_timeout_s": args.step_timeout, "max_minutes": args.max_minutes,
                       "max_cost_usd": args.max_cost_usd, "loop_repeat": args.loop_repeat,
                       "floor_step_cap": args.floor_step_cap},
            "concurrency": args.concurrency,
            "rps_limit": args.rps_limit,
            "resume": args.resume,
        }
        out_dir.mkdir(parents=True, exist_ok=True)
        cfg_path = out_dir / "config.json"
        if args.resume and cfg_path.exists():
            with open(cfg_path, encoding="utf-8") as f:
                prev = json.load(f)
            cfg["resumed_from"] = {k: prev.get(k) for k in ("created", "server_version", "repo_sha", "prompt_hash",
                                                            "reveal_moves", "protocol_version", "seedset_sha256",
                                                            "obs_mode")}
        with open(cfg_path, "w", encoding="utf-8") as f:
            json.dump(cfg, f, ensure_ascii=False, indent=1)
        limits = RunLimits(step_cap=args.step_cap, game_minutes=args.game_minutes,
                           step_timeout_s=args.step_timeout, max_minutes=args.max_minutes,
                           loop_repeat=args.loop_repeat, floor_step_cap=args.floor_step_cap)
        state = RunState(tracker=tracker)
        t0 = time.monotonic()
        try:
            await run_all(client, policy, seedset, out_dir, limits, state, concurrency=args.concurrency,
                          resume=args.resume, server_version=health.get("version"),
                          progress_interval=args.progress_interval, reveal_moves=args.reveal_moves,
                          obs_mode=args.obs_mode)
        except asyncio.CancelledError:
            state.abort("interrupted")
        run = {"wall_seconds": round(time.monotonic() - t0, 2), "abort_reason": state.abort_reason,
               "api_calls": tracker.calls, "spent_usd": round(tracker.spent_usd, 6),
               "finished": dt.datetime.now().isoformat(timespec="seconds")}
        with open(out_dir / "run.json", "w", encoding="utf-8") as f:
            json.dump(run, f, indent=1)
        return write_report(out_dir)
    finally:
        await client.aclose()


def cmd_run(args) -> int:
    try:
        seedset = seedsets.load(args.seedset)
    except seedsets.SeedSetError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    prices = P.load_prices(args.prices)
    tracker = P.CostTracker(max_cost_usd=args.max_cost_usd)
    if args.policy == "claude":
        try:
            thinking_param(args.model, args.thinking, args.effort)
            P.model_prices(prices, args.model)
        except ValueError as e:
            print(f"error: {e}", file=sys.stderr)
            return 2
        policy = ClaudePolicy(make_anthropic_client(args.api_key_file), model=args.model, effort=args.effort,
                              thinking=args.thinking, max_tokens=args.max_tokens, prices=prices,
                              tracker=tracker, limiter=RateLimiter(args.rps_limit),
                              system_prompt=system_prompt(args.obs_mode))
    else:
        policy = RandomPolicy()
    model = policy.model if args.policy == "claude" else None
    out_dir = Path(args.out) if args.out else repo_root() / "tmp" / "eval" / make_run_id(seedset["name"], args.policy, model)
    server = None
    try:
        if args.spawn:
            try:
                server = SpawnedServer(out_dir).start()
            except (FileNotFoundError, RuntimeError, TimeoutError) as e:
                print(f"error: {e}", file=sys.stderr)
                return 2
            url = server.url
        else:
            url = args.url or os.environ.get("SIM_PLAY_URL") or "http://127.0.0.1:8765"
        print(f"sts2eval: {len(seedset['games'])} games of {seedset['name']} -> {out_dir} (server {url})",
              file=sys.stderr)
        try:
            report = asyncio.run(_run(args, policy, seedset, out_dir, url, prices, tracker))
        except KeyboardInterrupt:
            print("interrupted; summaries written so far are kept (re-run with --resume)", file=sys.stderr)
            if (out_dir / "games").exists():
                write_report(out_dir)
            return 130
    finally:
        if server is not None:
            server.stop()
    print(render_md(report).split("## Per character")[0].rstrip())
    print(f"\nreport: {out_dir / 'report.md'}")
    return 0 if not report.get("aborted") else 3


def cmd_report(args) -> int:
    d = Path(args.dir)
    if not (d / "games").exists():
        print(f"error: {d}/games not found", file=sys.stderr)
        return 2
    report = write_report(d)
    print(render_md(report))
    return 0


def cmd_parity(args) -> int:
    from .parity import ParityStats, play_random, replay_run_dir

    async def go(url: str) -> dict:
        client = SimClient(url)
        stats = ParityStats(args.samples, args.seed)
        try:
            if args.replay:
                keys = args.keys.split(",") if args.keys else None
                await replay_run_dir(client, Path(args.replay), stats, keys=keys, games=args.games)
            else:
                chars = ("IRONCLAD", "SILENT", "DEFECT", "NECROBINDER", "REGENT")
                games = [(f"PARITY{i}", chars[i % len(chars)]) for i in range(args.games)]
                await play_random(client, stats, games, seed=args.seed)
        finally:
            await client.aclose()
        return stats.result()

    server = None
    try:
        if args.spawn:
            server = SpawnedServer(repo_root() / "tmp" / "eval" / "parity").start()
            url = server.url
        else:
            url = args.url or os.environ.get("SIM_PLAY_URL") or "http://127.0.0.1:8765"
        res = asyncio.run(go(url))
    finally:
        if server is not None:
            server.stop()
    print(json.dumps(res, indent=1, ensure_ascii=False))
    return 0 if res["failures"] == 0 and res["unstable_contexts"] == 0 else 1


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.cmd == "run":
        return cmd_run(args)
    if args.cmd == "parity":
        return cmd_parity(args)
    return cmd_report(args)


if __name__ == "__main__":
    sys.exit(main())
