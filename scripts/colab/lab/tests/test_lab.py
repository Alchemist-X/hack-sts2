"""Stub tests for scripts/colab/lab (audit OPS2): w3m_line_v2.sh, line_agent.py, keepalive_v3.sh, colab_guard.sh.

Nothing here touches Colab, GCS or the Tokyo VM: the trainer / evaluator are tests/stubs/*.sh (via the drivers' TRAIN /
EV env knobs), checkpoints are JSON read by a fake torch (tests/stubs/pymods), `sleep` is a PATH shim, the store is a
local directory, `colab` is tests/stubs/bin/colab, and google.cloud.storage is tests/stubs/fakegcs.

Run:  python3 -m pytest -q scripts/colab/lab/tests        or, without pytest:  python3 -I scripts/colab/lab/tests/test_lab.py
"""
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

LAB = Path(__file__).resolve().parents[1]
V1 = LAB.parent / "w3m_line.sh"
V2 = LAB / "w3m_line_v2.sh"
AGENT = LAB / "line_agent.py"
STUBS = LAB / "tests" / "stubs"
SESSION = "sts2a2"


def load_agent_module():
    spec = importlib.util.spec_from_file_location("line_agent", AGENT)
    m = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(m)
    return m


LA = load_agent_module()


def stub_env(**kw):
    env = dict(os.environ)
    env.update(TRAIN=f"bash {STUBS / 'train_stub.sh'}", EV=f"bash {STUBS / 'ev_stub.sh'}", SYNC="0",
               PATH=f"{STUBS / 'bin'}:{env.get('PATH', '/usr/bin:/bin')}", PYTHONPATH=str(STUBS / "pymods"),
               STUB_TRAIN_SLEEP="0.2")
    for k in ("STUB_MISMATCH_ONCE_AT", "STUB_NO_PIN_RUN", "STUB_EV_SKIP", "EVAL_PLAN", "EXTRA", "TAG", "ENDS"):
        env.pop(k, None)
    env.update({k: str(v) for k, v in kw.items()})
    return env


def write_json(p, obj):
    p = Path(p)
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(json.dumps(obj) + "\n")


def fake_ckpt(p, updates):
    write_json(p, {"updates": updates, "weights_hash": "stub"})


# ------------------------------------------------------------------------------------------------ fixtures (plain)
class World:
    """A work dir (the VM's /content/sts2) + a local store (the bucket) with the C4-like and S2-like specs."""

    def __init__(self, root, s2_need="terminal", queue=("c4", "s2"), c3_done=True):
        self.root = Path(root)
        self.work, self.store = self.root / "work", self.root / "store"
        (self.work / "bundle").mkdir(parents=True)
        (self.work / "bundle" / "run_train.sh").write_text("# stub bundle: TRAIN= replaces it in tests\n")
        (self.work / "saves").mkdir()
        fake_ckpt(self.work / "runs/w4-c3/checkpoints/pinned-0012164.pt", 12164)        # C4's local fork
        fake_ckpt(self.store / "runs/w10-s/checkpoints/pinned-0012164.pt", 12164)       # S2's bucket fork
        self.set_c3(c3_done)
        c4 = json.loads((LAB / "specs/example-c4.json").read_text())
        s2 = json.loads((LAB / "specs/example-s2.json").read_text())
        c4["bundle"] = s2["bundle"] = "bundle"
        s2["preflight"] = {"paths": [str(self.work / "saves")]}
        s2["env"]["EXTRA"] = s2["env"]["EXTRA"].replace("/content/sts2/saves-silent-a9a10-late", str(self.work / "saves"))
        s2["after"] = [{"line": "c4", "need": s2_need}]
        s2["on_fail"] = "next"
        self.specs = {"c4": c4, "s2": s2}
        for k, v in self.specs.items():
            assert LA.validate_spec(v) == [], LA.validate_spec(v)
            write_json(self.store / f"ops/lines/{k}.json", v)
        self.queue(list(queue))

    def set_c3(self, done):
        write_json(self.store / "runs/w4-c3/status.json",
                   {"update": 12164 if done else 12000, "state": "finished" if done else "running"})
        rep = self.store / "runs/w4-c3/evals/endpoint/u0012164-dev128-sample-off/repeats.json"
        if done:
            write_json(rep, {"repeats": 3})
        elif rep.exists():
            rep.unlink()

    def queue(self, lines):
        write_json(self.store / f"ops/queues/{SESSION}.json", {"session": SESSION, "lines": lines})

    def agent(self, max_s=50, exit_when_drained=True, **env):
        cmd = [sys.executable, "-I", str(AGENT), "run", "--store", f"dir:{self.store}", "--session", SESSION,
               "--work", str(self.work), "--interval", "0.2", "--sync", "0", "--max-runtime-s", str(max_s)]
        if exit_when_drained:
            cmd.append("--exit-when-drained")
        return subprocess.run(cmd, env=stub_env(**env), capture_output=True, text=True, timeout=max_s + 30)

    def status(self, run):
        p = self.store / f"runs/{run}/line_status.json"
        return json.loads(p.read_text()) if p.exists() else None

    def heartbeat(self):
        return json.loads((self.store / f"ops/agents/{SESSION}.json").read_text())

    def calls(self, run):
        p = self.work / "runs" / run / "train_calls.log"
        return p.read_text().splitlines() if p.exists() else []


