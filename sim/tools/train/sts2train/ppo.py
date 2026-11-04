"""PPO on sts2vec, built for multi-hour runs on one GPU box (and still fine for a CPU / MPS smoke).

Objectives (``sts2train.objective``): ``protocol`` (default) = per-step change of the STS2-Bench v0.2 progress metric
(the function ``sts2vec eval`` scores with) + ``win_bonus`` at a Victory terminal, so an episode's return is its
protocol progress (+ bonus); ``win`` = the contract's reward (1 at a Victory terminal, else 0; CONTRACTS.md D5);
``shaped`` (LEGACY, ``--shaped``) = ``win`` + delta(total_floor) / 48 per step, the 2026-11-02 smoke objective.

``aux_heads`` (``--aux-heads``, off by default) adds a win-probability head and a combat-hp head on the state
(``sts2train.heads``: targets per rollout window, masked BCE / squared error weighted ``aux_coef``); the policy, value
head and PPO loss are unchanged. ``deck_counts`` (``--deck-counts``) switches the deck / pile encoders to count-aware
pooling (``model.Policy._deck_bag``). ``acq_shaping`` (``--acq-shaping``) adds potential-based shaping for deck size,
relics and upgrades to the reward PPO trains on (``objective.AcqShaping``; ``reward_mean`` / ``return_mean`` /
progress stay the objective's, the shaping is logged apart). ``rollout_loop_breaker`` (``--rollout-loop-breaker``)
applies the evaluation's loop breaker (``sts2train.loopbreak``) to the rollout sampling: back at an observation of its
last 64 decisions (a no-op or a cycle) a slot gives probability 0 to the candidates it took there (the stored
log-probs are the masked distribution's, so PPO's ratio is taken against the policy that actually sampled). With these
flags off the trainer computes exactly what it computed before. The periodic eval's greedy policy runs with the loop
breaker unless ``loop_breaker`` is off (``--loop-breaker off``).

Long-run machinery: checkpoints every N updates and every M minutes (atomic, last K kept, ``best.pt`` by eval
progress among clean evals: ``best_skip_reason``), ``--resume <ckpt>|auto`` (model, optimizer, counters, RNG states;
the env restarts from fresh episodes),
``metrics.jsonl`` (one row per update), ``status.json`` (rewritten every update), periodic protocol evaluation on a
seed set (``sts2vec.evaluate`` through ``sts2train.evaluate``) into ``evals/``, a wall-clock budget (``max_hours``),
graceful stop on SIGINT / SIGTERM (checkpoint, then exit), bf16 autocast on CUDA behind ``amp``. Layout of the run
directory: ``sts2train.runlog``.

The model reads every shape, vocabulary size and column index from the env's published schema at start-up
(``ModelSpec.from_schema``); buffers it does not use are ignored, so a library with extra observation buffers (a new
``encoder_version``) trains unchanged. ``--resume`` accepts a checkpoint whose spec differs from the library's only
outside the model's inputs (``features.check_spec_compat``: the 27 input buffers' shapes and column maps, the vocabulary
and embedded-enum sizes, the pointer enums' members and card_vars must be identical; encoder_version, appended buffers
and enums the model does not embed may differ) and refuses any other checkpoint before the env is built; config.json
records both encoder versions. ``floor_step_cap`` is passed to ``VecEnv`` only when set, and only if this sts2vec's
``VecEnv`` accepts it (unset = the env's default: 80 with a library that implements the cap); the cap in effect is
recorded in config.json (``env.floor_step_cap``) and FLOOR_STALL truncations are counted per update.

Speed (``sts2train bench-update`` measures it): the update and the rollout run without host<->device syncs inside a
minibatch / step (row cuts from host-side counts, minibatch indices uploaded once per epoch, statistics copied back
once per update, no distribution argument validation, pinned asynchronous uploads on CUDA). ``compile`` =
``default`` / ``reduce-overhead`` wraps the minibatch forward + loss (``MinibatchUpdater``) and the rollout forward
(``InferenceRunner``) in ``torch.compile`` (``reduce-overhead`` adds CUDA graphs); it implies ``static_rows`` (the
candidate / prompt-option tables always ``max_rows`` wide, so shapes never change). The default, ``compile = off``
without static rows, computes exactly what the trainer computed before these changes.
"""

from __future__ import annotations

import contextlib
import hashlib
import inspect
import os
import platform
import signal
import socket
import sys
import threading
import time
from collections import deque
from dataclasses import asdict, dataclass, fields
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import torch
import torch.nn.functional as F
from sts2vec import STATUS, PoolSpec, VecEnv, load, random_actions, schema_of
from sts2vec.pool import ALL_CHARACTERS

from .features import ModelSpec, RolloutStore, SpecMismatch, bind_spec, check_spec_compat, obs_to_device
from .heads import AUX_STAT_NAMES, HEAD_OUTPUTS, AuxTargets, aux_losses
from .loopbreak import LoopBreaker
from .model import PRESETS, Policy, activation_bytes_per_sample, arch_of, model_arch, param_count
from .objective import OBJECTIVES, AcqShaping, EpisodeRewards
from .progress import ProgressTracker
from .runlog import CheckpointManager, JsonlWriter, atomic_torch_save, atomic_write_json, read_jsonl
from .startpool import recent_sums, recent_view, split_stats, start_pool_from_config, uses_start_saves

