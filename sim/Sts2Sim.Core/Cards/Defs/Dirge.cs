using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Dirge : CardDef
{
    public const string CardId = "DIRGE";
    public const int Cost = 0;
    public const decimal BaseSummon = 3m;
    public const decimal SummonUpgrade = 1m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Dirge()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self, costsX: true)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var dirge = play.Card;
        var owner = dirge.Owner;
        int xValue = dirge.ResolveEnergyXValue(cs);
        for (int i = 0; i < xValue; i++)
        {
            OstyCmd.Summon(cs, owner, dirge.Vars.Summon.BaseValue, null);
        }
        var souls = Soul.Create(cs, owner, xValue).ToList();
        if (dirge.IsUpgraded)
        {
            for (int i = 0; i < souls.Count; i++)
            {
                CardCmd.Upgrade(cs, souls[i]);
            }
        }
        CardPileCmd.AddGeneratedCardsToCombat(cs, souls, PileType.Draw, owner, CardPilePosition.Random);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
