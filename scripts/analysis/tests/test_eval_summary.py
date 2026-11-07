"""Tests of scripts/analysis/eval_summary.py (stdlib unittest; `python3 -I tests/test_eval_summary.py` or pytest)."""

from __future__ import annotations

import io
import json
import os
import statistics
import subprocess
import sys
import tempfile
import unittest
from contextlib import redirect_stdout

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import eval_summary as ES  # noqa: E402
import synth  # noqa: E402

TOOL = os.path.join(synth.ANALYSIS, "eval_summary.py")


class TestPorts(unittest.TestCase):
    def test_progress_matches_protocol_formula(self):
        self.assertEqual(ES.compute_progress("win", 3, 49, 10), 1.0)
        self.assertEqual(ES.compute_progress("loss", 1, 17, 10, {"1": 0}, {"1": 17}), round((16 / 17) / 3, 6))
        self.assertEqual(ES.compute_progress("loss", 2, 20, 10, {"1": 0, "2": 17}, {"1": 17, "2": 16}),
                         round((1 + 2 / 16) / 3, 6))
        # act 3 at A10 without a map: default 15 + 1 rooms
        self.assertEqual(ES.default_act_rooms(3, 10), 16)
        self.assertEqual(ES.default_act_rooms(3, 0), 15)
        self.assertIsNone(ES.compute_progress("loss", None, None, 10))

    def test_stages(self):
        def st(fl, **kw):
            return ES.stages(synth.game("X", fl, **kw), 10)
        self.assertEqual(st(16), dict.fromkeys(ES.STAGE_KEYS, False))
        a1b = st(17)
        self.assertTrue(a1b["act1_boss"])
        self.assertFalse(a1b["act1_boss_beaten"])
        a2 = st(20)
        self.assertTrue(a2["act1_boss_beaten"] and a2["act2"])
        self.assertFalse(a2["act2_boss"])
        a2b = st(33)
        self.assertTrue(a2b["act2_boss"])
        self.assertFalse(a2b["act2_boss_beaten"] or a2b["act3"])
        a3 = st(36)
        self.assertTrue(a3["act2_boss_beaten"] and a3["act3"])
        self.assertFalse(a3["cleared"])
        self.assertTrue(all(st(49, won=True).values()))
        # stuck in the act-1 boss room counts as beaten (reportcard rule)
        s = synth.game("X", 17)
        s["result"] = "stuck"
        self.assertTrue(ES.stages(s, 10)["act1_boss_beaten"])

    def test_mask_roundtrip(self):
        f = ES.stages(synth.game("X", 36), 10)
        self.assertEqual(ES.mask_flags(ES.stage_mask(f)), f)

    def test_checksum_and_wilson(self):
        ss = synth.seedset(4)
        self.assertEqual(len(ES.checksum(ss)), 64)
        ss2 = dict(ss, name="other-name")
        self.assertEqual(ES.checksum(ss), ES.checksum(ss2))      # identity = content, not name
        lo, hi = ES.wilson(0, 128)
        self.assertEqual(lo, 0.0)
        self.assertAlmostEqual(hi, 0.02913799589810803, places=12)  # report.json value of a 0/128 headline


