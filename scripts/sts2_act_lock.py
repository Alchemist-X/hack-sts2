"""Per-instance (per-port) lock so only one ``act`` touches a game at a time.

Two act calls racing against the same instance interleave their HTTP actions
and the ``pending-action.json`` bookkeeping (the S08-replay ``parallel_act_race``
friction: one play landed, the other was left pending with a
FileNotFoundError).  Every mutating command (sts2_live ``act`` / ``reconcile``,
agent_step ``act``) therefore takes an exclusive ``flock`` on
``<lock dir>/port-<port>.lock``.  By default a second caller fails fast with an
``act_in_flight`` error naming the holder; ``wait_s`` lets it queue instead.

The lock is keyed by port, not run directory, so two run directories pointed at
the same instance also serialize.  A parent that already holds the lock (agent_step
spawning sts2_live) exports ``STS2_ACT_LOCK_HELD=<port>`` so the child does not
deadlock on its own parent.  ``flock`` is released by the kernel when the
holder exits, so a crashed caller never leaves a stale lock behind.
"""
from __future__ import annotations

import contextlib
import fcntl
import json
import os
import tempfile
import time
from datetime import datetime, timezone
from pathlib import Path

HELD_ENV = 'STS2_ACT_LOCK_HELD'
DIR_ENV = 'STS2_ACT_LOCK_DIR'
POLL_S = 0.05


class ActInFlight(Exception):
    """Another act already holds this instance's lock."""

    def __init__(self, port, holder, waited_s):
        self.port, self.holder, self.waited_s = port, holder, waited_s
        who = ', '.join(f'{k}={v}' for k, v in (holder or {}).items() if k in ('pid', 'command', 'run_dir', 'since'))
        super().__init__(
            f'another act in flight on port {port} ({who or "holder unknown"}); nothing was sent. '
            'Game commands must be sequential: wait for that call to return, observe, then decide again.')


def lock_dir() -> Path:
    path = Path(os.environ.get(DIR_ENV) or Path(tempfile.gettempdir()) / 'sts2-act-locks')
    path.mkdir(parents=True, exist_ok=True)
    return path


def lock_path(port) -> Path:
    return lock_dir() / f'port-{int(port)}.lock'


def read_holder(path: Path) -> dict | None:
    try:
        text = path.read_text().strip()
        return json.loads(text) if text else None
    except (OSError, ValueError):
        return None


@contextlib.contextmanager
def act_lock(port, *, wait_s: float = 0.0, command: str = 'act', run_dir=None):
    """Hold the instance lock for the body; raise ActInFlight if it is taken.

    Re-entrant across a parent/child process pair via ``STS2_ACT_LOCK_HELD``.
    """
    port = int(port)
    if os.environ.get(HELD_ENV) == str(port):
        yield None
        return
    path = lock_path(port)
    handle = open(path, 'a+')
    began = time.monotonic()
    try:
        while True:
            try:
                fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except BlockingIOError:
                waited = time.monotonic() - began
                if waited >= wait_s:
                    raise ActInFlight(port, read_holder(path), round(waited, 3)) from None
                time.sleep(POLL_S)
        holder = {'pid': os.getpid(), 'command': command, 'run_dir': str(run_dir) if run_dir else None,
                  'since': datetime.now(timezone.utc).isoformat()}
        handle.seek(0)
        handle.truncate()
        handle.write(json.dumps(holder))
        handle.flush()
        previous = os.environ.get(HELD_ENV)
        os.environ[HELD_ENV] = str(port)
        try:
            yield holder
        finally:
            if previous is None:
                os.environ.pop(HELD_ENV, None)
            else:
                os.environ[HELD_ENV] = previous
            with contextlib.suppress(OSError):
                handle.seek(0)
                handle.truncate()
            fcntl.flock(handle.fileno(), fcntl.LOCK_UN)
    finally:
        handle.close()