def tmpdir():
    return Path(tempfile.mkdtemp(prefix="ops2lab-"))


def run_driver(script, run_dir, fork, ends="12664 13164", last=13164, **env):
    e = stub_env(TAG="t", ENT0="0.005", ENT1="0.005", FORK_U="12164", LAST=str(last), ENDS=ends, MACROS="all",
                 WAIT_MAX_S="5", **env)
    bundle = Path(run_dir).parent.parent / "bundle"
    bundle.mkdir(exist_ok=True)
    return subprocess.run(["bash", str(script), str(bundle), str(run_dir), "a", str(fork), "off"], env=e,
                          capture_output=True, text=True, timeout=60)


def driver_world():
    root = tmpdir()
    fork = root / "runs/src/checkpoints/pinned-0012164.pt"
    fake_ckpt(fork, 12164)
    return root, fork


# ---------------------------------------------------------------------------------------------------- tests
def test_shell_syntax_and_help():
    for s in (V2, LAB / "keepalive_v3.sh", LAB / "colab_guard.sh", STUBS / "train_stub.sh", STUBS / "ev_stub.sh"):
        assert subprocess.run(["bash", "-n", str(s)]).returncode == 0, s
    for s in (V2, LAB / "keepalive_v3.sh", LAB / "colab_guard.sh"):
        r = subprocess.run(["bash", str(s), "--help"], capture_output=True, text=True)
        assert r.returncode == 0 and len(r.stdout.splitlines()) > 5, (s, r.stderr)
    r = subprocess.run([sys.executable, "-I", str(AGENT), "--help"], capture_output=True, text=True)
    assert r.returncode == 0 and "line_status.json" in r.stdout


def test_specs_validate_and_reject():
    r = subprocess.run([sys.executable, "-I", str(AGENT), "validate", *map(str, sorted((LAB / "specs").glob("*.json")))],
                       capture_output=True, text=True)
    assert r.returncode == 0, r.stdout
    good = json.loads((LAB / "specs/example-c4.json").read_text())
    for mutate, needle in [
        (lambda d: d["env"].pop("ENDS"), "env.ENDS is required"),
        (lambda d: d["env"].update(FORK_U=12000), "FORK_U must equal fork.update"),
        (lambda d: d["env"].update(LAST=13000), "LAST must equal"),
        (lambda d: d.update(replicas=2), "replicas > 1"),
        (lambda d: d["env"].update(letter="b", OVERLAP="double"), "requires OVERLAP off"),
        (lambda d: d.update(eval_plan="fast"), "eval_plan"),
        (lambda d: d.update(after=[{"line": "x", "need": "maybe"}]), "need must be"),
        (lambda d: d["env"].update(BOGUS=1), "unknown env key"),
        (lambda d: d["fork"].update({"from": "s3"}), "fork.from"),
    ]:
        d = json.loads(json.dumps(good))
        mutate(d)
        errs = LA.validate_spec(d)
        assert any(needle in e for e in errs), (needle, errs)


