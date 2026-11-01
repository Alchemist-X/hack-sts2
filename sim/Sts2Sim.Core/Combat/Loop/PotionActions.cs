namespace Sts2Sim.Core.Combat.Loop;

public static class PotionActions
{
    public static PotionModel? GetPotionAtSlotIndex(PlayerState player, int index)
    {
        ArgumentNullException.ThrowIfNull(player);
        var slots = player.PotionSlots;
        if (index < 0 || index >= slots.Length)
        {
            throw new IndexOutOfRangeException($"Index {index} is not a valid potion slot index! Player has {slots.Length} potion slots");
        }
        return slots[index];
    }

    public static CreatureState? ResolveTarget(CombatState cs, PotionModel potion, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(potion);
        var player = potion.Owner;
        if (cs.IsInProgress)
        {
            if (target != null || !PotionModel.IsSingleTarget(potion.TargetType))
            {
                return target;
            }
            return player;
        }
        var targetPlayer = target as PlayerState;
        if (targetPlayer == null && potion.TargetType != TargetType.TargetedNoCreature)
        {
            return player;
        }
        return targetPlayer;
    }

    public static bool CanUse(CombatState cs, int slot, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var slots = cs.Player.PotionSlots;
        if (slot < 0 || slot >= slots.Length)
        {
            return false;
        }
        var potion = slots[slot];
        if (potion == null || potion.Owner.IsDead)
        {
            return false;
        }
        switch (potion.Usage)
        {
            case PotionUsage.None:
                throw new InvalidOperationException("No potions should have 'None' usage.");
            case PotionUsage.CombatOnly:
                if (!IsCombatOnlyUseEnabled(cs, potion.Owner))
                {
                    return false;
                }
                break;
            case PotionUsage.AnyTime:
                break;
            case PotionUsage.Automatic:
                return false;
            default:
                throw new ArgumentOutOfRangeException(nameof(potion.Usage), potion.Usage, null);
        }
        if (!potion.Owner.CanUseOrRemovePotions)
        {
            return false;
        }
        if (!potion.PassesCustomUsabilityCheck)
        {
            return false;
        }
        return potion.IsValidTarget(ResolveTarget(cs, potion, target));
    }

    public static bool CanDiscard(CombatState cs, int slot)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var slots = cs.Player.PotionSlots;
        if (slot < 0 || slot >= slots.Length)
        {
            return false;
        }
        var potion = slots[slot];
        if (potion == null || potion.Owner.IsDead)
        {
            return false;
        }
        return potion.Owner.CanUseOrRemovePotions;
    }

    public static void UsePotion(CombatState cs, int slot, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var potion = GetPotionAtSlotIndex(cs.Player, slot);
        if (potion == null)
        {
            return;
        }
        var creature = ResolveTarget(cs, potion, target);
        if (!potion.IsValidTarget(creature))
        {
            return;
        }
        using var context = cs.Choice.PushContext(ChoiceContextKind.Inline);
        potion.OnUseWrapper(cs.Run, creature);
    }

    public static void DiscardPotion(CombatState cs, int slot)
    {
        ArgumentNullException.ThrowIfNull(cs);
        var slots = cs.Player.PotionSlots;
        if (slot < 0 || slot >= slots.Length)
        {
            throw new IndexOutOfRangeException($"Tried to discard potion at slot index {slot}, but player {cs.Player.CombatId} only has {slots.Length} potion slots!");
        }
        var potion = slots[slot];
        if (potion == null)
        {
            return;
        }
        PotionCmd.Discard(cs.Run, potion);
    }

    private static bool IsCombatOnlyUseEnabled(CombatState cs, PlayerState owner)
        => cs.IsInProgress && cs.CurrentSide == owner.Side && owner.IsAlive;
}
