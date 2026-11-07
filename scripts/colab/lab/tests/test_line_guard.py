"""Tests of scripts/colab/lab/line_guard.py and preflight.sh (stdlib; pytest optional).

    python3 -I -m pytest scripts/colab/lab/tests/test_line_guard.py      # with pytest
    python3 -I scripts/colab/lab/tests/test_line_guard.py                 # without

Replay fixtures read the real run mirrors (metrics.jsonl / evals.jsonl / config.json / status.json per run) from
$STS2_GUARD_MIRROR, default the 2026-11-06 audit's scratchpad mirror; they are skipped when it is absent. The
synthetic tests always run; the kill test signals only processes it starts itself.
"""
from __future__ import annotations

import importlib.util
import json
import math
import os
import signal
import socket
import subprocess
import sys
import tempfile
import time
from pathlib import Path

LAB = Path(__file__).resolve().parents[1]
GUARD = LAB / "line_guard.py"
PREFLIGHT = LAB / "preflight.sh"
RULES = LAB / "guard-rules-default.json"
MIRROR = Path(os.environ.get(
    "STS2_GUARD_MIRROR",
    "/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/"
    "adbf2344-809a-4167-9774-cd8087651b17/scratchpad/mirror"))

try:
    import pytest
    Skip = pytest.skip.Exception

    def skip(msg):
        pytest.skip(msg)
except ImportError:   # plain runner
    class Skip(Exception):
        pass

    def skip(msg):
        raise Skip(msg)


def _load():
    spec = importlib.util.spec_from_file_location("line_guard", GUARD)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


lg = _load()


def guard(*args, timeout=120):
    p = subprocess.run([sys.executable, "-I", str(GUARD), *map(str, args)], capture_output=True, text=True,
                       timeout=timeout)
    return p.returncode, p.stdout + p.stderr


def mirror(run):
    d = MIRROR / run
    if not (d / "metrics.jsonl").exists():
        skip(f"no mirror of {run} under {MIRROR}")
    return d


def replay(run, *extra):
    """--once --dry-run replay of a mirrored run; returns (rc, output, result json)."""
    with tempfile.TemporaryDirectory() as t:
        js = Path(t) / "r.json"
        rc, out = guard(mirror(run), "--once", "--dry-run", "--json", js, *extra)
        return rc, out, json.loads(js.read_text())


def first(res):
    assert res["first"] is not None, "nothing fired"
    return res["first"]


# --------------------------------------------------------------------------------------- real-run replays
def test_e2_kl_fires_within_10_updates():
    rc, out, r = replay("w9-e2", "--fork-run", mirror("w3m-a"))
    f = first(r)
    assert rc == lg.EXIT_ALERT, out
    assert f["rule"] == "kl_high" and f["since_fork"] <= 12, f


def test_e3_push_clamped_fires_within_10_updates():
    rc, out, r = replay("w9-e3", "--fork-run", mirror("w3m-a"))
    f = first(r)
    assert f["rule"] == "push_clamped_high" and f["since_fork"] <= 12, f


def test_g_fires_within_20_updates_with_and_without_control():
    for extra in (("--fork-run", mirror("w4-c"), "--control", mirror("w4-c3")), ("--fork-run", mirror("w4-c"))):
        rc, out, r = replay("w10-g", *extra)
        f = first(r)
        assert f["since_fork"] <= 20, (extra, f)
        fired = {x["rule"] for x in r["results"] if x["status"] == "triggered"}
        assert "pmass_collapse" in fired
        if "--control" in map(str, extra):
            assert "sps_slow" in fired
    # the dev-128 fork rule (EV5 KILL) fires at G's first per-100 eval: 0.398 vs fork 0.489
    dev = [x for x in r["results"] if x["rule"] == "dev_window_vs_fork"][0]
    assert dev["status"] == "triggered" and dev["update"] == 10700, dev


def test_e_prime_fires_before_divergence():
    rc, out, r = replay("w9-e", "--fork-run", mirror("w3m-a"))
    f = first(r)
    assert f["update"] <= 7676, f   # u7676: loss 1.1e14, kl 5e14, then NaN
    assert f["rule"] == "kl_high"


