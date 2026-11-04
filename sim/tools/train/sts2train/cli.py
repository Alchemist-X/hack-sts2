"""sts2train CLI (also installed as ``sts2train-smoke``): ``train`` (PPO), ``eval`` (protocol v0.2 NN entry point),
``plot`` (curves of a run directory).

    sts2train train --preset large --device cuda --envs 2048 --max-hours 6 --eval-every 50 --out RUN_DIR
    sts2train train --resume auto --out RUN_DIR          # continue from the newest checkpoint in RUN_DIR
    sts2train train --characters SILENT --start-saves SAVES_DIR --save-mix 0.5 ...   # half the starts from saves
    sts2train eval --seedset std-a0-v1 --checkpoint RUN_DIR/checkpoints/best.pt --out EVAL_DIR

``train`` flags that are not given keep the default (fresh run) or the checkpoint's value (``--resume``).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

from .model import PRESETS


class _Log:
    """stdout (a closed pipe, e.g. an interrupted `| tee`, is ignored) + RUN_DIR/train.log with timestamps."""

    def __init__(self):
        self.f = None

    def open(self, path: Path) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        self.f = open(path, "a", encoding="utf-8")

    def __call__(self, m: str) -> None:
        try:
            print(m, flush=True)
        except (BrokenPipeError, OSError):
            pass
        if self.f is not None:
            try:
                self.f.write(f"{time.strftime('%Y-%m-%d %H:%M:%S')} {m}\n")
                self.f.flush()
            except OSError:
                pass


def _plot(out: Path) -> None:
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    from .ppo import curve

    rows, evals = curve(out)
    rows = [r for r in rows if r.get("progress_mean") is not None]
    if not rows:
        return
    x = [r["env_steps"] / 1e6 for r in rows]
    k = 20

    def smooth(v):
        return [sum(v[max(0, i - k + 1):i + 1]) / len(v[max(0, i - k + 1):i + 1]) for i in range(len(v))]

    fig, ax = plt.subplots(1, 2, figsize=(11, 4))
    saves = any("save_episodes" in r for r in rows)

    def series(key):            # rolling mean over the updates that finished such episodes (NaN = none yet)
        v = [r.get(key) for r in rows]
        out = []
        for i in range(len(v)):
            w = [y for y in v[max(0, i - k + 1):i + 1] if y is not None]
            out.append(sum(w) / len(w) if w else float("nan"))
        return out

    if saves:                   # --start-saves: fresh starts (comparable to a run without saves) vs save starts
        ax[0].plot(x, series("fresh_progress_mean"), color="#1f4e8c", lw=1.4,
                   label=f"fresh starts: progress (rolling {k})")
        ax[0].plot(x, series("save_progress_mean"), color="#c77d2a", lw=1.1, ls="--",
                   label="save starts: progress at the end")
        ax[0].plot(x, series("save_progress_gain_mean"), color="#8a4f0e", lw=1.4,
                   label="save starts: progress gained")
    else:
        p = [r["progress_mean"] for r in rows]
        ax[0].plot(x, p, color="#9db4d6", lw=0.6, label="per update (training episodes)")
        ax[0].plot(x, smooth(p), color="#1f4e8c", lw=1.4, label=f"rolling mean ({k} updates)")
    ev = [e for e in evals if e.get("progress_mean") is not None]
    if ev:
        ax[0].plot([e["env_steps"] / 1e6 for e in ev], [e["progress_mean"] for e in ev], "o-", color="#b04a3a",
                   ms=4, label=f"protocol eval ({ev[-1].get('seedset')})")
    ax[0].set_xlabel("env steps (M)")
    ax[0].set_ylabel("progress v0.2")
    ax[0].legend(frameon=False)
    ax[0].set_title("protocol progress")
    if saves:
        ax[1].plot(x, series("fresh_floor_mean"), color="#2e6b2a", lw=1.4, label=f"fresh starts (rolling {k})")
        ax[1].plot(x, series("save_floor_mean"), color="#c77d2a", lw=1.4, label="save starts")
    else:
        f = [r.get("floor_mean") or 0.0 for r in rows]
        ax[1].plot(x, f, color="#a9cfa4", lw=0.6, label="per update")
        ax[1].plot(x, smooth(f), color="#2e6b2a", lw=1.4, label=f"rolling mean ({k} updates)")
    ax[1].set_xlabel("env steps (M)")
    ax[1].set_ylabel("floor reached (finished episodes)")
    ax[1].legend(frameon=False)
    ax[1].set_title("floor reached")
    for a in ax:
        a.spines[["top", "right"]].set_visible(False)
    fig.tight_layout()
    fig.savefig(out / "curve.png", dpi=130)


def _train_parser(sub) -> argparse.ArgumentParser:
    from .ppo import TrainConfig

    D = TrainConfig()
    tr = sub.add_parser("train", help="PPO on sts2vec (fresh or --resume)", argument_default=argparse.SUPPRESS,
                        description="Flags not given keep the default (fresh run) or the checkpoint's value (--resume).")
    g = tr.add_argument_group("run")
    g.add_argument("--out", help=f"run directory (default {D.out}-<time>)")
    g.add_argument("--resume", help="checkpoint path, or 'auto' = the newest checkpoint under --out (fresh if none)")
    g.add_argument("--device", help=f"cpu | mps | cuda[:i] | auto (default {D.device})")
    g.add_argument("--amp", action=argparse.BooleanOptionalAction, help="bf16 autocast on CUDA (default off)")
    g.add_argument("--grad-checkpoint", dest="grad_ckpt", action=argparse.BooleanOptionalAction,
                   help="recompute the transformer blocks in backward: ~4x less activation memory for the large "
                        "preset, more compute (default off)")
    g.add_argument("--allow-big-local", dest="allow_big_local", action="store_true",
                   help="allow cpu / mps training of models above 3M parameters or the large preset (refused otherwise)")
    g.add_argument("--torch-threads", dest="torch_threads", type=int, help="torch CPU threads (0 = auto)")
    g.add_argument("--seed", type=int, help=f"(default {D.seed})")
    g = tr.add_argument_group("budget (whichever comes first)")
    g.add_argument("--max-hours", dest="max_hours", type=float,
                   help=f"wall-clock budget of this session, hours (default {D.max_hours}; 0 = none)")
    g.add_argument("--minutes", type=float, help="LEGACY: --max-hours in minutes")
    g.add_argument("--max-updates", dest="max_updates", type=int, help="total PPO updates incl. resumed (0 = none)")
    g.add_argument("--max-env-steps", dest="max_env_steps", type=int, help="total env steps incl. resumed (0 = none)")
    g = tr.add_argument_group("checkpoints, logs, eval")
    g.add_argument("--ckpt-every", dest="ckpt_every", type=int, help=f"checkpoint every N updates (default {D.ckpt_every})")
    g.add_argument("--ckpt-every-min", dest="ckpt_every_min", type=float,
                   help=f"and every M minutes (default {D.ckpt_every_min})")
    g.add_argument("--keep-ckpts", dest="keep_ckpts", type=int, help=f"keep the last K (default {D.keep_ckpts})")
    g.add_argument("--eval-every", dest="eval_every", type=int,
                   help="protocol eval every E updates (default 0 = off); also once at the end of a budget-ended session")
    g.add_argument("--eval-seedset", dest="eval_seedset", help=f"(default {D.eval_seedset})")
    g.add_argument("--eval-threads", dest="eval_threads", type=int, help="(0 = auto)")
    g.add_argument("--log-every", dest="log_every", type=int, help=f"stdout line every N updates (default {D.log_every})")
    g.add_argument("--baseline-steps", dest="baseline_steps", type=int, help="uniform-random baseline steps first")
    g = tr.add_argument_group("model")
    g.add_argument("--preset", choices=sorted(PRESETS),
                   help="architecture preset: " + "; ".join(f"{k} {v}" for k, v in PRESETS.items()))
    g.add_argument("--d-model", dest="d", type=int, help=f"(default {D.d})")
    g.add_argument("--state-dim", dest="s", type=int, help=f"(default {D.s})")
    g.add_argument("--depth", type=int, help=f"transformer blocks (default {D.depth} = pooled-concat model)")
    g.add_argument("--heads", type=int, help=f"(default {D.heads})")
    g.add_argument("--ff-mult", dest="ff_mult", type=int, help=f"(default {D.ff_mult})")
    g = tr.add_argument_group("objective")
    g.add_argument("--objective", choices=["protocol", "win", "shaped"],
                   help="protocol (default): per-step delta of the protocol v0.2 progress + --win-bonus at a win; "
                        "win: 1 at a Victory terminal; shaped: LEGACY smoke objective (win + delta floor / 48)")
    g.add_argument("--win-bonus", dest="win_bonus", type=float, help=f"(default {D.win_bonus})")
    g.add_argument("--shaped", action="store_true", help="LEGACY (= --objective shaped)")
    g = tr.add_argument_group("env")
    g.add_argument("--envs", type=int, help=f"(default {D.envs})")
    g.add_argument("--threads", type=int, help="env worker threads (default 0 = cpu count)")
    g.add_argument("--step-cap", dest="step_cap", type=int, help=f"(default {D.step_cap})")
    g.add_argument("--floor-step-cap", dest="floor_step_cap", type=int,
                   help="VecEnv floor_step_cap pass-through (only if this sts2vec has it; default unset)")
    g.add_argument("--ascension", type=int, help=f"(default {D.ascension})")
    g.add_argument("--pool", type=int, help=f"fresh start pool entries (default {D.pool})")
    g.add_argument("--characters", type=lambda v: tuple(x.strip() for x in v.split(",") if x.strip()),
                   help="comma separated (default all five)")
    g.add_argument("--start-saves", dest="start_saves",
                   help="directory of game .save files (searched recursively; scripts/train/collect_start_saves.py) "
                        "to start episodes from, mixed with --pool fresh starts; unsupported saves are skipped and "
                        "reported; 'none' turns it off on --resume (default: fresh starts only)")
    g.add_argument("--save-mix", dest="save_mix", type=float,
                   help=f"fraction of episode starts from --start-saves, via pool entry weights (default {D.save_mix})")
    g.add_argument("--save-copies", dest="save_copies", choices=["determinized", "exact"],
                   help="further pool copies of a save: determinized = same player view, hidden state (draw order, "
                        f"RNG streams) resampled per copy; exact = every start replays the save's RNG (default "
                        f"{D.save_copies})")
    g = tr.add_argument_group("PPO")
    for flag, dest, typ in (("--rollout", "rollout", int), ("--epochs", "epochs", int),
                            ("--minibatch", "minibatch", int), ("--lr", "lr", float), ("--gamma", "gamma", float),
                            ("--lam", "lam", float), ("--clip", "clip", float), ("--vf-coef", "vf_coef", float),
                            ("--ent-coef", "ent_coef", float), ("--max-grad-norm", "max_grad_norm", float),
                            ("--max-rows", "max_rows", int)):
        g.add_argument(flag, dest=dest, type=typ, help=f"(default {getattr(D, dest)})")
    return tr


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="sts2train")
    sub = ap.add_subparsers(dest="cmd", required=True)
    _train_parser(sub)
    ev = sub.add_parser("eval", aliases=["evaluate"],
                        help="play a protocol seed set with a checkpoint's greedy policy (protocol v0.2 NN entry)")
    ev.add_argument("--seedset", required=True, help="seed set name, id or path (sim/tools/eval/seedsets)")
    ev.add_argument("--checkpoint", required=True)
    ev.add_argument("--out", default=None, help="run directory (default tmp/eval/nn-<checkpoint>-<seedset>-<time>)")
    ev.add_argument("--threads", type=int, default=8)
    ev.add_argument("--device", default="cpu")
    pl = sub.add_parser("plot", help="draw curve.png of a train run directory")
    pl.add_argument("out")
    a = ap.parse_args(argv)
    if a.cmd == "train":
        from .ppo import plan_run, train

        given = {k: v for k, v in vars(a).items() if k != "cmd"}
        resume = given.pop("resume", None)
        if "minutes" in given:
            given["max_hours"] = given.pop("minutes") / 60.0
        if "start_saves" in given:      # absolute, so a resume from another working directory finds it
            v = given["start_saves"]
            given["start_saves"] = None if v.lower() in ("", "none") else os.path.abspath(os.path.expanduser(v))
        if "out" not in given and resume in (None, "auto"):
            if resume == "auto":
                ap.error("--resume auto needs --out RUN_DIR")
            given["out"] = f"tmp/train/run-{time.strftime('%Y%m%d-%H%M%S')}"
        from .ppo import BigLocalRunError

        log = _Log()
        cfg, ck, ck_path = plan_run(given, resume, log=log)
        log.open(Path(cfg.out) / "train.log")
        if cfg.objective == "shaped":
            log("NOTE: --shaped is the LEGACY smoke objective (floor-reached shaping); the default is --objective protocol.")
        log(f"run directory: {cfg.out}")
        try:
            s = train(cfg, log=log, resume=ck, resume_path=ck_path)
        except BigLocalRunError as e:
            log(f"error: {e}")
            return 2
        try:
            _plot(Path(cfg.out))
        except ImportError:
            pass
        log(json.dumps({k: v for k, v in s.items() if k not in ("config",)}, indent=1, default=str))
        return 0
    if a.cmd in ("eval", "evaluate"):
        from .evaluate import evaluate

        out = a.out or f"tmp/eval/nn-{Path(a.checkpoint).stem}-{Path(a.seedset).stem}-{time.strftime('%Y%m%d-%H%M%S')}"
        rep = evaluate(a.seedset, a.checkpoint, out, threads=a.threads, device=a.device,
                       log=lambda m: print(m, flush=True))
        h = rep["headline"]
        print(json.dumps({"out": out, "progress_mean": h["progress_mean"], "progress_ci95": h["progress_ci95"],
                          "wins": h["wins"], "games": h["games"], "results": h["results"],
                          "floor_mean": h["floor_mean"], "act_reached": h["act_reached"]}, indent=1))
        return 0
    if a.cmd == "plot":
        _plot(Path(a.out))
        return 0
    return 2


if __name__ == "__main__":
    sys.exit(main())
