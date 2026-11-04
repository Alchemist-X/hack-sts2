"""Pull-only Modal Sandbox transport. SQLite and credentials stay on the control host."""

from __future__ import annotations

import asyncio
import hashlib
import json
import math
import os
import re
import signal
import subprocess
import sys
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from tempfile import TemporaryDirectory
from types import SimpleNamespace

from .adapters import Outcome
from .artifact_adapter import ArtifactAdapter, ArtifactMirror, verify_bundle
from .broker import DetachedGrant, MAX_SEND_SECONDS
from .contracts import DIGEST, canonical, digest, identifier, integer
from .lineage import validate_job
from .modal_provider import ProviderUncertain, SdkProcessTransport
from .native_worker import Installation, run

MOUNT = Path("/sts2-study")
REPORT_LIMIT = 12_000
PACKET_LIMIT = 32_000
GPU = "H100!"
CPU = 16
MEMORY_MIB = 65536
MAX_RUNTIME = 4500
SDK_VERSION = "1.6.3"

BOOTSTRAP = r"""
import hashlib, json, signal, sys, time
from pathlib import Path
text, expected, bundle_hash, config_hash, cutoff = sys.argv[1:]
if time.time() >= int(cutoff) or hashlib.sha256(text.encode()).hexdigest() != expected:
    raise SystemExit('invalid or expired one-shot grant')
signal.setitimer(signal.ITIMER_REAL, int(cutoff) - time.time())
def sha(path):
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()
root = Path('/sts2-study/bundles') / config_hash
manifest = root / 'bundle.json'
if root.is_symlink() or manifest.is_symlink() or manifest.stat().st_size > 256000 or sha(manifest) != bundle_hash:
    raise SystemExit('invalid immutable bundle')
files = json.loads(manifest.read_bytes())['files']
actual = {}
for path in root.rglob('*'):
    if path.is_symlink() or (not path.is_file() and not path.is_dir()):
        raise SystemExit('unsafe bundle entry')
    if path.is_file() and path != manifest:
        actual[path.relative_to(root).as_posix()] = sha(path)
if actual != files:
    raise SystemExit('bundle member hashes differ')
sys.path.insert(0, str(root / 'code'))
from modal_research.sandbox_provider import worker
worker(text, expected, bundle_hash, int(cutoff))
"""


@dataclass(frozen=True)
class SandboxBinding:
    app_name: str
    app_id: str
    environment: str
    image_id: str
    volume_id: str
    bundle_hashes: dict[str, str]

    def __post_init__(self):
        identifier(self.app_name)
        identifier(self.environment)
        for value, prefix in ((self.app_id, "ap"), (self.image_id, "im"), (self.volume_id, "vo")):
            _object_id(value, prefix)
        if not self.bundle_hashes or any(
            not _digest(key) or not _digest(value) for key, value in self.bundle_hashes.items()
        ):
            raise ValueError("expected immutable prepared bundle bindings")


def _digest(value):
    return isinstance(value, str) and DIGEST.fullmatch(value) is not None


def _object_id(value, prefix):
    if not isinstance(value, str) or not re.fullmatch(fr"{prefix}-[A-Za-z0-9]+", value):
        raise ValueError(f"expected a fixed Modal {prefix} object ID")
    return value


def _attempt_name(launch_token):
    return "sts2-" + identifier(launch_token)


