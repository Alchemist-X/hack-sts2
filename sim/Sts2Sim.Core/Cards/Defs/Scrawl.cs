using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Scrawl : CardDef
{
    public const string CardId = "SCRAWL";
    public const int Cost = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Scrawl()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var owner = play.Card.Owner;
        int num = CardPile.MaxCardsInHand - owner.Hand.Cards.Count;
        CardPileCmd.Draw(cs, num, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
