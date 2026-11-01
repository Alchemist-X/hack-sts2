namespace Sts2Sim.Core.Cards;

public sealed class Accuracy : CardDef
{
    public const string CardId = "ACCURACY";
    public const int Cost = 1;
    public const decimal BaseAccuracy = 4m;
    public const decimal AccuracyUpgrade = 2m;
    public const string AccuracyVarName = "AccuracyPower";
    public const string AccuracyPowerId = "ACCURACY_POWER";

    public Accuracy()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(AccuracyVarName, BaseAccuracy)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var accuracy = play.Card;
        var owner = accuracy.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(AccuracyPowerId), owner, accuracy.Vars[AccuracyVarName].BaseValue, owner, accuracy);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[AccuracyVarName].UpgradeValueBy(AccuracyUpgrade);
    }
}
