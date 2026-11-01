namespace Sts2Sim.Core.Cards;

public sealed class Backflip : CardDef
{
    public const string CardId = "BACKFLIP";
    public const int Cost = 1;
    public const decimal BaseBlock = 5m;
    public const int BaseCards = 2;
    public const decimal BlockUpgrade = 3m;

    public Backflip()
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
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
