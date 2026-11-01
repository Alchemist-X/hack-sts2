using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class DecisionsDecisions : CardDef
{
    public const string CardId = "DECISIONS_DECISIONS";
    public const int Cost = 0;
    public const int StarCost = 6;
    public const int BaseCards = 3;
    public const int BaseRepeat = 3;
    public const decimal CardsUpgrade = 2m;
    public const int SelectCount = 1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public DecisionsDecisions()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var decisionsDecisions = play.Card;
        var owner = decisionsDecisions.Owner;
        CardPileCmd.Draw(cs, decisionsDecisions.Vars.Cards.IntValue, owner);
        var prefs = new CardSelectorPrefs(SelectionScreenPromptKey, SelectCount);
        var card = CardSelectCmd.FromHand(cs, owner, prefs, c => c.Type == CardType.Skill && !c.HasKeywordInCombat(cs, CardKeyword.Unplayable), ChoiceSource.For(decisionsDecisions, "OnPlay")).FirstOrDefault();
        if (card == null)
        {
            return;
        }
        for (int i = 0; i < decisionsDecisions.Vars.Repeat.IntValue; i++)
        {
            CardCmd.AutoPlay(cs, card, null);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