def test_v2_legacy_flags_identical_to_v1_and_dev512_plan():
    root, fork = driver_world()
    r1 = run_driver(V1, root / "runs/v1", fork)
    r2 = run_driver(V2, root / "runs/v2", fork)
    assert r1.returncode == 0 and r2.returncode == 0, (r1.stdout[-800:], r2.stdout[-800:])
    c1 = (root / "runs/v1/train_calls.log").read_text()
    c2 = (root / "runs/v2/train_calls.log").read_text()
    assert c1 == c2 and c1.count("\n") == 2, (c1, c2)
    e1 = (root / "runs/v1/ev_calls.log").read_text().replace(str(root / "runs/v1"), "RUN")
    e2 = (root / "runs/v2/ev_calls.log").read_text().replace(str(root / "runs/v2"), "RUN")
    assert e1 == e2, (e1, e2)
    assert r2.stdout.strip().endswith("w3m-t: done")
    r3 = run_driver(V2, root / "runs/v3", fork, EVAL_PLAN="dev512")
    assert r3.returncode == 0, r3.stdout[-800:]
    c3 = (root / "runs/v3/train_calls.log").read_text().splitlines()[0]
    assert "--eval-every 0" in c3 and "--eval-at-seedset silent-a10-dev-s512 --eval-at-modes greedy-off" in c3
    assert "silent-a10-v1" not in c3
    for u in (12664, 13164):
        assert (root / f"runs/v3/evals/endpoint/u{u:07d}-dev128-greedy-off/report.json").is_file()
    r4 = run_driver(V2, root / "runs/v4", fork, EVAL_PLAN="nope")
    assert r4.returncode == 2


def test_v2_compile_fallback_is_per_segment():
    root, fork = driver_world()
    r = run_driver(V2, root / "runs/cf", fork, STUB_MISMATCH_ONCE_AT="12664")
    assert r.returncode == 0, r.stdout[-1500:]
    calls = (root / "runs/cf/train_calls.log").read_text().splitlines()
    assert len(calls) == 3, calls
    assert "--compile off" not in calls[0] and calls[0].endswith("--max-updates 12664")
    assert "--compile off" in calls[1] and calls[1].endswith("--max-updates 12664")
    assert "--compile off" not in calls[2] and calls[2].endswith("--max-updates 13164")
    # v1 keeps it for the rest of the line
    r1 = run_driver(V1, root / "runs/cf1", fork, STUB_MISMATCH_ONCE_AT="12664")
    c1 = (root / "runs/cf1/train_calls.log").read_text().splitlines()
    assert r1.returncode == 0 and "--compile off" in c1[2]


def test_v2_stop_file_and_final_report_check():
    root, fork = driver_world()
    (root / "runs/st").mkdir(parents=True)
    (root / "runs/st/STOP").touch()
    r = run_driver(V2, root / "runs/st", fork)
    assert r.returncode == 3 and "killed" in r.stdout.splitlines()[-1], r.stdout[-600:]
    assert not (root / "runs/st/train_calls.log").exists()
    r = run_driver(V2, root / "runs/fc", fork, STUB_EV_SKIP="dev128-sample-off")
    assert r.returncode == 4, r.stdout[-800:]
    assert r.stdout.strip().splitlines()[-1].endswith("endpoint reports incomplete: stop")
    assert "w3m-t: done" not in r.stdout


