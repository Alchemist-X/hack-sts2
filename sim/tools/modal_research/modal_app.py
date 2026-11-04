"""Modal function declarations. Importing this module creates no live app or schedule."""

from __future__ import annotations

from pathlib import Path
from uuid import uuid4

from .contracts import identifier
from .supervisor import WorkerSupervisor

ARTIFACT_MOUNT = "/modal-research-artifacts"


def declare_app(modal, *, runtime, cpu_image, worker_image, volume_v2, secrets, deployment_ref,
                schedule_controller=False, engineering_smoke=False):
    """Bind the production runtime after admission and provider verification.

    runtime must provide an external transactional tick, an admission-aware
    worker dispatcher, and a validated deployment report. It must resolve job
    tokens against the ledger, claim each invocation atomically and enforce
    the remaining absolute deadline and spend before any trainer import or
    initialization. It owns preemption recovery, heartbeat, and cost settlement.

    Engineering smoke uses a separate app with short worker timeouts. The
    runtime ledger controls the number of authorized launches.
    """
    identifier(deployment_ref)
    if type(engineering_smoke) is not bool:
        raise ValueError("engineering_smoke must be a boolean")
    if engineering_smoke and schedule_controller:
        raise ValueError("engineering smoke cannot schedule the controller")
    capabilities = runtime.verified_capabilities()
    required = {
        "external_transactional_store", "provider_idempotency", "verified_research_agent",
        "verified_tool_enabled_sol", "approved_worker_image", "restart_admission",
        "volume_v2", "bounded_gross_spend",
    }
    if capabilities != required:
        raise RuntimeError("Modal deployment gate is closed; verified production bindings are required")
    supervisor = getattr(runtime, "worker_supervisor", None)
    if (not isinstance(supervisor, WorkerSupervisor) or not callable(getattr(modal, "current_input_id", None))
            or Path(supervisor.installation.artifacts) != Path(ARTIFACT_MOUNT)
            or supervisor.publisher.root != Path(ARTIFACT_MOUNT) or supervisor.publisher.volume is not volume_v2):
        raise RuntimeError("Modal deployment requires a bound worker supervisor and artifact volume")
    app_name = "sts2-modal-research-smoke" if engineering_smoke else "sts2-modal-research"
    app = modal.App(app_name)

    @app.function(image=cpu_image, secrets=secrets,
                  schedule=modal.Period(minutes=5) if schedule_controller else None,
                  timeout=90, startup_timeout=60, retries=0)
    def controller_tick():
        return runtime.tick_once()

    worker_options = {
        "image": worker_image, "gpu": "H100!", "cpu": (16, 16), "memory": (65536, 65536),
        "volumes": {ARTIFACT_MOUNT: volume_v2}, "secrets": secrets,
        "timeout": 120 if engineering_smoke else 4500,
        "startup_timeout": 60 if engineering_smoke else 600, "retries": 0,
    }

    def worker(kind, job_token):
        return supervisor.execute(kind, job_token, modal.current_input_id(), uuid4().hex)

    @app.function(**worker_options)
    def train_segment(job_token):
        return worker("train", job_token)

    @app.function(**worker_options)
    def evaluate_checkpoint(job_token):
        return worker("eval", job_token)

    return app
