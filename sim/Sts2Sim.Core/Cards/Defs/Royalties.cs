namespace Sts2Sim.Core.Cards;

[SimExclude]
public sealed class Royalties : CardDef
{
    public const string CardId = "ROYALTIES";
    public const int Cost = 1;
    public const string RoyaltiesPowerId = "ROYALTIES_POWER";
    public const int BaseGold = 30;
    public const decimal GoldUpgrade = 10m;

    public Royalties()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new GoldVar(BaseGold)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var royalties = play.Card;
        var owner = royalties.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(RoyaltiesPowerId), owner, royalties.Vars[GoldVar.DefaultName].BaseValue, owner, royalties);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[GoldVar.DefaultName].UpgradeValueBy(GoldUpgrade);
    }
}
