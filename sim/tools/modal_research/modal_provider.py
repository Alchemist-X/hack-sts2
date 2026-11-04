"""Bounded calls to fixed, already deployed Modal worker functions."""

from __future__ import annotations

import asyncio
import json
import math
import os
import re
import signal
import subprocess
import sys
import threading
import time
from dataclasses import dataclass

from .adapters import Outcome
from .contracts import DIGEST, canonical, identifier, integer

FUNCTION_ID = re.compile(r"fu-[A-Za-z0-9]+")
CALL_ID = re.compile(r"fc-[A-Za-z0-9]+")
MAX_SEND_SECONDS = 5.0


class ProviderUncertain(RuntimeError):
    """The broker must reconcile the existing intent, never resubmit it."""


@dataclass(frozen=True)
class ModalFunctions:
    train: str
    evaluate: str

    def __post_init__(self):
        for value in (self.train, self.evaluate):
            if type(value) is not str or not FUNCTION_ID.fullmatch(value):
                raise ValueError("expected a fixed Modal Function object ID")
        if self.train == self.evaluate:
            raise ValueError("train and evaluation must have distinct functions")

    def for_job(self, kind):
        if kind == "train":
            return self.train
        if kind == "eval":
            return self.evaluate
        raise ValueError("unsupported Modal worker kind")


class SdkProcessTransport:
    """Use the deployment's Modal credentials in a short-lived SDK process."""

    def __init__(self, *, verified_binding):
        if verified_binding is not True or os.name != "posix":
            raise ValueError("verified Modal authentication and POSIX process isolation required")

    def request(self, message, seconds):
        if seconds <= 0.15:
            raise TimeoutError("provider operation has no safe send window")
        operation_end = time.monotonic() + seconds
        command = [sys.executable, "-s", "-m", __name__, "--sdk-request"]
        process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   stderr=subprocess.DEVNULL, start_new_session=True)
        try:
            stdout, _ = process.communicate(canonical(message).encode(),
                                            timeout=max(0, operation_end - time.monotonic() - 0.10))
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            try:
                process.wait(timeout=max(0, operation_end - time.monotonic()))
            except subprocess.TimeoutExpired:
                pass
            if process.stdin is not None:
                process.stdin.close()
            if process.stdout is not None:
                process.stdout.close()
            raise
        if process.returncode or len(stdout) > 24_000:
            raise ProviderUncertain("Modal SDK process did not return bounded evidence")
        return json.loads(stdout)


class ModalProvider:
    """Transport only. The caller must reserve and fence a launch before spawn."""

    def __init__(self, *, functions, transport, cost_resolver=None, materializer=None,
                 io_timeout=10.0, clock=time.time):
        if not isinstance(functions, ModalFunctions) or not callable(getattr(transport, "request", None)):
            raise ValueError("fixed Modal functions and a bounded SDK transport are required")
        if not math.isfinite(io_timeout) or not 0 < io_timeout <= 30:
            raise ValueError("invalid provider I/O timeout")
        self.functions = functions
        self.transport = transport
        self.cost_resolver = cost_resolver
        self.materializer = materializer
        self.io_timeout = io_timeout
        self.clock = clock

    def spawn(self, job, launch_token, *, send_not_after=None):
        function_id = self.functions.for_job(job.kind)
        identifier(launch_token)
        if send_not_after is None or type(send_not_after) not in (int, float) or not math.isfinite(send_not_after):
            raise ValueError("an active broker send window is required")
        now = self.clock()
        deadline = min(job.deadline, send_not_after, now + self.io_timeout, now + MAX_SEND_SECONDS)
        if deadline <= now:
            raise TimeoutError("worker deadline elapsed before submission")
        response = self._request({"operation": "spawn", "id": function_id, "token": launch_token,
                                  "deadline": deadline}, deadline - now)
        call_id = response.get("provider_id") if type(response) is dict else None
        if type(call_id) is not str or not CALL_ID.fullmatch(call_id):
            raise ProviderUncertain("Modal did not return a valid FunctionCall ID")
        return call_id

    def poll(self, provider_id):
        _call_id(provider_id)
        return _result(self._request({"operation": "poll", "id": provider_id}, self.io_timeout))

    def cancel(self, provider_id):
        _call_id(provider_id)
        if self._request({"operation": "cancel", "id": provider_id}, self.io_timeout) != {"cancelled": True}:
            raise ProviderUncertain("Modal cancellation status is uncertain")

    def cost(self, provider_id, job):
        _call_id(provider_id)
        if self.cost_resolver is None:
            return None
        result = self._callback(self.cost_resolver, provider_id, job)
        if result is None:
            return None
        identity = {"provider_id": provider_id, "job_key": job.key, "logical_key": job.logical_key,
                    "attempt": job.attempt}
        if (type(result) is not dict or set(result) != {"actual_cents", *identity}
                or any(result[key] != value for key, value in identity.items())):
            raise ProviderUncertain("billing evidence does not belong to this attempt")
        return integer(result["actual_cents"])

    def materialize(self, job, reference):
        if self.materializer is None:
            return False
        result = self._callback(self.materializer, job, reference)
        if type(result) is not bool:
            raise ProviderUncertain("artifact synchronization readiness is uncertain")
        return result

    def _request(self, message, seconds):
        try:
            return self.transport.request(message, seconds)
        except Exception:
            raise ProviderUncertain("Modal provider operation is uncertain") from None

    def _callback(self, action, *args):
        # Both callbacks are read/status or idempotent enqueue bindings. If
        # their receipt is lost, reconciliation preserves the original attempt.
        ready = threading.Event()
        result = []

        def invoke():
            try:
                result.append((True, action(*args)))
            except Exception:
                result.append((False, None))
            finally:
                ready.set()

        threading.Thread(target=invoke, daemon=True).start()
        if not ready.wait(self.io_timeout) or not result[0][0]:
            raise ProviderUncertain("provider evidence binding is uncertain")
        return result[0][1]


