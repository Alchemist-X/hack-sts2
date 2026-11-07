#!/usr/bin/env python3
"""line_agent.py - one agent per Colab runtime: runs the lines of its queue through w3m_line_v2.sh (audit OPS2).

Replaces the ': done' string protocol and the hand-written launch_*.sh wrappers. The control plane lives in a store
(the GCS bucket when deployed, a local directory in tests):

  ops/lines/<line>.json      line spec (schema: specs/README.md; `line_agent.py validate` checks one)
  ops/queues/<session>.json  {"session": S, "lines": ["c4", "s2", ...]}   the runtime's queue, in priority order
  ops/agents/<session>.json  agent heartbeat {state: busy|drained|blocked, reason, active_line, queue, heartbeat_at, ...}
  runs/<run>/line_status.json  {state, reason, update, segment_end, next_action, heartbeat_at, session, ...}
  runs/<run>/STOP            (written by the orchestrator) graceful stop: the driver exits before its next session
  runs/<run>/KILL            (written by the orchestrator) immediate stop: SIGTERM to the driver's process group

Every tick (--interval, default 60 s) the agent re-reads its queue, advances the active line, and writes the active
line's status and its own heartbeat. With no active line it takes the FIRST queued line whose dependencies (`after`)
are satisfied, reading each dependency's runs/<run>/line_status.json from the store. A line is prepared (preflight
paths, bundle wait, restore of the run dir from the store with an md5 check of status/metrics/evals, fork checkpoint
local or fetched from the store once its size is stable) and then driven by w3m_line_v2.sh in its own process group
(rc 0 done, 3 killed, anything else failed). on_fail "next" moves on; on_fail "hold" blocks the queue (heartbeat
'blocked') until the failed line is removed from the queue. The heartbeat is 'drained' only when every queued line
is terminal (done|failed|killed); keepalive_v3.sh stops a runtime after two fresh 'drained' heartbeats.

Line states: waiting | forking | training | evaluating | pairing | done | failed | killed | held
('pairing' is reserved for DECIDE; 'held' = needs a human, e.g. an incomplete restore; it blocks the queue).

Restart-safe: one agent per session (fcntl lock in --work); a driver that survives an agent restart is adopted through
runs/<run>.driver.pid and its exit code file. Stdlib only; run with python3 -I.

Subcommands:  run | validate SPEC... | plan --line L | publish SPEC... | queue --session S LINE... | status --session S
"""
import argparse
import base64
import fcntl
import hashlib
import json
import os
import re
import shutil
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path

VERSION = "ops2-v1"
HERE = Path(__file__).resolve().parent
TERMINAL = ("done", "failed", "killed")
STATES = ("waiting", "forking", "training", "evaluating", "pairing", "done", "failed", "killed", "held")
NAME_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9_.-]*$")
ENV_REQUIRED = ("TAG", "ENT0", "ENT1", "MACROS", "FORK_U", "LAST", "ENDS", "OVERLAP", "letter")
ENV_OPTIONAL = ("EXTRA", "WAIT_RUN", "WAIT_MAX_S", "DEV", "AB", "DEV512")
EVAL_PLANS = ("legacy", "legacy+dev512", "dev512")
SPEC_KEYS = {"line", "run", "bundle", "fork", "env", "after", "control", "eval_plan", "kill", "pair_rule",
             "on_fail", "replicas", "preflight", "bundle_wait_s", "note", "hypothesis"}
SMALL3 = ("status.json", "metrics.jsonl", "evals.jsonl")


def now_iso(t=None):
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(t if t is not None else time.time()))


def log(msg):
    print(f"{time.strftime('%F %T', time.gmtime())} line_agent: {msg}", flush=True)


def md5_file(p):
    h = hashlib.md5()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


# ------------------------------------------------------------------------------------------------------- stores
class LocalStore:
    """A directory standing in for the bucket (tests, dry runs). Keys are '/'-separated paths under root."""

    def __init__(self, root):
        self.root = Path(root)
        self.url = f"dir:{self.root}"

    def _p(self, key):
        if key.startswith("/") or ".." in key.split("/"):
            raise ValueError(f"bad store key {key!r}")
        return self.root / key

    def get(self, key):
        try:
            return self._p(key).read_bytes()
        except FileNotFoundError:
            return None

    def put(self, key, data):
        p = self._p(key)
        p.parent.mkdir(parents=True, exist_ok=True)
        tmp = p.with_name(p.name + f".tmp{os.getpid()}")
        tmp.write_bytes(data)
        os.replace(tmp, p)

    def size(self, key):
        try:
            return self._p(key).stat().st_size
        except FileNotFoundError:
            return None

    def md5(self, key):
        p = self._p(key)
        return md5_file(p) if p.is_file() else None

    def download(self, key, dest):
        p = self._p(key)
        if not p.is_file():
            return False
        dest = Path(dest)
        dest.parent.mkdir(parents=True, exist_ok=True)
        tmp = dest.with_name(dest.name + ".part")
        shutil.copyfile(p, tmp)
        os.replace(tmp, dest)
        return True

    def list(self, prefix):
        base = self._p(prefix.rstrip("/"))
        if not base.is_dir():
            return []
        return sorted(str(f.relative_to(self.root)) for f in base.rglob("*") if f.is_file())

    def restore_run(self, run, dest):
        """gcs_pull.py --newest-ckpt-only --skip-existing semantics: every object, but only the newest ckpt-*.pt."""
        keys = self.list(f"runs/{run}")
        ck = [k for k in keys if re.search(r"/checkpoints/ckpt-\d+\.pt$", k)]
        newest = max(ck, key=lambda k: int(re.search(r"ckpt-(\d+)\.pt$", k).group(1))) if ck else None
        for k in keys:
            if k in ck and k != newest:
                continue
            rel = k[len(f"runs/{run}/"):]
            if rel in ("line_status.json", "STOP", "KILL", "line_driver.log"):
                continue
            p = Path(dest) / rel
            if p.is_file() and p.stat().st_size == self.size(k):
                continue
            self.download(k, p)
        return True


