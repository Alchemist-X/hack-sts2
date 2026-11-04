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
    /// True when <see cref="TryToProcure(RunState, PotionModel, PlayerState, int)"/> with no slot index would add
    /// <paramref name="potion"/> to <paramref name="player"/>'s belt: the procure hooks allow it (game
    /// Commands/PotionCmd.cs:258-266, NotAllowed otherwise, e.g. Models.Relics/Sozu.cs:17-20) and the belt has an empty
    /// slot (Entities.Players/Player.cs:1027-1031 and :1056-1068, TooFull otherwise). Pure: no hook side effect, no RNG.
    /// </summary>
    public static bool CanProcure(RunState rs, PotionModel potion, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(potion);
        ArgumentNullException.ThrowIfNull(player);
        return player.HasOpenPotionSlot && Hook.ShouldProcurePotion(rs, CombatOf(rs, player), potion, player);
    }

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
