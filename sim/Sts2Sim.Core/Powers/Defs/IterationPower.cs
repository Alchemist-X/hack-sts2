namespace Sts2Sim.Core.Powers;

public sealed class IterationPower : PowerModel
{
    public const string PowerId = "ITERATION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || card.Type != CardType.Status)
        {
            return;
        }
        int ownerId = Owner.CombatId;
        int num2 = cs.History.CountOf<CardDrawnEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.Stamp.ActorId == ownerId && e.CardType == CardType.Status);
        if (num2 <= 1)
        {
            CardPileCmd.Draw(cs, Amount, OwnerPlayer());
        }
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} draws for its owner's player (Models.Powers/IterationPower.cs:63), but its owner is not a player");
}
