namespace Sts2Sim.Core.Cards;

public sealed class Panache : CardDef
{
    public const string CardId = "PANACHE";
    public const int Cost = 0;
    public const string PanachePowerId = "PANACHE_POWER";
    public const string PanacheDamageKey = "PanacheDamage";
    public const decimal BasePanacheDamage = 10m;
    public const decimal PanacheDamageUpgrade = 4m;

    public Panache()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PanacheDamageKey, BasePanacheDamage)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PanachePowerId), owner, card.Vars[PanacheDamageKey].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PanacheDamageKey].UpgradeValueBy(PanacheDamageUpgrade);
    }
}
