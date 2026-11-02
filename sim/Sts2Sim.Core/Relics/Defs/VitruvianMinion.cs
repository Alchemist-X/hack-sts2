namespace Sts2Sim.Core.Relics;

public sealed class VitruvianMinion : RelicModel
{
    public const string RelicId = "VITRUVIAN_MINION";

    public const decimal MinionMultiplier = 2m;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override decimal ModifyDamageMultiplicative(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (cardSource == null)
        {
            return 1m;
        }
        if (!(cardSource.HasOwner && ReferenceEquals(cardSource.Owner, Owner)))
        {
            return 1m;
        }
        if (!cardSource.HasTag(CardTag.Minion))
        {
            return 1m;
        }
        return MinionMultiplier;
    }

    public override decimal ModifyBlockMultiplicative(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (cardSource == null)
        {
            return 1m;
        }
        if (!(cardSource.HasOwner && ReferenceEquals(cardSource.Owner, Owner)))
        {
            return 1m;
        }
        if (!cardSource.HasTag(CardTag.Minion))
        {
            return 1m;
        }
        return MinionMultiplier;
    }
}
