#!/usr/bin/env python3
"""Watch a trained sts2train checkpoint play the real Slay the Spire 2 ("twin lockstep").

The network never reads the game directly: a simulator twin (one sts2vec slot started from the same seed, or from the
game's own current_run.save) mirrors the run, the checkpoint picks a candidate on the twin's observation, the choice
is mapped by meaning onto the game's candidates (STS2_MCP) and executed there, and the twin takes the same step.
Every decision is diffed (floor, act, HP, gold, energy, hand, enemies, potions, relics, screen content); after a
desync the runner keeps playing in follow mode and re-seeds the twin from the game's next save point.

Real game (STS2_MCP installed, a run started with the seed / character / ascension below):
    uv run --project sim/tools/train python scripts/live/nn_twin_play.py \\
        --checkpoint best.pt --character SILENT --ascension 10 --out output/nn-twin/run1 [--seed SEED]

Headless worker with the patched MCP (eval API; the runner starts the seeded run itself):
    ... --port 15601 --start-run --seed TW1NA10S01 \\
        --save-root "headless-instances/inst1/home/Library/Application Support/SlayTheSpire2"

Dry run against the simulator's play server (no game):
    ... --dry-run-server http://127.0.0.1:8765 --seed TW1NA10S01

Output: <out>/trajectory.jsonl (one record per decision / glue step / resync), <out>/summary.json, <out>/status.log
(the live one-line status), <out>/saves/ (copies of the game saves the twin was re-seeded from).
Guide: scripts/live/README-nn-twin.md.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

HERE = Path(__file__).resolve().parent
if str(HERE) not in sys.path:
    sys.path.insert(0, str(HERE))


def parse_args(argv=None):
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--checkpoint", required=True, help="sts2train checkpoint (.pt)")
    ap.add_argument("--seed", help="the run's seed (default: read from the game: eval API or current_run.save)")
    ap.add_argument("--character", default="SILENT")
    ap.add_argument("--ascension", type=int, default=10)
    ap.add_argument("--out", required=True, help="run directory (created)")
    ap.add_argument("--port", type=int, default=15526, help="STS2_MCP port (human game: 15526)")
    ap.add_argument("--save-root", help="game user-data root holding current_run.save "
                                         "(default ~/Library/Application Support/SlayTheSpire2)")
    ap.add_argument("--save-path", help="explicit current_run.save path (overrides --save-root)")
    ap.add_argument("--start-run", action="store_true",
                    help="start the seeded run through the eval API first (patched MCP builds / headless workers)")
    ap.add_argument("--dry-run-server", metavar="URL",
                    help="use the sim play server at URL as the stand-in for the real game (needs --seed)")
    ap.add_argument("--content", choices=("all", "ported_only"), default="all",
                    help="twin content pools; 'all' matches the real game (default)")
    ap.add_argument("--anchor", choices=("desync", "room"), default="desync",
                    help="re-seed the twin from the game's save only after a desync (default) or at every save point")
    ap.add_argument("--lookahead", metavar="JSON", help="combat lookahead (needs sts2train.lookahead on this tree)")
    ap.add_argument("--max-decisions", type=int, default=4000)
    ap.add_argument("--stop-floor", type=int, help="stop (without abandoning) once the game reaches this floor")
    ap.add_argument("--loop-breaker", choices=("on", "off"), default="on",
                    help="greedy with sts2train's loop breaker (default, as sts2train eval) or the plain argmax")
    ap.add_argument("--loop-breaker-k", type=int, default=None,
                    help="returns to one observation before only leave rows stay (default: sts2train's, 3)")
    ap.add_argument("--macros", default="checkpoint",
                    help="the twin's macros (sim/VEC.md 'Room macros', 'Prompt macros v2'): 'checkpoint' (default) = "
                         "the checkpoint's training env's (config.macros, as sts2train eval does), 'none' = off, or a "
                         "list such as 'rest,shop' / 'all' (= rest,shop,prompt). A macro row is played in the game as "
                         "its clicks, each checked; with prompt, a pick that completes a selection is followed by the "
                         "game's confirm (nn_twin/macros.py)")
    ap.add_argument("--torch-threads", type=int, default=4)
    ap.add_argument("--settle-timeout", type=float, default=60.0, help="seconds to wait for a settled game frame")
    ap.add_argument("--inject-desync", type=int, metavar="N",
                    help="testing only: at decision N step the twin with a different row than the one played, to "
                         "exercise follow mode and the save resync")
    return ap.parse_args(argv)


def repo_state() -> dict:
    """The runner's own code version (provenance of a trajectory)."""
    import subprocess

    def git(*a):
        try:
            return subprocess.run(["git", *a], cwd=HERE, capture_output=True, text=True, timeout=10).stdout.strip()
        except (OSError, subprocess.SubprocessError):
            return ""
    return {"repo_sha": git("rev-parse", "HEAD") or None,
            "repo_dirty": bool(git("status", "--porcelain", "--", str(HERE)))}


