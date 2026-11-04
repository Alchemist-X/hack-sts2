"""Narrow trainer/evaluator entry point for an admitted autonomous Modal job.

Provisioning a runtime, installed image and artifact publisher remains a
deployment task. General-purpose legacy trainer CLI arguments are not accepted.
"""

from __future__ import annotations

import hashlib
import json
import math
import time
from dataclasses import dataclass
from pathlib import Path

from .contracts import canonical, identifier, integer, verify_seed_isolation
from .lineage import admit_worker, validate_job
from .manifests import file_digest


@dataclass(frozen=True)
class Installation:
    """Paths and image attestation supplied by deployment, never by a job/model."""

    code_commit: str
    image_digest: str
    engine: Path
    dependencies: Path
    seed_manifests: Path
    artifacts: Path
    scratch: Path


def _verified_seed_path(installation, hash_value):
    path = installation.seed_manifests / f"{hash_value}.json"
    if path.is_symlink() or file_digest(path) != hash_value:
        raise ValueError("seed manifest differs from the frozen hash")
    return path


def result_label(spec):
    if spec["eval_protocol"] == "silent-a10-capacity-v1":
        return "capacity"
    if spec["eval_protocol"] == "silent-a10-smoke-v1":
        return "engineering_smoke"
    raise ValueError("production worker requires a capacity or smoke protocol")


def _validate_seed_manifests(train_path, dev_path, blind_path, spec):
    expected_train = {"namespace": spec["env_seed_namespace"], "env_seed": spec["env_seed"],
                      "character": "SILENT", "ascension": 10, "start_mode": "stream", "start_cursor": 0}
    if json.loads(train_path.read_bytes()) != expected_train:
        raise ValueError("training stream manifest differs from the frozen fresh-start spec")
    from sts2train.isolation import IsolationGuard
    from sts2vec.protocol import run_seed, validate_seedset

    label = result_label(spec)
    counts = (integer(spec["dev_seed_count"], 1), integer(spec["blind_seed_count"], 1))
    if (label == "capacity" and counts != (256, 1000)) or (label == "engineering_smoke" and max(counts) > 16):
        raise ValueError("seed counts are outside the frozen evaluation protocol")
    holdouts = {}
    guard = IsolationGuard()
    for path, role, count in zip((dev_path, blind_path), ("dev", "blind"), counts):
        raw = json.loads(path.read_bytes())
        if (type(raw) is not dict or raw.get("role") != role or raw.get("ascension") != 10
                or raw.get("content") != "ported_only" or type(raw.get("games")) is not list
                or len(raw["games"]) != count):
            raise ValueError("expected a frozen ported-only Silent A10 seed manifest")
        if any(type(game) is not dict or set(game) != {"seed", "character"} or game["character"] != "SILENT"
               for game in raw["games"]):
            raise ValueError("holdout must contain only full fresh Silent runs")
        data = validate_seedset(raw, origin=str(path))
        holdouts[role] = [(game["seed"], f"{run_seed(game['seed']):016x}") for game in data["games"]]
        guard.add(data, path=str(path))
    verify_seed_isolation(holdouts)
    return guard


