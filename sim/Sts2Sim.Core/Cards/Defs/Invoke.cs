namespace Sts2Sim.Core.Cards;

public sealed class Invoke : CardDef
{
    public const string CardId = "INVOKE";
    public const int Cost = 1;
    public const string SummonNextTurnPowerId = "SUMMON_NEXT_TURN_POWER";
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const decimal BaseSummon = 2m;
    public const int BaseEnergy = 2;
    public const decimal SummonUpgrade = 1m;
    public const decimal EnergyUpgrade = 1m;

    public Invoke()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var invoke = play.Card;
        var owner = invoke.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(SummonNextTurnPowerId), owner, invoke.Vars.Summon.IntValue, owner, invoke);
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, invoke.Vars.Energy.IntValue, owner, invoke);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
