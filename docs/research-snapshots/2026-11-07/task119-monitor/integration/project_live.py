#!/usr/bin/env python3
"""Project existing, exact-call observer JSON into a small dashboard payload."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import sys

RUNS = {
    "M116": {
        "label": "M116", "call_id": "fc-01M6SREBKE87QMT2VQKEMC0Q6T", "baseline": 0,
        "target": 100007936, "submitted": 1794013343.2863705, "deadline": 1794034943,
    },
    "S115-S2": {
        "label": "S115 · S2", "call_id": "fc-01M6SSSTZJRPZSWJNQDBMZTFC6",
        "baseline": 595853312, "target": 695861248,
        "submitted": 1794014768.0605395, "deadline": 1794023768,
    },
}
OBSERVATION_NAME = re.compile(r"observation-(\d{10})\.json")
MAX_INPUT = 4 * 1024 * 1024


def reject_constant(value):
    raise ValueError("non-finite JSON value")


def read_json(path, limit=MAX_INPUT, expected_sha256=None):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, "rb") as source:
        if not stat.S_ISREG(os.fstat(source.fileno()).st_mode):
            raise ValueError("expected regular metadata file")
        data = source.read(limit + 1)
    if len(data) > limit:
        raise ValueError("metadata too large")
    if expected_sha256 is not None and hashlib.sha256(data).hexdigest() != expected_sha256:
        raise ValueError("metadata SHA-256 differs")
    return json.loads(data, parse_constant=reject_constant)


def utc(epoch):
    return datetime.fromtimestamp(epoch, timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def epoch(value):
    try:
        time = datetime.fromisoformat(value)
        return time.timestamp() if time.utcoffset() is not None else None
    except (TypeError, ValueError, OverflowError):
        return None


def number(value):
    return type(value) in (float, int) and math.isfinite(value)


def optional_count(value):
    return value if type(value) is int and 0 <= value <= 2**53 - 1 else None


def document(value):
    return value if isinstance(value, dict) else {}


def body(observation, key):
    return document(document(observation.get(key)).get("body"))


def project(raw, identifier):
    identity = RUNS[identifier]
    if raw.get("call_id") != identity["call_id"]:
        raise ValueError("foreign call identity")
    at = raw.get("at")
    if not number(at) or not identity["submitted"] <= at <= identity["deadline"] + 600:
        raise ValueError("invalid observer timestamp")
    handle = document(body(raw, "entry-status.json").get("handle"))
    if handle and (handle.get("call_id") != identity["call_id"]
                   or handle.get("absolute_deadline") != identity["deadline"]):
        raise ValueError("foreign worker metadata identity")
    states = {"live_unfinished": "running", "returned": "terminal", "unknown_exception": "unknown"}
    status = states.get(raw.get("call_state"), "unknown")
    latest = body(raw, "run/latest.json")
    update = optional_count(raw.get("checkpoint_update"))
    checkpoint = latest.get("checkpoint")
    dose = update * 131072 if update is not None else None
    if (dose is None or not identity["baseline"] < dose <= identity["target"]
            or raw.get("new_env_steps") != dose - identity["baseline"]
            or not isinstance(checkpoint, str)
            or re.fullmatch(r"checkpoints/ckpt-\d+\.pt", checkpoint) is None
            or int(checkpoint.removeprefix("checkpoints/ckpt-").removesuffix(".pt")) != update
            or re.fullmatch(r"[a-f0-9]{64}", str(latest.get("sha256"))) is None):
        dose = update = None
    health_status = body(document(raw.get("closed_metadata")), "run/status.json")
    status_at = epoch(health_status.get("updated_at"))
    if status_at is not None and not identity["submitted"] <= status_at <= at:
        status_at = None
    progress = document(health_status.get("recent")).get("progress_mean")
    if not number(progress) or not 0 <= progress <= 1:
        progress = None
    return {
        "id": identifier, "label": identity["label"], "status": status,
        "observed_at": utc(at), "started_at": utc(identity["submitted"]),
        "deadline_at": utc(identity["deadline"]), "decisions": dose,
        "target_decisions": identity["target"],
        "new_decisions": dose - identity["baseline"] if dose is not None else None,
        "updates": update,
        "checkpoint_evidence": "writer_metadata" if dose is not None else "none",
        "resources": {"gpu": "H100!", "cpu": 32, "memory_gib": 64},
        "progress_health": {
            "observed_at": utc(status_at) if status_at is not None else None,
            "training_progress": progress if status_at is not None else None,
            "episodes": optional_count(health_status.get("episodes")) if status_at is not None else None,
            "table_overflow_count": None,
        },
    }


def latest(directory, identifier):
    candidates = []
    with os.scandir(directory) as files:
        for index, entry in enumerate(files):
            if index >= 4096:
                raise ValueError("observer directory exceeds bounded listing")
            match = OBSERVATION_NAME.fullmatch(entry.name)
            if match:
                candidates.append((int(match[1]), Path(entry.path)))
    paths = [directory / "terminal-observation.json"]
    paths.extend(path for _, path in sorted(candidates, reverse=True)[:6])
    for path in paths:
        try:
            raw = read_json(path)
        except (FileNotFoundError, json.JSONDecodeError, UnicodeDecodeError):
            continue  # The current observer may still be writing the latest file.
        projected = project(raw, identifier)
        match = OBSERVATION_NAME.fullmatch(path.name)
        if match and int(raw["at"]) != int(match[1]):
            raise ValueError("observer filename timestamp differs")
        if path.name == "terminal-observation.json" and projected["status"] != "terminal":
            raise ValueError("terminal file lacks returned call evidence")
        return projected
    raise ValueError("no complete observation for configured call")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("m", "s2"):
        inputs = parser.add_mutually_exclusive_group(required=True)
        inputs.add_argument("--" + name, type=Path, help="immutable saved observer JSON")
        inputs.add_argument("--" + name + "-dir", type=Path, help="existing observer directory")
    args = parser.parse_args()
    runs = []
    for identifier, path, directory in (
        ("M116", args.m, args.m_dir), ("S115-S2", args.s2, args.s2_dir),
    ):
        runs.append(project(read_json(path), identifier) if path else latest(directory, identifier))
    newest = max(run["observed_at"] for run in runs)
    payload = {"schema_version": 1, "version": int(round(epoch(newest) * 1000)),
               "observed_at": newest, "runs": runs}
    print(json.dumps(payload, ensure_ascii=False, allow_nan=False, separators=(",", ":")))


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, AttributeError, TypeError, OverflowError) as error:
        print(f"monitor_projection_failed={type(error).__name__}", file=sys.stderr)
        raise SystemExit(1) from None
