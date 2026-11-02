namespace Sts2Sim.Core.Cards;

public sealed class Glacier : CardDef
{
    public const string CardId = "GLACIER";
    public const int Cost = 2;
    public const decimal BaseBlock = 6m;
    public const decimal BlockUpgrade = 3m;
    public const int FrostCount = 2;

    public Glacier()
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
        var glacier = play.Card;
        var owner = glacier.Owner;
        CreatureCmd.GainBlock(cs, owner, glacier.Vars.Block, play);
        for (int i = 0; i < FrostCount; i++)
        {
            OrbCmd.Channel<FrostOrb>(cs, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