def test_no_false_kill_on_c3_and_s():
    for run, ctl in (("w4-c3", "w10-s"), ("w10-s", "w4-c3")):
        rc, out, r = replay(run, "--fork-run", mirror("w4-c"), "--control", mirror(ctl), "--until-update", 12000)
        assert rc == lg.EXIT_OK and not r["fired"], (run, out)
        st = {x["rule"]: x["status"] for x in r["results"]}
        assert st["kl_high"] == "ok" and st["sps_slow"] == "ok" and st["dev_window_vs_fork"] == "ok", st


def test_kill_mode_dry_run_on_replay_performs_nothing():
    d = mirror("w9-e2")
    before = sorted(p.name for p in d.iterdir())
    with tempfile.TemporaryDirectory() as t:
        rc, out = guard(d, "--once", "--dry-run", "--mode", "kill", "--state-dir", t)
        assert rc == lg.EXIT_KILL, out
        assert "DRY-RUN would write" in out and "STOP" in out
        assert not any(Path(t).iterdir())
    assert sorted(p.name for p in d.iterdir()) == before


# ------------------------------------------------------------------------------------------ synthetic runs
def mkrun(root: Path, name="run", fork=100, rows=(), evals=(), status=None) -> Path:
    d = root / name
    d.mkdir(parents=True)
    (d / "config.json").write_text(json.dumps(
        {"sessions": [{"resumed_from": f"/x/{name}/checkpoints/ckpt-{fork:07d}.pt"}]}))
    with open(d / "metrics.jsonl", "w") as f:
        for i, r in enumerate(rows):
            f.write(json.dumps(dict({"update": fork + 1 + i, "env_steps_update": 131072, "t_total_s": 12.0}, **r))
                    + "\n")
    with open(d / "evals.jsonl", "w") as f:
        for e in evals:
            f.write(json.dumps(e) + "\n")
    if status is not None:
        (d / "status.json").write_text(json.dumps(status))
    return d


def rules_file(root: Path, rules, **top) -> Path:
    p = root / "rules.json"
    p.write_text(json.dumps(dict({"rules": rules}, **top)))
    return p


def test_nan_loss_fires_and_streaks():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        rows = [{"loss": 0.01, "approx_kl": 0.003}] * 4 + [{"loss": float("nan"), "approx_kl": 0.003}]
        d = mkrun(t, rows=rows)
        assert "NaN" in (d / "metrics.jsonl").read_text()
        rc, out = guard(d, "--once", "--dry-run")
        assert rc == lg.EXIT_ALERT and "loss_nonfinite" in out and "u105 (+5)" in out, out
        # for_updates: the condition must hold on consecutive judged updates
        rf = rules_file(t, [{"name": "x", "metric": "approx_kl", "op": ">", "value": 0.1, "for_updates": 3}])
        d2 = mkrun(t, "r2", rows=[{"approx_kl": v} for v in (0.2, 0.2, 0.01, 0.2, 0.2, 0.2, 0.2)])
        rc, out = guard(d2, "--once", "--dry-run", "--rules", rf)
        assert rc == lg.EXIT_ALERT and "u106 (+6)" in out, out


def test_warmup_window_and_glob():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        rows = [{"approx_kl": 1.0}] * 3 + [{"approx_kl": 0.001}] * 20
        rf = rules_file(t, [{"name": "kl", "metric": "approx_kl", "agg": "median", "window": 5, "op": ">",
                             "value": 0.05, "warmup_updates": 3}])
        rc, out = guard(mkrun(t, rows=rows), "--once", "--dry-run", "--rules", rf)
        assert rc == lg.EXIT_OK, out
        rows = [{"explore_pmass_macro.A": 0.3, "explore_pmass_macro.B": 1e-9}] * 5 + \
               [{"explore_pmass_macro.A": 0.001, "explore_pmass_macro.B": 1e-12}] * 3
        rf = rules_file(t, [{"name": "pm", "metric": "explore_pmass_macro.*", "op": "<", "ref": "first5",
                             "factor": 0.1, "min_ref": 0.01, "for_updates": 3}])
        js = t / "o.json"
        rc, out = guard(mkrun(t, "g", rows=rows), "--once", "--dry-run", "--rules", rf, "--json", js)
        res = {r["key"]: r for r in json.loads(js.read_text())["results"]}
        assert res["explore_pmass_macro.A"]["status"] == "triggered" and res["explore_pmass_macro.A"]["update"] == 108
        assert res["explore_pmass_macro.B"]["status"] == "skipped"   # was ~0 already: min_ref


