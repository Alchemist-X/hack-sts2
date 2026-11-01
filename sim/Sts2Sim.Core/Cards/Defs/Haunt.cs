namespace Sts2Sim.Core.Cards;

public sealed class Haunt : CardDef
{
    public const string CardId = "HAUNT";
    public const int Cost = 1;
    public const decimal BaseHpLoss = 7m;
    public const decimal HpLossUpgrade = 2m;
    public const string HauntPowerId = "HAUNT_POWER";

    public Haunt()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HpLossVar(BaseHpLoss)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var haunt = play.Card;
        var owner = haunt.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(HauntPowerId), owner, haunt.Vars.HpLoss.BaseValue, owner, haunt);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.HpLoss.UpgradeValueBy(HpLossUpgrade);
    }
}
