using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Random;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

// Real-data pins of the XXH64 seeding path (v0.110.1 / v0.111.0), which no eligible diff bundle covers yet (the only
// v0.111.0 Ironclad attempts hold unported cards). Expected values are the recorded game values of five first combats
// (floor 2) exported by `sts2rec export-bundles --include-ineligible` on 2026-10-30; the corpus is not read here. The
// opening hand depends only on the deck order and the Shuffle stream, and the enemy side only on Niche (HP rolls, after
// the contract offset), MonsterAi (first moves) and the encounter Rng, so non-Ironclad runs pin them too: the enemies
// are checked on a sim combat with the Ironclad starter deck. Cross-check over the whole export: sim/specs/diff-harness.md §7.
public static class RecordedXxh64OpeningsTests
{
    private sealed record Opening(
        string Session,
        string Seed,
        int Ascension,
        string EncounterId,
        int NicheOffset,
        int Hp,
        int MaxHp,
        string[] Deck,
        string[] Hand,
        string[] Enemies);

    private static string[] Repeat(string id, int n) => Enumerable.Repeat(id, n).ToArray();

    private static string[] Concat(params string[][] parts) => parts.SelectMany(p => p).ToArray();

    private static readonly Opening[] Openings =
    {
        // v0.111.0 Ironclad A10; Neow NEOWS_BONES (+GUILTY) -> LEAFY_POULTICE, KALEIDOSCOPE: Niche 6 + 1.
        new("1792878363-G0G2JMFEYHRS", "G0G2JMFEYHRS", 10, "SLUDGE_SPINNER_WEAK", 7, 64, 68,
            Concat(Repeat("STRIKE_IRONCLAD", 4), Repeat("DEFEND_IRONCLAD", 3),
                new[] { "BASH", "ASCENDERS_BANE", "DARK_EMBRACE", "OFFERING", "UP_MY_SLEEVE", "ARSENAL", "GUILTY" }),
            new[] { "GUILTY", "ARSENAL", "DEFEND_IRONCLAD", "DARK_EMBRACE", "BASH" },
            new[] { "1:SLUDGE_SPINNER 42/42 b0 [] Attack 9x1, Debuff" }),
        // v0.110.1 Necrobinder A9, encounter Rng composition.
        new("1786454524-4A5TGHWJGU1T", "4A5TGHWJGU1T", 9, "SLIMES_WEAK", 0, 52, 66,
            Concat(Repeat("STRIKE_NECROBINDER", 4), Repeat("DEFEND_NECROBINDER", 4),
                new[] { "BODYGUARD", "UNLEASH", "ASCENDERS_BANE", "DOWSING" }),
            new[] { "STRIKE_NECROBINDER", "DOWSING", "STRIKE_NECROBINDER", "DEFEND_NECROBINDER", "ASCENDERS_BANE" },
            new[]
            {
                "1:TWIG_SLIME_S 9/9 b0 [] Attack 5x1",
                "2:TWIG_SLIME_M 27/27 b0 [] StatusCard(card_count=1)",
                "3:LEAF_SLIME_S 16/16 b0 [] StatusCard(card_count=1)"
            }),
        // v0.111.0 Silent A6 (Ring of the Snake: 7-card opening hand), encounter Rng starter moves.
        new("1793298254-GM28D28DX794", "GM28D28DX794", 6, "CORPSE_SLUGS_WEAK", 0, 56, 70,
            Concat(Repeat("STRIKE_SILENT", 5), Repeat("DEFEND_SILENT", 5), new[] { "NEUTRALIZE", "SURVIVOR", "ASCENDERS_BANE", "DOWSING" }),
            new[] { "NEUTRALIZE", "STRIKE_SILENT", "DOWSING", "DEFEND_SILENT", "STRIKE_SILENT", "ASCENDERS_BANE", "STRIKE_SILENT" },
            new[]
            {
                "1:CORPSE_SLUG 26/26 b0 [RAVENOUS_POWER=4] Attack 3x2",
                "2:CORPSE_SLUG 25/25 b0 [RAVENOUS_POWER=4] Attack 8x1"
            }),
        // v0.111.0 Silent A5.
        new("1793233995-Q3X6PF8S37GA", "Q3X6PF8S37GA", 5, "FUZZY_WURM_CRAWLER_WEAK", 0, 56, 70,
            Concat(Repeat("STRIKE_SILENT", 5), Repeat("DEFEND_SILENT", 5), new[] { "NEUTRALIZE", "SURVIVOR", "ASCENDERS_BANE" }),
            new[] { "SURVIVOR", "STRIKE_SILENT", "DEFEND_SILENT", "STRIKE_SILENT", "DEFEND_SILENT", "STRIKE_SILENT", "ASCENDERS_BANE" },
            new[] { "1:FUZZY_WURM_CRAWLER 57/57 b0 [] Attack 4x1" }),
        // v0.111.0 Silent A4; Neow NEOWS_BONES (+INJURY) -> NEW_LEAF, LEAFY_POULTICE: Niche 1 + 1.
        new("1792989304-NBNDT3S61AUM", "NBNDT3S61AUM", 4, "TOADPOLES_WEAK", 2, 56, 58,
            Concat(Repeat("STRIKE_SILENT", 3), Repeat("DEFEND_SILENT", 4),
                new[] { "NEUTRALIZE", "SURVIVOR", "SERPENT_FORM", "NIGHTMARE", "BULLET_TIME", "INJURY" }),
            new[] { "STRIKE_SILENT", "DEFEND_SILENT", "BULLET_TIME", "INJURY", "NEUTRALIZE", "STRIKE_SILENT", "SERPENT_FORM" },
            new[] { "1:TOADPOLE 22/22 b0 [] Buff", "2:TOADPOLE 23/23 b0 [] Attack 7x1" })
    };

