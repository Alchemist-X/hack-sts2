namespace Sts2Sim.Core.Powers;

public sealed class FlutterPower : PowerModel
{
    public const string PowerId = "FLUTTER_POWER";

    public const string ThievingHopperId = "THIEVING_HOPPER";

    public const decimal DamageDecrease = 50m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return DamageDecrease / 100m;
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage == 0 || !props.IsPoweredAttack())
        {
            return;
        }
        var cs = rs.Combat!;
        PowerCmd.Decrement(cs, this);
        if (Amount > 0)
        {
            return;
        }
        var monster = (MonsterState)Owner;
        var ai = monster.Ai;
        var log = ai.StateLog;
        if (log.Count == 0)
        {
            throw new InvalidOperationException("Sequence contains no elements");
        }
        string? nextState = ai.Machine.Get(log[log.Count - 1]).GetNextState(cs, monster, cs.Rng.MonsterAi);
        CreatureCmd.StunWithMove(cs, monster, StunnedMove, nextState);
        var hopper = MonsterPowerCoupling.Require(monster, ThievingHopperId, PowerId);
        MonsterPowerCoupling.SetFlag(hopper, "IsHovering", false, PowerId);
    }

    private static void StunnedMove(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
    {
    }
}
