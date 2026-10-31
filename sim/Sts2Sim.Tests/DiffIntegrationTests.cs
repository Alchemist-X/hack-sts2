using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Random;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

// Integration seam between Sts2Sim.Diff and the encounter-private Rng (sim/specs/rng-encounter-private.md §2-§3,
// open point §8): the harness must build each combat with the run context its bundle describes, or encounters that
// draw from the encounter Rng (SLIMES_WEAK composition, CORPSE_SLUGS_WEAK opener) spawn the XXH64 floor-2 roster for
// every bundle, which is wrong for legacy_v18 (v0.107.1) bundles and for any floor other than 2.
public static class DiffIntegrationTests
{
    private const string SlimesWeak = "SLIMES_WEAK";
    private const int Ascension = 10;

    private static readonly string[] Seeds = { "G0G2JMFEYHRS", "3URCWVGADS", "VW67B447SR", "AAAAAAAAAA", "SEEDSEED01", "Z9Y8X7W6V5", "QWERTYUIOP", "MNBVCXZLKJ" };

    private static DiffBundle Bundle(string seeding, string seed, int? act, int? floor, string encounterId = SlimesWeak)
    {
        var deck = StartingDecks.IroncladAtRunStart(Ascension).Select(id => new BundleCard(id, false)).ToList();
        return new DiffBundle
        {
            Source = new BundleSource("TEST", "test-part", 1, act, floor),
            GameVersion = seeding == RngSetup.SeedingLegacyV18 ? "v0.107.1" : "v0.111.0",
            Seeding = seeding,
            Seed = seed,
            Character = StartingDecks.IroncladCharacterId,
            Ascension = Ascension,
            EncounterId = encounterId,
            PlayerHp = StartingDecks.IroncladHpAfterNeowArrival(Ascension),
            PlayerMaxHp = StartingDecks.IroncladStartingHp,
            Deck = deck,
            Relics = new[] { "BURNING_BLOOD" },
            Outcome = DiffBundle.OutcomeAbandoned
        };
    }

    private static string Roster(CombatSim sim) => string.Join(",", sim.State.Monsters.Select(m => $"{m.CombatId}:{m.MonsterId}"));

    public static void Test_DiffIntegration_RunContextFollowsBundleSourceAndSeeding()
    {
        Assert.Equal(CombatRunContext.ForAct(1, 2), BundleRunner.RunContext(Bundle(RngSetup.SeedingXxh64, Seeds[0], 1, 2)));
        Assert.Equal(CombatRunContext.ForAct(1, 2, null, null, RngSeedingScheme.LegacyV18),
            BundleRunner.RunContext(Bundle(RngSetup.SeedingLegacyV18, Seeds[1], 1, 2)));
        Assert.Equal(CombatRunContext.ForAct(2, 19, null, null, RngSeedingScheme.LegacyV18),
            BundleRunner.RunContext(Bundle(RngSetup.SeedingLegacyV18, Seeds[1], 2, 19)), "source.act is 1-based, source.floor is TotalFloor");
        Assert.Equal(CombatRunContext.Default, BundleRunner.RunContext(Bundle(RngSetup.SeedingXxh64, Seeds[0], null, null)),
            "absent act/floor fall back to the first-combat defaults");
        Assert.Equal(RngSeedingScheme.LegacyV18, BundleRunner.RunContext(Bundle(RngSetup.SeedingLegacyV18, Seeds[0], null, null)).Seeding);
    }

    public static void Test_DiffIntegration_CreateSimSeedsTheEncounterRngFromTheBundle()
    {
        int differsFromDefault = 0;
        foreach (var seeding in new[] { RngSetup.SeedingXxh64, RngSetup.SeedingLegacyV18 })
        {
            var scheme = CombatRunContext.ParseSeeding(seeding);
            foreach (var seed in Seeds)
            {
                foreach (int floor in new[] { 2, 5 })
                {
                    var b = Bundle(seeding, seed, 1, floor);
                    var viaHarness = BundleRunner.CreateSim(b);
                    var expected = CombatSim.Create(RngSetup.CreateForBundle(b), BundleRunner.DeckSpecs(b), b.EncounterId, b.Ascension,
                        b.PlayerHp, b.PlayerMaxHp, CombatRunContext.ForAct(1, floor, null, null, scheme));
                    Assert.Equal(Roster(expected), Roster(viaHarness), $"{seeding} {seed} floor {floor}: roster");
                    Assert.Equal(expected.StateHash(), viaHarness.StateHash(), $"{seeding} {seed} floor {floor}: whole opening state");
                    var withDefault = CombatSim.Create(RngSetup.CreateForBundle(b), BundleRunner.DeckSpecs(b), b.EncounterId, b.Ascension,
                        b.PlayerHp, b.PlayerMaxHp, CombatRunContext.Default);
                    if (Roster(withDefault) != Roster(viaHarness))
                    {
                        differsFromDefault++;
                    }
                }
            }
        }
        Assert.True(differsFromDefault > 0, "the bundle context must reach the encounter Rng (some legacy or floor-5 roster differs from the default context)");
    }

    public static void Test_DiffIntegration_DeterministicEncountersIgnoreTheContext()
    {
        foreach (var seeding in new[] { RngSetup.SeedingXxh64, RngSetup.SeedingLegacyV18 })
        {
            foreach (int floor in new[] { 2, 7 })
            {
                var b = Bundle(seeding, Seeds[2], 1, floor, "NIBBITS_WEAK");
                var plain = CombatSim.Create(RngSetup.CreateForBundle(b), BundleRunner.DeckSpecs(b), b.EncounterId, b.Ascension, b.PlayerHp, b.PlayerMaxHp);
                Assert.Equal(plain.StateHash(), BundleRunner.CreateSim(b).StateHash(), $"{seeding} floor {floor}: NIBBITS_WEAK draws from no private Rng");
            }
        }
    }
}
