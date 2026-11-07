#!/usr/bin/env python3
"""Bounded single-writer bridge for two existing saved observers."""
import argparse
import fcntl
import http.client
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import urllib.error

from project_live import latest, payload_for, read_json
from push_live import MAX_PAYLOAD, SiteClient, read_service_token

HARD_STOP = 1794035543
INTERVAL = 90
TERMINAL_MARGIN = 180
MAX_BACKOFF = 360
STATE_DIRECTORY = Path("/var/lib/task119-live-bridge")
DIRECTORIES = {
    "M116": Path("/data/raven/hack-sts2-modal-research/task116-M-ops/observer-v2"),
    "S115-S2": Path("/data/raven/hack-sts2-modal-research/task115-second-segment-stage/observer"),
}
TRANSIENT_ERRORS = (OSError, http.client.HTTPException, ValueError, KeyError, TypeError, AttributeError, OverflowError)


class EqualVersionPending(ValueError):
    """A changed peer must wait for a new global observation time."""


def log(**fields):
    print(json.dumps(fields, separators=(",", ":"), ensure_ascii=False, allow_nan=False), flush=True)


def summary(payload):
    return {
        "version": payload["version"],
        "observed_at": payload["observed_at"],
        "runs": [
            {key: run[key] for key in ("id", "status", "observed_at", "updates", "decisions", "checkpoint_evidence")}
            for run in payload["runs"]
        ],
    }


def observation():
    return payload_for([latest(directory, name) for name, directory in DIRECTORIES.items()])


def read_state(directory):
    try:
        value = read_json(directory / "state.json", 2 * MAX_PAYLOAD + 1024)
    except FileNotFoundError:
        return {"schema_version": 1, "last_good": None, "pending": None, "terminal_seen_at": None}
    if (not isinstance(value, dict) or set(value) != {"schema_version", "last_good", "pending", "terminal_seen_at"}
            or value["schema_version"] != 1
            or (value["terminal_seen_at"] is not None and type(value["terminal_seen_at"]) is not int)
            or any(value[key] is not None and not isinstance(value[key], dict) for key in ("last_good", "pending"))):
        raise ValueError("bridge state schema differs")
    return value


def persist(directory, state):
    with tempfile.NamedTemporaryFile(mode="w", encoding="utf8", prefix=".state-", dir=directory, delete=False) as file:
        path = Path(file.name)
        try:
            json.dump(state, file, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
            file.flush()
            os.fsync(file.fileno())
            file.close()
            path.replace(directory / "state.json")
            directory_fd = os.open(directory, os.O_DIRECTORY)
            try:
                os.fsync(directory_fd)
            finally:
                os.close(directory_fd)
        finally:
            path.unlink(missing_ok=True)


def deadline(state):
    seen = state["terminal_seen_at"]
    return HARD_STOP if seen is None else min(HARD_STOP, seen + TERMINAL_MARGIN)


def site_timeout(end, clock):
    remaining = end - clock()
    if remaining <= 0:
        raise TimeoutError("bridge deadline")
    return min(15, remaining)


def publish_new(client, payload, state, clock):
    previous = state["last_good"]
    if previous is not None and payload["version"] <= previous["version"]:
        if payload["version"] == previous["version"] and payload != previous:
            raise EqualVersionPending("observations differ at confirmed version")
        return None
    end = deadline(state)
    remote = client.get(site_timeout(end, clock))
    if remote["version"] > payload["version"]:
        return remote
    if remote["version"] == payload["version"]:
        if remote != payload:
            raise EqualVersionPending("Site and observer differ at same version")
        return remote
    client.post(payload, site_timeout(end, clock))
    readback = client.get(site_timeout(end, clock))
    if readback != payload:
        raise ValueError("Site readback differs from posted version or observations")
    return readback


def loop(directory, client, *, load=observation, clock=time.time, sleep=time.sleep):
    lock_fd = os.open(directory / "writer.lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(lock_fd, "r+") as lock:
        try:
            fcntl.flock(lock.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            log(event="writer_already_running")
            return
        state = read_state(directory)
        failures = 0
        retry_at = 0
        while clock() < deadline(state):
            tick = clock()
            try:
                payload = load()
                if (state["terminal_seen_at"] is None
                        and all(run["status"] == "terminal" for run in payload["runs"])):
                    updated = {**state, "terminal_seen_at": int(clock())}
                    persist(directory, updated)
                    state = updated
            except TRANSIENT_ERRORS as error:
                log(event="observation_unavailable", error=type(error).__name__)
            else:
                if payload != state["pending"] and clock() >= retry_at and clock() < deadline(state):
                    try:
                        verified = publish_new(client, payload, state, clock)
                        if verified is not None:
                            updated = {**state, "last_good": verified, "pending": None}
                            persist(directory, updated)
                            state = updated
                            log(event="site_verified", **summary(verified))
                        failures = 0
                    except EqualVersionPending:
                        updated = {**state, "pending": payload}
                        persist(directory, updated)
                        state = updated
                        log(event="peer_observation_pending_new_global_version", **summary(payload))
                    except TRANSIENT_ERRORS as error:
                        fields = {"status": error.code} if isinstance(error, urllib.error.HTTPError) else {"error": type(error).__name__}
                        log(event="site_update_unavailable", **fields)
                        failures += 1
                        backoff = min(MAX_BACKOFF, INTERVAL * 2**min(failures - 1, 2))
                        retry_at = clock() + backoff
            next_tick = min(tick + INTERVAL, deadline(state))
            remaining = next_tick - clock()
            if remaining > 0:
                sleep(remaining)
        reason = "both_calls_terminal" if state["terminal_seen_at"] is not None else "hard_deadline"
        if state["pending"] is not None:
            log(event="pending_observation_unpublished", **summary(state["pending"]))
        log(event="bridge_finished", reason=reason)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--readback", action="store_true", help="one authenticated GET with safe fields only")
    parser.add_argument("--credential-fd", type=int, help="use inherited FD in place of systemd LoadCredential")
    args = parser.parse_args()
    if not args.readback and time.time() >= HARD_STOP:
        log(event="bridge_finished", reason="hard_deadline")
        return
    client = SiteClient(read_service_token(args.credential_fd))
    if args.readback:
        log(event="site_verified", **summary(client.get(15)))
    else:
        loop(STATE_DIRECTORY, client)


if __name__ == "__main__":
    try:
        main()
    except urllib.error.HTTPError as error:
        log(event="bridge_failed", status=error.code)
        raise SystemExit(1) from None
    except TRANSIENT_ERRORS as error:
        log(event="bridge_failed", error=type(error).__name__)
        raise SystemExit(1) from None
