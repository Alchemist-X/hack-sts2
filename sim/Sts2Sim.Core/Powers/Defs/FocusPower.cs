namespace Sts2Sim.Core.Powers;

public sealed class FocusPower : PowerModel
{
    public const string PowerId = "FOCUS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool AllowNegative => true;

    public override decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value)
    {
        var orbOwner = orb.HasOwner ? orb.Owner : null;
        if (!ReferenceEquals(Owner as PlayerState, orbOwner))
        {
            return value;
        }
        return Math.Max(value + Amount, 0m);
    }
}