class TestSummary(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = self.tmp.name

    def tearDown(self):
        self.tmp.cleanup()

    def test_build_summary(self):
        floors = [9, 17, 20, 33, 36, 49]
        d = synth.write_eval(self.root, "w99-x", 12345, floors)
        s = ES.build_summary(d)
        self.assertEqual(s["schema"], ES.SCHEMA)
        self.assertEqual((s["run"], s["update"]), ("w99-x", 12345))
        self.assertEqual(s["run_source"], "checkpoint")
        self.assertEqual(s["seedset"]["n"], 6)
        self.assertEqual(s["seedset"]["sha256"], ES.checksum(synth.seedset(6)))
        self.assertEqual(s["mode"]["name"], "greedy-off")
        self.assertEqual(s["mode"]["macros"], ["rest", "shop", "prompt"])
        self.assertTrue(s["complete"])
        self.assertEqual(s["funnel"], {"act1_boss": 5, "act1_boss_beaten": 4, "act2": 4, "act2_boss": 3,
                                       "act2_boss_beaten": 2, "act3": 2, "cleared": 1})
        self.assertAlmostEqual(s["act2_boss_pass"], 2 / 3)
        self.assertEqual(s["wins"], 1)
        self.assertEqual(s["progress_stored_differs"], 0)
        self.assertTrue(s["report_check"]["progress_mean_match"])
        self.assertEqual(len(s["per_seed"]), 6)
        self.assertEqual(s["progress_mean"], statistics.fmean(s["per_seed"].values()))
        self.assertEqual(s["wall_s"], 12.5)
        self.assertEqual(s["weights_hash"], synth.rj(os.path.join(d, "config.json"))["weights_hash"])

    def test_identity_fallbacks(self):
        d = synth.write_eval(self.root, "w99-x", 200, [10, 20])
        cfg = synth.rj(os.path.join(d, "config.json"))
        cfg["checkpoint"] = "tmp/ck/s-copy.pt"
        cfg.pop("weights_hash")
        synth.wj(os.path.join(d, "config.json"), cfg)
        s = ES.build_summary(d)
        self.assertEqual((s["update"], s["update_source"]), (200, "path"))
        self.assertEqual(s["run"], "w99-x")                       # from report.out_dir
        self.assertTrue(s["weights_hash"])                        # from report prompt_hash
        s = ES.build_summary(d, run="r", update=7)
        self.assertEqual((s["run"], s["update"]), ("r", 7))

    def test_report_mismatch_and_incomplete(self):
        d = synth.write_eval(self.root, "w99-x", 300, [10, 20, 30])
        rep = synth.rj(os.path.join(d, "report.json"))
        rep["headline"]["progress_mean"] += 1e-9
        synth.wj(os.path.join(d, "report.json"), rep)
        os.remove(os.path.join(d, "games", "SILENT-SYN00002.summary.json"))
        s = ES.build_summary(d)
        self.assertFalse(s["report_check"]["progress_mean_match"])
        self.assertFalse(s["complete"])
        self.assertEqual(s["games_found"], 2)

    def test_backfill_and_cli(self):
        synth.make_line(self.root, "w99-a", [100, 200], n=8)
        synth.write_eval(self.root, "w99-a", 200, [12] * 8, role="ab", endpoint=True)
        out = io.StringIO()
        with redirect_stdout(out):
            r = ES.backfill([self.root], log=lambda *_: None)
        self.assertEqual(r["counts"], {"written": 3})
        self.assertEqual(ES.backfill([self.root], log=lambda *_: None)["counts"], {"exists": 3})
        self.assertEqual(ES.backfill([self.root], force=True, log=lambda *_: None)["counts"], {"written": 3})
        files = sorted(os.path.relpath(os.path.join(dp, f), self.root) for dp, _, fs in os.walk(self.root)
                       for f in fs if f == "summary.json")
        self.assertEqual(len(files), 3)
        p = subprocess.run([sys.executable, "-I", TOOL, "--backfill", self.root, "--dry-run"], capture_output=True,
                           text=True)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertEqual(json.loads(p.stdout)["counts"], {"exists": 3})

    def test_cli_single_dir(self):
        d = synth.write_eval(self.root, "w99-x", 400, [10, 20, 30])
        p = subprocess.run([sys.executable, "-I", TOOL, d], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0, p.stderr)
        self.assertIn("written", p.stdout)
        self.assertIn("report_match=True", p.stdout)
        s = synth.rj(os.path.join(d, "summary.json"))
        self.assertEqual(s["update"], 400)
        p = subprocess.run([sys.executable, "-I", TOOL, d], capture_output=True, text=True)
        self.assertIn("exists", p.stdout)
        p = subprocess.run([sys.executable, "-I", TOOL, os.path.join(self.root, "nope")], capture_output=True,
                           text=True)
        self.assertEqual(p.returncode, 2)

    def test_print_gcloud_is_valid_bash_and_not_run(self):
        p = subprocess.run([sys.executable, "-I", TOOL, "--print-gcloud", "--bucket", "gs://b/runs"],
                           capture_output=True, text=True)
        self.assertEqual(p.returncode, 0)
        self.assertIn('B=gs://b/runs', p.stdout)
        self.assertIn('gcloud storage cp "$L/summary.json" "$D/summary.json"', p.stdout)
        b = subprocess.run(["bash", "-n"], input=p.stdout, capture_output=True, text=True)
        self.assertEqual(b.returncode, 0, b.stderr)

    def test_help(self):
        p = subprocess.run([sys.executable, "-I", TOOL, "--help"], capture_output=True, text=True)
        self.assertEqual(p.returncode, 0)
        self.assertIn("--backfill", p.stdout)


REAL = os.path.join(synth.SCRATCH, "boss3", "s-u11000", "greedy-off")
REAL_JSONL = os.path.join(synth.SCRATCH, "mirror", "w10-s", "evals.jsonl")


@unittest.skipUnless(os.path.isdir(REAL), "mirrored eval data not present")
class TestRealData(unittest.TestCase):
    def test_report_parity_and_jsonl_parity(self):
        s = ES.build_summary(REAL)
        self.assertEqual((s["run"], s["update"], s["seedset"]["id"], s["games"]), ("w10-s", 11000,
                                                                                    "silent-a10-dev-v1", 128))
        self.assertTrue(s["report_check"]["progress_mean_match"])
        self.assertEqual(s["progress_stored_differs"], 0)
        self.assertEqual(s["seedset"]["sha256"], "7fada5746894c95e955061b4cac4bb72b364ecbc83bd0c684aa3389913f75e83")
        self.assertNotIn("sha256_declared", s["seedset"])
        if os.path.exists(REAL_JSONL):
            with open(REAL_JSONL) as f:
                rows = [json.loads(x) for x in f]
            row = [r for r in rows if r.get("update") == 11000 and r.get("mode") == "greedy-off"
                   and r.get("seedset") == "silent-a10-dev-v1"][0]
            self.assertEqual(row["progress_mean"], s["progress_mean"])
            self.assertEqual(int(row["act_reached"].get("3", 0)), s["funnel"]["act3"])
            self.assertEqual(row["act_reached"], s["act_reached"])


if __name__ == "__main__":
    unittest.main()
