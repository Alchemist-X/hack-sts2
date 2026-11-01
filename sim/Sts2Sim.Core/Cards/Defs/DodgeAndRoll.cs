namespace Sts2Sim.Core.Cards;

public sealed class DodgeAndRoll : CardDef
{
    public const string CardId = "DODGE_AND_ROLL";
    public const int Cost = 1;
    public const string BlockNextTurnPowerId = "BLOCK_NEXT_TURN_POWER";
    public const decimal BaseBlock = 4m;
    public const decimal BlockUpgrade = 2m;

    public DodgeAndRoll()
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
        var dodgeAndRoll = play.Card;
        var owner = dodgeAndRoll.Owner;
        var amount = CreatureCmd.GainBlock(cs, owner, dodgeAndRoll.Vars.Block, play);
        PowerCmd.Apply(cs, PowerRegistry.Create(BlockNextTurnPowerId), owner, amount, owner, dodgeAndRoll);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
