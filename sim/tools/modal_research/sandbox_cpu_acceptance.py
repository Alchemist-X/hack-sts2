"""Real CPU-only fault-injection driver for a separate Raven acceptance ledger."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
from pathlib import Path

from .adapters import Outcome
from .broker import RavenBroker, RavenRuntime
from .contracts import canonical, digest
from .fixture import spec
from .lineage import artifact_identity
from .modal_provider import SdkProcessTransport
from .sandbox_provider import MOUNT, SDK_VERSION, SandboxBinding, SandboxProvider, _sdk_request
from .store import Store

MODULE_NAME = "modal_research.sandbox_cpu_acceptance"

CPU_CODE = r"""
import hashlib, json, os, pathlib, subprocess, sys, time
root = pathlib.Path('/sts2-study')
job, identity, tag, fence, packet_hash, begin, scenario, ancestor, cutoff = json.loads(sys.argv[1])
crash = scenario == "crash"
if time.time() >= cutoff:
    raise SystemExit(124)
def canonical(obj): return json.dumps(obj, sort_keys=True, separators=(',', ':')).encode()
def sync(): subprocess.run(['/usr/bin/sync', str(root)], check=True, timeout=10)
if ancestor:
    origin = root / 'outputs' / ancestor['job_key'] / ancestor['generation']
    marker = (origin / 'ready.json').read_bytes()
    if hashlib.sha256(marker).hexdigest() != ancestor['manifest_hash']:
        raise SystemExit('ancestor ready hash mismatch')
    for member in json.loads(marker)['files'].values():
        if hashlib.sha256((origin / member['name']).read_bytes()).hexdigest() != member['sha256']:
            raise SystemExit('ancestor payload hash mismatch')
receipts = root / 'receipts'
receipts.mkdir(exist_ok=True)
receipt = receipts / (tag + '.json')
report = {'job_key': job, 'launch_token': tag, 'fence': fence, 'packet_hash': packet_hash,
          'status': 'running', 'partial': None, 'result': None, 'candidate': None}
with receipt.open('xb') as f:
    f.write(canonical(report))
sync()
if scenario == "stop":
    time.sleep(max(0, cutoff - time.time()))
    raise SystemExit(124)
name = 'acceptance-generation'
folder = root / 'outputs' / job / name
folder.mkdir(parents=True)
files = {}
for role in ('model','optimizer','scheduler','rng','seed_cursor','metrics'):
    data = canonical({'probe_only': True, 'role': role})
    filename = role + '.json'
    (folder / filename).write_bytes(data)
    files[role] = {'name': filename, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()}
metadata = {'decisions': 5 if crash else 10, 'cursor': begin + 1, 'updates': 1,
            'termination': 'checkpoint' if crash else 'milestone', 'resume_mode': 'fresh_episodes',
             'seed_issued_until': begin + 1}
marker = canonical({'version':1,'identity':identity,'metadata':metadata,'files':files})
ref = {'generation':name,'manifest_hash':hashlib.sha256(marker).hexdigest()}
report['candidate'] = {'reference':ref, 'partial':crash}
pending = receipt.with_suffix('.tmp')
pending.write_bytes(canonical(report))
pending.replace(receipt)
sync()
(folder / 'ready.json').write_bytes(marker)
sync()
if crash:
    os._exit(77)  # kill point after ready but before replacing the candidate
