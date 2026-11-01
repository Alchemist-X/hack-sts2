namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Instinct : EnchantmentModel
{
    public const string EnchantmentId = "INSTINCT";
    public const decimal DamageMultiplier = 2m;

    public override string Id => EnchantmentId;

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Attack;

    public override decimal EnchantDamageMultiplicative(decimal originalDamage, ValueProp props)
    {
        if (!props.IsPoweredAttack())
        {
            return 1m;
        }
        return DamageMultiplier;
    }
}
