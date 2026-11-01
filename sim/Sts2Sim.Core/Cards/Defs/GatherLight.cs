namespace Sts2Sim.Core.Cards;

public sealed class GatherLight : CardDef
{
    public const string CardId = "GATHER_LIGHT";
    public const int Cost = 1;
    public const decimal BaseBlock = 8m;
    public const int BaseStars = 1;
    public const decimal BlockUpgrade = 3m;

    public GatherLight()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new StarsVar(BaseStars)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var gatherLight = play.Card;
        var owner = gatherLight.Owner;
        CreatureCmd.GainBlock(cs, owner, gatherLight.Vars.Block, play);
        PlayerCmd.GainStars(cs, gatherLight.Vars.Stars.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
