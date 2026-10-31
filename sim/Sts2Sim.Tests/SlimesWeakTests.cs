using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Loop;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

public static class SlimesWeakTests
{
    private const int PlayerHp = 80;
    private const string Encounter = "SLIMES_WEAK";
    private const string LeafS = "LEAF_SLIME_S";
    private const string TwigS = "TWIG_SLIME_S";
    private const string LeafM = "LEAF_SLIME_M";
    private const string TwigM = "TWIG_SLIME_M";
    private const string SlimedId = "SLIMED";
    private const string Tackle = "TACKLE_MOVE";
    private const string Goop = "GOOP_MOVE";
    private const string Clump = "CLUMP_SHOT";
    private const string StickyM = "STICKY_SHOT";
    private const string Pokey = "POKEY_POUNCE_MOVE";
    private const string StickyTwig = "STICKY_SHOT_MOVE";
    private const string Rand = "RAND";

    private static readonly string[] Smalls = { LeafS, TwigS };
    private static readonly string[] Mediums = { LeafM, TwigM };

    public static void Test_SlimesWeak_IdsMatchSlugifiedClassNames()
    {
        Assert.Equal(Encounter, SlimesWeak.EncounterIdEntry);
        Assert.Equal(LeafS, LeafSlimeS.MonsterIdEntry);
        Assert.Equal(TwigS, TwigSlimeS.MonsterIdEntry);
        Assert.Equal(LeafM, LeafSlimeM.MonsterIdEntry);
        Assert.Equal(TwigM, TwigSlimeM.MonsterIdEntry);
        Assert.Equal(SlimedId, Slimed.CardId);
        Assert.Equal(DeterministicHash.Slugify("SlimesWeak"), SlimesWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("LeafSlimeS"), LeafSlimeS.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("TwigSlimeS"), TwigSlimeS.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("LeafSlimeM"), LeafSlimeM.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("TwigSlimeM"), TwigSlimeM.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("Slimed"), Slimed.CardId);
    }

    public static void Test_SlimesWeak_RegistryAndEncounterShape()
    {
        Assert.True(MonsterRegistry.Get(LeafS) is LeafSlimeS);
        Assert.True(MonsterRegistry.Get(TwigS) is TwigSlimeS);
        Assert.True(MonsterRegistry.Get(LeafM) is LeafSlimeM);
        Assert.True(MonsterRegistry.Get(TwigM) is TwigSlimeM);
        Assert.True(CardRegistry.Get(SlimedId) is Slimed);
        var encounter = EncounterRegistry.Get(Encounter);
        Assert.True(encounter is SlimesWeak);
        Assert.True(encounter.IsWeak);
        Assert.Equal(0, encounter.Slots.Count);
        Assert.SequenceEqual(Smalls, SlimesWeak.SmallSlimes);
        Assert.SequenceEqual(Mediums, SlimesWeak.MediumSlimes);
        Assert.Throws<InvalidOperationException>(() => encounter.GenerateMonsters(NewCombat(0, 1UL)));
        Assert.Throws<ArgumentNullException>(() => encounter.GenerateMonsters(NewCombat(0, 1UL), null!));
    }

    public static void Test_SlimesWeak_HpRangesByAscension()
    {
        var expected = new (string Id, int Min, int Max, int ToughMin, int ToughMax)[]
        {
            (LeafS, 11, 15, 12, 16),
            (TwigS, 7, 11, 8, 12),
            (LeafM, 32, 35, 33, 36),
            (TwigM, 26, 28, 27, 29)
        };
        foreach (var e in expected)
        {
            var def = MonsterRegistry.Get(e.Id);
            for (int asc = 0; asc <= 10; asc++)
            {
                var cs = NewCombat(asc, 1UL);
                bool tough = asc >= 8;
                Assert.Equal(tough ? e.ToughMin : e.Min, def.MinInitialHp(cs), $"{e.Id} min A{asc}");
                Assert.Equal(tough ? e.ToughMax : e.Max, def.MaxInitialHp(cs), $"{e.Id} max A{asc}");
            }
        }
    }

    public static void Test_SlimesWeak_HpRollIsOneNicheDrawOverTheRangeAtA0AndA8()
    {
        foreach (var id in new[] { LeafS, TwigS, LeafM, TwigM })
        {
            var def = MonsterRegistry.Get(id);
            foreach (int asc in new[] { 0, 8 })
            {
                var seen = new HashSet<int>();
                int min = def.MinInitialHp(NewCombat(asc, 1UL));
                int max = def.MaxInitialHp(NewCombat(asc, 1UL));
                for (ulong seed = 1; seed <= 200; seed++)
                {
                    var shared = new Rng(seed);
                    var cs = NewCombat(asc, new RunRngSet(shared));
                    var monster = SpawnAndAdd(cs, new MonsterSpawn(def));
                    int expected = min + new Rng(seed).NextInt(0, max - min + 1);
                    Assert.Equal(expected, monster.MaxHp, $"{id} A{asc} seed {seed}");
                    Assert.Equal(expected, monster.CurrentHp, $"{id} A{asc} seed {seed}");
                    Assert.Equal<int?>(expected, monster.MaxHpBeforeModification, $"{id} A{asc} seed {seed}");
                    Assert.Equal(1, shared.Counter, $"{id}: spawn and graph construction draw exactly once");
                    seen.Add(monster.MaxHp);
                }
                Assert.Equal(max - min + 1, seen.Count, $"{id} A{asc}: every HP value is reachable");
            }
        }
    }

