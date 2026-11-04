"""Modal function declarations. Importing this module creates no live app or schedule."""

from __future__ import annotations

from pathlib import Path

from .contracts import identifier
from .manifests import publish

ARTIFACT_MOUNT = "/modal-research-artifacts"


def declare_app(modal, *, runtime, cpu_image, worker_image, volume_v2, secrets, deployment_ref):
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

    @app.function(image=cpu_image, secrets=secrets, schedule=modal.Period(minutes=5),
                  timeout=90, startup_timeout=60, retries=0)
    def controller_tick():
        return runtime.tick_once()

    worker_options = {
        "image": worker_image, "gpu": "H100", "cpu": 16, "memory": 65536,
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
        volume_v2.reload()

        def publish_generation(generation, metadata, files):
            generation = identifier(generation)
            root = Path(ARTIFACT_MOUNT) / identifier(claim.identity["job_key"]) / generation
            manifest_hash = publish(root, claim.identity, metadata, files, commit=volume_v2.commit)
            reference = {"generation": generation, "manifest_hash": manifest_hash}
            runtime.record_generation(claim, reference)
            return reference

        return runtime.run_worker(claim, Path(ARTIFACT_MOUNT), publish_generation)

    @app.function(**worker_options)
    def train_segment(job_token):
        return worker("train", job_token)

    @app.function(**worker_options)
    def evaluate_checkpoint(job_token):
        return worker("eval", job_token)

    return app
