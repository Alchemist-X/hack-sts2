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
from pathlib import Path, PurePosixPath
from tempfile import NamedTemporaryFile, TemporaryDirectory
from types import SimpleNamespace

from .adapters import Outcome, ProviderIntegrityError, ProviderScopeError
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
LOOP_ACCEPTANCE_EXPERIMENT = "silent-a10-pull-loop-acceptance"

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
    allowed_job_keys: tuple[str, ...] | None = None
    max_target_by_config: dict[str, int] | None = None
    max_reserved_cents_by_config: dict[str, int] | None = None

    def __post_init__(self):
        identifier(self.app_name)
        identifier(self.environment)
        for value, prefix in ((self.app_id, "ap"), (self.image_id, "im"), (self.volume_id, "vo")):
            _object_id(value, prefix)
        if not self.bundle_hashes or any(
            not _digest(key) or not _digest(value) for key, value in self.bundle_hashes.items()
        ):
            raise ValueError("expected immutable prepared bundle bindings")
        if self.allowed_job_keys is not None:
            if (not isinstance(self.allowed_job_keys, (tuple, list)) or len(self.allowed_job_keys) != 4
                    or len(set(self.allowed_job_keys)) != 4):
                raise ValueError("GPU loop acceptance requires four fixed job keys")
            for key in self.allowed_job_keys:
                identifier(key)
        for limits in (self.max_target_by_config, self.max_reserved_cents_by_config):
            if limits is None:
                continue
            if type(limits) is not dict or limits.keys() != self.bundle_hashes.keys():
                raise ValueError("authorized tranche limits must cover each frozen configuration")
            for limit in limits.values():
                integer(limit, 1)


def _digest(value):
    return isinstance(value, str) and DIGEST.fullmatch(value) is not None


def _object_id(value, prefix):
    if not isinstance(value, str) or not re.fullmatch(fr"{prefix}-[A-Za-z0-9]+", value):
        raise ValueError(f"expected a fixed Modal {prefix} object ID")
    return value


def generation_bytes(job):
    return integer(job.payload["spec"]["actual_param_count"], 1) * 32 + 65 * 1024**2


def _attempt_name(launch_token):
    return "sts2-" + identifier(launch_token)


class SandboxProvider:
    """Bound transport for one named, network-blocked Sandbox per broker grant."""

    detached = True
    tracks_late_cost = False  # The starter binding lacks a per-Sandbox invoice resolver.

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
            acceptance = spec["experiment_id"] == LOOP_ACCEPTANCE_EXPERIMENT
            exact = self.binding.allowed_job_keys
            config_hash = job.payload["config_hash"]
            targets = self.binding.max_target_by_config
            return (config_hash in self.binding.bundle_hashes
                    and (targets is None or job.payload["target_decisions"] <= targets[config_hash])
                    and spec["checkpoint_policy"]["resume_mode"] == "fresh_episodes"
                    and (not acceptance if exact is None else acceptance and job.key in exact and job.attempt == 1))
        except (TypeError, ValueError, KeyError, AttributeError):
            return False

    def _request(self, operation, seconds, **payload):
        response = self.transport.request(
            {"operation": operation, "binding": asdict(self.binding), **payload}, seconds)
        issue = response.get("provider_access_issue")
        if issue == "integrity":
            raise ProviderIntegrityError("multiple Sandboxes match one attempt")
        if issue in {"authentication", "quota"}:
            raise ProviderScopeError(issue)
        return response

    def workspace_metering(self, workspace):
        return self._request("workspace_metering", 12, workspace=identifier(workspace))

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
        return Outcome("succeeded" if code == 0 else "failed", error=None if code == 0 else "sandbox_nonzero_exit")

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
        required = {"packet_hash", "job_key", "launch_token", "fence", "status", "partial", "result", "candidate"}
        if (type(report) is not dict or set(report) != required
                or (report["packet_hash"], report["job_key"], report["launch_token"], report["fence"])
                != (packet_hash, grant.job.key, grant.launch_token, grant.fence)
                or report["status"] not in {"running", "succeeded", "failed"}):
            raise ValueError("report differs from the frozen one-shot grant")
        local = self.artifacts / "_receipts"
        local.mkdir(mode=0o700, exist_ok=True)
        if local.is_symlink():
            raise ValueError("local receipt storage must not be a symlink")
        _write_report(local / f"{grant.launch_token}.json", report)
        if report["status"] == "succeeded" and provider_outcome.status == "succeeded":
            return Outcome("succeeded", output=report["result"])
        partial = report["partial"]
        candidate = report["candidate"]
        if grant.job.kind == "train":
            latest = report["result"] if report["status"] == "succeeded" else None
            if candidate:
                latest = candidate["reference"]
            if latest and self._request("ready", 15, job_key=grant.job.key, reference=latest) == {"ready": True}:
                partial = latest
        return Outcome("failed", output=partial, error="worker" if report["status"] == "failed" else "reclaimed")

    def prune(self, job_key, keep):
        response = self._request("prune", 15, job_key=identifier(job_key),
                                 keep=identifier(keep) if keep else None)
        return response == {"pruned": True}

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
    data = canonical(report).encode()
    if len(data) > REPORT_LIMIT:
        raise ValueError("detached worker report exceeds its byte bound")
    with NamedTemporaryFile(dir=path.parent, prefix=".receipt-", delete=False) as handle:
        temp = Path(handle.name)
        handle.write(data)
        handle.flush()
        os.fsync(handle.fileno())
    try:
        temp.replace(path)
    finally:
        temp.unlink(missing_ok=True)


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
    artifacts.mkdir(exist_ok=True)
    receipts = MOUNT / "receipts"
    receipts.mkdir(exist_ok=True)
    path = receipts / f"{grant.launch_token}.json"
    volume = _VolumeSync()
    report = {"packet_hash": packet_hash, "job_key": job.key, "launch_token": grant.launch_token,
              "fence": grant.fence, "status": "running", "partial": None, "result": None, "candidate": None}

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
            def announce(reference, metadata):
                report["candidate"] = {"reference": reference, "partial": metadata["termination"] == "checkpoint"}
                save()

            writer = ArtifactAdapter(artifacts, volume, before_ready=announce,
                                     max_generation_bytes=generation_bytes(job))

            def checkpoint_ready(checkpoint):
                previous = report["partial"]
                generation = writer.training(grant, checkpoint, installation.scratch, partial=True)
                report.update(partial=generation.reference, candidate=None)
                save()
                if previous:
                    # This path only owns its own obsolete temporary partials.
                    import shutil
                    old = artifacts / job.key / identifier(previous["generation"])
                    shutil.rmtree(old)
                    volume.commit()

            tiny_segment = (job.kind == "train" and spec["experiment_id"] == LOOP_ACCEPTANCE_EXPERIMENT)
            result = run(job, installation, grant.accepted_checkpoint,
                         checkpoint_ready=checkpoint_ready if job.kind == "train" else None,
                         seed_range=grant.seed_range, execution_deadline=cutoff, max_threads=CPU,
                         one_update_segment=tiny_segment)
            if job.kind == "train":
                if not result["last_checkpoint"] or result["env_steps"] <= job.payload["start_decisions"]:
                    raise RuntimeError("training ended without a completed rollout")
                generation = writer.training(grant, result["last_checkpoint"], installation.scratch, partial=False)
            else:
                generation = writer.evaluation(grant, result, installation)
            report.update(status="succeeded", result=generation.reference, candidate=None)
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
            raise ProviderIntegrityError("multiple Sandboxes match the original grant")
        return next(iter(ids), None)

    return asyncio.run(lookup())


