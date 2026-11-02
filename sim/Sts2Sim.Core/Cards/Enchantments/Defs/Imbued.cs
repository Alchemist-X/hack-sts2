namespace Sts2Sim.Core.Cards.Enchantments;

public sealed class Imbued : EnchantmentModel
{
    public const string EnchantmentId = "IMBUED";
    public const int LastAutoPlayTurn = 1;

    public override string Id => EnchantmentId;

    public override bool ShouldStartAtBottomOfDrawPile => true;

    public override bool CanEnchantCardType(CardType cardType) => cardType == CardType.Skill;

    public override void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
        var card = Card!;
        if (ReferenceEquals(player, card.Owner) && player.TurnNumber <= LastAutoPlayTurn)
        {
            CardCmd.AutoPlay(cs, card, null);
        }
    }
}
