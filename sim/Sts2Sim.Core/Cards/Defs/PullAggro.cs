namespace Sts2Sim.Core.Cards;

public sealed class PullAggro : CardDef
{
    public const string CardId = "PULL_AGGRO";
    public const int Cost = 2;
    public const decimal BaseSummon = 4m;
    public const decimal BaseBlock = 7m;
    public const decimal SummonUpgrade = 1m;
    public const decimal BlockUpgrade = 2m;

    public PullAggro()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new SummonVar(BaseSummon),
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var pullAggro = play.Card;
        var owner = pullAggro.Owner;
        OstyCmd.Summon(cs, owner, pullAggro.Vars.Summon.BaseValue, null);
        CreatureCmd.GainBlock(cs, owner, pullAggro.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Summon.UpgradeValueBy(SummonUpgrade);
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
