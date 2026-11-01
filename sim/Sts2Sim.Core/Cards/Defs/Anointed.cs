using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Anointed : CardDef
{
    public const string CardId = "ANOINTED";
    public const int Cost = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Anointed()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        int count = CardPile.MaxCardsInHand - owner.Hand.Cards.Count;
        var cards = owner.DrawPile.Cards.Where(c => c.Rarity == CardRarity.Rare).TakeRandom(count, cs.Rng.CombatCardSelection).ToList();
        CardPileCmd.Add(cs, cards, PileType.Hand);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