def _sdk_request(message, modal):
    binding = SandboxBinding(**message["binding"])
    operation = message["operation"]
    if operation == "workspace_metering":
        workspace = modal.Workspace.from_context().hydrate()
        if workspace.name != message["workspace"]:
            raise ProviderScopeError("authentication")
        summary = workspace.billing.summary()
        rates = workspace.billing.rates()
        keys = ("gpu_hour_cost_h100", "cpu_hour_cost_sandbox", "mem_gib_hour_cost_sandbox",
                "volume_storage_gib_month_cost")
        return {"workspace": workspace.name, "gross_used": str(summary.metered_cost),
                "net_spent": str(summary.billed_cost), "cycle_start": summary.start.date().isoformat(),
                "observed_at": time.time(), "rates": {key: str(rates[key]) for key in keys}}
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
        return {"sandbox_id": _find_finished(binding, name)}
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
    if operation == "ready":
        reference = message["reference"]
        generation = identifier(reference["generation"])
        if not _digest(reference["manifest_hash"]):
            raise ValueError("invalid pending manifest digest")
        path = f"/outputs/{identifier(message['job_key'])}/{generation}/ready.json"
        checksum = hashlib.sha256()
        size = 0
        try:
            for part in volume.read_file(path):
                size += len(part)
                if size > 32_000:
                    raise ValueError("ready manifest exceeds bound")
                checksum.update(part)
        except (FileNotFoundError, modal.exception.NotFoundError):
            return {"ready": False}
        return {"ready": checksum.hexdigest() == reference["manifest_hash"]}
    if operation == "prune":
        job_path = PurePosixPath("/outputs") / identifier(message["job_key"])
        keep = identifier(message["keep"]) if message["keep"] else None
        try:
            if keep is None:
                volume.remove_file(str(job_path), recursive=True)
            else:
                for entry in volume.iterdir(str(job_path), recursive=False):
                    path = PurePosixPath("/") / entry.path.lstrip("/")
                    if path.parent != job_path:
                        raise ValueError("unexpected job directory listing")
                    generation = identifier(path.name)
                    if generation != keep:
                        volume.remove_file(str(path), recursive=True)
        except (FileNotFoundError, modal.exception.NotFoundError):
            pass
        return {"pruned": True}
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
    try:
        response = _sdk_request(json.loads(payload), modal)
    except (ProviderScopeError, modal.exception.AuthError, modal.exception.PermissionDeniedError):
        response = {"provider_access_issue": "authentication"}
    except modal.exception.ResourceExhaustedError as error:
        if not re.search(r"quota|workspace.{0,30}limit|rate.limit|concurrent gpu", str(error), re.IGNORECASE):
            raise
        response = {"provider_access_issue": "quota"}
    except ProviderIntegrityError:
        response = {"provider_access_issue": "integrity"}
    print(canonical(response))


if __name__ == "__main__" and sys.argv[1:] == ["--sdk-request"]:
    _sdk_main()
