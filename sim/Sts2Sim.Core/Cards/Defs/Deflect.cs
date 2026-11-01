namespace Sts2Sim.Core.Cards;

public sealed class Deflect : CardDef
{
    public const string CardId = "DEFLECT";
    public const int Cost = 0;
    public const decimal BaseBlock = 4m;
    public const decimal BlockUpgrade = 3m;

    public Deflect()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var deflect = play.Card;
        CreatureCmd.GainBlock(cs, deflect.Owner, deflect.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
