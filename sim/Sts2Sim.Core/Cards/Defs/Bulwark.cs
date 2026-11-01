namespace Sts2Sim.Core.Cards;

public sealed class Bulwark : CardDef
{
    public const string CardId = "BULWARK";
    public const int Cost = 2;
    public const decimal BaseBlock = 12m;
    public const int BaseForge = 10;
    public const decimal BlockUpgrade = 3m;
    public const decimal ForgeUpgrade = 3m;

    public Bulwark()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var bulwark = play.Card;
        CreatureCmd.GainBlock(cs, bulwark.Owner, bulwark.Vars.Block, play);
        ForgeCmd.Forge(cs, bulwark.Vars.Forge.IntValue, bulwark.Owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
