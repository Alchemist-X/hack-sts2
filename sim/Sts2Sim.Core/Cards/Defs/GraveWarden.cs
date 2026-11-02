namespace Sts2Sim.Core.Cards;

public sealed class GraveWarden : CardDef
{
    public const string CardId = "GRAVE_WARDEN";
    public const int Cost = 1;
    public const decimal BaseBlock = 8m;
    public const decimal BlockUpgrade = 3m;
    public const int BaseCards = 1;

    public GraveWarden()
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
        var graveWarden = play.Card;
        var owner = graveWarden.Owner;
        CreatureCmd.GainBlock(cs, owner, graveWarden.Vars.Block, play);
        CardPileCmd.AddGeneratedCardsToCombat(cs, Soul.Create(cs, owner, graveWarden.Vars.Cards.IntValue), PileType.Draw, owner, CardPilePosition.Random);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
