import asyncio
import json
import tempfile
import subprocess
import time
import unittest
from copy import deepcopy
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from modal_research.adapters import Job
from modal_research.artifact_adapter import ArtifactAdapter, ArtifactMirror, Generation, verify_bundle
from modal_research.contracts import canonical, digest
from modal_research.fixture import spec
from modal_research.lineage import artifact_identity
from modal_research.manifests import file_digest, verify
from modal_research.modal_provider import (ModalFunctions, ModalProvider, ProviderUncertain,
                                            SdkProcessTransport, _sdk_request)
from modal_research.native_worker import Installation, _validate_seed_manifests
from modal_research.supervisor import WorkerLimits, WorkerSupervisor, _lease_watchdog, check_cgroup


def smoke(now):
    return {
        **spec(now), "eval_protocol": "silent-a10-smoke-v1",
        "dev_seed_count": 1, "blind_seed_count": 1,
    }


def job_for(frozen, now, *, kind="train", checkpoint=None):
    return Job("pure-test:1", "pure-test", 1, kind, now + 60, 10, {
        "study_id": "pure", "spec": frozen, "config_hash": digest(frozen), "checkpoint": checkpoint,
        "start_decisions": 0 if kind == "train" else 10, "target_decisions": 10,
        "start_cursor": 0 if kind == "train" else 8,
    })


class FakeModal:
    class FunctionTimeoutError(Exception):
        pass

    def __init__(self):
        self.exception = SimpleNamespace(FunctionTimeoutError=self.FunctionTimeoutError)
        self.calls = []
        self.response = TimeoutError()
        self.delayed = False
        self.resolve_delay = 0
        self.Function = SimpleNamespace(from_id=self.function)
        self.FunctionCall = SimpleNamespace(from_id=self.call)

    def function(self, object_id, *, client):
        self.calls.append(("function", object_id, client))
        time.sleep(self.resolve_delay)

        async def hydrate():
            self.calls.append(("hydrate", object_id))

        async def spawn(token):
            self.calls.append(("spawn", token))
            if self.delayed:
                await asyncio.sleep(60)
            return SimpleNamespace(object_id="fc-worker")

        return SimpleNamespace(spawn=SimpleNamespace(aio=spawn), hydrate=SimpleNamespace(aio=hydrate))

    def call(self, object_id, *, client):
        self.calls.append(("call", object_id, client))

        async def get(*, timeout):
            self.calls.append(("get", timeout))
            if isinstance(self.response, Exception):
                raise self.response
            return self.response

        async def cancel(**kwargs):
            self.calls.append(("cancel", kwargs))

        return SimpleNamespace(get=SimpleNamespace(aio=get), cancel=SimpleNamespace(aio=cancel))


class FakeTransport:
    def __init__(self, sdk, client):
        self.sdk, self.client = sdk, client
        self.windows = []

    def request(self, message, seconds):
        self.windows.append(seconds)
        return asyncio.run(asyncio.wait_for(_sdk_request(self.sdk, self.client, message), seconds))


