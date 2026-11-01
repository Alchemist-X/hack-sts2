using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class ThinkingAhead : CardDef
{
    public const string CardId = "THINKING_AHEAD";
    public const int Cost = 0;
    public const int BaseCards = 2;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public ThinkingAhead()
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
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Draw, CardPilePosition.Top);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.RemoveKeyword(CardKeyword.Exhaust);
    }
}
