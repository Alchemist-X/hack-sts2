namespace Sts2Sim.Core.Relics;

public sealed class SneckoSkull : RelicModel
{
    public const string RelicId = "SNECKO_SKULL";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, 1m)
    };

    public override decimal ModifyPowerAmountGivenAdditive(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource)
    {
        if (power.Id != PoisonPowerId)
        {
            return 0m;
        }
        if (!ReferenceEquals(giver, Owner))
        {
            return 0m;
        }
        return DynamicVars.Poison.BaseValue;
    }

    public override void AfterModifyingPowerAmountGiven(CombatState cs, PowerModel power)
    {
    }
}