# Runs in a separate, non-isolated python that has google-cloud-storage (the Colab image does); the agent itself
# stays stdlib-only. argv: KEY BUCKET OP ARGS...   exit 3 = object missing.
GCS_HELPER = r"""
import base64, os, sys
from google.cloud import storage
key, bucket, op = sys.argv[1:4]; args = sys.argv[4:]
b = storage.Client.from_service_account_json(key).bucket(bucket)
if op == "get":
    x = b.get_blob(args[0])
    if x is None: sys.exit(3)
    sys.stdout.buffer.write(x.download_as_bytes())
elif op == "put":
    b.blob(args[0]).upload_from_filename(args[1], timeout=600)
elif op == "size":
    x = b.get_blob(args[0])
    if x is None: sys.exit(3)
    print(x.size)
elif op == "md5":
    x = b.get_blob(args[0])
    if x is None: sys.exit(3)
    print(base64.b64decode(x.md5_hash).hex())
elif op == "download":
    x = b.get_blob(args[0])
    if x is None: sys.exit(3)
    tmp = args[1] + ".part"; x.download_to_filename(tmp); os.replace(tmp, args[1])
elif op == "list":
    for x in b.client.list_blobs(bucket, prefix=args[0]): print(x.name)
else:
    sys.exit(2)
"""


class GcsStore:
    """The bucket, through subprocesses: GCS_HELPER (google-cloud-storage + the service-account key, as gcs_sync.py
    does) for objects, and the existing scripts/colab/ops/gcs_pull.py for run-dir restores."""

    def __init__(self, bucket, key, python="python3", gcs_pull=None, timeout=900):
        self.bucket, self.key, self.python, self.timeout = bucket, key, python, timeout
        self.gcs_pull = gcs_pull
        self.url = f"gs://{bucket}"

    def _run(self, op, *args, binary=False):
        r = subprocess.run([self.python, "-c", GCS_HELPER, self.key, self.bucket, op, *args],
                           capture_output=True, timeout=self.timeout, stdin=subprocess.DEVNULL)
        if r.returncode == 3:
            return None
        if r.returncode != 0:
            raise RuntimeError(f"gcs {op} {args[:1]} rc {r.returncode}: {r.stderr.decode(errors='replace')[-300:]}")
        return r.stdout if binary else r.stdout.decode()

    def get(self, key):
        return self._run("get", key, binary=True)

    def put(self, key, data):
        tmp = Path(os.environ.get("TMPDIR", "/tmp")) / f"line_agent.{os.getpid()}.put"
        tmp.write_bytes(data)
        try:
            self._run("put", key, str(tmp))
        finally:
            tmp.unlink(missing_ok=True)

    def size(self, key):
        out = self._run("size", key)
        return None if out is None else int(out.strip())

    def md5(self, key):
        out = self._run("md5", key)
        return None if out is None else out.strip()

    def download(self, key, dest):
        Path(dest).parent.mkdir(parents=True, exist_ok=True)
        return self._run("download", key, str(dest)) is not None

    def list(self, prefix):
        out = self._run("list", prefix.rstrip("/") + "/")
        return [] if not out else [l for l in out.splitlines() if l.strip()]

    def restore_run(self, run, dest):
        if not self.gcs_pull:
            raise RuntimeError("gcs store: --gcs-pull (path of scripts/colab/ops/gcs_pull.py) is required for restores")
        r = subprocess.run([self.python, self.gcs_pull, "--key", self.key, "--bucket", self.bucket,
                            "--prefix", f"runs/{run}", "--dest", str(dest), "--newest-ckpt-only", "--skip-existing"],
                           capture_output=True, timeout=3600, stdin=subprocess.DEVNULL)
        if r.returncode != 0:
            raise RuntimeError(f"gcs_pull runs/{run} rc {r.returncode}: {r.stderr.decode(errors='replace')[-300:]}")
        return True


def make_store(url, key=None, python="python3", gcs_pull=None):
    if url.startswith("gs://"):
        if not key:
            raise SystemExit("--key is required for a gs:// store")
        return GcsStore(url[5:].strip("/"), key, python=python, gcs_pull=gcs_pull)
    return LocalStore(url[4:] if url.startswith("dir:") else url)


def get_json(store, key):
    raw = store.get(key)
    if raw is None:
        return None
    return json.loads(raw.decode())


def put_json(store, key, obj):
    store.put(key, (json.dumps(obj, indent=1, sort_keys=True) + "\n").encode())


