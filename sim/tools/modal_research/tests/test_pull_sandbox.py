import tempfile
import unittest
from pathlib import Path

from modal_research.adapters import Outcome
from modal_research.broker import RavenBroker, RavenRuntime
from modal_research.fixture import artifact, spec
from modal_research.raven_runtime import SandboxRates
from modal_research.sandbox_provider import SandboxBinding, SandboxProvider
from modal_research.store import Store


class FakeDetached:
    detached = True

    def __init__(self):
        self.grants = []
        self.cancelled = []
        self.pruned = []
        self.lost = False
        self.visible = False
        self.outcome = Outcome("running")
        self.report = None

    def spawn_detached(self, grant, *, send_not_after):
        self.grants.append(grant)
        if self.lost:
            raise TimeoutError("response lost")
        return f"sb-Test{len(self.grants)}"

    def find_pending(self, job, launch_token, input_id):
        if self.visible:
            return next((f"sb-Test{index}" for index, grant in enumerate(self.grants, 1)
                         if grant.launch_token == launch_token), None)

    def poll(self, provider_id):
        return self.outcome

    def cancel(self, provider_id):
        self.cancelled.append(provider_id)

    def collect_detached(self, grant, provider_id, provider_outcome):
        return self.report

    def materialize(self, job, reference):
        return True

    def prune(self, job_key, keep):
        self.pruned.append((job_key, keep))
        return True


class DetachedTest(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)
        self.artifacts = self.root / "artifacts"
        self.now = 1000
        self.cost = 75
        self.provider = FakeDetached()
        self.restart()
        self.store.approve(10, "approved")
        self.store.record_platform(gross_limit=42.5, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                   reference="platform")
        frozen = spec(self.now)
        frozen["eval_protocol"] = "silent-a10-smoke-v1"
        self.experiment = frozen["experiment_id"]
        self.store.register(frozen)
        with self.store.transaction() as db:
            db.execute("UPDATE experiments SET stage='needs_train',target=10")

    def restart(self):
        self.store = Store(self.root / "ledger.sqlite3", "test-pull", clock=lambda: self.now)
        self.broker = RavenBroker(self.store, self.artifacts, authorize_spawn=lambda job: self.cost <= job.reserve_cents,
                                  reserve_bound=lambda job: self.cost)
        self.runtime = RavenRuntime(self.broker, modal_provider=self.provider)

    def start(self):
        self.runtime.tick_once()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 1)
        return self.provider.grants[0]

    def test_lost_receipt_is_recovered_without_second_send_and_bound_survives_restart(self):
        self.provider.lost = True
        grant = self.start()
        self.assertEqual(grant.seed_range[0], 0)
        self.assertIsNone(self.broker.claim_worker("train", grant.launch_token, "input", "other"))
        self.restart()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 1)
        self.provider.visible = True
        self.provider.outcome = Outcome("succeeded")
        self.provider.report = Outcome("succeeded", output=artifact(self.artifacts, grant.job, 10, "milestone"))
        self.cost = 10_000  # Current policy expiry cannot revoke a recorded spend envelope.
        self.runtime.tick_once()
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(len(self.provider.grants), 1)
        self.assertEqual(state["spent_usd"], 0)
        self.assertEqual(state["reserved_usd"], 1)
        self.assertEqual(state["jobs_with_verified_cost_bounds"][0]["proven_upper_bound_usd"], 0.75)
        self.assertEqual(state["experiments"][0]["decisions"], 10)

    def test_stop_cancels_exact_existing_id_and_no_new_send(self):
        self.start()
        self.store.stop("operator")
        self.runtime.tick_once()
        self.assertEqual(self.provider.cancelled, ["sb-Test1"])
        self.assertEqual(len(self.provider.grants), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 1)

    def test_archive_preserves_verified_head_until_retention_expiry_even_while_stopped(self):
        grant = self.start()
        self.provider.outcome = Outcome("succeeded")
        self.provider.report = Outcome("succeeded", output=artifact(self.artifacts, grant.job, 10, "milestone"))
        self.store.stop("operator")
        self.runtime = RavenRuntime(self.broker, modal_provider=self.provider, archive_deadline=1020)
        self.runtime.tick_once()
        self.assertEqual(self.provider.pruned, [(grant.job.key, "generation-1")])
        self.now = 1021
        self.runtime.tick_once()
        self.assertEqual(self.provider.pruned[-1], (grant.job.key, None))
        self.assertTrue((self.artifacts / grant.job.key / "generation-1" / "ready.json").is_file())

    def test_failed_worker_only_accepts_verified_partial_and_advances_grant(self):
        grant = self.start()
        self.provider.outcome = Outcome("failed", error="reclaimed")
        self.provider.report = Outcome("failed", output=artifact(self.artifacts, grant.job, 5, "checkpoint"),
                                       error="reclaimed")
        self.runtime.tick_once()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 2)
        successor = self.provider.grants[1]
        self.assertEqual(successor.job.payload["start_decisions"], 5)
        self.assertEqual(successor.seed_range[0], grant.seed_range[1])
        self.assertEqual(successor.accepted_checkpoint.reference["job_key"], grant.job.key)


