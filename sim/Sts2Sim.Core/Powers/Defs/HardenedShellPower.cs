namespace Sts2Sim.Core.Powers;

public sealed class HardenedShellPower : PowerModel
{
    public const string PowerId = "HARDENED_SHELL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public decimal DamageReceivedThisTurn { get; private set; }

    public override decimal ModifyHpLostBeforeOstyLate(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (amount == 0m)
        {
            return amount;
        }
        return Math.Min(amount, Amount - DamageReceivedThisTurn);
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return;
        }
        if (result.WasFullyBlocked)
        {
            return;
        }
        DamageReceivedThisTurn += result.UnblockedDamage;
    }

    public override void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        DamageReceivedThisTurn = 0m;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(DamageReceivedThisTurn);
    }
}
