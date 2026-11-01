namespace Sts2Sim.Core.Powers;

public sealed class SelfFormingClayPower : PowerModel
{
    public const string PowerId = "SELF_FORMING_CLAY_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        if (!ReferenceEquals(creature, Owner))
        {
            return;
        }
        CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        PowerCmd.Remove(cs, this);
    }
}
