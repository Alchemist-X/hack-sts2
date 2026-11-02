namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Vigorous : EnchantmentModel
{
    public const string EnchantmentId = "VIGOROUS";

    public override string Id => EnchantmentId;

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Attack;

    public override decimal EnchantDamageAdditive(decimal originalDamage, ValueProp props)
    {
        if (Status != EnchantmentStatus.Normal)
        {
            return 0m;
        }
        if (!props.IsPoweredAttack())
        {
            return 0m;
        }
        return Amount;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(cardPlay.Card, Card))
        {
            return;
        }
        Status = EnchantmentStatus.Disabled;
    }
}
