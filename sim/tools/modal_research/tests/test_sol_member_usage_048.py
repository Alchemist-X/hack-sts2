import json
import tempfile
import unittest
from pathlib import Path

from modal_research.adapters import Outcome
from modal_research.agent_runner import SolTemplate
from modal_research.broker import RavenBroker, RavenRuntime
from modal_research.contracts import digest
from modal_research.raven_runtime import SolUsagePolicy
from modal_research.store import Store


class Provider:
    def __init__(self):
        self.jobs = []
        self.outcome = Outcome("running")

    def spawn(self, job, launch_token, *, send_not_after):
        self.jobs.append(job)
        return "one-sol-receipt"

    def poll(self, receipt):
        return self.outcome


class SolMemberUsage048Test(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.now = 1000
        self.store = Store(root / "operation.sqlite3", "member-sol", clock=lambda: self.now)
        self.store.approve(10, "approval")
        self.policy = SolUsagePolicy("sol-once", "member-proof", "metric-template", 200, 900,
                                     100_000, 20_000)
        self.template = SolTemplate("metric-template", "diagnose", ("one-slot",), "a" * 40,
                                    ("fix.patch", "test-report.json"), ("/usr/bin/python3", "-c", "assert True"),
                                    200, 900, 3000, 16000, "sol-role")
        self.provider = Provider()
        self.broker = RavenBroker(self.store, root / "artifacts",
                                  authorize_spawn=lambda job: self.policy.allows(job, self.now),
                                  sol_usage_policy=self.policy, tool_enabled=True,
                                  billing_sources={"sol": ("non_modal", "sol-role")})
        self.broker.sol_templates = {"diagnose": self.template}
        self.runtime = RavenRuntime(self.broker, agent_provider=self.provider)

    def start(self):
        payload = {"study_id": "member-sol", "task_id": "metric-task", "task_type": "diagnose",
                   "template_id": "metric-template", "description": "Check metric naming"}
        with self.store.transaction() as db:
            self.store.bind_sol_task(db, "metric-task", payload, self.template)
            self.broker.controller._create_job(db, "sol-task", 1, "sol", payload, 200, 1900, None)
        self.runtime.tick_once()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 1)
        return self.provider.jobs[0]

    def result(self, job):
        task = self.store.sol_task("metric-task")
        manifest = {name: "sha256:" + character * 64 for name, character in
                    zip(task.artifact_paths, ("a", "b"))}
        return {"task_id": "metric-task", "summary": "Checked metric definitions",
                "artifact_refs": list(manifest.values()), "artifact_manifest": manifest,
                "test_report": {"argv_hash": digest(task.test_argv), "exit_code": 0},
                "base_commit": task.base_commit, "payload_hash": task.payload_hash}

    def test_terminal_usage_keeps_unknown_cash_and_emits_result(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=self.result(job), usage_tokens={
            "input_tokens": 12000, "cached_input_tokens": 2000, "output_tokens": 1800})
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(state["spent_usd"], 0)
        self.assertEqual(state["sol_usage_cash_unknown"][0]["terminal_usage_tokens"]["input_tokens"], 12000)
        self.assertEqual(state["jobs_with_verified_cost_bounds"], [])
        with self.store.transaction() as db:
            self.assertIsNone(db.execute("SELECT actual FROM jobs WHERE kind='sol'").fetchone()[0])
            self.assertEqual(db.execute("SELECT COUNT(*) FROM broker_cost_envelopes").fetchone()[0], 0)
            self.assertEqual(db.execute("SELECT COUNT(*) FROM cost_holds").fetchone()[0], 0)
            event = db.execute("SELECT payload FROM events WHERE kind='sol_result'").fetchone()
            self.assertEqual(json.loads(event[0])["task_id"], "metric-task")
            self.assertEqual(db.execute("SELECT COUNT(*) FROM broker_sol_usage_admissions").fetchone()[0], 1)
        for _ in range(3):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 1)

    def test_missing_usage_pauses_member_and_preserves_full_hold(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=self.result(job))
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(state["provider_pauses"][0]["provider"], "member_models")
        self.assertEqual(state["provider_pauses"][0]["reason"], "sol_usage_unverified")
        self.assertEqual(state["sol_usage_cash_unknown"][0]["terminal_usage_tokens"], None)
        with self.store.transaction() as db:
            self.assertIsNone(db.execute("SELECT 1 FROM events WHERE kind='sol_result'").fetchone())
            self.assertEqual(db.execute("SELECT COUNT(*) FROM jobs WHERE kind='sol'").fetchone()[0], 1)

    def test_over_limit_usage_pauses_and_cannot_make_second_task(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=self.result(job), usage_tokens={
            "input_tokens": 100001, "cached_input_tokens": 0, "output_tokens": 1})
        self.runtime.tick_once()
        self.assertEqual(self.store.status()["provider_pauses"][0]["reason"], "sol_usage_over_limit")
        with self.store.transaction() as db:
            self.assertFalse(self.broker.can_plan_sol(db, job))

    def test_runner_reads_one_real_terminal_receipt_after_restart(self):
        from modal_research.gateway import AgentBinding
        from modal_research.tests.test_agent_runner import AgentRunnerTest

        fixture = AgentRunnerTest()
        fixture.setUp()
        self.addCleanup(fixture.doCleanups)
        fixture.bindings["sol"] = AgentBinding("gpt-6-sol", "fixture-sol", 16_000,
                                               "sol-isolated-files-and-tests")
        job, tasks = fixture.sol_job()
        runner = fixture.runner(tasks=tasks)
        self.assertEqual(runner.run(job).status, "succeeded")
        restarted = fixture.runner("empty", tasks=tasks)
        usage = restarted.sol_usage_receipt(job)
        self.assertEqual(usage, {"input_tokens": 20, "cached_input_tokens": 3, "output_tokens": 10})
        evidence = fixture.state / digest(["study-one", job.key]) / "evidence.json"
        document = json.loads(evidence.read_text())
        document["usage"] = None
        evidence.write_text(json.dumps(document))
        with self.assertRaisesRegex(ValueError, "usage receipt"):
            restarted.sol_usage_receipt(job)
        document["usage"] = usage
        document["terminal"].append("turn.completed")
        evidence.write_text(json.dumps(document))
        with self.assertRaisesRegex(ValueError, "one-turn"):
            restarted.sol_usage_receipt(job)


if __name__ == "__main__":
    unittest.main()
