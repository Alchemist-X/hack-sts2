namespace Sts2Sim.Core.Powers;

public sealed class RegenPower : PowerModel
{
    public const string PowerId = "REGEN_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => true;

    public override void BeforeSideTurnEndEarly(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner) && !Owner.IsDead)
        {
            CreatureCmd.Heal(cs, Owner, Amount);
            PowerCmd.Decrement(cs, this);
        }
    }
}
