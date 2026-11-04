"""Single-attempt Modal engineering smoke. The local CLI imports no trainer or torch."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import time
from dataclasses import asdict
from pathlib import Path
from types import SimpleNamespace

from .contracts import MAIN_EVAL, PURE_TRAINING, canonical, digest, identifier, validate_spec
from .manifests import file_digest

TASK = "modal-gpu-engineering-smoke-20261104-020"
REGISTRY_DIGEST = "c16f4c749e2d9e96878875cdf6cc45cddda1d1a36fddd371dd6f2360f1b6e2a2"
REGISTRY = f"pytorch/pytorch@sha256:{REGISTRY_DIGEST}"
ENGINE_HASH = "2d035a9be243fd40b9cef407d31fb46c12b6ca42ff8e39a6f95f15042fa984ce"
SCHEMA_HASH = "caddf802fff5f221bca659831c148ea7e5b268e014bcbe2b4e517d3e3bb98f96"
INDEX_HASH = "b2a24905597ccde87f079063a496b9e8d7cb7e4f60e1bb3c2a7d1fee5eeb30ed"
ALLOWLIST_HASH = "cfcdcaf7f9dd0eafb27317456d84856dc74e0e0769b539893ee096f8089ac236"
PROFILE = "sts2-training-30"
GPU_SECONDS = 300
MOUNT = Path("/sts2-smoke")
LIBRARY = "lib/Sts2Sim.Vec.so"
RESEARCH = ("__init__", "adapters", "artifact_adapter", "contracts", "lineage",
            "manifests", "native_worker", "sandbox_smoke")
MODEL = {"d": 192, "s": 384, "depth": 3, "heads": 6, "ff_mult": 4,
         "deck_counts": True, "aux_heads": True}
PPO = {"epochs": 1, "minibatch": 32, "max_rows": 192, "step_cap": 1200, "floor_step_cap": 80,
       "lr": 0.0003, "gamma": 1, "lam": 0.99, "clip": 0.2, "vf_coef": 0.5, "ent_coef": 0.02,
       "max_grad_norm": 0.5, "win_bonus": 1, "acq_deck_coef": 0, "acq_relic_coef": 0,
       "acq_upgrade_coef": 0, "cycle_cost": 0, "aux_coef": 0.1, "amp": True, "grad_ckpt": False,
       "acq_shaping": False, "aux_bootstrap": True, "objective": "protocol", "compile": "off"}

# The entrypoint is installed in the Sandbox request, outside the mounted files it verifies.
BOOTSTRAP = r"""
import hashlib, json, os, signal, sys, time
from pathlib import Path
mount = Path('/sts2-smoke')
packet = mount / 'packet.json'
expected, cutoff = sys.argv[1], int(sys.argv[2])
if time.time() >= cutoff:
    raise SystemExit('worker admission deadline elapsed')
signal.setitimer(signal.ITIMER_REAL, cutoff-time.time())
def sha(path):
    with path.open('rb') as handle:
        return hashlib.file_digest(handle, 'sha256').hexdigest()
if packet.is_symlink() or packet.stat().st_size > 16384 or sha(packet) != expected:
    raise SystemExit('worker packet failed hash verification')
spec = json.loads(packet.read_bytes())
root = mount / 'bundle'
manifest = root / 'bundle.json'
if root.is_symlink() or manifest.is_symlink() or manifest.stat().st_size > 64000 or sha(manifest) != spec['bundle_hash']:
    raise SystemExit('worker bundle failed hash verification')
files = json.loads(manifest.read_bytes())['files']
actual = {}
for path in root.rglob('*'):
    if path.is_symlink() or (not path.is_file() and not path.is_dir()):
        raise SystemExit('unsafe bundle entry')
    if path.is_file() and path != manifest:
        actual[path.relative_to(root).as_posix()] = sha(path)
if actual != files:
    raise SystemExit('worker bundle member verification failed')
