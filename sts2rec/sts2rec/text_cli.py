"""Terminal client for the text-only STS2 environment."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
from typing import Sequence

from .env import (
    PoolConfig,
    Sts2Env,
    Sts2EnvConfig,
    launch_pool,
    recycle_worker,
    stop_pool,
)
from .information import InformationMode
from .text_env import Sts2TextEnv, TextEnvConfig, render_text


def _text_env(port: int, mode: str, worker_id: str | None = None) -> Sts2TextEnv:
    return Sts2TextEnv(
        Sts2Env(config=Sts2EnvConfig(port=port)),
        config=TextEnvConfig(information_mode=InformationMode(mode)),
        worker_id=worker_id,
    )


def _cmd_observe(args: argparse.Namespace) -> int:
    step = _text_env(args.port, args.mode).observe()
    print(json.dumps(step.as_dict(), ensure_ascii=False, indent=2) if args.json else render_text(step))
    return 0


def _cmd_play(args: argparse.Namespace) -> int:
    env = _text_env(args.port, args.mode)
    step = env.observe() if args.no_reset else env.reset(character=args.character)
    while True:
        print(render_text(step))
        if step.terminated or step.truncated:
            return 0
        answer = input("action index [r=refresh, j=json, q=quit] > ").strip().lower()
        if answer == "q":
            return 0
        if answer == "r":
            step = env.observe()
            continue
        if answer == "j":
            print(json.dumps(step.as_dict(), ensure_ascii=False, indent=2))
            continue
        try:
            step = env.step(int(answer))
        except (ValueError, IndexError) as error:
            print(f"invalid choice: {error}")
    return 0


def _cmd_pool_start(args: argparse.Namespace) -> int:
    base = Path(args.base).resolve()
    config = PoolConfig(base_dir=base, scripts_dir=Path(__file__).resolve().parents[2] / "scripts")
    envs = launch_pool(args.workers, config=config, recording=args.record)
    print(f"started {len(envs)} text workers using one shared runtime")
    for index, env in enumerate(envs, start=1):
        print(f"  worker {index}: {env.config.endpoint}")
    return 0


def _cmd_pool_stop(args: argparse.Namespace) -> int:
    stop_pool(Path(args.base).resolve())
    print("stopped text worker pool")
    return 0


def _cmd_pool_status(args: argparse.Namespace) -> int:
    base = Path(args.base).resolve()
    runtime = base / "runtime" / "SlayTheSpire2.app"
    print(f"runtime: {runtime} ({'present' if runtime.is_dir() else 'missing'})")
    workers = sorted(base.glob("inst*"), key=lambda path: path.name)
    if not workers:
        print("workers: none")
        return 0
    for worker in workers:
        port = (worker / "port").read_text().strip() if (worker / "port").is_file() else "-"
        pid_text = (worker / "pid").read_text().strip() if (worker / "pid").is_file() else ""
        alive = False
        if pid_text.isdigit():
            try:
                os.kill(int(pid_text), 0)
                alive = True
            except OSError:
                pass
        private_bytes = sum(
            path.stat().st_size
            for path in worker.rglob("*")
            if path.is_file() and not path.is_symlink()
        )
        print(
            f"{worker.name}: port={port} pid={pid_text or '-'} "
            f"alive={str(alive).lower()} private={private_bytes / 1024:.1f} KiB"
        )
    return 0


def _cmd_pool_recycle(args: argparse.Namespace) -> int:
    env = recycle_worker(
        args.worker,
        Path(args.base).resolve(),
        clear_run=args.clear_run,
        recording=args.record,
    )
    print(f"recycled worker {args.worker}: {env.config.endpoint}")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="sts2text", description="Text-only STS2 environment")
    sub = parser.add_subparsers(dest="command", required=True)
    observe = sub.add_parser("observe", help="print one text/JSON observation")
    observe.add_argument("--port", type=int, default=15601)
    observe.add_argument("--mode", choices=[m.value for m in InformationMode], default="limited")
    observe.add_argument("--json", action="store_true")
    observe.set_defaults(handler=_cmd_observe)

    play = sub.add_parser("play", help="interactive terminal game")
    play.add_argument("--port", type=int, default=15601)
    play.add_argument("--mode", choices=[m.value for m in InformationMode], default="limited")
    play.add_argument("--character", default="IRONCLAD")
    play.add_argument("--no-reset", action="store_true")
    play.set_defaults(handler=_cmd_play)

    pool = sub.add_parser("pool", help="manage concurrent text workers")
    pool_sub = pool.add_subparsers(dest="pool_command", required=True)
    start = pool_sub.add_parser("start")
    start.add_argument("workers", type=int)
    start.add_argument("--base", default="headless-instances")
    start.add_argument("--record", action="store_true", help="enable verbose recorder output")
    start.set_defaults(handler=_cmd_pool_start)
    stop = pool_sub.add_parser("stop")
    stop.add_argument("--base", default="headless-instances")
    stop.set_defaults(handler=_cmd_pool_stop)
    status = pool_sub.add_parser("status")
    status.add_argument("--base", default="headless-instances")
    status.set_defaults(handler=_cmd_pool_status)
    recycle = pool_sub.add_parser("recycle")
    recycle.add_argument("worker", type=int)
    recycle.add_argument("--base", default="headless-instances")
    recycle.add_argument(
        "--clear-run",
        action="store_true",
        help="discard only this worker's sandbox current_run.save",
    )
    recycle.add_argument("--record", action="store_true")
    recycle.set_defaults(handler=_cmd_pool_recycle)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    return int(args.handler(args))


if __name__ == "__main__":
    raise SystemExit(main())
