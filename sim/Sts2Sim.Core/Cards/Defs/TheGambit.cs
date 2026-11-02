namespace Sts2Sim.Core.Cards;

public sealed class TheGambit : CardDef
{
    public const string CardId = "THE_GAMBIT";
    public const int Cost = 0;
    public const decimal BaseBlock = 50m;
    public const decimal PowerAmount = 1m;
    public const decimal BlockUpgrade = 25m;
    public const string TheGambitPowerId = "THE_GAMBIT_POWER";

    public TheGambit()
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
        var card = play.Card;
        var owner = card.Owner;
        CreatureCmd.GainBlock(cs, owner, card.Vars.Block, play);
        PowerCmd.Apply(cs, TheGambitPowerId, owner, PowerAmount, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Block.UpgradeValueBy(BlockUpgrade);
    }
}