# --------------------------------------------------------------------------------------------------------- specs
def validate_spec(d):
    """Errors (list of str) for a line spec dict; [] = valid."""
    err = []
    if not isinstance(d, dict):
        return ["spec is not a JSON object"]
    for k in sorted(set(d) - SPEC_KEYS):
        err.append(f"unknown key {k!r}")
    for k in ("line", "run"):
        if not isinstance(d.get(k), str) or not NAME_RE.match(d.get(k) or ""):
            err.append(f"{k} must be a name matching {NAME_RE.pattern}")
    if not isinstance(d.get("bundle"), str) or not d.get("bundle"):
        err.append("bundle must be a path (absolute, or relative to the agent's --work)")
    fk = d.get("fork")
    if not isinstance(fk, dict):
        err.append("fork must be an object {run, update, from}")
        fk = {}
    else:
        if not isinstance(fk.get("run"), str) or not NAME_RE.match(fk.get("run") or ""):
            err.append("fork.run must be a run name")
        if not isinstance(fk.get("update"), int) or fk.get("update") < 0:
            err.append("fork.update must be a non-negative integer")
        if fk.get("from") not in ("local", "bucket"):
            err.append("fork.from must be 'local' or 'bucket'")
        for k in sorted(set(fk) - {"run", "update", "from", "wait_s"}):
            err.append(f"unknown fork key {k!r}")
    env = d.get("env")
    if not isinstance(env, dict):
        err.append("env must be an object")
        env = {}
    for k in ENV_REQUIRED:
        if k not in env:
            err.append(f"env.{k} is required")
    for k in sorted(set(env) - set(ENV_REQUIRED) - set(ENV_OPTIONAL)):
        err.append(f"unknown env key {k!r} (allowed: {', '.join(ENV_REQUIRED + ENV_OPTIONAL)})")
    for k, v in env.items():
        if not isinstance(v, (str, int, float)) or isinstance(v, bool):
            err.append(f"env.{k} must be a string or a number")
    if env.get("letter") not in (None, "a", "b"):
        err.append("env.letter must be 'a' or 'b'")
    if env.get("OVERLAP") not in (None, "off", "double"):
        err.append("env.OVERLAP must be 'off' or 'double'")
    if env.get("letter") == "b" and env.get("OVERLAP") not in (None, "off"):
        err.append("letter b (harvest) requires OVERLAP off")
    try:
        ends = [int(x) for x in str(env.get("ENDS", "")).split()]
        fu, last = int(env.get("FORK_U", -1)), int(env.get("LAST", -1))
        if not ends:
            err.append("env.ENDS must list at least one segment end")
        elif ends != sorted(set(ends)) or ends[0] <= fu:
            err.append("env.ENDS must be strictly increasing and above FORK_U")
        elif ends[-1] != last:
            err.append("env.LAST must equal the last of env.ENDS")
        if isinstance(fk.get("update"), int) and "FORK_U" in env and fu != fk["update"]:
            err.append("env.FORK_U must equal fork.update")
    except (TypeError, ValueError):
        err.append("env.ENDS / FORK_U / LAST must be integers")
    after = d.get("after", [])
    if not isinstance(after, list):
        err.append("after must be a list")
        after = []
    for i, a in enumerate(after):
        if not isinstance(a, dict) or (("line" in a) == ("run" in a)):
            err.append(f"after[{i}] must name exactly one of line / run")
            continue
        need = a.get("need")
        if need not in ("done", "terminal", "update"):
            err.append(f"after[{i}].need must be done, terminal or update")
        if need == "update" and not isinstance(a.get("update"), int):
            err.append(f"after[{i}]: need 'update' requires an integer update")
        if "file" in a and (need != "update" or not isinstance(a["file"], str) or ".." in a["file"].split("/")):
            err.append(f"after[{i}].file (a path under runs/<run>/) only goes with need 'update'")
        for k in sorted(set(a) - {"line", "run", "need", "update", "file"}):
            err.append(f"unknown after[{i}] key {k!r}")
        if a.get("line") == d.get("line"):
            err.append(f"after[{i}]: a line cannot depend on itself")
    ctl = d.get("control")
    if ctl is not None and (not isinstance(ctl, dict) or not isinstance(ctl.get("run"), str)):
        err.append("control must be null or {run: <run name>}")
    if d.get("eval_plan", "legacy") not in EVAL_PLANS:
        err.append(f"eval_plan must be one of {', '.join(EVAL_PLANS)}")
    if not isinstance(d.get("kill", []), list):
        err.append("kill must be a list (GUARD rules; carried, not evaluated by the agent)")
    if not isinstance(d.get("pair_rule", {}), dict):
        err.append("pair_rule must be an object (DECIDE; carried, not evaluated by the agent)")
    if d.get("on_fail", "next") not in ("next", "hold"):
        err.append("on_fail must be 'next' or 'hold'")
    rep = d.get("replicas", 1)
    if not isinstance(rep, int) or rep < 1:
        err.append("replicas must be an integer >= 1")
    elif rep > 1:
        err.append("replicas > 1 is not implemented by the agent: queue each replica as its own line")
    pf = d.get("preflight", {})
    if not isinstance(pf, dict) or not isinstance(pf.get("paths", []), list):
        err.append("preflight must be {paths: [...]}")
    return err


def driver_env(spec, work, sync, base_env=None):
    """The environment w3m_line_v2.sh gets for this spec (on top of base_env)."""
    env = dict(os.environ if base_env is None else base_env)
    for k in ENV_REQUIRED + ENV_OPTIONAL + ("EVALS",):   # nothing line-specific leaks in from the agent's own env
        env.pop(k, None)
    for k, v in spec["env"].items():
        if k in ("letter", "OVERLAP"):
            continue
        env[k] = str(v)
    env.setdefault("EXTRA", "")
    env["STS"] = str(work)
    env["SYNC"] = "1" if sync else "0"
    env["EVAL_PLAN"] = spec.get("eval_plan", "legacy")
    if spec["fork"]["from"] == "local" and "WAIT_RUN" not in spec["env"]:
        env["WAIT_RUN"] = str(Path(work) / "runs" / spec["fork"]["run"])
    return env


