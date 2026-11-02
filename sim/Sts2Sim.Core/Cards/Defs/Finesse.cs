namespace Sts2Sim.Core.Cards;

public sealed class Finesse : CardDef
{
    public const string CardId = "FINESSE";
    public const int Cost = 0;
    public const decimal BaseBlock = 4m;
    public const int BaseCards = 1;
    public const decimal BlockUpgrade = 3m;

    public Finesse()
        : base(CardId, Cost, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(BaseBlock, ValueProp.Move),
        new CardsVar(BaseCards)
    };

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var finesse = play.Card;
        var owner = finesse.Owner;
        CreatureCmd.GainBlock(cs, owner, finesse.Vars.Block, play);
        CardPileCmd.Draw(cs, finesse.Vars.Cards.BaseValue, owner);
    }
}
