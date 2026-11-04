"""Modal function declarations. Importing this module creates no live app or schedule."""

from __future__ import annotations

from pathlib import Path

from .contracts import identifier
from .lineage import artifact_identity
from .manifests import publish
from .native_worker import run as run_native

ARTIFACT_MOUNT = "/modal-research-artifacts"


def declare_app(modal, *, runtime, cpu_image, worker_image, volume_v2, secrets, deployment_ref,
                schedule_controller=False):
    """Bind the production runtime after admission and provider verification.

    runtime must provide an external transactional tick, an admission-aware
    worker dispatcher, and a validated deployment report. It must resolve job
    tokens against the ledger, claim each invocation atomically and enforce
    the remaining absolute deadline and spend before any trainer import or
    initialization. It owns preemption recovery, heartbeat, and cost settlement.
    """
    identifier(deployment_ref)
    capabilities = runtime.verified_capabilities()
    required = {
        "external_transactional_store", "provider_idempotency", "verified_research_agent",
        "verified_tool_enabled_sol", "approved_worker_image", "restart_admission",
        "volume_v2", "bounded_gross_spend",
    }
    if capabilities != required:
        raise RuntimeError("Modal deployment gate is closed; verified production bindings are required")
    app = modal.App("sts2-modal-research")

    @app.function(image=cpu_image, secrets=secrets,
                  schedule=modal.Period(minutes=5) if schedule_controller else None,
                  timeout=90, startup_timeout=60, retries=0)
    def controller_tick():
        return runtime.tick_once()

    worker_options = {
        "image": worker_image, "gpu": "H100!", "cpu": (16, 16), "memory": (65536, 65536),
        "volumes": {ARTIFACT_MOUNT: volume_v2}, "secrets": secrets,
        "timeout": 4500, "startup_timeout": 600, "retries": 0,
    }

    def worker(kind, job_token):
        identifier(job_token)
        # No trainer work before a fresh atomic admission. Volume is the artifact
        # store only. Write unique generation paths across containers and attempts.
        claim = runtime.claim_worker(kind, job_token)
        if claim is None:
            return {"status": "not_admitted"}
        job = claim.job
        if job.kind != kind:
            raise ValueError("worker claim kind mismatch")
        identity = artifact_identity(job)
        volume_v2.reload()

        def publish_generation(generation, metadata, files):
            generation = identifier(generation)
            root = Path(ARTIFACT_MOUNT) / identity["job_key"] / generation
            manifest_hash = publish(root, identity, metadata, files, commit=volume_v2.commit)
            reference = {"generation": generation, "manifest_hash": manifest_hash}
            runtime.record_generation(claim, reference)
            return reference

        def checkpoint_ready(path):
            runtime.publish_training_checkpoint(claim, path, publish_generation)

        with runtime.supervise_worker(claim) as installation:
            if Path(installation.artifacts) != Path(ARTIFACT_MOUNT):
                raise ValueError("worker artifact root differs from deployment mount")
            result = run_native(job, installation, claim.accepted_checkpoint, checkpoint_ready=checkpoint_ready)
            return runtime.finish_worker(claim, result, publish_generation)

    @app.function(**worker_options)
    def train_segment(job_token):
        return worker("train", job_token)

    @app.function(**worker_options)
    def evaluate_checkpoint(job_token):
        return worker("eval", job_token)

    return app