    public static void Test_SlimesWeak_SmallSlimeHpRollsSkipTheOverlappingValue()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int overlap = asc >= 8 ? 12 : 11;
            var cases = new (string Existing, string Rolled, int Min, int Max)[]
            {
                (LeafS, TwigS, asc >= 8 ? 8 : 7, overlap),
                (TwigS, LeafS, overlap, asc >= 8 ? 16 : 15)
            };
            foreach (var c in cases)
            {
                for (ulong seed = 1; seed <= 50; seed++)
                {
                    var shared = new Rng(seed);
                    var cs = NewCombat(asc, new RunRngSet(shared));
                    cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), MonsterRegistry.Get(c.Existing), overlap, overlap, null, null));
                    var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(c.Rolled)));
                    var candidates = new List<int>();
                    for (int v = c.Min; v <= c.Max; v++)
                    {
                        if (v != overlap)
                        {
                            candidates.Add(v);
                        }
                    }
                    int expected = candidates[new Rng(seed).NextInt(0, candidates.Count)];
                    Assert.Equal(expected, monster.MaxHp, $"A{asc} {c.Rolled} after {c.Existing}@{overlap} seed {seed}");
                    Assert.NotEqual(overlap, monster.MaxHp);
                    Assert.Equal(1, shared.Counter);
                }
            }
        }
    }

    public static void Test_SlimesWeak_MoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 4 : 3, LeafSlimeS.TackleDamage(cs), $"leaf S tackle A{asc}");
            Assert.Equal(deadly ? 5 : 4, TwigSlimeS.TackleDamage(cs), $"twig S tackle A{asc}");
            Assert.Equal(deadly ? 9 : 8, LeafSlimeM.ClumpDamage(cs), $"leaf M clump A{asc}");
            Assert.Equal(deadly ? 12 : 11, TwigSlimeM.ClumpDamage(cs), $"twig M pokey pounce A{asc}");
        }
        Assert.Equal(1, LeafSlimeS.GoopAmount);
        Assert.Equal(2, LeafSlimeM.StickyAmount);
        Assert.Equal(1, TwigSlimeM.StickyAmount);
        Assert.Equal(2, TwigSlimeM.PokeyPounceMaxRepeats);
    }

    public static void Test_SlimesWeak_MoveGraphsAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            bool deadly = asc >= 9;

            var leafS = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(LeafS)));
            var m = leafS.Ai.Machine;
            Assert.Equal(3, m.States.Count);
            Assert.Equal(Rand, m.InitialStateId);
            var rand = (RandomBranchStateDef)m.Get(Rand);
            Assert.False(rand.ShouldAppearInLogs);
            Assert.Equal(2, rand.States.Count);
            AssertBranch(cs, leafS, rand.States[0], Tackle, MoveRepeatType.CannotRepeat, 0);
            AssertBranch(cs, leafS, rand.States[1], Goop, MoveRepeatType.CannotRepeat, 0);
            var tackle = (MoveStateDef)m.Get(Tackle);
            var goop = (MoveStateDef)m.Get(Goop);
            Assert.True(ReferenceEquals(rand, tackle.FollowUpState));
            Assert.True(ReferenceEquals(rand, goop.FollowUpState));
            AssertSingleAttack(cs, leafS, tackle, deadly ? 4 : 3);
            AssertStatus(goop, 1);
            Assert.Equal(0, leafS.Ai.StateLog.Count, "the initial random branch is not logged");
            Assert.Null(leafS.Ai.NextMove);

            var twigS = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TwigS)));
            m = twigS.Ai.Machine;
            Assert.Equal(1, m.States.Count);
            Assert.Equal(Tackle, m.InitialStateId);
            var twigTackle = (MoveStateDef)m.Get(Tackle);
            Assert.True(ReferenceEquals(twigTackle, twigTackle.FollowUpState));
            AssertSingleAttack(cs, twigS, twigTackle, deadly ? 5 : 4);
            Assert.SequenceEqual(new[] { Tackle }, twigS.Ai.StateLog);

            var leafM = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(LeafM)));
            m = leafM.Ai.Machine;
            Assert.Equal(2, m.States.Count);
            Assert.Equal(StickyM, m.InitialStateId);
            var clump = (MoveStateDef)m.Get(Clump);
            var sticky = (MoveStateDef)m.Get(StickyM);
            Assert.True(ReferenceEquals(sticky, clump.FollowUpState));
            Assert.True(ReferenceEquals(clump, sticky.FollowUpState));
            AssertSingleAttack(cs, leafM, clump, deadly ? 9 : 8);
            AssertStatus(sticky, 2);
            Assert.SequenceEqual(new[] { StickyM }, leafM.Ai.StateLog);

            var twigM = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TwigM)));
            m = twigM.Ai.Machine;
            Assert.Equal(3, m.States.Count);
            Assert.Equal(StickyTwig, m.InitialStateId);
            var twigRand = (RandomBranchStateDef)m.Get(Rand);
            Assert.Equal(2, twigRand.States.Count);
            AssertBranch(cs, twigM, twigRand.States[0], Pokey, MoveRepeatType.CanRepeatXTimes, 2);
            AssertBranch(cs, twigM, twigRand.States[1], StickyTwig, MoveRepeatType.CannotRepeat, 0);
            var pokey = (MoveStateDef)m.Get(Pokey);
            var twigSticky = (MoveStateDef)m.Get(StickyTwig);
            Assert.True(ReferenceEquals(twigRand, pokey.FollowUpState));
            Assert.True(ReferenceEquals(twigRand, twigSticky.FollowUpState));
            AssertSingleAttack(cs, twigM, pokey, deadly ? 12 : 11);
            AssertStatus(twigSticky, 1);
            Assert.SequenceEqual(new[] { StickyTwig }, twigM.Ai.StateLog);

            foreach (var monster in new[] { leafS, twigS, leafM, twigM })
            {
                foreach (var state in monster.Ai.Machine.States.Values)
                {
                    if (state is MoveStateDef move)
                    {
                        Assert.False(move.MustPerformOnceBeforeTransitioning, $"{monster.MonsterId} {move.Id}");
                    }
                }
            }
        }
    }

    public static void Test_SlimesWeak_CompositionIsThreeEncounterRngDraws()
    {
        var encounter = EncounterRegistry.Get(Encounter);
        var combos = new HashSet<(int, int)>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var cs = NewCombat(0, 1UL);
            var rng = new Rng(seed);
            var spawns = encounter.GenerateMonsters(cs, rng);
            var probe = new Rng(seed);
            int first = probe.NextInt(0, 2);
            int second = probe.NextInt(0, 1);
            int medium = probe.NextInt(0, 2);
            Assert.Equal(0, second);
            Assert.Equal(3, spawns.Count);
            Assert.Equal(Smalls[first], spawns[0].Def.Id, $"seed {seed} first small");
            Assert.Equal(Mediums[medium], spawns[1].Def.Id, $"seed {seed} medium");
            Assert.Equal(Smalls[1 - first], spawns[2].Def.Id, $"seed {seed} remaining small");
            for (int i = 0; i < 3; i++)
            {
                Assert.Null(spawns[i].SlotName);
                Assert.Null(spawns[i].Configure);
                Assert.True(ReferenceEquals(MonsterRegistry.Get(spawns[i].Def.Id), spawns[i].Def));
            }
            Assert.Equal(3, rng.Counter, "the lone remaining small slime still costs a draw");
            Assert.Equal(probe.ToSnapshot(), rng.ToSnapshot());
            combos.Add((first, medium));
        }
        Assert.Equal(4, combos.Count, "every small-first x medium combination occurs");
    }

    public static void Test_SlimesWeak_SpawnCreatesInOrderWithOneNicheDrawEach()
    {
        var encounter = EncounterRegistry.Get(Encounter);
        foreach (int asc in new[] { 0, 8 })
        {
            foreach (int floor in new[] { 2, 5 })
            {
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    var context = CombatRunContext.ForAct(1, floor);
                    var cs = NewCombat(asc, new RunRngSet(seed));
                    var spawned = new List<MonsterState>();
                    MonsterSpawner.SpawnEncounter(cs, encounter, context, spawned);

                    var encounterRng = context.CreateEncounterRng(cs.Rng.Seed, Encounter);
                    int first = encounterRng.NextInt(0, 2);
                    encounterRng.NextInt(0, 1);
                    int medium = encounterRng.NextInt(0, 2);
                    var ids = new[] { Smalls[first], Mediums[medium], Smalls[1 - first] };

                    var niche = new RunRngSet(seed).Niche;
                    var taken = new List<int>();
                    Assert.Equal(3, spawned.Count);
                    Assert.Equal(3, cs.Monsters.Count);
                    for (int i = 0; i < 3; i++)
                    {
                        var def = MonsterRegistry.Get(ids[i]);
                        int min = def.MinInitialHp(cs);
                        int max = def.MaxInitialHp(cs);
                        var candidates = new List<int>();
                        for (int v = min; v <= max; v++)
                        {
                            if (!taken.Contains(v))
                            {
                                candidates.Add(v);
                            }
                        }
                        int hp = candidates.Count > 0 ? candidates[niche.NextInt(0, candidates.Count)] : niche.NextInt(min, max + 1);
                        taken.Add(hp);
                        string label = $"A{asc} floor {floor} seed {seed} monster {i}";
                        Assert.True(ReferenceEquals(spawned[i], cs.Monsters[i]), label);
                        Assert.Equal(ids[i], spawned[i].MonsterId, label);
                        Assert.Equal(i + 1, spawned[i].CombatId, label + ": combat ids follow creation order");
                        Assert.Equal(hp, spawned[i].MaxHp, label);
                        Assert.Null(spawned[i].SlotName);
                    }
                    for (int s = 0; s < RunRngSet.StreamCount; s++)
                    {
                        var type = (RunRngType)s;
                        Assert.Equal(type == RunRngType.Niche ? 3 : 0, cs.Rng.Get(type).Counter, $"stream {type}");
                    }
                }
            }
        }
    }

    public static void Test_SlimesWeak_RecordedCompositions()
    {
        var encounter = EncounterRegistry.Get(Encounter);
        var cases = new (string Seed, RngSeedingScheme Scheme, int Floor, string[] Ids)[]
        {
            ("4A5TGHWJGU1T", RngSeedingScheme.Xxh64, 2, new[] { TwigS, TwigM, LeafS }),
            ("7UFKQYPX2YUT", RngSeedingScheme.Xxh64, 3, new[] { LeafS, TwigM, TwigS }),
            ("Q3X6PF8S37GA", RngSeedingScheme.Xxh64, 6, new[] { LeafS, LeafM, TwigS }),
            ("CPSVMWJPUL", RngSeedingScheme.LegacyV18, 2, new[] { TwigS, TwigM, LeafS }),
            ("UEH562K7L4", RngSeedingScheme.LegacyV18, 2, new[] { TwigS, LeafM, LeafS }),
            ("BP41SFWNWS", RngSeedingScheme.LegacyV18, 7, new[] { TwigS, TwigM, LeafS }),
            ("QC7ZPPWXEK", RngSeedingScheme.LegacyV18, 2, new[] { LeafS, LeafM, TwigS })
        };
        foreach (var c in cases)
        {
            var rng = RngSetup.Create(c.Scheme == RngSeedingScheme.LegacyV18 ? RngSetup.SeedingLegacyV18 : RngSetup.SeedingXxh64, c.Seed);
            var cs = NewCombat(0, rng);
            var context = CombatRunContext.ForAct(1, c.Floor, null, null, c.Scheme);
            var spawns = encounter.GenerateMonsters(cs, context.CreateEncounterRng(rng.Seed, Encounter));
            Assert.SequenceEqual(c.Ids, spawns.Select(s => s.Def.Id), $"{c.Seed} floor {c.Floor}");
            var spawned = new List<MonsterState>();
            MonsterSpawner.SpawnEncounter(NewCombat(0, rng.DeepClone()), encounter, context, spawned);
            Assert.SequenceEqual(c.Ids, spawned.Select(m => m.MonsterId), $"{c.Seed} floor {c.Floor} via SpawnEncounter");
        }
    }

    public static void Test_SlimesWeak_RecordedFightsMatchHpAndIntentsTurnByTurn()
    {
        var fights = new RecordedFight[]
        {
            new("QC7ZPPWXEK", RngSetup.SeedingLegacyV18, 10, 0, new[] { (1, LeafS, 13), (2, LeafM, 33), (3, TwigS, 9) }, new[]
            {
                new[] { (1, Tackle), (2, StickyM), (3, Tackle) },
                new[] { (1, Goop), (2, Clump) },
                new[] { (1, Tackle), (2, StickyM) },
                new[] { (2, Clump) },
                new[] { (2, StickyM) }
            }),
            new("4A5TGHWJGU1T", RngSetup.SeedingXxh64, 9, 0, new[] { (1, TwigS, 9), (2, TwigM, 27), (3, LeafS, 16) }, new[]
            {
                new[] { (1, Tackle), (2, StickyTwig), (3, Goop) },
                new[] { (2, Pokey), (3, Tackle) },
                new[] { (2, Pokey), (3, Goop) },
                new[] { (2, StickyTwig) },
                new[] { (2, Pokey) }
            }),
            new("Y81Y612KD9", RngSetup.SeedingLegacyV18, 10, 0, new[] { (1, TwigS, 9), (2, TwigM, 28), (3, LeafS, 13) }, new[]
            {
                new[] { (1, Tackle), (2, StickyTwig), (3, Tackle) },
                new[] { (2, Pokey), (3, Goop) },
                new[] { (2, Pokey), (3, Tackle) },
                new[] { (2, StickyTwig) },
                new[] { (2, Pokey) },
                new[] { (2, StickyTwig) }
            }),
            new("1FFPUBYSAS", RngSetup.SeedingLegacyV18, 8, 0, new[] { (1, LeafS, 15), (2, LeafM, 34), (3, TwigS, 9) }, new[]
            {
                new[] { (1, Goop), (2, StickyM), (3, Tackle) },
                new[] { (1, Tackle), (2, Clump) },
                new[] { (2, StickyM) },
                new[] { (2, Clump) }
            }),
            new("DMYX9NX08S", RngSetup.SeedingLegacyV18, 10, 1, new[] { (1, TwigS, 11), (2, LeafM, 36), (3, LeafS, 15) }, new[]
            {
                new[] { (1, Tackle), (2, StickyM), (3, Goop) },
                new[] { (2, Clump), (3, Tackle) },
                new[] { (2, StickyM) },
                new[] { (2, Clump) }
            })
        };
        foreach (var fight in fights)
        {
            var rng = RngSetup.Create(fight.Seeding, fight.Seed);
            RngSetup.Advance(rng.Niche, fight.NicheOffset);
            var context = CombatRunContext.ForAct(1, 2, null, null, CombatRunContext.ParseSeeding(fight.Seeding));
            var sim = CombatSim.Create(rng, CardSpec.FromIds(StartingDecks.Ironclad), Encounter, fight.Ascension, null, null, context);
            var cs = sim.State;
            Assert.Equal(fight.Opening.Length, cs.Monsters.Count, fight.Seed);
            for (int i = 0; i < fight.Opening.Length; i++)
            {
                var (combatId, id, hp) = fight.Opening[i];
                Assert.Equal(combatId, cs.Monsters[i].CombatId, fight.Seed);
                Assert.Equal(id, cs.Monsters[i].MonsterId, fight.Seed);
                Assert.Equal(hp, cs.Monsters[i].MaxHp, $"{fight.Seed} {id} hp");
            }
            Assert.Equal(1, cs.Rng.MonsterAi.Counter, $"{fight.Seed}: setup draws once on MonsterAi (the leaf slime S random branch)");
            for (int turn = 0; turn < fight.Turns.Length; turn++)
            {
                var expected = fight.Turns[turn];
                if (turn > 0)
                {
                    var alive = expected.Select(e => e.Item1).ToHashSet();
                    foreach (var monster in cs.Monsters.ToList())
                    {
                        if (!alive.Contains(monster.CombatId))
                        {
                            cs.Monsters.Remove(monster);
                            continue;
                        }
                        SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
                    }
                    foreach (var monster in cs.Monsters)
                    {
                        MonsterLifecycle.PrepareForNextTurn(cs, monster);
                    }
                }
                Assert.SequenceEqual(expected.Select(e => e.Item1), cs.Monsters.Select(m => m.CombatId), $"{fight.Seed} turn {turn + 1} alive");
                Assert.SequenceEqual(expected.Select(e => e.Item2), cs.Monsters.Select(m => m.Ai.NextMove!.Id), $"{fight.Seed} turn {turn + 1} intents");
            }
        }
    }

    public static void Test_SlimesWeak_LeafSlimeSFirstRollIsOneMonsterAiFloat()
    {
        var firsts = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var shared = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(shared));
            var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(LeafS)));
            var probe = shared.DeepClone();
            int before = shared.Counter;

            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            float num = probe.NextFloat(2f);
            num -= 1f;
            string expected = num <= 0f ? Tackle : Goop;
            Assert.Equal(expected, monster.Ai.NextMove?.Id, $"seed {seed}");
            Assert.Equal(before + 1, shared.Counter, "the initial random branch draws one NextFloat");
            Assert.SequenceEqual(new[] { expected }, monster.Ai.StateLog);

            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            Assert.Equal(expected, monster.Ai.NextMove?.Id, "the round-1 re-roll keeps the first move");
            Assert.Equal(before + 1, shared.Counter, "the round-1 re-roll draws nothing");
            firsts.Add(expected);
        }
        Assert.Equal(2, firsts.Count, "both openers occur");
    }

    public static void Test_SlimesWeak_LeafSlimeSAlternatesAfterItsRandomOpener()
    {
        var openers = new HashSet<string>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 120; seed++)
        {
            var shared = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(shared));
            var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(LeafS)));
            var reference = new ReferenceAi(LeafS);
            var probe = shared.DeepClone();
            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            reference.Start(probe);
            Assert.Equal(reference.Next, monster.Ai.NextMove!.Id, $"seed {seed} opener");
            openers.Add(reference.Next);
            var seen = new List<string> { monster.Ai.NextMove!.Id };
            for (int turn = 1; turn < 12; turn++)
            {
                SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
                int before = shared.Counter;
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
                reference.Roll(probe);
                Assert.Equal(before + 1, shared.Counter, $"seed {seed} turn {turn}: one MonsterAi draw per roll");
                Assert.Equal(reference.Next, monster.Ai.NextMove!.Id, $"seed {seed} turn {turn}");
                seen.Add(monster.Ai.NextMove!.Id);
            }
            for (int i = 1; i < seen.Count; i++)
            {
                Assert.NotEqual(seen[i - 1], seen[i], $"seed {seed}: CannotRepeat on both branches forces alternation");
            }
            Assert.SequenceEqual(seen, monster.Ai.StateLog);
            Assert.Equal(probe.ToSnapshot(), shared.ToSnapshot());
        }
        Assert.Equal(2, openers.Count);
    }

    public static void Test_SlimesWeak_TwigSlimeSAlwaysTacklesWithoutDrawing()
    {
        var shared = new Rng(5UL);
        var cs = NewCombat(0, new RunRngSet(shared));
        var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TwigS)));
        int before = shared.Counter;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        var seen = new List<string> { monster.Ai.NextMove!.Id };
        for (int turn = 1; turn < 8; turn++)
        {
            SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            seen.Add(monster.Ai.NextMove!.Id);
        }
        Assert.SequenceEqual(Enumerable.Repeat(Tackle, 8), seen);
        Assert.Equal(before, shared.Counter);
    }

    public static void Test_SlimesWeak_LeafSlimeMAlternatesStickyFirstWithoutDrawing()
    {
        var shared = new Rng(6UL);
        var cs = NewCombat(0, new RunRngSet(shared));
        var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(LeafM)));
        int before = shared.Counter;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        var seen = new List<string> { monster.Ai.NextMove!.Id };
        for (int turn = 1; turn < 7; turn++)
        {
            SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            seen.Add(monster.Ai.NextMove!.Id);
        }
        Assert.SequenceEqual(new[] { StickyM, Clump, StickyM, Clump, StickyM, Clump, StickyM }, seen);
        Assert.SequenceEqual(seen, monster.Ai.StateLog);
        Assert.Equal(before, shared.Counter);
    }

    public static void Test_SlimesWeak_TwigSlimeMOpensStickyThenRandomBranch()
    {
        bool sawForcedSticky = false;
        bool sawStickyAfterOnePokey = false;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var shared = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(shared));
            var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TwigM)));
            var reference = new ReferenceAi(TwigM);
            var probe = shared.DeepClone();
            int before = shared.Counter;
            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            reference.Start(probe);
            Assert.Equal(StickyTwig, monster.Ai.NextMove!.Id);
            Assert.Equal(before, shared.Counter, "the sticky opener is the initial move state: no draw");
            var seen = new List<string> { StickyTwig };
            for (int turn = 1; turn < 15; turn++)
            {
                SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
                int count = shared.Counter;
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
                reference.Roll(probe);
                Assert.Equal(count + 1, shared.Counter, $"seed {seed} turn {turn}: one MonsterAi draw per roll");
                Assert.Equal(reference.Next, monster.Ai.NextMove!.Id, $"seed {seed} turn {turn}");
                seen.Add(monster.Ai.NextMove!.Id);
            }
            Assert.Equal(Pokey, seen[1], "after the opener sticky cannot repeat, so pokey pounce is forced");
            for (int i = 1; i < seen.Count; i++)
            {
                if (seen[i] == StickyTwig)
                {
                    Assert.NotEqual(StickyTwig, seen[i - 1], $"seed {seed} index {i}: sticky shot cannot repeat");
                }
                if (i >= 2 && seen[i - 1] == Pokey && seen[i - 2] == Pokey)
                {
                    Assert.Equal(StickyTwig, seen[i], $"seed {seed} index {i}: pokey pounce at most twice in a row");
                    sawForcedSticky = true;
                }
                if (i >= 2 && seen[i] == StickyTwig && seen[i - 1] == Pokey && seen[i - 2] == StickyTwig)
                {
                    sawStickyAfterOnePokey = true;
                }
            }
            Assert.SequenceEqual(seen, monster.Ai.StateLog);
            Assert.Equal(probe.ToSnapshot(), shared.ToSnapshot());
        }
        Assert.True(sawForcedSticky);
        Assert.True(sawStickyAfterOnePokey);
    }

    public static void Test_SlimesWeak_MovesDealDamageAndInjectSlimedIntoDiscard()
    {
        foreach (int asc in new[] { 0, 8, 9 })
        {
            bool deadly = asc >= 9;
            var cases = new (string Monster, string Move, int Damage, int Slimed)[]
            {
                (LeafS, Tackle, deadly ? 4 : 3, 0),
                (LeafS, Goop, 0, 1),
                (TwigS, Tackle, deadly ? 5 : 4, 0),
                (LeafM, Clump, deadly ? 9 : 8, 0),
                (LeafM, StickyM, 0, 2),
                (TwigM, Pokey, deadly ? 12 : 11, 0),
                (TwigM, StickyTwig, 0, 1)
            };
            foreach (var c in cases)
            {
                string label = $"A{asc} {c.Monster} {c.Move}";
                var cs = NewCombat(asc, 77UL);
                var player = cs.Player;
                var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(c.Monster)));
                MonsterLifecycle.AfterCreatureAdded(cs, monster);
                var existing = CardInstance.Create(cs, CardRegistry.Get(StrikeIronclad.CardId));
                CardPileCmd.Add(cs, existing, PileType.Discard);
                MonsterLifecycle.SetMoveImmediate(monster, (MoveStateDef)monster.Ai.Machine.Get(c.Move), forceTransition: true);
                MonsterLifecycle.OnSideSwitch(monster);
                var counters = Counters(cs);
                int nextInstance = cs.NextCardInstanceId;

                MonsterLifecycle.TakeTurn(cs, monster);

                Assert.Equal(PlayerHp - c.Damage, player.CurrentHp, label + " damage");
                Assert.Equal(1 + c.Slimed, player.DiscardPile.Count, label + " discard size");
                Assert.True(ReferenceEquals(existing, player.DiscardPile[0]), label + ": Slimed goes to the bottom of the discard pile");
                for (int i = 0; i < c.Slimed; i++)
                {
                    var card = player.DiscardPile[1 + i];
                    Assert.Equal(SlimedId, card.Id, label);
                    Assert.Equal(nextInstance + i, card.InstanceId, label + " instance ids in creation order");
                    Assert.False(card.IsUpgraded);
                }
                Assert.Equal(nextInstance + c.Slimed, cs.NextCardInstanceId, label);
                Assert.Equal(0, player.Hand.Count + player.DrawPile.Count + player.ExhaustPile.Count, label);
                Assert.Equal(0, player.Powers.Count, label + ": slimes apply no power to the player");
                Assert.Equal(0, monster.Powers.Count, label + ": slimes apply no power to themselves");
                Assert.Equal(0, monster.Block, label);
                Assert.SequenceEqual(counters, Counters(cs), label + ": the move draws from no run stream");
                Assert.True(monster.Ai.PerformedFirstMove, label);
            }
        }
    }

    public static void Test_SlimesWeak_SlimedInjectionFollowsAddToCombatAndPreview()
    {
        var cs = NewCombat(0, 3UL);
        var player = cs.Player;
        var targets = MonsterLifecycle.PlayerCreatures(cs);
        var monster = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(TwigS)));
        int drawsAfterSpawn = cs.Rng.Niche.Counter;

        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, 3, null);
        Assert.Equal(3, player.DiscardPile.Count);
        Assert.SequenceEqual(new[] { 0, 1, 2 }, player.DiscardPile.Cards.Select(c => c.InstanceId));

        cs.IsInProgress = false;
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, 2, null);
        Assert.Equal(3, player.DiscardPile.Count, "outside a running combat the generated cards are not added");
        Assert.Equal(5, cs.NextCardInstanceId, "but they are still created, as CombatState.CreateCard runs first");
        cs.IsInProgress = true;

        player.CurrentHp = 0;
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, 2, null);
        Assert.Equal(3, player.DiscardPile.Count);
        Assert.Equal(5, cs.NextCardInstanceId, "a dead player gets nothing created");
        player.CurrentHp = PlayerHp;

        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, 0, null);
        Assert.Equal(3, player.DiscardPile.Count);
        Assert.Equal(drawsAfterSpawn, cs.Rng.Niche.Counter, "creating and adding the status cards draws nothing");
        Assert.Throws<NotSupportedException>(() => Slimed.AddToCombatAndPreview(cs, new CreatureState[] { monster }, PileType.Discard, 1, null));

        monster.CurrentHp = 0;
        Slimed.AddToCombatAndPreview(cs, targets, PileType.Discard, 1, null);
        Assert.Equal(3, player.DiscardPile.Count, "with no primary enemy alive the combat is ending and the add fails");
        Assert.Equal(6, cs.NextCardInstanceId);
    }

    public static void Test_SlimesWeak_SlimedCardValues()
    {
        var def = CardRegistry.Get(SlimedId);
        Assert.Equal(1, def.CanonicalCost);
        Assert.False(def.CostsX);
        Assert.Equal(CardType.Status, def.Type);
        Assert.Equal(CardRarity.Status, def.Rarity);
        Assert.Equal(TargetType.None, def.TargetType);
        Assert.Equal(0, def.MaxUpgradeLevel);
        Assert.SequenceEqual(new[] { CardKeyword.Exhaust }, def.CanonicalKeywords);
        Assert.Equal(0, def.CanonicalTags.Count);
        Assert.True(def.CanBeGeneratedInCombat);
        Assert.True(def.CanBeGeneratedByModifiers);
        var vars = def.CreateVars();
        Assert.Equal(1, vars.Count);
        Assert.Equal(1m, vars.Cards.BaseValue);

        var cs = NewCombat(0, 1UL);
        var card = CardInstance.Create(cs, def);
        Assert.True(card.HasKeyword(CardKeyword.Exhaust));
        Assert.False(card.IsUpgradable);
        card.Upgrade();
        Assert.False(card.IsUpgraded);
        Assert.Throws<ArgumentException>(() => CardInstance.ResolveDef(new CardSpec(SlimedId, true)));
        Assert.Equal(1, card.EnergyCost.GetAmountToSpend(cs, card));
    }

    public static void Test_SlimesWeak_PlayingSlimedCostsOneDrawsOneAndExhausts()
    {
        var sim = CombatSim.Create(new RunRngSet(31UL), CardSpec.FromIds(StartingDecks.Ironclad), Encounter, 0, null, null, CombatRunContext.Default);
        var cs = sim.State;
        var player = cs.Player;
        var slimed = CardInstance.Create(cs, CardRegistry.Get(SlimedId));
        CardPileCmd.AddGeneratedCardToCombat(cs, slimed, PileType.Hand);
        int index = player.Hand.IndexOf(slimed);
        Assert.Equal(5, index);
        Assert.True(sim.LegalActions().Contains(GameAction.Play(index)), "Slimed is playable and takes no target");
        Assert.False(sim.IsLegal(GameAction.Play(index, 0)));

        var top = player.DrawPile[0];
        int drawBefore = player.DrawPile.Count;
        int discardBefore = player.DiscardPile.Count;
        var counters = Counters(cs);
        sim.Step(GameAction.Play(index));

        Assert.Equal(2, player.Energy);
        Assert.Equal(6, player.Hand.Count, "the played Slimed leaves and one card is drawn");
        Assert.True(ReferenceEquals(top, player.Hand[5]), "the drawn card is the top of the draw pile");
        Assert.False(player.Hand.Contains(slimed));
        Assert.Equal(drawBefore - 1, player.DrawPile.Count);
        Assert.Equal(discardBefore, player.DiscardPile.Count);
        Assert.Equal(1, player.ExhaustPile.Count);
        Assert.True(ReferenceEquals(slimed, player.ExhaustPile[0]), "Slimed exhausts");
        Assert.SequenceEqual(counters, Counters(cs), "drawing from a non-empty draw pile uses no stream");
    }

    public static void Test_SlimesWeak_CombatSimEndTurnsFollowReferenceAi()
    {
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        var encounter = EncounterRegistry.Get(Encounter);
        var openers = new HashSet<string>(StringComparer.Ordinal);
        foreach (int asc in new[] { 0, 10 })
        {
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var rng = new RunRngSet(seed);
                var sim = CombatSim.Create(rng, deck, Encounter, asc, null, null, CombatRunContext.Default);
                var cs = sim.State;
                Assert.Equal(3, cs.Monsters.Count);
                Assert.Equal(3, cs.Rng.Niche.Counter);
                Assert.Equal(1, cs.Rng.MonsterAi.Counter, "only the leaf slime S draws at setup");

                var spawns = encounter.GenerateMonsters(NewCombat(asc, 1UL), CombatRunContext.Default.CreateEncounterRng(rng.Seed, Encounter));
                var monsterAi = new RunRngSet(seed).MonsterAi;
                var reference = spawns.Select(s => new ReferenceAi(s.Def.Id)).ToList();
                foreach (var r in reference)
                {
                    r.Start(monsterAi);
                }
                openers.Add(reference.First(r => r.MonsterId == LeafS).Next);

                int expectedSlimed = 0;
                int expectedHp = cs.Player.CurrentHp;
                int turns = asc >= 10 ? 3 : 4;
                for (int turn = 1; turn <= turns; turn++)
                {
                    string label = $"A{asc} seed {seed} turn {turn}";
                    Assert.SequenceEqual(reference.Select(r => r.MonsterId), cs.Monsters.Select(m => m.MonsterId), label);
                    Assert.SequenceEqual(reference.Select(r => r.Next), cs.Monsters.Select(m => m.Ai.NextMove!.Id), label);
                    Assert.Equal(monsterAi.Counter, cs.Rng.MonsterAi.Counter, label);
                    foreach (var r in reference)
                    {
                        expectedSlimed += SlimedCount(r.MonsterId, r.Next);
                        expectedHp -= Damage(asc, r.MonsterId, r.Next);
                    }
                    sim.Step(GameAction.EndTurn);
                    Assert.Equal(expectedHp, cs.Player.CurrentHp, label + " player hp after the enemy turn");
                    Assert.Equal(expectedSlimed, cs.Player.AllPiles.Sum(p => p.Cards.Count(c => c.Id == SlimedId)), label + " Slimed in the piles");
                    foreach (var r in reference)
                    {
                        r.Roll(monsterAi);
                    }
                }
            }
        }
        Assert.Equal(2, openers.Count);
    }

    public static void Test_SlimesWeak_CloneAndHashAreStable()
    {
        var sim = CombatSim.Create(new RunRngSet(9UL), CardSpec.FromIds(StartingDecks.Ironclad), Encounter, 10, null, null, CombatRunContext.Default);
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        for (int i = 0; i < 3; i++)
        {
            sim.Step(GameAction.EndTurn);
            clone.Step(GameAction.EndTurn);
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"turn {i}");
        }
        var branch = sim.Clone();
        branch.Step(GameAction.EndTurn);
        Assert.NotEqual(sim.StateHash(), branch.StateHash());
        Assert.Equal(sim.StateHash(), clone.StateHash(), "stepping a clone never changes its source");
    }

    public static void Test_SlimesWeak_RandomPlayoutsReplayAndCloneDeterministically()
    {
        var deck = CardSpec.FromIds(StartingDecks.Ironclad);
        int slimedPlays = 0;
        int finished = 0;
        var buffer = new List<GameAction>(32);
        for (ulong seed = 1; seed <= 30; seed++)
        {
            int asc = (int)(seed % 11);
            var policy = new Rng(seed * 7919UL);
            var sim = CombatSim.Create(new RunRngSet(seed), deck, Encounter, asc, null, null, CombatRunContext.Default);
            var script = new List<GameAction>();
            var hashes = new List<ulong> { sim.StateHash() };
            CombatSim? midClone = null;
            int midIndex = -1;
            for (int step = 0; step < 400 && !sim.IsTerminal; step++)
            {
                sim.LegalActions(buffer);
                var action = buffer[policy.NextInt(0, buffer.Count)];
                if (action.Kind == ActionKind.PlayCard && sim.State.Player.Hand[action.CardIndex].Id == SlimedId)
                {
                    slimedPlays++;
                }
                if (step == 8)
                {
                    midClone = sim.Clone();
                    midIndex = script.Count;
                }
                script.Add(action);
                sim.Step(action);
                hashes.Add(sim.StateHash());
            }
            if (sim.IsTerminal)
            {
                finished++;
            }
            var replay = CombatSim.Create(new RunRngSet(seed), deck, Encounter, asc, null, null, CombatRunContext.Default);
            Assert.Equal(hashes[0], replay.StateHash(), $"seed {seed} opening");
            for (int i = 0; i < script.Count; i++)
            {
                replay.Step(script[i]);
                Assert.Equal(hashes[i + 1], replay.StateHash(), $"seed {seed} replay step {i}");
            }
            if (midClone != null)
            {
                for (int i = midIndex; i < script.Count; i++)
                {
                    midClone.Step(script[i]);
                    Assert.Equal(hashes[i + 1], midClone.StateHash(), $"seed {seed} clone step {i}");
                }
            }
        }
        Assert.True(slimedPlays > 0, "the random policy played Slimed");
        Assert.True(finished >= 25, $"most random playouts reach a terminal state ({finished}/30)");
    }

    private static int SlimedCount(string monsterId, string move) => (monsterId, move) switch
    {
        (LeafS, Goop) => 1,
        (LeafM, StickyM) => 2,
        (TwigM, StickyTwig) => 1,
        _ => 0
    };

    private static int Damage(int asc, string monsterId, string move)
    {
        bool deadly = asc >= 9;
        return (monsterId, move) switch
        {
            (LeafS, Tackle) => deadly ? 4 : 3,
            (TwigS, Tackle) => deadly ? 5 : 4,
            (LeafM, Clump) => deadly ? 9 : 8,
            (TwigM, Pokey) => deadly ? 12 : 11,
            _ => 0
        };
    }

    private static int[] Counters(CombatState cs)
    {
        var counters = new int[RunRngSet.StreamCount];
        for (int i = 0; i < counters.Length; i++)
        {
            counters[i] = cs.Rng.Get((RunRngType)i).Counter;
        }
        return counters;
    }

    private static void AssertBranch(CombatState cs, MonsterState owner, RandomBranchStateDef.StateWeight branch, string stateId, MoveRepeatType repeat, int maxTimes)
    {
        Assert.Equal(stateId, branch.StateId);
        Assert.Equal(repeat, branch.RepeatType);
        Assert.Equal(maxTimes, branch.MaxTimes);
        Assert.Equal(0, branch.Cooldown);
        Assert.Equal(1f, branch.GetWeight(cs, owner));
    }

    private static void AssertSingleAttack(CombatState cs, MonsterState owner, MoveStateDef move, int damage)
    {
        Assert.Equal(1, move.Intents.Count);
        var intent = (SingleAttackIntent)move.Intents[0];
        Assert.Equal(IntentType.Attack, intent.IntentType);
        Assert.Equal((decimal)damage, intent.GetBaseDamage(cs, owner));
        Assert.Equal(1, intent.GetRepeats(cs, owner));
        Assert.Equal(damage, intent.GetSingleDamage(cs, MonsterLifecycle.PlayerCreatures(cs), owner));
        Assert.True(move.IntendsToAttack);
    }

    private static void AssertStatus(MoveStateDef move, int cards)
    {
        Assert.Equal(1, move.Intents.Count);
        var intent = (StatusIntent)move.Intents[0];
        Assert.Equal(IntentType.StatusCard, intent.IntentType);
        Assert.Equal(cards, intent.CardCount);
        Assert.False(move.IntendsToAttack);
    }

    private static CombatState NewCombat(int ascension, ulong seed) => NewCombat(ascension, new RunRngSet(new Rng(seed)));

    private static CombatState NewCombat(int ascension, RunRngSet rng)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, PlayerHp, PlayerHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, Encounter, ascension, rng);
        cs.NextCreatureId = 1;
        cs.CurrentSide = CombatSide.Player;
        cs.IsInProgress = true;
        return cs;
    }

    private static MonsterState SpawnAndAdd(CombatState cs, MonsterSpawn spawn)
    {
        var monster = MonsterSpawner.Spawn(cs, spawn);
        cs.Monsters.Add(monster);
        MonsterLifecycle.SetUpForCombat(cs, monster);
        return monster;
    }

    private static void SimulatePerformed(MonsterAiRuntime ai, MoveStateDef move)
    {
        ai.MarkPerformedAtLeastOnce(move);
        ai.OnMovePerformed(move);
    }

    private sealed record RecordedFight(string Seed, string Seeding, int Ascension, int NicheOffset, (int, string, int)[] Opening, (int, string)[][] Turns);

    private sealed class ReferenceAi
    {
        private readonly List<string> _log = new();

        public ReferenceAi(string monsterId)
        {
            MonsterId = monsterId;
        }

        public string MonsterId { get; }

        public string Next { get; private set; } = string.Empty;

        public void Start(Rng monsterAi)
        {
            Next = MonsterId switch
            {
                LeafS => Pick(monsterAi, (Tackle, CannotRepeat(Tackle)), (Goop, CannotRepeat(Goop))),
                TwigS => Tackle,
                LeafM => StickyM,
                TwigM => StickyTwig,
                _ => throw new ArgumentException(MonsterId)
            };
            _log.Add(Next);
        }

        public void Roll(Rng monsterAi)
        {
            Next = MonsterId switch
            {
                LeafS => Pick(monsterAi, (Tackle, CannotRepeat(Tackle)), (Goop, CannotRepeat(Goop))),
                TwigS => Tackle,
                LeafM => Next == StickyM ? Clump : StickyM,
                TwigM => Pick(monsterAi, (Pokey, AtMostTwice(Pokey)), (StickyTwig, CannotRepeat(StickyTwig))),
                _ => throw new ArgumentException(MonsterId)
            };
            _log.Add(Next);
        }

        private float CannotRepeat(string id) => _log.Count > 0 && _log[^1] == id ? 0f : 1f;

        private float AtMostTwice(string id) => _log.Count >= 2 && _log[^1] == id && _log[^2] == id ? 0f : 1f;

        private static string Pick(Rng rng, params (string Id, float Weight)[] branches)
        {
            double sum = 0.0;
            foreach (var b in branches)
            {
                sum += b.Weight;
            }
            float num = rng.NextFloat((float)sum);
            foreach (var b in branches)
            {
                num -= b.Weight;
                if (num <= 0f)
                {
                    return b.Id;
                }
            }
            throw new InvalidOperationException("no branch picked");
        }
    }
}
