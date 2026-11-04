"""PPO on sts2vec, built for multi-hour runs on one GPU box (and still fine for a CPU / MPS smoke).

Objectives (``sts2train.objective``): ``protocol`` (default) = per-step change of the STS2-Bench v0.2 progress metric
(the function ``sts2vec eval`` scores with) + ``win_bonus`` at a Victory terminal, so an episode's return is its
protocol progress (+ bonus); ``win`` = the contract's reward (1 at a Victory terminal, else 0; CONTRACTS.md D5);
``shaped`` (LEGACY, ``--shaped``) = ``win`` + delta(total_floor) / 48 per step, the 2026-11-02 smoke objective.

Long-run machinery: checkpoints every N updates and every M minutes (atomic, last K kept, ``best.pt`` by eval
progress), ``--resume <ckpt>|auto`` (model, optimizer, counters, RNG states; the env restarts from fresh episodes),
``metrics.jsonl`` (one row per update), ``status.json`` (rewritten every update), periodic protocol evaluation on a
seed set (``sts2vec.evaluate`` through ``sts2train.evaluate``) into ``evals/``, a wall-clock budget (``max_hours``),
graceful stop on SIGINT / SIGTERM (checkpoint, then exit), bf16 autocast on CUDA behind ``amp``. Layout of the run
directory: ``sts2train.runlog``.

The model reads every shape, vocabulary size and column index from the env's published schema at start-up
(``ModelSpec.from_schema``); buffers it does not use are ignored, so a library with extra observation buffers (a new
``encoder_version``) trains unchanged. ``floor_step_cap`` is passed to ``VecEnv`` only when set, and only if this
sts2vec's ``VecEnv`` accepts it.
"""

from __future__ import annotations

import contextlib
import hashlib
import inspect
import os
import platform
import signal
import socket
import threading
import time
from collections import deque
from dataclasses import asdict, dataclass, fields
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from sts2vec import STATUS, PoolSpec, VecEnv, random_actions
from sts2vec.pool import ALL_CHARACTERS

from .features import ModelSpec, RolloutStore, bind_spec, obs_to_device
from .model import ARCH_KEYS, PRESETS, Policy, activation_bytes_per_sample, arch_of, param_count
from .objective import OBJECTIVES, EpisodeRewards
from .progress import ProgressTracker
from .runlog import CheckpointManager, JsonlWriter, atomic_torch_save, atomic_write_json, read_jsonl

ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
CKPT_FORMAT = 2
LOCAL_PARAM_LIMIT = 3_000_000       # above this (or the large preset) cpu / mps training needs --allow-big-local
MPS_HIGH_WATERMARK = "0.3"          # default PYTORCH_MPS_HIGH_WATERMARK_RATIO (a 24 GB Mac is a smoke box)


@dataclass
class TrainConfig:
    # env
    envs: int = 1024
    threads: int = 0                # env worker threads (0 = os.cpu_count())
    step_cap: int = 3000
    floor_step_cap: int | None = None   # VecEnv pass-through (stall truncation) when set and supported
    ascension: int = 0
    pool: int = 2048
    characters: tuple = ALL_CHARACTERS
    # objective
    objective: str = "protocol"     # protocol | win | shaped (see sts2train.objective)
    win_bonus: float = 1.0          # protocol objective: extra reward at a Victory terminal (progress is 1.0 there)
    shaped: bool = False            # LEGACY flag: same as objective="shaped" (delta(total_floor) / shaped_floors)
    shaped_floors: float = 48.0
    # PPO
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
    max_rows: int = 64              # training candidate / prompt-option rows (actions restricted to j < min(cand_n, 64))
    # model (sts2train.model.PRESETS)
    preset: str | None = None
    d: int = 96                     # d_model
    s: int = 256                    # state width
    depth: int = 0                  # transformer trunk blocks (0 = the original pooled-concat model)
    heads: int = 4
    ff_mult: int = 4
    # runtime
    device: str = "cpu"             # cpu | mps | cuda[:i] | auto
    amp: bool = False               # bf16 autocast (CUDA only)
    grad_ckpt: bool = False         # recompute the trunk blocks in backward (less activation memory, more compute)
    allow_big_local: bool = False   # override the cpu / mps size guard (never inherited from a checkpoint)
    torch_threads: int = 0          # 0 = min(os.cpu_count(), 16)
    seed: int = 0
    out: str = "tmp/train/run"
    # budget (whichever comes first; max_updates / max_env_steps count across resumes, max_hours is per session)
    max_hours: float = 1.0
    max_updates: int = 0
    max_env_steps: int = 0
    # checkpoints / logs / eval
    ckpt_every: int = 50            # updates (0 = off)
    ckpt_every_min: float = 15.0    # minutes (0 = off)
    keep_ckpts: int = 3
    eval_every: int = 0             # updates (0 = off); a final eval also runs when a budget ends the session
    eval_seedset: str = "std-a0-v1"
    eval_threads: int = 0           # 0 = min(os.cpu_count(), 8)
    log_every: int = 10
    baseline_steps: int = 0         # uniform-random steps on the same env before training (0 = skip)


CONFIG_FIELDS = {f.name for f in fields(TrainConfig)}