def test_agent_two_line_queue_end_to_end():
    w = World(tmpdir())
    t0 = time.time()
    r = w.agent()
    took = time.time() - t0
    assert r.returncode == 0, r.stdout[-3000:] + r.stderr[-2000:]
    assert took < 60, took
    c4, s2 = w.status("w4-c4"), w.status("w10-s2")
    assert c4["state"] == "done" and s2["state"] == "done", (c4, s2)
    assert c4["session"] == SESSION and c4["update"] == 13164 and c4["driver_rc"] == 0
    hb = w.heartbeat()
    assert hb["state"] == "drained" and [q["state"] for q in hb["queue"]] == ["done", "done"]
    cc, sc = w.calls("w4-c4"), w.calls("w10-s2")
    assert len(cc) == 2 and len(sc) == 2
    assert "--ent-coef 0.005" in cc[0] and "--macros all" in cc[0] and "--save-mix" not in cc[0]
    assert "--boss-bonus 0.2" in sc[0] and "--save-mix 0.25" in sc[0]
    # s2 ran after c4 (dependency): its first session starts after c4's last report
    assert (w.work / "fork/pinned-0012164-w10-s.pt").is_file()
    assert (w.work / "runs/w10-s2/checkpoints/ckpt-0012164.pt").is_file()      # the driver's fork copy
    for run in ("w4-c4", "w10-s2"):
        assert (w.work / f"runs/{run}/evals/endpoint/u0013164-dev128-sample-off/repeats.json").is_file()
        assert (w.store / f"runs/{run}/line_driver.log").read_text().strip().endswith("done")
    c4_end = (w.work / "runs/w4-c4/evals/endpoint/u0013164-dev128-sample-off/repeats.json").stat().st_mtime
    assert (w.work / "runs/w10-s2/train_calls.log").stat().st_mtime >= c4_end
    assert "silent-a10-v1" in cc[0]                                                  # eval_plan legacy


def test_agent_failure_moves_on_and_done_dependency_fails():
    w = World(tmpdir())
    r = w.agent(STUB_NO_PIN_RUN="w4-c4")
    assert r.returncode == 0, r.stdout[-3000:]
    c4, s2 = w.status("w4-c4"), w.status("w10-s2")
    assert c4["state"] == "failed" and c4["driver_rc"] == 1 and "pins it" in c4["reason"], c4
    assert s2["state"] == "done", s2                       # need: terminal -> runs after a failed c4
    w2 = World(tmpdir(), s2_need="done")
    r = w2.agent(STUB_NO_PIN_RUN="w4-c4")
    assert r.returncode == 0, r.stdout[-3000:]
    s2 = w2.status("w10-s2")
    assert s2["state"] == "failed" and "need done" in s2["reason"], s2
    assert w2.calls("w10-s2") == []
    assert w2.heartbeat()["state"] == "drained"


def test_agent_waits_for_dependencies_then_resumes_after_restart():
    w = World(tmpdir(), queue=("s2", "c4"), c3_done=False)
    r = w.agent(max_s=2, exit_when_drained=False)
    assert r.returncode == 1                               # max runtime: never drained
    hb = w.heartbeat()
    assert hb["state"] == "blocked" and hb["active_line"] is None, hb
    assert w.status("w4-c4")["state"] == "waiting" and "w4-c3 at u12000" in w.status("w4-c4")["reason"]
    assert w.status("w10-s2")["state"] == "waiting"
    assert w.calls("w4-c4") == [] and w.calls("w10-s2") == []
    w.set_c3(True)                                         # the old-driver predecessor finishes
    r = w.agent()                                          # a fresh agent (restart) picks it up
    assert r.returncode == 0, r.stdout[-3000:]
    assert w.status("w4-c4")["state"] == "done" and w.status("w10-s2")["state"] == "done"
    assert w.heartbeat()["state"] == "drained"


