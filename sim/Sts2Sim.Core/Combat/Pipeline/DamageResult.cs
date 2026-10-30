namespace Sts2Sim.Core.Combat.Pipeline;

public sealed class DamageResult
{
    public DamageResult(CreatureState receiver, ValueProp props)
    {
        Receiver = receiver;
        Props = props;
    }

    public CreatureState Receiver { get; }

    public ValueProp Props { get; }

    public int BlockedDamage { get; set; }

    public int UnblockedDamage { get; set; }

    public int OverkillDamage { get; set; }

    public int TotalDamage => BlockedDamage + UnblockedDamage;

    public bool WasBlockBroken { get; set; }

    public bool WasFullyBlocked { get; set; }

    public bool WasTargetKilled { get; set; }
}

public static class ValuePropExtensions
{
    public static bool IsPoweredAttack(this ValueProp props)
    {
        if ((props & ValueProp.Move) != 0)
        {
            return (props & ValueProp.Unpowered) == 0;
        }
        return false;
    }

    public static bool IsPoweredCardOrMonsterMoveBlock(this ValueProp props)
    {
        if ((props & ValueProp.Move) != 0)
        {
            return (props & ValueProp.Unpowered) == 0;
        }
        return false;
    }

    public static bool IsCardOrMonsterMove(this ValueProp props) => (props & ValueProp.Move) != 0;

    public static bool HasProp(this ValueProp props, ValueProp flag) => (props & flag) == flag;
}