def run(job, installation, accepted_checkpoint=None, *, checkpoint_ready=None, seed_range=None,
        execution_deadline=None, max_threads=16):
    """Execute one bounded job through the guarded path. Call only after worker admission."""
    spec = validate_job(job)
    result_label(spec)
    if not 1 <= integer(max_threads, 1) <= 16:
        raise ValueError("worker thread count exceeds the bound envelope")
    if spec["checkpoint_policy"]["resume_mode"] != "fresh_episodes":
        raise ValueError("installed trainer supports only labeled fresh-episode recovery")
    deadline = job.deadline if execution_deadline is None else execution_deadline
    if not math.isfinite(deadline) or time.time() >= deadline:
        raise TimeoutError("worker deadline elapsed")
    if job.kind == "train":
        if (type(seed_range) is not tuple or len(seed_range) != 2
                or any(type(value) is not int for value in seed_range)
                or not job.payload["start_cursor"] <= seed_range[0] < seed_range[1]):
            raise ValueError("training requires a broker-reserved seed range")
        if not callable(checkpoint_ready):
            raise ValueError("training requires a bound checkpoint publisher")
    elif seed_range is not None:
        raise ValueError("evaluation cannot consume a training seed reservation")
    if installation.code_commit != spec["code_commit"] or installation.image_digest != spec["image_digest"]:
        raise ValueError("worker image attestation differs from freeze")
    engine_hash = file_digest(installation.engine)
    dependency_hash = file_digest(installation.dependencies)
    if engine_hash != spec["engine_hash"] or dependency_hash != spec["dependency_hash"]:
        raise ValueError("installed engine or dependencies differ from freeze")
    train_manifest = _verified_seed_path(installation, spec["train_seed_manifest_hash"])
    dev_seedset = _verified_seed_path(installation, spec["dev_seed_manifest_hash"])
    blind_seedset = _verified_seed_path(installation, spec["blind_seed_manifest_hash"])
    isolation_guard = _validate_seed_manifests(train_manifest, dev_seedset, blind_seedset, spec)

    # Loading the pinned environment is permitted. No trainer or torch import has occurred yet.
    from sts2vec import load

    environment = load(installation.engine)
    schema_hash = hashlib.sha256(environment.schema_json().encode()).hexdigest()
    measured = {
        "code_commit": installation.code_commit, "image_digest": installation.image_digest,
        "engine_hash": engine_hash, "schema_hash": schema_hash, "dependency_hash": dependency_hash,
        "train_seed_manifest_hash": spec["train_seed_manifest_hash"],
        "dev_seed_manifest_hash": spec["dev_seed_manifest_hash"],
        "blind_seed_manifest_hash": spec["blind_seed_manifest_hash"],
        **{key: spec[key] for key in ("character", "ascension", "training_protocol", "evaluation_policy",
                                     "rollout_shape", "model_config", "ppo_config")},
    }
    admitted = admit_worker(job, installation.artifacts, measured, accepted_checkpoint)
    output = installation.scratch / identifier(job.key)
    output.mkdir(parents=False, exist_ok=False)
    checkpoint = None
    if admitted.checkpoint_dir is not None:
        # The worker artifact writer stores the existing complete trainer .pt as
        # the model component. Its verified role, never request text, selects the file.
        checkpoint = admitted.checkpoint_dir / admitted.checkpoint_manifest["files"]["model"]["name"]

    if job.kind == "eval":
        from sts2train.evaluate import evaluate

        return evaluate(str(dev_seedset), str(checkpoint), str(output), device="cuda", policy="greedy",
                        threads=min(max_threads, 8),
                        loop_breaker=False, repeats=1, allow_blind=False)

    remaining = deadline - time.time() - 30  # leave room for final checkpoint/publish
    if remaining <= 0:
        raise TimeoutError("worker deadline leaves no time to train and persist")

    from sts2train.ppo import resolve_config, train
    options = {
        **spec["model_config"], **spec["rollout_shape"], **spec["ppo_config"],
        "device": "cuda", "characters": ("SILENT",), "ascension": 10,
        "threads": max_threads, "torch_threads": max_threads,
        "start_mode": "stream", "start_saves": None, "save_mix": 0,
        "seed_stream_prefix": spec["env_seed_namespace"], "init_seed": spec["init_seed"],
        "sample_seed": spec["policy_seed"], "env_seed": spec["env_seed"], "rng_scheme": "decoupled",
        "bootstrap_time_limits": True, "rollout_loop_breaker": False, "loop_breaker": False,
        "eval_every": 0, "eval_at": (), "no_best": True, "baseline_steps": 0,
        "max_env_steps": job.payload["target_decisions"], "max_hours": remaining / 3600,
        "ckpt_every_min": spec["checkpoint_policy"]["interval_seconds"] / 60, "out": str(output),
    }
    config = resolve_config(options)
    resume = None
    if checkpoint is not None:
        import torch

        resume = torch.load(checkpoint, map_location="cpu", weights_only=True)
        stream = resume["seed_stream"]
        if (resume["env_steps"], stream["next"], stream["prefix"]) != (
            job.payload["start_decisions"], job.payload["start_cursor"], spec["env_seed_namespace"],
        ):
            raise ValueError("checkpoint contents disagree with the accepted progress and seed stream")
        persisted = resume["config"]
        invariants = {key: options[key] for key in (
            *spec["model_config"], *spec["rollout_shape"], *spec["ppo_config"], "characters", "ascension",
            "start_mode", "start_saves", "save_mix", "init_seed", "sample_seed", "env_seed", "rng_scheme",
            "bootstrap_time_limits", "rollout_loop_breaker",
        )}
        if canonical({key: persisted[key] for key in invariants}) != canonical(invariants):
            raise ValueError("checkpoint trainer configuration differs from the frozen pure configuration")
        optimizer = resume.get("optimizer")
        rng = resume.get("rng")
        if (not resume.get("model") or type(optimizer) is not dict or not optimizer.get("state")
                or not optimizer.get("param_groups") or type(rng) is not dict or not rng.get("numpy")
                or not torch.is_tensor(rng.get("torch")) or type(rng.get("cuda")) is not list
                or len(rng["cuda"]) != 1 or not torch.is_tensor(rng["cuda"][0])
                or type(resume.get("updates")) is not int or resume["updates"] <= 0):
            raise ValueError("training checkpoint does not include complete optimizer and RNG resume state")
    return train(config, resume=resume, resume_path=checkpoint, given=options,
                 isolation_guard=isolation_guard, strict_resume=True,
                 checkpoint_ready=checkpoint_ready, seed_range=seed_range)
