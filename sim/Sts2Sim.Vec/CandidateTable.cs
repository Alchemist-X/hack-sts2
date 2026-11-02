using Sts2Sim.Core;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// Candidate rows (owner: V2). Row i describes <c>legal[i]</c> (LegalActions() order, CONTRACTS.md §D4/§D3.1 #25):
/// kind, A, B, hand_slot, target_slot, potion_slot, option_index, card_id, target_uid. Returns the number of rows written
/// (min(legal.Count, CAP_CANDIDATES)); only those indices are selectable by <c>step</c>.
/// </summary>
public static class CandidateTable
{
    /// <summary>STUB: clears the table and lists no candidates.</summary>
    public static int Fill(RunSim sim, IReadOnlyList<RunAction> legal, Span<int> cand, out bool overflow)
    {
        cand.Clear();
        overflow = false;
        return 0;
    }

    public static int Fill(RunSim sim, Span<int> cand, out bool overflow) => Fill(sim, sim.LegalActions(), cand, out overflow);
}
