namespace Sts2Sim.Core.Relics;

public sealed class SpikedGauntlets : RelicModel
{
    public const string RelicId = "SPIKED_GAUNTLETS";

    public const decimal PowerCostIncrease = 1m;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(1)
    };

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + DynamicVars.Energy.IntValue;
    }

    public override bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return false;
        }
        if (card.Type != CardType.Power)
        {
            return false;
        }
        modifiedCost = originalCost + PowerCostIncrease;
        return true;
    }
}
