"""Publish immutable, checksummed artifacts with a ready marker written last."""

from __future__ import annotations

import hashlib
import json
import os
import stat
from pathlib import Path
from tempfile import NamedTemporaryFile

from .contracts import canonical, integer
from .transition_contract import IDENTITY, ROLE as TRANSITION_ROLE, MAX_BYTES as TRANSITION_MAX_BYTES, verify_sidecar

TRAIN_FILES = {"model", "optimizer", "scheduler", "rng", "seed_cursor", "metrics"}
EVAL_FILES = {"metrics", "per_seed"}
MANIFEST_NAME = "ready.json"
HISTORY_MAX_BYTES = 128 * 1024
RAW_METRICS_MAX_BYTES = 16 * 1024 * 1024
RAW_METRICS_LINE_MAX_BYTES = 64 * 1024
RAW_METRICS_ROLE = "metrics_raw"
RAW_METRICS_NAME = "metrics.jsonl"
RAW_METRICS_REWARD_VERSION = "protocol-v0.2-delta-win1-noacq-cycle-0p01-stepcap-bootstrap-v1"


def file_digest(path):
    with Path(path).open("rb") as handle:
        return hashlib.file_digest(handle, "sha256").hexdigest()


def _file(root, name):
    relative = Path(name)
    if relative.is_absolute() or len(relative.parts) != 1 or relative.name != name or name == MANIFEST_NAME:
        raise ValueError("artifact must have a simple file name")
    path = root / relative
    if path.is_symlink() or not stat.S_ISREG(path.stat().st_mode):
        raise ValueError("artifact must be a regular file")
    return path


def _required(identity, metadata):
    if identity["kind"] == "train":
        if metadata["resume_mode"] not in {"exact", "fresh_episodes"}:
            raise ValueError("unknown resume mode")
        return TRAIN_FILES | ({"environment"} if metadata["resume_mode"] == "exact" else set())
    if identity["kind"] == "eval":
        return EVAL_FILES
    raise ValueError("unknown artifact kind")


def _valid_roles(identity, metadata, roles):
    required = _required(identity, metadata)
    auxiliary = identity.get("training_auxiliary")
    if auxiliary is not None and auxiliary != IDENTITY:
        return False
    if identity["kind"] == "train" and auxiliary == IDENTITY:
        if (roles != required | {"history", RAW_METRICS_ROLE, TRANSITION_ROLE}
                or "metrics_coverage" not in metadata or metadata.get("transition_auxiliary") != IDENTITY):
            return False
        roles = roles - {TRANSITION_ROLE}
    elif TRANSITION_ROLE in roles or "transition_auxiliary" in metadata:
        return False
    if RAW_METRICS_ROLE in roles or "metrics_coverage" in metadata:
        return (identity["kind"] == "train" and metadata["resume_mode"] == "fresh_episodes"
                and roles == required | {"history", RAW_METRICS_ROLE} and "metrics_coverage" in metadata)
    return roles == required or (identity["kind"] == "train" and roles == required | {"history"})


def raw_metrics_coverage(path, first_expected_update, checkpoint_update, checkpoint_decisions, *, partial):
    """Validate exact, contiguous completed updates. Only a periodic checkpoint may be one row ahead."""
    first = integer(first_expected_update, 1)
    last = integer(checkpoint_update, first)
    decisions = integer(checkpoint_decisions, 1)
    stride, remainder = divmod(decisions, last)
    if not stride or remainder or path.stat().st_size > RAW_METRICS_MAX_BYTES:
        raise ValueError("raw metric snapshot exceeds its bounds")
    update = first
    with path.open("rb") as handle:
        while line := handle.readline(RAW_METRICS_LINE_MAX_BYTES + 1):
            if len(line) > RAW_METRICS_LINE_MAX_BYTES or not line.endswith(b"\n"):
                raise ValueError("raw metric row is incomplete or oversized")
            row = json.loads(line)
            if (type(row) is not dict or type(row.get("update")) is not int or row["update"] != update
                    or type(row.get("env_steps")) is not int or row["env_steps"] != update * stride or update > last):
                raise ValueError("raw metric rows differ from checkpoint progress")
            canonical(row)  # reject non-finite JSON values
            update += 1
    missing_final = update == last
    if update != last + 1 and not (partial and missing_final):
        raise ValueError("raw metric snapshot has missing completed updates")
    return {"first_expected_update": first, "last_expected_update": last, "rows": update - first,
            "last_observed_update": update - 1 if update > first else None, "missing_final_update": missing_final}


