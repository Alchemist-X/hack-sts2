namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Slither : EnchantmentModel
{
    public const string EnchantmentId = "SLITHER";
    public const int EnergyCostRange = 4;

    public override string Id => EnchantmentId;

    public override bool CanEnchant(CardInstance card)
    {
        if (base.CanEnchant(card) && !card.Keywords(null).Contains(CardKeyword.Unplayable))
        {
            return !card.EnergyCost.CostsX;
        }
        return false;
    }

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(card, Card))
        {
            return;
        }
        if (card.GetCombatPile(cs)?.Type != PileType.Hand)
        {
            return;
        }
        card.EnergyCost.SetThisCombat(NextEnergyCost(cs));
    }

    private static int NextEnergyCost(CombatState cs) => cs.Rng.CombatEnergyCosts.NextInt(EnergyCostRange);
}