def fork_local_path(spec, work):
    f = spec["fork"]
    if f["from"] == "local":
        return Path(work) / "runs" / f["run"] / "checkpoints" / ("pinned-%07d.pt" % f["update"])
    return Path(work) / "fork" / ("pinned-%07d-%s.pt" % (f["update"], f["run"]))


def fork_store_key(spec):
    f = spec["fork"]
    return "runs/%s/checkpoints/pinned-%07d.pt" % (f["run"], f["update"])


def resolve_bundle(spec, work):
    b = Path(spec["bundle"])
    if not b.is_absolute():
        b = Path(work) / b
    return Path(os.path.realpath(b))


def driver_argv(spec, work, driver, fork_path):
    return ["bash", str(driver), str(resolve_bundle(spec, work)), str(Path(work) / "runs" / spec["run"]),
            spec["env"]["letter"], str(fork_path), spec["env"]["OVERLAP"]]


# -------------------------------------------------------------------------------------------------------- agent
def pid_alive(pid):
    try:
        os.kill(pid, 0)
        return True
    except ProcessLookupError:
        return False
    except PermissionError:
        return True


def pid_cmdline(pid):
    try:
        return Path(f"/proc/{pid}/cmdline").read_bytes().replace(b"\0", b" ").decode(errors="replace")
    except OSError:
        r = subprocess.run(["ps", "-o", "command=", "-p", str(pid)], capture_output=True, text=True)
        return r.stdout.strip()


