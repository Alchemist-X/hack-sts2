"""Publish immutable, checksummed artifacts with a ready marker written last."""

from __future__ import annotations

import hashlib
import json
import os
import stat
from pathlib import Path
from tempfile import NamedTemporaryFile

from .contracts import canonical, integer

TRAIN_FILES = {"model", "optimizer", "scheduler", "rng", "seed_cursor", "metrics"}
EVAL_FILES = {"metrics", "per_seed"}
MANIFEST_NAME = "ready.json"


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


def _sync_dir(path):
    fd = os.open(path, os.O_RDONLY)
    try:
        os.fsync(fd)
    finally:
        os.close(fd)


def publish(root, identity, metadata, files, commit=None):
    root = Path(root)
    if set(files) != _required(identity, metadata):
        raise ValueError("incomplete artifact role set")
    if len(set(files.values())) != len(files):
        raise ValueError("each role requires a distinct artifact")
    entries = {}
    for role, name in files.items():
        path = _file(root, name)
        with path.open("rb") as handle:
            os.fsync(handle.fileno())
        entries[role] = {"name": name, "size": path.stat().st_size, "sha256": file_digest(path)}
    document = {"version": 1, "identity": identity, "metadata": metadata, "files": entries}
    data = canonical(document).encode()
    if len(data) > 32000:
        raise ValueError("ready manifest too large")
    ready = root / MANIFEST_NAME
    _sync_dir(root)
    if commit:
        commit()  # commit payload files before making the marker visible
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
    return hashlib.sha256(data).hexdigest()


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
    if set(entries) != _required(identity, metadata):
        raise ValueError("incomplete artifact role set")
    if len({entry["name"] for entry in entries.values()}) != len(entries):
        raise ValueError("duplicate artifact path")
    for entry in entries.values():
        path = _file(root, entry["name"])
        if path.stat().st_size != entry["size"] or file_digest(path) != entry["sha256"]:
            raise ValueError("artifact checksum mismatch")
    integer(metadata["decisions"], 1)
    if identity["kind"] == "train":
        integer(metadata["cursor"])
        integer(metadata["updates"], 1)
    return document
