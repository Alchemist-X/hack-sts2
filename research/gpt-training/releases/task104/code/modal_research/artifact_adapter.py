"""Attest the worker bundle and publish broker-verifiable Volume generations."""

from __future__ import annotations

import json
import math
import re
import shutil
from datetime import datetime, timedelta
from dataclasses import dataclass
from pathlib import Path
from uuid import uuid4

from .contracts import DIGEST, canonical, identifier, integer
from .lineage import artifact_identity, trainer_config_matches, validate_job
from .manifests import (HISTORY_MAX_BYTES, RAW_METRICS_MAX_BYTES, RAW_METRICS_NAME,
                        RAW_METRICS_REWARD_VERSION, RAW_METRICS_ROLE, file_digest, publish,
                        raw_metrics_coverage, verify)
from .native_worker import result_label
from .transition_contract import (positive, total_params, validate_checkpoint, sidecar, verify_sidecar,
                                  ROLE as TRANSITION_ROLE, MAX_BYTES as TRANSITION_MAX_BYTES, IDENTITY, science_files, verify_ancestor_counts)

CODE_FILE = re.compile(r"code/(?:sts2train|sts2vec|modal_research)/(?:[A-Za-z_][A-Za-z0-9_]*/)*[A-Za-z_][A-Za-z0-9_]*\.py")
SEED_FILE = re.compile(r"seeds/[0-9a-f]{64}\.json")
BUNDLE_MANIFEST = "bundle.json"


def _json_file(path, limit):
    path = Path(path)
    if path.is_symlink() or not path.is_file() or not 0 < path.stat().st_size <= limit:
        raise ValueError("invalid JSON artifact")
    data = json.loads(path.read_bytes())
    if type(data) is not dict:
        raise ValueError("JSON artifact must be an object")
    canonical(data)
    return data


def verify_bundle(installation, spec, root, manifest_hash):
    """The image binding supplies both a manifest digest and an isolated bundle root."""
    root = Path(root)
    if root.is_symlink() or not root.is_dir():
        raise ValueError("invalid worker bundle root")
    manifest_path = root / BUNDLE_MANIFEST
    manifest = _json_file(manifest_path, 256 * 1024)
    if (not isinstance(manifest_hash, str) or not DIGEST.fullmatch(manifest_hash)
            or file_digest(manifest_path) != manifest_hash):
        raise ValueError("worker bundle manifest digest mismatch")
    if (set(manifest) != {"version", "image_digest", "code_commit", "files"} or manifest["version"] != 1
            or manifest["image_digest"] != installation.image_digest or manifest["code_commit"] != installation.code_commit
            or installation.image_digest != spec["image_digest"] or installation.code_commit != spec["code_commit"]):
        raise ValueError("worker bundle identity differs from experiment freeze")
    files = manifest["files"]
    if type(files) is not dict or len(files) > 400:
        raise ValueError("invalid bundle allowlist")
    library = "lib/Sts2Sim.Vec.so"
    dependencies = "dependencies.lock"
    required = {library, dependencies, "code/sts2train/__init__.py", "code/sts2vec/__init__.py",
                "code/sts2train/ppo.py", "code/sts2train/evaluate.py", "code/sts2train/seedstream.py",
                *(f"seeds/{spec[key]}.json" for key in (
                    "train_seed_manifest_hash", "dev_seed_manifest_hash", "blind_seed_manifest_hash",
                ))}
    if positive(spec):
        pinned_science = science_files(spec)
        required.update(pinned_science)
        if any(files.get(name) != pin for name, pin in pinned_science.items()):
            raise ValueError("Task089 science source bytes differ from the accepted integration")
        required.add("code/modal_research/transition_contract.py")
    if not required <= files.keys():
        raise ValueError("worker bundle is missing pinned inputs")
    if (Path(installation.engine) != root / library or Path(installation.dependencies) != root / dependencies
            or Path(installation.seed_manifests) != root / "seeds"):
        raise ValueError("worker paths are not bound to the verified bundle")
    actual = set()
    for entry in root.rglob("*"):
        if entry.is_symlink() or (not entry.is_file() and not entry.is_dir()):
            raise ValueError("worker bundle contains an unsafe entry")
        if entry.is_file():
            actual.add(entry.relative_to(root).as_posix())
    if actual != {BUNDLE_MANIFEST, *files}:
        raise ValueError("worker bundle contains files outside its allowlist")
    for name, sha in files.items():
        allowed = name in {library, dependencies} or CODE_FILE.fullmatch(name) or SEED_FILE.fullmatch(name)
        if not allowed or (SEED_FILE.fullmatch(name) and name not in required):
            raise ValueError("worker bundle contains a disallowed input")
        if type(sha) is not str or not DIGEST.fullmatch(sha) or file_digest(root / name) != sha:
            raise ValueError("worker bundle member hash mismatch")
    if files[library] != spec["engine_hash"] or files[dependencies] != spec["dependency_hash"]:
        raise ValueError("worker bundle engine or dependency hash differs from freeze")
    for name in required:
        if SEED_FILE.fullmatch(name) and files[name] != Path(name).stem:
            raise ValueError("seed artifact digest differs from its frozen name")
    return root / "code"


