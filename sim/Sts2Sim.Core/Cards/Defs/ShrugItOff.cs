namespace Sts2Sim.Core.Cards;

public sealed class ShrugItOff : CardDef
{
    public const string CardId = "SHRUG_IT_OFF";
    public const int Cost = 1;
    public const decimal BaseBlock = 8m;
    public const int BaseCards = 1;
    public const decimal BlockUpgrade = 3m;

    public ShrugItOff()
        : base(CardId, Cost, CardType.Skill, CardRarity.Common, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        CreatureCmd.GainBlock(cs, card.Owner, card.Vars.Block, play);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, card.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