class LineRun:
    """One line from preparation to a terminal state; step() is called every tick."""

    def __init__(self, agent, spec):
        self.a, self.spec = agent, spec
        self.line, self.run = spec["line"], spec["run"]
        self.run_dir = agent.work / "runs" / self.run
        self.log_path = agent.work / "runs" / f"{self.run}.driver.log"
        self.pid_path = agent.work / "runs" / f"{self.run}.driver.pid"
        self.rc_path = agent.work / "runs" / f"{self.run}.driver.rc"
        self.phase = "prepare"
        self.t0 = time.time()
        self.proc = None
        self.pid = None
        self.fork_size_seen = None
        self.restored = False
        self.kill_sent = None
        self.status = {"state": "forking", "reason": "preparing", "update": None, "segment_end": None,
                       "next_action": "prepare"}

    # -- helpers
    def set(self, state, reason, next_action=None, **kw):
        self.status.update(state=state, reason=reason, **kw)
        if next_action is not None:
            self.status["next_action"] = next_action

    def local_update(self):
        try:
            return int(json.loads((self.run_dir / "status.json").read_text()).get("update") or 0)
        except (OSError, ValueError):
            return None

    def last_log_lines(self, n=40):
        try:
            with open(self.log_path, "rb") as f:
                f.seek(0, 2)
                f.seek(max(0, f.tell() - 16384))
                return f.read().decode(errors="replace").splitlines()[-n:]
        except OSError:
            return []

    def fail(self, reason):
        self.set("failed", reason, next_action="next line" if self.spec.get("on_fail", "next") == "next" else "hold")
        self.phase = "finished"

    # -- preparation (one step per tick; never blocks on a wait)
    def prepare(self):
        a, spec = self.a, self.spec
        for p in (spec.get("preflight") or {}).get("paths", []):
            if not Path(p).exists():
                return self.fail(f"preflight: {p} missing")
        bundle = resolve_bundle(spec, a.work)
        if not (bundle / "run_train.sh").is_file():
            if time.time() - self.t0 > float(spec.get("bundle_wait_s", 43200)):
                return self.fail(f"bundle {spec['bundle']} not present after {spec.get('bundle_wait_s', 43200)} s")
            return self.set("waiting", f"waiting for bundle {spec['bundle']}", next_action="bundle")
        self.run_dir.mkdir(parents=True, exist_ok=True)
        if a.store.get(f"runs/{self.run}/STOP") is not None:
            (self.run_dir / "STOP").touch()
        # restore: a run the bucket knows about but this runtime does not (rotation / restart on a fresh VM)
        if not self.restored and not (self.run_dir / "status.json").exists() \
                and a.store.size(f"runs/{self.run}/status.json") is not None:
            self.set("forking", "restoring the run dir from the store", next_action="restore")
            a.write_status(self)
            ok, why = self.restore()
            if not ok:
                self.set("held", f"restore incomplete: {why}", next_action="human: check the run dir")
                self.phase = "finished"
                return
        self.restored = True
        have_ckpt = any((self.run_dir / "checkpoints").glob("ckpt-*.pt")) if (self.run_dir / "checkpoints").is_dir() else False
        fork = fork_local_path(spec, a.work)
        if not have_ckpt and not fork.is_file():
            key = fork_store_key(spec)
            size = a.store.size(key)
            if size and size == self.fork_size_seen:   # stable across two ticks (gcs_sync uploads >= 60 s old files)
                self.set("forking", f"fetching fork {key}", next_action="fetch fork")
                a.write_status(self)
                if not a.store.download(key, fork):
                    return self.fail(f"fork {key} download failed")
                log(f"{self.line}: fetched {key} -> {fork}")
            else:
                self.fork_size_seen = size
                wait_s = float(spec["fork"].get("wait_s", 21600))
                if time.time() - self.t0 > wait_s:
                    return self.fail(f"fork checkpoint {fork} / {key} not available after {wait_s:.0f} s")
                return self.set("forking", f"waiting for the fork checkpoint ({fork.name}; store size {size})",
                                next_action="fork")
        self.start_driver(fork)

    def restore(self):
        a = self.a
        why = "?"
        for attempt in (1, 2):
            try:
                a.store.restore_run(self.run, self.run_dir)
            except Exception as e:  # noqa: BLE001 - reported in the status
                why = f"restore attempt {attempt}: {e}"
                log(f"{self.line}: {why}")
                continue
            bad = []
            for name in SMALL3:
                remote = a.store.md5(f"runs/{self.run}/{name}")
                local = self.run_dir / name
                if remote is None:
                    if name != "evals.jsonl":
                        bad.append(f"{name} not in the store")
                    continue
                if not local.is_file() or md5_file(local) != remote:
                    bad.append(f"{name} md5 differs")
            if not bad:
                log(f"{self.line}: restored runs/{self.run} (update {self.local_update()}), small files verified")
                return True, ""
            why = "; ".join(bad)
            log(f"{self.line}: restore attempt {attempt}: {why}")
        return False, why

    def start_driver(self, fork):
        a, spec = self.a, self.spec
        self.rc_path.unlink(missing_ok=True)
        argv = driver_argv(spec, a.work, a.driver, fork)
        env = driver_env(spec, a.work, a.sync)
        wrap = 'rcf=$1; shift; "$@"; rc=$?; echo $rc > "$rcf.tmp" && mv "$rcf.tmp" "$rcf"; exit $rc'
        logf = open(self.log_path, "ab")
        logf.write(f"{now_iso()} line_agent {a.session}: start line {self.line}: {' '.join(argv)}\n".encode())
        logf.flush()
        self.proc = subprocess.Popen(["bash", "-c", wrap, "line_agent_driver", str(self.rc_path), *argv],
                                     stdout=logf, stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL,
                                     env=env, cwd=str(a.work), start_new_session=True)
        logf.close()
        self.pid = self.proc.pid
        self.pid_path.write_text(f"{self.pid} {now_iso()} {self.line}\n")
        self.phase = "driving"
        self.set("training", "driver started", next_action="train")
        log(f"{self.line}: driver pid {self.pid} ({' '.join(argv[1:])})")

    def adopt(self):
        """A driver from before an agent restart: True if adopted (still running, or finished with an rc file)."""
        try:
            pid = int(self.pid_path.read_text().split()[0])
        except (OSError, ValueError, IndexError):
            return False
        if pid_alive(pid) and "w3m_line_v2" in pid_cmdline(pid):
            self.pid, self.phase = pid, "driving"
            self.set("training", f"adopted running driver pid {pid}", next_action="train")
            log(f"{self.line}: adopted running driver pid {pid}")
            return True
        if self.rc_path.is_file() and self.rc_path.stat().st_mtime >= self.pid_path.stat().st_mtime:
            self.pid, self.phase = pid, "driving"
            log(f"{self.line}: driver pid {pid} finished while the agent was down")
            return True
        return False

    def driver_rc(self):
        if self.proc is not None:
            if self.proc.poll() is None:
                return None
        elif self.pid_alive_any():
            return None
        try:
            return int(self.rc_path.read_text().strip())
        except (OSError, ValueError):
            return self.proc.returncode if self.proc is not None else -1

    def pid_alive_any(self):
        return self.pid is not None and pid_alive(self.pid)

    def drive(self):
        a = self.a
        if a.store.get(f"runs/{self.run}/STOP") is not None and not (self.run_dir / "STOP").exists():
            (self.run_dir / "STOP").touch()
            log(f"{self.line}: STOP requested: the driver stops before its next session")
        if a.store.get(f"runs/{self.run}/KILL") is not None and self.kill_sent is None:
            self.kill_sent = time.time()
            self._signal(signal.SIGTERM)
            log(f"{self.line}: KILL requested: SIGTERM to process group {self.pid}")
        elif self.kill_sent is not None and time.time() - self.kill_sent > 60:
            self._signal(signal.SIGKILL)
        rc = self.driver_rc()
        upd = self.local_update()
        lines = self.last_log_lines()
        last = lines[-1] if lines else ""
        if rc is None:
            state, nxt, seg = "training", "train", self.status.get("segment_end")
            for ln in reversed(lines):
                m = re.search(r" train \S+ to update (\d+)", ln)
                if m:
                    seg = int(m.group(1))
                    break
            if re.search(r" eval -> |final gcs_sync|at update \d+$", last):
                state, nxt = "evaluating", "endpoint evals"
            elif re.search(r"waiting for ", last):
                state, nxt = "forking", "fork"
            elif seg:
                nxt = f"train to u{seg}"
            self.set(state, last[-200:] or "driver running", next_action=nxt, update=upd, segment_end=seg)
            return
        self.phase = "finished"
        self.pid_path.unlink(missing_ok=True)
        self.status["update"] = upd
        if self.kill_sent is not None:
            self.set("killed", f"KILL request (driver rc {rc})", next_action="next line")
        elif rc == 0:
            self.set("done", last[-200:] or "driver rc 0", next_action="next line")
        elif rc == 3:
            self.set("killed", f"STOP: {last[-200:]}", next_action="next line")
        else:
            why = {2: "bad driver arguments", 4: "endpoint reports incomplete"}.get(rc, "driver failed")
            self.fail(f"{why} (rc {rc}): {last[-200:]}")
        self.status["driver_rc"] = rc
        log(f"{self.line}: driver exited rc {rc} -> {self.status['state']}")

    def _signal(self, sig):
        try:
            os.killpg(self.pid, sig)
        except (ProcessLookupError, PermissionError, TypeError):
            pass

    def step(self):
        if self.phase == "prepare":
            self.prepare()
        elif self.phase == "driving":
            self.drive()


