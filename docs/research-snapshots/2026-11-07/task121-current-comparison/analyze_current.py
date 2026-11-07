"""One paired comparison of accepted S115 S2 and M116 current endpoints."""
import argparse
import math
from pathlib import Path
import re

from endpoint_validation import DEV_SHA, checked, endpoint, exclusive, read, sha
from paired_statistics import REPLICATES, SEED, bootstrap_contrasts, paired_deltas
from validation import descriptive_pairs

CODE = Path(__file__).resolve().parent
ARMS = {
    "S115_S2": {
        "study": "task115", "arm": "control_continuation",
        "science": "548320e5ba99de37c8cfbf93031164ab948259003f66c2394d1eff7b152de2fb",
        "study_parent_update": 3783, "from_update": 4546, "target_update": 5309, "full_label": "full200M_terminal",
        "policy_params": 2941072, "transition_params": 37188,
    },
    "M116": {
        "study": "task116", "arm": "fresh_M_current_recipe",
        "science": "edf98387857cec63590d6c4f840ef5b3ed6d748b0c188aec4f4548993c8a6871",
        "study_parent_update": 0, "from_update": 0, "target_update": 763, "full_label": "full100M_terminal",
        "policy_params": 16253584, "transition_params": 74052,
    },
}
M_PROFILE_OVERLAY = {
    "modal_research/transition_contract.py": "cebd63e6468138637b076e431242d4fe54b1adec2fbb0f71e6681d02d8684686",
    "sts2train/transition_gpu_gate.py": "663a19669c7f34583a279cb44e1029055db437ed020225eb83e6cc4420589b8b",
}
BATCH = 131072
PROOF_ROLES = ("root_actual_acceptance", "execution_receipt", "independent_acceptance", "accepted_training_source_proof", "training_provider_terminal")
OBSERVATION_SCOPES = {
    "segment_train_iteration_seconds": "Current segment only, sampled training iterations including rollout and learner",
    "segment_session_wall_seconds": "Current session elapsed, with producer-defined boundaries",
    "cumulative_lineage_train_wall_seconds": "Cumulative training lineage wall, non-additive across ancestors",
    "call_actual_fee_usd": "Actual provider amount for the exact call, if supplied by accepted billing evidence",
    "app_scope_metered_usd": "Provider-reported App scope, with covered time range and other activity limits",
    "segment_compute_estimate_usd": "Current segment compute estimate with stated timing and rate scope",
}

def require(ok, why):
    if not ok:
        raise ValueError(why)

def terminal_dose(name, model, ready, preflight, decision_ref):
    arm = ARMS[name]
    updates, steps = model["updates"], model["decisions"]
    require(type(updates) is int and type(steps) is int and arm["from_update"] < updates <= arm["target_update"] and steps == updates * BATCH, "actual current terminal must advance within its own call target")
    require(isinstance(model.get("checkpoint_file_sha256"), str) and re.fullmatch(r"[a-f0-9]{64}", model["checkpoint_file_sha256"]), "exact endpoint checkpoint SHA required")
    require(type(model.get("bytes")) is int and model["bytes"] > 0, "exact endpoint byte count required")
    full = updates == arm["target_update"]
    terminal = {
        "classification": arm["full_label"] if full else "resource_censored_terminal",
        "final_stop": True, "further_training_authorized": False,
        "midpoint_evaluation": False, "nominal_primary_observed": full,
    }
    for meta in (ready, preflight):
        require(meta.get("science_plan_sha256") == arm["science"] and meta.get("root_terminal_decision_sha256") == decision_ref["sha256"] and meta.get("terminal_evaluation") == terminal, "formal evaluation disposition differs")
    require(preflight.get("arm") == arm["arm"] and preflight.get("strategic_forward_kl", {}).get("beta") == 0, "foreign eval arm or objective")
    require(preflight.get("input_arch") == model["input_arch"] and preflight.get("critic_arch") == model["critic_arch"], "preflight architectures differ")
    if name == "M116":
        expected = {"model_profile": "M-current-recipe", "base_params": arm["policy_params"], "transition_params": arm["transition_params"], "profile_overlay": M_PROFILE_OVERLAY}
        require(all(meta.get(k) == v for meta in (ready, preflight) for k, v in expected.items()), "M profile differs")
    return {
        "cumulative_decisions": steps, "cumulative_updates": updates,
        "current_call_new_decisions": (updates - arm["from_update"]) * BATCH,
        "current_call_new_updates": updates - arm["from_update"],
        "study_new_decisions": (updates - arm["study_parent_update"]) * BATCH,
        "planned_cumulative_decisions": arm["target_update"] * BATCH,
        "policy_params": arm["policy_params"], "transition_params": arm["transition_params"],
        "total_trainable_params": arm["policy_params"] + arm["transition_params"],
        "terminal_evaluation": terminal,
    }

