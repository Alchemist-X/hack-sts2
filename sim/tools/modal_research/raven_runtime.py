"""Bounded oneshot entrypoints for the single Raven controller and agent executor."""

from __future__ import annotations

import argparse
import fcntl
import json
import os
import time
from dataclasses import dataclass
from decimal import Decimal, ROUND_CEILING
from pathlib import Path

from .agent_runner import AgentRunner, SolTask
from .broker import AgentQueueProvider, GPU_KINDS, RavenBroker, RavenRuntime
from .contracts import identifier
from .gateway import AgentBinding
from .sandbox_provider import CPU, MEMORY_MIB, SandboxBinding, SandboxProvider
from .store import Store

GIB = 2**30


@dataclass(frozen=True)
class SandboxRates:
    """Operator-reviewed maximum rates and bounded artifact retention."""

    reference: str
    valid_until: int
    gpu_per_second: str
    cpu_per_core_second: str
    memory_per_gib_second: str
    volume_per_gib_month: str
    startup_seconds: int
    storage_months: int

    def __post_init__(self):
        identifier(self.reference)
        if (type(self.valid_until) is not int or type(self.startup_seconds) is not int
                or not 0 <= self.startup_seconds <= 300 or type(self.storage_months) is not int
                or not 1 <= self.storage_months <= 2):
            raise ValueError("invalid verified Sandbox rates or bounded retention")
        for key in ("gpu_per_second", "cpu_per_core_second", "memory_per_gib_second", "volume_per_gib_month"):
            value = getattr(self, key)
            if type(value) is not str or not Decimal("0") < Decimal(value) <= Decimal("1"):
                raise ValueError("rates must be positive decimal strings")

    def bound(self, job, now):
        if job.kind not in GPU_KINDS or now >= self.valid_until or job.deadline <= now:
            return None
        rate = (Decimal(self.gpu_per_second) + CPU * Decimal(self.cpu_per_core_second)
                + Decimal(MEMORY_MIB) / 1024 * Decimal(self.memory_per_gib_second))
        seconds = Decimal(str(job.deadline - now)).to_integral_value(rounding=ROUND_CEILING) + self.startup_seconds
        # Two retained generations and one in flight. Treat three full copies
        # as stored for the whole authorized retention window.
        params = job.payload["spec"]["actual_param_count"]
        gib = Decimal(3 * (params * 32 + 65 * 1024**2)) / GIB
        volume = gib * Decimal(self.volume_per_gib_month) * self.storage_months
        return int(((seconds * rate + volume) * 100).to_integral_value(rounding=ROUND_CEILING))


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
    if agent is not None:
        required_agent = {"state", "workspaces", "launcher", "bindings", "sol_tasks", "cost_caps", "cost_verification"}
        if type(agent) is not dict or set(agent) != required_agent:
            raise ValueError("runtime agent requires verified role and spending bindings")
        identifier(agent["cost_verification"])
        if not agent["cost_caps"].keys() <= agent["bindings"].keys():
            raise ValueError("agent spend caps must refer to verified roles")
        runner = AgentRunner(
            Path(agent["state"]), Path(agent["workspaces"]), study_id, Path(agent["launcher"]),
            {name: AgentBinding(**value) for name, value in agent["bindings"].items()},
            sol_tasks={name: SolTask(**value) for name, value in agent["sol_tasks"].items()},
        )

    def bound(job):
        if job.kind in GPU_KINDS:
            return rates.bound(job, clock()) if sandbox and sandbox.supports(job) else None
        if runner and job.kind in runner.bindings:
            return agent["cost_caps"].get(job.kind)
        return None

    def authorize(job):
        ceiling = bound(job)
        return type(ceiling) is int and 0 < ceiling <= job.reserve_cents

    broker = RavenBroker(store, artifacts, authorize_spawn=authorize, reserve_bound=bound,
                         tool_enabled=bool(runner and "sol" in runner.bindings))
    queue = AgentQueueProvider(broker, runner) if runner is not None else None
    return RavenRuntime(broker, modal_provider=sandbox, agent_provider=queue), queue


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", required=True, type=Path)
    parser.add_argument("command", choices=("tick", "agent"))
    args = parser.parse_args()
    path = args.config.resolve()
    with path.open("rb") as handle:
        data = handle.read(128_000)
        if handle.read(1):
            raise ValueError("runtime config exceeds its byte bound")
    config = json.loads(data)
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
                pending = status.with_suffix(".pending")
                with pending.open("w", encoding="utf-8") as output:
                    json.dump(runtime.broker.store.status(), output, indent=2, sort_keys=True)
                    output.write("\n")
                    output.flush()
                    os.fsync(output.fileno())
                pending.replace(status)
        elif queue is None:
            raise RuntimeError("agent executor requires a verified binding")
        else:
            queue.run_one()


if __name__ == "__main__":
    main()