class Agent:
    def __init__(self, store, session, work, driver, interval=60.0, sync=True, stale_s=600.0):
        self.store, self.session, self.work = store, session, Path(work)
        self.driver, self.interval, self.sync, self.stale_s = Path(driver), interval, sync, stale_s
        self.active = None
        self.states = {}         # line -> last written status (this agent's view)
        self.specs = {}
        self.started_at = now_iso()
        self.hb = {"state": "blocked", "reason": "starting"}
        self.ticks_driving = 0

    # -- store access
    def spec(self, line):
        d = get_json(self.store, f"ops/lines/{line}.json")
        if d is not None:
            errs = validate_spec(d)
            if errs:
                raise ValueError(f"spec {line} invalid: {'; '.join(errs)}")
            if d["line"] != line:
                raise ValueError(f"ops/lines/{line}.json has line {d['line']!r}")
            self.specs[line] = d
        return d

    def remote_status(self, run):
        try:
            return get_json(self.store, f"runs/{run}/line_status.json")
        except (ValueError, UnicodeDecodeError):
            return None

    def write_status(self, lr_or_spec, state=None, reason=None, next_action=None):
        if isinstance(lr_or_spec, LineRun):
            spec, st = lr_or_spec.spec, dict(lr_or_spec.status)
        else:
            spec, st = lr_or_spec, {"state": state, "reason": reason, "next_action": next_action,
                                    "update": None, "segment_end": None}
        t = time.time()
        st.update(line=spec["line"], run=spec["run"], session=self.session, heartbeat_at=now_iso(t),
                  heartbeat_unix=round(t, 1), agent=VERSION, control=(spec.get("control") or {}).get("run"))
        try:
            put_json(self.store, f"runs/{spec['run']}/line_status.json", st)
            lp = self.work / "runs" / spec["run"]
            if lp.is_dir():
                (lp / "line_status.json").write_text(json.dumps(st, indent=1, sort_keys=True) + "\n")
        except Exception as e:  # noqa: BLE001 - a store hiccup must not kill the agent
            log(f"status write for {spec['line']} failed: {e}")
        self.states[spec["line"]] = st
        return st

    def upload_driver_log(self, lr):
        try:
            if lr.log_path.is_file():
                self.store.put(f"runs/{lr.run}/line_driver.log", lr.log_path.read_bytes()[-200000:])
        except Exception as e:  # noqa: BLE001
            log(f"driver log upload for {lr.line} failed: {e}")

    def write_heartbeat(self, state, reason, queue_view):
        t = time.time()
        self.hb = {"session": self.session, "state": state, "reason": reason,
                   "active_line": self.active.line if self.active else None, "queue": queue_view,
                   "heartbeat_at": now_iso(t), "heartbeat_unix": round(t, 1), "agent": VERSION,
                   "pid": os.getpid(), "host": socket.gethostname(), "started_at": self.started_at,
                   "interval_s": self.interval}
        try:
            put_json(self.store, f"ops/agents/{self.session}.json", self.hb)
        except Exception as e:  # noqa: BLE001
            log(f"heartbeat write failed: {e}")

    # -- dependency logic
    def dep_state(self, dep):
        """('ok'|'wait'|'never', text) for one `after` entry."""
        need = dep["need"]
        if "line" in dep:
            ds = self.specs.get(dep["line"]) or self.spec(dep["line"])
            if ds is None:
                return "wait", f"no spec for line {dep['line']}"
            run = ds["run"]
        else:
            run = dep["run"]
        if need == "update":   # a predecessor not run by an agent: the trainer's own status.json in the store
            st = get_json(self.store, f"runs/{run}/status.json") or {}
            u = int(st.get("update") or 0)
            if u >= dep["update"] and st.get("state") in ("finished", "stopped"):
                f = dep.get("file")
                if f and self.store.size(f"runs/{run}/{f}") is None:
                    return "wait", f"{run} at u{u} ({st.get('state')}); waiting for runs/{run}/{f}"
                return "ok", f"{run} at u{u} ({st.get('state')})"
            return "wait", f"{run} at u{u} ({st.get('state')}); need u{dep['update']} and the trainer exited"
        ls = self.remote_status(run) or {}
        s = ls.get("state")
        if s == "done" or (need == "terminal" and s in TERMINAL):
            return "ok", f"{run} {s}"
        if need == "done" and s in ("failed", "killed"):
            return "never", f"{run} ended {s} (need done)"
        return "wait", f"{run} is {s or 'unknown'} (need {need})"

    def owned_elsewhere(self, run):
        ls = self.remote_status(run) or {}
        if ls.get("session") and ls.get("session") != self.session and ls.get("state") not in TERMINAL:
            if time.time() - float(ls.get("heartbeat_unix") or 0) < self.stale_s:
                return ls.get("session")
        return None

    # -- one tick
    def tick(self):
        try:
            q = get_json(self.store, f"ops/queues/{self.session}.json")
        except (ValueError, UnicodeDecodeError) as e:
            q = None
            log(f"queue unreadable: {e}")
        if q is None:
            lines = None
        else:
            lines = [x if isinstance(x, str) else x.get("line") for x in q.get("lines", [])]
        # advance the active line
        if self.active is not None:
            lr = self.active
            lr.step()
            self.write_status(lr)
            if lr.phase == "finished":
                self.upload_driver_log(lr)
                log(f"line {lr.line} -> {lr.status['state']}: {lr.status['reason']}")
                self.active = None
            elif lr.phase == "driving":
                self.ticks_driving += 1
                if self.ticks_driving % 5 == 0:   # the driver log is outside the run dir: gcs_sync never mirrors it
                    self.upload_driver_log(lr)
        if lines is None:
            self.write_heartbeat("blocked", f"no queue ops/queues/{self.session}.json", [])
            return
        view, blocked_reason = [], None
        for line in lines:
            try:
                spec = self.spec(line)
            except ValueError as e:
                view.append({"line": line, "run": None, "state": "held", "reason": str(e)})
                blocked_reason = blocked_reason or str(e)
                continue
            if spec is None:
                view.append({"line": line, "run": None, "state": "held", "reason": "no spec"})
                blocked_reason = blocked_reason or f"no spec ops/lines/{line}.json"
                continue
            if self.active is not None and self.active.line == line:
                view.append({"line": line, "run": spec["run"], "state": self.active.status["state"]})
                continue
            st = self.states.get(line) or self.remote_status(spec["run"]) or {}
            state = st.get("state")
            if state in TERMINAL:
                view.append({"line": line, "run": spec["run"], "state": state})
                if state == "failed" and spec.get("on_fail", "next") == "hold":
                    blocked_reason = blocked_reason or f"line {line} failed with on_fail hold"
                continue
            if state == "held" and st.get("session") == self.session:
                view.append({"line": line, "run": spec["run"], "state": "held"})
                blocked_reason = blocked_reason or f"line {line} held: {st.get('reason')}"
                continue
            if self.active is not None or blocked_reason:
                view.append({"line": line, "run": spec["run"], "state": state or "waiting"})
                continue
            other = self.owned_elsewhere(spec["run"])
            if other:
                self.write_status(spec, "waiting", f"run {spec['run']} is live on session {other}", "wait")
                view.append({"line": line, "run": spec["run"], "state": "waiting"})
                continue
            deps = [(d, *self.dep_state(d)) for d in spec.get("after", [])]
            never = [t for _, s, t in deps if s == "never"]
            if never:
                fs = self.write_status(spec, "failed", "dependency: " + "; ".join(never), "next line")
                view.append({"line": line, "run": spec["run"], "state": "failed"})
                if spec.get("on_fail", "next") == "hold":
                    blocked_reason = f"line {line} failed (dependency) with on_fail hold"
                log(f"line {line} failed: {fs['reason']}")
                continue
            waits = [t for _, s, t in deps if s == "wait"]
            if waits:
                reason = "after: " + "; ".join(waits)
                if (st.get("state"), st.get("reason")) != ("waiting", reason):
                    self.write_status(spec, "waiting", reason, "wait for dependencies")
                view.append({"line": line, "run": spec["run"], "state": "waiting"})
                continue
            lr = LineRun(self, spec)
            if not lr.adopt():
                log(f"take line {line} (run {spec['run']})")
            self.active = lr
            lr.step()
            self.write_status(lr)
            if lr.phase == "finished":
                self.upload_driver_log(lr)
                self.active = None
            view.append({"line": line, "run": spec["run"], "state": lr.status["state"]})
        if self.active is not None:
            self.write_heartbeat("busy", f"line {self.active.line}: {self.active.status['state']}", view)
        elif blocked_reason:
            self.write_heartbeat("blocked", blocked_reason, view)
        elif any(v["state"] not in TERMINAL for v in view):
            self.write_heartbeat("blocked", "waiting for dependencies", view)
        else:
            self.write_heartbeat("drained", "every queued line is terminal", view)

    def loop(self, exit_when_drained=False, max_runtime_s=None):
        t0 = time.time()
        stop = {"flag": False}

        def on_term(signum, _frame):
            stop["flag"] = True
            log(f"signal {signum}: the agent exits; a running driver keeps running and is adopted on restart")

        signal.signal(signal.SIGTERM, on_term)
        signal.signal(signal.SIGINT, on_term)
        while not stop["flag"]:
            try:
                self.tick()
            except Exception as e:  # noqa: BLE001 - the agent must survive anything a tick throws
                log(f"tick error: {type(e).__name__}: {e}")
            if exit_when_drained and self.hb.get("state") == "drained":
                return 0
            if max_runtime_s and time.time() - t0 > max_runtime_s:
                log("max runtime reached")
                return 1
            time.sleep(self.interval)
        return 0


