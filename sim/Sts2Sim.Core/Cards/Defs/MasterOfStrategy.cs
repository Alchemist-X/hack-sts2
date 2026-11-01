using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class MasterOfStrategy : CardDef
{
    public const string CardId = "MASTER_OF_STRATEGY";
    public const int Cost = 0;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public MasterOfStrategy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, card.Owner);
    }
}
