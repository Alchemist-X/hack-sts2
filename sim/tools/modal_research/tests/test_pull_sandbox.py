from dataclasses import replace
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

from modal_research.adapters import Outcome, ProviderIntegrityError, ProviderScopeError
from modal_research.broker import RavenBroker, RavenRuntime
from modal_research.fixture import artifact, spec
from modal_research.raven_runtime import ResearchUsagePolicy, SandboxRates, WorkspaceMetering, active_grant
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

    def add_experiment(self, experiment_id):
        frozen = spec(self.now, experiment_id=experiment_id)
        frozen["eval_protocol"] = "silent-a10-smoke-v1"
        config_hash = self.store.register(frozen)
        self.store.start_registered(experiment_id, config_hash, f"start-{experiment_id}")

    def test_independent_grants_overlap_survive_restart_and_each_cancel_on_stop(self):
        self.add_experiment("parallel-m")
        self.add_experiment("parallel-l")
        self.provider.binding = SimpleNamespace(volume_id="vo-Test")
        for _ in range(4):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 3)
        initial = {grant.job.key: grant.launch_token for grant in self.provider.grants}
        self.assertEqual(len(initial), 3)
        snapshot = active_grant(self.runtime)
        self.assertEqual(snapshot["schema_version"], 2)
        self.assertEqual({row["job_key"]: row["launch_token"] for row in snapshot["grants"]}, initial)
        self.assertEqual(self.store.status()["reserved_usd"], 3)

        self.restart()
        self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 3)
        self.store.stop("operator")
        for _ in range(3):
            self.now += 1
            self.runtime.tick_once()
        self.assertCountEqual(self.provider.cancelled, ["sb-Test1", "sb-Test2", "sb-Test3"])
        self.assertEqual(len(self.provider.grants), 3)
        self.assertEqual(self.store.status()["reserved_usd"], 3)

    def test_independent_intents_share_the_stage_and_platform_budget(self):
        self.add_experiment("parallel-m")
        self.add_experiment("parallel-l")
        self.store.approve(10, "stage-cap", execution_cap_usd=2.5)
        self.store.record_platform(gross_limit=1.5, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                   reference="tight-platform")
        for _ in range(5):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 1)
        self.store.record_platform(gross_limit=10, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                   reference="platform-reconciled")
        for _ in range(5):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 2)
        self.assertEqual(self.store.status()["reserved_usd"], 2)

    def test_expired_ambiguous_receipt_quarantines_only_its_experiment(self):
        self.provider.lost = True
        unknown = self.start()
        self.now = unknown.job.deadline + 181
        self.runtime.tick_once()
        status = self.store.status()
        self.assertIsNone(status["stop"])
        self.assertEqual(status["active_jobs"][0]["state"], "uncertain")

        self.provider.lost = False
        self.add_experiment("parallel-m")
        with self.store.transaction() as db:
            db.execute("UPDATE experiments SET stage='needs_train' WHERE id=?", (self.experiment,))
        for _ in range(3):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 2)
        self.assertEqual(self.provider.grants[1].job.payload["spec"]["experiment_id"], "parallel-m")
        self.assertEqual(self.store.status()["reserved_usd"], 2)

        self.provider.visible = True
        self.restart()
        for _ in range(4):
            self.runtime.tick_once()
        self.assertIn("sb-Test1", self.provider.cancelled)
        self.assertNotIn("sb-Test2", self.provider.cancelled)
        self.assertEqual(len(self.provider.grants), 2)

    def test_research_intent_only_holds_the_related_experiment(self):
        self.add_experiment("parallel-m")
        policy = ResearchUsagePolicy("plan", "account", 2000, 200, 1, ("model-event",))
        self.broker = RavenBroker(self.store, self.artifacts, authorize_spawn=lambda job: True,
                                  reserve_bound=lambda job: self.cost, research_policy=policy)
        self.runtime = RavenRuntime(self.broker, modal_provider=self.provider)
        self.store.ingest("model-event", "bootstrap", {"experiment_id": self.experiment})
        for _ in range(3):
            self.runtime.tick_once()
        grant, = self.provider.grants
        self.assertEqual(grant.job.payload["spec"]["experiment_id"], "parallel-m")
        self.assertEqual(self.store.status()["reserved_usd"], 3)

    def test_external_unknown_scope_blocks_but_prepared_and_proven_isolated_can_coexist(self):
        self.store.reserve_access_probe("legacy", "legacy-op", "gpu_smoke", 1, 1200)
        self.runtime.tick_once()
        self.assertFalse(self.provider.grants)
        self.store.finish_access_probe("legacy", {"status": "gpu_not_launched", "session_ids": [],
                                                   "report_ref": "test-no-provider-send"})
        self.store.reserve_access_probe("prepared", "prepared-op", "gpu_smoke", 1, 1200, prepare_only=True)
        self.start()
        with self.assertRaisesRegex(ValueError, "scope conflicts"):
            self.store.activate_access_probe("prepared")
        scope = self.store.isolated_smoke_namespace("isolated")
        self.store.reserve_access_probe("isolated", "isolated-op", "gpu_smoke", 1, 1200,
                                        isolated_gpu_namespace=scope)
        with self.assertRaisesRegex(ValueError, "authorized decision or namespace"):
            self.store.gpu_smoke_step("isolated", "stage", {"task": "isolated-op", "app": scope, "volume": "wrong"})
        binding = {"task": "isolated-op", "app": scope, "volume": scope}
        self.store.gpu_smoke_step("isolated", "stage", binding)
        self.store.gpu_smoke_step("isolated", "launch", binding)
        self.add_experiment("parallel-m")
        for _ in range(3):
            self.runtime.tick_once()
        self.assertEqual(len(self.provider.grants), 2)
        self.assertEqual(self.store.status()["reserved_usd"], 5)

    def test_modal_quota_pauses_provider_while_identity_conflict_stops_study(self):
        self.add_experiment("parallel-m")
        def no_quota(grant, *, send_not_after):
            self.provider.grants.append(grant)
            raise ProviderScopeError("quota")
        self.provider.spawn_detached = no_quota
        self.start()
        for _ in range(3):
            self.now += 1
            self.runtime.tick_once()
        status = self.store.status()
        self.assertIsNone(status["stop"])
        self.assertEqual(status["provider_pauses"][0]["provider"], "modal")
        self.assertEqual(len(self.provider.grants), 1)
        scope = self.store.isolated_smoke_namespace("external-probe")
        with self.assertRaisesRegex(ValueError, "authorized headroom"):
            self.store.reserve_access_probe("external-probe", "external-op", "gpu_smoke", 1, 1200,
                                            isolated_gpu_namespace=scope)

        def conflicting_receipts(job, launch_token, input_id):
            raise ProviderIntegrityError("duplicate")
        self.provider.find_pending = conflicting_receipts
        self.runtime.tick_once()
        self.assertEqual(self.store.status()["stop"], "provider identity conflict")
        self.assertEqual(len(self.provider.grants), 1)

    def test_metering_updates_only_current_cycle_and_checks_live_quote_freshness(self):
        self.store.record_platform(gross_limit=42.50, net_limit=30, credit=27.66, gross_used=2.41, net_spent=0,
                                   reference="audited", observed_at=990, cycle_start="2026-11-01")
        raw_rates = {"gpu_hour_cost_h100": "3.95", "cpu_hour_cost_sandbox": "0.1419",
                     "mem_gib_hour_cost_sandbox": "0.024", "volume_storage_gib_month_cost": "0.09"}
        observation = {"workspace": "project-workspace", "gross_used": "2.4801", "net_spent": "0",
                       "cycle_start": "2026-11-01", "observed_at": self.now, "rates": raw_rates}
        self.provider.workspace_metering = lambda workspace: observation
        meter = WorkspaceMetering(self.store, self.provider, workspace="project-workspace",
                                  interval_seconds=30, max_age_seconds=90)
        meter.refresh()
        self.assertEqual(self.store.status()["platform_snapshot"]["gross_used_usd"], 2.49)
        grant = self.start()
        rates = SandboxRates("quote", 999, "0.001", "0.00004", "0.000007", "0.09", 0, 1, 2000)
        self.assertIsNone(meter.reservation(rates, grant.job, self.now))
        rates = replace(rates, valid_until=None)
        self.assertIsNotNone(meter.reservation(rates, grant.job, self.now))
        self.now = 1040
        observation.update(gross_used="2.45", observed_at=self.now)
        meter.refresh()
        self.assertEqual(self.store.status()["platform_snapshot"]["gross_used_usd"], 2.49)
        self.now = 1140
        observation.update(cycle_start="2026-12-01", observed_at=self.now, gross_used="0")
        meter.refresh()
        self.assertIsNone(meter.reservation(rates, replace(grant.job, deadline=self.now+30), self.now))
        self.assertEqual(self.store.status()["platform_snapshot"]["gross_used_usd"], 2.49)

    def test_conservative_compute_estimate_keeps_full_hold_without_proven_bound(self):
        self.broker = RavenBroker(self.store, self.artifacts, authorize_spawn=lambda job: True,
                                  conservative_reserve=lambda job: 75)
        self.runtime = RavenRuntime(self.broker, modal_provider=self.provider)
        grant = self.start()
        self.store.stop("acceptance")
        self.provider.outcome = Outcome("succeeded")
        self.provider.report = Outcome("succeeded", output=artifact(self.artifacts, grant.job, 10, "milestone"))
        self.runtime.tick_once()
        state = self.store.status()
        self.assertEqual(state["reserved_usd"], 1)
        self.assertEqual(state["jobs_with_verified_cost_bounds"], [])
        self.assertIsNone(state["jobs_with_conservative_reservations"][0]["proven_upper_bound_usd"])
        self.assertEqual(state["jobs_with_conservative_reservations"][0]["conservative_estimate_usd"], .75)
        self.assertEqual(state["experiments"][0]["decisions"], 10)

    def test_stop_cancels_exact_existing_id_and_no_new_send(self):
        self.start()
        self.store.stop("operator")
        self.runtime.tick_once()
        self.assertEqual(self.provider.cancelled, ["sb-Test1"])
        self.assertEqual(len(self.provider.grants), 1)
        self.assertEqual(self.store.status()["reserved_usd"], 1)

    def test_archive_preserves_latest_verified_head_after_retention_boundary(self):
        grant = self.start()
        self.provider.outcome = Outcome("succeeded")
        self.provider.report = Outcome("succeeded", output=artifact(self.artifacts, grant.job, 10, "milestone"))
        self.store.stop("operator")
        self.runtime = RavenRuntime(self.broker, modal_provider=self.provider, archive_deadline=1020)
        self.runtime.tick_once()
        self.assertEqual(self.provider.pruned, [(grant.job.key, "generation-1")])
        self.now = 1021
        self.runtime.tick_once()
        self.assertEqual(self.provider.pruned, [(grant.job.key, "generation-1")])
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
        self.assertEqual(rates.reservation(job, 1000), 76)
        self.assertIsNone(rates.reservation(job, 2001))

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
