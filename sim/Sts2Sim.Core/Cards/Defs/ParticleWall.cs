namespace Sts2Sim.Core.Cards;

public sealed class ParticleWall : CardDef
{
    public const string CardId = "PARTICLE_WALL";
    public const int Cost = 0;
    public const int StarCost = 2;
    public const decimal BaseBlock = 9m;
    public const decimal BlockUpgrade = 3m;

    public ParticleWall()
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
        var particleWall = play.Card;
        CreatureCmd.GainBlock(cs, particleWall.Owner, particleWall.Vars.Block, play);
    }

    public override CardLocation GetResultLocationForCardPlay(CombatState cs, CardInstance card)
    {
        var resultLocationForCardPlay = base.GetResultLocationForCardPlay(cs, card);
        if (resultLocationForCardPlay.PileType == PileType.Discard)
        {
            resultLocationForCardPlay = resultLocationForCardPlay with { PileType = PileType.Hand };
        }
        return resultLocationForCardPlay;
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
