namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Goopy : EnchantmentModel
{
    public const string EnchantmentId = "GOOPY";

    public override string Id => EnchantmentId;

    public override bool CanEnchant(CardInstance card)
    {
        if (base.CanEnchant(card))
        {
            return card.HasTag(CardTag.Defend);
        }
        return false;
    }

    protected override void OnEnchant()
    {
        Card!.AddKeyword(CardKeyword.Exhaust);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(cardPlay.Card, Card))
        {
            return;
        }
        Amount++;
        var deckVersion = Card!.FindDeckVersion();
        if (deckVersion != null)
        {
            deckVersion.Enchantment!.Amount++;
        }
    }

    public override decimal EnchantBlockAdditive(decimal originalBlock) => Amount - 1;
}