def load_checkpoint(path):
    import torch

    return torch.load(path, map_location="cpu", weights_only=True), torch.is_tensor


def _nonempty_tensor(value, is_tensor):
    return is_tensor(value) and value.numel() > 0


def _check_checkpoint(data, is_tensor, job, seed_range):
    spec = validate_job(job)
    payload = job.payload
    validate_checkpoint(data, spec)
    from .transition_contract import validate_checkpoint_gate
    validate_checkpoint_gate(data, spec, job)
    decisions = integer(data["env_steps"], 1)
    updates = integer(data["updates"], 1)
    stride = spec["rollout_shape"]["envs"] * spec["rollout_shape"]["rollout"]
    counters = data["counters"]
    if (data["format"] != 2 or data["params"] != spec["actual_param_count"] or decisions != updates * stride
            or not payload["start_decisions"] < decisions <= payload["target_decisions"]
            or counters["updates"] != updates or counters["env_steps"] != decisions):
        raise ValueError("checkpoint progress or architecture count mismatch")
    config = data["config"]
    frozen = {
        **spec["model_config"], **spec["rollout_shape"], **spec["ppo_config"],
        "characters": ["SILENT"], "ascension": 10, "start_mode": "stream", "start_saves": None, "save_mix": 0,
        "seed_stream_prefix": spec["env_seed_namespace"], "init_seed": spec["init_seed"],
        "sample_seed": spec["policy_seed"], "env_seed": spec["env_seed"], "rng_scheme": "decoupled",
        "bootstrap_time_limits": True, "rollout_loop_breaker": False,
    }
    if not trainer_config_matches(config, frozen):
        raise ValueError("checkpoint config differs from frozen experiment")
    if spec.get("input_arch") == "reward_choices_public_handselect_v1" and config.get("input_arch") != spec["input_arch"]:
        raise ValueError("checkpoint config public input marker differs from immutable experiment")
    recipe = spec["ppo_config"]
    if "tf32" in recipe:
        precision = data.get("training_precision")
        expected = {"bf16_autocast": recipe["amp"], "cuda_matmul_tf32": recipe["tf32"],
                    "cudnn_tf32": recipe["tf32"]}
        if not isinstance(precision, dict) or any(precision.get(key) is not value for key, value in expected.items()):
            raise ValueError("checkpoint lacks matching effective CUDA precision")
        if not recipe["tf32"] and not recipe["amp"] and (
            precision.get("default_dtype") != "torch.float32" or precision.get("float32_matmul_precision") != "highest"
            or precision.get("ambient_cuda_autocast") is not False
        ):
            raise ValueError("checkpoint lacks strict FP32 runtime evidence")
    from .transition_contract import critic_arch
    expected_critic = critic_arch(spec)
    if (data.get("arch", {}).get("critic_arch", "") != expected_critic
            or data.get("config", {}).get("critic_arch", "") != expected_critic):
        raise ValueError("checkpoint critic architecture differs from immutable experiment")
    architecture = data["arch"]
    if any(architecture.get(key, False) != value for key, value in spec["model_config"].items()):
        raise ValueError("checkpoint architecture differs from freeze")
    if (not isinstance(data["encoder_version"], str)
            or data["spec"]["encoder_version"] != data["encoder_version"]):
        raise ValueError("checkpoint encoder metadata mismatch")
    model = data["model"]
    if (type(model) is not dict or not model or not all(_nonempty_tensor(value, is_tensor) for value in model.values())
            or sum(value.numel() for value in model.values()) != data["params"]):
        raise ValueError("checkpoint model is empty or invalid")
    optimizer = data["optimizer"]
    groups, optimizer_state = optimizer["param_groups"], optimizer["state"]
    if (type(groups) is not list or not groups or type(optimizer_state) is not dict or not optimizer_state
            or any(group["lr"] != frozen["lr"] or not group["params"] for group in groups)):
        raise ValueError("checkpoint lacks fixed-rate optimizer state")
    params = {param for group in groups for param in group["params"]}
    if not optimizer_state.keys() <= params:
        raise ValueError("checkpoint optimizer state contains unbound parameters")
    for state in optimizer_state.values():
        if (not all(_nonempty_tensor(state[key], is_tensor) for key in ("step", "exp_avg", "exp_avg_sq"))
                or state["step"].numel() != 1 or state["exp_avg"].shape != state["exp_avg_sq"].shape):
            raise ValueError("checkpoint lacks Adam moments and step")
    rng = data["rng"]
    if (type(rng["numpy"]) is not dict or not rng["numpy"] or not _nonempty_tensor(rng["torch"], is_tensor)
            or type(rng["cuda"]) is not list or len(rng["cuda"]) != 1
            or not _nonempty_tensor(rng["cuda"][0], is_tensor)):
        raise ValueError("checkpoint lacks required CPU, NumPy, or single-GPU RNG state")
    if any(value.ndim != 1 or str(value.dtype) != "torch.uint8" for value in (rng["torch"], *rng["cuda"])):
        raise ValueError("checkpoint RNG buffers must be byte vectors")
    canonical(rng["numpy"])
    stream = data["seed_stream"]
    cursor = integer(stream["next"])
    start, end = seed_range
    if (stream["prefix"] != spec["env_seed_namespace"] or stream["characters"] != ["SILENT"] or stream["ascension"] != 10
            or not start < cursor <= end or integer(stream["started"]) + integer(stream["skipped"]) != cursor
            or integer(stream["failed"]) > stream["started"]):
        raise ValueError("checkpoint seed stream does not match the active reservation")
    issued_until = integer(stream.get("issued_until"))
    if (not cursor <= issued_until <= end or stream.get("seed_range") != list(seed_range) or stream.get("reserved_skip_interval") !=
            [payload["start_cursor"], start] or integer(stream.get("reserved_skipped")) > stream["skipped"]):
        raise ValueError("checkpoint lacks the reserved seed skip record")
    inflight = data["inflight"]
    fields = [inflight[key] for key in ("slot", "n", "seed_text", "steps")]
    if (any(type(field) is not list or len(field) != spec["rollout_shape"]["envs"] for field in fields)
            or set(inflight["slot"]) != set(range(spec["rollout_shape"]["envs"]))
            or len(set(inflight["n"])) != len(inflight["n"])):
        raise ValueError("checkpoint lacks the in-flight fresh restart record")
    for ordinal, seed, steps in zip(inflight["n"], inflight["seed_text"], inflight["steps"]):
        integer(steps)
        if not start <= integer(ordinal) < cursor or seed != f"{stream['prefix']}-SILENT-{ordinal}":
            raise ValueError("in-flight episode is outside the active seed reservation")
    counters_summary = {key: integer(counters[key]) for key in (
        "updates", "env_steps", "episodes", "wins", "sim_error", "content_gap", "illegal", "overflow_cand",
    )}
    return decisions, updates, cursor, issued_until, counters_summary


