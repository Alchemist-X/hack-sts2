namespace Sts2Sim.Core.Relics;

public sealed class TungstenRod : RelicModel
{
    public const string RelicId = "TUNGSTEN_ROD";

    public const string HpLossReductionKey = "HpLossReduction";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(HpLossReductionKey, 1m)
    };

    public override decimal ModifyHpLostAfterOsty(RunState rs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        if (!ReferenceEquals(target, Owner))
        {
            return amount;
        }
        return Math.Max(0m, amount - DynamicVars[HpLossReductionKey].BaseValue);
    }
}
