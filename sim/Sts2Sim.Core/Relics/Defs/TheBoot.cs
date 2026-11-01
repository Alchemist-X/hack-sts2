namespace Sts2Sim.Core.Relics;

public sealed class TheBoot : RelicModel
{
    public const string RelicId = "THE_BOOT";

    public const int DamageMinimum = 5;

    public const string DamageMinimumKey = "DamageMinimum";

    public const string DamageThresholdKey = "DamageThreshold";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DamageMinimumKey, 5m),
        new DynamicVar(DamageThresholdKey, 4m)
    };

    public override decimal ModifyHpLostAfterOstyLate(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var osty = rs.Combat == null ? null : Owner.GetOsty(rs.Combat);
        if (!ReferenceEquals(dealer, Owner) && !ReferenceEquals(dealer, osty))
        {
            return amount;
        }
        if (ReferenceEquals(target, Owner))
        {
            return amount;
        }
        if (!props.IsPoweredAttack())
        {
            return amount;
        }
        if (amount < 1m)
        {
            return amount;
        }
        if (amount >= DynamicVars[DamageMinimumKey].BaseValue)
        {
            return amount;
        }
        return DynamicVars[DamageMinimumKey].BaseValue;
    }
}
