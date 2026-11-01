namespace Sts2Sim.Core.Powers;

public sealed class HibernatePower : PowerModel
{
    public const string PowerId = "HIBERNATE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        if (ReferenceEquals(player, Owner))
        {
            PowerCmd.Decrement(cs, this);
        }
    }
}