def test_agent_stop_request_and_hold():
    w = World(tmpdir())
    write_json(w.store / "runs/w4-c4/STOP", {"by": "test"})
    r = w.agent()
    assert r.returncode == 0, r.stdout[-3000:]
    assert w.status("w4-c4")["state"] == "killed" and w.status("w4-c4")["driver_rc"] == 3
    assert w.status("w10-s2")["state"] == "done"
    # on_fail hold: a failed line blocks the rest of the queue (heartbeat blocked, never drained)
    w2 = World(tmpdir())
    w2.specs["c4"]["on_fail"] = "hold"
    write_json(w2.store / "ops/lines/c4.json", w2.specs["c4"])
    r = w2.agent(max_s=6, exit_when_drained=False, STUB_NO_PIN_RUN="w4-c4")
    assert w2.status("w4-c4")["state"] == "failed"
    assert w2.status("w10-s2") is None or w2.status("w10-s2")["state"] != "done"
    assert w2.heartbeat()["state"] == "blocked" and "on_fail hold" in w2.heartbeat()["reason"]


def test_agent_restore_from_store_with_md5_check():
    w = World(tmpdir(), queue=("c4",))
    src = w.store / "runs/w4-c4"                           # what a previous runtime left in the bucket
    write_json(src / "status.json", {"update": 12664, "state": "stopped"})
    (src / "metrics.jsonl").write_text('{"update": 12663}\n{"update": 12664}\n')
    (src / "evals.jsonl").write_text('{"update": 12600}\n')
    fake_ckpt(src / "checkpoints/ckpt-0012600.pt", 12600)
    fake_ckpt(src / "checkpoints/ckpt-0012664.pt", 12664)
    fake_ckpt(src / "checkpoints/pinned-0012664.pt", 12664)
    write_json(src / "evals/endpoint/u0012664-ab512-greedy-off/report.json", {"win_rate": 0.1})
    r = w.agent()
    assert r.returncode == 0, r.stdout[-3000:]
    assert "small files verified" in r.stdout
    assert w.status("w4-c4")["state"] == "done"
    calls = w.calls("w4-c4")
    assert len(calls) == 1 and calls[0].endswith("--max-updates 13164"), calls   # resumed, not retrained
    assert not (w.work / "runs/w4-c4/checkpoints/ckpt-0012600.pt").exists()      # newest ckpt only
    # a restore whose small files do not match the store -> held, the queue blocks, no driver starts
    w2 = World(tmpdir(), queue=("c4",))
    shutil.copytree(src, w2.store / "runs/w4-c4")
    for f in ("line_status.json", "line_driver.log"):                         # only what the trainer left
        (w2.store / "runs/w4-c4" / f).unlink()

    class BadStore(LA.LocalStore):
        def restore_run(self, run, dest):
            super().restore_run(run, dest)
            Path(dest, "metrics.jsonl").write_text("truncated\n")

    ag = LA.Agent(BadStore(w2.store), SESSION, w2.work, V2, interval=0.01, sync=False)
    for _ in range(3):
        ag.tick()
    st = w2.status("w4-c4")
    assert st["state"] == "held" and "metrics.jsonl md5 differs" in st["reason"], st
    assert ag.hb["state"] == "blocked" and w2.calls("w4-c4") == []


def test_gcs_store_helper_against_fake_storage():
    root = tmpdir()
    old = {k: os.environ.get(k) for k in ("PYTHONPATH", "FAKE_GCS_ROOT")}
    os.environ["PYTHONPATH"] = str(STUBS / "fakegcs")
    os.environ["FAKE_GCS_ROOT"] = str(root / "gcs")
    try:
        st = LA.make_store("gs://bkt", key=str(root / "key.json"), python=sys.executable)
        assert st.get("ops/queues/x.json") is None and st.size("nope") is None and st.md5("nope") is None
        LA.put_json(st, "ops/queues/x.json", {"lines": ["a"]})
        assert LA.get_json(st, "ops/queues/x.json") == {"lines": ["a"]}
        assert st.size("ops/queues/x.json") == (root / "gcs/bkt/ops/queues/x.json").stat().st_size
        assert st.md5("ops/queues/x.json") == LA.md5_file(root / "gcs/bkt/ops/queues/x.json")
        assert st.list("ops") == ["ops/queues/x.json"]
        assert st.download("ops/queues/x.json", root / "dl/x.json") and (root / "dl/x.json").is_file()
    finally:
        for k, v in old.items():
            if v is None:
                os.environ.pop(k, None)
            else:
                os.environ[k] = v


