#!/usr/bin/env python3
"""Unit tests for the classifier generators (sim/tools/inventory/classify) and the manifests they publish.

    python3 -m unittest discover -s sim/tools/inventory -p 'test_*.py'

Checks of the published files skip when a file is absent; checks that read the decompile trees skip when a
tree is absent.
"""

import collections
import glob
import importlib.util
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
CLASSIFY = os.path.join(HERE, "classify")
REPO = os.path.abspath(os.path.join(HERE, "..", "..", ".."))
INV = os.path.join(REPO, "sim", "specs", "inventory")
V111_ROOT = os.environ.get("STS2_DECOMPILE_ROOT", "/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled")
V107_ROOT = os.environ.get("STS2_V107_ROOT",
                           "/Users/Aincrad/dev-proj/hack-sts2/artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core")
PROMPT_KINDS = ["HandSelect", "HandUpgrade", "PileSelect", "ChooseCard", "SimpleGrid", "DeckUpgrade", "DeckTransform",
                "DeckEnchant", "DeckRemove", "DeckGeneric", "RewardsGrid", "Bundle", "CardReward"]
PHASES = {"Rewards", "RestSite", "EventCombat", "CrystalSphere", "Shop", "PotionUse", "AbandonRun"}
TRIAGES = {"changed", "refactor_only", "unknown"}
C20_KEYS = {
    "cards:DualWield", "cards:FiendFire", "cards:HeirloomHammer", "cards:Omnislice", "cards:SweepingBeam", "cards:Whirlwind",
    "monsters:BowlbugSilk", "monsters:BygoneEffigy", "monsters:KinFollower", "monsters:MagiKnight", "monsters:Nibbit",
    "monsters:SlumberingBeetle", "monsters:TestSubject", "encounters:TwoTailedRatsNormal", "events:ColossalFlower",
    "events:DenseVegetation", "events:EndlessConveyor", "events:Orobas", "events:TabletOfTruth", "events:Vakuu",
    "events:WelcomeToWongos",
}
_MODULES = {}


def load(rel, name):
    """Import classify/<rel> under a unique module name (several groups have a build.py)."""
    if name in _MODULES:
        return _MODULES[name]
    path = os.path.join(CLASSIFY, rel)
    for d in (CLASSIFY, os.path.dirname(path)):
        if d not in sys.path:
            sys.path.insert(0, d)
    spec = importlib.util.spec_from_file_location(name, path)
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    _MODULES[name] = mod
    return mod


def published(name):
    path = os.path.join(INV, name)
    if not os.path.exists(path):
        raise unittest.SkipTest("%s not published" % name)
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


class EvidenceTests(unittest.TestCase):
    def setUp(self):
        self.ev = load("version-deltas/evidence.py", "vd_evidence")

    def test_state_machine_plumbing_is_dropped(self):
        lines = ["\t\t\tnum = (_003C_003E1__state = 0);", "\t\t\tval = CreatureCmd.Damage(choiceContext, target, 5m).GetAwaiter();",
                 "\t\t\tgoto IL_00b4;", "\t\tTaskAwaiter val2;"]
        got = self.ev.logic_lines(lines)
        self.assertEqual([n for n, _ in got], [2])
        self.assertIn("CreatureCmd", got[0][1])
        self.assertIn("5", got[0][1])
        self.assertNotIn("GetAwaiter", got[0][1])

    def test_hoisted_locals_keep_their_name(self):
        got = self.ev.logic_lines(["\t\tCardModel card = _003Cselection_003E5__2.CreateClone();"])
        self.assertIn("selection", got[0][1])
        self.assertIn("CreateClone", got[0][1])

    def test_arrow_note_gives_sides(self):
        h = self.ev.Hints()
        h.add_note("star cost 3 -> 2; rarity Rare -> Uncommon")
        self.assertIn("3", h.side[self.ev.V107])
        self.assertIn("2", h.side[self.ev.V111])
        self.assertIn("Rare", h.side[self.ev.V107])
        self.assertIn("Uncommon", h.side[self.ev.V111])

    def test_value_change_cites_both_lines(self):
        new = ["public sealed class Alignment : CardModel", "{", "\tpublic override int CanonicalStarCost => 2;", "}"]
        old = ["public sealed class Alignment : CardModel", "{", "\tpublic override int CanonicalStarCost => 3;", "}"]
        h = self.ev.Hints()
        h.add_note("star cost 3 -> 2")
        got, method = self.ev.find(new, old, h, "Alignment")
        self.assertEqual(got[self.ev.V111], [3])
        self.assertEqual(got[self.ev.V107], [3])
        self.assertEqual(method, "token-diff")

    def test_one_sided_change_anchors_the_member(self):
        new = ["public sealed class Foo : RelicModel", "{", "\tpublic override async Task AfterTurnStart(Player player)",
               "\t{", "\t\tawait CardPileCmd.Draw(player, 2m);", "\t}", "}"]
        old = ["public sealed class Foo : RelicModel", "{", "\tpublic override async Task AfterTurnStart(Player player)",
               "\t{", "\t}", "}"]
        got, method = self.ev.find(new, old, self.ev.Hints(), "Foo")
        self.assertEqual(got[self.ev.V111], [5])
        self.assertEqual(got[self.ev.V107], [3])
        self.assertEqual(method, "token-diff+anchor")

    def test_same_tokens_fall_back_to_difflib(self):
        new = ["public sealed class Foo : RelicModel", "{", "\tpublic override decimal ModifyHandDraw(Player player, decimal count)",
               "\t{", "\t\treturn count + 1m;", "\t}", "}"]
        old = ["public sealed class Foo : RelicModel", "{", "\tpublic override decimal ModifyHandDraw(Player player, decimal count)",
               "\t{", "\t\treturn count;", "\t}", "}"]
        got, method = self.ev.find(new, old, self.ev.Hints(), "Foo")
        self.assertEqual((got[self.ev.V111], got[self.ev.V107], method), ([5], [5], "difflib"))


