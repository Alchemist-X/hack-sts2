"""SQLite state for a single host on local durable storage."""

from __future__ import annotations

import json
import sqlite3
import time
from contextlib import contextmanager
from pathlib import Path

from .contracts import canonical, cents, digest, identifier, validate_spec

SCHEMA = """
CREATE TABLE IF NOT EXISTS study (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    id TEXT NOT NULL, mode TEXT NOT NULL,
    version INTEGER NOT NULL DEFAULT 0,
    approved_budget INTEGER, approval_ref TEXT, execution_cap INTEGER, platform_snapshot TEXT,
    stopped TEXT, analysis_paused TEXT, next_wake TEXT,
    lease_owner TEXT, lease_until REAL NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS approvals (
    reference TEXT PRIMARY KEY, approved_budget INTEGER NOT NULL, execution_cap INTEGER NOT NULL,
    created REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS access_status (
    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
    pause_reason TEXT NOT NULL, next_wake TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS access_verifications (
    reference TEXT PRIMARY KEY, previous_pause TEXT NOT NULL, created REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS experiments (
    id TEXT PRIMARY KEY, spec TEXT NOT NULL, config_hash TEXT NOT NULL,
    decisions INTEGER NOT NULL DEFAULT 0, cursor INTEGER NOT NULL,
    checkpoint TEXT, stage TEXT NOT NULL DEFAULT 'idle', target INTEGER,
    segment INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS events (
    id TEXT PRIMARY KEY, kind TEXT NOT NULL, payload TEXT NOT NULL,
    created REAL NOT NULL, decision_id TEXT
);
CREATE TABLE IF NOT EXISTS decisions (
    id TEXT PRIMARY KEY, event_id TEXT NOT NULL UNIQUE REFERENCES events(id),
    payload TEXT NOT NULL, dispatch_error TEXT, accepted REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS jobs (
    id TEXT PRIMARY KEY, logical_key TEXT NOT NULL, attempt INTEGER NOT NULL,
    kind TEXT NOT NULL, experiment_id TEXT REFERENCES experiments(id),
    event_id TEXT REFERENCES events(id), payload TEXT NOT NULL,
    reserve INTEGER NOT NULL, actual INTEGER,
    state TEXT NOT NULL DEFAULT 'intent', remote_id TEXT, submit_started INTEGER NOT NULL DEFAULT 0,
    deadline REAL NOT NULL, last_checked REAL, outcome TEXT, error TEXT,
    UNIQUE(logical_key, attempt)
);
CREATE TABLE IF NOT EXISTS research_usage_holds (
    job_id TEXT PRIMARY KEY REFERENCES jobs(id),
    usage_value_cents INTEGER NOT NULL,
    created REAL NOT NULL
);
CREATE TABLE IF NOT EXISTS cost_holds (
    job_id TEXT PRIMARY KEY REFERENCES jobs(id),
    reserve INTEGER NOT NULL, proven_bound INTEGER NOT NULL,
    created REAL NOT NULL, settled_at REAL, actual INTEGER
);
"""


