namespace Sts2Sim.Core.Cards;

public sealed class LightningRod : CardDef
{
    public const string CardId = "LIGHTNING_ROD";
    public const int Cost = 1;
    public const string LightningRodPowerId = "LIGHTNING_ROD_POWER";
    public const string LightningRodPowerVar = "LightningRodPower";
    public const decimal BaseBlock = 4m;
    public const decimal BaseLightningRod = 2m;
    public const decimal BlockUpgrade = 3m;

    public LightningRod()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(LightningRodPowerVar, BaseLightningRod)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var lightningRod = play.Card;
        var owner = lightningRod.Owner;
        CreatureCmd.GainBlock(cs, owner, lightningRod.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(LightningRodPowerId), owner, lightningRod.Vars[LightningRodPowerVar].BaseValue, owner, lightningRod);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
