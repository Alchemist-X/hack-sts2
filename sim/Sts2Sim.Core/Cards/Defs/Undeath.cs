namespace Sts2Sim.Core.Cards;

public sealed class Undeath : CardDef
{
    public const string CardId = "UNDEATH";
    public const int Cost = 0;
    public const decimal BaseBlock = 7m;
    public const decimal BlockUpgrade = 2m;

    public Undeath()
        : base(CardId, Cost, CardType.Skill, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var undeath = play.Card;
        CreatureCmd.GainBlock(cs, undeath.Owner, undeath.Vars.Block, play);
        var card = undeath.CreateClone(cs);
        card.ExhaustOnNextPlay = false;
        CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Discard, undeath.Owner);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