# ---------------------------------------------------------------------------------------------------------- cli
def cmd_run(o):
    work = Path(o.work)
    work.mkdir(parents=True, exist_ok=True)
    (work / "runs").mkdir(exist_ok=True)
    lock = open(work / f".line_agent-{o.session}.lock", "w")
    try:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
    except BlockingIOError:
        print(f"another line_agent for session {o.session} holds {lock.name}", file=sys.stderr)
        return 2
    store = make_store(o.store, o.key, o.gcs_python, o.gcs_pull)
    log(f"agent {VERSION} session {o.session} store {store.url} work {work} driver {o.driver} interval {o.interval}s")
    ag = Agent(store, o.session, work, o.driver, interval=o.interval, sync=bool(o.sync), stale_s=o.stale_s)
    return ag.loop(exit_when_drained=o.exit_when_drained, max_runtime_s=o.max_runtime_s)


def cmd_validate(o):
    bad = 0
    for p in o.specs:
        try:
            d = json.loads(Path(p).read_text())
            errs = validate_spec(d)
        except (OSError, ValueError) as e:
            errs = [str(e)]
        print(f"{p}: {'ok' if not errs else 'INVALID'}")
        for e in errs:
            print(f"  - {e}")
        bad += bool(errs)
    return 1 if bad else 0