class ProviderTest(unittest.TestCase):
    def test_fixed_function_call_id_poll_and_unknown_cost(self):
        sdk = FakeModal()
        client = object()
        transport = FakeTransport(sdk, client)
        provider = ModalProvider(transport=transport, functions=ModalFunctions("fu-train", "fu-eval"))
        now = time.time()
        job = job_for(smoke(now), now)
        self.assertEqual(provider.spawn(job, "launch-test", send_not_after=now + 2), "fc-worker")
        self.assertEqual(sdk.calls[:3], [
            ("function", "fu-train", client), ("hydrate", "fu-train"), ("spawn", "launch-test"),
        ])
        self.assertLessEqual(transport.windows[0], 2)
        self.assertEqual(provider.poll("fc-worker").status, "running")
        sdk.response = {"status": "succeeded", "output": {"generation": "gen1", "manifest_hash": "3" * 64}}
        outcome = provider.poll("fc-worker")
        self.assertEqual(outcome.status, "succeeded")
        self.assertIsNone(outcome.actual_cents)
        self.assertIsNone(provider.cost("fc-worker", job))
        self.assertFalse(provider.materialize(job, outcome.output))
        provider.cancel("fc-worker")
        self.assertIn(("cancel", {"terminate_containers": False}), sdk.calls)
        sdk.response = {"status": "succeeded", "output": outcome.output, "actual_cents": 0}
        with self.assertRaises(ProviderUncertain):
            provider.poll("fc-worker")

    def test_resolution_after_send_window_does_not_spawn(self):
        sdk = FakeModal()
        sdk.resolve_delay = 0.08
        provider = ModalProvider(transport=FakeTransport(sdk, object()), functions=ModalFunctions("fu-train", "fu-eval"),
                                 io_timeout=0.04)
        now = time.time()
        with self.assertRaises(ProviderUncertain):
            provider.spawn(job_for(smoke(now), now), "launch-test", send_not_after=now + 1)
        self.assertFalse(any(call[0] == "spawn" for call in sdk.calls))

    def test_sdk_process_timeout_kills_the_process_group_once(self):
        fake = SimpleNamespace(pid=1234, stdin=None, stdout=None,
                               communicate=lambda *args, **kwargs: (_ for _ in ()).throw(
                                   subprocess.TimeoutExpired("fake", 0.5)),
                               wait=lambda **kwargs: 0)
        with patch("modal_research.modal_provider.subprocess.Popen", return_value=fake) as popen:
            with patch("modal_research.modal_provider.os.killpg") as kill:
                with self.assertRaises(subprocess.TimeoutExpired):
                    SdkProcessTransport(verified_binding=True).request({"operation": "poll"}, 1)
        popen.assert_called_once()
        self.assertTrue(popen.call_args.kwargs["start_new_session"])
        self.assertEqual(kill.call_args.args[0], 1234)

    def test_billing_receipt_must_match_exact_attempt(self):
        now = time.time()
        job = job_for(smoke(now), now)
        receipt = {"provider_id": "fc-worker", "job_key": job.key, "logical_key": job.logical_key,
                   "attempt": job.attempt, "actual_cents": 7}
        provider = ModalProvider(transport=FakeTransport(FakeModal(), object()),
                                 functions=ModalFunctions("fu-train", "fu-eval"), cost_resolver=lambda *_: receipt)
        self.assertEqual(provider.cost("fc-worker", job), 7)
        receipt["attempt"] += 1
        with self.assertRaises(ProviderUncertain):
            provider.cost("fc-worker", job)


class Tensor:
    def __init__(self, n=1, dtype="torch.float32"):
        self.shape, self.ndim, self.dtype = (n,), 1, dtype

    def numel(self):
        return self.shape[0]


class Volume:
    def __init__(self, root):
        self.root = root
        self.commits = []
        self.reloads = 0

    def commit(self):
        self.commits.append(sorted(str(path.relative_to(self.root)) for path in self.root.rglob("ready.json")))

    def reload(self):
        self.reloads += 1


class ArtifactTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.frozen = smoke(time.time())
        self.job = job_for(self.frozen, time.time())
        self.claim = SimpleNamespace(job=self.job, seed_range=(0, 9))

    def test_checkpoint_copy_verified_then_committed_ready_last(self):
        self.frozen["ppo_config"]["tf32"] = False
        self.job = job_for(self.frozen, time.time())
        self.claim = SimpleNamespace(job=self.job, seed_range=(0, 9))
        artifact_root = self.root / "artifacts"
        artifact_root.mkdir()
        scratch = self.root / "scratch"
        cp = scratch / self.job.key / "checkpoints" / "ckpt-0000001.pt"
        cp.parent.mkdir(parents=True)
        cp.write_bytes(b"checkpoint payload")
        model = self.frozen["model_config"]
        recipe = self.frozen["ppo_config"]
        frozen_config = {
            **model, **recipe, **self.frozen["rollout_shape"], "characters": ("SILENT",), "ascension": 10,
            "start_mode": "stream", "start_saves": None, "save_mix": 0, "seed_stream_prefix": "FIXTURETRAIN",
            "init_seed": 1, "sample_seed": 2, "env_seed": 3, "rng_scheme": "decoupled",
            "bootstrap_time_limits": True, "rollout_loop_breaker": False,
        }
        # resolve_config normalizes the operational freeze's integer zero.
        self.assertIs(type(recipe["cycle_cost"]), int)
        frozen_config["cycle_cost"] = float(recipe["cycle_cost"])
        record = {
            "format": 2, "env_steps": 1, "updates": 1, "params": 1, "config": frozen_config,
            "training_precision": {"bf16_autocast": False, "cuda_matmul_tf32": False, "cudnn_tf32": False,
                                   "default_dtype": "torch.float32", "float32_matmul_precision": "highest",
                                   "ambient_cuda_autocast": False},
            "spec": {"encoder_version": "enc"}, "encoder_version": "enc", "arch": model,
            "counters": {"updates": 1, "env_steps": 1, "episodes": 0, "wins": 0, "sim_error": 0,
                         "content_gap": 0, "illegal": 0, "overflow_cand": 0},
            "model": {"param": Tensor()},
            "optimizer": {"state": {0: {key: Tensor() for key in ("step", "exp_avg", "exp_avg_sq")}},
                          "param_groups": [{"lr": recipe["lr"], "params": [0]}]},
            "rng": {"numpy": {"bit_generator": "PCG64"}, "torch": Tensor(8, "torch.uint8"),
                    "cuda": [Tensor(8, "torch.uint8")]},
            "seed_stream": {"prefix": "FIXTURETRAIN", "characters": ["SILENT"], "ascension": 10,
                            "next": 1, "started": 1, "skipped": 0, "failed": 0, "seed_range": [0, 9],
                            "reserved_skip_interval": [0, 0], "reserved_skipped": 0, "issued_until": 3},
            "inflight": {"slot": [0], "n": [0], "seed_text": ["FIXTURETRAIN-SILENT-0"], "steps": [1]},
        }
        volume = Volume(artifact_root)
        names = iter(["gen-valid", "gen-changed-recipe", "gen-bool-recipe", "gen-tf32-enabled", "gen-invalid"])

        def load(path):
            self.assertNotEqual(path, cp)
            self.assertEqual(path.read_bytes(), b"checkpoint payload")
            return record, lambda value: isinstance(value, Tensor)

        adapter = ArtifactAdapter(artifact_root, volume, checkpoint_loader=load, generation_id=lambda: next(names))
        result = adapter.training(self.claim, cp, scratch, partial=True)
        self.assertEqual((result.decisions, result.cursor, result.partial, result.issued_until), (1, 1, True, 3))
        self.assertEqual(volume.commits, [[], [f"{self.job.key}/gen-valid/ready.json"]])
        folder = artifact_root / self.job.key / "gen-valid"
        manifest = verify(folder, result.reference["manifest_hash"], artifact_identity(self.job))
        self.assertEqual(manifest["metadata"]["termination"], "checkpoint")
        self.assertEqual(manifest["metadata"]["seed_issued_until"], 3)
        self.assertEqual(manifest["metadata"]["result_label"], "engineering_smoke")
        self.assertEqual(manifest["metadata"]["training_precision"], record["training_precision"])
        mirror_root = self.root / "raven"
        mirror_root.mkdir()
        mirror = ArtifactMirror(mirror_root, SimpleNamespace(
            read_file=lambda name: iter([(artifact_root / name).read_bytes()])))
        self.assertFalse(mirror.ready(self.job, result.reference))
        self.assertTrue(mirror.copy(self.job, result.reference))
        self.assertTrue(mirror.ready(self.job, result.reference))
        (mirror_root / self.job.key / "gen-valid" / "checkpoint.pt").write_text("corrupt")
        with self.assertRaisesRegex(ValueError, "checksum"):
            mirror.ready(self.job, result.reference)
        for changed in (0.1, False):
            record["config"]["cycle_cost"] = changed
            with self.assertRaisesRegex(ValueError, "config differs"):
                adapter.training(self.claim, cp, scratch, partial=False)
        record["config"]["cycle_cost"] = 0.0
        record["training_precision"]["cuda_matmul_tf32"] = True
        with self.assertRaisesRegex(ValueError, "effective CUDA precision"):
            adapter.training(self.claim, cp, scratch, partial=False)
        record["training_precision"]["cuda_matmul_tf32"] = False
        record["rng"]["cuda"] = []
        with self.assertRaisesRegex(ValueError, "RNG"):
            adapter.training(self.claim, cp, scratch, partial=False)
        self.assertFalse((artifact_root / self.job.key / "gen-invalid" / "ready.json").exists())
        self.assertEqual(len(volume.commits), 2)

    def test_bundle_allowlist_rejects_extra_historical_checkpoint(self):
        root = self.root / "bundle"
        body = {
            "code/sts2train/__init__.py": "",
            "code/sts2vec/__init__.py": "",
            "code/sts2train/ppo.py": "pass",
            "code/sts2train/seedstream.py": "pass",
            "code/sts2train/evaluate.py": "pass",
            "lib/Sts2Sim.Vec.so": "mock",
            "dependencies.lock": "pinned",
        }
        frozen = deepcopy(self.frozen)
        for key, value in (("train", "train-data"), ("dev", "dev-data"), ("blind", "blind-data")):
            file = root / "seed_tmp"
            root.mkdir(exist_ok=True)
            file.write_text(value)
            sha = file_digest(file)
            file.unlink()
            frozen[f"{key}_seed_manifest_hash"] = sha
            body[f"seeds/{sha}.json"] = value
        for name, value in body.items():
            path = root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(value)
        hashes = {name: file_digest(root / name) for name in body}
        frozen["engine_hash"] = hashes["lib/Sts2Sim.Vec.so"]
        frozen["dependency_hash"] = hashes["dependencies.lock"]
        manifest = root / "bundle.json"
        manifest.write_text(canonical({"version": 1, "image_digest": frozen["image_digest"],
                                      "code_commit": frozen["code_commit"], "files": hashes}))
        install = Installation(frozen["code_commit"], frozen["image_digest"], root / "lib/Sts2Sim.Vec.so",
                               root / "dependencies.lock", root / "seeds", self.root, self.root)
        self.assertEqual(verify_bundle(install, frozen, root, file_digest(manifest)), root / "code")
        (root / "old-weights.pt").write_text("blocked")
        with self.assertRaisesRegex(ValueError, "allowlist"):
            verify_bundle(install, frozen, root, file_digest(manifest))

    def test_evaluation_preserves_seed_count_and_engineering_label(self):
        checkpoint = {"job_key": "train:1", "generation": "origin", "manifest_hash": "5" * 64}
        job = job_for(self.frozen, time.time(), kind="eval", checkpoint=checkpoint)
        artifact_root = self.root / "artifacts"
        artifact_root.mkdir()
        seeds = self.root / "seeds"
        seeds.mkdir()
        source = self.root / job.key / "games"
        source.mkdir(parents=True)
        (seeds / f"{self.frozen['dev_seed_manifest_hash']}.json").write_text(
            canonical({"games": [{"seed": "DEV1", "character": "SILENT"}]}))
        (source / "SILENT-DEV1.summary.json").write_text(canonical({
            "key": "SILENT-DEV1", "seed": "DEV1", "character": "SILENT", "ascension": 10,
            "replayable": True, "result": "loss", "progress": 0.2,
        }))
        adapter = ArtifactAdapter(artifact_root, Volume(artifact_root), generation_id=lambda: "evaluated")
        install = SimpleNamespace(scratch=self.root, seed_manifests=seeds)
        result = adapter.evaluation(SimpleNamespace(job=job), {"headline": {"games": 1, "wins": 0}}, install)
        self.assertEqual((result.decisions, result.cursor, result.partial), (10, 8, False))
        folder = artifact_root / job.key / "evaluated"
        self.assertEqual(json.loads((folder / "metrics.json").read_text())["result_label"], "engineering_smoke")

    def test_smoke_seedsets_are_not_capacity_inputs(self):
        train, dev, blind = [self.root / f"{role}.json" for role in ("train", "dev", "blind")]
        train.write_text(canonical({"namespace": "FIXTURETRAIN", "env_seed": 3, "character": "SILENT",
                                    "ascension": 10, "start_mode": "stream", "start_cursor": 0}))
        for path, role in ((dev, "dev"), (blind, "blind")):
            path.write_text(canonical({"name": role, "role": role, "ascension": 10, "content": "ported_only",
                                       "games": [{"character": "SILENT", "seed": role.upper() + "1"}]}))
        guard = _validate_seed_manifests(train, dev, blind, self.frozen)
        self.assertEqual({record["role"] for record in guard.sets}, {"dev", "blind"})
        self.assertIsNotNone(guard.collision("SILENT", "BLIND1", 10))
        capacity = {**self.frozen, "eval_protocol": "silent-a10-capacity-v1", "dev_seed_count": 256,
                    "blind_seed_count": 1000}
        with self.assertRaises(ValueError):
            _validate_seed_manifests(train, dev, blind, capacity)


