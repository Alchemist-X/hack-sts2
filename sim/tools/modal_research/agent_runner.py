"""Single-host execution of broker-admitted Codex jobs.

The broker owns budget, provider receipts and the queue. The runner owns one
local launch per frozen job, process supervision and evidence. Use a private
local disk for state and a separately provisioned, verified tool sandbox.
"""

from __future__ import annotations

import fcntl
import hashlib
import json
import math
import os
import re
import selectors
import shlex
import signal
import stat
import subprocess
import sys
import tempfile
import time
from dataclasses import asdict, dataclass
from pathlib import Path, PurePosixPath

from .adapters import Job, Outcome
from .contracts import ACTIONS, COMMIT, bounded_text, canonical, digest, identifier, validate_decision
from .gateway import AgentBinding

PIPE_LIMIT = 1024 * 1024
PACKET_LIMIT = 64_000
ARTIFACT_LIMIT = 2 * 1024 * 1024
MAX_SECONDS = 900
SCOPES = {"research": "research-decision", "sol": "sol-isolated-files-and-tests"}
JOB_TYPES = {"diagnose", "implement_frozen_task"}
EVENTS = {
    "thread.started", "turn.started", "turn.completed", "turn.failed", "error", "warning",
    "item.started", "item.updated", "item.completed",
}
ITEM_TYPES = {"command_execution", "file_change", "agent_message", "reasoning", "error"}
SESSION = re.compile(r"[a-zA-Z0-9][a-zA-Z0-9_.:-]{0,127}")
DIAGNOSTICS = {
    "model_unavailable": r"model_not_found|model.{0,90}not supported",
    "sandbox_setup": r"bwrap|landlock|RTM_NEWADDR|sandbox setup",
    "file_write": r"failed to write file|read.only file system",
    "authentication": r"invalid_grant|unauthorized|not logged in",
    "rate_limit": r"rate.limit|too many requests|quota|usage.limit|credit.limit|insufficient credits",
    "network": r"connection (?:refused|timed out)|could not resolve",
}
# The child cannot launch until its identity has been persisted. If the parent
# dies before or during execution, EOF kills the entire original session.
_SUPERVISOR = """
import os, select, signal, subprocess, sys, time
gate, receipt, expires = int(sys.argv[1]), int(sys.argv[2]), float(sys.argv[3])
if os.read(gate, 1) != b"1" or time.monotonic() >= expires:
    sys.exit(124)
try:
    child = subprocess.Popen(sys.argv[4:])
except OSError:
    code = 126
else:
    while child.poll() is None:
        remaining = expires - time.monotonic()
        if remaining <= 0 or select.select([gate], [], [], min(0.1, remaining))[0]:
            os.killpg(os.getpgrp(), signal.SIGKILL)
    code = child.returncode
os.write(receipt, str(code).encode())
os.close(receipt)
os.close(1)
os.close(2)
# Keep the original session leader alive until the reader finishes draining the
# pipes. On parent death or deadline, terminate the original process group.
select.select([gate], [], [], max(0, expires - time.monotonic()))
os.killpg(os.getpgrp(), signal.SIGKILL)
"""


@dataclass(frozen=True)
class ProcessIdentity:
    boot_id: str
    pid: int
    start_ticks: int


def linux_process_identity(pid: int) -> ProcessIdentity | None:
    boot_id = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
    try:
        fields = Path(f"/proc/{pid}/stat").read_text().rsplit(")", 1)[1].split()
    except (FileNotFoundError, ProcessLookupError):
        return None
    return ProcessIdentity(boot_id, pid, int(fields[19]))


@dataclass(frozen=True)
class SolTask:
    """Operator-approved packet and pre-provisioned, dedicated clean worktree."""

    payload_hash: str
    workspace: str
    base_commit: str
    artifact_paths: tuple[str, ...]
    test_argv: tuple[str, ...]

    def validate(self):
        identifier(self.workspace)
        if not re.fullmatch(r"[a-f0-9]{64}", self.payload_hash) or not COMMIT.fullmatch(self.base_commit):
            raise ValueError("SOL task requires immutable payload and base")
        if not 1 <= len(self.artifact_paths) <= 10 or len(set(self.artifact_paths)) != len(self.artifact_paths):
            raise ValueError("SOL task needs distinct artifact paths")
        for path in self.artifact_paths:
            parts = PurePosixPath(path).parts
            if not parts or any(part.startswith(".") or not SESSION.fullmatch(part) for part in parts):
                raise ValueError("invalid SOL artifact path")
            if path != "/".join(parts):
                raise ValueError("SOL artifact path must be normalized")
        if (not 1 <= len(self.test_argv) <= 16 or not self.test_argv[0]
                or any(type(arg) is not str or "\0" in arg or len(arg) > 2000 for arg in self.test_argv)):
            raise ValueError("SOL test must be fixed argv")