# ---------------------------------------------------------------------------------------------- keepalive v3
def ka(root, sessions, **env):
    (root / "sessions.txt").write_text(sessions)
    e = dict(os.environ, COLAB=str(STUBS / "bin/colab"), STUB_SESSIONS=str(root / "sessions.txt"),
             STUB_LOG=str(root / "colab.log"), KA_DIR=str(root / "ka"), STORE=f"dir:{root / 'store'}",
             ONE_TICK="1", PATH=f"{STUBS / 'bin'}:{os.environ.get('PATH', '')}")
    e.update({k: str(v) for k, v in env.items()})
    return subprocess.run(["bash", str(LAB / "keepalive_v3.sh"), "1"], env=e, capture_output=True, text=True,
                          timeout=60)


def hb(root, session, state, age=0, queue=()):
    write_json(root / f"store/ops/agents/{session}.json",
               {"session": session, "state": state, "reason": f"test {state}", "active_line": None,
                "queue": list(queue), "heartbeat_unix": time.time() - age})


def colab_calls(root):
    p = root / "colab.log"
    return p.read_text() if p.exists() else ""


def test_keepalive_stops_only_after_two_fresh_drained():
    root = tmpdir()
    live = "[sts2a2] Hardware: A100 status RUNNING\n[sts2b3] Hardware: A100 status RUNNING\n"
    hb(root, "sts2a2", "drained")
    hb(root, "sts2b3", "busy")
    r = ka(root, live, DRY_RUN=0)
    assert r.returncode == 0, r.stderr
    assert "stop" not in colab_calls(root) and colab_calls(root).count("exec") == 2
    hb(root, "sts2a2", "drained")
    r = ka(root, live, DRY_RUN=0)
    calls = colab_calls(root)
    assert "colab stop -s sts2a2" in calls and "stop -s sts2b3" not in calls, calls
    # a non-drained read in between resets the count
    root2 = tmpdir()
    hb(root2, "sts2a2", "drained")
    ka(root2, live, DRY_RUN=0)
    hb(root2, "sts2a2", "busy")
    ka(root2, live, DRY_RUN=0)
    hb(root2, "sts2a2", "drained")
    ka(root2, live, DRY_RUN=0)
    assert "stop" not in colab_calls(root2)


def test_keepalive_stale_alerts_only_and_dry_run_default():
    root = tmpdir()
    live = "[sts2a2] Hardware: A100 status RUNNING\n[ghost] Hardware: CPU status RUNNING\n[legacy] Hardware: A100 x\n"
    hb(root, "sts2a2", "drained", age=3600, queue=[{"line": "c4", "state": "failed"}])
    for _ in range(3):
        r = ka(root, live, DRY_RUN=0)
    assert "stop" not in colab_calls(root), colab_calls(root)
    alerts = (root / "ka/ALERT").read_text()
    assert "sts2a2 heartbeat stale" in alerts and "line c4 on sts2a2 failed" in alerts and "ghost is a CPU" in alerts
    assert alerts.count("line c4 on sts2a2 failed") == 1                    # once per (session, line)
    assert "legacy (A100): no agent heartbeat" in (root / "ka/keepalive.log").read_text()
    assert (root / "ka/keepalive_heartbeat").is_file()
    # default DRY_RUN=1: two fresh drained reads print the stop, colab never gets exec/stop
    root2 = tmpdir()
    for _ in range(2):
        hb(root2, "sts2a2", "drained")
        r = ka(root2, "[sts2a2] Hardware: A100 status RUNNING\n")
    assert "DRY: " in r.stdout and "stop -s sts2a2" in r.stdout, r.stdout
    assert "exec" not in colab_calls(root2) and "stop" not in colab_calls(root2)
    # a failing `colab sessions` -> nothing happens
    root3 = tmpdir()
    hb(root3, "sts2a2", "drained")
    ka(root3, "error: No valid default credentials\n", DRY_RUN=0)
    ka(root3, "error: No valid default credentials\n", DRY_RUN=0)
    assert "stop" not in colab_calls(root3) and "exec" not in colab_calls(root3)


