namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Corrupted : EnchantmentModel
{
    public const string EnchantmentId = "CORRUPTED";
    public const decimal DamageAmount = 2m;
    public const decimal DamageMultiplier = 1.5m;

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

    public override void OnPlay(CombatState cs, CardPlay? cardPlay)
    {
        var card = Card!;
        CreatureCmd.Damage(cs, card.Owner, DamageAmount, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, cardPlay);
    }
}
