namespace Sts2Sim.Core.Cards;

public sealed class Reflect : CardDef
{
    public const string CardId = "REFLECT";
    public const int Cost = 1;
    public const int StarCost = 3;
    public const decimal BaseBlock = 15m;
    public const decimal BlockUpgrade = 5m;
    public const decimal ReflectAmount = 1m;
    public const string ReflectPowerId = "REFLECT_POWER";

    public Reflect()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override int CanonicalStarCost => StarCost;

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var reflect = play.Card;
        var owner = reflect.Owner;
        CreatureCmd.GainBlock(cs, owner, reflect.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(ReflectPowerId), owner, ReflectAmount, owner, reflect);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