def resolve_config(given: dict, base: dict | None = None) -> TrainConfig:
    """dataclass defaults < `base` (a resumed checkpoint's config) < preset (`given["preset"]`) < other `given`."""
    cfg = asdict(TrainConfig())
    if base:
        cfg.update({k: v for k, v in base.items() if k in CONFIG_FIELDS and k != "allow_big_local"})
    preset = given.get("preset")
    if preset:
        if preset not in PRESETS:
            raise ValueError(f"preset {preset!r}: expected one of {sorted(PRESETS)}")
        cfg.update(PRESETS[preset])
    unknown = set(given) - CONFIG_FIELDS
    if unknown:
        raise ValueError(f"unknown config keys {sorted(unknown)}")
    cfg.update(given)
    cfg["characters"] = tuple(cfg["characters"])
    out = TrainConfig(**cfg)
    if out.shaped:
        out.objective = "shaped"
    if out.objective not in OBJECTIVES:
        raise ValueError(f"objective {out.objective!r}: expected one of {OBJECTIVES}")
    return out


def config_dict(cfg: TrainConfig) -> dict:
    return {k: (list(v) if isinstance(v, tuple) else v) for k, v in asdict(cfg).items()}


# ------------------------------------------------------------------------------------------------ checkpoints


def weights_hash(model: torch.nn.Module) -> str:
    h = hashlib.sha256()
    for k, v in sorted(model.state_dict().items()):
        h.update(k.encode())
        h.update(v.detach().cpu().contiguous().numpy().tobytes())
    return h.hexdigest()[:16]


def save_checkpoint(path: Path, model: Policy, spec: ModelSpec, cfg: TrainConfig, extra: dict) -> str:
    """A weights-only checkpoint (eval / tests); training checkpoints add optimizer, counters and RNG states."""
    wh = weights_hash(model)
    state = {k: v.detach().cpu() for k, v in model.state_dict().items()}
    atomic_torch_save({"format": CKPT_FORMAT, "spec": spec.to_json(), "model": state, "config": config_dict(cfg),
                       "arch": model.arch, "weights_hash": wh, **extra}, path)
    return wh


def load_checkpoint(path, device="cpu") -> tuple[Policy, ModelSpec, dict]:
    ck = torch.load(path, map_location="cpu", weights_only=False)
    spec = ModelSpec.from_json(ck["spec"])
    model = Policy(spec, **arch_of(ck.get("arch") or ck.get("config")))
    model.load_state_dict(ck["model"])
    model.to(device).eval()
    return model, spec, ck


def make_checkpoint(model: Policy, opt: torch.optim.Optimizer, spec: ModelSpec, cfg: TrainConfig, counters: dict,
                    rng: np.random.Generator, device: torch.device, **extra) -> dict:
    """A resumable training checkpoint: weights, optimizer, counters, RNG states, spec and config."""
    return {"format": CKPT_FORMAT, "spec": spec.to_json(), "config": config_dict(cfg), "arch": model.arch,
            "model": {k: v.detach().cpu() for k, v in model.state_dict().items()}, "weights_hash": weights_hash(model),
            "optimizer": opt.state_dict(), "updates": int(counters.get("updates", 0)),
            "env_steps": int(counters.get("env_steps", 0)), "counters": dict(counters), "rng": _rng_state(rng, device),
            "params": param_count(model), "encoder_version": spec.encoder_version, "saved_at": _now_iso(), **extra}


def restore_checkpoint(ck: dict, model: Policy, opt: torch.optim.Optimizer | None, rng: np.random.Generator | None,
                       device: torch.device, lr: float | None = None) -> None:
    """Load weights (strict: parameter shapes must match), optimizer state (moved to the parameters' device) and RNG
    states from a `make_checkpoint` dict."""
    model.load_state_dict(ck["model"])
    if opt is not None and ck.get("optimizer"):
        opt.load_state_dict(ck["optimizer"])
        if lr is not None:
            for g in opt.param_groups:
                g["lr"] = lr
    if rng is not None:
        _set_rng_state(rng, ck.get("rng"), device)


def _rng_state(rng: np.random.Generator, device: torch.device) -> dict:
    st = {"numpy": rng.bit_generator.state, "torch": torch.get_rng_state()}
    if device.type == "cuda":
        st["cuda"] = torch.cuda.get_rng_state_all()
    if device.type == "mps" and hasattr(torch, "mps"):
        try:
            st["mps"] = torch.mps.get_rng_state()
        except Exception:
            pass
    return st


def _set_rng_state(rng: np.random.Generator, st: dict, device: torch.device) -> None:
    if not st:
        return
    rng.bit_generator.state = st["numpy"]
    torch.set_rng_state(st["torch"])
    if device.type == "cuda" and "cuda" in st:
        with contextlib.suppress(Exception):
            torch.cuda.set_rng_state_all(st["cuda"])
    if device.type == "mps" and "mps" in st:
        with contextlib.suppress(Exception):
            torch.mps.set_rng_state(st["mps"])


# ------------------------------------------------------------------------------------------------ env / device


