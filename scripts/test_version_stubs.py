"""Tests of scripts/version_stubs.py (work packets) and scripts/version_synthetic.py (synthetic tree).

  python3 -m unittest discover -s scripts -p 'test_version_stubs.py'

Synthetic old / new decompile trees and a miniature repo (inventory, sim Core base classes, specs, tests) are built
in a temp dir; version_delta.run makes the manifest, version_stubs.build_packets the packets.
"""

import json
import os
import shutil
import sys
import tempfile
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import version_delta as V  # noqa: E402
import version_stubs as S  # noqa: E402
import version_synthetic as Y  # noqa: E402
from test_version_delta import card, write  # noqa: E402

LISTENER_CARD = """
using System;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class Midnight : CardModel
{
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(60m, ValueProp.Move)
    };

    public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;

    public Midnight()
        : base(12, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
    {
    }

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        await Cmd.Wait(0.1f);
    }

    public override async Task AfterCardExhausted(PlayerChoiceContext ctx, CardModel card, bool causedByEthereal)
    {
        await Cmd.Wait(0.1f);
    }

    public override Task AfterSomethingUnknown()
    {
        return Task.CompletedTask;
    }
}
"""

ENUM_CARD = """
using System;

namespace MegaCrit.Sts2.Core.Models.Cards;

public sealed class Accelerant : CardModel
{{
    public Accelerant()
        : base(1, CardType.Power, CardRarity.{rarity}, TargetType.Self)
    {{
    }}

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new global::{coll}<IHoverTip>(HoverTipFactory.Static());
}}
"""


class TokenEditTests(unittest.TestCase):
    def test_numeric_edit_with_label(self):
        e = S.token_edits("new BlockVar(15m, ValueProp.Move)", "new BlockVar(16m, ValueProp.Move)")
        self.assertEqual(e, [("BlockVar", "15", "16", True)])

    def test_enum_edit(self):
        e = S.token_edits(": base(1, CardType.Power, CardRarity.Rare, TargetType.Self)",
                          ": base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)")
        self.assertEqual(e, [("CardRarity", "Rare", "Uncommon", False)])

    def test_punctuation_and_generated_names_are_skipped(self):
        self.assertEqual(S.token_edits("Foo(a);", "Foo(a))"), [])
        self.assertEqual(S.token_edits("new global::_003C_003Ez__ReadOnlyArray<X>(a)",
                                       "new global::_003C_003Ez__ReadOnlySingleElementList<X>(a)"), [])


class PacketTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="vstubs-")
        old = os.path.join(cls.tmp, "old")
        new = os.path.join(cls.tmp, "new")
        repo = os.path.join(cls.tmp, "repo")
        nn = "MegaCrit.Sts2.Core."
        write(old, nn + "Models.Cards/Bash.cs", card("Bash", 8))
        write(new, nn + "Models.Cards/Bash.cs", card("Bash", 9, "this, cardPlay"))
        for i in range(9):
            write(old, nn + "Models.Cards/Strike%d.cs" % i, card("Strike%d" % i))
            write(new, nn + "Models.Cards/Strike%d.cs" % i, card("Strike%d" % i, fromcard="this, cardPlay"))
        write(new, nn + "Models.Cards/Abundance.cs", card("Abundance"))
        write(new, nn + "Models.Cards/Midnight.cs", LISTENER_CARD)
        write(old, nn + "Models.Cards/Scare.cs", card("Scare"))
        write(old, nn + "Models.Cards/Accelerant.cs", ENUM_CARD.format(rarity="Rare", coll="_003C_003Ez__ReadOnlyArray"))
        write(new, nn + "Models.Cards/Accelerant.cs",
              ENUM_CARD.format(rarity="Uncommon", coll="_003C_003Ez__ReadOnlySingleElementList"))
        inv = {"entries": [{"file": "Models.Cards/Bash.cs", "model_id": "CARD.BASH", "id": "BASH", "class": "Bash"}]}
        write(repo, "sim/specs/inventory/cards.json", json.dumps(inv))
        write(repo, "sim/Sts2Sim.Core/Cards/Defs/Bash.cs", "public sealed class Bash : CardDef {}\n")
        write(repo, "sim/Sts2Sim.Core/Cards/Defs/Accelerant.cs", "public sealed class Accelerant : CardDef {}\n")
        write(repo, "sim/Sts2Sim.Core/Cards/CardDef.cs", """
            namespace Sts2Sim.Core.Cards;

            public abstract class CardDef
            {
                protected CardDef(string id, int canonicalCost, CardType type, CardRarity rarity, TargetType targetType)
                {
                }

                public virtual CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.None;

                protected virtual IEnumerable<DynamicVar> CanonicalVars => Array.Empty<DynamicVar>();

                public abstract void OnPlay(CombatState cs, CardPlay play);

                public virtual void OnUpgrade(CardInstance card)
                {
                }
            }
            """)
        write(repo, "sim/Sts2Sim.Core/Combat/Pipeline/AbstractModel.cs", """
            public abstract class AbstractModel
            {
                public virtual void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
                {
                }
            }
            """)
        write(repo, "sim/Sts2Sim.Core/Cards/CardListener.cs", """
            public abstract class CardListener : AbstractModel, IOwnedListener<CardInstance>
            {
                public abstract AbstractModel CloneFor(CardInstance newOwner);
            }
            """)
        write(repo, "sim/Sts2Sim.Core/Cards/DynamicVars/DamageVar.cs", "public sealed class DamageVar : DynamicVar {}\n")
        write(repo, "sim/specs/kernel-map.json", json.dumps({"schema": "sts2sim.kernel-map/v1", "files": {},
                                                            "patterns": [], "curated": {}}))
        write(repo, "sim/specs/cards.md", """
            # Cards
            ## 1. Bash
            Damage 8 (`Models.Cards/Bash.cs:10`), vars `Models.Cards/Bash.cs:8`.
            """)
        write(repo, "sim/Sts2Sim.Tests/CardTests.cs", """
            public static class CardTests
            {
                public static void Test_Bash_Deals8()
                {
                    var id = "BASH";
                }
            }
            """)
        cls.man = V.run(old, new, repo=repo, write=False, old_version="1.0.0", new_version="1.1.0", cite_tree=new)
        cls.out = os.path.join(cls.tmp, "out")
        cls.idx = S.build_packets(cls.man, cls.out, repo=repo)
        cls.rows = {r["model_id"]: r for r in cls.idx["packets"]}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.tmp, ignore_errors=True)

    def packet(self, mid):
        with open(os.path.join(self.out, self.rows[mid]["packet"]), encoding="utf-8") as fh:
            return fh.read()

    def test_counts(self):
        c = self.idx["counts"]
        self.assertEqual((c.get("new"), c.get("removed"), c.get("unique"), c.get("bulk_only")), (2, 1, 2, 9))
        self.assertEqual(self.idx["schema"], S.SCHEMA)

    def test_modified_packet(self):
        r = self.rows["CARD.BASH"]
        self.assertEqual(r["section"], "unique")
        self.assertIn("DamageVar 8 -> 9", r["summary"])
        p = self.packet("CARD.BASH")
        self.assertIn("sim/Sts2Sim.Core/Cards/Defs/Bash.cs", p)
        self.assertIn("sim/specs/cards.md:", p)
        self.assertIn("CardTests.Test_Bash_Deals8", p)
        self.assertIn("dotnet run -c Release --project sim/Sts2Sim.Tests -- CardTests", p)
        self.assertIn("-        new DamageVar(8m, ValueProp.Move)", p)
        self.assertIn("+        new DamageVar(9m, ValueProp.Move)", p)
        diff = p.split("## Decompiled source diff", 1)[1]
        self.assertNotIn("using System", diff)  # trimmed to the class

    def test_enum_edit_and_generated_noise(self):
        s = self.rows["CARD.ACCELERANT"]["summary"]
        self.assertIn("CardRarity Rare -> Uncommon", s)
        self.assertNotIn("_003C", s)

    def test_bulk_only(self):
        r = self.rows["CARD.STRIKE0"]
        self.assertEqual(r["section"], "bulk_only")
        self.assertRegex(r["summary"], r"bulk B\d+ only")

    def test_added_skeleton(self):
        r = self.rows["CARD.ABUNDANCE"]
        self.assertTrue(r["skeleton"])
        self.assertTrue(r["summary"].startswith("NEW CardModel"))
        p = self.packet("CARD.ABUNDANCE")
        for s in ("[SimExclude]", "public sealed class Abundance : CardDef", 'public const string CardId = "ABUNDANCE";',
                  "new DamageVar(6m, ValueProp.Move)", "public override void OnPlay(CombatState cs, CardPlay play)",
                  "CardRegistry.TryGet(Abundance.CardId", "sim/Sts2Sim.Core/Cards/Defs/Abundance.cs"):
            self.assertIn(s, p)

    def test_card_hooks_go_to_a_listener(self):
        p = self.packet("CARD.MIDNIGHT")
        self.assertIn(": base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)", p)
        self.assertIn("public const int Cost = 12;", p)
        self.assertIn("public override CardMultiplayerConstraint MultiplayerConstraint => "
                      "CardMultiplayerConstraint.MultiplayerOnly;", p)
        self.assertIn("private sealed class Listener : CardListener", p)
        self.assertIn("public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)",
                      p)
        self.assertIn("TODO: decompiled override AfterSomethingUnknown", p)

    def test_removed(self):
        r = self.rows["CARD.SCARE"]
        self.assertEqual((r["section"], r["delta"]), ("removed", "removed"))
        self.assertIn("REMOVED", r["summary"])
        self.assertIn("VersionExclusive", self.packet("CARD.SCARE"))

    def test_index_order_and_bulk_collapse(self):
        with open(os.path.join(self.out, "packets", "INDEX.md"), encoding="utf-8") as fh:
            idx = fh.read()
        pos = [idx.index("[%s]" % m) for m in ("CARD.ABUNDANCE", "CARD.SCARE", "CARD.BASH")]
        self.assertEqual(pos, sorted(pos))
        self.assertIn("9 bulk-only packets", idx)
        self.assertNotIn("[CARD.STRIKE0]", idx)


