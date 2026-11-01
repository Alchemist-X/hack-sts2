namespace Sts2Sim.Core.Cards;

public sealed class Defragment : CardDef
{
    public const string CardId = "DEFRAGMENT";
    public const int Cost = 1;
    public const decimal BaseFocus = 1m;
    public const decimal FocusUpgrade = 1m;
    public const string FocusVarName = "FocusPower";

    public Defragment()
        : base(CardId, Cost, CardType.Power, CardRarity.Rare, TargetType.Self)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<FocusPower>(BaseFocus)
    };

    public override void OnPlay(CombatState cs, CardPlay play)
    {
        var defragment = play.Card;
        var owner = defragment.Owner;
        PowerCmd.Apply<FocusPower>(cs, owner, defragment.Vars[FocusVarName].BaseValue, owner, defragment);
    }

    public override void OnUpgrade(CardInstance card)
    {
        card.Vars[FocusVarName].UpgradeValueBy(FocusUpgrade);
    }
}