def observation_context(ref, root):
    """Carry root-accepted measurements and provenance without recomputing prices."""
    context = {}
    require(set(ref) == set(OBSERVATION_SCOPES), "explicit context coverage or null required")
    for kind, scope in OBSERVATION_SCOPES.items():
        obs = ref[kind]
        if obs is None:
            context[kind] = None
            continue
        value = obs["value"]
        require(type(value) in (float, int) and math.isfinite(value) and value >= 0, "invalid context measurement")
        require(isinstance(obs.get("boundary_note"), str) and obs["boundary_note"].strip(), "measurement boundary required")
        checked(obs["source"], root, parse=False)
        context[kind] = {**obs, "scope": scope}
    return context

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ("input-binding", "input-binding-sha256", "once-authority", "once-authority-sha256", "research-root", "output"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    root, out = Path(args.research_root), Path(args.output)
    claim = out.with_suffix(out.suffix + ".once")
    if out.exists() or claim.exists():
        raise FileExistsError("prior once evidence exists")
    require(sha(args.input_binding) == args.input_binding_sha256 and sha(args.once_authority) == args.once_authority_sha256, "exact input/authority differs")
    binding, authority = read(args.input_binding), read(args.once_authority)
    pins = CODE / "pins.json"
    freeze = read(pins)
    expected = {
        "allow_once_analysis": True, "input_binding_sha256": args.input_binding_sha256,
        "analysis_pins_sha256": sha(pins), "output": str(out), "prospective_scope_sha256": freeze["prospective_scope_sha256"],
    }
    require(binding.get("status") == "ROOT_BOUND_BOTH_ACCEPTED_CURRENT_FORMAL_DEVS" and authority.get("allow_once_analysis") is True and all(authority.get(k) == v for k, v in expected.items()), "exact root once authority required")
    scope_ref = binding["prospective_scope"]
    require(scope_ref["sha256"] == freeze["prospective_scope_sha256"], "prospective scope differs")
    scope = checked(scope_ref, root)
    for name, pin in freeze["files"].items():
        require(Path(name).name == name and sha(CODE / name) == pin, "frozen code differs")
    dev = checked(binding["dev_manifest"], root)
    require(binding["dev_manifest"]["sha256"] == DEV_SHA and (dev["role"], dev["ascension"], dev["content"]) == ("dev", 10, "ported_only"), "fixed public dev manifest required")
    keys = sorted(g["character"] + "-" + g["seed"] for g in dev["games"])
    require(len(keys) == len(set(keys)) == 256 and all(g["character"] == "SILENT" for g in dev["games"]), "exact Silent256 keys required")
    data, summaries = {}, {}
    require(set(binding["arms"]) == set(ARMS) == set(authority["accepted_input_sha256"]), "exact current pair required")
    for name, arm in ARMS.items():
        ref = binding["arms"][name]
        require(ref["study"] == arm["study"], "foreign study")
        for role in (*PROOF_ROLES, "terminal_decision"):
            checked(ref[role], root, parse=False)
            require(authority["accepted_input_sha256"][name][role] == ref[role]["sha256"], "accepted input source differs")
        training_receipt = checked(ref["training_provider_terminal"], root)
        scoped_call = scope["current_training_calls"]["S" if name == "S115_S2" else "M"]
        require(training_receipt.get("call_id") == scoped_call and training_receipt.get("provider_terminal_verified") is True and training_receipt.get("observation_class") in {"FunctionCall_returned", "FunctionCall_verified_terminal_exception", "SDK_verified_terminal_cancel"}, "wrong current training call terminal")
        model = ref["actual_model"]
        ready, preflight = checked(ref["ready"], root), checked(ref["preflight"], root)
        dose = terminal_dose(name, model, ready, preflight, ref["terminal_decision"])
        data[name], summary, validation = endpoint(ref, root, keys, (model["decisions"], model["updates"]))
        require(validation["formal_quality_validation_eligible"], "both formal devs must pass replay, illegal and error checks")
        context = observation_context(ref["observations"], root)
        summaries[name] = {"training_call_id": scoped_call, "checkpoint_file_sha256": model["checkpoint_file_sha256"], "summary": summary, "validation": validation, "dose": dose, "observations": context}
    require(summaries["S115_S2"]["checkpoint_file_sha256"] != summaries["M116"]["checkpoint_file_sha256"], "same checkpoint cannot fill both arms")
    m, s = data["M116"], data["S115_S2"]
    pairs = {"M116_minus_S115_S2": paired_deltas(m, s, keys)}
    import numpy
    exclusive(claim, {"input_binding_sha256": args.input_binding_sha256, "authority_sha256": args.once_authority_sha256, "analysis_pins_sha256": sha(pins), "seed": SEED, "replicates": REPLICATES, "n": 256, "one_shared_index": True})
    try:
        receipt = bootstrap_contrasts(pairs)
        result = {
            "schema": "task121-current-endpoints-paired-comparison-v1", "status": "completed",
            "input_binding_sha256": args.input_binding_sha256, "authority_sha256": args.once_authority_sha256,
            "analysis_pins_sha256": sha(pins), "prospective_scope_sha256": scope_ref["sha256"],
            "arms": summaries, "bootstrap_receipt": receipt, "paired_descriptive": descriptive_pairs(m, s, keys),
            "limits": [
                "Unequal-dose current S and M endpoints. This describes observed performance and cannot isolate model capacity.",
                "One training lineage per size. Exposed dev seed intervals do not measure training replicate variation or blind performance.",
                "Zero observed wins in both arms yields a degenerate paired win bootstrap. Per-arm Wilson intervals quantify uncertainty on observed win rates.",
                "Use each actual accepted dose and resource censor label. The current-call increment and cumulative lineage totals have different scopes.",
                "App and compute estimates can overlap. Unknown actual fees remain null.",
            ],
        }
        exclusive(out, result)
    except Exception as error:
        exclusive(out, {"status": "failed_after_once_claim_no_retry", "error_type": type(error).__name__, "input_binding_sha256": args.input_binding_sha256})
        raise
    print({"status": "completed", "output": str(out), "sha256": sha(out)})

if __name__ == "__main__":
    main()
