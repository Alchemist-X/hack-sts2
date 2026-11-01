using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BundleOfJoy : CardDef
{
    public const string CardId = "BUNDLE_OF_JOY";
    public const int Cost = 1;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public BundleOfJoy()
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
        var bundleOfJoy = play.Card;
        var owner = bundleOfJoy.Owner;
        var rs = cs.Run;
        var options = CardPools.Colorless.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        var distinctForCombat = CardFactory.GetDistinctForCombat(cs, owner, options, bundleOfJoy.Vars.Cards.IntValue, rs.Rng.CombatCardGeneration);
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
