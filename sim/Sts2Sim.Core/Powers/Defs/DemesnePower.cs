namespace Sts2Sim.Core.Powers;

public sealed class DemesnePower : PowerModel
{
    public const string PowerId = "DEMESNE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return count;
        }
        return count + Amount;
    }

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return amount;
        }
        return amount + Amount;
    }
}
