namespace Sts2Sim.Core.Combat.Pipeline;

public static class Hook
{
    public static void IterateCombatHookListeners(CombatState cs, List<AbstractModel> buffer)
        => Fill(ListenerSnapshot.Unmasked(ListenerSource.Guarded, cs.Run, cs), buffer);

    public static void IterateHookListeners(CombatState cs, List<AbstractModel> buffer)
        => Fill(ListenerSnapshot.Unmasked(ListenerSource.Combat, cs.Run, cs), buffer);

    public static void IterateRunHookListeners(CombatState cs, List<AbstractModel> buffer)
        => IterateRunHookListeners(cs.Run, cs, buffer);

    public static void IterateRunHookListeners(RunState rs, CombatState? cs, List<AbstractModel> buffer)
    {
        ArgumentNullException.ThrowIfNull(rs);
        Fill(ListenerSnapshot.Unmasked(ListenerSource.Run, rs, cs), buffer);
    }

    internal static void IterateListenersFor(HookId hook, ListenerSource source, RunState rs, CombatState? cs, List<AbstractModel> buffer)
    {
        ArgumentNullException.ThrowIfNull(rs);
        Fill(ListenerSnapshot.Of(source, rs, cs, hook), buffer);
    }

    private static void Fill(ListenerSnapshot listeners, List<AbstractModel> buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        while (listeners.MoveNext())
        {
            buffer.Add(listeners.Current);
        }
    }

    private static void AddModifier(ref List<AbstractModel>? modifiers, AbstractModel model)
    {
        (modifiers ??= new List<AbstractModel>(2)).Add(model);
    }

    private static IReadOnlyList<CreatureState> SideTurnParticipants(CombatState cs, CombatSide side)
        => side == CombatSide.Enemy ? cs.Monsters : cs.Allies;

    private static void RequireRunCombat(RunState rs, CombatState? cs, string dispatcher)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (cs is not null && !ReferenceEquals(rs.Combat, cs))
        {
            throw new InvalidOperationException(dispatcher + ": cs must be null or rs.Combat, the combat attached to the run (CONTRACTS Part B section B3)");
        }
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