class TriageTableTests(unittest.TestCase):
    def setUp(self):
        self.t = load("version-deltas/triage.py", "vd_triage")

    def test_covers_the_c20_disagreements(self):
        self.assertTrue(C20_KEYS <= set(self.t.MANUAL))

    def test_adjudications_named_by_the_critique(self):
        self.assertEqual(self.t.MANUAL["events:Orobas"][0], "changed")
        self.assertEqual(self.t.MANUAL["cards:DualWield"][0], "refactor_only")

    def test_records_are_well_formed(self):
        for key, (triage, basis, note, ev) in self.t.MANUAL.items():
            self.assertIn(triage, TRIAGES, key)
            self.assertIn(basis, ("manual", "il"), key)
            self.assertTrue(note, key)
            self.assertTrue(ev[self.t.V111] and ev[self.t.V107], key)
            for c in ev[self.t.V111] + ev[self.t.V107]:
                self.assertTrue(c.startswith("IL:") or c.rsplit(":", 1)[1].isdigit(), c)


class NormalizeHelperTests(unittest.TestCase):
    def setUp(self):
        self.n = load("normalize/normalize.py", "inv_normalize")
        self.p = load("normalize/prompts.py", "inv_norm_prompts")

    def test_prompt_kinds_are_the_contract_enum(self):
        self.assertEqual(self.p.PROMPT_KINDS, PROMPT_KINDS)

    def test_topo_orders_and_groups_cycles(self):
        nodes = ["cards:1", "powers:1", "cards:2", "powers:2", "relics:1"]
        edges = [("powers:1", "cards:2"), ("cards:2", "powers:2"), ("powers:2", "cards:2"), ("cards:1", "relics:1")]
        order, cycles = self.n.topo(nodes, edges)
        self.assertEqual(sorted(order), sorted(nodes))
        self.assertLess(order.index("powers:1"), order.index("cards:2"))
        self.assertLess(order.index("cards:1"), order.index("relics:1"))
        self.assertEqual(cycles, [["cards:2", "powers:2"]])


class PublishedClassifiedTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.req = published("requirements.json")
        cls.files = sorted(glob.glob(os.path.join(INV, "*.classified.json")))
        if not cls.files:
            raise unittest.SkipTest("no classified manifests")

    def test_fifteen_files_one_schema(self):
        self.assertEqual(len(self.files), 15)
        for p in self.files:
            with open(p, encoding="utf-8") as fh:
                d = json.load(fh)
            self.assertEqual(d["schema"], "sts2sim.inventory.classified/v2", p)
            self.assertEqual(d["count"], len(d["entries"]), p)
            for g in d["generated_by"]:
                self.assertTrue(os.path.exists(os.path.join(REPO, g)), g)

    def test_entries_follow_the_schema(self):
        keys = set(self.req["keys"])
        for p in self.files:
            with open(p, encoding="utf-8") as fh:
                d = json.load(fh)
            for e in d["entries"]:
                ident = "%s %s" % (os.path.basename(p), e["id"])
                self.assertIn(e["tier"], (1, 2, 3, 4), ident)
                self.assertGreaterEqual(e["tier_with_deps"], e["tier"], ident)
                if e["tier"] != 4:
                    self.assertGreaterEqual(e["tier"], e["tier_from_requirements"], ident)
                self.assertTrue(set(e["kernel_requirements"]) <= keys, ident)
                self.assertEqual(sorted(e["dependencies"]), ["co_port", "reads", "requires"], ident)
                for pr in e["prompts"]:
                    self.assertIn(pr["kind"], PROMPT_KINDS, ident)
                for ph in e["phases"]:
                    self.assertIn(ph["phase"], PHASES, ident)

    def test_every_prompt_kind_is_used(self):
        seen = collections.Counter()
        for p in self.files:
            with open(p, encoding="utf-8") as fh:
                for e in json.load(fh)["entries"]:
                    seen.update(pr["kind"] for pr in e["prompts"])
        self.assertEqual(set(seen), set(PROMPT_KINDS))

    def test_requirement_catalogue(self):
        self.assertEqual(self.req["count"], len(self.req["keys"]))
        for k, v in self.req["keys"].items():
            self.assertIn(v["level"], (1, 2, 3, 4), k)
            self.assertIn(v["kind"], ("cmd_facade", "hook_virtual", "model_virtual", "feature"), k)


