using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Wish : CardDef
{
    public const string CardId = "WISH";
    public const int Cost = 0;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Wish()
        : base(CardId, Cost, CardType.Skill, CardRarity.Ancient, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var cardModel = CardSelectCmd.FromCombatPile(cs, owner.DrawPile, owner, prefs, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Hand);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.AddKeyword(CardKeyword.Retain);
    }
}
