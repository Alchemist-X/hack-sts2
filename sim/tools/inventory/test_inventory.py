#!/usr/bin/env python3
"""Unit tests for the inventory tooling (stdlib unittest).

    python3 -m unittest discover -s sim/tools/inventory -p 'test_*.py'

Tests that need the decompile tree are skipped when it is absent.
"""

import os
import sys
import tempfile
import unittest

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import cs_source  # noqa: E402
import extract_models  # noqa: E402
from model_ids import slugify, slugify_category, snake_case, model_id  # noqa: E402

DOTNET_VECTORS = [
    ("StrikeIronclad", "STRIKE_IRONCLAD", "strike_ironclad"),
    ("LeafSlimeM", "LEAF_SLIME_M", "leaf_slime_m"),
    ("ExpectAFight", "EXPECT_A_FIGHT", "expect_a_fight"),
    ("FairyInABottle", "FAIRY_IN_A_BOTTLE", "fairy_in_a_bottle"),
    ("GhostInAJar", "GHOST_IN_A_JAR", "ghost_in_a_jar"),
    ("BattlewornDummyEventV1Encounter", "BATTLEWORN_DUMMY_EVENT_V1_ENCOUNTER", "battleworn_dummy_event_v1_encounter"),
    ("SkillIronclad1Achievement", "SKILL_IRONCLAD1_ACHIEVEMENT", "skill_ironclad1_achievement"),
    ("TheAdversaryMkOne", "THE_ADVERSARY_MK_ONE", "the_adversary_mk_one"),
    ("ABC", "A_B_C", "a_b_c"),
    ("ABCD", "A_BC_D", "a_bc_d"),
    ("ABCDE", "A_BC_D_E", "a_bc_d_e"),
    ("ABCDef", "A_BC_DEF", "a_bc_def"),
    ("HTMLParser", "H_TM_L_PARSER", "h_tm_l_parser"),
    ("A1B2C3", "A1_B2_C3", "a1_b2_c3"),
    ("aBCD", "A_BC_D", "a_bc_d"),
    ("Event1Epoch", "EVENT1_EPOCH", "event1_epoch"),
    ("Zz9Z", "ZZ9_Z", "zz9_z"),
    ("1A", "1_A", "1_a"),
    ("Strike_Ironclad", "STRIKE_IRONCLAD", "strike_ironclad"),
    ("Foo Bar", "FOO_BAR", "foo bar"),
    (" Trim Me ", "TRIM_ME", "trim me"),
    ("a  b", "A_B", "a  b"),
    ("X-Y", "XY", "x-y"),
]

SAMPLE = '''using System;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class Sample : CardModel
{
\t[StructLayout((LayoutKind)3)]
\t[CompilerGenerated]
\tprivate struct _003COnPlay_003Ed__5 : IAsyncStateMachine
\t{
\t\tpublic int _003C_003E1__state;

\t\tprivate void MoveNext()
\t\t{
\t\t\tint num = _003C_003E1__state;
\t\t\tSample sample = _003C_003E4__this;
\t\t\ttry
\t\t\t{
\t\t\t\tval = DamageCmd.Attack(sample.DynamicVars.Damage.BaseValue).Execute(choiceContext).GetAwaiter();
\t\t\t\tif (!val.IsCompleted)
\t\t\t\t{
\t\t\t\t\tnum = (_003C_003E1__state = 0);
\t\t\t\t\treturn;
\t\t\t\t}
\t\t\t\tval.GetResult();
\t\t\t\tPowerCmd.Apply<WeakPower>(choiceContext, null, 1m, null, sample);
\t\t\t}
\t\t\tcatch (System.Exception exception)
\t\t\t{
\t\t\t\t_003C_003E1__state = -2;
\t\t\t\treturn;
\t\t\t}
\t\t}
\t}

\tprivate const string _key = "Key}{";

\tprivate static readonly int[] _table = new int[2] { 1, 2 };

\tprotected override System.Collections.Generic.IEnumerable<DynamicVar> CanonicalVars => new _003C_003Ez__ReadOnlyArray<DynamicVar>(new DynamicVar[2]
\t{
\t\tnew DamageVar(8m, ValueProp.Move),
\t\tnew DynamicVar("Key}{", 1.5m)
\t});

\tpublic override CardType Type => CardType.Skill;

\tpublic Sample()
\t\t: base(2, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
\t{
\t}

\t[AsyncStateMachine(typeof(_003COnPlay_003Ed__5))]
\tprotected override System.Threading.Tasks.Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
\t{
\t\t_003COnPlay_003Ed__5 x = default(_003COnPlay_003Ed__5);
\t\tx._003C_003Et__builder = AsyncTaskMethodBuilder.Create();
\t\treturn x._003C_003Et__builder.Task;
\t}

\tprotected override void OnUpgrade()
\t{
\t\tbase.DynamicVars.Damage.UpgradeValueBy(2m);
\t}
}
'''


