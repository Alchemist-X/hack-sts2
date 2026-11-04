"""A deterministic, bounded tick. External jobs run outside controller transactions."""

from __future__ import annotations

import json
from pathlib import Path
from uuid import uuid4

from .adapters import Job, Outcome
from .contracts import TRAIN, bounded_text, canonical, cents, digest, identifier, validate_decision
from .lineage import AcceptedCheckpoint, artifact_identity, validate_job
from .manifests import verify

RETRYABLE = {"api", "oom", "missing_artifact", "reclaimed", "invalid_response"}
TERMINAL = {"succeeded", "failed"}
JOB_BATCH = 4


class DispatchBlocked(ValueError):
    pass


class Controller:
    def __init__(self, store, backend, artifacts, *, research_cents=25, sol_cents=50, admission_policy=None, research_seconds=600):
        self.store = store
        self.backend = backend
        self.artifacts = Path(artifacts)
        self.research_cents = research_cents
        self.sol_cents = sol_cents
        self.admission_policy = admission_policy
        self.research_seconds = research_seconds

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

    def _experiment_limit(self, spec):
        static_limit = cents(spec["max_cost"])
        limits = getattr(self.backend, "experiment_reserve_limits", {})
        return min(static_limit, limits.get(digest(spec), static_limit))

    def _create_job(self, db, logical, attempt, kind, payload, reserve, deadline, experiment=None, event=None):
        state = db.execute("SELECT * FROM study").fetchone()
        if state["stopped"] or state["analysis_paused"]:
            raise DispatchBlocked("study paused or stopped")
        if state["mode"] != "fixture" and db.execute("SELECT 1 FROM access_status").fetchone():
            raise DispatchBlocked("production access gate paused")
        if self.store.provider_paused(db, kind):
            raise DispatchBlocked("provider admission paused")
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
        billing = getattr(self.backend, "billing_sources", {}).get(kind)
        if billing is None or billing[0] == "modal":
            modal_spent, modal_held = self.store.modal_ledger(db)
            platform = self.store.platform_headroom(state, modal_spent)
            if platform is None and state["mode"] != "fixture":
                raise DispatchBlocked("platform budget snapshot required")
            if platform is not None and modal_held + reserve > platform:
                raise DispatchBlocked("platform gross usage ceiling")
        if experiment:
            spec = json.loads(db.execute("SELECT spec FROM experiments WHERE id=?", (experiment,)).fetchone()[0])
            if sum(self.store.ledger(db, experiment)) + reserve > self._experiment_limit(spec):
                raise DispatchBlocked("experiment budget limit")
        key = f"{logical}:{attempt}"
        job = Job(key, logical, attempt, kind, deadline, reserve, payload)
        if self.admission_policy is not None and not self.admission_policy(job):
            raise DispatchBlocked(f"{kind} requires an authorized provider and spending policy")
        if kind == "research" and hasattr(self.backend, "can_plan_research") and not self.backend.can_plan_research(db, job):
            raise DispatchBlocked("finite research authorization exhausted or event unavailable")
        if kind in {"train", "eval"}:
            if self.store.external_gpu_conflicts(db, experiment):
                raise DispatchBlocked("external GPU reservation lacks independent scope")
            if not experiment or db.execute(
                """SELECT 1 FROM jobs WHERE experiment_id=? AND kind IN ('train','eval')
                   AND state IN ('intent','running','uncertain') LIMIT 1""", (experiment,),
            ).fetchone():
                raise DispatchBlocked("experiment already has active or unresolved GPU work")
            if hasattr(self.backend, "gpu_waits_for_research") and self.backend.gpu_waits_for_research(db, experiment):
                raise DispatchBlocked("experiment waiting for research terminal usage reconciliation")
        db.execute(
            """INSERT INTO jobs (id,logical_key,attempt,kind,experiment_id,event_id,payload,reserve,deadline)
               VALUES (?,?,?,?,?,?,?,?,?)""",
            (key, logical, attempt, kind, experiment, event, canonical(payload), reserve, deadline),
        )
        if billing is not None:
            self.store.record_job_billing(db, key, *billing)

    def _schedule(self, db, state):
        if state["stopped"] or state["analysis_paused"]:
            return
        # Analysis is serialized. Check the persisted intent as well as running jobs.
        analysis = db.execute(
            """SELECT 1 FROM jobs WHERE kind='research' AND
               (state IN ('intent','running') OR (state='uncertain' AND deadline>?)) LIMIT 1""",
            (self.store.clock() - getattr(self.backend, "kill_grace_seconds", 0),),
        ).fetchone()
        policy = getattr(self.backend, "research_policy", None)
        event_filter = ""
        event_ids = ()
        if policy is not None:
            if policy.allowed_event_ids is not None:
                event_ids = tuple(policy.allowed_event_ids)
                event_filter = "AND events.id IN (" + ",".join("?" for _ in event_ids) + ")"
            else:
                event_ids = tuple(policy.allowed_config_hashes)
                hashes = ",".join("?" for _ in event_ids)
                event_filter = f"""AND EXISTS (
                    SELECT 1 FROM research_origins o JOIN experiments x ON x.id=o.experiment_id
                    WHERE o.event_id=events.id AND x.config_hash IN ({hashes}))"""
        event = db.execute(
            f"""SELECT events.* FROM events WHERE decision_id IS NULL
               AND kind IN ('bootstrap','result','anomaly','sol_result') AND NOT EXISTS (
               SELECT 1 FROM jobs WHERE jobs.event_id=events.id AND kind='research')
               AND NOT EXISTS (
                   SELECT 1 FROM research_pauses p WHERE p.event_id=events.id
                   OR p.experiment_id=json_extract(events.payload,'$.experiment_id'))
               {event_filter} ORDER BY rowid LIMIT 1""", event_ids,
        ).fetchone()
        if event and not analysis:
            self._schedule_analysis(db, state, event)
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
            "handled_event_ids": [row["event_id"] for row in db.execute(
                "SELECT event_id FROM decisions ORDER BY rowid DESC LIMIT 16"
            )],
            "evidence_refs": [event["id"]],
            "event": {"kind": event["kind"], "evidence": json.loads(event["payload"])},
            "budget": self.store.budget_evidence(db, state),
            "allowed_experiments": dict(db.execute("SELECT id,config_hash FROM experiments").fetchall()),
            "engineering_task_types": ["diagnose", "implement_frozen_task"] if self.backend.tool_enabled else [],
        }
        origin = db.execute("SELECT job_id,experiment_id FROM research_origins WHERE event_id=?",
                            (event["id"],)).fetchone()
        if origin:
            payload["evidence_origin"] = dict(origin)
        experiment_id = origin["experiment_id"] if origin else payload["event"]["evidence"].get("experiment_id")
        experiment = db.execute("SELECT * FROM experiments WHERE id=?", (experiment_id,)).fetchone()
        if experiment:
            spec = json.loads(experiment["spec"])
            payload["current_experiment"] = {
                "experiment_id": experiment_id, "config_hash": experiment["config_hash"],
                "stage": experiment["stage"], "completed_decisions": experiment["decisions"],
                "next_milestone": next((n for n in spec["evaluation_milestones"] if n > experiment["decisions"]), None),
                "next_train_reserve_usd": spec["resource_request"]["train_cost"],
                "hypothesis": spec["hypothesis"], "stop_rules": spec["stop_rules"],
                "promotion_rules": spec["promotion_rules"],
            }
            if event["kind"] == "result":
                payload["matched_comparisons"] = self._matched_results(db, experiment, spec)
        try:
            self._create_job(
                db, f"research-{digest(event['id'])[:32]}", 1, "research", payload,
                self.research_cents, self.store.clock() + self.research_seconds, event=event["id"],
            )
        except DispatchBlocked as error:
            db.execute("UPDATE study SET next_wake=?", (str(error),))

    @staticmethod
    def _matched_results(db, experiment, spec):
        rows = db.execute(
            """SELECT o.experiment_id,e.payload FROM events e JOIN research_origins o ON o.event_id=e.id
               JOIN jobs j ON j.id=o.job_id
               WHERE e.kind='result' AND j.kind='eval' AND j.state='done' AND o.experiment_id<>?
               AND json_extract(e.payload,'$.manifest.metadata.decisions')=?
               AND json_extract(e.payload,'$.manifest.identity.eval_protocol')=?
               AND json_extract(e.payload,'$.manifest.identity.dev_seed_manifest_hash')=?
               ORDER BY e.rowid DESC LIMIT 8""",
            (experiment["id"], experiment["decisions"], spec["eval_protocol"], spec["dev_seed_manifest_hash"]),
        )
        summaries = []
        for row in rows:
            result = json.loads(row["payload"])
            headline = result["metrics"]
            summaries.append({"experiment_id": row["experiment_id"],
                              "config_hash": result["manifest"]["identity"]["config_hash"],
                              "decisions": experiment["decisions"],
                              "eval_protocol": spec["eval_protocol"],
                              "dev_seed_manifest_hash": spec["dev_seed_manifest_hash"],
                              "headline": {key: headline[key] for key in
                                           ("games", "wins", "win_rate_ci95", "progress_mean", "progress_ci95", "results")
                                           if key in headline}})
        return summaries

    def _schedule_experiment(self, db, experiment):
        spec = json.loads(experiment["spec"])
        resources = spec["resource_request"]
        kind = "train" if experiment["stage"] == "needs_train" else "eval"
        seconds = resources["segment_seconds" if kind == "train" else "eval_seconds"]
        segment = experiment["segment"] + 1
        logical = f"{kind}-{digest([experiment['id'], segment])[:32]}"
        checkpoint = json.loads(experiment["checkpoint"]) if experiment["checkpoint"] else None
        study_id = db.execute("SELECT id FROM study").fetchone()[0]
        payload = {
            "study_id": study_id, "spec": spec, "config_hash": experiment["config_hash"], "checkpoint": checkpoint,
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
            else:
                reconcile_until = getattr(self.backend, "reconciliation_deadline", None)
                deadline = reconcile_until(job) if reconcile_until else job.deadline
                if deadline <= self.store.clock():
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
        if job.kind in {"train", "eval"}:
            try:
                validate_job(job)
                reference = job.payload["checkpoint"]
                if reference:
                    with self.store.transaction() as db:
                        self.store.fence(db, owner)
                        accepted = self._accepted_checkpoint(db, job)
                    # Revalidate the saved proof and local file content before asking the provider.
                    self._artifact(self._job(accepted[1]), {
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
                # Held reserves or a scoped pause can temporarily prevent
                # this send. Only confirmed overruns set a study STOP.
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
        if (state["stopped"] or state["analysis_paused"] or job.deadline <= self.store.clock()
                or self.store.provider_paused(db, job.kind)):
            return False
        if state["mode"] != "fixture" and db.execute("SELECT 1 FROM access_status").fetchone():
            return False
        if job.kind in {"train", "eval"}:
            spec = job.payload["spec"]
            if sum(self.store.ledger(db, spec["experiment_id"])) > self._experiment_limit(spec):
                return False
        spent, held = self.store.ledger(db)
        if state["approved_budget"] is None or spent + held > min(state["approved_budget"], state["execution_cap"]):
            return False
        if not self.store.bills_modal(db, job.key):
            return True
        modal_spent, modal_held = self.store.modal_ledger(db)
        platform = self.store.platform_headroom(state, modal_spent)
        return modal_held <= platform if platform is not None else state["mode"] == "fixture"

    def _attach(self, owner, row, remote_id):
        with self.store.transaction() as db:
            self.store.fence(db, owner)
            db.execute("UPDATE jobs SET state='running',remote_id=? WHERE id=?", (remote_id, row["id"]))

    def _quarantine(self, owner, row, reason):
        with self.store.transaction() as db:
            self.store.fence(db, owner)
            db.execute("UPDATE jobs SET state='uncertain',error=? WHERE id=?", (reason, row["id"]))
            if row["kind"] == "research" and self._pause_scoped_research(db, row, reason):
                return
            if row["kind"] == "sol" and self.store.provider_paused(db, "sol"):
                return
            if not (row["kind"] in {"train", "eval"} and row["experiment_id"]
                    and getattr(self.backend, "isolate_gpu_uncertainty", False)):
                db.execute("UPDATE study SET stopped=COALESCE(stopped,?)", (reason,))

    def _pause_scoped_research(self, db, row, reason):
        if getattr(self.backend, "research_policy", None) is None:
            return False
        payload = json.loads(row["payload"])
        experiment = payload.get("current_experiment", {}).get("experiment_id")
        if experiment is None:
            experiment = payload.get("event", {}).get("evidence", {}).get("experiment_id")
        self.store.pause_research_chain(db, row["event_id"], experiment, reason)
        return True

    _identity = staticmethod(artifact_identity)

    def _accepted_checkpoint(self, db, job):
        reference = job.payload["checkpoint"]
        experiment_id = job.payload["spec"]["experiment_id"]
        experiment = db.execute("SELECT * FROM experiments WHERE id=?", (experiment_id,)).fetchone()
        if not experiment or json.loads(experiment["checkpoint"] or "null") != reference:
            raise ValueError("checkpoint is not the accepted experiment head")
        source = db.execute("SELECT * FROM jobs WHERE id=?", (reference["job_key"],)).fetchone()
        if (not source or source["kind"] != "train" or source["state"] not in {"done", "failed"}
                or source["experiment_id"] != experiment_id or not source["outcome"]):
            raise ValueError("checkpoint origin job was not accepted")
        source_job = self._job(source)
        source_identity = artifact_identity(source_job)
        outcome = json.loads(source["outcome"])
        if outcome["checkpoint"] != reference or outcome["manifest"]["identity"] != source_identity:
            raise ValueError("checkpoint is absent from accepted origin outcome")
        return AcceptedCheckpoint(job.payload["study_id"], experiment_id, experiment["config_hash"],
                                  reference, source_identity), source

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
        stride = spec["rollout_shape"]["envs"] * spec["rollout_shape"]["rollout"]
        if decisions % stride:
            raise ValueError("worker progress must report a full rollout")
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
        bound = outcome.cost_bound_cents
        provisional = (actual is None and getattr(self.backend, "verified_cost_bounds", False)
                       and type(bound) is int and 0 < bound <= row["reserve"])
        usage = outcome.usage_value_cents
        scoped_usage = (job.kind == "research" and actual is None and outcome.status == "succeeded"
                        and type(usage) is int and 0 < usage <= row["reserve"]
                        and hasattr(self.backend, "research_usage_admitted"))
        estimate = outcome.reserved_estimate_cents
        reservation_only = (job.kind in {"train", "eval"} and actual is None and type(estimate) is int
                            and 0 < estimate <= row["reserve"] and hasattr(self.backend, "conservative_reserved"))
        scoped_unverified = (job.kind == "research" and actual is None and outcome.status == "failed"
                             and outcome.error == "research_usage_unverified"
                             and getattr(self.backend, "research_policy", None) is not None)
        if not (provisional or scoped_usage or scoped_unverified or reservation_only) and (
            type(actual) is not int or not 0 <= actual <= 10_000_000
        ):
            self._quarantine(owner, row, "terminal job cost unresolved")
            return
        error = None
        result = None
        if outcome.status == "failed":
            error = outcome.error if (scoped_unverified or isinstance(outcome.error, str)
                                      and outcome.error in RETRYABLE) else "worker"
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
            if (scoped_usage or scoped_unverified) and not self.backend.research_usage_admitted(db, job.key):
                raise ValueError("research usage is outside its durable admission")
            if reservation_only and self.backend.conservative_reserved(db, job.key) != estimate:
                raise ValueError("conservative reservation is outside its durable admission")
            if provisional:
                latest = self.backend.authoritative_cost(db, job.key)
                if latest is not None:
                    actual, provisional = latest, False
                    if actual > bound:
                        db.execute("UPDATE study SET stopped=COALESCE(stopped,'billing exceeded verified cost bound')")
            # The raw research response is bounded diagnostic data; it is never executed.
            diagnostic = None
            if job.kind == "research" and isinstance(outcome.output, str):
                encoded = outcome.output[:16000].encode("utf-8", errors="backslashreplace")
                diagnostic = encoded[:16000].decode("utf-8", errors="ignore")
            db.execute("UPDATE jobs SET state=?,actual=?,outcome=?,error=? WHERE id=?",
                       ("failed" if error else "done", actual, canonical(result) if result else
                        diagnostic, error, job.key))
            if scoped_usage or (scoped_unverified and type(usage) is int and usage > 0):
                db.execute("INSERT INTO research_usage_holds VALUES (?,?,?)", (job.key, usage, self.store.clock()))
            if reservation_only:
                db.execute("INSERT INTO conservative_holds VALUES (?,?,?)", (job.key, estimate, self.store.clock()))
            if provisional:
                db.execute("INSERT INTO cost_holds (job_id,reserve,proven_bound,created) VALUES (?,?,?,?)",
                           (job.key, row["reserve"], bound, self.store.clock()))
            if actual is not None and actual > row["reserve"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'job exceeded its reserve')")
            elif self.store.ledger(db)[0] >= state["approved_budget"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'study budget exhausted')")
            elif self.store.ledger(db)[0] >= state["execution_cap"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'current execution cap exhausted')")
            if actual is not None and self.store.bills_modal(db, job.key):
                platform = self.store.platform_headroom(state, self.store.modal_ledger(db)[0])
                if platform is not None and platform < 0:
                    db.execute("UPDATE study SET stopped=COALESCE(stopped,'known Modal use exceeded workspace limit')")
            if error:
                self._failed(db, row, job, error, result)
            elif job.kind == "research":
                self._accept_decision(db, state, row, result)
            elif job.kind in {"train", "eval"}:
                self._completed_worker(db, row, job, result)
            else:
                experiment_id = job.payload.get("experiment_id")
                origin = (job.key, experiment_id) if experiment_id else None
                self.store.append_event(db, f"sol-{digest(job.key)[:32]}", "sol_result",
                                        result, self.store.clock(), origin=origin)

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
        scoped_research = job.kind == "research" and getattr(self.backend, "research_policy", None) is not None
        retryable = error == "api" if scoped_research else error in RETRYABLE
        if job.attempt < 2 and retryable and not reached_target:
            try:
                self._create_job(db, job.logical_key, job.attempt + 1, job.kind, payload,
                                 job.reserve_cents, job.deadline, row["experiment_id"], row["event_id"])
                return
            except DispatchBlocked:
                pass
        if job.kind == "research":
            reason = f"research {error}; inspect {job.key}"
            if not self._pause_scoped_research(db, row, reason):
                db.execute("UPDATE study SET analysis_paused=?", (reason,))
            return
        if row["experiment_id"]:
            stage = "needs_eval" if reached_target else "failed"
            if (stage == "failed" and error == "reclaimed" and result and job.kind == "train"
                    and job.payload["spec"]["eval_protocol"] == "silent-a10-capacity-v1"):
                # The provider is terminal and the complete same-lineage head
                # is verified. A later segment still needs its own admission.
                stage = "needs_train"
            db.execute("UPDATE experiments SET stage=? WHERE id=?", (stage, row["experiment_id"]))
            if reached_target:
                # Analysis gets the recovered checkpoint incident together with its evaluation.
                return
        self.store.append_event(db, f"anomaly-{digest(job.key)[:32]}", "anomaly",
                                {"job_key": job.key, "kind": job.kind, "error": error,
                                 "experiment_id": row["experiment_id"], "checkpoint": result}, self.store.clock(),
                                origin=(job.key, row["experiment_id"]) if row["experiment_id"] else None)

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
        identity = job.key
        if job.payload["spec"]["eval_protocol"] == "silent-a10-capacity-v1":
            identity = [experiment, job.payload["config_hash"], job.payload["start_decisions"]]
        self.store.append_event(db, f"result-{digest(identity)[:32]}", "result", evidence, self.store.clock(),
                                origin=(job.key, experiment))

    def _accept_decision(self, db, state, row, result):
        if result["expected_state_version"] != state["version"]:
            if not self._pause_scoped_research(db, row, "research decision state is stale"):
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
                experiment = json.loads(row["payload"]).get("current_experiment")
                if experiment:
                    payload["experiment_id"] = experiment["experiment_id"]
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
