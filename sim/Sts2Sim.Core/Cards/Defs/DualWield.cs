namespace Sts2Sim.Core.Cards;

public sealed class DualWield : CardDef
{
    public const string CardId = "DUAL_WIELD";
    public const int Cost = 1;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;
    public const int SelectCount = 1;

    public DualWield()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override CardPool VisualCardPool => CardPools.Ironclad;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(card.Def.SelectionScreenPromptKey, SelectCount);
        var selection = CardSelectCmd.FromHand(cs, owner, prefs, static c => c.Type == CardType.Attack || c.Type == CardType.Power, ChoiceSource.For(card, "OnPlay")).FirstOrDefault();
        if (selection == null)
        {
            return;
        }
        for (int i = 0; i < card.Vars.Cards.IntValue; i++)
        {
            var clone = cs.CloneCard(selection);
            clone.ExhaustOnNextPlay = false;
            CardPileCmd.AddGeneratedCardToCombat(cs, clone, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
