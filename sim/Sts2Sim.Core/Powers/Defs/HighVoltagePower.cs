namespace Sts2Sim.Core.Powers;

public sealed class HighVoltagePower : PowerModel
{
    public const string PowerId = "HIGH_VOLTAGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!participants.Contains(Owner))
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
    }
}
