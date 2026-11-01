namespace Sts2Sim.Core.Relics;

public sealed class PaperKrane : RelicModel, IWeakMultiplierModifier
{
    public const string RelicId = "PAPER_KRANE";

    public const decimal WeakMultiplierReduction = 0.15m;

    public override string Id => RelicId;

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
        return amount - WeakMultiplierReduction;
    }
}
