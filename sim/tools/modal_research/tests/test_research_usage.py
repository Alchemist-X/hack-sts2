import tempfile
import unittest
from pathlib import Path

from modal_research.adapters import Outcome
from modal_research.broker import RavenBroker, RavenRuntime
from modal_research.fixture import decision
from modal_research.raven_runtime import ResearchUsagePolicy
from modal_research.store import Store


class AgentProvider:
    def __init__(self):
        self.jobs = []
        self.outcome = Outcome("running")

    def spawn(self, job, launch_token, *, send_not_after):
        self.jobs.append(job)
        return "agent-probe"

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
        policy = ResearchUsagePolicy("finite-plan", "account-verified", 2000, 200, 1, ("first-event",))
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

    def test_claimed_zero_cash_cannot_bypass_usage_proof(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", actual_cents=0, output=decision(job))
        self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNotNone(state["stop"])
        self.assertIsNone(state["latest_decision"])
        self.assertEqual(state["reserved_usd"], 2)

    def test_missing_usage_stops_before_decision_or_retry(self):
        job = self.start()
        self.provider.outcome = Outcome("succeeded", output=decision(job))
        for _ in range(3):
            self.runtime.tick_once()
        state = self.store.status()
        self.assertIsNotNone(state["stop"])
        self.assertIsNone(state["latest_decision"])
        self.assertEqual(state["reserved_usd"], 2)
        self.assertEqual(len(self.provider.jobs), 1)


if __name__ == "__main__":
    unittest.main()
