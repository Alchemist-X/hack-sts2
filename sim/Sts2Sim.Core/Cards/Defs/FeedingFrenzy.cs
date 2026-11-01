namespace Sts2Sim.Core.Cards;

public sealed class FeedingFrenzy : CardDef
{
    public const string CardId = "FEEDING_FRENZY";
    public const int Cost = 0;
    public const string FeedingFrenzyPowerId = "FEEDING_FRENZY_POWER";
    public const decimal BaseStrength = 5m;
    public const decimal StrengthUpgrade = 2m;

    public FeedingFrenzy()
        : base(CardId, Cost, CardType.Skill, CardRarity.Event, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(FeedingFrenzyPowerId), owner, card.Vars.Strength.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
