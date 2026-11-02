namespace Sts2Sim.Core.Cards;

public sealed class EscapePlan : CardDef
{
    public const string CardId = "ESCAPE_PLAN";
    public const int Cost = 0;
    public const decimal BaseBlock = 3m;
    public const decimal BlockUpgrade = 2m;
    public const decimal DrawCount = 1m;

    public EscapePlan()
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
        var escapePlan = play.Card;
        var owner = escapePlan.Owner;
        var cardModel = CardPileCmd.DrawCards(cs, DrawCount, owner).FirstOrDefault();
        if (cardModel != null && cardModel.Type == CardType.Skill)
        {
            CreatureCmd.GainBlock(cs, owner, escapePlan.Vars.Block, play);
        }
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
