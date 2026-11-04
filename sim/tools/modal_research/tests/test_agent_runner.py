import json
import os
import signal
import subprocess
import sys
import tempfile
import time
import unittest
from dataclasses import replace
from pathlib import Path

from modal_research.adapters import Job
from modal_research.agent_runner import AgentRunner, ProcessIdentity, SolTask, SolTemplate
from modal_research.contracts import digest
from modal_research.gateway import AgentBinding
from modal_research.store import Store


FAKE_CLI = r'''
import json
import shlex
import subprocess
import sys
import time
from pathlib import Path

args = sys.argv
mode, launches = MODE, Path(LAUNCHES)
with launches.open("a") as log:
    log.write("started\n")
prompt = sys.stdin.read()
if args[-1] != "-" or not prompt or "--ephemeral" not in args or "--output-last-message" in args:
    sys.exit(2)

def event(kind, **fields):
    print(json.dumps({"type": kind, **fields}), flush=True)

event("thread.started", thread_id="fake-agent-session")
event("turn.started")
if mode == "hang":
    time.sleep(15)
    launches.with_suffix(".late").write_text("survived")
    sys.exit(0)
if mode == "linger":
    subprocess.Popen([sys.executable, "-c", "import time; time.sleep(15)"])
    sys.exit(0)
if mode == "overflow":
    sys.stderr.write("x" * (1024 * 1024 + 1))
    sys.stderr.flush()
    sys.exit(0)
if mode == "empty":
    sys.exit(0)
packet = json.loads(prompt.rsplit("\n", 1)[-1])
if "task" in packet:
    if mode == "file_fail":
        print("Failed to write file. authorization=very-secret-credential", file=sys.stderr, flush=True)
    else:
        Path("proof.txt").write_bytes(b"verified\n")
        test = shlex.split(packet["fixed_test"])
        result = subprocess.run(test, check=False)
        event("item.completed", item={"type": "command_execution", "command": shlex.join(test),
                                      "exit_code": result.returncode})
    value = {"task_id": packet["task"]["task_id"], "summary": "proof attempted", "artifact_refs": ["proof.txt"]}
else:
    value = {
        "decision_id": packet["decision_id"], "study_id": packet["study_id"],
        "expected_state_version": packet["expected_state_version"],
        "consumed_event_ids": packet["pending_event_ids"], "evidence_refs": packet["evidence_refs"],
        "observed_facts": "Synthetic event", "comparison": "None", "uncertainty": "Synthetic only",
        "action": "WAIT", "action_reason": "Below milestone", "next_wake_condition": "milestone",
        "reserve_budget_usd": 0, "experiment_spec": None, "engineering_task": None,
    }
if mode in ("privacy", "analysis_only"):
    event("item.completed", item={"type": "reasoning", "text": "private-chain-marker"})
    event("item.completed", item={"type": "agent_message", "channel": "analysis",
                                  "text": json.dumps({**value, "observed_facts": "analysis-channel-marker"})})
    print("analysis-unstructured-marker", flush=True)
    print("analysis-stderr-marker", file=sys.stderr, flush=True)
if mode != "analysis_only":
    event("item.completed", item={"type": "agent_message", "text": json.dumps(value)})
event("turn.completed", usage={"input_tokens": 20, "cached_input_tokens": 3, "output_tokens": 10})
'''


def fixture_identity(pid):
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return None
    return ProcessIdentity("fixture-boot", pid, pid)


