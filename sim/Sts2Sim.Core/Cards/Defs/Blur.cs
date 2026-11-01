namespace Sts2Sim.Core.Cards;

public sealed class Blur : CardDef
{
    public const string CardId = "BLUR";
    public const int Cost = 1;
    public const string BlurPowerId = "BLUR_POWER";
    public const string BlurVarName = "Blur";
    public const decimal BaseBlock = 5m;
    public const decimal BaseBlur = 1m;
    public const decimal BlockUpgrade = 3m;

    public Blur()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(BlurVarName, BaseBlur)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var blur = play.Card;
        var owner = blur.Owner;
        CreatureCmd.GainBlock(cs, owner, blur.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(BlurPowerId), owner, blur.Vars[BlurVarName].BaseValue, owner, blur);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
