namespace Sts2Sim.Core.Cards;

public sealed class BoostAway : CardDef
{
    public const string CardId = "BOOST_AWAY";
    public const int Cost = 0;
    public const string DazedCardId = "DAZED";
    public const decimal BaseBlock = 6m;
    public const decimal BlockUpgrade = 3m;

    public BoostAway()
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
        var boostAway = play.Card;
        var owner = boostAway.Owner;
        CreatureCmd.GainBlock(cs, owner, boostAway.Vars.Block, play);
        var card = cs.CreateCard(DazedCardId, owner);
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