def test_eval_rules_fork_ci_and_consecutive():
    def ev(u, pm, hw=0.03):
        return {"update": u, "seedset": "dev", "mode": "greedy-off", "progress_mean": pm,
                "progress_ci95": [pm - hw, pm + hw]}
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        fork = mkrun(t, "fork", fork=0, rows=[{}], evals=[ev(100, 0.48)])
        rules = [{"name": "dev", "source": "evals", "seedset": "dev", "mode": "greedy-off", "agg": "mean",
                  "window": 3, "min_points": 1, "op": "<", "ref": "fork", "delta": -0.04, "ci": True},
                 {"name": "floor", "source": "evals", "seedset": "dev", "mode": "greedy-off", "op": "<",
                  "value": 0.42, "consecutive_evals": 3}]
        rf = rules_file(t, rules)
        # -0.05 but the interval (hw sqrt(0.05^2 + 0.03^2) = 0.058) still covers 0 -> no fire; -0.09 -> fire
        d = mkrun(t, "a", rows=[{}], evals=[ev(200, 0.43, 0.05)])
        js = t / "a.json"
        guard(d, "--once", "--dry-run", "--rules", rf, "--fork-run", fork, "--json", js)
        r = {x["rule"]: x for x in json.loads(js.read_text())["results"]}
        assert r["dev"]["status"] == "ok", r["dev"]
        d = mkrun(t, "b", rows=[{}], evals=[ev(200, 0.39, 0.03)])
        rc, out = guard(d, "--once", "--dry-run", "--rules", rf, "--fork-run", fork)
        assert "rule=dev" in out and "u200" in out, out
        # --fork-eval overrides; consecutive floor
        d = mkrun(t, "c", rows=[{}], evals=[ev(u, 0.41) for u in (200, 300, 400)])
        rc, out = guard(d, "--once", "--dry-run", "--rules", rf, "--fork-eval", 0.40)
        assert "rule=floor" in out and "u400" in out and "rule=dev" not in out, out


def test_alert_mode_writes_once_and_keeps_state():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        d = mkrun(t, rows=[{"approx_kl": 1.0}] * 12)
        (d / "line_status.json").write_text(json.dumps({"state": "training", "update": 105}))
        rc, out = guard(d, "--once")
        assert rc == lg.EXIT_ALERT, out
        alert = (d / "ALERT").read_text().splitlines()
        assert len(alert) == 1 and "kl_high" in alert[0]
        ls = json.loads((d / "line_status.json").read_text())
        assert ls["state"] == "training" and "kl_high" in ls["reason"] and ls["alert_update"] == 110
        assert not (d / "STOP").exists()
        rc, out = guard(d, "--once")   # already fired: not re-alerted
        assert len((d / "ALERT").read_text().splitlines()) == 1
        # follow mode exits on a terminal line state
        (d / "line_status.json").write_text(json.dumps({"state": "done"}))
        rc, out = guard(d, "--interval", "0.1", "--max-polls", "5", timeout=30)
        assert "guard exits" in out, out


FAKE_TRAINER = r"""
import json, os, signal, socket, sys, time
run = sys.argv[1]
def term(s, f):
    open(os.path.join(run, "trainer.sigterm"), "a").write("x\n")
    time.sleep(0.5)            # "checkpoint"
    sys.exit(0)
signal.signal(signal.SIGTERM, term)
json.dump({"state": "running", "pid": os.getpid(), "host": socket.gethostname(), "update": 112},
          open(os.path.join(run, "status.json"), "w"))
time.sleep(120)
"""