METRIC_FIELDS = {
    "update", "env_steps", "env_steps_update", "learning_samples_update", "env_steps_per_s", "t_env_s",
    "t_infer_s", "t_update_s", "t_ckpt_s", "episodes", "wins", "progress_mean", "entropy", "approx_kl",
    "value_loss", "policy_loss", "grad_norm", "clipfrac", "cycles_total", "floor_stall", "sim_error", "content_gap",
    "illegal", "cand_over_max_rows", "prompt_over_max_rows", "native_candidate_overflow", "native_table_overflow",
    "bootstrap_step_caps", "gpu_mem_peak_mb",
}


HISTORY_LIMIT = HISTORY_MAX_BYTES


def _utc_time(value):
    if type(value) is not str or len(value) > 64:
        return None
    try:
        parsed = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if parsed.tzinfo is None or parsed.utcoffset() != timedelta(0):
        return None
    return value


def _metric_history(path, maximum_update, maximum_decisions):
    """Project completed JSONL records only; never infer missing updates or timestamps."""
    path = Path(path)
    history = {"version": 1, "series": "train", "omitted_rows": 0, "rows": []}
    if not path.exists():
        return history
    if path.is_symlink() or not path.is_file():
        raise ValueError("invalid local metric log")
    previous_update = previous_steps = 0
    with path.open("rb") as handle:
        for line in handle:
            if not line.endswith(b"\n") or len(line) > 64 * 1024:
                continue  # an interrupted append or an implausibly large record
            try:
                raw = json.loads(line)
            except (UnicodeDecodeError, json.JSONDecodeError):
                continue
            if type(raw) is not dict:
                continue
            update, steps = raw.get("update"), raw.get("env_steps")
            if (type(update) is not int or type(steps) is not int
                    or not previous_update < update <= maximum_update
                    or not previous_steps < steps <= maximum_decisions):
                continue
            row = {"update": update, "env_steps": steps, "at": _utc_time(raw.get("time"))}
            row.update({key: value for key, value in raw.items()
                        if key in METRIC_FIELDS - {"update", "env_steps"}
                        and type(value) in (int, float) and math.isfinite(value)})
            history["rows"].append(row)
            previous_update, previous_steps = update, steps
            while len(canonical(history).encode("utf-8")) > HISTORY_LIMIT:
                history["rows"].pop(0)
                history["omitted_rows"] += 1
    return history


