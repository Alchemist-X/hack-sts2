namespace Sts2Sim.Core.Cards;

public sealed class Glasswork : CardDef
{
    public const string CardId = "GLASSWORK";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;
    public const decimal BlockUpgrade = 3m;

    public Glasswork()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var glasswork = play.Card;
        var owner = glasswork.Owner;
        CreatureCmd.GainBlock(cs, owner, glasswork.Vars.Block, play);
        OrbCmd.Channel<GlassOrb>(cs, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
