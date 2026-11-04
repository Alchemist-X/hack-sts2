"""Claim, fence and supervise one isolated Modal worker process."""

from __future__ import annotations

import ctypes
import json
import math
import multiprocessing
import os
import signal
import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path

from .adapters import Outcome
from .artifact_adapter import verify_bundle
from .contracts import canonical, identifier, integer
from .lineage import AcceptedCheckpoint, validate_job
from .native_worker import run as run_native

MAX_MESSAGE = 64 * 1024
POLL_SECONDS = 0.1
CHILD_ENV = frozenset({
    "PATH", "HOME", "LANG", "LC_ALL", "TZ", "LD_LIBRARY_PATH", "PYTHONNOUSERSITE", "TMPDIR",
    "CUDA_VISIBLE_DEVICES", "NVIDIA_VISIBLE_DEVICES", "NVIDIA_DRIVER_CAPABILITIES",
    "DOTNET_gcServer", "DOTNET_gcConcurrent", "DOTNET_PROCESSOR_COUNT",
})


class LeaseLost(RuntimeError):
    pass


@dataclass(frozen=True)
class WorkerLimits:
    cpu: int = 16
    memory_bytes: int = 64 * 1024**3
    max_runtime_seconds: int = 4500

    def __post_init__(self):
        if not 1 <= integer(self.cpu, 1) <= 16 or not 1 <= integer(self.memory_bytes, 1) <= 64 * 1024**3:
            raise ValueError("worker resources exceed the deployed envelope")
        if not 1 <= integer(self.max_runtime_seconds, 1) <= 4500:
            raise ValueError("worker runtime exceeds the deployed envelope")


def check_cgroup(limits, *, proc=Path("/proc/self/cgroup"), mount=Path("/sys/fs/cgroup")):
    """Verify effective CPU/memory limits, including caps inherited from ancestors."""
    entries = [line.removeprefix("0::") for line in proc.read_text().splitlines() if line.startswith("0::")]
    if len(entries) != 1 or not entries[0].startswith("/") or ".." in Path(entries[0]).parts:
        raise ValueError("worker requires a visible cgroup v2")
    mount = mount.resolve()
    group = mount / entries[0].lstrip("/")
    if not group.is_dir():
        raise ValueError("worker cgroup is not visible")
    cpu_cap = memory_cap = math.inf
    while True:
        cpu_file, memory_file = group / "cpu.max", group / "memory.max"
        if cpu_file.is_file():
            quota, period = cpu_file.read_text().split()
            if quota != "max":
                cpu_cap = min(cpu_cap, int(quota) / int(period))
        if memory_file.is_file():
            amount = memory_file.read_text().strip()
            if amount != "max":
                memory_cap = min(memory_cap, int(amount))
        if group == mount:
            break
        group = group.parent
    if not 0 < cpu_cap <= limits.cpu or not 0 < memory_cap <= limits.memory_bytes:
        raise ValueError("effective cgroup limits exceed the worker grant")


class _Pending:
    """One bounded in-flight control operation. An ambiguous operation is never retried."""

    def __init__(self, target, *args, **kwargs):
        self.done = threading.Event()
        self.value = self.error = None

        def run():
            try:
                self.value = target(*args, **kwargs)
            except Exception as exc:
                self.error = exc
            finally:
                self.done.set()

        threading.Thread(target=run, daemon=True).start()

    def result(self):
        if self.error is not None:
            raise self.error
        return self.value


CLOCK_SAMPLE_SECONDS = 2.0
CLOCK_MARGIN_SECONDS = 0.25


def _clock_offset(broker, clock, monotonic):
    wall_before, mono_before = clock(), monotonic()
    server = broker.server_time()
    wall_span, mono_span = clock() - wall_before, monotonic() - mono_before
    if (type(server) not in (int, float) or not math.isfinite(server)
            or not 0 <= wall_span <= CLOCK_SAMPLE_SECONDS or mono_span > CLOCK_SAMPLE_SECONDS
            or abs(wall_span - mono_span) > CLOCK_MARGIN_SECONDS):
        raise LeaseLost("broker clock could not be bounded")
    # The authenticated sample is taken after the request started. Its offset
    # relative to wall_before is the conservative upper bound on skew.
    return server - wall_before + CLOCK_MARGIN_SECONDS


