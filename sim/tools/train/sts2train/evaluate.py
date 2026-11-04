"""NN entry point of the STS2-Bench protocol v0.2 (docs/sts2-bench-protocol-v0.2-2026-11-01.md §7 "NN").

A thin wrapper over the policy-agnostic evaluator in the env package (``sts2vec.evaluate``, CLI ``sts2vec eval``):
this module only turns a model (or a checkpoint) into a greedy policy callable ``(obs, ctx) -> actions`` and records
the weights hash. Everything else — one pool entry and one slot per game, no auto-reset, the protocol limits (step_cap
1200 → ``capped``, floor_step_cap 80 → ``stuck``), scoring, the run directory in the LLM runner's layout, the replay
audit — is ``sts2vec.evaluate.evaluate``. ``sts2eval report DIR`` re-renders the same directory.

``evaluate_model`` is what the trainer calls in-process every ``eval_every`` updates (the live model, no reload);
``evaluate`` loads a checkpoint first (``sts2train eval``).
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
import torch
from sts2vec.evaluate import FLOOR_STEP_CAP, STEP_CAP, replay  # noqa: F401  (re-exported)
from sts2vec.evaluate import evaluate as vec_evaluate

from .features import bind_spec, obs_to_torch


def greedy_policy(model, device: str = "cpu"):
    """The model's greedy policy as an ``sts2vec`` policy callable (every candidate row, float32)."""

    def act(obs: dict, ctx) -> np.ndarray:
        with torch.no_grad():
            logits, _ = model(obs_to_torch(obs, device))
        return logits.argmax(-1).cpu().numpy().astype(np.int32)

    return act


def evaluate_model(model, spec, seedset: str, out_dir: str, *, weights_hash: str, checkpoint: str | None = None,
                   threads: int = 8, device: str = "cpu", step_cap: int = STEP_CAP,
                   floor_step_cap: int = FLOOR_STEP_CAP, log=print) -> dict:
    """Play `seedset` greedily with `model` and write the protocol run directory `out_dir`; returns the report."""
    bind_spec(spec)
    was_training = model.training
    model.eval()
    name = f"nn:{Path(checkpoint).name}" if checkpoint else "nn:live"
    act = greedy_policy(model, device)

    def checked(obs: dict, ctx) -> np.ndarray:
        if ctx.step == 0 and ctx.schema.encoder_version != spec.encoder_version:
            log(f"warning: checkpoint encoder_version {spec.encoder_version} != library {ctx.schema.encoder_version}")
        return act(obs, ctx)

    try:
        return vec_evaluate(
            seedset, checked, out_dir, threads=threads, step_cap=step_cap, floor_step_cap=floor_step_cap,
            policy_name=name, policy_hash=f"weights:{weights_hash}", log=log,
            summary_extra={"weights_hash": weights_hash},
            config_extra={"model": "sts2train entity-encoder PPO", "checkpoint": str(checkpoint) if checkpoint else None,
                          "weights_hash": weights_hash, "params": sum(p.numel() for p in model.parameters()),
                          "arch": getattr(model, "arch", None), "device": str(device), "greedy": True})
    finally:
        model.train(was_training)


def evaluate(seedset: str, checkpoint: str, out_dir: str, *, threads: int = 8, device: str = "cpu",
             step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP, log=print) -> dict:
    from .ppo import load_checkpoint

    model, spec, ck = load_checkpoint(checkpoint, device)
    return evaluate_model(model, spec, seedset, out_dir, weights_hash=ck.get("weights_hash") or "?",
                          checkpoint=checkpoint, threads=threads, device=device, step_cap=step_cap,
                          floor_step_cap=floor_step_cap, log=log)
