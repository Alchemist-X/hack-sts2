"""Tests of scripts/version_delta.py: normaliser, layout keys, classification, mapping, rerun plan.

  python3 -m unittest discover -s scripts -p 'test_version_delta.py'

Synthetic trees and a synthetic repo (inventory, specs, sim Core, tests) are built in a temp dir; one check reads
the real rule-truth decompile and is skipped when it is absent.
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
import version_delta as V  # noqa: E402


def write(root, rel, text):
    path = os.path.join(root, rel)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(textwrap.dedent(text).lstrip("\n"))
    return path


CARD = """
using System;
using MegaCrit.Sts2.Core.Commands;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class {cls} : CardModel
{{
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {{
        new DamageVar({dmg}m, ValueProp.Move)
    }};

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {{
        await DamageCmd.Attack(base.DynamicVars.Damage.BaseValue).FromCard({fromcard}).Execute(choiceContext);
    }}
}}
"""


def card(cls, dmg=6, fromcard="this"):
    return CARD.format(cls=cls, dmg=dmg, fromcard=fromcard)


class NormaliserTests(unittest.TestCase):
    def test_strip_comments_keeps_literals_and_lines(self):
        src = 'a = "x // not a comment"; // gone\nb = @"c:\\\\path /* kept */ ""q"""; /* gone\nstill gone */ c = \'/\';'
        out = V.strip_comments(src)
        self.assertIn('"x // not a comment"', out)
        self.assertNotIn("gone", out)
        self.assertIn('/* kept */', out)
        self.assertIn("'/'", out)
        self.assertEqual(out.count("\n"), src.count("\n"))

    def test_interpolated_string_with_nested_string(self):
        src = 'var s = $"{(a ? "//x" : "y")} // in";  // out\n'
        out = V.strip_comments(src)
        self.assertIn('// in"', out)
        self.assertNotIn("out", out.split(";", 1)[1])

    def test_whitespace_comment_using_and_wrap_changes_are_cosmetic(self):
        a = "using A;\nint  f(int x)\n{\n    return x+1; // one\n}\n"
        b = "using A;\nusing B.C;\n/* doc */\nint f( int x ) { return x + 1; }\n"
        self.assertEqual(V.signature(V.normalise_lines(a)), V.signature(V.normalise_lines(b)))

    def test_real_change_is_not_cosmetic(self):
        a = V.normalise_lines("new BlockVar(10m, ValueProp.Unpowered);")
        b = V.normalise_lines("new BlockVar(12m, ValueProp.Unpowered);")
        self.assertNotEqual(V.signature(a), V.signature(b))

    def test_line_diff_reports_original_line_numbers(self):
        old = V.normalise_lines("// header\n\nint a = 1;\nint b = 2;\n")
        new = V.normalise_lines("int a = 1;\n\n\nint b = 3;\n")
        diff, ch_old, ch_new, pats = V.line_diff(old, new)
        self.assertEqual(ch_old, {4})
        self.assertEqual(ch_new, {4})
        self.assertIn("-     4  int b = 2;", diff)
        self.assertIn("+     4  int b = 3;", diff)
        self.assertIn(("~", "b =", "2", "3"), pats)


