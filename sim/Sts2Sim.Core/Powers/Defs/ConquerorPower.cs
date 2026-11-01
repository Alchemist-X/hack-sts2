namespace Sts2Sim.Core.Powers;

public sealed class ConquerorPower : PowerModel
{
    public const string PowerId = "CONQUEROR_POWER";

    public const decimal SovereignBladeMultiplier = 2m;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (cardSource == null || !string.Equals(cardSource.Id, ForgeCmd.SovereignBladeId, StringComparison.Ordinal))
        {
            return 1m;
        }
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        if (!ReferenceEquals(target, Owner))
        {
            return 1m;
        }
        return SovereignBladeMultiplier;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
