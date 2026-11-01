namespace Sts2Sim.Core.Cards;

public sealed class HiddenDaggers : CardDef
{
    public const string CardId = "HIDDEN_DAGGERS";
    public const int Cost = 0;
    public const int BaseCards = 2;
    public const string ShivKey = "Shivs";
    public const decimal BaseShivs = 2m;
    public const string ShivId = "SHIV";

    public HiddenDaggers()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    private static IReadOnlyList<CardInstance> CreateShivsInHand(CombatState cs, PlayerState owner, int count)
    {
        if (count == 0)
        {
            return Array.Empty<CardInstance>();
        }
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return Array.Empty<CardInstance>();
        }
        var shivs = new List<CardInstance>(count);
        for (int i = 0; i < count; i++)
        {
            shivs.Add(cs.CreateCard(ShivId, owner));
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
        return shivs;
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards),
        new DynamicVar(ShivKey, BaseShivs)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.DiscardSelectionPrompt, card.Vars.Cards.IntValue);
        var cards = CardSelectCmd.FromHandForDiscard(cs, owner, prefs, null, ChoiceSource.For(card, "OnPlay")).ToList();
        CardCmd.Discard(cs, cards);
        var shivs = CreateShivsInHand(cs, owner, card.Vars[ShivKey].IntValue);
        if (card.IsUpgraded)
        {
            for (int i = 0; i < shivs.Count; i++)
            {
                CardCmd.Upgrade(cs, shivs[i]);
            }
        }
    }
}
