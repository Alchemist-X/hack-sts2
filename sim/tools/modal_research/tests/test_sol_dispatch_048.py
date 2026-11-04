import json
import subprocess
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from modal_research.adapters import Outcome
from modal_research.agent_runner import SolTemplate, _git
from modal_research.broker import RavenBroker
from modal_research.controller import Controller
from modal_research.contracts import digest
from modal_research.fixture import FixtureBackend, decision, spec
from modal_research.store import Store
from modal_research.raven_runtime import ResearchUsagePolicy, verify_sol_workspaces


class SolDispatch048Test(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.now = 1000
        self.store = Store(self.root / "state.db", "study", "fixture", clock=lambda: self.now)
        self.backend = FixtureBackend(self.root / "provider.db")
        self.backend.sol_templates = {"diagnose": SolTemplate(
            "reviewed-diagnose", "diagnose", ("slot-one",), "1" * 40,
            ("fix.patch", "test-report.json"), ("/usr/bin/python3", "-c", "assert True"),
            50, 180, 80, 2048, "approved-sol-project",
        )}
        self.controller = Controller(self.store, self.backend, self.root / "artifacts")
        self.store.approve(10, "fixture-approval")

    def research(self, event_id="evidence-one"):
        self.store.ingest(event_id, "result", {"synthetic": True})
        self.controller.tick()
        self.controller.tick()
        remote, job = self.backend.jobs()[-1]
        self.assertEqual(job.kind, "research")
        self.assertEqual(job.payload["engineering_task_types"], ["diagnose"])
        return remote, job

    def request(self, remote, job, description="Investigate the frozen fixture", reserve="0.50"):
        self.backend.complete(remote, Outcome("succeeded", 1, decision(
            job, "REQUEST_SOL", reserve_budget_usd=reserve,
            engineering_task={"task_type": "diagnose", "description": description},
        )))
        self.controller.tick()

    def test_freezes_exact_packet_and_returns_named_artifacts_to_research(self):
        remote, research = self.research()
        self.request(remote, research)
        with self.store.transaction() as db:
            row = db.execute("SELECT * FROM sol_task_bindings").fetchone()
            job = db.execute("SELECT * FROM jobs WHERE kind='sol'").fetchone()
            self.assertEqual(row["task_id"], json.loads(job["payload"])["task_id"])
            self.assertEqual(json.loads(row["task_json"])["workspace"], "slot-one")
            self.assertEqual(json.loads(row["task_json"])["artifact_paths"], ["fix.patch", "test-report.json"])
            self.assertEqual(json.loads(row["task_json"])["payload_hash"], digest(json.loads(job["payload"])))
            self.assertEqual(job["reserve"], 50)
            self.assertEqual(job["deadline"], 1180)
        self.controller.tick()
        remote, sol = self.backend.jobs()[-1]
        self.assertEqual(sol.kind, "sol")
        task = self.store.sol_task(sol.payload["task_id"])
        manifest = {name: "sha256:" + character * 64 for name, character in
                    zip(task.artifact_paths, ("a", "b"))}
        self.backend.complete(remote, Outcome("succeeded", 10, {
            "task_id": sol.payload["task_id"], "summary": "Checked fixture",
            "artifact_refs": list(manifest.values()), "artifact_manifest": manifest,
            "test_report": {"argv_hash": digest(task.test_argv), "exit_code": 0},
            "base_commit": task.base_commit, "payload_hash": task.payload_hash,
        }))
        self.controller.tick()
        self.controller.tick()
        with self.store.transaction() as db:
            event = db.execute("SELECT payload FROM events WHERE kind='sol_result'").fetchone()
            self.assertIsNotNone(event, [tuple(row) for row in db.execute("SELECT kind,state,error,outcome FROM jobs")])
            self.assertEqual(json.loads(event[0])["artifact_manifest"]["fix.patch"], "sha256:" + "a" * 64)
        self.controller.tick()
        self.assertEqual(self.backend.jobs()[-1][1].kind, "research")

    def test_failed_budget_admission_rolls_back_workspace_binding(self):
        remote, research = self.research()
        with self.store.transaction() as db:
            db.execute("UPDATE study SET execution_cap=25")
        self.request(remote, research)
        with self.store.transaction() as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM sol_task_bindings").fetchone()[0], 0)
            self.assertEqual(db.execute("SELECT COUNT(*) FROM jobs WHERE kind='sol'").fetchone()[0], 0)
            self.assertIn("execution cap", db.execute("SELECT dispatch_error FROM decisions").fetchone()[0])

    def test_description_bound_rejects_without_claiming_slot(self):
        remote, research = self.research()
        self.request(remote, research, description="x" * 81)
        with self.store.transaction() as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM sol_task_bindings").fetchone()[0], 0)
            self.assertIn("description", db.execute("SELECT dispatch_error FROM decisions").fetchone()[0])

    def test_bound_workspace_can_hold_artifacts_across_runtime_restart(self):
        template = self.backend.sol_templates["diagnose"]
        slots = self.root / "workspaces"
        work = slots / "slot-one"
        work.mkdir(parents=True)
        subprocess.run(["git", "-C", str(work), "init", "-q"], check=True)
        subprocess.run(["git", "-C", str(work), "-c", "user.name=Fixture",
                        "-c", "user.email=fixture@example.test", "commit", "--allow-empty", "-qm", "base"], check=True)
        base = _git(work, "rev-parse", "HEAD").decode().strip()
        template = SolTemplate(template.template_id, template.task_type, template.workspaces, base,
                               template.artifact_paths, template.test_argv, template.reserve_cents,
                               template.deadline_seconds, template.description_max_bytes,
                               template.output_max_bytes, template.model_binding_ref)
        runner = SimpleNamespace(workspaces=slots, git=_git)
        verify_sol_workspaces(self.store, runner, {"diagnose": template})
        (work / "fix.patch").write_text("patch output")
        with self.assertRaisesRegex(ValueError, "unused SOL workspace"):
            verify_sol_workspaces(self.store, runner, {"diagnose": template})
        payload = {"task_id": "task-one", "task_type": "diagnose", "description": "Review"}
        with self.store.transaction() as db:
            self.store.bind_sol_task(db, "task-one", payload, template)
        verify_sol_workspaces(self.store, runner, {"diagnose": template})
        self.assertEqual(self.store.sol_task("task-one").payload_hash, digest(payload))
        with self.store.transaction() as db:
            with self.assertRaisesRegex(ValueError, "no prepared SOL workspace"):
                self.store.bind_sol_task(db, "task-two", {**payload, "task_id": "task-two"}, template)
        with self.assertRaisesRegex(ValueError, "durable SOL binding differs"):
            verify_sol_workspaces(self.store, runner, {"diagnose": SolTemplate(
                "changed-template", template.task_type, template.workspaces, base,
                template.artifact_paths, template.test_argv, template.reserve_cents,
                template.deadline_seconds, template.description_max_bytes,
                template.output_max_bytes, template.model_binding_ref)})

    def test_production_dynamic_origin_allows_sol_result_research(self):
        store = Store(self.root / "production-state.db", "study", "fixture", clock=lambda: self.now)
        store.approve(10, "production-fixture-approval")
        frozen = spec(self.now, experiment_id="arm-one")
        config_hash = store.register(frozen)
        policy = ResearchUsagePolicy("dynamic-plan", "research-account", 2000, 25,
                                     allowed_config_hashes=(config_hash,))
        broker = RavenBroker(store, self.root / "production-artifacts", research_policy=policy,
                             authorize_spawn=lambda job: job.kind == "sol" or policy.allows(job, self.now),
                             reserve_bound=lambda job: 50 if job.kind == "sol" else None,
                             billing_sources={"research": ("non_modal", "research-account"),
                                              "sol": ("non_modal", "approved-sol-project")},
                             tool_enabled=True)
        broker.sol_templates = self.backend.sol_templates
        with store.transaction() as db:
            db.execute("INSERT INTO jobs(id,logical_key,attempt,kind,experiment_id,payload,reserve,actual,state,deadline) "
                       "VALUES ('eval-one:1','eval-one',1,'eval','arm-one','{}',1,1,'done',1500)")
            store.append_event(db, "eval-result-one", "result", {"experiment_id": "arm-one"}, self.now,
                               origin=("eval-one:1", "arm-one"))
            state = db.execute("SELECT * FROM study").fetchone()
            event = db.execute("SELECT * FROM events WHERE id='eval-result-one'").fetchone()
            broker.controller._schedule_analysis(db, state, event)
            row = db.execute("SELECT * FROM jobs WHERE kind='research'").fetchone()
            first = broker.controller._job(row)
            self.assertEqual(first.payload["engineering_task_types"], ["diagnose"])
            self.assertTrue(broker.can_plan_research(db, first))
            result = json.loads(decision(first, "REQUEST_SOL", reserve_budget_usd="0.50",
                                         engineering_task={"task_type": "diagnose", "description": "Review metrics"}))
            broker.controller._accept_decision(db, state, row, result)
            db.execute("UPDATE jobs SET state='done',actual=1 WHERE id=?", (first.key,))
            sol = db.execute("SELECT * FROM jobs WHERE kind='sol'").fetchone()
            self.assertIsNotNone(sol)
            self.assertEqual(json.loads(sol["payload"])["experiment_id"], "arm-one")
            db.execute("UPDATE jobs SET state='done',actual=10 WHERE id=?", (sol["id"],))
            store.append_event(db, "sol-result-one", "sol_result", {"task_id": json.loads(sol["payload"])["task_id"]},
                               self.now, origin=(sol["id"], "arm-one"))
        with store.transaction() as db:
            state = db.execute("SELECT * FROM study").fetchone()
            event = db.execute("SELECT * FROM events WHERE id='sol-result-one'").fetchone()
            broker.controller._schedule_analysis(db, state, event)
            second = broker.controller._job(db.execute(
                "SELECT * FROM jobs WHERE kind='research' AND event_id='sol-result-one'").fetchone())
            self.assertEqual(second.payload["current_experiment"]["experiment_id"], "arm-one")
            self.assertTrue(broker.can_plan_research(db, second))


if __name__ == "__main__":
    unittest.main()