class AgentRunnerTest(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.state = self.root / "state"
        self.workspaces = self.root / "workspaces"
        self.launches = self.root / "launches.log"
        self.launcher = self.root / "fake-cli"
        self.bindings = {
            "research": AgentBinding("fixture-astra", "fixture-research", 16_000, "research-decision"),
            "sol": AgentBinding("fixture-sol", "fixture-sol", 16_000, "sol-isolated-files-and-tests"),
        }
        self.research = Job("decision-one:1", "decision-one", 1, "research", time.time() + 10, 1, {
            "study_id": "study-one", "decision_id": "decision-one", "expected_state_version": 0,
            "consumed_event_ids": ["event-one"], "evidence_refs": ["event-one"],
        })

    def runner(self, mode="success", *, tasks=None, identity=fixture_identity, loader=None):
        self.launcher.write_text(f"#!{sys.executable}\nMODE = {mode!r}\nLAUNCHES = {str(self.launches)!r}\n" + FAKE_CLI)
        self.launcher.chmod(0o700)
        return AgentRunner(self.state, self.workspaces, "study-one", self.launcher, self.bindings,
                           sol_tasks=tasks, sol_task_loader=loader, fixture=True, process_identity=identity)

    def record(self, job, filename="state.json"):
        return json.loads((self.state / digest(["study-one", job.key]) / filename).read_text())

    def sol_job(self):
        self.workspaces.mkdir(mode=0o700, exist_ok=True)
        work = self.workspaces / "sol-isolated"
        work.mkdir(mode=0o700)
        git = ["/usr/bin/git", "-C", str(work)]
        subprocess.run([*git, "init", "-q"], check=True)
        subprocess.run([*git, "-c", "user.name=Fixture", "-c", "user.email=fixture@example.test",
                        "commit", "--allow-empty", "-qm", "fixture"], check=True)
        base = subprocess.check_output([*git, "rev-parse", "HEAD"]).decode().strip()
        payload = {"task_id": "task-one", "task_type": "diagnose", "decision_id": "decision-one",
                   "description": "Write a synthetic proof"}
        job = Job("task-one:1", "task-one", 1, "sol", time.time() + 10, 1, payload)
        tasks = {"task-one": SolTask(digest(payload), work.name, base, ("proof.txt",), (
            sys.executable, "-c", "from pathlib import Path; assert Path('proof.txt').read_bytes() == b'verified\\n'",
        ))}
        return job, tasks

    def test_real_process_result_is_durable_and_never_replayed(self):
        runner = self.runner()
        self.assertEqual(runner.poll(self.research).status, "running")
        outcome = runner.run(self.research)
        self.assertEqual(outcome.status, "succeeded")
        self.assertIsNone(outcome.actual_cents)
        self.assertEqual(json.loads(outcome.output)["action"], "WAIT")
        self.assertEqual(self.record(self.research)["session_id"], "fake-agent-session")
        self.assertEqual(self.record(self.research, "evidence.json")["usage"]["output_tokens"], 10)
        self.assertEqual(self.runner("empty").run(self.research), outcome)
        self.assertEqual(self.launches.read_text().splitlines(), ["started"])
        with self.assertRaisesRegex(ValueError, "different inputs"):
            runner.run(replace(self.research, reserve_cents=2))

    def test_reasoning_and_analysis_text_are_not_persisted(self):
        outcome = self.runner("privacy").run(self.research)
        self.assertEqual(outcome.status, "succeeded")
        job_dir = self.state / digest(["study-one", self.research.key])
        files = {path.name: path.read_text() for path in job_dir.iterdir() if path.is_file()}
        self.assertNotIn("final.json", files)
        for marker in ("private-chain-marker", "analysis-channel-marker", "analysis-unstructured-marker",
                       "analysis-stderr-marker"):
            self.assertNotIn(marker, "".join(files.values()))
        evidence = json.loads(files["evidence.json"])
        self.assertTrue(any(event.get("item_type") == "reasoning" for event in evidence["events"]))
        self.assertTrue(all(type(event["observed_at"]) is float for event in evidence["events"]))

    def test_analysis_only_cannot_be_used_as_a_final_decision(self):
        outcome = self.runner("analysis_only").run(self.research)
        self.assertEqual((outcome.status, outcome.error), ("failed", "invalid_response"))
        self.assertIsNone(outcome.output)

    def test_exit_zero_without_terminal_or_final_is_a_failure(self):
        outcome = self.runner("empty").run(self.research)
        self.assertEqual((outcome.status, outcome.error), ("failed", "agent_incomplete"))
        self.assertIsNone(outcome.actual_cents)

    def test_hard_deadline_and_lingering_child(self):
        for index, mode in enumerate(("hang", "linger")):
            with self.subTest(mode=mode):
                job = replace(self.research, key=f"deadline-{index}:1", deadline=time.time() + 0.5)
                before = time.monotonic()
                outcome = self.runner(mode).run(job)
                self.assertEqual((outcome.status, outcome.error), ("failed", "deadline"))
                self.assertLess(time.monotonic() - before, 3)
                self.assertFalse(self.launches.with_suffix(".late").exists())

    def test_pipe_limit(self):
        outcome = self.runner("overflow").run(self.research)
        self.assertEqual((outcome.status, outcome.error), ("failed", "output_limit"))
        self.assertLessEqual(self.record(self.research, "evidence.json")["bytes"], 1024 * 1024)

    def test_sol_requires_approved_packet_file_and_fixed_tool_test(self):
        job, tasks = self.sol_job()
        runner = self.runner(tasks=tasks)
        with self.assertRaisesRegex(ValueError, "not approved"):
            runner.run(replace(job, payload={**job.payload, "description": "different task"}))
        outcome = runner.run(job)
        self.assertEqual(outcome.status, "succeeded")
        self.assertEqual(len(self.record(job, "evidence.json")["artifacts"]["proof.txt"]), 64)
        self.assertTrue(outcome.output["artifact_refs"][0].startswith("sha256:"))

    def test_sol_restart_loads_exact_durable_binding(self):
        job, tasks = self.sol_job()
        original = tasks["task-one"]
        store = Store(self.root / "binding.db", "study-one", "fixture")
        template = SolTemplate("reviewed-one", "diagnose", (original.workspace,), original.base_commit,
                               original.artifact_paths, original.test_argv, 1, 30, 128, 16_000,
                               self.bindings["sol"].verification_ref)
        with store.transaction() as db:
            store.bind_sol_task(db, "task-one", job.payload, template)
        runner = self.runner(loader=store.sol_task)
        with self.assertRaisesRegex(ValueError, "not approved"):
            runner.run(replace(job, payload={**job.payload, "description": "different"}))
        self.assertEqual(runner.run(job).status, "succeeded")
        restarted = self.runner("empty", loader=store.sol_task)
        self.assertEqual(restarted.poll(job).status, "succeeded")
        self.assertEqual(self.launches.read_text().splitlines(), ["started"])

    def test_sol_text_cannot_replace_file_or_test_and_diagnostics_are_redacted(self):
        job, tasks = self.sol_job()
        runner = self.runner("file_fail", tasks=tasks)
        outcome = runner.run(job)
        self.assertEqual(outcome.status, "failed")
        evidence = self.record(job, "evidence.json")
        self.assertIn("file_write", evidence["diagnostics"])
        self.assertNotIn("very-secret-credential", json.dumps(evidence))
        self.assertEqual(runner.run(job), outcome)
        self.assertEqual(self.launches.read_text().splitlines(), ["started"])

    def test_expired_job_has_no_launch(self):
        outcome = self.runner().run(replace(self.research, deadline=time.time() - 1))
        self.assertEqual((outcome.status, outcome.actual_cents, outcome.error), ("failed", 0, "deadline"))
        self.assertFalse(self.launches.exists())

    def test_crash_before_process_receipt_never_releases_gate(self):
        runner = self.runner(identity=lambda _: os._exit(71))
        pid = os.fork()
        if pid == 0:
            runner.run(self.research)
            os._exit(72)
        _, wait_status = os.waitpid(pid, 0)
        self.assertEqual(os.waitstatus_to_exitcode(wait_status), 71)
        outcome = self.runner().run(self.research)
        self.assertEqual((outcome.status, outcome.error), ("failed", "agent_execution_uncertain"))
        self.assertFalse(self.launches.exists())
        self.assertIsNone(outcome.actual_cents)

    def test_crash_after_launch_preserves_at_most_once(self):
        runner = self.runner("hang")
        pid = os.fork()
        if pid == 0:
            runner.run(self.research)
            os._exit(72)
        try:
            deadline = time.monotonic() + 3
            while not self.launches.exists() and time.monotonic() < deadline:
                time.sleep(0.01)
            self.assertTrue(self.launches.exists())
            self.assertEqual(self.runner("hang").run(self.research).status, "running")
        finally:
            os.kill(pid, signal.SIGKILL)
            os.waitpid(pid, 0)
        outcome = self.runner().run(self.research)
        self.assertEqual((outcome.status, outcome.error), ("failed", "agent_execution_uncertain"))
        self.assertEqual(self.launches.read_text().splitlines(), ["started"])

    def test_nonlaunch_recovery_fences_an_absent_state(self):
        runner = self.runner()
        result = runner.recover(self.research)
        self.assertEqual((result.status, result.error), ("failed", "agent_execution_uncertain"))
        self.assertEqual(runner.run(self.research), result)
        self.assertFalse(self.launches.exists())

    def test_production_refuses_launch_without_cgroup_v2(self):
        from unittest.mock import patch
        from modal_research.agent_runner import SystemdExecution

        self.runner()
        with patch.object(SystemdExecution, "CGROUP", self.root / "missing-cgroup"):
            runner = AgentRunner(self.state, self.workspaces, "study-one", self.launcher, self.bindings)
            result = runner.run(self.research)
        self.assertEqual((result.status, result.error), ("failed", "unverified_kill_domain"))
        self.assertFalse(self.launches.exists())


class SystemdExecutionTest(unittest.TestCase):
    def setUp(self):
        from unittest.mock import patch
        from modal_research import agent_runner

        self.patch = patch
        self.module = agent_runner
        self.transport = agent_runner.SystemdExecution
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.folder = self.root / "test"
        self.folder.mkdir()
        self.unit = self.transport._unit(self.folder)
        self.group = f"/test.slice/{self.unit}"
        self.domain = self.root / self.group.lstrip("/")
        self.domain.mkdir(parents=True)
        for name, content in {"cpu.max": "100000 100000\n", "memory.max": "536870912\n",
                              "memory.swap.max": "0\n", "pids.max": "32\n", "cgroup.kill": "", "cgroup.events": "populated 1\n"}.items():
            (self.domain / name).write_text(content)
        self.props = {
            "Id": self.unit, "InvocationID": "a" * 32, "MainPID": "600",
            "ControlGroup": self.group, "ActiveState": "active", "KillMode": "control-group",
            "SendSIGKILL": "yes", "KillSignal": "9", "RuntimeMaxUSec": "40s",
            "TimeoutStopUSec": "2s", "Restart": "no", "ProtectControlGroups": "yes",
            "Delegate": "no", "NoNewPrivileges": "yes",
            "InaccessiblePaths": " ".join(self.transport._manager_sockets()),
        }
        self.identity = ProcessIdentity("test-boot", 600, 99)
        for patcher in (
            patch.object(self.transport, "CGROUP", self.root),
            patch.object(self.transport, "_memberships", return_value=[f"0::{self.group}"]),
            patch.object(self.module, "linux_process_identity", return_value=self.identity),
        ):
            patcher.start()
            self.addCleanup(patcher.stop)

    def receipt(self):
        identity, domain, directory, killer = self.transport._receipt(self.props, self.unit)
        os.close(directory)
        if killer is not None:
            os.close(killer)
        return {"process": {"boot_id": identity.boot_id, "pid": identity.pid,
                            "start_ticks": identity.start_ticks}, "domain": domain}

    def test_unit_command_includes_resource_limits_and_fixed_argv(self):
        command = self.transport._command(self.unit, self.root, self.root / "state.json",
                                          time.monotonic() + 10, ["/trusted/launcher", "literal;$"])
        self.assertEqual(command[-2:], ["/trusted/launcher", "literal;$"])
        self.assertIn("--expand-environment=no", command)
        self.assertIn("--pipe", command)
        self.assertIn("--property=KillMode=control-group", command)
        self.assertIn("--property=KillSignal=SIGKILL", command)
        self.assertIn("--property=CPUQuota=100%", command)
        self.assertIn("--property=MemoryMax=536870912", command)
        self.assertIn("--property=MemorySwapMax=0", command)
        self.assertIn("--property=TasksMax=32", command)
        self.assertIn("--property=ProtectControlGroups=yes", command)
        self.assertIn("--property=TimeoutStopSec=2s", command)
        self.assertTrue(any(arg.startswith("--property=RuntimeMaxSec=") for arg in command))

    def test_receipt_requires_the_declared_cgroup_and_effective_limits(self):
        receipt = self.receipt()
        self.assertEqual(receipt["domain"]["invocation_id"], "a" * 32)
        self.assertEqual(receipt["domain"]["inode"], self.domain.stat().st_ino)
        with self.assertRaisesRegex(ValueError, "control-group properties"):
            self.transport._receipt({**self.props, "KillMode": "process"}, self.unit)
        (self.domain / "pids.max").write_text("33")
        with self.assertRaisesRegex(ValueError, "resource limits"):
            self.transport._receipt(self.props, self.unit)
        (self.domain / "pids.max").write_text("32")
        (self.domain / "cgroup.kill").unlink()
        with self.assertRaises(FileNotFoundError):
            self.transport._receipt(self.props, self.unit)

    def test_receipt_refuses_missing_nonzero_or_unlimited_swap(self):
        swap = self.domain / "memory.swap.max"
        for value in ("1", "max"):
            with self.subTest(value=value):
                swap.write_text(value)
                with self.assertRaisesRegex(ValueError, "resource limits"):
                    self.receipt()
        swap.unlink()
        with self.assertRaises(FileNotFoundError):
            self.receipt()

    def test_recovery_never_signals_replaced_invocation_and_requires_quiescence(self):
        receipt = self.receipt()
        original_read = Path.read_text

        def mock_boot(path, *args, **kwargs):
            if str(path) == "/proc/sys/kernel/random/boot_id":
                return self.identity.boot_id
            return original_read(path, *args, **kwargs)

        with self.patch.object(Path, "read_text", mock_boot):
            with self.patch.object(self.transport, "_show", return_value={"InvocationID": "b" * 32}):
                self.assertFalse(self.transport().recover(self.folder, receipt))
            self.assertEqual((self.domain / "cgroup.kill").read_text(), "")
            with (self.patch.object(self.transport, "_show", return_value=self.props),
                  self.patch.object(self.module, "_wait_cgroup_empty", return_value=False)):
                self.assertFalse(self.transport().recover(self.folder, receipt))
            self.assertEqual((self.domain / "cgroup.kill").read_text(), "1")
            (self.domain / "cgroup.events").write_text("populated 0\n")
            self.assertTrue(self.transport().recover(self.folder, receipt))

    def test_guard_does_not_launch_until_matching_durable_receipt(self):
        state = self.root / "state.json"
        marker = self.root / "started"
        deadline = time.monotonic() + 2
        command = [
            sys.executable, "-I", "-c", self.module._SYSTEMD_GUARD, str(state), str(deadline),
            sys.executable, "-c", f"from pathlib import Path; Path({str(marker)!r}).touch()",
        ]
        env = {**os.environ, "INVOCATION_ID": "a" * 32}
        with subprocess.Popen(command, env=env) as proc:
            try:
                self.module._save(self.root, state.name, {
                    "phase": "launching", "domain": {"invocation_id": "b" * 32}, "process": {"pid": proc.pid},
                })
                time.sleep(0.07)
                self.assertFalse(marker.exists())
                self.module._save(self.root, state.name, {
                    "phase": "launching", "domain": {"invocation_id": "a" * 32}, "process": {"pid": proc.pid},
                })
                self.assertEqual(proc.wait(timeout=2), 0)
            finally:
                if proc.poll() is None:
                    proc.kill()
        self.assertTrue(marker.exists())

    def test_user_manager_can_cancel_without_direct_cgroup_kill(self):
        receipt = self.receipt()
        original_read = Path.read_text

        def mock_boot(path, *args, **kwargs):
            if str(path) == "/proc/sys/kernel/random/boot_id":
                return self.identity.boot_id
            return original_read(path, *args, **kwargs)

        with (self.patch.object(Path, "read_text", mock_boot),
              self.patch.object(self.transport, "_show", return_value=self.props),
              self.patch.object(self.module, "_cgroup_killer", return_value=None),
              self.patch.object(self.module, "_wait_cgroup_empty", return_value=False)):
            self.assertFalse(self.transport().recover(self.folder, receipt))
        record = json.loads((self.folder / "state.json").read_text())
        self.assertEqual(record["phase"], "cancelling")
        self.assertEqual(record["domain"]["invocation_id"], "a" * 32)
        self.assertEqual((self.domain / "cgroup.kill").read_text(), "")