ERR_BITS = STATUS["SIM_ERROR"] | STATUS["CONTENT_GAP"]
FLOOR_STALL = STATUS.get("FLOOR_STALL", 0)   # truncated by the env's floor_step_cap (0 with an sts2vec without it)
CKPT_FORMAT = 2
COMPILE_MODES = ("off", "default", "reduce-overhead")
STAT_NAMES = ("entropy", "value_loss", "policy_loss", "approx_kl", "clipfrac", "loss", "grad_norm")
NEG_LIVE = -1e8                     # logits above this are live candidates (masked ones are model.NEG = -1e9)
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
    start_saves: str | None = None  # directory of game .save files (recursive) to start episodes from (startpool.py)
    save_mix: float = 0.5           # fraction of episode starts from start_saves (pool entry multiplicities)
    save_copies: str = "determinized"   # further copies of a save: determinized (hidden state resampled) | exact
    # objective
    objective: str = "protocol"     # protocol | win | shaped (see sts2train.objective)
    win_bonus: float = 1.0          # protocol objective: extra reward at a Victory terminal (progress is 1.0 there)
    shaped: bool = False            # LEGACY flag: same as objective="shaped" (delta(total_floor) / shaped_floors)
    shaped_floors: float = 48.0
    acq_shaping: bool = False       # potential-based acquisition shaping on top of the objective (objective.AcqShaping)
    acq_deck_coef: float = 0.003    # potential per deck card above 10 (at most 12 of them)
    acq_relic_coef: float = 0.01    # potential per relic
    acq_upgrade_coef: float = 0.004  # potential per upgrade level in the deck
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
    deck_counts: bool = False       # count-aware deck / pile pooling + deck count features (architecture)
    # auxiliary heads (sts2train.heads; architecture: part of the checkpoint's arch)
    aux_heads: bool = False         # win-probability + combat-hp heads on the state, trained with the PPO loss
    aux_coef: float = 0.1           # weight of their losses (masked BCE + masked squared error)
    aux_bootstrap: bool = True      # episodes / combats still running at the window end: target = the head there
    # runtime
    device: str = "cpu"             # cpu | mps | cuda[:i] | auto
    amp: bool = False               # bf16 autocast (CUDA only)
    compile: str = "off"            # off | default | reduce-overhead: torch.compile of the update + rollout forwards
    static_rows: bool | None = None  # cand / prompt_options always max_rows wide (None = on iff compile != off)
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
    loop_breaker: bool = True       # the periodic eval's greedy policy runs with the loop breaker (sts2train.loopbreak)
    loop_breaker_k: int = 3         # returns to an observation before the breaker prefers leaving the screen
    rollout_loop_breaker: bool = False  # the breaker's mask on the rollout sampling distribution
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
    if not 0.0 <= float(out.save_mix) <= 1.0 or out.save_copies not in ("determinized", "exact"):
        raise ValueError(f"save_mix {out.save_mix} must be in [0, 1] and save_copies {out.save_copies!r} one of "
                         "determinized / exact")
    if out.compile not in COMPILE_MODES:
        raise ValueError(f"compile {out.compile!r}: expected one of {COMPILE_MODES}")
    if out.compile == "reduce-overhead" and out.static_rows is False:
        raise ValueError("compile reduce-overhead (CUDA graphs) needs static shapes: drop --no-static-rows")
    if not float(out.aux_coef) >= 0.0:
        raise ValueError(f"aux_coef {out.aux_coef} must be >= 0")
    if isinstance(out.loop_breaker, str):
        if out.loop_breaker not in ("on", "off"):
            raise ValueError(f"loop_breaker {out.loop_breaker!r}: expected on / off")
        out.loop_breaker = out.loop_breaker == "on"
    if int(out.loop_breaker_k) < 1:
        raise ValueError(f"loop_breaker_k {out.loop_breaker_k} must be >= 1")
    return out


def stat_names(cfg: TrainConfig) -> tuple[str, ...]:
    """Names of the per-minibatch statistics `MinibatchUpdater` returns: STAT_NAMES, then AUX_STAT_NAMES with
    aux_heads."""
    return STAT_NAMES + (AUX_STAT_NAMES if cfg.aux_heads else ())


def use_static_rows(cfg: TrainConfig) -> bool:
    return cfg.compile != "off" if cfg.static_rows is None else bool(cfg.static_rows)


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
                       "arch": model.arch, "weights_hash": wh, **head_record(model), **extra}, path)
    return wh


def head_record(model: Policy) -> dict:
    """{"heads": {output: definition}} for a model with aux heads (what `sts2train eval` can read out), else {}."""
    return {"heads": dict(HEAD_OUTPUTS)} if getattr(model, "aux_heads", False) else {}


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
            "params": param_count(model), "encoder_version": spec.encoder_version, "saved_at": _now_iso(),
            **head_record(model), **extra}


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
    """VecEnv keyword arguments; `floor_step_cap` only when set, and an error if this VecEnv does not take it. With
    `start_saves` the pool starts empty: `startpool.start_pool_from_config` adds the fresh and save entries."""
    pool = PoolSpec(characters=tuple(cfg.characters), ascension=cfg.ascension,
                    count=0 if uses_start_saves(cfg) else cfg.pool, seed_prefix=f"TRAIN{cfg.seed}")
    kw = dict(threads=cfg.threads or os.cpu_count() or 8, step_cap=cfg.step_cap, auto_reset=True, seed_text=seed_text,
              pool=pool, pool_cache=max(4096, cfg.pool))
    if cfg.floor_step_cap is not None:
        if "floor_step_cap" not in inspect.signature(env_cls.__init__).parameters:
            raise ValueError("floor_step_cap is set but this sts2vec VecEnv has no floor_step_cap parameter "
                             "(install an sts2vec with the stall truncation, or drop --floor-step-cap)")
        kw["floor_step_cap"] = int(cfg.floor_step_cap)
    return kw


def effective_floor_step_cap(env) -> int:
    """The floor stall cap the env enforces (0 = none): ``VecEnv.floor_step_cap`` when the library implements it."""
    if not getattr(env.schema, "supports_floor_step_cap", False):
        return 0
    return int(getattr(env, "floor_step_cap", 0) or 0)


def resume_compat(resume: dict, log=print) -> dict:
    """Check a resume checkpoint's spec against the library sts2vec loads (before any env exists, so a refused resume
    leaves the run directory untouched): `features.check_spec_compat`, raising `SpecMismatch` with every difference in
    the model's inputs. Returns the compat record for config.json."""
    compat = check_spec_compat(resume["spec"], ModelSpec.from_schema(schema_of(load())), what="resume")
    if compat["other_differences"]:
        log(f"resume across encoder versions: checkpoint {compat['checkpoint_encoder_version']} -> library "
            f"{compat['library_encoder_version']}; the model's inputs are identical; other differences (not read by "
            f"the model): {'; '.join(compat['other_differences'])}")
    return compat


class CompiledMismatchError(RuntimeError):
    """The torch.compile'd update / forward computes something else than eager (`check_compiled`)."""


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