class Store:
    def __init__(self, path, study_id, mode="local", clock=time.time):
        identifier(study_id)
        if mode not in {"local", "fixture"}:
            raise ValueError("this controller supports local disk only; configure a remote transactional store separately")
        self.path = Path(path)
        self.clock = clock
        with self.transaction() as db:
            db.executescript(SCHEMA)
        with self.transaction() as db:
            db.execute("INSERT OR IGNORE INTO study (singleton,id,mode) VALUES (1,?,?)", (study_id, mode))
            row = db.execute("SELECT id, mode FROM study").fetchone()
            if tuple(row) != (study_id, mode):
                raise ValueError("database belongs to another study or execution mode")

    @contextmanager
    def transaction(self):
        db = sqlite3.connect(self.path, timeout=5, isolation_level=None)
        db.row_factory = sqlite3.Row
        try:
            db.execute("PRAGMA foreign_keys=ON")
            db.execute("PRAGMA synchronous=FULL")
            db.execute("BEGIN IMMEDIATE")
            yield db
            db.commit()
        except BaseException:
            db.rollback()
            raise
        finally:
            db.close()

    def acquire(self, owner, seconds):
        now = self.clock()
        with self.transaction() as db:
            row = db.execute(
                "UPDATE study SET lease_owner=?, lease_until=? WHERE lease_until<=?",
                (owner, now + seconds, now),
            )
            return row.rowcount == 1

    def fence(self, db, owner):
        state = db.execute("SELECT * FROM study").fetchone()
        if state["lease_owner"] != owner or state["lease_until"] <= self.clock():
            raise TimeoutError("controller lease expired")
        return state

    def release(self, owner):
        with self.transaction() as db:
            db.execute("UPDATE study SET lease_owner=NULL,lease_until=0 WHERE lease_owner=?", (owner,))

    def approve(self, usd, reference, execution_cap_usd=None):
        """Operator entry point. Nothing in the decision dispatch calls this."""
        amount = cents(usd)
        identifier(reference)
        execution_cap = amount if execution_cap_usd is None else cents(execution_cap_usd)
        if not 0 < execution_cap <= amount:
            raise ValueError("execution cap must be positive and at most approved budget")
        with self.transaction() as db:
            row = db.execute("SELECT approved_budget FROM study").fetchone()
            if row[0] is not None and amount < row[0]:
                raise ValueError("use stop to suspend; approved budget cannot be reset")
            db.execute("INSERT OR IGNORE INTO approvals VALUES (?,?,?,?)", (reference, amount, execution_cap, self.clock()))
            approval = db.execute("SELECT approved_budget,execution_cap FROM approvals WHERE reference=?",
                                  (reference,)).fetchone()
            if tuple(approval) != (amount, execution_cap):
                raise ValueError("approval reference collision")
            db.execute("UPDATE study SET approved_budget=?,approval_ref=?,execution_cap=?",
                       (amount, reference, execution_cap))

    def record_platform(self, *, gross_limit, net_limit, credit, gross_used, net_spent, reference):
        identifier(reference)
        snapshot = {key: cents(value) for key, value in {
            "gross_limit": gross_limit, "net_limit": net_limit, "credit": credit,
            "gross_used": gross_used, "net_spent": net_spent,
        }.items()}
        with self.transaction() as db:
            snapshot.update(reference=reference, observed_at=self.clock(),
                            local_spend_at_snapshot=self.ledger(db)[0])
            db.execute("UPDATE study SET platform_snapshot=?", (canonical(snapshot),))

    @staticmethod
    def platform_headroom(state, spent):
        if not state["platform_snapshot"]:
            return None
        snapshot = json.loads(state["platform_snapshot"])
        at_snapshot = min(snapshot["gross_limit"] - snapshot["gross_used"],
                          snapshot["net_limit"] - snapshot["net_spent"] + snapshot["credit"])
        return at_snapshot - (spent - snapshot["local_spend_at_snapshot"])

    def stop(self, reason):
        with self.transaction() as db:
            db.execute("UPDATE study SET stopped=? WHERE stopped IS NULL", (reason,))

    def resume(self, reason, reference):
        identifier(reference)
        with self.transaction() as db:
            if db.execute("SELECT 1 FROM jobs WHERE state='uncertain' LIMIT 1").fetchone():
                raise ValueError("reconcile uncertain jobs before resuming")
            row = db.execute(
                """UPDATE study SET
                   stopped=CASE WHEN stopped=? THEN NULL ELSE stopped END,
                   analysis_paused=CASE WHEN analysis_paused=? THEN NULL ELSE analysis_paused END,
                   next_wake=? WHERE ? IN (stopped,analysis_paused)""",
                (reason, reason, f"operator resume {reference}", reason),
            )
            if row.rowcount != 1:
                raise ValueError("pause reason changed; review status")

    def register(self, spec):
        validate_spec(spec)
        payload = canonical(spec)
        config_hash = digest(spec)
        with self.transaction() as db:
            mode = db.execute("SELECT mode FROM study").fetchone()[0]
            if mode != "fixture" and spec["eval_protocol"] == "fixture-greedy-no-intervention":
                raise ValueError("fixture evaluation is forbidden in the operational ledger")
            db.execute(
                "INSERT OR IGNORE INTO experiments (id,spec,config_hash,cursor) VALUES (?,?,?,?)",
                (spec["experiment_id"], payload, config_hash, spec["cursor"]),
            )
            row = db.execute("SELECT spec FROM experiments WHERE id=?", (spec["experiment_id"],)).fetchone()
            if row["spec"] != payload:
                raise ValueError("experiment ID already has a different frozen spec")
        return config_hash

    @staticmethod
    def append_event(db, event_id, kind, payload, now):
        identifier(event_id)
        encoded = canonical(payload)
        if len(encoded.encode()) > 96_000:
            raise ValueError("evidence exceeds size limit")
        db.execute("INSERT OR IGNORE INTO events VALUES (?,?,?,?,NULL)", (event_id, kind, encoded, now))
        row = db.execute("SELECT kind,payload FROM events WHERE id=?", (event_id,)).fetchone()
        if tuple(row) != (kind, encoded):
            raise ValueError("event ID collision")

    def ingest(self, event_id, kind, evidence):
        if kind not in {"result", "anomaly", "sol_result", "bootstrap"}:
            raise ValueError("unsupported event kind")
        with self.transaction() as db:
            mode = db.execute("SELECT mode FROM study").fetchone()[0]
            if kind in {"result", "sol_result"} and mode != "fixture":
                raise ValueError("operational results require reconciled job evidence")
            self.append_event(db, event_id, kind, evidence, self.clock())

    def reserve_access_probe(self, key, decision_id, category, reserve_usd, seconds, *, prepare_only=False):
        identifier(key)
        identifier(decision_id)
        identifier(category)
        reserve = cents(reserve_usd)
        max_reserve, max_seconds = {
            "env_cpu_validation": (10, 600), "gpu_smoke": (200, 1200),
        }.get(category, (100, 180))
        if not 0 < reserve <= max_reserve or type(seconds) is not int or not 0 < seconds <= max_seconds:
            raise ValueError("access probe exceeds local authorization envelope")
        payload = canonical({"decision_id": decision_id, "category": category, "seconds": seconds,
                             "retry_limit": 0})
        with self.transaction() as db:
            state = db.execute("SELECT * FROM study").fetchone()
            if state["mode"] != "local" or state["stopped"] or state["analysis_paused"]:
                raise ValueError("operational study is paused or in fixture mode")
            old = db.execute("SELECT payload,reserve FROM jobs WHERE id=?", (key,)).fetchone()
            if old:
                if tuple(old) != (payload, reserve):
                    raise ValueError("probe key already used with different parameters")
                return
            if category == "gpu_smoke" and db.execute(
                """SELECT 1 FROM jobs WHERE
                   (kind='access_probe' AND json_extract(payload,'$.category')='gpu_smoke'
                    AND (state IN ('external_prepared','external_reserved') OR error='gpu_receipt_unknown'))
                   OR (kind IN ('train','eval') AND state IN ('intent','running','uncertain'))"""
            ).fetchone():
                raise ValueError("another GPU operation is active or launch receipt is unresolved")
            spent, reserved = self.ledger(db)
            platform = self.platform_headroom(state, spent)
            if (state["approved_budget"] is None or platform is None
                    or spent + reserved + reserve > min(state["approved_budget"], state["execution_cap"])
                    or reserved + reserve > platform):
                raise ValueError("probe requires sufficient authorized headroom")
            db.execute(
                """INSERT INTO jobs (id,logical_key,attempt,kind,payload,reserve,state,deadline)
                   VALUES (?,?,1,'access_probe',?,?,?,?)""",
                (key, key, payload, reserve, "external_prepared" if prepare_only else "external_reserved",
                 0 if prepare_only else self.clock() + seconds),
            )

    def activate_access_probe(self, key):
        identifier(key)
        with self.transaction() as db:
            row = db.execute("SELECT payload,state FROM jobs WHERE id=? AND kind='access_probe'", (key,)).fetchone()
            if not row or row["state"] != "external_prepared":
                raise ValueError("probe is not prepared for activation")
            state = db.execute("SELECT * FROM study").fetchone()
            spent, reserved = self.ledger(db)
            platform = self.platform_headroom(state, spent)
            if (state["stopped"] or state["analysis_paused"] or platform is None
                    or spent + reserved > min(state["approved_budget"], state["execution_cap"])
                    or reserved > platform):
                raise ValueError("probe lacks current authorized headroom")
            seconds = json.loads(row["payload"])["seconds"]
            db.execute("UPDATE jobs SET state='external_reserved',deadline=? WHERE id=?",
                       (self.clock() + seconds, key))

    def gpu_smoke_step(self, key, step, binding):
        """A paid step is written before its sole application-level provider call."""
        identifier(key)
        if step not in {"stage", "launch", "inspect"}:
            raise ValueError("invalid GPU smoke operation")
        with self.transaction() as db:
            state = db.execute("SELECT * FROM study").fetchone()
            row = db.execute("SELECT * FROM jobs WHERE id=? AND kind='access_probe'", (key,)).fetchone()
            if (not row or json.loads(row["payload"])["category"] != "gpu_smoke"
                    or row["state"] not in {"external_reserved", "external_finished_cost_unknown"}):
                raise ValueError("GPU smoke requires its own operational reservation")
            if binding.get("decision_id", binding.get("task")) != json.loads(row["payload"])["decision_id"]:
                raise ValueError("GPU packet is outside its authorized decision")
            previous = db.execute("SELECT payload FROM events WHERE id=?", (f"{key}-stage",)).fetchone()
            if previous and json.loads(previous["payload"]) != binding:
                raise ValueError("GPU smoke packet differs from durable intent")
            if step == "inspect":
                if not previous:
                    raise ValueError("GPU smoke has no durable provider intent")
                return dict(row)
            now = self.clock()
            spent, reserved = self.ledger(db)
            platform = self.platform_headroom(state, spent)
            minimum_time = 320 if step == "launch" else 380
            if (row["state"] != "external_reserved" or row["deadline"] - now < minimum_time
                    or state["stopped"] or state["analysis_paused"] or state["approved_budget"] is None
                    or platform is None or spent + reserved > min(state["approved_budget"], state["execution_cap"])
                    or reserved > platform):
                raise ValueError("GPU smoke lacks current authorization, budget or time")
            if step == "stage":
                if previous:
                    raise ValueError("stage already attempted; reconcile without starting again")
            else:
                if not previous or row["submit_started"] or db.execute(
                    "SELECT 1 FROM jobs WHERE kind IN ('train','eval') AND state IN ('intent','running','uncertain')"
                ).fetchone():
                    raise ValueError("launch already attempted, concurrent GPU job, or packet was never staged")
                db.execute("UPDATE jobs SET submit_started=1,error='gpu_receipt_unknown' WHERE id=?", (key,))
            self.append_event(db, f"{key}-{step}", f"gpu_smoke_{step}",
                              binding if step == "stage" else {**binding, "worker_deadline": int(now) + 300}, now)
            return {**dict(row), "worker_deadline": int(now) + 300}

    def gpu_smoke_receipt(self, key, remote_id):
        identifier(remote_id)
        with self.transaction() as db:
            row = db.execute("SELECT payload,submit_started,remote_id FROM jobs WHERE id=?", (key,)).fetchone()
            if (not row or json.loads(row["payload"])["category"] != "gpu_smoke" or not row["submit_started"]
                    or row["remote_id"] not in (None, remote_id)):
                raise ValueError("GPU sandbox receipt does not match an immutable launch")
            db.execute("UPDATE jobs SET remote_id=?,error=NULL WHERE id=?", (remote_id, key))

    def finish_access_probe(self, key, result):
        identifier(key)
        required = {"status", "session_ids", "report_ref"}
        if type(result) is not dict or set(result) not in (required, required | {"details"}):
            raise ValueError("invalid access probe evidence")
        if "details" in result and (type(result["details"]) is not dict or len(canonical(result["details"])) > 4000):
            raise ValueError("access probe detail exceeds evidence bounds")
        environment = result["status"] in {"blocked_prelaunch", "env_verified", "env_failed"}
        gpu = result["status"] in {"gpu_stage_failed", "gpu_not_launched", "gpu_verified", "gpu_failed", "gpu_receipt_unknown"}
        if not (environment or gpu) and result["status"] not in {"no_terminal_evidence", "model_not_found", "verified", "failed", "tool_sandbox_failed"}:
            raise ValueError("unknown access probe status")
        session_ids = result["session_ids"]
        details = result.get("details", {})
        no_session = (result["status"] == "no_terminal_evidence" and session_ids == []
                      and {"session_id", "job_id", "invocation_id", "exit_code"} <= details.keys()
                      and details["session_id"] is None and type(details["exit_code"]) is int)
        if no_session:
            identifier(details["job_id"])
            identifier(details["invocation_id"])
        if type(session_ids) is not list or (len(session_ids) != 0 if environment or gpu or no_session
                                             else not 1 <= len(session_ids) <= 2):
            raise ValueError("access probe session IDs do not match the probe type")
        for session in result["session_ids"]:
            identifier(session)
        if not isinstance(result["report_ref"], str) or not 1 <= len(result["report_ref"]) <= 256:
            raise ValueError("invalid access probe report reference")
        outcome = canonical(result)
        with self.transaction() as db:
            old = db.execute("SELECT state,outcome,payload,remote_id,submit_started FROM jobs WHERE id=? AND kind='access_probe'", (key,)).fetchone()
            if not old:
                raise ValueError("unknown access probe reservation")
            category = json.loads(old["payload"])["category"]
            if environment != (category == "env_cpu_validation") or gpu != (category == "gpu_smoke"):
                raise ValueError("probe evidence category does not match the reservation")
            if result["status"] in {"gpu_verified", "gpu_failed"}:
                details = result.get("details", {})
                if (not old["remote_id"] or not old["submit_started"] or details.get("sandbox_id") != old["remote_id"]
                        or type(details.get("provider_exit_code")) is not int
                        or (result["status"] == "gpu_verified" and details["provider_exit_code"] != 0)):
                    raise ValueError("terminal GPU evidence requires the durable receipt and matching Sandbox exit")
            if result["status"] in {"gpu_stage_failed", "gpu_not_launched"} and old["submit_started"]:
                raise ValueError("a submitted GPU run requires terminal or unknown-receipt evidence")
            if old["state"] == "external_finished_cost_unknown":
                previous = json.loads(old["outcome"])
                if previous == result:
                    return
                if gpu and previous["status"] == "gpu_receipt_unknown" and result["status"] in {"gpu_verified", "gpu_failed"}:
                    db.execute("UPDATE jobs SET outcome=? WHERE id=?", (outcome, key))
                    return
                if "details" in previous or "details" not in result or any(previous[key] != result[key] for key in required):
                    raise ValueError("access probe result already finalized")
            elif old["state"] != "external_reserved":
                raise ValueError("access probe reservation is not active")
            db.execute("UPDATE jobs SET state='external_finished_cost_unknown',outcome=? WHERE id=?", (outcome, key))

    def pause_access(self, reason, next_wake):
        from .contracts import bounded_text

        with self.transaction() as db:
            db.execute(
                "INSERT INTO access_status VALUES(1,?,?) ON CONFLICT(singleton) DO UPDATE SET "
                "pause_reason=excluded.pause_reason,next_wake=excluded.next_wake",
                (bounded_text(reason), bounded_text(next_wake)),
            )

    def resume_access(self, reason, verification_reference):
        """Operator acknowledgement after reviewing externally verified bindings."""
        identifier(verification_reference)
        with self.transaction() as db:
            row = db.execute("SELECT pause_reason FROM access_status").fetchone()
            if not row or row["pause_reason"] != reason:
                raise ValueError("access pause changed; review current evidence")
            db.execute("INSERT INTO access_verifications VALUES(?,?,?)",
                       (verification_reference, reason, self.clock()))
            db.execute("DELETE FROM access_status")

    @staticmethod
    def ledger(db, experiment_id=None):
        where = " WHERE experiment_id=?" if experiment_id else ""
        return tuple(db.execute(
            "SELECT COALESCE(SUM(actual),0), COALESCE(SUM(CASE WHEN actual IS NULL THEN reserve ELSE 0 END),0)"
            f" FROM jobs{where}", (experiment_id,) if experiment_id else (),
        ).fetchone())

    def status(self):
        with self.transaction() as db:
            state = db.execute("SELECT * FROM study").fetchone()
            spent, reserved = self.ledger(db)
            active = db.execute(
                "SELECT id,kind,state,remote_id,deadline FROM jobs WHERE actual IS NULL "
                "AND state IN ('intent','running','uncertain','external_reserved') ORDER BY id",
            ).fetchall()
            last = db.execute("SELECT id,kind,created FROM events ORDER BY rowid DESC LIMIT 1").fetchone()
            decision = db.execute(
                "SELECT id,payload,dispatch_error FROM decisions ORDER BY rowid DESC LIMIT 1",
            ).fetchone()
            experiments = db.execute(
                "SELECT id,stage,decisions,target,cursor,config_hash,checkpoint FROM experiments ORDER BY id",
            ).fetchall()
            approved = state["approved_budget"]
            access = db.execute("SELECT pause_reason,next_wake FROM access_status").fetchone()
            unsettled = db.execute(
                "SELECT id,reserve,outcome FROM jobs WHERE state='external_finished_cost_unknown' ORDER BY id"
            ).fetchall()
            prepared = db.execute("SELECT id,reserve,payload FROM jobs WHERE state='external_prepared' ORDER BY id").fetchall()
            snapshot = json.loads(state["platform_snapshot"]) if state["platform_snapshot"] else None
            platform_remaining = self.platform_headroom(state, spent)
            held = db.execute(
                "SELECT job_id,reserve,proven_bound FROM cost_holds WHERE settled_at IS NULL ORDER BY job_id",
            ).fetchall()
            usage_holds = db.execute(
                "SELECT h.job_id,j.reserve,h.usage_value_cents FROM research_usage_holds h "
                "JOIN jobs j ON j.id=h.job_id WHERE j.actual IS NULL ORDER BY h.created"
            ).fetchall()
            broker = []
            if db.execute("SELECT 1 FROM sqlite_master WHERE type='table' AND name='broker_jobs'").fetchone():
                broker = db.execute(
                    """SELECT b.job_id,b.receipt,b.provider_id,b.state,b.input_id,b.lease_until,b.safe_after,
                       b.seed_begin,b.seed_end,b.reusable_tail,b.send_authorized_at,b.send_not_after,
                       b.send_finished_at,b.cancel_attempts,b.error
                       FROM broker_jobs b WHERE b.state!='finished' ORDER BY b.rowid""",
                ).fetchall()
            return {
                "study_id": state["id"], "mode": state["mode"], "state_version": state["version"],
                "approved_budget": None if approved is None else approved / 100,
                "approval_ref": state["approval_ref"],
                "execution_cap_usd": None if approved is None else state["execution_cap"] / 100,
                "execution_remaining_usd": (
                    None if approved is None else (state["execution_cap"] - spent - reserved) / 100
                ),
                "platform_snapshot": {
                    **{f"{key}_usd": snapshot[key] / 100 for key in
                       ("gross_limit", "net_limit", "credit", "gross_used", "net_spent")},
                    "reference": snapshot["reference"], "observed_at": snapshot["observed_at"],
                } if snapshot else None,
                "platform_gross_remaining_usd": None if platform_remaining is None else (platform_remaining - reserved) / 100,
                "spent_usd": spent / 100, "spend_complete": not unsettled and not active and not held and not usage_holds,
                "reserved_usd": reserved / 100,
                "conservative_budget_held_usd": sum(row["reserve"] for row in held) / 100,
                "jobs_with_verified_cost_bounds": [
                    {"id": row["job_id"], "reserved_usd": row["reserve"] / 100,
                     "proven_upper_bound_usd": row["proven_bound"] / 100}
                    for row in held
                ],
                "research_usage_cash_unknown": [
                    {"id": row["job_id"], "reserved_usd": row["reserve"] / 100,
                     "standard_credit_value_usd": row["usage_value_cents"] / 100} for row in usage_holds
                ],
                "access_gate": dict(access) if access else None,
                "prepared_probes": [
                    {"id": row["id"], "reserved_usd": row["reserve"] / 100, **json.loads(row["payload"])}
                    for row in prepared
                ],
                "finished_probes_cost_unknown": [
                    {"id": row["id"], "reserved_usd": row["reserve"] / 100, **json.loads(row["outcome"])}
                    for row in unsettled
                ],
                "remaining_usd": None if approved is None else (approved - spent - reserved) / 100,
                "stop": state["stopped"], "analysis_pause": state["analysis_paused"],
                "next_wake": state["next_wake"], "active_jobs": [dict(row) for row in active],
                "broker_in_flight": [dict(row) for row in broker],
                "experiments": [dict(row) for row in experiments],
                "latest_event": dict(last) if last else None,
                "latest_decision": (
                    {"id": decision["id"], "action": json.loads(decision["payload"])["action"],
                     "dispatch_error": decision["dispatch_error"]} if decision else None
                ),
            }
