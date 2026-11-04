"""Async job contract and safety checks shared by agent and worker adapters."""

from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol


@dataclass(frozen=True)
class Job:
    key: str               # per-attempt idempotency key
    logical_key: str       # stable across all attempts
    attempt: int
    kind: str
    deadline: float        # absolute UTC time, including queueing and retry
    reserve_cents: int
    payload: dict


@dataclass(frozen=True)
class Outcome:
    status: str
    actual_cents: int | None = None
    output: dict | str | None = None
    error: str | None = None


class Backend(Protocol):
    """A backend must guarantee atomic idempotent submit keyed by Job.key.

    lookup returns None only for authoritative absence, and raises on ambiguity.
    Status/lookup requests must honor bounded I/O timeouts. Results must include
    final cost and complete evidence. Exception messages are never recorded.
    """
    fixture: bool
    tool_enabled: bool

    def submit(self, job: Job) -> str: ...  # local fixture only
    def submit_admitted(self, job: Job, admission: dict) -> str: ...  # provider must atomically check the fence
    def lookup(self, job: Job) -> str | None: ...
    def poll(self, remote_id: str) -> Outcome: ...


class RemoteDisabled:
    fixture = False
    tool_enabled = False

    def __init__(self):
        raise RuntimeError("remote execution requires a verified idempotent gateway, tool-enabled SOL, and transactional state")
