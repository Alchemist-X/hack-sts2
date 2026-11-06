"""Task103 transport overlay of immutable Task097 worker; training semantics unchanged."""
from pathlib import Path
from tempfile import TemporaryDirectory
import hashlib
import os
import shutil
import time
from .sandbox_provider import (PACKET_LIMIT, LOOP_ACCEPTANCE_EXPERIMENT, _write_report,
    generation_bytes, _export_transition_gate_failure)
from .broker import DetachedGrant
from .lineage import validate_job
from .contracts import identifier, integer, canonical
from .artifact_adapter import ArtifactAdapter, verify_bundle
from .manifests import verify
from .native_worker import Installation, run


def copy_accepted(grant, source, destination):
    """Copy only an admitted complete generation from a read-only mounted tree.

    The original publisher checks this direct ancestor's complete manifest,
    gate witness and cumulative auxiliary counters before publishing a child.
    It does not require recursively copying all historical model blobs.
    """
    ref = grant.job.payload["checkpoint"]
    if ref is None:
        if grant.accepted_checkpoint is not None:
            raise ValueError("fresh grant has an unexpected ancestor")
        return
    accepted = grant.accepted_checkpoint
    if accepted is None or accepted.reference != ref:
        raise ValueError("resume requires the exact accepted checkpoint")
    parent = source / identifier(ref["job_key"]) / identifier(ref["generation"])
    manifest = verify(parent, ref["manifest_hash"], accepted.identity)
    target = destination / identifier(ref["job_key"]) / identifier(ref["generation"])
    target.parent.mkdir(exist_ok=True)
    target.mkdir(exist_ok=False)
    for name in ["ready.json", *(entry["name"] for entry in manifest["files"].values())]:
        source_file = parent / name
        if source_file.is_symlink() or not source_file.is_file():
            raise ValueError("resume component is not a regular file")
        shutil.copyfile(source_file, target / name)
    verify(target, ref["manifest_hash"], accepted.identity)


def worker(packet, packet_hash, bundle_hash, cutoff, *, source_root, attempt_root, resume_root, volume):
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
    root = source_root / "bundles" / job.payload["config_hash"]
    artifacts = attempt_root / "outputs"
    artifacts.mkdir(exist_ok=True)
    receipts = attempt_root / "receipts"
    receipts.mkdir(exist_ok=True)
    path = attempt_root / "receipt.json"
    report = {"packet_hash": packet_hash, "job_key": job.key, "launch_token": grant.launch_token,
              "fence": grant.fence, "status": "running", "partial": None, "result": None, "candidate": None}

    def save():
        _write_report(path, report)
        volume.commit()

    # This receipt belongs to one UUID attempt only. Server-side Modal Dict
    # admission in function_app permits only one training attempt per token.
    with path.open("x", encoding="utf-8") as handle:
        handle.write(canonical(report))
        handle.flush()
        os.fsync(handle.fileno())
    volume.commit()
    try:
        copy_accepted(grant, resume_root, artifacts)
        with TemporaryDirectory(prefix="sts2-pull-") as work:
            installation = Installation(spec["code_commit"], spec["image_digest"], root / "lib" / "Sts2Sim.Vec.so",
                                        root / "dependencies.lock", root / "seeds", artifacts, Path(work))
            verify_bundle(installation, spec, root, bundle_hash)
            os.environ.update(STS2VEC_AOT_LIB=str(installation.engine), DOTNET_gcServer="1",
                              DOTNET_gcConcurrent="0", DOTNET_PROCESSOR_COUNT="32")
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

            from .transition_contract import positive
            gate_options = {}
            if job.kind == "train" and positive(spec):
                gate_options["gate_failure_ready"] = lambda source: _export_transition_gate_failure(
                    volume, receipts, grant.launch_token, job, source)
            _runtime_witness(attempt_root, volume)
            tiny_segment = (job.kind == "train" and spec["experiment_id"] == LOOP_ACCEPTANCE_EXPERIMENT)
            result = run(job, installation, grant.accepted_checkpoint,
                         checkpoint_ready=checkpoint_ready if job.kind == "train" else None,
                         seed_range=grant.seed_range, execution_deadline=cutoff, max_threads=32,
                         one_update_segment=tiny_segment, **gate_options)
            _preserve_session(installation.scratch / job.key, attempt_root, volume)
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


def _runtime_witness(root, volume):
    import platform
    pairs = set()
    cpuinfo = Path('/proc/cpuinfo')
    if cpuinfo.exists():
        for block in cpuinfo.read_text().split('\n\n'):
            values = dict(line.split(':', 1) for line in block.splitlines() if ':' in line)
            fields = {key.strip(): value.strip() for key, value in values.items()}
            if 'physical id' in fields and 'core id' in fields:
                pairs.add((fields['physical id'], fields['core id']))
    quota = Path('/sys/fs/cgroup/cpu.max')
    body = {'pid':os.getpid(), 'role':'trainer_process_before_run', 'requested_physical_cores':32, 'native_thread_cap':32, 'torch_thread_cap':32,
            'dotnet_processor_count_env':os.environ.get('DOTNET_PROCESSOR_COUNT'),
            'affinity_cpus':len(os.sched_getaffinity(0)) if hasattr(os,'sched_getaffinity') else None,
            'host_visible_physical_core_pairs':len(pairs),
            'cgroup_cpu_max':quota.read_text().strip() if quota.exists() else None,
            'python':platform.python_version()}
    (root/'runtime-witness.json').write_text(canonical(body))
    volume.commit()


def _preserve_session(scratch, root, volume):
    import torch
    observed = {"pid":os.getpid(), "role":"trainer_process_post_run", "torch_num_threads": torch.get_num_threads(),
                "torch_interop_threads": torch.get_num_interop_threads(),
                "cuda_device": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None}
    (root/"runtime-observed.json").write_text(canonical(observed))
    # config.json sessions report the actual native/torch settings, RNG restore,
    # Adam step, cursor, and fresh-episode discard count from the pinned trainer.
    for name in ('config.json','summary.json','metrics.jsonl','train.log'):
        source = scratch/name
        if source.is_file() and not source.is_symlink() and source.stat().st_size <= 8*1024**2:
            shutil.copyfile(source,root/name)
    volume.commit()
