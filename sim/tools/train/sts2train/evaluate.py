"""NN entry point of the STS2-Bench protocol v0.2 (docs/sts2-bench-protocol-v0.2-2026-11-01.md §7 "NN").

A thin wrapper over the policy-agnostic evaluator in the env package (``sts2vec.evaluate``, CLI ``sts2vec eval``):
this module only turns a model (or a checkpoint) into a greedy policy callable ``(obs, ctx) -> actions`` and records
the weights hash. Everything else — one pool entry and one slot per game, no auto-reset, the protocol limits (step_cap
1200 → ``capped``, floor_step_cap 80 → ``stuck``), scoring, the run directory in the LLM runner's layout, the replay
audit — is ``sts2vec.evaluate.evaluate``. ``sts2eval report DIR`` re-renders the same directory.

``evaluate_model`` is what the trainer calls in-process every ``eval_every`` updates (the live model, no reload);
``evaluate`` loads a checkpoint first (``sts2train eval``). A model whose spec differs from the library's is evaluated
only when the model's inputs are identical (``features.check_spec_compat``, the rule ``--resume`` uses); the run
directory's config.json records the checkpoint's encoder_version next to the library's.

A model with aux heads (``--aux-heads``, ``sts2train.heads``) also reads them out at every decision: each game's
``games/<KEY>.summary.json`` gets ``win_prob`` (P(win) under the policy, one value per entry of ``history_actions``)
and ``combat_hp_pred`` (predicted hp / max_hp at the end of the current combat; null outside combat), plus
``win_prob_first`` / ``win_prob_last``; config.json lists the heads and the returned report carries ``head_readout``.
"""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import torch
from sts2vec.evaluate import FLOOR_STEP_CAP, STEP_CAP, replay  # noqa: F401  (re-exported)
from sts2vec.evaluate import evaluate as vec_evaluate

from .features import ModelSpec, bind_spec, check_spec_compat, obs_to_torch


def greedy_policy(model, device: str = "cpu", heads: "HeadLog | None" = None):
    """The model's greedy policy as an ``sts2vec`` policy callable (every candidate row, float32). With `heads` (a
    model with aux heads) every call also records the heads' readouts of the slots that act."""

    def act(obs: dict, ctx) -> np.ndarray:
        with torch.no_grad():
            if heads is None:
                logits, _ = model(obs_to_torch(obs, device))
            else:
                logits, _, h = model(obs_to_torch(obs, device), aux=True)
                heads.record(obs, ctx, h)
        return logits.argmax(-1).cpu().numpy().astype(np.int32)

    return act


class HeadLog:
    """Per game, per decision readouts of the aux heads during ``sts2vec.evaluate``: the evaluator calls the policy
    once per batched step and appends one ``history_actions`` entry per slot in ``ctx.live``, so recording exactly
    those slots keeps the lists aligned with ``history_actions``. ``write`` adds them to the game summaries."""

    def __init__(self, is_combat_col: int):
        self.col = is_combat_col
        self.win: list[list[float]] = []
        self.combat: list[list[float | None]] = []
        self.keys: list[str] = []

    def record(self, obs: dict, ctx, h: dict) -> None:
        if not self.win:
            self.keys = list(ctx.keys)
            self.win = [[] for _ in range(ctx.num_envs)]
            self.combat = [[] for _ in range(ctx.num_envs)]
        wp = torch.sigmoid(h["win_logit"]).cpu().numpy()
        cp = torch.sigmoid(h["combat_logit"]).cpu().numpy()
        in_combat = obs["global_i"][:, self.col] != 0
        for i in np.flatnonzero(ctx.live):
            self.win[i].append(round(float(wp[i]), 6))
            self.combat[i].append(round(float(cp[i]), 6) if in_combat[i] else None)

    def write(self, out_dir: str | Path) -> dict:
        """Add ``win_prob`` / ``combat_hp_pred`` (+ first / last win_prob) to every game summary; -> a readout."""
        firsts, lasts = [], []
        for i, key in enumerate(self.keys):
            p = Path(out_dir) / "games" / f"{key}.summary.json"
            s = json.loads(p.read_text(encoding="utf-8"))
            if len(s.get("history_actions") or []) != len(self.win[i]):
                raise RuntimeError(f"{key}: {len(self.win[i])} head readouts for {len(s.get('history_actions') or [])} "
                                   "recorded actions")
            first = self.win[i][0] if self.win[i] else None
            last = self.win[i][-1] if self.win[i] else None
            s.update(win_prob=self.win[i], combat_hp_pred=self.combat[i], win_prob_first=first, win_prob_last=last)
            with open(p, "w", encoding="utf-8") as f:
                json.dump(s, f, ensure_ascii=False, indent=1)
            firsts += [first] if first is not None else []
            lasts += [last] if last is not None else []
        return {"games": len(self.keys), "win_prob_first_mean": float(np.mean(firsts)) if firsts else None,
                "win_prob_last_mean": float(np.mean(lasts)) if lasts else None}


