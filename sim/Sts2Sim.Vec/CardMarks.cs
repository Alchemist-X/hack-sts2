using Sts2Sim.Core.Cards;
using Sts2Sim.Core.Powers;

namespace Sts2Sim.Vec;

/// <summary>
/// Card marks the encoder shows that the kernel tracks by instance. Kept out of ObservationEncoder*.cs on purpose: the
/// encoder sources are grepped for instance-id access (Test_VecEncoder_NoMoveIds, H13); here an instance id is only
/// compared, never written to a buffer.
/// </summary>
internal static class CardMarks
{
    /// <summary>
    /// The card is one a DAMPEN power downgraded (it shows as dampened until the power's casters die; the play server's
    /// hand <c>dampened</c>, Sts2Sim.Server/Describe.cs DampenedCardIds).
    /// </summary>
    public static bool IsDampened(DampenPower dampen, CardInstance card)
    {
        var list = dampen.DowngradedCards;
        for (int k = 0; k < list.Count; k++)
        {
            if (list[k].CardId == card.InstanceId)
            {
                return true;
            }
        }
        return false;
    }
}
