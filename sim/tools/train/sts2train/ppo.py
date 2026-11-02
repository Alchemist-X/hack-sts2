"""PPO on sts2vec (smoke scale).

Default objective is the contract's: reward 1 at a Victory terminal, else 0, gamma = 1 (CONTRACTS.md D5); progress and
floor are logged only. `--shaped` (SMOKE ONLY) adds the floor-reached shaping reward (delta total_floor / 48 per step)
so a short run on one Mac sees any learning signal at all; it is not the benchmark objective.
"""

from __future__ import annotations

import csv
import hashlib
import json
import math
import os
import time
from dataclasses import asdict, dataclass, field
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions
from sts2vec.pool import ALL_CHARACTERS

from .features import ModelSpec, RolloutStore, bind_spec, obs_to_torch
from .model import Policy, param_count
from .progress import ProgressTracker

ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]


@dataclass
class TrainConfig:
    envs: int = 1024
    threads: int = 8
    rollout: int = 32
    epochs: int = 2
    minibatch: int = 4096
    lr: float = 3e-4
    gamma: float = 1.0
    lam: float = 0.95
    clip: float = 0.2
    vf_coef: float = 0.5
    ent_coef: float = 0.01
    max_grad_norm: float = 0.5
    shaped: bool = False            # SMOKE ONLY: + delta(total_floor) / shaped_floors per step
    shaped_floors: float = 48.0
    minutes: float = 50.0
    max_updates: int = 0            # 0 = until `minutes`
    ascension: int = 0
    pool: int = 2048
    characters: tuple = ALL_CHARACTERS
    step_cap: int = 3000
    max_rows: int = 64              # training candidate / prompt-option rows (actions restricted to j < min(cand_n, 64))
    d: int = 96
    s: int = 256
    device: str = "cpu"
    torch_threads: int = 4
    seed: int = 0
    out: str = "tmp/train-smoke"
    window_min: float = 5.0
    ckpt_every_min: float = 5.0
    baseline_steps: int = 300       # uniform-random steps on the same env before training (0 = skip)


def weights_hash(model: torch.nn.Module) -> str:
    h = hashlib.sha256()
    for k, v in sorted(model.state_dict().items()):
        h.update(k.encode())
        h.update(v.detach().cpu().contiguous().numpy().tobytes())
    return h.hexdigest()[:16]


def save_checkpoint(path: Path, model: Policy, spec: ModelSpec, cfg: TrainConfig, extra: dict) -> str:
    wh = weights_hash(model)
    state = {k: v.detach().cpu() for k, v in model.state_dict().items()}
    torch.save({"spec": spec.to_json(), "model": state, "config": asdict(cfg), "weights_hash": wh, **extra}, path)
    return wh


def load_checkpoint(path, device="cpu") -> tuple[Policy, ModelSpec, dict]:
    ck = torch.load(path, map_location="cpu", weights_only=False)
    spec = ModelSpec.from_json(ck["spec"])
    cfg = ck.get("config") or {}
    model = Policy(spec, d=cfg.get("d", 96), s=cfg.get("s", 256))
    model.load_state_dict(ck["model"])
    model.to(device).eval()
    return model, spec, ck