class AgentRunner:
    def __init__(
        self, root: Path, workspaces: Path, study_id: str, launcher: Path,
        bindings: dict[str, AgentBinding], *, sol_tasks: dict[str, SolTask] | None = None,
        fixture=False, process_identity=linux_process_identity, system_identity=None, scoped_research=False,
    ):
        system_execution = SystemServiceExecution(**system_identity) if system_identity is not None else None
        if system_execution is not None and (fixture or Path(root) != system_execution.STATE):
            raise ValueError("system member execution requires its approved root-owned state")
        self.root = _private_directory(root)
        if type(scoped_research) is not bool or (scoped_research and system_execution is None):
            raise ValueError("scoped research requires the verified system helper")
        self.scoped_research = scoped_research
        self.workspaces = (system_execution.workspace_root(workspaces) if system_execution is not None
                           else _private_directory(workspaces))
        if self.root == self.workspaces or self.root in self.workspaces.parents or self.workspaces in self.root.parents:
            raise ValueError("runner state and workspaces must be separate")
        self.study_id = identifier(study_id)
        self.launcher = Path(launcher)
        if not self.launcher.is_absolute() or not self.launcher.is_file() or not os.access(self.launcher, os.X_OK):
            raise ValueError("launcher must be a trusted executable path")
        if system_execution is not None:
            metadata = self.launcher.stat()
            if (self.launcher.resolve() != self.launcher or metadata.st_uid != 0
                    or metadata.st_mode & 0o022 or not stat.S_ISREG(metadata.st_mode)):
                raise ValueError("system member launcher must be a root-owned immutable executable")
        if not bindings or not bindings.keys() <= SCOPES.keys():
            raise ValueError("unrecognized agent binding")
        for role, binding in bindings.items():
            binding.validate(SCOPES[role])
            if not SESSION.fullmatch(binding.model_id):
                raise ValueError("invalid model ID")
        self.bindings = dict(bindings)
        self.sol_tasks = dict(sol_tasks or {})
        if len({task.workspace for task in self.sol_tasks.values()}) != len(self.sol_tasks):
            raise ValueError("each SOL task requires its own workspace")
        for task_id, task in self.sol_tasks.items():
            identifier(task_id)
            task.validate()
        if type(fixture) is not bool or (not fixture and process_identity is not linux_process_identity):
            raise ValueError("custom process identity is limited to subprocess fixtures")
        self.process_identity = process_identity
        self.execution = None if fixture else (system_execution or SystemdExecution())
        self.git = system_execution.git if system_execution else _git

    def _fingerprint(self, job: Job):
        identifier(job.key)
        identifier(job.logical_key)
        if (job.kind not in self.bindings or type(job.attempt) is not int or job.attempt < 1
                or type(job.reserve_cents) is not int or job.reserve_cents <= 0
                or type(job.deadline) not in (float, int) or not math.isfinite(job.deadline)
                or type(job.payload) is not dict or len(canonical(job.payload).encode()) > PACKET_LIMIT):
            raise ValueError("invalid admitted agent job")
        if job.kind == "research":
            if job.payload.get("study_id") != self.study_id:
                raise ValueError("job study mismatch")
            identifier(job.payload.get("decision_id"))
            version = job.payload.get("expected_state_version")
            if type(version) is not int or version < 0:
                raise ValueError("invalid research version")
            for key in ("consumed_event_ids", "evidence_refs"):
                refs = job.payload.get(key)
                if type(refs) is not list or not refs:
                    raise ValueError("missing research evidence")
                for ref in refs:
                    identifier(ref)
        else:
            task = self.sol_tasks.get(identifier(job.payload.get("task_id")))
            if (not task or job.payload.get("task_type") not in JOB_TYPES
                    or job.payload.get("study_id", self.study_id) != self.study_id
                    or task.payload_hash != digest(job.payload)):
                raise ValueError("SOL task packet is not approved for this study")
        return digest(asdict(job))

    def _folder(self, job: Job):
        return self.root / digest([self.study_id, job.key])

    @staticmethod
    def _load(folder: Path, fingerprint: str):
        try:
            raw = (folder / "state.json").read_text()
        except FileNotFoundError:
            return None
        record = json.loads(raw)
        if record["fingerprint"] != fingerprint:
            raise ValueError("job key was reused with different inputs")
        return record

    @staticmethod
    def _outcome(record):
        if record and "outcome" in record:
            return Outcome(**record["outcome"])
        return Outcome("running")

    def poll(self, job: Job) -> Outcome:
        """Read the durable receipt without dispatch or process mutation."""
        return self._outcome(self._load(self._folder(job), self._fingerprint(job)))

    def standard_usage_value(self, job):
        """Read the exact successful one-turn receipt and estimate purchased credit value."""
        if not self.scoped_research or job.kind != "research" or self.bindings[job.kind].model_id != "gpt-6-astra":
            raise ValueError("unbound research usage metering")
        folder = self._folder(job)
        record = self._load(folder, self._fingerprint(job))
        if self._outcome(record).status != "succeeded":
            raise ValueError("research did not complete successfully")
        path = folder / "evidence.json"
        if path.is_symlink() or not 0 < path.stat().st_size <= 2 * PIPE_LIMIT:
            raise ValueError("missing bounded research usage evidence")
        evidence = json.loads(path.read_bytes())
        kinds = [event["type"] for event in evidence["events"]]
        usage = evidence["usage"]
        keys = {"input_tokens", "cached_input_tokens", "output_tokens"}
        optional = {"cache_write_input_tokens", "reasoning_output_tokens"}
        if (type(usage) is not dict or not keys <= usage.keys() <= keys | optional or evidence["exit_code"] != 0
                or evidence["terminal"] != ["turn.completed"] or kinds.count("turn.started") != 1
                or kinds.count("thread.started") != 1 or evidence.get("unrecognized_items", 0)
                or evidence["commands"] or evidence["diagnostics"] or any(kind in {"warning", "error"} for kind in kinds)
                or any(event.get("item_type") not in {None, "agent_message", "reasoning"} for event in evidence["events"])
                or any(type(value) is not int for value in usage.values())):
            raise ValueError("research lacks a valid no-tool terminal usage receipt")
        inputs, cached, outputs = (usage[key] for key in ("input_tokens", "cached_input_tokens", "output_tokens"))
        if (not 0 <= cached <= inputs or not 0 < inputs or not 0 < outputs
                or not 0 <= usage.get("cache_write_input_tokens", 0) <= inputs
                or not 0 <= usage.get("reasoning_output_tokens", 0) <= outputs):
            raise ValueError("research terminal token usage is inconsistent")
        # GPT-6 Astra Standard credits per million: 250 / 25 / 1250.
        # $100 per 2500 purchased credits = 4 cents per credit.
        units = 4 * ((inputs - cached) * 250 + cached * 25 + outputs * 1250)
        return (units + 999_999) // 1_000_000

    def run(self, job: Job) -> Outcome:
        """Run at most once. Only the broker may hand an admitted job to this method."""
        return self._claim(job, start=True)

    def recover(self, job: Job) -> Outcome:
        """Fence a stale broker claim and reconcile without launching any process."""
        return self._claim(job, start=False)

    def _claim(self, job, *, start):
        fingerprint = self._fingerprint(job)
        folder = self._folder(job)
        folder.mkdir(mode=0o700, exist_ok=True)
        _sync_directory(self.root)
        with (folder / "claim.lock").open("ab") as lock:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            except BlockingIOError:
                return self._outcome(self._load(folder, fingerprint))
            record = self._load(folder, fingerprint)
            if record is not None:
                if "outcome" not in record:
                    self._recover(folder, record)
                return self._outcome(record)
            binding = self.bindings[job.kind]
            record = {
                "fingerprint": fingerprint, "key": job.key, "role": job.kind,
                "binding": asdict(binding), "deadline": job.deadline, "session_id": None, "phase": "claimed",
            }
            _save(folder, "state.json", record)
            if not start:
                return self._finish(folder, record, Outcome("failed", error="agent_execution_uncertain"))
            if job.deadline <= time.time():
                return self._finish(folder, record, Outcome("failed", 0, error="deadline"))
            return self._run_claimed(job, binding, folder, record)

    def _finish(self, folder: Path, record, outcome: Outcome):
        record["phase"] = "terminal"
        record["outcome"] = asdict(outcome)
        _save(folder, "state.json", record)
        return outcome

    def _recover(self, folder: Path, record):
        if self.execution is not None and not self.execution.recover(folder, record):
            return
        # Fixture watchdogs see the dead parent's gate close. No recovery path
        # signals a numeric PID or process group read from a disk record.
        (folder / "final.json").unlink(missing_ok=True)
        self._finish(folder, record, Outcome("failed", error="agent_execution_uncertain"))

    def _run_claimed(self, job, binding, folder, record):
        task = self.sol_tasks[job.payload["task_id"]] if job.kind == "sol" else None
        try:
            if task is None:
                workspace = self.workspaces / folder.name
                workspace.mkdir(mode=0o700)
                if isinstance(self.execution, SystemServiceExecution):
                    os.chown(workspace, self.execution.WORKER_UID, self.execution.WORKER_GID)
                prompt, schema = _research_input(job.payload)
                if self.scoped_research and len(prompt.encode()) > 24_000:
                    raise ValueError("scoped research prompt exceeds 24000 bytes")
            else:
                workspace = self.workspaces / task.workspace
                if not workspace.is_dir() or workspace.resolve() != workspace:
                    raise ValueError("missing isolated SOL workspace")
                # A different attempt must receive a fresh operator-provisioned
                # worktree; an old crash may still own the prior one.
                (self.root / f"workspace-{digest(task.workspace)}").mkdir(mode=0o700)
                _sync_directory(self.root)
                if self.git(workspace, "rev-parse", "HEAD").strip().decode() != task.base_commit or _changes(workspace, self.git):
                    raise ValueError("SOL workspace is not at its clean base")
                prompt, schema = _sol_input(job.payload, task)
            _save(folder, "schema.json", schema)
        except (OSError, ValueError, subprocess.SubprocessError):
            return self._finish(folder, record, Outcome("failed", 0, error="agent_preflight"))
        sandbox = "read-only" if task is None else "workspace-write"
        argv = [
            str(self.launcher), "-a", "never", "exec", "--model", binding.model_id, "--sandbox", sandbox,
            "--cd", str(workspace), "--skip-git-repo-check", "--ephemeral", "--json",
            "--output-schema", str(folder / "schema.json"), "-",
        ]
        limit = 180 if job.kind == "research" and self.scoped_research else MAX_SECONDS
        left = min(job.deadline - time.time(), limit)
        if left <= 0:
            return self._finish(folder, record, Outcome("failed", 0, error="deadline"))
        deadline = time.monotonic() + left
        try:
            execute = self._execute_fixture if self.execution is None else self.execution.execute
            exit_code, error, evidence = execute(
                argv, workspace, prompt, deadline, folder, record,
                **({"pipe_limit": 1_000_000} if job.kind == "research" and self.scoped_research else {}),
            )
        except (OSError, ValueError, subprocess.SubprocessError):
            return self._finish(folder, record, Outcome("failed", error="agent_execution_uncertain"))
        # Raw agent messages exist only in memory. Only schema-validated
        # final decisions and allowlisted evidence fields are durable.
        final_text = evidence.pop("_final_text", None)
        evidence["exit_code"] = exit_code
        if error == "containment_unresolved":
            _save(folder, "evidence.json", evidence)
            if record["phase"] != "cancelling":
                record["phase"] = "cleanup_pending"
                _save(folder, "state.json", record)
            return Outcome("running", error=error)
        try:
            outcome = _accept(job, binding, task, workspace, final_text, exit_code, error, evidence, git=self.git)
        except (ValueError, OSError, KeyError, TypeError, RecursionError, subprocess.SubprocessError):
            outcome = Outcome("failed", error="invalid_response")
        _save(folder, "evidence.json", evidence)
        return self._finish(folder, record, outcome)

    def _execute_fixture(self, argv, workspace, prompt, deadline, folder, record):
        gate_read, gate_write = os.pipe()
        code_read, code_write = os.pipe()
        process = None
        evidence = _evidence()
        try:
            with tempfile.TemporaryFile() as input_file:
                input_file.write(prompt.encode())
                input_file.seek(0)
                process = subprocess.Popen(
                    [sys.executable, "-I", "-c", _SUPERVISOR, str(gate_read), str(code_write),
                     str(deadline), *argv],
                    cwd=workspace, stdin=input_file, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                    pass_fds=(gate_read, code_write), start_new_session=True,
                )
                os.close(code_write)
                code_write = None
                identity = self.process_identity(process.pid)
                if identity is None:
                    raise OSError("missing supervisor identity")
                record["phase"] = "launching"
                record["process"] = asdict(identity)
                _save(folder, "state.json", record)
                os.write(gate_write, b"1")
                error = _capture(process, deadline, evidence, lambda sid: _record_session(folder, record, sid))
                if error is not None:
                    return None, error, evidence
                os.set_blocking(code_read, False)
                try:
                    raw = os.read(code_read, 16)
                except BlockingIOError:
                    raw = b""
                if not raw:
                    return None, "deadline" if time.monotonic() >= deadline else "agent_incomplete", evidence
                return int(raw), None, evidence
        finally:
            os.close(gate_read)
            os.close(gate_write)
            os.close(code_read)
            if code_write is not None:
                os.close(code_write)
            if process is not None:
                try:
                    process.wait(timeout=0.5)
                except subprocess.TimeoutExpired:
                    _kill_group(process.pid)
                    process.wait(timeout=2)
                finally:
                    process.stdout.close()
                    process.stderr.close()



