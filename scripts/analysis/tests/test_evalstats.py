"""Tests of scripts/analysis/evalstats.py (stdlib unittest; `python3 -I tests/test_evalstats.py` or pytest)."""

from __future__ import annotations

import json
import math
import os
import statistics
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import eval_summary as ES  # noqa: E402
import evalstats as EV  # noqa: E402
import synth  # noqa: E402

TOOL = os.path.join(synth.ANALYSIS, "evalstats.py")


class TestStats(unittest.TestCase):
    def test_t_ppf(self):
        self.assertAlmostEqual(EV.t_ppf(0.975, 4), 2.776445, places=5)
        self.assertAlmostEqual(EV.t_ppf(0.975, 127), 1.978815, places=5)
        self.assertAlmostEqual(EV.t_ppf(0.5, 10), 0.0, places=6)

    def test_percentile_like_numpy(self):
        xs = [1.0, 2.0, 3.0, 4.0]
        self.assertEqual(EV.percentile(xs, 0), 1.0)
        self.assertEqual(EV.percentile(xs, 100), 4.0)
        self.assertAlmostEqual(EV.percentile(xs, 2.5), 1.075)
        self.assertAlmostEqual(EV.percentile(xs, 97.5), 3.925)

    def test_diff_stats(self):
        d = [0.1, -0.05, 0.0, 0.2, 0.05]
        s = EV.diff_stats(d)
        self.assertAlmostEqual(s["delta_mean"], 0.06)
        half = EV.t_ppf(0.975, 4) * statistics.stdev(d) / math.sqrt(5)
        self.assertAlmostEqual(s["t_ci95"][0], 0.06 - half)
        self.assertEqual((s["better"], s["worse"], s["same"]), (3, 1, 1))
        lo, hi = s["bootstrap_ci95"]
        self.assertTrue(-0.05 <= lo <= 0.06 <= hi <= 0.2)
        self.assertEqual(EV.diff_stats(d)["bootstrap_ci95"], s["bootstrap_ci95"])  # deterministic (seed 0)

    def test_model_ci(self):
        m = EV.model_ci(0.02, 128, 5, 5)
        self.assertAlmostEqual(m["se"], math.sqrt(2 * 0.0156 / 5 / 128))
        self.assertAlmostEqual(m["ci95"][1] - m["ci95"][0], 2 * 1.96 * m["se"])
        # the EV1 table: 128x1 paired half-width 0.031, 128x5 0.014
        self.assertAlmostEqual(1.96 * EV.model_ci(0, 128, 1, 1)["se"], 0.031, places=3)
        self.assertAlmostEqual(1.96 * EV.model_ci(0, 128, 5, 5)["se"], 0.014, places=3)

    def test_ols(self):
        o = EV.ols([0, 1, 2, 3], [1.0, 3.0, 5.0, 7.0])
        self.assertAlmostEqual(o["slope"], 2.0)
        self.assertAlmostEqual(o["se"], 0.0)


class TestSeedsets(unittest.TestCase):
    def test_alias(self):
        dev = {"id": "silent-a10-dev-v1", "name": "silent-a10-dev-s128", "n": 128}
        ab = {"id": "silent-a10-ab-v1", "name": "silent-a10-ab-s512", "n": 512}
        for spec in ("dev128", "dev-128", "silent-a10-dev-v1", "silent-a10-dev-s128"):
            self.assertTrue(EV.seedset_matches(spec, dev), spec)
        self.assertFalse(EV.seedset_matches("dev512", dev))
        self.assertFalse(EV.seedset_matches("ab512", dev))
        self.assertTrue(EV.seedset_matches("ab-512", ab))
        self.assertFalse(EV.seedset_matches("dev128", {"id": "silent-a10-v1", "n": 20}))
        self.assertTrue(EV.is_blind({"id": "silent-a10-blind-v1", "n": 256}))

    def test_blind_refused(self):
        with tempfile.TemporaryDirectory() as root:
            synth.make_line(root, "w99-b", [100], n=4, role="blind")
            ix = EV.Index(os.path.join(root, "w99-b"))
            with self.assertRaises(PermissionError):
                ix.points("blind4", "greedy-off")
            self.assertEqual(len(ix.points("blind4", "greedy-off", allow_blind=True)), 1)