def evaluate_model(model, spec, seedset: str, out_dir: str, *, weights_hash: str, checkpoint: str | None = None,
                   threads: int = 8, device: str = "cpu", step_cap: int = STEP_CAP,
                   floor_step_cap: int = FLOOR_STEP_CAP, log=print) -> dict:
    """Play `seedset` greedily with `model` and write the protocol run directory `out_dir`; returns the report.

    Raises `features.SpecMismatch` (before any game is played) when `spec` differs from the library's in the model's
    inputs; an encoder_version change outside them is accepted and noted."""
    from sts2vec import load, schema_of

    compat = check_spec_compat(spec, ModelSpec.from_schema(schema_of(load())), what="evaluate")
    if compat["other_differences"]:
        log(f"note: checkpoint encoder_version {compat['checkpoint_encoder_version']}, library "
            f"{compat['library_encoder_version']}: the model's inputs are identical (other differences: "
            f"{'; '.join(compat['other_differences'])})")
    bind_spec(spec)
    was_training = model.training
    model.eval()
    name = f"nn:{Path(checkpoint).name}" if checkpoint else "nn:live"
    heads = HeadLog(spec.cols["global_i"]["is_combat"]) if getattr(model, "aux_heads", False) else None
    act = greedy_policy(model, device, heads)
    extra = {"heads": ["win_prob", "combat_hp_pred"]} if heads is not None else {}

    try:
        rep = vec_evaluate(
            seedset, act, out_dir, threads=threads, step_cap=step_cap, floor_step_cap=floor_step_cap,
            policy_name=name, policy_hash=f"weights:{weights_hash}", log=log,
            summary_extra={"weights_hash": weights_hash},
            config_extra={"model": "sts2train entity-encoder PPO", "checkpoint": str(checkpoint) if checkpoint else None,
                          "weights_hash": weights_hash, "params": sum(p.numel() for p in model.parameters()),
                          "arch": getattr(model, "arch", None), "device": str(device), "greedy": True,
                          "checkpoint_encoder_version": spec.encoder_version, **extra})
        if heads is not None:
            rep["head_readout"] = heads.write(out_dir)
            r = rep["head_readout"]
            log(f"aux heads: per-step win_prob / combat_hp_pred written to {Path(out_dir) / 'games'}; mean win_prob "
                f"at the first decision {r['win_prob_first_mean']}, at the last {r['win_prob_last_mean']}")
        return rep
    finally:
        model.train(was_training)


def evaluate(seedset: str, checkpoint: str, out_dir: str, *, threads: int = 8, device: str = "cpu",
             step_cap: int = STEP_CAP, floor_step_cap: int = FLOOR_STEP_CAP, log=print) -> dict:
    from .ppo import load_checkpoint

    model, spec, ck = load_checkpoint(checkpoint, device)
    return evaluate_model(model, spec, seedset, out_dir, weights_hash=ck.get("weights_hash") or "?",
                          checkpoint=checkpoint, threads=threads, device=device, step_cap=step_cap,
                          floor_step_cap=floor_step_cap, log=log)
