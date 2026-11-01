namespace Sts2Sim.Core.Cards;

public sealed class Delay : CardDef
{
    public const string CardId = "DELAY";
    public const int Cost = 2;
    public const string EnergyNextTurnPowerId = "ENERGY_NEXT_TURN_POWER";
    public const decimal BaseBlock = 11m;
    public const int BaseEnergy = 1;
    public const decimal BlockUpgrade = 2m;
    public const decimal EnergyUpgrade = 1m;

    public Delay()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
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
        var delay = play.Card;
        var owner = delay.Owner;
        CreatureCmd.GainBlock(cs, owner, delay.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(EnergyNextTurnPowerId), owner, delay.Vars.Energy.IntValue, owner, delay);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Energy.UpgradeValueBy(EnergyUpgrade);
    }
}
