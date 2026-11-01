namespace Sts2Sim.Core.Relics;

public sealed class GamePiece : RelicModel
{
    public const string RelicId = "GAME_PIECE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1)
    };

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.HasOwner && ReferenceEquals(card.Owner, Owner) && cs.IsInProgress && card.Type == CardType.Power)
        {
            CardPileCmd.Draw(cs, DynamicVars.Cards.BaseValue, Owner);
        }
    }
}
