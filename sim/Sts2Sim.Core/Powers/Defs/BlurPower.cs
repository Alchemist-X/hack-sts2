namespace Sts2Sim.Core.Powers;

public sealed class BlurPower : PowerModel
{
    public const string PowerId = "BLUR_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldClearBlock(CombatState cs, CreatureState creature) => !ReferenceEquals(Owner, creature);

    public override void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
