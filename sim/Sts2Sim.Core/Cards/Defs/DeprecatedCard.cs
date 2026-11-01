using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DeprecatedCard : CardDef
{
    public const string CardId = "DEPRECATED_CARD";
    public const int Cost = 0;
    public const decimal DrawCount = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public DeprecatedCard()
        : base(CardId, Cost, CardType.Status, CardRarity.Status, TargetType.None)
    {
    }

    public override int MaxUpgradeLevel => 0;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CardPileCmd.Draw(cs, DrawCount, card.Owner);
        var deckVersion = card.FindDeckVersion();
        if (deckVersion != null)
        {
            CardPileCmd.RemoveFromDeck(cs, deckVersion);
            card.DeckVersionId = CardInstance.NoDeckVersion;
        }
    }
}
