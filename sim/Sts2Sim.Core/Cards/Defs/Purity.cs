using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Purity : CardDef
{
    public const string CardId = "PURITY";
    public const int Cost = 0;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 2m;
    public const int MinSelect = 0;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Retain, CardKeyword.Exhaust });

    public Purity()
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
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, MinSelect, card.Vars.Cards.IntValue);
        var selection = CardSelectCmd.FromHand(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        for (int i = 0; i < selection.Count; i++)
        {
            CardCmd.Exhaust(cs, selection[i]);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