    public static void AfterActEntered(RunState rs)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterActEntered);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterActEntered(rs);
        }
    }

    public static void BeforeAttack(CombatState cs, AttackCommand command)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeAttack);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeAttack(cs, command);
        }
    }

    public static void AfterAttack(CombatState cs, AttackCommand command)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterAttack);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterAttack(cs, command);
        }
    }

    public static void AfterBlockBroken(CombatState cs, CreatureState target, CreatureState? breaker)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.AfterBlockBroken);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterBlockBroken(cs, target, breaker);
        }
    }

    public static void AfterBlockCleared(CombatState cs, CreatureState creature)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterBlockCleared);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterBlockCleared(cs, creature);
        }
    }

    public static void BeforeBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeBlockGained);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeBlockGained(cs, creature, amount, props, cardSource);
        }
    }

    public static void AfterBlockGained(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardInstance? cardSource)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterBlockGained);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterBlockGained(cs, creature, amount, props, cardSource);
        }
    }

    public static void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeCardAutoPlayed);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCardAutoPlayed(cs, card, target, type);
        }
    }

    public static void AfterCardChangedPiles(RunState rs, CombatState? cs, CardInstance card, PileType oldPile, AbstractModel? clonedBy)
    {
        RequireRunCombat(rs, cs, "Hook.AfterCardChangedPiles (Hooks/Hook.cs:8935)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCardChangedPiles);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardChangedPiles(rs, card, oldPile, clonedBy);
        }
        listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCardChangedPilesLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardChangedPilesLate(rs, card, oldPile, clonedBy);
        }
    }

    public static void AfterCardDiscarded(CombatState cs, CardInstance card)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardDiscarded);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardDiscarded(cs, card);
        }
    }

    public static void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardDrawnEarly);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardDrawnEarly(cs, card, fromHandDraw);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardDrawn);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardDrawn(cs, card, fromHandDraw);
        }
    }

    public static void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardEnteredCombat);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardEnteredCombat(cs, card);
        }
    }

    public static void AfterCardExhausted(CombatState cs, CardInstance card, bool causedByEthereal)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardExhausted);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardExhausted(cs, card, causedByEthereal);
        }
    }

    public static void AfterCardGeneratedForCombat(CombatState cs, CardInstance card, PlayerState? creator)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterCardGeneratedForCombat);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardGeneratedForCombat(cs, card, creator);
        }
    }

    public static void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeCardPlayed);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCardPlayed(cs, cardPlay);
        }
    }

    public static void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.AfterCardPlayed);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardPlayed(cs, cardPlay);
        }
        listeners = ListenerSnapshot.Combat(cs, HookId.AfterCardPlayedLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCardPlayedLate(cs, cardPlay);
        }
    }

    public static void BeforeCardRemoved(RunState rs, CardInstance card)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.BeforeCardRemoved);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCardRemoved(rs, card);
        }
    }

    public static void BeforeCombatStart(CombatState cs) => BeforeCombatStart(cs.Run, cs);

    public static void BeforeCombatStart(RunState rs, CombatState? cs)
    {
        RequireRunCombat(rs, cs, "Hook.BeforeCombatStart (Hooks/Hook.cs:9071)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.BeforeCombatStart);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCombatStart(rs);
        }
        listeners = ListenerSnapshot.Run(rs, cs, HookId.BeforeCombatStartLate);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCombatStartLate(rs);
        }
    }

    public static void AfterCombatEnd(RunState rs, CombatState? cs, CombatRoom? room)
    {
        RequireRunCombat(rs, cs, "Hook.AfterCombatEnd (Hooks/Hook.cs:9085)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCombatEnd);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCombatEnd(rs, room);
        }
    }

    public static void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.BeforeCombatRewardOffered);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeCombatRewardOffered(rs, rewards, room);
        }
    }

    public static void AfterCombatVictory(RunState rs, CombatState? cs, CombatRoom? room)
    {
        RequireRunCombat(rs, cs, "Hook.AfterCombatVictory (Hooks/Hook.cs:9115)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCombatVictoryEarly);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCombatVictoryEarly(rs, room);
        }
        listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCombatVictory);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCombatVictory(rs, room);
        }
    }

    public static void AfterCreatureAddedToCombat(CombatState cs, CreatureState creature)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.AfterCreatureAddedToCombat);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCreatureAddedToCombat(cs, creature);
        }
    }

    public static void AfterCurrentHpChanged(CombatState cs, CreatureState creature, decimal delta) => AfterCurrentHpChanged(cs.Run, cs, creature, delta);

    public static void AfterCurrentHpChanged(RunState rs, CombatState? cs, CreatureState creature, decimal delta)
    {
        RequireRunCombat(rs, cs, "Hook.AfterCurrentHpChanged (Hooks/Hook.cs:9144)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterCurrentHpChanged);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterCurrentHpChanged(rs, creature, delta);
        }
    }

    public static void AfterDamageGiven(CombatState cs, CreatureState? dealer, DamageResult result, ValueProp props, CreatureState target, CardInstance? cardSource)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.AfterDamageGiven);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterDamageGiven(cs, dealer, result, props, target, cardSource);
        }
    }

    public static void BeforeDamageReceived(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        => BeforeDamageReceived(cs.Run, cs, target, amount, props, dealer, cardSource);

    public static void BeforeDamageReceived(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        RequireRunCombat(rs, cs, "Hook.BeforeDamageReceived (Hooks/Hook.cs:9179)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.BeforeDamageReceived);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeDamageReceived(rs, target, amount, props, dealer, cardSource);
        }
    }

    public static void AfterDamageReceived(CombatState cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
        => AfterDamageReceived(cs.Run, cs, target, result, props, dealer, cardSource);

    public static void AfterDamageReceived(RunState rs, CombatState? cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        RequireRunCombat(rs, cs, "Hook.AfterDamageReceived (Hooks/Hook.cs:9199)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterDamageReceived);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterDamageReceived(rs, target, result, props, dealer, cardSource);
        }
        listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterDamageReceivedLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterDamageReceivedLate(rs, target, result, props, dealer, cardSource);
        }
    }

    public static void BeforeDeath(CombatState cs, CreatureState creature) => BeforeDeath(cs.Run, cs, creature);

    public static void BeforeDeath(RunState rs, CombatState? cs, CreatureState creature)
    {
        RequireRunCombat(rs, cs, "Hook.BeforeDeath (Hooks/Hook.cs:9219)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.BeforeDeath);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeDeath(rs, creature);
        }
    }

    public static void AfterDeath(CombatState cs, CreatureState creature, bool wasRemovalPrevented) => AfterDeath(cs.Run, cs, creature, wasRemovalPrevented);

    public static void AfterDeath(RunState rs, CombatState? cs, CreatureState creature, bool wasRemovalPrevented)
    {
        RequireRunCombat(rs, cs, "Hook.AfterDeath (Hooks/Hook.cs:9234)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterDeath);
        while (listeners.MoveNext())
        {
            using var scope = rs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.AfterDeath(rs, creature, wasRemovalPrevented);
        }
    }

    public static void AfterGoldGained(RunState rs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterGoldGained);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterGoldGained(rs, player);
        }
    }

    public static void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.AfterDiedToDoom);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.AfterDiedToDoom(cs, creatures);
        }
    }

    public static void AfterEnergyReset(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterEnergyReset);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterEnergyReset(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterEnergyResetLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterEnergyResetLate(cs, player);
        }
    }

    public static void AfterEnergySpent(CombatState cs, CardInstance card, int amount)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterEnergySpent);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterEnergySpent(cs, card, amount);
        }
    }

    public static void BeforeFlush(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeFlush);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeFlush(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeFlushLate);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeFlushLate(cs, player);
        }
    }

    public static void AfterFlush(CombatState cs, PlayerState player, IReadOnlyList<CardInstance> flushedCards, IReadOnlyList<CardInstance> retainedCards)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterFlush);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterFlush(cs, player, flushedCards, retainedCards);
        }
    }

    public static void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterForge);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterForge(cs, amount, forger, source);
        }
    }

    public static void BeforeHandDraw(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeHandDraw);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeHandDraw(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeHandDrawLate);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeHandDrawLate(cs, player);
        }
    }

    public static void AfterHandEmptied(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterHandEmptied);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterHandEmptied(cs, player);
        }
    }

    public static void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterItemPurchased);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterItemPurchased(rs, player, itemPurchased, goldSpent);
        }
    }

    public static void AfterMapGenerated(RunState rs, ActMap map, int actIndex)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterMapGenerated);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterMapGenerated(rs, map, actIndex);
        }
    }

    public static void AfterModifyingBlockAmount(CombatState cs, decimal modifiedBlock, CardInstance? cardSource, CardPlay? cardPlay, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingBlockAmount);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingBlockAmount(cs, modifiedBlock, cardSource, cardPlay);
            }
        }
    }

    public static void AfterModifyingCardPlayCount(CombatState cs, CardInstance card, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingCardPlayCount);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingCardPlayCount(cs, card);
            }
        }
    }

    public static void AfterModifyingCardRewardOptions(RunState rs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterModifyingCardRewardOptions);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingCardRewardOptions(rs);
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
        RequireRunCombat(rs, cs, "Hook.AfterModifyingDamageAmount (Hooks/Hook.cs:9462)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterModifyingDamageAmount);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingDamageAmount(rs, cardSource);
            }
        }
    }

    public static void AfterModifyingEnergyGain(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingEnergyGain);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingEnergyGain(cs);
            }
        }
    }

    public static void AfterModifyingGoldGained(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers, PlayerState player, decimal amount)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        RequireRunCombat(rs, cs, "Hook.AfterModifyingGoldGained (Hooks/Hook.cs:9492)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterModifyingGoldGained);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingGoldGained(rs, player, amount);
            }
        }
    }

    public static void AfterModifyingHandDraw(CombatState cs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingHandDraw);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingHandDraw(cs);
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
        RequireRunCombat(rs, cs, "Hook.AfterModifyingHpLostBeforeOsty (Hooks/Hook.cs:9523)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterModifyingHpLostBeforeOsty);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
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
        RequireRunCombat(rs, cs, "Hook.AfterModifyingHpLostAfterOsty (Hooks/Hook.cs:9538)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterModifyingHpLostAfterOsty);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingHpLostAfterOsty(rs);
            }
        }
    }

    public static void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingOrbPassiveTriggerCount);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingOrbPassiveTriggerCount(cs, orb);
            }
        }
    }

    public static void AfterModifyingPowerAmountGiven(CombatState cs, IReadOnlyList<AbstractModel>? modifiers, PowerModel modifiedPower)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingPowerAmountGiven);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
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
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterModifyingPowerAmountReceived);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingPowerAmountReceived(cs, modifiedPower);
            }
        }
    }

    public static void AfterModifyingRewards(RunState rs, IReadOnlyList<AbstractModel>? modifiers)
    {
        if (modifiers == null || modifiers.Count == 0)
        {
            return;
        }
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterModifyingRewards);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (ContainsModel(modifiers, model))
            {
                model.AfterModifyingRewards(rs);
            }
        }
    }

    public static void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterOrbChanneled);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterOrbChanneled(cs, player, orb);
        }
    }

    public static void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterOrbEvoked);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterOrbEvoked(cs, orb, targets);
        }
    }

    public static void AfterOstyRevived(CombatState cs, CreatureState osty)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterOstyRevived);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterOstyRevived(cs, osty);
        }
    }

    public static void AfterPlayerTurnStart(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPlayerTurnStartEarly);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPlayerTurnStartEarly(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPlayerTurnStart);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPlayerTurnStart(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPlayerTurnStartLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPlayerTurnStartLate(cs, player);
        }
    }

    public static void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterAutoPostPlayPhaseEntered);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterAutoPostPlayPhaseEntered(cs, player);
        }
    }

    public static void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterAutoPrePlayPhaseEnteredEarly);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterAutoPrePlayPhaseEnteredEarly(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterAutoPrePlayPhaseEntered);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterAutoPrePlayPhaseEntered(cs, player);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterAutoPrePlayPhaseEnteredLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterAutoPrePlayPhaseEnteredLate(cs, player);
        }
    }

    public static void AfterPotionDiscarded(RunState rs, CombatState? cs, PotionModel potion)
    {
        RequireRunCombat(rs, cs, "Hook.AfterPotionDiscarded (Hooks/Hook.cs:9703)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterPotionDiscarded);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPotionDiscarded(rs, potion);
        }
    }

    public static void AfterPotionProcured(RunState rs, CombatState? cs, PotionModel potion)
    {
        RequireRunCombat(rs, cs, "Hook.AfterPotionProcured (Hooks/Hook.cs:9718)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterPotionProcured);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPotionProcured(rs, potion);
        }
    }

    public static void BeforePotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)
    {
        RequireRunCombat(rs, cs, "Hook.BeforePotionUsed (Hooks/Hook.cs:9733)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.BeforePotionUsed);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforePotionUsed(rs, potion, target);
        }
    }

    public static void AfterPotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)
    {
        RequireRunCombat(rs, cs, "Hook.AfterPotionUsed (Hooks/Hook.cs:9749)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterPotionUsed);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPotionUsed(rs, potion, target);
        }
    }

    public static void BeforePowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState target, CreatureState? applier, CardInstance? cardSource)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforePowerAmountChanged);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforePowerAmountChanged(cs, power, amount, target, applier, cardSource);
        }
    }

    public static void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPowerAmountChanged);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterPowerAmountChanged(cs, power, amount, applier, cardSource);
        }
    }

    public static void AfterPreventingBlockClear(CombatState cs, AbstractModel preventer, CreatureState creature)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPreventingBlockClear);
        if (listeners.Contains(preventer))
        {
            preventer.AfterPreventingBlockClear(cs, preventer, creature);
        }
    }

    public static void AfterPreventingDeath(CombatState cs, AbstractModel preventer, CreatureState creature) => AfterPreventingDeath(cs.Run, cs, preventer, creature);

    public static void AfterPreventingDeath(RunState rs, CombatState? cs, AbstractModel preventer, CreatureState creature)
    {
        RequireRunCombat(rs, cs, "Hook.AfterPreventingDeath (Hooks/Hook.cs:9816)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.AfterPreventingDeath);
        if (listeners.Contains(preventer))
        {
            preventer.AfterPreventingDeath(rs, creature);
        }
    }

    public static void AfterPreventingDraw(CombatState cs, AbstractModel modifier)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterPreventingDraw);
        if (listeners.Contains(modifier))
        {
            modifier.AfterPreventingDraw(cs);
        }
    }

    public static void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterRestSiteHeal);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterRestSiteHeal(rs, player, isMimicked);
        }
    }

    public static void AfterRestSiteSmith(RunState rs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterRestSiteSmith);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterRestSiteSmith(rs, player);
        }
    }

    public static void AfterRewardTaken(RunState rs, PlayerState player, Reward reward)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterRewardTaken);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterRewardTaken(rs, player, reward);
        }
    }

    public static void BeforeRoomEntered(RunState rs, AbstractRoom room)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.BeforeRoomEntered);
        while (listeners.MoveNext())
        {
            listeners.Current.BeforeRoomEntered(rs, room);
        }
    }

    public static void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.AfterRoomEntered);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterRoomEntered(rs, room);
        }
    }

    public static void AfterShuffle(CombatState cs, PlayerState shuffler)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterShuffle);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterShuffle(cs, shuffler);
        }
    }

    public static void BeforeSideTurnStart(CombatState cs, CombatSide side) => BeforeSideTurnStart(cs, side, SideTurnParticipants(cs, side));

    public static void BeforeSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeSideTurnStart);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeSideTurnStart(cs, side, participants);
        }
    }

    public static void AfterSideTurnStart(CombatState cs, CombatSide side) => AfterSideTurnStart(cs, side, SideTurnParticipants(cs, side));

    public static void AfterSideTurnStart(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterSideTurnStart);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterSideTurnStart(cs, side, participants);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterSideTurnStartLate);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterSideTurnStartLate(cs, side, participants);
        }
    }

    public static void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterStarsGained);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterStarsGained(cs, amount, gainer);
        }
    }

    public static void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterStarsSpent);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterStarsSpent(cs, amount, spender);
        }
    }

    public static void AfterSummon(CombatState cs, PlayerState summoner, decimal amount)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterSummon);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterSummon(cs, summoner, amount);
        }
    }

    public static void AfterTakingExtraTurn(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterTakingExtraTurn);
        while (listeners.MoveNext())
        {
            listeners.Current.AfterTakingExtraTurn(cs, player);
        }
    }

    public static void BeforeSideTurnEnd(CombatState cs, CombatSide side) => BeforeSideTurnEnd(cs, side, SideTurnParticipants(cs, side));

    public static void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeSideTurnEndVeryEarly);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeSideTurnEndVeryEarly(cs, side, participants);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeSideTurnEndEarly);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeSideTurnEndEarly(cs, side, participants);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.BeforeSideTurnEnd);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.BeforeSideTurnEnd(cs, side, participants);
        }
    }

    public static void AfterSideTurnEnd(CombatState cs, CombatSide side) => AfterSideTurnEnd(cs, side, SideTurnParticipants(cs, side));

    public static void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.AfterSideTurnEnd);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.AfterSideTurnEnd(cs, side, participants);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.AfterSideTurnEndLate);
        while (listeners.MoveNext())
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener);
            listeners.Current.AfterSideTurnEndLate(cs, side, participants);
        }
    }

    public static int ModifyAttackHitCount(CombatState cs, AttackCommand attack, int originalHitCount)
    {
        int num = originalHitCount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyAttackHitCount);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyAttackHitCount(cs, attack, num);
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
        modifiers = null;
        decimal num = block;
        if (cardSource != null && cardSource.Enchantment != null)
        {
            var enchantment = cardSource.Enchantment;
            num += enchantment.EnchantBlockAdditive(num);
            num *= enchantment.EnchantBlockMultiplicative(num);
        }
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyBlockAdditive);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal add = model.ModifyBlockAdditive(cs, target, num, props, cardSource, cardPlay);
            num += add;
            if (add != 0m)
            {
                AddModifier(ref modifiers, model);
            }
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyBlockMultiplicative);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal mul = model.ModifyBlockMultiplicative(cs, target, num, props, cardSource, cardPlay);
            num *= mul;
            if (mul != 1m)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return Math.Max(0m, num);
    }

    public static CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card)
    {
        List<AbstractModel>? modifyingModels = null;
        return ModifyCardBeingAddedToDeck(rs, card, ref modifyingModels);
    }

    public static CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card, ref List<AbstractModel>? modifyingModels)
    {
        modifyingModels = null;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyCardBeingAddedToDeck);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyCardBeingAddedToDeck(rs, card, out var newCard) && newCard != null)
            {
                AddModifier(ref modifyingModels, model);
                card = newCard;
            }
        }
        listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyCardBeingAddedToDeckLate);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyCardBeingAddedToDeckLate(rs, card, out var newCard) && newCard != null)
            {
                AddModifier(ref modifyingModels, model);
                card = newCard;
            }
        }
        return card;
    }

    public static int ModifyCardPlayCount(CombatState cs, CardInstance card, int playCount, CreatureState? target)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyCardPlayCount(cs, card, playCount, target, ref modifiers);
    }

    public static int ModifyCardPlayCount(CombatState cs, CardInstance card, int playCount, CreatureState? target, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        int num = playCount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyCardPlayCount);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            int before = num;
            num = model.ModifyCardPlayCount(cs, card, target, num);
            if (num != before)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyCardPlayResultLocation(cs, card, isAutoPlay, resources, cardLocation, ref modifiers);
    }

    public static CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyCardPlayResultLocation);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
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
    {
        var modifiers = new List<AbstractModel>();
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyCardRewardAlternatives);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyCardRewardAlternatives(rs, player, cardReward, alternatives))
            {
                modifiers.Add(model);
            }
        }
        return modifiers;
    }

    public static CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyCardRewardCreationOptions);
        while (listeners.MoveNext())
        {
            options = listeners.Current.ModifyCardRewardCreationOptions(rs, player, options);
        }
        listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyCardRewardCreationOptionsLate);
        while (listeners.MoveNext())
        {
            options = listeners.Current.ModifyCardRewardCreationOptionsLate(rs, player, options);
        }
        return options;
    }

    public static bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        List<AbstractModel>? modifiers = null;
        return TryModifyCardRewardOptions(rs, player, cardRewardOptions, creationOptions, ref modifiers);
    }

    public static bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        bool flag = false;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyCardRewardOptions);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            bool modified = model.TryModifyCardRewardOptions(rs, player, cardRewardOptions, creationOptions);
            flag = flag || modified;
            if (modified)
            {
                AddModifier(ref modifiers, model);
            }
        }
        listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyCardRewardOptionsLate);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            bool modified = model.TryModifyCardRewardOptionsLate(rs, player, cardRewardOptions, creationOptions);
            flag = flag || modified;
            if (modified)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return flag;
    }

    public static decimal ModifyCardRewardUpgradeOdds(RunState rs, PlayerState player, CardInstance card, decimal originalOdds)
    {
        decimal num = originalOdds;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyCardRewardUpgradeOdds);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyCardRewardUpgradeOdds(rs, player, card, num);
        }
        return num;
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
        RequireRunCombat(rs, cs, "Hook.ModifyDamage (Hooks/Hook.cs:10328)");
        modifiers = null;
        decimal num = damage;
        if (cardSource != null && cardSource.Enchantment != null)
        {
            if ((hookType & ModifyDamageHookType.Additive) == ModifyDamageHookType.Additive)
            {
                num += cardSource.Enchantment.EnchantDamageAdditive(num, props);
            }
            if ((hookType & ModifyDamageHookType.Multiplicative) == ModifyDamageHookType.Multiplicative)
            {
                num *= cardSource.Enchantment.EnchantDamageMultiplicative(num, props);
            }
        }
        num = ModifyDamageInternal(rs, cs, target, dealer, num, props, cardSource, cardPlay, hookType, ref modifiers);
        return Math.Max(0m, num);
    }

    public static decimal ModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost)
    {
        if (originalCost < 0m)
        {
            return originalCost;
        }
        decimal modifiedCost = originalCost;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.TryModifyEnergyCostInCombat);
        while (listeners.MoveNext())
        {
            listeners.Current.TryModifyEnergyCostInCombat(cs, card, modifiedCost, out modifiedCost);
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.TryModifyEnergyCostInCombatLate);
        while (listeners.MoveNext())
        {
            listeners.Current.TryModifyEnergyCostInCombatLate(cs, card, modifiedCost, out modifiedCost);
        }
        return modifiedCost;
    }

    public static void ModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.TryModifyKeywordsInCombat);
        while (listeners.MoveNext())
        {
            listeners.Current.TryModifyKeywordsInCombat(cs, card, keywords);
        }
    }

    public static decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal originalAmount)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyEnergyGain(cs, player, originalAmount, ref modifiers);
    }

    public static decimal ModifyEnergyGain(CombatState cs, PlayerState player, decimal originalAmount, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        decimal num = originalAmount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyEnergyGain);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal before = num;
            num = model.ModifyEnergyGain(cs, player, num);
            if ((int)before != (int)num)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyGoldGained(rs, cs, amount, player, ref modifiers);
    }

    public static decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player, ref List<AbstractModel>? modifiers)
    {
        RequireRunCombat(rs, cs, "Hook.ModifyGoldGained (Hooks/Hook.cs:10501)");
        modifiers = null;
        decimal num = amount;
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyGoldGained);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal before = num;
            num = model.ModifyGoldGained(rs, player, num);
            if ((int)before != (int)num)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> extraText)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyExtraRestSiteHealText);
        while (listeners.MoveNext())
        {
            extraText = listeners.Current.ModifyExtraRestSiteHealText(rs, player, extraText);
        }
        return extraText;
    }

    public static ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyGeneratedMap);
        while (listeners.MoveNext())
        {
            map = listeners.Current.ModifyGeneratedMap(rs, map, actIndex);
        }
        return ModifyGeneratedMapLate(rs, map, actIndex);
    }

    public static ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyGeneratedMapLate);
        while (listeners.MoveNext())
        {
            map = listeners.Current.ModifyGeneratedMapLate(rs, map, actIndex);
        }
        return map;
    }

    public static decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal originalCardCount)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyHandDraw(cs, player, originalCardCount, ref modifiers);
    }

    public static decimal ModifyHandDraw(CombatState cs, PlayerState player, decimal originalCardCount, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        decimal num = originalCardCount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyHandDraw);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal before = num;
            num = model.ModifyHandDraw(cs, player, num);
            if ((int)before != (int)num)
            {
                AddModifier(ref modifiers, model);
            }
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyHandDrawLate);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal before = num;
            num = model.ModifyHandDrawLate(cs, player, num);
            if ((int)before != (int)num)
            {
                AddModifier(ref modifiers, model);
            }
        }
        return num;
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
        RequireRunCombat(rs, cs, "Hook.ModifyHpLost (Hooks/Hook.cs:10627)");
        modifiers = null;
        decimal num = amount;
        if ((phases & HpLossHookPhase.BeforeOsty) == HpLossHookPhase.BeforeOsty)
        {
            var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyHpLostBeforeOsty);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal before = num;
                num = model.ModifyHpLostBeforeOsty(rs, target, num, props, dealer, cardSource);
                if (decimal.Truncate(before) != decimal.Truncate(num))
                {
                    AddModifier(ref modifiers, model);
                }
            }
            listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyHpLostBeforeOstyLate);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal before = num;
                num = model.ModifyHpLostBeforeOstyLate(rs, target, num, props, dealer, cardSource);
                if (decimal.Truncate(before) != decimal.Truncate(num))
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        if ((phases & HpLossHookPhase.AfterOsty) == HpLossHookPhase.AfterOsty)
        {
            var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyHpLostAfterOsty);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal before = num;
                num = model.ModifyHpLostAfterOsty(rs, target, num, props, dealer, cardSource);
                if (decimal.Truncate(before) != decimal.Truncate(num))
                {
                    AddModifier(ref modifiers, model);
                }
            }
            listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyHpLostAfterOstyLate);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal before = num;
                num = model.ModifyHpLostAfterOstyLate(rs, target, num, props, dealer, cardSource);
                if (decimal.Truncate(before) != decimal.Truncate(num))
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        return num;
    }

    public static decimal ModifyMaxEnergy(CombatState cs, PlayerState player, decimal amount)
    {
        decimal num = amount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyMaxEnergy);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyMaxEnergy(cs, player, num);
        }
        return num;
    }

    public static void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyMerchantCardCreationResults);
        while (listeners.MoveNext())
        {
            listeners.Current.ModifyMerchantCardCreationResults(rs, player, cards);
        }
    }

    public static IEnumerable<CardDef> ModifyMerchantCardPool(RunState rs, PlayerState player, IEnumerable<CardDef> options)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyMerchantCardPool);
        while (listeners.MoveNext())
        {
            options = listeners.Current.ModifyMerchantCardPool(rs, player, options);
        }
        return options;
    }

    public static CardRarity ModifyMerchantCardRarity(RunState rs, PlayerState player, CardRarity rarity)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyMerchantCardRarity);
        while (listeners.MoveNext())
        {
            rarity = listeners.Current.ModifyMerchantCardRarity(rs, player, rarity);
        }
        return rarity;
    }

    public static decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal result)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyMerchantPrice);
        while (listeners.MoveNext())
        {
            result = listeners.Current.ModifyMerchantPrice(rs, player, entry, result);
        }
        return result;
    }

    public static EventModel ModifyNextEvent(RunState rs, EventModel currentEvent)
    {
        var eventModel = currentEvent;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyNextEvent);
        while (listeners.MoveNext())
        {
            eventModel = listeners.Current.ModifyNextEvent(rs, eventModel);
        }
        return eventModel;
    }

    public static float ModifyOddsIncreaseForUnrolledRoomType(RunState rs, RoomType roomType, float oddsIncrease)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyOddsIncreaseForUnrolledRoomType);
        while (listeners.MoveNext())
        {
            oddsIncrease = listeners.Current.ModifyOddsIncreaseForUnrolledRoomType(rs, roomType, oddsIncrease);
        }
        return oddsIncrease;
    }

    public static int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount)
    {
        List<AbstractModel>? modifyingModels = null;
        return ModifyOrbPassiveTriggerCount(cs, orb, triggerCount, ref modifyingModels);
    }

    public static int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount, ref List<AbstractModel>? modifyingModels)
    {
        modifyingModels = null;
        int num = triggerCount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyOrbPassiveTriggerCounts);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            int before = num;
            num = model.ModifyOrbPassiveTriggerCounts(cs, orb, num);
            if (num != before)
            {
                AddModifier(ref modifyingModels, model);
            }
        }
        return num;
    }

    public static decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal amount)
    {
        decimal num = amount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyOrbValue);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyOrbValue(cs, orb, num);
        }
        return num;
    }

    public static decimal ModifyPowerAmountGiven(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource)
    {
        List<AbstractModel>? modifiers = null;
        return ModifyPowerAmountGiven(cs, power, giver, amount, target, cardSource, ref modifiers);
    }

    public static decimal ModifyPowerAmountGiven(CombatState cs, PowerModel power, CreatureState giver, decimal amount, CreatureState? target, CardInstance? cardSource, ref List<AbstractModel>? modifiers)
    {
        modifiers = null;
        decimal num = amount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyPowerAmountGivenAdditive);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal add = model.ModifyPowerAmountGivenAdditive(cs, power, giver, num, target, cardSource);
            num += add;
            if (add != 0m)
            {
                AddModifier(ref modifiers, model);
            }
        }
        listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyPowerAmountGivenMultiplicative);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            decimal mul = model.ModifyPowerAmountGivenMultiplicative(cs, power, giver, num, target, cardSource);
            num *= mul;
            if (mul != 1m)
            {
                AddModifier(ref modifiers, model);
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
        modifiers = null;
        decimal num = amount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.TryModifyPowerAmountReceived);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyPowerAmountReceived(cs, canonicalPower, target, num, giver, out var modifiedAmount))
            {
                num = modifiedAmount;
                AddModifier(ref modifiers, model);
            }
        }
        return num;
    }

    public static decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount)
    {
        decimal num = amount;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyRestSiteHealAmount);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyRestSiteHealAmount(rs, creature, num);
        }
        return num;
    }

    public static IEnumerable<AbstractModel> ModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        var modifiers = new List<AbstractModel>();
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyRestSiteOptions);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyRestSiteOptions(rs, player, options))
            {
                modifiers.Add(model);
            }
        }
        return modifiers;
    }

    public static IEnumerable<AbstractModel> ModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)
    {
        var modifiers = new List<AbstractModel>();
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyRestSiteHealRewards);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyRestSiteHealRewards(rs, player, rewards, isMimicked))
            {
                modifiers.Add(model);
            }
        }
        return modifiers;
    }

    public static IEnumerable<AbstractModel> ModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        var modifiers = new List<AbstractModel>();
        var listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyRewards);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyRewards(rs, player, rewards, room))
            {
                modifiers.Add(model);
            }
        }
        listeners = ListenerSnapshot.Run(rs, null, HookId.TryModifyRewardsLate);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (model.TryModifyRewardsLate(rs, player, rewards, room))
            {
                modifiers.Add(model);
            }
        }
        return modifiers;
    }

    public static void ModifyShuffleOrder(CombatState cs, PlayerState player, List<CardInstance> cards, bool isInitialShuffle)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyShuffleOrder);
        while (listeners.MoveNext())
        {
            listeners.Current.ModifyShuffleOrder(cs, player, cards, isInitialShuffle);
        }
    }

    public static decimal ModifyStarCost(CombatState cs, CardInstance card, decimal originalCost)
    {
        if (originalCost < 0m)
        {
            return originalCost;
        }
        decimal modifiedCost = originalCost;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.TryModifyStarCost);
        while (listeners.MoveNext())
        {
            listeners.Current.TryModifyStarCost(cs, card, modifiedCost, out modifiedCost);
        }
        return modifiedCost;
    }

    public static decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source)
    {
        decimal num = amount;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifySummonAmount);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifySummonAmount(cs, summoner, num, source);
        }
        return num;
    }

    public static CreatureState ModifyUnblockedDamageTarget(CombatState cs, CreatureState originalTarget, decimal amount, ValueProp props, CreatureState? dealer)
    {
        var creature = originalTarget;
        var listeners = ListenerSnapshot.Combat(cs, HookId.ModifyUnblockedDamageTarget);
        while (listeners.MoveNext())
        {
            creature = listeners.Current.ModifyUnblockedDamageTarget(cs, creature, amount, props, dealer);
        }
        return creature;
    }

    public static IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)
    {
        IReadOnlySet<RoomType> result = new HashSet<RoomType>(roomTypes);
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ModifyUnknownMapPointRoomTypes);
        while (listeners.MoveNext())
        {
            result = listeners.Current.ModifyUnknownMapPointRoomTypes(rs, result);
        }
        return result;
    }

    public static int ModifyXValue(CombatState cs, CardInstance card, int originalValue)
    {
        int num = originalValue;
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ModifyXValue);
        while (listeners.MoveNext())
        {
            num = listeners.Current.ModifyXValue(cs, card, num);
        }
        return num;
    }

    public static bool ShouldAddToDeck(RunState rs, CardInstance card) => ShouldAddToDeck(rs, card, out _);

    public static bool ShouldAddToDeck(RunState rs, CardInstance card, out AbstractModel? preventer)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldAddToDeck);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldAddToDeck(rs, card))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldAfflict(CombatState cs, CardInstance card, AfflictionModel affliction)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldAfflict);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldAfflict(cs, card, affliction))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldAllowAncient(RunState rs, PlayerState player, AncientEventModel ancient)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldAllowAncient);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldAllowAncient(rs, player, ancient))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldAllowHitting(CombatState cs, CreatureState creature)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldAllowHitting);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldAllowHitting(cs, creature))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldAllowMerchantCardRemoval(RunState rs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldAllowMerchantCardRemoval);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldAllowMerchantCardRemoval(rs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldAllowSelectingMoreCardRewards(RunState rs, PlayerState player, CardReward reward)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldAllowSelectingMoreCardRewards);
        while (listeners.MoveNext())
        {
            if (listeners.Current.ShouldAllowSelectingMoreCardRewards(rs, player, reward))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldAllowTargeting(CombatState cs, CreatureState target) => ShouldAllowTargeting(cs, target, out _);

    public static bool ShouldAllowTargeting(CombatState cs, CreatureState target, out AbstractModel? preventer)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldAllowTargeting);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldAllowTargeting(cs, target))
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
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldClearBlock);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldClearBlock(cs, creature))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState cs, CreatureState creature)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.ShouldCreatureBeRemovedFromCombatAfterDeath);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, creature))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldDie(CombatState cs, CreatureState creature) => ShouldDie(cs.Run, cs, creature, out _);

    public static bool ShouldDie(CombatState cs, CreatureState creature, out AbstractModel? preventer) => ShouldDie(cs.Run, cs, creature, out preventer);

    public static bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature) => ShouldDie(rs, cs, creature, out _);

    public static bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature, out AbstractModel? preventer)
    {
        RequireRunCombat(rs, cs, "Hook.ShouldDie (Hooks/Hook.cs:11365)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.ShouldDie);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldDie(rs, creature))
            {
                preventer = model;
                return false;
            }
        }
        listeners = ListenerSnapshot.Run(rs, cs, HookId.ShouldDieLate);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldDieLate(rs, creature))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldDisableRemainingRestSiteOptions);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldDisableRemainingRestSiteOptions(rs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw) => ShouldDraw(cs, player, fromHandDraw, out _);

    public static bool ShouldDraw(CombatState cs, PlayerState player, bool fromHandDraw, out AbstractModel? modifier)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldDraw);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldDraw(cs, player, fromHandDraw))
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
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldEtherealTrigger);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldEtherealTrigger(cs, card))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldFlush(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldFlush);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldFlush(cs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldGenerateTreasure(RunState rs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldGenerateTreasure);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldGenerateTreasure(rs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldGainStars);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldGainStars(cs, amount, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldPayExcessEnergyCostWithStars(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldPayExcessEnergyCostWithStars);
        while (listeners.MoveNext())
        {
            if (listeners.Current.ShouldPayExcessEnergyCostWithStars(cs, player))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldPlay(CombatState cs, CardInstance card) => ShouldPlay(cs, card, out _, AutoPlayType.None);

    public static bool ShouldPlay(CombatState cs, CardInstance card, out AbstractModel? preventer) => ShouldPlay(cs, card, out preventer, AutoPlayType.None);

    public static bool ShouldPlay(CombatState cs, CardInstance card, out AbstractModel? preventer, AutoPlayType autoPlayType)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldPlay);
        while (listeners.MoveNext())
        {
            var model = listeners.Current;
            if (!model.ShouldPlay(cs, card, autoPlayType))
            {
                preventer = model;
                return false;
            }
        }
        preventer = null;
        return true;
    }

    public static bool ShouldPlayerResetEnergy(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldPlayerResetEnergy);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldPlayerResetEnergy(cs, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldProceedToNextMapPoint(RunState rs)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldProceedToNextMapPoint);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldProceedToNextMapPoint(rs))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldProcurePotion(RunState rs, CombatState? cs, PotionModel potion, PlayerState player)
    {
        RequireRunCombat(rs, cs, "Hook.ShouldProcurePotion (Hooks/Hook.cs:11619)");
        var listeners = ListenerSnapshot.Run(rs, cs, HookId.ShouldProcurePotion);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldProcurePotion(rs, potion, player))
            {
                return false;
            }
        }
        return true;
    }

    public static bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player)
    {
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldRefillMerchantEntry);
        while (listeners.MoveNext())
        {
            if (listeners.Current.ShouldRefillMerchantEntry(rs, entry, player))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldStopCombatFromEnding(CombatState cs)
    {
        var listeners = ListenerSnapshot.Combat(cs, HookId.ShouldStopCombatFromEnding);
        while (listeners.MoveNext())
        {
            if (listeners.Current.ShouldStopCombatFromEnding(cs))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldTakeExtraTurn(CombatState cs, PlayerState player)
    {
        var listeners = ListenerSnapshot.Guarded(cs, HookId.ShouldTakeExtraTurn);
        while (listeners.MoveNext())
        {
            if (listeners.Current.ShouldTakeExtraTurn(cs, player))
            {
                return true;
            }
        }
        return false;
    }

    public static bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)
    {
        bool flag = false;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldForcePotionReward);
        while (listeners.MoveNext())
        {
            flag = flag || listeners.Current.ShouldForcePotionReward(rs, player, roomType);
        }
        return flag;
    }

    public static bool ShouldAllowFreeTravel(RunState rs)
    {
        bool flag = false;
        var listeners = ListenerSnapshot.Run(rs, null, HookId.ShouldAllowFreeTravel);
        while (listeners.MoveNext())
        {
            flag = flag || listeners.Current.ShouldAllowFreeTravel(rs);
        }
        return flag;
    }

    public static bool ShouldPowerBeRemovedOnDeath(CombatState cs, PowerModel power)
    {
        if (!ListenerSnapshot.IsInCombat(cs, power.Owner))
        {
            return true;
        }
        var listeners = ListenerSnapshot.Combat(cs, HookId.ShouldPowerBeRemovedOnDeath);
        while (listeners.MoveNext())
        {
            if (!listeners.Current.ShouldPowerBeRemovedOnDeath(cs, power))
            {
                return false;
            }
        }
        return true;
    }

    private static decimal ModifyDamageInternal(RunState rs, CombatState? cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)
    {
        decimal num = damage;
        if ((hookType & ModifyDamageHookType.Additive) == ModifyDamageHookType.Additive)
        {
            var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyDamageAdditive);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal add = model.ModifyDamageAdditive(rs, target, num, props, dealer, cardSource, cardPlay);
                num += add;
                if (add != 0m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        if ((hookType & ModifyDamageHookType.Multiplicative) == ModifyDamageHookType.Multiplicative)
        {
            var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyDamageMultiplicative);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
                decimal mul = model.ModifyDamageMultiplicative(rs, target, num, props, dealer, cardSource, cardPlay);
                num *= mul;
                if (mul != 1m)
                {
                    AddModifier(ref modifiers, model);
                }
            }
        }
        if ((hookType & ModifyDamageHookType.Cap) == ModifyDamageHookType.Cap)
        {
            decimal cap = decimal.MaxValue;
            var listeners = ListenerSnapshot.Run(rs, cs, HookId.ModifyDamageCap);
            while (listeners.MoveNext())
            {
                var model = listeners.Current;
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
}
