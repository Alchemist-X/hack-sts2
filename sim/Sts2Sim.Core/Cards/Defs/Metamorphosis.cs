using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Metamorphosis : CardDef
{
    public const string CardId = "METAMORPHOSIS";
    public const int Cost = 2;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 2m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Metamorphosis()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var metamorphosis = play.Card;
        var owner = metamorphosis.Owner;
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(owner.CharacterId);
        var options = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Type == CardType.Attack);
        var forCombat = CardFactory.GetForCombat(cs, owner, options, metamorphosis.Vars.Cards.IntValue, rs.Rng.CombatCardGeneration);
        foreach (var current in forCombat)
        {
            current.SetToFreeThisCombat();
            CardPileCmd.AddGeneratedCardToCombat(cs, current, PileType.Draw, owner, CardPilePosition.Random);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }
}
