namespace Sts2Sim.Core.Combat.Pipeline;

public static class Hook
{
    public static void IterateCombatHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Guarded, buffer);

    public static void IterateHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Combat, buffer);

    public static void IterateRunHookListeners(CombatState cs, List<AbstractModel> buffer) => Fill(cs, ListenerSource.Run, buffer);

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
    {
        List<AbstractModel>? modifiers = null;
        return ModifyDamage(cs, target, dealer, damage, props, cardSource, cardPlay, hookType, ref modifiers);
    }

    public static decimal ModifyDamage(CombatState cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
    {
        decimal num = damage;
        num = ModifyDamageInternal(cs, target, dealer, num, props, cardSource, cardPlay, hookType, ref modifiers);
        return Math.Max(0m, num);
    }

    private static decimal ModifyDamageInternal(CombatState cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
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
                decimal add = model.ModifyDamageAdditive(cs, target, num, props, dealer, cardSource, cardPlay);
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
                decimal mul = model.ModifyDamageMultiplicative(cs, target, num, props, dealer, cardSource, cardPlay);
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
                decimal c = model.ModifyDamageCap(cs, target, props, dealer, cardSource, cardPlay);
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
    {
        List<AbstractModel>? modifiers = null;
        return ModifyHpLost(cs, target, amount, props, dealer, cardSource, phases, ref modifiers);
    }

    public static decimal ModifyHpLost(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases, ref List<AbstractModel>? modifiers)
    {
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
                    num = model.ModifyHpLostBeforeOsty(cs, target, num, props, dealer, cardSource);
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
                    num = model.ModifyHpLostBeforeOstyLate(cs, target, num, props, dealer, cardSource);
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
                    num = model.ModifyHpLostAfterOsty(cs, target, num, props, dealer, cardSource);
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
                    num = model.ModifyHpLostAfterOstyLate(cs, target, num, props, dealer, cardSource);
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

    public static bool ShouldDie(CombatState cs, CreatureState creature) => ShouldDie(cs, creature, out _);

    public static bool ShouldDie(CombatState cs, CreatureState creature, out AbstractModel? preventer)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldDie(cs, creature))
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
                if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldDieLate(cs, creature))
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

    public static bool ShouldPlay(CombatState cs, CardInstance card) => ShouldPlay(cs, card, out _);

    public static bool ShouldPlay(CombatState cs, CardInstance card, out AbstractModel? preventer)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && !model.ShouldPlay(cs, card))
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

    public static void BeforeCombatStart(CombatState cs)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeCombatStart(cs);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeCombatStartLate(cs);
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

    public static void BeforeSideTurnStart(CombatState cs, CombatSide side)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeSideTurnStart(cs, side);
            }
        }
    }

    public static void AfterSideTurnStart(CombatState cs, CombatSide side)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnStart(cs, side);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnStartLate(cs, side);
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

    public static void BeforeSideTurnEnd(CombatState cs, CombatSide side)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEndVeryEarly(cs, side);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEndEarly(cs, side);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.BeforeSideTurnEnd(cs, side);
                }
            }
        }
    }

    public static void AfterSideTurnEnd(CombatState cs, CombatSide side)
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnEnd(cs, side);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Guarded))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterSideTurnEndLate(cs, side);
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
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeDamageReceived(cs, target, amount, props, dealer, cardSource);
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
    {
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterDamageReceived(cs, target, result, props, dealer, cardSource);
                }
            }
        }
        using (var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run))
        {
            for (int i = 0; i < listeners.Count; i++)
            {
                if (listeners.TryGetLive(cs, i, out var model))
                {
                    model.AfterDamageReceivedLate(cs, target, result, props, dealer, cardSource);
                }
            }
        }
    }

    public static void AfterCurrentHpChanged(CombatState cs, CreatureState creature, decimal delta)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterCurrentHpChanged(cs, creature, delta);
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

    public static void BeforeDeath(CombatState cs, CreatureState creature)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.BeforeDeath(cs, creature);
            }
        }
    }

    public static void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented)
    {
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model))
            {
                model.AfterDeath(cs, creature, wasRemovalPrevented);
            }
        }
    }

    public static void AfterModifyingDamageAmount(CombatState cs, CardInstance? cardSource, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingDamageAmount(cs, cardSource);
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

    public static void AfterModifyingHpLostBeforeOsty(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingHpLostBeforeOsty(cs);
            }
        }
    }

    public static void AfterModifyingHpLostAfterOsty(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        using var listeners = ListenerSnapshot.Take(cs, ListenerSource.Run);
        for (int i = 0; i < listeners.Count; i++)
        {
            if (listeners.TryGetLive(cs, i, out var model) && ContainsModel(modifiers, model))
            {
                model.AfterModifyingHpLostAfterOsty(cs);
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

    public static void AfterPreventingDeath(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
        if (ListenerSnapshot.Contains(cs, ListenerSource.Run, preventer))
        {
            preventer.AfterPreventingDeath(cs, creature);
        }
    }

    public static void AfterPreventingDraw(CombatState cs, AbstractModel modifier)
    {
        if (ListenerSnapshot.Contains(cs, ListenerSource.Guarded, modifier))
        {
            modifier.AfterPreventingDraw(cs);
        }
    }
}
