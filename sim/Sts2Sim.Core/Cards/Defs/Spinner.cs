namespace Sts2Sim.Core.Cards;

public sealed class Spinner : CardDef
{
    public const string CardId = "SPINNER";
    public const int Cost = 1;
    public const string SpinnerPowerId = "SPINNER_POWER";
    public const string SpinnerPowerVar = "SpinnerPower";
    public const decimal BaseSpinner = 1m;

    public Spinner()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(SpinnerPowerVar, BaseSpinner)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var spinner = play.Card;
        var owner = spinner.Owner;
        if (spinner.IsUpgraded)
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            OrbCmd.Channel<GlassOrb>(cs, owner);
        }
        PowerCmd.Apply(cs, PowerRegistry.Create(SpinnerPowerId), owner, spinner.Vars[SpinnerPowerVar].BaseValue, owner, spinner);
    }
}
