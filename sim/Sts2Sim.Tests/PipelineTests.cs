using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.State;

namespace Sts2Sim.Tests;

public static class PipelineTests
{
    private sealed class MonsterDouble : MonsterDef
    {
        public MonsterDouble()
            : base("PIPELINE_TEST_MONSTER")
        {
        }

        public override int MinInitialHp(CombatState cs) => 50;

        public override int MaxInitialHp(CombatState cs) => 50;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self) => throw new NotSupportedException();
    }

    private sealed class AttackDouble : CardDef
    {
        public AttackDouble(bool costsX = false)
            : base("PIPELINE_TEST_ATTACK", 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy, costsX)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class SkillDouble : CardDef
    {
        public SkillDouble()
            : base("PIPELINE_TEST_SKILL", 1, CardType.Skill, CardRarity.Basic, TargetType.Self)
        {
        }

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class RecordingPower : PowerModel
    {
        private readonly List<string> _log;
        private readonly string _tag;

        public RecordingPower(List<string> log, string tag)
        {
            _log = log;
            _tag = tag;
        }

        public override string Id => "PIPELINE_RECORDING_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override void AfterSideTurnStart(CombatState cs, CombatSide side) => _log.Add(_tag + ":start");

        public override void AfterSideTurnStartLate(CombatState cs, CombatSide side) => _log.Add(_tag + ":late");

        public override void AfterDamageReceived(CombatState cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner))
            {
                _log.Add(_tag + ":hit" + result.UnblockedDamage);
            }
        }

        public override void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented) => _log.Add(_tag + ":death" + (wasRemovalPrevented ? "-prevented" : string.Empty));

        public override void AfterRemoved(CombatState cs, CreatureState oldOwner) => _log.Add(_tag + ":removed");
    }

    private sealed class FlatBonusPower : PowerModel
    {
        private readonly decimal _bonus;
        private readonly List<string> _log;

        public FlatBonusPower(decimal bonus, List<string> log)
        {
            _bonus = bonus;
            _log = log;
        }

        public override string Id => "PIPELINE_FLAT_BONUS_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override decimal ModifyDamageAdditive(CombatState cs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
            => ReferenceEquals(dealer, Owner) ? _bonus : 0m;

        public override void AfterModifyingDamageAmount(CombatState cs, CardInstance? cardSource) => _log.Add("after-modifying:" + _bonus);
    }

    private sealed class CapPower : PowerModel
    {
        private readonly decimal _cap;

        public CapPower(decimal cap)
        {
            _cap = cap;
        }

        public override string Id => "PIPELINE_CAP_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override decimal ModifyDamageCap(CombatState cs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
            => ReferenceEquals(target, Owner) ? _cap : decimal.MaxValue;
    }

    private sealed class PreventDeathOncePower : PowerModel
    {
        private bool _used;

        public override string Id => "PIPELINE_PREVENT_DEATH_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldDie(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner) || _used;

        public override void AfterPreventingDeath(CombatState cs, CreatureState creature)
        {
            _used = true;
            CreatureCmd.Heal(cs, creature, 1m);
        }
    }

    private sealed class MinionPower : PowerModel
    {
        public override string Id => "PIPELINE_MINION_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool OwnerIsSecondaryEnemy => true;
    }

    private static CombatState NewCombat(int monsters = 1, int monsterHp = 50, int playerHp = 80)
    {
        var player = new PlayerState(0, "IRONCLAD", playerHp, playerHp, 3);
        var cs = new CombatState(player, "PIPELINE_TEST", 0, new RunRngSet(new Rng(12345UL)));
        cs.IsInProgress = true;
        var def = new MonsterDouble();
        for (int i = 0; i < monsters; i++)
        {
            cs.Monsters.Add(new MonsterState(i + 1, def, monsterHp, monsterHp, null, null));
        }
        return cs;
    }

    private static CardInstance NewAttackCard(int instanceId = 100) => new(new AttackDouble(), instanceId);

    private static CardInstance NewSkillCard(int instanceId = 200) => new(new SkillDouble(), instanceId);

    private static AttackCommand PlayerAttack(CombatState cs, CreatureState target, decimal damage, int hits = 1)
    {
        var card = NewAttackCard();
        var play = new CardPlay(card, target);
        return DamageCmd.Attack(cs, damage).FromCard(card, play).Targeting(target).WithHitCount(hits).Execute();
    }

    private static AttackCommand MonsterAttack(CombatState cs, MonsterState monster, decimal damage, int hits = 1)
    {
        return DamageCmd.Attack(cs, damage).FromMonster(monster).WithHitCount(hits).Execute();
    }

    private static T Give<T>(CombatState cs, CreatureState target, decimal amount, CreatureState? applier = null) where T : PowerModel, new()
    {
        return Assert.NotNull(PowerCmd.Apply<T>(cs, target, amount, applier, null));
    }

    private static void Attach(CreatureState owner, PowerModel power, int amount = 1)
    {
        power.ApplyInternal(owner, amount);
    }

    private static byte[] Canonical(ICanonical value)
    {
        var w = new CanonicalWriter(256);
        value.WriteCanonical(w);
        return w.ToArray();
    }

    public static void Test_Pipeline_ValuePropPoweredPredicates()
    {
        Assert.True(ValueProp.Move.IsPoweredAttack());
        Assert.False((ValueProp.Move | ValueProp.Unpowered).IsPoweredAttack());
        Assert.False(ValueProp.Unpowered.IsPoweredAttack());
        Assert.False((ValueProp.Unblockable | ValueProp.Unpowered).IsPoweredAttack());
        Assert.True(ValueProp.Move.IsPoweredCardOrMonsterMoveBlock());
        Assert.False((ValueProp.Move | ValueProp.Unpowered).IsPoweredCardOrMonsterMoveBlock());
        Assert.True((ValueProp.Move | ValueProp.Unpowered).IsCardOrMonsterMove());
        Assert.False(ValueProp.None.IsCardOrMonsterMove());
    }

    public static void Test_Pipeline_PowerIdsAndConstants()
    {
        Assert.Equal("VULNERABLE_POWER", new VulnerablePower().Id);
        Assert.Equal("WEAK_POWER", new WeakPower().Id);
        Assert.Equal("STRENGTH_POWER", new StrengthPower().Id);
        Assert.Equal("DEXTERITY_POWER", new DexterityPower().Id);
        Assert.Equal("FRAIL_POWER", new FrailPower().Id);
        Assert.Equal(1.5m, VulnerablePower.DamageIncrease);
        Assert.Equal(0.75m, WeakPower.DamageDecrease);
        Assert.Equal(0.75m, FrailPower.BlockMultiplier);
        Assert.Equal(PowerType.Debuff, new VulnerablePower().Type);
        Assert.Equal(PowerType.Debuff, new WeakPower().Type);
        Assert.Equal(PowerType.Debuff, new FrailPower().Type);
        Assert.Equal(PowerType.Buff, new StrengthPower().Type);
        Assert.Equal(PowerType.Buff, new DexterityPower().Type);
        Assert.True(new StrengthPower().AllowNegative);
        Assert.True(new DexterityPower().AllowNegative);
        Assert.False(new VulnerablePower().AllowNegative);
        Assert.Equal(PowerStackType.Counter, new FrailPower().StackType);
        Assert.Equal(PowerInstanceType.None, new WeakPower().InstanceType);
    }

    public static void Test_Pipeline_PlainAttackDamage()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var cmd = PlayerAttack(cs, m, 6m);
        Assert.Equal(44, m.CurrentHp);
        Assert.Equal(1, cmd.Results.Count);
        Assert.Equal(6, cmd.Results[0].UnblockedDamage);
        Assert.Equal(0, cmd.Results[0].BlockedDamage);
        Assert.False(cmd.Results[0].WasTargetKilled);
        Assert.Equal(1, cmd.HitsCompleted);
    }

    public static void Test_Pipeline_StrengthAddsBeforeVulnerable()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<StrengthPower>(cs, cs.Player, 2m, cs.Player);
        Give<VulnerablePower>(cs, m, 2m, cs.Player);
        PlayerAttack(cs, m, 6m);
        Assert.Equal(50 - 12, m.CurrentHp);
    }

    public static void Test_Pipeline_WeakTruncatesHpLoss()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<WeakPower>(cs, cs.Player, 1m, m);
        PlayerAttack(cs, m, 6m);
        Assert.Equal(50 - 4, m.CurrentHp);
    }

    public static void Test_Pipeline_WeakAndVulnerableCombine()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<WeakPower>(cs, cs.Player, 1m, m);
        Give<VulnerablePower>(cs, m, 1m, cs.Player);
        Assert.Equal(6.75m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null));
        PlayerAttack(cs, m, 6m);
        Assert.Equal(50 - 6, m.CurrentHp);
    }

    public static void Test_Pipeline_StrengthWeakVulnerableOrder()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<StrengthPower>(cs, cs.Player, 3m, cs.Player);
        Give<WeakPower>(cs, cs.Player, 1m, m);
        Give<VulnerablePower>(cs, m, 1m, cs.Player);
        Assert.Equal(10.125m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null));
        PlayerAttack(cs, m, 6m);
        Assert.Equal(50 - 10, m.CurrentHp);
    }

    public static void Test_Pipeline_NegativeStrengthFloorsAtZero()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var str = Give<StrengthPower>(cs, cs.Player, -10m, cs.Player);
        Assert.Equal(-10, str.Amount);
        Assert.Equal(0m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null));
        var cmd = PlayerAttack(cs, m, 6m);
        Assert.Equal(50, m.CurrentHp);
        Assert.Equal(0, cmd.Results[0].UnblockedDamage);
        Assert.False(cmd.Results[0].WasFullyBlocked);
    }

    public static void Test_Pipeline_UnpoweredAttackIgnoresModifiers()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<StrengthPower>(cs, cs.Player, 4m, cs.Player);
        Give<WeakPower>(cs, cs.Player, 1m, m);
        Give<VulnerablePower>(cs, m, 1m, cs.Player);
        var card = NewAttackCard();
        DamageCmd.Attack(cs, 6m).FromCard(card, new CardPlay(card, m)).Targeting(m).Unpowered().Execute();
        Assert.Equal(44, m.CurrentHp);
    }

    public static void Test_Pipeline_ModifierOwnershipChecks()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<VulnerablePower>(cs, cs.Player, 1m, m);
        Give<WeakPower>(cs, m, 1m, cs.Player);
        Give<StrengthPower>(cs, m, 5m, m);
        Assert.Equal(6m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null));
        Assert.Equal((10m + 5m) * 0.75m * 1.5m, Hook.ModifyDamage(cs, cs.Player, m, 10m, ValueProp.Move, null, null));
    }

    public static void Test_Pipeline_MonsterAttackIntoBlock()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        cs.Player.Block = 4;
        var cmd = MonsterAttack(cs, m, 10m);
        Assert.Equal(80 - 6, cs.Player.CurrentHp);
        Assert.Equal(0, cs.Player.Block);
        var r = cmd.Results[0];
        Assert.Equal(4, r.BlockedDamage);
        Assert.Equal(6, r.UnblockedDamage);
        Assert.True(r.WasBlockBroken);
        Assert.False(r.WasFullyBlocked);
        Assert.Equal(CombatSide.Player, cmd.TargetSide);
    }

    public static void Test_Pipeline_FractionalDamageAgainstBlock()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<WeakPower>(cs, cs.Player, 2m, m);
        m.Block = 10;
        var r = PlayerAttack(cs, m, 7m).Results[0];
        Assert.Equal(5, m.Block);
        Assert.Equal(50, m.CurrentHp);
        Assert.Equal(5, r.BlockedDamage);
        Assert.Equal(0, r.UnblockedDamage);
        Assert.True(r.WasFullyBlocked);
        Assert.False(r.WasBlockBroken);

        m.Block = 5;
        r = PlayerAttack(cs, m, 7m).Results[0];
        Assert.Equal(0, m.Block);
        Assert.Equal(50, m.CurrentHp);
        Assert.Equal(5, r.BlockedDamage);
        Assert.True(r.WasBlockBroken);
        Assert.True(r.WasFullyBlocked);

        m.Block = 3;
        r = PlayerAttack(cs, m, 7m).Results[0];
        Assert.Equal(0, m.Block);
        Assert.Equal(48, m.CurrentHp);
        Assert.Equal(3, r.BlockedDamage);
        Assert.Equal(2, r.UnblockedDamage);
    }

    public static void Test_Pipeline_UnblockableBypassesBlock()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<VulnerablePower>(cs, m, 1m, cs.Player);
        m.Block = 10;
        var results = CreatureCmd.Damage(cs, m, 5m, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
        Assert.Equal(45, m.CurrentHp);
        Assert.Equal(10, m.Block);
        Assert.Equal(0, results[0].BlockedDamage);
        Assert.False(results[0].WasFullyBlocked);
    }

    public static void Test_Pipeline_LethalHitKillsRemovesAndStripsPowers()
    {
        var cs = NewCombat(2, 5);
        var a = cs.Monsters[0];
        var b = cs.Monsters[1];
        var log = new List<string>();
        Give<VulnerablePower>(cs, a, 2m, cs.Player);
        Attach(a, new RecordingPower(log, "A"));
        var r = PlayerAttack(cs, a, 9m).Results[0];
        Assert.True(r.WasTargetKilled);
        Assert.Equal(5, r.UnblockedDamage);
        Assert.Equal(13 - 5, r.OverkillDamage);
        Assert.Equal(0, a.CurrentHp);
        Assert.Equal(1, cs.Monsters.Count);
        Assert.True(ReferenceEquals(b, cs.Monsters[0]));
        Assert.Equal(0, a.Powers.Count);
        Assert.SequenceEqual(new[] { "A:death", "A:removed" }, log);
        Assert.False(CombatStatus.IsEnding(cs));
        PlayerAttack(cs, b, 5m);
        Assert.Equal(0, cs.Monsters.Count);
        Assert.True(CombatStatus.IsEnding(cs));
        Assert.True(CombatStatus.IsOverOrEnding(cs));
        Assert.Equal(CombatOutcome.InProgress, cs.Outcome);
    }

    public static void Test_Pipeline_MultiHitConsumesBlockThenHp()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        m.Block = 3;
        var cmd = PlayerAttack(cs, m, 2m, 3);
        Assert.Equal(3, cmd.HitsCompleted);
        Assert.Equal(3, cmd.HitResults.Count);
        Assert.Equal(0, m.Block);
        Assert.Equal(47, m.CurrentHp);
        Assert.SequenceEqual(new[] { 2, 1, 0 }, cmd.Results.Select(r => r.BlockedDamage));
        Assert.SequenceEqual(new[] { 0, 1, 2 }, cmd.Results.Select(r => r.UnblockedDamage));
    }

    public static void Test_Pipeline_MultiHitStopsWhenTargetDies()
    {
        var cs = NewCombat(2, 5);
        var a = cs.Monsters[0];
        var cmd = PlayerAttack(cs, a, 3m, 4);
        Assert.Equal(2, cmd.HitsCompleted);
        Assert.Equal(0, a.CurrentHp);
        Assert.Equal(1, cs.Monsters.Count);
    }

    public static void Test_Pipeline_AllOpponentsAttackHitsEachLivingEnemy()
    {
        var cs = NewCombat(3);
        var a = cs.Monsters[0];
        var b = cs.Monsters[1];
        var c = cs.Monsters[2];
        Give<VulnerablePower>(cs, a, 1m, cs.Player);
        c.CurrentHp = 0;
        var card = NewAttackCard();
        var cmd = DamageCmd.Attack(cs, 6m).FromCard(card, new CardPlay(card, null)).TargetingAllOpponents().Execute();
        Assert.Equal(50 - 9, a.CurrentHp);
        Assert.Equal(50 - 6, b.CurrentHp);
        Assert.Equal(0, c.CurrentHp);
        Assert.Equal(2, cmd.Results.Count);
        Assert.Equal(1, cmd.HitResults.Count);
        Assert.Equal(CombatSide.Enemy, cmd.TargetSide);
    }

    public static void Test_Pipeline_RandomTargetingIsDeterministic()
    {
        var first = NewCombat(2);
        var second = NewCombat(2);
        foreach (var cs in new[] { first, second })
        {
            var card = NewAttackCard();
            DamageCmd.Attack(cs, 3m).FromCard(card, null).TargetingRandomOpponents().WithHitCount(4).Execute();
            Assert.Equal(12, 100 - cs.Monsters[0].CurrentHp - cs.Monsters[1].CurrentHp);
            Assert.Equal(4, cs.Rng.CombatTargets.Counter);
        }
        Assert.Equal(first.Monsters[0].CurrentHp, second.Monsters[0].CurrentHp);
    }

    public static void Test_Pipeline_RandomTargetingWithoutDuplicates()
    {
        var cs = NewCombat(2);
        var card = NewAttackCard();
        DamageCmd.Attack(cs, 3m).FromCard(card, null).TargetingRandomOpponents(false).WithHitCount(2).Execute();
        Assert.Equal(47, cs.Monsters[0].CurrentHp);
        Assert.Equal(47, cs.Monsters[1].CurrentHp);
        var again = NewCombat(2);
        Assert.Throws<InvalidOperationException>(() => DamageCmd.Attack(again, 3m).FromCard(card, null).TargetingRandomOpponents(false).WithHitCount(3).Execute());
    }

    public static void Test_Pipeline_AttackFromDeadAttackerOrEndedCombatDoesNothing()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        m.CurrentHp = 0;
        var cmd = MonsterAttack(cs, m, 10m);
        Assert.Equal(80, cs.Player.CurrentHp);
        Assert.Equal(0, cmd.HitsCompleted);

        var over = NewCombat();
        over.Outcome = CombatOutcome.Victory;
        over.IsInProgress = false;
        PlayerAttack(over, over.Monsters[0], 10m);
        Assert.Equal(50, over.Monsters[0].CurrentHp);
    }

    public static void Test_Pipeline_DamageFromDeadDealerReturnsEmptyResults()
    {
        var cs = NewCombat(2);
        var a = cs.Monsters[0];
        a.CurrentHp = 0;
        var results = CreatureCmd.Damage(cs, cs.Player, 10m, ValueProp.Move, a, null, null);
        Assert.Equal(1, results.Count);
        Assert.Equal(0, results[0].TotalDamage);
        Assert.Equal(80, cs.Player.CurrentHp);
    }

    public static void Test_Pipeline_DamageCapUsesSmallestCap()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Attach(m, new CapPower(20m));
        Attach(m, new CapPower(8m));
        Attach(m, new CapPower(12m));
        Give<StrengthPower>(cs, cs.Player, 10m, cs.Player);
        Assert.Equal(8m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null));
        Assert.Equal(16m, Hook.ModifyDamage(cs, m, cs.Player, 6m, ValueProp.Move, null, null, ModifyDamageHookType.Additive));
        PlayerAttack(cs, m, 6m);
        Assert.Equal(42, m.CurrentHp);
    }

    public static void Test_Pipeline_AfterModifyingDamageOnlyCallsModifiers()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var log = new List<string>();
        Attach(cs.Player, new FlatBonusPower(2m, log));
        Attach(cs.Player, new FlatBonusPower(0m, log));
        PlayerAttack(cs, m, 6m);
        Assert.Equal(42, m.CurrentHp);
        Assert.SequenceEqual(new[] { "after-modifying:2" }, log);
    }

    public static void Test_Pipeline_ListenerOrderPlayerThenMonsters()
    {
        var cs = NewCombat(2);
        var log = new List<string>();
        Attach(cs.Monsters[1], new RecordingPower(log, "M2"));
        Attach(cs.Player, new RecordingPower(log, "P"));
        Attach(cs.Monsters[0], new RecordingPower(log, "M1"));
        Hook.AfterSideTurnStart(cs, CombatSide.Player);
        Assert.SequenceEqual(new[] { "P:start", "M1:start", "M2:start", "P:late", "M1:late", "M2:late" }, log);
        var buffer = new List<AbstractModel>();
        Hook.IterateCombatHookListeners(cs, buffer);
        Assert.Equal(3, buffer.Count);
    }

    public static void Test_Pipeline_GuardedHooksSilentOnceCombatEnding()
    {
        var cs = NewCombat();
        var log = new List<string>();
        Attach(cs.Player, new RecordingPower(log, "P"));
        cs.Monsters[0].CurrentHp = 0;
        Assert.True(CombatStatus.IsEnding(cs));
        Hook.AfterSideTurnStart(cs, CombatSide.Player);
        Assert.Equal(0, log.Count);
        var buffer = new List<AbstractModel>();
        Hook.IterateCombatHookListeners(cs, buffer);
        Assert.Equal(0, buffer.Count);
        Hook.IterateHookListeners(cs, buffer);
        Assert.Equal(1, buffer.Count);
    }

    public static void Test_Pipeline_SecondaryEnemiesDieWithLastPrimary()
    {
        var cs = NewCombat(2, 5);
        var boss = cs.Monsters[0];
        var minion = cs.Monsters[1];
        Attach(minion, new MinionPower());
        Assert.True(CombatStatus.IsSecondaryEnemy(minion));
        Assert.True(CombatStatus.IsPrimaryEnemy(boss));
        PlayerAttack(cs, boss, 10m);
        Assert.Equal(0, minion.CurrentHp);
        Assert.Equal(0, cs.Monsters.Count);
    }

    public static void Test_Pipeline_DeathPreventionKeepsCreature()
    {
        var cs = NewCombat(2, 5);
        var m = cs.Monsters[0];
        var log = new List<string>();
        Attach(m, new PreventDeathOncePower());
        Attach(m, new RecordingPower(log, "M"));
        var r = PlayerAttack(cs, m, 9m).Results[0];
        Assert.True(r.WasTargetKilled);
        Assert.Equal(1, m.CurrentHp);
        Assert.Equal(2, cs.Monsters.Count);
        Assert.SequenceEqual(new[] { "M:death-prevented" }, log);
        Assert.Equal(2, m.Powers.Count);
        PlayerAttack(cs, m, 9m);
        Assert.Equal(0, m.CurrentHp);
        Assert.Equal(1, cs.Monsters.Count);
        Assert.SequenceEqual(new[] { "M:death-prevented", "M:death", "M:removed" }, log);
    }

    public static void Test_Pipeline_LastEnemyCannotBeHealedWhileEnding()
    {
        var cs = NewCombat(1, 5);
        var m = cs.Monsters[0];
        Attach(m, new PreventDeathOncePower());
        PlayerAttack(cs, m, 9m);
        Assert.Equal(0, m.CurrentHp);
        Assert.Equal(0, cs.Monsters.Count);
        Assert.Null(PowerCmd.Apply<WeakPower>(cs, m, 1m, cs.Player, null));
    }

    public static void Test_Pipeline_PlayerDeathLosesCombat()
    {
        var cs = NewCombat(1, 50, 5);
        var log = new List<string>();
        Attach(cs.Player, new RecordingPower(log, "P"));
        Assert.True(cs.Player.IsActiveForHooks);
        MonsterAttack(cs, cs.Monsters[0], 6m);
        Assert.Equal(0, cs.Player.CurrentHp);
        Assert.Equal(CombatOutcome.Defeat, cs.Outcome);
        Assert.False(cs.Player.IsActiveForHooks);
        Assert.SequenceEqual(new[] { "P:death", "P:removed" }, log);
        Assert.True(CombatStatus.IsEnding(cs));
        Assert.Equal(0m, CreatureCmd.GainBlock(cs, cs.Monsters[0], 5m, ValueProp.Move, null));
        var buffer = new List<AbstractModel>();
        Attach(cs.Player, new RecordingPower(log, "Q"));
        Hook.IterateHookListeners(cs, buffer);
        Assert.Equal(0, buffer.Count);
        CreatureInternals.HealInternal(cs.Player, 3m);
        Assert.Equal(3, cs.Player.CurrentHp);
        Assert.True(cs.Player.IsActiveForHooks);
        Hook.IterateHookListeners(cs, buffer);
        Assert.Equal(1, buffer.Count);
    }

    public static void Test_Pipeline_BlockGainPlainAndDexterity()
    {
        var cs = NewCombat();
        var card = NewSkillCard();
        var play = new CardPlay(card, null);
        Assert.Equal(5m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play));
        Assert.Equal(5, cs.Player.Block);
        Give<DexterityPower>(cs, cs.Player, 2m, cs.Player);
        Assert.Equal(7m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play));
        Assert.Equal(12, cs.Player.Block);
    }

    public static void Test_Pipeline_BlockGainFrailAndDexterityTruncate()
    {
        var cs = NewCombat();
        var card = NewSkillCard();
        var play = new CardPlay(card, null);
        Give<FrailPower>(cs, cs.Player, 2m, cs.Monsters[0]);
        Assert.Equal(3.75m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play));
        Assert.Equal(3, cs.Player.Block);
        Give<DexterityPower>(cs, cs.Player, 2m, cs.Player);
        Assert.Equal(5.25m, Hook.ModifyBlock(cs, cs.Player, 5m, ValueProp.Move, card, play));
        CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play);
        Assert.Equal(8, cs.Player.Block);
        cs.Player.Block = 3;
        CreatureCmd.GainBlock(cs, cs.Player, 3m, ValueProp.Move, play);
        Assert.Equal(6, cs.Player.Block);
    }

    public static void Test_Pipeline_NegativeDexterityClampsBlockAtZero()
    {
        var cs = NewCombat();
        var play = new CardPlay(NewSkillCard(), null);
        Give<DexterityPower>(cs, cs.Player, -7m, cs.Monsters[0]);
        Assert.Equal(0m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play));
        Assert.Equal(0, cs.Player.Block);
    }

    public static void Test_Pipeline_UnpoweredBlockIgnoresDexterityAndFrail()
    {
        var cs = NewCombat();
        var play = new CardPlay(NewSkillCard(), null);
        Give<DexterityPower>(cs, cs.Player, 3m, cs.Player);
        Give<FrailPower>(cs, cs.Player, 1m, cs.Monsters[0]);
        Assert.Equal(5m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move | ValueProp.Unpowered, play));
        Assert.Equal(4m, CreatureCmd.GainBlock(cs, cs.Player, 4m, ValueProp.Unpowered, null));
        Assert.Equal(9, cs.Player.Block);
    }

    public static void Test_Pipeline_DexterityOwnershipForMonsterBlock()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        Give<DexterityPower>(cs, cs.Player, 3m, cs.Player);
        Assert.Equal(6m, CreatureCmd.GainBlock(cs, m, 6m, ValueProp.Move, null));
        Give<DexterityPower>(cs, m, 2m, m);
        Assert.Equal(8m, CreatureCmd.GainBlock(cs, m, 6m, ValueProp.Move, null));
        Assert.Equal(14, m.Block);
    }

    public static void Test_Pipeline_LoseAndClearBlock()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        m.Block = 5;
        CreatureCmd.LoseBlock(cs, m, 3m, cs.Player);
        Assert.Equal(2, m.Block);
        CreatureCmd.LoseBlock(cs, m, 7m, cs.Player);
        Assert.Equal(0, m.Block);
        m.Block = 9;
        CreatureCmd.ClearBlock(cs, m);
        Assert.Equal(0, m.Block);
        cs.Player.Block = 4;
        cs.Player.TurnNumber = 1;
        CreatureCmd.AfterTurnStart(cs, cs.Player, CombatSide.Player);
        Assert.Equal(4, cs.Player.Block);
        cs.Player.TurnNumber = 2;
        CreatureCmd.AfterTurnStart(cs, cs.Player, CombatSide.Player);
        Assert.Equal(0, cs.Player.Block);
    }

    public static void Test_Pipeline_HealAndMaxHp()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        m.CurrentHp = 30;
        CreatureCmd.Heal(cs, m, 100m);
        Assert.Equal(50, m.CurrentHp);
        CreatureCmd.GainMaxHp(cs, m, 5m);
        Assert.Equal(55, m.MaxHp);
        Assert.Equal(55, m.CurrentHp);
        CreatureCmd.LoseMaxHp(cs, m, 10m, false);
        Assert.Equal(45, m.MaxHp);
        Assert.Equal(45, m.CurrentHp);
        CreatureCmd.SetCurrentHp(cs, m, 0m);
        Assert.Equal(0, cs.Monsters.Count);
    }

    public static void Test_Pipeline_ApplyAndStackSameInstance()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var vuln = Give<VulnerablePower>(cs, m, 2m, cs.Player);
        Assert.Equal(2, vuln.Amount);
        Assert.False(vuln.SkipNextDurationTick);
        Assert.Equal(cs.Player.CombatId, vuln.ApplierId);
        var again = Give<VulnerablePower>(cs, m, 1m, cs.Player);
        Assert.True(ReferenceEquals(vuln, again));
        Assert.Equal(3, vuln.Amount);
        Assert.Equal(1, m.Powers.Count);
        var zero = PowerCmd.Apply<VulnerablePower>(cs, m, 0m, cs.Player, null);
        Assert.True(ReferenceEquals(vuln, zero));
        Assert.Equal(3, vuln.Amount);
        Assert.Equal(1, m.Powers.Count);
    }

    public static void Test_Pipeline_ZeroAmountApplyAddsNothing()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var returned = PowerCmd.Apply<WeakPower>(cs, m, 0m, cs.Player, null);
        Assert.NotNull(returned);
        Assert.False(returned!.HasOwner);
        Assert.Equal(0, m.Powers.Count);
    }

    public static void Test_Pipeline_EnemyDebuffTicksAtEnemyTurnEnd()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var vuln = Give<VulnerablePower>(cs, m, 2m, cs.Player);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player);
        Assert.Equal(2, vuln.Amount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(1, vuln.Amount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(0, vuln.Amount);
        Assert.Null(m.GetPower<VulnerablePower>());
    }

    public static void Test_Pipeline_PlayerDebuffSkipsFirstTick()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var weak = Give<WeakPower>(cs, cs.Player, 2m, m);
        var frail = Give<FrailPower>(cs, cs.Player, 1m, m);
        Assert.True(weak.SkipNextDurationTick);
        Assert.True(frail.SkipNextDurationTick);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(2, weak.Amount);
        Assert.Equal(1, frail.Amount);
        Assert.False(weak.SkipNextDurationTick);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player);
        Assert.Equal(2, weak.Amount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(1, weak.Amount);
        Assert.Null(cs.Player.GetPower<FrailPower>());
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Null(cs.Player.GetPower<WeakPower>());
    }

    public static void Test_Pipeline_StackingPlayerDebuffDoesNotRearmSkip()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var weak = Give<WeakPower>(cs, cs.Player, 1m, m);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(1, weak.Amount);
        Assert.False(weak.SkipNextDurationTick);
        Give<WeakPower>(cs, cs.Player, 1m, m);
        Assert.Equal(2, weak.Amount);
        Assert.False(weak.SkipNextDurationTick);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Assert.Equal(1, weak.Amount);
    }

    public static void Test_Pipeline_BuffsDoNotTickAndAllowNegative()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var str = Give<StrengthPower>(cs, cs.Player, -3m, m);
        var dex = Give<DexterityPower>(cs, cs.Player, 2m, cs.Player);
        Assert.False(str.SkipNextDurationTick);
        Assert.Equal(PowerType.Debuff, str.TypeForCurrentAmount);
        Assert.Equal(PowerType.Buff, dex.TypeForCurrentAmount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player);
        Assert.Equal(-3, str.Amount);
        Assert.Equal(2, dex.Amount);
        Assert.Null(PowerCmd.Apply<StrengthPower>(cs, cs.Player, 3m, cs.Player, null));
        Assert.Null(cs.Player.GetPower<StrengthPower>());
    }

    public static void Test_Pipeline_RemovalByAmountRules()
    {
        var debuff = new VulnerablePower();
        debuff.SetAmount(1);
        Assert.False(debuff.ShouldRemoveDueToAmount());
        debuff.SetAmount(0);
        Assert.True(debuff.ShouldRemoveDueToAmount());
        debuff.SetAmount(-2);
        Assert.True(debuff.ShouldRemoveDueToAmount());
        Assert.Equal(PowerType.Buff, debuff.GetTypeForAmount(-2m));
        var buff = new StrengthPower();
        buff.SetAmount(-2);
        Assert.False(buff.ShouldRemoveDueToAmount());
        buff.SetAmount(0);
        Assert.True(buff.ShouldRemoveDueToAmount());
        buff.SetAmount(int.MaxValue);
        Assert.Equal(PowerModel.MaxAmount, buff.Amount);
        buff.SetAmount(int.MinValue);
        Assert.Equal(-PowerModel.MaxAmount, buff.Amount);
    }

    public static void Test_Pipeline_ApplyBlockedWhenCombatEnding()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var vuln = Give<VulnerablePower>(cs, m, 1m, cs.Player);
        m.CurrentHp = 0;
        Assert.Null(PowerCmd.Apply<WeakPower>(cs, cs.Player, 1m, m, null));
        Assert.Equal(0, PowerCmd.ModifyAmount(cs, vuln, 1m, cs.Player, null));
        Assert.Equal(1, vuln.Amount);
        Assert.Equal(0, cs.Player.Powers.Count);
    }

    public static void Test_Pipeline_RemoveCallsAfterRemoved()
    {
        var cs = NewCombat();
        var log = new List<string>();
        var power = new RecordingPower(log, "P");
        Attach(cs.Player, power);
        PowerCmd.Remove(cs, power);
        Assert.Equal(0, cs.Player.Powers.Count);
        Assert.SequenceEqual(new[] { "P:removed" }, log);
        var vuln = Give<VulnerablePower>(cs, cs.Monsters[0], 1m, cs.Player);
        PowerCmd.Remove<VulnerablePower>(cs, cs.Monsters[0]);
        Assert.False(cs.Monsters[0].HasPower<VulnerablePower>());
        Assert.True(ReferenceEquals(cs.Monsters[0], vuln.Owner));
    }

    public static void Test_Pipeline_DuplicateNonInstancedPowerThrows()
    {
        var cs = NewCombat();
        Give<WeakPower>(cs, cs.Monsters[0], 1m, cs.Player);
        Assert.Throws<InvalidOperationException>(() => new WeakPower().ApplyInternal(cs.Monsters[0], 1m));
        var p = new WeakPower();
        p.ApplyInternal(cs.Player, 1m);
        Assert.Throws<InvalidOperationException>(() => p.AttachTo(cs.Monsters[0]));
    }

    public static void Test_Pipeline_PowerCloneAndCanonical()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        var weak = Give<WeakPower>(cs, cs.Player, 2m, m);
        weak.AmountOnTurnStart = 2;
        var bytes = Canonical(weak);
        var other = new PlayerState(0, "IRONCLAD", 80, 80, 3);
        var clone = weak.CloneFor(other);
        Assert.True(ReferenceEquals(other, clone.Owner));
        Assert.True(ReferenceEquals(cs.Player, weak.Owner));
        Assert.SequenceEqual(bytes, Canonical(clone));
        clone.SetAmount(5);
        clone.SkipNextDurationTick = false;
        Assert.Equal(2, weak.Amount);
        Assert.True(weak.SkipNextDurationTick);
        Assert.SequenceEqual(bytes, Canonical(weak));
        Assert.False(Enumerable.SequenceEqual(bytes, Canonical(clone)));
    }

    public static void Test_Pipeline_EnergyGainLoseSetSpend()
    {
        var cs = NewCombat();
        var p = cs.Player;
        EnergyCmd.ResetEnergy(cs, p);
        Assert.Equal(3, p.Energy);
        Assert.Equal(3, EnergyCmd.GetMaxEnergy(cs, p));
        EnergyCmd.GainEnergy(cs, 2m, p);
        Assert.Equal(5, p.Energy);
        EnergyCmd.GainEnergy(cs, -4m, p);
        Assert.Equal(5, p.Energy);
        EnergyCmd.LoseEnergy(cs, 10m, p);
        Assert.Equal(0, p.Energy);
        EnergyCmd.SetEnergy(cs, 4m, p);
        Assert.Equal(4, p.Energy);
        EnergyCmd.SetEnergy(cs, 1m, p);
        Assert.Equal(1, p.Energy);
        EnergyCmd.AddMaxEnergyToCurrent(cs, p);
        Assert.Equal(4, p.Energy);
        EnergyCmd.SpendEnergy(cs, NewAttackCard(), 3);
        Assert.Equal(1, p.Energy);
        var xCard = new CardInstance(new AttackDouble(true), 300);
        EnergyCmd.SpendEnergy(cs, xCard, 1);
        Assert.Equal(1, xCard.EnergyCost.CapturedXValue);
        Assert.Equal(0, p.Energy);
        Assert.Throws<ArgumentException>(() => EnergyCmd.LoseEnergyInternal(p, -1m));
    }

    public static void Test_Pipeline_CreatureInternalsClampAndValidate()
    {
        var cs = NewCombat();
        var m = cs.Monsters[0];
        CreatureInternals.GainBlockInternal(m, 2.9m);
        Assert.Equal(2, m.Block);
        Assert.Throws<ArgumentException>(() => CreatureInternals.GainBlockInternal(m, -1m));
        CreatureInternals.LoseBlockInternal(m, 10m);
        Assert.Equal(0, m.Block);
        var r = CreatureInternals.LoseHpInternal(m, 49.5m, ValueProp.Move);
        Assert.Equal(49, r.UnblockedDamage);
        Assert.False(r.WasTargetKilled);
        Assert.Equal(1, m.CurrentHp);
        r = CreatureInternals.LoseHpInternal(m, 1m, ValueProp.Move);
        Assert.True(r.WasTargetKilled);
        Assert.Equal(0, r.OverkillDamage);
        Assert.Equal(0, m.CurrentHp);
        Assert.Throws<ArgumentException>(() => CreatureInternals.SetMaxHpInternal(m, -1m));
    }

    public static void Test_Pipeline_AmountOnTurnStartSnapshot()
    {
        var cs = NewCombat();
        var str = Give<StrengthPower>(cs, cs.Player, 2m, cs.Player);
        CreatureCmd.BeforeTurnStart(cs, cs.Player, CombatSide.Player);
        Give<StrengthPower>(cs, cs.Player, 3m, cs.Player);
        Assert.Equal(2, str.AmountOnTurnStart);
        Assert.Equal(5, str.Amount);
    }
}
