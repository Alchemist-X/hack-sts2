"""Durable single-host broker for Raven. Provider calls never run inside its SQLite transactions."""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, replace
from pathlib import Path
from uuid import uuid4

from .adapters import Job, MODEL_PROVIDER_ISSUES, Outcome, ProviderIntegrityError, ProviderScopeError
from .contracts import DIGEST, canonical, digest, identifier, integer
from .controller import Controller
from .lineage import AcceptedCheckpoint, validate_job

SCHEMA = """
CREATE TABLE IF NOT EXISTS broker_jobs (
    job_id TEXT PRIMARY KEY REFERENCES jobs(id),
    receipt TEXT NOT NULL UNIQUE,
    launch_token TEXT UNIQUE,
    provider_id TEXT UNIQUE,
    state TEXT NOT NULL DEFAULT 'accepted',
    last_checked REAL NOT NULL DEFAULT 0,
    send_authorized_at REAL, send_not_after REAL, send_finished_at REAL,
    cancel_attempts INTEGER NOT NULL DEFAULT 0, cost_pending INTEGER NOT NULL DEFAULT 0,
    input_id TEXT, invocation_id TEXT,
    fence INTEGER NOT NULL DEFAULT 0,
    lease_until REAL NOT NULL DEFAULT 0,
    safe_after REAL NOT NULL DEFAULT 0,
    claim_proof TEXT, worker_outcome TEXT, outcome TEXT,
    seed_begin INTEGER, seed_end INTEGER, reusable_tail INTEGER,
    error TEXT
);
CREATE TABLE IF NOT EXISTS broker_research_usage_admissions (
    job_id TEXT PRIMARY KEY REFERENCES broker_jobs(job_id)
);
CREATE TABLE IF NOT EXISTS broker_conservative_reservations (
    job_id TEXT PRIMARY KEY REFERENCES broker_jobs(job_id),
    estimate_cents INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS broker_cost_envelopes (
    job_id TEXT PRIMARY KEY REFERENCES broker_jobs(job_id),
    upper_cents INTEGER NOT NULL
);
CREATE TABLE IF NOT EXISTS broker_cloud_archive (
    job_id TEXT PRIMARY KEY REFERENCES broker_jobs(job_id),
    kept_generation TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS broker_detached (
    job_id TEXT PRIMARY KEY REFERENCES broker_jobs(job_id),
    grant_json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS broker_generations (
    job_id TEXT NOT NULL REFERENCES broker_jobs(job_id),
    fence INTEGER NOT NULL,
    generation TEXT NOT NULL,
    manifest_hash TEXT NOT NULL,
    decisions INTEGER NOT NULL,
    cursor INTEGER NOT NULL, issued_until INTEGER,
    partial INTEGER NOT NULL,
    PRIMARY KEY(job_id,generation),
    UNIQUE(job_id, decisions, partial)
);
"""
GPU_KINDS = {"train", "eval"}
PROVIDER_PENDING = {"sending", "ambiguous", "submitted"}
TERMINAL = {"succeeded", "failed"}
MAX_SEND_SECONDS = 5


@dataclass(frozen=True)
class Claim:
    job: Job
    accepted_checkpoint: AcceptedCheckpoint | None
    receipt: str
    input_id: str
    invocation_id: str
    fence: int
    lease_until: float
    seed_range: tuple[int, int] | None


@dataclass(frozen=True)
class DetachedGrant:
    """One offline invocation whose authority expires at the frozen job deadline."""

    job: Job
    accepted_checkpoint: AcceptedCheckpoint | None
    receipt: str
    launch_token: str
    fence: int
    seed_range: tuple[int, int] | None

    @classmethod
    def from_json(cls, encoded):
        raw = json.loads(encoded)
        raw["job"] = Job(**raw["job"])
        if raw["accepted_checkpoint"] is not None:
            raw["accepted_checkpoint"] = AcceptedCheckpoint(**raw["accepted_checkpoint"])
        if raw["seed_range"] is not None:
            raw["seed_range"] = tuple(raw["seed_range"])
        return cls(**raw)


