import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock

from modal_research.adapters import Outcome, RemoteDisabled
from modal_research.contracts import verify_seed_isolation
from modal_research.controller import Controller
from modal_research.fixture import FixtureBackend, artifact, decision, spec
from modal_research.gateway import AgentBinding, GatewayBackend
from modal_research.manifests import publish, verify
from modal_research.modal_app import declare_app
from modal_research.store import Store


class LoopTest(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.now = 1000
        self.backend = FixtureBackend(self.root / "provider.db")
        self.restore()
        self.spec = spec(self.now)
        self.reference = {"experiment_id": self.spec["experiment_id"],
                          "config_hash": self.store.register(self.spec)}

    def restore(self):
        self.store = Store(self.root / "state.db", "study", "fixture", clock=lambda: self.now)
        self.controller = Controller(self.store, self.backend, self.root / "artifacts")

    def tick(self, count=1):
        for _ in range(count):
            self.assertTrue(self.controller.tick())

    def last(self, kind=None):
        remote_id, job = self.backend.jobs()[-1]
        if kind:
            self.assertEqual(job.kind, kind)
        return remote_id, job

    def ingest(self):
        self.store.ingest("initial", "result", {"synthetic": True})

    def start(self, budget=10, execution_cap=None):
        self.store.approve(budget, "test-authorization", execution_cap)
        self.ingest()
        self.tick(2)
        remote_id, job = self.last("research")
        self.backend.complete(remote_id, Outcome("succeeded", 20, decision(
            job, "NEW_EXPERIMENT", experiment_spec=self.reference, reserve_budget_usd=1,
        )))
        self.tick(2)

    def succeed(self, remote_id, job, steps, termination, cost=20):
        result = artifact(self.root / "artifacts", job, steps, termination)
        self.backend.complete(remote_id, Outcome("succeeded", cost, result))
        return result

    def test_result_dedup_lost_receipt_restart_continuation_and_eval(self):
        self.store.approve(10, "test-authorization")
        self.ingest()
        self.ingest()
        self.tick()
        submit = self.backend.submit

        def lost_receipt(job):
            submit(job)
            raise TimeoutError

        self.backend.submit = lost_receipt
        self.tick()
        self.restore()
        self.tick()
        remote_id, job = self.last("research")
        self.backend.complete(remote_id, Outcome("succeeded", 20, decision(
            job, "NEW_EXPERIMENT", experiment_spec=self.reference, reserve_budget_usd=1,
        )))
        self.backend.submit = submit
        self.tick(2)
        remote_id, first = self.last("train")
        checkpoint = self.succeed(remote_id, first, 5, "segment")
        self.restore()
        self.tick(2)
        remote_id, resumed = self.last("train")
        self.assertEqual(resumed.payload["spec"], first.payload["spec"])
        self.assertEqual(resumed.payload["checkpoint"]["manifest_hash"], checkpoint["manifest_hash"])
        self.assertEqual(resumed.payload["start_decisions"], 5)
        self.assertEqual(resumed.payload["start_cursor"], 1)
        self.assertEqual(resumed.payload["target_decisions"], 10)
        self.assertEqual([job.kind for _, job in self.backend.jobs()], ["research", "train", "train"])
        self.succeed(remote_id, resumed, 10, "milestone")
        self.tick(2)
        remote_id, evaluation = self.last("eval")
        self.succeed(remote_id, evaluation, 10, "completed")
        self.tick(2)
        remote_id, research = self.last("research")
        self.backend.complete(remote_id, Outcome("succeeded", 20, decision(research)))
        self.tick()
        self.ingest()
        self.restore()
        self.tick(3)
        self.assertEqual(len(self.backend.jobs()), 5)
        self.assertEqual(self.store.status()["state_version"], 2)
        self.assertEqual(self.store.status()["latest_decision"]["action"], "WAIT")

    def test_oom_and_reclaim_retry_keeps_deadline_cost_and_newest_checkpoint(self):
        self.start()
        remote_id, original = self.last("train")
        checkpoint = artifact(self.root / "artifacts", original, 2, "checkpoint")
        self.backend.complete(remote_id, Outcome("failed", 30, checkpoint, "reclaimed"))
        self.restore()
        self.tick(2)
        remote_id, retry = self.last("train")
        self.assertEqual(retry.attempt, 2)
        self.assertEqual(retry.logical_key, original.logical_key)
        self.assertEqual(retry.deadline, original.deadline)
        self.assertEqual(retry.payload["checkpoint"]["manifest_hash"], checkpoint["manifest_hash"])
        self.backend.complete(remote_id, Outcome("failed", 40, error="oom"))
        self.restore()
        self.tick(2)
        self.last("research")
        status = self.store.status()
        self.assertEqual(status["spent_usd"], 0.90)
        self.assertEqual(status["experiments"][0]["stage"], "failed")
        self.assertEqual(status["latest_event"]["kind"], "anomaly")

    def test_null_budget_stop_and_separate_execution_cap(self):
        self.ingest()
        self.tick(3)
        self.assertEqual(self.backend.jobs(), [])
        self.assertIsNone(self.store.status()["approved_budget"])
        self.store.approve(100, "test-authorization", execution_cap_usd="0.25")
        self.tick(2)
        remote_id, job = self.last("research")
        self.backend.complete(remote_id, Outcome("succeeded", 25, decision(
            job, "NEW_EXPERIMENT", experiment_spec=self.reference, reserve_budget_usd=1,
        )))
        self.restore()
        self.tick(2)
        status = self.store.status()
        self.assertEqual(len(self.backend.jobs()), 1)
        self.assertEqual(status["stop"], "current execution cap exhausted")
        self.assertEqual(status["approved_budget"], 100)
        self.assertEqual(status["spent_usd"], 0.25)
        self.assertEqual(status["execution_remaining_usd"], 0)

    def test_no_event_or_heartbeat_does_not_call_research(self):
        self.store.approve(10, "test-authorization")
        self.tick(3)
        self.assertEqual(self.backend.jobs(), [])
        with self.assertRaises(ValueError):
            self.store.ingest("progress", "heartbeat", {})

    def test_api_failures_retry_once_then_pause(self):
        self.store.approve(10, "test-authorization")
        self.ingest()
        self.tick(2)
        remote_id, original = self.last("research")
        self.backend.complete(remote_id, Outcome("failed", 10, error="api"))
        self.tick(2)
        remote_id, retry = self.last("research")
        self.assertEqual(retry.deadline, original.deadline)
        self.assertEqual(retry.attempt, 2)
        self.backend.complete(remote_id, Outcome("succeeded", 5, '{"action":"RUN_SHELL","cmd":"arbitrary"}'))
        self.restore()
        self.tick(3)
        self.assertEqual(len(self.backend.jobs()), 2)
        status = self.store.status()
        self.assertIsNotNone(status["analysis_pause"])
        self.assertEqual(status["spent_usd"], 0.15)
        self.assertEqual(status["state_version"], 0)

    def test_missing_and_corrupted_artifacts_never_advance_progress(self):
        self.start()
        remote_id, job = self.last("train")
        self.backend.complete(remote_id, Outcome("succeeded", 20, {
            "generation": "missing", "manifest_hash": "0" * 64,
        }))
        self.tick(2)
        remote_id, retry = self.last("train")
        result = artifact(self.root / "artifacts", retry, 2, "segment")
        (self.root / "artifacts" / retry.key / result["generation"] / "rng.json").write_text("bad")
        self.backend.complete(remote_id, Outcome("succeeded", 20, result))
        self.tick()
        status = self.store.status()
        self.assertEqual(status["experiments"][0]["decisions"], 0)
        self.assertEqual(status["experiments"][0]["stage"], "failed")

    def test_committed_manifest_is_immutable(self):
        self.start()
        _, job = self.last("train")
        result = artifact(self.root / "artifacts", job, 2, "segment")
        root = self.root / "artifacts" / job.key / result["generation"]
        identity = Controller._identity(job)
        document = verify(root, result["manifest_hash"], identity)
        with self.assertRaises(ValueError):
            publish(root, identity, {**document["metadata"], "decisions": 3},
                    {role: data["name"] for role, data in document["files"].items()})
        self.assertEqual(verify(root, result["manifest_hash"], identity)["metadata"]["decisions"], 2)

    def test_result_can_trigger_bounded_sol_task(self):
        self.store.approve(10, "test-authorization")
        self.ingest()
        self.tick(2)
        remote_id, job = self.last("research")
        self.backend.complete(remote_id, Outcome("succeeded", 20, decision(
            job, "REQUEST_SOL", reserve_budget_usd="0.50",
            engineering_task={"task_type": "diagnose", "description": "Inspect fixture"},
        )))
        self.tick(2)
        remote_id, sol = self.last("sol")
        self.backend.complete(remote_id, Outcome("succeeded", 30, {
            "task_id": sol.payload["task_id"], "summary": "Fixture diagnosis", "artifact_refs": ["fixture"],
        }))
        self.tick(2)
        self.last("research")
        self.assertEqual(self.store.status()["latest_event"]["kind"], "sol_result")

    def test_lease_fencing_and_remote_disabled(self):
        self.assertTrue(self.store.acquire("other", 1))
        self.assertFalse(self.controller.tick())
        self.now += 2
        self.tick()
        with self.store.transaction() as db:
            with self.assertRaises(TimeoutError):
                self.store.fence(db, "other")
        with self.assertRaises(RuntimeError):
            RemoteDisabled()
        with self.assertRaises(ValueError):
            Store(self.root / "remote.db", "study", "modal")

    def test_reclaimed_milestone_checkpoint_routes_to_evaluation(self):
        self.start()
        remote_id, original = self.last("train")
        checkpoint = artifact(self.root / "artifacts", original, 10, "checkpoint")
        self.backend.complete(remote_id, Outcome("failed", 30, checkpoint, "reclaimed"))
        self.tick(2)
        _, evaluation = self.last("eval")
        self.assertEqual(evaluation.payload["checkpoint"]["manifest_hash"], checkpoint["manifest_hash"])
        self.assertEqual([job.kind for _, job in self.backend.jobs()].count("train"), 1)

    def test_modal_gate_and_remote_agent_scope(self):
        modal = Mock()
        runtime = Mock()
        runtime.verified_capabilities.return_value = {"external_transactional_store"}
        with self.assertRaises(RuntimeError):
            declare_app(modal, runtime=runtime, cpu_image=None, worker_image=None,
                        volume_v2=None, secrets=(), deployment_ref="test-deployment")
        modal.App.assert_not_called()
        text_only = AgentBinding("demo", "verified-fixture", 1024, "research-decision")
        with self.assertRaises(ValueError):
            GatewayBackend("https://example.test", "fixture", text_only, text_only, "test-provider")
        tools = AgentBinding("demo-sol", "verified-fixture", 1024, "sol-isolated-files-and-tests")
        gateway = GatewayBackend("https://example.test", "fixture", text_only, tools, "test-provider")
        self.assertFalse(gateway.fixture)
        self.assertTrue(gateway.tool_enabled)

    def test_seed_overlap_detects_environment_alias(self):
        verify_seed_isolation({"train": [("A", "e1")], "dev": [("B", "e2")], "blind": [("C", "e3")]})
        with self.assertRaises(ValueError):
            verify_seed_isolation({"train": [("A", "e1")], "dev": [("B", "e1")]})


if __name__ == "__main__":
    unittest.main()