def _private_directory(path):
    path = Path(path).absolute()
    path.mkdir(mode=0o700, parents=True, exist_ok=True)
    metadata = path.stat()
    if (path != path.resolve() or not stat.S_ISDIR(metadata.st_mode)
            or metadata.st_uid != os.geteuid() or metadata.st_mode & 0o077):
        raise ValueError("runner directories must be private and owned by the service user")
    return path


def _save(folder, name, payload):
    fd, path = tempfile.mkstemp(dir=folder)
    try:
        with os.fdopen(fd, "w") as handle:
            handle.write(canonical(payload))
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(path, folder / name)
        _sync_directory(folder)
    finally:
        Path(path).unlink(missing_ok=True)


def _sync_directory(folder):
    directory = os.open(folder, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def _kill_group(pid):
    try:
        if os.getpgid(pid) == pid and os.getsid(pid) == pid:
            os.killpg(pid, signal.SIGKILL)
    except ProcessLookupError:
        pass


def _record_session(folder, record, session_id):
    if record["session_id"] is None:
        record["session_id"] = session_id
        _save(folder, "state.json", record)
    elif record["session_id"] != session_id:
        raise ValueError("multiple sessions for one agent job")


def _capture(process, deadline, evidence, record_session, pipe_limit=PIPE_LIMIT):
    pending = {"stdout": bytearray(), "stderr": bytearray()}
    with selectors.DefaultSelector() as selector:
        for name in pending:
            selector.register(getattr(process, name), selectors.EVENT_READ, name)
        while selector.get_map():
            left = deadline - time.monotonic()
            if left <= 0:
                return "deadline"
            for key, _ in selector.select(min(left, 0.25)):
                remaining = pipe_limit - evidence["bytes"]
                chunk = os.read(key.fd, min(65_536, remaining + 1))
                if not chunk:
                    selector.unregister(key.fileobj)
                    if pending[key.data]:
                        _event(key.data, bytes(pending[key.data]), evidence, record_session)
                    continue
                if len(chunk) > remaining:
                    return "output_limit"
                evidence["bytes"] += len(chunk)
                pending[key.data].extend(chunk)
                lines = pending[key.data].split(b"\n")
                pending[key.data] = lines.pop()
                for line in lines:
                    _event(key.data, bytes(line), evidence, record_session)
    return None


def _event(stream, line, evidence, record_session):
    if stream == "stderr":
        _diagnose(line, evidence)
        return
    try:
        event = json.loads(line)
    except (ValueError, UnicodeDecodeError, RecursionError):
        _diagnose(line, evidence)
        return
    if not isinstance(event, dict) or type(kind := event.get("type")) is not str or kind not in EVENTS:
        return
    if kind in {"error", "warning"}:
        _diagnose(line, evidence)
    item = event.get("item")
    item_type = item.get("type") if isinstance(item, dict) else None
    entry = {"type": kind, "observed_at": time.time()}
    if isinstance(item_type, str) and item_type in ITEM_TYPES:
        entry["item_type"] = item_type
    elif kind.startswith("item."):
        evidence["unrecognized_items"] = evidence.get("unrecognized_items", 0) + 1
    if kind == "thread.started" and isinstance(sid := event.get("thread_id"), str) and SESSION.fullmatch(sid):
        record_session(sid)
        entry["session_id"] = sid
    if kind in {"turn.completed", "turn.failed"}:
        evidence["terminal"].append(kind)
    if kind == "turn.completed" and isinstance(usage := event.get("usage"), dict):
        evidence["usage"] = {key: value for key, value in usage.items()
                             if key in {"input_tokens", "cached_input_tokens", "output_tokens", "cache_write_input_tokens",
                                        "reasoning_output_tokens"}
                             and type(value) is int and value >= 0}
    if (kind == "item.completed" and item_type == "agent_message"
            and item.get("channel") in (None, "final")
            and item.get("phase") in (None, "final", "final_answer")):
        evidence["_final_text"] = item.get("text")
    if kind == "item.completed" and item_type == "command_execution":
        command = item.get("command")
        argv = _command_argv(command) if isinstance(command, str) and len(command) <= 16_000 else []
        # Only hash tool commands. The returned text might contain credentials.
        evidence["commands"].append({"argv_hash": digest(argv), "exit_code": _event_exit_code(item)})
    if kind in {"turn.failed", "error", "warning"}:
        _diagnose(line, evidence)
    evidence["events"].append(entry)


def _event_exit_code(item):
    code = item.get("exit_code")
    return code if type(code) is int else None


def _diagnose(line, evidence):
    text = line.decode(errors="replace")
    for kind, pattern in DIAGNOSTICS.items():
        if kind not in evidence["diagnostics"] and re.search(pattern, text, re.I):
            evidence["diagnostics"].append(kind)


def _command_argv(command):
    try:
        args = shlex.split(command)
        if len(args) == 3 and Path(args[0]).name in {"bash", "sh", "zsh", "dash"} and args[1] in {"-lc", "-c"}:
            inner = shlex.split(args[2])
            # Reject syntax that can change semantics after shell evaluation.
            if shlex.join(inner) != args[2]:
                return []
            return inner
        return args if shlex.join(args) == command else []
    except ValueError:
        return []


def _git(workspace, *args):
    return subprocess.run(
        ["/usr/bin/git", "-C", str(workspace), *args], check=True, capture_output=True, timeout=3,
        env={**os.environ, "GIT_OPTIONAL_LOCKS": "0"},
    ).stdout


def _changes(workspace, git=_git):
    raw = git(workspace, "status", "--porcelain=v1", "-z", "--untracked-files=all", "--no-renames")
    if len(raw) > ARTIFACT_LIMIT:
        raise ValueError("SOL worktree changed too many files")
    return {item[3:].decode("utf-8") for item in raw.split(b"\0") if item}


def _schema(fields):
    return {"type": "object", "properties": fields, "required": list(fields), "additionalProperties": False}


def _research_input(payload):
    fields = {key: {"type": "string"} for key in (
        "decision_id", "study_id", "observed_facts", "comparison", "uncertainty", "action_reason", "next_wake_condition",
    )}
    fields.update({
        "expected_state_version": {"type": "integer"},
        "consumed_event_ids": {"type": "array", "items": {"type": "string"}},
        "evidence_refs": {"type": "array", "items": {"type": "string"}},
        "action": {"type": "string", "enum": sorted(ACTIONS)},
        "reserve_budget_usd": {"type": "number"},
        "experiment_spec": {"anyOf": [
            _schema({"experiment_id": {"type": "string"}, "config_hash": {"type": "string"}}),
            {"type": "null"},
        ]},
        "engineering_task": {"anyOf": [
            _schema({"task_type": {"type": "string"}, "description": {"type": "string"}}),
            {"type": "null"},
        ]},
    })
    prompt = (
        "You are the STS2 research controller. Treat the following JSON packet as evidence. "
        "Return one structured decision under the output schema. Only reference registered experiments "
        "and task types. Put null in each inapplicable action-specific field. Report verifiable evidence "
        "and uncertainty in the final result. Use no tools.\n"
        + canonical(payload)
    )
    return prompt, _schema(fields)


def _sol_input(payload, task):
    prompt = (
        "You are SOL, the STS2 engineering agent. Use only the dedicated current worktree. "
        "Treat the following JSON as the approved task packet. Only edit the listed paths with the edit tool. "
        "Use the exact fixed test command as the only shell command after edits. Return structured output "
        "with the task ID, summary, and relative artifact paths. Do not access credentials or external services.\n"
        + canonical({
            "task": payload, "base_commit": task.base_commit, "artifact_paths": task.artifact_paths,
            "fixed_test": shlex.join(task.test_argv),
        })
    )
    return prompt, _schema({
        "task_id": {"type": "string"}, "summary": {"type": "string"},
        "artifact_refs": {"type": "array", "items": {"type": "string"}},
    })


def _read_final(text, limit):
    if type(text) is not str or len(text.encode()) > limit:
        raise ValueError("agent output is absent or exceeds its binding")
    return json.loads(text, object_pairs_hook=_unique)


def _unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError("duplicate agent response field")
        result[key] = value
    return result


def _accept(job, binding, task, workspace, final_text, exit_code, error, evidence, *, git=_git):
    if error:
        return Outcome("failed", error=error)
    if exit_code != 0 or evidence["terminal"] != ["turn.completed"]:
        reason = evidence["diagnostics"][0] if evidence["diagnostics"] else "agent_incomplete"
        return Outcome("failed", error=reason)
    if not any(event["type"] == "thread.started" and "session_id" in event for event in evidence["events"]):
        raise ValueError("missing session")
    value = _read_final(final_text, binding.max_output_bytes)
    if task is None:
        if evidence["commands"] or any(event.get("item_type") == "file_change" for event in evidence["events"]):
            raise ValueError("research role used engineering tools")
        if type(value) is not dict:
            raise ValueError("invalid research decision")
        for field in ("experiment_spec", "engineering_task"):
            if field in value and value[field] is None:
                del value[field]
        return Outcome("succeeded", output=canonical(validate_decision(canonical(value), job.payload)))
    if type(value) is not dict or set(value) != {"task_id", "summary", "artifact_refs"}:
        raise ValueError("invalid SOL result")
    if value["task_id"] != job.payload["task_id"] or value["artifact_refs"] != list(task.artifact_paths):
        raise ValueError("SOL result does not match the approved task")
    bounded_text(value["summary"], 8000)
    if (git(workspace, "rev-parse", "HEAD").strip().decode() != task.base_commit
            or _changes(workspace, git) != set(task.artifact_paths)):
        raise ValueError("SOL worktree differs from the approved artifact set")
    if evidence["commands"] != [{"argv_hash": digest(task.test_argv), "exit_code": 0}]:
        raise ValueError("SOL did not complete the single approved test")
    hashes = {}
    for relative in task.artifact_paths:
        artifact = workspace / relative
        if artifact.resolve() != artifact or not artifact.is_file():
            raise ValueError("SOL artifact is missing or not a regular workspace file")
        with artifact.open("rb") as handle:
            contents = handle.read(ARTIFACT_LIMIT + 1)
        if len(contents) > ARTIFACT_LIMIT:
            raise ValueError("SOL artifact too large")
        hashes[relative] = hashlib.sha256(contents).hexdigest()
    evidence["artifacts"] = hashes
    value["artifact_refs"] = [f"sha256:{value}" for value in hashes.values()]
    return Outcome("succeeded", output=value)


# The transient service starts only this trusted guard. The paid executable is
# gated on the runner's durable receipt matching systemd's invocation identity.
_SYSTEMD_GUARD = """
import json, os, subprocess, sys, time
path, cutoff = sys.argv[1], float(sys.argv[2])
invocation = os.environ.get("INVOCATION_ID")
def own_record():
    try:
        with open(path) as handle:
            record = json.load(handle)
    except (OSError, ValueError):
        return {}
    if (invocation and record.get("domain", {}).get("invocation_id") == invocation
        and record.get("process", {}).get("pid") == os.getpid()):
        return record
    return {}
while time.monotonic() < cutoff:
    if own_record().get("phase") == "launching":
        if time.monotonic() >= cutoff:
            break
        child = subprocess.Popen(sys.argv[3:])
        while child.poll() is None:
            remaining = cutoff - time.monotonic()
            if remaining <= 0 or own_record().get("phase") == "cancelling":
                sys.exit(124)
            time.sleep(min(0.02, remaining))
        sys.exit(child.returncode if child.returncode >= 0 else 128 - child.returncode)
    time.sleep(0.02)
sys.exit(124)
"""


class SystemdExecution:
    """Linux cgroup v2 transient user units, with no process-group fallback.

    A dedicated, verified user manager must expose the bound cgroup control
    metadata. The guard handles cancellation under systemd's verified
    control-group policy if the manager does not delegate cgroup.kill.
    """

    SYSTEMD_RUN = "/usr/bin/systemd-run"
    SYSTEMCTL = "/usr/bin/systemctl"
    CGROUP = Path("/sys/fs/cgroup")
    CPU_PERCENT = 100
    MEMORY = 512 * 1024 * 1024
    TASKS = 32
    MANAGER_OPTIONS = ("--user",)
    EXTRA_PROPERTIES = {}

    @classmethod
    def _unit(cls, folder):
        return f"sts2-agent-{folder.name}.service"

    @staticmethod
    def _manager_sockets():
        run = f"/run/user/{os.geteuid()}"
        return {f"{run}/bus", f"{run}/systemd/private", "/run/dbus/system_bus_socket"}

    @classmethod
    def _command(cls, unit, workspace, runner_state, deadline, argv):
        runtime = max(0.001, deadline - time.monotonic())
        properties = {
            "Type": "exec", "Restart": "no", "KillMode": "control-group",
            "KillSignal": "SIGKILL", "SendSIGKILL": "yes", "RuntimeMaxSec": f"{runtime:.6f}s",
            "TimeoutStopSec": "2s", "CPUQuota": f"{cls.CPU_PERCENT}%", "MemoryMax": str(cls.MEMORY),
            "MemorySwapMax": "0", "TasksMax": str(cls.TASKS), "ProtectControlGroups": "yes", "Delegate": "no",
            "NoNewPrivileges": "yes",
            "InaccessiblePaths": " ".join(f"-{path}" for path in sorted(cls._manager_sockets())),
            **cls.EXTRA_PROPERTIES,
        }
        return [cls.SYSTEMD_RUN, *cls.MANAGER_OPTIONS, "--quiet", "--pipe", "--wait", "--collect",
                "--expand-environment=no", f"--unit={unit}", f"--working-directory={workspace}",
                *(f"--property={key}={value}" for key, value in properties.items()), "--",
                sys.executable, "-I", "-c", _SYSTEMD_GUARD, str(runner_state), str(deadline), *argv]

    @classmethod
    def _show(cls, unit, timeout):
        props = ("Id", "InvocationID", "MainPID", "ControlGroup", "ActiveState", "KillMode",
                 "KillSignal", "SendSIGKILL", "RuntimeMaxUSec", "TimeoutStopUSec", "Restart",
                 "ProtectControlGroups", "Delegate", "NoNewPrivileges", "InaccessiblePaths", *cls.EXTRA_PROPERTIES)
        env = {**os.environ, "LC_ALL": "C"}
        result = subprocess.run(
            [cls.SYSTEMCTL, *cls.MANAGER_OPTIONS, "show", *(f"--property={p}" for p in props), "--", unit],
            check=False, capture_output=True, timeout=timeout, env=env,
        )
        return dict(line.split("=", 1) for line in result.stdout.decode().splitlines() if "=" in line)

    @classmethod
    def _directory(cls, group, unit):
        path = PurePosixPath(group)
        if (not path.is_absolute() or ".." in path.parts or path.name != unit or str(path) != group):
            raise ValueError("unexpected systemd cgroup path")
        directory = cls.CGROUP.joinpath(*path.parts[1:])
        if directory.resolve() != directory:
            raise ValueError("symlink in cgroup path")
        return os.open(directory, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)

    @classmethod
    def _receipt(cls, props, unit):
        invocation = props.get("InvocationID", "")
        if (props.get("Id") != unit or not re.fullmatch(r"[a-f0-9]{32}", invocation)
                or invocation == "0" * 32 or props.get("ActiveState") != "active"
                or props.get("KillMode") != "control-group" or props.get("SendSIGKILL") != "yes"
                or props.get("KillSignal") not in {"9", "SIGKILL", "KILL"}
                or props.get("RuntimeMaxUSec") in {None, "", "infinity", "0"}
                or props.get("TimeoutStopUSec") != "2s" or props.get("Restart") != "no"
                or props.get("ProtectControlGroups") != "yes" or props.get("Delegate") != "no"
                or props.get("NoNewPrivileges") != "yes"
                or not cls._manager_sockets() <= {value.lstrip("-") for value in props.get("InaccessiblePaths", "").split()}):
            raise ValueError("unverified systemd control-group properties")
        pid = int(props["MainPID"])
        identity = linux_process_identity(pid)
        group = props["ControlGroup"]
        memberships = cls._memberships(pid)
        if identity is None or f"0::{group}" not in memberships:
            raise ValueError("unit main process is outside its control group")
        directory = cls._directory(group, unit)
        try:
            cpu, period = map(int, _cgroup_read(directory, "cpu.max").split())
            if (cpu <= 0 or period <= 0 or cpu * 100 > period * cls.CPU_PERCENT
                    or not 0 < int(_cgroup_read(directory, "memory.max")) <= cls.MEMORY
                    or _cgroup_read(directory, "memory.swap.max").strip() != "0"
                    or not 0 < int(_cgroup_read(directory, "pids.max")) <= cls.TASKS):
                raise ValueError("unit cgroup resource limits were not applied")
            metadata = os.fstat(directory)
            receipt = {"unit": unit, "invocation_id": invocation, "control_group": group,
                       "device": metadata.st_dev, "inode": metadata.st_ino}
            killer = _cgroup_killer(directory)
        except BaseException:
            os.close(directory)
            raise
        return identity, receipt, directory, killer

    @staticmethod
    def _memberships(pid):
        return Path(f"/proc/{pid}/cgroup").read_text().splitlines()

    def recover(self, folder, record):
        saved = record.get("domain")
        identity = record.get("process")
        if not saved or not identity:
            return True  # No durable gate was opened.
        unit = self._unit(folder)
        if unit != saved.get("unit"):
            return False
        try:
            boot_id = Path("/proc/sys/kernel/random/boot_id").read_text().strip()
        except OSError:
            return False
        if boot_id != identity.get("boot_id"):
            return True
        try:
            directory = self._directory(saved["control_group"], unit)
        except FileNotFoundError:
            return True
        except (OSError, ValueError, KeyError):
            return False
        try:
            metadata = os.fstat(directory)
            if (metadata.st_dev, metadata.st_ino) != (saved["device"], saved["inode"]):
                return True  # The original cgroup was removed.
            if not _cgroup_populated(directory):
                return True
            props = self._show(unit, 1)
            if props.get("InvocationID") != saved["invocation_id"]:
                return False
            killer = _cgroup_killer(directory)
            try:
                return _cancel_cgroup(folder, record, directory, killer)
            finally:
                if killer is not None:
                    os.close(killer)
        except (OSError, ValueError, KeyError, subprocess.SubprocessError):
            return False
        finally:
            os.close(directory)

    def execute(self, argv, workspace, prompt, deadline, folder, record, *, pipe_limit=PIPE_LIMIT):
        evidence = _evidence()
        if (sys.platform != "linux" or not (self.CGROUP / "cgroup.controllers").is_file()
                or not os.access(self.SYSTEMD_RUN, os.X_OK) or not os.access(self.SYSTEMCTL, os.X_OK)):
            return None, "unverified_kill_domain", evidence
        unit = self._unit(folder)
        proc = None
        directory = killer = None
        error = exit_code = None
        try:
            with tempfile.TemporaryFile() as input_file:
                input_file.write(prompt.encode())
                input_file.seek(0)
                proc = subprocess.Popen(
                    self._command(unit, workspace, folder / "state.json", deadline, argv),
                    cwd=workspace, stdin=input_file, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                )
                admission_deadline = min(deadline, time.monotonic() + 5)
                while (left := admission_deadline - time.monotonic()) > 0 and proc.poll() is None:
                    props = self._show(unit, left)
                    if props.get("ActiveState") == "active" and props.get("MainPID") not in {None, "0"}:
                        break
                    time.sleep(min(0.02, max(0, admission_deadline - time.monotonic())))
                else:
                    raise ValueError("systemd did not report a live guard")
                identity, domain, directory, killer = self._receipt(props, unit)
                record.update(phase="launching", process=asdict(identity), domain=domain)
                # This fsync is the guard's only launch signal. The service is
                # pinned by the file descriptor before model work is possible.
                _save(folder, "state.json", record)
                error = _capture(proc, deadline, evidence, lambda sid: _record_session(folder, record, sid), pipe_limit)
                if not error:
                    try:
                        exit_code = proc.wait(timeout=max(0, min(0.5, deadline - time.monotonic())))
                    except subprocess.TimeoutExpired:
                        error = "deadline"
        except (OSError, ValueError, KeyError, subprocess.SubprocessError):
            error = "agent_execution_uncertain" if record["phase"] == "launching" else "unverified_kill_domain"
        finally:
            if directory is not None:
                try:
                    if _cgroup_populated(directory):
                        if not _cancel_cgroup(folder, record, directory, killer):
                            error = "containment_unresolved"
                        elif not error:
                            error = "lingering_control_group"
                except OSError:
                    error = "containment_unresolved"
                finally:
                    if killer is not None:
                        os.close(killer)
                    os.close(directory)
            if proc is not None:
                if proc.poll() is None:
                    proc.kill()  # This is only our systemd-run client child.
                    proc.wait(timeout=2)
                proc.stdout.close()
                proc.stderr.close()
        return exit_code, error, evidence


class SystemServiceExecution(SystemdExecution):
    """A root-owned project guard launches a fixed privilege-dropping helper."""

    MANAGER_OPTIONS = ("--system",)
    WORKER_UID = 1001
    WORKER_GID = 1002
    PROJECT_GROUP = 985
    TASKS = 64  # Raven's pinned Codex needs up to 59 with the verified one-core affinity.
    STATE = Path("/var/lib/sts2-modal-member-runner/agent-state")
    WORKSPACES = Path("/var/lib/sts2-modal-member-runner/workspaces")
    EXTRA_PROPERTIES = {"CapabilityBoundingSet": "CAP_SETUID CAP_SETGID CAP_SETPCAP",
                        "AllowedCPUs": "0", "CPUAffinity": "0"}

    def __init__(self, uid, gid, group):
        if (os.geteuid() != 0 or (uid, gid, group) != (self.WORKER_UID, self.WORKER_GID, self.PROJECT_GROUP)):
            raise ValueError("system member identity differs from the approved Raven binding")

    @classmethod
    def _manager_sockets(cls):
        run = f"/run/user/{cls.WORKER_UID}"
        return super()._manager_sockets() | {"/run/systemd/private", "/run/docker.sock", f"{run}/bus", f"{run}/systemd/private"}

    @classmethod
    def _receipt(cls, props, unit):
        if (set(props.get("CapabilityBoundingSet", "").upper().split())
                != set(cls.EXTRA_PROPERTIES["CapabilityBoundingSet"].split())
                or props.get("AllowedCPUs") != cls.EXTRA_PROPERTIES["AllowedCPUs"]
                or props.get("CPUAffinity") != cls.EXTRA_PROPERTIES["CPUAffinity"]):
            raise ValueError("unexpected system-agent CPU affinity or capabilities")
        return super()._receipt(props, unit)

    @classmethod
    def _directory(cls, group, unit):
        if group != f"/system.slice/{unit}":
            raise ValueError("system agent must run in its dedicated system.slice unit")
        return super()._directory(group, unit)

    @classmethod
    def workspace_root(cls, path):
        path = Path(path).absolute()
        metadata = path.stat()
        if (path != cls.WORKSPACES or path != path.resolve() or not stat.S_ISDIR(metadata.st_mode) or metadata.st_uid != 0
                or metadata.st_gid != cls.PROJECT_GROUP or stat.S_IMODE(metadata.st_mode) != 0o750):
            raise ValueError("system agent workspace anchor must be root:project 0750")
        return path

    @classmethod
    def git(cls, workspace, *args):
        # Even a hostile working tree never asks a root Git process to read
        # its config. Drop every unrelated group and capability before Git.
        return subprocess.run(
            ["/usr/bin/setpriv", "--reuid", str(cls.WORKER_UID), "--regid", str(cls.WORKER_GID),
             "--groups", str(cls.PROJECT_GROUP), "--no-new-privs", "--inh-caps=-all",
             "--ambient-caps=-all", "--bounding-set=-all", "--", "/usr/bin/git",
             "-c", "core.fsmonitor=false", "-c", "core.hooksPath=/dev/null", "-C", str(workspace), *args],
            check=True, capture_output=True, timeout=3,
            env={"PATH": "/usr/bin:/bin", "HOME": "/nonexistent", "GIT_CONFIG_NOSYSTEM": "1",
                 "GIT_CONFIG_GLOBAL": "/dev/null", "GIT_OPTIONAL_LOCKS": "0"},
        ).stdout


def _evidence():
    return {"events": [], "diagnostics": [], "usage": None, "terminal": [], "commands": [], "bytes": 0}


def _cgroup_read(directory, filename):
    fd = os.open(filename, os.O_RDONLY | os.O_NOFOLLOW, dir_fd=directory)
    try:
        return os.read(fd, 512).decode()
    finally:
        os.close(fd)


def _cgroup_populated(directory):
    try:
        events = _cgroup_read(directory, "cgroup.events").splitlines()
    except FileNotFoundError:
        return False  # A removed cgroup has no member processes.
    return "populated 0" not in events


def _cgroup_killer(directory):
    try:
        return os.open("cgroup.kill", os.O_WRONLY | os.O_NOFOLLOW, dir_fd=directory)
    except PermissionError:
        # systemd user delegation need not grant cgroup.kill writes. The guard
        # exits on its matching cancellation record, so the manager kills its
        # entire verified control group, including detached descendants.
        return None


def _cancel_cgroup(folder, record, directory, killer):
    record["phase"] = "cancelling"
    _save(folder, "state.json", record)
    if killer is not None:
        _kill_cgroup(killer)
    return _wait_cgroup_empty(directory)


def _wait_cgroup_empty(directory):
    cutoff = time.monotonic() + 2
    while _cgroup_populated(directory):
        remaining = cutoff - time.monotonic()
        if remaining <= 0:
            return False
        time.sleep(min(0.02, remaining))
    return True


def _kill_cgroup(killer):
    try:
        os.write(killer, b"1")
    except OSError:
        # Kernel removal races with a natural exit; the unit's KillMode and
        # bounded lifetime remain the independent fallback.
        pass