def env_kwargs(cfg: TrainConfig, seed_text: str, env_cls=VecEnv) -> dict:
    """VecEnv keyword arguments; `floor_step_cap` only when set, and an error if this VecEnv does not take it."""
    pool = PoolSpec(characters=tuple(cfg.characters), ascension=cfg.ascension, count=cfg.pool,
                    seed_prefix=f"TRAIN{cfg.seed}")
    kw = dict(threads=cfg.threads or os.cpu_count() or 8, step_cap=cfg.step_cap, auto_reset=True, seed_text=seed_text,
              pool=pool, pool_cache=max(4096, cfg.pool))
    if cfg.floor_step_cap is not None:
        if "floor_step_cap" not in inspect.signature(env_cls.__init__).parameters:
            raise ValueError("floor_step_cap is set but this sts2vec VecEnv has no floor_step_cap parameter "
                             "(install an sts2vec with the stall truncation, or drop --floor-step-cap)")
        kw["floor_step_cap"] = int(cfg.floor_step_cap)
    return kw


class BigLocalRunError(RuntimeError):
    """A model too big for a cpu / mps box (see `check_local_size`)."""


def check_local_size(cfg: TrainConfig, n_params: int, act_per_sample: float, device: torch.device) -> None:
    """Refuse cpu / mps training of a model above LOCAL_PARAM_LIMIT parameters or of the large preset unless
    `allow_big_local` (the large preset is for --device cuda: one fp32 minibatch of 4096 saves ~30 GiB)."""
    if device.type not in ("cpu", "mps") or cfg.allow_big_local:
        return
    if n_params <= LOCAL_PARAM_LIMIT and cfg.preset != "large":
        return
    gib = act_per_sample * cfg.minibatch / 2 ** 30
    raise BigLocalRunError(
        f"refusing to train a {n_params:,}-parameter model" + (f" (preset {cfg.preset})" if cfg.preset else "")
        + f" on {device.type}: one PPO minibatch of {cfg.minibatch} saves an estimated {gib:.1f} GiB of activations "
        f"(fp32{', trunk checkpointing' if cfg.grad_ckpt else ''}; {act_per_sample / 2 ** 20:.2f} MiB/sample) on top of "
        f"the rollout store. Models above {LOCAL_PARAM_LIMIT:,} parameters and the large preset are for --device cuda; "
        f"use --preset smoke or small for a local smoke, or pass --allow-big-local to override.")


def cap_mps_memory(log=print) -> None:
    """Default the MPS allocator's high watermark to MPS_HIGH_WATERMARK (and the low one below it, which torch
    requires) unless set; must run before the first MPS allocation of the process."""
    hi = os.environ.setdefault("PYTORCH_MPS_HIGH_WATERMARK_RATIO", MPS_HIGH_WATERMARK)
    if "PYTORCH_MPS_LOW_WATERMARK_RATIO" not in os.environ:
        with contextlib.suppress(ValueError):
            os.environ["PYTORCH_MPS_LOW_WATERMARK_RATIO"] = f"{min(float(hi), 1.4) * 0.8:.3f}"
    log(f"MPS memory caps: high watermark {os.environ['PYTORCH_MPS_HIGH_WATERMARK_RATIO']}, low "
        f"{os.environ.get('PYTORCH_MPS_LOW_WATERMARK_RATIO')} (PYTORCH_MPS_*_WATERMARK_RATIO)")


def pick_device(name: str) -> torch.device:
    if name == "auto":
        if torch.cuda.is_available():
            return torch.device("cuda")
        if getattr(torch.backends, "mps", None) is not None and torch.backends.mps.is_available():
            return torch.device("mps")
        return torch.device("cpu")
    dev = torch.device(name)
    if dev.type == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("--device cuda but torch.cuda.is_available() is False")
    if dev.type == "mps" and not (getattr(torch.backends, "mps", None) and torch.backends.mps.is_available()):
        raise RuntimeError("--device mps but the MPS backend is not available")
    return dev


def gpu_memory(device: torch.device) -> dict:
    mb = 2 ** 20
    if device.type == "cuda":
        return {"gpu_mem_alloc_mb": round(torch.cuda.memory_allocated(device) / mb, 1),
                "gpu_mem_peak_mb": round(torch.cuda.max_memory_allocated(device) / mb, 1),
                "gpu_mem_reserved_mb": round(torch.cuda.memory_reserved(device) / mb, 1)}
    if device.type == "mps":
        with contextlib.suppress(Exception):
            return {"gpu_mem_alloc_mb": round(torch.mps.current_allocated_memory() / mb, 1),
                    "gpu_mem_reserved_mb": round(torch.mps.driver_allocated_memory() / mb, 1)}
    return {}


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


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


def _mean(xs) -> float | None:
    return float(np.mean(xs)) if len(xs) else None


def _episode_stats(eps: list[dict]) -> dict:
    prog = [e["progress"] for e in eps if e.get("progress") is not None]
    acts: dict[int, int] = {}
    for e in eps:
        acts[e["act"]] = acts.get(e["act"], 0) + 1
    n = len(eps)
    wins = sum(1 for e in eps if e["win"])
    return {"episodes": n, "wins": wins, "win_rate": wins / n if n else None, "progress_mean": _mean(prog),
            "progress_median": float(np.median(prog)) if prog else None,
            "floor_mean": _mean([e["floor"] for e in eps]), "return_mean": _mean([e["return"] for e in eps]),
            "steps_per_episode": _mean([e["steps"] for e in eps]), "act_reached": dict(sorted(acts.items()))}


# ------------------------------------------------------------------------------------------------ run planning


