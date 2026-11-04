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
        max_reserve, max_seconds = (10, 600) if category == "env_cpu_validation" else (100, 180)
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

    def finish_access_probe(self, key, result):
        identifier(key)
        required = {"status", "session_ids", "report_ref"}
        if type(result) is not dict or set(result) not in (required, required | {"details"}):
            raise ValueError("invalid access probe evidence")
        if "details" in result and (type(result["details"]) is not dict or len(canonical(result["details"])) > 4000):
            raise ValueError("access probe detail exceeds evidence bounds")
        environment = result["status"] in {"blocked_prelaunch", "env_verified", "env_failed"}
        if not environment and result["status"] not in {"no_terminal_evidence", "model_not_found", "verified", "failed", "tool_sandbox_failed"}:
            raise ValueError("unknown access probe status")
        session_ids = result["session_ids"]
        if type(session_ids) is not list or (len(session_ids) != 0 if environment else not 1 <= len(session_ids) <= 2):
            raise ValueError("access probe session IDs do not match the probe type")
        for session in result["session_ids"]:
            identifier(session)
        if not isinstance(result["report_ref"], str) or not 1 <= len(result["report_ref"]) <= 256:
            raise ValueError("invalid access probe report reference")
        outcome = canonical(result)
        with self.transaction() as db:
            old = db.execute("SELECT state,outcome,payload FROM jobs WHERE id=? AND kind='access_probe'", (key,)).fetchone()
            if not old:
                raise ValueError("unknown access probe reservation")
            if environment != (json.loads(old["payload"])["category"] == "env_cpu_validation"):
                raise ValueError("probe evidence category does not match the reservation")
            if old["state"] == "external_finished_cost_unknown":
                previous = json.loads(old["outcome"])
                if previous == result:
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
                "AND state NOT IN ('external_finished_cost_unknown','external_prepared') ORDER BY id",
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
                "spent_usd": spent / 100, "spend_complete": not unsettled and not active,
                "reserved_usd": reserved / 100,
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
                "experiments": [dict(row) for row in experiments],
                "latest_event": dict(last) if last else None,
                "latest_decision": (
                    {"id": decision["id"], "action": json.loads(decision["payload"])["action"],
                     "dispatch_error": decision["dispatch_error"]} if decision else None
                ),
            }
