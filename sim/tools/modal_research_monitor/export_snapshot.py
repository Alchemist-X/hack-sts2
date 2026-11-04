#!/usr/bin/env python3
"""Read-only, allowlist-only public monitor snapshot exporter.

No raw source document, path, command, token, or host field is copied to output.
The sole write is an atomic replacement of --output under the monitor directory.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import math
import os
import re
from pathlib import Path
import tempfile
import time

SCHEMA = 1
ARMS = ("S", "M", "L")
METRICS = ("progress", "act1_reach", "act2_reach", "act3_reach", "win_rate",
           "approx_kl", "entropy", "value_loss", "env_steps_per_s")
METRIC_ALIASES = {"throughput": "env_steps_per_s"}
SOURCE_LIMIT = 2_000_000
MAX_POINTS = 2000
FORMAL_ID = re.compile(r"^silent-a10-capacity-([sml])-formal-v1$")


def formal_arm(experiment_id):
    """Only the frozen capacity-arm naming contract is eligible for formal data."""
    match = FORMAL_ID.fullmatch(experiment_id) if isinstance(experiment_id, str) else None
    return match.group(1).upper() if match else None


def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    value = float(value)
    return value if math.isfinite(value) else None


def integer(value):
    n = number(value)
    return int(n) if n is not None and n >= 0 and n.is_integer() else None


def utc(ts):
    return dt.datetime.fromtimestamp(ts, dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")


def read_json(path):
    if path.stat().st_size > SOURCE_LIMIT:
        raise ValueError("source too large")
    with path.open(encoding="utf-8") as f:
        return json.load(f)


def safe_text(value, allowed):
    return value if isinstance(value, str) and value in allowed else None


def metric_point(raw):
    if not isinstance(raw, dict):
        return None
    decisions = integer(raw.get("decisions", raw.get("env_steps")))
    when = raw.get("at")
    try:
        parsed = dt.datetime.fromisoformat(when.replace("Z", "+00:00")) if isinstance(when, str) else None
        if parsed and parsed.tzinfo is None:
            parsed = None
    except ValueError:
        parsed = None
    values = {}
    for name in METRICS:
        v = number(raw.get(name))
        if v is None and name == "progress":
            v = number(raw.get("progress_mean"))
        if v is None and name == "win_rate":
            wins, episodes = number(raw.get("wins")), number(raw.get("episodes"))
            v = wins / episodes if wins is not None and episodes and episodes > 0 else None
        if v is None and name == "env_steps_per_s":
            v = number(raw.get("throughput"))
        values[name] = v
    if decisions is None and parsed is None:
        return None
    return {"decisions": decisions, "at": parsed.astimezone(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z") if parsed else None, "values": values}


def export(root, output, manifest=None, include_engineering=True, telemetry_input=None, active_grant=None):
    root = Path(root)
    status_path = root / "status.json"
    plan_path = root / "research" / "plan.json"
    status = read_json(status_path)
    plan = read_json(plan_path)
    experiments = status.get("experiments", [])
    if not isinstance(experiments, list):
        raise ValueError("invalid status experiments")
    now = time.time()
    source_ts = max(status_path.stat().st_mtime, plan_path.stat().st_mtime)
    phases = ["plan", "code", "deployed", "real"]
    arms = {a: {"stage": "plan", "decisions": 0, "target": None, "points": []} for a in ARMS}
    targets = plan.get("capacity_comparison", {}).get("new_decisions_each")
    for a in ARMS:
        arms[a]["target"] = integer(targets)
    for exp in experiments:
        if not isinstance(exp, dict):
            raise ValueError("invalid experiment status")
        exp_id = exp.get("id")
        arm = formal_arm(exp_id)
        if arm:
            arms[arm]["decisions"] = integer(exp.get("decisions")) or 0
            arms[arm]["target"] = integer(exp.get("target")) or arms[arm]["target"]
            arms[arm]["stage"] = "real" if arms[arm]["decisions"] else ("deployed" if exp.get("stage") in ("train", "eval", "needs_train", "needs_eval", "running", "submitted") else "code")
    conditional = root / "research" / "formal-S-conditional-035.json"
    if conditional.exists() and not experiments:
        source_ts = max(source_ts, conditional.stat().st_mtime)
    formal = {"arms": arms, "status": "active" if experiments else "pending", "current_task": "正式实验执行中" if experiments else "等待正式实验准入"}
    smoke_arms = {a: {"stage": "plan", "decisions": 0, "target": None, "points": []} for a in ARMS}
    completed_probes = status.get("finished_probes_cost_unknown", [])
    gpu_verified = (isinstance(completed_probes, list) and any(
        isinstance(probe, dict) and probe.get("id") == "reservation_runtime_gpu_015"
        and probe.get("status") == "runtime_gpu_verified" for probe in completed_probes
    ))
    engineering = {"arms": smoke_arms, "status": "verified" if gpu_verified else "pending",
                   "current_task": "GPU 闭环验收通过" if gpu_verified else "GPU 闭环待验收"}
    if include_engineering:
        p = root / "task023" / "sandbox-package" / "verified-output" / "reservation_gpu_fp32_009-train-batch" / "34355ef9915a4d8c8686d4d78acace4b" / "metrics.json"
        if p.exists():
            data = read_json(p)
            if data.get("result_label") == "engineering_smoke":
                raw = data.get("last_completed_update", {})
                pt = metric_point(raw)
                smoke_arms["S"]["stage"] = "real"
                smoke_arms["S"]["decisions"] = integer(data.get("env_steps")) or 0
                if pt:
                    smoke_arms["S"]["points"].append(pt)
                source_ts = max(source_ts, p.stat().st_mtime)
    checkpoint_source_ts = None
    if telemetry_input and active_grant and Path(telemetry_input).is_file() and Path(active_grant).is_file():
        grant = read_json(Path(active_grant))
        if not isinstance(grant, dict) or grant.get("schema_version") != 1:
            raise ValueError("invalid active grant projection")
        if grant.get("active") is not False:
            required = {"schema_version", "volume_id", "job_key", "launch_token", "packet_hash", "fence", "identity", "deadline"}
            if set(grant) != required or not isinstance(grant["identity"], dict):
                raise ValueError("invalid active grant fields")
            telemetry = read_json(Path(telemetry_input))
            expected_keys = {"job_key", "experiment_id", "config_hash", "grant_fence", "receipt_fence", "receipt_status", "observed_at", "checkpoint_at", "manifest_hash", "identity", "metadata", "metrics"}
            if not isinstance(telemetry, dict) or set(telemetry) != expected_keys:
                raise ValueError("invalid telemetry projection")
            # A completed previous attempt may leave its last checkpoint file.
            # Ignore that file if the current grant has moved to another job.
            if telemetry["receipt_status"] not in ("running", "succeeded", "failed"):
                raise ValueError("invalid telemetry receipt status")
            if telemetry["job_key"] == grant["job_key"] and telemetry["receipt_status"] == "running":
                if (telemetry["grant_fence"] != telemetry["receipt_fence"] or telemetry["grant_fence"] != grant["fence"]
                        or integer(telemetry["grant_fence"]) is None or telemetry["grant_fence"] < 1
                        ):
                    raise ValueError("telemetry fence or status mismatch")
                if not isinstance(telemetry["manifest_hash"], str) or not re.fullmatch(r"[0-9a-f]{64}", telemetry["manifest_hash"]):
                    raise ValueError("invalid telemetry manifest hash")
                identity, metadata, metrics = telemetry["identity"], telemetry["metadata"], telemetry["metrics"]
                grant_identity = grant["identity"]
                if not all(isinstance(x, dict) for x in (identity, metadata, metrics)):
                    raise ValueError("invalid telemetry fields")
                for key in ("job_key", "experiment_id", "config_hash"):
                    if identity.get(key) != telemetry[key] or grant_identity.get(key) != telemetry[key]:
                        raise ValueError("telemetry identity mismatch")
                if metadata.get("result_label") != "capacity" or metadata.get("termination") != "checkpoint":
                    raise ValueError("telemetry is not a formal checkpoint")
                exp = next((e for e in experiments if e.get("id") == telemetry["experiment_id"]), None)
                jobs = status.get("active_jobs", [])
                job = next((j for j in jobs if isinstance(j, dict) and j.get("id") == telemetry["job_key"]), None) if isinstance(jobs, list) else None
                if not exp or exp.get("config_hash") != telemetry["config_hash"] or not job or job.get("kind") != "train" or job.get("state") not in ("intent", "running", "uncertain", "external_reserved"):
                    # Status has moved past this attempt; it is no longer live telemetry.
                    pass
                else:
                    def parse_observed(value, label):
                        if value is None and label == "checkpoint":
                            return None
                        try:
                            parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
                            if parsed.tzinfo is None or parsed.timestamp() > now + 300:
                                raise ValueError
                            return parsed
                        except (TypeError, AttributeError, ValueError):
                            raise ValueError(f"invalid telemetry {label} time") from None
                    parse_observed(telemetry["observed_at"], "collection")
                    checkpoint_at = parse_observed(telemetry["checkpoint_at"], "checkpoint")
                    arm = formal_arm(telemetry["experiment_id"])
                    if arm is None:
                        raise ValueError("telemetry experiment arm unknown")
                    checkpoint_decisions = integer(metadata.get("decisions"))
                    if checkpoint_decisions is None or checkpoint_decisions < arms[arm]["decisions"]:
                        raise ValueError("checkpoint decision count regressed")
                    row = metrics.get("last_completed_update")
                    metric_decisions = integer(row.get("env_steps")) if isinstance(row, dict) else None
                    if metric_decisions is not None:
                        if metric_decisions > checkpoint_decisions:
                            raise ValueError("metric row exceeds checkpoint decisions")
                        raw = dict(row)
                        raw["at"] = checkpoint_at.isoformat().replace("+00:00", "Z") if checkpoint_at else None
                        point = metric_point(raw)
                        if point and any(value is not None for value in point["values"].values()):
                            arms[arm]["points"].append(point)
                    arms[arm]["decisions"] = checkpoint_decisions
                    arms[arm]["stage"] = "real"
                    formal["status"] = "active"
                    formal["current_task"] = "运行中遥测，未终态验收；指标可滞后一批"
                    if checkpoint_at:
                        checkpoint_source_ts = checkpoint_at.timestamp()
    if manifest:
        config = read_json(Path(manifest))
        if not isinstance(config, list) or len(config) > 12:
            raise ValueError("manifest must be a list of at most 12 sources")
        for item in config:
            if not isinstance(item, dict):
                raise ValueError("invalid manifest item")
            arm = safe_text(item.get("arm"), ARMS)
            track = safe_text(item.get("track"), ("formal", "engineering"))
            if not arm or not track:
                raise ValueError("invalid arm or track")
            p = Path(item.get("path", ""))
            if not p.is_file():
                raise ValueError("metric source missing")
            data = read_json(p)
            if track == "formal" and data.get("result_label") not in ("capacity",):
                raise ValueError("formal metric source lacks formal provenance")
            if track == "engineering" and data.get("result_label") != "engineering_smoke":
                raise ValueError("engineering metric source lacks provenance")
            raw_points = data.get("points")
            if raw_points is None:
                raw_points = [data.get("last_completed_update", data)]
            if not isinstance(raw_points, list) or len(raw_points) > MAX_POINTS:
                raise ValueError("invalid point count")
            out_arm = (formal if track == "formal" else engineering)["arms"][arm]
            out_arm["points"] = [p for raw in raw_points if (p := metric_point(raw))]
            out_arm["points"].sort(key=lambda x: (x["decisions"] is None, x["decisions"] or 0, x["at"] or ""))
            out_arm["stage"] = "real"
            out_arm["decisions"] = max([out_arm["decisions"]] + [p["decisions"] or 0 for p in out_arm["points"]])
            source_ts = max(source_ts, p.stat().st_mtime)
    def halt_reason(raw, paused=False):
        if not raw:
            return None
        reason = str(raw).lower()
        if any(word in reason for word in ("budget", "reserve", "billing", "cost", "headroom")):
            return "预算或费用边界触发"
        if any(word in reason for word in ("deadline", "timeout", "time limit")):
            return "任务时限触发"
        if any(word in reason for word in ("stale", "state version")):
            return "决策状态过期"
        if any(word in reason for word in ("artifact", "verification", "mismatch", "integrity")):
            return "结果验证需核查"
        return "原因待核查" if paused else "运行边界需核查"

    stop_reason = halt_reason(status.get("stop"))
    pause_reason = halt_reason(status.get("analysis_pause"), paused=True)
    halt_message = ("已停止 · " + stop_reason) if stop_reason else (("分析暂停 · " + pause_reason) if pause_reason else None)
    if halt_message:
        for view in (formal, engineering):
            view["status"] = "pending"
            view["current_task"] = halt_message
    actual = number(status.get("spent_usd")) if status.get("spend_complete") is True else None
    known_platform = number((status.get("platform_snapshot") or {}).get("gross_used_usd"))
    snapshot = {
        "schema_version": SCHEMA,
        "version": 1,
        "generated_at": utc(now),
        "data_at": utc(checkpoint_source_ts if checkpoint_source_ts is not None else source_ts),
        "update_interval_seconds": 300,
        "poll_interval_seconds": 45000,
        "study": "SILENT · A10 · 从零训练",
        "phase_order": phases,
        "streams": {"formal": formal, "engineering": engineering},
        "budget": {"approved_usd": number(status.get("approved_budget")), "actual_usd": actual,
                   "actual_status": "known" if actual is not None else "unknown",
                   "platform_gross_observed_usd": known_platform,
                   "reserved_usd": number(status.get("reserved_usd"))},
        "notice": halt_message or "仅展示脱敏、可核验指标；缺失值保持空白。",
    }
    output = Path(output)
    # A private sidecar fences historical points to the same formal experiment
    # and config. Engineering smoke always shows the current isolated run only.
    lineage = {a: None for a in ARMS}
    for exp in experiments:
        exp_id = exp.get("id")
        arm_name = formal_arm(exp_id)
        if arm_name:
            lineage[arm_name] = [exp_id, exp.get("config_hash")]
    lineage_path = output.with_name(output.name + ".lineage.json")
    old_lineage = None
    if lineage_path.exists():
        try:
            old_lineage = read_json(lineage_path)
        except (OSError, ValueError, json.JSONDecodeError):
            pass
    if output.exists():
        try:
            prior = read_json(output)
            if prior.get("schema_version") == SCHEMA and prior.get("study") == snapshot["study"]:
                for track, view in snapshot["streams"].items():
                    if track != "formal":
                        continue
                    for arm_name, arm_view in view["arms"].items():
                        if lineage[arm_name] is None or not isinstance(old_lineage, dict) or old_lineage.get("version") != prior.get("version") or old_lineage.get("arms", {}).get(arm_name) != lineage[arm_name]:
                            continue
                        old = prior["streams"][track]["arms"][arm_name].get("points", [])
                        new = arm_view["points"]
                        by_decisions = {p.get("decisions"): p for p in old if isinstance(p, dict) and integer(p.get("decisions")) is not None}
                        by_decisions.update({p["decisions"]: p for p in new if p["decisions"] is not None})
                        arm_view["points"] = sorted(by_decisions.values(), key=lambda p: p["decisions"])[-MAX_POINTS:]
        except (ValueError, KeyError, TypeError, json.JSONDecodeError):
            pass
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        try:
            prior = read_json(output)
            snapshot["version"] = max(integer(prior.get("version")) or 0, 0) + 1
            old_time = dt.datetime.fromisoformat(prior["generated_at"].replace("Z", "+00:00")).timestamp()
            snapshot["generated_at"] = utc(max(now, old_time + 1))
        except (ValueError, KeyError, TypeError, json.JSONDecodeError):
            pass
    payload = json.dumps(snapshot, ensure_ascii=False, separators=(",", ":"), allow_nan=False) + "\n"
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=output.parent, prefix=".snapshot-", delete=False) as tmp:
        tmp.write(payload)
        tmp.flush()
        os.fsync(tmp.fileno())
        tempname = tmp.name
    try:
        os.replace(tempname, output)
    finally:
        if os.path.exists(tempname):
            os.unlink(tempname)
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", dir=output.parent, prefix=".lineage-", delete=False) as tmp:
        json.dump({"version": snapshot["version"], "arms": lineage}, tmp, separators=(",", ":"))
        tmp.flush()
        os.fsync(tmp.fileno())
        lineage_temp = tmp.name
    try:
        os.replace(lineage_temp, lineage_path)
    finally:
        if os.path.exists(lineage_temp):
            os.unlink(lineage_temp)
    return snapshot


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, help="Research artifact root, read only")
    parser.add_argument("--output", required=True, help="Atomic public snapshot output")
    parser.add_argument("--metrics-manifest", help="Explicit allowlisted metric sources")
    parser.add_argument("--no-engineering", action="store_true")
    parser.add_argument("--telemetry-input", help="Verified bounded checkpoint projection")
    parser.add_argument("--active-grant", help="Trusted exact active grant projection")
    args = parser.parse_args()
    snapshot = export(args.root, args.output, args.metrics_manifest, not args.no_engineering, args.telemetry_input, args.active_grant)
    print(f"snapshot v{snapshot['version']} data_at={snapshot['data_at']}")


if __name__ == "__main__":
    main()
