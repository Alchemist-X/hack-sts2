namespace Sts2Sim.Core.Relics;

public sealed class JeweledMask : RelicModel
{
    public const string RelicId = "JEWELED_MASK";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner) || Owner.TurnNumber > 1)
        {
            return;
        }
        var powers = player.DrawPile.Cards.Where(c => c.Type == CardType.Power).ToList();
        if (powers.Count == 0)
        {
            return;
        }
        var nonInnate = powers.Where(c => !c.Keywords(cs).Contains(CardKeyword.Innate)).ToList();
        if (nonInnate.Count > 0)
        {
            powers = nonInnate;
        }
        var card = cs.Rng.CombatCardSelection.NextItem(powers)!;
        card.SetToFreeThisTurn();
        CardPileCmd.Add(cs, card, PileType.Hand);
    }
}
