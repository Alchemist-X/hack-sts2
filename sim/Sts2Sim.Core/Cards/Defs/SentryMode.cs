namespace Sts2Sim.Core.Cards;

public sealed class SentryMode : CardDef
{
    public const string CardId = "SENTRY_MODE";
    public const int Cost = 2;
    public const int CostUpgrade = -1;
    public const decimal BaseSentryMode = 1m;
    public const string SentryModePowerId = "SENTRY_MODE_POWER";
    public const string SentryModePowerVar = "SentryModePower";

    public SentryMode()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(SentryModePowerVar, BaseSentryMode)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var sentryMode = play.Card;
        var owner = sentryMode.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(SentryModePowerId), owner, sentryMode.Vars[SentryModePowerVar].BaseValue, owner, sentryMode);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.EnergyCost.UpgradeBy(CostUpgrade);
    }
}
