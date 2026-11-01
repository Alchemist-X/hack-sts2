namespace Sts2Sim.Core.Cards;

public sealed class BloodWall : CardDef
{
    public const string CardId = "BLOOD_WALL";
    public const int Cost = 2;
    public const decimal BaseHpLoss = 2m;
    public const decimal BaseBlock = 16m;
    public const decimal BlockUpgrade = 4m;

    public BloodWall()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss),
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.Damage(cs, owner, card.Vars.HpLoss.BaseValue, ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move, card, play);
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
