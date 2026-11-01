namespace Sts2Sim.Core.Powers;

public sealed class DemisePower : PowerModel
{
    public const string PowerId = "DEMISE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            CreatureCmd.Damage(cs, Owner, Amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null, null);
        }
    }
}
