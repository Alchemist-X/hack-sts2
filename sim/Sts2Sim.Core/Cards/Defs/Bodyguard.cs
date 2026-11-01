namespace Sts2Sim.Core.Cards;

public sealed class Bodyguard : CardDef
{
    public const string CardId = "BODYGUARD";
    public const int Cost = 1;
    public const decimal BaseSummon = 5m;
    public const decimal SummonUpgrade = 2m;

    public Bodyguard()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bodyguard = play.Card;
        OstyCmd.Summon(cs, bodyguard.Owner, bodyguard.Vars.Summon.BaseValue, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
