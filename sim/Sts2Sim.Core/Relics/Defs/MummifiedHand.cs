namespace Sts2Sim.Core.Relics;

public sealed class MummifiedHand : RelicModel
{
    public const string RelicId = "MUMMIFIED_HAND";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        var played = cardPlay.Card;
        if (!played.HasOwner || !ReferenceEquals(played.Owner, Owner))
        {
            return;
        }
        if (played.Type != CardType.Power)
        {
            return;
        }
        var rng = cs.Rng.CombatCardSelection;
        var cards = Owner.Hand.Cards.ToList();
        var costed = cards.Where(c => c.EnergyCost.GetWithModifiers(CostModifiers.None) > 0 || c.BaseStarCost > 0).ToList();
        var card = rng.NextItem(costed.Where(c => c.CostsEnergyOrStars(cs, includeGlobalModifiers: true)));
        if (card == null)
        {
            card = rng.NextItem(cards.Where(c => c.CostsEnergyOrStars(cs, includeGlobalModifiers: true)));
        }
        if (card == null)
        {
            card = rng.NextItem(costed);
        }
        if (card == null)
        {
            card = rng.NextItem(cards);
        }
        card?.SetToFreeThisTurn();
    }
}
