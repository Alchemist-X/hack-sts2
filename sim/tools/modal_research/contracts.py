"""Inputs at the controller boundary. Monetary storage uses integer US cents."""

from __future__ import annotations

import hashlib
import json
import math
import re
from decimal import Decimal

ACTIONS = {"WAIT", "CONTINUE", "REPLICATE", "NEW_EXPERIMENT", "REQUEST_SOL", "PAUSE", "STOP"}
TRAIN = {"CONTINUE", "REPLICATE", "NEW_EXPERIMENT"}
IDENTIFIER = re.compile(r"[A-Za-z0-9][A-Za-z0-9_.:-]{0,127}")
DIGEST = re.compile(r"[0-9a-f]{64}")
COMMIT = re.compile(r"[0-9a-f]{40}")
STREAM_NAMESPACE = re.compile(r"[A-Z][A-Z0-9_]{0,23}")
PURE_TRAINING = {
    "initialization": "random", "start_mode": "stream", "save_mix": 0,
    "start_saves": None, "harvest": 0, "teacher": None, "action_labels": None,
    "pretrained": None, "trajectory_data": None, "rollout_loop_breaker": False,
}
MAIN_EVAL = {
    "start_mode": "fresh", "policy": "greedy", "loop_breaker": False,
    "search": False, "human_intervention": False,
}
PPO_INTEGERS = {"epochs", "minibatch", "max_rows", "step_cap", "floor_step_cap"}
PPO_NUMBERS = {"lr", "gamma", "lam", "clip", "vf_coef", "ent_coef", "max_grad_norm", "win_bonus",
               "acq_deck_coef", "acq_relic_coef", "acq_upgrade_coef", "cycle_cost", "aux_coef"}
PPO_FLAGS = {"amp", "grad_ckpt", "acq_shaping", "aux_bootstrap"}
PPO_CHOICES = {"objective": {"protocol", "win"}, "compile": {"off", "default", "reduce-overhead"}}


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def digest(value):
    return hashlib.sha256(canonical(value).encode()).hexdigest()


def identifier(value):
    if not isinstance(value, str) or not IDENTIFIER.fullmatch(value):
        raise ValueError("invalid identifier")
    return value


def integer(value, minimum=0):
    if type(value) is not int or value < minimum:
        raise ValueError(f"expected integer >= {minimum}")
    return value


def cents(value):
    if type(value) not in (int, float, str):
        raise ValueError("invalid USD amount")
    try:
        number = Decimal(str(value))
        if not number.is_finite() or not 0 <= number <= 100_000:
            raise ValueError("USD amount out of range")
        minor_units = number * 100
        if minor_units != minor_units.to_integral_value():
            raise ValueError("USD amount must be specified to whole cents")
        return int(minor_units)
    except ArithmeticError as error:
        raise ValueError("invalid USD amount") from error


def bounded_text(value, size=2000):
    if not isinstance(value, str) or not value.strip() or len(value) > size:
        raise ValueError("invalid text length")
    return value