class SlugifyTests(unittest.TestCase):
    def test_dotnet_vectors(self):
        for name, slug, snake in DOTNET_VECTORS:
            self.assertEqual(slugify(name), slug, name)
            self.assertEqual(snake_case(name), snake, name)

    def test_category(self):
        self.assertEqual(slugify_category("CardModel"), "CARD")
        self.assertEqual(slugify_category("CardPoolModel"), "CARD_POOL")
        self.assertEqual(model_id("PowerModel", "WeakPower"), "POWER.WEAK_POWER")


class MaskerTests(unittest.TestCase):
    def mask(self, text):
        m = cs_source.Masker(text)
        return m.run(), m.literals

    def test_comments_and_strings(self):
        out, lits = self.mask('a = "x{y}"; // tail\n/* block\n */ b = @"q""r";\n')
        self.assertEqual(out.count("\n"), 3)
        self.assertNotIn("tail", out)
        self.assertNotIn("block", out)
        self.assertEqual([l.value for l in lits], ["x{y}", 'q"r'])

    def test_interpolated_nested_quotes(self):
        out, lits = self.mask('s = $"{(a ? "x" : "y")}-{b}"; t = 1;')
        self.assertIn("t = 1;", out)
        self.assertEqual(len(lits), 1)
        self.assertEqual(lits[0].value, "{}-{}")

    def test_char_literals(self):
        out, _ = self.mask("c = '\"'; d = '\\''; e = '{';")
        self.assertNotIn("{", out)
        self.assertIn("e = ' ';", out)


class MemberTests(unittest.TestCase):
    def setUp(self):
        fd, self.path = tempfile.mkstemp(suffix=".cs")
        with os.fdopen(fd, "w") as fh:
            fh.write(SAMPLE)
        self.f = cs_source.CsFile(self.path, "Models.Cards/Sample.cs")

    def tearDown(self):
        os.remove(self.path)

    def test_split(self):
        td = self.f.main_type()
        self.assertEqual(td.name, "Sample")
        self.assertEqual(td.bases, ["CardModel"])
        kinds = [(m.kind, m.name) for m in td.members]
        self.assertEqual(kinds, [("type", "_003COnPlay_003Ed__5"), ("const", "_key"), ("field", "_table"),
                                 ("property", "CanonicalVars"), ("property", "Type"), ("ctor", "Sample"),
                                 ("method", "OnPlay"), ("method", "OnUpgrade")])
        onplay = td.find("OnPlay")[0]
        attr = SAMPLE.split("\n").index("\t[AsyncStateMachine(typeof(_003COnPlay_003Ed__5))]") + 1
        self.assertEqual(onplay.attr_line, attr)
        self.assertEqual(onplay.line, attr + 1)
        self.assertEqual(cs_source.generated_owner("_003COnPlay_003Ed__5"), "OnPlay")
        self.assertEqual(cs_source.generated_owner("_003CGenerateMoveStateMachine_003Eb__12_0"),
                         "GenerateMoveStateMachine")

    def test_logic_lines(self):
        td = self.f.main_type()
        counts, _ = extract_models.logic_members(self.f, td)
        self.assertEqual(counts, {"OnPlay": 2, "OnUpgrade": 1})

    def test_dynamic_vars(self):
        ctx = extract_models.Ctx()
        ctx.dynvar_default = {"DamageVar": "Damage"}
        simple, details = extract_models.parse_dynamic_vars(ctx, self.f, self.f.main_type())
        self.assertEqual(simple, {"Damage": "8", "Key}{": "1.5"})
        self.assertEqual(details[0]["props"], "Move")


