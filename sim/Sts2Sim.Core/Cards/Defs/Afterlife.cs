using System.Collections.ObjectModel;

namespace Sts2Sim.Core.Cards;

public sealed class Afterlife : CardDef
{
    public const string CardId = "AFTERLIFE";
    public const int Cost = 1;
    public const decimal BaseSummon = 6m;
    public const decimal SummonUpgrade = 3m;

    private static readonly ReadOnlyCollection<CardKeyword> Keywords = Array.AsReadOnly(new[] { CardKeyword.Exhaust });

    public Afterlife()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override IReadOnlyCollection<CardKeyword> CanonicalKeywords => Keywords;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var afterlife = play.Card;
        OstyCmd.Summon(cs, afterlife.Owner, afterlife.Vars.Summon.BaseValue, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
