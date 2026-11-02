using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class JackOfAllTrades : CardDef
{
    public const string CardId = "JACK_OF_ALL_TRADES";
    public const int Cost = 0;
    public const int BaseCards = 1;
    public const decimal CardsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public JackOfAllTrades()
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
        var jackOfAllTrades = play.Card;
        var owner = jackOfAllTrades.Owner;
        var rs = cs.Run;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c is not JackOfAllTrades);
        var distinctForCombat = CardFactory.GetDistinctForCombat(cs, owner, options, jackOfAllTrades.Vars.Cards.IntValue, rs.Rng.CombatCardGeneration).ToList();
        foreach (var current in distinctForCombat)
        {
            CardPileCmd.AddGeneratedCardToCombat(cs, current, PileType.Hand, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
