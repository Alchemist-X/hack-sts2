import tempfile
import unittest
from pathlib import Path

from modal_research.store import Store


class ExternalProbeLedgerTest(unittest.TestCase):
    def test_prepared_environment_probe_activates_once_and_retains_unknown_cost(self):
        with tempfile.TemporaryDirectory() as directory:
            now = [1000]
            path = Path(directory) / "state.sqlite3"
            store = Store(path, "study", clock=lambda: now[0])
            store.approve(100, "operator", execution_cap_usd=10)
            store.record_platform(gross_limit=42.5, net_limit=30, credit=30, gross_used=0, net_spent=0,
                                  reference="snapshot")
            store.reserve_access_probe("probe", "decision", "env_cpu_validation", ".10", 600, prepare_only=True)
            status = store.status()
            self.assertEqual(status["active_jobs"], [])
            self.assertEqual(status["prepared_probes"][0]["seconds"], 600)
            now[0] = 1100
            store = Store(path, "study", clock=lambda: now[0])
            store.activate_access_probe("probe")
            self.assertEqual(store.status()["active_jobs"][0]["deadline"], 1700)
            with self.assertRaises(ValueError):
                store.activate_access_probe("probe")
            evidence = {"status": "env_verified", "session_ids": [], "report_ref": "audit/result.json"}
            store.finish_access_probe("probe", evidence)
            store.finish_access_probe("probe", {**evidence, "details": {"actual_cost_usd": None}})
            status = store.status()
            self.assertEqual(status["active_jobs"], [])
            self.assertEqual(status["prepared_probes"], [])
            self.assertEqual(status["finished_probes_cost_unknown"][0]["reserved_usd"], 0.10)
            self.assertEqual(status["execution_remaining_usd"], 9.9)
            self.assertFalse(status["spend_complete"])
            with self.assertRaises(ValueError):
                store.finish_access_probe("probe", {**evidence, "report_ref": "audit/replacement.json"})

    def test_access_pause_requires_explicit_new_verification_reference(self):
        with tempfile.TemporaryDirectory() as directory:
            store = Store(Path(directory) / "state.sqlite3", "study")
            store.pause_access("sandbox blocked", "isolated command proof")
            with self.assertRaises(ValueError):
                store.resume_access("older pause", "approval")
            store.resume_access("sandbox blocked", "approval")
            self.assertIsNone(store.status()["access_gate"])
            with store.transaction() as db:
                self.assertEqual(db.execute("SELECT previous_pause FROM access_verifications").fetchone()[0],
                                 "sandbox blocked")
