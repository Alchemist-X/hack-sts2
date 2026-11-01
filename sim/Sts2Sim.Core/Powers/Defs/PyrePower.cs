namespace Sts2Sim.Core.Powers;

public sealed class PyrePower : PowerModel
{
    public const string PowerId = "PYRE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return amount;
        }
        return amount + Amount;
    }
}
