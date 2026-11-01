namespace Sts2Sim.Core.Relics;

public sealed class PaperPhrog : RelicModel, IVulnerableMultiplierModifier
{
    public const string RelicId = "PAPER_PHROG";

    public const decimal VulnerableMultiplierBonus = 0.25m;

    public override string Id => RelicId;

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
        return amount + VulnerableMultiplierBonus;
    }
}
