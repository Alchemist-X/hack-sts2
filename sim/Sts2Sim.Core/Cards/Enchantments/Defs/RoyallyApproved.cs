namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class RoyallyApproved : EnchantmentModel
{
    public const string EnchantmentId = "ROYALLY_APPROVED";

    public override string Id => EnchantmentId;

    public override bool CanEnchantCardType(CardType cardType)
    {
        if ((uint)(cardType - 1) <= 1u)
        {
            return true;
        }
        return false;
    }

    protected override void OnEnchant()
    {
        Card!.AddKeyword(CardKeyword.Innate);
        Card!.AddKeyword(CardKeyword.Retain);
    }
}
