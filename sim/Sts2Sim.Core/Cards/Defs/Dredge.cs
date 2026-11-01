using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Dredge : CardDef
{
    public const string CardId = "DREDGE";
    public const int Cost = 1;
    public const int BaseCards = 3;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Dredge()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        int count = Math.Min(card.Vars.Cards.IntValue, CardPile.MaxCardsInHand - owner.Hand.Cards.Count);
        if (count > 0)
        {
            var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, count);
            var cards = CardSelectCmd.FromCombatPile(cs, owner.DiscardPile, owner, prefs, ChoiceSource.For(card, "OnPlay")).ToList();
            CardPileCmd.Add(cs, cards, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