    private static RunRngSet Streams(Opening o)
    {
        var rng = RngSetup.Create(RngSetup.SeedingXxh64, o.Seed);
        RngSetup.ApplyOffsets(rng, new[] { new KeyValuePair<string, int>("Niche", o.NicheOffset) });
        return rng;
    }

    private static string Render(EnemyProjection e)
    {
        string powers = string.Join(", ", e.Powers.Select(p => $"{p.Id}={p.Amount}"));
        string intents = string.Join(", ", e.Intents.Select(i =>
            (i.AttackValue is int v ? $"{i.Type} {v}x{i.AttackTimes ?? 1}" : i.Type)
            + (i.Extras.Count > 0 ? "(" + string.Join(",", i.Extras.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value?.ToJsonString()}")) + ")" : string.Empty)));
        return $"{e.CombatId}:{e.MonsterId} {e.Hp}/{e.MaxHp} b{e.Block} [{powers}] {intents}";
    }

    public static void Test_RecordedXxh64Openings_OpeningHandIsTheShuffledDeckOrder()
    {
        foreach (var o in Openings)
        {
            var order = o.Deck.ToList();
            order.UnstableShuffle(Streams(o).Shuffle);
            Assert.SequenceEqual(o.Hand, order.Take(o.Hand.Length), $"{o.Session}: opening hand = first draws of UnstableShuffle(deck order) on Shuffle");
        }
    }

    public static void Test_RecordedXxh64Openings_EnemiesMatchTheRecording()
    {
        foreach (var o in Openings)
        {
            var rng = Streams(o);
            var sim = CombatSim.Create(rng, CardSpec.FromIds(StartingDecks.IroncladAtRunStart(o.Ascension)), o.EncounterId, o.Ascension,
                o.Hp, o.MaxHp, CombatRunContext.ForAct(1, 2, null, null, RngSeedingScheme.Xxh64));
            var enemies = SimProjector.Project(sim.State).Enemies.Select(Render).ToArray();
            Assert.SequenceEqual(o.Enemies, enemies, o.Session);
        }
    }

    public static void Test_RecordedXxh64Openings_LegacySeedingDealsOtherHands()
    {
        // Negative control: the same seeds through the legacy V18 kernels deal none of the recorded hands, so the hand
        // pins select the XXH64 constructor (Shuffle is seeded by the same constructor as Niche and MonsterAi). The enemy
        // pins are not used for this: single-monster HP ranges are 3-4 values wide and can coincide.
        foreach (var o in Openings)
        {
            var legacy = RngSetup.Create(RngSetup.SeedingLegacyV18, o.Seed);
            var order = o.Deck.ToList();
            order.UnstableShuffle(legacy.Shuffle);
            Assert.False(order.Take(o.Hand.Length).SequenceEqual(o.Hand), $"{o.Session}: a legacy Shuffle stream deals another hand");
        }
    }
}
