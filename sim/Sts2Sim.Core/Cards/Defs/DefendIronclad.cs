namespace Sts2Sim.Core.Cards;

public sealed class DefendIronclad : CardDef
{
    public const string CardId = "DEFEND_IRONCLAD";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;
    public const decimal BlockUpgrade = 3m;

    private static readonly CardTag[] Tags = { CardTag.Defend };

    public DefendIronclad()
        : base(CardId, Cost, CardType.Skill, CardRarity.Basic, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    public override IReadOnlyCollection<CardTag> CanonicalTags => Tags;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var block = play.Card.Vars.Block;
        CreatureCmd.GainBlock(cs, cs.Player, block.BaseValue, block.Props, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