def random_baseline(env: VecEnv, tracker: ProgressTracker, steps: int, seed: int, starts=None) -> dict:
    """Uniform-random policy on the training env (same pool/tracker): finished-episode floor and progress (split by
    fresh / save start when `starts`, the trainer's StartPool, is given)."""
    rng = np.random.default_rng(seed)
    obs = env.reset()
    tracker.observe(obs["aux"])
    start = env.info()["start_entry"].copy()
    eps = []
    errs = 0
    for _ in range(steps):
        obs, rew, term, trunc, status, info = env.step(random_actions(rng, obs["cand_n"]))
        done = (term | trunc).astype(bool)
        errs += int(np.count_nonzero(status & ERR_BITS))
        fin = tracker.finished(done, info["last_episode"])
        for e in fin:
            e["start_entry"] = int(start[e["slot"]])
        eps += fin
        tracker.observe(obs["aux"])
        start = info["start_entry"].copy()
    prog = [e["progress"] for e in eps if e["progress"] is not None]
    split = {k: v for k, v in split_stats(eps, starts).items() if k.split("_", 1)[1] in
             ("episodes", "wins", "progress_mean", "floor_mean")}
    return {"steps": steps, "episodes": len(eps), "floor_mean": float(np.mean([e["floor"] for e in eps])) if eps else None,
            "progress_mean": float(np.mean(prog)) if prog else None, "wins": sum(e["win"] for e in eps),
            "sim_error_or_gap": errs, **split}


def best_skip_reason(row: dict | None) -> str | None:
    """Why an eval row cannot become ``best_eval`` / ``best.pt`` (None = it can): the evaluation failed (``error``), it
    has no progress mean, not every game's history replayed (``replayable`` != ``games``: the replay audit failed, so
    the score is not reproducible), or a game ended ``crashed`` (SIM_ERROR / CONTENT_GAP) or ``error`` (a live slot
    without candidates). Among the remaining rows the highest ``progress_mean`` is best."""
    if not row:
        return "no eval"
    if row.get("error"):
        return f"evaluation failed: {row['error']}"
    if row.get("progress_mean") is None:
        return "no progress mean"
    games, replayable = row.get("games"), row.get("replayable")
    if not games or replayable != games:
        return f"replay audit: {replayable}/{games} games replayable"
    bad = [f"{n} {k}" for k in ("crashed", "error") if (n := int((row.get("results") or {}).get(k) or 0))]
    return f"games ended {', '.join(bad)}" if bad else None


def is_new_best(row: dict, best: dict | None) -> bool:
    """Whether eval `row` replaces `best` as ``best_eval`` / ``best.pt``: `row` is clean (`best_skip_reason`) and there
    is no clean best yet (none, or one kept from before this rule that is not clean) or `row` has a higher
    ``progress_mean`` (a tie keeps the earlier one)."""
    if best_skip_reason(row) is not None:
        return False
    return best is None or best_skip_reason(best) is not None or row["progress_mean"] > best["progress_mean"]


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


# ------------------------------------------------------------------------------------------------ PPO step


def _autocast(amp: bool):
    return torch.autocast("cuda", dtype=torch.bfloat16) if amp else contextlib.nullcontext()


def ppo_loss(model: Policy, xb: dict[str, torch.Tensor], act: torch.Tensor, oldlogp: torch.Tensor,
             madv: torch.Tensor, vret: torch.Tensor, vold: torch.Tensor, *, clip: float, vf_coef: float,
             ent_coef: float, amp: bool = False, aux: tuple | None = None,
             aux_coef: float = 0.0) -> tuple[torch.Tensor, torch.Tensor]:
    """PPO minibatch loss (clipped policy + clipped value - entropy bonus; the forward under bf16 autocast when `amp`)
    and its detached statistics [entropy, value_loss, policy_loss, approx_kl, clipfrac, loss] as one float32 tensor.
    `aux` = (win_y, win_m, combat_y, combat_m) minibatch targets / masks of the aux heads: adds `aux_coef` x their
    losses (`heads.aux_losses`) and appends [win loss, combat loss, win target share, combat target share]."""
    heads = None
    with _autocast(amp):
        if aux is None:
            logits, v = model(xb)
        else:
            logits, v, heads = model(xb, aux=True)
    logp_all = F.log_softmax(logits, -1)
    newlogp = logp_all.gather(1, act.unsqueeze(1)).squeeze(1)
    ent = -(logp_all.exp() * logp_all.clamp_min(-1e4)).sum(-1).mean()
    ratio = (newlogp - oldlogp).exp()
    pg = -torch.min(ratio * madv, ratio.clamp(1 - clip, 1 + clip) * madv).mean()
    vclip = vold + (v - vold).clamp(-clip, clip)
    vl = 0.5 * torch.max((v - vret) ** 2, (vclip - vret) ** 2).mean()
    loss = pg + vf_coef * vl - ent_coef * ent
    if heads is not None:
        wl, cl, wf, cf_ = aux_losses(heads, *aux)
        loss = loss + aux_coef * (wl + cl)
    with torch.no_grad():
        kl = ((ratio - 1) - (newlogp - oldlogp)).mean()
        cf = ((ratio - 1).abs() > clip).float().mean()
    if heads is not None:
        return loss, torch.stack([ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach(), wl.detach(),
                                  cl.detach(), wf, cf_])
    return loss, torch.stack([ent.detach(), vl.detach(), pg.detach(), kl, cf, loss.detach()])


def _macos_inductor_openmp() -> None:
    """macOS: inductor's CPU kernels would link Homebrew's libomp next to the libomp torch already loaded, and the
    second OpenMP runtime aborts the process (OMP Error #15). With OMP_PREFIX set, inductor takes omp.h from there and
    leaves the OpenMP symbols to the runtime already in the process."""
    if sys.platform != "darwin" or "OMP_PREFIX" in os.environ:
        return
    for prefix in ("/opt/homebrew/opt/libomp", "/usr/local/opt/libomp"):
        if os.path.exists(os.path.join(prefix, "include", "omp.h")):
            os.environ["OMP_PREFIX"] = prefix
            return


def compile_fn(fn, mode: str, static: bool = True):
    """`torch.compile(fn)` for `mode` default / reduce-overhead, None for off. `static` (fixed input shapes): a new
    shape recompiles rather than switching to dynamic kernels; otherwise shapes that change between calls become
    dynamic after the first recompile."""
    if mode == "off":
        return None
    if mode not in COMPILE_MODES:
        raise ValueError(f"compile {mode!r}: expected one of {COMPILE_MODES}")
    _macos_inductor_openmp()
    return torch.compile(fn, mode=None if mode == "default" else mode, dynamic=False if static else None)


