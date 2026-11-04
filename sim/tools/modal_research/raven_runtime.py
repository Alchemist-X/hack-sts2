"""Bounded oneshot entrypoints for the single Raven controller and agent executor."""

from __future__ import annotations

import argparse
import fcntl
import json
import os
import stat
import time
from dataclasses import dataclass
from decimal import Decimal, ROUND_CEILING
from pathlib import Path

from .agent_runner import AgentRunner, SolTask, _research_input
from .broker import AgentQueueProvider, DetachedGrant, GPU_KINDS, RavenBroker, RavenRuntime
from .contracts import digest, identifier
from .gateway import AgentBinding
from .lineage import artifact_identity
from .manifests import file_digest
from .sandbox_provider import CPU, MEMORY_MIB, SandboxBinding, SandboxProvider, generation_bytes
from .store import Store

GIB = 2**30


@dataclass(frozen=True)
class SandboxRates:
    """Quoted rates, hard requested runtime and a conservative startup allowance."""

    reference: str
    valid_until: int
    gpu_per_second: str
    cpu_per_core_second: str
    memory_per_gib_second: str
    volume_per_gib_month: str
    startup_seconds: int
    storage_months: int
    storage_verified_until: int | None = None

    def __post_init__(self):
        identifier(self.reference)
        if (type(self.valid_until) is not int or type(self.startup_seconds) is not int
                or not 0 <= self.startup_seconds <= 300 or type(self.storage_months) is not int
                or not 1 <= self.storage_months <= 2
                or (self.storage_verified_until is not None and type(self.storage_verified_until) is not int)):
            raise ValueError("invalid verified Sandbox rates or bounded retention")
        for key in ("gpu_per_second", "cpu_per_core_second", "memory_per_gib_second", "volume_per_gib_month"):
            value = getattr(self, key)
            if type(value) is not str or not Decimal("0") < Decimal(value) <= Decimal("1"):
                raise ValueError("rates must be positive decimal strings")

    def reservation(self, job, now):
        if (job.kind not in GPU_KINDS or job.deadline <= now or job.deadline > self.valid_until
                or self.storage_verified_until is None or job.deadline > self.storage_verified_until
                or self.storage_verified_until - now > self.storage_months * 28 * 86400):
            return None
        rate = (Decimal(self.gpu_per_second) + CPU * Decimal(self.cpu_per_core_second)
                + Decimal(MEMORY_MIB) / 1024 * Decimal(self.memory_per_gib_second))
        seconds = Decimal(str(job.deadline - now)).to_integral_value(rounding=ROUND_CEILING) + self.startup_seconds
        # Two retained generations and one in flight. Treat three full copies
        # as stored for the whole authorized retention window.
        gib = Decimal(3 * generation_bytes(job)) / GIB
        # Modal can retain metered bytes for four days after removal. The
        # reviewed retention binding must cover deletion and this tail.
        volume = gib * Decimal(self.volume_per_gib_month) * (Decimal(self.storage_months) + Decimal(4) / 28)
        return int(((seconds * rate + volume) * 100).to_integral_value(rounding=ROUND_CEILING))


@dataclass(frozen=True)
class ResearchUsagePolicy:
    """Explicitly bounded invocation policy. This is not a cash-cost upper bound."""

    reference: str
    account_verification: str
    valid_until: int
    reserve_cents: int
    max_calls: int
    allowed_event_ids: tuple[str, ...]
    instructions: str = ""

    def __post_init__(self):
        identifier(self.reference)
        identifier(self.account_verification)
        if (type(self.valid_until) is not int or type(self.reserve_cents) is not int
                or self.reserve_cents != 200 or type(self.max_calls) is not int or not 1 <= self.max_calls <= 2
                or type(self.instructions) is not str or len(self.instructions.encode()) > 16_000
                or not isinstance(self.allowed_event_ids, (tuple, list))
                or len(self.allowed_event_ids) != self.max_calls or len(set(self.allowed_event_ids)) != self.max_calls):
            raise ValueError("invalid finite research allocation")
        for event in self.allowed_event_ids:
            identifier(event)

    def allows(self, job, now):
        return (job.kind == "research" and job.attempt == 1 and job.reserve_cents == self.reserve_cents
                and now < job.deadline <= self.valid_until and job.deadline - now <= 180
                and job.payload.get("consumed_event_ids") in ([event] for event in self.allowed_event_ids)
                and len(_research_input(job.payload, self.instructions)[0].encode()) <= 24_000)


