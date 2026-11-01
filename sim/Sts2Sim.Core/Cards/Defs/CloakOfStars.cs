namespace Sts2Sim.Core.Cards;

public sealed class CloakOfStars : CardDef
{
    public const string CardId = "CLOAK_OF_STARS";
    public const int Cost = 0;
    public const int StarCost = 1;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 3m;

    public CloakOfStars()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
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
        var cloakOfStars = play.Card;
        CreatureCmd.GainBlock(cs, cloakOfStars.Owner, cloakOfStars.Vars.Block, play);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
