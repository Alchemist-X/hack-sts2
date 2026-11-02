namespace Sts2Sim.Core.Powers;

public sealed class DisintegrationPower : PowerModel
{
    public const string PowerId = "DISINTEGRATION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterSideTurnEndLate(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            CreatureCmd.Damage(cs, Owner, Amount, ValueProp.Unpowered, Owner);
        }
    }
}
