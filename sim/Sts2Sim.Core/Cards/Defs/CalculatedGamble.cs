using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class CalculatedGamble : CardDef
{
    public const string CardId = "CALCULATED_GAMBLE";
    public const int Cost = 0;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public CalculatedGamble()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var calculatedGamble = play.Card;
        var cards = calculatedGamble.Owner.Hand.Cards;
        int cardsToDraw = cards.Count;
        CardCmd.DiscardAndDraw(cs, cards, cardsToDraw);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
