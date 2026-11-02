namespace Sts2Sim.Core.Powers;

public sealed class MindRotPower : PowerModel
{
    public const string PowerId = "MIND_ROT_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal count)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return count;
        }
        return Math.Max(0m, count - Amount);
    }
}
