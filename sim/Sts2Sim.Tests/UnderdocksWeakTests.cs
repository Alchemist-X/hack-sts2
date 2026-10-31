using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;
using Sts2Sim.Diff;

namespace Sts2Sim.Tests;

public static class UnderdocksWeakTests
{
    private const int PlayerHp = 80;
    private const string Oil = SludgeSpinner.OilSprayMoveId;
    private const string Slam = SludgeSpinner.SlamMoveId;
    private const string Rage = SludgeSpinner.RageMoveId;
    private const string Spit = Toadpole.SpikeSpitMoveId;
    private const string Whirl = Toadpole.WhirlMoveId;
    private const string Spiken = Toadpole.SpikenMoveId;
    private static readonly string[] RandBranchOrder = { Oil, Slam, Rage };

    public static void Test_UnderdocksWeak_IdsMatchSlugifiedClassNames()
    {
        Assert.Equal("SLUDGE_SPINNER", SludgeSpinner.MonsterIdEntry);
        Assert.Equal("TOADPOLE", Toadpole.MonsterIdEntry);
        Assert.Equal("SLUDGE_SPINNER_WEAK", SludgeSpinnerWeak.EncounterIdEntry);
        Assert.Equal("TOADPOLES_WEAK", ToadpolesWeak.EncounterIdEntry);
        Assert.Equal("THORNS_POWER", ThornsPower.PowerId);
        Assert.Equal(DeterministicHash.Slugify("SludgeSpinner"), SludgeSpinner.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("Toadpole"), Toadpole.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("SludgeSpinnerWeak"), SludgeSpinnerWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("ToadpolesWeak"), ToadpolesWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("ThornsPower"), ThornsPower.PowerId);
        Assert.Equal(DeterministicHash.Slugify("Omnislice"), ThornsPower.OmnisliceCardId);
    }

    public static void Test_UnderdocksWeak_RegistriesContainDefs()
    {
        Assert.True(MonsterRegistry.Get(SludgeSpinner.MonsterIdEntry) is SludgeSpinner);
        Assert.True(MonsterRegistry.Get(Toadpole.MonsterIdEntry) is Toadpole);
        var sludge = EncounterRegistry.Get(SludgeSpinnerWeak.EncounterIdEntry);
        var toads = EncounterRegistry.Get(ToadpolesWeak.EncounterIdEntry);
        Assert.True(sludge is SludgeSpinnerWeak);
        Assert.True(toads is ToadpolesWeak);
        Assert.True(sludge.IsWeak);
        Assert.True(toads.IsWeak);
        Assert.Equal(0, sludge.Slots.Count);
        Assert.Equal(0, toads.Slots.Count);
        var thorns = PowerRegistry.Create(ThornsPower.PowerId);
        Assert.True(thorns is ThornsPower);
        Assert.Equal(PowerType.Buff, thorns.Type);
        Assert.Equal(PowerStackType.Counter, thorns.StackType);
        Assert.Equal(PowerInstanceType.None, thorns.InstanceType);
        Assert.False(thorns.AllowNegative);
    }