def plan_run(given: dict, resume: str | None = None, log=print) -> tuple[TrainConfig, dict | None, Path | None]:
    """Resolve the config of a fresh or resumed run -> (config, checkpoint dict or None, checkpoint path or None).

    `resume`: a checkpoint path, or "auto" = the newest checkpoint under `given["out"]` (a fresh run if none).
    On resume the checkpoint's config is the base and only `given` keys override it; the architecture must match."""
    given = dict(given)
    ck_path = None
    if resume == "auto":
        if not given.get("out"):
            raise ValueError("--resume auto needs --out RUN_DIR")
        ck_path = CheckpointManager(given["out"]).latest()
        if ck_path is None:
            log(f"resume auto: no checkpoint under {given['out']}/checkpoints, starting a fresh run")
    elif resume:
        ck_path = Path(resume)
        if not ck_path.exists():
            raise FileNotFoundError(f"--resume {resume}: no such checkpoint")
        if not given.get("out"):
            given["out"] = str(ck_path.parent.parent if ck_path.parent.name == "checkpoints" else ck_path.parent)
    ck = torch.load(ck_path, map_location="cpu", weights_only=False) if ck_path else None
    if ck is not None and "optimizer" not in ck:
        raise ValueError(f"{ck_path} is a weights-only checkpoint (no optimizer / counters); it cannot be resumed")
    cfg = resolve_config(given, ck.get("config") if ck else None)
    if ck is not None:
        want, have = arch_of(ck.get("arch") or ck.get("config")), {k: getattr(cfg, k) for k in ARCH_KEYS}
        if want != have:
            raise ValueError(f"architecture {have} differs from the checkpoint's {want}; drop the model flags to resume")
    elif CheckpointManager(cfg.out).latest() is not None:
        raise ValueError(f"{cfg.out} already holds checkpoints: pass --resume auto (continue) or a new --out")
    return cfg, ck, ck_path


# ------------------------------------------------------------------------------------------------ training


