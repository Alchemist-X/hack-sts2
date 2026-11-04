from pathlib import Path
import tempfile
import unittest

from modal_research.adapters import Outcome
from modal_research.broker import RavenBroker
from modal_research.fixture import artifact, decision, spec
from modal_research.store import Store

ALL_KINDS = {"research", "sol", "train", "eval"}


class FakeProvider:
    def __init__(self):
        self.jobs = {}
        self.calls = []
        self.lost = False
        self.recoverable = True

    def spawn(self, job, token, *, send_not_after):
        receipt = f"provider-{len(self.calls)}"
        self.calls.append((job, token, receipt))
        self.jobs[receipt] = Outcome("running")
        if self.lost:
            raise TimeoutError("receipt lost after external acceptance")
        return receipt

    def find_pending(self, job, token, input_id):
        if self.recoverable:
            return next((receipt for _, key, receipt in self.calls if key == token), None)

    def poll(self, receipt):
        return self.jobs[receipt]


class BrokerTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.now = 1000
        self.provider = FakeProvider()
        self.restart()
        self.store.approve(10, "test-approval")
        self.store.record_platform(gross_limit=42.5, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                   reference="test-snapshot")
        self.frozen = spec(self.now)
        self.frozen["eval_protocol"] = "silent-a10-smoke-v1"
        self.frozen["resource_request"]["segment_seconds"] = 180
        self.reference = {"experiment_id": self.frozen["experiment_id"],
                          "config_hash": self.store.register(self.frozen)}

    def restart(self):
        self.store = Store(self.root / "state.db", "broker-study", clock=lambda: self.now)
        self.broker = RavenBroker(self.store, self.root / "artifacts", authorize_spawn=lambda job: True,
                                  grant_seconds=10, kill_grace_seconds=5)
        self.controller = self.broker.controller

    def ticks(self, count=1):
        for _ in range(count):
            self.assertTrue(self.controller.tick())

    def broker_spawn(self):
        return self.broker.spawn_one(self.provider, ALL_KINDS)

    def reconcile(self):
        return self.broker.reconcile_one(self.provider, ALL_KINDS)

    def accepted_research(self):
        self.store.ingest("seed-evidence", "bootstrap", {"source": "test"})
        self.ticks(2)

    def train_ready(self):
        self.accepted_research()
        self.broker_spawn()
        research, _, receipt = self.provider.calls[-1]
        self.provider.jobs[receipt] = Outcome("succeeded", 20, decision(
            research, "NEW_EXPERIMENT", experiment_spec=self.reference, reserve_budget_usd=1,
        ))
        self.reconcile()
        self.ticks(2)

    def publish(self, claim, decisions, termination, name):
        reference = artifact(self.broker.artifacts, claim.job, decisions, termination, name)
        cursor = claim.job.payload["start_cursor"] + 1
        self.broker.record_generation(claim, reference, decisions=decisions,
                                      cursor=cursor, issued_until=cursor, partial=termination == "checkpoint")
        return reference

    def test_broker_receipt_is_durable_and_fenced(self):
        self.accepted_research()
        remote_id = self.store.status()["active_jobs"][0]["remote_id"]
        with self.store.transaction() as db:
            job = self.controller._job(db.execute("SELECT * FROM jobs").fetchone())
        self.restart()
        self.assertEqual(self.broker.lookup(job), remote_id)
        with self.assertRaises(TimeoutError):
            self.broker.submit_admitted(job, {"study_id": "broker-study", "lease_owner": "old", "lease_until": 0})
        self.assertEqual(len(self.provider.calls), 0)

    def test_lost_spawn_receipt_is_never_respawned(self):
        self.accepted_research()
        self.provider.lost = True
        self.provider.recoverable = False
        token = self.broker_spawn()
        self.assertIsNotNone(token)
        self.restart()
        for _ in range(3):
            self.assertIsNone(self.broker_spawn())
            self.reconcile()
        self.assertEqual(len(self.provider.calls), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 0.25)
        self.provider.recoverable = True
        self.reconcile()
        self.provider.jobs[self.provider.calls[0][2]] = Outcome("failed", 10, error="api")
        self.reconcile()
        self.ticks()
        self.assertEqual(self.store.status()["spent_usd"], 0.10)
        self.assertEqual(len(self.provider.calls), 1)

    def test_worker_fences_and_waits_through_kill_grace(self):
        self.train_ready()
        token = self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        claim = self.broker.claim_worker("train", token, "input-1", "invocation-1")
        self.assertEqual(claim.seed_range[0], 0)
        self.assertEqual(claim.lease_until, 1010)
        self.assertEqual(self.broker.claim_worker("train", token, "input-1", "invocation-1"), claim)
        self.assertIsNone(self.broker.claim_worker("train", token, "input-1", "duplicate"))
        latest = self.publish(claim, 5, "checkpoint", "gen-5")
        with self.assertRaises(ValueError):
            self.publish(claim, 4, "checkpoint", "gen-4")
        self.provider.jobs[receipt] = Outcome("failed", 40, error="reclaimed")
        self.now = 1012
        self.restart()
        self.assertIsNone(self.broker.renew(claim))
        with self.assertRaises(ValueError):
            self.broker.complete(claim, Outcome("failed"))
        self.assertIsNone(self.broker.claim_worker("train", token, "input-1", "restart"))
        self.reconcile()
        broker_receipt = self.broker.lookup(job)
        self.assertEqual(self.broker.poll(broker_receipt).status, "running")
        self.now = 1015
        self.reconcile()
        outcome = self.broker.poll(broker_receipt)
        self.assertEqual(outcome, Outcome("failed", 40, latest, "reclaimed"))
        self.ticks(2)
        next_token = self.broker_spawn()
        retry = self.broker.claim_worker("train", next_token, "input-2", "retry-invocation")
        self.assertEqual(retry.job.attempt, 2)
        self.assertEqual(retry.job.payload["start_cursor"], 1)
        self.assertEqual(retry.seed_range[0], claim.seed_range[1])
        self.assertEqual(retry.accepted_checkpoint.reference["manifest_hash"], latest["manifest_hash"])
        self.assertIsNone(self.broker.claim_worker("train", token, "input-1", "late-restart"))

    def test_normal_verified_final_cursor_releases_unissued_seed_tail(self):
        self.train_ready()
        token = self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        claim = self.broker.claim_worker("train", token, "input-1", "invocation-1")
        ref = self.publish(claim, 5, "segment", "final")
        self.broker.complete(claim, Outcome("succeeded", output=ref))
        self.broker.complete(claim, Outcome("succeeded", output=ref))
        self.provider.jobs[receipt] = Outcome("succeeded", 20)
        self.reconcile()
        self.ticks(2)
        token = self.broker_spawn()
        next_claim = self.broker.claim_worker("train", token, "input-2", "invocation-2")
        self.assertEqual(next_claim.seed_range[0], 1)
        with self.store.transaction() as db:
            row = db.execute("SELECT reusable_tail FROM broker_jobs WHERE job_id=?", (job.key,)).fetchone()
            self.assertEqual(row["reusable_tail"], 1)

    def test_worker_rejection_or_unsettled_provider_terminal_keeps_reserve(self):
        self.train_ready()
        token = self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        self.store.stop("operator")
        self.assertIsNone(self.broker.claim_worker("train", token, "input", "invocation"))
        self.provider.jobs[receipt] = Outcome("succeeded", output={"status": "not_admitted"})
        self.reconcile()
        self.ticks()
        self.assertEqual(self.store.status()["reserved_usd"], 1.0)
        self.assertEqual(self.broker.poll(self.broker.lookup(job)).status, "running")
        self.provider.jobs[receipt] = Outcome("succeeded", 11, output={"status": "not_admitted"})
        self.reconcile()
        self.ticks()
        self.assertEqual(self.store.status()["spent_usd"], 0.31)
        self.assertEqual(self.store.status()["reserved_usd"], 0.0)

    def test_no_provider_launch_without_bounded_spend_binding(self):
        self.accepted_research()
        unbound = RavenBroker(self.store, self.root / "artifacts")
        with self.assertRaises(RuntimeError):
            unbound.spawn_one(self.provider, ALL_KINDS)
        self.assertEqual(len(self.provider.calls), 0)

    def test_raven_runtime_drives_local_agent_receipt_and_persisted_outcome(self):
        from modal_research.broker import AgentQueueProvider, RavenRuntime

        class Runner:
            calls = 0

            def run(inner, job):
                inner.calls += 1
                return Outcome("succeeded", output=decision(job))

            def poll(inner, job):
                raise AssertionError("completed agent result should come from the durable queue")

        runner = Runner()
        self.store.ingest("agent-evidence", "bootstrap", {"source": "fake-agent"})
        queue = AgentQueueProvider(self.broker, runner)
        runtime = RavenRuntime(self.broker, agent_provider=queue)
        runtime.tick_once()
        runtime.tick_once()
        self.assertEqual(runner.calls, 0)
        self.assertTrue(queue.run_one())
        self.assertFalse(queue.run_one())
        runtime.tick_once()  # terminal but unsettled cost holds the reserve
        self.assertEqual(self.store.status()["reserved_usd"], .25)
        self.restart()
        queue = AgentQueueProvider(self.broker, runner, cost_resolver=lambda _job, _outcome: 15)
        runtime = RavenRuntime(self.broker, agent_provider=queue)
        runtime.tick_once()
        self.assertEqual(runner.calls, 1)
        self.assertEqual(self.store.status()["spent_usd"], .15)
        self.assertEqual(self.store.status()["latest_decision"]["action"], "WAIT")
        self.assertEqual(self.store.status()["broker_in_flight"], [])

    def test_stop_between_prepare_and_send_cancels_without_provider_side_effect(self):
        from contextlib import contextmanager

        self.train_ready()
        transaction = self.store.transaction
        stopped = False

        @contextmanager
        def inject():
            nonlocal stopped
            with transaction() as db:
                yield db
                prepared = db.execute("SELECT 1 FROM broker_jobs WHERE state='spawning'").fetchone()
            if prepared and not stopped:
                stopped = True
                self.store.stop("operator test")

        self.store.transaction = inject
        self.assertIsNone(self.broker_spawn())
        self.assertTrue(stopped)
        self.assertEqual(len(self.provider.calls), 1)  # completed research only
        self.ticks()
        self.assertEqual(self.store.status()["reserved_usd"], 0.0)

    def test_no_stale_send_after_authorization_window_and_no_worker_after_late_stop(self):
        from contextlib import contextmanager

        self.train_ready()
        transaction = self.store.transaction
        expired = False

        @contextmanager
        def inject():
            nonlocal expired
            with transaction() as db:
                yield db
                armed = db.execute("SELECT 1 FROM broker_jobs WHERE state='sending'").fetchone()
            if armed and not expired:
                expired = True
                self.now += 6

        self.store.transaction = inject
        self.assertIsNone(self.broker_spawn())
        self.assertTrue(expired)
        self.assertEqual(len(self.provider.calls), 1)

    def test_post_send_stop_cancels_but_does_not_release_unknown_startup(self):
        self.train_ready()
        self.provider.cancelled = []
        self.provider.cancel = self.provider.cancelled.append
        token = self.broker_spawn()
        _, _, receipt = self.provider.calls[-1]
        self.store.stop("after send")
        self.assertIsNone(self.broker.claim_worker("train", token, "input", "invocation"))
        for _ in range(3):
            self.reconcile()
        self.assertEqual(self.provider.cancelled, [receipt, receipt])
        self.assertEqual(self.store.status()["reserved_usd"], 1.0)
        self.assertEqual(self.store.status()["broker_in_flight"][0]["cancel_attempts"], 2)

    def test_raven_agent_queue_recovers_claimed_job_without_relaunch(self):
        from modal_research.broker import AgentQueueProvider, RavenRuntime

        class Runner:
            launched = 0
            recovered = 0

            def run(inner, job):
                inner.launched += 1
                raise SystemExit("simulated process crash after durable queue claim")

            def recover(inner, job):
                inner.recovered += 1
                return Outcome("failed", error="agent_interrupted")

            def poll(inner, job):
                return Outcome("running")

        runner = Runner()
        self.store.ingest("agent-crash", "bootstrap", {"source": "fake-agent"})
        queue = AgentQueueProvider(self.broker, runner)
        runtime = RavenRuntime(self.broker, agent_provider=queue)
        runtime.tick_once()
        runtime.tick_once()
        with self.assertRaises(SystemExit):
            queue.run_one()
        self.restart()
        queue = AgentQueueProvider(self.broker, runner)
        self.assertFalse(queue.run_one())
        self.now = 1605
        self.assertTrue(queue.run_one())
        self.assertEqual((runner.launched, runner.recovered), (1, 1))
        self.assertEqual(self.store.status()["reserved_usd"], .25)

    def test_runtime_evidence_to_agent_to_train_eval_and_back(self):
        from modal_research.broker import AgentQueueProvider, RavenRuntime

        class Runner:
            calls = []

            def run(inner, job):
                inner.calls.append(job.kind)
                if len(inner.calls) == 1:
                    raw = decision(job, "NEW_EXPERIMENT", experiment_spec=self.reference, reserve_budget_usd=1)
                else:
                    self.assertEqual(job.payload["event"]["kind"], "result")
                    raw = decision(job)
                return Outcome("succeeded", 15, raw)

            def poll(inner, job):
                raise AssertionError("agent terminal result must already be durable")

        runner = Runner()
        queue = AgentQueueProvider(self.broker, runner)
        runtime = RavenRuntime(self.broker, modal_provider=self.provider, agent_provider=queue)
        self.store.ingest("start-study", "bootstrap", {"source": "fixture"})
        runtime.tick_once()
        runtime.tick_once()
        self.assertTrue(queue.run_one())
        runtime.tick_once()
        runtime.tick_once()
        train, token, provider_receipt = self.provider.calls[-1]
        self.assertEqual(train.kind, "train")
        claim = self.broker.claim_worker("train", token, "train-input", "train-run")
        train_ref = self.publish(claim, 10, "milestone", "final-train")
        self.broker.complete(claim, Outcome("succeeded", output=train_ref))
        self.provider.jobs[provider_receipt] = Outcome("succeeded", 20)
        runtime.tick_once()
        runtime.tick_once()
        evaluation, token, provider_receipt = self.provider.calls[-1]
        self.assertEqual(evaluation.kind, "eval")
        claim = self.broker.claim_worker("eval", token, "eval-input", "eval-run")
        self.assertEqual(claim.accepted_checkpoint.reference["manifest_hash"], train_ref["manifest_hash"])
        evaluation_ref = artifact(self.broker.artifacts, evaluation, 10, "completed")
        self.broker.record_generation(claim, evaluation_ref, decisions=10, cursor=1, partial=False)
        self.broker.complete(claim, Outcome("succeeded", output=evaluation_ref))
        self.provider.jobs[provider_receipt] = Outcome("succeeded", 10)
        runtime.tick_once()
        runtime.tick_once()
        self.assertTrue(queue.run_one())
        for _ in range(3):
            runtime.tick_once()
        self.assertEqual(runner.calls, ["research", "research"])
        self.assertEqual(self.store.status()["latest_decision"]["action"], "WAIT")
        self.assertEqual(self.store.status()["spent_usd"], .60)
        self.assertEqual(self.store.status()["active_jobs"], [])

    def test_proven_cost_bound_allows_result_without_releasing_reserve(self):
        self.broker.verify_cost_bound = lambda job, outcome: 20
        self.accepted_research()
        self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        self.provider.jobs[receipt] = Outcome("succeeded", output=decision(job))
        self.reconcile()
        self.ticks()
        status = self.store.status()
        self.assertEqual(status["latest_decision"]["action"], "WAIT")
        self.assertEqual(status["active_jobs"], [])
        self.assertEqual(status["spent_usd"], 0)
        self.assertEqual(status["conservative_budget_held_usd"], .25)
        self.assertEqual(status["reserved_usd"], .25)
        self.assertFalse(status["spend_complete"])
        self.restart()
        self.provider.jobs[receipt] = Outcome("succeeded", 18)
        self.reconcile()
        status = self.store.status()
        self.assertEqual(status["spent_usd"], .18)
        self.assertEqual(status["reserved_usd"], 0)
        self.assertEqual(status["state_version"], 1)
        self.assertTrue(status["spend_complete"])

    def test_provider_cannot_self_certify_unknown_cost(self):
        self.accepted_research()
        self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        self.provider.jobs[receipt] = Outcome("succeeded", output=decision(job), cost_bound_cents=1)
        self.reconcile()
        self.ticks()
        status = self.store.status()
        self.assertIsNone(status["latest_decision"])
        self.assertEqual(status["reserved_usd"], .25)
        self.assertEqual(status["conservative_budget_held_usd"], 0)

    def test_late_bill_between_poll_and_decision_transaction_is_not_lost(self):
        self.broker.verify_cost_bound = lambda job, outcome: 20
        self.accepted_research()
        self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        self.provider.jobs[receipt] = Outcome("succeeded", output=decision(job))
        self.reconcile()
        poll = self.broker.poll

        def resolve_while_polling(broker_receipt):
            provisional = poll(broker_receipt)
            self.provider.jobs[receipt] = Outcome("succeeded", 21)
            self.reconcile()
            return provisional

        self.broker.poll = resolve_while_polling
        self.ticks()
        status = self.store.status()
        self.assertEqual(status["spent_usd"], .21)
        self.assertEqual(status["reserved_usd"], 0)
        self.assertEqual(status["stop"], "billing exceeded verified cost bound")
        self.assertEqual(status["latest_decision"]["action"], "WAIT")

    def test_gpu_terminal_requires_local_artifact_mirror_and_bound_receipt_cost(self):
        self.train_ready()
        token = self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        claim = self.broker.claim_worker("train", token, "input", "invocation")
        ref = self.publish(claim, 5, "segment", "final")
        self.broker.complete(claim, Outcome("succeeded", output=ref))
        self.provider.jobs[receipt] = Outcome("succeeded")
        mirrors = []
        self.provider.materialize = lambda received_job, received_ref: (
            mirrors.append((received_job, received_ref)) or len(mirrors) == 2
        )
        self.provider.cost = lambda received_id, received_job: 30 if (received_id, received_job) == (receipt, job) else None
        self.reconcile()
        self.ticks()
        self.assertEqual(self.store.status()["broker_in_flight"][0]["error"], "verified artifact mirror pending")
        self.assertEqual(self.store.status()["reserved_usd"], 1.0)
        self.reconcile()
        self.ticks()
        self.assertEqual(mirrors, [(job, ref), (job, ref)])
        self.assertEqual(self.store.status()["experiments"][0]["decisions"], 5)
        self.assertEqual(self.store.status()["spent_usd"], .50)

    def test_normal_checkpoint_never_reuses_already_prefetched_seeds(self):
        self.train_ready()
        token = self.broker_spawn()
        job, _, receipt = self.provider.calls[-1]
        claim = self.broker.claim_worker("train", token, "input-1", "invocation-1")
        reference = artifact(self.broker.artifacts, claim.job, 5, "segment", "ahead", issued_until=4)
        self.broker.record_generation(claim, reference, decisions=5, cursor=1, issued_until=4, partial=False)
        self.broker.complete(claim, Outcome("succeeded", output=reference))
        self.provider.jobs[receipt] = Outcome("succeeded", 20)
        self.reconcile()
        self.ticks(2)
        next_claim = self.broker.claim_worker("train", self.broker_spawn(), "input-2", "invocation-2")
        self.assertEqual(next_claim.job.payload["start_cursor"], 1)
        self.assertEqual(next_claim.seed_range[0], 4)