def cmd_plan(o):
    d = json.loads(Path(o.spec).read_text())
    errs = validate_spec(d)
    if errs:
        print("INVALID: " + "; ".join(errs), file=sys.stderr)
        return 1
    env = driver_env(d, o.work, bool(o.sync), base_env={})
    print("# env")
    for k in sorted(env):
        print(f"{k}={env[k]!r}")
    print("# fork:", fork_local_path(d, o.work), "(from the store:" if d["fork"]["from"] == "bucket" else "(local;",
          fork_store_key(d) + ")")
    print("# command")
    print(" ".join(driver_argv(d, o.work, o.driver, fork_local_path(d, o.work))))
    return 0


def cmd_publish(o):
    store = make_store(o.store, o.key, o.gcs_python, o.gcs_pull)
    for p in o.specs:
        d = json.loads(Path(p).read_text())
        errs = validate_spec(d)
        if errs:
            print(f"{p}: INVALID: {'; '.join(errs)}", file=sys.stderr)
            return 1
        put_json(store, f"ops/lines/{d['line']}.json", d)
        print(f"published {p} -> {store.url}/ops/lines/{d['line']}.json")
    return 0


def cmd_queue(o):
    store = make_store(o.store, o.key, o.gcs_python, o.gcs_pull)
    missing = [l for l in o.lines if get_json(store, f"ops/lines/{l}.json") is None]
    if missing and not o.force:
        print(f"no spec in the store for: {', '.join(missing)} (publish first, or --force)", file=sys.stderr)
        return 1
    put_json(store, f"ops/queues/{o.session}.json", {"session": o.session, "lines": o.lines, "written_at": now_iso()})
    print(f"queue {o.session}: {' '.join(o.lines)}")
    return 0


def cmd_status(o):
    store = make_store(o.store, o.key, o.gcs_python, o.gcs_pull)
    hb = get_json(store, f"ops/agents/{o.session}.json")
    q = get_json(store, f"ops/queues/{o.session}.json") or {}
    out = {"heartbeat": hb, "age_s": round(time.time() - float(hb["heartbeat_unix"]), 1) if hb else None, "lines": []}
    for l in q.get("lines", []):
        sp = get_json(store, f"ops/lines/{l}.json") or {}
        out["lines"].append({"line": l, "status": get_json(store, f"runs/{sp.get('run')}/line_status.json")
                             if sp else None})
    print(json.dumps(out, indent=1, sort_keys=True))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(prog="line_agent.py", description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)

    def store_args(p):
        p.add_argument("--store", required=True, help="gs://BUCKET, or dir:/path (or a plain path) for a local store")
        p.add_argument("--key", help="service-account key json for gs:// (default $STS/gcs-sts2-ckpt.json)",
                       default=os.path.join(os.environ.get("STS", "/content/sts2"), "gcs-sts2-ckpt.json"))
        p.add_argument("--gcs-python", default="python3",
                       help="python with google-cloud-storage for the gs:// helper (default python3)")
        p.add_argument("--gcs-pull", default=os.path.join(os.environ.get("STS", "/content/sts2"), "gcs_pull.py"),
                       help="scripts/colab/ops/gcs_pull.py on the VM, used for restores (default $STS/gcs_pull.py)")

    p = sub.add_parser("run", help="run the agent loop for one session")
    store_args(p)
    p.add_argument("--session", required=True, help="the Colab session name this agent runs on (its queue)")
    p.add_argument("--work", default=os.environ.get("STS", "/content/sts2"), help="STS root (runs/ lives here)")
    p.add_argument("--driver", default=str(HERE / "w3m_line_v2.sh"))
    p.add_argument("--interval", type=float, default=60.0, help="tick / status / heartbeat period in s (default 60)")
    p.add_argument("--sync", type=int, choices=(0, 1), default=1, help="SYNC for the driver (0 = no gcs_sync)")
    p.add_argument("--stale-s", type=float, default=600.0,
                   help="a non-terminal line_status from another session newer than this blocks taking the line")
    p.add_argument("--exit-when-drained", action="store_true", help="exit 0 once the heartbeat is 'drained' (tests)")
    p.add_argument("--max-runtime-s", type=float, help="exit 1 after this long (tests)")
    p.set_defaults(fn=cmd_run)

    p = sub.add_parser("validate", help="check line spec files")
    p.add_argument("specs", nargs="+")
    p.set_defaults(fn=cmd_validate)

    p = sub.add_parser("plan", help="print the env and the driver command a spec would run")
    p.add_argument("spec")
    p.add_argument("--work", default="/content/sts2")
    p.add_argument("--driver", default=str(HERE / "w3m_line_v2.sh"))
    p.add_argument("--sync", type=int, choices=(0, 1), default=1)
    p.set_defaults(fn=cmd_plan)

    p = sub.add_parser("publish", help="validate specs and write them to ops/lines/<line>.json")
    store_args(p)
    p.add_argument("specs", nargs="+")
    p.set_defaults(fn=cmd_publish)

    p = sub.add_parser("queue", help="write ops/queues/<session>.json")
    store_args(p)
    p.add_argument("--session", required=True)
    p.add_argument("--force", action="store_true", help="allow lines without a published spec")
    p.add_argument("lines", nargs="+")
    p.set_defaults(fn=cmd_queue)

    p = sub.add_parser("status", help="print a session's heartbeat and its lines' status")
    store_args(p)
    p.add_argument("--session", required=True)
    p.set_defaults(fn=cmd_status)

    o = ap.parse_args(argv)
    return o.fn(o)


if __name__ == "__main__":
    sys.exit(main())
