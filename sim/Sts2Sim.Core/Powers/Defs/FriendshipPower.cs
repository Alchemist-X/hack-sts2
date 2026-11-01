namespace Sts2Sim.Core.Powers;

public sealed class FriendshipPower : PowerModel
{
    public const string PowerId = "FRIENDSHIP_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return amount;
        }
        return amount + Amount;
    }
}
