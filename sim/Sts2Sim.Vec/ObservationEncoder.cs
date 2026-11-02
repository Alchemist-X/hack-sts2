using Sts2Sim.Core;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Vec;

/// <summary>
/// Player-view observation encoder (owner: V1). Writes every observation buffer of one slot (<see cref="Buf.Observation"/>),
/// following CONTRACTS.md §D3.3. Does not touch cand / cand_n / signals (VecEnv and CandidateTable own those).
/// Sets <see cref="SlotStatus.TableOverflow"/> in <paramref name="status"/> when a table is truncated to its capacity.
/// May throw; VecEnv maps exceptions to CONTENT_GAP / SIM_ERROR.
/// </summary>
public static class ObservationEncoder
{
    /// <summary>STUB: zero-fills every table, then writes global_i.phase / decision_kind / is_combat.</summary>
    public static void Encode(RunSim sim, in SlotBuffers b, ref SlotStatus status)
    {
        b.ClearObservation();
        var gi = b.GlobalI;
        gi[GI.Phase] = (int)sim.Phase;
        gi[GI.DecisionKind] = (int)(sim.HasPendingChoice ? RunPhase.Prompt : sim.Phase);
        gi[GI.IsCombat] = sim.State.Combat is { IsInProgress: true } ? 1 : 0;
    }

    /// <summary>Observation of a slot that holds no run: every observation buffer zero.</summary>
    public static void EncodeEmpty(in SlotBuffers b) => b.ClearObservation();
}
