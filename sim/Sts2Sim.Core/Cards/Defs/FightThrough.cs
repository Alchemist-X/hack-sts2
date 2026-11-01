namespace Sts2Sim.Core.Cards;

public sealed class FightThrough : CardDef
{
    public const string CardId = "FIGHT_THROUGH";
    public const int Cost = 1;
    public const string WoundCardId = "WOUND";
    public const decimal BaseBlock = 13m;
    public const decimal BlockUpgrade = 4m;
    public const int WoundCount = 2;

    public FightThrough()
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
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        for (int i = 0; i < WoundCount; i++)
        {
            var wound = cs.CreateCard(WoundCardId, owner);
            CardPileCmd.AddGeneratedCardToCombat(cs, wound, PileType.Discard, owner);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
