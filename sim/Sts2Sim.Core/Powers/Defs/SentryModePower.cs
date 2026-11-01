namespace Sts2Sim.Core.Powers;

public sealed class SentryModePower : PowerModel
{
    public const string PowerId = "SENTRY_MODE_POWER";

    public const string SweepingGazeCardId = "SWEEPING_GAZE";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        if (!ReferenceEquals(player, Owner as PlayerState))
        {
            return;
        }
        for (int i = 0; i < Amount; i++)
        {
            var card = cs.CreateCard(SweepingGazeCardId, player);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, player);
        }
    }
}
