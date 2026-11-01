using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Seance : CardDef
{
    public const string CardId = "SEANCE";
    public const int Cost = 1;
    public const int BaseCards = 1;
    public const int CostUpgrade = -1;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Ethereal });

    public Seance()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var seance = play.Card;
        var owner = seance.Owner;
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, seance.Vars.Cards.IntValue);
        var selection = CardSelectCmd.FromCombatPile(cs, owner.DrawPile, owner, prefs, ChoiceSource.For(seance, "OnPlay")).ToList();
        var soul = CardRegistry.Get(Soul.CardId);
        for (int i = 0; i < selection.Count; i++)
        {
            CardCmd.TransformTo(cs, selection[i], soul);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
