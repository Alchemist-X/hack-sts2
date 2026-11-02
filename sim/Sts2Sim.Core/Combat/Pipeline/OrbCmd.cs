namespace Sts2Sim.Core.Combat.Pipeline;

public static class OrbCmd
{
    public const int MaxSlots = 10;

    public static void AddSlots(CombatState cs, PlayerState player, int amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var queue = player.OrbQueue;
        amount = Math.Min(MaxSlots - queue.Capacity, amount);
        queue.AddCapacity(amount);
    }

    public static void RemoveSlots(CombatState cs, PlayerState player, int amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var queue = player.OrbQueue;
        amount = Math.Min(queue.Capacity, amount);
        queue.RemoveCapacity(amount);
    }

    public static void Channel<T>(CombatState cs, PlayerState player) where T : OrbModel, new()
    {
        Channel(cs, new T(), player);
    }

    public static void Channel(CombatState cs, string orbId, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(orbId);
        Channel(cs, OrbRegistry.Create(orbId), player);
    }

    public static void Channel(CombatState cs, OrbModel orb, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(orb);
        ArgumentNullException.ThrowIfNull(player);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var orbQueue = player.OrbQueue;
        if (player.BaseOrbSlotCount == 0 && orbQueue.Capacity == 0)
        {
            AddSlots(cs, player, 1);
        }
        orb.AttachTo(player);
        if (orbQueue.Orbs.Count >= orbQueue.Capacity)
        {
            EvokeNext(cs, player);
        }
        if (player.OrbQueue.TryEnqueue(orb))
        {
            cs.History.AppendOrbChanneled(cs, orb);
            Hook.AfterOrbChanneled(cs, player, orb);
        }
    }

    public static void EvokeNext(CombatState cs, PlayerState player, bool dequeue = true)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        var orbs = player.OrbQueue.Orbs;
        if (orbs.Count > 0)
        {
            Evoke(cs, player, orbs[0], dequeue);
        }
    }

    public static void EvokeLast(CombatState cs, PlayerState player, bool dequeue = true)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        var orbs = player.OrbQueue.Orbs;
        if (orbs.Count > 0)
        {
            Evoke(cs, player, orbs[orbs.Count - 1], dequeue);
        }
    }

    private static void Evoke(CombatState cs, PlayerState player, OrbModel evokedOrb, bool dequeue = true)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var orbQueue = player.OrbQueue;
        if (orbQueue.Orbs.Count <= 0)
        {
            return;
        }
        bool removed = false;
        if (dequeue)
        {
            removed = orbQueue.Remove(evokedOrb);
        }
        var targets = evokedOrb.Evoke(cs);
        if (IsInCombat(cs, player))
        {
            Hook.AfterOrbEvoked(cs, evokedOrb, targets);
            if (removed)
            {
                evokedOrb.RemoveInternal();
            }
        }
    }

    public static void Passive(CombatState cs, OrbModel orb, CreatureState? target, bool countAffectedByHooks = false)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(orb);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        if (countAffectedByHooks)
        {
            orb.TriggerPassive(cs, target);
        }
        else
        {
            orb.Passive(cs, target);
        }
    }

    private static bool IsInCombat(CombatState cs, PlayerState player)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], player))
            {
                return true;
            }
        }
        return false;
    }
}
