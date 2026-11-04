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
MAX_POINTS = 512
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


def merge_metric_point(old, new):
    """A newer row may add finite values but cannot erase a verified point time."""
    return {"decisions": new["decisions"], "at": new["at"] or old["at"],
            "values": {key: (value if value is not None else old["values"][key])
                       for key, value in new["values"].items()}}


def training_metric_points(metrics, checkpoint_decisions):
    """Use only the metric row's own decision count; no synthetic row times."""
    history = metrics.get("update_history")
    if history is not None and (not isinstance(history, list) or len(history) > MAX_POINTS):
        raise ValueError("invalid metric history")
    rows = list(history or [])
    if isinstance(metrics.get("last_completed_update"), dict):
        rows.append(metrics["last_completed_update"])
    points = {}
    prior_update = -1
    prior_steps = -1
    for raw in rows:
        if not isinstance(raw, dict):
            raise ValueError("invalid metric row")
        update, steps = integer(raw.get("update")), integer(raw.get("env_steps"))
        if steps is None or steps > checkpoint_decisions or (raw is not metrics.get("last_completed_update") and update is None):
            raise ValueError("metric row exceeds checkpoint")
        if raw is not metrics.get("last_completed_update"):
            if update <= prior_update or steps <= prior_steps:
                raise ValueError("metric history is not increasing")
            prior_update, prior_steps = update, steps
        at = raw.get("at")
        if at is not None:
            try:
                parsed_at = dt.datetime.fromisoformat(at.replace("Z", "+00:00"))
                if parsed_at.tzinfo is None:
                    raise ValueError
            except (TypeError, AttributeError, ValueError):
                raise ValueError("invalid per-update time") from None
        point = metric_point({**raw, "decisions": steps})
        if point and any(value is not None for value in point["values"].values()):
            points[steps] = merge_metric_point(points[steps], point) if steps in points else point
    return [points[key] for key in sorted(points)]


