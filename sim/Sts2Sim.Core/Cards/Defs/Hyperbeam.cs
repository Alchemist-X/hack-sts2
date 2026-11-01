namespace Sts2Sim.Core.Cards;

public sealed class Hyperbeam : CardDef
{
    public const string CardId = "HYPERBEAM";
    public const int Cost = 2;
    public const decimal BaseDamage = 24m;
    public const decimal BaseFocusDown = 3m;
    public const decimal DamageUpgrade = 6m;
    public const string FocusVarName = "FocusPower";
    public const string HyperbeamFocusDownPowerId = "HYPERBEAM_FOCUS_DOWN_POWER";

    public Hyperbeam()
        : base(CardId, Cost, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(BaseDamage, ValueProp.Move),
        new PowerVar<FocusPower>(BaseFocusDown)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var hyperbeam = play.Card;
        DamageCmd.Attack(cs, hyperbeam.Vars.Damage.BaseValue).FromCard(hyperbeam, play).TargetingAllOpponents().Execute();
        var owner = hyperbeam.Owner;
        PowerCmd.Apply(cs, PowerRegistry.Create(HyperbeamFocusDownPowerId), owner, hyperbeam.Vars[FocusVarName].BaseValue, owner, hyperbeam);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars.Damage.UpgradeValueBy(DamageUpgrade);
    }
}