class Windows:
    """Wall-clock windows (default 5 min) of finished-episode stats and timings -> CSV rows."""

    FIELDS = ["window", "t_end_min", "updates", "env_steps", "env_steps_per_s", "env_s", "infer_s", "update_s",
              "other_s", "episodes", "wins", "floor_mean", "progress_mean", "progress_median", "act_reached",
              "sim_error", "content_gap", "illegal", "steps_per_episode", "entropy", "value_loss", "policy_loss",
              "approx_kl", "clipfrac"]

    def __init__(self, path: Path, window_s: float):
        self.path, self.window_s = path, window_s
        self.rows: list[dict] = []
        self._f = open(path, "w", newline="")
        self._w = csv.DictWriter(self._f, fieldnames=self.FIELDS)
        self._w.writeheader()
        self.reset(0.0)

    def reset(self, t0: float):
        self.t0 = t0
        self.eps: list[dict] = []
        self.c = dict(updates=0, env_steps=0, env_s=0.0, infer_s=0.0, update_s=0.0, sim_error=0, content_gap=0,
                      illegal=0)
        self.stats: list[dict] = []

    def flush(self, now: float, start: float) -> dict:
        eps = self.eps
        prog = [e["progress"] for e in eps if e.get("progress") is not None]
        acts = {}
        for e in eps:
            acts[e["act"]] = acts.get(e["act"], 0) + 1
        wall = max(now - self.t0, 1e-9)
        st = self.stats
        mean = lambda k: float(np.mean([x[k] for x in st])) if st else float("nan")  # noqa: E731
        row = {"window": len(self.rows), "t_end_min": round((now - start) / 60, 2), "updates": self.c["updates"],
               "env_steps": self.c["env_steps"], "env_steps_per_s": round(self.c["env_steps"] / wall, 1),
               "env_s": round(self.c["env_s"], 2), "infer_s": round(self.c["infer_s"], 2),
               "update_s": round(self.c["update_s"], 2),
               "other_s": round(wall - self.c["env_s"] - self.c["infer_s"] - self.c["update_s"], 2),
               "episodes": len(eps), "wins": sum(1 for e in eps if e["win"]),
               "floor_mean": round(float(np.mean([e["floor"] for e in eps])), 4) if eps else float("nan"),
               "progress_mean": round(float(np.mean(prog)), 5) if prog else float("nan"),
               "progress_median": round(float(np.median(prog)), 5) if prog else float("nan"),
               "act_reached": json.dumps(dict(sorted(acts.items()))),
               "sim_error": self.c["sim_error"], "content_gap": self.c["content_gap"], "illegal": self.c["illegal"],
               "steps_per_episode": round(float(np.mean([e["steps"] for e in eps])), 1) if eps else float("nan"),
               "entropy": round(mean("entropy"), 4), "value_loss": round(mean("value_loss"), 5),
               "policy_loss": round(mean("policy_loss"), 5), "approx_kl": round(mean("approx_kl"), 5),
               "clipfrac": round(mean("clipfrac"), 4)}
        self.rows.append(row)
        self._w.writerow(row)
        self._f.flush()
        self.reset(now)
        return row

    def close(self):
        self._f.close()


def random_baseline(env: VecEnv, tracker: ProgressTracker, steps: int, seed: int) -> dict:
    """Uniform-random policy on the training env (same pool/tracker): finished-episode floor and progress."""
    rng = np.random.default_rng(seed)
    obs = env.reset()
    tracker.observe(obs["aux"])
    eps = []
    errs = 0
    for _ in range(steps):
        obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
        done = (term | trunc).astype(bool)
        errs += int(np.count_nonzero(status & ERR_BITS))
        eps += tracker.finished(done, info["last_episode"])
        tracker.observe(obs["aux"])
    prog = [e["progress"] for e in eps if e["progress"] is not None]
    return {"steps": steps, "episodes": len(eps), "floor_mean": float(np.mean([e["floor"] for e in eps])) if eps else None,
            "progress_mean": float(np.mean(prog)) if prog else None, "wins": sum(e["win"] for e in eps),
            "sim_error_or_gap": errs}


