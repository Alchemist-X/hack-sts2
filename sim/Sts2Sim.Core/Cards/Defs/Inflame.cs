namespace Sts2Sim.Core.Cards;

public sealed class Inflame : CardDef
{
    public const string CardId = "INFLAME";
    public const int Cost = 1;
    public const decimal BaseStrength = 2m;
    public const decimal StrengthUpgrade = 1m;

    public Inflame()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var inflame = play.Card;
        var owner = inflame.Owner;
        PowerCmd.Apply<StrengthPower>(cs, owner, inflame.Vars[DynamicVarSet.StrengthName].BaseValue, owner, inflame);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[DynamicVarSet.StrengthName].UpgradeValueBy(StrengthUpgrade);
    }
}
