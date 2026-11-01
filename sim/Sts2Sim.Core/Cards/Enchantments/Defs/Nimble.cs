namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Nimble : EnchantmentModel
{
    public const string EnchantmentId = "NIMBLE";

    public override string Id => EnchantmentId;

    public override bool CanEnchant(CardInstance card)
    {
        if (base.CanEnchant(card))
        {
            return card.GainsBlock;
        }
        return false;
    }

    public override decimal EnchantBlockAdditive(decimal originalBlock) => Amount;
}