def main(argv=None) -> int:
    args = parse_args(argv)
    os.environ.setdefault("OMP_NUM_THREADS", str(args.torch_threads))
    if args.lookahead:
        try:
            import sts2train.lookahead  # noqa: F401
        except ImportError:
            print("--lookahead: sts2train.lookahead is not on this tree (branch train/lookahead is not merged); "
                  "run without it (greedy)", file=sys.stderr)
            return 2
        print("--lookahead: sts2train.lookahead's mirror env replays fresh seeds and cannot follow the twin's save "
              "resyncs; this runner plays greedy only", file=sys.stderr)
        return 2

    from nn_twin.game import GameError
    from nn_twin.runner import Runner, canonical_seed
    from nn_twin.twin import Policy, Twin

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)
    seed = canonical_seed(args.seed) if args.seed else None
    character = args.character.upper()

    print(f"loading {args.checkpoint} ...", flush=True)
    policy = Policy(args.checkpoint, torch_threads=args.torch_threads, loop_breaker=args.loop_breaker == "on",
                    loop_breaker_k=args.loop_breaker_k)
    if args.macros == "checkpoint":
        macros = policy.meta.get("macros") or ""
    else:
        macros = "" if args.macros.strip().lower() in ("", "none", "off") else args.macros
    twin = Twin(content=args.content, macros=macros or None)
    if twin.macros:
        print(f"twin macros: {','.join(twin.macros)}", flush=True)
    compat = policy.bind(twin.schema)
    cfg_chars = policy.meta.get("characters") or []
    if cfg_chars and character not in [str(c).upper() for c in cfg_chars]:
        print(f"note: the checkpoint was trained on {cfg_chars}, playing {character}", flush=True)

    if args.dry_run_server:
        from nn_twin.standin import PlayServerGame

        if not seed:
            print("--dry-run-server needs --seed", file=sys.stderr)
            return 2
        game = PlayServerGame(args.dry_run_server, seed=seed, character=character, ascension=args.ascension,
                              content=args.content)
        game_desc = {"kind": "standin", "url": args.dry_run_server, "game_id": game.id,
                     "server_version": game.server_version}
    else:
        from nn_twin.mcp import McpGame

        try:
            game = McpGame(args.port, save_root=args.save_root, save_path=args.save_path,
                           settle_timeout=args.settle_timeout)
        except GameError as e:
            print(f"{e}\nIs the game running with STS2_MCP installed (scripts/mcp_mod.sh on)?", file=sys.stderr)
            return 2
        if args.start_run:
            if not seed:
                print("--start-run needs --seed", file=sys.stderr)
                return 2
            reply = game.start_run(character, args.ascension, seed)
            print(f"started {character} A{args.ascension} seed {seed}: {reply.get('seed_mechanism')}", flush=True)
        game_desc = {"kind": "mcp", "port": args.port, "eval_api": game.has_eval,
                     "save_root": str(game.save_root), "save_path": args.save_path}

    config = {"started_at": datetime.now(timezone.utc).isoformat(timespec="seconds"), "argv": sys.argv,
              "args": vars(args), "game": game_desc, "checkpoint": policy.meta, "spec_compat": compat,
              "twin_content": args.content, "twin_macros": list(twin.macros), "encoder_version": twin.schema.encoder_version, **repo_state()}
    (out / "config.json").write_text(json.dumps(config, ensure_ascii=False, indent=1, default=str))

    runner = Runner(game, twin, policy, out, character=character, ascension=args.ascension, seed=seed,
                    anchor=args.anchor, max_decisions=args.max_decisions, stop_floor=args.stop_floor,
                    audit_tokens=bool(args.dry_run_server), inject_desync_at=args.inject_desync)
    try:
        summary = runner.run()
    finally:
        twin.close()
        if args.dry_run_server:
            game.close()
    short = {k: summary.get(k) for k in ("decisions", "desync_decisions", "desync_events", "final_mode", "error",
                                         "wall_s", "s_per_decision")}
    short["loop_breaks"] = (summary.get("counters") or {}).get("loop_breaks", 0)
    short["resyncs"] = summary["resyncs"]["count"]
    short["result"] = summary.get("result")
    if "dry_run_audit" in summary:
        short["dry_run_audit"] = {k: summary["dry_run_audit"][k] for k in
                                  ("lockstep_steps_compared", "token_mismatches", "history_equal")}
    print(json.dumps(short, ensure_ascii=False, default=str))
    return 0 if not summary.get("error") else 1


if __name__ == "__main__":
    sys.exit(main())