class SandboxContractTest(unittest.TestCase):
    def test_cost_includes_lifetime_resources_and_storage_without_credit(self):
        from modal_research.adapters import Job
        rates = SandboxRates("rates-audited", 2000, "0.001097", "0.00003942", "0.00000667", "0.09", 30, 1, storage_verified_until=2000)
        job = Job("attempt:1", "attempt", 1, "train", 1300, 100, {"spec": {"actual_param_count": 2904208}})
        self.assertEqual(rates.bound(job, 1000), 76)
        self.assertIsNone(rates.bound(job, 2001))

    def test_ready_checkpoint_survives_receipt_pointer_crash_window(self):
        from dataclasses import asdict
        from modal_research.adapters import Job
        from modal_research.broker import DetachedGrant
        from modal_research.contracts import digest

        grant = DetachedGrant(Job("job", "job", 1, "train", 1000, 100, {}), None,
                              "raven-a", "launch-ab", 1, (0, 2))
        old = {"generation": "old", "manifest_hash": "a" * 64}
        new = {"generation": "new", "manifest_hash": "b" * 64}
        remote = {"packet_hash": digest(asdict(grant)), "job_key": "job", "launch_token": "launch-ab", "fence": 1,
                  "status": "running", "partial": old, "result": None,
                  "candidate": {"reference": new, "partial": True}}
        visible = False

        def request(message, seconds):
            if message["operation"] == "ready":
                self.assertEqual(message["reference"], new)
                return {"ready": visible}
            return {"report": remote}

        with tempfile.TemporaryDirectory() as path:
            provider = SandboxProvider(SandboxBinding("app", "ap-1", "main", "im-1", "vo-1", {"a"*64: "b"*64}),
                                       path, transport=type("Transport", (), {"request": staticmethod(request)})())
            self.assertEqual(provider.collect_detached(grant, "sb-Found", Outcome("failed")).output, old)
            visible = True
            self.assertEqual(provider.collect_detached(grant, "sb-Found", Outcome("failed")).output, new)

    def test_transport_reconciles_only_original_name_and_rejects_wrong_report(self):
        calls = []

        def request(message, seconds):
            calls.append((message, seconds))
            return {"report": {"packet_hash": "wrong"}} if message["operation"] == "report" else {"sandbox_id": "sb-Found"}

        from modal_research.adapters import Job
        from modal_research.broker import DetachedGrant
        with tempfile.TemporaryDirectory() as path:
            provider = SandboxProvider(SandboxBinding("app", "ap-1", "main", "im-1", "vo-1", {"a"*64: "b"*64}),
                                       path, transport=type("Transport", (), {"request": staticmethod(request)})())
            self.assertEqual(provider.find_pending(None, "launch-ab", None), "sb-Found")
            self.assertEqual(calls[0][0]["name"], "sts2-launch-ab")
            grant = DetachedGrant(Job("job", "job", 1, "train", 1000, 100, {}), None,
                                  "raven-a", "launch-ab", 1, (0, 2))
            with self.assertRaises(ValueError):
                provider.collect_detached(grant, "sb-Found", Outcome("succeeded"))


if __name__ == "__main__":
    unittest.main()
