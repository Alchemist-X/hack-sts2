namespace Sts2Sim.Core.Cards;

public sealed class DemonForm : CardDef
{
    public const string CardId = "DEMON_FORM";
    public const int Cost = 3;
    public const string DemonFormPowerId = "DEMON_FORM_POWER";
    public const decimal BaseStrength = 3m;
    public const decimal StrengthUpgrade = 1m;

    public DemonForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(BaseStrength)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var demonForm = play.Card;
        var owner = demonForm.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(DemonFormPowerId), owner, demonForm.Vars.Strength.BaseValue, owner, demonForm);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Strength.UpgradeValueBy(StrengthUpgrade);
    }
}
