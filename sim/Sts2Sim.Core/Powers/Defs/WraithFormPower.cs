namespace Sts2Sim.Core.Powers;

public sealed class WraithFormPower : PowerModel
{
    public const string PowerId = "WRAITH_FORM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            using var throwing = cs.Choice.PushContext(ChoiceContextKind.Throwing);
            PowerCmd.Apply<DexterityPower>(cs, Owner, -Amount, Owner, null);
        }
    }
}