class RngTokenTests(unittest.TestCase):
    def test_normalisation(self):
        text = ("x.Owner.RunState.Rng.CombatTargets.NextItem(a); base.Rng.NextInt(3); y.RunRng.MonsterAi.NextItem(b);"
                "z.Owner.PlayerRng.Rewards.NextItem(c); MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextFloat(); "
                "mapRng.NextGaussianInt(7, 1, 6, 7); RunRngType.Niche;")
        self.assertEqual(extract_models.rng_usage(text), [
            "ModelRng.NextInt", "PlayerRng.Rewards", "Rng.Chaotic", "RunRng.CombatTargets", "RunRng.MonsterAi",
            "RunRngType.Niche", "local:mapRng.NextGaussianInt"])


ROOT = os.environ.get("STS2_DECOMPILE_ROOT", extract_models.DEFAULT_ROOT)


@unittest.skipUnless(os.path.isdir(ROOT), "decompile tree not available")
class TreeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        sim = os.path.join(extract_models.REPO_ROOT, "sim")
        cls.ctx = extract_models.load_context(ROOT, sim)
        cls.entries, cls.non_model = extract_models.build_entries(cls.ctx)
        cls.report = extract_models.validate(cls.ctx, cls.entries, cls.non_model)

    def entry(self, kind, cls_name):
        return [e for e in self.entries[kind] if e["class"] == cls_name][0]

    def test_every_file_accounted_once(self):
        for k in extract_models.KIND_NAMES:
            self.assertTrue(self.report[k]["accounted_exactly_once"], k)
        self.assertEqual(self.report["_problems"], [])

    def test_bash(self):
        e = self.entry("cards", "Bash")
        self.assertEqual((e["id"], e["model_id"], e["pool"], e["rarity"], e["type"], e["cost"], e["target_type"]),
                         ("BASH", "CARD.BASH", "IRONCLAD", "Basic", "Attack", 2, "AnyEnemy"))
        self.assertEqual(e["dynamic_vars"], {"Damage": "8", "VulnerablePower": "2"})
        self.assertTrue(e["ported"])

    def test_power_and_monster(self):
        p = self.entry("powers", "VulnerablePower")
        self.assertEqual((p["power_type"], p["stack_type"], p["dynamic_vars"]), ("Debuff", "Counter",
                                                                                 {"DamageIncrease": "1.5"}))
        m = self.entry("monsters", "Nibbit")
        self.assertEqual(m["min_hp"], {"a0": 42, "ascended": 44, "level": "ToughEnemies"})
        self.assertEqual(m["encounters"], ["NibbitsNormal", "NibbitsWeak"])

    def test_encounter_rng_users(self):
        users = sorted(e["class"] for e in self.entries["encounters"] if e["uses_encounter_rng"])
        self.assertIn("CorpseSlugsWeak", users)
        self.assertIn("SlimesWeak", users)
        self.assertNotIn("NibbitsWeak", users)

    def test_ids_unique_per_kind(self):
        for k in extract_models.KIND_NAMES:
            ids = [e["model_id"] for e in self.entries[k]]
            self.assertEqual(len(ids), len(set(ids)), k)


if __name__ == "__main__":
    unittest.main()