# ------------------------------------------------------------------------------------------------ colab_guard
def guard(root, *args, sessions="[sts2a2] Hardware: A100 status RUNNING\n[ghost] Hardware: CPU status RUNNING\n",
          **env):
    (root / "sessions.txt").write_text(sessions)
    e = dict(os.environ, COLAB=str(STUBS / "bin/colab"), STUB_SESSIONS=str(root / "sessions.txt"),
             STUB_LOG=str(root / "colab.log"), GUARD_ALERT=str(root / "ALERT"), DRY_RUN="1")
    e.update({k: str(v) for k, v in env.items()})
    return subprocess.run(["bash", str(LAB / "colab_guard.sh"), *args], env=e, capture_output=True, text=True,
                          timeout=30, stdin=subprocess.DEVNULL)


def test_colab_guard():
    root = tmpdir()
    r = guard(root, "ssh", "sts2a2", "nvidia-smi")
    assert r.returncode == 0 and r.stdout.strip() == "DRY: ssh colab-sts2a2 nvidia-smi", (r.stdout, r.stderr)
    r = guard(root, "ssh", "sts2a2", "hostname", SSH_CONFIG="/x/ssh_config")
    assert r.stdout.strip() == "DRY: ssh -F /x/ssh_config colab-sts2a2 hostname"
    for args in (("ssh", "sts2dead", "ls"), ("ssh", "ghost", "ls"), ("exec", "sts2dead"),
                 ("scp", "a.tgz", "colab-sts2dead:/content/"), ("scp", "a", "b"), ("ssh", "bad;name"),
                 ("check", "sts2a2", "ghost")):
        r = guard(root, *args)
        assert r.returncode == 3 and "REFUSED" in r.stderr, (args, r.returncode, r.stderr)
    assert (root / "ALERT").read_text().count("refused") == 7
    r = guard(root, "scp", "-q", "a.tgz", "colab-sts2a2:/content/")
    assert r.returncode == 0 and r.stdout.strip() == "DRY: scp -q a.tgz colab-sts2a2:/content/"
    r = guard(root, "exec", "sts2a2", "--timeout", "60")
    assert r.returncode == 0 and "exec -s sts2a2 --timeout 60" in r.stdout
    r = guard(root, "check", "sts2a2")
    assert r.returncode == 0
    r = guard(root, "check", "ghost", REQUIRE_GPU=0)
    assert r.returncode == 0
    r = guard(root, "ssh", "sts2a2", sessions="error: No valid default credentials\n")
    assert r.returncode == 3 and "colab sessions failed" in r.stderr
    r = guard(root, "sessions")
    assert r.stdout.split() == ["sts2a2", "A100", "ghost", "CPU"]
    # nothing but `colab sessions` ever reached the colab CLI (DRY_RUN=1)
    assert set(l.split()[1] for l in (root / "colab.log").read_text().splitlines()) == {"sessions"}


if __name__ == "__main__":
    tests = [(n, f) for n, f in sorted(globals().items()) if n.startswith("test_") and callable(f)]
    failed = 0
    for n, f in tests:
        t0 = time.time()
        try:
            f()
            print(f"PASS {n} ({time.time() - t0:.1f}s)")
        except Exception as e:  # noqa: BLE001
            failed += 1
            import traceback
            traceback.print_exc()
            print(f"FAIL {n}: {type(e).__name__}: {str(e)[:500]}")
    print(f"{len(tests) - failed}/{len(tests)} passed")
    sys.exit(1 if failed else 0)