class LayoutAndClassifyTests(unittest.TestCase):
    def test_canonical_key_both_layouts(self):
        self.assertEqual(V.canonical_key("MegaCrit.Sts2.Core.Models.Cards/Bash.cs"), "Models.Cards/Bash.cs")
        self.assertEqual(V.canonical_key("MegaCrit/sts2/Core/Models/Cards/Bash.cs"), "Models.Cards/Bash.cs")
        self.assertEqual(V.canonical_key("MegaCrit.sts2.Core.Nodes.TopBar/NTopBar.cs"), "Nodes.TopBar/NTopBar.cs")
        self.assertEqual(V.canonical_key("MegaCrit/sts2/GameInfo/Objects/X.cs"), "GameInfo.Objects/X.cs")
        self.assertEqual(V.canonical_key("NDefectVfx.cs"), "NDefectVfx.cs")

    def test_normalise_citation_forms(self):
        self.assertEqual(V.normalise_citation("Models/Potions/FairyInABottle.cs"), "Models.Potions/FairyInABottle.cs")
        self.assertEqual(V.normalise_citation(
            "artifacts/knowledge/decompiled-9cb4f1ad/MegaCrit.Sts2.Core.Models.Monsters/KnowledgeDemon.cs"),
            "Models.Monsters/KnowledgeDemon.cs")
        self.assertEqual(V.normalise_citation("Bash.cs"), "Bash.cs")

    def test_classify(self):
        c = V.classify
        self.assertEqual(c("Models.Cards/Bash.cs")[:2], ("content", "cards"))
        self.assertEqual(c("Models.Monsters/LeafSlimeM.cs")[:2], ("content", "monsters"))
        self.assertEqual(c("Models.Cards.Mocks/MockAttack.cs")[:2], ("out_of_scope", "test_mocks"))
        self.assertEqual(c("Models/CardModel.cs")[:2], ("kernel", "models_base"))
        self.assertEqual(c("Commands/PowerCmd.cs")[:2], ("kernel", "commands"))
        self.assertEqual(c("Localization.DynamicVars/DamageVar.cs")[:2], ("kernel", "dynamic_vars"))
        self.assertEqual(c("Localization/LocString.cs")[0], "out_of_scope")
        self.assertEqual(c("Saves.Migrations/Foo.cs")[:2], ("kernel", "save"))
        self.assertEqual(c("Saves.Managers/RunSaveManager.cs")[0], "out_of_scope")
        self.assertEqual(c("Entities.Rngs/RunRngSet.cs")[:2], ("kernel", "rng"))
        self.assertEqual(c("GameActions.Multiplayer/PlayerChoiceContext.cs")[:2], ("kernel", "game_actions"))
        self.assertEqual(c("Nodes.Vfx/NFoo.cs")[0], "out_of_scope")
        self.assertEqual(c("Map/StandardActMap.cs")[:2], ("kernel", "map"))
        self.assertEqual(c("Weird.Thing/X.cs")[0], "other")

    def test_param_signature_drops_names_defaults_and_qualifiers(self):
        self.assertEqual(V.param_signature(
            "PlayerChoiceContext choiceContext, System.Collections.Generic.IEnumerable<Creature>? targets, "
            "decimal amount, bool silent = false"),
            "PlayerChoiceContext,IEnumerable<Creature>?,decimal,bool")

    def test_members_overlap_overloads(self):
        self.assertTrue(V.members_overlap(["P.Apply(int)"], {"P.Apply(int)"}))
        self.assertFalse(V.members_overlap(["P.Apply(int)"], {"P.Apply(string)"}))
        self.assertTrue(V.members_overlap(["P.Apply"], {"P.Apply(string)"}))


