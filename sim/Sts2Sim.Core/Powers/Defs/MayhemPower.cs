namespace Sts2Sim.Core.Powers;

public sealed class MayhemPower : PowerModel
{
    public const string PowerId = "MAYHEM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        CardPileCmd.AutoPlayFromDrawPile(cs, player, Amount, CardPilePosition.Top, forceExhaust: false);
    }
}
