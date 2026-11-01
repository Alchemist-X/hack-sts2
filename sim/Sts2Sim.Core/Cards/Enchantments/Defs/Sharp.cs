namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Sharp : EnchantmentModel
{
    public const string EnchantmentId = "SHARP";

    public override string Id => EnchantmentId;

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Attack;

    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
    {
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return Amount;
    }
}
