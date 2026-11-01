namespace Sts2Sim.Core.Cards;

public sealed class NecroMastery : CardDef
{
    public const string CardId = "NECRO_MASTERY";
    public const int Cost = 2;
    public const string NecroMasteryPowerId = "NECRO_MASTERY_POWER";
    public const decimal BaseSummon = 5m;
    public const decimal NecroMasteryAmount = 1m;
    public const decimal SummonUpgrade = 3m;

    public NecroMastery()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var necroMastery = play.Card;
        var owner = necroMastery.Owner;
        OstyCmd.Summon(cs, owner, necroMastery.Vars.Summon.BaseValue, null);
        PowerCmd.Apply(cs, PowerRegistry.Create(NecroMasteryPowerId), owner, NecroMasteryAmount, owner, necroMastery);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
    }
}
