"""Project accepted M terminal and comparison evidence into the existing monitor schema."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sys

OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[3]
MONITOR = ROOT / "research/task119-monitor"
BASE = OUT.parent / "v1-s-final-dev"
sys.path.insert(0, str(MONITOR / "integration"))
from project_live import project, read_json

SECTIONS = {
    "history": "history/experiments.json",
    "architecture": "architecture-performance/architecture.json",
    "performance": "architecture-performance/performance.json",
    "budget": "budget/breakdown.json",
}
BASE_PINS = {
    "history": "25fd4c69f46fce9c460281762f4c2c58838b052146a6e8425687c6fd7e6f0b82",
    "architecture": "e11ba09b2f2be04302585120ce9a5ceeeb7b78b5db687594e24736d28bb310e1",
    "performance": "b71c85f663e6875024b6ece05a338482c00ab42cab3f4175abf09bd47c3e6083",
    "budget": "c7dfba2d94dcfe88fed9c5b94a8c64a879be7d51f639097b35f80acb0e2ba405",
}
M_TERMINAL = "research/task116/terminal-root-readback"
SOURCES = {
    "closeout_directive": ("research/operator-finish-existing-and-push-20261107.json",
        "bfec45e178eba632f6bc306bbf7db4f80a1ebf14d600dc1cbc13ca2f01ed261c"),
    "m_train_root": (f"{M_TERMINAL}/root-actual-endpoint-acceptance.json",
        "5ba9aa7d27a41a35ee30a7b538b53c705102fb7d5b260566b3b8ba6e6b912ec7"),
    "m_train_review": ("research/task116/actual-independent-review/terminal-actual-acceptance.json",
        "182ef12703fb9c102012b3e3b0105d5c1604886ce80d2ef209b492e98c143f89"),
    "m_train_inspection": (f"{M_TERMINAL}/inspection.json",
        "2bdc4ec8299935cbd210b76ab47cb0950d783fe222798a6b26cd7ddc5cdb77a6"),
    "m_train_source": (f"{M_TERMINAL}/actual-training-source-projection.json",
        "5e79ce0fb0aa5898fda2bff551afa2c8b2229a495debdb10ea8499a98e4ecca9"),
    "m_train_provider": (f"{M_TERMINAL}/provider-terminal-receipt.json",
        "5a2be25a6237b2aed9f1534d3fb792038e8c40f2b8b43eb87ea0e517398a221c"),
    "m_train_terminal": (f"{M_TERMINAL}/terminal-observation.json",
        "01460eef4a8f87dfeafc6a1ba14ac17ae34ee3ae08402dd51ab0528c4b25ece4"),
    "m_train_finance": (f"{M_TERMINAL}/financial-retirement/execution-and-readback.json",
        "41a5a58723703eeaf2e1bb94b591762af2edc722bf573d8ca65d3c8a1a278cbe"),
    "m_dev_admission": ("research/task116/finaldev-execution-preparation/actual-metadata/metadata-admission.json",
        "5a304349b5087426f86a2d90e25af5b9f1ecba8ab8cbb461a778d0b70c2d18dd"),
    "m_dev_initial_guard": ("research/task116/finaldev-execution-preparation/existing-deployment-resume/initial-claim-rejection.json",
        "024c2384734642155d55fd7577d5c6d808a24dd79bd8c35e09a651010f52472a"),
    "m_dev_submission": ("research/task116/finaldev-execution-preparation/actual-deploy/submission-receipt.json",
        "07879528e5f3cbb0ee2e3b49de83851ed1effe09f80ed0f6e9122821d8044f58"),
    "m_dev_root": ("research/task116/actual-finaldev/root-actual-acceptance.json",
        "3f6b11fac065cbe55cc38cf28c65eca19fdca4dd8c35a63fbbe654f1adb8a2c8"),
    "m_dev_review": ("research/task116/eval-independent-review/actual-finaldev/actual-acceptance.json",
        "73c3e853b386323541cffd5e833706ed443ca1ac1026c9753bbc12a5ab540cfb"),
    "m_dev_ready": ("research/task116/actual-finaldev/ready.json",
        "a3160eb1c54d63a6bf096a405cecd2320b45deb3e0715be92c41bde7a0d58e8b"),
    "m_dev_archive": ("research/task116/actual-finaldev/archive-verification.json",
        "188e39d9ac8ab43973cba2716c95d72d8698a0dbf6c644601a19fe71d2024bfa"),
    "m_dev_finance": ("research/task116/actual-finaldev/financial-closeout/execution-and-readback.json",
        "abc4a13168340a150192be57e54365995aa86bcf062904655f1bb1fe5fae019c"),
    "task121_result": ("research/task121-current-comparison-actual/analysis-result.json",
        "e11a200956b8cbc6289e6310c3b11fca12b5c777d9b25a98df0e3b47285100fb"),
    "task121_execution": ("research/task121-current-comparison-actual/execution-receipt.json",
        "b8b8393f5884acfb9d3a3b501f8284d4f025cac7746ee456ce05bb1c2795f24b"),
    "task121_review": ("research/task121-current-comparison-actual/independent-review.json",
        "6e3c2794f25bcf7e74c5e05e254ac602891631562f51b28d860c795edd807d5f"),
    "task121_root": ("research/task121-current-comparison-actual/root-acceptance.json",
        "aadee00451127c04ba572b3d698a1192f07ea9aad0a3a29d5b1c49892ce52e72"),
    "final_quiescence": ("research/task116/finaldev-execution-preparation/final-quiescence-20261107T053244Z.json",
        "8bb4ab54db4497c7d8fa7921fb93fef0345f423ab2a1aca37c370a9599b90846"),
}
BASE_LIVE_SHA = 'bfc293524a7f6496d8ef6de67ac2f82010119cd4eb8fc29bdb166e64c95829f5'
PAUSE_SCOPE = "完成既有S/M训练及各自原定dev和本次对照，完成监控复盘与Git收尾后暂停。117、118、120、blind、新训练、新探针和改进均延期。"


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n", encoding="utf8")


def source_refs(*names):
    return [{"path": SOURCES[name][0], "sha256": SOURCES[name][1],
             "bytes": (ROOT / SOURCES[name][0]).stat().st_size, "source_id": name} for name in names]


def retire_slot(budget, receipt, slot_name, cents):
    assert receipt["status"] == "VERIFIED"
    event = receipt["after"]["terminal_event"]
    payload = event["payload"]
    assert payload["slot"] == slot_name
    assert payload["completed_allocation_cents"] == cents and payload["actual_cost"] is None
    slot = next(slot for slot in budget["direct_plan_slots"]
                if slot["plan_event"] == payload["plan_event"] and slot["slot"] == slot_name)
    assert slot["active_cents"] == cents
    slot.update(active_cents=0, historical_maximum_cents=cents, terminal_event=event["id"])


def project_comparison(history, m, s_dev, m_point, evidence):
    result = evidence["task121_result"]
    assert result["status"] == "completed"
    bootstrap = result["bootstrap_receipt"]
    assert bootstrap["replicates"] == 20000 and bootstrap["n"] == 256
    contrast = bootstrap["contrasts"]["M116_minus_S115_S2"]
    endpoints = contrast["endpoints"]
    progress = endpoints["progress"]
    accepted = evidence["task121_root"]
    assert accepted["status"] == "ROOT_ACCEPTED_ONCE_DESCRIPTIVE_CURRENT_ENDPOINT_COMPARISON"
    assert accepted["analysis_result_sha256"] == SOURCES["task121_result"][1]
    assert accepted["independent_review_sha256"] == SOURCES["task121_review"][1]
    assert accepted["progress_M_minus_S"] == progress
    assert accepted["shared_bootstrap_index_sha256"] == bootstrap["index_sha256_little_endian_int64"]
    assert not accepted["goal_achieved"] and not accepted["new_training_or_improvement_authorized"]
    dose_fields = ("cumulative_decisions", "current_call_new_decisions", "study_new_decisions", "total_trainable_params")
    summaries = {}
    for arm, point in (("S115_S2", s_dev), ("M116", m_point)):
        row = result["arms"][arm]
        assert row["dose"]["cumulative_decisions"] == point["cumulative_decisions"]
        assert row["summary"]["mean_protocol_progress"] == point["mean_progress"]
        assert row["summary"]["games"] == point["n"]
        summaries[arm] = {field: row["dose"][field] for field in dose_fields}
    q = evidence["final_quiescence"]
    assert q["status"] == "PASS" and q["task119_units_inactive"]
    assert q["all_owned_functions_quiet"] and q["observers_quiet"] and q["old_timers_quiet"]
    history["current_endpoint_comparison"] = {
        "comparison_id": "task121-current-endpoints", "status": "root_accepted_once_analysis",
        "contrast": "M116_minus_S115_S2", "n": contrast["n"], "replicates": bootstrap["replicates"],
        "dose_by_arm": summaries,
        "endpoints": {name: {key: metric[key] for key in ("mean_delta", "ci95", "improved", "worse", "tied")}
                      for name, metric in endpoints.items()},
        "source_refs": source_refs("task121_root", "task121_review", "task121_result", "task121_execution"),
        "limits": ["单次、不同剂量的训练谱系，容量效应与训练复现方差未知。",
                   "区间描述同一已使用开发集上的局级差异，blind表现未知。"],
    }
    m["conclusion"] += (
        f"Task121以M减S计算，平均进度差{progress['mean_delta']:+.8f}，"
        f"配对95%区间[{progress['ci95'][0]:+.8f}, {progress['ci95'][1]:+.8f}]。"
        f"M有{progress['improved']}局进度较高、{progress['worse']}局较低、{progress['tied']}局持平。"
        "S累计695,861,248决策、M累计100,007,936决策，训练谱系与剂量不同。"
        "现有训练与原定评测已结束，按用户要求收尾。"
    )
    m["timeline"] += [
        {"event": "运行资源静止检查", "at_utc": q["observed_end_utc"],
         "detail": f"所查{len(q['owned_function_stats'])}个函数的输入、运行器、积压均为0。旧定时器与监控推送已停。"},
        {"event": "Task121配对对照验收", "at_utc": accepted["at"],
         "detail": "已验收同一开发集的两端点对照，使用既有评测结果。"},
    ]
    m["sources"] += source_refs("task121_root", "task121_review", "task121_result", "task121_execution", "final_quiescence")
    history["closeout"] = {
        "status": "existing_training_evaluation_and_comparison_complete",
        "quiescence_at_utc": q["observed_end_utc"], "quiet_function_count": len(q["owned_function_stats"]),
        "goal_achieved": False, "blind_used": False, "new_training_authorized": False,
        "scope": PAUSE_SCOPE, "source_refs": source_refs("final_quiescence", "closeout_directive"),
    }
    history["as_of_note"] = "S115与M116终态和原定dev已验收。Task121一次配对对照已验收，既有训练与评测结束。"


def main():
    now = datetime.now(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")
    data = {name: read_json(BASE / path, expected_sha256=BASE_PINS[name]) for name, path in SECTIONS.items()}
    evidence = {name: read_json(ROOT / path, expected_sha256=sha) for name, (path, sha) in SOURCES.items()}
    history = data["history"]
    m = next(record for record in history["experiments"] if record["experiment_id"] == "task116-m")
    train = evidence["m_train_root"]
    raw = evidence["m_train_terminal"]
    m_live = project(raw, "M116")
    provider = evidence["m_train_provider"]
    trainer_status = raw["closed_metadata"]["run/status.json"]["body"]
    assert train["actual_terminal_accepted"] and train["formal_dev256_result"] is None
    assert m_live["status"] == "terminal" and m_live["decisions"] == train["checkpoint_steps"] == 100007936
    assert m_live["updates"] == train["checkpoint_updates"] == 763
    assert provider["provider_terminal_verified"] and provider["observed_at"] == raw["at"]
    assert evidence["m_train_review"]["status"] == "PASS_FULL_FRESH_M100_ENDPOINT"
    dose, updates = train["checkpoint_steps"], train["checkpoint_updates"]

    m.update(status="completed", status_detail="training_completed_original_dev_pending",
             status_as_of_utc=train["recorded_at"], submitted_at_utc=m_live["started_at"],
             actual_dose_display=f"累计及本实验新增均为{dose:,}决策，{updates:,}次更新",
             conclusion="M116由随机初始化完成全部100,007,936新增决策，终态检查点已验收。")
    m["actual"].update(cumulative_decisions=dose, additional_decisions=dose, updates=updates,
        accepted_cumulative_decisions=dose, accepted_additional_decisions=dose, accepted_updates=updates,
        status_updated_at_utc=m_live["progress_health"]["observed_at"],
        counter_basis="root_accepted_full_terminal_checkpoint", terminal=True)
    m["checkpoint"] = {
        "kind": "root_and_independent_terminal_body_verified", "sha256": train["checkpoint_sha256"],
        "bytes": train["checkpoint_size"], "updates": updates, "cumulative_decisions": dose,
    }
    m["actual_calls"][0]["status"] = "completed"
    m["evidence_completeness"]["current_status_is_snapshot"] = False
    m["timeline"] += [
        {"event": "provider_terminal_observed", "at_utc": m_live["observed_at"], "cumulative_decisions": dose},
        {"event": "terminal_checkpoint_accepted", "at_utc": train["recorded_at"], "cumulative_decisions": dose},
    ]
    guard = evidence["m_dev_initial_guard"]
    assert guard["status"] == "FAILED_BEFORE_CLAIM_NO_SPAWN" and guard["canonical_dispatch_count"] == 0
    m["timeline"].append({
        "event": "原定评测派发前检查",
        "at_utc": datetime.fromtimestamp(guard["observed_at"], timezone.utc).isoformat(),
        "detail": "首次提交在派发前被检查拦截，评测未启动。后续沿用原部署和原定一次评测额度。",
    })
    dev_submit = evidence["m_dev_submission"]
    assert dev_submit["status"] == "submitted_not_yet_started_verified"
    m["timeline"].append({
        "event": "原定正式评测提交",
        "at_utc": datetime.fromtimestamp(dev_submit["submitted_at"], timezone.utc).isoformat(),
        "detail": "沿用原部署，已提交原定唯一dev256。提交回执独立于正式评测结果。",
    })
    m["actual_calls"].append({"provider_id": dev_submit["call_id"], "purpose": "original_formal_dev256",
                              "status": "submitted_pending_acceptance"})
    m["sources"] += source_refs("m_train_root", "m_train_review", "m_train_inspection", "m_train_source",
                                 "m_train_provider", "m_dev_initial_guard", "m_dev_submission", "closeout_directive")
    dev = evidence["m_dev_root"]
    ready = evidence["m_dev_ready"]
    archive = evidence["m_dev_archive"]
    assert dev["status"] == "ROOT_ACCEPTED_VALID_COMPLETE_ORIGINAL_DEV256"
    assert dev["call_id"] == dev_submit["call_id"] == archive["call_id"]
    assert dev["checkpoint"]["sha256"] == train["checkpoint_sha256"] == ready["checkpoint_sha256"]
    assert dev["checkpoint"]["env_steps"] == dose and dev["checkpoint"]["updates"] == updates
    assert archive["all_hashes_verified"] and archive["file_count"] == 264
    assert archive["ready_sha256"] == SOURCES["m_dev_ready"][1]
    assert dev["independent_acceptance_sha256"] == SOURCES["m_dev_review"][1]
    metrics = dev["metrics"]
    assert metrics["games"] == metrics["replayable"] == 256 and not dev["source_protocol"]["blind"]
    assert dev["source_protocol"]["policy"] == "greedy"
    assert not dev["source_protocol"]["search"] and not dev["source_protocol"]["loop_breaker"]
    s_dev = next(point for point in history["formal_evaluations"] if point["evaluation_id"] == "task115-final-dev")
    assert s_dev["protocol"]["seed_manifest_sha256"] == dev["source_protocol"]["dev_sha256"]
    m_point = {
        "evaluation_id": "task116-final-dev", "experiment_id": "task116-m", "source_record_id": "task116-m",
        "cumulative_decisions": dose, "mean_progress": metrics["progress_mean"], "wins": metrics["wins"],
        "n": metrics["games"], "win_rate": dev["win_rate"], "act2_reach": dev["reach_act2_or_later"],
        "act3_reach": dev["reach_act3"], "highest_floor": metrics["floor_max"],
        "reach_count_semantics": "inclusive terminal Act2 or later; Act3 separately",
        "protocol": dict(s_dev["protocol"]), "training_reward_used": False, "source": source_refs("m_dev_root")[0],
    }
    history["formal_evaluations"].append(m_point)
    history["counts"]["distinct_formal_dev_points"] = len(history["formal_evaluations"])
    m.update(status_detail="completed_with_original_dev256", status_as_of_utc=dev["recorded_at"],
             formal_eval_points=[m_point], formal_eval_ids=[m_point["evaluation_id"]])
    m["actual_calls"][-1]["status"] = "completed"
    m["timeline"].append({"event": "formal_eval_accepted", "at_utc": dev["recorded_at"],
                          **{key: m_point[key] for key in ("evaluation_id", "cumulative_decisions", "mean_progress", "wins", "n")}})
    m["sources"] += source_refs("m_dev_root", "m_dev_review", "m_dev_ready", "m_dev_archive", "m_dev_finance")
    m["conclusion"] += (
        f"原定正式dev256平均进度{metrics['progress_mean']}，{metrics['wins']}/{metrics['games']}胜，"
        f"二幕及以后{m_point['act2_reach']}/{metrics['games']}，三幕{m_point['act3_reach']}/{metrics['games']}，"
        f"最高{m_point['highest_floor']}层。"
    )
    m["uncertainty"] += ["正式评测导出终态健康计数，运行中的瞬态标志未知。", PAUSE_SCOPE]
    m["issues_recovery"].append("完整训练记录含546,465个闭合训练局、0个训练胜局，8项训练健康计数均为0。正式dev样本另列。")
    m["training_parameters"] = train["base_parameters"] + train["aux_parameters"]
    assert m["training_parameters"] == 16327636
    project_comparison(history, m, s_dev, m_point, evidence)
    history["generated_at_utc"] = now
    for record in history["planned"]:
        if record.get("execution_status") == "deferred_by_user":
            record["sources"] += source_refs("closeout_directive")

    performance = data["performance"]
    m_perf = next(experiment for experiment in performance["experiments"] if experiment["experiment_id"] == "task116-m")
    window = evidence["m_train_inspection"]["windows"]["all"]
    assert window["window_complete"] and window["observed_updates"] == updates
    phases = {target: window["timing_seconds"][source]["value"] for target, source in {
        "environment_s": "t_env_s", "inference_host_interval_s": "t_infer_s", "learner_update_s": "t_update_s",
        "checkpoint_phase_s": "t_ckpt_s", "iteration_total_s": "t_total_s",
    }.items()}
    assert phases["iteration_total_s"] == train["training_iteration_seconds"] == 14170.225
    assert len(window["health_counts"]) == 8 and all(v["value"] == 0 for v in window["health_counts"].values())
    phases.update(sync_s=None, network_publish_s=None)
    m_perf["source_refs"] += source_refs("m_train_source", "m_train_root")
    m_perf["phase_aggregates"].append({
        "kind": "complete_fresh100m_summary", "segment_id": "fresh100M", "first_update": 1,
        "last_update": updates, "observed_decisions": dose, "rows": updates, "phase_seconds": phases,
        "coverage": "M116完整763个更新周期，单H100、32 vCPU，16,327,636个训练参数。",
        "reported_session_decisions_per_s": trainer_status["env_steps_per_s_session"],
        "closed_training_episodes": int(window["closed_training_episodes"]["value"]),
        "closed_training_wins": int(window["closed_training_wins"]["value"]),
        "native_table_overflow_slot_steps": int(window["health_counts"]["native_table_overflow"]["value"]),
        "source_refs": source_refs("m_train_inspection", "m_train_source"),
    })
    performance["status_snapshots"].append({
        "experiment_id": "task116-m", "segment_id": "fresh100M", "observed_at_utc": m_live["observed_at"],
        "status_updated_at_utc": m_live["progress_health"]["observed_at"], "cumulative_decisions": dose,
        "experiment_additional_decisions": dose, "session_additional_decisions": dose, "updates": updates,
        "reported_decisions_per_s": trainer_status["env_steps_per_s"],
        "reported_session_decisions_per_s": trainer_status["env_steps_per_s_session"],
        "session_elapsed_s": trainer_status["session_s"], "iteration_wall_s": trainer_status["wall_s"],
        "state": "terminal", "provider_wall_s": None, "source_refs": source_refs("m_train_terminal"),
    })
    performance["checkpoint_points"].append({
        "experiment_id": "task116-m", "segment_id": "fresh100M", "observed_at_utc": train["recorded_at"],
        "updates": updates, "cumulative_decisions_derived_from_update": dose,
        "experiment_additional_decisions_derived": dose, "checkpoint_bytes": train["checkpoint_size"],
        "checkpoint_sha256": train["checkpoint_sha256"], "status": "root_independent_body_verified_terminal",
        "source_refs": source_refs("m_train_root", "m_train_review"),
    })
    performance["latest_observed_status_at_utc"] = m_live["progress_health"]["observed_at"]
    performance["created_at_utc"] = now

    budget = data["budget"]
    finance = evidence["m_train_finance"]
    retire_slot(budget, finance, "train", 3992)
    holds = finance["after"]["remaining"]
    assert sum(slot["active_cents"] for slot in budget["direct_plan_slots"]) == holds["total_direct_commitments_cents"] == 2845
    dev_finance = evidence["m_dev_finance"]
    retire_slot(budget, dev_finance, "final_dev", 744)
    remaining = dev_finance["after"]["remaining"]
    assert sum(slot["active_cents"] for slot in budget["direct_plan_slots"]) == remaining["total_direct_commitments_cents"] == 2101
    for slot in budget["direct_plan_slots"]:
        if slot["active_cents"]:
            assert slot["slot"] in {"global_blind_protection", "shared_blind_increment"}
            slot["reservation_status"] = "deferred_blind_protection_only"
    budget["current_direct_plan_projection"].update(
        active_cents=2101, m116_held_cents=1026, active_dev_commitment_cents=0,
        deferred_blind_protection_cents=2101, source_refs=[
            "s_train_finance", "s_dev_finance", *source_refs("m_train_finance", "m_dev_finance", "closeout_directive")
        ],
    )
    blind = next(record for record in history["planned"] if record["experiment_id"] == "blind-evaluation")
    blind["conclusion"] = "保留未来blind额度21.01美元。该项已延期，当前训练和评测预算槽位均已退役，逐调用费用未知。"
    blind["sources"] += source_refs("m_dev_finance")

    admission = evidence["m_dev_admission"]
    assert admission["status"] == "PASS"
    budget["admission_history"] = [{
        **{key: admission[key] for key in (
            "gross_effective_cents", "net_effective_cents", "existing_plan_commitments_cents",
            "ledger_existing_holds_cents", "archive_commitment_cents",
            "project_room_after_all_existing_holds_cents", "project_cap_cents",
        )},
        "observed_at": datetime.fromtimestamp(admission["observed_at"], timezone.utc).isoformat(),
        "scope": "M原定dev的保守准入基准。精确工作区账单和App小时计量仍各用原采集时刻。",
        "source_refs": source_refs("m_dev_admission"),
    }]

    base_live = read_json(BASE / "initial-live.json", expected_sha256=BASE_LIVE_SHA)
    s_live = next(run for run in base_live["runs"] if run["id"] == "S115-S2")
    assert s_live["status"] == "terminal"
    runs = [m_live, s_live]
    latest = max(run["observed_at"] for run in runs)
    live = {"schema_version": 1, "version": round(datetime.fromisoformat(latest).timestamp() * 1000),
            "observed_at": latest, "runs": runs}

    for name, path in SECTIONS.items():
        write(OUT / path, data[name])
    write(OUT / "initial-live.json", live)
    write(OUT / "input-pins.json", {"base_sections": BASE_PINS, "base_live": BASE_LIVE_SHA,
          "sources": source_refs(*SOURCES)})
    write(OUT / "curated-input-pins.json", {name: sha256(OUT / path) for name, path in SECTIONS.items()})
    write(OUT / "change-summary.json", {
        "schema_version": 1, "generated_at": now, "status": "FINAL_ACCEPTED_DATA_READY_FOR_PUBLICATION",
        "formal_eval_points": len(history["formal_evaluations"]), "direct_plan_active_cents": 2101,
        "scope": PAUSE_SCOPE, "initial_live_mode": "saved_both_training_terminal",
    })
    print(f"Prepared {OUT}")


if __name__ == "__main__":
    main()
