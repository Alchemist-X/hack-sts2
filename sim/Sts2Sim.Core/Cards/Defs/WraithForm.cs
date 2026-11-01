namespace Sts2Sim.Core.Cards;

public sealed class WraithForm : CardDef
{
    public const string CardId = "WRAITH_FORM";
    public const int Cost = 3;
    public const string IntangibleVarName = "IntangiblePower";
    public const string WraithFormVarName = "WraithFormPower";
    public const decimal BaseIntangible = 2m;
    public const decimal BaseWraithForm = 1m;
    public const decimal IntangibleUpgrade = 1m;
    public const string IntangiblePowerId = "INTANGIBLE_POWER";
    public const string WraithFormPowerId = "WRAITH_FORM_POWER";

    public WraithForm()
        : base(CardId, Cost, CardType.Power, CardRarity.Ancient, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(IntangibleVarName, BaseIntangible),
        new DynamicVar(WraithFormVarName, BaseWraithForm)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var card = play.Card;
        var owner = card.Owner;
        PowerCmd.Apply(cs, IntangiblePowerId, owner, card.Vars[IntangibleVarName].BaseValue, owner, card);
        PowerCmd.Apply(cs, WraithFormPowerId, owner, card.Vars[WraithFormVarName].BaseValue, owner, card);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[IntangibleVarName].UpgradeValueBy(IntangibleUpgrade);
    }
}
