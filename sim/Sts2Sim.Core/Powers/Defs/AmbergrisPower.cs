namespace Sts2Sim.Core.Powers;

public sealed class AmbergrisPower : PowerModel
{
    public const string PowerId = "AMBERGRIS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public bool IsVisibleInternal => false;

    public override bool ShouldTakeExtraTurn(CombatState cs, PlayerState player)
    {
        if (Amount > 0)
        {
            return ReferenceEquals(player, Owner as PlayerState);
        }
        return false;
    }

    public override void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner as PlayerState))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