report.update(status='succeeded', result=ref, candidate=None)
pending.write_bytes(canonical(report))
pending.replace(receipt)
sync()
"""


class CpuProvider(SandboxProvider):
    def __init__(self, binding, artifacts, marker, scenario, operation_deadline, **kwargs):
        super().__init__(binding, artifacts, transport=SdkProcessTransport(
            verified_binding=True, worker_module=MODULE_NAME), **kwargs)
        self.marker = marker
        self.scenario = scenario
        self.operation_deadline = operation_deadline

    def _request(self, operation, seconds, **payload):
        return super()._request(operation, min(seconds, self.operation_deadline - time.time() - 0.1), **payload)

    def spawn_detached(self, grant, *, send_not_after):
        from dataclasses import asdict

        first = self.scenario == "recover" and grant.job.attempt == 1
        scenario = "crash" if first else ("stop" if self.scenario == "stop" else "success")
        seconds = min(send_not_after - time.time(), 5)
        packet_hash = digest(asdict(grant))
        work = [grant.job.key, artifact_identity(grant.job), grant.launch_token,
                grant.fence, packet_hash, grant.seed_range[0], scenario,
                grant.accepted_checkpoint.reference if grant.accepted_checkpoint else None, int(grant.job.deadline)]
        response = self._request("cpu-probe-spawn", seconds, work=work,
                                 name=f"sts2-{grant.launch_token}", send_not_after=send_not_after)
        if first:
            with self.marker.open("x", encoding="utf-8") as handle:
                handle.write(canonical({"provider_id": response["sandbox_id"], "grant_hash": packet_hash}))
                handle.flush()
                os.fsync(handle.fileno())
            os._exit(77)  # deliberately skip broker receipt attachment
        return response["sandbox_id"]


def _cpu_sdk():
    message = json.loads(sys.stdin.buffer.read(256_000))
    if message == {"operation": "local-module-check"}:
        print(canonical({"module": __spec__.name}))
        return
    import modal

    if modal.__version__ != SDK_VERSION or os.environ.get("MODAL_SANDBOX_V2") != "0":
        raise RuntimeError("CPU acceptance requires the pinned V1 transport")
    if message["operation"] != "cpu-probe-spawn":
        print(canonical(_sdk_request(message, modal)))
        return
    binding = SandboxBinding(**message["binding"])
    app = modal.App.lookup(binding.app_name, environment_name=binding.environment)
    if app.app_id != binding.app_id:
        raise ValueError("CPU acceptance App differs from the frozen binding")
    work = message["work"]
    timeout = min(120, int(work[-1] - time.time()))
    volume = modal.Volume.from_id(binding.volume_id).hydrate()
    if timeout < 1 or time.time() >= message["send_not_after"]:
        raise TimeoutError("CPU acceptance send cutoff")
    name = message["name"]
    sandbox = modal.Sandbox.create(
        "/opt/conda/bin/python", "-I", "-B", "-c", CPU_CODE, canonical(work),
        app=app, image=modal.Image.from_id(binding.image_id), name=name, tags={"sts2-attempt": name},
        timeout=timeout, runtime="gvisor", cpu=(1, 1), memory=(512, 512), block_network=True,
        volumes={str(MOUNT): volume},
    )
    print(canonical({"sandbox_id": sandbox.object_id}))


def tick(root, binding, scenario, operation_deadline):
    root = Path(root)
    state = root / "acceptance.sqlite3"
    artifacts = root / "artifacts"
    artifacts.mkdir(exist_ok=True)
    store = Store(state, f"cpu-pull-acceptance-{scenario}")
    if operation_deadline - time.time() <= 10:
        print(canonical(store.status()))
        return
    frozen = spec(operation_deadline, experiment_id=f"cpu-acceptance-{scenario}")
    frozen.update(eval_protocol="silent-a10-smoke-v1", max_cost="0.10", absolute_deadline=operation_deadline)
    frozen["resource_request"].update(train_cost="0.03", eval_cost="0.03", segment_seconds=120)
    config_hash = store.register(frozen)
    config = SandboxBinding(**binding, bundle_hashes={config_hash: "1" * 64})
    with store.transaction() as db:
        initialized = db.execute("SELECT approved_budget FROM study").fetchone()[0]
    if initialized is None:
        store.approve("0.10", "cpu-sandbox-acceptance")
        store.record_platform(gross_limit="0.10", net_limit="0.10", credit="0", gross_used="0", net_spent="0",
                              reference="acceptance-scoped-limit")
        with store.transaction() as db:
            db.execute("UPDATE experiments SET stage='needs_train',target=10")
    provider = CpuProvider(config, artifacts, root / "lost-provider-receipt.json", scenario, operation_deadline)
    maximum_attempts = 2 if scenario == "recover" else 1
    allowed = lambda job: job.kind == "train" and job.attempt <= maximum_attempts and job.reserve_cents == 3
    broker = RavenBroker(store, artifacts, authorize_spawn=allowed, reserve_bound=lambda job: 3)
    if scenario == "stop":
        with store.transaction() as db:
            row = db.execute("SELECT provider_id,launch_token FROM broker_jobs WHERE state='submitted'").fetchone()
        if row:
            report = provider._request("report", 8, token=row["launch_token"]).get("report")
            if report and report["status"] == "running" and report["launch_token"] == row["launch_token"]:
                store.stop("acceptance stop after committed worker start")
    RavenRuntime(broker, modal_provider=provider).tick_once()
    print(canonical(store.status()))


def main():
    if sys.argv[1:] == ["--check-module"]:
        transport = SdkProcessTransport(verified_binding=True, worker_module=MODULE_NAME)
        if transport.request({"operation": "local-module-check"}, 5) != {"module": MODULE_NAME}:
            raise ValueError("unexpected CPU SDK subprocess module")
        print(MODULE_NAME)
        return
    if sys.argv[1:] == ["--sdk-request"]:
        _cpu_sdk()
        return
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=Path)
    parser.add_argument("--binding", required=True, type=Path)
    parser.add_argument("--scenario", choices=("recover", "stop"), default="recover")
    parser.add_argument("--deadline", type=int, required=True)
    args = parser.parse_args()
    tick(args.root, json.loads(args.binding.read_bytes()), args.scenario, args.deadline)


if __name__ == "__main__":
    main()
