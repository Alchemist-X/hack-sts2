namespace Sts2Sim.Core.Cards;

public sealed class Shroud : CardDef
{
    public const string CardId = "SHROUD";
    public const int Cost = 1;
    public const decimal BaseBlock = 3m;
    public const decimal BlockUpgrade = 1m;
    public const string ShroudPowerId = "SHROUD_POWER";

    public Shroud()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Unpowered)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, ShroudPowerId, owner, card.Vars.Block.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
