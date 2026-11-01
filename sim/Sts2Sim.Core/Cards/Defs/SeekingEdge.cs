namespace Sts2Sim.Core.Cards;

public sealed class SeekingEdge : CardDef
{
    public const string CardId = "SEEKING_EDGE";
    public const int Cost = 1;
    public const int BaseForge = 7;
    public const decimal ForgeUpgrade = 4m;
    public const decimal SeekingEdgeAmount = 1m;
    public const string SeekingEdgePowerId = "SEEKING_EDGE_POWER";

    public SeekingEdge()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new ForgeVar(BaseForge)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var seekingEdge = play.Card;
        var owner = seekingEdge.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(SeekingEdgePowerId), owner, SeekingEdgeAmount, owner, seekingEdge);
        ForgeCmd.Forge(cs, seekingEdge.Vars.Forge.IntValue, owner, null);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Forge.UpgradeValueBy(ForgeUpgrade);
    }
}