def _validate_raw_metrics(path, metadata):
    coverage = metadata["metrics_coverage"]
    if path.name != RAW_METRICS_NAME or type(coverage) is not dict:
        raise ValueError("invalid raw metric artifact or coverage record")
    actual = raw_metrics_coverage(path, coverage.get("first_expected_update"), metadata["updates"],
                                  metadata["decisions"], partial=metadata["termination"] == "checkpoint")
    if canonical(coverage) != canonical(actual):
        raise ValueError("raw metric coverage differs from the ready manifest")


def _sync_dir(path):
    fd = os.open(path, os.O_RDONLY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def publish(root, identity, metadata, files, commit=None, before_ready=None):
    root = Path(root)
    if not _valid_roles(identity, metadata, set(files)):
        raise ValueError("incomplete artifact role set")
    if len(set(files.values())) != len(files):
        raise ValueError("each role requires a distinct artifact")
    entries = {}
    for role, name in files.items():
        path = _file(root, name)
        size = path.stat().st_size
        if role == TRANSITION_ROLE and size > TRANSITION_MAX_BYTES:
            raise ValueError("transition sidecar exceeds bound")
        if role == "history" and size > HISTORY_MAX_BYTES:
            raise ValueError("history artifact exceeds its byte bound")
        if role == RAW_METRICS_ROLE:
            _validate_raw_metrics(path, metadata)
        with path.open("rb") as handle:
            os.fsync(handle.fileno())
        entries[role] = {"name": name, "size": size, "sha256": file_digest(path)}
    verify_sidecar(root, identity, metadata, entries)
    document = {"version": 1, "identity": identity, "metadata": metadata, "files": entries}
    data = canonical(document).encode()
    if len(data) > 32000:
        raise ValueError("ready manifest too large")
    ready = root / MANIFEST_NAME
    checksum = hashlib.sha256(data).hexdigest()
    _sync_dir(root)
    if commit:
        commit()  # commit payload files before making the marker visible
    if before_ready:
        before_ready(checksum)
    with NamedTemporaryFile(dir=root, prefix=".ready-", delete=False) as handle:
        temporary = Path(handle.name)
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    try:
        try:
            os.link(temporary, ready)  # atomic create without replacing an existing ready marker
        except FileExistsError:
            if ready.is_symlink() or ready.read_bytes() != data:
                raise ValueError("attempt to replace immutable ready manifest")
    finally:
        temporary.unlink()
    _sync_dir(root)
    if commit:
        commit()
    return checksum


def verify(root, reference, identity):
    root = Path(root)
    if root.is_symlink():
        raise ValueError("artifact directory may not be a symlink")
    ready = root / MANIFEST_NAME
    if ready.is_symlink() or not 0 < ready.stat().st_size <= 32000:
        raise ValueError("invalid ready manifest")
    data = ready.read_bytes()
    if hashlib.sha256(data).hexdigest() != reference:
        raise ValueError("manifest digest mismatch")
    document = json.loads(data)
    if document["version"] != 1 or document["identity"] != identity:
        raise ValueError("artifact identity mismatch")
    metadata = document["metadata"]
    entries = document["files"]
    if not _valid_roles(identity, metadata, set(entries)):
        raise ValueError("incomplete artifact role set")
    if len({entry["name"] for entry in entries.values()}) != len(entries):
        raise ValueError("duplicate artifact path")
    for role, entry in entries.items():
        path = _file(root, entry["name"])
        size = path.stat().st_size
        declared_size = integer(entry["size"])
        if role == "history" and (size > HISTORY_MAX_BYTES or declared_size > HISTORY_MAX_BYTES):
            raise ValueError("history artifact exceeds its byte bound")
        if role == TRANSITION_ROLE and (size > TRANSITION_MAX_BYTES or declared_size > TRANSITION_MAX_BYTES):
            raise ValueError("transition sidecar exceeds bound")
        if size != declared_size or file_digest(path) != entry["sha256"]:
            raise ValueError("artifact checksum mismatch")
        if role == RAW_METRICS_ROLE:
            _validate_raw_metrics(path, metadata)
    verify_sidecar(root, identity, metadata, entries)
    integer(metadata["decisions"], 1)
    if identity["kind"] == "train":
        integer(metadata["cursor"])
        integer(metadata["updates"], 1)
    return document