    public static void Test_UnderdocksWeak_SpawnListsAndCreationOrder()
    {
        var cs = NewCombat(0, 1UL);
        var sludgeSpawns = EncounterRegistry.Get(SludgeSpinnerWeak.EncounterIdEntry).GenerateMonsters(cs);
        Assert.Equal(1, sludgeSpawns.Count);
        Assert.True(ReferenceEquals(MonsterRegistry.Get(SludgeSpinner.MonsterIdEntry), sludgeSpawns[0].Def));
        Assert.Null(sludgeSpawns[0].SlotName);
        Assert.Null(sludgeSpawns[0].Configure);

        var toadSpawns = EncounterRegistry.Get(ToadpolesWeak.EncounterIdEntry).GenerateMonsters(cs);
        Assert.Equal(2, toadSpawns.Count);
        Assert.True(ReferenceEquals(toadSpawns[0].Def, toadSpawns[1].Def));
        Assert.True(ReferenceEquals(MonsterRegistry.Get(Toadpole.MonsterIdEntry), toadSpawns[0].Def));
        Assert.Null(toadSpawns[0].SlotName);
        Assert.Null(toadSpawns[1].SlotName);

        var spawned = new List<MonsterState>();
        MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(ToadpolesWeak.EncounterIdEntry), spawned);
        Assert.Equal(2, spawned.Count);
        Assert.Equal(1, spawned[0].CombatId);
        Assert.Equal(2, spawned[1].CombatId);
        Assert.True(Toadpole.GetExtra(spawned[0]).IsFront, "the first creature is the front toadpole");
        Assert.False(Toadpole.GetExtra(spawned[1]).IsFront);
        Assert.True(ReferenceEquals(spawned[0], cs.Monsters[0]));
        Assert.True(ReferenceEquals(spawned[1], cs.Monsters[1]));
    }

    public static void Test_UnderdocksWeak_HpRangesByAscension()
    {
        var sludge = MonsterRegistry.Get(SludgeSpinner.MonsterIdEntry);
        var toad = MonsterRegistry.Get(Toadpole.MonsterIdEntry);
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool tough = asc >= 8;
            Assert.Equal(tough ? 41 : 37, sludge.MinInitialHp(cs), $"sludge min A{asc}");
            Assert.Equal(tough ? 42 : 39, sludge.MaxInitialHp(cs), $"sludge max A{asc}");
            Assert.Equal(tough ? 22 : 21, toad.MinInitialHp(cs), $"toadpole min A{asc}");
            Assert.Equal(tough ? 26 : 25, toad.MaxInitialHp(cs), $"toadpole max A{asc}");
        }
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerHpRollMatchesNicheDraw()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int min = asc >= 8 ? 41 : 37;
            int count = asc >= 8 ? 2 : 3;
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var shared = new Rng(seed);
                var cs = NewCombat(asc, new RunRngSet(shared));
                int expected = min + new Rng(seed).NextInt(0, count);
                var spawned = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry);
                Assert.Equal(1, spawned.Count);
                var monster = spawned[0];
                Assert.Equal(expected, monster.MaxHp, $"A{asc} seed {seed}");
                Assert.Equal(expected, monster.CurrentHp, $"A{asc} seed {seed}");
                Assert.Equal<int?>(expected, monster.MaxHpBeforeModification, $"A{asc} seed {seed}");
                Assert.Equal(1, shared.Counter, "one Niche draw per monster");
                seen.Add(monster.MaxHp);
            }
            Assert.Equal(count, seen.Count, $"every HP value is reachable at A{asc}");
        }
    }

    public static void Test_UnderdocksWeak_ToadpoleHpRollsOneNicheDrawPerMonsterInCreationOrder()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int min = asc >= 8 ? 22 : 21;
            var seenFront = new HashSet<int>();
            var seenBack = new HashSet<int>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var shared = new Rng(seed);
                var cs = NewCombat(asc, new RunRngSet(shared));
                var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);

                var probe = new Rng(seed);
                int frontExpected = min + probe.NextInt(0, 5);
                var remaining = new List<int>();
                for (int v = min; v <= min + 4; v++)
                {
                    if (v != frontExpected)
                    {
                        remaining.Add(v);
                    }
                }
                int backExpected = remaining[probe.NextInt(0, remaining.Count)];

                Assert.Equal(frontExpected, spawned[0].MaxHp, $"front A{asc} seed {seed}");
                Assert.Equal(backExpected, spawned[1].MaxHp, $"back A{asc} seed {seed}");
                Assert.Equal(frontExpected, spawned[0].CurrentHp);
                Assert.Equal(backExpected, spawned[1].CurrentHp);
                Assert.NotEqual(spawned[0].MaxHp, spawned[1].MaxHp, "the second roll excludes the first toadpole's HP");
                Assert.Equal(2, shared.Counter, "one Niche draw per toadpole");
                seenFront.Add(spawned[0].MaxHp);
                seenBack.Add(spawned[1].MaxHp);
            }
            Assert.Equal(5, seenFront.Count, $"front HP covers the range at A{asc}");
            Assert.Equal(5, seenBack.Count, $"back HP covers the range at A{asc}");
        }
    }

    public static void Test_UnderdocksWeak_SpawnUsesNicheStreamOnly()
    {
        foreach (var encounter in new[] { SludgeSpinnerWeak.EncounterIdEntry, ToadpolesWeak.EncounterIdEntry })
        {
            const ulong seed = 4242UL;
            var cs = NewCombat(0, new RunRngSet(seed));
            var spawned = SpawnEncounter(cs, encounter);
            foreach (var type in Enum.GetValues<RunRngType>())
            {
                Assert.Equal(type == RunRngType.Niche ? spawned.Count : 0, cs.Rng.Get(type).Counter, $"{encounter} {type}");
            }
        }
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerMoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 9 : 8, SludgeSpinner.OilSprayDamage(cs), $"oil spray A{asc}");
            Assert.Equal(deadly ? 12 : 11, SludgeSpinner.SlamDamage(cs), $"slam A{asc}");
            Assert.Equal(deadly ? 7 : 6, SludgeSpinner.RageDamage(cs), $"rage A{asc}");
        }
        Assert.Equal(1, SludgeSpinner.OilSprayWeakAmount);
        Assert.Equal(3, SludgeSpinner.RageStrengthAmount);
    }

    public static void Test_UnderdocksWeak_ToadpoleMoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 4 : 3, Toadpole.SpikeSpitDamage(cs), $"spike spit A{asc}");
            Assert.Equal(deadly ? 8 : 7, Toadpole.WhirlDamage(cs), $"whirl A{asc}");
        }
        Assert.Equal(3, Toadpole.SpikeSpitRepeat);
        Assert.Equal(2, Toadpole.SpikenAmount);
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
            var machine = monster.Ai.Machine;
            Assert.Equal(Oil, machine.InitialStateId, "the initial state is the OIL_SPRAY move itself");
            Assert.Equal(4, machine.States.Count);
            Assert.SequenceEqual(new[] { SludgeSpinner.RandStateId, Oil, Slam, Rage }, machine.States.Keys);

            var rand = (RandomBranchStateDef)machine.Get(SludgeSpinner.RandStateId);
            Assert.False(rand.ShouldAppearInLogs);
            Assert.Equal(3, rand.States.Count);
            for (int i = 0; i < 3; i++)
            {
                var branch = rand.States[i];
                Assert.Equal(RandBranchOrder[i], branch.StateId);
                Assert.Equal(MoveRepeatType.CannotRepeat, branch.RepeatType);
                Assert.Equal(0, branch.Cooldown);
                Assert.Equal(1f, branch.GetWeight(cs, monster));
            }

            var oil = (MoveStateDef)machine.Get(Oil);
            var slam = (MoveStateDef)machine.Get(Slam);
            var rage = (MoveStateDef)machine.Get(Rage);
            foreach (var move in new[] { oil, slam, rage })
            {
                Assert.True(ReferenceEquals(rand, move.FollowUpState), $"{move.Id} follows up into RAND");
                Assert.Null(move.FollowUpStateId);
                Assert.False(move.MustPerformOnceBeforeTransitioning);
                Assert.True(move.IntendsToAttack);
            }

            Assert.Equal(2, oil.Intents.Count);
            var oilAttack = (SingleAttackIntent)oil.Intents[0];
            Assert.Equal((decimal)SludgeSpinner.OilSprayDamage(cs), oilAttack.GetBaseDamage(cs, monster));
            Assert.Equal(1, oilAttack.GetRepeats(cs, monster));
            Assert.Equal(IntentType.Debuff, oil.Intents[1].IntentType);
            Assert.False(((DebuffIntent)oil.Intents[1]).Strong);

            Assert.Equal(1, slam.Intents.Count);
            Assert.Equal((decimal)SludgeSpinner.SlamDamage(cs), ((SingleAttackIntent)slam.Intents[0]).GetBaseDamage(cs, monster));

            Assert.Equal(2, rage.Intents.Count);
            Assert.Equal((decimal)SludgeSpinner.RageDamage(cs), ((SingleAttackIntent)rage.Intents[0]).GetBaseDamage(cs, monster));
            Assert.Equal(IntentType.Buff, rage.Intents[1].IntentType);
        }
    }

    public static void Test_UnderdocksWeak_ToadpoleGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
            var monster = spawned[0];
            var machine = monster.Ai.Machine;
            Assert.Equal(Toadpole.InitMoveId, machine.InitialStateId);
            Assert.SequenceEqual(new[] { Toadpole.InitMoveId, Spiken, Spit, Whirl }, machine.States.Keys);
            Assert.False(ReferenceEquals(machine, spawned[1].Ai.Machine), "each monster builds its own graph");

            var init = (ConditionalBranchStateDef)machine.Get(Toadpole.InitMoveId);
            Assert.False(init.ShouldAppearInLogs);
            Assert.Equal(2, init.States.Count, "both opener branches exist whatever IsFront is");
            Assert.Equal(Whirl, init.States[0].StateId);
            Assert.Equal(Spiken, init.States[1].StateId);

            var spit = (MoveStateDef)machine.Get(Spit);
            var whirl = (MoveStateDef)machine.Get(Whirl);
            var spiken = (MoveStateDef)machine.Get(Spiken);
            Assert.Equal(Spiken, whirl.FollowUpState?.Id);
            Assert.Equal(Spit, spiken.FollowUpState?.Id);
            Assert.Equal(Whirl, spit.FollowUpState?.Id);

            Assert.Equal(1, spit.Intents.Count);
            var spitIntent = (MultiAttackIntent)spit.Intents[0];
            Assert.Equal(IntentType.Attack, spitIntent.IntentType);
            Assert.Equal((decimal)Toadpole.SpikeSpitDamage(cs), spitIntent.GetBaseDamage(cs, monster));
            Assert.Equal(3, spitIntent.GetRepeats(cs, monster));
            var targets = MonsterLifecycle.PlayerCreatures(cs);
            Assert.Equal(Toadpole.SpikeSpitDamage(cs), spitIntent.GetSingleDamage(cs, targets, monster));
            Assert.Equal(3 * Toadpole.SpikeSpitDamage(cs), spitIntent.GetTotalDamage(cs, targets, monster));

            Assert.Equal(1, whirl.Intents.Count);
            var whirlIntent = (SingleAttackIntent)whirl.Intents[0];
            Assert.Equal((decimal)Toadpole.WhirlDamage(cs), whirlIntent.GetBaseDamage(cs, monster));
            Assert.Equal(1, whirlIntent.GetRepeats(cs, monster));

            Assert.Equal(1, spiken.Intents.Count);
            Assert.Equal(IntentType.Buff, spiken.Intents[0].IntentType);
            Assert.False(spiken.IntendsToAttack);
            Assert.True(spit.IntendsToAttack);
            Assert.True(whirl.IntendsToAttack);
        }
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerOpensWithOilSprayWithoutDraw()
    {
        var cs = NewCombat(0, new RunRngSet(77UL));
        var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
        var ai = monster.Ai;
        Assert.SequenceEqual(new[] { Oil }, ai.StateLog, "the initial move state is logged when the machine is built");
        Assert.Null(ai.NextMove);

        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Equal(Oil, ai.NextMove?.Id);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(Oil, ai.NextMove?.Id, "the turn-1 re-roll keeps the first move");
        Assert.SequenceEqual(new[] { Oil }, ai.StateLog, "rolls before the first performed move log nothing");
        Assert.Equal(0, cs.Rng.MonsterAi.Counter, "the opener draws nothing");

        SimulatePerformed(ai, ai.NextMove!);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(1, cs.Rng.MonsterAi.Counter, "every later roll draws one NextFloat");
        Assert.True(ai.NextMove!.Id is Slam or Rage, "CannotRepeat excludes OIL_SPRAY after OIL_SPRAY");
        Assert.SequenceEqual(new[] { Oil, ai.NextMove.Id }, ai.StateLog, "RAND is not logged, the move it picks is");
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerRandWeightsFollowCannotRepeat()
    {
        var cs = NewCombat(0, 3UL);
        var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
        var ai = monster.Ai;
        var rand = (RandomBranchStateDef)ai.Machine.Get(SludgeSpinner.RandStateId);

        float[] Weights(params string[] log)
        {
            ai.StateLog.Clear();
            ai.StateLog.AddRange(log);
            var w = new float[rand.States.Count];
            for (int i = 0; i < w.Length; i++)
            {
                w[i] = RandomBranchStateDef.GetStateWeight(cs, rand.States[i], monster, ai);
            }
            return w;
        }

        Assert.SequenceEqual(new[] { 1f, 1f, 1f }, Weights());
        Assert.SequenceEqual(new[] { 0f, 1f, 1f }, Weights(Oil));
        Assert.SequenceEqual(new[] { 1f, 0f, 1f }, Weights(Oil, Slam));
        Assert.SequenceEqual(new[] { 1f, 1f, 0f }, Weights(Oil, Slam, Rage));
        Assert.SequenceEqual(new[] { 0f, 1f, 1f }, Weights(Rage, Slam, Oil), "only the last logged move is excluded");
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerSequenceMatchesReplayedMonsterAiDraws()
    {
        const int turns = 12;
        var seenSecond = new HashSet<string>();
        var seenAll = new HashSet<string>();
        for (ulong aiSeed = 1; aiSeed <= 300; aiSeed++)
        {
            var set = new RunRngSet(9UL);
            set.MockRng(RunRngType.MonsterAi, aiSeed);
            var cs = NewCombat(aiSeed % 2 == 0 ? 0 : 10, set);
            var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
            var ai = monster.Ai;
            MonsterLifecycle.AfterCreatureAdded(cs, monster);

            var probe = new Rng(aiSeed);
            var expected = new List<string> { Oil };
            for (int t = 1; t < turns; t++)
            {
                expected.Add(ReplayRandRoll(probe, expected[^1]));
            }

            var actual = new List<string>();
            for (int t = 0; t < turns; t++)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
                var move = ai.NextMove!;
                actual.Add(move.Id);
                SimulatePerformed(ai, move);
            }
            Assert.SequenceEqual(expected, actual, $"MonsterAi seed {aiSeed}");
            Assert.SequenceEqual(expected, ai.StateLog, $"MonsterAi seed {aiSeed}");
            Assert.Equal(turns - 1, cs.Rng.MonsterAi.Counter, "one draw per roll after the opener");
            Assert.Equal(probe.Counter, cs.Rng.MonsterAi.Counter);
            for (int i = 1; i < actual.Count; i++)
            {
                Assert.NotEqual(actual[i - 1], actual[i], $"seed {aiSeed} turn {i}: CannotRepeat");
            }
            seenSecond.Add(actual[1]);
            foreach (var id in actual)
            {
                seenAll.Add(id);
            }
        }
        Assert.SequenceEqual(new[] { Rage, Slam }, seenSecond.OrderBy(s => s, StringComparer.Ordinal), "both second moves occur");
        Assert.Equal(3, seenAll.Count);
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerScriptedBranchBoundary()
    {
        var cases = new List<(ulong Seed, float Draw)>();
        for (ulong aiSeed = 1; cases.Count < 40 && aiSeed < 5000; aiSeed++)
        {
            float draw = new Rng(aiSeed).NextFloat(2f);
            cases.Add((aiSeed, draw));
        }
        bool sawLow = false;
        bool sawHigh = false;
        foreach (var (aiSeed, draw) in cases)
        {
            var set = new RunRngSet(5UL);
            set.MockRng(RunRngType.MonsterAi, aiSeed);
            var cs = NewCombat(0, set);
            var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            SimulatePerformed(monster.Ai, monster.Ai.NextMove!);
            var second = MonsterLifecycle.RollMove(cs, monster);
            string expected = draw <= 1f ? Slam : Rage;
            Assert.Equal(expected, second.Id, $"draw {draw} on seed {aiSeed}: weights [0, 1, 1] give SLAM for (0, 1] and RAGE above");
            sawLow |= draw <= 1f;
            sawHigh |= draw > 1f;
        }
        Assert.True(sawLow && sawHigh, "both sides of the boundary are exercised");
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerRecordedFirstFights()
    {
        var cases = new (string Version, string Seeding, string Seed, int Ascension, int NicheOffset, int Hp, string[] Moves)[]
        {
            ("v0.111.0", RngSetup.SeedingXxh64, "G0G2JMFEYHRS", 10, 7, 42, new[] { Oil, Slam, Oil }),
            ("v0.111.0", RngSetup.SeedingXxh64, "DSB363J2LQWZ", 4, 1, 39, Array.Empty<string>()),
            ("v0.110.1", RngSetup.SeedingXxh64, "1YFPCSHL7GRU", 9, 0, 41, new[] { Oil, Rage }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "8UA4FD2WAT", 7, 0, 37, new[] { Oil, Rage, Slam, Rage }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "N5CJ4ETMB5", 8, 0, 41, new[] { Oil, Rage, Slam, Oil }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "SDE1XAV9DM", 9, 1, 41, new[] { Oil, Slam, Oil, Slam }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "0X624QSWSM", 3, 6, 37, new[] { Oil, Rage, Slam }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "TZULW8B9K5", 4, 0, 37, new[] { Oil, Rage, Oil }),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "DEMNMB4MXQ", 10, 0, 42, new[] { Oil, Slam, Oil }),
        };
        foreach (var c in cases)
        {
            string label = $"{c.Version} {c.Seed} A{c.Ascension}";
            var rng = RngSetup.Create(c.Seeding, c.Seed);
            RngSetup.Advance(rng.Niche, c.NicheOffset);
            var cs = NewCombat(c.Ascension, rng);
            var spawned = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry);
            Assert.Equal(1, spawned.Count, label);
            Assert.Equal(c.Hp, spawned[0].MaxHp, label);
            var moves = PlayMoves(cs, spawned, c.Moves.Length)[0];
            Assert.SequenceEqual(c.Moves, moves, label);
        }
    }

    public static void Test_UnderdocksWeak_ToadpolesRecordedFirstFights()
    {
        var cases = new (string Version, string Seeding, string Seed, int Ascension, int NicheOffset, int FrontHp, int BackHp)[]
        {
            ("v0.111.0", RngSetup.SeedingXxh64, "NBNDT3S61AUM", 4, 2, 22, 23),
            ("v0.110.1", RngSetup.SeedingXxh64, "Q9UNSU305H87", 9, 0, 25, 23),
            ("v0.110.1", RngSetup.SeedingXxh64, "15TMTYNMWP0B", 9, 0, 24, 23),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "94UYFQVD4Y", 8, 6, 23, 24),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "15KK92L7ZC", 8, 0, 25, 26),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "A752ZUM0NF", 1, 0, 24, 22),
            ("v0.107.1", RngSetup.SeedingLegacyV18, "VYZBSBYJX1", 10, 0, 23, 26),
        };
        foreach (var c in cases)
        {
            string label = $"{c.Version} {c.Seed} A{c.Ascension}";
            var rng = RngSetup.Create(c.Seeding, c.Seed);
            RngSetup.Advance(rng.Niche, c.NicheOffset);
            var cs = NewCombat(c.Ascension, rng);
            var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
            Assert.Equal(c.FrontHp, spawned[0].MaxHp, label);
            Assert.Equal(c.BackHp, spawned[1].MaxHp, label);
            var moves = PlayMoves(cs, spawned, 4);
            Assert.SequenceEqual(new[] { Spiken, Spit, Whirl, Spiken }, moves[0], label);
            Assert.SequenceEqual(new[] { Whirl, Spiken, Spit, Whirl }, moves[1], label);
            Assert.Equal(0, cs.Rng.MonsterAi.Counter, label);
        }
    }

    public static void Test_UnderdocksWeak_ToadpolesCycleWithoutDraws()
    {
        var shared = new Rng(99UL);
        var cs = NewCombat(0, new RunRngSet(shared));
        var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
        int counterAfterSpawn = shared.Counter;
        Assert.Equal(2, counterAfterSpawn);
        foreach (var m in spawned)
        {
            Assert.Equal(0, m.Ai.StateLog.Count, "the conditional initial state is not logged");
            MonsterLifecycle.AfterCreatureAdded(cs, m);
        }
        Assert.Equal(Spiken, spawned[0].Ai.NextMove?.Id);
        Assert.Equal(Whirl, spawned[1].Ai.NextMove?.Id);
        Assert.SequenceEqual(new[] { Spiken }, spawned[0].Ai.StateLog, "the move the branch enters is logged on the first roll");
        Assert.SequenceEqual(new[] { Whirl }, spawned[1].Ai.StateLog);

        var moves = PlayMoves(cs, spawned, 7, rollOpeners: false);
        Assert.SequenceEqual(new[] { Spiken, Spit, Whirl, Spiken, Spit, Whirl, Spiken }, moves[0]);
        Assert.SequenceEqual(new[] { Whirl, Spiken, Spit, Whirl, Spiken, Spit, Whirl }, moves[1]);
        Assert.SequenceEqual(moves[0], spawned[0].Ai.StateLog);
        Assert.SequenceEqual(moves[1], spawned[1].Ai.StateLog);
        Assert.Equal(counterAfterSpawn, shared.Counter, "Toadpole AI consumes no randomness");
    }

    public static void Test_UnderdocksWeak_ToadpoleOpenerReadsLiveIsFront()
    {
        var cs = NewCombat(0, 5UL);
        var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
        Toadpole.GetExtra(spawned[0]).IsFront = false;
        Toadpole.GetExtra(spawned[1]).IsFront = true;
        MonsterLifecycle.AfterCreatureAdded(cs, spawned[0]);
        MonsterLifecycle.AfterCreatureAdded(cs, spawned[1]);
        Assert.Equal(Whirl, spawned[0].Ai.NextMove?.Id, "the condition reads IsFront at roll time, not at graph build time");
        Assert.Equal(Spiken, spawned[1].Ai.NextMove?.Id);
    }

    public static void Test_UnderdocksWeak_InitialRollOnlyOnPlayerSide()
    {
        var cs = NewCombat(0, 6UL);
        cs.CurrentSide = CombatSide.Enemy;
        var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
        spawned.AddRange(SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry));
        foreach (var m in spawned)
        {
            MonsterLifecycle.AfterCreatureAdded(cs, m);
            Assert.Null(m.Ai.NextMove);
        }
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerMovesApplyDamageWeakAndStrength()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 43UL);
            var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
            var player = cs.Player;
            int oil = SludgeSpinner.OilSprayDamage(cs);
            int slam = SludgeSpinner.SlamDamage(cs);
            int rage = SludgeSpinner.RageDamage(cs);
            MonsterLifecycle.AfterCreatureAdded(cs, monster);

            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp, player.CurrentHp, "a monster spawned this turn does not act");
            MonsterLifecycle.OnSideSwitch(monster);

            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp - oil, player.CurrentHp, $"OIL_SPRAY damage A{asc}");
            var weak = Assert.NotNull(player.GetPower<WeakPower>());
            Assert.Equal(SludgeSpinner.OilSprayWeakAmount, weak.Amount, "OIL_SPRAY applies Weak 1");
            Assert.Equal(monster.CombatId, weak.ApplierId);
            Assert.True(weak.SkipNextDurationTick, "a debuff applied to the player skips its next duration tick");
            int hp = player.CurrentHp;

            ForceMove(monster, Rage);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(hp - rage, player.CurrentHp, $"RAGE_MOVE damage A{asc}, before its Strength");
            Assert.Equal(SludgeSpinner.RageStrengthAmount, monster.GetPowerAmount<StrengthPower>());
            hp = player.CurrentHp;

            ForceMove(monster, Slam);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(hp - (slam + 3), player.CurrentHp, $"SLAM_MOVE with Strength 3 A{asc}");
            hp = player.CurrentHp;

            ForceMove(monster, Rage);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(hp - (rage + 3), player.CurrentHp);
            Assert.Equal(6, monster.GetPowerAmount<StrengthPower>(), "Strength stacks per RAGE_MOVE");
            hp = player.CurrentHp;

            ForceMove(monster, Oil);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(hp - (oil + 6), player.CurrentHp);
            Assert.Equal(2, player.GetPowerAmount<WeakPower>(), "a second OIL_SPRAY stacks Weak on the existing instance");
            Assert.Equal(1, cs.Monsters.Count);
        }
    }

    public static void Test_UnderdocksWeak_SludgeSpinnerIntentPreviewTracksStrength()
    {
        var cs = NewCombat(9, 53UL);
        var monster = SpawnEncounter(cs, SludgeSpinnerWeak.EncounterIdEntry)[0];
        var targets = MonsterLifecycle.PlayerCreatures(cs);
        var slam = (AttackIntent)((MoveStateDef)monster.Ai.Machine.Get(Slam)).Intents[0];
        Assert.Equal(12, slam.GetSingleDamage(cs, targets, monster));
        PowerCmd.Apply<StrengthPower>(cs, monster, SludgeSpinner.RageStrengthAmount, monster, null);
        Assert.Equal(15, slam.GetSingleDamage(cs, targets, monster));
        PowerCmd.Apply<WeakPower>(cs, monster, 1m, cs.Player, null);
        Assert.Equal(11, slam.GetSingleDamage(cs, targets, monster), "(12 + 3) * 0.75 truncates to 11");
    }

    public static void Test_UnderdocksWeak_ToadpoleMovesThornsAndDamage()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 61UL);
            var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
            var front = spawned[0];
            var back = spawned[1];
            var player = cs.Player;
            int spit = Toadpole.SpikeSpitDamage(cs);
            int whirl = Toadpole.WhirlDamage(cs);
            foreach (var m in spawned)
            {
                MonsterLifecycle.AfterCreatureAdded(cs, m);
                MonsterLifecycle.OnSideSwitch(m);
            }

            MonsterLifecycle.TakeTurn(cs, front);
            var thorns = Assert.NotNull(front.GetPower<ThornsPower>(), "SPIKEN_MOVE applies Thorns");
            Assert.Equal(Toadpole.SpikenAmount, thorns.Amount);
            Assert.Equal(front.CombatId, thorns.ApplierId);
            Assert.False(thorns.SkipNextDurationTick);
            Assert.Equal(PlayerHp, player.CurrentHp);

            MonsterLifecycle.TakeTurn(cs, back);
            Assert.Equal(PlayerHp - whirl, player.CurrentHp, $"WHIRL_MOVE A{asc}");
            int hp = player.CurrentHp;

            foreach (var m in spawned)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, m);
            }
            Assert.Equal(Spit, front.Ai.NextMove?.Id);
            Assert.Equal(Spiken, back.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, front);
            Assert.False(front.HasPower<ThornsPower>(), "SPIKE_SPIT_MOVE removes the Thorns it stacked (2 - 2 = 0)");
            Assert.Equal(hp - 3 * spit, player.CurrentHp, $"SPIKE_SPIT_MOVE hits {Toadpole.SpikeSpitRepeat} times for {spit} at A{asc}");
            MonsterLifecycle.TakeTurn(cs, back);
            Assert.Equal(Toadpole.SpikenAmount, back.GetPowerAmount<ThornsPower>());
            Assert.Equal(hp - 3 * spit, player.CurrentHp, "SPIKEN_MOVE deals no damage");
        }
    }

    public static void Test_UnderdocksWeak_ThornsStacksAndNegativeApplication()
    {
        var cs = NewCombat(0, 67UL);
        var spawned = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry);
        var toad = spawned[0];
        foreach (var m in spawned)
        {
            MonsterLifecycle.AfterCreatureAdded(cs, m);
            MonsterLifecycle.OnSideSwitch(m);
        }
        ForceMove(toad, Spiken);
        MonsterLifecycle.TakeTurn(cs, toad);
        ForceMove(toad, Spiken);
        MonsterLifecycle.TakeTurn(cs, toad);
        Assert.Equal(4, toad.GetPowerAmount<ThornsPower>(), "Thorns is a Counter power that stacks");
        Assert.Equal(1, toad.Powers.Count, "one non-instanced Thorns power");
        ForceMove(toad, Spit);
        MonsterLifecycle.TakeTurn(cs, toad);
        Assert.Equal(2, toad.GetPowerAmount<ThornsPower>(), "SPIKE_SPIT removes only SpikenAmount");

        var other = spawned[1];
        Assert.False(other.HasPower<ThornsPower>());
        ForceMove(other, Spit);
        MonsterLifecycle.TakeTurn(cs, other);
        Assert.Equal(-2, other.GetPowerAmount<ThornsPower>(), "a first application of -2 attaches Thorns(-2), as PowerCmd.Apply does in the game");
    }

    public static void Test_UnderdocksWeak_ThornsRetaliatesAgainstPoweredHits()
    {
        var cs = NewCombat(0, 71UL);
        var toad = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry)[0];
        var player = cs.Player;
        PowerCmd.Apply<ThornsPower>(cs, toad, 2m, toad, null);
        int toadHp = toad.CurrentHp;

        CreatureCmd.Damage(cs, toad, 5m, ValueProp.Move, player, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "a powered hit costs the dealer Thorns damage");
        Assert.Equal(toadHp - 5, toad.CurrentHp);

        CreatureCmd.Damage(cs, toad, 5m, ValueProp.Move | ValueProp.Unpowered, player, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "unpowered damage does not trigger Thorns");

        CreatureCmd.Damage(cs, toad, 5m, ValueProp.Unpowered, player, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "non-move damage does not trigger Thorns");

        CreatureCmd.Damage(cs, toad, 1m, ValueProp.Move, null, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "damage without a dealer does not trigger Thorns");

        var back = MonsterSpawner.Spawn(cs, new MonsterSpawn(MonsterRegistry.Get(Toadpole.MonsterIdEntry)));
        cs.Monsters.Add(back);
        CreatureCmd.Damage(cs, back, 1m, ValueProp.Move, player, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "Thorns reacts only to damage aimed at its owner");

        player.Block = 5;
        CreatureCmd.Damage(cs, toad, 1m, ValueProp.Move, player, null, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp);
        Assert.Equal(3, player.Block, "Thorns damage is blockable");

        player.Block = 0;
        var strike = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), cs.AllocateCardInstanceId());
        var attack = DamageCmd.Attack(cs, 1m).FromCard(strike, null).Targeting(toad).WithHitCount(3).Execute();
        Assert.Equal(3, attack.HitsCompleted);
        Assert.Equal(PlayerHp - 2 - 6, player.CurrentHp, "each powered hit triggers Thorns");
    }

    public static void Test_UnderdocksWeak_ThornsTriggersBeforeTheKillingHit()
    {
        var cs = NewCombat(0, 73UL);
        var toad = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry)[0];
        var player = cs.Player;
        PowerCmd.Apply<ThornsPower>(cs, toad, 2m, toad, null);
        CreatureCmd.Damage(cs, toad, toad.CurrentHp + 10, ValueProp.Move, player, null, null);
        Assert.True(toad.IsDead);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "BeforeDamageReceived fires even when the hit is lethal");
        Assert.False(toad.HasPower<ThornsPower>(), "Thorns is removed with its dead owner");
    }

    public static void Test_UnderdocksWeak_ThornsTriggersForOmnisliceSecondaryDamage()
    {
        var cs = NewCombat(0, 79UL);
        var toad = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry)[0];
        var player = cs.Player;
        PowerCmd.Apply<ThornsPower>(cs, toad, 2m, toad, null);
        var omnislice = new CardInstance(new FakeOmnislice(), cs.AllocateCardInstanceId());
        CreatureCmd.Damage(cs, toad, 3m, ValueProp.Unpowered, player, omnislice, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "cardSource is Omnislice also triggers Thorns");
        var strike = new CardInstance(CardRegistry.Get(StrikeIronclad.CardId), cs.AllocateCardInstanceId());
        CreatureCmd.Damage(cs, toad, 3m, ValueProp.Unpowered, player, strike, null);
        Assert.Equal(PlayerHp - 2, player.CurrentHp, "other unpowered card damage does not");
    }

    public static void Test_UnderdocksWeak_CloneAndCanonical()
    {
        var extra = new ToadpoleExtraState { IsFront = true };
        var clone = (ToadpoleExtraState)extra.DeepClone();
        Assert.True(clone.IsFront);
        Assert.Equal(StateHasher.Hash(extra), StateHasher.Hash(clone));
        clone.IsFront = false;
        Assert.True(extra.IsFront);
        Assert.NotEqual(StateHasher.Hash(extra), StateHasher.Hash(clone));

        var cs = NewCombat(0, 83UL);
        var toad = SpawnEncounter(cs, ToadpolesWeak.EncounterIdEntry)[0];
        MonsterLifecycle.AfterCreatureAdded(cs, toad);
        PowerCmd.Apply<ThornsPower>(cs, toad, 2m, toad, null);
        var toadClone = toad.DeepClone();
        Assert.Equal(StateHasher.Hash(toad), StateHasher.Hash(toadClone));
        Assert.False(ReferenceEquals(toad.Extra, toadClone.Extra));
        Assert.True(Toadpole.GetExtra(toadClone).IsFront);
        var thornsClone = Assert.NotNull(toadClone.GetPower<ThornsPower>());
        Assert.False(ReferenceEquals(toad.GetPower<ThornsPower>(), thornsClone));
        Assert.True(ReferenceEquals(toadClone, thornsClone.Owner));
        thornsClone.SetAmount(5);
        Assert.Equal(2, toad.GetPowerAmount<ThornsPower>());
        Assert.NotEqual(StateHasher.Hash(toad), StateHasher.Hash(toadClone));
    }

    public static void Test_UnderdocksWeak_CombatSimSludgeSpinnerWeakEndToEnd()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var sim = CombatSim.Create(seed, StartingDecks.Ironclad, SludgeSpinnerWeak.EncounterIdEntry, asc);
                var cs = sim.State;
                Assert.Equal(1, cs.Monsters.Count);
                var monster = cs.Monsters[0];
                Assert.Equal(SludgeSpinner.MonsterIdEntry, monster.MonsterId);
                Assert.Equal(1, monster.CombatId);
                int min = asc >= 8 ? 41 : 37;
                int max = asc >= 8 ? 42 : 39;
                Assert.Equal(min + new RunRngSet(seed).Niche.NextInt(0, max - min + 1), monster.MaxHp, $"seed {seed}");
                Assert.Equal(0, cs.Rng.MonsterAi.Counter);
                Assert.Equal(Oil, monster.Ai.NextMove?.Id);

                var probe = new RunRngSet(seed).MonsterAi;
                string expectedMove = Oil;
                int strength = 0;
                bool weakSeen = false;
                for (int turn = 1; turn <= 6 && !sim.IsTerminal; turn++)
                {
                    Assert.Equal(expectedMove, monster.Ai.NextMove?.Id, $"seed {seed} turn {turn}");
                    int hp = cs.Player.CurrentHp;
                    int baseDamage = expectedMove == Oil ? SludgeSpinner.OilSprayDamage(cs) : expectedMove == Slam ? SludgeSpinner.SlamDamage(cs) : SludgeSpinner.RageDamage(cs);
                    sim.Step(GameAction.EndTurn);
                    Assert.Equal(hp - (baseDamage + strength), cs.Player.CurrentHp, $"seed {seed} turn {turn} {expectedMove}");
                    if (expectedMove == Rage)
                    {
                        strength += SludgeSpinner.RageStrengthAmount;
                    }
                    Assert.Equal(strength, monster.GetPowerAmount<StrengthPower>());
                    if (expectedMove == Oil)
                    {
                        Assert.Equal(1, cs.Player.GetPowerAmount<WeakPower>(), $"seed {seed} turn {turn}: Weak 1 survives its first enemy turn end");
                        weakSeen = true;
                    }
                    else
                    {
                        Assert.False(cs.Player.HasPower<WeakPower>(), $"seed {seed} turn {turn}: Weak 1 expired at the next enemy turn end");
                    }
                    expectedMove = ReplayRandRoll(probe, expectedMove);
                    Assert.Equal(turn, cs.Rng.MonsterAi.Counter, "one MonsterAi draw per player turn from turn 2");
                }
                Assert.True(weakSeen);
            }
        }
    }

    public static void Test_UnderdocksWeak_CombatSimToadpolesWeakEndToEnd()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var sim = CombatSim.Create(3UL, StartingDecks.Ironclad, ToadpolesWeak.EncounterIdEntry, asc);
            var cs = sim.State;
            Assert.Equal(2, cs.Monsters.Count);
            var front = cs.Monsters[0];
            var back = cs.Monsters[1];
            Assert.Equal(1, front.CombatId);
            Assert.Equal(2, back.CombatId);
            Assert.True(Toadpole.GetExtra(front).IsFront);
            Assert.False(Toadpole.GetExtra(back).IsFront);
            Assert.NotEqual(front.MaxHp, back.MaxHp);
            Assert.Equal(2, cs.Rng.Niche.Counter, "one Niche draw per toadpole");
            Assert.Equal(Spiken, front.Ai.NextMove?.Id);
            Assert.Equal(Whirl, back.Ai.NextMove?.Id);

            int spit = 3 * Toadpole.SpikeSpitDamage(cs);
            int whirl = Toadpole.WhirlDamage(cs);
            int hp = cs.Player.CurrentHp;
            var expectedLoss = new[] { whirl, spit, whirl + spit, whirl, spit };
            var expectedFrontThorns = new[] { 2, 0, 0, 2, 0 };
            var expectedBackThorns = new[] { 0, 2, 0, 0, 2 };
            for (int turn = 0; turn < expectedLoss.Length; turn++)
            {
                sim.Step(GameAction.EndTurn);
                Assert.False(sim.IsTerminal, $"A{asc} turn {turn + 1}");
                Assert.Equal(hp - expectedLoss[turn], cs.Player.CurrentHp, $"A{asc} enemy turn {turn + 1}");
                hp = cs.Player.CurrentHp;
                Assert.Equal(expectedFrontThorns[turn], front.GetPowerAmount<ThornsPower>(), $"front thorns after enemy turn {turn + 1}");
                Assert.Equal(expectedBackThorns[turn], back.GetPowerAmount<ThornsPower>(), $"back thorns after enemy turn {turn + 1}");
            }
            Assert.Equal(0, cs.Rng.MonsterAi.Counter, "TOADPOLES_WEAK never draws MonsterAi");
        }
    }

    public static void Test_UnderdocksWeak_CombatSimBackToadpoleCarriesOnAlone()
    {
        var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, ToadpolesWeak.EncounterIdEntry);
        var cs = sim.State;
        var front = cs.Monsters[0];
        var back = cs.Monsters[1];
        CreatureCmd.Damage(cs, front, front.CurrentHp, ValueProp.Unpowered, null, null, null);
        Assert.True(front.IsDead);
        Assert.Equal(1, cs.Monsters.Count, "the dead front toadpole leaves the enemy list");
        Assert.False(sim.IsTerminal);
        int hp = cs.Player.CurrentHp;
        var expected = new[] { Whirl, Spiken, Spit, Whirl };
        var losses = new[] { Toadpole.WhirlDamage(cs), 0, 3 * Toadpole.SpikeSpitDamage(cs), Toadpole.WhirlDamage(cs) };
        for (int turn = 0; turn < expected.Length; turn++)
        {
            Assert.Equal(expected[turn], back.Ai.NextMove?.Id, $"turn {turn + 1}");
            sim.Step(GameAction.EndTurn);
            Assert.Equal(hp - losses[turn], cs.Player.CurrentHp, $"turn {turn + 1}");
            hp = cs.Player.CurrentHp;
        }
        Assert.False(Toadpole.GetExtra(back).IsFront, "IsFront never changes when the other toadpole dies");
    }

    public static void Test_UnderdocksWeak_CombatSimStrikeIntoThorns()
    {
        bool tested = false;
        for (ulong seed = 1; seed <= 10 && !tested; seed++)
        {
            var sim = CombatSim.Create(seed, StartingDecks.Ironclad, ToadpolesWeak.EncounterIdEntry);
            sim.Step(GameAction.EndTurn);
            var cs = sim.State;
            var front = cs.Monsters[0];
            Assert.Equal(2, front.GetPowerAmount<ThornsPower>());
            int strikeIndex = -1;
            var hand = cs.Player.Hand.Cards;
            for (int i = 0; i < hand.Count; i++)
            {
                if (hand[i].Id == "STRIKE_IRONCLAD")
                {
                    strikeIndex = i;
                    break;
                }
            }
            if (strikeIndex < 0)
            {
                continue;
            }
            int playerHp = cs.Player.CurrentHp;
            int playerBlock = cs.Player.Block;
            int frontHp = front.CurrentHp;
            sim.Step(GameAction.Play(strikeIndex, 0));
            Assert.Equal(frontHp - 6, front.CurrentHp, $"seed {seed}: Strike hits the front toadpole");
            Assert.Equal(playerHp - Math.Max(0, 2 - playerBlock), cs.Player.CurrentHp, $"seed {seed}: Thorns 2 hits back");
            tested = true;
        }
        Assert.True(tested, "some seed has a Strike in hand on turn 2");
    }

    public static void Test_UnderdocksWeak_CombatSimCloneIsIndependent()
    {
        var sim = CombatSim.Create(17UL, StartingDecks.Ironclad, ToadpolesWeak.EncounterIdEntry, 9);
        sim.Step(GameAction.EndTurn);
        var clone = sim.Clone();
        Assert.Equal(sim.StateHash(), clone.StateHash());
        ulong before = sim.StateHash();
        clone.Step(GameAction.EndTurn);
        Assert.Equal(before, sim.StateHash(), "stepping the clone leaves the source unchanged");
        Assert.NotEqual(before, clone.StateHash());
        sim.Step(GameAction.EndTurn);
        Assert.Equal(sim.StateHash(), clone.StateHash(), "equal histories hash equal");

        var sludge = CombatSim.Create(17UL, StartingDecks.Ironclad, SludgeSpinnerWeak.EncounterIdEntry, 9);
        sludge.Step(GameAction.EndTurn);
        var sludgeClone = sludge.Clone();
        sludge.Step(GameAction.EndTurn);
        sludgeClone.Step(GameAction.EndTurn);
        Assert.Equal(sludge.StateHash(), sludgeClone.StateHash(), "the cloned MonsterAi stream replays the same roll");
    }

    private static string ReplayRandRoll(Rng probe, string last)
    {
        var weights = new float[RandBranchOrder.Length];
        double sum = 0.0;
        for (int i = 0; i < weights.Length; i++)
        {
            weights[i] = RandBranchOrder[i] == last ? 0f : 1f;
            sum += weights[i];
        }
        float num = probe.NextFloat((float)sum);
        for (int i = 0; i < weights.Length; i++)
        {
            num -= weights[i];
            if (num <= 0f)
            {
                return RandBranchOrder[i];
            }
        }
        throw new InvalidOperationException("no branch");
    }

    private static List<List<string>> PlayMoves(CombatState cs, List<MonsterState> monsters, int turns, bool rollOpeners = true)
    {
        var result = new List<List<string>>();
        foreach (var m in monsters)
        {
            if (rollOpeners)
            {
                MonsterLifecycle.AfterCreatureAdded(cs, m);
            }
            result.Add(new List<string>());
        }
        for (int t = 0; t < turns; t++)
        {
            for (int i = 0; i < monsters.Count; i++)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monsters[i]);
            }
            for (int i = 0; i < monsters.Count; i++)
            {
                var move = monsters[i].Ai.NextMove!;
                result[i].Add(move.Id);
                SimulatePerformed(monsters[i].Ai, move);
            }
        }
        return result;
    }

    private static void ForceMove(MonsterState monster, string moveId)
    {
        MonsterLifecycle.SetMoveImmediate(monster, (MoveStateDef)monster.Ai.Machine.Get(moveId), forceTransition: true);
    }

    private static CombatState NewCombat(int ascension, ulong seed) => NewCombat(ascension, new RunRngSet(new Rng(seed)));

    private static CombatState NewCombat(int ascension, RunRngSet rng)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, PlayerHp, PlayerHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, SludgeSpinnerWeak.EncounterIdEntry, ascension, rng);
        cs.NextCreatureId = 1;
        cs.CurrentSide = CombatSide.Player;
        cs.IsInProgress = true;
        return cs;
    }

    private static List<MonsterState> SpawnEncounter(CombatState cs, string encounterId)
    {
        var spawned = new List<MonsterState>();
        MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(encounterId), spawned);
        foreach (var m in spawned)
        {
            MonsterLifecycle.SetUpForCombat(cs, m);
        }
        return spawned;
    }

    private static void SimulatePerformed(MonsterAiRuntime ai, MoveStateDef move)
    {
        ai.MarkPerformedAtLeastOnce(move);
        ai.OnMovePerformed(move);
    }

    [SimExclude]
    private sealed class FakeOmnislice : CardDef
    {
        public FakeOmnislice()
            : base(ThornsPower.OmnisliceCardId, 0, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }
}