def _latest_metrics(path, maximum_update):
    if not path.exists():
        return {}
    if path.is_symlink() or not path.is_file():
        raise ValueError("invalid local metric log")
    with path.open("rb") as handle:
        size = handle.seek(0, 2)
        handle.seek(max(0, size - 64 * 1024))
        lines = handle.read().splitlines()
    for line in reversed(lines if size <= 64 * 1024 else lines[1:]):
        row = json.loads(line)
        if type(row) is dict and type(row.get("update")) is int and row["update"] <= maximum_update:
            return {"last_completed_update": {key: row[key] for key in METRIC_FIELDS if key in row}}
    return {}


@dataclass(frozen=True)
class Generation:
    reference: dict
    decisions: int
    cursor: int | None
    partial: bool
    issued_until: int | None = None


class ArtifactAdapter:
    """Publish local worker output. Recording a reference belongs to the broker."""

    def __init__(self, root, volume, *, checkpoint_loader=load_checkpoint, generation_id=lambda: uuid4().hex,
                 before_ready=None, max_generation_bytes=None):
        if not callable(getattr(volume, "commit", None)) or not callable(getattr(volume, "reload", None)):
            raise ValueError("a bound Volume V2 client is required")
        self.root = Path(root)
        self.volume = volume
        self.load = checkpoint_loader
        self.generation_id = generation_id
        self.before_ready = before_ready
        self.max_generation_bytes = max_generation_bytes

    def refresh(self):
        self.volume.reload()

    def _directory(self, job):
        identity = artifact_identity(job)
        generation = identifier(self.generation_id())
        parent = self.root / identity["job_key"]
        if self.root.is_symlink() or parent.is_symlink():
            raise ValueError("artifact root cannot be a symlink")
        parent.mkdir(exist_ok=True)
        directory = parent / generation
        directory.mkdir()
        return identity, generation, directory

    def _finish(self, identity, generation, directory, metadata, files):
        if (self.max_generation_bytes is not None
                and sum((directory / name).stat().st_size for name in files.values()) + 32_000
                > self.max_generation_bytes):
            raise ValueError("generation exceeds the detached storage cap")

        def announce(sha):
            self.before_ready({"generation": generation, "manifest_hash": sha}, metadata)

        sha = publish(directory, identity, metadata, files, commit=self.volume.commit,
                      before_ready=announce if self.before_ready is not None else None)
        verify(directory, sha, identity)
        return Generation({"generation": generation, "manifest_hash": sha}, metadata["decisions"],
                          metadata.get("cursor"), metadata["termination"] == "checkpoint", metadata.get("seed_issued_until"))

    def training(self, claim, path, scratch, *, partial):
        job = claim.job
        path, scratch = Path(path), Path(scratch)
        expected_parent = scratch / job.key / "checkpoints"
        if (path.parent != expected_parent or path.is_symlink() or path.parent.is_symlink()
                or not re.fullmatch(r"ckpt-\d{7,}\.pt", path.name) or not path.is_file()
                or not 0 < path.stat().st_size <= total_params(job.payload["spec"]) * 32 + 64 * 1024 * 1024):
            raise ValueError("invalid local training checkpoint path")
        identity, generation, directory = self._directory(job)
        packed = directory / "checkpoint.pt"
        shutil.copyfile(path, packed)
        data, is_tensor = self.load(packed)
        if type(data) is not dict:
            raise ValueError("checkpoint must contain a complete training state")
        decisions, updates, cursor, issued_until, counters = _check_checkpoint(data, is_tensor, job, claim.seed_range)
        if path.name != f"ckpt-{updates:07d}.pt":
            raise ValueError("checkpoint update differs from its file name")
        packed_hash = file_digest(packed)
        files = {"model": packed.name}
        metrics_path = expected_parent.parent / RAW_METRICS_NAME
        coverage = None
        if job.payload["spec"]["reward_version"] == RAW_METRICS_REWARD_VERSION:
            if metrics_path.is_symlink() or not metrics_path.is_file():
                raise ValueError("raw metric log is not a regular file")
            with metrics_path.open("rb") as handle:
                raw = handle.read(RAW_METRICS_MAX_BYTES + 1)
            if len(raw) > RAW_METRICS_MAX_BYTES:
                raise ValueError("raw metric log exceeds its byte bound")
            destination = directory / RAW_METRICS_NAME
            destination.write_bytes(raw)
            shape = job.payload["spec"]["rollout_shape"]
            first = job.payload["start_decisions"] // (shape["envs"] * shape["rollout"]) + 1
            coverage = raw_metrics_coverage(destination, first, updates, decisions, partial=partial)
            files[RAW_METRICS_ROLE] = RAW_METRICS_NAME
            metrics_path = destination
        sidecars = {
            "optimizer": {"embedded": "optimizer", "checkpoint_sha256": packed_hash},
            "scheduler": {"kind": "constant_lr", "lr": data["config"]["lr"], "checkpoint_sha256": packed_hash},
            "rng": {"embedded": "rng", "streams": ["numpy", "torch", "cuda"], "checkpoint_sha256": packed_hash},
            "seed_cursor": {"stream": data["seed_stream"], "grant": list(claim.seed_range),
                            "checkpoint_sha256": packed_hash},
            "metrics": {**counters, **_latest_metrics(metrics_path, updates),
                        "result_label": result_label(job.payload["spec"])},
        }
        if job.payload["spec"]["reward_version"] == RAW_METRICS_REWARD_VERSION:
            sidecars["history"] = _metric_history(metrics_path, updates, decisions)
        from .transition_contract import mirror_gate, verify_gate_ancestor
        mirror_gate(data, job.payload["spec"], sidecars["metrics"], job)
        if positive(job.payload["spec"]):
            verify_gate_ancestor(self.root, job, data["first_transition_gate"])
        transition = sidecar(data, job.payload["spec"], packed_hash)
        if transition is not None:
            verify_ancestor_counts(self.root, job, transition)
            sidecars[TRANSITION_ROLE] = transition
        for role, body in sidecars.items():
            name = f"{role}.json"
            (directory / name).write_text(canonical(body), encoding="utf-8")
            files[role] = name
        termination = "checkpoint" if partial else ("milestone" if decisions == job.payload["target_decisions"] else "segment")
        metadata = {"decisions": decisions, "updates": updates, "cursor": cursor, "termination": termination,
                    "resume_mode": "fresh_episodes", "result_label": result_label(job.payload["spec"]),
                    "seed_issued_until": issued_until}
        if "tf32" in job.payload["spec"]["ppo_config"]:
            metadata["training_precision"] = data["training_precision"]
        if transition is not None:
            metadata["transition_auxiliary"] = IDENTITY
        if coverage is not None:
            metadata["metrics_coverage"] = coverage
        return self._finish(identity, generation, directory, metadata, files)

    def evaluation(self, claim, result, installation):
        job = claim.job
        spec = validate_job(job)
        source = Path(installation.scratch) / job.key / "games"
        seedset = _json_file(Path(installation.seed_manifests) / f"{spec['dev_seed_manifest_hash']}.json", 256_000)
        expected = {f"{game['character']}-{game['seed']}": game for game in seedset["games"]}
        found = {path.name.removesuffix(".summary.json"): path for path in source.glob("*.summary.json")}
        if (len(expected) != spec["dev_seed_count"] or expected.keys() != found.keys()
                or len(expected) != len(seedset["games"])):
            raise ValueError("evaluation does not include every frozen fresh seed exactly once")
        rows = []
        fields = ("result", "progress", "act", "floor", "steps", "end_kind", "end_status", "replayable")
        for key, game in expected.items():
            summary = _json_file(found[key], 256_000)
            if (summary.get("key") != key or summary.get("seed") != game["seed"]
                    or summary.get("character") != "SILENT" or summary.get("ascension") != 10):
                raise ValueError("evaluated seed identity differs from freeze")
            rows.append({"key": key, **{field: summary.get(field) for field in fields}})
        headline = result["headline"]
        wins = sum(row["result"] == "win" for row in rows)
        if (type(headline) is not dict or headline.get("games") != len(rows) or headline.get("wins") != wins
                or not all(row["replayable"] is True for row in rows)):
            raise ValueError("evaluation is incomplete or replay audit failed")
        identity, generation, directory = self._directory(job)
        label = result_label(spec)
        (directory / "metrics.json").write_text(canonical({**headline, "result_label": label}), encoding="utf-8")
        (directory / "per_seed.json").write_text(canonical(rows), encoding="utf-8")
        metadata = {"decisions": job.payload["start_decisions"], "cursor": job.payload["start_cursor"],
                    "termination": "completed", "result_label": label}
        return self._finish(identity, generation, directory, metadata,
                            {"metrics": "metrics.json", "per_seed": "per_seed.json"})


