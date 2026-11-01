namespace Sts2Sim.Core.Powers;

public sealed class SlipperyPower : PowerModel
{
    public const string PowerId = "SLIPPERY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override decimal ModifyHpLostAfterOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (amount < 1m)
        {
            return amount;
        }
        return 1m;
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage < 1)
        {
            return;
        }
        PowerCmd.Decrement(rs.Combat!, this);
    }

    public override decimal GetScaledAmountForMultiplayer(CombatState cs, CreatureState? applier, decimal amount, CreatureState target, CardInstance? cardSource)
        => amount * CombatStatus.PlayerCount(cs);
}