sys.path.insert(0, str(root / 'code'))
from modal_research.sandbox_smoke import worker
worker(spec, cutoff, expected)
"""


def _atomic(path, data):
    path = Path(path)
    temp = path.with_name(path.name + ".pending")
    with temp.open("w", encoding="utf-8") as handle:
        handle.write(canonical(data) + "\n")
        handle.flush()
        os.fsync(handle.fileno())
    temp.replace(path)
    directory = os.open(path.parent, os.O_RDONLY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def _git(root, *args):
    return subprocess.check_output(["git", "-C", str(root), *args], timeout=10)


def _write_member(bundle, destination, content):
    target = bundle / destination
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(content)


def prepare(root, inputs, engine, out, reservation):
    root, inputs, out = Path(root), Path(inputs), Path(out)
    index_path = inputs / "seed-inputs" / "manifest-index.json"
    if file_digest(index_path) != INDEX_HASH or file_digest(engine) != ENGINE_HASH:
        raise ValueError("smoke seed index or verified native library differs")
    index = json.loads(index_path.read_bytes())
    allowlist = inputs / "package-allowlist.json"
    if file_digest(allowlist) != ALLOWLIST_HASH:
        raise ValueError("audited source allowlist differs")
    allow = json.loads(allowlist.read_bytes())
    commit = _git(root, "rev-parse", "HEAD").decode().strip()
    allowed_sources = allow["source_files"]
    for source, sha in allowed_sources.items():
        parts = Path(source).parts
        if (len(parts) != 5 or parts[:2] != ("sim", "tools") or
                (parts[2], parts[3]) not in {("train", "sts2train"), ("vec", "sts2vec")}
                or not parts[4].endswith(".py")):
            raise ValueError("non-source input in package allowlist")
        if hashlib.sha256(_git(root, "show", f"{commit}:{source}")).hexdigest() != sha:
            raise ValueError("audited source bytes differ from current commit")
    out.mkdir(mode=0o700, parents=False, exist_ok=False)
    bundle = out / "bundle"
    for source in allowed_sources:
        _write_member(bundle, "code/" + "/".join(Path(source).parts[-2:]),
                      _git(root, "show", f"{commit}:{source}"))
    for name in RESEARCH:
        source = f"sim/tools/modal_research/{name}.py"
        _write_member(bundle, f"code/modal_research/{name}.py", _git(root, "show", f"{commit}:{source}"))
    for role, meta in index["manifests"].items():
        sha = meta["file_sha256"]
        expected_path = Path("sealed" if role == "blind" else role) / f"{sha}.json"
        seed = index_path.parent / expected_path
        if meta["file"] != expected_path.as_posix() or file_digest(seed) != sha:
            raise ValueError("smoke seed manifest differs")
        _write_member(bundle, f"seeds/{sha}.json", seed.read_bytes())
    (bundle / LIBRARY).parent.mkdir()
    shutil.copyfile(engine, bundle / LIBRARY)
    _atomic(bundle / "dependencies.lock", {
        "kind": "immutable_engineering_oci", "linux_amd64_image": REGISTRY,
        "expected_torch": "2.7.1", "expected_cuda": "12.8",
    })
    _atomic(bundle / "bundle.json", {
        "version": 1, "code_commit": commit, "image_digest": REGISTRY_DIGEST,
        "files": {path.relative_to(bundle).as_posix(): file_digest(path)
                  for path in sorted(bundle.rglob("*")) if path.is_file()},
    })
    packet = {"task": TASK, "reservation": identifier(reservation), "code_commit": commit,
              "bundle_hash": file_digest(bundle / "bundle.json"), "seed_index_hash": INDEX_HASH,
              "seeds": index["registration_fields"], "image_digest": REGISTRY_DIGEST,
              "dependency_hash": file_digest(bundle / "dependencies.lock")}
    _atomic(out / "packet.json", packet)
    validate_package(out, packet)
    return packet


def _installation(root, packet, artifacts, scratch):
    from .native_worker import Installation

    return Installation(packet["code_commit"], REGISTRY_DIGEST, root / LIBRARY,
                        root / "dependencies.lock", root / "seeds", artifacts, scratch)


def validate_package(out, packet):
    from .artifact_adapter import verify_bundle

    if (packet["task"], packet["image_digest"], packet["seed_index_hash"]) != (TASK, REGISTRY_DIGEST, INDEX_HASH):
        raise ValueError("unknown engineering smoke packet")
    root = out / "bundle"
    install = _installation(root, packet, out / "output", out / "scratch")
    verify_bundle(install, _spec(packet, 1, 1), root, packet["bundle_hash"])


def _spec(packet, params, deadline):
    spec = {
        "experiment_id": "silent-a10-gpu-engineering-s", "parent_id": None, "control_id": None,
        "hypothesis": "Engineering smoke validates fresh update, durable resume and dev evaluation.",
        "changed_factor": "engineering wiring", "character": "SILENT", "ascension": 10,
        "data_lineage": "autonomous-fresh", "code_commit": packet["code_commit"],
        "image_digest": REGISTRY_DIGEST, "engine_hash": ENGINE_HASH, "schema_hash": SCHEMA_HASH,
        "dependency_hash": packet["dependency_hash"],
        "model_config": MODEL, "training_protocol": PURE_TRAINING, "evaluation_policy": MAIN_EVAL,
        "rollout_shape": {"envs": 8, "rollout": 8}, "ppo_config": PPO, "actual_param_count": params,
        "reward_version": "protocol-progress-win1", "action_protocol": "native-abi2-ported-only",
        "init_seed": 218537741, "policy_seed": 618293491, **packet["seeds"],
        "cumulative_decision_target": 128, "evaluation_milestones": [64, 128],
        "resource_request": {"train_cost": "0.50", "eval_cost": "0.50", "segment_seconds": 100,
                             "eval_seconds": 100},
        "max_cost": "2.00", "absolute_deadline": deadline,
        "checkpoint_policy": {"interval_seconds": 120, "resume_mode": "fresh_episodes"},
        "stop_rules": "Stop on stage failure or absolute deadline.",
        "promotion_rules": "Engineering smoke is ineligible for capacity promotion.",
    }
    if (spec["eval_protocol"], spec["dev_seed_count"], spec["blind_seed_count"]) != ("silent-a10-smoke-v1", 4, 4):
        raise ValueError("independent smoke seed protocol required")
    validate_spec(spec)
    return spec


def _worker_job(packet, spec, suffix, kind, start, target, cursor, checkpoint, deadline):
    from .adapters import Job

    key = f"{packet['reservation']}-{suffix}"
    return Job(key, key, 1, kind, deadline, 200, {
        "study_id": TASK, "spec": spec, "config_hash": digest(spec), "checkpoint": checkpoint,
        "start_decisions": start, "target_decisions": target, "start_cursor": cursor,
    })


class _VolumeSync:
    def commit(self):
        subprocess.run(["/usr/bin/sync", str(MOUNT)], check=True, timeout=15,
                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def reload(self):
        pass  # A single Sandbox is the only writer and consumer of its newly published generations.


def _runtime(installation):
    import importlib.metadata
    import platform
    import numpy
    import torch
    from sts2train.features import ModelSpec
    from sts2train.model import Policy, param_count
    from sts2vec import load, schema_of

    if (sys.version_info < (3, 11) or platform.machine() != "x86_64"
            or torch.__version__.split("+")[0] != "2.7.1" or torch.version.cuda != "12.8"
            or torch.cuda.device_count() != 1 or "H100" not in torch.cuda.get_device_name(0)):
        raise RuntimeError("smoke runtime image, CUDA or GPU identity mismatch")
    lib = load(installation.engine)
    if hashlib.sha256(lib.schema_json().encode()).hexdigest() != SCHEMA_HASH:
        raise ValueError("native runtime schema differs from CPU validation")
    counts = param_count(Policy(ModelSpec.from_schema(schema_of(lib)), **MODEL))
    packages = {}
    for name in ("torch", "numpy"):
        dist = importlib.metadata.distribution(name)
        record = dist.read_text("RECORD")
        packages[name] = {"version": dist.version,
                          "installed_record_sha256": hashlib.sha256(record.encode()).hexdigest() if record else None}
    return counts, {"python": platform.python_version(), "libc": list(platform.libc_ver()),
                    "packages": packages, "numpy_import_version": numpy.__version__,
                    "torch_import_version": torch.__version__, "cuda": torch.version.cuda,
                    "cudnn": torch.backends.cudnn.version(), "device": torch.cuda.get_device_name(0)}


def worker(packet, cutoff, packet_hash):
    """Process entrypoint. The immutable Sandbox command already verified the bundle bytes."""
    from tempfile import TemporaryDirectory
    from .artifact_adapter import ArtifactAdapter
    from .lineage import AcceptedCheckpoint, artifact_identity
    from .native_worker import run

    if time.time() >= cutoff:
        raise TimeoutError("worker admission deadline elapsed")
    validate_package(MOUNT, packet)
    output = MOUNT / "output"
    output.mkdir(exist_ok=False)
    volume = _VolumeSync()
    report = {"task": TASK, "reservation": packet["reservation"], "packet_hash": packet_hash,
              "result_label": "engineering_smoke", "status": "running", "stage": "runtime",
              "blind_evaluation_count": 0, "actual_cost_usd": None,
              "gpu_lifetime_seconds": GPU_SECONDS, "worker_deadline": cutoff, "evidence": []}

    def save():
        _atomic(output / "result.json", report)
        volume.commit()

    try:
        save()
        with TemporaryDirectory(prefix="sts2-smoke-") as work:
            installation = _installation(MOUNT / "bundle", packet, output, Path(work))
            os.environ.update(STS2VEC_AOT_LIB=str(installation.engine), DOTNET_gcServer="1",
                              DOTNET_gcConcurrent="0", DOTNET_PROCESSOR_COUNT="2")
            params, runtime = _runtime(installation)
            spec = _spec(packet, params, cutoff)
            report.update(spec=spec, config_hash=digest(spec), runtime=runtime)
            artifacts = ArtifactAdapter(output, volume)
            accepted = reference = None
            cursor = issued = 0
            for number in (1, 2):
                report["stage"] = "train_fresh" if number == 1 else "train_resume"
                save()
                start, target = (number - 1) * 64, number * 64
                deadline = min(cutoff - 45, time.time() + 100)
                job = _worker_job(packet, spec, f"train-{number}", "train", start, target, cursor, reference, deadline)
                grant = (issued, issued + 1024)
                result = run(job, installation, accepted, checkpoint_ready=lambda path: None,
                             seed_range=grant, execution_deadline=deadline, max_threads=8)
                if result["env_steps"] != target or result["session_updates"] != 1 or not result["last_checkpoint"]:
                    raise RuntimeError("required engineering PPO update did not complete")
                final = artifacts.training(SimpleNamespace(job=job, seed_range=grant),
                                           result["last_checkpoint"], installation.scratch, partial=False)
                if final.decisions != target:
                    raise ValueError("published checkpoint decision count disagrees with PPO")
                reference = {"job_key": job.key, **final.reference}
                accepted = AcceptedCheckpoint(TASK, spec["experiment_id"], digest(spec), reference, artifact_identity(job))
                cursor, issued = final.cursor, final.issued_until
                report["evidence"].append({"job": asdict(job), "generation": asdict(final),
                                           "weights_hash": result["weights_hash"], "session_updates": 1})
                save()
            report["stage"] = "dev"
            save()
            job = _worker_job(packet, spec, "dev", "eval", 128, 128, cursor, reference, cutoff)
            result = run(job, installation, accepted, execution_deadline=cutoff, max_threads=8)
            final = artifacts.evaluation(SimpleNamespace(job=job), result, installation)
            report["evidence"].append({"job": asdict(job), "generation": asdict(final), "headline": result["headline"]})
            report.update(status="passed", stage="completed")
            save()
    except BaseException as error:
        report.update(status="failed", error_type=type(error).__name__, error=str(error)[:1000])
        save()
        raise


def _binding(directory, packet):
    suffix = digest(packet["reservation"])[:12]
    return {"task": TASK, "packet_hash": file_digest(directory / "packet.json"), "image": REGISTRY,
            "app": f"sts2-a10-smoke-{suffix}", "volume": f"sts2-a10-smoke-{suffix}", "name": f"smoke-{suffix}",
            "profile": PROFILE, "environment": "main"}


def _alarm(seconds):
    def expired(signum, frame):
        raise TimeoutError("bounded local Modal command timed out; remote state may be unknown")

    signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, seconds)


def _modal():
    if os.environ.get("MODAL_PROFILE") != PROFILE or any(
        os.environ.get(key) for key in ("MODAL_TOKEN_ID", "MODAL_TOKEN_SECRET", "MODAL_SERVER_URL",
                                        "MODAL_FORCE_BUILD", "MODAL_IGNORE_CACHE")
    ):
        raise RuntimeError("select the audited Modal profile without overrides")
    import modal

    if modal.__version__ != "1.6.3":
        raise RuntimeError("the audited Modal SDK 1.6.3 is required")
    return modal


def stage(store, directory, packet, binding, modal):
    row = store.gpu_smoke_step(packet["reservation"], "stage", binding)
    _alarm(min(480, row["deadline"] - time.time() - 320))
    cloud_path = directory / "cloud.json"
    cloud = {**binding, "stage_complete": False}
    _atomic(cloud_path, cloud)
    app = modal.App.lookup(binding["app"], create_if_missing=True, environment_name=binding["environment"])
    cloud["app_id"] = app.app_id
    _atomic(cloud_path, cloud)
    # An immutable registry import is the sole preparation build. No apt, pip or GPU builder steps.
    image = modal.Image.from_registry(REGISTRY).build(app)
    cloud["image_id"] = image.object_id
    _atomic(cloud_path, cloud)
    volume = modal.Volume.from_name(binding["volume"], create_if_missing=True, version=2,
                                    environment_name=binding["environment"]).hydrate()
    cloud["volume_id"] = volume.object_id
    _atomic(cloud_path, cloud)
    with volume.batch_upload(force=False) as upload:
        upload.put_directory(directory / "bundle", "/bundle")
        upload.put_file(directory / "packet.json", "/packet.json")
    cloud["stage_complete"] = True
    _atomic(cloud_path, cloud)
    return cloud


def launch(store, directory, packet, binding, modal):
    cloud = json.loads((directory / "cloud.json").read_bytes())
    if not cloud["stage_complete"] or any(cloud[key] != value for key, value in binding.items()):
        raise ValueError("paid launch requires the exact completed staging binding")
    _alarm(35)
    app = modal.App.lookup(binding["app"], environment_name=binding["environment"])
    if app.app_id != cloud["app_id"]:
        raise ValueError("staged Modal app identity changed")
    image = modal.Image.from_id(cloud["image_id"]).hydrate()
    volume = modal.Volume.from_id(cloud["volume_id"]).hydrate()
    row = store.gpu_smoke_step(packet["reservation"], "launch", binding)
    cutoff = row["worker_deadline"]
    sandbox = modal.Sandbox.create(
        "/opt/conda/bin/python", "-I", "-B", "-u", "-c", BOOTSTRAP, binding["packet_hash"], str(cutoff),
        app=app, name=binding["name"], tags={"sts2-task": TASK}, image=image,
        timeout=GPU_SECONDS, gpu="H100!", cpu=(16, 16), memory=(65536, 65536),
        runtime="gvisor", block_network=True, volumes={str(MOUNT): volume},
        env={"OMP_NUM_THREADS": "1", "MKL_NUM_THREADS": "1", "OPENBLAS_NUM_THREADS": "1"},
    )
    store.gpu_smoke_receipt(packet["reservation"], sandbox.object_id)
    return {"sandbox_id": sandbox.object_id, "worker_deadline": cutoff, "actual_cost_usd": None}


def _read_volume_json(volume, name):
    data = bytearray()
    for part in volume.read_file(name):
        data.extend(part)
        if len(data) > 64_000:
            raise ValueError("GPU report exceeds its byte bound")
    return json.loads(data)


def _find_finished_v1(binding, app_id):
    """The pinned SDK public name/list helpers only return running V1 Sandboxes."""
    import asyncio

    from modal.client import _Client
    from modal_proto import api_pb2

    async def lookup():
        client = await _Client.from_env()
        response = await client._stub.SandboxList(api_pb2.SandboxListRequest(
            app_id=app_id, environment_name=binding["environment"], include_finished=True,
            tags=[api_pb2.SandboxTag(tag_name="sts2-task", tag_value=TASK)],
        ))
        ids = {item.id for item in response.sandboxes}
        if len(ids) > 1:
            raise ValueError("multiple provider receipts for the single bound launch")
        return next(iter(ids), None)

    return asyncio.run(lookup())


def reconcile(store, directory, packet, binding, modal):
    from .adapters import Job
    from .artifact_adapter import ArtifactMirror

    row = store.gpu_smoke_step(packet["reservation"], "inspect", binding)
    cloud_path = directory / "cloud.json"
    cloud = json.loads(cloud_path.read_bytes()) if cloud_path.exists() else {}
    _alarm(90)
    sandbox = None
    if row["submit_started"]:
        try:
            sandbox = (modal.Sandbox.from_id(row["remote_id"]) if row["remote_id"] else
                       modal.Sandbox.from_name(binding["app"], binding["name"], environment_name=binding["environment"]))
        except modal.exception.NotFoundError:
            recovered = _find_finished_v1(binding, cloud["app_id"]) if cloud.get("app_id") else None
            if recovered:
                sandbox = modal.Sandbox.from_id(recovered)
        if sandbox:
            store.gpu_smoke_receipt(packet["reservation"], sandbox.object_id)
    exit_code = sandbox.poll() if sandbox else None
    report = {"status": "no_worker_report", "packet_hash": binding["packet_hash"],
              "reservation": packet["reservation"], "actual_cost_usd": None}
    if cloud.get("volume_id"):
        volume = modal.Volume.from_id(cloud["volume_id"]).hydrate()
        try:
            report = _read_volume_json(volume, "/output/result.json")
        except (FileNotFoundError, modal.exception.NotFoundError):
            pass
        if (report["packet_hash"], report["reservation"]) != (binding["packet_hash"], packet["reservation"]):
            raise ValueError("worker report does not match the durable intent")
        local = directory / "verified-output"
        local.mkdir(exist_ok=True)
        mirror = ArtifactMirror(local, SimpleNamespace(read_file=lambda name: volume.read_file(f"/output/{name}")))
        for entry in report.get("evidence", ()):
            mirror.copy(Job(**entry["job"]), entry["generation"]["reference"])
    report.update(provider_exit_code=exit_code, sandbox_id=sandbox.object_id if sandbox else row["remote_id"])
    _atomic(directory / "result.json", report)
    status = None
    if row["submit_started"] and sandbox is not None and exit_code is not None:
        complete = report["status"] == "passed" and exit_code == 0 and len(report.get("evidence", ())) == 3
        if complete or report["status"] == "failed" or time.time() >= row["deadline"]:
            status = "gpu_verified" if complete else "gpu_failed"
    elif not row["submit_started"] and time.time() >= row["deadline"]:
        status = "gpu_not_launched" if cloud.get("stage_complete") else "gpu_stage_failed"
    elif row["submit_started"] and not row["remote_id"] and sandbox is None and time.time() >= row["deadline"]:
        status = "gpu_receipt_unknown"
    if status:
        evidence = {"status": status, "session_ids": [], "report_ref": str(directory / "result.json")}
        if status in {"gpu_verified", "gpu_failed"}:
            evidence["details"] = {"sandbox_id": sandbox.object_id, "provider_exit_code": exit_code,
                                   "report_sha256": file_digest(directory / "result.json")}
        store.finish_access_probe(packet["reservation"], evidence)
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare_parser = commands.add_parser("prepare")
    for key in ("repo", "inputs", "engine", "directory"):
        prepare_parser.add_argument(f"--{key}", required=True, type=Path)
    prepare_parser.add_argument("--reservation", required=True)
    for name in ("stage", "launch", "reconcile"):
        command = commands.add_parser(name)
        command.add_argument("--directory", required=True, type=Path)
        command.add_argument("--state", required=True, type=Path)
        command.add_argument("--study-id", default="silent-a10-modal")
    args = parser.parse_args()
    if args.command == "prepare":
        result = prepare(args.repo, args.inputs, args.engine, args.directory, args.reservation)
    else:
        from .store import Store

        packet = json.loads((args.directory / "packet.json").read_bytes())
        validate_package(args.directory, packet)
        binding = _binding(args.directory, packet)
        store = Store(args.state, args.study_id)
        modal = _modal()
        try:
            result = {"stage": stage, "launch": launch, "reconcile": reconcile}[args.command](
                store, args.directory, packet, binding, modal)
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
    print(canonical(result))


if __name__ == "__main__":
    main()