class SyntheticTreeTests(unittest.TestCase):
    def test_edits_apply_exactly_once(self):
        tmp = tempfile.mkdtemp(prefix="vsynth-")
        try:
            src = os.path.join(tmp, "src")
            for scope, rel, old, new in Y.EDITS:
                write(src, rel, "public sealed class Bash\n" + old + "\nfooter\n")
            for _, src_rel, _, _, _ in Y.ADDS:
                if not os.path.exists(os.path.join(src, src_rel)):
                    write(src, src_rel, "class Bash {}\n")
            applied = Y.build(src, os.path.join(tmp, "dst"))
            self.assertEqual(len(applied), len(Y.EDITS) + len(Y.ADDS))
            with open(os.path.join(tmp, "dst", Y.EDITS[0][1]), encoding="utf-8") as fh:
                self.assertIn(Y.EDITS[0][3], fh.read())
            with open(os.path.join(tmp, "dst", Y.ADDS[0][2]), encoding="utf-8") as fh:
                self.assertIn("class SyntheticBash", fh.read())
            with self.assertRaises(SystemExit):
                Y.build(src, os.path.join(tmp, "dst"))  # exists, no --force
        finally:
            shutil.rmtree(tmp, ignore_errors=True)


if __name__ == "__main__":
    unittest.main()
