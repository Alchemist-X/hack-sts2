namespace Sts2Sim.Core.Cards;

public sealed class Capacitor : CardDef
{
    public const string CardId = "CAPACITOR";
    public const int Cost = 1;
    public const int BaseRepeat = 2;
    public const decimal RepeatUpgrade = 1m;

    public Capacitor()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(BaseRepeat)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var capacitor = play.Card;
        OrbCmd.AddSlots(cs, capacitor.Owner, capacitor.Vars.Repeat.IntValue);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Repeat.UpgradeValueBy(RepeatUpgrade);
    }
}