def validate_spec(spec):
    required = {
        "experiment_id", "parent_id", "hypothesis", "control_id", "changed_factor",
        "code_commit", "image_digest", "engine_hash", "schema_hash", "model_config",
        "actual_param_count", "reward_version", "action_protocol", "init_seed", "policy_seed",
        "env_seed_namespace", "cursor", "train_seed_manifest_hash", "dev_seed_manifest_hash", "blind_seed_manifest_hash",
        "cumulative_decision_target", "evaluation_milestones", "resource_request", "max_cost",
        "absolute_deadline", "checkpoint_policy", "stop_rules", "promotion_rules", "character",
        "ascension", "eval_protocol", "data_lineage", "training_protocol", "evaluation_policy",
        "rollout_shape", "env_seed", "dependency_hash", "ppo_config",
    }
    if type(spec) is not dict or set(spec) != required:
        raise ValueError("experiment fields must match the frozen contract")
    if len(canonical(spec)) > 16000:
        raise ValueError("experiment spec exceeds size bound")
    if (spec["character"], spec["ascension"], spec["data_lineage"]) != ("SILENT", 10, "autonomous-fresh"):
        raise ValueError("requires autonomous fresh starts with Silent A10")
    if canonical(spec["training_protocol"]) != canonical(PURE_TRAINING):
        raise ValueError("pure training requires random initialization and environment-only fresh starts")
    if canonical(spec["evaluation_policy"]) != canonical(MAIN_EVAL):
        raise ValueError("primary evaluation requires full fresh greedy runs with no interventions")
    identifier(spec["experiment_id"])
    for key in ("parent_id", "control_id"):
        if spec[key] is not None:
            identifier(spec[key])
    for key in ("hypothesis", "changed_factor", "reward_version", "action_protocol", "env_seed_namespace",
                "eval_protocol", "stop_rules", "promotion_rules"):
        bounded_text(spec[key])
    if not isinstance(spec["code_commit"], str) or not COMMIT.fullmatch(spec["code_commit"]):
        raise ValueError("expected full code commit")
    for key in ("image_digest", "engine_hash", "schema_hash", "dependency_hash",
                "train_seed_manifest_hash", "dev_seed_manifest_hash", "blind_seed_manifest_hash"):
        if not isinstance(spec[key], str) or not DIGEST.fullmatch(spec[key]):
            raise ValueError(f"invalid {key}")
    if len({spec[key] for key in ("train_seed_manifest_hash", "dev_seed_manifest_hash", "blind_seed_manifest_hash")}) != 3:
        raise ValueError("train, dev and blind seed manifests must be distinct")
    for key in ("init_seed", "policy_seed", "env_seed"):
        integer(spec[key])
    if len({spec["init_seed"], spec["policy_seed"], spec["env_seed"]}) != 3:
        raise ValueError("initialization, policy and environment seeds must be independent")
    if integer(spec["cursor"]) != 0:
        raise ValueError("a registered experiment starts with seed cursor zero")
    namespace = spec["env_seed_namespace"]
    if not STREAM_NAMESPACE.fullmatch(namespace) or namespace.startswith("TRAIN"):
        raise ValueError("training seed namespace must be a new stream prefix")
    for key in ("actual_param_count", "absolute_deadline", "cumulative_decision_target"):
        integer(spec[key], 1)
    model = spec["model_config"]
    if type(model) is not dict or set(model) != {"d", "s", "depth", "heads", "ff_mult", "deck_counts", "aux_heads"}:
        raise ValueError("only frozen model architecture fields are allowed")
    for key in ("d", "s", "heads", "ff_mult"):
        integer(model[key], 1)
    integer(model["depth"])
    if any(type(model[key]) is not bool for key in ("deck_counts", "aux_heads")):
        raise ValueError("model feature flags must be booleans")
    shape = spec["rollout_shape"]
    if type(shape) is not dict or set(shape) != {"envs", "rollout"}:
        raise ValueError("missing fixed rollout shape")
    stride = integer(shape["envs"], 1) * integer(shape["rollout"], 1)
    ppo = spec["ppo_config"]
    if type(ppo) is not dict or set(ppo) != PPO_INTEGERS | PPO_NUMBERS | PPO_FLAGS | PPO_CHOICES.keys():
        raise ValueError("only frozen trainer recipe fields are allowed")
    for key in PPO_INTEGERS:
        integer(ppo[key], 1)
    for key in PPO_NUMBERS:
        if type(ppo[key]) not in (int, float) or not math.isfinite(ppo[key]) or ppo[key] < 0:
            raise ValueError("invalid numeric trainer recipe value")
    if any(type(ppo[key]) is not bool for key in PPO_FLAGS):
        raise ValueError("trainer recipe flags must be booleans")
    for key, choices in PPO_CHOICES.items():
        if type(ppo[key]) is not str or ppo[key] not in choices:
            raise ValueError("invalid trainer recipe choice")
    milestones = spec["evaluation_milestones"]
    if type(milestones) is not list or not milestones:
        raise ValueError("evaluation milestones required")
    for step in milestones:
        if integer(step, 1) % stride:
            raise ValueError("milestone must align to a full rollout")
    if milestones != sorted(set(milestones)) or milestones[-1] != spec["cumulative_decision_target"]:
        raise ValueError("evaluation milestones must increase and end at the decision target")
    resources = spec["resource_request"]
    if type(resources) is not dict or set(resources) != {"train_cost", "eval_cost", "segment_seconds", "eval_seconds"}:
        raise ValueError("invalid worker resource request")
    if min(cents(resources["train_cost"]), cents(resources["eval_cost"])) <= 0:
        raise ValueError("worker reserves must be positive")
    if integer(resources["segment_seconds"], 1) > 4500 or integer(resources["eval_seconds"], 1) > 4500:
        raise ValueError("worker timeout exceeds limit")
    if not cents(spec["max_cost"]):
        raise ValueError("experiment cost limit must be positive")
    policy = spec["checkpoint_policy"]
    if type(policy) is not dict or set(policy) != {"interval_seconds", "resume_mode"}:
        raise ValueError("invalid checkpoint policy")
    if integer(policy["interval_seconds"], 1) > 300 or policy["resume_mode"] not in {"exact", "fresh_episodes"}:
        raise ValueError("invalid checkpoint recovery mode or save interval")


