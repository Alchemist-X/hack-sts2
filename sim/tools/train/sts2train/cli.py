"""sts2train-smoke CLI: `train` (PPO smoke run) and `eval` (protocol v0.2 NN entry point)."""

from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path


def _plot(out: Path) -> None:
    import csv

    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    rows = list(csv.DictReader(open(out / "updates.csv")))
    wins = list(csv.DictReader(open(out / "curve.csv")))
    if not rows:
        return
    summ = json.load(open(out / "summary.json")) if (out / "summary.json").exists() else {}
    base = (summ.get("baseline") or {}).get("progress_mean")
    t = [float(r["t_min"]) for r in rows if r["progress_mean"]]
    p = [float(r["progress_mean"]) for r in rows if r["progress_mean"]]
    f = [float(r["floor_mean"]) for r in rows if r["floor_mean"]]
    fig, ax = plt.subplots(1, 2, figsize=(11, 4))
    ax[0].plot(t, p, color="#9db4d6", lw=0.8, label="per update")
    if wins:
        ax[0].plot([float(w["t_end_min"]) for w in wins], [float(w["progress_mean"]) for w in wins], "o-",
                   color="#1f4e8c", label="5-min window mean")
    if base is not None:
        ax[0].axhline(base, color="#b04a3a", ls="--", lw=1, label=f"uniform random ({base:.3f})")
    ax[0].set_xlabel("minutes")
    ax[0].set_ylabel("progress v0.2 (finished episodes)")
    ax[0].legend(frameon=False)
    ax[0].set_title("sts2train-smoke: protocol progress")
    ax[1].plot(t, f, color="#a9cfa4", lw=0.8, label="per update")
    if wins:
        ax[1].plot([float(w["t_end_min"]) for w in wins], [float(w["floor_mean"]) for w in wins], "o-",
                   color="#2e6b2a", label="5-min window mean")
    bf = (summ.get("baseline") or {}).get("floor_mean")
    if bf is not None:
        ax[1].axhline(bf, color="#b04a3a", ls="--", lw=1, label=f"uniform random ({bf:.2f})")
    ax[1].set_xlabel("minutes")
    ax[1].set_ylabel("floor reached (finished episodes)")
    ax[1].legend(frameon=False)
    ax[1].set_title("floor reached")
    for a in ax:
        a.spines[["top", "right"]].set_visible(False)
    fig.tight_layout()
    fig.savefig(out / "curve.png", dpi=130)


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="sts2train-smoke")
    sub = ap.add_subparsers(dest="cmd", required=True)
    tr = sub.add_parser("train", help="PPO smoke run on sts2vec")
    tr.add_argument("--minutes", type=float, default=50.0)
    tr.add_argument("--max-updates", type=int, default=0)
    tr.add_argument("--envs", type=int, default=1024)
    tr.add_argument("--threads", type=int, default=8)
    tr.add_argument("--rollout", type=int, default=32)
    tr.add_argument("--epochs", type=int, default=2)
    tr.add_argument("--minibatch", type=int, default=4096)
    tr.add_argument("--lr", type=float, default=3e-4)
    tr.add_argument("--gamma", type=float, default=1.0)
    tr.add_argument("--lam", type=float, default=0.95)
    tr.add_argument("--ent-coef", type=float, default=0.01)
    tr.add_argument("--objective", choices=["protocol", "win", "shaped"], default="protocol",
                    help="protocol (default): per-step delta of the protocol v0.2 progress + --win-bonus at a win; "
                         "win: 1 at a Victory terminal; shaped: LEGACY smoke objective (win + delta floor / 48)")
    tr.add_argument("--win-bonus", type=float, default=1.0, help="protocol objective: reward added at a Victory terminal")
    tr.add_argument("--shaped", action="store_true",
                    help="LEGACY (= --objective shaped): add delta(total_floor)/48 per step to the win/loss reward")
    tr.add_argument("--ascension", type=int, default=0)
    tr.add_argument("--pool", type=int, default=2048)
    tr.add_argument("--device", default="cpu", help="cpu | mps | cuda")
    tr.add_argument("--torch-threads", type=int, default=4)
    tr.add_argument("--seed", type=int, default=0)
    tr.add_argument("--out", default="tmp/train-smoke")
    tr.add_argument("--baseline-steps", type=int, default=300)
    tr.add_argument("--window-min", type=float, default=5.0)
    ev = sub.add_parser("eval", aliases=["evaluate"], help="play a protocol seed set with a checkpoint's greedy policy (protocol v0.2 NN entry)")
    ev.add_argument("--seedset", required=True, help="seed set name, id or path (sim/tools/eval/seedsets)")
    ev.add_argument("--checkpoint", required=True)
    ev.add_argument("--out", default=None, help="run directory (default tmp/eval/nn-<checkpoint>-<seedset>-<time>)")
    ev.add_argument("--threads", type=int, default=8)
    ev.add_argument("--device", default="cpu")
    pl = sub.add_parser("plot", help="re-draw curve.png from a train output directory")
    pl.add_argument("out")
    a = ap.parse_args(argv)
    if a.cmd == "train":
        from .ppo import TrainConfig, train

        cfg = TrainConfig(envs=a.envs, threads=a.threads, rollout=a.rollout, epochs=a.epochs, minibatch=a.minibatch,
                          lr=a.lr, gamma=a.gamma, lam=a.lam, ent_coef=a.ent_coef, objective=a.objective,
                          win_bonus=a.win_bonus, shaped=a.shaped, minutes=a.minutes,
                          max_updates=a.max_updates, ascension=a.ascension, pool=a.pool, device=a.device,
                          torch_threads=a.torch_threads, seed=a.seed, out=a.out, baseline_steps=a.baseline_steps,
                          window_min=a.window_min)
        if cfg.shaped or cfg.objective == "shaped":
            print("NOTE: --shaped is the LEGACY smoke objective (floor-reached shaping); the default is --objective protocol.")
        s = train(cfg, log=lambda m: print(m, flush=True))
        _plot(Path(a.out))
        print(json.dumps({k: v for k, v in s.items() if k not in ("windows", "config")}, indent=1, default=str))
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
