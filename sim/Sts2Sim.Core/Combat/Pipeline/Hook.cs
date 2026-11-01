using System.Diagnostics.CodeAnalysis;

namespace Sts2Sim.Core.Combat.Pipeline;

public static class Hook
{
    public static void IterateCombatHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Guarded, buffer);

    public static void IterateHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Combat, buffer);

    public static void IterateRunHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Run, buffer);

    public static void IterateRunHookListeners(RunState rs, CombatState? cs, List<AbstractModel> buffer)
        => throw new NotImplementedException("Hook.IterateRunHookListeners(rs, cs): hooks agent (Runs/RunState.cs:829-933)");

    private static void Fill(CombatState cs, ListenerSource source, List<AbstractModel> buffer)
    {
        buffer.Clear();
        using var listeners = ListenerSnapshot.Take(cs, source);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                buffer.Add(model);
            }
        }
    }

    private static void AddModifier(ref List<AbstractModel>? modifiers, AbstractModel model)
    {
        (modifiers ??= new List<AbstractModel>(2)).Add(model);
    }

    private static IReadOnlyList<CreatureState> SideTurnParticipants(CombatState cs, CombatSide side)
        => side == CombatSide.Enemy ? cs.Monsters : cs.Allies;

    private static void RequireAttachedCombat(RunState rs, [NotNull] CombatState? cs, string dispatcher)
    {
        if (cs is null || !ReferenceEquals(rs.Combat, cs))
        {
            ThrowNoAttachedCombat(cs, dispatcher);
        }
    }

    [DoesNotReturn]
    private static void ThrowNoAttachedCombat(CombatState? cs, string dispatcher)
    {
        if (cs is null)
        {
            throw new NotImplementedException(dispatcher + " without a combat: hooks agent (run listeners without a combat, Runs/RunState.cs:829-933)");
        }
        throw new InvalidOperationException(dispatcher + ": cs must be rs.Combat, the combat attached to the run (CONTRACTS Part B section B3)");
    }

    private static bool ContainsModel(IReadOnlyList<AbstractModel> models, AbstractModel model)
    {
        for (int i = 0; i < models.Count; i++)
        {
            if (ReferenceEquals(models[i], model))
            {
                return true;
            }
        }
        return false;
    }

    public static decimal ModifyDamage(CombatState cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType = ModifyDamageHookType.All)
        => ModifyDamage(cs.Run, cs, target, dealer, damage, props, cardSource, cardPlay, hookType);

    public static decimal ModifyDamage(CombatState cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
        => ModifyDamage(cs.Run, cs, target, dealer, damage, props, cardSource, cardPlay, hookType, ref modifiers);

    public static decimal ModifyDamage(RunState rs, CombatState? cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType = ModifyDamageHookType.All)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyDamage(rs, cs, target, dealer, damage, props, cardSource, cardPlay, hookType, ref modifiers);
    }

    public static decimal ModifyDamage(RunState rs, CombatState? cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
    {
        RequireAttachedCombat(rs, cs, "Hook.ModifyDamage (Hooks/Hook.cs:10328)");
        decimal num = damage;
        num = ModifyDamageInternal(rs, cs, target, dealer, num, props, cardSource, cardPlay, hookType, ref modifiers);
        return Math.Max(0m, num);
    }

    private static decimal ModifyDamageInternal(RunState rs, CombatState cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
    {
        decimal num = damage;
        if ((hookType & ModifyDamageHookType.Additive) != 0)
        {
            using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal add = model.ModifyDamageAdditive(rs, target, num, props, dealer, cardSource, cardPlay);
                num += add;
                if (add != 0m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        if ((hookType & ModifyDamageHookType.Multiplicative) != 0)
        {
            using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal mul = model.ModifyDamageMultiplicative(rs, target, num, props, dealer, cardSource, cardPlay);
                num *= mul;
                if (mul != 1m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        if ((hookType & ModifyDamageHookType.Cap) != 0)
        {
            decimal cap = decimal.MaxValue;
            using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal c = model.ModifyDamageCap(rs, target, props, dealer, cardSource, cardPlay);
                if (c < cap)
                {
                    cap = c;
                    if (num > c)
                    {
                        num = c;
                        AddModifier(ref modifiers, model);
                    }
                }
            }
        }
        return num;
    }

    public static decimal ModifyBlock(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyBlock(cs, target, block, props, cardSource, cardPlay, ref modifiers);
    }

    public static decimal ModifyBlock(CombatState cs, CreatureState target, decimal block, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ref List<AbstractModel>? modifiers)
    {
        decimal num = block;
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal add = model.ModifyBlockAdditive(cs, target, num, props, cardSource, cardPlay);
                num += add;
                if (add != 0m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal mul = model.ModifyBlockMultiplicative(cs, target, num, props, cardSource, cardPlay);
                num *= mul;
                if (mul != 1m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        return Math.Max(0m, num);
    }

    public static decimal ModifyHpLost(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases)
        => ModifyHpLost(cs.Run, cs, target, amount, props, dealer, cardSource, phases);

    public static decimal ModifyHpLost(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases, ref List<AbstractModel>? modifiers)
        => ModifyHpLost(cs.Run, cs, target, amount, props, dealer, cardSource, phases, ref modifiers);

    public static decimal ModifyHpLost(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyHpLost(rs, cs, target, amount, props, dealer, cardSource, phases, ref modifiers);
    }

    public static decimal ModifyHpLost(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases, ref List<AbstractModel>? modifiers)
    {
        RequireAttachedCombat(rs, cs, "Hook.ModifyHpLost (Hooks/Hook.cs:10627)");
        decimal num = amount;
        if ((phases & HpLossHookPhase.BeforeOsty) != 0)
        {
            using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    if (!listeners.TryGetLive(cs, i, out var model))
                    {
                        continue;
                    }
                    decimal before = num;
                    num = model.ModifyHpLostBeforeOsty(rs, target, num, props, dealer, cardSource);
                    if (decimal.Truncate(before) != decimal.Truncate(num))
                    {
                        AddModifier(ref modifiers, model);
                    }
                }
            }
            using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    if (!listeners.TryGetLive(cs, i, out var model))
                    {
                        continue;
                    }
                    decimal before = num;
                    num = model.ModifyHpLostBeforeOstyLate(rs, target, num, props, dealer, cardSource);
                    if (decimal.Truncate(before) != decimal.Truncate(num))
                    {
                        AddModifier(ref modifiers, model);
                    }
                }
            }
        }
        if ((phases & HpLossHookPhase.AfterOsty) != 0)
        {
            using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    if (!listeners.TryGetLive(cs, i, out var model))
                    {
                        continue;
                    }
                    decimal before = num;
                    num = model.ModifyHpLostAfterOsty(rs, target, num, props, dealer, cardSource);
                    if (decimal.Truncate(before) != decimal.Truncate(num))
                    {
                        AddModifier(ref modifiers, model);
                    }
                }
            }
            using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
            {
                for (int i = 0; i < listeners.Count; i++)
                {
                    if (!listeners.TryGetLive(cs, i, out var model))
                    {
                        continue;
                    }
                    decimal before = num;
                    num = model.ModifyHpLostAfterOstyLate(rs, target, num, props, dealer, cardSource);
                    if (decimal.Truncate(before) != decimal.Truncate(num))
                    {
                        AddModifier(ref modifiers, model);
                    }
                }
            }
        }
        return num;
    }

    public static CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState originalTarget, decimal amount, ValueProp props, CreatureState? dealer)
    {
        var creature = originalTarget;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                creature = model.ModifyUnblockedDamageTarget(cs, creature, amount, props, dealer);
            }
        }
        return creature;
    }

    public static int ModifyAttackHitCount(CombatState cs, AttackCommand attack, int originalHitCount)
    {
        int num = originalHitCount;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                num = model.ModifyAttackHitCount(cs, attack, num);
            }
        }
        return num;
    }

    public static int ModifyCardPlayCount(CombatState cs, CardInstance card, int playCount, CreatureState? target)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyCardPlayCount(cs, card, playCount, target, ref modifiers);
    }

    public static int ModifyCardPlayCount(CombatState cs, CardInstance card, int playCount, CreatureState? target, ref List<AbstractModel>? modifiers)
    {
        int num = playCount;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (!listeners.TryGetLive(cs, i, out var model))
            {
                continue;
            }
            int before = num;
            num = model.ModifyCardPlayCount(cs, card, target, num);
            if (num != before)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static int ModifyXValue(CombatState cs, CardInstance card, int originalValue)
    {
        int num = originalValue;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                num = model.ModifyXValue(cs, card, num);
            }
        }
        return num;
    }

    public static decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal originalCardCount)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyHandDraw(cs, player, originalCardCount, ref modifiers);
    }

    public static decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal originalCardCount, ref List<AbstractModel>? modifiers)
    {
        decimal num = originalCardCount;
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal before = num;
                num = model.ModifyHandDraw(cs, player, num);
                if ((int)before != (int)num)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal before = num;
                num = model.ModifyHandDrawLate(cs, player, num);
                if ((int)before != (int)num)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        return num;
    }

    public static decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal originalAmount)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyEnergyGain(cs, player, originalAmount, ref modifiers);
    }

    public static decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal originalAmount, ref List<AbstractModel>? modifiers)
    {
        decimal num = originalAmount;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (!listeners.TryGetLive(cs, i, out var model))
            {
                continue;
            }
            decimal before = num;
            num = model.ModifyEnergyGain(cs, player, num);
            if ((int)before != (int)num)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        decimal num = amount;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                num = model.ModifyMaxEnergy(cs, player, num);
            }
        }
        return num;
    }

    public static decimal ModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost)
    {
        if (originalCost < 0m)
        {
            return originalCost;
        }
        decimal modifiedCost = originalCost;
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.TryModifyEnergyCostInCombat(cs, card, modifiedCost, out modifiedCost);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.TryModifyEnergyCostInCombatLate(cs, card, modifiedCost, out modifiedCost);
                }
            }
        }
        return modifiedCost;
    }

    public static decimal ModifyStarCost(CombatState cs, CardInstance card, decimal originalCost)
    {
        if (originalCost < 0m)
        {
            return originalCost;
        }
        decimal modifiedCost = originalCost;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.TryModifyStarCost(cs, card, modifiedCost, out modifiedCost);
            }
        }
        return modifiedCost;
    }

    public static void ModifyShuffleOrder(CombatState cs, PlayerState player, List<CardInstance> cards, bool isInitialShuffle)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.ModifyShuffleOrder(cs, player, cards, isInitialShuffle);
            }
        }
    }

    public static decimal ModifyPowerAmountGiven(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyPowerAmountGiven(cs, power, giver, amount, target, cardSource, ref modifiers);
    }

    public static decimal ModifyPowerAmountGiven(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource, ref List<AbstractModel>? modifiers)
    {
        decimal num = amount;
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal add = model.ModifyPowerAmountGivenAdditive(cs, power, giver, num, target, cardSource);
                num += add;
                if (add != 0m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (!listeners.TryGetLive(cs, i, out var model))
                {
                    continue;
                }
                decimal mul = model.ModifyPowerAmountGivenMultiplicative(cs, power, giver, num, target, cardSource);
                num *= mul;
                if (mul != 1m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        return num;
    }

    public static decimal ModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? giver)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyPowerAmountReceived(cs, canonicalPower, target, amount, giver, ref modifiers);
    }

    public static decimal ModifyPowerAmountReceived(CombatState cs, PowerModel canonicalPower, CreatureState target, decimal amount, CreatureState? giver, ref List<AbstractModel>? modifiers)
    {
        decimal num = amount;
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (!listeners.TryGetLive(cs, i, out var model))
            {
                continue;
            }
            if (model.TryModifyPowerAmountReceived(cs, canonicalPower, target, num, giver, out var modifiedAmount))
            {
                num = modifiedAmount;
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static bool ShouldAllowHitting(CombatState cs, CreatureState creature)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldAllowHitting(cs, creature))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldAllowTargeting(CombatState cs, CreatureState target) => ShouldAllowTargeting(cs, target, out _);

    public static bool ShouldAllowTargeting(CombatState cs, CreatureState target, out AbstractModel? preventer)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldAllowTargeting(cs, target))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldClearBlock(CombatState cs, CreatureState creature) => ShouldClearBlock(cs, creature, out _);

    public static bool ShouldClearBlock(CombatState cs, CreatureState creature, out AbstractModel? preventer)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldClearBlock(cs, creature))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldDie(CombatState cs, CreatureState creature) => ShouldDie(cs.Run, cs, creature, out _);

    public static bool ShouldDie(CombatState cs, CreatureState creature, out AbstractModel? preventer) => ShouldDie(cs.Run, cs, creature, out preventer);

    public static bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature) => ShouldDie(rs, cs, creature, out _);

    public static bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature, out AbstractModel? preventer)
    {
        RequireAttachedCombat(rs, cs, "Hook.ShouldDie (Hooks/Hook.cs:11365)");
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldDie(rs, creature))
                {
                    preventer = model;
                    return false;
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldDieLate(rs, creature))
                {
                    preventer = model;
                    return false;
                }
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw) => ShouldDraw(cs, player, fromHandDraw, out _);

    public static bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw, out AbstractModel? modifier)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldDraw(cs, player, fromHandDraw))
            {
                modifier = model;
                return false;
            }
        }
        modifier = null;
        return true;
    }

    public static bool ShouldEtherealTrigger(CombatState cs, CardInstance card)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldEtherealTrigger(cs, card))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldPlay(CombatState cs, CardInstance card) => ShouldPlay(cs, card, out _, AutoPlayType.None);

    public static bool ShouldPlay(CombatState cs, CardInstance card, out AbstractModel? preventer) => ShouldPlay(cs, card, out preventer, AutoPlayType.None);

    public static bool ShouldPlay(CombatState cs, CardInstance card, out AbstractModel? preventer, AutoPlayType autoPlayType)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldPlay(cs, card, autoPlayType))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && model.ShouldPayExcessEnergyCostWithStars(cs, player))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldPlayerResetEnergy(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldPlayerResetEnergy(cs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldFlush(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldFlush(cs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldTakeExtraTurn(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && model.ShouldTakeExtraTurn(cs, player))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, creature))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldStopCombatFromEnding(CombatState cs)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && model.ShouldStopCombatFromEnding(cs))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldPowerBeRemovedOnDeath(CombatState cs, PowerModel power)
    {
        if (!CombatStatus.IsAttached(cs, power.Owner))
        {
            return true;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldPowerBeRemovedOnDeath(cs, power))
            {
                return false;
            }
        }
        return true;
    }

    public static void BeforeCombatStart(CombatState cs) => BeforeCombatStart(cs.Run, cs);

    public static void BeforeCombatStart(RunState rs, CombatState? cs)
    {
        RequireAttachedCombat(rs, cs, "Hook.BeforeCombatStart (Hooks/Hook.cs:9071)");
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeCombatStart(rs);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeCombatStartLate(rs);
                }
            }
        }
    }

    public static void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCreatureAddedToCombat(cs, creature);
            }
        }
    }

    public static void BeforeSideTurnStart(CombatState cs, CombatSide side) => BeforeSideTurnStart(cs, side, SideTurnParticipants(cs, side));

    public static void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeSideTurnStart(cs, side, participants);
            }
        }
    }

    public static void AfterSideTurnStart(CombatState cs, CombatSide side) => AfterSideTurnStart(cs, side, SideTurnParticipants(cs, side));

    public static void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnStart(cs, side, participants);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnStartLate(cs, side, participants);
                }
            }
        }
    }

    public static void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterPlayerTurnStartEarly(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterPlayerTurnStart(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterPlayerTurnStartLate(cs, player);
                }
            }
        }
    }

    public static void BeforeSideTurnEnd(CombatState cs, CombatSide side) => BeforeSideTurnEnd(cs, side, SideTurnParticipants(cs, side));

    public static void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEndVeryEarly(cs, side, participants);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEndEarly(cs, side, participants);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEnd(cs, side, participants);
                }
            }
        }
    }

    public static void AfterSideTurnEnd(CombatState cs, CombatSide side) => AfterSideTurnEnd(cs, side, SideTurnParticipants(cs, side));

    public static void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnEnd(cs, side, participants);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnEndLate(cs, side, participants);
                }
            }
        }
    }

    public static void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterTakingExtraTurn(cs, player);
            }
        }
    }

    public static void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterEnergyReset(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterEnergyResetLate(cs, player);
                }
            }
        }
    }

    public static void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeHandDraw(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeHandDrawLate(cs, player);
                }
            }
        }
    }

    public static void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardDrawnEarly(cs, card, fromHandDraw);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardDrawn(cs, card, fromHandDraw);
                }
            }
        }
    }

    public static void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCardEnteredCombat(cs, card);
            }
        }
    }

    public static void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCardGeneratedForCombat(cs, card, creator);
            }
        }
    }

    public static void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterShuffle(cs, shuffler);
            }
        }
    }

    public static void AfterHandEmptied(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterHandEmptied(cs, player);
            }
        }
    }

    public static void BeforeFlush(CombatState cs, PlayerState player)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeFlush(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeFlushLate(cs, player);
                }
            }
        }
    }

    public static void AfterFlush(CombatState cs, PlayerState player, IReadOnlyList<CardInstance> flushedCards, IReadOnlyList<CardInstance> retainedCards)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterFlush(cs, player, flushedCards, retainedCards);
            }
        }
    }

    public static void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeCardPlayed(cs, cardPlay);
            }
        }
    }

    public static void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardPlayed(cs, cardPlay);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardPlayedLate(cs, cardPlay);
                }
            }
        }
    }

    public static void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCardExhausted(cs, card, causedByEthereal);
            }
        }
    }

    public static void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCardDiscarded(cs, card);
            }
        }
    }

    public static void AfterEnergySpent(CombatState cs, CardInstance card, int amount)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterEnergySpent(cs, card, amount);
            }
        }
    }

    public static void BeforeAttack(CombatState cs, AttackCommand command)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeAttack(cs, command);
            }
        }
    }

    public static void AfterAttack(CombatState cs, AttackCommand command)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterAttack(cs, command);
            }
        }
    }

    public static void BeforeDamageReceived(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        => BeforeDamageReceived(cs.Run, cs, target, amount, props, dealer, cardSource);

    public static void BeforeDamageReceived(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        RequireAttachedCombat(rs, cs, "Hook.BeforeDamageReceived (Hooks/Hook.cs:9179)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeDamageReceived(rs, target, amount, props, dealer, cardSource);
            }
        }
    }

    public static void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterDamageGiven(cs, dealer, result, props, target, cardSource);
            }
        }
    }

    public static void AfterDamageReceived(CombatState cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        => AfterDamageReceived(cs.Run, cs, target, result, props, dealer, cardSource);

    public static void AfterDamageReceived(RunState rs, CombatState? cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterDamageReceived (Hooks/Hook.cs:9199)");
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterDamageReceived(rs, target, result, props, dealer, cardSource);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterDamageReceivedLate(rs, target, result, props, dealer, cardSource);
                }
            }
        }
    }

    public static void AfterCurrentHpChanged(CombatState cs, CreatureState creature, decimal delta) => AfterCurrentHpChanged(cs.Run, cs, creature, delta);

    public static void AfterCurrentHpChanged(RunState rs, CombatState? cs, CreatureState creature, decimal delta)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterCurrentHpChanged (Hooks/Hook.cs:9144)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCurrentHpChanged(rs, creature, delta);
            }
        }
    }

    public static void BeforeBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeBlockGained(cs, creature, amount, props, cardSource);
            }
        }
    }

    public static void AfterBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterBlockGained(cs, creature, amount, props, cardSource);
            }
        }
    }

    public static void AfterBlockBroken(CombatState cs, CreatureState target, CreatureState? breaker)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterBlockBroken(cs, target, breaker);
            }
        }
    }

    public static void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterBlockCleared(cs, creature);
            }
        }
    }

    public static void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforePowerAmountChanged(cs, power, amount, target, applier, cardSource);
            }
        }
    }

    public static void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterPowerAmountChanged(cs, power, amount, applier, cardSource);
            }
        }
    }

    public static void BeforeDeath(CombatState cs, CreatureState creature) => BeforeDeath(cs.Run, cs, creature);

    public static void BeforeDeath(RunState rs, CombatState? cs, CreatureState creature)
    {
        RequireAttachedCombat(rs, cs, "Hook.BeforeDeath (Hooks/Hook.cs:9219)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeDeath(rs, creature);
            }
        }
    }

    public static void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented) => AfterDeath(cs.Run, cs, creature, wasRemovalPrevented);

    public static void AfterDeath(RunState rs, CombatState? cs, CreatureState creature, bool wasRemovalPrevented)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterDeath (Hooks/Hook.cs:9234)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterDeath(rs, creature, wasRemovalPrevented);
            }
        }
    }

    public static void AfterModifyingDamageAmount(CombatState cs, CardInstance? cardSource, IReadOnlyList<AbstractModel>? modifiers)
        => AfterModifyingDamageAmount(cs.Run, cs, cardSource, modifiers);

    public static void AfterModifyingDamageAmount(RunState rs, CombatState? cs, CardInstance? cardSource, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        RequireAttachedCombat(rs, cs, "Hook.AfterModifyingDamageAmount (Hooks/Hook.cs:9462)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingDamageAmount(rs, cardSource);
            }
        }
    }

    public static void AfterModifyingBlockAmount(CombatState cs, decimal modifiedBlock, CardInstance? cardSource, CardPlay? cardPlay, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingBlockAmount(cs, modifiedBlock, cardSource, cardPlay);
            }
        }
    }

    public static void AfterModifyingHpLostBeforeOsty(CombatState cs, IReadOnlyList<AbstractModel>? modifiers) => AfterModifyingHpLostBeforeOsty(cs.Run, cs, modifiers);

    public static void AfterModifyingHpLostBeforeOsty(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        RequireAttachedCombat(rs, cs, "Hook.AfterModifyingHpLostBeforeOsty (Hooks/Hook.cs:9523)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingHpLostBeforeOsty(rs);
            }
        }
    }

    public static void AfterModifyingHpLostAfterOsty(CombatState cs, IReadOnlyList<AbstractModel>? modifiers) => AfterModifyingHpLostAfterOsty(cs.Run, cs, modifiers);

    public static void AfterModifyingHpLostAfterOsty(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        RequireAttachedCombat(rs, cs, "Hook.AfterModifyingHpLostAfterOsty (Hooks/Hook.cs:9538)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingHpLostAfterOsty(rs);
            }
        }
    }

    public static void AfterModifyingPowerAmountGiven(CombatState cs, IReadOnlyList<AbstractModel>? modifiers, PowerModel modifiedPower)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingPowerAmountGiven(cs, modifiedPower);
            }
        }
    }

    public static void AfterModifyingPowerAmountReceived(CombatState cs, IReadOnlyList<AbstractModel>? modifiers, PowerModel modifiedPower)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingPowerAmountReceived(cs, modifiedPower);
            }
        }
    }

    public static void AfterModifyingEnergyGain(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingEnergyGain(cs);
            }
        }
    }

    public static void AfterModifyingHandDraw(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingHandDraw(cs);
            }
        }
    }

    public static void AfterModifyingCardPlayCount(CombatState cs, CardInstance card, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingCardPlayCount(cs, card);
            }
        }
    }

    public static void AfterPreventingBlockClear(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
        if (ListenerSnapshot.Contains(cs, ListenerSource.Guarded, preventer))
        {
            preventer.AfterPreventingBlockClear(cs, preventer, creature);
        }
    }

    public static void AfterPreventingDeath(CombatState cs, AbstractModel preventer, CreatureState creature) => AfterPreventingDeath(cs.Run, cs, preventer, creature);

    public static void AfterPreventingDeath(RunState rs, CombatState? cs, AbstractModel preventer, CreatureState creature)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterPreventingDeath (Hooks/Hook.cs:9816)");
        if (ListenerSnapshot.Contains(cs, ListenerSource.Run, preventer))
        {
            preventer.AfterPreventingDeath(rs, creature);
        }
    }

    public static void AfterPreventingDraw(CombatState cs, AbstractModel modifier)
    {
        if (ListenerSnapshot.Contains(cs, ListenerSource.Guarded, modifier))
        {
            modifier.AfterPreventingDraw(cs);
        }
    }

    public static void AfterActEntered(RunState rs)
        => throw new NotImplementedException("Hook.AfterActEntered: hooks agent (Hooks/Hook.cs:8813)");

    public static void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeCardAutoPlayed(cs, card, target, type);
            }
        }
    }

    public static void AfterCardChangedPiles(RunState rs, CombatState? cs, CardInstance card, PileType oldPile, AbstractModel? clonedBy)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterCardChangedPiles (Hooks/Hook.cs:8935)");
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardChangedPiles(rs, card, oldPile, clonedBy);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCardChangedPilesLate(rs, card, oldPile, clonedBy);
                }
            }
        }
    }

    public static void BeforeCardRemoved(RunState rs, CardInstance card)
        => throw new NotImplementedException("Hook.BeforeCardRemoved: hooks agent (Hooks/Hook.cs:9057)");

    public static void AfterCombatEnd(RunState rs, CombatState? cs, CombatRoom? room)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterCombatEnd (Hooks/Hook.cs:9085)");
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCombatEnd(rs, room);
            }
        }
    }

    public static void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)
        => throw new NotImplementedException("Hook.BeforeCombatRewardOffered: hooks agent (Hooks/Hook.cs:9100)");

    public static void AfterCombatVictory(RunState rs, CombatState? cs, CombatRoom? room)
    {
        RequireAttachedCombat(rs, cs, "Hook.AfterCombatVictory (Hooks/Hook.cs:9115)");
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCombatVictoryEarly(rs, room);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterCombatVictory(rs, room);
                }
            }
        }
    }

    public static void AfterGoldGained(RunState rs, PlayerState player)
        => throw new NotImplementedException("Hook.AfterGoldGained: hooks agent (Hooks/Hook.cs:9251)");

    public static void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)
        => throw new NotImplementedException("Hook.AfterDiedToDoom: hooks agent (Hooks/Hook.cs:9265)");

    public static void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)
        => throw new NotImplementedException("Hook.AfterForge: hooks agent (Hooks/Hook.cs:9339)");

    public static void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)
        => throw new NotImplementedException("Hook.AfterItemPurchased: hooks agent (Hooks/Hook.cs:9385)");

    public static void AfterMapGenerated(RunState rs, ActMap map, int actIndex)
        => throw new NotImplementedException("Hook.AfterMapGenerated: hooks agent (Hooks/Hook.cs:9401)");

    public static void AfterModifyingCardRewardOptions(RunState rs, IReadOnlyList<AbstractModel>? modifiers)
        => throw new NotImplementedException("Hook.AfterModifyingCardRewardOptions: hooks agent (Hooks/Hook.cs:9448)");

    public static void AfterModifyingGoldGained(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers, PlayerState player, decimal amount)
        => throw new NotImplementedException("Hook.AfterModifyingGoldGained: hooks agent (Hooks/Hook.cs:9492)");

    public static void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb, IReadOnlyList<AbstractModel>? modifiers)
        => throw new NotImplementedException("Hook.AfterModifyingOrbPassiveTriggerCount: hooks agent (Hooks/Hook.cs:9553)");

    public static void AfterModifyingRewards(RunState rs, IReadOnlyList<AbstractModel>? modifiers)
        => throw new NotImplementedException("Hook.AfterModifyingRewards: hooks agent (Hooks/Hook.cs:9598)");

    public static void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)
        => throw new NotImplementedException("Hook.AfterOrbChanneled: hooks agent (Hooks/Hook.cs:9612)");

    public static void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)
        => throw new NotImplementedException("Hook.AfterOrbEvoked: hooks agent (Hooks/Hook.cs:9628)");

    public static void AfterOstyRevived(CombatState cs, CreatureState osty)
        => throw new NotImplementedException("Hook.AfterOstyRevived: hooks agent (Hooks/Hook.cs:9644)");

    public static void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterAutoPostPlayPhaseEntered(cs, player);
            }
        }
    }

    public static void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterAutoPrePlayPhaseEnteredEarly(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterAutoPrePlayPhaseEntered(cs, player);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterAutoPrePlayPhaseEnteredLate(cs, player);
                }
            }
        }
    }

    public static void AfterPotionDiscarded(RunState rs, CombatState? cs, PotionModel potion)
        => throw new NotImplementedException("Hook.AfterPotionDiscarded: hooks agent (Hooks/Hook.cs:9703)");

    public static void AfterPotionProcured(RunState rs, CombatState? cs, PotionModel potion)
        => throw new NotImplementedException("Hook.AfterPotionProcured: hooks agent (Hooks/Hook.cs:9718)");

    public static void BeforePotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)
        => throw new NotImplementedException("Hook.BeforePotionUsed: hooks agent (Hooks/Hook.cs:9733)");

    public static void AfterPotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)
        => throw new NotImplementedException("Hook.AfterPotionUsed: hooks agent (Hooks/Hook.cs:9749)");

    public static void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
        => throw new NotImplementedException("Hook.AfterRestSiteHeal: hooks agent (Hooks/Hook.cs:9846)");

    public static void AfterRestSiteSmith(RunState rs, PlayerState player)
        => throw new NotImplementedException("Hook.AfterRestSiteSmith: hooks agent (Hooks/Hook.cs:9861)");

    public static void AfterRewardTaken(RunState rs, PlayerState player, Reward reward)
        => throw new NotImplementedException("Hook.AfterRewardTaken: hooks agent (Hooks/Hook.cs:9875)");

    public static void BeforeRoomEntered(RunState rs, AbstractRoom room)
        => throw new NotImplementedException("Hook.BeforeRoomEntered: hooks agent (Hooks/Hook.cs:9890)");

    public static void AfterRoomEntered(RunState rs, AbstractRoom room)
        => throw new NotImplementedException("Hook.AfterRoomEntered: hooks agent (Hooks/Hook.cs:9904)");

    public static void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)
        => throw new NotImplementedException("Hook.AfterStarsGained: hooks agent (Hooks/Hook.cs:9963)");

    public static void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
        => throw new NotImplementedException("Hook.AfterStarsSpent: hooks agent (Hooks/Hook.cs:9978)");

    public static void AfterSummon(CombatState cs, PlayerState summoner, decimal amount)
        => throw new NotImplementedException("Hook.AfterSummon: hooks agent (Hooks/Hook.cs:9993)");

    public static CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card)
    {
        List<AbstractModel>? modifyingModels = null;
        return ModifyCardBeingAddedToDeck(rs, card, ref modifyingModels);
    }

    public static CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card, ref List<AbstractModel>? modifyingModels)
        => throw new NotImplementedException("Hook.ModifyCardBeingAddedToDeck: hooks agent (Hooks/Hook.cs:10121)");

    public static CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyCardPlayResultLocation(cs, card, isAutoPlay, resources, cardLocation, ref modifiers);
    }

    public static CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation, ref List<AbstractModel>? modifiers)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (!listeners.TryGetLive(cs, i, out var model))
            {
                continue;
            }
            var before = cardLocation;
            cardLocation = model.ModifyCardPlayResultLocation(cs, card, isAutoPlay, resources, cardLocation);
            if (before != cardLocation)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return cardLocation;
    }

    public static IEnumerable<AbstractModel> ModifyCardRewardAlternatives(RunState rs, PlayerState player, CardReward cardReward, List<CardRewardAlternative> alternatives)
        => throw new NotImplementedException("Hook.ModifyCardRewardAlternatives: hooks agent (Hooks/Hook.cs:10213)");

    public static CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options)
        => throw new NotImplementedException("Hook.ModifyCardRewardCreationOptions: hooks agent (Hooks/Hook.cs:10235)");

    public static bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        List<AbstractModel>? modifiers = null;
        return TryModifyCardRewardOptions(rs, player, cardRewardOptions, creationOptions, ref modifiers);
    }

    public static bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions, ref List<AbstractModel>? modifiers)
        => throw new NotImplementedException("Hook.TryModifyCardRewardOptions: hooks agent (Hooks/Hook.cs:10266)");

    public static decimal ModifyCardRewardUpgradeOdds(RunState rs, PlayerState player, CardInstance card, decimal originalOdds)
        => throw new NotImplementedException("Hook.ModifyCardRewardUpgradeOdds: hooks agent (Hooks/Hook.cs:10309)");

    public static void ModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Combat);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.TryModifyKeywordsInCombat(cs, card, keywords);
            }
        }
    }

    public static decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyGoldGained(rs, cs, amount, player, ref modifiers);
    }

    public static decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player, ref List<AbstractModel>? modifiers)
        => throw new NotImplementedException("Hook.ModifyGoldGained: hooks agent (Hooks/Hook.cs:10501)");

    public static IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> extraText)
        => throw new NotImplementedException("Hook.ModifyExtraRestSiteHealText: hooks agent (Hooks/Hook.cs:10527)");

    public static ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)
        => throw new NotImplementedException("Hook.ModifyGeneratedMap: hooks agent (Hooks/Hook.cs:10545)");

    public static ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)
        => throw new NotImplementedException("Hook.ModifyGeneratedMapLate: hooks agent (Hooks/Hook.cs:10564)");

    public static void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
        => throw new NotImplementedException("Hook.ModifyMerchantCardCreationResults: hooks agent (Hooks/Hook.cs:10732)");

    public static IEnumerable<CardDef> ModifyMerchantCardPool(RunState rs, PlayerState player, IEnumerable<CardDef> options)
        => throw new NotImplementedException("Hook.ModifyMerchantCardPool: hooks agent (Hooks/Hook.cs:10749)");

    public static CardRarity ModifyMerchantCardRarity(RunState rs, PlayerState player, CardRarity rarity)
        => throw new NotImplementedException("Hook.ModifyMerchantCardRarity: hooks agent (Hooks/Hook.cs:10767)");

    public static decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal result)
        => throw new NotImplementedException("Hook.ModifyMerchantPrice: hooks agent (Hooks/Hook.cs:10785)");

    public static EventModel ModifyNextEvent(RunState rs, EventModel currentEvent)
        => throw new NotImplementedException("Hook.ModifyNextEvent: hooks agent (Hooks/Hook.cs:10803)");

    public static float ModifyOddsIncreaseForUnrolledRoomType(RunState rs, RoomType roomType, float oddsIncrease)
        => throw new NotImplementedException("Hook.ModifyOddsIncreaseForUnrolledRoomType: hooks agent (Hooks/Hook.cs:10822)");

    public static int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount)
    {
        List<AbstractModel>? modifyingModels = null;
        return ModifyOrbPassiveTriggerCount(cs, orb, triggerCount, ref modifyingModels);
    }

    public static int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount, ref List<AbstractModel>? modifyingModels)
        => throw new NotImplementedException("Hook.ModifyOrbPassiveTriggerCount: hooks agent (Hooks/Hook.cs:10840)");

    public static decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal amount)
        => throw new NotImplementedException("Hook.ModifyOrbValue: hooks agent (Hooks/Hook.cs:10865)");

    public static decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount)
        => throw new NotImplementedException("Hook.ModifyRestSiteHealAmount: hooks agent (Hooks/Hook.cs:10953)");

    public static IEnumerable<AbstractModel> ModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
        => throw new NotImplementedException("Hook.ModifyRestSiteOptions: hooks agent (Hooks/Hook.cs:10972)");

    public static IEnumerable<AbstractModel> ModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)
        => throw new NotImplementedException("Hook.ModifyRestSiteHealRewards: hooks agent (Hooks/Hook.cs:10994)");

    public static IEnumerable<AbstractModel> ModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
        => throw new NotImplementedException("Hook.ModifyRewards: hooks agent (Hooks/Hook.cs:11016)");

    public static decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source)
        => throw new NotImplementedException("Hook.ModifySummonAmount: hooks agent (Hooks/Hook.cs:11094)");

    public static IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)
        => throw new NotImplementedException("Hook.ModifyUnknownMapPointRoomTypes: hooks agent (Hooks/Hook.cs:11132)");

    public static bool ShouldAddToDeck(RunState rs, CardInstance card) => ShouldAddToDeck(rs, card, out _);

    public static bool ShouldAddToDeck(RunState rs, CardInstance card, out AbstractModel? preventer)
        => throw new NotImplementedException("Hook.ShouldAddToDeck: hooks agent (Hooks/Hook.cs:11170)");

    public static bool ShouldAfflict(CombatState cs, CardInstance card, AfflictionModel affliction)
        => throw new NotImplementedException("Hook.ShouldAfflict: hooks agent (Hooks/Hook.cs:11193)");

    public static bool ShouldAllowAncient(RunState rs, PlayerState player, AncientEventModel ancient)
        => throw new NotImplementedException("Hook.ShouldAllowAncient: hooks agent (Hooks/Hook.cs:11214)");

    public static bool ShouldAllowMerchantCardRemoval(RunState rs, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldAllowMerchantCardRemoval: hooks agent (Hooks/Hook.cs:11256)");

    public static bool ShouldAllowSelectingMoreCardRewards(RunState rs, PlayerState player, CardReward reward)
        => throw new NotImplementedException("Hook.ShouldAllowSelectingMoreCardRewards: hooks agent (Hooks/Hook.cs:11277)");

    public static bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldDisableRemainingRestSiteOptions: hooks agent (Hooks/Hook.cs:11405)");

    public static bool ShouldGenerateTreasure(RunState rs, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldGenerateTreasure: hooks agent (Hooks/Hook.cs:11491)");

    public static bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldGainStars: hooks agent (Hooks/Hook.cs:11512)");

    public static bool ShouldProceedToNextMapPoint(RunState rs)
        => throw new NotImplementedException("Hook.ShouldProceedToNextMapPoint: hooks agent (Hooks/Hook.cs:11598)");

    public static bool ShouldProcurePotion(RunState rs, CombatState? cs, PotionModel potion, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldProcurePotion: hooks agent (Hooks/Hook.cs:11619)");

    public static bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player)
        => throw new NotImplementedException("Hook.ShouldRefillMerchantEntry: hooks agent (Hooks/Hook.cs:11640)");

    public static bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)
        => throw new NotImplementedException("Hook.ShouldForcePotionReward: hooks agent (Hooks/Hook.cs:11703)");

    public static bool ShouldAllowFreeTravel(RunState rs)
        => throw new NotImplementedException("Hook.ShouldAllowFreeTravel: hooks agent (Hooks/Hook.cs:11722)");
}
