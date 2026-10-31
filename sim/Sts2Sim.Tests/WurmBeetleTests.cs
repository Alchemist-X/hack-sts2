using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class WurmBeetleTests
{
    private const int PlayerHp = 80;
    private const string Wurm = "FUZZY_WURM_CRAWLER";
    private const string Beetle = "SHRINKER_BEETLE";
    private const string WurmWeak = "FUZZY_WURM_CRAWLER_WEAK";
    private const string BeetleWeak = "SHRINKER_BEETLE_WEAK";
    private const string Shrink = "SHRINK_POWER";
    private const string FirstGoop = "FIRST_ACID_GOOP";
    private const string Goop = "ACID_GOOP";
    private const string Inhale = "INHALE";
    private const string Shrinker = "SHRINKER_MOVE";
    private const string Chomp = "CHOMP_MOVE";
    private const string Stomp = "STOMP_MOVE";
    private const string StrikeId = "STRIKE_IRONCLAD";
    private const string BashId = "BASH";
    private const string DefendId = "DEFEND_IRONCLAD";

    public static void Test_WurmBeetle_IdsMatchSlugifiedClassNames()
    {
        Assert.Equal(Wurm, FuzzyWurmCrawler.MonsterIdEntry);
        Assert.Equal(Beetle, ShrinkerBeetle.MonsterIdEntry);
        Assert.Equal(WurmWeak, FuzzyWurmCrawlerWeak.EncounterIdEntry);
        Assert.Equal(BeetleWeak, ShrinkerBeetleWeak.EncounterIdEntry);
        Assert.Equal(Shrink, ShrinkPower.PowerId);
        Assert.Equal(DeterministicHash.Slugify("FuzzyWurmCrawler"), FuzzyWurmCrawler.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("ShrinkerBeetle"), ShrinkerBeetle.MonsterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("FuzzyWurmCrawlerWeak"), FuzzyWurmCrawlerWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("ShrinkerBeetleWeak"), ShrinkerBeetleWeak.EncounterIdEntry);
        Assert.Equal(DeterministicHash.Slugify("ShrinkPower"), ShrinkPower.PowerId);
        Assert.Equal(FirstGoop, FuzzyWurmCrawler.FirstAcidGoopMoveId);
        Assert.Equal(Goop, FuzzyWurmCrawler.AcidGoopMoveId);
        Assert.Equal(Inhale, FuzzyWurmCrawler.InhaleMoveId);
        Assert.Equal(Shrinker, ShrinkerBeetle.ShrinkerMoveId);
        Assert.Equal(Chomp, ShrinkerBeetle.ChompMoveId);
        Assert.Equal(Stomp, ShrinkerBeetle.StompMoveId);
    }

    public static void Test_WurmBeetle_RegistryContainsEncountersMonstersAndPower()
    {
        Assert.True(MonsterRegistry.Get(Wurm) is FuzzyWurmCrawler);
        Assert.True(MonsterRegistry.Get(Beetle) is ShrinkerBeetle);
        foreach (var id in new[] { WurmWeak, BeetleWeak })
        {
            var encounter = EncounterRegistry.Get(id);
            Assert.True(encounter.IsWeak, id);
            Assert.Equal(0, encounter.Slots.Count, $"{id} defines no slots");
        }
        Assert.True(EncounterRegistry.Get(WurmWeak) is FuzzyWurmCrawlerWeak);
        Assert.True(EncounterRegistry.Get(BeetleWeak) is ShrinkerBeetleWeak);
        Assert.True(PowerRegistry.Create(Shrink) is ShrinkPower);
        Assert.Equal(typeof(ShrinkPower), PowerRegistry.TypeOf(Shrink));
        foreach (var id in new[] { Wurm, Beetle })
        {
            var def = MonsterRegistry.Get(id);
            Assert.False(def.UsesPrivateRng, $"{id} never reads MonsterModel.Rng");
            Assert.Null(def.CreateExtraState(), $"{id} has no rule-relevant per-instance field");
        }
    }

    public static void Test_WurmBeetle_SpawnListsAreOneUnslottedMonster()
    {
        foreach (var (encounterId, monsterId, initialMove) in new[] { (WurmWeak, Wurm, FirstGoop), (BeetleWeak, Beetle, Shrinker) })
        {
            var cs = NewCombat(0, 1UL, encounterId);
            var spawns = EncounterRegistry.Get(encounterId).GenerateMonsters(cs);
            Assert.Equal(1, spawns.Count, encounterId);
            Assert.True(ReferenceEquals(MonsterRegistry.Get(monsterId), spawns[0].Def), encounterId);
            Assert.Null(spawns[0].SlotName, encounterId);
            Assert.Null(spawns[0].Configure, encounterId);

            var monster = SpawnAndAdd(cs, spawns[0]);
            Assert.Null(monster.Extra);
            Assert.Equal(1, monster.CombatId);
            Assert.Equal(CombatSide.Enemy, monster.Side);
            Assert.True(monster.SpawnedThisTurn);
            Assert.Equal(initialMove, monster.Ai.CurrentStateId);
            Assert.SequenceEqual(new[] { initialMove }, monster.Ai.StateLog, "the initial move state is logged when the machine is built");
            Assert.Null(monster.Ai.NextMove);
            Assert.False(monster.Ai.PerformedFirstMove);
        }
    }

    public static void Test_WurmBeetle_EncountersIgnoreTheEncounterRng()
    {
        foreach (var id in new[] { WurmWeak, BeetleWeak })
        {
            var encounter = EncounterRegistry.Get(id);
            var cs = NewCombat(0, 3UL, id);
            var plain = encounter.GenerateMonsters(cs);
            var rng = new Rng(12345UL);
            var withRng = encounter.GenerateMonsters(cs, rng);
            Assert.Equal(0, rng.Counter, $"{id} draws nothing from its encounter Rng");
            Assert.Equal(plain.Count, withRng.Count);
            Assert.True(ReferenceEquals(plain[0].Def, withRng[0].Def));

            (ulong Monsters, ulong Player, ulong Rng)? reference = null;
            for (int floor = 1; floor <= 8; floor++)
            {
                var sim = CombatSim.Create(new RunRngSet(77UL), CardSpec.FromIds(StartingDecks.Ironclad), id, 0, null, null, CombatRunContext.ForAct(1, floor));
                var state = sim.State;
                Assert.Equal(1, state.Monsters.Count);
                var hashes = (StateHasher.Hash(state.Monsters[0]), StateHasher.Hash(state.Player), StateHasher.Hash(state.Rng));
                reference ??= hashes;
                Assert.Equal(reference.Value, hashes, $"{id} floor {floor}: composition, HP roll and streams do not depend on the floor");
            }
        }
    }

    public static void Test_WurmBeetle_HpBoundsByAscension()
    {
        var wurm = MonsterRegistry.Get(Wurm);
        var beetle = MonsterRegistry.Get(Beetle);
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL, WurmWeak);
            bool tough = asc >= 8;
            Assert.Equal(tough ? 58 : 55, wurm.MinInitialHp(cs), $"wurm min A{asc}");
            Assert.Equal(tough ? 59 : 57, wurm.MaxInitialHp(cs), $"wurm max A{asc}");
            Assert.Equal(tough ? 40 : 38, beetle.MinInitialHp(cs), $"beetle min A{asc}");
            Assert.Equal(tough ? 42 : 40, beetle.MaxInitialHp(cs), $"beetle max A{asc}");
        }
    }

    public static void Test_WurmBeetle_HpRollMatchesNicheDrawOnMockRng()
    {
        foreach (var (encounterId, monsterId) in new[] { (WurmWeak, Wurm), (BeetleWeak, Beetle) })
        {
            foreach (int asc in new[] { 0, 8 })
            {
                var (min, max) = HpRange(monsterId, asc);
                int count = max - min + 1;
                var seen = new HashSet<int>();
                for (ulong seed = 1; seed <= 200; seed++)
                {
                    var shared = new Rng(seed);
                    var cs = NewCombat(asc, new RunRngSet(shared), encounterId);
                    var monster = SpawnWeak(cs, encounterId);
                    int expected = min + new Rng(seed).NextInt(0, count);
                    string where = $"{encounterId} A{asc} seed {seed}";
                    Assert.Equal(expected, monster.MaxHp, where);
                    Assert.Equal(expected, monster.CurrentHp, where);
                    Assert.Equal<int?>(expected, monster.MaxHpBeforeModification, where);
                    Assert.Equal(1, shared.Counter, $"{where}: the spawn draws exactly once");
                    seen.Add(monster.MaxHp);
                }
                Assert.Equal(count, seen.Count, $"{encounterId} A{asc}: every HP in [{min}, {max}] is reachable");
                Assert.Equal(min, seen.Min());
                Assert.Equal(max, seen.Max());
            }
        }
    }

    public static void Test_WurmBeetle_HpRollUsesNicheStreamOnly()
    {
        foreach (var (encounterId, monsterId) in new[] { (WurmWeak, Wurm), (BeetleWeak, Beetle) })
        {
            const ulong seed = 12345UL;
            var cs = NewCombat(0, new RunRngSet(seed), encounterId);
            var (min, max) = HpRange(monsterId, 0);
            int expected = min + new RunRngSet(seed).Niche.NextInt(0, max - min + 1);
            var monster = SpawnWeak(cs, encounterId);
            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            Assert.Equal(expected, monster.MaxHp, encounterId);
            foreach (var type in Enum.GetValues<RunRngType>())
            {
                Assert.Equal(type == RunRngType.Niche ? 1 : 0, cs.Rng.Get(type).Counter, $"{encounterId} {type}");
            }
        }
    }

    public static void Test_WurmBeetle_MoveValuesByAscension()
    {
        for (int asc = 0; asc <= 10; asc++)
        {
            var cs = NewCombat(asc, 1UL, WurmWeak);
            bool deadly = asc >= 9;
            Assert.Equal(deadly ? 6 : 4, FuzzyWurmCrawler.AcidGoopDamage(cs), $"acid goop A{asc}");
            Assert.Equal(deadly ? 8 : 7, ShrinkerBeetle.ChompDamage(cs), $"chomp A{asc}");
            Assert.Equal(deadly ? 14 : 13, ShrinkerBeetle.StompDamage(cs), $"stomp A{asc}");
        }
        Assert.Equal(7, FuzzyWurmCrawler.InhaleStrengthGain, "Inhale is a flat 7 Strength at every ascension");
        Assert.Equal(-1, ShrinkerBeetle.ShrinkAmount);
        Assert.Equal(30m, ShrinkPower.DamageDecrease);
        Assert.Equal(0.7m, ShrinkPower.DamageMultiplier);
    }

    public static void Test_WurmBeetle_WurmGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL, WurmWeak);
            var monster = SpawnWeak(cs, WurmWeak);
            var machine = monster.Ai.Machine;
            int goopDamage = asc >= 9 ? 6 : 4;
            Assert.Equal(FirstGoop, machine.InitialStateId);
            Assert.SequenceEqual(new[] { FirstGoop, Goop, Inhale }, machine.States.Keys, "registration order");
            foreach (var state in machine.States.Values)
            {
                Assert.True(state is MoveStateDef, $"{state.Id} is a plain move: no random or conditional branch");
            }

            var first = (MoveStateDef)machine.Get(FirstGoop);
            var goop = (MoveStateDef)machine.Get(Goop);
            var inhale = (MoveStateDef)machine.Get(Inhale);
            Assert.True(ReferenceEquals(first, machine.InitialState));
            Assert.Equal(Inhale, first.FollowUpState?.Id);
            Assert.Equal(Goop, inhale.FollowUpState?.Id);
            Assert.Equal(FirstGoop, goop.FollowUpState?.Id);
            foreach (var move in new[] { first, goop, inhale })
            {
                Assert.False(move.MustPerformOnceBeforeTransitioning, move.Id);
                Assert.Null(move.FollowUpStateId, move.Id);
                Assert.Equal(1, move.Intents.Count, move.Id);
            }
            foreach (var attack in new[] { first, goop })
            {
                var intent = (SingleAttackIntent)attack.Intents[0];
                Assert.Equal(IntentType.Attack, intent.IntentType, attack.Id);
                Assert.Equal((decimal)goopDamage, intent.GetBaseDamage(cs, monster), $"{attack.Id} A{asc}");
                Assert.Equal(1, intent.GetRepeats(cs, monster));
                Assert.True(attack.IntendsToAttack);
            }
            Assert.Equal(IntentType.Buff, inhale.Intents[0].IntentType);
            Assert.False(inhale.IntendsToAttack);
        }
    }

    public static void Test_WurmBeetle_BeetleGraphAndIntents()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 1UL, BeetleWeak);
            var monster = SpawnWeak(cs, BeetleWeak);
            var machine = monster.Ai.Machine;
            Assert.Equal(Shrinker, machine.InitialStateId);
            Assert.SequenceEqual(new[] { Shrinker, Chomp, Stomp }, machine.States.Keys, "registration order");
            foreach (var state in machine.States.Values)
            {
                Assert.True(state is MoveStateDef, $"{state.Id} is a plain move: no random or conditional branch");
            }

            var shrinker = (MoveStateDef)machine.Get(Shrinker);
            var chomp = (MoveStateDef)machine.Get(Chomp);
            var stomp = (MoveStateDef)machine.Get(Stomp);
            Assert.Equal(Chomp, shrinker.FollowUpState?.Id);
            Assert.Equal(Stomp, chomp.FollowUpState?.Id);
            Assert.Equal(Chomp, stomp.FollowUpState?.Id);
            foreach (var move in new[] { shrinker, chomp, stomp })
            {
                Assert.False(move.MustPerformOnceBeforeTransitioning, move.Id);
                Assert.Equal(1, move.Intents.Count, move.Id);
            }

            var debuff = (DebuffIntent)shrinker.Intents[0];
            Assert.True(debuff.Strong);
            Assert.Equal(IntentType.DebuffStrong, debuff.IntentType);
            Assert.False(shrinker.IntendsToAttack);

            var chompIntent = (SingleAttackIntent)chomp.Intents[0];
            var stompIntent = (SingleAttackIntent)stomp.Intents[0];
            Assert.Equal(asc >= 9 ? 8m : 7m, chompIntent.GetBaseDamage(cs, monster));
            Assert.Equal(asc >= 9 ? 14m : 13m, stompIntent.GetBaseDamage(cs, monster));
            Assert.Equal(1, chompIntent.GetRepeats(cs, monster));
            Assert.Equal(1, stompIntent.GetRepeats(cs, monster));
        }
    }

    public static void Test_WurmBeetle_WurmCycleOnMockRngDrawsNothing()
    {
        var shared = new Rng(99UL);
        var cs = NewCombat(0, new RunRngSet(shared), WurmWeak);
        var monster = SpawnWeak(cs, WurmWeak);
        var ai = monster.Ai;
        int counterAfterSpawn = shared.Counter;

        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Equal(FirstGoop, ai.NextMove?.Id);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(FirstGoop, ai.NextMove?.Id, "the round-1 re-roll keeps the first move");
        Assert.Equal(1, ai.StateLog.Count);

        var expected = new[] { FirstGoop, Inhale, Goop, FirstGoop, Inhale, Goop, FirstGoop, Inhale, Goop, FirstGoop };
        Assert.SequenceEqual(expected, RunScriptedCycle(cs, monster, expected.Length));
        Assert.SequenceEqual(expected, ai.StateLog);
        Assert.Equal(counterAfterSpawn, shared.Counter, "the wurm AI never draws");
    }

    public static void Test_WurmBeetle_BeetleCycleOnMockRngDrawsNothing()
    {
        var shared = new Rng(101UL);
        var cs = NewCombat(0, new RunRngSet(shared), BeetleWeak);
        var monster = SpawnWeak(cs, BeetleWeak);
        var ai = monster.Ai;
        int counterAfterSpawn = shared.Counter;

        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        Assert.Equal(Shrinker, ai.NextMove?.Id);
        MonsterLifecycle.PrepareForNextTurn(cs, monster);
        Assert.Equal(Shrinker, ai.NextMove?.Id, "the round-1 re-roll keeps the first move");

        var expected = new[] { Shrinker, Chomp, Stomp, Chomp, Stomp, Chomp, Stomp, Chomp, Stomp };
        Assert.SequenceEqual(expected, RunScriptedCycle(cs, monster, expected.Length));
        Assert.SequenceEqual(expected, ai.StateLog);
        Assert.Equal(1, ai.StateLog.Count(id => id == Shrinker), "SHRINKER_MOVE only opens the fight");
        Assert.Equal(counterAfterSpawn, shared.Counter, "the beetle AI never draws");
    }

    public static void Test_WurmBeetle_WurmMovesDealDamageAndStackStrength()
    {
        foreach (int asc in new[] { 0, 8, 9, 10 })
        {
            var cs = NewCombat(asc, 43UL, WurmWeak);
            var monster = SpawnWeak(cs, WurmWeak);
            var player = cs.Player;
            int goop = asc >= 9 ? 6 : 4;
            var acidIntent = (AttackIntent)((MoveStateDef)monster.Ai.Machine.Get(Goop)).Intents[0];
            var targets = MonsterLifecycle.PlayerCreatures(cs);

            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp, player.CurrentHp, "a monster spawned this turn does not act");
            MonsterLifecycle.OnSideSwitch(monster);

            int hp = PlayerHp;
            var expectedMoves = new[] { FirstGoop, Inhale, Goop, FirstGoop, Inhale, Goop, FirstGoop, Inhale };
            var expectedLoss = new[] { goop, 0, goop + 7, goop + 7, 0, goop + 14, goop + 14, 0 };
            var expectedStrength = new[] { 0, 7, 7, 7, 14, 14, 14, 21 };
            for (int turn = 0; turn < expectedMoves.Length; turn++)
            {
                if (turn > 0)
                {
                    MonsterLifecycle.PrepareForNextTurn(cs, monster);
                }
                string where = $"A{asc} turn {turn + 1}";
                Assert.Equal(expectedMoves[turn], monster.Ai.NextMove?.Id, where);
                MonsterLifecycle.TakeTurn(cs, monster);
                hp -= expectedLoss[turn];
                Assert.Equal(hp, player.CurrentHp, $"{where} {expectedMoves[turn]}");
                Assert.Equal(expectedStrength[turn], monster.GetPowerAmount<StrengthPower>(), $"{where} strength");
                Assert.Equal(0, monster.Block, where);
                Assert.Equal(goop + expectedStrength[turn], acidIntent.GetSingleDamage(cs, targets, monster), $"{where} intent preview includes Strength");
            }
            Assert.Equal(1, monster.Powers.Count, "Inhale stacks into one Strength instance");
            var strength = monster.GetPower<StrengthPower>()!;
            Assert.Equal(monster.CombatId, strength.ApplierId, "the wurm applies Strength to itself");
            Assert.Equal(0, player.Powers.Count);
        }
    }

    public static void Test_WurmBeetle_BeetleMovesShrinkThenAttack()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 47UL, BeetleWeak);
            var monster = SpawnWeak(cs, BeetleWeak);
            var player = cs.Player;
            int chomp = asc >= 9 ? 8 : 7;
            int stomp = asc >= 9 ? 14 : 13;

            MonsterLifecycle.AfterCreatureAdded(cs, monster);
            MonsterLifecycle.PrepareForNextTurn(cs, monster);
            MonsterLifecycle.OnSideSwitch(monster);

            Assert.Equal(Shrinker, monster.Ai.NextMove?.Id);
            MonsterLifecycle.TakeTurn(cs, monster);
            Assert.Equal(PlayerHp, player.CurrentHp, "SHRINKER_MOVE deals no damage");
            Assert.Equal(1, player.Powers.Count);
            var shrink = player.GetPower<ShrinkPower>();
            Assert.NotNull(shrink, "SHRINKER_MOVE applies Shrink to the player");
            Assert.Equal(-1, shrink!.Amount);
            Assert.True(shrink.IsInfinite);
            Assert.Equal(PowerStackType.Single, shrink.StackType);
            Assert.Equal(PowerType.Debuff, shrink.Type);
            Assert.Equal(PowerType.Debuff, shrink.TypeForCurrentAmount);
            Assert.Equal(monster.CombatId, shrink.ApplierId);
            Assert.True(shrink.SkipNextDurationTick, "a debuff on the player side is flagged by PowerCmd.Apply");
            Assert.Equal(0, monster.Powers.Count);

            int hp = PlayerHp;
            var expectedMoves = new[] { Chomp, Stomp, Chomp, Stomp, Chomp };
            var expectedLoss = new[] { chomp, stomp, chomp, stomp, chomp };
            for (int turn = 0; turn < expectedMoves.Length; turn++)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
                string where = $"A{asc} turn {turn + 2}";
                Assert.Equal(expectedMoves[turn], monster.Ai.NextMove?.Id, where);
                MonsterLifecycle.TakeTurn(cs, monster);
                hp -= expectedLoss[turn];
                Assert.Equal(hp, player.CurrentHp, $"{where}: the player's Shrink does not reduce the beetle's attacks");
                Assert.Equal(-1, player.GetPowerAmount<ShrinkPower>(), $"{where}: Shrink is applied once");
            }
        }
    }

    public static void Test_WurmBeetle_ShrinkMultipliesOnlyItsOwnersPoweredAttacks()
    {
        var cs = NewCombat(0, 5UL, BeetleWeak);
        var beetle = SpawnWeak(cs, BeetleWeak);
        var player = cs.Player;
        PowerCmd.Apply<ShrinkPower>(cs, MonsterLifecycle.PlayerCreatures(cs), -1m, beetle, null);
        var shrink = player.GetPower<ShrinkPower>()!;

        Assert.Equal(0.7m, shrink.ModifyDamageMultiplicative(cs, beetle, 6m, ValueProp.Move, player, null, null));
        Assert.Equal(1m, shrink.ModifyDamageMultiplicative(cs, beetle, 6m, ValueProp.Move | ValueProp.Unpowered, player, null, null), "unpowered");
        Assert.Equal(1m, shrink.ModifyDamageMultiplicative(cs, beetle, 6m, ValueProp.None, player, null, null), "not a move");
        Assert.Equal(1m, shrink.ModifyDamageMultiplicative(cs, player, 7m, ValueProp.Move, beetle, null, null), "another dealer");
        Assert.Equal(1m, shrink.ModifyDamageMultiplicative(cs, beetle, 6m, ValueProp.Move, null, null, null), "no dealer");

        Assert.Equal(4.2m, Hook.ModifyDamage(cs, beetle, player, 6m, ValueProp.Move, null, null));
        Assert.Equal(5.6m, Hook.ModifyDamage(cs, beetle, player, 8m, ValueProp.Move, null, null));
        Assert.Equal(6m, Hook.ModifyDamage(cs, beetle, player, 6m, ValueProp.Move | ValueProp.Unpowered, null, null));
        Assert.Equal(7m, Hook.ModifyDamage(cs, player, beetle, 7m, ValueProp.Move, null, null), "the beetle's own attack is unchanged");

        PowerCmd.Apply<StrengthPower>(cs, player, 2m, player, null);
        Assert.Equal(5.6m, Hook.ModifyDamage(cs, beetle, player, 6m, ValueProp.Move, null, null), "Strength is additive, before the multiplier");
        PowerCmd.Apply<VulnerablePower>(cs, beetle, 2m, player, null);
        Assert.Equal(8.4m, Hook.ModifyDamage(cs, beetle, player, 6m, ValueProp.Move, null, null), "(6 + 2) x 0.7 x 1.5");
    }

    public static void Test_WurmBeetle_ShrinkReducesCardDamageEndToEnd()
    {
        ulong seed = FindSeedWithTurnTwoHand(BashId, StrikeId);
        var sim = CombatSim.Create(seed, StartingDecks.Ironclad, BeetleWeak);
        var beetle = sim.State.Monsters[0];

        var turnOne = sim.Clone();
        int before = turnOne.State.Monsters[0].CurrentHp;
        turnOne.Step(GameAction.Play(HandIndexOf(turnOne, StrikeId, out _), 0));
        Assert.Equal(before - 6, turnOne.State.Monsters[0].CurrentHp, "turn 1: Strike deals 6 before the beetle shrinks the player");

        sim.Step(GameAction.EndTurn);
        Assert.Equal(-1, sim.State.Player.GetPowerAmount<ShrinkPower>(), "SHRINKER_MOVE resolved on enemy turn 1");
        Assert.Equal(Chomp, beetle.Ai.NextMove?.Id);
        var chompIntent = (AttackIntent)beetle.Ai.NextMove!.Intents[0];
        Assert.Equal(7, chompIntent.GetSingleDamage(sim.State, MonsterLifecycle.PlayerCreatures(sim.State), beetle), "the player's Shrink does not touch the intent");

        var strikeOnly = sim.Clone();
        before = strikeOnly.State.Monsters[0].CurrentHp;
        strikeOnly.Step(GameAction.Play(HandIndexOf(strikeOnly, StrikeId, out _), 0));
        Assert.Equal(before - 4, strikeOnly.State.Monsters[0].CurrentHp, "Strike: (int)(6 x 0.7) = 4");

        before = beetle.CurrentHp;
        sim.Step(GameAction.Play(HandIndexOf(sim, BashId, out _), 0));
        Assert.Equal(before - 5, beetle.CurrentHp, "Bash: (int)(8 x 0.7) = 5");
        Assert.Equal(2, beetle.GetPowerAmount<VulnerablePower>());
        before = beetle.CurrentHp;
        sim.Step(GameAction.Play(HandIndexOf(sim, StrikeId, out _), 0));
        Assert.Equal(before - 6, beetle.CurrentHp, "Strike on a Vulnerable beetle: (int)(6 x 0.7 x 1.5) = 6");
    }

    public static void Test_WurmBeetle_ShrinkIsRemovedOnlyWhenItsApplierDies()
    {
        var cs = NewCombat(0, 11UL, BeetleWeak);
        var beetle = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(Beetle)));
        var wurm = SpawnAndAdd(cs, new MonsterSpawn(MonsterRegistry.Get(Wurm)));
        var player = cs.Player;
        PowerCmd.Apply<ShrinkPower>(cs, MonsterLifecycle.PlayerCreatures(cs), -1m, beetle, null);
        var shrink = player.GetPower<ShrinkPower>()!;

        shrink.AfterDeath(cs, beetle, wasRemovalPrevented: true);
        Assert.True(ReferenceEquals(shrink, player.GetPower<ShrinkPower>()), "a prevented death keeps the power");
        shrink.AfterDeath(cs, player, wasRemovalPrevented: false);
        Assert.True(ReferenceEquals(shrink, player.GetPower<ShrinkPower>()), "only the applier's death counts");

        CreatureCmd.Kill(cs, wurm);
        Assert.True(wurm.IsDead);
        Assert.True(ReferenceEquals(shrink, player.GetPower<ShrinkPower>()), "another enemy's death keeps the power");
        Assert.Equal(-1, shrink.Amount);

        CreatureCmd.Kill(cs, beetle);
        Assert.True(beetle.IsDead);
        Assert.Null(player.GetPower<ShrinkPower>(), "the applier's death removes Shrink from the player");
        Assert.Equal(0, player.Powers.Count);
    }

    public static void Test_WurmBeetle_ShrinkDurationFollowsTurnParticipants()
    {
        var cs = NewCombat(0, 13UL, BeetleWeak);
        var beetle = SpawnWeak(cs, BeetleWeak);
        var player = cs.Player;

        PowerCmd.Apply<ShrinkPower>(cs, player, -1m, beetle, null);
        for (int i = 0; i < 4; i++)
        {
            Hook.AfterSideTurnEnd(cs, CombatSide.Player);
            Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        }
        Assert.Equal(-1, player.GetPowerAmount<ShrinkPower>(), "a negative (infinite) Shrink never decrements");

        PowerCmd.Apply<ShrinkPower>(cs, beetle, 2m, player, null);
        var onBeetle = beetle.GetPower<ShrinkPower>()!;
        Assert.False(onBeetle.IsInfinite);
        Assert.Equal(PowerStackType.Counter, onBeetle.StackType);
        Assert.False(onBeetle.SkipNextDurationTick, "only player-side debuffs are flagged");
        Assert.False(onBeetle.IsTurnParticipant(cs, CombatSide.Player));
        Assert.True(onBeetle.IsTurnParticipant(cs, CombatSide.Enemy));
        Hook.AfterSideTurnEnd(cs, CombatSide.Player);
        Assert.Equal(2, onBeetle.Amount, "the player's turn end does not tick an enemy's Shrink");
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(1, onBeetle.Amount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Null(beetle.GetPower<ShrinkPower>(), "removed at zero");
        Assert.Equal(-1, player.GetPowerAmount<ShrinkPower>());

        var cs2 = NewCombat(0, 17UL, BeetleWeak);
        var beetle2 = SpawnWeak(cs2, BeetleWeak);
        PowerCmd.Apply<ShrinkPower>(cs2, cs2.Player, 2m, beetle2, null);
        var onPlayer = cs2.Player.GetPower<ShrinkPower>()!;
        Assert.True(onPlayer.SkipNextDurationTick);
        Hook.AfterSideTurnEnd(cs2, CombatSide.Enemy);
        Assert.Equal(2, onPlayer.Amount, "the enemy turn end does not tick the player's Shrink");
        Hook.AfterSideTurnEnd(cs2, CombatSide.Player);
        Assert.Equal(1, onPlayer.Amount, "Decrement ignores SkipNextDurationTick");
        Assert.False(onPlayer.IsTurnParticipant(cs2, CombatSide.None));
    }

    public static void Test_WurmBeetle_ShrinkOnAMonsterReducesItsIntent()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var cs = NewCombat(asc, 19UL, BeetleWeak);
            var beetle = SpawnWeak(cs, BeetleWeak);
            var targets = MonsterLifecycle.PlayerCreatures(cs);
            var chomp = (AttackIntent)((MoveStateDef)beetle.Ai.Machine.Get(Chomp)).Intents[0];
            var stomp = (AttackIntent)((MoveStateDef)beetle.Ai.Machine.Get(Stomp)).Intents[0];
            Assert.Equal(asc >= 9 ? 8 : 7, chomp.GetSingleDamage(cs, targets, beetle));
            PowerCmd.Apply<ShrinkPower>(cs, beetle, 3m, cs.Player, null);
            Assert.Equal(asc >= 9 ? 5 : 4, chomp.GetSingleDamage(cs, targets, beetle), "(int)(chomp x 0.7)");
            Assert.Equal(9, stomp.GetSingleDamage(cs, targets, beetle), "(int)(13 x 0.7) = (int)(14 x 0.7) = 9");
        }
    }

    public static void Test_WurmBeetle_ShrinkTypeAndStackTypeFollowTheSign()
    {
        var power = new ShrinkPower();
        Assert.Equal(PowerType.Debuff, power.Type);
        Assert.True(power.AllowNegative);
        Assert.Equal(PowerInstanceType.None, power.InstanceType);
        Assert.False(power.IsInfinite);
        Assert.Equal(PowerStackType.Counter, power.StackType);
        Assert.Equal(PowerType.Debuff, power.GetTypeForAmount(-1m), "a negative amount on a counter power that allows negatives is a debuff");
        Assert.Equal(PowerType.Debuff, power.GetTypeForAmount(4m));

        power.SetAmount(-1);
        Assert.True(power.IsInfinite);
        Assert.Equal(PowerStackType.Single, power.StackType);
        Assert.Equal(PowerType.Debuff, power.GetTypeForAmount(-1m));
        Assert.False(power.ShouldRemoveDueToAmount());

        power.SetAmount(3);
        Assert.False(power.IsInfinite);
        Assert.Equal(PowerStackType.Counter, power.StackType);
        Assert.False(power.ShouldRemoveDueToAmount());

        power.SetAmount(0);
        Assert.True(power.ShouldRemoveDueToAmount());
    }

    public static void Test_WurmBeetle_SecondShrinkStacksIntoOneInstance()
    {
        var cs = NewCombat(0, 23UL, BeetleWeak);
        var beetle = SpawnWeak(cs, BeetleWeak);
        var player = cs.Player;
        var first = PowerCmd.Apply<ShrinkPower>(cs, player, -1m, beetle, null);
        var second = PowerCmd.Apply<ShrinkPower>(cs, player, -1m, beetle, null);
        Assert.True(ReferenceEquals(first, second), "a non-instanced power stacks through ModifyAmount");
        Assert.Equal(1, player.Powers.Count);
        Assert.Equal(-2, player.GetPowerAmount<ShrinkPower>());
        Assert.Equal(7m, Hook.ModifyDamage(cs, beetle, player, 10m, ValueProp.Move, null, null), "the multiplier does not depend on the amount");
    }

    public static void Test_WurmBeetle_CombatSimStartsBothEncounters()
    {
        foreach (var (encounterId, monsterId, opener) in new[] { (WurmWeak, Wurm, FirstGoop), (BeetleWeak, Beetle, Shrinker) })
        {
            foreach (int asc in new[] { 0, 8, 10 })
            {
                var sim = CombatSim.Create(1UL, StartingDecks.Ironclad, encounterId, asc);
                var state = sim.State;
                Assert.Equal(1, state.Monsters.Count);
                var monster = state.Monsters[0];
                var (min, max) = HpRange(monsterId, asc);
                Assert.Equal(monsterId, monster.MonsterId);
                Assert.True(monster.MaxHp >= min && monster.MaxHp <= max, $"{encounterId} A{asc} hp {monster.MaxHp}");
                Assert.Equal(monster.MaxHp, monster.CurrentHp);
                Assert.Equal(opener, monster.Ai.NextMove?.Id);
                Assert.Equal(1, state.Rng.Niche.Counter, "one HP roll");
                Assert.Equal(0, state.Rng.MonsterAi.Counter, "the opener is fixed");
                Assert.Equal(PlayerTurnPhase.Play, state.Player.Phase);
            }
        }
    }

    public static void Test_WurmBeetle_CombatSimWurmCycleAndDamage()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var sim = CombatSim.Create(2UL, StartingDecks.Ironclad, WurmWeak, asc);
            int goop = asc >= 9 ? 6 : 4;
            var monster = sim.State.Monsters[0];
            var expectedMoves = new[] { FirstGoop, Inhale, Goop, FirstGoop, Inhale, Goop };
            var expectedLoss = new[] { goop, 0, goop + 7, goop + 7, 0, goop + 14 };
            var expectedPreview = new[] { goop, -1, goop + 7, goop + 7, -1, goop + 14 };
            int hp = sim.State.Player.CurrentHp;
            for (int turn = 0; turn < expectedMoves.Length; turn++)
            {
                string where = $"A{asc} turn {turn + 1}";
                var move = monster.Ai.NextMove!;
                Assert.Equal(expectedMoves[turn], move.Id, where);
                if (expectedPreview[turn] >= 0)
                {
                    var intent = (AttackIntent)move.Intents[0];
                    Assert.Equal(expectedPreview[turn], intent.GetSingleDamage(sim.State, MonsterLifecycle.PlayerCreatures(sim.State), monster), $"{where} intent label");
                }
                sim.Step(GameAction.EndTurn);
                hp -= expectedLoss[turn];
                Assert.Equal(hp, sim.State.Player.CurrentHp, where);
            }
            Assert.Equal(14, monster.GetPowerAmount<StrengthPower>(), "two Inhales");
            Assert.Equal(0, sim.State.Rng.MonsterAi.Counter, "no MonsterAi draw in six turns");
            Assert.False(sim.IsTerminal);
        }
    }

    public static void Test_WurmBeetle_CombatSimBeetleCycleAndShrinkPersists()
    {
        foreach (int asc in new[] { 0, 9 })
        {
            var sim = CombatSim.Create(3UL, StartingDecks.Ironclad, BeetleWeak, asc);
            int chomp = asc >= 9 ? 8 : 7;
            int stomp = asc >= 9 ? 14 : 13;
            var monster = sim.State.Monsters[0];
            var expectedMoves = new[] { Shrinker, Chomp, Stomp, Chomp, Stomp };
            var expectedLoss = new[] { 0, chomp, stomp, chomp, stomp };
            int hp = sim.State.Player.CurrentHp;
            Assert.Null(sim.State.Player.GetPower<ShrinkPower>());
            for (int turn = 0; turn < expectedMoves.Length; turn++)
            {
                string where = $"A{asc} turn {turn + 1}";
                Assert.Equal(expectedMoves[turn], monster.Ai.NextMove?.Id, where);
                sim.Step(GameAction.EndTurn);
                hp -= expectedLoss[turn];
                Assert.Equal(hp, sim.State.Player.CurrentHp, where);
                Assert.Equal(1, sim.State.Player.Powers.Count, where);
                Assert.Equal(-1, sim.State.Player.GetPowerAmount<ShrinkPower>(), $"{where}: Shrink stays at -1 across turn ends");
            }
            Assert.Equal(0, sim.State.Rng.MonsterAi.Counter, "no MonsterAi draw in five turns");
        }
    }

    public static void Test_WurmBeetle_KillingTheBeetleEndsShrinkEndToEnd()
    {
        var sim = CombatSim.Create(1UL, StartingDecks.Ironclad, BeetleWeak);
        bool sawShrink = false;
        for (int step = 0; step < 300 && !sim.IsTerminal; step++)
        {
            sawShrink |= sim.State.Player.GetPower<ShrinkPower>() != null;
            sim.Step(GreedyAttack(sim));
        }
        Assert.True(sim.IsTerminal, "the greedy policy finishes the fight");
        Assert.Equal(CombatOutcome.Victory, sim.Outcome);
        Assert.True(sawShrink);
        Assert.True(sim.State.Player.IsAlive);
        Assert.Null(sim.State.Player.GetPower<ShrinkPower>(), "the beetle's death removed Shrink");
    }

    public static void Test_WurmBeetle_TwoMonsterSpawnDrawsNicheOncePerMonsterInCreationOrder()
    {
        var orders = new[] { new[] { Beetle, Wurm }, new[] { Wurm, Beetle } };
        foreach (int asc in new[] { 0, 8 })
        {
            foreach (var order in orders)
            {
                for (ulong seed = 1; seed <= 60; seed++)
                {
                    var shared = new Rng(seed);
                    var cs = NewCombat(asc, new RunRngSet(shared), BeetleWeak);
                    var spawned = new List<MonsterState>();
                    MonsterSpawner.SpawnEncounter(cs, new PairEncounter(order), spawned);
                    Assert.Equal(2, shared.Counter, "one Niche draw per monster");

                    var probe = new Rng(seed);
                    for (int i = 0; i < order.Length; i++)
                    {
                        var (min, max) = HpRange(order[i], asc);
                        int expected = min + probe.NextInt(0, max - min + 1);
                        string where = $"A{asc} {string.Join("+", order)} seed {seed} #{i}";
                        Assert.Equal(order[i], cs.Monsters[i].MonsterId, where);
                        Assert.Equal(i + 1, cs.Monsters[i].CombatId, where);
                        Assert.Equal(expected, cs.Monsters[i].MaxHp, where);
                        Assert.True(ReferenceEquals(spawned[i], cs.Monsters[i]));
                    }

                    foreach (var m in cs.Monsters)
                    {
                        MonsterLifecycle.SetUpForCombat(cs, m);
                    }
                    foreach (var m in cs.Monsters.ToArray())
                    {
                        MonsterLifecycle.AfterCreatureAdded(cs, m);
                    }
                    Assert.Equal(2, shared.Counter, "the first RollMove of both monsters draws nothing");
                    Assert.Equal(order[0] == Beetle ? Shrinker : FirstGoop, cs.Monsters[0].Ai.NextMove?.Id);
                    Assert.Equal(order[1] == Beetle ? Shrinker : FirstGoop, cs.Monsters[1].Ai.NextMove?.Id);
                }
            }
        }
    }

    public static void Test_WurmBeetle_DuplicateMonstersRollDistinctHp()
    {
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var shared = new Rng(seed);
            var cs = NewCombat(0, new RunRngSet(shared), BeetleWeak);
            MonsterSpawner.SpawnEncounter(cs, new PairEncounter(Beetle, Beetle));
            var probe = new Rng(seed);
            int first = 38 + probe.NextInt(0, 3);
            var rest = new List<int>();
            for (int v = 38; v <= 40; v++)
            {
                if (v != first)
                {
                    rest.Add(v);
                }
            }
            int second = rest[probe.NextInt(0, rest.Count)];
            Assert.Equal(first, cs.Monsters[0].MaxHp, $"seed {seed}");
            Assert.Equal(second, cs.Monsters[1].MaxHp, $"seed {seed}");
            Assert.Equal(2, shared.Counter);
        }
    }

    public static void Test_WurmBeetle_CloneAndHashInvariants()
    {
        foreach (var id in new[] { WurmWeak, BeetleWeak })
        {
            var sim = CombatSim.Create(5UL, StartingDecks.Ironclad, id);
            sim.Step(GameAction.EndTurn);
            sim.Step(GameAction.EndTurn);
            var clone = sim.Clone();
            ulong hash = sim.StateHash();
            Assert.Equal(hash, clone.StateHash(), id);
            Assert.Equal(StateHasher.Hash(sim.State), StateHasher.Hash(sim.State.DeepClone()), id);

            if (id == BeetleWeak)
            {
                var original = sim.State.Player.GetPower<ShrinkPower>()!;
                var copied = clone.State.Player.GetPower<ShrinkPower>()!;
                Assert.False(ReferenceEquals(original, copied));
                Assert.True(ReferenceEquals(clone.State.Player, copied.Owner));
                Assert.Equal(original.Amount, copied.Amount);
                Assert.Equal(original.ApplierId, copied.ApplierId);
                Assert.Equal(original.SkipNextDurationTick, copied.SkipNextDurationTick);
                PowerCmd.Remove(clone.State, copied);
                Assert.True(ReferenceEquals(original, sim.State.Player.GetPower<ShrinkPower>()), "the source keeps its power");
                Assert.NotEqual(hash, clone.StateHash());
                Assert.Equal(hash, sim.StateHash());
                clone = sim.Clone();
            }

            clone.Step(GameAction.EndTurn);
            Assert.Equal(hash, sim.StateHash(), $"{id}: stepping the clone leaves the source alone");
            sim.Step(GameAction.EndTurn);
            Assert.Equal(sim.StateHash(), clone.StateHash(), $"{id}: equal histories hash equal");

            var a = CombatSim.Create(9UL, StartingDecks.Ironclad, id, 10);
            var b = CombatSim.Create(9UL, StartingDecks.Ironclad, id, 10);
            for (int i = 0; i < 4 && !a.IsTerminal; i++)
            {
                Assert.Equal(a.StateHash(), b.StateHash(), $"{id} step {i}");
                var action = GreedyAttack(a);
                a.Step(action);
                b.Step(action);
            }
            Assert.Equal(a.StateHash(), b.StateHash());
        }
    }

    private static List<string> RunScriptedCycle(CombatState cs, MonsterState monster, int turns)
    {
        var ai = monster.Ai;
        var seen = new List<string>(turns);
        for (int turn = 0; turn < turns; turn++)
        {
            if (turn > 0)
            {
                MonsterLifecycle.PrepareForNextTurn(cs, monster);
            }
            var move = ai.NextMove!;
            seen.Add(move.Id);
            ai.MarkPerformedAtLeastOnce(move);
            ai.OnMovePerformed(move);
        }
        return seen;
    }

    private static (int Min, int Max) HpRange(string monsterId, int ascension)
    {
        bool tough = ascension >= 8;
        return monsterId switch
        {
            Wurm => tough ? (58, 59) : (55, 57),
            Beetle => tough ? (40, 42) : (38, 40),
            _ => throw new ArgumentOutOfRangeException(nameof(monsterId), monsterId, null)
        };
    }

    private static ulong FindSeedWithTurnTwoHand(params string[] cardIds)
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var sim = CombatSim.Create(seed, StartingDecks.Ironclad, BeetleWeak);
            if (!HandHasAll(sim, StrikeId))
            {
                continue;
            }
            sim.Step(GameAction.EndTurn);
            if (HandHasAll(sim, cardIds))
            {
                return seed;
            }
        }
        Assert.Fail("no seed in 1..500 deals the requested opening hands");
        return 0;
    }

    private static bool HandHasAll(CombatSim sim, params string[] cardIds)
    {
        foreach (var id in cardIds)
        {
            HandIndexOf(sim, id, out bool found);
            if (!found)
            {
                return false;
            }
        }
        return true;
    }

    private static int HandIndexOf(CombatSim sim, string cardId, out bool found)
    {
        var hand = sim.State.Player.Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            if (hand[i].Id == cardId)
            {
                found = true;
                return i;
            }
        }
        found = false;
        return -1;
    }

    private static GameAction GreedyAttack(CombatSim sim)
    {
        var legal = sim.LegalActions();
        var hand = sim.State.Player.Hand.Cards;
        foreach (var preferred in new[] { BashId, StrikeId, DefendId })
        {
            foreach (var action in legal)
            {
                if (action.Kind == ActionKind.PlayCard && hand[action.CardIndex].Id == preferred)
                {
                    return action;
                }
            }
        }
        return GameAction.EndTurn;
    }

    private static CombatState NewCombat(int ascension, ulong seed, string encounterId) => NewCombat(ascension, new RunRngSet(new Rng(seed)), encounterId);

    private static CombatState NewCombat(int ascension, RunRngSet rng, string encounterId)
    {
        var player = new PlayerState(0, StartingDecks.IroncladCharacterId, PlayerHp, PlayerHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, encounterId, ascension, rng);
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

    private static MonsterState SpawnWeak(CombatState cs, string encounterId)
    {
        var spawned = new List<MonsterState>(1);
        MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(encounterId), spawned);
        Assert.Equal(1, spawned.Count);
        MonsterLifecycle.SetUpForCombat(cs, spawned[0]);
        return spawned[0];
    }

    private sealed class PairEncounter : EncounterDef
    {
        private readonly string[] _monsterIds;

        public PairEncounter(params string[] monsterIds)
            : base("WURM_BEETLE_TESTS_PAIR")
        {
            _monsterIds = monsterIds;
        }

        public override IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
        {
            var spawns = new MonsterSpawn[_monsterIds.Length];
            for (int i = 0; i < spawns.Length; i++)
            {
                spawns[i] = new MonsterSpawn(MonsterRegistry.Get(_monsterIds[i]));
            }
            return spawns;
        }
    }
}
