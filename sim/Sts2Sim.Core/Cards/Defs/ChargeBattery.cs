namespace Sts2Sim.Core.Cards;

public sealed class ChargeBattery : CardDef
{
    public const string CardId = "CHARGE_BATTERY";
    public const int Cost = 1;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const decimal BaseBlock = 7m;
    public const int BaseEnergy = 1;
    public const decimal BlockUpgrade = 3m;

    public ChargeBattery()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new EnergyVar(BaseEnergy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var chargeBattery = play.Card;
        var owner = chargeBattery.Owner;
        CreatureCmd.GainBlock(cs, owner, chargeBattery.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, chargeBattery.Vars.Energy.BaseValue, owner, chargeBattery);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