def _unique_object(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate decision field")
        result[key] = value
    return result


def validate_decision(raw, payload):
    """A decision may select a registered spec by hash or an allowlisted engineering task."""
    if not isinstance(raw, str) or len(raw.encode()) > 16000:
        raise ValueError("decision output is absent or too large")
    decision = json.loads(raw, object_pairs_hook=_unique_object)
    common = {
        "decision_id", "study_id", "expected_state_version", "consumed_event_ids", "evidence_refs",
        "observed_facts", "comparison", "uncertainty", "action", "action_reason",
        "reserve_budget_usd", "next_wake_condition",
    }
    if type(decision) is not dict or not common <= set(decision):
        raise ValueError("decision is missing required fields")
    for key in ("decision_id", "study_id", "expected_state_version", "consumed_event_ids"):
        if decision[key] != payload[key] or type(decision[key]) is not type(payload[key]):
            raise ValueError(f"stale or mismatched {key}")
    refs = decision["evidence_refs"]
    if type(refs) is not list or not refs or any(type(ref) is not str or ref not in payload["evidence_refs"] for ref in refs):
        raise ValueError("decision must cite submitted evidence")
    for key in ("observed_facts", "comparison", "uncertainty", "action_reason", "next_wake_condition"):
        bounded_text(decision[key])
    action = decision["action"]
    if type(action) is not str or action not in ACTIONS:
        raise ValueError("unsupported action")
    reserve = cents(decision["reserve_budget_usd"])
    extra = set(decision) - common
    if action in TRAIN:
        if extra != {"experiment_spec"} or not reserve:
            raise ValueError("training requires a spec reference and a positive reserve")
        reference = decision["experiment_spec"]
        if type(reference) is not dict or set(reference) != {"experiment_id", "config_hash"}:
            raise ValueError("expected an immutable experiment spec reference")
        experiment = identifier(reference["experiment_id"])
        if payload["allowed_experiments"].get(experiment) != reference["config_hash"]:
            raise ValueError("unregistered experiment spec")
    elif action == "REQUEST_SOL":
        if extra != {"engineering_task"} or not reserve:
            raise ValueError("SOL task requires a positive reserve")
        task = decision["engineering_task"]
        if type(task) is not dict or set(task) != {"task_type", "description"}:
            raise ValueError("invalid engineering task fields")
        if type(task["task_type"]) is not str or task["task_type"] not in payload["engineering_task_types"]:
            raise ValueError("engineering task type is not allowlisted")
        bounded_text(task["description"])
    elif extra or reserve:
        raise ValueError("passive decisions have no dispatch parameters")
    return decision


def verify_seed_isolation(seed_sets):
    """Validate sets of (normalized seed, actual environment key) with explicit splits."""
    seen_seeds, seen_keys = {}, {}
    if set(seed_sets) - {"train", "dev", "blind"} or "dev" not in seed_sets or len(seed_sets) < 2:
        raise ValueError("invalid seed splits")
    for split, pairs in seed_sets.items():
        if not pairs:
            raise ValueError("empty seed split")
        for seed, key in pairs:
            bounded_text(seed, 128)
            bounded_text(key, 128)
            if seed in seen_seeds or key in seen_keys:
                raise ValueError(f"duplicate normalized seed or environment key in {split}")
            seen_seeds[seed] = split
            seen_keys[key] = split