class _Fence:
    def __init__(self, broker, claim, clock, monotonic, limit, renewal_seconds, offset):
        self.broker, self.claim = broker, claim
        self.clock, self.monotonic = clock, monotonic
        self.renewal_seconds = renewal_seconds
        self.deadline = min(claim.job.deadline - offset, clock() + limit)
        self.hard = monotonic() + self.deadline - clock()
        self.renew_at = monotonic()
        self.pending = None
        self.heartbeat = None
        self.set_lease(claim.lease_until, offset)

    def set_lease(self, expiration, offset):
        if (type(expiration) not in (int, float) or not math.isfinite(expiration)
                or not self.clock() + offset < expiration <= self.claim.job.deadline):
            raise LeaseLost("worker grant is absent or invalid")
        self.deadline = min(self.deadline, self.claim.job.deadline - offset)
        self.hard = min(self.hard, self.monotonic() + self.deadline - self.clock())
        self.lease_until = expiration - offset
        self.lease_mono = self.monotonic() + self.lease_until - self.clock()
        if self.heartbeat is not None:
            self.heartbeat.send_bytes(str(min(self.lease_until, self.deadline)).encode("ascii"))

    def renew(self):
        expiration = self.broker.renew(self.claim)
        if expiration is None:
            raise LeaseLost("worker grant was revoked")
        return expiration, _clock_offset(self.broker, self.clock, self.monotonic)

    def check(self):
        now = self.monotonic()
        if (now >= min(self.hard, self.lease_mono) or self.clock() >= min(self.deadline, self.lease_until)):
            raise LeaseLost("worker deadline or lease elapsed")
        if self.pending is not None and self.pending.done.is_set():
            try:
                renewed, offset = self.pending.result()
                self.set_lease(renewed, offset)
            except Exception:
                raise LeaseLost("worker lease renewal is uncertain") from None
            self.pending = None
            self.renew_at = now + self.renewal_seconds
        if self.pending is None and (now >= self.renew_at or self.lease_mono - now < self.renewal_seconds):
            self.pending = _Pending(self.renew)

    def wait_for(self, target, *args, terminal=False, **kwargs):
        self.check()
        operation = _Pending(target, *args, **kwargs)
        while not operation.done.wait(POLL_SECONDS):
            self.check()
        if not terminal:
            self.check()
        return operation.result()


def _send(connection, message):
    data = canonical(message).encode()
    if len(data) > MAX_MESSAGE:
        raise ValueError("trainer IPC message exceeds size bound")
    connection.send_bytes(data)


def _receive(connection):
    message = json.loads(connection.recv_bytes(MAX_MESSAGE))
    if type(message) is not dict:
        raise ValueError("invalid trainer IPC message")
    return message


def _parent_death_signal(parent_pid):
    if sys.platform != "linux":
        raise RuntimeError("worker isolation requires Linux parent-death signaling")
    libc = ctypes.CDLL(None, use_errno=True)
    if libc.prctl(1, signal.SIGKILL, 0, 0, 0) or os.getppid() != parent_pid:  # PR_SET_PDEATHSIG
        raise RuntimeError("cannot establish parent-death signaling")


def _lease_watchdog(connection, trainer_pipe, expiration, deadline):
    """A separate process stops the whole trainer group when its lease expires."""
    trainer_pipe.close()
    hard_mono = time.monotonic() + deadline - time.time()
    group = os.getpgrp()
    try:
        while True:
            lease_mono = time.monotonic() + expiration - time.time()
            remaining = min(hard_mono, lease_mono) - time.monotonic()
            if remaining <= 0 or not connection.poll(remaining):
                break
            expiration = float(connection.recv_bytes(128))
            if not math.isfinite(expiration) or expiration > deadline:
                break
    except (OSError, EOFError, ValueError):
        pass
    os.killpg(group, signal.SIGKILL)
    os._exit(1)


def _child(connection, lease_reader, parent_pid, initial_lease, job, installation, accepted, seed_range,
           deadline, code_root, cpu, worker):
    """This runs in a clean spawned interpreter, outside the control process."""
    watcher_pid = None
    try:
        _parent_death_signal(parent_pid)
        os.setsid()
        watcher_pid = os.fork()
        if watcher_pid == 0:
            _lease_watchdog(lease_reader, connection, initial_lease, deadline)
        lease_reader.close()
        for name in tuple(os.environ):
            if name not in CHILD_ENV:
                del os.environ[name]
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(code_root))
        if not time.time() < deadline:
            raise TimeoutError("deadline before environment startup")

        def checkpoint_ready(path):
            _send(connection, {"kind": "checkpoint", "path": str(path)})
            if _receive(connection) != {"kind": "published"}:
                raise RuntimeError("checkpoint was not accepted by the supervisor")

        result = worker(job, installation, accepted, checkpoint_ready=checkpoint_ready,
                        seed_range=seed_range, execution_deadline=deadline, max_threads=cpu)
        if job.kind == "train":
            result = {key: result[key] for key in ("last_checkpoint", "env_steps", "stop_reason")}
        else:
            result = {"headline": result["headline"]}
        _send(connection, {"kind": "complete", "result": result})
        if _receive(connection) != {"kind": "exit"}:
            raise RuntimeError("supervisor did not acknowledge trainer exit")
    except Exception as exc:
        code = "worker"
        if type(exc).__name__ == "SeedGrantExhausted":
            code = "seed_grant"
        elif isinstance(exc, TimeoutError):
            code = "timeout"
        elif type(exc).__name__ == "OutOfMemoryError":
            code = "oom"
        _send(connection, {"kind": "failed", "error": code})
    finally:
        if watcher_pid:
            try:
                os.kill(watcher_pid, signal.SIGTERM)
            except ProcessLookupError:
                pass
            os.waitpid(watcher_pid, 0)
        lease_reader.close()
        connection.close()


