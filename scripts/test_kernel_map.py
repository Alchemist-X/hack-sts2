"""Tests of scripts/kernel_map.py: derived sources, curated / pattern records, staleness, output layout.

  python3 -m unittest discover -s scripts -p 'test_kernel_map.py'

A synthetic decompile tree and repo (sim Core, specs) are built in a temp dir; one check reads the committed map.
"""

import json
import os
import shutil
import sys
import tempfile
import textwrap
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import kernel_map as K  # noqa: E402


def write(root, rel, text):
    path = os.path.join(root, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(textwrap.dedent(text).lstrip("\n"))


NS = "MegaCrit.Sts2.Core."


class KernelMapTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="kmap-")
        tree = cls.tree = os.path.join(cls.tmp, "tree")
        repo = cls.repo = os.path.join(cls.tmp, "repo")
        write(tree, NS + "Commands/PowerCmd.cs", "namespace X;\npublic static class PowerCmd { }\n")
        write(tree, NS + "Entities.Creatures/Creature.cs", "namespace X;\npublic class Creature { }\n")
        write(tree, NS + "Models/CardModel.cs", "namespace X;\npublic abstract class CardModel { }\n")
        write(tree, NS + "Combat/CombatManager.cs", "namespace X;\npublic class CombatManager {\n int a;\n}\n")
        write(tree, NS + "Hooks/Hook.cs", "namespace X;\npublic static class Hook { }\n")
        write(tree, NS + "Runs/RunManager.cs", "namespace X;\npublic class RunManager { }\n")
        write(tree, NS + "Saves/SaveManager.cs", "namespace X;\npublic class SaveManager { }\n")
        write(tree, NS + "Helpers/Lonely.cs", "namespace X;\npublic class Lonely {\n" + " int a;\n" * 60 + "}\n")
        write(tree, NS + "Models.Cards/Bash.cs", "namespace X;\npublic class Bash { }\n")  # content: not kernel
        core = "sim/Sts2Sim.Core/"
        write(repo, core + "Combat/Pipeline/PowerCmd.cs", "public static class PowerCmd { }\n")
        write(repo, core + "State/CreatureState.cs", "public class CreatureState { }\n")
        write(repo, core + "Cards/CardDef.cs", "public abstract class CardDef { }\n")
        write(repo, core + "Combat/Loop/CombatLoop.cs", "public static partial class CombatLoop { }\n")
        write(repo, core + "Combat/Loop/TurnFlow.cs", "public static partial class CombatLoop { }\n")
        write(repo, core + "Combat/Pipeline/Hook.cs", "// see Hooks/Hook.cs:12\npublic static class Hook { }\n")
        write(repo, core + "Run/Loop/RunLoop.cs", "// RunManager.cs:40 is the game side\npublic static class RunLoop { }\n")
        write(repo, core + "Cards/Defs/Bash.cs", "public sealed class Hook { }\n")  # Defs never count as kernel types
        write(repo, "sim/specs/loop.md", """
            # Loop
            | Rule | Sim | Citation |
            | --- | --- | --- |
            | draw | `CombatLoop.BaseHandDrawCount` | `Combat/CombatManager.cs:3` |
            | turn | `CombatLoop.StartTurn` and `Hook.AfterTurn` | `Combat/CombatManager.cs:2` |
            | once | `RunLoop.X` | `Runs/RunManager.cs:1` |
            """)
        cls.old = {
            "patterns": [{"glob": "Saves/*", "status": "no_port", "reason": "save I/O"},
                         {"glob": "Nothing/*", "status": "no_port", "reason": "stale"}],
            "curated": {"Runs/RunManager.cs": {"status": "mapped", "sim": [core + "Run/Loop/RunLoop.cs"],
                                               "reason": "run loop"},
                        "Gone/Away.cs": {"status": "no_port", "reason": "file left the tree"}},
        }
        cls.m, cls.stale = K.build(tree, repo, cls.old, "1.1.0")
        cls.f = {e["key"]: e for e in cls.m["files"].values()}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def test_only_kernel_files(self):
        self.assertNotIn("Models.Cards/Bash.cs", self.f)
        self.assertEqual(self.m["stats"]["kernel_files"], 8)

    def test_type_sources(self):
        self.assertEqual(self.f["Commands/PowerCmd.cs"]["via"], {"sim/Sts2Sim.Core/Combat/Pipeline/PowerCmd.cs": ["type"]})
        self.assertEqual(self.f["Entities.Creatures/Creature.cs"]["sim"], ["sim/Sts2Sim.Core/State/CreatureState.cs"])
        self.assertEqual(self.f["Models/CardModel.cs"]["sim"], ["sim/Sts2Sim.Core/Cards/CardDef.cs"])

    def test_cite_source_resolves_bare_and_full_names(self):
        self.assertEqual(self.f["Hooks/Hook.cs"]["via"]["sim/Sts2Sim.Core/Combat/Pipeline/Hook.cs"], ["type", "cite"])
        self.assertNotIn("sim/Sts2Sim.Core/Cards/Defs/Bash.cs", self.f["Hooks/Hook.cs"]["sim"])

    def test_spec_lines(self):
        cm = self.f["Combat/CombatManager.cs"]
        # CombatLoop is partial over two files; two lines name it; Hook is a game type of another file: skipped
        self.assertEqual(sorted(cm["sim"]), ["sim/Sts2Sim.Core/Combat/Loop/CombatLoop.cs",
                                             "sim/Sts2Sim.Core/Combat/Loop/TurnFlow.cs"])
        self.assertEqual(cm["via"]["sim/Sts2Sim.Core/Combat/Loop/CombatLoop.cs"], ["spec:loop.md x2"])

    def test_curated_patterns_and_unmapped(self):
        rm = self.f["Runs/RunManager.cs"]
        self.assertEqual(rm["status"], "mapped")  # one spec line only (< SPEC_MIN_LINES): the curated record maps it
        self.assertEqual(rm["via"], {"sim/Sts2Sim.Core/Run/Loop/RunLoop.cs": ["cite", "curated"]})
        self.assertEqual(rm["reason"], "run loop")
        sm = self.f["Saves/SaveManager.cs"]
        self.assertEqual((sm["status"], sm["sim"], sm["pattern"], sm["reason"]), ("no_port", [], "Saves/*", "save I/O"))
        self.assertEqual(self.f["Helpers/Lonely.cs"]["status"], "unmapped")
        self.assertEqual(self.m["stats"]["by_status"], {"mapped": 6, "no_port": 1, "unmapped": 1})

    def test_stale_records_are_reported(self):
        self.assertIn("curated key not in the tree: Gone/Away.cs", self.stale)
        self.assertIn("pattern matches nothing: Nothing/*", self.stale)

    def test_dump_is_one_line_per_entry_and_round_trips(self):
        text = K.dumps(self.m)
        self.assertEqual(json.loads(text), json.loads(json.dumps(self.m)))
        lines = text.split("\n")
        self.assertEqual(sum(1 for ln in lines if ln.startswith('  "' + NS)), 8)
        out = os.path.join(self.tmp, "map.json")
        with open(out, "w", encoding="utf-8") as fh:
            fh.write(text)
        m2, _ = K.build(self.tree, self.repo, K.load_map(out), "1.1.0")
        self.assertEqual(K.dumps(m2), text)  # regeneration keeps curated / patterns and is idempotent

    def test_check_flags_unmapped_and_stale(self):
        out = os.path.join(self.tmp, "check.json")
        with open(out, "w", encoding="utf-8") as fh:
            fh.write(K.dumps(self.m))
        args = ["--tree", self.tree, "--out", out, "--repo", self.repo, "--tree-version", "1.1.0", "--check"]
        self.assertEqual(K.main(args), 1)  # Lonely.cs (64 lines) is unmapped; the stale records count too
        old = dict(self.old, curated={k: v for k, v in self.old["curated"].items() if k != "Gone/Away.cs"},
                   patterns=self.old["patterns"][:1])
        old["curated"]["Helpers/Lonely.cs"] = {"status": "no_port", "reason": "test"}
        m, stale = K.build(self.tree, self.repo, old, "1.1.0")
        self.assertEqual(stale, [])
        with open(out, "w", encoding="utf-8") as fh:
            fh.write(K.dumps(m))
        self.assertEqual(K.main(args), 0)
        with open(out, "a", encoding="utf-8") as fh:
            fh.write(" ")
        self.assertEqual(K.main(args), 1)  # the committed text differs from a regeneration


@unittest.skipUnless(os.path.exists(K.MAP_PATH), "kernel map absent")
class CommittedMapTests(unittest.TestCase):
    def test_committed_map_is_consistent(self):
        m = K.load_map(K.MAP_PATH)
        self.assertEqual(m["schema"], K.SCHEMA)
        st = m["stats"]
        self.assertEqual(st["kernel_files"], len(m["files"]))
        self.assertEqual(sum(st["by_status"].values()), len(m["files"]))
        repo = os.path.dirname(HERE)
        for rel, e in m["files"].items():
            self.assertIn(e["status"], ("mapped", "no_port", "unmapped"), rel)
            self.assertEqual(e["status"] == "mapped", bool(e["sim"]) and e["status"] != "no_port", rel)
            if e["status"] == "no_port":
                self.assertTrue(e.get("reason"), rel)
            for s in e["sim"]:
                self.assertTrue(os.path.exists(os.path.join(repo, s)), s)
        for p in m["patterns"]:
            self.assertTrue(p.get("reason"), p["glob"])


if __name__ == "__main__":
    unittest.main()