def _call_id(value):
    if type(value) is not str or not CALL_ID.fullmatch(value):
        raise ValueError("invalid Modal FunctionCall ID")
    return value


def _result(response):
    if type(response) is not dict:
        raise ProviderUncertain("invalid Modal worker result")
    status = response.get("status")
    if status == "running" and set(response) == {"status"}:
        return Outcome("running")
    if status == "not_admitted" and set(response) == {"status"}:
        return Outcome("failed", error="not_admitted")
    if status == "failed" and set(response) == {"status", "error"}:
        code = response["error"]
        return Outcome("failed", error=code if isinstance(code, str) and code in {
            "worker", "timeout", "preempted", "missing_artifact", "oom", "invalid_response", "seed_grant",
        } else "worker")
    if status == "succeeded" and set(response) == {"status", "output"}:
        ref = response["output"]
        if (type(ref) is dict and set(ref) == {"generation", "manifest_hash"}
                and isinstance(ref["manifest_hash"], str) and DIGEST.fullmatch(ref["manifest_hash"])):
            identifier(ref["generation"])
            return Outcome("succeeded", output=ref)
    raise ProviderUncertain("invalid Modal worker result")


async def _sdk_request(modal, client, message):
    operation, object_id = message["operation"], message["id"]
    if operation == "spawn":
        deadline = message["deadline"]
        if not time.time() < deadline:
            raise TimeoutError("SDK send authorization elapsed")
        function = modal.Function.from_id(object_id, client=client)
        if not time.time() < deadline:
            raise TimeoutError("SDK send authorization elapsed after resolving function")
        await function.hydrate.aio()
        remaining = deadline - time.time()
        if remaining <= 0:
            raise TimeoutError("SDK send authorization elapsed after hydration")
        call = await asyncio.wait_for(function.spawn.aio(message["token"]), remaining)
        return {"provider_id": call.object_id}
    call = modal.FunctionCall.from_id(_call_id(object_id), client=client)
    if operation == "cancel":
        await call.cancel.aio(terminate_containers=False)
        return {"cancelled": True}
    if operation != "poll":
        raise ValueError("unsupported provider operation")
    try:
        return await call.get.aio(timeout=0)
    except modal.exception.FunctionTimeoutError:
        return {"status": "failed", "error": "timeout"}
    except TimeoutError:
        return {"status": "running"}


def _sdk_main():
    if sys.argv[1:] != ["--sdk-request"]:
        return 2
    try:
        request = json.loads(sys.stdin.buffer.read(4096))
        import modal

        client = modal.Client.from_env()
        response = asyncio.run(_sdk_request(modal, client, request))
        data = canonical(response).encode()
        if len(data) > 24_000:
            return 1
        sys.stdout.buffer.write(data)
        return 0
    except Exception:
        return 1


if __name__ == "__main__":
    raise SystemExit(_sdk_main())
