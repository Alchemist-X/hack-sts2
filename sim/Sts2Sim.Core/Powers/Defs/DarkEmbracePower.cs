namespace Sts2Sim.Core.Powers;

public sealed class DarkEmbracePower : PowerModel
{
    public const string PowerId = "DARK_EMBRACE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int EtherealCount { get; private set; }

    public override void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        if (!causedByEthereal)
        {
            CardPileCmd.Draw(cs, Amount, OwnerPlayer());
            return;
        }
        EtherealCount++;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        CardPileCmd.Draw(cs, Amount * EtherealCount, OwnerPlayer());
        EtherealCount = 0;
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} draws for its owner's player (Models.Powers/DarkEmbracePower.cs:64-78), but its owner is not a player");

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(EtherealCount);
    }
}
