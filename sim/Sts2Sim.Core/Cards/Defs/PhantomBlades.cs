namespace Sts2Sim.Core.Cards;

public sealed class PhantomBlades : CardDef
{
    public const string CardId = "PHANTOM_BLADES";
    public const int Cost = 1;
    public const string PhantomBladesPowerKey = "PhantomBladesPower";
    public const decimal BasePhantomBlades = 9m;
    public const decimal PhantomBladesUpgrade = 3m;
    public const string PhantomBladesPowerId = "PHANTOM_BLADES_POWER";

    public PhantomBlades()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PhantomBladesPowerKey, BasePhantomBlades)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var phantomBlades = play.Card;
        var owner = phantomBlades.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(PhantomBladesPowerId), owner, phantomBlades.Vars[PhantomBladesPowerKey].BaseValue, owner, phantomBlades);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[PhantomBladesPowerKey].UpgradeValueBy(PhantomBladesUpgrade);
    }
}