class ArtifactMirror:
    """Copy one immutable Volume generation into the Raven verifier's artifact root.

    Invoke copy from a bounded synchronization service, not inside a broker
    transaction or synchronous short poll. Its status endpoint may use ready.
    """

    def __init__(self, root, volume):
        if not callable(getattr(volume, "read_file", None)):
            raise ValueError("an authenticated Modal Volume reader is required")
        self.root = Path(root)
        self.volume = volume

    def _target(self, job, reference):
        if (type(reference) is not dict or set(reference) != {"generation", "manifest_hash"}
                or type(reference["manifest_hash"]) is not str or not DIGEST.fullmatch(reference["manifest_hash"])):
            raise ValueError("invalid mirrored artifact reference")
        identity = artifact_identity(job)
        generation = identifier(reference["generation"])
        target = self.root / identity["job_key"] / generation
        if self.root.is_symlink() or target.parent.is_symlink() or target.is_symlink():
            raise ValueError("mirror target cannot be a symlink")
        return target, identity

    @staticmethod
    def _verify_generation(directory, reference, identity, job):
        manifest = verify(directory, reference["manifest_hash"], identity)
        if positive(job.payload["spec"]) and job.kind == "train":
            shape = job.payload["spec"]["rollout_shape"]
            first = job.payload["start_decisions"] // (shape["envs"] * shape["rollout"]) + 1
            if manifest["metadata"]["metrics_coverage"]["first_expected_update"] != first:
                raise ValueError("Task089 raw metrics are not bound to this segment")
            body = _json_file(directory / manifest["files"][TRANSITION_ROLE]["name"], TRANSITION_MAX_BYTES)
            verify_ancestor_counts(directory.parent.parent, job, body)
            from .transition_contract import verify_gate_ancestor
            metrics = _json_file(directory / manifest["files"]["metrics"]["name"], 64 * 1024)
            verify_gate_ancestor(directory.parent.parent, job, metrics.get("first_transition_gate"))
        return manifest

    def ready(self, job, reference):
        target, identity = self._target(job, reference)
        if not (target / "ready.json").exists():
            return False
        self._verify_generation(target, reference, identity, job)
        return True

    def copy(self, job, reference):
        import errno
        import os
        from tempfile import TemporaryDirectory

        target, identity = self._target(job, reference)
        if target.exists():
            self._verify_generation(target, reference, identity, job)
            return True
        target.parent.mkdir(exist_ok=True)
        remote = f"{identity['job_key']}/{target.name}"
        size_cap = max(64 * 1024**2, total_params(job.payload["spec"]) * 32 + 64 * 1024**2)
        with TemporaryDirectory(prefix=".mirror-", dir=target.parent) as scratch:
            staging = Path(scratch)
            ready = staging / "ready.json"
            self._copy_file(f"{remote}/ready.json", ready, 32_000)
            if file_digest(ready) != reference["manifest_hash"]:
                raise ValueError("remote ready manifest digest mismatch")
            document = _json_file(ready, 32_000)
            if document.get("identity") != identity or document.get("version") != 1:
                raise ValueError("remote ready manifest does not belong to this job")
            entries = document["files"]
            if type(entries) is not dict or not 2 <= len(entries) <= (9 if positive(job.payload["spec"]) and job.kind == "train" else 7):
                raise ValueError("invalid remote artifact roles")
            for role, entry in entries.items():
                name = entry["name"]
                if (type(name) is not str or not re.fullmatch(r"[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}", name)
                        or name == "ready.json"):
                    raise ValueError("invalid remote artifact path")
                expected = integer(entry["size"], 0 if role == RAW_METRICS_ROLE else 1)
                limit = (TRANSITION_MAX_BYTES if role == TRANSITION_ROLE else RAW_METRICS_MAX_BYTES if role == RAW_METRICS_ROLE else HISTORY_LIMIT if role == "history" else
                         size_cap if name == "checkpoint.pt" else 16 * 1024**2)
                if expected > limit:
                    raise ValueError("remote artifact exceeds its byte bound")
                self._copy_file(f"{remote}/{name}", staging / name, expected)
            self._verify_generation(staging, reference, identity, job)
            try:
                os.rename(staging, target)
            except OSError as error:
                if error.errno not in {errno.EEXIST, errno.ENOTEMPTY}:
                    raise
                self._verify_generation(target, reference, identity, job)
            fd = os.open(target.parent, os.O_RDONLY)
            try:
                os.fsync(fd)
            finally:
                os.close(fd)
        return True

    def _copy_file(self, remote, destination, maximum):
        import os

        size = 0
        with destination.open("xb") as output:
            for chunk in self.volume.read_file(remote):
                if type(chunk) is not bytes or size + len(chunk) > maximum:
                    raise ValueError("remote artifact stream exceeds its byte bound")
                size += len(chunk)
                output.write(chunk)
            output.flush()
            os.fsync(output.fileno())
