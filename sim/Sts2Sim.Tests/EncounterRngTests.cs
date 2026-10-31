using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class EncounterRngTests
{
    private const string SlimesWeak = "SLIMES_WEAK";
    private const string BowlbugsNormal = "BOWLBUGS_NORMAL";
    private const string NibbitsWeakId = "NIBBITS_WEAK";

    public static void Test_EncounterRng_IdHashVectors()
    {
        Assert.Equal(0xE11262E73FC9E82EUL, DeterministicHash.GetDeterministicHashCode(SlimesWeak));
        Assert.Equal(0x71DAB08CE00B9931UL, DeterministicHash.GetDeterministicHashCode(NibbitsWeakId));
        Assert.Equal(0x715DE8033CA21B61UL, DeterministicHash.GetDeterministicHashCode(BowlbugsNormal));
        Assert.Equal(1426820976, DeterministicHash.GetDeterministicHashCodeOld(SlimesWeak));
        Assert.Equal(2119076252, DeterministicHash.GetDeterministicHashCodeOld(NibbitsWeakId));
        Assert.Equal(-255709297, DeterministicHash.GetDeterministicHashCodeOld("CPSVMWJPUL"));
    }

    public static void Test_EncounterRng_EncounterSeedFormula()
    {
        Assert.Equal(0x0F142C2D7938AB2DUL, RngSeeds.Encounter(RngSeeds.RunSeed("4A5TGHWJGU1T"), 2, SlimesWeak));
        Assert.Equal(0x5D8284AC432677E1UL, RngSeeds.Encounter(RngSeeds.RunSeed("Q3X6PF8S37GA"), 6, SlimesWeak));
        Assert.Equal(0x39DE8B2C971D8DFDUL, RngSeeds.Encounter(RngSeeds.RunSeed("7UFKQYPX2YUT"), 3, SlimesWeak));
        Assert.Equal(0x63CF18046579EB77UL, RngSeeds.Encounter(RngSeeds.RunSeed("NBNDT3S61AUM"), 23, BowlbugsNormal));
        Assert.Equal(0x71DAB08CE00B9934UL, RngSeeds.Encounter(0xFFFFFFFFFFFFFFFEUL, 5, NibbitsWeakId), "Seed + floor wraps before the id hash is added");

        ulong runSeed = RngSeeds.RunSeed("G0G2JMFEYHRS");
        Assert.Equal(0x40A1EA70186DF6E6UL, runSeed);
        Assert.Equal(0xB27C9AFCF8799019UL, CombatRunContext.Default.EncounterSeed(runSeed, NibbitsWeakId));
        Assert.Equal(RngSeeds.Encounter(runSeed, 9, SlimesWeak), CombatRunContext.ForAct(1, 9).EncounterSeed(runSeed, SlimesWeak));
        var rng = CombatRunContext.Default.CreateEncounterRng(runSeed, NibbitsWeakId);
        Assert.Equal(0, rng.Counter);
        Assert.Equal(0x5C18C102C7BDEFE0UL, rng.NextUnsignedLong());
        Assert.Equal(new Rng(0xB27C9AFCF8799019UL).ToSnapshot(), CombatRunContext.Default.CreateEncounterRng(runSeed, NibbitsWeakId).ToSnapshot());
        Assert.NotEqual(CombatRunContext.ForAct(1, 2).EncounterSeed(runSeed, SlimesWeak), CombatRunContext.ForAct(1, 3).EncounterSeed(runSeed, SlimesWeak));
        Assert.Equal(CombatRunContext.ForAct(1, 5).EncounterSeed(runSeed, SlimesWeak), CombatRunContext.ForAct(3, 5, 4, 2).EncounterSeed(runSeed, SlimesWeak), "act and map coordinate do not enter the encounter seed");
    }

    public static void Test_EncounterRng_LegacyV18EncounterSeedFormula()
    {
        ulong cpsv = RngSeeds.RunSeed(RngSeeds.OldSeedPrefix + "CPSVMWJPUL");
        Assert.Equal(0xF0C22F8FUL, cpsv);
        Assert.Equal(0x45CDBF01u, RngSeeds.LegacyV18Encounter(cpsv, 2, SlimesWeak));
        Assert.Equal(0x45CDBF01u, RngSeeds.LegacyV18Encounter(unchecked((ulong)(long)DeterministicHash.GetDeterministicHashCodeOld("CPSVMWJPUL")), 2, SlimesWeak), "sign- and zero-extended legacy seeds agree");
        Assert.Equal(0xD04AB98Fu, RngSeeds.LegacyV18Encounter(RngSeeds.RunSeed(RngSeeds.OldSeedPrefix + "UEH562K7L4"), 2, SlimesWeak));
        Assert.Equal(0xF7AFD8B2u, RngSeeds.LegacyV18Encounter(RngSeeds.RunSeed(RngSeeds.OldSeedPrefix + "BP41SFWNWS"), 7, SlimesWeak));
        Assert.Equal(0x450B8F72u, RngSeeds.LegacyV18Encounter(0xF0000000UL, 2, SlimesWeak), "int arithmetic wraps");

        var legacy = CombatRunContext.ForAct(1, 2, seeding: RngSeedingScheme.LegacyV18);
        Assert.Equal(0x45CDBF01UL, legacy.EncounterSeed(cpsv, SlimesWeak));
        Assert.Equal(new Rng(0x45CDBF01UL).ToSnapshot(), legacy.CreateEncounterRng(cpsv, SlimesWeak).ToSnapshot());
        Assert.NotEqual(CombatRunContext.ForAct(1, 2).EncounterSeed(cpsv, SlimesWeak), legacy.EncounterSeed(cpsv, SlimesWeak));
    }

    public static void Test_EncounterRng_RecordedCompositionsMatchDraws()
    {
        var cases = new (string Seed, RngSeedingScheme Scheme, int Floor, string Encounter, (int Min, int Max, int Pick)[] Draws, string Recorded)[]
        {
            ("4A5TGHWJGU1T", RngSeedingScheme.Xxh64, 2, SlimesWeak, new[] { (0, 2, 1), (0, 1, 0), (0, 2, 1) }, "v0.110.1 TWIG_SLIME_S, TWIG_SLIME_M, LEAF_SLIME_S"),
            ("7UFKQYPX2YUT", RngSeedingScheme.Xxh64, 3, SlimesWeak, new[] { (0, 2, 0), (0, 1, 0), (0, 2, 1) }, "v0.110.1 LEAF_SLIME_S, TWIG_SLIME_M, TWIG_SLIME_S"),
            ("Q3X6PF8S37GA", RngSeedingScheme.Xxh64, 6, SlimesWeak, new[] { (0, 2, 0), (0, 1, 0), (0, 2, 0) }, "v0.111.0 LEAF_SLIME_S, LEAF_SLIME_M, TWIG_SLIME_S"),
            ("NBNDT3S61AUM", RngSeedingScheme.Xxh64, 23, BowlbugsNormal, new[] { (0, 3, 2), (0, 2, 1) }, "v0.111.0 BOWLBUG_ROCK, BOWLBUG_NECTAR, BOWLBUG_SILK"),
            ("CPSVMWJPUL", RngSeedingScheme.LegacyV18, 2, SlimesWeak, new[] { (0, 2, 1), (0, 1, 0), (0, 2, 1) }, "v0.107.1 TWIG_SLIME_S, TWIG_SLIME_M, LEAF_SLIME_S"),
            ("UEH562K7L4", RngSeedingScheme.LegacyV18, 2, SlimesWeak, new[] { (0, 2, 1), (0, 1, 0), (0, 2, 0) }, "v0.107.1 TWIG_SLIME_S, LEAF_SLIME_M, LEAF_SLIME_S"),
            ("BP41SFWNWS", RngSeedingScheme.LegacyV18, 7, SlimesWeak, new[] { (0, 2, 1), (0, 1, 0), (0, 2, 1) }, "v0.107.1 TWIG_SLIME_S, TWIG_SLIME_M, LEAF_SLIME_S"),
        };
        foreach (var c in cases)
        {
            string seedText = c.Scheme == RngSeedingScheme.LegacyV18 ? RngSeeds.OldSeedPrefix + c.Seed : c.Seed;
            ulong runSeed = new RunRngSet(seedText).Seed;
            var rng = CombatRunContext.ForAct(1, c.Floor, seeding: c.Scheme).CreateEncounterRng(runSeed, c.Encounter);
            for (int i = 0; i < c.Draws.Length; i++)
            {
                var (min, max, pick) = c.Draws[i];
                Assert.Equal(pick, rng.NextInt(min, max), $"{c.Seed} floor {c.Floor} {c.Encounter} draw {i} (recorded {c.Recorded})");
            }
            Assert.Equal(c.Draws.Length, rng.Counter);
        }
    }

    public static void Test_EncounterRng_MonsterSeedFormula()
    {
        ulong runSeed = RngSeeds.RunSeed("G0G2JMFEYHRS");
        Assert.Equal(0x40A1EA70186DF6EBUL, RngSeeds.Monster(runSeed, 3, 1, 0, 1u));
        Assert.Equal(0x40A1EA70186DF6E7UL, RngSeeds.Monster(runSeed, 0, 0, 0, 1u));
        Assert.Equal(0x40A1EA70186DF703UL, RngSeeds.Monster(runSeed, 6, 14, 2, 7u));
        Assert.Equal(9UL, RngSeeds.Monster(ulong.MaxValue, 2, 3, 1, 4u), "the sum wraps in 64 bits");

        Assert.Equal(0x40A1EA70186DF703UL, new CombatRunContext(2, 30, 6, 14).MonsterSeed(runSeed, 7));
        Assert.Equal(0x40A1EA70186DF6E7UL, CombatRunContext.Default.MonsterSeed(runSeed, 1), "null map coordinates count as 0");
        Assert.Equal(RngSeeds.Monster(runSeed, 0, 5, 0, 2u), new CombatRunContext(0, 2, null, 5).MonsterSeed(runSeed, 2));
        var rng = CombatRunContext.ForAct(1, 2, 3, 1).CreateMonsterRng(runSeed, 1);
        Assert.Equal(0xF036051839A64C77UL, rng.NextUnsignedLong());
        Assert.Throws<NotSupportedException>(() => CombatRunContext.ForAct(1, 2, seeding: RngSeedingScheme.LegacyV18).MonsterSeed(runSeed, 1));
    }

    public static void Test_EncounterRng_ContextDefaultsAndParsing()
    {
        var d = CombatRunContext.Default;
        Assert.Equal(0, d.ActIndex);
        Assert.Equal(2, d.TotalFloor);
        Assert.Null(d.MapCol);
        Assert.Null(d.MapRow);
        Assert.Equal(RngSeedingScheme.Xxh64, d.Seeding);
        Assert.Equal(d, CombatRunContext.ForAct(1, 2));
        Assert.Equal(new CombatRunContext(2, 33, 4, 9, RngSeedingScheme.LegacyV18), CombatRunContext.ForAct(3, 33, 4, 9, RngSeedingScheme.LegacyV18));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRunContext.ForAct(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => CombatRunContext.ForAct(1, -1));
        Assert.Equal(RngSeedingScheme.Xxh64, CombatRunContext.ParseSeeding("xxh64"));
        Assert.Equal(RngSeedingScheme.LegacyV18, CombatRunContext.ParseSeeding("legacy_v18"));
        Assert.Throws<ArgumentException>(() => CombatRunContext.ParseSeeding("XXH64"));
        Assert.Throws<ArgumentNullException>(() => CombatRunContext.ParseSeeding(null!));
    }

    public static void Test_EncounterRng_SpawnEncounterHandsTheEncounterItsPrivateRng()
    {
        foreach (var scheme in new[] { RngSeedingScheme.Xxh64, RngSeedingScheme.LegacyV18 })
        {
            for (int floor = 1; floor <= 8; floor++)
            {
                var context = CombatRunContext.ForAct(1, floor, seeding: scheme);
                var encounter = new RngProbeEncounter();
                var cs = NewState(41UL);
                var spawned = new List<MonsterState>();
                MonsterSpawner.SpawnEncounter(cs, encounter, context, spawned);
                var expected = new Rng(context.EncounterSeed(cs.Rng.Seed, encounter.Id));
                Assert.Equal(expected.ToSnapshot(), Assert.NotNull(encounter.Seen).Entry, $"{scheme} floor {floor}: the encounter Rng is fresh");
                int pick = expected.NextInt(0, 2);
                Assert.Equal(pick == 0 ? PlainMonster.LeftId : PlainMonster.RightId, spawned[0].MonsterId, $"{scheme} floor {floor}: the pick comes from the encounter Rng");
                Assert.Equal(spawned.Count, cs.Monsters.Count);
                for (int i = 0; i < RunRngSet.StreamCount; i++)
                {
                    var type = (RunRngType)i;
                    Assert.Equal(type == RunRngType.Niche ? spawned.Count : 0, cs.Rng.Get(type).Counter, $"{scheme} floor {floor}: run stream {type}");
                }
            }
        }

        var defaultEncounter = new RngProbeEncounter();
        var defaultState = NewState(41UL);
        MonsterSpawner.SpawnEncounter(defaultState, defaultEncounter);
        Assert.Equal(new Rng(RngSeeds.Encounter(defaultState.Rng.Seed, CombatRunContext.DefaultTotalFloor, defaultEncounter.Id)).ToSnapshot(), Assert.NotNull(defaultEncounter.Seen).Entry, "the context-free overload uses the default context");
        Assert.Throws<InvalidOperationException>(() => new RngProbeEncounter().GenerateMonsters(NewState(1UL)));
        Assert.Throws<ArgumentNullException>(() => MonsterSpawner.SpawnEncounter(NewState(1UL), null!, CombatRunContext.Default));
    }

    public static void Test_EncounterRng_DeterministicEncountersIgnoreTheRng()
    {
        var nibbits = EncounterRegistry.Get(NibbitsWeakId);
        var cs = NewState(3UL);
        var rng = new Rng(17UL);
        var viaRng = nibbits.GenerateMonsters(cs, rng);
        var plain = nibbits.GenerateMonsters(cs);
        Assert.Equal(0, rng.Counter, "NIBBITS_WEAK draws nothing from its encounter Rng");
        Assert.Equal(plain.Count, viaRng.Count);
        Assert.Equal(plain[0].Def, viaRng[0].Def);
        Assert.Null(viaRng[0].SlotName);
        Assert.Equal(0, nibbits.Slots.Count, "act-1 weak encounters define no slots");
    }

    public static void Test_EncounterRng_EncounterRngLeavesNoStateBehind()
    {
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        ulong reference = 0;
        for (int floor = 0; floor <= 12; floor++)
        {
            var encounter = new RngProbeEncounter(fixedPick: true);
            var cs = CombatLoop.CreateCombat(new RunRngSet(23UL), deck, encounter, 0, null, null, CombatRunContext.ForAct(1, floor));
            Assert.Equal(3, Assert.NotNull(encounter.Seen).Draws, "the probe draws from its Rng");
            ulong hash = StateHasher.Hash(cs);
            if (floor == 0)
            {
                reference = hash;
            }
            Assert.Equal(reference, hash, $"floor {floor}: the encounter Rng is not combat state");
        }

        ulong baseline = CombatSim.Create(new RunRngSet(5UL), deck, NibbitsWeakId, 0, null, null).StateHash();
        var contexts = new[]
        {
            CombatRunContext.Default,
            CombatRunContext.ForAct(1, 1),
            CombatRunContext.ForAct(1, 17, 3, 15),
            CombatRunContext.ForAct(2, 20, 6, 2),
            CombatRunContext.ForAct(1, 2, seeding: RngSeedingScheme.LegacyV18),
            default
        };
        foreach (var context in contexts)
        {
            Assert.Equal(baseline, CombatSim.Create(new RunRngSet(5UL), deck, NibbitsWeakId, 0, null, null, context).StateHash(), $"NIBBITS_WEAK is independent of {context}");
        }
    }

    public static void Test_EncounterRng_CombatSimCreateTakesContext()
    {
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        var rng = new RunRngSet("G0G2JMFEYHRS");
        var before = rng.ToSnapshot();
        var context = CombatRunContext.ForAct(1, 2, 3, 1);
        var sim = CombatSim.Create(rng, deck, NibbitsWeakId, 10, 64, 80, context);
        Assert.Equal(PlayerTurnPhase.Play, sim.State.Player.Phase);
        Assert.Equal(1, sim.State.Monsters.Count);
        var after = rng.ToSnapshot();
        for (int i = 0; i < RunRngSet.StreamCount; i++)
        {
            var type = (RunRngType)i;
            Assert.Equal(before.Rngs[type], after.Rngs[type], $"the caller's {type} stream is not advanced");
        }
        Assert.Equal(CombatSim.Create(rng, deck, NibbitsWeakId, 10, 64, 80).StateHash(), sim.StateHash());
        var byDef = CombatLoop.CreateCombat(rng.DeepClone(), deck, EncounterRegistry.Get(NibbitsWeakId), 10, 64, 80, context);
        Assert.Equal(sim.StateHash(), StateHasher.Hash(byDef), "the EncounterDef overload equals the id overload");
        Assert.Throws<ArgumentNullException>(() => CombatSim.Create(null!, deck, NibbitsWeakId, 0, null, null, context));
        Assert.Throws<ArgumentNullException>(() => CombatLoop.SetUpCombat(new RunRngSet(1UL), deck, (EncounterDef)null!, 0, null, null, context));
        Assert.Throws<KeyNotFoundException>(() => CombatSim.Create(new RunRngSet(1UL), deck, "NOT_AN_ENCOUNTER", 0, null, null, context));
    }

    public static void Test_EncounterRng_PrivateMonsterRngSeam()
    {
        var context = CombatRunContext.ForAct(2, 20, 4, 9);
        var encounter = new SeamEncounter();
        var cs = NewState(77UL);
        var spawned = new List<MonsterState>();
        MonsterSpawner.SpawnEncounter(cs, encounter, context, spawned);
        var seen = encounter.Rngs;
        Assert.Equal(2, seen.Count, "only monsters that opt in get their private Rng");
        Assert.SequenceEqual(new[] { 1, 3 }, seen.Select(s => s.CombatId));
        foreach (var s in seen)
        {
            Assert.Equal(new Rng(RngSeeds.Monster(cs.Rng.Seed, 4, 9, 1, (uint)s.CombatId)).ToSnapshot(), s.Snapshot, $"combat id {s.CombatId}");
            Assert.True(s.HpRolled, $"combat id {s.CombatId}: the Rng is created after the HP roll, as in CreateCreature");
        }
        Assert.Equal(3, cs.Rng.Niche.Counter, "one Niche draw per spawn; the private Rng draws nothing from the run streams");
        Assert.Equal(0, cs.Rng.MonsterAi.Counter);

        var nullCoord = new SeamEncounter();
        var cs2 = NewState(77UL);
        MonsterSpawner.SpawnEncounter(cs2, nullCoord, CombatRunContext.ForAct(1, 2));
        Assert.Equal(new Rng(RngSeeds.Monster(cs2.Rng.Seed, 0, 0, 0, 1u)).ToSnapshot(), nullCoord.Rngs[0].Snapshot, "null col and row count as 0");

        var legacyState = NewState(77UL);
        Assert.Throws<NotSupportedException>(() => MonsterSpawner.SpawnEncounter(legacyState, new SeamEncounter(), CombatRunContext.ForAct(1, 2, seeding: RngSeedingScheme.LegacyV18)));
        var legacyPlain = NewState(77UL);
        MonsterSpawner.SpawnEncounter(legacyPlain, new RngProbeEncounter(), CombatRunContext.ForAct(1, 2, seeding: RngSeedingScheme.LegacyV18));
        Assert.Equal(1, legacyPlain.Monsters.Count, "monsters that do not opt in spawn under legacy seeding");

        Assert.False(MonsterRegistry.Get("NIBBIT").UsesPrivateRng, "NIBBIT does not read MonsterModel.Rng");
        foreach (var id in Act1WeakPoolMonsters)
        {
            if (MonsterRegistry.TryGet(id, out var def))
            {
                Assert.False(def.UsesPrivateRng, $"{id}: no act-1 weak-pool monster reads MonsterModel.Rng");
            }
        }
    }

    private static readonly string[] Act1WeakPoolMonsters =
    {
        "NIBBIT", "LEAF_SLIME_S", "TWIG_SLIME_S", "LEAF_SLIME_M", "TWIG_SLIME_M", "SHRINKER_BEETLE", "FUZZY_WURM_CRAWLER"
    };

    public static void Test_EncounterRng_IndexOfSlotMatchesListExtensionsIndexOf()
    {
        var slots = new[] { "first", "second", "third" };
        Assert.Equal(0, MonsterSpawner.IndexOfSlot(slots, "first"));
        Assert.Equal(2, MonsterSpawner.IndexOfSlot(slots, "third"));
        Assert.Equal(-1, MonsterSpawner.IndexOfSlot(slots, null));
        Assert.Equal(-1, MonsterSpawner.IndexOfSlot(slots, "First"), "ordinal comparison");
        Assert.Equal(-1, MonsterSpawner.IndexOfSlot(slots, "fourth"));
        Assert.Equal(-1, MonsterSpawner.IndexOfSlot(Array.Empty<string>(), "first"));
        Assert.Equal(1, MonsterSpawner.IndexOfSlot(new[] { "a", "b", "b" }, "b"), "the first match wins");
    }

    public static void Test_EncounterRng_SortEnemiesBySlotNameUsesSlotIndex()
    {
        var encounter = new SlottedEncounter("first", "second", "third");
        var cs = StateWithSlots("third", "first", "second");
        MonsterSpawner.SortEnemiesBySlotName(cs, encounter);
        Assert.SequenceEqual(new[] { "first", "second", "third" }, cs.Monsters.Select(m => m.SlotName));
        Assert.SequenceEqual(new[] { 2, 3, 1 }, cs.Monsters.Select(m => m.CombatId), "combat ids keep the creation order");

        var notAlphabetical = StateWithSlots("front", "back");
        MonsterSpawner.SortEnemiesBySlotName(notAlphabetical, new SlottedEncounter("back", "front"));
        Assert.SequenceEqual(new[] { "back", "front" }, notAlphabetical.Monsters.Select(m => m.SlotName), "the order of Slots decides, not the name");

        var unknown = StateWithSlots("second", null, "zzz", "first");
        MonsterSpawner.SortEnemiesBySlotName(unknown, encounter);
        Assert.SequenceEqual(new[] { 2, 3, 4, 1 }, unknown.Monsters.Select(m => m.CombatId), "null and unknown slots have index -1 and sort first");

        var noEncounter = StateWithSlots("third", "first");
        MonsterSpawner.SortEnemiesBySlotName(noEncounter, null);
        Assert.SequenceEqual(new[] { "third", "first" }, noEncounter.Monsters.Select(m => m.SlotName), "no encounter, no sort");

        var noSlots = StateWithSlots("b", "a");
        MonsterSpawner.SortEnemiesBySlotName(noSlots, new SlottedEncounter());
        Assert.SequenceEqual(new[] { "b", "a" }, noSlots.Monsters.Select(m => m.SlotName), "an encounter without Slots gives every creature index -1");
    }

    public static void Test_EncounterRng_SlotSortIsTheGameListSort()
    {
        var encounter = new SlottedEncounter("a", "b");
        var three = StateWithSlots("b", "b", "a");
        MonsterSpawner.SortEnemiesBySlotName(three, encounter);
        Assert.SequenceEqual(new[] { 3, 2, 1 }, three.Monsters.Select(m => m.CombatId), "List.Sort's 3-element network swaps the two equal keys");
        Assert.SequenceEqual(ReferenceSort(new[] { 1, 1, 0 }), three.Monsters.Select(m => m.CombatId));

        var two = StateWithSlots("b", "a");
        MonsterSpawner.SortEnemiesBySlotName(two, encounter);
        Assert.SequenceEqual(new[] { 2, 1 }, two.Monsters.Select(m => m.CombatId));

        var five = StateWithSlots("b", "b", "a", "b", "a");
        MonsterSpawner.SortEnemiesBySlotName(five, encounter);
        Assert.SequenceEqual(new[] { 3, 5, 1, 2, 4 }, five.Monsters.Select(m => m.CombatId), "insertion sort keeps equal keys in order");
        Assert.SequenceEqual(ReferenceSort(new[] { 1, 1, 0, 1, 0 }), five.Monsters.Select(m => m.CombatId));
    }

    public static void Test_EncounterRng_SetUpSortsAfterEachSlottedSetUpAndRollsInSlotOrder()
    {
        for (ulong seed = 1; seed <= 20; seed++)
        {
            var log = new List<string>();
            var encounter = new SlottedEncounter(log, new[] { "first", "second", "third" }, "third", "first", "second");
            var cs = CombatLoop.SetUpCombat(new RunRngSet(seed), Strikes(10), encounter, 0, null, null, CombatRunContext.Default);
            Assert.SequenceEqual(new[]
            {
                "setup:third|third,first,second",
                "setup:first|first,second,third",
                "setup:second|first,second,third"
            }, log, "SetUpForCombat runs once per creature in creation order; the list is re-sorted after each slotted creature");
            Assert.SequenceEqual(new[] { "first", "second", "third" }, cs.Monsters.Select(m => m.SlotName));
            Assert.SequenceEqual(new[] { 2, 3, 1 }, cs.Monsters.Select(m => m.CombatId));
            Assert.Equal(SlottedEncounter.TestId, cs.EncounterId);

            var replay = cs.Rng.MonsterAi.DeepClone();
            log.Clear();
            CombatLoop.StartCombat(cs);
            Assert.SequenceEqual(new[]
            {
                "added:first", "roll:first",
                "added:second", "roll:second",
                "added:third", "roll:third"
            }, log, "AfterCreatureAdded and the first RollMove follow the slot-sorted order");
            Assert.Equal(3, cs.Rng.MonsterAi.Counter, "one MonsterAi draw per monster");
            foreach (var monster in cs.Monsters)
            {
                float num = replay.NextFloat(2f);
                num -= 1f;
                string expected = num <= 0f ? SlotProbeMonster.MoveA : SlotProbeMonster.MoveB;
                Assert.Equal(expected, monster.Ai.NextMove?.Id, $"seed {seed}: {monster.SlotName} takes the next MonsterAi draw");
            }
        }
    }

    public static void Test_EncounterRng_UnslottedCreaturesNeverTriggerTheSort()
    {
        var slots = new[] { "only" };
        var unslotted = new string?[17];
        var cs = CombatLoop.SetUpCombat(new RunRngSet(9UL), Strikes(10), new SlottedEncounter(null, slots, unslotted), 0, null, null, CombatRunContext.Default);
        Assert.SequenceEqual(Enumerable.Range(1, 17), cs.Monsters.Select(m => m.CombatId), "no creature has a slot, so the list keeps its creation order");

        var lastSlotted = new string?[17];
        lastSlotted[16] = "zzz";
        var sorted = CombatLoop.SetUpCombat(new RunRngSet(9UL), Strikes(10), new SlottedEncounter(null, slots, lastSlotted), 0, null, null, CombatRunContext.Default);
        var reference = ReferenceSort(new int[17]);
        Assert.SequenceEqual(reference, sorted.Monsters.Select(m => m.CombatId), "one slotted creature triggers List.Sort over 17 equal keys");
        Assert.False(reference.SequenceEqual(Enumerable.Range(1, 17)), "introsort moves equal keys above 16 elements");

        var populated = CombatLoop.SetUpCombat(new RunRngSet(9UL), Strikes(10), "ENCOUNTER_RNG_NOT_REGISTERED", 0, null, null, CombatRunContext.Default, state =>
        {
            state.Monsters.Add(MonsterSpawner.Spawn(state, new MonsterSpawn(new PlainMonster(PlainMonster.LeftId), "second")));
            state.Monsters.Add(MonsterSpawner.Spawn(state, new MonsterSpawn(new PlainMonster(PlainMonster.RightId), "first")));
        });
        Assert.SequenceEqual(new[] { "second", "first" }, populated.Monsters.Select(m => m.SlotName), "an unregistered encounter id has no Slots to sort by");
    }

    private static int[] ReferenceSort(int[] keys)
    {
        var items = keys.Select((key, index) => (Key: key, Id: index + 1)).ToList();
        items.Sort((a, b) => a.Key - b.Key);
        return items.Select(i => i.Id).ToArray();
    }

    private static CardSpec[] Strikes(int count) => CardSpec.FromIds(Enumerable.Repeat(StrikeIronclad.CardId, count).ToArray());

    private static CombatState NewState(ulong seed)
        => new(new PlayerState(CombatLoop.PlayerCombatId, StartingDecks.IroncladCharacterId, 80, 80, StartingDecks.IroncladMaxEnergy), "ENCOUNTER_RNG_TEST", 0, new RunRngSet(seed));

    private static CombatState StateWithSlots(params string?[] slots)
    {
        var cs = NewState(1UL);
        var def = new PlainMonster(PlainMonster.LeftId);
        for (int i = 0; i < slots.Length; i++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, 10, 10, null, slots[i]));
        }
        return cs;
    }

    private sealed record ProbeRecord(RngSnapshot Entry, int Draws);

    private sealed record PrivateRngRecord(int CombatId, RngSnapshot Snapshot, bool HpRolled);

    private sealed class RngProbeEncounter : EncounterDef
    {
        private readonly bool _fixedPick;

        public RngProbeEncounter(bool fixedPick = false)
            : base("ENCOUNTER_RNG_PROBE")
        {
            _fixedPick = fixedPick;
        }

        public ProbeRecord? Seen { get; private set; }

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => throw EncounterRngRequired();

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng)
        {
            var entry = rng.ToSnapshot();
            int pick = rng.NextInt(0, 2);
            if (_fixedPick)
            {
                rng.NextInt(0, 7);
                rng.NextBool();
                pick = 0;
            }
            Seen = new ProbeRecord(entry, rng.Counter);
            return new[] { new MonsterSpawn(new PlainMonster(pick == 0 ? PlainMonster.LeftId : PlainMonster.RightId)) };
        }
    }

    private sealed class SeamEncounter : EncounterDef
    {
        public SeamEncounter()
            : base("ENCOUNTER_RNG_SEAM")
        {
        }

        public List<PrivateRngRecord> Rngs { get; } = new();

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs) => new[]
        {
            new MonsterSpawn(new PrivateRngMonster(Rngs)),
            new MonsterSpawn(new PlainMonster(PlainMonster.LeftId)),
            new MonsterSpawn(new PrivateRngMonster(Rngs))
        };
    }

    private sealed class SlottedEncounter : EncounterDef
    {
        public const string TestId = "ENCOUNTER_RNG_SLOTTED";

        private readonly string[] _slots;
        private readonly string?[] _spawnSlots;
        private readonly List<string>? _log;

        public SlottedEncounter(params string[] slots)
            : this(null, slots)
        {
        }

        public SlottedEncounter(List<string>? log, string[] slots, params string?[] spawnSlots)
            : base(TestId)
        {
            _log = log;
            _slots = slots;
            _spawnSlots = spawnSlots;
        }

        public override IReadOnlyList<string> Slots => _slots;

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        {
            var spawns = new MonsterSpawn[_spawnSlots.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                MonsterDef def = _log != null ? new SlotProbeMonster(_log) : new PlainMonster(PlainMonster.LeftId);
                spawns[i] = new MonsterSpawn(def, _spawnSlots[i]);
            }
            return spawns;
        }
    }

    private sealed class PlainMonster : MonsterDef
    {
        public const string LeftId = "ENCOUNTER_RNG_LEFT";
        public const string RightId = "ENCOUNTER_RNG_RIGHT";

        public PlainMonster(string id)
            : base(id)
        {
        }

        public override int MinInitialHp(CombatState cs) => 10;

        public override int MaxInitialHp(CombatState cs) => 60;

        public override void InitPrivateRng(CombatState cs, MonsterState self, Rng rng)
            => throw new InvalidOperationException("a monster that does not opt in never receives a private Rng");

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var wait = new MoveStateDef("ENCOUNTER_RNG_WAIT", static (_, _, _) => { }) { FollowUpStateId = "ENCOUNTER_RNG_WAIT" };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
        }
    }

    private sealed class PrivateRngMonster : MonsterDef
    {
        private readonly List<PrivateRngRecord> _records;

        public PrivateRngMonster(List<PrivateRngRecord> records)
            : base("ENCOUNTER_RNG_PRIVATE")
        {
            _records = records;
        }

        public override int MinInitialHp(CombatState cs) => 10;

        public override int MaxInitialHp(CombatState cs) => 60;

        public override bool UsesPrivateRng => true;

        public override void InitPrivateRng(CombatState cs, MonsterState self, Rng rng)
            => _records.Add(new PrivateRngRecord(self.CombatId, rng.ToSnapshot(), self.MaxHpBeforeModification.HasValue && self.MaxHp >= 10));

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var wait = new MoveStateDef("ENCOUNTER_RNG_WAIT", static (_, _, _) => { }) { FollowUpStateId = "ENCOUNTER_RNG_WAIT" };
            return new MonsterMoveStateMachineDef(new AiStateDef[] { wait }, wait);
        }
    }

    private sealed class SlotProbeMonster : MonsterDef
    {
        public const string MoveA = "ENCOUNTER_RNG_PROBE_A";
        public const string MoveB = "ENCOUNTER_RNG_PROBE_B";

        private readonly List<string> _log;

        public SlotProbeMonster(List<string> log)
            : base("ENCOUNTER_RNG_SLOT_PROBE")
        {
            _log = log;
        }

        public override int MinInitialHp(CombatState cs) => 20;

        public override int MaxInitialHp(CombatState cs) => 40;

        public override void AfterAddedToRoom(CombatState cs, MonsterState self) => _log.Add("added:" + self.SlotName);

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            _log.Add("setup:" + self.SlotName + "|" + string.Join(",", cs.Monsters.Select(m => m.SlotName ?? "-")));
            var log = _log;
            var a = new MoveStateDef(MoveA, static (_, _, _) => { });
            var b = new MoveStateDef(MoveB, static (_, _, _) => { });
            var rand = new RandomBranchStateDef("ENCOUNTER_RNG_PROBE_RAND");
            rand.AddBranch(a, MoveRepeatType.CanRepeatForever, 1f);
            rand.AddBranch(b, MoveRepeatType.CanRepeatForever, 1f);
            a.FollowUpState = rand;
            b.FollowUpState = rand;
            var init = new ConditionalBranchStateDef("ENCOUNTER_RNG_PROBE_INIT");
            init.AddState(rand, (_, m) =>
            {
                log.Add("roll:" + m.SlotName);
                return true;
            });
            return new MonsterMoveStateMachineDef(new AiStateDef[] { init, rand, a, b }, init);
        }
    }
}
