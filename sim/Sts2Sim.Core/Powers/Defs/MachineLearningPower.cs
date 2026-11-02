namespace Sts2Sim.Core.Powers;

public sealed class MachineLearningPower : PowerModel
{
    public const string PowerId = "MACHINE_LEARNING_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        return count + Amount;
    }
}
