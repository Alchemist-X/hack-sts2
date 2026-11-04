"""Bounded oneshot entrypoints for the single Raven controller and agent executor."""

from __future__ import annotations

import argparse
import hashlib
import fcntl
import json
import math
import os
import stat
import time
from dataclasses import dataclass, replace
from datetime import datetime, timedelta
from decimal import Decimal, ROUND_CEILING
from pathlib import Path

from .agent_runner import AgentRunner, SolTask, _research_input
from .artifact_adapter import METRIC_FIELDS
from .broker import AgentQueueProvider, DetachedGrant, GPU_KINDS, RavenBroker, RavenRuntime
from .contracts import DIGEST, digest, identifier
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
    valid_until: int | None
    gpu_per_second: str
    cpu_per_core_second: str
    memory_per_gib_second: str
    volume_per_gib_month: str
    startup_seconds: int
    storage_months: int
    storage_verified_until: int | None = None

    def __post_init__(self):
        identifier(self.reference)
        if ((self.valid_until is not None and type(self.valid_until) is not int)
                or type(self.startup_seconds) is not int
                or not 0 <= self.startup_seconds <= 300 or type(self.storage_months) is not int
                or not 1 <= self.storage_months <= 2
                or (self.storage_verified_until is not None and type(self.storage_verified_until) is not int)):
            raise ValueError("invalid verified Sandbox rates or bounded retention")
        for key in ("gpu_per_second", "cpu_per_core_second", "memory_per_gib_second", "volume_per_gib_month"):
            value = getattr(self, key)
            if type(value) is not str or not Decimal("0") < Decimal(value) <= Decimal("1"):
                raise ValueError("rates must be positive decimal strings")

    def reservation(self, job, now):
        if (job.kind not in GPU_KINDS or job.deadline <= now
                or (self.valid_until is not None and job.deadline > self.valid_until)
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
    """Finite per-event research admission. Reserve values are not proven cash caps."""

    reference: str
    account_verification: str
    valid_until: int | None
    reserve_cents: int
    max_calls: int | None = None  # Compatibility with frozen exact-event bindings.
    allowed_event_ids: tuple[str, ...] | None = None
    instructions: str = ""
    allowed_config_hashes: tuple[str, ...] = ()

    def __post_init__(self):
        identifier(self.reference)
        identifier(self.account_verification)
        if ((self.valid_until is not None and type(self.valid_until) is not int)
                or type(self.reserve_cents) is not int or not 0 < self.reserve_cents <= 10_000
                or (self.max_calls is not None and (type(self.max_calls) is not int or self.max_calls < 1))
                or type(self.instructions) is not str or len(self.instructions.encode()) > 16_000):
            raise ValueError("invalid per-event research allocation")
        if self.allowed_event_ids is not None:
            if (self.valid_until is None or not isinstance(self.allowed_event_ids, (tuple, list)) or not self.allowed_event_ids
                    or len(set(self.allowed_event_ids)) != len(self.allowed_event_ids)
                    or (self.max_calls is not None and len(self.allowed_event_ids) != self.max_calls)):
                raise ValueError("invalid exact-event research allowlist")
            for event in self.allowed_event_ids:
                identifier(event)
        elif (self.max_calls is not None or not self.allowed_config_hashes
              or any(not isinstance(value, str) or not DIGEST.fullmatch(value)
                     for value in self.allowed_config_hashes)):
            raise ValueError("dynamic research requires approved configuration hashes")

    def allows(self, job, now):
        events = job.payload.get("consumed_event_ids")
        return (job.kind == "research" and type(job.attempt) is int
                and 1 <= job.attempt <= (2 if self.allowed_event_ids is None else 1)
                and job.reserve_cents == self.reserve_cents
                and 0 < job.deadline - now <= 180
                and (self.valid_until is None or job.deadline <= self.valid_until)
                and type(events) is list and len(events) == 1 and isinstance(events[0], str)
                and (job.payload.get("current_experiment", {}).get("config_hash") in self.allowed_config_hashes
                     if self.allowed_event_ids is None else events[0] in self.allowed_event_ids)
                and len(_research_input(job.payload, self.instructions)[0].encode()) <= 24_000)


class WorkspaceMetering:
    """Refresh read-only usage/quotes on a configured cadence; expired data gates only Modal sends."""

    def __init__(self, store, sandbox, *, workspace, interval_seconds, max_age_seconds):
        self.store, self.sandbox = store, sandbox
        self.workspace = identifier(workspace)
        if (type(interval_seconds) is not int or type(max_age_seconds) is not int
                or not 0 < interval_seconds < max_age_seconds):
            raise ValueError("invalid metering cadence and freshness window")
        self.interval_seconds = interval_seconds
        self.max_age_seconds = max_age_seconds
        self.snapshot = store.platform_snapshot()

    @staticmethod
    def quoted_rates(raw):
        return {field: str(Decimal(raw[key]) / denominator) for field, key, denominator in (
            ("gpu_per_second", "gpu_hour_cost_h100", 3600),
            ("cpu_per_core_second", "cpu_hour_cost_sandbox", 3600),
            ("memory_per_gib_second", "mem_gib_hour_cost_sandbox", 3600),
            ("volume_per_gib_month", "volume_storage_gib_month_cost", 1),
        )}

    def refresh(self):
        last = (self.snapshot or {}).get("metered_observed_at", 0)
        if self.store.clock() - last < self.interval_seconds:
            return
        try:
            observation = self.sandbox.workspace_metering(self.workspace)
            if observation["workspace"] != self.workspace:
                return
            rates = self.quoted_rates(observation["rates"])
            if not all(Decimal("0") < Decimal(value) <= Decimal("1") for value in rates.values()):
                return
            gross = Decimal(observation["gross_used"]).quantize(Decimal("0.01"), rounding=ROUND_CEILING)
            net = max(Decimal(0), Decimal(observation["net_spent"]).quantize(Decimal("0.01"), rounding=ROUND_CEILING))
            self.snapshot = self.store.record_metered_usage(
                gross_used=str(gross), net_spent=str(net), cycle_start=observation["cycle_start"],
                observed_at=observation["observed_at"], rates=rates)
        except (OSError, TimeoutError, RuntimeError, ValueError, KeyError, TypeError, ArithmeticError):
            return  # retain the last good observation; never extend its freshness

    def reservation(self, rates, job, now):
        snapshot = self.snapshot or {}
        if not 0 <= now - snapshot.get("metered_observed_at", 0) <= self.max_age_seconds:
            return None
        # Freshness is checked at admission. The worker keeps its own
        # immutable deadline and reservation even if the quote later changes.
        quoted = replace(rates, **snapshot["metered_rates"])
        return quoted.reservation(job, now)


def load_runtime(config, *, clock=time.time):
    """Build a runtime only from a trusted on-host file with reviewed bindings."""
    required = {"study_id", "state", "artifacts", "sandbox", "sandbox_identity_verification",
                "sandbox_rates", "agent"}
    if type(config) is not dict or not required <= set(config) <= required | {"modal_metering", "research_goal"}:
        raise ValueError("runtime requires the complete deployment binding")
    state = Path(config["state"])
    artifacts = Path(config["artifacts"])
    if not state.is_absolute() or not state.is_file() or not artifacts.is_absolute() or not artifacts.is_dir():
        raise ValueError("bind existing local durable state and artifact directories")
    study_id = identifier(config["study_id"])
    store = Store(state, study_id, clock=clock)
    if config.get("research_goal") is not None:
        store.install_goal_file(config["research_goal"])
    sandbox = None
    rates = None
    metering = None
    if config["sandbox"] is not None:
        identifier(config["sandbox_identity_verification"])
        sandbox = SandboxProvider(SandboxBinding(**config["sandbox"]), artifacts, verified_identity=True, clock=clock)
        rates = SandboxRates(**config["sandbox_rates"])
        if config.get("modal_metering") is not None:
            metering = WorkspaceMetering(store, sandbox, **config["modal_metering"])
        elif rates.valid_until is None:
            raise ValueError("a Sandbox rate policy without fixed expiry requires live verified metering")
    elif config.get("modal_metering") is not None:
        raise ValueError("metering requires the exact Sandbox provider binding")
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
            policy = dict(agent["research_usage_policy"])
            if policy.get("allowed_event_ids") is None:
                policy["allowed_config_hashes"] = tuple(sandbox.binding.bundle_hashes) if sandbox else ()
            research_policy = ResearchUsagePolicy(**policy, instructions=instructions)
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
        if not sandbox or not sandbox.supports(job):
            return None
        now = clock()
        return metering.reservation(rates, job, now) if metering else rates.reservation(job, now)

    def bound(job):
        if runner and job.kind in runner.bindings:
            return agent["cost_caps"].get(job.kind)
        return None

    def authorize(job):
        if job.kind == "research" and research_policy is not None:
            return research_policy.allows(job, clock())
        ceiling = estimated(job) if job.kind in GPU_KINDS else bound(job)
        return type(ceiling) is int and 0 < ceiling <= job.reserve_cents

    billing = ({kind: ("modal", config["sandbox_identity_verification"]) for kind in GPU_KINDS}
               if sandbox else {})
    if runner:
        billing.update({kind: ("non_modal", role.verification_ref) for kind, role in runner.bindings.items()})
    broker = RavenBroker(store, artifacts, billing_sources=billing, authorize_spawn=authorize, reserve_bound=bound, conservative_reserve=estimated,
                         research_policy=research_policy,
                         tool_enabled=bool(runner and "sol" in runner.bindings),
                         experiment_reserve_limits=sandbox.binding.max_reserved_cents_by_config if sandbox else None)
    queue = AgentQueueProvider(broker, runner) if runner is not None else None
    return RavenRuntime(broker, modal_provider=sandbox, agent_provider=queue,
                        archive_deadline=rates.storage_verified_until if rates else None,
                        refresh_metering=metering.refresh if metering else None), queue


MONITOR_GRANT = Path("/var/lib/sts2-modal-production/monitor/active-grants.json")


def active_grant(runtime):
    if runtime.modal_provider is None:
        return {"schema_version": 1, "active": False}
    with runtime.broker.store.transaction() as db:
        rows = db.execute(
            """SELECT d.grant_json FROM broker_detached d JOIN broker_jobs b ON b.job_id=d.job_id
               WHERE b.state IN ('sending','ambiguous','submitted') ORDER BY b.rowid"""
        ).fetchall()
    if not rows:
        return {"schema_version": 1, "active": False}
    grants = []
    for row in rows:
        raw = row["grant_json"]
        grant = DetachedGrant.from_json(raw)
        grants.append({"schema_version": 1, "volume_id": runtime.modal_provider.binding.volume_id,
                       "job_key": grant.job.key, "launch_token": grant.launch_token,
                       "packet_hash": digest(json.loads(raw)), "fence": grant.fence,
                       "identity": artifact_identity(grant.job), "deadline": grant.job.deadline})
    return grants[0] if len(grants) == 1 else {"schema_version": 2, "grants": grants}


def accepted_update_history(artifacts, ref, manifest):
    """Read an optional accepted history sidecar, never checkpoint weights."""
    entry = manifest["files"].get("history")
    if not entry:
        return []
    size = entry.get("size")
    if type(size) is not int or not 0 < size <= 128 * 1024:
        return []
    try:
        root = Path(artifacts).resolve(strict=True)
        folder = root / identifier(ref["job_key"]) / identifier(ref["generation"])
        path = folder / identifier(entry["name"])
        if folder.resolve(strict=True) != folder:
            return []
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
        with os.fdopen(fd, "rb") as handle:
            metadata = os.fstat(handle.fileno())
            if not stat.S_ISREG(metadata.st_mode) or metadata.st_size != size:
                return []
            raw = handle.read(size + 1)
        if len(raw) != size or hashlib.sha256(raw).hexdigest() != entry.get("sha256"):
            return []
        doc = json.loads(raw)
        if doc["version"] != 1 or doc["series"] != "train" or type(doc["rows"]) is not list:
            return []
        rows = []
        previous_update = previous_steps = 0
        metadata = manifest["metadata"]
        for raw in doc["rows"]:
            update, steps = raw["update"], raw["env_steps"]
            if (type(update) is not int or type(steps) is not int
                    or not previous_update < update <= metadata["updates"]
                    or not previous_steps < steps <= metadata["decisions"]):
                return []
            at = raw.get("at")
            if at is not None and (type(at) is not str or len(at) > 64
                                  or datetime.fromisoformat(at.replace("Z", "+00:00")).utcoffset() != timedelta(0)):
                return []
            row = {key: value for key, value in raw.items()
                   if key in METRIC_FIELDS and type(value) in (int, float) and math.isfinite(value)}
            rows.append({**row, "at": at})
            previous_update, previous_steps = update, steps
        return rows
    except (OSError, ValueError, KeyError, TypeError, OverflowError, RecursionError):
        return []  # The accepted last completed update is still available.


def accepted_metrics(runtime):
    """Use only the already accepted checkpoint head recorded in SQLite."""
    items = []
    with runtime.broker.store.transaction() as db:
        rows = db.execute(
            """SELECT x.id,x.config_hash,x.checkpoint,j.id AS job_id,j.outcome
               FROM experiments x JOIN jobs j ON j.id=json_extract(x.checkpoint,'$.job_key')
               WHERE j.kind='train' AND j.state IN ('done','failed') ORDER BY x.id"""
        )
        for row in rows:
            ref = json.loads(row["checkpoint"])
            outcome = json.loads(row["outcome"])
            manifest = outcome["manifest"]
            meta = manifest["metadata"]
            if (outcome["checkpoint"] != ref or meta.get("result_label") != "capacity"
                    or manifest["identity"]["config_hash"] != row["config_hash"]):
                continue
            values = outcome["metrics"].get("last_completed_update", {})
            last = {key: value for key, value in values.items()
                    if key in METRIC_FIELDS and type(value) in (int, float) and math.isfinite(value)}
            if not 0 <= last.get("env_steps", -1) <= meta["decisions"]:
                continue
            metrics = {"last_completed_update": last}
            history = accepted_update_history(runtime.broker.artifacts, ref, manifest)
            if history:
                metrics["update_history"] = history
            items.append({"experiment_id": row["id"], "config_hash": row["config_hash"],
                          "job_key": row["job_id"], "manifest_hash": ref["manifest_hash"], "checkpoint_at": None,
                          "metadata": {key: meta[key] for key in ("decisions", "result_label", "termination")},
                          "metrics": metrics})
    return {"schema_version": 1, "items": items}


def accepted_evaluations(runtime):
    """Publish completed dev metrics separately from training update points."""
    items = []
    with runtime.broker.store.transaction() as db:
        rows = db.execute(
            """SELECT x.id,x.config_hash,j.id AS job_id,j.outcome
               FROM jobs j JOIN experiments x ON x.id=j.experiment_id
               WHERE j.kind='eval' AND j.state='done' ORDER BY j.rowid"""
        )
        for row in rows:
            outcome = json.loads(row["outcome"])
            manifest = outcome["manifest"]
            meta = manifest["metadata"]
            if (meta.get("result_label") != "capacity" or meta["termination"] != "completed"
                    or manifest["identity"]["config_hash"] != row["config_hash"]):
                continue
            values = outcome["metrics"]
            metrics = {key: values[key] for key in ("games", "wins", "act_reached", "progress_mean",
                       "win_rate", "win_rate_ci95", "progress_ci95") if key in values}
            items.append({"experiment_id": row["id"], "config_hash": row["config_hash"],
                          "job_key": row["job_id"], "manifest_hash": outcome["checkpoint"]["manifest_hash"],
                          "checkpoint_at": None,
                          "metadata": {key: meta[key] for key in ("decisions", "result_label", "termination")},
                          "metrics": metrics})
    return {"schema_version": 1, "items": items}


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
                    _write_status(MONITOR_GRANT.with_name("accepted-metrics.json"), accepted_metrics(runtime))
                    _write_status(MONITOR_GRANT.with_name("accepted-evaluations.json"), accepted_evaluations(runtime))
        elif queue is None:
            raise RuntimeError("agent executor requires a verified binding")
        else:
            queue.run_one()


if __name__ == "__main__":
    main()