class PublishedGapManifestTests(unittest.TestCase):
    def test_generated_by_names_committed_generators(self):
        for name in ("kernel-gaps.json", "run-layer-gaps.json", "subsystems.json", "corpus-inventory.json", "batches.json",
                     "requirements.json", "version-deltas.json"):
            d = published(name)
            self.assertTrue(d.get("generated_by"), name)
            for g in d["generated_by"]:
                self.assertTrue(g.startswith("sim/tools/inventory/classify/"), g)
                self.assertTrue(os.path.exists(os.path.join(REPO, g)), g)

    def test_batches_order_covers_edges(self):
        b = published("batches.json")
        order = b["order"]
        self.assertEqual(len(order), len(set(order)))
        pos = dict((n, i) for i, n in enumerate(order))
        cyc = set(n for g in b["requires_cycles"]["groups"] for n in g)
        for e in b["edges"]:
            self.assertIn(e["from"], pos)
            self.assertIn(e["to"], pos)
            if not (e["from"] in cyc and e["to"] in cyc):
                self.assertLess(pos[e["from"]], pos[e["to"]], e)


class PublishedVersionDeltaTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.vd = published("version-deltas.json")
        cls.by_key = dict((e["key"], e) for e in cls.vd["entries"])

    def test_named_adjudications(self):
        self.assertEqual(self.by_key["events:Orobas"]["triage"], "changed")
        self.assertEqual(self.by_key["cards:DualWield"]["triage"], "refactor_only")
        for k in C20_KEYS:
            self.assertIn(self.by_key[k]["basis"], ("manual", "il"), k)

    def test_counts_and_evidence(self):
        c = self.vd["counts"]
        self.assertEqual(c["content_entries"], len(self.vd["entries"]))
        self.assertEqual(sum(c["by_triage"].values()), len(self.vd["entries"]))
        self.assertEqual(c["engine_entries"], len(self.vd["engine_entries"]))
        for e in self.vd["entries"]:
            self.assertIsInstance(e["single_player_reachable"], bool, e["key"])
            self.assertEqual(e["single_player_reachable"], "single_player_note" not in e, e["key"])
        for e in self.vd["entries"] + self.vd["engine_entries"]:
            self.assertIn(e["triage"], TRIAGES, e["key"])
            ev = e["evidence"]
            if e["delta_type"] == "modified":
                self.assertTrue(ev["v0.111.0"] and ev["v0.107.1"], e["key"])
            elif e["delta_type"] == "added":
                self.assertTrue(ev["v0.111.0"] and not ev["v0.107.1"], e["key"])
            else:
                self.assertTrue(ev["v0.107.1"] and not ev["v0.111.0"], e["key"])

    def test_union_of_both_sources(self):
        flagged = set()
        for p in glob.glob(os.path.join(INV, "*.classified.json")):
            with open(p, encoding="utf-8") as fh:
                d = json.load(fh)
            for e in d["entries"]:
                if ((e.get("version_delta_v107") or {}).get("status") or "same") != "same":
                    flagged.add("%s:%s" % (d["content_kind"], e["class"]))
        gate = published("corpus-inventory.json")["version_diff_v0_107_1_vs_v0_111_0"]
        for area, lst in gate["content_files"].items():
            flagged |= set("%s:%s" % (area, x["file"][:-3]) for x in lst)
        for side in gate["version_exclusive_content"].values():
            for area, names in side.items():
                flagged |= set("%s:%s" % (area, n) for n in names)
        self.assertEqual(flagged, set(self.by_key))
        self.assertEqual(len(self.vd["engine_entries"]), len(gate["engine_files"]))


@unittest.skipUnless(os.path.isdir(V111_ROOT) and os.path.isdir(V107_ROOT), "decompile trees absent")
class TreeBackedVersionDeltaTests(unittest.TestCase):
    def test_every_citation_resolves(self):
        vd = published("version-deltas.json")
        cache = {}
        for e in vd["entries"] + vd["engine_entries"]:
            for tree, cites in e["evidence"].items():
                for c in cites:
                    if c.startswith("IL:"):
                        continue
                    rel, ln = c.rsplit(":", 1)
                    if tree == "v0.111.0":
                        d, f = rel.split("/", 1)
                        path = os.path.join(V111_ROOT, "MegaCrit.Sts2.Core." + d, f)
                    else:
                        path = os.path.join(V107_ROOT, rel)
                    if path not in cache:
                        with open(path, encoding="utf-8-sig") as fh:
                            cache[path] = fh.read().split("\n")
                    lines = cache[path]
                    self.assertTrue(1 <= int(ln) <= len(lines) and lines[int(ln) - 1].strip(), "%s %s" % (e["key"], c))


if __name__ == "__main__":
    unittest.main()
