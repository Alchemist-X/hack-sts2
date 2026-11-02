namespace Sts2Sim.Core.Powers;

public sealed class TheSealedThronePower : PowerModel
{
    public const string PowerId = "THE_SEALED_THRONE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var player = Owner as PlayerState;
        if (player != null && ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), player))
        {
            PlayerCmd.GainStars(cs, Amount, player);
        }
    }
}