def _cudagraphs(mode: str, device: torch.device) -> bool:
    return mode == "reduce-overhead" and device.type == "cuda"


class MinibatchUpdater:
    """One PPO minibatch step: forward + loss (`ppo_loss`, eager or `torch.compile`d), backward, grad-norm clip,
    optimizer step. Returns the step's statistics (`STAT_NAMES`) as a float32 device tensor: nothing here waits for
    the device, so consecutive minibatches queue back to back.

    `compiled=False` runs the eager function (the trainer uses it for a short last minibatch, whose shape would
    otherwise recompile the static-shape graph). Outputs of a CUDA-graph replay are overwritten by the next replay,
    so the returned statistics are a fresh tensor and gradients are cleared before the forward.

    With `cfg.aux_heads` the six PPO tensors are followed by the aux heads' four (win_y, win_m, combat_y, combat_m)
    and the statistics by AUX_STAT_NAMES (after grad_norm: `stat_names`)."""

    def __init__(self, model: Policy, opt: torch.optim.Optimizer, cfg: TrainConfig, amp: bool,
                 device: torch.device | str):
        self.model, self.opt = model, opt
        self.max_grad_norm = float(cfg.max_grad_norm)
        clip, vf_coef, ent_coef = float(cfg.clip), float(cfg.vf_coef), float(cfg.ent_coef)
        self.coefs = (vf_coef, ent_coef)
        self.aux = bool(getattr(cfg, "aux_heads", False))
        aux_coef = float(getattr(cfg, "aux_coef", 0.0))

        if self.aux:
            def loss_fn(xb, act, oldlogp, madv, vret, vold, win_y, win_m, combat_y, combat_m):
                return ppo_loss(model, xb, act, oldlogp, madv, vret, vold, clip=clip, vf_coef=vf_coef,
                                ent_coef=ent_coef, amp=amp, aux=(win_y, win_m, combat_y, combat_m), aux_coef=aux_coef)
        else:
            def loss_fn(xb, act, oldlogp, madv, vret, vold):
                return ppo_loss(model, xb, act, oldlogp, madv, vret, vold, clip=clip, vf_coef=vf_coef,
                                ent_coef=ent_coef, amp=amp)

        self.eager = loss_fn
        self.compiled = compile_fn(loss_fn, cfg.compile, use_static_rows(cfg))
        self.cudagraphs = _cudagraphs(cfg.compile, torch.device(device))

    def loss(self, *args, compiled: bool = True):
        """`args` = (xb, act, oldlogp, madv, vret, vold[, win_y, win_m, combat_y, combat_m])."""
        if compiled and self.compiled is not None:
            if self.cudagraphs:
                torch.compiler.cudagraph_mark_step_begin()
            return self.compiled(*args)
        return self.eager(*args)

    def __call__(self, *args, compiled: bool = True) -> torch.Tensor:
        self.opt.zero_grad(set_to_none=True)
        loss, st = self.loss(*args, compiled=compiled)
        loss.backward()
        gn = torch.nn.utils.clip_grad_norm_(self.model.parameters(), self.max_grad_norm)
        self.opt.step()
        if self.aux:
            return torch.cat([st[:6], gn.detach().float().view(1), st[6:]])
        return torch.cat([st, gn.detach().float().view(1)])


def check_compiled(updater: MinibatchUpdater, args: tuple, *, amp: bool, tf32: bool = False) -> dict:
    """Eager vs compiled forward + backward of one minibatch `args` (no optimizer step; gradients are cleared after):
    the loss and every parameter's gradient norm. With CUDA graphs the compiled side runs three times (warm-up,
    record, replay) and the replay is compared. `ok` is False when the compiled path computes something else (beyond
    bf16 / TF32 rounding): a backend bug must stop a run rather than train it on wrong gradients.

    The loss difference is measured against the magnitude of its terms (|policy| + vf * |value| + ent * |entropy|; the
    total can cancel to ~0), a parameter's gradient-norm difference against its norm floored at a fraction of the
    global norm (1e-3 fp32, 3e-3 TF32, 1e-2 bf16): gradients that are rounding noise (c_bias.bias: the loss is
    invariant to a per-sample logit shift) must not fail the check, while a wrong gradient anywhere above that floor
    does."""
    model, opt = updater.model, updater.opt
    names, params = zip(*model.named_parameters())
    res = {}
    for kind, repeats in (("eager", 1), ("compiled", 3 if updater.cudagraphs else 1)):
        for _ in range(repeats):
            opt.zero_grad(set_to_none=True)
            loss, st = updater.loss(*args, compiled=kind == "compiled")
            loss.backward()
            gn = torch.stack([(p.grad if p.grad is not None else torch.zeros_like(p)).detach().float().norm()
                              for p in params])
            res[kind] = (st.detach().cpu().numpy().astype(np.float64), gn.cpu().numpy().astype(np.float64))
    opt.zero_grad(set_to_none=True)
    (se, ge), (sc, gc) = res["eager"], res["compiled"]
    i = {k: STAT_NAMES.index(k) for k in ("entropy", "value_loss", "policy_loss", "loss")}
    vf_coef, ent_coef = updater.coefs
    scale = max(abs(se[i["loss"]]), abs(se[i["policy_loss"]]) + vf_coef * abs(se[i["value_loss"]])
                + ent_coef * abs(se[i["entropy"]]), 1e-6)
    loss_rel = float(abs(sc[i["loss"]] - se[i["loss"]]) / scale)
    g_e, g_c = float(np.sqrt((ge ** 2).sum())), float(np.sqrt((gc ** 2).sum()))
    floor = (1e-2 if amp else 3e-3 if tf32 else 1e-3) * g_e
    grel = np.abs(gc - ge) / np.maximum(ge, max(floor, 1e-12))
    worst = int(np.argmax(grel)) if len(grel) else 0
    tol_loss = 1e-2 if (amp or tf32) else 1e-4
    tol_grad = 0.25 if amp else 0.05 if tf32 else 1e-3
    out = {"loss_eager": float(se[i["loss"]]), "loss_compiled": float(sc[i["loss"]]), "loss_rel": loss_rel,
           "grad_norm_eager": g_e, "grad_norm_compiled": g_c, "worst_param": names[worst],
           "worst_param_grad_rel": float(grel[worst]) if len(grel) else 0.0, "tol_loss": tol_loss,
           "tol_grad": tol_grad, "replayed": updater.cudagraphs}
    out["ok"] = bool(loss_rel <= tol_loss and out["worst_param_grad_rel"] <= tol_grad)
    return out