def _group_alive(pid):
    try:
        os.killpg(pid, 0)
    except ProcessLookupError:
        return False
    return True


def _kill(process, *, grace=3.0, monotonic=time.monotonic, sleep=time.sleep):
    """Kill the trainer group, including compiler subprocesses, and prove exit."""
    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        if process.is_alive():
            process.kill()
    process.join(timeout=grace)
    deadline = monotonic() + grace
    while _group_alive(process.pid) and monotonic() < deadline:
        sleep(POLL_SECONDS)
    return not process.is_alive() and not _group_alive(process.pid)


class WorkerSupervisor:
    """Only the parent process can renew a grant or register an artifact."""

    def __init__(self, broker, publisher, installation, *, bundle_root, bundle_manifest_hash,
                 limits=WorkerLimits(), renewal_seconds=10, clock=time.time, monotonic=time.monotonic,
                 resource_check=check_cgroup, bundle_check=verify_bundle, process_context=None, worker=run_native):
        if not all(callable(getattr(broker, name, None)) for name in (
                "server_time", "claim_worker", "renew", "record_generation", "complete",
        )):
            raise ValueError("an authenticated admission broker is required")
        if not math.isfinite(renewal_seconds) or not 0 < renewal_seconds <= 30:
            raise ValueError("invalid lease renewal interval")
        self.broker, self.publisher, self.installation = broker, publisher, installation
        self.bundle_root, self.bundle_manifest_hash = bundle_root, bundle_manifest_hash
        self.limits, self.renewal_seconds = limits, renewal_seconds
        self.clock, self.monotonic = clock, monotonic
        self.resource_check, self.bundle_check = resource_check, bundle_check
        self.context = process_context or multiprocessing.get_context("spawn")
        self.worker = worker

    def execute(self, kind, job_token, input_id, invocation_id):
        for value in (job_token, input_id, invocation_id):
            identifier(value)
        timing = _Pending(_clock_offset, self.broker, self.clock, self.monotonic)
        if not timing.done.wait(CLOCK_SAMPLE_SECONDS):
            return {"status": "failed", "error": "preempted"}
        try:
            offset = timing.result()
        except Exception:
            return {"status": "failed", "error": "preempted"}
        admission = _Pending(self.broker.claim_worker, kind, job_token, input_id, invocation_id)
        if not admission.done.wait(5):
            return {"status": "failed", "error": "preempted"}
        try:
            claim = admission.result()
        except Exception:
            return {"status": "failed", "error": "preempted"}
        if claim is None:
            return {"status": "not_admitted"}
        job = claim.job
        fence = process = connection = lease_sender = None
        try:
            if (kind not in {"train", "eval"} or job.kind != kind or claim.input_id != input_id
                    or claim.invocation_id != invocation_id):
                raise ValueError("invalid worker claim")
            identifier(claim.receipt)
            integer(claim.fence, 1)
            integer(job.reserve_cents, 1)
            spec = validate_job(job)
            if not math.isfinite(job.deadline) or job.deadline > spec["absolute_deadline"]:
                raise ValueError("worker deadline exceeds the experiment freeze")
            self._validate_inputs(claim)
            duration = spec["resource_request"]["segment_seconds" if kind == "train" else "eval_seconds"]
            fence = _Fence(self.broker, claim, self.clock, self.monotonic,
                           min(duration, self.limits.max_runtime_seconds), self.renewal_seconds, offset)
            self.resource_check(self.limits)
            code_root = fence.wait_for(self.bundle_check, self.installation, spec, self.bundle_root,
                                       self.bundle_manifest_hash)
            fence.wait_for(self.publisher.refresh)
            fence.check()
            connection, child = self.context.Pipe()
            lease_reader, lease_sender = self.context.Pipe(duplex=False)
            initial_lease = min(fence.lease_until, fence.deadline)
            process = self.context.Process(target=_child, args=(
                child, lease_reader, os.getpid(), initial_lease, job, self.installation, claim.accepted_checkpoint,
                claim.seed_range, fence.deadline, code_root, self.limits.cpu, self.worker,
            ))
            try:
                process.start()
            finally:
                child.close()
                lease_reader.close()
            fence.heartbeat = lease_sender
            generation = self._monitor(claim, fence, process, connection)
            if not self._exit_confirmed(process):
                raise LeaseLost("trainer exit was not verified")
            fence.wait_for(self.broker.complete, claim, Outcome("succeeded", output=generation.reference), terminal=True)
            return {"status": "succeeded", "output": generation.reference}
        except LeaseLost:
            return {"status": "failed", "error": "preempted"}
        except Exception as error:
            code = error.args[0] if isinstance(error, _ChildFailure) else "worker"
            if process is not None and process.pid is not None and not _kill(process):
                return {"status": "failed", "error": "preempted"}
            if fence is not None:
                try:
                    fence.wait_for(self.broker.complete, claim, Outcome("failed", error=code), terminal=True)
                except Exception:
                    return {"status": "failed", "error": "preempted"}
            return {"status": "failed", "error": code}
        finally:
            if lease_sender is not None:
                lease_sender.close()
            if connection is not None:
                connection.close()
            if process is not None and process.pid is not None:
                if process.is_alive() or _group_alive(process.pid):
                    _kill(process)
                if not process.is_alive():
                    process.close()

    @staticmethod
    def _validate_inputs(claim):
        job = claim.job
        reference = job.payload["checkpoint"]
        if reference is None:
            if claim.accepted_checkpoint is not None:
                raise ValueError("fresh initialization cannot resume an artifact")
        elif (not isinstance(claim.accepted_checkpoint, AcceptedCheckpoint)
              or claim.accepted_checkpoint.reference != reference):
            raise ValueError("resume requires the broker-accepted checkpoint")
        if job.kind == "train":
            if type(claim.seed_range) is not tuple or len(claim.seed_range) != 2:
                raise ValueError("worker has no seed grant")
            start, end = claim.seed_range
            if integer(start) < job.payload["start_cursor"] or integer(end) <= start:
                raise ValueError("invalid seed reservation")
        elif claim.seed_range is not None:
            raise ValueError("evaluation cannot use the training seed stream")

    def _monitor(self, claim, fence, process, connection):
        while True:
            fence.check()
            if not connection.poll(POLL_SECONDS):
                if not process.is_alive():
                    raise _ChildFailure("worker")
                continue
            try:
                message = fence.wait_for(_receive, connection, terminal=True)
            except EOFError:
                raise _ChildFailure("worker") from None
            kind = message.get("kind")
            if kind == "checkpoint" and claim.job.kind == "train":
                generation = fence.wait_for(self.publisher.training, claim, message["path"],
                                            self.installation.scratch, partial=True)
                self._record(claim, fence, generation)
                _send(connection, {"kind": "published"})
            elif kind == "complete":
                result = message["result"]
                fence.heartbeat = None  # keep the lease pipe open until the child has exited
                fence.check()
                _send(connection, {"kind": "exit"})
                while process.is_alive():
                    fence.check()
                    process.join(timeout=POLL_SECONDS)
                if not self._exit_confirmed(process):
                    raise _ChildFailure("worker")
                if claim.job.kind == "train":
                    if not result.get("last_checkpoint") or result["stop_reason"] not in {"max_env_steps", "max_hours"}:
                        raise _ChildFailure("missing_artifact")
                    generation = fence.wait_for(self.publisher.training, claim, result["last_checkpoint"],
                                                self.installation.scratch, partial=False)
                    if result["env_steps"] != generation.decisions:
                        raise _ChildFailure("missing_artifact")
                else:
                    generation = fence.wait_for(self.publisher.evaluation, claim, result, self.installation)
                self._record(claim, fence, generation)
                return generation
            elif kind == "failed":
                raise _ChildFailure(message.get("error", "worker"))
            else:
                raise _ChildFailure("invalid_response")

    def _record(self, claim, fence, generation):
        fence.wait_for(self.broker.record_generation, claim, generation.reference, decisions=generation.decisions,
                       cursor=generation.cursor, partial=generation.partial, issued_until=generation.issued_until)

    @staticmethod
    def _exit_confirmed(process):
        return not process.is_alive() and process.exitcode == 0 and not _group_alive(process.pid)


class _ChildFailure(RuntimeError):
    pass
