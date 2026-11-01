namespace Sts2Sim.Core.Powers;

public sealed class HatchPower : PowerModel
{
    public const string PowerId = "HATCH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
