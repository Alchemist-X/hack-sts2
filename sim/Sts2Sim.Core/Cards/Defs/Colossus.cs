namespace Sts2Sim.Core.Cards;

public sealed class Colossus : CardDef
{
    public const string CardId = "COLOSSUS";
    public const int Cost = 1;
    public const string ColossusPowerId = "COLOSSUS_POWER";
    public const string PowerVarName = "Colossus";
    public const decimal BaseBlock = 4m;
    public const decimal BaseColossus = 1m;
    public const decimal BlockUpgrade = 3m;

    public Colossus()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new DynamicVar(PowerVarName, BaseColossus)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var colossus = play.Card;
        var owner = colossus.Owner;
        CreatureCmd.GainBlock(cs, owner, colossus.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(ColossusPowerId), owner, colossus.Vars[PowerVarName].BaseValue, owner, colossus);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
