using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class MonsterTests
{
    private const int PlayerHp = 80;
    private const string Butt = Nibbit.ButtMoveId;
    private const string Slice = Nibbit.SliceMoveId;
    private const string Hiss = Nibbit.HissMoveId;

    public static void Test_Monsters_IdsMatchSlugifiedClassNames()
    {
        Assert.Equal("NIBBIT", Nibbit.MonsterIdEntry);
        Assert.Equal("NIBBITS_WEAK", NibbitsWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("Nibbit"), Nibbit.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("NibbitsWeak"), NibbitsWeak.EncounterIdEntry);
    }

    public static void Test_Monsters_RegistryContainsNibbitsWeak()
    {
        Assert.True(MonsterRegistry.Get(Nibbit.MonsterIdEntry) is Nibbit);
        var encounter = EncounterRegistry.Get(NibbitsWeak.EncounterIdEntry);
        Assert.True(encounter is NibbitsWeak);
        Assert.True(encounter.IsWeak);
    }

    public static void Test_Monsters_NibbitsWeakSpawnList()
    {
        var cs = NewCombat(0, 1UL);
        var spawns = EncounterRegistry.Get(NibbitsWeak.EncounterIdEntry).GenerateMonsters(cs);
        Assert.Equal(1, spawns.Count);
        Assert.True(ReferenceEquals(MonsterRegistry.Get(Nibbit.MonsterIdEntry), spawns[0].Def));
        Assert.Null(spawns[0].SlotName);

        var monster = SpawnAndAdd(cs, spawns[0]);
        var extra = Nibbit.GetExtra(monster);
        Assert.True(extra.IsAlone);
        Assert.False(extra.IsFront);
        Assert.Equal(1, cs.Monsters.Count);
        Assert.Equal(1, monster.CombatId);
        Assert.Equal(CombatSide.Enemy, monster.Side);
        Assert.True(monster.SpawnedThisTurn);
        Assert.Equal(Nibbit.InitMoveId, monster.Ai.CurrentStateId);
        Assert.Equal(0, monster.Ai.StateLog.Count);
        Assert.Null(monster.Ai.NextMove);
        Assert.False(monster.Ai.PerformedFirstMove);
    }

    public static void Test_Monsters_NibbitHpRangeByAscension()
    {
        var def = MonsterRegistry.Get(Nibbit.MonsterIdEntry);
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool tough = asc >= 8;
            Assert.Equal(tough ? 44 : 42, def.MinInitialHp(cs), $"min hp at A{asc}");
            Assert.Equal(tough ? 48 : 46, def.MaxInitialHp(cs), $"max hp at A{asc}");
        }
    }

    public static void Test_Monsters_NibbitHpRollMatchesNicheDraw()
    {
        foreach (int asc in new[] { 0, 8 })
        {
            int min = asc >= 8 ? 44 : 42;
            var seen = new HashSet<int>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                var shared = new Rng(seed);
                var cs = NewCombat(asc, new RunRngSet(shared));
                int expected = min + new Rng(seed).NextInt(0, 5);
                var monster = SpawnNibbitsWeak(cs);
                Assert.Equal(expected, monster.MaxHp, $"A{asc} seed {seed}");
                Assert.Equal(expected, monster.CurrentHp, $"A{asc} seed {seed}");
                Assert.Equal<int?>(expected, monster.MaxHpBeforeModification, $"A{asc} seed {seed}");
                Assert.Equal(1, shared.Counter, "spawn consumes exactly one draw");
                Assert.True(monster.MaxHp >= min && monster.MaxHp <= min + 4, $"hp {monster.MaxHp} out of range");
                seen.Add(monster.MaxHp);
            }
            Assert.Equal(5, seen.Count, $"every HP value is reachable at A{asc}");
        }
    }

    public static void Test_Monsters_HpRollUsesNicheStreamOnly()
    {
        const ulong seed = 12345UL;
        var cs = NewCombat(0, new RunRngSet(seed));
        int expected = 42 + new RunRngSet(seed).Niche.NextInt(0, 5);
        var monster = SpawnNibbitsWeak(cs);
        Assert.Equal(expected, monster.MaxHp);
        foreach (var type in Enum.GetValues<RunRngType>())
        {
            Assert.Equal(type == RunRngType.Niche ? 1 : 0, cs.Rng.Get(type).Counter, type.ToString());
        }
    }

    public static void Test_Monsters_HpRollSkipsValuesTakenByOtherEnemies()
    {
        var def = MonsterRegistry.Get(Nibbit.MonsterIdEntry);
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var shared = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(shared));
            var first = SpawnAndAdd(cs, new MonsterSpawn(def));
            var second = SpawnAndAdd(cs, new MonsterSpawn(def));

            var probe = new Rng(seed);
            int firstExpected = 42 + probe.NextInt(0, 5);
            var available = new List<int>();
            for (int v = 42; v <= 46; v++)
            {
                if (v != firstExpected)
                {
                    available.Add(v);
                }
            }
            int secondExpected = available[probe.NextInt(0, available.Count)];

            Assert.Equal(firstExpected, first.MaxHp, $"seed {seed}");
            Assert.Equal(secondExpected, second.MaxHp, $"seed {seed}");
            Assert.NotEqual(first.MaxHp, second.MaxHp, $"seed {seed}");
            Assert.Equal(2, shared.Counter);
        }
    }

    public static void Test_Monsters_HpRollFallsBackToFullRangeWhenAllTaken()
    {
        const ulong seed = 7UL;
        var def = MonsterRegistry.Get(Nibbit.MonsterIdEntry);
        var shared = new Rng(seed);
        var cs = NewCombat(0, new RunRngSet(shared));
        for (int hp = 42; hp <= 46; hp++)
        {
            cs.Monsters.Add(new MonsterState(cs.AllocateCreatureId(), def, hp, hp, null, null));
        }
        int expected = new Rng(seed).NextInt(42, 47);
        var monster = SpawnAndAdd(cs, new MonsterSpawn(def));
        Assert.Equal(expected, monster.MaxHp);
        Assert.Equal(1, shared.Counter);
    }

    public static void Test_Monsters_NibbitMoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL);
            bool tough = asc >= 8;
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 13 : 12, Nibbit.ButtDamage(cs), $"butt A{asc}");
            Assert.Equal(deadly ? 7 : 6, Nibbit.SliceDamage(cs), $"slice damage A{asc}");
            Assert.Equal(tough ? 6 : 5, Nibbit.SliceBlock(cs), $"slice block A{asc}");
            Assert.Equal(deadly ? 3 : 2, Nibbit.HissStrengthGain(cs), $"hiss A{asc}");
        }
    }

    public static void Test_Monsters_NibbitGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL);
            var monster = SpawnNibbitsWeak(cs);
            var machine = monster.Ai.Machine;
            Assert.Equal(Nibbit.InitMoveId, machine.InitialStateId);
            Assert.Equal(4, machine.States.Count);

            var init = (ConditionalBranchStateDef)machine.Get(Nibbit.InitMoveId);
            Assert.Equal(1, init.States.Count);
            Assert.Equal(Butt, init.States[0].StateId);
            Assert.False(init.ShouldAppearInLogs);

            var butt = (MoveStateDef)machine.Get(Butt);
            var slice = (MoveStateDef)machine.Get(Slice);
            var hiss = (MoveStateDef)machine.Get(Hiss);
            Assert.Equal(Slice, butt.FollowUpState?.Id);
            Assert.Equal(Hiss, slice.FollowUpState?.Id);
            Assert.Equal(Butt, hiss.FollowUpState?.Id);
            Assert.False(butt.MustPerformOnceBeforeTransitioning);

            Assert.Equal(1, butt.Intents.Count);
            var buttIntent = (SingleAttackIntent)butt.Intents[0];
            Assert.Equal(IntentType.Attack, buttIntent.IntentType);
            Assert.Equal((decimal)Nibbit.ButtDamage(cs), buttIntent.GetBaseDamage(cs, monster));
            Assert.Equal(1, buttIntent.GetRepeats(cs, monster));

            Assert.Equal(2, slice.Intents.Count);
            Assert.Equal(IntentType.Attack, slice.Intents[0].IntentType);
            Assert.Equal((decimal)Nibbit.SliceDamage(cs), ((AttackIntent)slice.Intents[0]).GetBaseDamage(cs, monster));
            Assert.Equal(IntentType.Defend, slice.Intents[1].IntentType);

            Assert.Equal(1, hiss.Intents.Count);
            Assert.Equal(IntentType.Buff, hiss.Intents[0].IntentType);

            Assert.True(butt.IntendsToAttack);
            Assert.True(slice.IntendsToAttack);
            Assert.False(hiss.IntendsToAttack);
        }
    }

    public static void Test_Monsters_NibbitAloneCyclesButtSliceHiss()
    {
        var shared = new Rng(99UL);
        var cs = NewCombat(0, new RunRngSet(shared));
        var monster = SpawnNibbitsWeak(cs);
        var ai = monster.Ai;
        int counterAfterSpawn = shared.Counter;

        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Equal(Butt, ai.NextMove?.Id);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(Butt, ai.NextMove?.Id, "re-roll before the first move keeps the first move");
        Assert.Equal(1, ai.StateLog.Count);

        var expected = new[] { Butt, Slice, Hiss, Butt, Slice, Hiss, Butt };
        var actual = new List<string>();
        for (int turn = 0; turn < expected.Length; turn++)
        {
            if (turn > 0)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
            }
            var move = ai.NextMove!;
            actual.Add(move.Id);
            SimulatePerformed(ai, move);
        }
        Assert.SequenceEqual(expected, actual);
        Assert.SequenceEqual(expected, ai.StateLog);
        Assert.Equal(counterAfterSpawn, shared.Counter, "Nibbit AI consumes no randomness");
    }

    public static void Test_Monsters_NibbitNotAloneOpensByPosition()
    {
        var def = MonsterRegistry.Get(Nibbit.MonsterIdEntry);
        var cs = NewCombat(0, 3UL);
        var front = SpawnAndAdd(cs, new MonsterSpawn(def, null, static m => Nibbit.GetExtra(m).IsFront = true));
        var back = SpawnAndAdd(cs, new MonsterSpawn(def, "back"));
        Assert.Equal(2, ((ConditionalBranchStateDef)front.Ai.Machine.Get(Nibbit.InitMoveId)).States.Count);

        MonsterLifecycle.AfterCreatureAdded(cs, front);
        MonsterLifecycle.AfterCreatureAdded(cs, back);
        Assert.Equal(Slice, front.Ai.NextMove?.Id);
        Assert.Equal(Hiss, back.Ai.NextMove?.Id);

        SimulatePerformed(front.Ai, front.Ai.NextMove!);
        SimulatePerformed(back.Ai, back.Ai.NextMove!);
        MonsterLifecycle.PrepareForNextTurn(cs, front);
        MonsterLifecycle.PrepareForNextTurn(cs, back);
        Assert.Equal(Hiss, front.Ai.NextMove?.Id);
        Assert.Equal(Butt, back.Ai.NextMove?.Id);
    }

    public static void Test_Monsters_ConditionReadsLiveExtraState()
    {
        var cs = NewCombat(0, 5UL);
        var monster = SpawnNibbitsWeak(cs);
        Nibbit.GetExtra(monster).IsAlone = false;
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.RollMove(cs, monster));
    }

    public static void Test_Monsters_InitialRollOnlyOnPlayerSide()
    {
        var cs = NewCombat(0, 6UL);
        cs.CurrentSide = CombatSide.Enemy;
        var monster = SpawnNibbitsWeak(cs);
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Null(monster.Ai.NextMove);
        Assert.Equal(Nibbit.InitMoveId, monster.Ai.CurrentStateId);
    }

    public static void Test_Monsters_RandomBranchWeightRules()
    {
        var cs = NewCombat(0, 3UL);
        var monster = SpawnAndAdd(cs, new MonsterSpawn(new RepeatRulesMonster()));
        var ai = monster.Ai;
        var rand = (RandomBranchStateDef)ai.Machine.Get(RepeatRulesMonster.Rand);
        const string a = RepeatRulesMonster.A;
        const string b = RepeatRulesMonster.B;
        const string c = RepeatRulesMonster.C;
        const string d = RepeatRulesMonster.D;

        float W(int branch, params string[] log)
        {
            ai.StateLog.Clear();
            ai.StateLog.AddRange(log);
            return RandomBranchStateDef.GetStateWeight(cs, rand.States[branch], monster, ai);
        }

        Assert.Equal(1f, W(0));
        Assert.Equal(0f, W(0, a));
        Assert.Equal(1f, W(0, a, b));
        Assert.Equal(0f, W(0, b, a));

        Assert.Equal(1f, W(1, b));
        Assert.Equal(0f, W(1, b, b));
        Assert.Equal(0f, W(1, a, b, b));
        Assert.Equal(1f, W(1, b, a));
        Assert.Equal(1f, W(1, b, b, a));

        Assert.Equal(0.5f, W(2));
        Assert.Equal(0.5f, W(2, a, b, d));
        Assert.Equal(0f, W(2, c, a, b));

        Assert.Equal(1f, W(3));
        Assert.Equal(0f, W(3, d));
        Assert.Equal(0f, W(3, d, a));
        Assert.Equal(1f, W(3, d, a, b));
    }

    public static void Test_Monsters_RandomBranchSequencesRespectRepeatRules()
    {
        var def = new RepeatRulesMonster();
        var seen = new HashSet<string>();
        const int turns = 60;
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var rng = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(rng));
            var monster = SpawnAndAdd(cs, new MonsterSpawn(def));
            var ai = monster.Ai;
            int before = rng.Counter;
            for (int t = 0; t < turns; t++)
            {
                var move = MonsterLifecycle.RollMove(cs, monster);
                SimulatePerformed(ai, move);
            }
            Assert.Equal(before + turns, rng.Counter, "one NextFloat per random-branch roll");

            var log = ai.StateLog;
            Assert.Equal(turns, log.Count);
            int onlyOnceCount = 0;
            for (int i = 0; i < log.Count; i++)
            {
                string id = log[i];
                seen.Add(id);
                if (id == RepeatRulesMonster.A && i >= 1)
                {
                    Assert.NotEqual(RepeatRulesMonster.A, log[i - 1], $"seed {seed} index {i}: CannotRepeat repeated");
                }
                if (id == RepeatRulesMonster.B && i >= 2)
                {
                    Assert.False(log[i - 1] == RepeatRulesMonster.B && log[i - 2] == RepeatRulesMonster.B, $"seed {seed} index {i}: CanRepeatXTimes(2) exceeded");
                }
                if (id == RepeatRulesMonster.C)
                {
                    onlyOnceCount++;
                }
                if (id == RepeatRulesMonster.D)
                {
                    for (int k = 1; k <= 2 && i - k >= 0; k++)
                    {
                        Assert.NotEqual(RepeatRulesMonster.D, log[i - k], $"seed {seed} index {i}: cooldown violated");
                    }
                }
            }
            Assert.True(onlyOnceCount <= 1, $"seed {seed}: UseOnlyOnce used {onlyOnceCount} times");
        }
        Assert.True(seen.Contains(RepeatRulesMonster.A));
        Assert.True(seen.Contains(RepeatRulesMonster.B));
        Assert.True(seen.Contains(RepeatRulesMonster.C));
        Assert.True(seen.Contains(RepeatRulesMonster.D));
    }

    public static void Test_Monsters_RandomBranchSelectionMatchesWeightedDraw()
    {
        var def = new WeightedMonster();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var rng = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(rng));
            var monster = SpawnAndAdd(cs, new MonsterSpawn(def));
            var probe = rng.DeepClone();
            var move = MonsterLifecycle.RollMove(cs, monster);

            float num = probe.NextFloat(6f);
            string expected;
            num -= 1f;
            if (num <= 0f)
            {
                expected = WeightedMonster.X;
            }
            else
            {
                num -= 2f;
                expected = num <= 0f ? WeightedMonster.Y : WeightedMonster.Z;
            }
            Assert.Equal(expected, move.Id, $"seed {seed}");
            Assert.Equal(probe.Counter, rng.Counter);
            counts[move.Id] = counts.TryGetValue(move.Id, out int n) ? n + 1 : 1;
        }
        Assert.Equal(3, counts.Count);
        Assert.True(counts[WeightedMonster.Z] > counts[WeightedMonster.X], "weight 3 branch is picked more often than weight 1");
    }

    public static void Test_Monsters_RandomBranchAllZeroWeightsPicksFirstBranch()
    {
        var rng = new Rng(11UL);
        var cs = NewCombat(0, new RunRngSet(rng));
        var monster = SpawnAndAdd(cs, new MonsterSpawn(new OnlyOnceMonster()));
        var ai = monster.Ai;
        var first = MonsterLifecycle.RollMove(cs, monster);
        Assert.Equal(OnlyOnceMonster.Once, first.Id);
        SimulatePerformed(ai, first);
        int before = rng.Counter;
        var second = MonsterLifecycle.RollMove(cs, monster);
        Assert.Equal(OnlyOnceMonster.Once, second.Id);
        Assert.Equal(before + 1, rng.Counter);
        Assert.SequenceEqual(new[] { OnlyOnceMonster.Once, OnlyOnceMonster.Once }, ai.StateLog);
    }

    public static void Test_Monsters_GraphConstructionErrors()
    {
        var a = new MoveStateDef("SAME", NoOpMove);
        var a2 = new MoveStateDef("SAME", NoOpMove);
        Assert.Throws<ArgumentException>(() => new MonsterMoveStateMachineDef(new AiStateDef[] { a, a2 }, a));

        var rand = new RandomBranchStateDef("RAND");
        Assert.Throws<ArgumentException>(() => rand.AddBranch(a, 0, MoveRepeatType.CanRepeatXTimes, (_, _) => 1f));

        var cs = NewCombat(0, 8UL);
        var dangling = new MoveStateDef("DANGLING", NoOpMove) { FollowUpStateId = "MISSING" };
        var machine = new MonsterMoveStateMachineDef(new AiStateDef[] { dangling }, dangling);
        var monster = new MonsterState(cs.AllocateCreatureId(), MonsterRegistry.Get(Nibbit.MonsterIdEntry), 10, 10, null, null);
        var ai = new MonsterAiRuntime(machine);
        monster.InitAi(ai);
        Assert.Equal(1, ai.StateLog.Count, "an initial move state is logged on construction");
        Assert.Equal("DANGLING", ai.RollMove(cs, monster, cs.Rng.MonsterAi).Id);
        SimulatePerformed(ai, ai.NextMove!);
        Assert.Throws<InvalidOperationException>(() => ai.RollMove(cs, monster, cs.Rng.MonsterAi));
    }

    public static void Test_Monsters_StunHoldsUntilPerformedThenResumes()
    {
        var cs = NewCombat(0, 21UL);
        var monster = SpawnNibbitsWeak(cs);
        var ai = monster.Ai;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        SimulatePerformed(ai, ai.NextMove!);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(Slice, ai.NextMove?.Id);

        MonsterLifecycle.StunInternal(cs, monster, static (_, _, _) => { }, null);
        Assert.True(monster.IsStunned);
        Assert.Equal(MoveStateDef.StunnedMoveId, ai.CurrentStateId);
        Assert.Equal(IntentType.Stun, ai.NextMove!.Intents[0].IntentType);
        Assert.Equal(Slice, ai.NextMove!.FollowUpStateId);

        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(MoveStateDef.StunnedMoveId, ai.NextMove?.Id, "an unperformed stun cannot transition away");

        SimulatePerformed(ai, ai.NextMove!);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(Slice, ai.NextMove?.Id);
        Assert.False(monster.IsStunned);
        Assert.SequenceEqual(new[] { Butt, Slice, Slice }, ai.StateLog);
    }

    public static void Test_Monsters_SetMoveImmediateRespectsMustPerformOnce()
    {
        var cs = NewCombat(0, 23UL);
        var monster = SpawnNibbitsWeak(cs);
        var ai = monster.Ai;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        MonsterLifecycle.StunInternal(cs, monster, static (_, _, _) => { }, Hiss);
        var hiss = (MoveStateDef)ai.Machine.Get(Hiss);

        MonsterLifecycle.SetMoveImmediate(monster, hiss);
        Assert.Equal(MoveStateDef.StunnedMoveId, ai.NextMove?.Id);

        MonsterLifecycle.SetMoveImmediate(monster, hiss, forceTransition: true);
        Assert.Equal(Hiss, ai.NextMove?.Id);
        Assert.Equal(Hiss, ai.CurrentStateId);
    }

    public static void Test_Monsters_AiRuntimeCloneIsIndependent()
    {
        var cs = NewCombat(0, 31UL);
        var monster = SpawnNibbitsWeak(cs);
        var ai = monster.Ai;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        SimulatePerformed(ai, ai.NextMove!);

        var clone = ai.DeepClone();
        Assert.True(ReferenceEquals(ai.Machine, clone.Machine), "the move graph is shared");
        Assert.Equal(ai.CurrentStateId, clone.CurrentStateId);
        Assert.SequenceEqual(ai.StateLog, clone.StateLog);
        Assert.True(ReferenceEquals(ai.NextMove, clone.NextMove));
        Assert.Equal(ai.PerformedFirstMove, clone.PerformedFirstMove);
        Assert.True(clone.PerformedAtLeastOnce.SetEquals(ai.PerformedAtLeastOnce));
        Assert.False(ReferenceEquals(ai.StateLog, clone.StateLog));
        Assert.False(ReferenceEquals(ai.PerformedAtLeastOnce, clone.PerformedAtLeastOnce));

        clone.RollMove(cs, monster, cs.Rng.MonsterAi);
        var butt = (MoveStateDef)ai.Machine.Get(Butt);
        Assert.Equal(Slice, clone.CurrentStateId);
        Assert.Equal(Butt, ai.CurrentStateId);
        Assert.Equal(2, clone.StateLog.Count);
        Assert.Equal(1, ai.StateLog.Count);
        Assert.True(ai.HasPerformedAtLeastOnce(butt));
        Assert.False(clone.HasPerformedAtLeastOnce(butt));
    }

    public static void Test_Monsters_AiRuntimeCanonicalTracksState()
    {
        var cs = NewCombat(0, 37UL);
        var monster = SpawnNibbitsWeak(cs);
        var ai = monster.Ai;
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        var clone = ai.DeepClone();
        Assert.Equal(StateHasher.Hash(ai), StateHasher.Hash(clone));

        SimulatePerformed(clone, clone.NextMove!);
        Assert.NotEqual(StateHasher.Hash(ai), StateHasher.Hash(clone));

        SimulatePerformed(ai, ai.NextMove!);
        Assert.Equal(StateHasher.Hash(ai), StateHasher.Hash(clone));

        clone.RollMove(cs, monster, cs.Rng.MonsterAi);
        Assert.NotEqual(StateHasher.Hash(ai), StateHasher.Hash(clone));
    }

    public static void Test_Monsters_NibbitExtraStateCloneAndCanonical()
    {
        var extra = new NibbitExtraState { IsAlone = true };
        var clone = (NibbitExtraState)extra.DeepClone();
        Assert.True(clone.IsAlone);
        Assert.False(clone.IsFront);
        Assert.Equal(StateHasher.Hash(extra), StateHasher.Hash(clone));
        clone.IsFront = true;
        Assert.False(extra.IsFront);
        Assert.NotEqual(StateHasher.Hash(extra), StateHasher.Hash(clone));
    }

    public static void Test_Monsters_MonsterStateCloneCarriesAiAndExtra()
    {
        var cs = NewCombat(0, 41UL);
        var monster = SpawnNibbitsWeak(cs);
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        var clone = monster.DeepClone();
        Assert.Equal(StateHasher.Hash(monster), StateHasher.Hash(clone));
        Assert.False(ReferenceEquals(monster.Ai, clone.Ai));
        Assert.False(ReferenceEquals(monster.Extra, clone.Extra));
        Assert.True(Nibbit.GetExtra(clone).IsAlone);

        SimulatePerformed(clone.Ai, clone.Ai.NextMove!);
        MonsterLifecycle.PrepareForNextTurn(cs, clone);
        Assert.Equal(Slice, clone.Ai.NextMove?.Id);
        Assert.Equal(Butt, monster.Ai.NextMove?.Id);
        Assert.NotEqual(StateHasher.Hash(monster), StateHasher.Hash(clone));
    }

    public static void Test_Monsters_PerformMoveRemovesMonsterThatDiedDuringItsMove()
    {
        var cs = NewCombat(0, 59UL);
        var monster = SpawnAndAdd(cs, new MonsterSpawn(new SelfDestructMonster()));
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Equal(SelfDestructMonster.Boom, monster.Ai.NextMove?.Id);
        MonsterLifecycle.OnSideSwitch(monster);
        MonsterLifecycle.TakeTurn(cs, monster);
        Assert.True(monster.IsDead, "the move ran while IsPerformingMove was set and targeted the player");
        Assert.False(monster.Ai.IsPerformingMove);
        Assert.True(monster.Ai.PerformedFirstMove);
        Assert.Equal(0, cs.Monsters.Count);
    }

    public static void Test_Monsters_TakeTurnRequiresEnemySide()
    {
        var cs = NewCombat(0, 47UL);
        var monster = SpawnNibbitsWeak(cs);
        monster.Side = CombatSide.Player;
        Assert.Throws<InvalidOperationException>(() => MonsterLifecycle.TakeTurn(cs, monster));
    }

    public static void Test_Monsters_AttackIntentDisplayIncludesStrength()
    {
        var cs = NewCombat(0, 53UL);
        var monster = SpawnNibbitsWeak(cs);
        var intent = (AttackIntent)((MoveStateDef)monster.Ai.Machine.Get(Butt)).Intents[0];
        var targets = MonsterLifecycle.PlayerCreatures(cs);
        Assert.Equal(12, intent.GetSingleDamage(cs, targets, monster));
        PowerCmd.Apply<StrengthPower>(cs, monster, 2m, monster, null);
        Assert.Equal(14, intent.GetSingleDamage(cs, targets, monster));
        Assert.Equal(14, intent.GetTotalDamage(cs, targets, monster));
    }

    public static void Test_Monsters_NibbitMovesApplyDamageBlockAndStrength()
    {
        foreach (int asc in new[] { 0, 10 })
        {
            var cs = NewCombat(asc, 43UL);
            var monster = SpawnNibbitsWeak(cs);
            var player = cs.Player;
            int butt = Nibbit.ButtDamage(cs);
            int slice = Nibbit.SliceDamage(cs);
            int block = Nibbit.SliceBlock(cs);
            int strength = Nibbit.HissStrengthGain(cs);

            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp, player.CurrentHp, "a monster spawned this turn does not act");
            Assert.False(monster.Ai.PerformedFirstMove);

            MonsterLifecycle.OnSideSwitch(monster);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp - butt, player.CurrentHp, $"BUTT_MOVE A{asc}");
            Assert.True(monster.Ai.PerformedFirstMove);
            Assert.False(monster.Ai.IsPerformingMove);

            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            Assert.Equal(Slice, monster.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp - butt - slice, player.CurrentHp, $"SLICE_MOVE damage A{asc}");
            Assert.Equal(block, monster.Block, $"SLICE_MOVE block A{asc}");

            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            Assert.Equal(Hiss, monster.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(strength, monster.GetPowerAmount<StrengthPower>(), $"HISS_MOVE A{asc}");
            Assert.Equal(PlayerHp - butt - slice, player.CurrentHp);

            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            Assert.Equal(Butt, monster.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp - butt - slice - (butt + strength), player.CurrentHp, $"strengthened BUTT_MOVE A{asc}");
            Assert.Equal(1, cs.Monsters.Count);
        }
    }

    public static void Test_Monsters_CombatSimStartsNibbitsWeak()
    {
        var sim = CombatSim.Create(1UL, StartingDecks.Ironclad, NibbitsWeak.EncounterIdEntry);
        Assert.Equal(1, sim.State.Monsters.Count);
        var nibbit = sim.State.Monsters[0];
        Assert.Equal(Nibbit.MonsterIdEntry, nibbit.MonsterId);
        Assert.True(nibbit.MaxHp >= 42 && nibbit.MaxHp <= 46, $"hp {nibbit.MaxHp}");
        Assert.Equal(nibbit.MaxHp, nibbit.CurrentHp);
        Assert.True(Nibbit.GetExtra(nibbit).IsAlone);
        Assert.Equal(Butt, nibbit.Ai.NextMove?.Id);
    }

    public static void Test_Monsters_CombatSimNibbitIntentCycle()
    {
        var sim = CombatSim.Create(2UL, StartingDecks.Ironclad, NibbitsWeak.EncounterIdEntry);
        int startHp = sim.State.Player.CurrentHp;
        var expected = new[] { Butt, Slice, Hiss, Butt };
        var seen = new List<string>();
        for (int turn = 0; turn < expected.Length && !sim.IsTerminal; turn++)
        {
            seen.Add(sim.State.Monsters[0].Ai.NextMove!.Id);
            sim.Step(GameAction.EndTurn);
        }
        Assert.SequenceEqual(expected, seen);
        Assert.Equal(startHp - 12 - 6 - (12 + 2), sim.State.Player.CurrentHp, "BUTT 12, SLICE 6, HISS +2 Strength, BUTT 14 with no player block");
    }

    private static CombatState NewCombat(int ascension, ulong seed) => NewCombat(ascension, new RunRngSet(new Rng(seed)));

    private static CombatState NewCombat(int ascension, RunRngSet rng)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, PlayerHp, PlayerHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, NibbitsWeak.EncounterIdEntry, ascension, rng);
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

    private static MonsterState SpawnNibbitsWeak(CombatState cs)
    {
        var spawns = EncounterRegistry.Get(NibbitsWeak.EncounterIdEntry).GenerateMonsters(cs);
        Assert.Equal(1, spawns.Count);
        return SpawnAndAdd(cs, spawns[0]);
    }

    private static void SimulatePerformed(MonsterAiRuntime ai, MoveStateDef move)
    {
        ai.MarkPerformedAtLeastOnce(move);
        ai.OnMovePerformed(move);
    }

    private static void NoOpMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }

    private sealed class RepeatRulesMonster : MonsterDef
    {
        public const string A = "A_CANNOT_REPEAT";
        public const string B = "B_TWICE_MAX";
        public const string C = "C_ONLY_ONCE";
        public const string D = "D_COOLDOWN_TWO";
        public const string Rand = "RAND";

        public RepeatRulesMonster()
            : base("MONSTER_TESTS_REPEAT_RULES")
        {
        }

        public override int MinInitialHp(CombatState cs) => 30;

        public override int MaxInitialHp(CombatState cs) => 30;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var a = new MoveStateDef(A, NoOpMove, Intents.Buff);
            var b = new MoveStateDef(B, NoOpMove, Intents.Buff);
            var c = new MoveStateDef(C, NoOpMove, Intents.Buff);
            var d = new MoveStateDef(D, NoOpMove, Intents.Buff);
            var rand = new RandomBranchStateDef(Rand);
            rand.AddBranch(a, MoveRepeatType.CannotRepeat);
            rand.AddBranch(b, 2);
            rand.AddBranch(c, MoveRepeatType.UseOnlyOnce, 0.5f);
            rand.AddBranch(d, 2, MoveRepeatType.CanRepeatForever);
            a.FollowUpState = rand;
            b.FollowUpState = rand;
            c.FollowUpState = rand;
            d.FollowUpState = rand;
            return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, a, b, c, d }, rand);
        }
    }

    private sealed class WeightedMonster : MonsterDef
    {
        public const string X = "X_WEIGHT_ONE";
        public const string Y = "Y_WEIGHT_TWO";
        public const string Z = "Z_WEIGHT_THREE";

        public WeightedMonster()
            : base("MONSTER_TESTS_WEIGHTED")
        {
        }

        public override int MinInitialHp(CombatState cs) => 30;

        public override int MaxInitialHp(CombatState cs) => 30;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var x = new MoveStateDef(X, NoOpMove, Intents.Buff);
            var y = new MoveStateDef(Y, NoOpMove, Intents.Buff);
            var z = new MoveStateDef(Z, NoOpMove, Intents.Buff);
            var rand = new RandomBranchStateDef("RAND");
            rand.AddBranch(x, MoveRepeatType.CanRepeatForever, 1f);
            rand.AddBranch(y, MoveRepeatType.CanRepeatForever, 2f);
            rand.AddBranch(z, MoveRepeatType.CanRepeatForever, 3f);
            x.FollowUpState = rand;
            y.FollowUpState = rand;
            z.FollowUpState = rand;
            return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, x, y, z }, rand);
        }
    }

    private sealed class SelfDestructMonster : MonsterDef
    {
        public const string Boom = "BOOM";

        public SelfDestructMonster()
            : base("MONSTER_TESTS_SELF_DESTRUCT")
        {
        }

        public override int MinInitialHp(CombatState cs) => 30;

        public override int MaxInitialHp(CombatState cs) => 30;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var boom = new MoveStateDef(Boom, Explode, Intents.Unknown);
            boom.FollowUpState = boom;
            return new MonsterMoveStateMachineDef(new AiStateDef[] { boom }, boom);
        }

        private static void Explode(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
        {
            if (self.Ai.IsPerformingMove && targets.Count == 1 && ReferenceEquals(targets[0], cs.Player))
            {
                self.CurrentHp = 0;
            }
        }
    }

    private sealed class OnlyOnceMonster : MonsterDef
    {
        public const string Once = "ONCE";

        public OnlyOnceMonster()
            : base("MONSTER_TESTS_ONLY_ONCE")
        {
        }

        public override int MinInitialHp(CombatState cs) => 30;

        public override int MaxInitialHp(CombatState cs) => 30;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var once = new MoveStateDef(Once, NoOpMove, Intents.Buff);
            var rand = new RandomBranchStateDef("RAND");
            rand.AddBranch(once, MoveRepeatType.UseOnlyOnce);
            once.FollowUpState = rand;
            return new MonsterMoveStateMachineDef(new AiStateDef[] { rand, once }, rand);
        }
    }
}