class SandboxProvider:
    """Bound transport for one named, network-blocked Sandbox per broker grant."""

    detached = True

    def __init__(self, binding, artifacts, *, verified_identity=False, transport=None, clock=time.time):
        if not isinstance(binding, SandboxBinding):
            raise ValueError("a frozen Modal Sandbox binding is required")
        self.binding = binding
        self.artifacts = Path(artifacts).resolve()
        if not self.artifacts.is_dir():
            raise ValueError("local artifact mirror must be provisioned")
        self.transport = transport if transport is not None else SdkProcessTransport(
            verified_binding=verified_identity, worker_module=__name__)
        self.clock = clock

    def supports(self, job):
        try:
            spec = validate_job(job)
            return (job.payload["config_hash"] in self.binding.bundle_hashes
                    and spec["checkpoint_policy"]["resume_mode"] == "fresh_episodes")
        except (TypeError, ValueError, KeyError, AttributeError):
            return False

    def _request(self, operation, seconds, **payload):
        return self.transport.request(
            {"operation": operation, "binding": asdict(self.binding), **payload}, seconds)

    def spawn_detached(self, grant, *, send_not_after):
        if not isinstance(grant, DetachedGrant) or not self.supports(grant.job):
            raise ValueError("unbound or invalid detached job")
        now = self.clock()
        deadline = min(send_not_after, now + MAX_SEND_SECONDS)
        cutoff = int(grant.job.deadline)
        if not math.isfinite(deadline) or cutoff - now <= MAX_SEND_SECONDS or deadline <= now:
            raise ValueError("detached job has no safe launch window")
        packet = canonical(asdict(grant))
        if not 0 < len(packet.encode()) <= PACKET_LIMIT:
            raise ValueError("detached packet exceeds the command envelope")
        response = self._request(
            "spawn", deadline - now, packet=packet, cutoff=cutoff, send_not_after=deadline,
            packet_hash=digest(asdict(grant)), bundle_hash=self.binding.bundle_hashes[grant.job.payload["config_hash"]],
            config_hash=grant.job.payload["config_hash"], name=_attempt_name(grant.launch_token),
        )
        return _object_id(response.get("sandbox_id"), "sb")

    def find_pending(self, job, launch_token, input_id):
        response = self._request("find", 8, name=_attempt_name(launch_token))
        return _object_id(response["sandbox_id"], "sb") if response.get("sandbox_id") else None

    def poll(self, provider_id):
        response = self._request("poll", 8, sandbox_id=_object_id(provider_id, "sb"))
        code = response["exit_code"]
        if code is None:
            return Outcome("running")
        if type(code) is not int:
            raise ProviderUncertain("invalid Sandbox exit code")
        return Outcome("succeeded" if code == 0 else "failed", error=None if code == 0 else "reclaimed")

    def cancel(self, provider_id):
        if self._request("cancel", 8, sandbox_id=_object_id(provider_id, "sb")) != {"cancelled": True}:
            raise ProviderUncertain("Sandbox cancellation remains uncertain")

    def collect_detached(self, grant, provider_id, provider_outcome):
        _object_id(provider_id, "sb")
        packet_hash = digest(asdict(grant))
        response = self._request("report", 15, token=grant.launch_token)
        report = response.get("report")
        if report is None:
            # Allow a bounded delay for durable Volume visibility.
            if self.clock() < grant.job.deadline + 30:
                raise ProviderUncertain("Sandbox terminal report not visible")
            return Outcome("failed", error="missing_artifact")
        required = {"packet_hash", "job_key", "launch_token", "fence", "status", "partial", "result"}
        if (type(report) is not dict or set(report) != required
                or (report["packet_hash"], report["job_key"], report["launch_token"], report["fence"])
                != (packet_hash, grant.job.key, grant.launch_token, grant.fence)
                or report["status"] not in {"running", "succeeded", "failed"}):
            raise ValueError("report differs from the frozen one-shot grant")
        if report["status"] == "succeeded" and provider_outcome.status == "succeeded":
            return Outcome("succeeded", output=report["result"])
        return Outcome("failed", output=report["partial"], error="reclaimed")

    def materialize(self, job, reference):
        response = self._request("mirror", 100, job=asdict(job), reference=reference, destination=str(self.artifacts))
        return response == {"ready": True}