def train(cfg: TrainConfig, log=print) -> dict:
    out = Path(cfg.out)
    out.mkdir(parents=True, exist_ok=True)
    torch.manual_seed(cfg.seed)
    torch.set_num_threads(cfg.torch_threads)
    device = torch.device(cfg.device)
    rng = np.random.default_rng(cfg.seed)
    start = time.monotonic()

    pool = PoolSpec(characters=tuple(cfg.characters), ascension=cfg.ascension, count=cfg.pool,
                    seed_prefix=f"TRAIN{cfg.seed}")
    env = VecEnv(cfg.envs, threads=cfg.threads, step_cap=cfg.step_cap, auto_reset=True, seed_text=f"SMOKE{cfg.seed}",
                 pool=pool, pool_cache=max(4096, cfg.pool))
    spec = ModelSpec.from_schema(env.schema)
    bind_spec(spec)
    t = time.monotonic()
    warmed = env.pool.warm()
    warm_s = time.monotonic() - t
    log(f"pool warm: {warmed} templates in {warm_s:.1f} s; encoder_version {spec.encoder_version}")
    tracker = ProgressTracker(spec.aux_fields, env.schema["last_episode"].fields, cfg.envs, cfg.ascension)

    baseline = None
    if cfg.baseline_steps:
        t = time.monotonic()
        baseline = random_baseline(env, tracker, cfg.baseline_steps, cfg.seed + 1)
        baseline["seconds"] = round(time.monotonic() - t, 2)
        log(f"random baseline ({cfg.baseline_steps} steps x {cfg.envs}): {baseline}")

    model = Policy(spec, d=cfg.d, s=cfg.s).to(device)
    n_params = param_count(model)
    log(f"model: {n_params:,} parameters on {device}")
    opt = torch.optim.Adam(model.parameters(), lr=cfg.lr, eps=1e-5)

    dtypes = {k: env.buffers[k].dtype for k in env.buffers.arrays}
    store = RolloutStore(spec, cfg.rollout, cfg.envs, cfg.max_rows, dtypes)
    log(f"rollout store: {store.nbytes / 2**20:.0f} MiB")
    aux_floor = spec.cols["aux"]["total_floor"]
    le_floor = spec.cols["last_episode"]["floor"]

    obs = env.reset()
    tracker.observe(obs["aux"])
    win = Windows(out / "curve.csv", cfg.window_min * 60)
    updates_log = open(out / "updates.csv", "w", newline="")
    uw = csv.writer(updates_log)
    uw.writerow(["update", "t_min", "env_steps", "rollout_s", "env_s", "infer_s", "update_s", "episodes", "floor_mean",
                 "progress_mean", "entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac", "reward_sum"])
    train_start = time.monotonic()
    win.reset(train_start)
    last_ckpt = train_start
    totals = dict(env_steps=0, env_s=0.0, infer_s=0.0, update_s=0.0, episodes=0, sim_error=0, content_gap=0,
                  illegal=0, wins=0, overflow_cand=0)
    all_eps: list[dict] = []
    upd = 0
    errors_seen: dict[str, int] = {}

    while True:
        if cfg.max_updates and upd >= cfg.max_updates:
            break
        if not cfg.max_updates and time.monotonic() - train_start >= cfg.minutes * 60:
            break
        t_roll = time.monotonic()
        ep_this: list[dict] = []
        env_s = infer_s = 0.0
        model.eval()
        for t in range(cfg.rollout):
            t0 = time.monotonic()
            x = obs_to_torch(obs, device, max_rows=cfg.max_rows)
            with torch.no_grad():
                logits, value = model(x)
                dist = torch.distributions.Categorical(logits=logits)
                a = dist.sample()
                logp = dist.log_prob(a)
            a_np = a.cpu().numpy().astype(np.int32)
            cand_n = obs["cand_n"]
            valid = cand_n > 0
            a_np[~valid] = -1
            store.put_obs(t, obs)
            store.actions[t] = np.maximum(a_np, 0)
            store.logp[t] = logp.cpu().numpy()
            store.values[t] = value.cpu().numpy()
            store.valid[t] = valid
            prev_floor = tracker.prev[:, 1].copy()
            t1 = time.monotonic()
            obs, rew, term, trunc, status, info = env.step(a_np)
            t2 = time.monotonic()
            done = (term | trunc).astype(bool)
            fin = tracker.finished(done, info["last_episode"])
            floor_now = obs["aux"][:, aux_floor].astype(np.int64)
            floor_now[done] = info["last_episode"][done, le_floor]
            r = rew.astype(np.float32).copy()
            if cfg.shaped:
                r += (floor_now - prev_floor).astype(np.float32) / cfg.shaped_floors
            tracker.observe(obs["aux"])
            store.rewards[t] = r
            store.dones[t] = done
            se = int(np.count_nonzero(status & STATUS["SIM_ERROR"]))
            cg = int(np.count_nonzero(status & STATUS["CONTENT_GAP"]))
            il = int(np.count_nonzero(status & STATUS["ILLEGAL_ACTION"]))
            if se or cg:
                for slot, text in env.slot_errors().items():
                    key = text.splitlines()[0][:160] if text else "?"
                    errors_seen[key] = errors_seen.get(key, 0) + 1
            totals["overflow_cand"] += int(np.count_nonzero(cand_n > cfg.max_rows))
            for c, v in (("sim_error", se), ("content_gap", cg), ("illegal", il)):
                totals[c] += v
                win.c[c] += v
            n_steps = int(np.count_nonzero(valid))
            totals["env_steps"] += n_steps
            win.c["env_steps"] += n_steps
            infer_s += t1 - t0
            env_s += t2 - t1
            ep_this += fin
        # bootstrap
        t0 = time.monotonic()
        with torch.no_grad():
            _, last_v = model(obs_to_torch(obs, device, max_rows=cfg.max_rows))
        last_v = last_v.cpu().numpy()
        infer_s += time.monotonic() - t0
        adv = np.zeros_like(store.rewards)
        gae = np.zeros(cfg.envs, dtype=np.float32)
        for t in reversed(range(cfg.rollout)):
            nv = last_v if t == cfg.rollout - 1 else store.values[t + 1]
            nonterm = 1.0 - store.dones[t]
            delta = store.rewards[t] + cfg.gamma * nv * nonterm - store.values[t]
            gae = delta + cfg.gamma * cfg.lam * nonterm * gae
            adv[t] = gae
        ret = adv + store.values

        # update
        t_up = time.monotonic()
        model.train()
        flat = store.flat()
        f_act = store.actions.reshape(-1)
        f_logp = store.logp.reshape(-1)
        f_adv = adv.reshape(-1)
        f_ret = ret.reshape(-1)
        f_val = store.values.reshape(-1)
        idx_all = np.flatnonzero(store.valid.reshape(-1))
        a_mean, a_std = f_adv[idx_all].mean(), f_adv[idx_all].std() + 1e-8
        stats = []
        for _ in range(cfg.epochs):
            perm = rng.permutation(idx_all)
            for k in range(0, len(perm), cfg.minibatch):
                mb = np.sort(perm[k:k + cfg.minibatch])
                x = obs_to_torch(flat, device, max_rows=cfg.max_rows, index=mb)
                logits, v = model(x)
                logp_all = F.log_softmax(logits, -1)
                act = torch.from_numpy(f_act[mb]).to(device)
                newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
                probs = logp_all.exp()
                ent = -(probs * logp_all.clamp_min(-1e4)).sum(-1).mean()
                oldlogp = torch.from_numpy(f_logp[mb]).to(device)
                ratio = (newlogp - oldlogp).exp()
                madv = torch.from_numpy((f_adv[mb] - a_mean) / a_std).to(device)
                pg = -torch.min(ratio * madv, ratio.clamp(1 - cfg.clip, 1 + cfg.clip) * madv).mean()
                vret = torch.from_numpy(f_ret[mb]).to(device)
                vold = torch.from_numpy(f_val[mb]).to(device)
                vclip = vold + (v - vold).clamp(-cfg.clip, cfg.clip)
                vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
                loss = pg + cfg.vf_coef * vl - cfg.ent_coef * ent
                opt.zero_grad(set_to_none=True)
                loss.backward()
                torch.nn.utils.clip_grad_norm_(model.parameters(), cfg.max_grad_norm)
                opt.step()
                with torch.no_grad():
                    kl = ((ratio - 1) - (newlogp - oldlogp)).mean().item()
                    cf = ((ratio - 1).abs() > cfg.clip).float().mean().item()
                stats.append({"entropy": ent.item(), "value_loss": vl.item(), "policy_loss": pg.item(),
                              "approx_kl": kl, "clipfrac": cf, "loss": loss.item()})
                if not math.isfinite(loss.item()):
                    raise FloatingPointError(f"non-finite loss at update {upd}")
        update_s = time.monotonic() - t_up
        upd += 1
        totals["env_s"] += env_s
        totals["infer_s"] += infer_s
        totals["update_s"] += update_s
        totals["episodes"] += len(ep_this)
        totals["wins"] += sum(e["win"] for e in ep_this)
        all_eps += [{k: e[k] for k in ("win", "floor", "act", "progress", "steps")} for e in ep_this]
        win.c["updates"] += 1
        win.c["env_s"] += env_s
        win.c["infer_s"] += infer_s
        win.c["update_s"] += update_s
        win.eps += ep_this
        win.stats += stats
        prog = [e["progress"] for e in ep_this if e["progress"] is not None]
        sm = {k: float(np.mean([s[k] for s in stats])) for k in ("entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac")}
        uw.writerow([upd, round((time.monotonic() - train_start) / 60, 3), totals["env_steps"],
                     round(time.monotonic() - t_roll, 3), round(env_s, 3), round(infer_s, 3), round(update_s, 3),
                     len(ep_this), round(float(np.mean([e["floor"] for e in ep_this])), 3) if ep_this else "",
                     round(float(np.mean(prog)), 5) if prog else "", *(round(sm[k], 5) for k in sm),
                     round(float(store.rewards.sum()), 3)])
        updates_log.flush()
        now = time.monotonic()
        if upd == 1 or upd % 10 == 0:
            log(f"update {upd} t={(now - train_start) / 60:.1f} min steps={totals['env_steps']:,} "
                f"sps={totals['env_steps'] / max(now - train_start, 1e-9):,.0f} env={env_s:.2f}s infer={infer_s:.2f}s "
                f"upd={update_s:.2f}s eps={len(ep_this)} floor={np.mean([e['floor'] for e in ep_this]) if ep_this else float('nan'):.2f} "
                f"prog={np.mean(prog) if prog else float('nan'):.4f} ent={sm['entropy']:.3f} kl={sm['approx_kl']:.4f}")
        if now - win.t0 >= win.window_s:
            row = win.flush(now, train_start)
            log(f"[window {row['window']}] {row}")
        if now - last_ckpt >= cfg.ckpt_every_min * 60:
            save_checkpoint(out / "checkpoint.pt", model, spec, cfg, {"updates": upd, "env_steps": totals["env_steps"]})
            last_ckpt = now
    now = time.monotonic()
    if win.eps or win.c["updates"]:
        row = win.flush(now, train_start)
        log(f"[window {row['window']}] {row}")
    win.close()
    updates_log.close()
    wh = save_checkpoint(out / "checkpoint.pt", model, spec, cfg, {"updates": upd, "env_steps": totals["env_steps"]})
    env.close()
    wall = now - train_start
    summary = {
        "config": {k: (list(v) if isinstance(v, tuple) else v) for k, v in asdict(cfg).items()},
        "encoder_version": spec.encoder_version, "params": n_params, "weights_hash": wh,
        "pool_warm_s": round(warm_s, 2), "pool_warmed": warmed, "baseline": baseline,
        "updates": upd, "wall_s": round(wall, 1), "env_steps": totals["env_steps"],
        "env_steps_per_s": round(totals["env_steps"] / wall, 1),
        "env_s": round(totals["env_s"], 1), "infer_s": round(totals["infer_s"], 1), "update_s": round(totals["update_s"], 1),
        "env_only_steps_per_s": round(totals["env_steps"] / max(totals["env_s"], 1e-9), 1),
        "episodes": totals["episodes"], "wins": totals["wins"], "sim_error": totals["sim_error"],
        "content_gap": totals["content_gap"], "illegal": totals["illegal"], "cand_over_max_rows": totals["overflow_cand"],
        "errors": errors_seen, "windows": win.rows, "total_s": round(time.monotonic() - start, 1),
    }
    with open(out / "summary.json", "w") as f:
        json.dump(summary, f, indent=1, default=str)
    return summary
