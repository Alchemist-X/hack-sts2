using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Reanimate : CardDef
{
    public const string CardId = "REANIMATE";
    public const int Cost = 3;
    public const decimal BaseSummon = 20m;
    public const decimal SummonUpgrade = 5m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Reanimate()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var reanimate = play.Card;
        OstyCmd.Summon(cs, reanimate.Owner, reanimate.Vars.Summon.BaseValue, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