def check_compiled_forward(runner: "InferenceRunner", x: dict[str, torch.Tensor], *, amp: bool,
                           tf32: bool = False) -> dict:
    """Eager vs compiled rollout forward on `x`: largest difference of the live logits / values relative to their
    scale (with CUDA graphs: the third, replayed, call)."""
    le, ve = runner.forward(x, compiled=False)
    for _ in range(3 if runner.cudagraphs else 1):
        lc, vc = runner.forward(x)
    live = le > NEG_LIVE
    scale_l = float(le[live].abs().max().clamp_min(1.0)) if bool(live.any()) else 1.0
    dl = float((lc - le)[live].abs().max()) / scale_l if bool(live.any()) else 0.0
    dv = float((vc - ve).abs().max()) / float(ve.abs().max().clamp_min(1.0))
    tol = 5e-2 if amp else 1e-2 if tf32 else 1e-4
    return {"logits_rel": dl, "value_rel": dv, "tol": tol, "ok": bool(dl <= tol and dv <= tol)}


class InferenceRunner:
    """The rollout's policy forward (no grad; bf16 autocast when `amp`; eager or `torch.compile`d) and the action
    sampling: `sample(x)` -> (actions, log-probs, values) on the device. The categorical distribution skips its
    argument validation (two device syncs per step); the samples and log-probs are the validated ones'."""

    def __init__(self, model: Policy, amp: bool, compile_mode: str = "off", device: torch.device | str = "cpu", *,
                 static: bool = True):
        self.model = model

        def forward(x):
            with _autocast(amp):
                return model(x)

        self.eager = forward
        self.compiled = compile_fn(forward, compile_mode, static)
        self.cudagraphs = _cudagraphs(compile_mode, torch.device(device))

    def forward(self, x: dict[str, torch.Tensor], *, compiled: bool = True) -> tuple[torch.Tensor, torch.Tensor]:
        with torch.no_grad():
            if compiled and self.compiled is not None:
                if self.cudagraphs:
                    torch.compiler.cudagraph_mark_step_begin()
                return self.compiled(x)
            return self.eager(x)

    def sample(self, x: dict[str, torch.Tensor], *, compiled: bool = True, adjust=None):
        """`adjust` (logits -> logits, e.g. ``LoopBreaker.apply``) runs on the logits before sampling, eager."""
        logits, value = self.forward(x, compiled=compiled)
        if adjust is not None:
            logits = adjust(logits)
        dist = torch.distributions.Categorical(logits=logits, validate_args=False)
        a = dist.sample()
        return a, dist.log_prob(a), value


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
        want, have = arch_of(ck.get("arch") or ck.get("config")), arch_of(config_dict(cfg))
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
    static = use_static_rows(cfg)
    torch.manual_seed(cfg.seed)
    rng = np.random.default_rng(cfg.seed)

    c = dict(updates=0, env_steps=0, episodes=0, wins=0, sim_error=0, content_gap=0, illegal=0, overflow_cand=0,
             floor_stall=0, wall_s=0.0, sessions=0)
    best_eval = last_eval = None
    if resume is not None:
        c.update({k: v for k, v in (resume.get("counters") or {}).items() if k in c})
        c["updates"] = int(resume.get("updates", c["updates"]))
        c["env_steps"] = int(resume.get("env_steps", c["env_steps"]))
        best_eval, last_eval = resume.get("best_eval"), resume.get("last_eval")
    c["sessions"] += 1
    upd0 = c["updates"]

    seed_text = f"PPO{cfg.seed}" if resume is None else f"PPO{cfg.seed}R{upd0}"
    compat = resume_compat(resume, log) if resume is not None else None   # raises SpecMismatch
    env = VecEnv(cfg.envs, **env_kwargs(cfg, seed_text))
    old_handlers: dict = {}
    try:
        spec = ModelSpec.from_schema(env.schema)
        bind_spec(spec)
        if compat is not None and compat["library_encoder_version"] != spec.encoder_version:
            raise SpecMismatch(f"the env loaded encoder_version {spec.encoder_version}, the resume check saw "
                               f"{compat['library_encoder_version']}")
        floor_cap = effective_floor_step_cap(env)
        starts = start_pool_from_config(env, cfg, log=log)     # None = config.pool.fresh only (also on --resume)
        arch = model_arch(cfg)                             # size knobs + the optional parts that are on
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
            f"{warm_s:.1f} s; {cfg.envs} envs x {env.threads} threads; floor_step_cap "
            + (f"{floor_cap} (FLOOR_STALL truncation)" if floor_cap else "off"))
        rewards = EpisodeRewards(cfg.objective, spec.aux_fields, env.schema["last_episode"].fields, cfg.envs,
                                 cfg.ascension, win_bonus=cfg.win_bonus, shaped_floors=cfg.shaped_floors)
        log(f"objective: {cfg.objective}" + (f" (win bonus {cfg.win_bonus})" if cfg.objective == "protocol" else ""))
        shaper = None
        if cfg.acq_shaping:
            shaper = AcqShaping(spec.aux_fields, spec.cols["deck"], cfg.envs, cfg.gamma, deck=cfg.acq_deck_coef,
                                relic=cfg.acq_relic_coef, upgrade=cfg.acq_upgrade_coef)
            log(f"acquisition shaping: phi = {cfg.acq_deck_coef} x min(deck - 10, 12) + {cfg.acq_relic_coef} x relics + "
                f"{cfg.acq_upgrade_coef} x upgrades (0 at episode end), F = gamma phi(s') - phi(s) added to the "
                f"{cfg.objective} reward; reward_mean / return_mean / progress stay unshaped")
        aux_tg = None
        if cfg.aux_heads:
            aux_tg = AuxTargets(spec.aux_fields, env.schema["last_episode"].fields, cfg.rollout, cfg.envs,
                                bootstrap=cfg.aux_bootstrap)
            log(f"aux heads: win_prob (BCE to the episode outcome) + combat_hp_pred (squared error), weight "
                f"{cfg.aux_coef}; running episodes / combats at the window end: "
                + ("bootstrapped from the heads" if cfg.aux_bootstrap else "masked"))

        breaker = None
        if cfg.rollout_loop_breaker:
            breaker = LoopBreaker(spec, cfg.envs, k=cfg.loop_breaker_k)
            log(f"rollout loop breaker: back at an observation of its last {breaker.window} decisions a slot gives "
                f"probability 0 to the candidates it took there; back there {cfg.loop_breaker_k} times or more, only "
                f"the rows that leave the screen stay (when legal)")

        baseline = None
        if cfg.baseline_steps and resume is None:
            t = time.monotonic()
            tracker = ProgressTracker(spec.aux_fields, env.schema["last_episode"].fields, cfg.envs, cfg.ascension)
            baseline = random_baseline(env, tracker, cfg.baseline_steps, cfg.seed + 1, starts)
            baseline["seconds"] = round(time.monotonic() - t, 2)
            log(f"random baseline ({cfg.baseline_steps} steps x {cfg.envs}): {baseline}")

        model = model.to(device)
        opt = torch.optim.Adam(model.parameters(), lr=cfg.lr, eps=1e-5)
        if resume is not None:
            restore_checkpoint(resume, model, opt, rng, device, lr=cfg.lr)
            log(f"resumed from {resume_path} at update {upd0:,} ({c['env_steps']:,} env steps, session {c['sessions']})")
        updater = MinibatchUpdater(model, opt, cfg, amp, device)
        runner = InferenceRunner(model, amp, cfg.compile, device, static=static)
        tf32 = device.type == "cuda" and torch.backends.cuda.matmul.allow_tf32
        compile_check: dict = {}            # eager vs compiled, once per session (first rollout step / minibatch)
        log(f"device: {device}" + (" [bf16 autocast]" if amp else "")
            + (" [trunk checkpointing]" if cfg.grad_ckpt else "")
            + (f" [torch.compile {cfg.compile}: the first update compiles]" if cfg.compile != "off" else "")
            + (f" [static rows: cand / prompt_options {cfg.max_rows} wide]" if static else ""))

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
                         "pid": os.getpid(), "device": str(device), "amp": amp, "compile": cfg.compile,
                         "static_rows": static, "torch": torch.__version__,
                         "cuda_device": torch.cuda.get_device_name(device) if device.type == "cuda" else None,
                         "python": platform.python_version(), "cpu_count": os.cpu_count(), "env_threads": env.threads,
                         "bundle": os.environ.get("STS2TRAIN_BUNDLE"), "encoder_version": spec.encoder_version,
                         "checkpoint_encoder_version": compat["checkpoint_encoder_version"] if compat else None})
        from sts2vec.evaluate import repo_sha

        atomic_write_json(run / "config.json", {
            "config": config_dict(cfg), "arch": arch, "params": n_params, "encoder_version": spec.encoder_version,
            "checkpoint_encoder_version": compat["checkpoint_encoder_version"] if compat else None,
            "resume_compat": compat,
            "library": {"path": str(env.lib.path), "mode": env.lib.mode, "version": env.lib.version()},
            "env": {"step_cap": cfg.step_cap, "floor_step_cap": floor_cap,
                    "floor_step_cap_supported": bool(getattr(env.schema, "supports_floor_step_cap", False))},
            "repo_sha": repo_sha(), "sessions": sessions, "start_pool": starts.summary() if starts else None})

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
                                     device=str(device), loop_breaker=cfg.loop_breaker,
                                     loop_breaker_k=cfg.loop_breaker_k, log=lambda m: None)
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
            lb = rep.get("loop_breaker")
            if lb:
                row.update(loop_breaker=lb["mode"], loop_breaks=lb["loop_breaks"], no_op_actions=lb["no_op_actions"],
                           cycle_revisits=lb.get("cycle_revisits"))
            skip = best_skip_reason(row)
            if skip:
                row["best_skip"] = skip
            evals.write(row)
            last_eval = row
            if is_new_best(row, best_eval):          # best.pt: the clean eval with the highest progress
                best_eval = row
                ckpts.mark_best(last_ckpt_path)
            pm = row["progress_mean"]
            log(f"[eval u{upd}] {cfg.eval_seedset}: progress {'n/a' if pm is None else format(pm, '.4f')} "
                f"wins {row['wins']}/{row['games']} floor {row['floor_mean']} ({row['seconds']:.1f} s)"
                + (f" loop breaks {lb['loop_breaks']}" if lb and lb["loop_breaks"] else "")
                + (" *best*" if best_eval is row else "") + (f" [not eligible for best.pt: {skip}]" if skip else ""))
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
                           "floor_mean": (sum(x["floor_sum"] for x in recent) / r_eps) if r_eps else None,
                           **recent_view(recent)},
                "best_eval": best_eval, "last_eval": last_eval,
                "last_checkpoint": _rel(last_ckpt_path, run),
                "last_checkpoint_update": last_ckpt_upd if last_ckpt_upd >= 0 else None,
                "objective": cfg.objective, "params": n_params, "device": str(device), "amp": amp,
                "max_hours": cfg.max_hours, "max_updates": cfg.max_updates, "max_env_steps": cfg.max_env_steps,
                "pid": os.getpid(), "host": socket.gethostname(), "updated_at": _now_iso()})

        obs = env.reset()
        rewards.reset(obs["aux"], start_entry=env.info()["start_entry"])
        r_obj = None
        if shaper is not None:
            shaper.reset(obs["aux"], obs["deck"])
            r_obj = np.zeros((cfg.rollout, cfg.envs), dtype=np.float32)     # the objective's reward (unshaped)
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
            n_steps = se_n = cg_n = il_n = over_n = fs_n = 0
            model.eval()
            for t in range(cfg.rollout):
                t0 = time.monotonic()
                cand_n = obs["cand_n"].copy()
                if aux_tg is not None:
                    aux_tg.before(t, obs["aux"])
                x = obs_to_device(obs, device, max_rows=cfg.max_rows, static=static)
                if runner.compiled is not None and "forward" not in compile_check:
                    compile_check["forward"] = chk = check_compiled_forward(runner, x, amp=amp, tf32=tf32)
                    log(f"compiled rollout forward vs eager: logits rel {chk['logits_rel']:.2e}, values rel "
                        f"{chk['value_rel']:.2e} (tol {chk['tol']:g}): {'OK' if chk['ok'] else 'MISMATCH'}")
                    if not chk["ok"]:
                        raise CompiledMismatchError(f"the compiled rollout forward disagrees with eager ({chk}); "
                                                    f"rerun with --compile off")
                if breaker is None:
                    a, logp, value = runner.sample(x)
                else:                               # obs is still this step's (the env steps below)
                    a, logp, value = runner.sample(x, adjust=lambda lg: breaker.apply(obs, lg))
                store.put(t, x, obs)
                a_np = a.cpu().numpy().astype(np.int32)
                valid = cand_n > 0
                a_np[~valid] = -1
                if breaker is not None:
                    breaker.commit(a_np)
                store.actions[t] = np.maximum(a_np, 0)
                store.logp[t] = logp.cpu().numpy()
                store.values[t] = value.cpu().numpy()
                store.valid[t] = valid
                t1 = time.monotonic()
                obs, rew, term, trunc, status, info = env.step(a_np)
                t2 = time.monotonic()
                done = (term | trunc).astype(bool)
                if breaker is not None:             # auto-reset slots start new episodes
                    breaker.reset(done)
                r, fin = rewards.step(rew, done, info["last_episode"], obs["aux"], start_entry=info["start_entry"])
                if aux_tg is not None:
                    aux_tg.after(t, term, trunc, info["last_episode"], obs["aux"])
                if shaper is not None:              # shaping: PPO trains on r + F, the logs keep r apart
                    f_acq, ended = shaper.step(done, obs["aux"], obs["deck"])
                    for e in fin:
                        e["shaping"] = ended.get(e["slot"], 0.0)
                        e["shaped_return"] = e["return"] + e["shaping"]
                    r_obj[t] = r
                    r = r + f_acq
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
                fs_n += int(np.count_nonzero(status & FLOOR_STALL)) if FLOOR_STALL else 0
                over_n += int(np.count_nonzero(cand_n > cfg.max_rows))
                n_steps += int(np.count_nonzero(valid))
                infer_s += t1 - t0
                env_s += t2 - t1
                ep_this += fin
            # bootstrap value of the state after the rollout
            t0 = time.monotonic()
            x_last = obs_to_device(obs, device, max_rows=cfg.max_rows, static=static)
            _, last_v = runner.forward(x_last)
            last_v = last_v.cpu().numpy()
            aux_np, aux_st = (), {}
            if aux_tg is not None:                  # the heads at the state after the window (bootstrap targets)
                with torch.no_grad(), _autocast(amp):
                    _, _, hb = model(x_last, aux=True)
                win_b, cmb_b = (torch.sigmoid(hb[k]).cpu().numpy() for k in ("win_logit", "combat_logit"))
                *aux_np, aux_st = aux_tg.targets(win_b, cmb_b)
                in_cmb = obs["aux"][:, spec.aux_fields.index("is_combat")] != 0
                aux_st.update(aux_win_prob_mean=float(win_b.mean()),
                              aux_combat_pred_mean=float(cmb_b[in_cmb].mean()) if in_cmb.any() else None)
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

            # PPO update: minibatches gathered on the device; every epoch's minibatch indices go up in one copy, row
            # cuts come from host counts and the statistics come back once, so the minibatches queue without a sync
            t_up = time.monotonic()
            model.train()
            idx_all = np.flatnonzero(store.valid.reshape(-1))
            f_adv = adv.reshape(-1)
            a_mean, a_std = float(f_adv[idx_all].mean()), float(f_adv[idx_all].std()) + 1e-8
            to = lambda a_: torch.from_numpy(np.ascontiguousarray(a_)).to(device)  # noqa: E731
            d_act, d_logp = to(store.actions.reshape(-1)), to(store.logp.reshape(-1))
            d_adv = to(((f_adv - a_mean) / a_std).astype(np.float32))
            d_ret, d_val = to(ret.reshape(-1)), to(store.values.reshape(-1))
            mbs = [np.sort(perm[k:k + cfg.minibatch]) for perm in (rng.permutation(idx_all) for _ in range(cfg.epochs))
                   for k in range(0, len(perm), cfg.minibatch)]
            d_mbs = torch.split(to(np.concatenate(mbs)), [len(m) for m in mbs]) if mbs else ()
            d_aux = tuple(to(a_.reshape(-1)) for a_ in aux_np)      # win_y, win_m, combat_y, combat_m (aux heads)

            def mb_args(mb_h, mb):
                return (store.batch(mb, rows=None if static else store.rows(mb_h), static=static), d_act[mb],
                        d_logp[mb], d_adv[mb], d_ret[mb], d_val[mb], *(a_[mb] for a_ in d_aux))

            if updater.compiled is not None and "update" not in compile_check and mbs \
                    and len(mbs[0]) == cfg.minibatch:
                compile_check["update"] = chk = check_compiled(updater, mb_args(mbs[0], d_mbs[0]), amp=amp, tf32=tf32)
                log(f"compiled minibatch update vs eager: loss {chk['loss_compiled']:.6g} vs {chk['loss_eager']:.6g} "
                    f"(rel {chk['loss_rel']:.2e}, tol {chk['tol_loss']:g}), worst parameter gradient norm "
                    f"{chk['worst_param']} rel {chk['worst_param_grad_rel']:.2e} (tol {chk['tol_grad']:g}): "
                    f"{'OK' if chk['ok'] else 'MISMATCH'}")
                if not chk["ok"]:
                    raise CompiledMismatchError(f"the compiled PPO update disagrees with eager ({chk}); rerun with "
                                                f"--compile off")
            stats = [updater(*mb_args(mb_h, mb), compiled=len(mb_h) == cfg.minibatch) for mb_h, mb in zip(mbs, d_mbs)]
            stats = torch.stack(stats).cpu().numpy() if stats else None
            update_s = time.monotonic() - t_up
            if stats is not None and not np.isfinite(stats[:, STAT_NAMES.index("loss")]).all():
                raise FloatingPointError(f"non-finite loss at update {upd + 1}")
            sm = dict(zip(stat_names(cfg), (float(x) for x in np.mean(stats, 0)))) if stats is not None else {}
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
            c["floor_stall"] += fs_n
            es = _episode_stats(ep_this)
            ss = split_stats(ep_this, starts)            # fresh_* / save_* ({} without --start-saves)
            lb_st = {}
            if breaker is not None:
                lb_n, no_n, cy_n = breaker.pop_counts()
                lb_st = {"loop_breaks": lb_n, "no_op_actions": no_n, "cycle_revisits": cy_n}
            recent.append({"episodes": len(ep_this), "wins": es["wins"],
                           "progress_sum": sum(e["progress"] or 0.0 for e in ep_this),
                           "floor_sum": sum(e["floor"] for e in ep_this), **recent_sums(ep_this, starts)})

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
                   "episodes_total": c["episodes"], **es, **ss,
                   "reward_mean": float((store.rewards if r_obj is None else r_obj).reshape(-1)[idx_all].mean())
                   if len(idx_all) else None,
                   **sm, "explained_variance": ev,
                   "t_env_s": round(env_s, 3), "t_infer_s": round(infer_s, 3), "t_update_s": round(update_s, 3),
                   "t_ckpt_s": round(ckpt_s, 3), "t_eval_s": round(eval_s, 3),
                   "t_other_s": round(iter_s - env_s - infer_s - update_s - ckpt_s - eval_s, 3),
                   "t_total_s": round(iter_s, 3), "sim_error": se_n, "content_gap": cg_n, "illegal": il_n,
                   "cand_over_max_rows": over_n, "floor_stall": fs_n, "lr": opt.param_groups[0]["lr"],
                   **aux_st, **shaping_stats(store.rewards, r_obj, idx_all, ep_this, shaper), **lb_st,
                   **gpu_memory(device)}
            metrics.write(row)
            last_sps = round(sps, 1)
            write_status(upd, state)
            if upd == upd0 + 1 or upd % max(cfg.log_every, 1) == 0:
                fmt = lambda v, f: "nan" if v is None else format(v, f)  # noqa: E731
                log(f"update {upd} t={(t_end - train_start) / 60:.1f} min steps={c['env_steps']:,} sps={sps:,.0f} "
                    f"env={env_s:.2f}s infer={infer_s:.2f}s upd={update_s:.2f}s eps={len(ep_this)} "
                    f"prog={fmt(es['progress_mean'], '.4f')} floor={fmt(es['floor_mean'], '.2f')} wins={es['wins']} "
                    f"ent={fmt(sm.get('entropy'), '.3f')} kl={fmt(sm.get('approx_kl'), '.4f')}"
                    + (f" | fresh eps={ss['fresh_episodes']} prog={fmt(ss['fresh_progress_mean'], '.4f')} | save "
                       f"eps={ss['save_episodes']} gain={fmt(ss['save_progress_gain_mean'], '.4f')} "
                       f"prog={fmt(ss['save_progress_mean'], '.4f')} wins={ss['save_wins']}" if ss else "")
                    + (f" | shaped ret={fmt(row.get('shaped_return_mean'), '.4f')} shaping/step="
                       f"{fmt(row.get('shaping_mean'), '.5f')}" if shaper is not None else "")
                    + (f" | aux win_bce={fmt(sm.get('aux_win_loss'), '.4f')} combat_se="
                       f"{fmt(sm.get('aux_combat_loss'), '.4f')} p_win={fmt(aux_st.get('aux_win_prob_mean'), '.4f')}"
                       if aux_tg is not None else "")
                    + (f" | loop breaks={lb_st['loop_breaks']} no-ops={lb_st['no_op_actions']} "
                       f"cycles={lb_st['cycle_revisits']}" if lb_st else "")
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
            "checkpoint_encoder_version": compat["checkpoint_encoder_version"] if compat else None,
            "floor_step_cap": floor_cap, "params": n_params, "arch": arch, "weights_hash": weights_hash(model),
            "device": str(device), "amp": amp,
            "stop_reason": reason, "session": c["sessions"], "update_start": upd0, "updates": upd,
            "session_updates": upd - upd0, "pool_warm_s": round(warm_s, 2), "pool_warmed": warmed,
            "baseline": baseline, "env_steps": c["env_steps"], "session_env_steps": session_steps,
            "session_train_s": round(train_s, 1), "session_s": round(time.monotonic() - session_start, 1),
            "env_steps_per_s_session": round(session_steps / max(train_s, 1e-9), 1),
            "time_split_s": {k: round(v, 1) for k, v in totals_t.items()},
            "env_only_steps_per_s": round(session_steps / max(totals_t["env_s"], 1e-9), 1),
            "episodes": c["episodes"], "wins": c["wins"], "sim_error": c["sim_error"],
            "content_gap": c["content_gap"], "illegal": c["illegal"], "cand_over_max_rows": c["overflow_cand"],
            "floor_stall": c["floor_stall"], "errors": errors_seen, "best_eval": best_eval, "last_eval": last_eval,
            "start_pool": starts.summary(brief=True) if starts else None, "compile": cfg.compile,
            "static_rows": static, "compile_check": compile_check or None,
            "last_checkpoint": str(last_ckpt_path) if last_ckpt_path else None,
        }
        if breaker is not None:
            t_lb = breaker.totals()
            summary["rollout_loop_breaker"] = {k: t_lb[k] for k in ("k", "window", "loop_breaks", "no_op_actions",
                                                                    "cycle_revisits")}
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


def shaping_stats(rewards: np.ndarray, r_obj: np.ndarray | None, idx_all: np.ndarray, eps: list[dict],
                  shaper: AcqShaping | None) -> dict:
    """Metrics of --acq-shaping ({} without it): the mean shaped reward PPO trained on and the mean shaping per step
    (valid steps of the window), the finished episodes' shaped return and shaping sum, the mean potential now."""
    if shaper is None:
        return {}
    shaped = rewards.reshape(-1)[idx_all]
    obj = r_obj.reshape(-1)[idx_all]
    return {"reward_mean_shaped": float(shaped.mean()) if len(shaped) else None,
            "shaping_mean": float((shaped - obj).mean()) if len(shaped) else None,
            "shaped_return_mean": _mean([e["shaped_return"] for e in eps if "shaped_return" in e]),
            "shaping_return_mean": _mean([e["shaping"] for e in eps if "shaping" in e]),
            "acq_potential_mean": float(shaper.phi.mean())}


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
