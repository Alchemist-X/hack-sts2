namespace Sts2Sim.Core.Relics;

public sealed class MysticLighter : RelicModel
{
    public const string RelicId = "MYSTIC_LIGHTER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DamageVar(9m, ValueProp.Unpowered)
    };

    public override decimal ModifyDamageAdditive(RunState rs, CreatureState? target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        if (cardSource?.Enchantment == null)
        {
            return 0m;
        }
        if (!cardSource.HasOwner || !ReferenceEquals(cardSource.Owner, Owner))
        {
            return 0m;
        }
        return DynamicVars.Damage.IntValue;
    }
}
