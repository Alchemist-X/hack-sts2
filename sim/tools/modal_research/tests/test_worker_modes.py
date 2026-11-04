import os
from contextlib import contextmanager
from pathlib import Path
from types import SimpleNamespace
from unittest import TestCase
from unittest.mock import Mock, patch

from modal_research.modal_app import ARTIFACT_MOUNT, declare_app
from modal_research.native_worker import _evaluate_checkpoint, _evaluation_precision
from modal_research.supervisor import WorkerSupervisor


class WorkerModesTest(TestCase):
    def setUp(self):
        self.modal = Mock()
        self.modal.App.return_value.function.side_effect = lambda **_: lambda fn: fn
        self.volume = object()
        self.runtime = Mock()
        self.runtime.verified_capabilities.return_value = {
            "external_transactional_store", "provider_idempotency", "verified_research_agent",
            "verified_tool_enabled_sol", "approved_worker_image", "restart_admission",
            "volume_v2", "bounded_gross_spend",
        }
        supervisor = Mock(spec=WorkerSupervisor)
        supervisor.installation = SimpleNamespace(artifacts=Path(ARTIFACT_MOUNT))
        supervisor.publisher = SimpleNamespace(root=Path(ARTIFACT_MOUNT), volume=self.volume)
        self.runtime.worker_supervisor = supervisor

    def declare(self, **options):
        return declare_app(self.modal, runtime=self.runtime, cpu_image=object(), worker_image=object(),
                           volume_v2=self.volume, secrets=(), deployment_ref="test-deployment", **options)

    def test_smoke_declaration_is_short_and_capacity_defaults_are_preserved(self):
        for options, name, timeouts in (
            ({"engineering_smoke": True}, "sts2-modal-research-smoke", (120, 60)),
            ({}, "sts2-modal-research", (4500, 600)),
        ):
            with self.subTest(options=options):
                self.modal.reset_mock()
                app = self.declare(**options)
                self.modal.App.assert_called_once_with(name)
                cpu, *workers = [call.kwargs for call in app.function.call_args_list]
                self.assertIsNone(cpu["schedule"])
                self.assertEqual(len(workers), 2)
                for worker in workers:
                    self.assertEqual(worker["gpu"], "H100!")
                    self.assertEqual(worker["cpu"], (16, 16))
                    self.assertEqual(worker["memory"], (65536, 65536))
                    self.assertEqual((worker["timeout"], worker["startup_timeout"]), timeouts)
                    self.assertEqual(worker["retries"], 0)

    def test_smoke_cannot_schedule_or_bypass_verified_capabilities(self):
        with self.assertRaisesRegex(ValueError, "cannot schedule"):
            self.declare(engineering_smoke=True, schedule_controller=True)
        self.runtime.verified_capabilities.return_value.remove("bounded_gross_spend")
        with self.assertRaisesRegex(RuntimeError, "verified production bindings"):
            self.declare(engineering_smoke=True)
        self.modal.App.assert_not_called()

    def test_fp32_precision_and_tf32_flags_are_scoped(self):
        for modern in (True, False):
            with self.subTest(modern=modern):
                matmul = SimpleNamespace(allow_tf32=True)
                cudnn = SimpleNamespace(allow_tf32=True)
                backends = SimpleNamespace(cuda=SimpleNamespace(matmul=matmul), cudnn=cudnn)
                if modern:
                    cudnn.conv = SimpleNamespace(fp32_precision="tf32")
                    cudnn.rnn = SimpleNamespace(fp32_precision="tf32")
                    flags = (backends, matmul, cudnn, cudnn.conv, cudnn.rnn)
                    for backend in flags:
                        backend.fp32_precision = "tf32"
                torch = SimpleNamespace(backends=backends, float32="float32", dtype="float64", autocast_active=True)
                torch.get_default_dtype = lambda: torch.dtype
                torch.set_default_dtype = lambda dtype: setattr(torch, "dtype", dtype)

                @contextmanager
                def autocast(*, device_type, enabled):
                    self.assertEqual(device_type, "cuda")
                    self.assertFalse(enabled)
                    torch.autocast_active = False
                    try:
                        yield
                    finally:
                        torch.autocast_active = True

                torch.autocast = autocast
                with self.assertRaisesRegex(RuntimeError, "evaluation failed"):
                    with _evaluation_precision(torch):
                        self.assertEqual(torch.dtype, "float32")
                        self.assertFalse(torch.autocast_active)
                        if modern:
                            self.assertTrue(all(backend.fp32_precision == "ieee" for backend in flags))
                            self.assertTrue(matmul.allow_tf32 and cudnn.allow_tf32)
                        else:
                            self.assertFalse(matmul.allow_tf32 or cudnn.allow_tf32)
                        raise RuntimeError("evaluation failed")
                self.assertEqual(torch.dtype, "float64")
                self.assertTrue(torch.autocast_active)
                self.assertTrue(matmul.allow_tf32 and cudnn.allow_tf32)
                if modern:
                    self.assertTrue(all(backend.fp32_precision == "tf32" for backend in flags))

    def test_evaluation_invokes_checkpoint_with_frozen_flags(self):
        evaluate = Mock(return_value={"headline": {}})
        torch = object()
        precision = Mock()
        with (patch.dict("sys.modules", {"torch": torch, "sts2train.evaluate": SimpleNamespace(evaluate=evaluate)}),
              patch("modal_research.native_worker._evaluation_precision", precision)):
            precision.return_value = guard = Mock()
            guard.__enter__ = Mock()
            guard.__exit__ = Mock()
            self.assertEqual(_evaluate_checkpoint("dev.json", "checkpoint.pt", "output", 4), {"headline": {}})
        precision.assert_called_once_with(torch)
        guard.__enter__.assert_called_once_with()
        evaluate.assert_called_once_with(
            "dev.json", "checkpoint.pt", "output", device="cuda", policy="greedy",
            threads=4, step_cap=1200, floor_step_cap=80, loop_breaker=False, repeats=1, allow_blind=False,
        )

    def test_environment_cannot_force_tf32_on(self):
        with (patch.dict(os.environ, {"TORCH_ALLOW_TF32_CUBLAS_OVERRIDE": "1"}),
              self.assertRaisesRegex(ValueError, "TF32 override")):
            with _evaluation_precision(None):
                self.fail("conflicting precision reached evaluation")
