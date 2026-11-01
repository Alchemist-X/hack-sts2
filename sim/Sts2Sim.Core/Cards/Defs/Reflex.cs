using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Reflex : CardDef
{
    public const string CardId = "REFLEX";
    public const int Cost = 3;
    public const int BaseCards = 2;
    public const decimal CardsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Sly });

    public Reflex()
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
        var reflex = play.Card;
        CardPileCmd.Draw(cs, reflex.Vars.Cards.BaseValue, reflex.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
