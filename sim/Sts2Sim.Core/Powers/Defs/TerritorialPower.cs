namespace Sts2Sim.Core.Powers;

public sealed class TerritorialPower : PowerModel
{
    public const string PowerId = "TERRITORIAL_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
    }
}
