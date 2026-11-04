namespace Sts2Sim.Core.Combat.Pipeline;

public static class PotionCmd
{
    public static PotionProcureResult TryToProcure(RunState rs, string potionId, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(potionId);
        return TryToProcure(rs, PotionRegistry.Create(potionId), player);
    }

    public static PotionProcureResult TryToProcure(RunState rs, PotionModel potion, PlayerState player, int slotIndex = -1)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(potion);
        ArgumentNullException.ThrowIfNull(player);
        var cs = CombatOf(rs, player);
        if (!Hook.ShouldProcurePotion(rs, cs, potion, player))
        {
            return new PotionProcureResult(false, potion, PotionProcureFailureReason.NotAllowed);
        }
        var result = player.AddPotionInternal(potion, slotIndex);
        if (result.Success)
        {
            Hook.AfterPotionProcured(rs, cs, potion);
        }
        return result;
    }

    /// <summary>
    /// The outcome <see cref="TryToProcure(RunState, PotionModel, PlayerState, int)"/> with no slot index would have, without
    /// procuring: <see cref="PotionProcureFailureReason.NotAllowed"/> when a procure hook vetoes it (game
    /// Commands/PotionCmd.cs:258-266, e.g. Models.Relics/Sozu.cs:17-20), else <see cref="PotionProcureFailureReason.TooFull"/>
    /// when the belt has no empty slot (Entities.Players/Player.cs:1027-1031 and :1056-1068), else None (it would succeed).
    /// Pure: no hook side effect, no RNG.
    /// </summary>
    public static PotionProcureFailureReason ProcureCheck(RunState rs, PotionModel potion, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(potion);
        ArgumentNullException.ThrowIfNull(player);
        if (!Hook.ShouldProcurePotion(rs, CombatOf(rs, player), potion, player))
        {
            return PotionProcureFailureReason.NotAllowed;
        }
        return player.HasOpenPotionSlot ? PotionProcureFailureReason.None : PotionProcureFailureReason.TooFull;
    }

    /// <summary>True when <see cref="TryToProcure(RunState, PotionModel, PlayerState, int)"/> would add the potion (<see cref="ProcureCheck"/> is None).</summary>
    public static bool CanProcure(RunState rs, PotionModel potion, PlayerState player)
        => ProcureCheck(rs, potion, player) == PotionProcureFailureReason.None;

    public static void Discard(RunState rs, PotionModel potion)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(potion);
        potion.Discard();
        Hook.AfterPotionDiscarded(rs, CombatOf(rs, potion.Owner), potion);
    }

    private static CombatState? CombatOf(RunState rs, PlayerState player)
    {
        var cs = rs.Combat;
        return cs != null && CombatStatus.IsAttached(cs, player) ? cs : null;
    }
}
