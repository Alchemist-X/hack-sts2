import json
import tempfile
import unittest
from pathlib import Path
from dataclasses import replace

from modal_research.adapters import Outcome
from modal_research.agent_runner import _research_input
from modal_research.broker import RavenBroker, RavenRuntime
from modal_research.contracts import validate_decision
from modal_research.fixture import decision, spec
from modal_research.raven_runtime import ResearchUsagePolicy
from modal_research.store import Store


class AgentProvider:
    def __init__(self):
        self.jobs = []
        self.outcome = Outcome("running")

    def spawn(self, job, launch_token, *, send_not_after):
        self.jobs.append(job)
        return f"agent-probe-{len(self.jobs)}"

    def poll(self, receipt):
        return self.outcome


class ResearchUsageTest(unittest.TestCase):
    def setUp(self):
        work = tempfile.TemporaryDirectory()
        self.addCleanup(work.cleanup)
        root = Path(work.name)
        self.now = 1000
        self.store = Store(root / "ledger", "research-test", clock=lambda: self.now)
        self.store.approve(20, "approval")
        self.store.record_platform(gross_limit=40, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                   reference="platform")
        policy = ResearchUsagePolicy("finite-plan", "account-verified", 2000, 200, 2, ("first-event", "second-event"))
        self.provider = AgentProvider()
        self.broker = RavenBroker(self.store, root / "artifacts",
                                 authorize_spawn=lambda job: policy.allows(job, self.now),
                                 reserve_bound=lambda job: None, research_policy=policy)
        self.runtime = RavenRuntime(self.broker, agent_provider=self.provider)

    def start(self):
        self.store.ingest("first-event", "bootstrap", {"target": "test"})
        self.runtime.tick_once()
        self.runtime.tick_once()
        job, = self.provider.jobs
        self.assertEqual(job.deadline, 1180)
        self.assertEqual(job.attempt, 1)
        return job

    def test_valid_usage_is_separate_from_cash_and_caps_are_durable(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=decision(job), usage_value_cents=17)
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(state["spent_usd"], 0)
        self.assertFalse(state["spend_complete"])
        self.assertFalse(state["jobs_with_verified_cost_bounds"])
        self.assertEqual(state["research_usage_cash_unknown"][0]["standard_credit_value_usd"], .17)
        self.assertEqual(state["latest_decision"]["action"], "WAIT")
        self.store.ingest("other-event", "bootstrap", {})
        for _ in range(3):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 2)

    def test_new_event_is_pending_until_accepted_and_history_does_not_replay(self):
        first = self.start()
        self.provider.outcome = Outcome("succeeded", output=decision(first), usage_value_cents=17)
        self.runtime.tick_once()
        self.provider.outcome = Outcome("running")
        self.store.ingest("first-event", "bootstrap", {"target": "test"})
        self.runtime.tick_once()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 1)
        self.store.ingest("second-event", "bootstrap", {"target": "new evidence"})
        self.runtime.tick_once()
        self.runtime.tick_once()
        _, second = self.provider.jobs

        prompt, _ = _research_input(second.payload)
        packet = json.loads(prompt.rsplit("\n", 1)[-1])
        self.assertEqual(packet["pending_event_ids"], ["second-event"])
        self.assertEqual(packet["handled_event_ids"], ["first-event"])
        self.assertNotIn("consumed_event_ids", packet)
        with self.store.transaction() as db:
            events = dict(db.execute("SELECT id,decision_id FROM events WHERE kind='bootstrap'"))
        self.assertEqual(events, {"first-event": first.payload["decision_id"], "second-event": None})

        wrong = json.loads(decision(second))
        wrong["consumed_event_ids"] = ["first-event"]
        with self.assertRaisesRegex(ValueError, "stale or mismatched consumed_event_ids"):
            validate_decision(json.dumps(wrong), second.payload)
        self.provider.outcome = Outcome("succeeded", output=decision(second), usage_value_cents=17)
        self.runtime.tick_once()
        self.store.ingest("first-event", "bootstrap", {"target": "test"})
        for _ in range(3):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 2)
        self.assertEqual(self.store.status()["latest_decision"]["id"], second.payload["decision_id"])

    def test_claimed_zero_cash_cannot_bypass_usage_proof(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", actual_cents=0, output=decision(job))
        self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNone(state["stop"])
        self.assertEqual(state["research_pauses"][0]["event_id"], "first-event")
        self.assertIsNone(state["latest_decision"])
        self.assertFalse(state["spend_complete"])
        self.assertEqual(state["reserved_usd"], 2)

    def test_missing_usage_stops_before_decision_or_retry(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=decision(job))
        for _ in range(3):
            self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNone(state["stop"])
        self.assertEqual(state["research_pauses"][0]["event_id"], "first-event")
        self.assertIsNone(state["latest_decision"])
        self.assertFalse(state["spend_complete"])
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(len(self.provider.jobs), 1)

    def test_nominal_model_usage_over_reserve_pauses_the_model_provider(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=decision(job), usage_value_cents=201)
        self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNone(state["stop"])
        self.assertEqual(state["provider_pauses"][0]["reason"], "usage_value_over_reserve")
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(state["research_usage_cash_unknown"][0]["standard_credit_value_usd"], 2.01)

    def test_verified_cash_over_reserve_stops_study_and_keeps_actual(self):
        self.start()
        self.provider.outcome = Outcome("failed", actual_cents=201, error="api")
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(state["stop"], "job exceeded its reserve")
        self.assertEqual(state["spent_usd"], 2.01)
        self.assertEqual(state["reserved_usd"], 0)

    def test_model_provider_quota_does_not_stop_independent_modal_work(self):
        self.start()
        self.provider.outcome = Outcome("failed", error="rate_limit")
        self.runtime.tick_once()
        self.store.ingest("second-event", "bootstrap", {})
        for _ in range(3):
            self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNone(state["stop"])
        self.assertEqual(state["provider_pauses"][0]["provider"], "member_models")
        self.assertEqual(state["provider_pauses"][0]["reason"], "rate_limit")
        self.assertEqual(len(self.provider.jobs), 1)
        with self.store.transaction() as db:
            self.assertFalse(self.store.provider_paused(db, "train"))

    def test_invalid_scoped_schema_keeps_hold_without_automatically_replaying(self):
        first = self.start()
        self.assertFalse(self.broker.research_policy.allows(replace(first, attempt=2), self.now))
        self.provider.outcome = Outcome("succeeded", output="{}", usage_value_cents=17)
        for _ in range(6):
            self.now += 1
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 2)
        self.assertEqual(self.store.status()["research_pauses"][0]["event_id"], "first-event")

    def test_verified_dynamic_events_and_matched_results_without_lifetime_slots(self):
        ids = ["arm-current", "arm-matched", "arm-other-seeds"]
        specs = [spec(self.now, experiment_id=name) for name in ids]
        specs[-1]["dev_seed_manifest_hash"] = "f" * 64
        for frozen in specs:
            frozen["eval_protocol"] = "silent-a10-smoke-v1"
        hashes = [self.store.register(frozen) for frozen in specs]
        policy = ResearchUsagePolicy("dynamic-plan", "account", 2000, 200, allowed_config_hashes=(hashes[0],))
        self.broker = RavenBroker(self.store, self.broker.artifacts, authorize_spawn=lambda job: policy.allows(job, self.now),
                                 research_policy=policy, billing_sources={"research": ("non_modal", "member-proof")})
        self.runtime = RavenRuntime(self.broker, agent_provider=self.provider)
        self.store.ingest("forged-anomaly", "anomaly", {"experiment_id": ids[0]})

        def accepted_eval(arm, index, number):
            event = f"eval-event-{number}"
            key = f"eval-source-{number}:1"
            result = {"experiment_id": arm, "metrics": {"games": 4, "wins": 0, "progress_mean": 0.1},
                      "manifest": {"metadata": {"decisions": 10}, "identity": {
                          "config_hash": hashes[index], "eval_protocol": "silent-a10-smoke-v1",
                          "dev_seed_manifest_hash": specs[index]["dev_seed_manifest_hash"]}}}
            with self.store.transaction() as db:
                db.execute("UPDATE experiments SET stage='await_decision',decisions=10,target=10 WHERE id=?", (arm,))
                db.execute("""INSERT INTO jobs(id,logical_key,attempt,kind,experiment_id,payload,reserve,actual,state,deadline)
                              VALUES (?,?,1,'eval',?,'{}',0,0,'done',?)""", (key, key, arm, self.now + 100))
                self.store.append_event(db, event, "result", result, self.now, origin=(key, arm))
            return event

        accepted_eval(ids[1], 1, 1)
        accepted_eval(ids[2], 2, 2)
        for n in (3, 4, 5):
            event = accepted_eval(ids[0], 0, n)
            self.provider.outcome = Outcome("running")
            self.runtime.tick_once()
            self.runtime.tick_once()
            job = self.provider.jobs[-1]
            self.assertEqual(job.payload["consumed_event_ids"], [event])
            self.assertTrue(policy.allows(replace(job, attempt=2), self.now))
            self.assertFalse(policy.allows(replace(job, attempt=3), self.now))
            self.assertEqual([row["experiment_id"] for row in job.payload["matched_comparisons"]], [ids[1]])
            self.assertIn("platform_snapshot_cents", job.payload["budget"])
            self.provider.outcome = Outcome("succeeded", output=decision(job), usage_value_cents=17)
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.jobs), 3)
        self.assertEqual(self.store.status()["reserved_usd"], 6)
        self.assertEqual(self.store.status()["platform_reserved_usd"], 0)

    def test_historical_billing_classification_is_atomic_and_retains_project_hold(self):
        self.store.reserve_access_probe("member-history", "history", "model_interface_validation", 1, 120)
        self.store.reserve_access_probe("unknown-history", "unknown", "env_cpu_validation", .10, 120)
        original = self.store.status()
        self.assertEqual(original["platform_reserved_usd"], 1.1)
        with self.assertRaises(ValueError):
            self.store.classify_billing({"member-history": "non_modal", "missing": "non_modal"}, "audit")
        self.assertEqual(self.store.status()["platform_reserved_usd"], 1.1)
        self.store.classify_billing({"member-history": "non_modal"}, "audit")
        self.store.classify_billing({"member-history": "non_modal"}, "audit")
        status = self.store.status()
        self.assertEqual(status["reserved_usd"], 1.1)
        self.assertEqual(status["platform_reserved_usd"], .10)
        self.assertEqual(status["non_modal_reserved_usd"], 1)
        self.assertEqual(status["platform_gross_remaining_usd"], 39.90)


if __name__ == "__main__":
    unittest.main()
