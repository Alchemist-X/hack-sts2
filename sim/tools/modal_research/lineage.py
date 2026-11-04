"""Pure fresh-start lineage, enforced before a research worker opens model inputs."""

from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .contracts import DIGEST, PPO_NUMBERS, canonical, digest, identifier, integer, validate_spec
from .manifests import verify

FROZEN_HASHES = ("code_commit", "image_digest", "engine_hash", "schema_hash", "dependency_hash",
                 "train_seed_manifest_hash", "dev_seed_manifest_hash", "blind_seed_manifest_hash")
JOB_FIELDS = {
    "study_id", "spec", "config_hash", "checkpoint", "start_decisions", "target_decisions", "start_cursor",
}
REFERENCE_FIELDS = {"job_key", "generation", "manifest_hash"}


@dataclass(frozen=True)
class AcceptedCheckpoint:
    """A proof created by the ledger after validating the current experiment head."""

    study_id: str
    experiment_id: str
    config_hash: str
    reference: dict
    identity: dict


@dataclass(frozen=True)
class WorkerInputs:
    identity: dict
    checkpoint_dir: Path | None
    checkpoint_manifest: dict | None


def validate_job(job):
    payload = job.payload
    if job.kind not in {"train", "eval"} or type(payload) is not dict or set(payload) != JOB_FIELDS:
        raise ValueError("invalid worker request fields")
    identifier(job.key)
    identifier(payload["study_id"])
    spec = payload["spec"]
    validate_spec(spec)
    if digest(spec) != payload["config_hash"]:
        raise ValueError("frozen experiment hash mismatch")
    stride = spec["rollout_shape"]["envs"] * spec["rollout_shape"]["rollout"]
    start = integer(payload["start_decisions"])
    target = integer(payload["target_decisions"], 1)
    cursor = integer(payload["start_cursor"])
    if start % stride or target not in spec["evaluation_milestones"]:
        raise ValueError("decision count is outside frozen rollout/milestone schedule")
    reference = payload["checkpoint"]
    if reference is None:
        if job.kind != "train" or start or cursor:
            raise ValueError("fresh initialization requires train at decision zero with seed cursor zero")
    else:
        if type(reference) is not dict or set(reference) != REFERENCE_FIELDS or not start:
            raise ValueError("checkpoint must be a ledger reference for an in-progress experiment")
        identifier(reference["job_key"])
        identifier(reference["generation"])
        if not isinstance(reference["manifest_hash"], str) or not DIGEST.fullmatch(reference["manifest_hash"]):
            raise ValueError("invalid accepted checkpoint hash")
    if (job.kind == "train" and start >= target) or (job.kind == "eval" and start != target):
        raise ValueError("worker start and target do not match the selected stage")
    return spec


def trainer_config_matches(actual, frozen):
    """The trainer may normalize a frozen numeric PPO value from int to float."""
    for key, expected in frozen.items():
        value = actual[key]
        if key in PPO_NUMBERS:
            if type(value) not in (int, float) or value != expected:
                return False
        elif canonical(value) != canonical(expected):
            return False
    return True


def artifact_identity(job):
    """All generations record the random root and the immediate accepted ancestor."""
    spec = validate_job(job)
    payload = job.payload
    reference = payload["checkpoint"]
    return {
        "kind": job.kind, "job_key": job.key, "study_id": payload["study_id"],
        "experiment_id": spec["experiment_id"], "config_hash": payload["config_hash"],
        **{key: spec[key] for key in FROZEN_HASHES},
        "eval_protocol": spec["eval_protocol"], "data_lineage": spec["data_lineage"],
        "dev_seed_count": spec["dev_seed_count"], "blind_seed_count": spec["blind_seed_count"],
        "root_initialization": {"kind": "random", "init_seed": spec["init_seed"]},
        "ancestor_checkpoint": reference,
    }


def checkpoint_path(root, reference):
    root = Path(root)
    directory = root / identifier(reference["job_key"]) / identifier(reference["generation"])
    if directory.is_symlink() or directory.parent.is_symlink():
        raise ValueError("checkpoint path cannot be a symlink")
    return directory


def admit_worker(job, root, measured, accepted=None):
    """Resolve inputs from a trusted job claim. This does not open or deserialize weights."""
    identity = artifact_identity(job)
    spec = job.payload["spec"]
    expected = {
        **{key: spec[key] for key in FROZEN_HASHES},
        "character": spec["character"], "ascension": spec["ascension"],
        "training_protocol": spec["training_protocol"], "evaluation_policy": spec["evaluation_policy"],
        "rollout_shape": spec["rollout_shape"], "model_config": spec["model_config"],
        "ppo_config": spec["ppo_config"],
    }
    if type(measured) is not dict or measured != expected:
        raise ValueError("worker configuration, engine, image, or seed manifest differs from the freeze")
    reference = job.payload["checkpoint"]
    if reference is None:
        if accepted is not None:
            raise ValueError("fresh initialization cannot admit a prior checkpoint")
        return WorkerInputs(identity, None, None)
    if not isinstance(accepted, AcceptedCheckpoint) or (
        accepted.study_id, accepted.experiment_id, accepted.config_hash, accepted.reference,
    ) != (
        identity["study_id"], identity["experiment_id"], identity["config_hash"], reference,
    ):
        raise ValueError("checkpoint was not accepted as this study and experiment's head")
    origin = accepted.identity
    if (origin.get("kind") != "train" or origin.get("job_key") != reference["job_key"]
            or any(origin.get(key) != identity[key] for key in (
                "study_id", "experiment_id", "config_hash", "data_lineage", "root_initialization", *FROZEN_HASHES,
            ))):
        raise ValueError("checkpoint origin differs from the pure lineage")
    directory = checkpoint_path(root, reference)
    manifest = verify(directory, reference["manifest_hash"], origin)
    metadata = manifest["metadata"]
    if (metadata["decisions"], metadata["cursor"], metadata["resume_mode"]) != (
        job.payload["start_decisions"], job.payload["start_cursor"], spec["checkpoint_policy"]["resume_mode"],
    ):
        raise ValueError("accepted checkpoint progress or seed cursor mismatch")
    return WorkerInputs(identity, directory, manifest)
