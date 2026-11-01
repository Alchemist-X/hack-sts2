namespace Sts2Sim.Core.Cards;

public sealed class Rupture : CardDef
{
    public const string CardId = "RUPTURE";
    public const int Cost = 1;
    public const decimal BaseStrength = 1m;
    public const decimal StrengthUpgrade = 1m;
    public const string RupturePowerId = "RUPTURE_POWER";

    public Rupture()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
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
        PowerCmd.Apply(cs, RupturePowerId, owner, card.Vars.Strength.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
