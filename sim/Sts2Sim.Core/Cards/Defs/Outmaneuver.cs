namespace Sts2Sim.Core.Cards;

public sealed class Outmaneuver : CardDef
{
    public const string CardId = "OUTMANEUVER";
    public const int Cost = 1;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const int BaseEnergy = 2;
    public const decimal EnergyUpgrade = 1m;

    public Outmaneuver()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, card.Vars.Energy.IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
