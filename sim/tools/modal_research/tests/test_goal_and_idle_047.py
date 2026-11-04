import json
import tempfile
import unittest
from pathlib import Path

from modal_research.adapters import Job
from modal_research.broker import RavenBroker
from modal_research.controller import Controller
from modal_research.fixture import FixtureBackend, spec
from modal_research.raven_runtime import ResearchUsagePolicy
from modal_research.store import Store


def goal(study_id):
    return {"version": 1, "study_id": study_id, "character": "SILENT", "ascension": 10,
            "evaluation": {"start_mode": "fresh", "policy": "greedy", "loop_breaker": False,
                           "search": False, "human_intervention": False},
            "dev_seed_count": 256, "blind_seed_count": 1000, "min_progress_mean": 0.6,
            "min_win_rate": 0.1, "freeze_before_blind": True,
            "report_confidence_intervals": True}


class GoalAndIdle047Test(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.now = 1000
        self.store = Store(self.root / "state.db", "study", "fixture", clock=lambda: self.now)
        self.backend = FixtureBackend(self.root / "provider.db")
        self.controller = Controller(self.store, self.backend, self.root / "artifacts")

    def install_goal(self):
        research = self.root / "research"
        research.mkdir(exist_ok=True)
        source = research / "goal.json"
        source.write_text(json.dumps(goal("study")))
        self.store.install_goal_file(source)
        return source

    def test_goal_persists_and_enters_each_scientific_packet(self):
        source = self.install_goal()
        self.store.approve(10, "approved")
        self.store.ingest("event-1", "bootstrap", {"source": "fixture"})
        self.controller.tick()
        with self.store.transaction() as db:
            packet = json.loads(db.execute("SELECT payload FROM jobs WHERE kind='research'").fetchone()[0])
        self.assertEqual(packet["research_goal"], goal("study"))
        self.assertEqual(self.store.status()["research_goal"], goal("study"))
        restored = Store(self.root / "state.db", "study", "fixture", clock=lambda: self.now)
        restored.install_goal_file(source)
        self.assertEqual(restored.status()["research_goal"], goal("study"))
        source.write_text(json.dumps({**goal("study"), "min_win_rate": 0.2}))
        with self.assertRaisesRegex(ValueError, "invalid versioned research goal"):
            restored.install_goal_file(source)

    def test_idle_status_is_deterministic_without_event_or_model_call(self):
        self.store.approve(10, "approved")
        self.store.register(spec(self.now))
        self.controller.tick()
        first = self.store.status()
        self.assertEqual(first["blocked_reason"], "registered experiment is idle")
        self.assertEqual(first["next_action"], "activate an approved plan explicitly")
        self.assertEqual(first["state_version"], 0)
        self.assertEqual(first["active_jobs"], [])
        self.controller.tick()
        self.assertEqual(self.store.status()["blocked_reason"], first["blocked_reason"])
        with self.store.transaction() as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM events").fetchone()[0], 0)
            self.assertEqual(db.execute("SELECT COUNT(*) FROM jobs").fetchone()[0], 0)

    def test_external_reserved_is_active_but_finished_unknown_cost_is_not(self):
        self.store.approve(10, "approved")
        self.store.register(spec(self.now))
        with self.store.transaction() as db:
            db.execute("""INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,deadline,state,
                          remote_id,submit_started) VALUES('external:1','external',1,'access_probe','{}',1,1060,
                          'external_reserved','remote-receipt',1)""")
        self.controller.tick()
        active = self.store.status()
        self.assertEqual(active["active_jobs"][0]["state"], "external_reserved")
        self.assertIsNone(active["blocked_reason"])
        self.assertEqual(active["next_action"], "reconcile active job")
        with self.store.transaction() as db:
            db.execute("UPDATE jobs SET state='external_finished_cost_unknown',outcome='{}' WHERE id='external:1'")
        self.controller.tick()
        held = self.store.status()
        self.assertEqual(held["active_jobs"], [])
        self.assertEqual(held["blocked_reason"], "registered experiment is idle")
        self.assertEqual(held["next_action"], "activate an approved plan explicitly")
        self.assertEqual(len(held["finished_probes_cost_unknown"]), 1)

    def test_scientific_packet_lists_bounded_registered_experiment_options(self):
        self.store.approve(10, "approved")
        frozen_s = spec(self.now, experiment_id="capacity-s")
        frozen_m = spec(self.now, experiment_id="capacity-m")
        hashes = {frozen["experiment_id"]: self.store.register(frozen) for frozen in (frozen_s, frozen_m)}
        self.store.ingest("new-evidence", "bootstrap", {"source": "fixture"})
        self.controller.tick()
        with self.store.transaction() as db:
            packet = json.loads(db.execute("SELECT payload FROM jobs WHERE kind='research'").fetchone()[0])
        options = packet["experiment_options"]
        self.assertEqual([row["experiment_id"] for row in options], ["capacity-m", "capacity-s"])
        self.assertEqual(packet["experiment_options_omitted"], 0)
        self.assertEqual({row["experiment_id"]: row["config_hash"] for row in options}, hashes)
        self.assertEqual(options[0]["actual_param_count"], frozen_m["actual_param_count"])
        self.assertEqual(options[0]["model_config"], frozen_m["model_config"])
        self.assertEqual(options[0]["next_milestone"], frozen_m["evaluation_milestones"][0])
        self.assertEqual(options[0]["next_train_reserve_usd"], frozen_m["resource_request"]["train_cost"])

    def test_experiment_option_summary_has_explicit_count_when_bounded(self):
        self.store.approve(10, "approved")
        for index in range(17):
            self.store.register(spec(self.now, experiment_id=f"capacity-{index:02d}"))
        self.store.ingest("new-evidence", "bootstrap", {"source": "fixture"})
        self.controller.tick()
        with self.store.transaction() as db:
            packet = json.loads(db.execute("SELECT payload FROM jobs WHERE kind='research'").fetchone()[0])
        self.assertEqual(len(packet["experiment_options"]), 16)
        self.assertEqual(packet["experiment_options_omitted"], 1)
        self.assertEqual(len(packet["allowed_experiments"]), 17)

    def test_terminal_sol_failure_has_origin_without_mutating_experiment_stage(self):
        self.store.approve(10, "approved")
        frozen = spec(self.now)
        self.store.register(frozen)
        task = Job("sol-test:2", "sol-test", 2, "sol", self.now + 60, 50,
                   {"task_id": "task", "experiment_id": frozen["experiment_id"]})
        with self.store.transaction() as db:
            db.execute("""INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,deadline,state,actual,error)
                          VALUES(?,?,?,?,?,?,?,'failed',0,'invalid_response')""",
                       (task.key, task.logical_key, task.attempt, task.kind,
                        json.dumps(task.payload), task.reserve_cents, task.deadline))
            row = db.execute("SELECT * FROM jobs WHERE id=?", (task.key,)).fetchone()
            self.controller._failed(db, row, task, "invalid_response", None)
            origin = db.execute("SELECT * FROM research_origins").fetchone()
            event = db.execute("SELECT kind,payload FROM events").fetchone()
            stage = db.execute("SELECT stage FROM experiments WHERE id=?", (frozen["experiment_id"],)).fetchone()[0]
        self.assertEqual((origin["job_id"], origin["experiment_id"]), (task.key, frozen["experiment_id"]))
        self.assertEqual(event["kind"], "anomaly")
        self.assertEqual(json.loads(event["payload"])["experiment_id"], frozen["experiment_id"])
        self.assertEqual(stage, "idle")

    def test_dynamic_research_accepts_only_origin_bound_failed_sol_anomaly(self):
        store = Store(self.root / "dynamic.db", "dynamic-study", clock=lambda: self.now)
        store.approve(10, "approved")
        frozen = spec(self.now)
        frozen["eval_protocol"] = "silent-a10-smoke-v1"
        config_hash = store.register(frozen)
        policy = ResearchUsagePolicy("plan", "account", 2000, 25,
                                     allowed_config_hashes=(config_hash,))
        broker = RavenBroker(store, self.root / "artifacts", authorize_spawn=lambda job: policy.allows(job, self.now),
                             research_policy=policy, billing_sources={"research": ("non_modal", "proof")})
        store.ingest("forged-anomaly", "anomaly", {"experiment_id": frozen["experiment_id"]})
        broker.controller.tick()
        with store.transaction() as db:
            self.assertEqual(db.execute("SELECT COUNT(*) FROM jobs WHERE kind='research'").fetchone()[0], 0)
            task = Job("sol-failed:2", "sol-failed", 2, "sol", self.now + 60, 50,
                       {"task_id": "task", "experiment_id": frozen["experiment_id"]})
            db.execute("""INSERT INTO jobs(id,logical_key,attempt,kind,payload,reserve,deadline,state,actual,error)
                          VALUES(?,?,?,?,?,?,?,'failed',0,'invalid_response')""",
                       (task.key, task.logical_key, task.attempt, task.kind,
                        json.dumps(task.payload), task.reserve_cents, task.deadline))
            row = db.execute("SELECT * FROM jobs WHERE id=?", (task.key,)).fetchone()
            broker.controller._failed(db, row, task, "invalid_response", None)
        broker.controller.tick()
        with store.transaction() as db:
            research = db.execute("SELECT payload FROM jobs WHERE kind='research'").fetchone()
        self.assertIsNotNone(research)
        self.assertEqual(json.loads(research[0])["event"]["kind"], "anomaly")


if __name__ == "__main__":
    unittest.main()
