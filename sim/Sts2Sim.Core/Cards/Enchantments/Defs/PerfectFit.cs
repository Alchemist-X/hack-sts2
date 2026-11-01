namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class PerfectFit : EnchantmentModel
{
    public const string EnchantmentId = "PERFECT_FIT";

    public override string Id => EnchantmentId;

    public override void ModifyShuffleOrder(CombatState cs, PlayerState player, List<CardInstance> cards, bool isInitialShuffle)
    {
        var card = Card!;
        if (!isInitialShuffle && cards.Contains(card))
        {
            cards.Remove(card);
            cards.Insert(0, card);
        }
    }
}