def load_runtime(config, *, clock=time.time):
    """Build a runtime only from a trusted on-host file with reviewed bindings."""
    required = {"study_id", "state", "artifacts", "sandbox", "sandbox_identity_verification",
                "sandbox_rates", "agent"}
    if type(config) is not dict or set(config) != required:
        raise ValueError("runtime requires the complete deployment binding")
    state = Path(config["state"])
    artifacts = Path(config["artifacts"])
    if not state.is_absolute() or not state.is_file() or not artifacts.is_absolute() or not artifacts.is_dir():
        raise ValueError("bind existing local durable state and artifact directories")
    study_id = identifier(config["study_id"])
    store = Store(state, study_id, clock=clock)
    sandbox = None
    rates = None
    if config["sandbox"] is not None:
        identifier(config["sandbox_identity_verification"])
        sandbox = SandboxProvider(SandboxBinding(**config["sandbox"]), artifacts, verified_identity=True, clock=clock)
        rates = SandboxRates(**config["sandbox_rates"])
    agent = config["agent"]
    runner = None
    research_policy = None
    if agent is not None:
        required_agent = {"state", "workspaces", "launcher", "bindings", "sol_tasks", "cost_caps", "cost_verification"}
        if type(agent) is not dict or not (required_agent <= set(agent) <= required_agent | {"system_identity", "research_usage_policy", "research_instructions", "launcher_sha256"}):
            raise ValueError("runtime agent requires verified role and spending bindings")
        if agent["cost_caps"]:
            identifier(agent["cost_verification"])
        if not agent["cost_caps"].keys() <= agent["bindings"].keys():
            raise ValueError("agent spend caps must refer to verified roles")
        if agent.get("research_usage_policy"):
            instructions = agent.get("research_instructions")
            if (type(instructions) is not str or not instructions.strip()
                    or file_digest(Path(agent["launcher"])) != agent.get("launcher_sha256")):
                raise ValueError("finite research requires pinned instructions and the verified launcher digest")
            research_policy = ResearchUsagePolicy(**agent["research_usage_policy"], instructions=instructions)
            if (set(agent["bindings"]) != {"research"} or agent["bindings"]["research"]["model_id"] != "gpt-6-astra"
                    or not agent.get("system_identity") or "research" in agent["cost_caps"]
                    or not 1 <= agent["bindings"]["research"]["max_output_bytes"] <= 16_000):
                raise ValueError("finite policy requires the no-tool system Astra binding only")
        runner = AgentRunner(
            Path(agent["state"]), Path(agent["workspaces"]), study_id, Path(agent["launcher"]),
            {name: AgentBinding(**value) for name, value in agent["bindings"].items()},
            sol_tasks={name: SolTask(**value) for name, value in agent["sol_tasks"].items()},
            system_identity=agent.get("system_identity"), scoped_research=research_policy is not None,
            research_instructions=research_policy.instructions if research_policy else "",
        )

    def estimated(job):
        return rates.reservation(job, clock()) if sandbox and sandbox.supports(job) else None

    def bound(job):
        if runner and job.kind in runner.bindings:
            return agent["cost_caps"].get(job.kind)
        return None

    def authorize(job):
        if job.kind == "research" and research_policy is not None:
            return research_policy.allows(job, clock())
        ceiling = estimated(job) if job.kind in GPU_KINDS else bound(job)
        return type(ceiling) is int and 0 < ceiling <= job.reserve_cents

    broker = RavenBroker(store, artifacts, authorize_spawn=authorize, reserve_bound=bound, conservative_reserve=estimated,
                         research_policy=research_policy,
                         tool_enabled=bool(runner and "sol" in runner.bindings))
    queue = AgentQueueProvider(broker, runner) if runner is not None else None
    return RavenRuntime(broker, modal_provider=sandbox, agent_provider=queue,
                        archive_deadline=rates.storage_verified_until if rates else None), queue


MONITOR_GRANT = Path("/var/lib/sts2-modal-production/monitor/active-grants.json")


def active_grant(runtime):
    if runtime.modal_provider is None:
        return {"schema_version": 1, "active": False}
    with runtime.broker.store.transaction() as db:
        rows = db.execute(
            """SELECT d.grant_json FROM broker_detached d JOIN broker_jobs b ON b.job_id=d.job_id
               WHERE b.state IN ('sending','ambiguous','submitted') LIMIT 2"""
        ).fetchall()
    if not rows:
        return {"schema_version": 1, "active": False}
    if len(rows) != 1:
        raise ValueError("multiple active GPU grants")
    raw = rows[0]["grant_json"]
    grant = DetachedGrant.from_json(raw)
    return {"schema_version": 1, "volume_id": runtime.modal_provider.binding.volume_id,
            "job_key": grant.job.key, "launch_token": grant.launch_token, "packet_hash": digest(json.loads(raw)),
            "fence": grant.fence, "identity": artifact_identity(grant.job), "deadline": grant.job.deadline}


def _write_status(path, data):
    pending = path.with_suffix(".pending")
    fd = os.open(pending, os.O_CREAT | os.O_TRUNC | os.O_WRONLY | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as output:
        json.dump(data, output, indent=2, sort_keys=True)
        output.write("\n")
        output.flush()
        os.fsync(output.fileno())
    pending.replace(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("--export-monitor-grant", action="store_true")
    parser.add_argument("command", choices=("tick", "agent"))
    args = parser.parse_args()
    path = args.config.resolve()
    with path.open("rb") as handle:
        data = handle.read(128_000)
        if handle.read(1):
            raise ValueError("runtime config exceeds its byte bound")
    config = json.loads(data)
    if args.export_monitor_grant:
        metadata = MONITOR_GRANT.parent.stat()
        if (args.command != "tick" or os.geteuid() != 0 or MONITOR_GRANT.parent.resolve() != MONITOR_GRANT.parent
                or metadata.st_uid != 0 or stat.S_IMODE(metadata.st_mode) != 0o700):
            raise ValueError("monitor grant output requires its root-only production directory")
    lock = Path(config["state"]).with_suffix(f".{args.command}.lock")
    with lock.open("a") as handle:
        try:
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            return
        runtime, queue = load_runtime(config)
        if args.command == "tick":
            try:
                runtime.tick_once()
            finally:
                status = Path(config["state"]).with_name("status.json")
                _write_status(status, runtime.broker.store.status())
                if args.export_monitor_grant:
                    _write_status(MONITOR_GRANT, active_grant(runtime))
        elif queue is None:
            raise RuntimeError("agent executor requires a verified binding")
        else:
            queue.run_one()


if __name__ == "__main__":
    main()