def export(root, output, manifest=None, include_engineering=True, telemetry_input=None, active_grant=None, accepted_metrics=None, history=None, accepted_evaluations=None):
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
    live_telemetry_seen = False
    active_grants = []
    if active_grant and Path(active_grant).is_file():
        handoff = read_json(Path(active_grant))
        if not isinstance(handoff, dict):
            raise ValueError("invalid active grant projection")
        if handoff == {"schema_version": 1, "active": False}:
            active_grants = []
        elif handoff.get("schema_version") == 1:
            active_grants = [handoff]
        elif handoff.get("schema_version") == 2 and set(handoff) == {"schema_version", "grants"} and isinstance(handoff["grants"], list) and len(handoff["grants"]) <= 10:
            active_grants = handoff["grants"]
        else:
            raise ValueError("invalid active grant schema")
    grant_fields = {"schema_version", "volume_id", "job_key", "launch_token", "packet_hash", "fence", "identity", "deadline"}
    grants_by_job = {}
    for grant in active_grants:
        if not isinstance(grant, dict) or set(grant) != grant_fields or grant.get("schema_version") != 1 or not isinstance(grant.get("identity"), dict):
            raise ValueError("invalid active grant fields")
        if formal_arm(grant["identity"].get("experiment_id")) is None or grant["identity"].get("kind") != "train":
            continue
        if grant["job_key"] in grants_by_job:
            raise ValueError("duplicate active grant")
        grants_by_job[grant["job_key"]] = grant
    telemetry_items = []
    if telemetry_input and Path(telemetry_input).is_file():
        projection = read_json(Path(telemetry_input))
        if isinstance(projection, dict) and projection.get("schema_version") == 2:
            if set(projection) != {"schema_version", "items"} or not isinstance(projection["items"], list) or len(projection["items"]) > 3:
                raise ValueError("invalid multi-telemetry projection")
            telemetry_items = projection["items"]
        elif isinstance(projection, dict):
            telemetry_items = [projection]
        else:
            raise ValueError("invalid telemetry projection")
    telemetry_keys = {"job_key", "experiment_id", "config_hash", "grant_fence", "receipt_fence", "receipt_status", "observed_at", "checkpoint_at", "manifest_hash", "identity", "metadata", "metrics"}
    seen_telemetry = set()
    for telemetry in telemetry_items:
        if not isinstance(telemetry, dict) or set(telemetry) != telemetry_keys:
            raise ValueError("invalid telemetry projection")
        job_key = telemetry["job_key"]
        if job_key in seen_telemetry:
            raise ValueError("duplicate telemetry job")
        seen_telemetry.add(job_key)
        grant = grants_by_job.get(job_key)
        if grant is None:
            continue  # verified projection from an attempt no longer active
        if telemetry["receipt_status"] not in ("running", "succeeded", "failed"):
            raise ValueError("invalid telemetry receipt status")
        if telemetry["receipt_status"] == "failed":
            continue
        if (telemetry["grant_fence"] != telemetry["receipt_fence"] or telemetry["grant_fence"] != grant["fence"]
                or integer(telemetry["grant_fence"]) is None or telemetry["grant_fence"] < 1):
            raise ValueError("telemetry fence mismatch")
        if not isinstance(telemetry["manifest_hash"], str) or not re.fullmatch(r"[0-9a-f]{64}", telemetry["manifest_hash"]):
            raise ValueError("invalid telemetry manifest hash")
        identity, metadata, metrics = telemetry["identity"], telemetry["metadata"], telemetry["metrics"]
        if not all(isinstance(x, dict) for x in (identity, metadata, metrics)):
            raise ValueError("invalid telemetry fields")
        for key in ("job_key", "experiment_id", "config_hash"):
            if identity.get(key) != telemetry[key] or grant["identity"].get(key) != telemetry[key]:
                raise ValueError("telemetry identity mismatch")
        if metadata.get("result_label") != "capacity":
            raise ValueError("telemetry is not formal capacity data")
        terminal = telemetry["receipt_status"] == "succeeded"
        if metadata.get("termination") not in (("segment", "milestone") if terminal else ("checkpoint",)):
            raise ValueError("telemetry termination mismatch")
        exp = next((e for e in experiments if isinstance(e, dict) and e.get("id") == telemetry["experiment_id"]), None)
        jobs = status.get("active_jobs", [])
        job = next((j for j in jobs if isinstance(j, dict) and j.get("id") == job_key), None) if isinstance(jobs, list) else None
        if not exp or exp.get("config_hash") != telemetry["config_hash"]:
            continue
        accepted = False
        if terminal and isinstance(exp.get("checkpoint"), str):
            try:
                ref = json.loads(exp["checkpoint"])
                accepted = ref.get("job_key") == job_key and ref.get("manifest_hash") == telemetry["manifest_hash"]
            except (ValueError, TypeError):
                pass
        if not (job and job.get("kind") == "train" and job.get("state") in ("intent", "running", "uncertain", "external_reserved")) and not accepted:
            continue
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
            continue
        checkpoint_decisions = integer(metadata.get("decisions"))
        if checkpoint_decisions is None:
            raise ValueError("invalid checkpoint decisions")
        if checkpoint_decisions < arms[arm]["decisions"]:
            continue  # old partial from an already accepted segment
        arms[arm]["points"].extend(training_metric_points(metrics, checkpoint_decisions))
        arms[arm]["decisions"] = checkpoint_decisions
        arms[arm]["stage"] = "real"
        if not terminal:
            live_telemetry_seen = True
        if checkpoint_at:
            checkpoint_source_ts = max(checkpoint_source_ts or 0, checkpoint_at.timestamp())
    if accepted_metrics and Path(accepted_metrics).is_file():
        accepted_projection = read_json(Path(accepted_metrics))
        if (not isinstance(accepted_projection, dict) or set(accepted_projection) != {"schema_version", "items"}
                or accepted_projection["schema_version"] != 1 or not isinstance(accepted_projection["items"], list)
                or len(accepted_projection["items"]) > 3):
            raise ValueError("invalid accepted metric projection")
        seen_arms = set()
        for accepted_item in accepted_projection["items"]:
            fields = {"experiment_id", "config_hash", "job_key", "manifest_hash", "checkpoint_at", "metadata", "metrics"}
            if not isinstance(accepted_item, dict) or set(accepted_item) != fields:
                raise ValueError("invalid accepted metric item")
            arm = formal_arm(accepted_item["experiment_id"])
            if arm is None or arm in seen_arms:
                raise ValueError("invalid or duplicate accepted arm")
            seen_arms.add(arm)
            exp = next((e for e in experiments if isinstance(e, dict) and e.get("id") == accepted_item["experiment_id"]), None)
            if not exp or exp.get("config_hash") != accepted_item["config_hash"]:
                continue  # stale prior experiment/config projection
            try:
                ref = json.loads(exp.get("checkpoint") or "null")
            except (ValueError, TypeError):
                ref = None
            if (not isinstance(ref, dict) or ref.get("job_key") != accepted_item["job_key"]
                    or ref.get("manifest_hash") != accepted_item["manifest_hash"]):
                continue  # no longer the ledger-accepted head
            metadata, metrics = accepted_item["metadata"], accepted_item["metrics"]
            if not isinstance(metadata, dict) or not isinstance(metrics, dict):
                raise ValueError("invalid accepted fields")
            if metadata.get("result_label") != "capacity" or metadata.get("termination") not in ("segment", "milestone", "checkpoint"):
                raise ValueError("accepted result provenance mismatch")
            accepted_decisions = integer(metadata.get("decisions"))
            if accepted_decisions is None or accepted_decisions != integer(exp.get("decisions")):
                raise ValueError("accepted decision count mismatch")
            at = accepted_item["checkpoint_at"]
            if at is not None:
                try:
                    stamp = dt.datetime.fromisoformat(at.replace("Z", "+00:00"))
                    if stamp.tzinfo is None or stamp.timestamp() > now + 300:
                        raise ValueError
                except (AttributeError, TypeError, ValueError):
                    raise ValueError("invalid accepted checkpoint time") from None
                checkpoint_source_ts = max(checkpoint_source_ts or 0, stamp.timestamp())
            arms[arm]["points"].extend(training_metric_points(metrics, accepted_decisions))
            arms[arm]["decisions"] = max(arms[arm]["decisions"], accepted_decisions)
            arms[arm]["stage"] = "real"
    if history and Path(history).is_file():
        backfill = read_json(Path(history))
        fields = {"schema_version", "experiment_id", "config_hash", "job_key", "terminal", "points"}
        if not isinstance(backfill, dict) or set(backfill) != fields or backfill["schema_version"] != 1:
            raise ValueError("invalid verified history projection")
        arm = formal_arm(backfill["experiment_id"])
        terminal = backfill["terminal"]
        if arm is None or not isinstance(terminal, dict) or set(terminal) != {"generation", "manifest_hash", "updates", "decisions"}:
            raise ValueError("invalid history lineage")
        exp = next((e for e in experiments if isinstance(e, dict) and e.get("id") == backfill["experiment_id"]), None)
        if exp and exp.get("config_hash") == backfill["config_hash"]:
            try:
                ref = json.loads(exp.get("checkpoint") or "null")
            except (ValueError, TypeError):
                ref = None
            if (isinstance(ref, dict) and ref.get("job_key") == backfill["job_key"]
                    and ref.get("generation") == terminal["generation"]
                    and ref.get("manifest_hash") == terminal["manifest_hash"]):
                updates, decisions = integer(terminal["updates"]), integer(terminal["decisions"])
                if updates is None or decisions is None or decisions != integer(exp.get("decisions")):
                    raise ValueError("history terminal count mismatch")
                rows = backfill["points"]
                if not isinstance(rows, list) or len(rows) > MAX_POINTS:
                    raise ValueError("invalid history point count")
                old_update, old_steps = -1, -1
                permitted = {"progress_mean", "entropy", "approx_kl", "value_loss", "env_steps_per_s", "episodes", "wins", "floor_mean", "clipfrac", "content_gap", "grad_norm", "illegal", "policy_loss", "sim_error"}
                points_by_steps = {}
                for row in rows:
                    if not isinstance(row, dict) or set(row) != {"update", "env_steps", "at", "source", "metrics"}:
                        raise ValueError("invalid history row")
                    update, steps = integer(row["update"]), integer(row["env_steps"])
                    if (update is None or steps is None or not old_update < update <= updates
                            or not old_steps < steps <= decisions or row["at"] is not None
                            or row["source"] not in ("stdout", "partial_metrics", "terminal_metrics")
                            or not isinstance(row["metrics"], dict) or not set(row["metrics"]).issubset(permitted)):
                        raise ValueError("invalid history source or count")
                    if any(number(value) is None for value in row["metrics"].values()):
                        raise ValueError("nonfinite history metric")
                    old_update, old_steps = update, steps
                    point = metric_point({**row["metrics"], "env_steps": steps, "at": None})
                    if point and any(value is not None for value in point["values"].values()):
                        points_by_steps[steps] = point
                arms[arm]["points"].extend(points_by_steps.values())
    if accepted_evaluations and Path(accepted_evaluations).is_file():
        eval_projection = read_json(Path(accepted_evaluations))
        if (not isinstance(eval_projection, dict) or set(eval_projection) != {"schema_version", "items"}
                or eval_projection["schema_version"] != 1 or not isinstance(eval_projection["items"], list)
                or len(eval_projection["items"]) > 12):
            raise ValueError("invalid accepted evaluation projection")
        for arm_view in arms.values():
            arm_view["evaluations"] = []
        for item in eval_projection["items"]:
            fields = {"experiment_id", "config_hash", "job_key", "manifest_hash", "checkpoint_at", "metadata", "metrics"}
            if not isinstance(item, dict) or set(item) != fields:
                raise ValueError("invalid accepted evaluation item")
            arm = formal_arm(item["experiment_id"])
            if arm is None:
                raise ValueError("unknown evaluation arm")
            exp = next((e for e in experiments if isinstance(e, dict) and e.get("id") == item["experiment_id"]), None)
            if not exp or exp.get("config_hash") != item["config_hash"]:
                continue
            if not isinstance(item["job_key"], str) or not isinstance(item["manifest_hash"], str) or not re.fullmatch(r"[0-9a-f]{64}", item["manifest_hash"]):
                raise ValueError("invalid evaluation provenance")
            metadata, metrics = item["metadata"], item["metrics"]
            if not isinstance(metadata, dict) or not isinstance(metrics, dict):
                raise ValueError("invalid evaluation fields")
            if metadata.get("result_label") != "capacity" or metadata.get("termination") != "completed":
                raise ValueError("evaluation result label mismatch")
            decisions = integer(metadata.get("decisions"))
            accepted_decisions = integer(exp.get("decisions"))
            games, wins = integer(metrics.get("games")), integer(metrics.get("wins"))
            if (decisions is None or accepted_decisions is None or decisions > accepted_decisions
                    or games is None or games < 1 or wins is None or wins > games):
                raise ValueError("invalid evaluation denominator")
            at = item["checkpoint_at"]
            if at is not None:
                try:
                    stamp = dt.datetime.fromisoformat(at.replace("Z", "+00:00"))
                    if stamp.tzinfo is None or stamp.timestamp() > now + 300:
                        raise ValueError
                except (AttributeError, TypeError, ValueError):
                    raise ValueError("invalid evaluation time") from None
                at = stamp.astimezone(dt.timezone.utc).isoformat(timespec="seconds").replace("+00:00", "Z")
            reached = metrics.get("act_reached")
            if not isinstance(reached, dict):
                raise ValueError("invalid act reached counts")
            values = {name: None for name in METRICS}
            progress = number(metrics.get("progress_mean"))
            win_rate = number(metrics.get("win_rate"))
            if progress is not None and not 0 <= progress <= 1:
                raise ValueError("invalid evaluation progress")
            if win_rate is not None and not 0 <= win_rate <= 1:
                raise ValueError("invalid evaluation win rate")
            values["progress"] = progress
            values["win_rate"] = win_rate
            for index in (1, 2, 3):
                count = reached.get(str(index))
                if count is not None:
                    count = integer(count)
                    if count is None or count > games:
                        raise ValueError("invalid act reach count")
                    values[f"act{index}_reach"] = count / games
            arms[arm]["evaluations"].append({"decisions": decisions, "at": at, "values": values})
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
    active_jobs = status.get("active_jobs", [])
    if not isinstance(active_jobs, list):
        raise ValueError("invalid active job status")
    active_train_ids = {j.get("id") for j in active_jobs if isinstance(j, dict) and j.get("kind") == "train"}
    running_arms = sorted({formal_arm(g["identity"].get("experiment_id")) for g in active_grants
                           if isinstance(g, dict) and g.get("job_key") in active_train_ids
                           and isinstance(g.get("identity"), dict)
                           and formal_arm(g["identity"].get("experiment_id"))})
    waiting_arms = sorted({formal_arm(e.get("id")) for e in experiments
                           if isinstance(e, dict) and e.get("stage") == "await_decision" and formal_arm(e.get("id"))})
    if running_arms:
        formal["status"] = "active"
        formal["current_task"] = "训练中 " + "/".join(running_arms) + ("；等待决策 " + "/".join(waiting_arms) if waiting_arms else "") + ("；遥测未验收，指标可滞后一批" if live_telemetry_seen else "")
    elif active_train_ids:
        formal["status"] = "active"
        formal["current_task"] = "训练任务运行中，等待遥测"
    elif any(isinstance(j, dict) and j.get("kind") == "eval" for j in active_jobs):
        formal["status"] = "active"
        formal["current_task"] = "正式评测中"
    elif any(isinstance(j, dict) and j.get("kind") == "research" for j in active_jobs):
        formal["status"] = "active"
        formal["current_task"] = "研究分析中"
    elif waiting_arms:
        formal["status"] = "pending"
        formal["current_task"] = "等待研究决策 " + "/".join(waiting_arms)
    elif experiments:
        formal["status"] = "pending"
        formal["current_task"] = "等待任务调度"
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
                        by_decisions = {p.get("decisions"): ({**p} if old_lineage.get("time_mode") == "per_update" else {**p, "at": None})
                                        for p in old if isinstance(p, dict) and integer(p.get("decisions")) is not None}
                        for point in new:
                            key = point.get("decisions")
                            if key is not None:
                                by_decisions[key] = merge_metric_point(by_decisions[key], point) if key in by_decisions else point
                        arm_view["points"] = sorted(by_decisions.values(), key=lambda p: p["decisions"])[-MAX_POINTS:]
        except (ValueError, KeyError, TypeError, json.JSONDecodeError):
            pass
    for arm_view in formal["arms"].values():
        deduped = {}
        for point in arm_view["points"]:
            key = point.get("decisions")
            if key is not None:
                deduped[key] = merge_metric_point(deduped[key], point) if key in deduped else point
        arm_view["points"] = [deduped[key] for key in sorted(deduped)][-MAX_POINTS:]
    output.parent.mkdir(parents=True, exist_ok=True)
    if output.exists():
        try:
            prior = read_json(output)
            snapshot["version"] = max(integer(prior.get("version")) or 0, 0) + 1
            old_time = dt.datetime.fromisoformat(prior["generated_at"].replace("Z", "+00:00")).timestamp()
            snapshot["generated_at"] = utc(max(now, old_time + 1))
            old_data_time = dt.datetime.fromisoformat(prior["data_at"].replace("Z", "+00:00")).timestamp()
            new_data_time = dt.datetime.fromisoformat(snapshot["data_at"].replace("Z", "+00:00")).timestamp()
            snapshot["data_at"] = utc(max(old_data_time, new_data_time))
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
        json.dump({"version": snapshot["version"], "arms": lineage, "time_mode": "per_update"}, tmp, separators=(",", ":"))
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
    parser.add_argument("--accepted-metrics", help="Root verified accepted train metric projection")
    parser.add_argument("--history", help="Verified sparse old-run history projection")
    parser.add_argument("--accepted-evaluations", help="Root verified development evaluation projection")
    args = parser.parse_args()
    snapshot = export(args.root, args.output, args.metrics_manifest, not args.no_engineering, args.telemetry_input, args.active_grant, args.accepted_metrics, args.history, args.accepted_evaluations)
    print(f"snapshot v{snapshot['version']} data_at={snapshot['data_at']}")


if __name__ == "__main__":
    main()
