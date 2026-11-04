"""Explicit durable fixtures. No trainer, network, model, or GPU imports."""

from __future__ import annotations

import json
import sqlite3
from contextlib import closing
from pathlib import Path

from .adapters import Job, Outcome
from .contracts import MAIN_EVAL, PURE_TRAINING, canonical, digest
from .controller import Controller
from .manifests import EVAL_FILES, TRAIN_FILES, publish


class FixtureBackend:
    fixture = True
    tool_enabled = True

    def __init__(self, path):
        self.path = path
        with closing(sqlite3.connect(path)) as db:
            db.execute("CREATE TABLE IF NOT EXISTS provider (id PRIMARY KEY, job_key UNIQUE, payload, outcome)")

    def lookup(self, job):
        with closing(sqlite3.connect(self.path)) as db:
            row = db.execute("SELECT id FROM provider WHERE job_key=?", (job.key,)).fetchone()
            return row[0] if row else None

    def submit(self, job):
        remote_id = f"fixture-{digest(job.key)[:32]}"
        payload = canonical(vars(job))
        with closing(sqlite3.connect(self.path)) as db, db:
            db.execute("INSERT OR IGNORE INTO provider VALUES (?,?,?,NULL)", (remote_id, job.key, payload))
            previous = db.execute("SELECT payload FROM provider WHERE id=?", (remote_id,)).fetchone()[0]
            if previous != payload:
                raise ValueError("fixture key payload mismatch")
        return remote_id

    def poll(self, remote_id):
        with closing(sqlite3.connect(self.path)) as db:
            row = db.execute("SELECT outcome FROM provider WHERE id=?", (remote_id,)).fetchone()
            return Outcome(**json.loads(row[0])) if row[0] else Outcome("running")

    def jobs(self):
        with closing(sqlite3.connect(self.path)) as db:
            return [(remote_id, Job(**json.loads(payload))) for remote_id, payload
                    in db.execute("SELECT id,payload FROM provider ORDER BY rowid")]

    def complete(self, remote_id, outcome):
        with closing(sqlite3.connect(self.path)) as db, db:
            row = db.execute("UPDATE provider SET outcome=? WHERE id=? AND outcome IS NULL",
                             (canonical(vars(outcome)), remote_id))
            if row.rowcount != 1:
                raise ValueError("fixture job missing or already settled")


def spec(now, experiment_id="silent-fixture"):
    """Synthetic identities and resource limits for the fixture study only."""
    return {
        "experiment_id": experiment_id, "parent_id": None, "hypothesis": "Fixture",
        "control_id": None, "changed_factor": "fixture", "character": "SILENT", "ascension": 10,
        "data_lineage": "autonomous-fresh", "code_commit": "1" * 40, "image_digest": "2" * 64,
        "engine_hash": "3" * 64, "schema_hash": "4" * 64, "dependency_hash": "7" * 64,
        "model_config": {"d": 1, "s": 1, "depth": 0, "heads": 1, "ff_mult": 1,
                         "deck_counts": False, "aux_heads": False},
        "training_protocol": dict(PURE_TRAINING), "evaluation_policy": dict(MAIN_EVAL),
        "rollout_shape": {"envs": 1, "rollout": 1},
        "ppo_config": {"epochs": 1, "minibatch": 1, "max_rows": 192, "step_cap": 3000, "floor_step_cap": 80,
                       "lr": 0.0003, "gamma": 1, "lam": 0.99, "clip": 0.2, "vf_coef": 0.5, "ent_coef": 0.02,
                       "max_grad_norm": 0.5, "win_bonus": 1, "acq_deck_coef": 0, "acq_relic_coef": 0,
                       "acq_upgrade_coef": 0, "cycle_cost": 0, "aux_coef": 0, "amp": False, "grad_ckpt": False,
                       "acq_shaping": False, "aux_bootstrap": False, "objective": "protocol", "compile": "off"},
        "actual_param_count": 1, "reward_version": "fixture", "action_protocol": "fixture",
        "init_seed": 1, "policy_seed": 2, "env_seed": 3, "env_seed_namespace": "FIXTURETRAIN", "cursor": 0,
        "train_seed_manifest_hash": "5" * 64, "dev_seed_manifest_hash": "6" * 64, "blind_seed_manifest_hash": "8" * 64,
        "cumulative_decision_target": 20, "evaluation_milestones": [10, 20],
        "resource_request": {"train_cost": "1.00", "eval_cost": "0.20", "segment_seconds": 60,
                             "eval_seconds": 60},
        "max_cost": "5.00", "absolute_deadline": int(now) + 1000,
        "checkpoint_policy": {"interval_seconds": 10, "resume_mode": "fresh_episodes"},
        "eval_protocol": "fixture-greedy-no-intervention", "dev_seed_count": 1, "blind_seed_count": 1,
        "stop_rules": "fixture", "promotion_rules": "fixture",
    }


def decision(job, action="WAIT", **fields):
    payload = job.payload
    keys = ("decision_id", "study_id", "expected_state_version", "consumed_event_ids", "evidence_refs")
    return canonical({
        **{key: payload[key] for key in keys}, "observed_facts": "Fixture event",
        "comparison": "No actual gameplay", "uncertainty": "Synthetic",
        "action": action, "action_reason": "Test a single deterministic transition",
        "reserve_budget_usd": 0, "next_wake_condition": "next complete event", **fields,
    })


def artifact(root, job, decisions, termination, generation="generation-1", *, issued_until=None):
    path = Path(root) / job.key / generation
    path.mkdir(parents=True, exist_ok=False)
    metadata = {"decisions": decisions, "termination": termination}
    roles = EVAL_FILES
    if job.kind == "train":
        roles = TRAIN_FILES
        mode = job.payload["spec"]["checkpoint_policy"]["resume_mode"]
        cursor = job.payload["start_cursor"] + 1
        metadata.update(cursor=cursor, seed_issued_until=issued_until if issued_until is not None else cursor,
                        updates=1, resume_mode=mode)
        if mode == "exact":
            roles = roles | {"environment"}
    files = {role: f"{role}.json" for role in roles}
    for role, name in files.items():
        (path / name).write_text(canonical({"fixture": True, "role": role}))
    return {"generation": generation, "manifest_hash": publish(path, Controller._identity(job), metadata, files)}
