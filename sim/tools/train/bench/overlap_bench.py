"""--overlap off vs double: phase timings of short stream-mode runs (same config, same seeds).

    uv run --project sim/tools/train python sim/tools/train/bench/overlap_bench.py --out tmp/bench-overlap \
        --envs 32 --rollout 16 --updates 6 --preset small --threads 4 --torch-threads 2 [--device cuda]

Prints, per mode, the median (over --repeats interleaved runs) of the median over updates 2.. (update 1 = warm-up) of t_env_s, t_infer_s, t_update_s, t_total_s and,
for double, t_overlap_wait_s / t_rollout_s, then the A100 projection (README "Rollout overlap"). Writes <out>/bench.json.
"""

from __future__ import annotations

import argparse
import json
import statistics
from pathlib import Path

from sts2train.ppo import resolve_config, train
from sts2train.runlog import read_jsonl

KEYS = ("t_env_s", "t_infer_s", "t_update_s", "t_other_s", "t_total_s", "t_overlap_wait_s", "t_rollout_s")


def arch(a) -> dict:
    """--arch d,s,depth,heads,ff_mult (overrides --preset)."""
    if not a.arch:
        return {}
    d, s, depth, heads, ff = (int(v) for v in a.arch.split(","))
    return {"preset": None, "d": d, "s": s, "depth": depth, "heads": heads, "ff_mult": ff}


def run(out: Path, mode: str, a, rep: int = 0) -> dict:
    given = {"out": str(out / f"{mode}-{rep}"), "envs": a.envs, "rollout": a.rollout, "max_updates": a.updates,
             "threads": a.threads, "torch_threads": a.torch_threads, "device": a.device, "preset": a.preset, **arch(a),
             "minibatch": a.minibatch, "epochs": a.epochs, "ckpt_every": 0, "ckpt_every_min": 0, "eval_every": 0,
             "max_hours": 0.5, "log_every": 100, "characters": ("SILENT",), "ascension": 10,
             "start_mode": "stream", "seed": a.seed, "init_seed": a.seed, "sample_seed": a.seed, "overlap": mode}
    train(resolve_config(given), log=lambda m: None)
    rows = read_jsonl(out / f"{mode}-{rep}" / "metrics.jsonl")
    rows = rows[1:] or rows
    res = {k: round(statistics.median(r[k] for r in rows), 4) for k in KEYS if k in rows[0]}
    res["updates_averaged"] = len(rows)
    res["env_steps_update"] = rows[0]["env_steps_update"]
    return res


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--out", default="tmp/bench-overlap")
    p.add_argument("--envs", type=int, default=32)
    p.add_argument("--rollout", type=int, default=16)
    p.add_argument("--updates", type=int, default=6)
    p.add_argument("--preset", default="small")
    p.add_argument("--arch", help="d,s,depth,heads,ff_mult instead of --preset (e.g. 32,64,1,2,2)")
    p.add_argument("--threads", type=int, default=4)
    p.add_argument("--torch-threads", type=int, default=2)
    p.add_argument("--minibatch", type=int, default=256)
    p.add_argument("--epochs", type=int, default=1)
    p.add_argument("--device", default="cpu")
    p.add_argument("--seed", type=int, default=11)
    p.add_argument("--modes", default="off,double")
    p.add_argument("--repeats", type=int, default=1)
    a = p.parse_args()
    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    modes = a.modes.split(",")
    reps: dict[str, list[dict]] = {m: [] for m in modes}
    for i in range(a.repeats):                  # interleaved (off, double, off, double ...): shared-host noise
        for m in modes:
            reps[m].append(run(out, m, a, i))
    res = {m: {k: round(statistics.median(r[k] for r in rs), 4) for k in rs[0]} for m, rs in reps.items()}
    res["repeats"] = reps
    off, dbl = res.get("off"), res.get("double")
    if off and dbl:
        r_env = dbl["t_env_s"] / max(off["t_env_s"], 1e-9)
        r_inf = dbl["t_infer_s"] / max(off["t_infer_s"], 1e-9)
        roll_off = off["t_env_s"] + off["t_infer_s"]
        res["measured"] = {"rollout_off_s": round(roll_off, 4), "rollout_double_s": dbl["t_rollout_s"],
                           "rollout_speedup": round(roll_off / max(dbl["t_rollout_s"], 1e-9), 3),
                           "total_speedup": round(off["t_total_s"] / max(dbl["t_total_s"], 1e-9), 3),
                           "env_ratio": round(r_env, 3), "infer_ratio": round(r_inf, 3)}
        # A100 per update today: env 2.4 s, infer 2.7 s, update 3.2 s (serial: 8.3 s). Double: the rollout costs the
        # larger of the two overlapped phases (scaled by the measured half-batch ratios) plus the measured residual.
        env, inf, upd = 2.4, 2.7, 3.2
        resid = max(0.0, dbl["t_rollout_s"] - max(dbl["t_env_s"], dbl["t_infer_s"])) / max(roll_off, 1e-9)
        roll = max(env * r_env, inf * r_inf) + resid * (env + inf)
        res["a100_projection"] = {"off_s": round(env + inf + upd, 2), "double_s": round(roll + upd, 2),
                                  "ideal_double_s": round(max(env, inf) + upd, 2),
                                  "speedup": round((env + inf + upd) / (roll + upd), 3)}
    (out / "bench.json").write_text(json.dumps(res, indent=2))
    print(json.dumps(res, indent=2))


if __name__ == "__main__":
    main()