def train(cfg: TrainConfig, log=print, resume: dict | None = None, resume_path: Path | None = None) -> dict:
    """Train until a budget ends the session (or SIGINT / SIGTERM). `resume` = a checkpoint dict from `plan_run`."""
    session_start = time.monotonic()
    run = Path(cfg.out)
    run.mkdir(parents=True, exist_ok=True)
    device = pick_device(cfg.device)
    if device.type == "mps":
        cap_mps_memory(log)
    torch.set_num_threads(cfg.torch_threads or min(os.cpu_count() or 4, 16))
    amp = bool(cfg.amp) and device.type == "cuda"
    if cfg.amp and not amp:
        log(f"note: --amp only applies on CUDA; running {device} in float32")
    if device.type == "cuda":
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    autocast = (lambda: torch.autocast("cuda", dtype=torch.bfloat16)) if amp else contextlib.nullcontext
    torch.manual_seed(cfg.seed)
    rng = np.random.default_rng(cfg.seed)

    c = dict(updates=0, env_steps=0, episodes=0, wins=0, sim_error=0, content_gap=0, illegal=0, overflow_cand=0,
             wall_s=0.0, sessions=0)
    best_eval = last_eval = None
    if resume is not None:
        c.update({k: v for k, v in (resume.get("counters") or {}).items() if k in c})
        c["updates"] = int(resume.get("updates", c["updates"]))
        c["env_steps"] = int(resume.get("env_steps", c["env_steps"]))
        best_eval, last_eval = resume.get("best_eval"), resume.get("last_eval")
    c["sessions"] += 1
    upd0 = c["updates"]

    seed_text = f"PPO{cfg.seed}" if resume is None else f"PPO{cfg.seed}R{upd0}"
    env = VecEnv(cfg.envs, **env_kwargs(cfg, seed_text))
    old_handlers: dict = {}
    try:
        spec = ModelSpec.from_schema(env.schema)
        bind_spec(spec)
        if resume is not None and resume["spec"].get("encoder_version") != spec.encoder_version:
            log(f"warning: checkpoint encoder_version {resume['spec'].get('encoder_version')} != library "
                f"{spec.encoder_version}; resuming (parameter shapes are checked on load)")
        arch = {k: getattr(cfg, k) for k in ARCH_KEYS}
        model = Policy(spec, **arch)                       # on the CPU until the size guard has passed
        model.grad_ckpt = bool(cfg.grad_ckpt)
        n_params = param_count(model)
        act = activation_bytes_per_sample(model, spec, rows=cfg.max_rows)
        check_local_size(cfg, n_params, act, device)
        mb_gib = act * cfg.minibatch / 2 ** 30
        log(f"model: {n_params:,} parameters ({', '.join(f'{k}={v}' for k, v in arch.items())}); estimated saved "
            f"activations {act / 2 ** 20:.2f} MiB/sample -> {mb_gib:.2f} GiB per minibatch of {cfg.minibatch} (fp32"
            + (", trunk checkpointing" if cfg.grad_ckpt else "") + ("; bf16 autocast roughly halves it)" if amp else ")"))
        if device.type == "cuda":
            free, total = torch.cuda.mem_get_info(device)
            need = mb_gib * 2 ** 30 * (0.55 if amp else 1.0)
            if need > 0.7 * free:
                log(f"WARNING: the minibatch's activations (~{need / 2 ** 30:.1f} GiB) exceed 70% of the free GPU memory "
                    f"({free / 2 ** 30:.1f} of {total / 2 ** 30:.1f} GiB): expect an out-of-memory error; use --amp, "
                    f"--grad-checkpoint or a smaller --minibatch")
        t = time.monotonic()
        warmed = env.pool.warm()
        warm_s = time.monotonic() - t
        log(f"library {env.lib.path} ({env.lib.mode}) {env.lib.version()}; pool warm: {warmed} templates in "
            f"{warm_s:.1f} s; {cfg.envs} envs x {env.threads} threads")
        rewards = EpisodeRewards(cfg.objective, spec.aux_fields, env.schema["last_episode"].fields, cfg.envs,
                                 cfg.ascension, win_bonus=cfg.win_bonus, shaped_floors=cfg.shaped_floors)
        log(f"objective: {cfg.objective}" + (f" (win bonus {cfg.win_bonus})" if cfg.objective == "protocol" else ""))

        baseline = None
        if cfg.baseline_steps and resume is None:
            t = time.monotonic()
            tracker = ProgressTracker(spec.aux_fields, env.schema["last_episode"].fields, cfg.envs, cfg.ascension)
            baseline = random_baseline(env, tracker, cfg.baseline_steps, cfg.seed + 1)
            baseline["seconds"] = round(time.monotonic() - t, 2)
            log(f"random baseline ({cfg.baseline_steps} steps x {cfg.envs}): {baseline}")

        model = model.to(device)
        opt = torch.optim.Adam(model.parameters(), lr=cfg.lr, eps=1e-5)
        if resume is not None:
            restore_checkpoint(resume, model, opt, rng, device, lr=cfg.lr)
            log(f"resumed from {resume_path} at update {upd0:,} ({c['env_steps']:,} env steps, session {c['sessions']})")
        log(f"device: {device}" + (" [bf16 autocast]" if amp else "") + (" [trunk checkpointing]" if cfg.grad_ckpt else ""))

        dtypes = {k: env.buffers[k].dtype for k in env.buffers.arrays}
        store = RolloutStore(spec, cfg.rollout, cfg.envs, cfg.max_rows, dtypes, device)
        log(f"rollout store: {store.nbytes / 2**20:.0f} MiB on {device}")

        ckpts = CheckpointManager(run, cfg.keep_ckpts)
        metrics = JsonlWriter(run / "metrics.jsonl", keep_upto=upd0)
        evals = JsonlWriter(run / "evals.jsonl", keep_upto=upd0)
        last_ckpt_path = resume_path if resume is not None else None
        last_ckpt_upd = upd0 if resume is not None else -1
        if cfg.eval_every:
            from sts2vec.protocol import load_seedset

            load_seedset(cfg.eval_seedset)          # fail now, not hours in, on an unknown seed set

        prev_cfg = {}
        if (run / "config.json").exists():
            import json

            with contextlib.suppress(Exception):
                prev_cfg = json.loads((run / "config.json").read_text())
        sessions = list(prev_cfg.get("sessions", []))[: c["sessions"] - 1]
        sessions.append({"session": c["sessions"], "started_at": _now_iso(), "update_start": upd0,
                         "resumed_from": str(resume_path) if resume_path else None, "host": socket.gethostname(),
                         "pid": os.getpid(), "device": str(device), "amp": amp, "torch": torch.__version__,
                         "cuda_device": torch.cuda.get_device_name(device) if device.type == "cuda" else None,
                         "python": platform.python_version(), "cpu_count": os.cpu_count(), "env_threads": env.threads,
                         "bundle": os.environ.get("STS2TRAIN_BUNDLE")})
        from sts2vec.evaluate import repo_sha

        atomic_write_json(run / "config.json", {
            "config": config_dict(cfg), "arch": arch, "params": n_params, "encoder_version": spec.encoder_version,
            "library": {"path": str(env.lib.path), "mode": env.lib.mode, "version": env.lib.version()},
            "repo_sha": repo_sha(), "sessions": sessions})

        stop = {"signal": None}

        def on_signal(signum, frame):
            if stop["signal"] is not None and signum == signal.SIGINT:
                raise KeyboardInterrupt
            stop["signal"] = signal.Signals(signum).name
            log(f"{stop['signal']}: stopping after this update (checkpoint first); send SIGINT again to abort")

        if threading.current_thread() is threading.main_thread():
            for sg in (signal.SIGINT, signal.SIGTERM):
                old_handlers[sg] = signal.signal(sg, on_signal)

        def save(upd: int) -> Path:
            nonlocal last_ckpt_path, last_ckpt_upd
            state = make_checkpoint(model, opt, spec, cfg, dict(c, updates=upd), rng, device, best_eval=best_eval,
                                    last_eval=last_eval)
            last_ckpt_path = ckpts.save(state, upd)
            last_ckpt_upd = upd
            return last_ckpt_path

        def run_eval(upd: int) -> dict | None:
            nonlocal best_eval, last_eval
            from .evaluate import evaluate_model

            if last_ckpt_upd != upd:
                save(upd)
            out = run / "evals" / f"u{upd:07d}"
            t = time.monotonic()
            model.eval()
            try:
                rep = evaluate_model(model, spec, cfg.eval_seedset, str(out), weights_hash=weights_hash(model),
                                     checkpoint=str(last_ckpt_path), threads=cfg.eval_threads or min(os.cpu_count() or 4, 8),
                                     device=str(device), log=lambda m: None)
            except Exception as e:  # an eval failure must not end a multi-hour run
                log(f"eval at update {upd} failed: {type(e).__name__}: {e}")
                row = {"update": upd, "env_steps": c["env_steps"], "seedset": cfg.eval_seedset,
                       "error": f"{type(e).__name__}: {e}", "seconds": round(time.monotonic() - t, 2)}
                evals.write(row)
                return row
            h = rep["headline"]
            row = {"update": upd, "env_steps": c["env_steps"], "wall_s": round(c["wall_s"], 1), "time": _now_iso(),
                   "seedset": cfg.eval_seedset, "progress_mean": h["progress_mean"],
                   "progress_ci95": h.get("progress_ci95"), "progress_median": h.get("progress_median"),
                   "wins": h["wins"], "games": h["games"], "win_rate": h["wins"] / max(h["games"], 1),
                   "floor_mean": h.get("floor_mean"), "results": h.get("results"), "act_reached": h.get("act_reached"),
                   "replayable": h.get("replayable"), "weights_hash": weights_hash(model),
                   "checkpoint": _rel(last_ckpt_path, run), "dir": _rel(out, run),
                   "seconds": round(time.monotonic() - t, 2)}
            evals.write(row)
            last_eval = row
            if best_eval is None or (row["progress_mean"] or 0) > (best_eval.get("progress_mean") or 0):
                best_eval = row
                ckpts.mark_best(last_ckpt_path)
            log(f"[eval u{upd}] {cfg.eval_seedset}: progress {row['progress_mean']:.4f} "
                f"wins {row['wins']}/{row['games']} floor {row['floor_mean']} ({row['seconds']:.1f} s)"
                + (" *best*" if best_eval is row else ""))
            return row

        recent: deque = deque(maxlen=20)
        errors_seen: dict[str, int] = {}
        totals_t = dict(env_s=0.0, infer_s=0.0, update_s=0.0, eval_s=0.0, ckpt_s=0.0)
        session_steps = 0
        state = "running"

        last_sps: float | None = None

        def write_status(upd: int, state: str, reason: str | None = None) -> None:
            r_eps = sum(x["episodes"] for x in recent)
            atomic_write_json(run / "status.json", {
                "state": state, "stop_reason": reason, "update": upd, "env_steps": c["env_steps"],
                "episodes": c["episodes"], "wins": c["wins"], "wall_s": round(c["wall_s"], 1),
                "session": c["sessions"], "session_s": round(time.monotonic() - session_start, 1),
                "env_steps_per_s": last_sps,
                "env_steps_per_s_session": round(session_steps / max(time.monotonic() - train_start, 1e-9), 1),
                "recent": {"updates": len(recent), "episodes": r_eps,
                           "progress_mean": (sum(x["progress_sum"] for x in recent) / r_eps) if r_eps else None,
                           "win_rate": (sum(x["wins"] for x in recent) / r_eps) if r_eps else None,
                           "floor_mean": (sum(x["floor_sum"] for x in recent) / r_eps) if r_eps else None},
                "best_eval": best_eval, "last_eval": last_eval,
                "last_checkpoint": _rel(last_ckpt_path, run),
                "last_checkpoint_update": last_ckpt_upd if last_ckpt_upd >= 0 else None,
                "objective": cfg.objective, "params": n_params, "device": str(device), "amp": amp,
                "max_hours": cfg.max_hours, "max_updates": cfg.max_updates, "max_env_steps": cfg.max_env_steps,
                "pid": os.getpid(), "host": socket.gethostname(), "updated_at": _now_iso()})

        obs = env.reset()
        rewards.reset(obs["aux"])
        train_start = time.monotonic()
        last_ckpt_t = train_start
        last_update_s = 0.0
        upd = upd0
        reason = None
        write_status(upd, state)

        while True:
            elapsed = time.monotonic() - session_start
            if stop["signal"]:
                reason = f"signal {stop['signal']}"
            elif cfg.max_updates and upd >= cfg.max_updates:
                reason = "max_updates"
            elif cfg.max_env_steps and c["env_steps"] >= cfg.max_env_steps:
                reason = "max_env_steps"
            elif cfg.max_hours and elapsed + last_update_s > cfg.max_hours * 3600:
                reason = "max_hours"
            if reason:
                break
            t_iter = time.monotonic()
            if device.type == "cuda":
                torch.cuda.reset_peak_memory_stats(device)
            ep_this: list[dict] = []
            env_s = infer_s = 0.0
            n_steps = se_n = cg_n = il_n = over_n = 0
            model.eval()
            for t in range(cfg.rollout):
                t0 = time.monotonic()
                cand_n = obs["cand_n"].copy()
                x = obs_to_device(obs, device, max_rows=cfg.max_rows)
                with torch.no_grad(), autocast():
                    logits, value = model(x)
                dist = torch.distributions.Categorical(logits=logits)
                a = dist.sample()
                logp = dist.log_prob(a)
                store.put(t, x)
                a_np = a.cpu().numpy().astype(np.int32)
                valid = cand_n > 0
                a_np[~valid] = -1
                store.actions[t] = np.maximum(a_np, 0)
                store.logp[t] = logp.cpu().numpy()
                store.values[t] = value.cpu().numpy()
                store.valid[t] = valid
                t1 = time.monotonic()
                obs, rew, term, trunc, status, info = env.step(a_np)
                t2 = time.monotonic()
                done = (term | trunc).astype(bool)
                r, fin = rewards.step(rew, done, info["last_episode"], obs["aux"])
                store.rewards[t] = r
                store.dones[t] = done
                se = int(np.count_nonzero(status & STATUS["SIM_ERROR"]))
                cg = int(np.count_nonzero(status & STATUS["CONTENT_GAP"]))
                il = int(np.count_nonzero(status & STATUS["ILLEGAL_ACTION"]))
                if se or cg:
                    for slot, text in env.slot_errors().items():
                        key = text.splitlines()[0][:160] if text else "?"
                        errors_seen[key] = errors_seen.get(key, 0) + 1
                se_n, cg_n, il_n = se_n + se, cg_n + cg, il_n + il
                over_n += int(np.count_nonzero(cand_n > cfg.max_rows))
                n_steps += int(np.count_nonzero(valid))
                infer_s += t1 - t0
                env_s += t2 - t1
                ep_this += fin
            # bootstrap value of the state after the rollout
            t0 = time.monotonic()
            with torch.no_grad(), autocast():
                _, last_v = model(obs_to_device(obs, device, max_rows=cfg.max_rows))
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

            # PPO update (minibatches gathered on the device)
            t_up = time.monotonic()
            model.train()
            idx_all = np.flatnonzero(store.valid.reshape(-1))
            f_adv = adv.reshape(-1)
            a_mean, a_std = float(f_adv[idx_all].mean()), float(f_adv[idx_all].std()) + 1e-8
            to = lambda a_: torch.from_numpy(np.ascontiguousarray(a_)).to(device)  # noqa: E731
            d_act, d_logp = to(store.actions.reshape(-1)), to(store.logp.reshape(-1))
            d_adv = to(((f_adv - a_mean) / a_std).astype(np.float32))
            d_ret, d_val = to(ret.reshape(-1)), to(store.values.reshape(-1))
            stats = []
            for _ in range(cfg.epochs):
                perm = rng.permutation(idx_all)
                for k in range(0, len(perm), cfg.minibatch):
                    mb = to(np.sort(perm[k:k + cfg.minibatch]))
                    xb = store.batch(mb)
                    with autocast():
                        logits, v = model(xb)
                    logp_all = F.log_softmax(logits, -1)
                    act = d_act[mb]
                    newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
                    ent = -(logp_all.exp() * logp_all.clamp_min(-1e4)).sum(-1).mean()
                    oldlogp = d_logp[mb]
                    ratio = (newlogp - oldlogp).exp()
                    madv = d_adv[mb]
                    pg = -torch.min(ratio * madv, ratio.clamp(1 - cfg.clip, 1 + cfg.clip) * madv).mean()
                    vret, vold = d_ret[mb], d_val[mb]
                    vclip = vold + (v - vold).clamp(-cfg.clip, cfg.clip)
                    vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
                    loss = pg + cfg.vf_coef * vl - cfg.ent_coef * ent
                    opt.zero_grad(set_to_none=True)
                    loss.backward()
                    gn = torch.nn.utils.clip_grad_norm_(model.parameters(), cfg.max_grad_norm)
                    opt.step()
                    with torch.no_grad():
                        kl = ((ratio - 1) - (newlogp - oldlogp)).mean()
                        cf = ((ratio - 1).abs() > cfg.clip).float().mean()
                    st = torch.stack([ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach(),
                                      gn.detach().float()]).cpu().numpy()
                    stats.append(st)
                    if not np.isfinite(st[5]):
                        raise FloatingPointError(f"non-finite loss at update {upd + 1}")
            update_s = time.monotonic() - t_up
            sm = dict(zip(("entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac", "loss", "grad_norm"),
                          (float(x) for x in np.mean(stats, 0)))) if stats else {}
            ret_v, val_v = ret.reshape(-1)[idx_all], store.values.reshape(-1)[idx_all]
            var_r = float(np.var(ret_v)) if len(ret_v) else 0.0
            ev = (1.0 - float(np.var(ret_v - val_v)) / var_r) if var_r > 1e-12 else None
            upd += 1
            c["updates"] = upd
            c["env_steps"] += n_steps
            session_steps += n_steps
            c["episodes"] += len(ep_this)
            c["wins"] += sum(1 for e in ep_this if e["win"])
            c["sim_error"] += se_n
            c["content_gap"] += cg_n
            c["illegal"] += il_n
            c["overflow_cand"] += over_n
            es = _episode_stats(ep_this)
            recent.append({"episodes": len(ep_this), "wins": es["wins"],
                           "progress_sum": sum(e["progress"] or 0.0 for e in ep_this),
                           "floor_sum": sum(e["floor"] for e in ep_this)})

            # checkpoint / eval
            t_ck = time.monotonic()
            now = t_ck
            if (cfg.ckpt_every and upd % cfg.ckpt_every == 0) or \
                    (cfg.ckpt_every_min and now - last_ckpt_t >= cfg.ckpt_every_min * 60):
                save(upd)
                last_ckpt_t = time.monotonic()
            ckpt_s = time.monotonic() - t_ck
            t_ev = time.monotonic()
            if cfg.eval_every and upd % cfg.eval_every == 0:
                run_eval(upd)
            eval_s = time.monotonic() - t_ev
            t_end = time.monotonic()
            iter_s = t_end - t_iter
            c["wall_s"] += iter_s
            last_update_s = iter_s
            for k, v in (("env_s", env_s), ("infer_s", infer_s), ("update_s", update_s), ("eval_s", eval_s),
                         ("ckpt_s", ckpt_s)):
                totals_t[k] += v
            sps = n_steps / max(t_ck - t_iter, 1e-9)
            row = {"update": upd, "time": _now_iso(), "wall_s": round(c["wall_s"], 2),
                   "session_s": round(t_end - session_start, 2), "env_steps": c["env_steps"],
                   "env_steps_update": n_steps, "env_steps_per_s": round(sps, 1),
                   "env_steps_per_s_session": round(session_steps / max(t_end - train_start, 1e-9), 1),
                   "episodes_total": c["episodes"], **es,
                   "reward_mean": float(store.rewards.reshape(-1)[idx_all].mean()) if len(idx_all) else None,
                   **sm, "explained_variance": ev,
                   "t_env_s": round(env_s, 3), "t_infer_s": round(infer_s, 3), "t_update_s": round(update_s, 3),
                   "t_ckpt_s": round(ckpt_s, 3), "t_eval_s": round(eval_s, 3),
                   "t_other_s": round(iter_s - env_s - infer_s - update_s - ckpt_s - eval_s, 3),
                   "t_total_s": round(iter_s, 3), "sim_error": se_n, "content_gap": cg_n, "illegal": il_n,
                   "cand_over_max_rows": over_n, "lr": opt.param_groups[0]["lr"], **gpu_memory(device)}
            metrics.write(row)
            last_sps = round(sps, 1)
            write_status(upd, state)
            if upd == upd0 + 1 or upd % max(cfg.log_every, 1) == 0:
                fmt = lambda v, f: "nan" if v is None else format(v, f)  # noqa: E731
                log(f"update {upd} t={(t_end - train_start) / 60:.1f} min steps={c['env_steps']:,} sps={sps:,.0f} "
                    f"env={env_s:.2f}s infer={infer_s:.2f}s upd={update_s:.2f}s eps={len(ep_this)} "
                    f"prog={fmt(es['progress_mean'], '.4f')} floor={fmt(es['floor_mean'], '.2f')} wins={es['wins']} "
                    f"ent={fmt(sm.get('entropy'), '.3f')} kl={fmt(sm.get('approx_kl'), '.4f')}"
                    + (f" gpu_peak={row.get('gpu_mem_peak_mb')}MB" if device.type == "cuda" else ""))

        # session end: checkpoint, final eval (budget stops only), status, summary
        log(f"stopping: {reason} at update {upd:,} ({c['env_steps']:,} env steps)")
        if upd > upd0 and last_ckpt_upd != upd:
            save(upd)
        if cfg.eval_every and upd > upd0 and not stop["signal"] and (last_eval or {}).get("update") != upd:
            t = time.monotonic()
            run_eval(upd)
            totals_t["eval_s"] += time.monotonic() - t
        state = "stopped" if stop["signal"] else "finished"
        write_status(upd, state, reason)
        metrics.close()
        evals.close()
        train_s = time.monotonic() - train_start
        summary = {
            "config": config_dict(cfg), "objective": cfg.objective, "encoder_version": spec.encoder_version,
            "params": n_params, "arch": arch, "weights_hash": weights_hash(model), "device": str(device), "amp": amp,
            "stop_reason": reason, "session": c["sessions"], "update_start": upd0, "updates": upd,
            "session_updates": upd - upd0, "pool_warm_s": round(warm_s, 2), "pool_warmed": warmed,
            "baseline": baseline, "env_steps": c["env_steps"], "session_env_steps": session_steps,
            "session_train_s": round(train_s, 1), "session_s": round(time.monotonic() - session_start, 1),
            "env_steps_per_s_session": round(session_steps / max(train_s, 1e-9), 1),
            "time_split_s": {k: round(v, 1) for k, v in totals_t.items()},
            "env_only_steps_per_s": round(session_steps / max(totals_t["env_s"], 1e-9), 1),
            "episodes": c["episodes"], "wins": c["wins"], "sim_error": c["sim_error"],
            "content_gap": c["content_gap"], "illegal": c["illegal"], "cand_over_max_rows": c["overflow_cand"],
            "errors": errors_seen, "best_eval": best_eval, "last_eval": last_eval,
            "last_checkpoint": str(last_ckpt_path) if last_ckpt_path else None,
        }
        atomic_write_json(run / "summary.json", summary)
        return summary
    except BaseException as e:
        with contextlib.suppress(Exception):
            if (run / "status.json").exists():
                import json

                st = json.loads((run / "status.json").read_text())
                st.update(state="aborted" if isinstance(e, KeyboardInterrupt) else "error",
                          stop_reason=f"{type(e).__name__}: {e}"[:500], updated_at=_now_iso())
                atomic_write_json(run / "status.json", st)
        raise
    finally:
        for sg, h in old_handlers.items():
            with contextlib.suppress(Exception):
                signal.signal(sg, h)
        env.close()


def _rel(p: Path | None, run: Path) -> str | None:
    """`p` relative to the run directory when it lies inside it (status / eval rows), else as given."""
    if p is None:
        return None
    try:
        return str(Path(p).resolve().relative_to(run.resolve()))
    except ValueError:
        return str(p)


def curve(run_dir: str | os.PathLike) -> tuple[list[dict], list[dict]]:
    """(metrics rows, eval rows) of a run directory."""
    run = Path(run_dir)
    return read_jsonl(run / "metrics.jsonl"), read_jsonl(run / "evals.jsonl")
