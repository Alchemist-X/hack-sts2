"""A deterministic, bounded tick. External jobs run outside controller transactions."""

from __future__ import annotations

import json
from pathlib import Path
from uuid import uuid4

from .adapters import Job, Outcome
from .contracts import TRAIN, bounded_text, canonical, cents, digest, identifier, validate_decision
from .manifests import verify

RETRYABLE = {"api", "oom", "missing_artifact", "reclaimed", "invalid_response"}
TERMINAL = {"succeeded", "failed"}
JOB_BATCH = 4


class DispatchBlocked(ValueError):
    pass


class Controller:
    def __init__(self, store, backend, artifacts, *, research_cents=25, sol_cents=50):
        self.store = store
        self.backend = backend
        self.artifacts = Path(artifacts)
        self.research_cents = research_cents
        self.sol_cents = sol_cents

    def tick(self):
        owner = uuid4().hex
        if not self.store.acquire(owner, 120):
            return False
        try:
            with self.store.transaction() as db:
                state = self.store.fence(db, owner)
                if (state["mode"] == "fixture") != self.backend.fixture:
                    raise ValueError("fixture backend cannot share operational state")
                active = db.execute(
                    "SELECT * FROM jobs WHERE state IN ('intent','running','uncertain') "
                    "ORDER BY COALESCE(last_checked,0),rowid LIMIT ?", (JOB_BATCH,),
                ).fetchall()
            for row in active:
                self._reconcile(owner, row)
            with self.store.transaction() as db:
                state = self.store.fence(db, owner)
                self._schedule(db, state)
            return True
        finally:
            self.store.release(owner)

    def _create_job(self, db, logical, attempt, kind, payload, reserve, deadline, experiment=None, event=None):
        state = db.execute("SELECT * FROM study").fetchone()
        if state["stopped"] or state["analysis_paused"]:
            raise DispatchBlocked("study paused or stopped")
        if type(reserve) is not int or reserve <= 0:
            raise DispatchBlocked("positive job reserve required")
        if deadline <= self.store.clock():
            raise DispatchBlocked("job deadline elapsed")
        spent, held = self.store.ledger(db)
        if state["approved_budget"] is None:
            raise DispatchBlocked("approved_budget is null")
        if spent + held + reserve > state["approved_budget"]:
            raise DispatchBlocked("study budget limit")
        if spent + held + reserve > state["execution_cap"]:
            raise DispatchBlocked("current execution cap")
        platform = self.store.platform_headroom(state, spent)
        if platform is None and state["mode"] != "fixture":
            raise DispatchBlocked("platform budget snapshot required")
        if platform is not None and held + reserve > platform:
            raise DispatchBlocked("platform gross usage ceiling")
        if experiment:
            spec = json.loads(db.execute("SELECT spec FROM experiments WHERE id=?", (experiment,)).fetchone()[0])
            if sum(self.store.ledger(db, experiment)) + reserve > cents(spec["max_cost"]):
                raise DispatchBlocked("experiment budget limit")
        key = f"{logical}:{attempt}"
        db.execute(
            """INSERT INTO jobs (id,logical_key,attempt,kind,experiment_id,event_id,payload,reserve,deadline)
               VALUES (?,?,?,?,?,?,?,?,?)""",
            (key, logical, attempt, kind, experiment, event, canonical(payload), reserve, deadline),
        )

    def _schedule(self, db, state):
        if state["stopped"] or state["analysis_paused"]:
            return
        # Analysis is serialized. Check the persisted intent as well as running jobs.
        analysis = db.execute(
            "SELECT 1 FROM jobs WHERE kind='research' AND actual IS NULL LIMIT 1",
        ).fetchone()
        event = db.execute(
            """SELECT events.* FROM events WHERE decision_id IS NULL AND NOT EXISTS (
               SELECT 1 FROM jobs WHERE jobs.event_id=events.id AND kind='research')
               ORDER BY rowid LIMIT 1""",
        ).fetchone()
        if event and not analysis:
            self._schedule_analysis(db, state, event)
            return
        for experiment in db.execute(
            "SELECT * FROM experiments WHERE stage IN ('needs_train','needs_eval') ORDER BY id",
        ).fetchall():
            try:
                self._schedule_experiment(db, experiment)
            except DispatchBlocked as error:
                db.execute("UPDATE study SET next_wake=?", (str(error),))

    def _schedule_analysis(self, db, state, event):
        payload = {
            "decision_id": f"decision-{digest([state['id'], event['id']])[:32]}",
            "study_id": state["id"],
            "expected_state_version": state["version"],
            "consumed_event_ids": [event["id"]],
            "evidence_refs": [event["id"]],
            "event": {"kind": event["kind"], "evidence": json.loads(event["payload"])},
            "budget": {
                "approved_cents": state["approved_budget"],
                "spent_and_reserved_cents": self.store.ledger(db),
            },
            "allowed_experiments": dict(db.execute("SELECT id,config_hash FROM experiments").fetchall()),
            "engineering_task_types": ["diagnose", "implement_frozen_task"] if self.backend.tool_enabled else [],
        }
        try:
            self._create_job(
                db, f"research-{digest(event['id'])[:32]}", 1, "research", payload,
                self.research_cents, self.store.clock() + 600, event=event["id"],
            )
        except DispatchBlocked as error:
            db.execute("UPDATE study SET next_wake=?", (str(error),))

    def _schedule_experiment(self, db, experiment):
        spec = json.loads(experiment["spec"])
        resources = spec["resource_request"]
        kind = "train" if experiment["stage"] == "needs_train" else "eval"
        seconds = resources["segment_seconds" if kind == "train" else "eval_seconds"]
        segment = experiment["segment"] + 1
        logical = f"{kind}-{digest([experiment['id'], segment])[:32]}"
        checkpoint = json.loads(experiment["checkpoint"]) if experiment["checkpoint"] else None
        payload = {
            "spec": spec, "config_hash": experiment["config_hash"], "checkpoint": checkpoint,
            "start_decisions": experiment["decisions"], "target_decisions": experiment["target"],
            "start_cursor": experiment["cursor"],
        }
        self._create_job(db, logical, 1, kind, payload, cents(resources[f"{kind}_cost"]),
                         min(spec["absolute_deadline"], self.store.clock() + seconds), experiment["id"])
        db.execute("UPDATE experiments SET stage=?,segment=? WHERE id=?", (kind, segment, experiment["id"]))

    @staticmethod
    def _job(row):
        return Job(row["id"], row["logical_key"], row["attempt"], row["kind"], row["deadline"],
                   row["reserve"], json.loads(row["payload"]))

    def _reconcile(self, owner, row):
        job = self._job(row)
        with self.store.transaction() as db:
            self.store.fence(db, owner)
            db.execute("UPDATE jobs SET last_checked=? WHERE id=?", (self.store.clock(), row["id"]))
        if row["remote_id"]:
            try:
                outcome = self.backend.poll(row["remote_id"])
                if not isinstance(outcome, Outcome) or outcome.status not in TERMINAL | {"running"}:
                    raise ValueError("invalid provider status")
            except Exception:
                outcome = Outcome("running")
            if outcome.status in TERMINAL:
                self._settle(owner, row, outcome)
            elif job.deadline <= self.store.clock():
                self._quarantine(owner, row, "remote completion or spend unresolved at deadline")
            return
        try:
            remote_id = self.backend.lookup(job) if row["submit_started"] else None
            if remote_id is not None:
                identifier(remote_id)
        except Exception:
            if job.deadline <= self.store.clock():
                self._quarantine(owner, row, "submission receipt unresolved at deadline")
            return
        if remote_id:
            self._attach(owner, row, remote_id)
            return
        if row["submit_started"]:
            # The backend contract promises authoritative absence and no billable job.
            # Never loop submissions on the same persisted attempt.
            self._settle(owner, row, Outcome("failed", 0, error="api"))
            return
        reference = job.payload.get("checkpoint") if job.kind in {"train", "eval"} else None
        if reference:
            with self.store.transaction() as db:
                self.store.fence(db, owner)
                source = db.execute("SELECT * FROM jobs WHERE id=?", (reference["job_key"],)).fetchone()
            try:
                self._artifact(self._job(source), {
                    "manifest_hash": reference["manifest_hash"], "generation": reference["generation"],
                })
            except (ValueError, TypeError, KeyError, OSError, AttributeError):
                self._settle(owner, row, Outcome("failed", 0, error="missing_artifact"))
                return
        with self.store.transaction() as db:
            state = self.store.fence(db, owner)
            if job.deadline <= self.store.clock():
                db.execute("UPDATE jobs SET state='cancelled',actual=0,error='deadline' WHERE id=?", (row["id"],))
                self._failed(db, row, job, "deadline", None)
                return
            if not self._can_submit(db, state, job):
                if not (state["stopped"] or state["analysis_paused"]):
                    db.execute("UPDATE study SET stopped='budget unavailable before submit'")
                return
            db.execute("UPDATE jobs SET submit_started=1 WHERE id=?", (row["id"],))
            admission = {"study_id": state["id"], "lease_owner": owner, "lease_until": state["lease_until"]}
        try:
            if self.backend.fixture:
                # The fixture is a bounded local DB write, so operator stop and a
                # successor tick cannot interleave with it.
                with self.store.transaction() as db:
                    state = self.store.fence(db, owner)
                    if not self._can_submit(db, state, job):
                        return
                    remote_id = identifier(self.backend.submit(job))
            else:
                # The remote service must check this fence atomically with acceptance.
                remote_id = identifier(self.backend.submit_admitted(job, admission))
        except Exception:
            return  # reconcile by key before considering another submission
        self._attach(owner, row, remote_id)

    def _can_submit(self, db, state, job):
        if state["stopped"] or state["analysis_paused"] or job.deadline <= self.store.clock():
            return False
        spent, held = self.store.ledger(db)
        platform = self.store.platform_headroom(state, spent)
        return (spent + held <= min(state["approved_budget"], state["execution_cap"])
                and (held <= platform if platform is not None else state["mode"] == "fixture"))

    def _attach(self, owner, row, remote_id):
        with self.store.transaction() as db:
            self.store.fence(db, owner)
            db.execute("UPDATE jobs SET state='running',remote_id=? WHERE id=?", (remote_id, row["id"]))

    def _quarantine(self, owner, row, reason):
        with self.store.transaction() as db:
            self.store.fence(db, owner)
            db.execute("UPDATE jobs SET state='uncertain',error=? WHERE id=?", (reason, row["id"]))
            db.execute("UPDATE study SET stopped=COALESCE(stopped,?)", (reason,))

    @staticmethod
    def _identity(job):
        spec = job.payload["spec"]
        return {
            "kind": job.kind, "job_key": job.key, "experiment_id": spec["experiment_id"],
            "config_hash": job.payload["config_hash"], "code_commit": spec["code_commit"],
            "image_digest": spec["image_digest"], "engine_hash": spec["engine_hash"], "schema_hash": spec["schema_hash"],
            "train_seed_manifest_hash": spec["train_seed_manifest_hash"],
            "dev_seed_manifest_hash": spec["dev_seed_manifest_hash"], "eval_protocol": spec["eval_protocol"],
            "input_checkpoint": job.payload["checkpoint"]["manifest_hash"] if job.payload["checkpoint"] else None,
        }

    def _artifact(self, job, output):
        if type(output) is not dict or set(output) != {"manifest_hash", "generation"}:
            raise ValueError("worker output must reference a ready manifest")
        generation = identifier(output["generation"])
        folder = self.artifacts / job.key / generation
        if folder.parent.is_symlink():
            raise ValueError("job artifact directory is a symlink")
        document = verify(folder, output["manifest_hash"], self._identity(job))
        spec = job.payload["spec"]
        metadata = document["metadata"]
        reason = metadata["termination"]
        decisions = metadata["decisions"]
        if job.kind == "train":
            if metadata["resume_mode"] != spec["checkpoint_policy"]["resume_mode"]:
                raise ValueError("checkpoint recovery policy changed")
            cursor = metadata["cursor"]
            start_cursor = job.payload["start_cursor"]
            if cursor < start_cursor or (cursor == start_cursor and metadata["resume_mode"] == "fresh_episodes"):
                raise ValueError("checkpoint does not preserve a non-repeating seed stream")
            if not job.payload["start_decisions"] < decisions <= job.payload["target_decisions"]:
                raise ValueError("unexpected train progress")
            if reason != ("milestone" if decisions == job.payload["target_decisions"] else "segment"):
                if reason != "checkpoint":
                    raise ValueError("training termination mismatched progress")
        elif reason != "completed" or decisions != job.payload["start_decisions"]:
            raise ValueError("eval did not complete at frozen training progress")
        metric_path = folder / document["files"]["metrics"]["name"]
        if metric_path.stat().st_size > 24_000:
            raise ValueError("metric summary too large")
        metrics = json.loads(metric_path.read_bytes())
        if type(metrics) is not dict:
            raise ValueError("invalid metric summary")
        canonical(metrics)
        return {"checkpoint": {**output, "job_key": job.key}, "manifest": document, "metrics": metrics}

    def _settle(self, owner, row, outcome):
        job = self._job(row)
        actual = outcome.actual_cents
        if type(actual) is not int or not 0 <= actual <= 10_000_000:
            self._quarantine(owner, row, "terminal job cost unresolved")
            return
        error = None
        result = None
        if outcome.status == "failed":
            error = outcome.error if isinstance(outcome.error, str) and outcome.error in RETRYABLE else "worker"
        try:
            if job.kind in {"train", "eval"} and (not error or (job.kind == "train" and outcome.output is not None)):
                result = self._artifact(job, outcome.output)
                termination = result["manifest"]["metadata"]["termination"]
                if not error and termination == "checkpoint":
                    raise ValueError("partial checkpoint is not a complete segment")
                if error and termination != "checkpoint":
                    result = None
            elif not error and job.kind == "research":
                result = validate_decision(outcome.output, job.payload)
            elif not error and job.kind == "sol":
                if type(outcome.output) is not dict or set(outcome.output) != {"summary", "artifact_refs", "task_id"}:
                    raise ValueError("invalid SOL result")
                result = outcome.output
                if result["task_id"] != job.payload["task_id"]:
                    raise ValueError("mismatched SOL task ID")
                bounded_text(result["summary"], 8000)
                refs = result["artifact_refs"]
                if type(refs) is not list or not 1 <= len(refs) <= 10:
                    raise ValueError("SOL must report artifact references")
                for ref in refs:
                    bounded_text(ref, 256)
        except (ValueError, TypeError, KeyError, OSError, AttributeError, RecursionError):
            result = None
            error = "invalid_response" if job.kind in {"research", "sol"} else "missing_artifact"
        with self.store.transaction() as db:
            state = self.store.fence(db, owner)
            # The raw research response is bounded diagnostic data; it is never executed.
            diagnostic = None
            if job.kind == "research" and isinstance(outcome.output, str):
                encoded = outcome.output[:16000].encode("utf-8", errors="backslashreplace")
                diagnostic = encoded[:16000].decode("utf-8", errors="ignore")
            db.execute("UPDATE jobs SET state=?,actual=?,outcome=?,error=? WHERE id=?",
                       ("failed" if error else "done", actual, canonical(result) if result else
                        diagnostic, error, job.key))
            if actual > row["reserve"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'job exceeded its reserve')")
            elif self.store.ledger(db)[0] >= state["approved_budget"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'study budget exhausted')")
            elif self.store.ledger(db)[0] >= state["execution_cap"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'current execution cap exhausted')")
            if error:
                self._failed(db, row, job, error, result)
            elif job.kind == "research":
                self._accept_decision(db, state, row, result)
            elif job.kind in {"train", "eval"}:
                self._completed_worker(db, row, job, result)
            else:
                self.store.append_event(db, f"sol-{digest(job.key)[:32]}", "sol_result", result, self.store.clock())

    def _save_checkpoint(self, db, experiment_id, result):
        metadata = result["manifest"]["metadata"]
        db.execute("UPDATE experiments SET checkpoint=?,decisions=?,cursor=? WHERE id=?",
                   (canonical(result["checkpoint"]), metadata["decisions"], metadata["cursor"], experiment_id))

    def _failed(self, db, row, job, error, result):
        payload = job.payload
        if job.kind == "train" and result:
            self._save_checkpoint(db, row["experiment_id"], result)
            metadata = result["manifest"]["metadata"]
            payload.update(checkpoint=result["checkpoint"], start_decisions=metadata["decisions"],
                           start_cursor=metadata["cursor"])
        reached_target = result and job.kind == "train" and payload["start_decisions"] >= payload["target_decisions"]
        if job.attempt < 2 and error in RETRYABLE and not reached_target:
            try:
                self._create_job(db, job.logical_key, job.attempt + 1, job.kind, payload,
                                 job.reserve_cents, job.deadline, row["experiment_id"], row["event_id"])
                return
            except DispatchBlocked:
                pass
        if job.kind == "research":
            db.execute("UPDATE study SET analysis_paused=?", (f"research {error}; inspect {job.key}",))
            return
        if row["experiment_id"]:
            stage = "needs_eval" if reached_target else "failed"
            db.execute("UPDATE experiments SET stage=? WHERE id=?", (stage, row["experiment_id"]))
            if reached_target:
                # Analysis gets the recovered checkpoint incident together with its evaluation.
                return
        self.store.append_event(db, f"anomaly-{digest(job.key)[:32]}", "anomaly",
                                {"job_key": job.key, "kind": job.kind, "error": error,
                                 "experiment_id": row["experiment_id"], "checkpoint": result}, self.store.clock())

    def _completed_worker(self, db, row, job, result):
        experiment = row["experiment_id"]
        if job.kind == "train":
            self._save_checkpoint(db, experiment, result)
            metadata = result["manifest"]["metadata"]
            stage = "needs_eval" if metadata["termination"] == "milestone" else "needs_train"
            db.execute("UPDATE experiments SET stage=? WHERE id=?", (stage, experiment))
            return
        db.execute("UPDATE experiments SET stage='await_decision' WHERE id=?", (experiment,))
        origin = db.execute("SELECT id,error FROM jobs WHERE id=? AND state='failed'",
                            (job.payload["checkpoint"]["job_key"],)).fetchone()
        evidence = {**result, "experiment_id": experiment}
        if origin:
            evidence["checkpoint_recovery_incident"] = dict(origin)
        self.store.append_event(db, f"result-{digest(job.key)[:32]}", "result", evidence, self.store.clock())

    def _accept_decision(self, db, state, row, result):
        if result["expected_state_version"] != state["version"]:
            db.execute("UPDATE study SET analysis_paused='research decision state is stale'")
            return
        action = result["action"]
        dispatch_error = None
        try:
            if action in TRAIN:
                self._dispatch_experiment(db, action, result)
            elif action == "REQUEST_SOL":
                if cents(result["reserve_budget_usd"]) < self.sol_cents or not self.backend.tool_enabled:
                    raise DispatchBlocked("SOL capability or reserve unavailable")
                task_id = f"sol-{digest(result['decision_id'])[:32]}"
                payload = {"task_id": task_id, "decision_id": result["decision_id"], **result["engineering_task"]}
                self._create_job(db, task_id, 1, "sol", payload, self.sol_cents, self.store.clock() + 900)
            elif action in {"PAUSE", "STOP"}:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,?)",
                           (f"{action}: {result['action_reason']}",))
        except DispatchBlocked as error:
            dispatch_error = str(error)
        db.execute("INSERT INTO decisions VALUES (?,?,?,?,?)",
                   (result["decision_id"], row["event_id"], canonical(result), dispatch_error, self.store.clock()))
        db.execute("UPDATE events SET decision_id=? WHERE id=?", (result["decision_id"], row["event_id"]))
        db.execute("UPDATE study SET version=version+1,next_wake=?",
                   (dispatch_error or result["next_wake_condition"],))

    def _dispatch_experiment(self, db, action, decision):
        experiment_id = decision["experiment_spec"]["experiment_id"]
        experiment = db.execute("SELECT * FROM experiments WHERE id=?", (experiment_id,)).fetchone()
        if action == "CONTINUE":
            if experiment["stage"] != "await_decision":
                raise DispatchBlocked("CONTINUE requires a completed evaluation milestone")
        elif experiment["stage"] != "idle":
            raise DispatchBlocked("experiment already launched")
        spec = json.loads(experiment["spec"])
        if action == "REPLICATE" and not spec["parent_id"]:
            raise DispatchBlocked("replication requires a parent")
        resources = spec["resource_request"]
        if cents(decision["reserve_budget_usd"]) < cents(resources["train_cost"]):
            raise DispatchBlocked("decision reserve below fixed train segment cost")
        target = next((n for n in spec["evaluation_milestones"] if n > experiment["decisions"]), None)
        if target is None:
            raise DispatchBlocked("experiment already at sample target")
        # Create the first intent in the decision transaction; failed dispatch leaves the experiment intact.
        self._schedule_experiment(db, {**experiment, "stage": "needs_train", "target": target})
        db.execute("UPDATE experiments SET target=? WHERE id=?", (target, experiment_id))
