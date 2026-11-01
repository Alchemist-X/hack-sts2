namespace Sts2Sim.Core.Cards;

public sealed class Glitterstream : CardDef
{
    public const string CardId = "GLITTERSTREAM";
    public const int Cost = 2;
    public const string BlockNextTurnPowerId = "BLOCK_NEXT_TURN_POWER";
    public const string BlockNextTurnKey = "BlockNextTurn";
    public const decimal BaseBlock = 11m;
    public const decimal BaseBlockNextTurn = 5m;
    public const decimal BlockUpgrade = 2m;
    public const decimal BlockNextTurnUpgrade = 2m;

    public Glitterstream()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new BlockVar(BlockNextTurnKey, BaseBlockNextTurn, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        var blockVar = (BlockVar)card.Vars[BlockNextTurnKey];
        decimal blockNextTurnAmount = Hook.ModifyBlock(cs, owner, blockVar.BaseValue, blockVar.Props, card, play);
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(BlockNextTurnPowerId), owner, blockNextTurnAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
        card.Vars[BlockNextTurnKey].UpgradeValueBy(BlockNextTurnUpgrade);
    }
}
