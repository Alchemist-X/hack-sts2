"""Tests of scripts/analysis/decide.py (stdlib unittest; `python3 -I tests/test_decide.py` or pytest)."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import decide as DC  # noqa: E402
import evalstats as EV  # noqa: E402
import synth  # noqa: E402

TOOL = os.path.join(synth.ANALYSIS, "decide.py")
FIX = os.path.join(synth.HERE, "fixtures")
PREREG_S2C4 = os.path.join(synth.ANALYSIS, "prereg", "s2-vs-c4.json")
FORK = 1000
UPS = [1100, 1200, 1300, 1400, 1500, 1600, 1700, 1800, 1900, 2000]
N = 64
BOOT = 2000                                                       # fewer resamples: speed


def prereg(root, treat, control, **kw):
    p = {"schema": "sts2.prereg/1", "name": kw.pop("name", "syn"), "arms": {"treat": treat, "control": control},
         "fork": FORK, "at": [1500, 2000], "max_at": 2000,
         "primary": {"seedset": ["dev512", f"dev{N}"], "window": 5, "mode": "greedy-off"},
         "confirm": {"seedset": f"ab{N}", "mode": "greedy-off", "endpoint": True},
         "mde": 0.015, "margin": 0.01, "kill": {"after_updates": 300, "delta": -0.03}}
    p.update(kw)
    path = os.path.join(root, f"{p['name']}.json")
    synth.wj(path, p)
    return path


def line(root, run, shift, seed, ups=UPS, confirm_shift=None, confirm_at=(1500, 2000)):
    synth.make_line(root, run, ups, n=N, shift=shift, seed=seed)
    import random
    rng = random.Random(seed + 100)
    base = synth.base_floors(N, seed=7)
    for u in confirm_at:
        if u <= ups[-1]:
            cs = shift if confirm_shift is None else confirm_shift
            synth.write_eval(root, run, u, synth.jitter(base, rng, cs), role="ab", endpoint=True)


def run_decide(path, at, root):
    p, _sha = DC.load_prereg(path)
    ixs = {r: EV.Index(f"{r}=" + os.path.join(root, r)) for r in p["arms"]["treat"] + p["arms"]["control"]}
    return DC.decide(p, at, ixs, boot=BOOT)


class TestVerdicts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        r = cls.root = cls.tmp.name
        line(r, "c1", 0, 1)
        line(r, "c2", 0, 2)
        line(r, "t1", 3, 3)
        line(r, "t2", 3, 4)
        line(r, "n1", 0, 5)
        line(r, "bad", -6, 6)
        line(r, "tneg", 3, 7, confirm_shift=-3)
        line(r, "late", 3, 8, ups=UPS[:4])                            # stops at u1400

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_adopt_two_pairs(self):
        r = run_decide(prereg(self.root, ["t1", "t2"], ["c1", "c2"]), 1500, self.root)
        self.assertEqual(r["verdict"], "ADOPT", r["rule"])
        self.assertEqual(r["primary"]["seedset"], f"dev{N}")         # dev512 absent -> fallback
        self.assertEqual(r["primary"]["pooled"]["n_pairs"], 2)
        self.assertEqual(r["primary"]["pooled"]["ci_method"], "per-seed")
        self.assertTrue(r["confirm"]["complete"])
        self.assertEqual(r["pairs"][0]["treat_window"]["updates"], [1100, 1200, 1300, 1400, 1500])

    def test_replicate_single_pair(self):
        r = run_decide(prereg(self.root, ["t1"], ["c1"]), 1500, self.root)
        self.assertEqual(r["verdict"], "REPLICATE", r["rule"])

    def test_confirm_blocks_adopt(self):
        r = run_decide(prereg(self.root, ["t1", "tneg"], ["c1", "c2"]), 1500, self.root)
        self.assertNotEqual(r["verdict"], "ADOPT")
        self.assertEqual(r["verdict"], "CONTINUE", r["rule"])
        self.assertTrue(any("primary would ADOPT" in w for w in r["warnings"]))

    def test_kill_on_monitoring_read(self):
        p = prereg(self.root, ["bad"], ["c1"])
        self.assertEqual(run_decide(p, 1200, self.root)["verdict"], "CONTINUE")     # only 200 updates in
        r = run_decide(p, 1300, self.root)
        self.assertEqual(r["verdict"], "KILL", r["rule"])
        self.assertIsNone(r["confirm"])                                              # confirm set not read

    def test_null_continue_then_reject(self):
        p = prereg(self.root, ["n1"], ["c1"])
        r1 = run_decide(p, 1500, self.root)
        self.assertEqual(r1["verdict"], "CONTINUE", r1["rule"])
        r2 = run_decide(p, 2000, self.root)
        self.assertEqual(r2["verdict"], "REJECT", r2["rule"])

    def test_monitoring_read_no_verdict(self):
        r = run_decide(prereg(self.root, ["t1"], ["c1"]), 1700, self.root)
        self.assertEqual((r["verdict"], r.get("state")), ("CONTINUE", "monitoring"))

    def test_pairing_wait(self):
        r = run_decide(prereg(self.root, ["late"], ["c1"]), 1500, self.root)
        self.assertEqual((r["verdict"], r.get("state")), ("CONTINUE", "pairing_wait"))

    def test_prereg_validation(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(DC.PreregError):
                DC.load_prereg(prereg(d, ["a"], ["b", "c"]))
            with self.assertRaises(DC.PreregError):
                DC.load_prereg(prereg(d, ["a"], ["b"], at=[900]))
            path = os.path.join(d, "x.json")
            synth.wj(path, {"schema": "nope"})
            with self.assertRaises(DC.PreregError):
                DC.load_prereg(path)

    def test_cli_writes_pair_json_and_logs_confirm_reads(self):
        p = prereg(self.root, ["t1", "t2"], ["c1", "c2"], name="cli")
        with tempfile.TemporaryDirectory() as out:
            od = os.path.join(out, "pairs", "u1500")
            args = [sys.executable, "-I", TOOL, p, "--at", "1500", "--root", self.root, "--out", od]
            q = subprocess.run(args, capture_output=True, text=True)
            self.assertEqual(q.returncode, 0, q.stderr)
            self.assertTrue(q.stdout.startswith("ADOPT cli @u1500"), q.stdout)
            pj = synth.rj(os.path.join(od, "pair.json"))
            self.assertEqual(pj["verdict"], "ADOPT")
            self.assertEqual(len(pj["prereg_sha256"]), 64)
            log = os.path.join(out, "pairs", "ab-access.jsonl")
            q = subprocess.run(args, capture_output=True, text=True)
            self.assertIn("already read 1 time", q.stderr)
            with open(log) as f:
                self.assertEqual(len(f.readlines()), 2)
            # a monitoring read does not touch the confirm set or its log
            subprocess.run([sys.executable, "-I", TOOL, p, "--at", "1300", "--root", self.root, "--out",
                            os.path.join(out, "pairs", "u1300")], capture_output=True, text=True, check=True)
            with open(log) as f:
                self.assertEqual(len(f.readlines()), 2)
            q = subprocess.run([sys.executable, "-I", TOOL, p, "--at", "1500", "--root", self.root, "--no-write",
                                "--json", "-"], capture_output=True, text=True)
            self.assertEqual(json.loads(q.stdout.split("\n", 1)[1])["verdict"], "ADOPT")

    def test_help(self):
        h = subprocess.run([sys.executable, "-I", TOOL, "--help"], capture_output=True, text=True)
        self.assertEqual(h.returncode, 0)
        self.assertIn("REPLICATE", h.stdout)


class TestPreregS2C4(unittest.TestCase):
    def test_s2_vs_c4(self):
        p, sha = DC.load_prereg(PREREG_S2C4)
        self.assertEqual(p["arms"], {"treat": ["w10-s2"], "control": ["w4-c4"]})
        self.assertEqual((p["fork"], p["at"], p["max_at"]), (12164, [12664, 13164], 13164))
        self.assertEqual(p["primary"]["seedset"], ["dev512", "dev128"])
        self.assertEqual((p["primary"]["window"], p["primary"]["mode"]), (5, "greedy-off"))
        self.assertEqual(p["confirm"]["seedset"], "ab512")
        self.assertTrue(p["confirm"]["endpoint"])
        self.assertEqual((p["mde"], p["margin"]), (0.015, 0.01))
        self.assertEqual((p["kill"]["after_updates"], p["kill"]["delta"]), (300, -0.03))
        self.assertEqual(p["leading"], ["act3", "act2_boss_pass"])


S = synth.SCRATCH
M = os.path.join(S, "mirror")


def real(path, at, **data):
    p, _ = DC.load_prereg(path)
    ixs = {}
    for r in p["arms"]["treat"] + p["arms"]["control"]:
        paths = [os.path.join(M, r)] + [x for x in data.get(r.replace("-", "_"), [])]
        ixs[r] = EV.Index(f"{r}=" + ",".join(paths))
    return DC.decide(p, at, ixs, boot=BOOT)


@unittest.skipUnless(all(os.path.exists(os.path.join(M, r, "evals.jsonl"))
                         for r in ("w4-c", "w4-d", "w4-crep", "w4-c3", "w10-s", "w10-g")), "mirror absent")
class TestRealRegression(unittest.TestCase):
    def test_c_vs_d_u9164_not_adopt(self):
        r = real(os.path.join(FIX, "retro-r2-c-vs-d.json"), 9164)
        self.assertNotEqual(r["verdict"], "ADOPT")
        self.assertEqual(r["verdict"], "REPLICATE", r["rule"])          # EV5: single seed, window +0.019
        self.assertAlmostEqual(r["primary"]["pooled"]["delta_mean"], 0.0195, places=4)

    def test_c_crep_vs_d_reject(self):
        r = real(os.path.join(FIX, "retro-r2-c-crep-vs-d.json"), 9164)
        self.assertEqual(r["verdict"], "REJECT", r["rule"])             # after C-rep: about +0.003

    def test_s_vs_c3_u11164_continue(self):
        b3 = [os.path.join(S, "boss3")] if os.path.isdir(os.path.join(S, "boss3")) else []
        r = real(os.path.join(FIX, "retro-s-vs-c3.json"), 11164, w10_s=b3, w4_c3=b3)
        self.assertEqual(r["verdict"], "CONTINUE", r["rule"])
        self.assertAlmostEqual(r["primary"]["pooled"]["delta_mean"], -0.0082, places=4)

    def test_g_killed_at_first_read(self):
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "g.json")
            synth.wj(p, {"schema": "sts2.prereg/1", "name": "retro-g-vs-c3", "arms": {"treat": ["w10-g"],
                     "control": ["w4-c3"]}, "fork": 10664, "at": [11164], "primary": {"seedset": "dev128"},
                     "mde": 0.015, "margin": 0.01, "kill": {"after_updates": 0, "delta": -0.03}})
            r = real(p, 10700)
            self.assertEqual(r["verdict"], "KILL", r["rule"])          # G u10700 0.398 vs C3 0.475


if __name__ == "__main__":
    unittest.main()
