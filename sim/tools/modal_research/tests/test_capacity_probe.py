from pathlib import Path
import tempfile
import time
from types import SimpleNamespace
import unittest

from modal_research.sandbox_smoke import CAPACITY_TASKS, _capacity_quote
from modal_research.store import Store


class CapacityProbeTest(unittest.TestCase):
    def test_extended_duration_and_cost_are_bound_to_exact_isolated_reservation(self):
        with tempfile.TemporaryDirectory() as directory:
            store = Store(Path(directory) / "operation.sqlite3", "capacity-probe-test", clock=lambda: 1000)
            store.approve(100, "approved")
            store.record_platform(gross_limit=42.50, net_limit=30, credit=27.66, gross_used=2.41,
                                  net_spent=0, reference="verified")
            key = "reservation_capacity_l_018"
            task = CAPACITY_TASKS["L"]
            scope = store.isolated_smoke_namespace(key)
            for reserve, namespace in (("2.00", scope), ("2.50", None)):
                with self.assertRaises(ValueError):
                    store.reserve_access_probe(key, task, "gpu_smoke", reserve, 1200,
                                               isolated_gpu_namespace=namespace, worker_seconds=600)
            store.reserve_access_probe(key, task, "gpu_smoke", "2.50", 1200,
                                       isolated_gpu_namespace=scope, worker_seconds=600,
                                       billing_vendor="modal", billing_reference="verified")
            binding = {"task": task, "app": scope, "volume": scope, "worker_seconds": 600}
            with self.assertRaises(ValueError):
                store.gpu_smoke_step(key, "stage", {**binding, "worker_seconds": 300})
            store.gpu_smoke_step(key, "stage", binding)
            row = store.gpu_smoke_step(key, "launch", binding)
            self.assertEqual((row["reserve"], row["worker_seconds"], row["worker_deadline"]), (250, 600, 1600))
            with self.assertRaises(ValueError):
                store.gpu_smoke_step(key, "launch", binding)
            self.assertEqual(store.status()["platform_reserved_usd"], 2.5)

    def test_live_l_quote_includes_original_startup_and_output_bound(self):
        snapshot = {"metered_observed_at": time.time(), "metered_rates": {
            "gpu_per_second": "0.0010972222223", "cpu_per_core_second": "0.0000394166667",
            "memory_per_gib_second": "0.0000066666667", "volume_per_gib_month": "0.09"}}
        store = SimpleNamespace(platform_snapshot=lambda: snapshot)
        packet = {"task": CAPACITY_TASKS["L"], "arm": "L", "worker_seconds": 600}
        quote = _capacity_quote(store, packet)
        self.assertEqual(quote["startup_allowance_seconds"], 300)
        self.assertEqual(quote["estimate_usd"], "2.42")
        snapshot["metered_rates"]["volume_per_gib_month"] = "0.2"
        with self.assertRaisesRegex(ValueError, "exceeds"):
            _capacity_quote(store, packet)


if __name__ == "__main__":
    unittest.main()
