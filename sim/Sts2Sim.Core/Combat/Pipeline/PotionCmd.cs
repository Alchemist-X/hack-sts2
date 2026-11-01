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
