using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class BladeDance : CardDef
{
    public const string CardId = "BLADE_DANCE";
    public const int Cost = 1;
    public const int BaseCards = 3;
    public const decimal CardsUpgrade = 1m;
    public const string ShivId = "SHIV";

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public BladeDance()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bladeDance = play.Card;
        var owner = bladeDance.Owner;
        for (int i = 0; i < bladeDance.Vars.Cards.IntValue; i++)
        {
            CreateShivInHand(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Cards.UpgradeValueBy(CardsUpgrade);
    }

    private static void CreateShivInHand(CombatState cs, PlayerState owner)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var shivs = new List<CardInstance> { cs.CreateCard(ShivId, owner) };
        CardPileCmd.AddGeneratedCardsToCombat(cs, shivs, PileType.Hand, owner);
    }
}