class TestWindowPair(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.root = cls.tmp.name
        ups = [1000, 1100, 1200, 1300, 1400, 1500, 1564]
        synth.make_line(cls.root, "w99-c", ups, n=64, shift=0, seed=1)
        synth.make_line(cls.root, "w99-t", ups, n=64, shift=2, seed=2)

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def ix(self, run):
        return EV.Index(f"{run}=" + os.path.join(self.root, run))

    def test_window_selects_k_latest_after_fork(self):
        w = EV.window(self.ix("w99-c"), "dev64", 1564, k=5, after=1000)
        self.assertEqual(w["updates"], [1200, 1300, 1400, 1500, 1564])
        self.assertEqual(w["method"], "per-seed")
        self.assertEqual(w["games"], 64)
        w3 = EV.window(self.ix("w99-c"), "dev64", 1250, k=5, after=1000)
        self.assertEqual(w3["updates"], [1100, 1200])
        self.assertAlmostEqual(w["mean"], statistics.fmean(w["point_means"]))

    def test_pair_per_seed(self):
        wa = EV.window(self.ix("w99-c"), "dev64", 1564)
        wb = EV.window(self.ix("w99-t"), "dev64", 1564)
        p = EV.paired(wa, wb)
        self.assertEqual(p["ci_method"], "per-seed")
        self.assertAlmostEqual(p["delta_mean"], wb["mean"] - wa["mean"])
        self.assertGreater(p["delta_mean"], 0.02)                     # +2 floors ~ +0.04 progress
        self.assertGreater(p["ci95"][0], 0)
        self.assertTrue(p["aligned"])
        self.assertIn("act3", p["leading"])
        self.assertIsNotNone(p["leading"]["act2_boss_pass"]["delta"])
        # single checkpoints: McNemar discordant counts
        p1 = EV.paired(EV.window(self.ix("w99-c"), "dev64", 1564, k=1), EV.window(self.ix("w99-t"), "dev64", 1564, k=1))
        mc = p1["leading"]["act3"]["mcnemar"]
        self.assertAlmostEqual((mc["b_only"] - mc["a_only"]) / 64, p1["leading"]["act3"]["delta_mean"])

    def test_summary_json_equals_games_dir(self):
        with tempfile.TemporaryDirectory() as root:
            synth.make_line(root, "w99-s", [100, 200], n=16, seed=3)
            a = EV.window(EV.Index(os.path.join(root, "w99-s")), "dev16", 200, k=2)
            ES.backfill([root], log=lambda *_: None)
            b = EV.window(EV.Index(os.path.join(root, "w99-s")), "dev16", 200, k=2)
            self.assertTrue(all(x.endswith("summary.json") for x in b["sources"]))
            self.assertEqual(a["per_seed"], b["per_seed"])
            self.assertEqual(a["lead_per_seed"], b["lead_per_seed"])

    def test_means_fallback_and_mismatch(self):
        with tempfile.TemporaryDirectory() as root:
            run = os.path.join(root, "w99-m")
            os.makedirs(run)
            rows = [{"update": u, "seedset": "silent-a10-dev-v1", "mode": "greedy-off", "games": 128,
                     "progress_mean": 0.40 + 0.001 * i, "act_reached": {"1": 40, "2": 80, "3": 8}}
                    for i, u in enumerate([100, 200, 300, 400, 500])]
            rows.append({"update": 500, "seedset": "silent-a10-v1", "games": 20, "progress_mean": 0.5})
            with open(os.path.join(run, "evals.jsonl"), "w") as f:
                f.write("\n".join(json.dumps(r) for r in rows) + "\n")
            ix = EV.Index(run)
            self.assertEqual(ix.label, "w99-m")
            w = EV.window(ix, "dev128", 500)
            self.assertEqual(w["method"], "means")
            self.assertAlmostEqual(w["mean"], 0.402)
            self.assertAlmostEqual(w["act3"], 8 / 128)
            p = EV.paired(w, w)
            self.assertEqual(p["ci_method"], "variance-model")
            self.assertAlmostEqual(p["delta_mean"], 0.0)
            self.assertIsNone(p["leading"]["act2_boss_pass"]["delta"])
            s = EV.slope(ix, "dev128")
            self.assertEqual(s["method"], "means")
            self.assertAlmostEqual(s["slope"], 0.005)                  # 0.001 per 100 updates
            # a per-seed dir of the same checkpoint with a different mean -> mean_mismatch warning
            synth.write_eval(root, "w99-m", 500, [20] * 128)
            warn = []
            ix2 = EV.Index("w99-m=" + run, warn)
            pts = ix2.points("dev128", "greedy-off")
            self.assertEqual(pts[-1]["source"], "summary.json")
            self.assertTrue(any(x.startswith("mean_mismatch") for x in warn))

    def test_run_filter_and_mixed_runs(self):
        with self.assertRaises(ValueError):
            EV.Index(self.root)                                       # two runs, no RUN=
        self.assertEqual({e["run"] for e in EV.Index("w99-t=" + self.root).entries.values()}, {"w99-t"})

    def test_slope_per_seed(self):
        with tempfile.TemporaryDirectory() as root:
            base = synth.base_floors(32)
            for i, u in enumerate([100, 200, 300, 400, 500, 600]):
                synth.write_eval(root, "w99-l", u, [f + i for f in base])
            s = EV.slope(EV.Index(os.path.join(root, "w99-l")), "dev32")
            self.assertEqual(s["method"], "per-seed")
            self.assertAlmostEqual(s["slope"], s["means_ols"]["slope"])
            self.assertGreater(s["ci95"][0], 0.05)
            self.assertFalse(s["plateau"])

    def test_cli(self):
        a = "w99-c=" + os.path.join(self.root, "w99-c")
        b = "w99-t=" + os.path.join(self.root, "w99-t")
        p = subprocess.run([sys.executable, "-I", TOOL, "pair", a, b, "--seedset", "dev64", "--at", "1564",
                            "--json", "-"], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0, p.stderr)
        first, rest = p.stdout.split("\n{", 1)
        self.assertIn("pair w99-t - w99-c", p.stdout)
        r = json.loads("{" + rest)
        self.assertNotIn("per_seed_delta", r["pair"])                 # stripped without --full
        for cmd in (["window", a, "--seedset", "dev64", "--at", "1564"], ["slope", a, "--seedset", "dev64"],
                    ["index", a]):
            q = subprocess.run([sys.executable, "-I", TOOL] + cmd, capture_output=True, text=True)
            self.assertEqual(q.returncode, 0, q.stderr)
        for sub in ([], ["window"], ["pair"], ["slope"], ["index"]):
            h = subprocess.run([sys.executable, "-I", TOOL] + sub + ["--help"], capture_output=True, text=True)
            self.assertEqual(h.returncode, 0)
        e = subprocess.run([sys.executable, "-I", TOOL, "window", "/nonexistent", "--at", "1"], capture_output=True,
                           text=True)
        self.assertEqual(e.returncode, 2)


S = synth.SCRATCH
M = os.path.join(S, "mirror")


def have(*parts):
    return os.path.exists(os.path.join(S, *parts))


class TestRealData(unittest.TestCase):
    """Regression values from the mirrored / pulled data (2026-11-06); skipped where the data is absent."""

    @unittest.skipUnless(have("boss2", "u0010500"), "boss2 per-game data absent")
    def test_adjacent_checkpoints_c(self):
        ix = EV.Index("w4-c=" + os.path.join(S, "boss2"))
        p = EV.paired(EV.window(ix, "dev128", 10400, k=1), EV.window(ix, "dev128", 10500, k=1))
        self.assertAlmostEqual(p["delta_sd"], 0.168015, places=5)     # EV1: sd_diff 0.168, corr 0.400
        self.assertAlmostEqual(p["corr"], 0.400137, places=5)

    @unittest.skipUnless(have("boss3", "s-u11000"), "boss3 per-game data absent")
    def test_s_vs_c3_matches_compare_py(self):
        b3 = os.path.join(S, "boss3")
        p = EV.paired(EV.window(EV.Index("w4-c3=" + b3), "dev128", 11300, k=1),
                      EV.window(EV.Index("w10-s=" + b3), "dev128", 11000, k=1))
        # sts2train compare c3-u11300 s-u11000: -0.0131 [-0.0448, +0.0185] [-0.0448, +0.0178] 40/44/44
        self.assertAlmostEqual(p["delta_mean"], -0.0131, places=4)
        self.assertAlmostEqual(p["t_ci95"][0], -0.0448, places=4)
        self.assertAlmostEqual(p["t_ci95"][1], 0.0185, places=4)
        self.assertEqual((p["better"], p["worse"], p["same"]), (40, 44, 44))
        self.assertAlmostEqual(p["bootstrap_ci95"][0], -0.0448, delta=0.002)
        self.assertAlmostEqual(p["corr"], 0.4213, places=4)

    @unittest.skipUnless(have("mirror", "w4-c", "evals.jsonl") and have("mirror", "w4-d", "evals.jsonl"),
                         "mirror absent")
    def test_c_vs_d_window_and_c_slope(self):
        wd = EV.window(EV.Index(os.path.join(M, "w4-d")), "dev128", 9164, after=7664)
        wc = EV.window(EV.Index(os.path.join(M, "w4-c")), "dev128", 9164, after=7664)
        self.assertEqual(wc["updates"], [8800, 8900, 9000, 9100, 9164])
        p = EV.paired(wd, wc)
        self.assertAlmostEqual(wc["mean"], 0.463, places=3)           # EV1: C 0.463, D 0.444 (+0.019)
        self.assertAlmostEqual(wd["mean"], 0.444, places=3)
        self.assertAlmostEqual(p["delta_mean"], 0.0195, places=4)
        s = EV.slope(EV.Index(os.path.join(M, "w4-c")), "dev128", lo=9164, hi=10664)
        self.assertAlmostEqual(s["slope"], 0.0096, places=4)          # EV1: +0.0096 / 500

    @unittest.skipUnless(have("mirror", "w4-c3", "evals.jsonl"), "mirror absent")
    def test_c_lineage_plateau(self):
        ix = EV.merge_lineage([EV.Index(os.path.join(M, "w4-c")), EV.Index(os.path.join(M, "w4-c3"))])
        s = EV.slope(ix, "dev128", lo=10664, hi=11900)
        self.assertAlmostEqual(s["slope"], 0.0017, places=4)          # EV1: +0.0017 / 500
        self.assertLess(s["ci95"][0], 0)


if __name__ == "__main__":
    unittest.main()
