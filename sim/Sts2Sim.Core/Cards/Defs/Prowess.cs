namespace Sts2Sim.Core.Cards;

public sealed class Prowess : CardDef
{
    public const string CardId = "PROWESS";
    public const int Cost = 1;
    public const decimal BaseStrength = 1m;
    public const decimal BaseDexterity = 1m;
    public const decimal DexterityUpgrade = 1m;
    public const decimal StrengthUpgrade = 1m;

    public Prowess()
        : base(CardId, Cost, CardType.Power, CardRarity.Uncommon, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength),
        new PowerVar<DexterityPower>(BaseDexterity)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply<StrengthPower>(cs, owner, card.Vars.Strength.BaseValue, owner, card);
        PowerCmd.Apply<DexterityPower>(cs, owner, card.Vars.Dexterity.BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Dexterity.UpgradeValueBy(DexterityUpgrade);
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