class PipelineTests(unittest.TestCase):
    """Old tree in the nested layout, new tree flattened, plus a miniature repo."""

    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="vdelta-")
        old = os.path.join(cls.tmp, "old")
        new = os.path.join(cls.tmp, "new")
        repo = os.path.join(cls.tmp, "repo")
        on = "MegaCrit/sts2/Core/"
        nn = "MegaCrit.Sts2.Core."
        # Bash: rule change (damage 8 -> 9) + the bulk refactor; Strike* x9: bulk refactor only
        write(old, on + "Models/Cards/Bash.cs", card("Bash", 8))
        write(new, nn + "Models.Cards/Bash.cs", card("Bash", 9, "this, cardPlay"))
        for i in range(9):
            write(old, on + "Models/Cards/Strike%d.cs" % i, card("Strike%d" % i))
            write(new, nn + "Models.Cards/Strike%d.cs" % i, card("Strike%d" % i, fromcard="this, cardPlay"))
        # cosmetic only
        write(old, on + "Models/Relics/Anchor.cs", "class Anchor { int x = 10; }\n")
        write(new, nn + "Models.Relics/Anchor.cs", "// reformatted\nclass Anchor\n{\n    int x = 10;\n}\n")
        # added / removed content
        write(new, nn + "Models.Cards/Abundance.cs", card("Abundance"))
        write(old, on + "Models/Cards/Scare.cs", card("Scare"))
        # kernel: rng change, out of scope node, moved event
        write(old, on + "Random/Rng.cs", "class Rng {\n    int Next() { return 1; }\n}\n")
        write(new, nn + "Random/Rng.cs", "class Rng {\n    int Next() { return 2; }\n}\n")
        # kernel files the kernel map resolves: CombatManager (map -> CombatLoop), SaveManager (pattern: no_port),
        # FooCmd (added, not in the map: unmapped)
        write(old, on + "Combat/CombatManager.cs", "class CombatManager {\n    int draw = 5;\n}\n")
        write(new, nn + "Combat/CombatManager.cs", "class CombatManager {\n    int draw = 6;\n}\n")
        write(old, on + "Saves/SaveManager.cs", "class SaveManager { int v = 1; }\n")
        write(new, nn + "Saves/SaveManager.cs", "class SaveManager { int v = 2; }\n")
        write(new, nn + "Commands/FooCmd.cs", "class FooCmd {\n    void Foo() { }\n}\n")
        write(old, on + "Nodes/Vfx/NFoo.cs", "class NFoo { int a; }\n")
        write(new, nn + "Nodes.Vfx/NFoo.cs", "class NFoo { int b; }\n")
        write(old, on + "Models/Events/OldPlace/Wongo.cs", "class Wongo { int gold = 1; }\n")
        write(new, nn + "Models.Events/Wongo.cs", "class Wongo { int gold = 2; }\n")
        # repo: inventory, sim Core, specs, tests
        inv = {"entries": [{"file": "Models.Cards/Bash.cs", "model_id": "CARD.BASH", "id": "BASH", "class": "Bash"}]}
        write(repo, "sim/specs/inventory/cards.json", json.dumps(inv))
        write(repo, "sim/Sts2Sim.Core/Cards/Defs/Bash.cs", "public sealed class Bash : CardDef {}\n")
        write(repo, "sim/Sts2Sim.Core/Random/Rng.cs", "// port of Random/Rng.cs:1\npublic sealed class Rng {}\n")
        write(repo, "sim/Sts2Sim.Core/Combat/Loop/CombatLoop.cs", "public static class CombatLoop {}\n")
        kmap = {"schema": "sts2sim.kernel-map/v1", "tree": "t", "tree_version": "1.1.0",
                "stats": {"kernel_files": 3, "by_status": {"mapped": 2, "no_port": 1}},
                "patterns": [{"glob": "Saves/*", "status": "no_port", "reason": "save I/O"}],
                "curated": {},
                "files": {"MegaCrit.Sts2.Core.Combat/CombatManager.cs": {
                    "key": "Combat/CombatManager.cs", "area": "combat", "lines": 3, "status": "mapped",
                    "sim": ["sim/Sts2Sim.Core/Combat/Loop/CombatLoop.cs", "sim/Sts2Sim.Core/Gone.cs"]},
                    "MegaCrit.Sts2.Core.Random/Rng.cs": {"key": "Random/Rng.cs", "area": "rng", "lines": 3,
                                                         "status": "mapped", "sim": ["sim/Sts2Sim.Core/Random/Rng.cs"]}}}
        write(repo, "sim/specs/kernel-map.json", json.dumps(kmap))
        write(repo, "sim/specs/cards.md", """
            # Cards
            ## 1. Bash
            Damage 8 (`Models.Cards/Bash.cs:10`), vars `Models.Cards/Bash.cs:8`; play body `Models.Cards/Bash.cs:14-17`.
            ## 2. Rng
            Next (`Random/Rng.cs:2`).
            """)
        write(repo, "sim/Sts2Sim.Tests/CardTests.cs", """
            public static class CardTests
            {
                public static void Test_Bash_Deals8()
                {
                    var id = "BASH";
                }
                public static void Test_Other() { }
            }
            """)
        cls.man = V.run(old, new, repo=repo, write=False, old_version="1.0.0", new_version="1.1.0", cite_tree=new)
        cls.by_key = {f["key"]: f for f in cls.man["files"]}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def test_deltas(self):
        b = self.by_key
        self.assertEqual(b["Models.Cards/Bash.cs"]["delta"], "modified")
        self.assertEqual(b["Models.Relics/Anchor.cs"]["delta"], "cosmetic")
        self.assertEqual(b["Models.Cards/Abundance.cs"]["delta"], "added")
        self.assertEqual(b["Models.Cards/Scare.cs"]["delta"], "removed")
        self.assertEqual(b["Models.Events/Wongo.cs"]["delta"], "moved")
        self.assertEqual(b["Models.Events/Wongo.cs"]["old_key"], "Models.Events.OldPlace/Wongo.cs")
        self.assertEqual(b["Nodes.Vfx/NFoo.cs"]["scope"], "out_of_scope")
        self.assertNotIn("diff", b["Nodes.Vfx/NFoo.cs"])

    def test_mapping(self):
        bash = self.by_key["Models.Cards/Bash.cs"]
        self.assertEqual([m["model_id"] for m in bash["models"]], ["CARD.BASH"])
        self.assertEqual(bash["sim_files"], ["sim/Sts2Sim.Core/Cards/Defs/Bash.cs"])
        self.assertEqual(bash["tests"], ["CardTests.Test_Bash_Deals8"])
        self.assertEqual(self.by_key["Models.Cards/Abundance.cs"]["models"][0]["model_id"], "CARD.ABUNDANCE")
        self.assertEqual(self.by_key["Random/Rng.cs"]["sim_files"], ["sim/Sts2Sim.Core/Random/Rng.cs"])

    def test_kernel_map(self):
        cm = self.by_key["Combat/CombatManager.cs"]
        self.assertEqual(cm["sim_files"], ["sim/Sts2Sim.Core/Combat/Loop/CombatLoop.cs"])  # missing sim file dropped
        self.assertEqual(cm["kernel_map"], {"status": "mapped", "source": "map"})
        self.assertTrue(cm["sim_files_from_map_only"])
        sm = self.by_key["Saves/SaveManager.cs"]
        self.assertEqual((sm["kernel_map"]["status"], sm["kernel_map"]["source"]), ("no_port", "pattern:Saves/*"))
        self.assertEqual(sm["kernel_map"]["reason"], "save I/O")
        self.assertEqual(self.by_key["Commands/FooCmd.cs"]["kernel_map"]["status"], "unmapped")
        self.assertFalse(self.by_key["Random/Rng.cs"]["sim_files_from_map_only"])
        kc = self.man["kernel_coverage"]
        self.assertEqual(kc["changed_kernel_files"], 4)
        self.assertEqual(kc["by_status"], {"mapped": 2, "no_port": 1, "unmapped": 1})
        self.assertEqual(kc["unmapped"], [{"key": "Commands/FooCmd.cs", "delta": "added", "lines": 4}])
        self.assertEqual([u["key"] for u in kc["no_port"]], ["Saves/SaveManager.cs"])
        self.assertEqual(kc["with_sim_files"], 2)
        self.assertEqual(kc["map_coverage"]["kernel_files"], 3)
        self.assertTrue(any("Commands/FooCmd.cs" in n and "kernel-map.json" in n for n in self.man["rerun_plan"]["notes"]))
        md = V.render_report(self.man)
        self.assertIn("## Kernel map coverage", md)
        self.assertIn("| `Commands/FooCmd.cs` | added | 4 |", md)

    def test_kernel_map_entry_resolution(self):
        km = {"path": "k", "files": {}, "curated": {"Runs/X.cs": {"sim": ["a.cs"], "reason": "r"}},
              "patterns": [{"glob": "MegaCrit.Sts2.Core.Saves.Migrations*/*", "status": "no_port", "reason": "m"}]}
        self.assertEqual(V.kernel_map_entry(km, "Runs/X.cs"),
                         {"status": "mapped", "sim": ["a.cs"], "reason": "r", "source": "curated"})
        self.assertEqual(V.kernel_map_entry(km, "Saves.Migrations.Foo/Y.cs")["status"], "no_port")  # rel-form glob
        self.assertEqual(V.kernel_map_entry(km, "Runs/Z.cs")["status"], "unmapped")
        self.assertIsNone(V.kernel_map_entry(None, "Runs/Z.cs"))

    def test_spec_citations(self):
        hits = {h["cite"]: h["hit"] for h in self.by_key["Models.Cards/Bash.cs"]["spec_cites"]}
        self.assertEqual(hits["Models.Cards/Bash.cs:10"], "line")
        self.assertEqual(hits["Models.Cards/Bash.cs:8"], "member")
        self.assertEqual(hits["Models.Cards/Bash.cs:14-17"], "line")
        rng = self.by_key["Random/Rng.cs"]["spec_cites"]
        self.assertEqual([(h["hit"], h["section"]) for h in rng], [("line", "2. Rng")])

    def test_member_hit_via_separate_cite_tree(self):
        ctx = {"tree": "x", "index": {}, "spans": {"Models.Cards/Bash.cs": [(1, 5, "Bash.CanonicalVars"),
                                                                             (6, 30, "Bash.OnPlay(int)")]},
               "same": None}
        f = {"key": "Models.Cards/Bash.cs", "old_key": "Models.Cards/Bash.cs", "delta": "modified"}
        cites = {"Models.Cards/Bash.cs": [{"doc": "d", "md_line": 1, "section": "s", "cite": "c1", "lo": 20,
                                           "hi": 22}]}
        res = V.match_citations(f, cites, {}, ctx, {"Bash.OnPlay(int)"}, [], [])
        self.assertEqual(res[0]["hit"], "member")
        res = V.match_citations(f, cites, {}, ctx, {"Bash.CanonicalVars"}, [], [])
        self.assertEqual(res[0]["hit"], "file")

    def test_bulk_patterns(self):
        pats = self.man["bulk_edits"]["patterns"]
        self.assertTrue(any(p["files"] == 10 and "cardPlay" in p["pattern"] for p in pats), pats)
        self.assertTrue(self.by_key["Models.Cards/Strike0.cs"]["bulk_only"])
        bash = self.by_key["Models.Cards/Bash.cs"]
        self.assertFalse(bash["bulk_only"])
        self.assertEqual(bash["unique_edits"], 1)

    def test_rerun_plan(self):
        p = self.man["rerun_plan"]
        self.assertTrue(p["full_suite"])  # kernel (rng) changed
        self.assertIn("CardTests", p["test_filters"])
        self.assertEqual(p["content_soak"], ["Test_ContentSoak_EveryCard"])
        self.assertTrue(p["g0_combat_diff"])
        self.assertTrue(p["g3_seed_replay"])
        self.assertTrue(p["registry_version_filter"]["must_move"])
        self.assertEqual(sorted(p["registry_version_filter"]["version_exclusive_candidates"]),
                         ["CARD.ABUNDANCE", "CARD.SCARE"])
        self.assertTrue(p["knowledge_export"]["must_move"])
        self.assertTrue(any("Saves.Migrations" in n for n in p["notes"]))

    def test_plan_for_event_only_change(self):
        files = [{"key": "Models.Events/Wongo.cs", "delta": "modified", "scope": "content", "kind": "events",
                  "tests": []}]
        p = V.rerun_plan(files, "1.0.0", "1.0.0")
        self.assertFalse(p["full_suite"])
        self.assertFalse(p["g0_combat_diff"])
        self.assertTrue(p["g3_seed_replay"])
        self.assertTrue(p["run_soak"])
        self.assertEqual(p["run_soak_filter"], "Test_RunSoak_RandomRuns")
        self.assertEqual(p["content_soak"], [])
        self.assertFalse(p["registry_version_filter"]["must_move"])
        self.assertFalse(p["knowledge_export"]["must_move"])

    def test_report_renders(self):
        md = V.render_report(self.man)
        self.assertIn("## Rerun plan", md)
        self.assertIn("`Models.Cards/Bash.cs`", md)
        self.assertIn("```diff", md)


@unittest.skipUnless(os.path.isdir(V.DEFAULT_CITE_TREE), "rule-truth decompile absent")
class RuleTruthTreeTests(unittest.TestCase):
    def test_state_machine_maps_to_its_method_overload(self):
        path = os.path.join(V.DEFAULT_CITE_TREE, "MegaCrit.Sts2.Core.Models.Cards", "Bash.cs")
        spans = V.member_spans(path)
        owners = {o for _, _, o in spans}
        self.assertIn("Bash.OnPlay(PlayerChoiceContext,CardPlay)", owners)
        self.assertNotIn("Bash._003COnPlay_003Ed__5", owners)
        # the state machine struct (first member) is owned by OnPlay
        self.assertEqual(spans[0][2], "Bash.OnPlay(PlayerChoiceContext,CardPlay)")


if __name__ == "__main__":
    unittest.main()
