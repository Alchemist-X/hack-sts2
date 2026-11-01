namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class SoulsPower : EnchantmentModel
{
    public const string EnchantmentId = "SOULS_POWER";

    public override string Id => EnchantmentId;

    public override bool CanEnchant(CardInstance card)
    {
        if (!base.CanEnchant(card))
        {
            return false;
        }
        if (!card.GetKeywordsWithSources(null, KeywordSources.Local).Contains(CardKeyword.Exhaust))
        {
            return false;
        }
        return true;
    }

    protected override void OnEnchant()
    {
        CardCmd.RemoveKeyword(Card!, CardKeyword.Exhaust);
    }
}
