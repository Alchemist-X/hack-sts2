namespace Sts2Sim.Core.Cards;

public sealed class Patter : CardDef
{
    public const string CardId = "PATTER";
    public const int Cost = 1;
    public const string VigorPowerId = "VIGOR_POWER";
    public const string VigorVarName = "VigorPower";
    public const decimal BaseBlock = 8m;
    public const decimal BaseVigor = 2m;
    public const decimal BlockUpgrade = 2m;
    public const decimal VigorUpgrade = 1m;

    public Patter()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(VigorVarName, BaseVigor)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(VigorPowerId), owner, card.Vars[VigorVarName].IntValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars[VigorVarName].UpgradeValueBy(VigorUpgrade);
    }
}
