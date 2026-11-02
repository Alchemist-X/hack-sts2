namespace Sts2Sim.Core.Cards;

public sealed class PillarOfCreation : CardDef
{
    public const string CardId = "PILLAR_OF_CREATION";
    public const int Cost = 1;
    public const string PillarOfCreationPowerId = "PILLAR_OF_CREATION_POWER";
    public const decimal BaseBlock = 2m;
    public const decimal BlockUpgrade = 1m;

    public PillarOfCreation()
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
        PowerCmd.Apply(cs, PowerRegistry.Create(PillarOfCreationPowerId), owner, card.Vars.Block.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
