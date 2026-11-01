namespace Sts2Sim.Core.Cards;

public sealed class Charge : CardDef
{
    public const string CardId = "CHARGE";
    public const int Cost = 1;
    public const int BaseCards = 2;
    public const string MinionDiveBombId = "MINION_DIVE_BOMB";

    public Charge()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, card.Vars.Cards.IntValue);
        var selection = CardSelectCmd.FromCombatPile(cs, owner.DrawPile, owner, prefs, ChoiceSource.For(card, "OnPlay")).ToList();
        for (int i = 0; i < selection.Count; i++)
        {
            var result = CardCmd.TransformTo(cs, selection[i], MinionDiveBombId);
            if (card.IsUpgraded && result.HasValue)
            {
                CardCmd.Upgrade(cs, result.Value.CardAdded);
            }
        }
    }
}