class RavenBroker:
    """Transactional in-process API. A network wrapper must authenticate worker callers."""

    fixture = False
    verified_cost_bounds = True
    isolate_gpu_uncertainty = True

    def __init__(self, store, artifacts, *, authorize_spawn=None, verify_cost_bound=None, reserve_bound=None, conservative_reserve=None, research_policy=None,
                 tool_enabled=False, experiment_reserve_limits=None, billing_sources=None,
                 grant_seconds=30, kill_grace_seconds=10):
        self.store = store
        self.artifacts = Path(artifacts)
        self.authorize_spawn = authorize_spawn
        self.verify_cost_bound = verify_cost_bound
        self.reserve_bound = reserve_bound
        self.conservative_reserve = conservative_reserve
        self.research_policy = research_policy
        self.experiment_reserve_limits = experiment_reserve_limits or {}
        self.billing_sources = billing_sources or {}
        self.tool_enabled = bool(tool_enabled)
        self.grant_seconds = integer(grant_seconds, 1)
        self.kill_grace_seconds = integer(kill_grace_seconds, 1)
        if self.grant_seconds > 60 or self.kill_grace_seconds > 30:
            raise ValueError("unbounded worker lease settings")
        self.controller = Controller(store, self, artifacts, admission_policy=authorize_spawn if reserve_bound or conservative_reserve or research_policy else None,
                                     research_seconds=180 if research_policy else 600,
                                     research_cents=research_policy.reserve_cents if research_policy else 25)
        with self.store.transaction() as db:
            db.executescript(SCHEMA)

    def research_usage_admitted(self, db, job_key):
        return db.execute("SELECT 1 FROM broker_research_usage_admissions WHERE job_id=?", (job_key,)).fetchone() is not None

    @staticmethod
    def conservative_reserved(db, job_key):
        row = db.execute("SELECT estimate_cents FROM broker_conservative_reservations WHERE job_id=?",
                         (job_key,)).fetchone()
        return row["estimate_cents"] if row else None

    def can_plan_research(self, db, job):
        if self.research_policy is None:
            return True
        policy = self.research_policy
        if not policy.allows(job, self.store.clock()):
            return False
        event, = job.payload["consumed_event_ids"]
        if policy.allowed_event_ids is None:
            origin = db.execute(
                """SELECT o.job_id,o.experiment_id,x.config_hash,e.kind,j.kind AS job_kind,j.state
                   FROM research_origins o JOIN experiments x ON x.id=o.experiment_id
                   JOIN events e ON e.id=o.event_id JOIN jobs j ON j.id=o.job_id
                   WHERE o.event_id=? AND e.decision_id IS NULL""", (event,),
            ).fetchone()
            if (origin is None or origin["config_hash"] not in policy.allowed_config_hashes
                    or job.payload.get("evidence_origin") != {key: origin[key] for key in ("job_id", "experiment_id")}
                    or (origin["kind"], origin["job_kind"], origin["state"]) not in {
                        ("result", "eval", "done"), ("sol_result", "sol", "done"),
                        ("anomaly", "train", "failed"), ("anomaly", "eval", "failed"),
                        ("anomaly", "sol", "failed"),
                    }):
                return False
        count = db.execute(
            """SELECT COUNT(*) FROM broker_research_usage_admissions a JOIN jobs j ON j.id=a.job_id
               WHERE j.event_id=?""", (event,),
        ).fetchone()[0]
        return count < 2

    def gpu_waits_for_research(self, db, experiment_id):
        return self.research_policy is not None and db.execute(
            """SELECT 1 FROM research_pauses WHERE experiment_id=?
               UNION ALL
               SELECT 1 FROM jobs WHERE kind='research' AND state IN ('intent','running','uncertain')
               AND COALESCE(json_extract(payload,'$.current_experiment.experiment_id'),
                            json_extract(payload,'$.event.evidence.experiment_id'))=? LIMIT 1""",
            (experiment_id, experiment_id),
        ).fetchone() is not None

    def server_time(self):
        """Clock sample for an authenticated worker's conservative lease translation."""
        return self.store.clock()

    def _state(self, db):
        return db.execute("SELECT * FROM study").fetchone()

    def _dispatch_allowed(self, db, row):
        state = self._state(db)
        if row["kind"] in GPU_KINDS:
            experiment_id = row["experiment_id"]
            if (experiment_id is None or self.store.external_gpu_conflicts(db, experiment_id)
                    or self.gpu_waits_for_research(db, experiment_id)):
                return False
            # The same experiment has one effective writer. Positive-only
            # receipt recovery and the reservation survive provider ambiguity.
            if db.execute(
                """SELECT 1 FROM jobs j LEFT JOIN broker_jobs b ON b.job_id=j.id
                   WHERE j.experiment_id=? AND j.id<>? AND j.kind IN ('train','eval')
                     AND (j.state='uncertain' OR b.state IN ('spawning','sending','ambiguous','submitted'))
                   LIMIT 1""", (experiment_id, row["id"]),
            ).fetchone():
                return False
        return (state["mode"] == "local" and row["actual"] is None
                and row["state"] in {"intent", "running"}
                and self.controller._can_submit(db, state, Controller._job(row)))

    @staticmethod
    def _job_row(db, key):
        row = db.execute("SELECT * FROM jobs WHERE id=?", (key,)).fetchone()
        if row is None:
            raise ValueError("job is absent from the controller ledger")
        return row

    @staticmethod
    def _check_job(row, job):
        if Controller._job(row) != job:
            raise ValueError("broker job differs from the controller intent")

    def submit_admitted(self, job, admission):
        """Atomically accept the current fenced and already reserved controller intent."""
        if set(admission) != {"study_id", "lease_owner", "lease_until"}:
            raise ValueError("invalid controller admission envelope")
        with self.store.transaction() as db:
            state = self.store.fence(db, admission["lease_owner"])
            if (state["id"], state["lease_until"]) != (admission["study_id"], admission["lease_until"]):
                raise ValueError("controller admission fence differs")
            row = self._job_row(db, job.key)
            self._check_job(row, job)
            if not row["submit_started"] or not self._dispatch_allowed(db, row):
                raise ValueError("controller intent is not eligible for broker acceptance")
            if job.kind not in GPU_KINDS | {"research", "sol"} or (job.kind == "sol" and not self.tool_enabled):
                raise ValueError("job kind or tool-enabled SOL unavailable")
            receipt = f"raven-{digest([state['id'], job.key])[:40]}"
            db.execute("INSERT OR IGNORE INTO broker_jobs (job_id,receipt) VALUES (?,?)", (job.key, receipt))
            return receipt

    def lookup(self, job):
        with self.store.transaction() as db:
            self._check_job(self._job_row(db, job.key), job)
            row = db.execute("SELECT receipt FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            return row["receipt"] if row else None

    def reconciliation_deadline(self, job):
        with self.store.transaction() as db:
            # This is a bounded read/mirror grace. It grants no provider runtime.
            return job.deadline + (180 if self._detached(db, job.key) else 0)

    def poll(self, receipt):
        with self.store.transaction() as db:
            row = db.execute("SELECT state,outcome FROM broker_jobs WHERE receipt=?", (identifier(receipt),)).fetchone()
            if row is None:
                raise ValueError("unknown broker receipt")
            return Outcome(**json.loads(row["outcome"])) if row["state"] == "finished" else Outcome("running")

    def expire_unspawned(self):
        """Settle only intents which provably never crossed provider authorization."""
        with self.store.transaction() as db:
            state = self._state(db)
            rows = db.execute(
                """SELECT b.job_id FROM broker_jobs b JOIN jobs j ON j.id=b.job_id
                   WHERE b.state IN ('accepted','spawning') AND (j.deadline<=? OR ? IS NOT NULL)""",
                (self.store.clock(), state["stopped"]),
            ).fetchall()
            for row in rows:
                self._finish(db, row["job_id"], Outcome("failed", 0, error="cancelled_before_spawn"))

    def spawn_one(self, provider, kinds):
        """Persist an irreversible launch intent. A missing return is never a retry signal."""
        kinds = self._kinds(kinds)
        if provider is None or self.authorize_spawn is None:
            raise RuntimeError("provider and a verified bounded spend policy are required")
        with self.store.transaction() as db:
            rows = db.execute(
                """SELECT b.job_id, j.kind FROM broker_jobs b JOIN jobs j ON j.id=b.job_id
                   WHERE b.state IN ('accepted','spawning') ORDER BY b.rowid""",
            ).fetchall()
            for candidate in rows:
                if candidate["kind"] not in kinds:
                    continue
                row = self._job_row(db, candidate["job_id"])
                if not self._dispatch_allowed(db, row):
                    if row["deadline"] <= self.store.clock() or self._state(db)["stopped"]:
                        self._finish(db, row["id"], Outcome("failed", 0, error="cancelled_before_spawn"))
                    continue
                job = Controller._job(row)
                if not self.authorize_spawn(job):
                    continue
                prepared = db.execute("SELECT launch_token FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
                launch_token = prepared["launch_token"] or f"launch-{uuid4().hex}"
                db.execute("UPDATE broker_jobs SET state='spawning',launch_token=? WHERE job_id=?",
                           (launch_token, job.key))
                break
            else:
                return None
        detached = None
        with self.store.transaction() as db:
            row = self._job_row(db, job.key)
            if not self._dispatch_allowed(db, row) or not self.authorize_spawn(job):
                # Nothing has crossed the provider boundary.
                if row["deadline"] <= self.store.clock() or self._state(db)["stopped"]:
                    current = db.execute("SELECT state FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
                    if current["state"] == "spawning":
                        self._finish(db, job.key, Outcome("failed", 0, error="cancelled_before_spawn"))
                return None
            usage_admission = job.kind == "research" and self.research_policy is not None
            if usage_admission and not self.can_plan_research(db, job):
                return None
            estimated = job.kind in GPU_KINDS and self.conservative_reserve is not None
            bound = (self.conservative_reserve(job) if estimated else
                     self.reserve_bound(job) if self.reserve_bound is not None else None)
            if not usage_admission and (estimated or self.reserve_bound is not None) and (
                type(bound) is not int or not 0 < bound <= job.reserve_cents
            ):
                return None
            authorized = self.store.clock()
            if job.deadline - authorized <= MAX_SEND_SECONDS:
                return None
            send_not_after = authorized + MAX_SEND_SECONDS
            marked = db.execute(
                """UPDATE broker_jobs SET state='sending',send_authorized_at=?,send_not_after=?
                   WHERE job_id=? AND state='spawning' AND launch_token=?""",
                (authorized, send_not_after, job.key, launch_token),
            )
            if marked.rowcount != 1:
                return None
            if usage_admission:
                db.execute("INSERT INTO broker_research_usage_admissions VALUES (?)", (job.key,))
            elif estimated:
                db.execute("INSERT INTO broker_conservative_reservations VALUES (?,?)", (job.key, bound))
            elif bound is not None:
                db.execute("INSERT INTO broker_cost_envelopes VALUES (?,?)", (job.key, bound))
            if job.kind in GPU_KINDS and getattr(provider, "detached", False) is True:
                detached = self._prepare_detached(db, job, row, launch_token)
        with self.store.transaction() as db:
            row = self._job_row(db, job.key)
            if not self._dispatch_allowed(db, row) or self.store.clock() >= send_not_after:
                self._finish(db, job.key, Outcome("failed", 0, error="cancelled_before_spawn"))
                return None
        # A stop after this check fences worker claim and retains any startup
        # reserve. The bound provider must hard-limit its SDK send to 5 seconds.
        if self.store.clock() >= send_not_after:
            with self.store.transaction() as db:
                self._finish(db, job.key, Outcome("failed", 0, error="cancelled_before_spawn"))
            return None
        try:
            receipt = (provider.spawn_detached(detached, send_not_after=send_not_after) if detached else
                       provider.spawn(job, launch_token, send_not_after=send_not_after))
            provider_id = identifier(receipt)
        except Exception as error:
            with self.store.transaction() as db:
                if isinstance(error, ProviderIntegrityError):
                    db.execute("UPDATE study SET stopped=COALESCE(stopped,'provider identity conflict')")
                elif job.kind in GPU_KINDS and isinstance(error, ProviderScopeError):
                    self.store.pause_provider(db, "modal", job.key, str(error), self.store.clock())
                db.execute("UPDATE broker_jobs SET state='ambiguous',error='provider receipt unknown',send_finished_at=? "
                           "WHERE job_id=? AND state='sending'", (self.store.clock(), job.key))
            return launch_token
        try:
            self.attach_provider_receipt(launch_token, provider_id)
        except ProviderIntegrityError as error:
            self._pause_modal(job, error)
        return launch_token

    def _prepare_detached(self, db, job, job_row, launch_token):
        validate_job(job)
        row = db.execute("SELECT * FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
        if row["invocation_id"] is not None:
            raise ValueError("detached invocation already authorized")
        proof = self.controller._accepted_checkpoint(db, job)[0] if job.payload["checkpoint"] else None
        seed_range = self._allocate_seeds(db, job, job_row) if job.kind == "train" else None
        grant = DetachedGrant(job, proof, row["receipt"], launch_token, row["fence"] + 1, seed_range)
        db.execute("INSERT INTO broker_detached VALUES (?,?)", (job.key, canonical(asdict(grant))))
        db.execute(
            """UPDATE broker_jobs SET invocation_id=?,fence=?,lease_until=?,safe_after=?,
               seed_begin=?,seed_end=? WHERE job_id=?""",
            (launch_token, grant.fence, job.deadline, job.deadline, *(seed_range or (None, None)), job.key),
        )
        return grant

    @staticmethod
    def _detached(db, job_key):
        row = db.execute("SELECT grant_json FROM broker_detached WHERE job_id=?", (job_key,)).fetchone()
        return DetachedGrant.from_json(row["grant_json"]) if row else None

    @staticmethod
    def _kinds(kinds):
        kinds = frozenset(kinds)
        if not kinds or not kinds <= GPU_KINDS | {"research", "sol"}:
            raise ValueError("unknown provider kinds")
        return kinds

    def attach_provider_receipt(self, launch_token, provider_id):
        identifier(launch_token)
        identifier(provider_id)
        with self.store.transaction() as db:
            row = db.execute("SELECT * FROM broker_jobs WHERE launch_token=?", (launch_token,)).fetchone()
            if not row or row["state"] not in PROVIDER_PENDING:
                raise ValueError("receipt does not belong to an unresolved provider attempt")
            owner = db.execute("SELECT job_id FROM broker_jobs WHERE provider_id=?", (provider_id,)).fetchone()
            if ((row["provider_id"] and row["provider_id"] != provider_id)
                    or (owner is not None and owner["job_id"] != row["job_id"])):
                raise ProviderIntegrityError("conflicting provider receipt")
            db.execute("UPDATE broker_jobs SET state='submitted',provider_id=?,error=NULL,"
                       "send_finished_at=COALESCE(send_finished_at,?) WHERE job_id=?",
                       (provider_id, self.store.clock(), row["job_id"]))

    def reconcile_one(self, provider, kinds):
        kinds = self._kinds(kinds)
        if provider is None:
            raise RuntimeError("provider binding is required")
        with self.store.transaction() as db:
            rows = db.execute(
                """SELECT b.*, j.kind FROM broker_jobs b JOIN jobs j ON j.id=b.job_id
                   WHERE b.state IN ('sending','ambiguous','submitted') OR (b.state='finished' AND b.cost_pending=1)
                   ORDER BY CASE WHEN b.state='finished' THEN 1 ELSE 0 END,b.last_checked,b.rowid""",
            ).fetchall()
            has_cost = getattr(provider, "tracks_late_cost", True) is not False
            row = next((item for item in rows if item["kind"] in kinds and (item["state"] != "finished" or has_cost)), None)
            if row is None:
                return False
            job = Controller._job(self._job_row(db, row["job_id"]))
            detached = self._detached(db, job.key)
            db.execute(
                "UPDATE broker_jobs SET last_checked=MAX(?, (SELECT MAX(last_checked)+0.001 FROM broker_jobs)) WHERE job_id=?",
                (self.store.clock(), job.key),
            )
        if not row["provider_id"]:
            # Optional provider lookup is positive-only. An absent receipt might be
            # a crash before send, a completed launch, or queued work not yet visible.
            find = getattr(provider, "find_pending", None)
            if find is not None:
                try:
                    provider_id = find(job, row["launch_token"], row["input_id"])
                    if provider_id is not None:
                        self.attach_provider_receipt(row["launch_token"], provider_id)
                except (ProviderScopeError, ProviderIntegrityError) as error:
                    self._pause_modal(job, error)
                except Exception:
                    pass
            return True
        try:
            outcome = provider.poll(row["provider_id"])
        except Exception as error:
            if isinstance(error, (ProviderScopeError, ProviderIntegrityError)):
                self._pause_modal(job, error)
            self._request_cancel(provider, row, job)
            return True
        if not isinstance(outcome, Outcome) or outcome.status not in TERMINAL:
            self._request_cancel(provider, row, job)
            return True
        if detached and row["state"] != "finished":
            if not self._collect_detached(provider, row, detached, outcome):
                return True
        actual = outcome.actual_cents
        cost = getattr(provider, "cost", None)
        if actual is None and cost is not None:
            try:
                actual = cost(row["provider_id"], job)
            except Exception:
                actual = None
        actual_known = type(actual) is int and 0 <= actual <= 10_000_000
        with self.store.transaction() as db:
            current = db.execute("SELECT * FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            if current["provider_id"] != row["provider_id"]:
                return True
            if current["state"] == "finished" and current["cost_pending"]:
                if actual_known:
                    self._settle_cost_hold(db, current, actual)
                return True
            if current["state"] not in PROVIDER_PENDING:
                return True
            if current["invocation_id"] and not current["worker_outcome"] and self.store.clock() < current["safe_after"]:
                return True
            if job.kind in GPU_KINDS:
                outcome = self._gpu_outcome(db, current, outcome)
            envelope = db.execute("SELECT upper_cents FROM broker_cost_envelopes WHERE job_id=?", (job.key,)).fetchone()
            estimated = self.conservative_reserved(db, job.key)
            if job.kind in {"research", "sol"} and outcome.error in MODEL_PROVIDER_ISSUES:
                self.store.pause_provider(db, "member_models", job.key, outcome.error, self.store.clock())
            if job.kind == "research" and self.research_usage_admitted(db, job.key):
                usage = outcome.usage_value_cents
                known_no_launch = (outcome.status == "failed" and type(actual) is int and actual == 0
                                   and outcome.error in {"cancelled_before_agent_start", "agent_preflight", "deadline"})
                if actual_known and actual > job.reserve_cents:
                    db.execute("UPDATE study SET stopped=COALESCE(stopped,'job exceeded its reserve')")
                    self._finish(db, job.key, Outcome("failed", actual, error="research_cost_over_reserve"))
                elif type(usage) is int and usage > job.reserve_cents:
                    self.store.pause_provider(db, "member_models", job.key, "usage_value_over_reserve", self.store.clock())
                    self._finish(db, job.key, Outcome("failed", error="research_usage_unverified",
                                                     usage_value_cents=usage))
                elif known_no_launch or (outcome.status == "succeeded" and actual is None
                                       and type(usage) is int and 0 < usage <= job.reserve_cents):
                    self._finish(db, job.key, self._bounded_outcome(outcome))
                else:
                    self._finish(db, job.key, Outcome("failed", error="research_usage_unverified"))
                return True
        materialize = getattr(provider, "materialize", None)
        if job.kind in GPU_KINDS and not detached and outcome.output is not None and materialize is not None:
            try:
                ready = materialize(job, outcome.output) is True
            except Exception:
                ready = False
            if not ready:
                self._note_pending(job.key, "verified artifact mirror pending")
                return True
        bound = envelope["upper_cents"] if envelope and not actual_known and actual is None else None
        if bound is None and not actual_known and actual is None and self.verify_cost_bound is not None:
            # Only trusted local policy can prove a hard maximum from verified
            # identity, resource/usage limits and rate evidence.
            try:
                bound = self.verify_cost_bound(job, outcome)
            except Exception:
                bound = None
        accepted_estimate = estimated if actual is None else None
        if not actual_known and accepted_estimate is None and (type(bound) is not int or not 0 < bound <= job.reserve_cents):
            self._note_pending(job.key, "provider terminal cost unknown")
            return True
        settled = Outcome(outcome.status, actual if actual_known else None, outcome.output, outcome.error,
                          bound, reserved_estimate_cents=accepted_estimate)
        with self.store.transaction() as db:
            latest = db.execute("SELECT state,provider_id,fence FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            if (latest["state"] in PROVIDER_PENDING
                    and (latest["provider_id"], latest["fence"]) == (current["provider_id"], current["fence"])):
                self._finish(db, job.key, self._bounded_outcome(settled))
        return True

    def _collect_detached(self, provider, row, grant, provider_outcome):
        """Accept bytes from the exact terminated one-shot grant, never a live callback."""
        job = grant.job
        if row["worker_outcome"]:
            return True
        try:
            worker = provider.collect_detached(grant, row["provider_id"], provider_outcome)
            if (not isinstance(worker, Outcome) or worker.status not in TERMINAL
                    or worker.actual_cents is not None or worker.cost_bound_cents is not None
                    or (worker.status == "succeeded" and (provider_outcome.status != "succeeded" or not worker.output))):
                raise ValueError("invalid detached terminal proof")
            metadata = None
            if worker.output:
                if provider.materialize(job, worker.output) is not True:
                    raise ValueError("artifact mirror pending")
                metadata = self.controller._artifact(job, worker.output)["manifest"]["metadata"]
                if (worker.status == "succeeded" and metadata["termination"] == "checkpoint"
                        or worker.status == "failed" and job.kind != "train"):
                    raise ValueError("detached result phase differs from checkpoint")
            with self.store.transaction() as db:
                current = db.execute("SELECT * FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
                if (current["state"] not in PROVIDER_PENDING
                        or (current["provider_id"], current["fence"]) != (row["provider_id"], grant.fence)):
                    return False
                if current["worker_outcome"]:
                    return True
                if metadata:
                    self._insert_generation(db, current, job, grant.fence, worker.output,
                                            metadata["decisions"], metadata["cursor"],
                                            metadata["termination"] == "checkpoint", metadata.get("seed_issued_until"))
                db.execute("UPDATE broker_jobs SET worker_outcome=? WHERE job_id=?",
                           (canonical(asdict(self._bounded_outcome(worker))), job.key))
            return True
        except Exception as error:
            if isinstance(error, (ProviderScopeError, ProviderIntegrityError)):
                self._pause_modal(job, error)
            self._note_pending(job.key, "detached terminal evidence or mirror pending")
            return False

    def archive_one(self, provider, retention_deadline):
        """Prune one finished provider's cloud copies after local hash verification."""
        if retention_deadline is None or not callable(getattr(provider, "prune", None)):
            return False
        with self.store.transaction() as db:
            protected = {
                ref["job_key"]: ref["generation"]
                for (raw,) in db.execute("SELECT checkpoint FROM experiments WHERE checkpoint IS NOT NULL")
                if (ref := json.loads(raw))
            }
            candidates = db.execute(
                """SELECT j.*,a.kept_generation FROM broker_detached d JOIN broker_jobs b ON b.job_id=d.job_id
                   JOIN jobs j ON j.id=d.job_id LEFT JOIN broker_cloud_archive a ON a.job_id=d.job_id
                   WHERE b.state='finished' AND b.provider_id IS NOT NULL AND j.state IN ('done','failed')
                   AND NOT EXISTS (
                       SELECT 1 FROM jobs active WHERE active.experiment_id=j.experiment_id
                       AND active.kind IN ('train','eval') AND active.state IN ('intent','running','uncertain')
                   ) ORDER BY b.rowid"""
            ).fetchall()
            row = next((item for item in candidates
                        if item["kept_generation"] != protected.get(item["id"], "")), None)
        if row is None:
            return False
        job = Controller._job(row)
        keep = protected.get(job.key)
        try:
            accepted = json.loads(row["outcome"]) if row["outcome"] else None
            if accepted and accepted.get("checkpoint"):
                ref = accepted["checkpoint"]
                self.controller._artifact(job, {key: ref[key] for key in ("generation", "manifest_hash")})
            elif keep:
                return False
            if provider.prune(job.key, keep) is not True:
                return False
        except Exception:
            return False
        with self.store.transaction() as db:
            db.execute("INSERT INTO broker_cloud_archive VALUES (?,?) ON CONFLICT(job_id) "
                       "DO UPDATE SET kept_generation=excluded.kept_generation", (job.key, keep or ""))
        return True

    def _pause_modal(self, job, error):
        if job.kind in GPU_KINDS or isinstance(error, ProviderIntegrityError):
            with self.store.transaction() as db:
                if isinstance(error, ProviderIntegrityError):
                    db.execute("UPDATE study SET stopped=COALESCE(stopped,'provider identity conflict')")
                else:
                    self.store.pause_provider(db, "modal", job.key, str(error), self.store.clock())

    def _note_pending(self, job_key, reason):
        with self.store.transaction() as db:
            db.execute("UPDATE broker_jobs SET error=? WHERE job_id=? AND state!='finished'", (reason, job_key))

    def _request_cancel(self, provider, row, job):
        cancel = getattr(provider, "cancel", None)
        if cancel is None:
            return
        with self.store.transaction() as db:
            if not self._state(db)["stopped"] and self.store.clock() < job.deadline:
                return
            updated = db.execute(
                "UPDATE broker_jobs SET cancel_attempts=cancel_attempts+1 "
                "WHERE job_id=? AND provider_id=? AND state='submitted' AND cancel_attempts<2",
                (job.key, row["provider_id"]),
            )
            if updated.rowcount != 1:
                return
        try:
            cancel(row["provider_id"])
        except Exception:
            pass  # keep the attempt reserved until terminal cost is known

    @staticmethod
    def _bounded_outcome(outcome):
        try:
            if outcome.error is not None:
                identifier(outcome.error)
            if len(canonical(asdict(outcome)).encode()) <= 64_000:
                return outcome
        except (ValueError, TypeError, RecursionError):
            pass
        return Outcome("failed", outcome.actual_cents, error="invalid_response", cost_bound_cents=outcome.cost_bound_cents)

    @staticmethod
    def _finish(db, key, outcome):
        db.execute("UPDATE broker_jobs SET state='finished',outcome=?,error=?,cost_pending=? WHERE job_id=?",
                   (canonical(asdict(outcome)), outcome.error, int(outcome.cost_bound_cents is not None), key))

    @staticmethod
    def authoritative_cost(db, job_key):
        row = db.execute("SELECT outcome FROM broker_jobs WHERE job_id=? AND state='finished'", (job_key,)).fetchone()
        return json.loads(row["outcome"])["actual_cents"] if row else None

    def _settle_cost_hold(self, db, row, actual):
        prior = Outcome(**json.loads(row["outcome"]))
        self._finish(db, row["job_id"], Outcome(prior.status, actual, prior.output, prior.error))
        if actual > prior.cost_bound_cents:
            db.execute("UPDATE study SET stopped=COALESCE(stopped,'billing exceeded verified cost bound')")
        hold = db.execute("SELECT * FROM cost_holds WHERE job_id=? AND settled_at IS NULL", (row["job_id"],)).fetchone()
        if hold is not None:
            db.execute("UPDATE cost_holds SET settled_at=?,actual=? WHERE job_id=?",
                       (self.store.clock(), actual, row["job_id"]))
            db.execute("UPDATE jobs SET actual=? WHERE id=? AND actual IS NULL", (actual, row["job_id"]))
            state = self._state(db)
            spent, _ = self.store.ledger(db)
            if spent >= state["execution_cap"]:
                db.execute("UPDATE study SET stopped=COALESCE(stopped,'current execution cap exhausted')")

    def _gpu_outcome(self, db, row, provider_outcome):
        if row["worker_outcome"]:
            worker = Outcome(**json.loads(row["worker_outcome"]))
            if worker.status == "failed" or provider_outcome.status == "succeeded":
                return Outcome(worker.status, provider_outcome.actual_cents, worker.output, worker.error)
        # An infrastructure failure can return a verified checkpoint reference,
        # but a provider success cannot stand in for the fenced worker result.
        partial = db.execute(
            "SELECT generation,manifest_hash FROM broker_generations WHERE job_id=? AND partial=1 "
            "ORDER BY decisions DESC LIMIT 1", (row["job_id"],),
        ).fetchone()
        return Outcome("failed", provider_outcome.actual_cents, dict(partial) if partial else None,
                       "reclaimed" if row["invocation_id"] else "worker_not_admitted")

    def _allocate_seeds(self, db, job, row):
        payload = job.payload
        experiment = row["experiment_id"]
        previous = db.execute(
            """SELECT b.*,j.outcome AS accepted_outcome,j.state AS controller_state
               FROM broker_jobs b JOIN jobs j ON j.id=b.job_id
               WHERE j.experiment_id=? AND b.seed_end IS NOT NULL ORDER BY b.rowid DESC LIMIT 1""",
            (experiment,),
        ).fetchone()
        begin = payload["start_cursor"]
        if previous:
            prior_end = previous["seed_end"]
            accepted = json.loads(previous["accepted_outcome"]) if previous["accepted_outcome"] else None
            finished = json.loads(previous["outcome"]) if previous["outcome"] else None
            if (previous["controller_state"] == "done" and finished and finished["status"] == "succeeded"
                    and accepted and accepted["checkpoint"] == payload["checkpoint"]
                    and accepted["manifest"]["metadata"]["cursor"] == begin):
                final = db.execute(
                    "SELECT cursor,issued_until FROM broker_generations "
                    "WHERE job_id=? AND partial=0 AND generation=? AND manifest_hash=?",
                    (previous["job_id"], payload["checkpoint"]["generation"], payload["checkpoint"]["manifest_hash"]),
                ).fetchone()
                issued_until = final["issued_until"] if final else None
                if (final and final["cursor"] == begin and issued_until is not None
                        and previous["seed_begin"] < begin <= issued_until <= prior_end
                        and accepted["manifest"]["metadata"].get("seed_issued_until") == issued_until):
                    db.execute("UPDATE broker_jobs SET reusable_tail=? WHERE job_id=?",
                               (issued_until, previous["job_id"]))
                    prior_end = issued_until
            begin = max(begin, prior_end)
        spec = payload["spec"]
        envs = spec["rollout_shape"]["envs"]
        remaining = payload["target_decisions"] - payload["start_decisions"]
        # At most one transition can end one episode. Include failed-start retries,
        # bounded prefetch and a cushion. The worker still enforces the exact cap.
        end = begin + 8 * (envs + remaining) + 16 * envs + 256
        if end >= 2**63:
            raise ValueError("seed reservation exceeds durable integer range")
        return begin, end

    def claim_worker(self, kind, job_token, input_id, invocation_id):
        identifier(job_token)
        identifier(input_id)
        identifier(invocation_id)
        if kind not in GPU_KINDS:
            raise ValueError("worker admission is reserved for GPU job kinds")
        with self.store.transaction() as db:
            row = db.execute("SELECT * FROM broker_jobs WHERE launch_token=?", (job_token,)).fetchone()
            if row is None or row["state"] not in PROVIDER_PENDING or self._detached(db, row["job_id"]):
                return None
            job_row = self._job_row(db, row["job_id"])
            job = Controller._job(job_row)
            if job.kind != kind or not self._dispatch_allowed(db, job_row):
                return None
            if row["invocation_id"]:
                if (row["invocation_id"], row["input_id"]) != (invocation_id, input_id):
                    return None  # restart can use a new paid attempt only after reconciliation
                if row["worker_outcome"] or self.store.clock() >= row["lease_until"]:
                    return None
                return self._claim(row, job)
            validate_job(job)
            proof = None
            if job.payload["checkpoint"]:
                proof, _ = self.controller._accepted_checkpoint(db, job)
            seed_range = self._allocate_seeds(db, job, job_row) if kind == "train" else (None, None)
            lease = min(job.deadline, self.store.clock() + self.grant_seconds)
            db.execute(
                """UPDATE broker_jobs SET input_id=?,invocation_id=?,fence=fence+1,lease_until=?,safe_after=?,
                   claim_proof=?,seed_begin=?,seed_end=? WHERE job_id=?""",
                (input_id, invocation_id, lease, lease + self.kill_grace_seconds,
                 canonical(asdict(proof)) if proof else None, *seed_range, job.key),
            )
            claimed = db.execute("SELECT * FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            return self._claim(claimed, job)

    @staticmethod
    def _claim(row, job):
        proof = AcceptedCheckpoint(**json.loads(row["claim_proof"])) if row["claim_proof"] else None
        seed_range = (row["seed_begin"], row["seed_end"]) if row["seed_begin"] is not None else None
        return Claim(job, proof, row["receipt"], row["input_id"], row["invocation_id"],
                     row["fence"], row["lease_until"], seed_range)

    def _live_claim(self, db, claim):
        row = db.execute("SELECT * FROM broker_jobs WHERE receipt=?", (claim.receipt,)).fetchone()
        if (row is None or row["state"] not in PROVIDER_PENDING or row["worker_outcome"]
                or self._detached(db, row["job_id"])
                or row["lease_until"] <= self.store.clock()
                or (row["job_id"], row["input_id"], row["invocation_id"], row["fence"]) != (
                    claim.job.key, claim.input_id, claim.invocation_id, claim.fence,
                )):
            raise ValueError("worker claim is stale or terminal")
        self._check_job(self._job_row(db, claim.job.key), claim.job)
        return row

    def renew(self, claim):
        with self.store.transaction() as db:
            try:
                self._live_claim(db, claim)
            except ValueError:
                return None
            job = self._job_row(db, claim.job.key)
            if not self._dispatch_allowed(db, job):
                return None
            lease = min(claim.job.deadline, self.store.clock() + self.grant_seconds)
            db.execute("UPDATE broker_jobs SET lease_until=?,safe_after=? WHERE job_id=?",
                       (lease, lease + self.kill_grace_seconds, job["id"]))
            return lease

    @staticmethod
    def _generation_ref(reference):
        if type(reference) is not dict or set(reference) != {"generation", "manifest_hash"}:
            raise ValueError("expected immutable generation reference")
        name = identifier(reference["generation"])
        hash_value = reference["manifest_hash"]
        if not isinstance(hash_value, str) or not DIGEST.fullmatch(hash_value):
            raise ValueError("invalid generation manifest hash")
        return name, hash_value

    def record_generation(self, claim, reference, *, decisions, cursor, partial=True, issued_until=None):
        with self.store.transaction() as db:
            row = self._live_claim(db, claim)
            self._insert_generation(db, row, claim.job, claim.fence, reference, decisions, cursor, partial, issued_until)

    def _insert_generation(self, db, row, job, fence, reference, decisions, cursor, partial, issued_until):
        name, hash_value = self._generation_ref(reference)
        integer(decisions, 1)
        integer(cursor)
        if type(partial) is not bool:
            raise ValueError("generation phase must be explicit")
        if job.kind == "train":
            if not job.payload["start_decisions"] < decisions <= job.payload["target_decisions"]:
                raise ValueError("generation decisions outside admitted range")
            if not row["seed_begin"] < cursor <= row["seed_end"]:
                raise ValueError("checkpoint cursor outside admitted seed range")
            if issued_until is not None and not cursor <= integer(issued_until) <= row["seed_end"]:
                raise ValueError("issued seed upper cursor outside admitted range")
        elif (partial or decisions != job.payload["target_decisions"]
              or cursor != job.payload["start_cursor"] or issued_until is not None):
            raise ValueError("invalid evaluation generation progress")
        old = db.execute("SELECT manifest_hash,decisions,cursor,issued_until,partial FROM broker_generations "
                         "WHERE job_id=? AND generation=?", (job.key, name)).fetchone()
        if old:
            if tuple(old) != (hash_value, decisions, cursor, issued_until, int(partial)):
                raise ValueError("immutable generation reference changed")
            return
        latest = db.execute("SELECT decisions,cursor,issued_until,partial FROM broker_generations WHERE job_id=? "
                            "ORDER BY decisions DESC, partial ASC LIMIT 1", (job.key,)).fetchone()
        if latest and (not latest["partial"] or decisions < latest["decisions"] or cursor < latest["cursor"]
                       or (decisions == latest["decisions"] and partial)
                       or (issued_until is not None and latest["issued_until"] is not None
                           and issued_until < latest["issued_until"])):
            raise ValueError("generation arrived behind the latest accepted progress")
        db.execute("INSERT INTO broker_generations VALUES (?,?,?,?,?,?,?,?)",
                   (job.key, fence, name, hash_value, decisions, cursor, issued_until, int(partial)))

    def complete(self, claim, outcome):
        if (not isinstance(outcome, Outcome) or outcome.status not in TERMINAL or outcome.actual_cents is not None
                or outcome.cost_bound_cents is not None or len(canonical(asdict(outcome))) > 16000):
            raise ValueError("worker completion must be bounded and leave billing to the provider")
        payload = canonical(asdict(outcome))
        with self.store.transaction() as db:
            row = db.execute("SELECT * FROM broker_jobs WHERE receipt=?", (claim.receipt,)).fetchone()
            if (row and (row["job_id"], row["input_id"], row["invocation_id"], row["fence"], row["worker_outcome"]) ==
                    (claim.job.key, claim.input_id, claim.invocation_id, claim.fence, payload)):
                return
            self._live_claim(db, claim)
            if outcome.status == "succeeded" or outcome.output is not None:
                name, hash_value = self._generation_ref(outcome.output)
                generation = db.execute("SELECT partial FROM broker_generations WHERE job_id=? AND generation=? "
                                        "AND manifest_hash=? AND fence=?",
                                        (claim.job.key, name, hash_value, claim.fence)).fetchone()
                if not generation or bool(generation["partial"]) != (outcome.status == "failed"):
                    raise ValueError("worker completion does not refer to a fenced generation")
            db.execute("UPDATE broker_jobs SET worker_outcome=? WHERE job_id=?", (payload, claim.job.key))


class AgentQueueProvider:
    """Local durable asynchronous facade for an injected AgentRunner.

    The timer accepts a job without blocking on inference. A separate process
    calls run_one and recovers evidence via AgentRunner.poll after interruption.
    """

    def __init__(self, broker, runner, *, cost_resolver=None):
        self.broker = broker
        self.runner = runner
        self.cost_resolver = cost_resolver
        with self.broker.store.transaction() as db:
            db.execute(
                """CREATE TABLE IF NOT EXISTS broker_agent_queue (
                   receipt TEXT PRIMARY KEY, job_id TEXT NOT NULL UNIQUE REFERENCES broker_jobs(job_id),
                   state TEXT NOT NULL DEFAULT 'queued', outcome TEXT, recovery_after REAL
                )""",
            )

    def spawn(self, job, launch_token, *, send_not_after=None):
        if send_not_after is None or self.broker.store.clock() >= send_not_after:
            raise ValueError("local agent queue send window elapsed")
        if job.kind not in {"research", "sol"}:
            raise ValueError("local agent queue accepts only research and engineering")
        with self.broker.store.transaction() as db:
            self.broker._check_job(self.broker._job_row(db, job.key), job)
            row = db.execute("SELECT launch_token,state FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            if not row or row["state"] not in PROVIDER_PENDING or row["launch_token"] != launch_token:
                raise ValueError("agent queue requires a broker-admitted launch")
            receipt = f"raven-agent-{digest(job.key)[:40]}"
            db.execute("INSERT OR IGNORE INTO broker_agent_queue (receipt,job_id) VALUES (?,?)", (receipt, job.key))
            return receipt

    def find_pending(self, job, launch_token, input_id):
        with self.broker.store.transaction() as db:
            row = db.execute(
                """SELECT a.receipt FROM broker_agent_queue a JOIN broker_jobs b ON b.job_id=a.job_id
                   WHERE a.job_id=? AND b.launch_token=?""", (job.key, launch_token),
            ).fetchone()
            return row["receipt"] if row else None

    def run_one(self):
        """Execute one queued job or recover an expired claim without relaunching it."""
        with self.broker.store.transaction() as db:
            now = self.broker.store.clock()
            agent = db.execute(
                """SELECT * FROM broker_agent_queue
                   WHERE state='queued' OR (state='claimed' AND recovery_after<=?)
                   ORDER BY CASE WHEN state='claimed' THEN 0 ELSE 1 END,rowid LIMIT 1""", (now,),
            ).fetchone()
            if not agent:
                return False
            job_row = self.broker._job_row(db, agent["job_id"])
            job = Controller._job(job_row)
            if agent["state"] == "claimed":
                recover = getattr(self.runner, "recover", None)
                if recover is None:
                    return False
                execute = recover
                recovery_after = now + 30
            else:
                if not self.broker._dispatch_allowed(db, job_row):
                    state = self.broker._state(db)
                    if job.deadline <= now or state["stopped"]:
                        self._record_agent(db, agent["receipt"], Outcome("failed", 0, error="cancelled_before_agent_start"))
                    return False
                if self.broker.authorize_spawn is None or not self.broker.authorize_spawn(job):
                    return False
                execute = self.runner.run
                recovery_after = job.deadline + self.broker.kill_grace_seconds
            db.execute("UPDATE broker_agent_queue SET state='claimed',recovery_after=? WHERE receipt=?",
                       (recovery_after, agent["receipt"]))
        outcome = execute(job)
        if isinstance(outcome, Outcome) and outcome.status in TERMINAL:
            with self.broker.store.transaction() as db:
                self._record_agent(db, agent["receipt"], self.broker._bounded_outcome(outcome))
        return True

    @staticmethod
    def _record_agent(db, receipt, outcome):
        db.execute("UPDATE broker_agent_queue SET state='finished',outcome=? WHERE receipt=? AND state!='finished'",
                   (canonical(asdict(outcome)), receipt))

    def poll(self, receipt):
        with self.broker.store.transaction() as db:
            row = db.execute("SELECT * FROM broker_agent_queue WHERE receipt=?", (identifier(receipt),)).fetchone()
            if not row:
                raise ValueError("unknown local agent receipt")
            job = Controller._job(self.broker._job_row(db, row["job_id"]))
        if row["state"] == "queued":
            return Outcome("running")
        outcome = Outcome(**json.loads(row["outcome"])) if row["outcome"] else self.runner.poll(job)
        diagnose = getattr(self.runner, "provider_issue", None)
        if outcome.status in TERMINAL and diagnose is not None:
            try:
                issue = diagnose(job)
            except (OSError, ValueError, KeyError, TypeError):
                issue = None
            if issue in MODEL_PROVIDER_ISSUES:
                return Outcome("failed", error=issue)
        if job.kind == "research" and getattr(self.runner, "scoped_research", False) and outcome.status == "succeeded":
            try:
                return replace(outcome, usage_value_cents=self.runner.standard_usage_value(job))
            except (OSError, ValueError, KeyError, TypeError):
                return Outcome("failed", error="research_usage_unverified")
        if outcome.status not in TERMINAL or outcome.actual_cents is not None or self.cost_resolver is None:
            return outcome
        # Authoritative billing is separate from token usage and terminal output.
        return Outcome(outcome.status, self.cost_resolver(job, outcome), outcome.output, outcome.error)


class RavenRuntime:
    """One bounded scheduler tick; supervisor services own long-running work."""

    def __init__(self, broker, *, modal_provider=None, agent_provider=None, archive_deadline=None, refresh_metering=None):
        self.broker = broker
        self.modal_provider = modal_provider
        self.archive_deadline = archive_deadline
        self.refresh_metering = refresh_metering
        self.bindings = []
        if modal_provider is not None:
            self.bindings.append((modal_provider, GPU_KINDS))
        if agent_provider is not None:
            self.bindings.append((agent_provider, {"research", "sol"}))

    def tick_once(self):
        if not self.bindings or self.broker.authorize_spawn is None:
            raise RuntimeError("runtime requires explicitly bound providers and a bounded spend policy")
        if self.refresh_metering is not None:
            self.refresh_metering()
        self.broker.expire_unspawned()
        for provider, kinds in self.bindings:
            self.broker.reconcile_one(provider, kinds)
        advanced = self.broker.controller.tick()
        self.broker.archive_one(self.modal_provider, self.archive_deadline)
        if advanced:
            for provider, kinds in self.bindings:
                self.broker.spawn_one(provider, kinds)
        return advanced