class _VolumeSync:
    def commit(self):
        subprocess.run(["/usr/bin/sync", str(MOUNT)], check=True, timeout=20,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def reload(self):
        pass  # A fresh Sandbox mounts committed Volume data at startup.


def _write_report(path, report):
    temp = path.with_suffix(".pending")
    data = canonical(report).encode()
    if len(data) > REPORT_LIMIT:
        raise ValueError("detached worker report exceeds its byte bound")
    with temp.open("wb") as handle:
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    temp.replace(path)


def worker(packet, packet_hash, bundle_hash, cutoff):
    """Fixed entrypoint. It has no broker credentials and no network."""
    if not 0 < len(packet.encode()) <= PACKET_LIMIT or hashlib.sha256(packet.encode()).hexdigest() != packet_hash:
        raise ValueError("invalid detached packet")
    grant = DetachedGrant.from_json(packet)
    job = grant.job
    spec = validate_job(job)
    identifier(grant.launch_token)
    identifier(grant.receipt)
    integer(grant.fence, 1)
    if (cutoff != int(job.deadline) or time.time() >= cutoff
            or spec["checkpoint_policy"]["resume_mode"] != "fresh_episodes"):
        raise ValueError("worker deadline or recovery mode differs")
    root = MOUNT / "bundles" / job.payload["config_hash"]
    artifacts = MOUNT / "outputs"
    receipts = MOUNT / "receipts"
    receipts.mkdir(exist_ok=True)
    path = receipts / f"{grant.launch_token}.json"
    volume = _VolumeSync()
    report = {"packet_hash": packet_hash, "job_key": job.key, "launch_token": grant.launch_token,
              "fence": grant.fence, "status": "running", "partial": None, "result": None}

    def save():
        _write_report(path, report)
        volume.commit()

    # A committed marker fences a sequential re-entry. Single-instance
    # admission is the broker + named Sandbox contract, not Volume locking.
    with path.open("x", encoding="utf-8") as handle:
        handle.write(canonical(report))
        handle.flush()
        os.fsync(handle.fileno())
    volume.commit()
    try:
        with TemporaryDirectory(prefix="sts2-pull-") as work:
            installation = Installation(spec["code_commit"], spec["image_digest"], root / "lib" / "Sts2Sim.Vec.so",
                                        root / "dependencies.lock", root / "seeds", artifacts, Path(work))
            verify_bundle(installation, spec, root, bundle_hash)
            os.environ.update(STS2VEC_AOT_LIB=str(installation.engine), DOTNET_gcServer="1",
                              DOTNET_gcConcurrent="0", DOTNET_PROCESSOR_COUNT="2")
            writer = ArtifactAdapter(artifacts, volume)

            def checkpoint_ready(checkpoint):
                previous = report["partial"]
                generation = writer.training(grant, checkpoint, installation.scratch, partial=True)
                report["partial"] = generation.reference
                save()
                if previous:
                    # This path only owns its own obsolete temporary partials.
                    import shutil
                    old = artifacts / job.key / identifier(previous["generation"])
                    shutil.rmtree(old)
                    volume.commit()

            result = run(job, installation, grant.accepted_checkpoint,
                         checkpoint_ready=checkpoint_ready if job.kind == "train" else None,
                         seed_range=grant.seed_range, execution_deadline=cutoff, max_threads=CPU)
            if job.kind == "train":
                if not result["last_checkpoint"] or result["env_steps"] <= job.payload["start_decisions"]:
                    raise RuntimeError("training ended without a completed rollout")
                generation = writer.training(grant, result["last_checkpoint"], installation.scratch, partial=False)
            else:
                generation = writer.evaluation(grant, result, installation)
            report.update(status="succeeded", result=generation.reference)
            save()
    except BaseException:
        report["status"] = "failed"
        save()
        raise


def _report(volume, token):
    raw = bytearray()
    for chunk in volume.read_file(f"/receipts/{identifier(token)}.json"):
        raw.extend(chunk)
        if len(raw) > REPORT_LIMIT:
            raise ValueError("remote report exceeds bound")
    return json.loads(raw)


def _find_finished(binding, name):
    # GPU Sandboxes use V1 in the pinned SDK. Its public lookup and list
    # exclude finished attempts, so recovery uses the same SDK list RPC.
    from modal.client import _Client
    from modal_proto import api_pb2

    async def lookup():
        client = await _Client.from_env()
        response = await client._stub.SandboxList(api_pb2.SandboxListRequest(
            app_id=binding.app_id, environment_name=binding.environment, include_finished=True,
            tags=[api_pb2.SandboxTag(tag_name="sts2-attempt", tag_value=name)],
        ))
        ids = {_object_id(item.id, "sb") for item in response.sandboxes}
        if len(ids) > 1:
            raise ValueError("multiple Sandboxes match the original grant")
        return next(iter(ids), None)

    return asyncio.run(lookup())


def _sdk_request(message, modal):
    binding = SandboxBinding(**message["binding"])
    operation = message["operation"]
    if operation == "spawn":
        app = modal.App.lookup(binding.app_name, environment_name=binding.environment)
        if app.app_id != binding.app_id:
            raise ValueError("Modal app identity differs from frozen binding")
        name = identifier(message["name"])
        image = modal.Image.from_id(binding.image_id)
        volume = modal.Volume.from_id(binding.volume_id).hydrate()
        timeout = min(MAX_RUNTIME, int(message["cutoff"] - time.time()))
        if timeout < 1 or time.time() >= message["send_not_after"]:
            raise TimeoutError("final Sandbox send window expired")
        sandbox = modal.Sandbox.create(
            "/opt/conda/bin/python", "-I", "-B", "-u", "-c", BOOTSTRAP,
            message["packet"], message["packet_hash"], message["bundle_hash"], message["config_hash"],
            str(message["cutoff"]), app=app, image=image, name=name, tags={"sts2-attempt": name},
            runtime="gvisor", timeout=timeout, gpu=GPU, cpu=(CPU, CPU), memory=(MEMORY_MIB, MEMORY_MIB),
            block_network=True, volumes={str(MOUNT): volume},
            env={"OMP_NUM_THREADS": "1", "MKL_NUM_THREADS": "1", "OPENBLAS_NUM_THREADS": "1"},
        )
        return {"sandbox_id": _object_id(sandbox.object_id, "sb")}
    if operation == "find":
        name = identifier(message["name"])
        try:
            sandbox = modal.Sandbox.from_name(binding.app_name, name, environment_name=binding.environment)
        except modal.exception.NotFoundError:
            return {"sandbox_id": _find_finished(binding, name)}
        return {"sandbox_id": _object_id(sandbox.object_id, "sb")}
    if operation in {"poll", "cancel"}:
        sandbox = modal.Sandbox.from_id(_object_id(message["sandbox_id"], "sb"))
        if operation == "poll":
            return {"exit_code": sandbox.poll()}
        sandbox.terminate()
        return {"cancelled": True}
    volume = modal.Volume.from_id(binding.volume_id).hydrate()
    if operation == "report":
        try:
            return {"report": _report(volume, message["token"])}
        except (FileNotFoundError, modal.exception.NotFoundError):
            return {"report": None}
    if operation == "mirror":
        from .adapters import Job

        job = Job(**message["job"])
        adapter = SimpleNamespace(read_file=lambda path: volume.read_file("/outputs/" + path))
        return {"ready": ArtifactMirror(message["destination"], adapter).copy(job, message["reference"])}
    raise ValueError("unsupported Sandbox SDK operation")


def _sdk_main():
    import modal

    if modal.__version__ != SDK_VERSION:
        raise RuntimeError("Sandbox transport requires the audited SDK version")
    payload = sys.stdin.buffer.read(256_000)
    print(canonical(_sdk_request(json.loads(payload), modal)))


if __name__ == "__main__" and sys.argv[1:] == ["--sdk-request"]:
    _sdk_main()
