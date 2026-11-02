using Sts2Sim.Core;
using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Monsters;
using Sts2Sim.Core.Orbs;
using Sts2Sim.Core.Powers;
using Sts2Sim.Core.Random;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
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

        public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => _log.Add(_tag + ":start");

        public override void AfterSideTurnStartLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants) => _log.Add(_tag + ":late");

        public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner))
            {
                _log.Add(_tag + ":hit" + result.UnblockedDamage);
            }
        }

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented) => _log.Add(_tag + ":death" + (wasRemovalPrevented ? "-prevented" : string.Empty));

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

        public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
            => ReferenceEquals(dealer, Owner) ? _bonus : 0m;

        public override void AfterModifyingDamageAmount(RunState rs, CardInstance? cardSource) => _log.Add("after-modifying:" + _bonus);
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

        public override decimal ModifyDamageCap(RunState rs, CreatureState? target, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
            => ReferenceEquals(target, Owner) ? _cap : decimal.MaxValue;
    }

    private sealed class PreventDeathOncePower : PowerModel
    {
        private bool _used;

        public override string Id => "PIPELINE_PREVENT_DEATH_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldDie(RunState rs, CreatureState creature) => !ReferenceEquals(creature, Owner) || _used;

        public override void AfterPreventingDeath(RunState rs, CreatureState creature)
        {
            _used = true;
            CreatureCmd.Heal(rs.Combat!, creature, 1m);
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
        var play = new CardPlay(cs, card, target);
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
        DamageCmd.Attack(cs, 6m).FromCard(card, new CardPlay(cs, card, m)).Targeting(m).Unpowered().Execute();
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
        var cmd = DamageCmd.Attack(cs, 6m).FromCard(card, new CardPlay(cs, card, null)).TargetingAllOpponents().Execute();
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
        var play = new CardPlay(cs, card, null);
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
        var play = new CardPlay(cs, card, null);
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
        var play = new CardPlay(cs, NewSkillCard(), null);
        Give<DexterityPower>(cs, cs.Player, -7m, cs.Monsters[0]);
        Assert.Equal(0m, CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, play));
        Assert.Equal(0, cs.Player.Block);
    }

    public static void Test_Pipeline_UnpoweredBlockIgnoresDexterityAndFrail()
    {
        var cs = NewCombat();
        var play = new CardPlay(cs, NewSkillCard(), null);
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

    private sealed class MovingMonsterDouble : MonsterDef
    {
        public MovingMonsterDouble(string id)
            : base(id)
        {
        }

        public override int MinInitialHp(CombatState cs) => 20;

        public override int MaxInitialHp(CombatState cs) => 20;

        public override MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
        {
            var move = new MoveStateDef("PIPELINE_TEST_NOTHING", static (_, _, _) => { });
            move.FollowUpState = move;
            return new MonsterMoveStateMachineDef(new AiStateDef[] { move }, move);
        }
    }

    private sealed class StrikeDouble : CardDef
    {
        public StrikeDouble()
            : base("PIPELINE_TEST_STRIKE", 1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
        {
        }

        public override IReadOnlyCollection<CardTag> CanonicalTags => new[] { CardTag.Strike };

        public override void OnPlay(CombatState cs, CardPlay play)
        {
        }
    }

    private sealed class OrbDouble : OrbModel
    {
        public override string Id => "PIPELINE_TEST_ORB";

        public override decimal PassiveVal(CombatState cs) => 3m;

        public override decimal EvokeVal(CombatState cs) => 8m;
    }

    private sealed class PhrogDouble : RelicModel, IVulnerableMultiplierModifier
    {
        public override string Id => "PIPELINE_TEST_PHROG";

        public override RelicRarity Rarity => RelicRarity.Uncommon;

        public decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner))
            {
                return amount;
            }
            if (!props.IsPoweredAttack())
            {
                return amount;
            }
            return amount + 0.25m;
        }
    }

    private sealed class KraneDouble : RelicModel, IWeakMultiplierModifier
    {
        public override string Id => "PIPELINE_TEST_KRANE";

        public override RelicRarity Rarity => RelicRarity.Rare;

        public decimal ModifyWeakMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (!ReferenceEquals(target, Owner))
            {
                return amount;
            }
            if (!props.IsPoweredAttack())
            {
                return amount;
            }
            return amount - 0.15m;
        }
    }

    private sealed class CrueltyDouble : PowerModel, IVulnerableMultiplierModifier
    {
        public override string Id => MultiplierModifierIds.CrueltyPower;

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (ReferenceEquals(target, Owner))
            {
                return amount;
            }
            if (!props.IsPoweredAttack())
            {
                return amount;
            }
            return amount + Amount / 100m;
        }
    }

    private sealed class DebilitateDouble : PowerModel, IVulnerableMultiplierModifier, IWeakMultiplierModifier
    {
        public override string Id => MultiplierModifierIds.DebilitatePower;

        public override PowerType Type => PowerType.Debuff;

        public override PowerStackType StackType => PowerStackType.Counter;

        public decimal ModifyVulnerableMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (!ReferenceEquals(target, Owner))
            {
                return amount;
            }
            if (!props.IsPoweredAttack())
            {
                return amount;
            }
            return amount + (amount - 1m);
        }

        public decimal ModifyWeakMultiplier(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        {
            if (!ReferenceEquals(dealer, Owner))
            {
                return amount;
            }
            if (!props.IsPoweredAttack())
            {
                return amount;
            }
            return amount - (1m - amount);
        }
    }

    private sealed class RedirectToCreaturePower : PowerModel
    {
        private readonly int _redirectId;

        public RedirectToCreaturePower(int redirectId)
        {
            _redirectId = redirectId;
        }

        public override string Id => "PIPELINE_REDIRECT_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer)
        {
            if (!ReferenceEquals(target, Owner))
            {
                return target;
            }
            return CombatStatus.FindCreature(cs, _redirectId) ?? target;
        }
    }

    private sealed class ContextSpyPower : PowerModel
    {
        private readonly List<string> _log;

        public ContextSpyPower(List<string> log)
        {
            _log = log;
        }

        public override string Id => "PIPELINE_CONTEXT_SPY_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
        {
            if (!ReferenceEquals(power, this))
            {
                _log.Add("power:" + power.Id + ":" + Top(cs));
            }
        }

        public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
            => _log.Add("damage:" + target.CombatId + ":" + Top(rs.Combat!));

        public override void AfterAttack(CombatState cs, AttackCommand command) => _log.Add("attack:" + Top(cs));

        private static string Top(CombatState cs)
        {
            var contexts = cs.Choice.Contexts;
            return contexts.Count == 0 ? "none" : contexts[contexts.Count - 1].Kind.ToString();
        }
    }

    private sealed class CreatureEventSpyPower : PowerModel
    {
        private readonly List<string> _log;

        public CreatureEventSpyPower(List<string> log)
        {
            _log = log;
        }

        public override string Id => "PIPELINE_CREATURE_EVENT_SPY_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

        public override bool ShouldPowerBeRemovedAfterOwnerDeath() => false;

        public override void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature) => _log.Add("added:" + creature.CombatId);

        public override void BeforeDeath(RunState rs, CreatureState creature) => _log.Add("before-death:" + creature.CombatId);

        public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented) => _log.Add("death:" + creature.CombatId);

        public override void AfterAttack(CombatState cs, AttackCommand command) => _log.Add("attack-history:" + cs.History.CreatureAttackedEntries.Count);
    }

    private sealed class KeepPetsInCombatPower : PowerModel
    {
        public override string Id => "PIPELINE_KEEP_PETS_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature) => creature.Side != CombatSide.Player;
    }

    private sealed class UnhittableOwnerPower : PowerModel
    {
        public override string Id => "PIPELINE_UNHITTABLE_POWER";

        public override PowerType Type => PowerType.Buff;

        public override PowerStackType StackType => PowerStackType.Single;

        public override bool ShouldAllowHitting(CombatState cs, CreatureState creature) => !ReferenceEquals(creature, Owner);
    }

    private sealed class TemporaryStrengthDouble : TemporaryStrengthPower
    {
        public override string Id => "PIPELINE_TEMPORARY_STRENGTH_POWER";

        public override string OriginModelId => "PIPELINE_TEST_ORIGIN";
    }

    private sealed class TemporaryStrengthDownDouble : TemporaryStrengthPower
    {
        public override string Id => "PIPELINE_TEMPORARY_STRENGTH_DOWN_POWER";

        public override string OriginModelId => "PIPELINE_TEST_ORIGIN";

        protected override bool IsPositive => false;
    }

    private static MonsterState NewPet(CombatState cs, string monsterId, int hp)
    {
        return new MonsterState(cs.AllocateCreatureId(), new MovingMonsterDouble(monsterId), hp, hp, null, null)
        {
            Side = CombatSide.Player,
            PetOwnerId = cs.Player.CombatId
        };
    }

    private static MonsterState AddPet(CombatState cs, string monsterId, int hp)
    {
        var pet = NewPet(cs, monsterId, hp);
        cs.AddAlly(pet);
        cs.Player.PetIds.Add(pet.CombatId);
        return pet;
    }

    private static CombatState NewCombatWithIds(int monsters, int monsterHp = 50)
    {
        var cs = NewCombat(monsters, monsterHp);
        cs.NextCreatureId = monsters + 1;
        return cs;
    }

    public static void Test_Pipeline_CombatStatusAttachmentWithoutPets()
    {
        var cs = NewCombatWithIds(2);
        var m = cs.Monsters[0];
        Assert.True(CombatStatus.IsAttached(cs, cs.Player));
        Assert.True(CombatStatus.IsAttached(cs, m));
        Assert.True(ReferenceEquals(cs, CombatStatus.CombatOf(cs, m)));
        Assert.True(ReferenceEquals(m, CombatStatus.FindCreature(cs, m.CombatId)));
        Assert.True(ReferenceEquals(cs.Player, CombatStatus.FindCreature(cs, cs.Player.CombatId)));
        Assert.Null(CombatStatus.FindCreature(cs, PowerModel.NoCreature));
        Assert.Equal(1, CombatStatus.PlayerCount(cs));
        Assert.False(CombatStatus.AllPlayersDead(cs));
        Assert.Null(CombatStatus.GetOsty(cs, cs.Player));
        Assert.False(CombatStatus.IsOstyAlive(cs, cs.Player));
        Assert.True(CombatStatus.IsOstyMissing(cs, cs.Player));
        var side = new List<CreatureState>();
        CombatStatus.GetCreaturesOnSide(cs, CombatSide.Player, side);
        Assert.SequenceEqual(new CreatureState[] { cs.Player }, side);
        CombatStatus.GetOpponentsOf(cs, cs.Player, side);
        Assert.SequenceEqual(new CreatureState[] { cs.Monsters[0], cs.Monsters[1] }, side);
        cs.RemoveMonster(m);
        Assert.False(CombatStatus.IsAttached(cs, m));
        Assert.Null(CombatStatus.CombatOf(cs, m));
        Assert.False(CombatStatus.CanReceivePowers(cs, m));
        var card = NewAttackCard();
        Assert.True(ReferenceEquals(cs.Player, CombatStatus.CardOwnerCreature(cs, card)));
    }

    public static void Test_Pipeline_PetsAreAttachedAlliesOnThePlayerSide()
    {
        W1Pending.Run(nameof(Test_Pipeline_PetsAreAttachedAlliesOnThePlayerSide), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 10);
            var other = AddPet(cs, "PIPELINE_TEST_PET", 4);
            Assert.True(CombatStatus.IsAttached(cs, osty));
            Assert.True(CombatStatus.CanReceivePowers(cs, osty));
            var side = new List<CreatureState>();
            CombatStatus.GetCreaturesOnSide(cs, CombatSide.Player, side);
            Assert.SequenceEqual(new CreatureState[] { cs.Player, osty, other }, side);
            CombatStatus.GetTeammatesOf(cs, osty, side);
            Assert.SequenceEqual(new CreatureState[] { cs.Player, osty, other }, side);
            CombatStatus.GetOpponentsOf(cs, cs.Monsters[0], side);
            Assert.SequenceEqual(new CreatureState[] { cs.Player, osty, other }, side);
            CombatStatus.GetPlayerCreatures(cs, side);
            Assert.SequenceEqual(new CreatureState[] { cs.Player }, side);
            Assert.True(ReferenceEquals(cs.Player, CombatStatus.PetOwnerOf(cs, osty)));
            Assert.True(CombatStatus.IsPet(cs, other));
            Assert.False(CombatStatus.IsPet(cs, cs.Monsters[0]));
            Assert.True(ReferenceEquals(osty, CombatStatus.GetOsty(cs, cs.Player)));
            Assert.True(CombatStatus.IsOstyAlive(cs, cs.Player));
            Assert.Equal(1, CombatStatus.PlayerCount(cs));
            var die = Assert.NotNull(PowerCmd.Apply<DieForYouPower>(cs, osty, 1m, null, null));
            Assert.True(ReferenceEquals(osty, die.Owner));
            CreatureCmd.Heal(cs, osty, 0m);
            Assert.Equal(10, osty.CurrentHp);
        });
    }

    public static void Test_Pipeline_HitOnPetTakesItsOwnersBlock()
    {
        W1Pending.Run(nameof(Test_Pipeline_HitOnPetTakesItsOwnersBlock), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 10);
            var m = cs.Monsters[0];
            cs.Player.Block = 7;
            var first = CreatureCmd.Damage(cs, osty, 5m, ValueProp.Move, m, null, null);
            Assert.Equal(2, cs.Player.Block);
            Assert.Equal(10, osty.CurrentHp);
            Assert.Equal(1, first.Count);
            Assert.Equal(5, first[0].BlockedDamage);
            Assert.True(first[0].WasBlockBroken, "the flags read the pet's own block, which is 0");
            Assert.True(first[0].WasFullyBlocked);
            var second = CreatureCmd.Damage(cs, osty, 6m, ValueProp.Move, m, null, null);
            Assert.Equal(0, cs.Player.Block);
            Assert.Equal(6, osty.CurrentHp);
            Assert.Equal(2, second[0].BlockedDamage);
            Assert.Equal(4, second[0].UnblockedDamage);
            Assert.True(second[0].WasBlockBroken);
            Assert.False(second[0].WasFullyBlocked);
            Assert.Equal(2, cs.History.DamageReceivedEntries.Count);
        });
    }

    public static void Test_Pipeline_RedirectedUnblockedDamageReturnsOverkillToTheOriginalTarget()
    {
        var cs = NewCombatWithIds(2);
        var attacker = cs.Monsters[0];
        var shield = cs.Monsters[1];
        shield.CurrentHp = 3;
        Attach(cs.Player, new RedirectToCreaturePower(shield.CombatId));
        cs.Player.Block = 2;
        var results = CreatureCmd.Damage(cs, cs.Player, 10m, ValueProp.Move, attacker, null, null);
        Assert.Equal(2, results.Count);
        Assert.True(ReferenceEquals(shield, results[0].Receiver));
        Assert.Equal(3, results[0].UnblockedDamage);
        Assert.Equal(5, results[0].OverkillDamage);
        Assert.True(results[0].WasTargetKilled);
        Assert.Equal(0, results[0].BlockedDamage, "only the original target's result carries the block flags");
        Assert.True(ReferenceEquals(cs.Player, results[1].Receiver));
        Assert.Equal(5, results[1].UnblockedDamage);
        Assert.Equal(2, results[1].BlockedDamage);
        Assert.True(results[1].WasBlockBroken);
        Assert.False(results[1].WasFullyBlocked);
        Assert.Equal(75, cs.Player.CurrentHp);
        Assert.Equal(0, cs.Player.Block);
        Assert.True(shield.IsDead);
        Assert.False(cs.Monsters.Contains(shield), "the killed redirect target is removed");
        Assert.Equal(2, cs.History.DamageReceivedEntries.Count);
        Assert.Equal(shield.CombatId, cs.History.DamageReceivedEntries[0].Result.ReceiverId);
        Assert.Equal(cs.Player.CombatId, cs.History.DamageReceivedEntries[1].Result.ReceiverId);
    }

    public static void Test_Pipeline_DieForYouPowerRules()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var osty = NewPet(cs, CombatStatus.OstyMonsterId, 5);
        var die = new DieForYouPower();
        die.ApplyInternal(osty, 1m);
        Assert.Equal("DIE_FOR_YOU_POWER", die.Id);
        Assert.Equal(PowerType.Buff, die.Type);
        Assert.Equal(PowerStackType.Single, die.StackType);
        Assert.True(ReferenceEquals(osty, die.ModifyUnblockedDamageTarget(cs, cs.Player, 5m, ValueProp.Move, m)));
        Assert.True(ReferenceEquals(cs.Player, die.ModifyUnblockedDamageTarget(cs, cs.Player, 5m, ValueProp.Move | ValueProp.Unpowered, m)));
        Assert.True(ReferenceEquals(m, die.ModifyUnblockedDamageTarget(cs, m, 5m, ValueProp.Move, cs.Player)));
        Assert.False(die.ShouldAllowHitting(cs, NewPet(cs, "PIPELINE_TEST_PET", 0)));
        Assert.True(die.ShouldAllowHitting(cs, m));
        Assert.False(die.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, osty));
        Assert.True(die.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, m));
        Assert.False(die.ShouldPowerBeRemovedAfterOwnerDeath());
        osty.CurrentHp = 0;
        Assert.True(ReferenceEquals(cs.Player, die.ModifyUnblockedDamageTarget(cs, cs.Player, 5m, ValueProp.Move, m)));
        var stray = new MonsterState(cs.AllocateCreatureId(), new MovingMonsterDouble(CombatStatus.OstyMonsterId), 5, 5, null, null);
        var unowned = new DieForYouPower();
        unowned.ApplyInternal(stray, 1m);
        Assert.True(ReferenceEquals(cs.Player, unowned.ModifyUnblockedDamageTarget(cs, cs.Player, 5m, ValueProp.Move, m)));
    }

    public static void Test_Pipeline_DieForYouRedirectsPoweredAttacksOnTheOwnerToOsty()
    {
        W1Pending.Run(nameof(Test_Pipeline_DieForYouRedirectsPoweredAttacksOnTheOwnerToOsty), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var m = cs.Monsters[0];
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 4);
            Assert.NotNull(PowerCmd.Apply<DieForYouPower>(cs, osty, 1m, null, null));
            cs.Player.Block = 3;
            MonsterAttack(cs, m, 10m);
            Assert.Equal(0, cs.Player.Block);
            Assert.True(osty.IsDead);
            Assert.Equal(77, cs.Player.CurrentHp);
            Assert.True(CombatStatus.IsAttached(cs, osty), "DieForYouPower keeps Osty in the combat after its death");
            Assert.Equal(1, cs.Player.PetIds.Count);
            Assert.True(osty.HasPower<DieForYouPower>());
            MonsterAttack(cs, m, 6m);
            Assert.Equal(71, cs.Player.CurrentHp);
        });
    }

    public static void Test_Pipeline_PlayerDeathKillsOnlyAnAliveOstyBeforeDeactivatingHooks()
    {
        W1Pending.Run(nameof(Test_Pipeline_PlayerDeathKillsOnlyAnAliveOstyBeforeDeactivatingHooks), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 3);
            var other = AddPet(cs, "PIPELINE_TEST_PET", 4);
            var log = new List<string>();
            Attach(cs.Player, new CreatureEventSpyPower(log));
            CreatureCmd.Kill(cs, cs.Player);
            Assert.Equal(CombatOutcome.Defeat, cs.Outcome);
            Assert.True(osty.IsDead);
            Assert.True(other.IsAlive, "no other pet dies with its owner");
            Assert.SequenceEqual(new[] { "before-death:0", "death:0", "before-death:" + osty.CombatId, "death:" + osty.CombatId }, log);
            Assert.False(cs.Player.IsActiveForHooks);
            Assert.Equal(0, cs.Player.OrbQueue.Capacity);
        });
    }

    public static void Test_Pipeline_DeadOstyIsNotKilledAgainWithItsOwner()
    {
        W1Pending.Run(nameof(Test_Pipeline_DeadOstyIsNotKilledAgainWithItsOwner), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 3);
            Attach(cs.Player, new KeepPetsInCombatPower());
            CreatureCmd.Kill(cs, osty);
            Assert.True(osty.IsDead);
            var log = new List<string>();
            Attach(cs.Player, new CreatureEventSpyPower(log));
            CreatureCmd.Kill(cs, cs.Player);
            Assert.SequenceEqual(new[] { "before-death:0", "death:0" }, log);
        });
    }

    public static void Test_Pipeline_PetDeathDropsItFromPetIdsUnlessKeptInCombat()
    {
        W1Pending.Run(nameof(Test_Pipeline_PetDeathDropsItFromPetIdsUnlessKeptInCombat), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var pet = AddPet(cs, "PIPELINE_TEST_PET", 3);
            CreatureCmd.Kill(cs, pet);
            Assert.True(pet.IsDead);
            Assert.Equal(0, cs.Player.PetIds.Count, "ShouldCreatureBeRemovedFromCombatAfterDeath holds, so OnPetDied drops the pet");
            Assert.True(CombatStatus.IsAttached(cs, pet), "a dead pet stays in the allies");
            var cs2 = NewCombatWithIds(1);
            Attach(cs2.Player, new KeepPetsInCombatPower());
            var kept = AddPet(cs2, CombatStatus.OstyMonsterId, 3);
            CreatureCmd.Kill(cs2, kept);
            Assert.True(kept.IsDead);
            Assert.SequenceEqual(new[] { kept.CombatId }, cs2.Player.PetIds);
            Assert.True(ReferenceEquals(kept, CombatStatus.GetOsty(cs2, cs2.Player)));
            Assert.False(CombatStatus.IsOstyAlive(cs2, cs2.Player));
        });
    }

    public static void Test_Pipeline_AddCreatureSetsUpRollsAndDispatches()
    {
        var cs = NewCombatWithIds(1);
        var log = new List<string>();
        Attach(cs.Player, new CreatureEventSpyPower(log));
        var spawned = new MonsterState(cs.AllocateCreatureId(), new MovingMonsterDouble("PIPELINE_TEST_SUMMON"), 20, 20, null, null);
        CreatureCmd.Add(cs, spawned);
        Assert.Equal(2, cs.Monsters.Count);
        Assert.True(ReferenceEquals(spawned, cs.Monsters[1]));
        Assert.True(spawned.HasAi);
        Assert.True(spawned.SpawnedThisTurn);
        Assert.NotNull(spawned.Ai.NextMove, "an enemy added during the player turn rolls its move");
        Assert.SequenceEqual(new[] { "added:" + spawned.CombatId }, log);
        Assert.Throws<InvalidOperationException>(() => CreatureCmd.Add(cs, spawned));
        Assert.Throws<ArgumentException>(() => CreatureCmd.Add(cs, cs.Player));
        cs.CurrentSide = CombatSide.Enemy;
        var late = new MonsterState(cs.AllocateCreatureId(), new MovingMonsterDouble("PIPELINE_TEST_SUMMON"), 20, 20, null, null);
        CreatureCmd.Add(cs, late);
        Assert.Null(late.Ai.NextMove, "no roll while the enemy side acts");
        cs.IsInProgress = false;
        var outside = new MonsterState(cs.AllocateCreatureId(), new MovingMonsterDouble("PIPELINE_TEST_SUMMON"), 20, 20, null, null);
        Assert.Throws<InvalidOperationException>(() => CreatureCmd.Add(cs, outside));
        Assert.False(cs.Monsters.Contains(outside));
    }

    public static void Test_Pipeline_AddMonsterDefSpawnsThroughTheSpawner()
    {
        W1Pending.Run(nameof(Test_Pipeline_AddMonsterDefSpawnsThroughTheSpawner), "monsters-kernel", () =>
        {
            var cs = NewCombatWithIds(1);
            var log = new List<string>();
            Attach(cs.Player, new CreatureEventSpyPower(log));
            var spawned = CreatureCmd.Add(cs, new MovingMonsterDouble("PIPELINE_TEST_SUMMON"));
            Assert.True(cs.Monsters.Contains(spawned));
            Assert.Equal(CombatSide.Enemy, spawned.Side);
            Assert.True(spawned.HasAi);
            Assert.NotNull(spawned.Ai.NextMove);
            Assert.Equal(20, spawned.MaxHp);
            Assert.SequenceEqual(new[] { "added:" + spawned.CombatId }, log);
        });
    }

    public static void Test_Pipeline_AddPetJoinsTheAllies()
    {
        W1Pending.Run(nameof(Test_Pipeline_AddPetJoinsTheAllies), "state", () =>
        {
            var cs = NewCombatWithIds(1);
            var log = new List<string>();
            Attach(cs.Player, new CreatureEventSpyPower(log));
            var pet = NewPet(cs, CombatStatus.OstyMonsterId, 1);
            CreatureCmd.Add(cs, pet);
            Assert.False(cs.Monsters.Contains(pet));
            Assert.True(CombatStatus.IsAttached(cs, pet));
            Assert.True(pet.HasAi);
            Assert.Null(pet.Ai.NextMove, "pets never roll a move");
            Assert.SequenceEqual(new[] { "added:" + pet.CombatId }, log);
        });
    }

    public static void Test_Pipeline_EscapeStripsPowersSilentlyRemovesAndRecords()
    {
        var cs = NewCombatWithIds(2);
        var m = cs.Monsters[0];
        var log = new List<string>();
        Give<StrengthPower>(cs, m, 3m, m);
        Attach(m, new RecordingPower(log, "R"));
        CreatureCmd.Escape(cs, m);
        Assert.False(cs.Monsters.Contains(m));
        Assert.Equal(0, m.Powers.Count);
        Assert.Equal(0, log.Count, "escape removes powers without AfterRemoved");
        Assert.True(m.IsAlive);
        Assert.Equal(0, cs.Run.Fidelity.Count);
        Assert.SequenceEqual(new CreatureState[] { m }, cs.EscapedCreatures);
        CreatureCmd.Escape(cs, m);
        Assert.Equal(1, cs.EscapedCreatures.Count, "a creature that already left is ignored");
        var dead = cs.Monsters[0];
        dead.CurrentHp = 0;
        CreatureCmd.Escape(cs, dead);
        Assert.True(cs.Monsters.Contains(dead), "a dead creature cannot escape");
    }

    public static void Test_Pipeline_CalculatedAttackComputesEachHitAfterTargetSelection()
    {
        var cs = NewCombatWithIds(2);
        var card = NewAttackCard();
        var receivers = new List<int>();
        var cmd = DamageCmd.Attack(cs, (_, target) => 5m + (target?.CombatId ?? 100))
            .FromCard(card, null)
            .TargetingRandomOpponents()
            .WithHitCount(4)
            .Execute();
        Assert.True(cmd.IsCalculated);
        Assert.Equal(AttackCommand.CalculatedDamagePerHit, cmd.DamagePerHit);
        Assert.Equal(4, cmd.Results.Count);
        for (int i = 0; i < cmd.Results.Count; i++)
        {
            Assert.Equal(5 + cmd.Results[i].Receiver.CombatId, cmd.Results[i].UnblockedDamage);
        }
        var cs2 = NewCombatWithIds(2);
        var all = DamageCmd.Attack(cs2, (_, target) => target == null ? 3m : 50m).FromCard(card, null).TargetingAllOpponents().Execute();
        Assert.Equal(2, all.Results.Count);
        Assert.Equal(3, all.Results[0].UnblockedDamage, "a multi-target hit has no single target");
        Assert.Equal(3, all.Results[1].UnblockedDamage);
    }

    public static void Test_Pipeline_FromOstyAcceptsOnlyOsty()
    {
        var cs = NewCombatWithIds(1);
        var card = NewAttackCard();
        Assert.Throws<ArgumentException>(() => DamageCmd.Attack(cs, 4m).FromOsty(cs.Monsters[0], card, null));
        Assert.Throws<ArgumentException>(() => DamageCmd.Attack(cs, 4m).FromOsty(cs.Player, card, null));
        var detached = NewPet(cs, CombatStatus.OstyMonsterId, 5);
        var cmd = DamageCmd.Attack(cs, 4m).FromOsty(detached, card, null).Targeting(cs.Monsters[0]);
        Assert.True(ReferenceEquals(detached, cmd.Attacker));
        Assert.True(ReferenceEquals(card, cmd.ModelSource));
        Assert.Throws<InvalidOperationException>(() => cmd.Execute());
    }

    public static void Test_Pipeline_OstyRandomAttackDrawsThePetOwnersTargetRng()
    {
        W1Pending.Run(nameof(Test_Pipeline_OstyRandomAttackDrawsThePetOwnersTargetRng), "state", () =>
        {
            var cs = NewCombatWithIds(3);
            var osty = AddPet(cs, CombatStatus.OstyMonsterId, 10);
            var card = NewAttackCard();
            int before = cs.Rng.CombatTargets.Counter;
            var cmd = DamageCmd.Attack(cs, 4m).FromOsty(osty, card, null).TargetingRandomOpponents().WithHitCount(3).Execute();
            Assert.Equal(3, cmd.HitsCompleted);
            Assert.Equal(before + 3, cs.Rng.CombatTargets.Counter);
            Assert.Equal(1, cs.History.CreatureAttackedEntries.Count);
            Assert.Equal(osty.CombatId, cs.History.CreatureAttackedEntries[0].Stamp.ActorId);
            Assert.Equal(3, cs.History.CreatureAttackedEntries[0].DamageResults.Count);
        });
    }

    public static void Test_Pipeline_MonsterAttacksRunUnderAnInlineContext()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var log = new List<string>();
        Attach(cs.Player, new ContextSpyPower(log));
        MonsterAttack(cs, m, 3m);
        Assert.SequenceEqual(new[] { "damage:0:Inline", "attack:Inline" }, log);
        Assert.Equal(0, cs.Choice.ContextDepth);
        log.Clear();
        PlayerAttack(cs, m, 3m);
        Assert.SequenceEqual(new[] { "damage:" + m.CombatId + ":none", "attack:none" }, log);
    }

    public static void Test_Pipeline_DecrementRunsUnderAThrowingContext()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var log = new List<string>();
        Attach(cs.Player, new ContextSpyPower(log));
        var weak = Give<WeakPower>(cs, m, 3m, cs.Player);
        log.Clear();
        PowerCmd.Decrement(cs, weak);
        Assert.Equal(2, weak.Amount);
        Assert.SequenceEqual(new[] { "power:WEAK_POWER:Throwing" }, log);
        Assert.Equal(0, cs.Choice.ContextDepth);
        log.Clear();
        PowerCmd.ModifyAmount(cs, weak, -1m, null, null);
        Assert.SequenceEqual(new[] { "power:WEAK_POWER:none" }, log);
    }

    public static void Test_Pipeline_HistoryRecordsDamageBlockPowerAndEnergyAtTheGameSites()
    {
        var cs = NewCombatWithIds(2);
        var m = cs.Monsters[0];
        var card = NewAttackCard();
        var play = new CardPlay(cs, card, m);
        DamageCmd.Attack(cs, 6m).FromCard(card, play).Targeting(m).Execute();
        Assert.Equal(1, cs.History.DamageReceivedEntries.Count);
        var dmg = cs.History.DamageReceivedEntries[0];
        Assert.Equal(m.CombatId, dmg.Result.ReceiverId);
        Assert.Equal(6, dmg.Result.UnblockedDamage);
        Assert.Equal(cs.Player.CombatId, dmg.DealerId);
        Assert.Equal(card.InstanceId, dmg.CardSourceInstanceId);
        Assert.Equal(card.Id, dmg.CardSourceId);
        Assert.Equal(1, cs.History.CreatureAttackedEntries.Count);
        Assert.Equal(cs.Player.CombatId, cs.History.CreatureAttackedEntries[0].Stamp.ActorId);
        Assert.True(cs.History.CreatureAttackedEntries[0].Stamp.Sequence > dmg.Stamp.Sequence, "CreatureAttacked follows the hits");
        CreatureCmd.GainBlock(cs, cs.Player, 5.9m, ValueProp.Move, null);
        Assert.Equal(1, cs.History.BlockGainedEntries.Count);
        Assert.Equal(5, cs.History.BlockGainedEntries[0].Amount);
        Assert.Equal(cs.Player.CombatId, cs.History.BlockGainedEntries[0].ReceiverId);
        Assert.Null(cs.History.BlockGainedEntries[0].CardPlay);
        Give<DexterityPower>(cs, cs.Player, -10m, cs.Player);
        CreatureCmd.GainBlock(cs, cs.Player, 5m, ValueProp.Move, null);
        Assert.Equal(1, cs.History.BlockGainedEntries.Count, "no entry when the modified block is 0");
        int powers = cs.History.PowerReceivedEntries.Count;
        var str = Give<StrengthPower>(cs, cs.Player, 2m, cs.Player);
        Give<StrengthPower>(cs, cs.Player, 3m, cs.Player);
        PowerCmd.ModifyAmount(cs, str, 0m, null, null);
        Assert.Equal(powers + 3, cs.History.PowerReceivedEntries.Count);
        Assert.Equal(2m, cs.History.PowerReceivedEntries[powers].Amount);
        Assert.Equal(3m, cs.History.PowerReceivedEntries[powers + 1].Amount);
        Assert.Equal(0m, cs.History.PowerReceivedEntries[powers + 2].Amount, "ModifyAmount records before SetAmount even for a zero offset");
        Assert.Equal("STRENGTH_POWER", cs.History.PowerReceivedEntries[powers].PowerId);
        Assert.Equal(cs.Player.CombatId, cs.History.PowerReceivedEntries[powers].ApplierId);
        cs.Player.Energy = 3;
        EnergyCmd.SpendEnergy(cs, card, 2);
        EnergyCmd.SpendEnergy(cs, card, 0);
        Assert.Equal(1, cs.History.EnergySpentEntries.Count);
        Assert.Equal(2, cs.History.EnergySpentEntries[0].Amount);
        Assert.Equal(cs.Player.CombatId, cs.History.EnergySpentEntries[0].Stamp.ActorId);
        Assert.Equal(1, cs.Player.Energy);
    }

    public static void Test_Pipeline_KillingBlowOnTheLastEnemyIsNotRecorded()
    {
        var cs = NewCombatWithIds(1, monsterHp: 10);
        var m = cs.Monsters[0];
        var log = new List<string>();
        Attach(cs.Player, new CreatureEventSpyPower(log));
        PlayerAttack(cs, m, 4m);
        PlayerAttack(cs, m, 10m);
        Assert.True(m.IsDead);
        Assert.True(CombatStatus.IsEnding(cs));
        Assert.Equal(1, cs.History.DamageReceivedEntries.Count, "the killing blow ends the combat before it is recorded");
        Assert.Equal(4, cs.History.DamageReceivedEntries[0].Result.UnblockedDamage);
        Assert.Equal(2, cs.History.CreatureAttackedEntries.Count);
        Assert.Equal(6, cs.History.CreatureAttackedEntries[1].DamageResults[0].UnblockedDamage);
        Assert.SequenceEqual(new[] { "attack-history:1", "before-death:" + m.CombatId, "death:" + m.CombatId }, log, "AfterAttack is a guarded dispatch, silent once the combat is ending");
    }

    public static void Test_Pipeline_VulnerableConsultsTheDealersMultiplierRelic()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var phrog = new PhrogDouble();
        phrog.AttachTo(cs.Player);
        cs.Player.Relics.Add(phrog);
        Give<VulnerablePower>(cs, m, 2m, cs.Player);
        Assert.Equal(17.5m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move, null, null));
        PlayerAttack(cs, m, 10m);
        Assert.Equal(33, m.CurrentHp);
        Assert.Equal(10m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move | ValueProp.Unpowered, null, null));
        Give<VulnerablePower>(cs, cs.Player, 1m, m);
        Assert.Equal(15m, Hook.ModifyDamage(cs, cs.Player, m, 10m, ValueProp.Move, null, null), "a monster dealer has no relics");
        phrog.IsMelted = true;
        Assert.Equal(17.5m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move, null, null), "GetRelic does not skip melted relics");
    }

    public static void Test_Pipeline_WeakConsultsTheTargetsMultiplierRelic()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var krane = new KraneDouble();
        krane.AttachTo(cs.Player);
        cs.Player.Relics.Add(krane);
        Give<WeakPower>(cs, m, 1m, cs.Player);
        Assert.Equal(6m, Hook.ModifyDamage(cs, cs.Player, m, 10m, ValueProp.Move, null, null));
        MonsterAttack(cs, m, 10m);
        Assert.Equal(74, cs.Player.CurrentHp);
        Give<WeakPower>(cs, cs.Player, 1m, m);
        Assert.Equal(7.5m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move, null, null), "the target is not a player");
    }

    public static void Test_Pipeline_VulnerableAndWeakApplyRelicThenCrueltyThenDebilitate()
    {
        var cs = NewCombatWithIds(1);
        var m = cs.Monsters[0];
        var phrog = new PhrogDouble();
        phrog.AttachTo(cs.Player);
        cs.Player.Relics.Add(phrog);
        var cruelty = new CrueltyDouble();
        cruelty.ApplyInternal(cs.Player, 25m);
        Give<VulnerablePower>(cs, m, 1m, cs.Player);
        Assert.Equal(20m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move, null, null), "1.5 + 0.25 + 25/100");
        var debilitate = new DebilitateDouble();
        debilitate.ApplyInternal(m, 1m);
        Assert.Equal(30m, Hook.ModifyDamage(cs, m, cs.Player, 10m, ValueProp.Move, null, null), "2.0 + (2.0 - 1)");
        var osty = NewPet(cs, CombatStatus.OstyMonsterId, 5);
        Assert.Equal(17.5m, Hook.ModifyDamage(cs, m, osty, 7m, ValueProp.Move, null, null), "a pet dealer uses its owner's Cruelty and has no relics: 7 x (1.75 + 0.75)");
        var cs2 = NewCombatWithIds(1);
        var m2 = cs2.Monsters[0];
        var krane = new KraneDouble();
        krane.AttachTo(cs2.Player);
        cs2.Player.Relics.Add(krane);
        Give<WeakPower>(cs2, m2, 1m, cs2.Player);
        var weakDebilitate = new DebilitateDouble();
        weakDebilitate.ApplyInternal(m2, 1m);
        Assert.Equal(2m, Hook.ModifyDamage(cs2, cs2.Player, m2, 10m, ValueProp.Move, null, null), "0.75 - 0.15 = 0.6, then 0.6 - (1 - 0.6)");
    }

    public static void Test_Pipeline_ShrinkTicksByParticipantMembership()
    {
        var cs = NewCombatWithIds(2);
        var m = cs.Monsters[0];
        var shrink = Give<ShrinkPower>(cs, m, 2m, cs.Player);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, Array.Empty<CreatureState>());
        Assert.Equal(2, shrink.Amount, "an owner outside the participants does not tick");
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[1] });
        Assert.Equal(2, shrink.Amount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { m });
        Assert.Equal(1, shrink.Amount, "membership alone decides, whatever the side");
        var copy = m.DeepClone();
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { copy });
        Assert.Null(m.GetPower<ShrinkPower>(), "membership is by combat id");
        var onPlayer = Give<ShrinkPower>(cs, cs.Player, 2m, m);
        Assert.True(onPlayer.SkipNextDurationTick);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Player });
        Assert.Equal(1, onPlayer.Amount, "Decrement ignores SkipNextDurationTick");
        var infinite = Give<ShrinkPower>(cs, cs.Monsters[1], -1m, cs.Player);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[1] });
        Assert.Equal(-1, infinite.Amount);
    }

    public static void Test_Pipeline_PilotPowerIdsAndTypes()
    {
        Assert.Equal("HELLRAISER_POWER", new HellraiserPower().Id);
        Assert.Equal("TOOLS_OF_THE_TRADE_POWER", new ToolsOfTheTradePower().Id);
        Assert.Equal("ENTROPY_POWER", new EntropyPower().Id);
        Assert.Equal("DARK_EMBRACE_POWER", new DarkEmbracePower().Id);
        Assert.Equal("FOCUS_POWER", new FocusPower().Id);
        Assert.Equal("DIE_FOR_YOU_POWER", new DieForYouPower().Id);
        Assert.Equal(PowerStackType.Single, new HellraiserPower().StackType);
        Assert.Equal(PowerStackType.Counter, new ToolsOfTheTradePower().StackType);
        Assert.Equal(PowerStackType.Counter, new EntropyPower().StackType);
        Assert.Equal(PowerStackType.Counter, new DarkEmbracePower().StackType);
        Assert.Equal(PowerStackType.Counter, new FocusPower().StackType);
        Assert.True(new FocusPower().AllowNegative);
        Assert.False(new DarkEmbracePower().AllowNegative);
        Assert.Equal(9, HellraiserPower.InfiniteAutoPlayCap);
        foreach (var id in new[] { "HELLRAISER_POWER", "TOOLS_OF_THE_TRADE_POWER", "ENTROPY_POWER", "DARK_EMBRACE_POWER", "FOCUS_POWER", "DIE_FOR_YOU_POWER" })
        {
            Assert.Equal(id, PowerRegistry.Create(id).Id);
            Assert.Equal(PowerType.Buff, PowerRegistry.Create(id).Type);
        }
    }

    public static void Test_Pipeline_HellraiserIgnoresCardsThatAreNotItsOwnersStrikes()
    {
        var cs = NewCombatWithIds(1);
        var hellraiser = Give<HellraiserPower>(cs, cs.Player, 1m, cs.Player);
        Hook.AfterCardDrawn(cs, NewAttackCard(), true);
        Assert.Equal(0, hellraiser.InfiniteAutoPlaysThisTurn);
        var foreign = new CardInstance(new StrikeDouble(), 501);
        foreign.AttachTo(new PlayerState(99, "IRONCLAD", 80, 80, 3));
        Hook.AfterCardDrawn(cs, foreign, true);
        Assert.Equal(0, hellraiser.InfiniteAutoPlaysThisTurn, "another player's Strike is ignored");
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(0, hellraiser.InfiniteAutoPlaysThisTurn);
        var bytes = Canonical(hellraiser);
        var clone = hellraiser.CloneFor(cs.Player);
        Assert.SequenceEqual(bytes, Canonical(clone));
    }

    public static void Test_Pipeline_HellraiserAutoPlaysDrawnStrikes()
    {
        W1Pending.Run(nameof(Test_Pipeline_HellraiserAutoPlaysDrawnStrikes), "loop", () =>
        {
            var cs = NewCombatWithIds(1);
            var hellraiser = Give<HellraiserPower>(cs, cs.Player, 1m, cs.Player);
            var strike = new CardInstance(new StrikeDouble(), 500);
            strike.AttachTo(cs.Player);
            cs.Player.Hand.AddInternal(strike);
            Hook.AfterCardDrawn(cs, strike, true);
            Assert.Equal(0, hellraiser.InfiniteAutoPlaysThisTurn, "a hittable enemy without infinite HP resets the count");
            Assert.False(cs.Player.Hand.Contains(strike), "the drawn Strike was auto-played");
        });
    }

    public static void Test_Pipeline_HellraiserCapsAutoPlaysWithoutHittableEnemiesAtNine()
    {
        W1Pending.Run(nameof(Test_Pipeline_HellraiserCapsAutoPlaysWithoutHittableEnemiesAtNine), "loop", () =>
        {
            var cs = NewCombatWithIds(1);
            var hellraiser = Give<HellraiserPower>(cs, cs.Player, 1m, cs.Player);
            Attach(cs.Monsters[0], new UnhittableOwnerPower());
            for (int i = 0; i < 11; i++)
            {
                var strike = new CardInstance(new StrikeDouble(), 600 + i);
                strike.AttachTo(cs.Player);
                cs.Player.Hand.AddInternal(strike);
                Hook.AfterCardDrawn(cs, strike, true);
            }
            Assert.Equal(11, hellraiser.InfiniteAutoPlaysThisTurn);
            Assert.Equal(2, cs.Player.Hand.Count, "the 10th and 11th Strikes pass the cap of 9 and stay in the hand");
            Hook.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
            Assert.Equal(0, hellraiser.InfiniteAutoPlaysThisTurn);
        });
    }

    public static void Test_Pipeline_ToolsOfTheTradeAddsToItsOwnersHandDraw()
    {
        var cs = NewCombatWithIds(1);
        var tools = Give<ToolsOfTheTradePower>(cs, cs.Player, 2m, cs.Player);
        Assert.Equal(7m, Hook.ModifyHandDraw(cs, cs.Player, 5m));
        var stranger = new PlayerState(99, "IRONCLAD", 80, 80, 3);
        Assert.Equal(5m, Hook.ModifyHandDraw(cs, stranger, 5m));
        tools.AfterPlayerTurnStart(cs, stranger);
        Assert.Equal(0, cs.Player.DiscardPile.Count);
    }

    public static void Test_Pipeline_ToolsOfTheTradePromptsItsOwnerToDiscard()
    {
        W1Pending.Run(nameof(Test_Pipeline_ToolsOfTheTradePromptsItsOwnerToDiscard), "choice", () =>
        {
            var cs = NewCombatWithIds(1);
            Give<ToolsOfTheTradePower>(cs, cs.Player, 1m, cs.Player);
            Hook.AfterPlayerTurnStart(cs, cs.Player);
            Assert.Equal(0, cs.Player.DiscardPile.Count, "an empty hand selects nothing, so nothing is discarded");
        });
    }

    public static void Test_Pipeline_EntropyPromptsItsOwnerToTransform()
    {
        var cs = NewCombatWithIds(1);
        var entropy = Give<EntropyPower>(cs, cs.Player, 1m, cs.Player);
        entropy.AfterPlayerTurnStart(cs, new PlayerState(99, "IRONCLAD", 80, 80, 3));
        W1Pending.Run(nameof(Test_Pipeline_EntropyPromptsItsOwnerToTransform), "choice", () =>
        {
            Hook.AfterPlayerTurnStart(cs, cs.Player);
            Assert.Equal(0, cs.Player.Hand.Count, "an empty hand selects nothing to transform");
        });
    }

    public static void Test_Pipeline_DarkEmbraceDrawsOnExhaustAndAfterEtherealTurnEnd()
    {
        var cs = NewCombatWithIds(1);
        for (int i = 0; i < 5; i++)
        {
            cs.Player.DrawPile.AddInternal(new CardInstance(new SkillDouble(), 700 + i));
        }
        var embrace = Give<DarkEmbracePower>(cs, cs.Player, 2m, cs.Player);
        var exhausted = NewSkillCard();
        Hook.AfterCardExhausted(cs, exhausted, false);
        Assert.Equal(2, cs.Player.Hand.Count);
        Hook.AfterCardExhausted(cs, exhausted, true);
        Hook.AfterCardExhausted(cs, exhausted, true);
        Assert.Equal(2, cs.Player.Hand.Count, "ethereal exhausts are counted, not drawn");
        Assert.Equal(2, embrace.EtherealCount);
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[0] });
        Assert.Equal(2, embrace.EtherealCount, "only a turn end its owner takes part in draws");
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Equal(5, cs.Player.Hand.Count, "2 x 2 requested, 3 left in the draw pile");
        Assert.Equal(0, embrace.EtherealCount);
        var clone = embrace.CloneFor(cs.Player);
        Assert.SequenceEqual(Canonical(embrace), Canonical(clone));
    }

    public static void Test_Pipeline_FocusModifiesOnlyItsOwnersOrbValues()
    {
        var cs = NewCombatWithIds(1);
        var focus = Give<FocusPower>(cs, cs.Player, 2m, cs.Player);
        var orb = new OrbDouble();
        orb.AttachTo(cs.Player);
        Assert.Equal(5m, focus.ModifyOrbValue(cs, orb, 3m));
        var foreign = new OrbDouble();
        foreign.AttachTo(new PlayerState(99, "DEFECT", 70, 70, 3));
        Assert.Equal(3m, focus.ModifyOrbValue(cs, foreign, 3m));
        Assert.Equal(3m, focus.ModifyOrbValue(cs, new OrbDouble(), 3m));
        PowerCmd.ModifyAmount(cs, focus, -7m, null, null);
        Assert.Equal(-5, focus.Amount);
        Assert.Equal(0m, focus.ModifyOrbValue(cs, orb, 3m), "Max(value + Amount, 0)");
        W1Pending.Run(nameof(Test_Pipeline_FocusModifiesOnlyItsOwnersOrbValues), "hooks", () =>
        {
            Assert.Equal(0m, Hook.ModifyOrbValue(cs, orb, 3m));
        });
    }

    public static void Test_Pipeline_TemporaryStrengthAppliesAndRevertsItsInternalPower()
    {
        var cs = NewCombatWithIds(1);
        var log = new List<string>();
        Attach(cs.Player, new ContextSpyPower(log));
        var temp = Assert.NotNull(PowerCmd.Apply<TemporaryStrengthDouble>(cs, cs.Player, 2m, cs.Player, null));
        ITemporaryPower marker = temp;
        Assert.Equal("STRENGTH_POWER", marker.InternallyAppliedPowerId);
        Assert.Equal("PIPELINE_TEST_ORIGIN", marker.OriginModelId);
        Assert.Equal(PowerType.Buff, temp.Type);
        Assert.Equal(2, cs.Player.GetPowerAmount<StrengthPower>());
        Assert.Equal(2, temp.Amount);
        Assert.SequenceEqual(new[] { "power:STRENGTH_POWER:Throwing", "power:PIPELINE_TEMPORARY_STRENGTH_POWER:none" }, log);
        PowerCmd.Apply<TemporaryStrengthDouble>(cs, cs.Player, 1m, cs.Player, null);
        Assert.Equal(3, temp.Amount);
        Assert.Equal(3, cs.Player.GetPowerAmount<StrengthPower>());
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[0] });
        Assert.Equal(3, cs.Player.GetPowerAmount<StrengthPower>());
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, new CreatureState[] { cs.Player });
        Assert.Null(cs.Player.GetPower<TemporaryStrengthDouble>());
        Assert.Null(cs.Player.GetPower<StrengthPower>(), "reverting to 0 removes the Strength");
        var down = Assert.NotNull(PowerCmd.Apply<TemporaryStrengthDownDouble>(cs, cs.Monsters[0], 2m, cs.Player, null));
        Assert.Equal(PowerType.Debuff, down.Type);
        Assert.Equal(-2, cs.Monsters[0].GetPowerAmount<StrengthPower>());
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, new CreatureState[] { cs.Monsters[0] });
        Assert.Null(cs.Monsters[0].GetPower<StrengthPower>());
        Assert.Equal(0, cs.Choice.ContextDepth);
    }

    public static void Test_Pipeline_ApplyToTargetsReturnsTheAppliedPowers()
    {
        var cs = NewCombatWithIds(2);
        var detached = new MonsterState(cs.AllocateCreatureId(), new MonsterDouble(), 50, 50, null, null);
        var powers = PowerCmd.Apply<WeakPower>(cs, new CreatureState[] { cs.Monsters[0], detached, cs.Monsters[1] }, 1m, cs.Player, null);
        Assert.Equal(2, powers.Count);
        Assert.True(ReferenceEquals(cs.Monsters[0], powers[0].Owner));
        Assert.True(ReferenceEquals(cs.Monsters[1], powers[1].Owner));
        Assert.Equal(0, PowerCmd.Apply<WeakPower>(cs, (IReadOnlyList<CreatureState>?)null, 1m, cs.Player, null).Count);
    }

    public static void Test_Pipeline_DamageVarAndBlockVarOverloadsForwardValueAndProps()
    {
        var cs = NewCombatWithIds(2);
        var m = cs.Monsters[0];
        var card = NewAttackCard();
        var results = CreatureCmd.Damage(cs, m, new DamageVar(7m, ValueProp.Move), card, null);
        Assert.Equal(43, m.CurrentHp);
        Assert.Equal(ValueProp.Move, results[0].Props);
        Assert.Equal(cs.Player.CombatId, cs.History.DamageReceivedEntries[0].DealerId);
        CreatureCmd.Damage(cs, cs.Monsters, new DamageVar(2m, ValueProp.Unpowered), cs.Player);
        Assert.Equal(41, m.CurrentHp);
        Assert.Equal(48, cs.Monsters[1].CurrentHp);
        CreatureCmd.Damage(cs, m, new DamageVar(1m, ValueProp.Unblockable), cs.Player);
        Assert.Equal(40, m.CurrentHp);
        Assert.Equal(5m, CreatureCmd.GainBlock(cs, cs.Player, new BlockVar(5m, ValueProp.Move), null));
        Assert.Equal(5, cs.Player.Block);
        Assert.Equal(0, CreatureCmd.Damage(cs, (IReadOnlyList<CreatureState>?)null, 5m, ValueProp.Move, cs.Player, null, null).Count);
    }

    public static void Test_Pipeline_CloneAndHashAreStableAcrossPipelineOperations()
    {
        var cs = NewCombatWithIds(3);
        var card = NewAttackCard();
        var steps = new List<Action<CombatState>>
        {
            s => PowerCmd.Apply<StrengthPower>(s, s.Player, 2m, s.Player, null),
            s => PowerCmd.Apply<VulnerablePower>(s, s.Monsters[0], 2m, s.Player, null),
            s => DamageCmd.Attack(s, 6m).FromCard(card, null).Targeting(s.Monsters[0]).Execute(),
            s => DamageCmd.Attack(s, 3m).FromCard(card, null).TargetingAllOpponents().WithHitCount(2).Execute(),
            s => DamageCmd.Attack(s, 2m).FromCard(card, null).TargetingRandomOpponents().WithHitCount(3).Execute(),
            s => CreatureCmd.GainBlock(s, s.Player, 5m, ValueProp.Move, null),
            s => DamageCmd.Attack(s, 8m).FromMonster(s.Monsters[1]).Execute(),
            s => PowerCmd.Apply<WeakPower>(s, s.Player, 1m, s.Monsters[1], null),
            s => PowerCmd.TickDownDuration(s, s.Monsters[0].GetPower<VulnerablePower>()!),
            s => CreatureCmd.Heal(s, s.Player, 3m),
            s => EnergyCmd.SpendEnergy(s, card, 1),
            s => CreatureCmd.Kill(s, s.Monsters[2])
        };
        var twin = NewCombatWithIds(3);
        foreach (var step in steps)
        {
            step(cs);
            step(twin);
            ulong hash = StateHasher.Hash(cs);
            Assert.Equal(hash, StateHasher.Hash(twin), "the same operations give the same hash");
            var clone = cs.DeepClone();
            Assert.Equal(hash, StateHasher.Hash(clone));
            Assert.Equal(cs.History.Count, clone.History.Count);
            DamageCmd.Attack(clone, 1m).FromCard(card, null).Targeting(clone.Player).Execute();
            Assert.NotEqual(hash, StateHasher.Hash(clone));
            Assert.Equal(hash, StateHasher.Hash(cs), "stepping the clone leaves the source unchanged");
        }
        Assert.True(cs.History.Count > 10);
    }
}