def test_kill_mode_stops_trainer_and_driver():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        d = mkrun(t, rows=[{"approx_kl": 1.0}] * 12)
        tr = t / "fake_trainer.py"
        tr.write_text(FAKE_TRAINER)
        drv = t / "fake_w3m_line.sh"   # restarts the trainer after rc 0, like w3m_line.sh
        drv.write_text(f'#!/bin/bash\nwhile true; do "{sys.executable}" -I "{tr}" "$1"; sleep 0.2; done\n')
        p = subprocess.Popen(["bash", str(drv), str(d)], start_new_session=True,
                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        try:
            for _ in range(100):
                if (d / "status.json").exists():
                    break
                time.sleep(0.1)
            st = json.loads((d / "status.json").read_text())
            rc, out = guard(d, "--once", "--mode", "kill", "--driver-match", "fake_w3m_line", "--kill-wait", "15")
            assert rc == lg.EXIT_KILL, out
            assert (d / "STOP").exists() and "kl_high" in (d / "STOP").read_text()
            assert "SIGTERM trainer pid %d" % st["pid"] in out, out
            assert "SIGTERM driver pgid" in out, out
            ls = json.loads((d / "line_status.json").read_text())
            assert ls["state"] == "killed" and ls["rule"] == "kl_high" and ls["update"] == 110
            p.wait(timeout=10)
            time.sleep(1.0)
            assert (d / "trainer.sigterm").exists()
            assert lg.ps_cmdline(st["pid"]) is None
            # no new trainer was started by the (dead) driver
            assert json.loads((d / "status.json").read_text())["pid"] == st["pid"]
            gs = json.loads((d / "guard.state.json").read_text())
            assert gs["killed"]["rule"] == "kl_high"
        finally:
            try:
                os.killpg(p.pid, signal.SIGKILL)
            except OSError:
                pass


def test_kill_refuses_foreign_host_pid():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        d = mkrun(t, rows=[{"approx_kl": 1.0}] * 12,
                  status={"state": "running", "pid": os.getpid(), "host": "not-" + socket.gethostname()})
        rc, out = guard(d, "--once", "--mode", "kill", "--driver-match", "no-such-driver-xyz")
        assert rc == lg.EXIT_KILL and "is not this host" in out and "SIGTERM" not in out, out
        # same host but the pid's command line does not mention the run dir (this test process)
        (d / "status.json").write_text(json.dumps({"pid": os.getpid(), "host": socket.gethostname()}))
        (d / "line_status.json").unlink()
        (d / "guard.state.json").unlink()
        rc, out = guard(d, "--once", "--mode", "kill", "--driver-match", "no-such-driver-xyz")
        assert "does not mention" in out and "SIGTERM trainer" not in out, out


def test_bad_rules_and_help():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        d = mkrun(t, rows=[{}])
        rc, out = guard(d, "--once", "--rules", rules_file(t, [{"metric": "x", "op": "~", "value": 1}]))
        assert rc == lg.EXIT_USAGE, out
    rc, out = guard("--help")
    assert rc == 0 and "--mode" in out
    spec = json.loads(RULES.read_text())
    assert {r["name"] for r in spec["rules"]} >= {"kl_high", "push_clamped_high", "pmass_collapse", "sps_slow",
                                                  "loss_nonfinite", "dev_window_vs_fork"}


# ------------------------------------------------------------------------------------------------ preflight
def preflight(*args, env=None):
    e = {k: v for k, v in os.environ.items() if k not in ("RUN", "EXTRA", "MACROS", "LAST", "ENT0", "ENT1")}
    e.update(env or {})
    p = subprocess.run(["bash", str(PREFLIGHT), *map(str, args)], capture_output=True, text=True, env=e,
                       timeout=120)
    return p.returncode, p.stdout + p.stderr


def test_preflight_syntax_and_help():
    assert subprocess.run(["bash", "-n", str(PREFLIGHT)]).returncode == 0
    rc, out = preflight("--help")
    assert rc == 0 and "premise" in out and "RUN=1" in out
    rc, out = preflight()
    assert rc == 2


def test_preflight_g_premise_fails_and_smoke_judged():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        g = mirror("w10-g")
        smoke = t / "smoke-g"
        smoke.mkdir()
        (smoke / "metrics.jsonl").write_text("".join((g / "metrics.jsonl").read_text().splitlines(True)[:10]))
        out_json = t / "g.json"
        rc, out = preflight("--line", "g", "--fork-ckpt", "/nonexistent/pinned-0010664.pt", "--fork-update",
                            10664, "--smoke-dir", smoke, "--control", mirror("w4-c3"), "--out", out_json,
                            env={"EXTRA": "--explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3"})
        doc = json.loads(out_json.read_text())
        assert rc == 1 and doc["verdict"] == "FAIL", out
        pr = doc["checks"]["premise"]
        assert pr["status"] == "fail" and abs(pr["values"]["ChooseRestOption"]["pmass"] - 0.34) < 0.01
        assert pr["values"]["BuyEntry:remove"]["key"] == "explore_pmass_macro.BuyEntry:card_removal"
        assert doc["checks"]["smoke"]["status"] == "fail"   # sps 4,500 vs C3 11,100
        assert doc["checks"]["whatif"]["status"] == "pending" and len(doc["checks"]["whatif"]["commands"]) == 3
        assert "--force" in doc["checks"]["whatif"]["commands"][1]
        assert "ENDS=10674" in doc["checks"]["smoke"]["command"] and not doc["checks"]["smoke"]["ran"]


def test_preflight_pass_and_whatif_rules():
    with tempfile.TemporaryDirectory() as t:
        t = Path(t)
        rows = [{"approx_kl": 0.003, "loss": 0.001, "explore_pmass_macro.ChooseRestOption": 1e-9}] * 10
        smoke = mkrun(t, "smoke", fork=500, rows=rows)
        cmp_ = t / "cmp.json"

        def run(delta, just=None):
            cmp_.write_text(json.dumps({"pairs": [{"a": "greedy", "b": "force", "delta_mean": delta,
                                                   "t_ci95": [delta - 0.02, delta + 0.02]}]}))
            args = ["--line", "x", "--fork-ckpt", "/nonexistent.pt", "--fork-update", 500, "--kinds",
                    "ChooseRestOption", "--smoke-dir", smoke, "--whatif-json", cmp_, "--out", t / "x.json"]
            if just:
                args += ["--whatif-justification", just]
            rc, out = preflight(*args)
            return rc, json.loads((t / "x.json").read_text()), out

        rc, doc, out = run(0.01)
        assert rc == 0 and doc["verdict"] == "PASS", out
        assert doc["checks"]["premise"]["status"] == "pass" and doc["checks"]["smoke"]["status"] == "pass"
        rc, doc, out = run(-0.14)
        assert rc == 1 and doc["checks"]["whatif"]["status"] == "fail", out
        rc, doc, out = run(-0.14, "conditional push only on hard-zero rows")
        assert rc == 0 and doc["checks"]["whatif"]["justification"], out
        # nothing measured yet -> INCOMPLETE
        rc, out = preflight("--line", "y", "--fork-update", 500, "--kinds", "ChooseRestOption",
                            "--smoke-dir", t / "none", "--out", t / "y.json")
        assert rc == 3 and json.loads((t / "y.json").read_text())["verdict"] == "INCOMPLETE", out


if __name__ == "__main__":
    tests = [(n, f) for n, f in sorted(globals().items()) if n.startswith("test_") and callable(f)]
    bad = 0
    for n, f in tests:
        try:
            f()
            print(f"ok    {n}")
        except Skip as e:
            print(f"skip  {n}: {e}")
        except Exception as e:   # noqa: BLE001
            bad += 1
            print(f"FAIL  {n}: {type(e).__name__}: {e}")
    print(f"{len(tests) - bad}/{len(tests)} passed")
    sys.exit(1 if bad else 0)