class FakeConnection:
    def __init__(self, messages):
        self.messages = list(messages)
        self.sent = []

    def poll(self, seconds):
        if self.messages:
            return True
        time.sleep(min(seconds, 0.03))
        return False

    def recv_bytes(self, maximum):
        return canonical(self.messages.pop(0)).encode()

    def send_bytes(self, data):
        self.sent.append(json.loads(data))

    def close(self):
        pass


class FakeProcess:
    pid = None
    exitcode = None

    def __init__(self, on_start):
        self.on_start = on_start
        self.alive = False

    def start(self):
        self.pid = 987654
        self.alive = True
        self.on_start()

    def is_alive(self):
        return self.alive

    def join(self, timeout):
        self.alive = False
        self.exitcode = 0

    def close(self):
        pass


class Broker:
    def __init__(self, claim):
        self.claim = claim
        self.renewal_denied = False
        self.generations = []
        self.completed = []

    def server_time(self):
        return time.time()

    def claim_worker(self, *args):
        return self.claim

    def renew(self, claim):
        return None if self.renewal_denied else time.time() + 5

    def record_generation(self, claim, ref, **kwargs):
        self.generations.append((ref, kwargs))

    def complete(self, claim, outcome):
        self.completed.append(outcome)


class SupervisorTest(unittest.TestCase):
    def runtime(self, messages, *, lose=False, deny_resource=False):
        now = time.time()
        job = job_for(smoke(now), now)
        claim = SimpleNamespace(job=job, input_id="input-1", invocation_id="invocation-1", fence=1,
                                receipt="receipt-1", seed_range=(0, 99), lease_until=now + 5,
                                accepted_checkpoint=None)
        broker = Broker(claim)
        connection = FakeConnection(messages)
        process = FakeProcess(lambda: setattr(broker, "renewal_denied", lose))
        context = SimpleNamespace(Pipe=lambda duplex=True: (connection if duplex else FakeConnection([]),
                                                                  FakeConnection([])),
                                  Process=lambda **kw: process)
        published = []

        def training(claim, path, scratch, *, partial):
            if not partial:
                self.assertEqual(process.exitcode, 0)
            published.append(partial)
            return Generation({"generation": "partial" if partial else "final", "manifest_hash": "8" * 64}, 1, 2,
                              partial)

        def check(limits):
            if deny_resource:
                raise ValueError("not bounded")

        supervisor = WorkerSupervisor(
            broker, SimpleNamespace(refresh=lambda: None, training=training), SimpleNamespace(scratch=Path("/scratch")),
            bundle_root=Path("/bundle"), bundle_manifest_hash="0" * 64,
            renewal_seconds=0.01, resource_check=check, bundle_check=lambda *args: Path("/bundle/code"),
            process_context=context,
        )
        return supervisor, broker, process, connection, published

    @patch("modal_research.supervisor._group_alive", return_value=False)
    def test_registers_partial_and_final_after_process_exit(self, group_alive):
        supervisor, broker, _, pipe, published = self.runtime([
            {"kind": "checkpoint", "path": "same.pt"},
            {"kind": "complete", "result": {"last_checkpoint": "same.pt", "env_steps": 1,
                                            "stop_reason": "max_hours"}},
        ])
        response = supervisor.execute("train", "launch", "input-1", "invocation-1")
        self.assertEqual(response["status"], "succeeded")
        self.assertEqual(published, [True, False])
        self.assertEqual(pipe.sent, [{"kind": "published"}, {"kind": "exit"}])
        self.assertEqual([row[1]["partial"] for row in broker.generations], [True, False])
        self.assertIsNone(broker.completed[0].actual_cents)

    @patch("modal_research.supervisor._group_alive", return_value=False)
    def test_rejected_renewal_kills_worker_and_never_completes(self, group_alive):
        supervisor, broker, process, _, _ = self.runtime([], lose=True)
        with patch("modal_research.supervisor._kill", side_effect=lambda p: p.join(0) is None) as kill:
            response = supervisor.execute("train", "launch", "input-1", "invocation-1")
        self.assertEqual(response, {"status": "failed", "error": "preempted"})
        kill.assert_called_once_with(process)
        self.assertEqual(broker.completed, [])

    def test_resource_gate_precedes_process_start(self):
        supervisor, _, process, _, _ = self.runtime([], deny_resource=True)
        response = supervisor.execute("train", "launch", "input-1", "invocation-1")
        self.assertEqual(response["status"], "failed")
        self.assertIsNone(process.pid)

    def test_worker_watcher_kills_the_group_when_parent_pipe_closes(self):
        pipe = SimpleNamespace(poll=lambda _: True, recv_bytes=lambda _: (_ for _ in ()).throw(EOFError()))
        trainer_pipe = FakeConnection([])
        now = time.time()
        with patch("modal_research.supervisor.os.killpg") as kill:
            with patch("modal_research.supervisor.os._exit", side_effect=SystemExit):
                with self.assertRaises(SystemExit):
                    _lease_watchdog(pipe, trainer_pipe, now + 1, now + 2)
        kill.assert_called_once()

    def test_cgroup_inherited_cpu_and_memory_limits(self):
        with tempfile.TemporaryDirectory() as name:
            root = Path(name)
            proc = root / "proc"
            mount = root / "cgroup"
            child = mount / "app" / "worker"
            child.mkdir(parents=True)
            proc.write_text("0::/app/worker\n")
            (mount / "app" / "cpu.max").write_text("100000 100000\n")
            (mount / "app" / "memory.max").write_text("536870912\n")
            check_cgroup(WorkerLimits(cpu=1, memory_bytes=512 * 1024**2), proc=proc, mount=mount)
            (mount / "app" / "cpu.max").write_text("max 100000\n")
            with self.assertRaises(ValueError):
                check_cgroup(WorkerLimits(cpu=1, memory_bytes=512 * 1024**2), proc=proc, mount=mount)


class SeedGrantTest(unittest.TestCase):
    def test_half_open_grant_with_explicit_skipped_interval(self):
        from sts2train.seedstream import SeedGrantExhausted, SeedStream

        accepted = {"prefix": "FRESH", "next": 4, "started": 4, "skipped": 0}
        stream = SeedStream("FRESH", ("SILENT",), 10, state=accepted, seed_range=(7, 9))
        self.assertEqual(stream.state_dict()["reserved_skip_interval"], [4, 7])
        self.assertEqual(stream.state_dict()["reserved_skipped"], 3)
        for expected in (7, 8):
            index, character, seed = stream.generate()
            self.assertEqual((index, character, seed), (expected, "SILENT", f"FRESH-SILENT-{expected}"))
            stream.consume(index)
        with self.assertRaises(SeedGrantExhausted):
            stream.generate()
        with self.assertRaises(SeedGrantExhausted):
            stream.consume(9)
        self.assertEqual(stream.state_dict()["next"], 9)
        self.assertEqual(accepted["next"], 4)
