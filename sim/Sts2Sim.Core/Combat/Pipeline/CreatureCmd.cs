namespace Sts2Sim.Core.Combat.Pipeline;

public static class CreatureCmd
{
    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, decimal amount, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        var dealer = cardSource == null ? null : CombatStatus.CardOwnerCreature(cs, cardSource);
        return Damage(cs, new[] { target }, amount, props, dealer, cardSource, cardPlay);
    }

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        return Damage(cs, new[] { target }, amount, props, dealer, cardSource, cardPlay);
    }

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, IReadOnlyList<CreatureState> targets, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (dealer != null && dealer.IsDead)
        {
            var empty = new List<DamageResult>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                empty.Add(new DamageResult(targets[i], props));
            }
            return empty;
        }
        var results = new List<DamageResult>(targets.Count);
        if (targets.Count == 0)
        {
            return results;
        }
        var targetList = new CreatureState[targets.Count];
        for (int i = 0; i < targetList.Length; i++)
        {
            targetList[i] = targets[i];
        }
        for (int t = 0; t < targetList.Length; t++)
        {
            var originalTarget = targetList[t];
            if (originalTarget.IsDead)
            {
                continue;
            }
            List<AbstractModel>? modifiers = null;
            decimal modifiedAmount = Hook.ModifyDamage(cs, originalTarget, dealer, amount, props, cardSource, cardPlay, ModifyDamageHookType.All, ref modifiers);
            Hook.AfterModifyingDamageAmount(cs, cardSource, modifiers);
            Hook.BeforeDamageReceived(cs, originalTarget, modifiedAmount, props, dealer, cardSource);
            var blocker = originalTarget;
            decimal blockedDamage = CreatureInternals.DamageBlockInternal(blocker, modifiedAmount, props);
            modifiers = null;
            decimal unblockedDamage = Hook.ModifyHpLost(cs, originalTarget, Math.Max(modifiedAmount - blockedDamage, 0m), props, dealer, cardSource, HpLossHookPhase.BeforeOsty, ref modifiers);
            Hook.AfterModifyingHpLostBeforeOsty(cs, modifiers);
            var unblockedDamageTarget = Hook.ModifyUnblockedDamageTarget(cs, originalTarget, unblockedDamage, props, dealer);
            modifiers = null;
            unblockedDamage = Hook.ModifyHpLost(cs, unblockedDamageTarget, unblockedDamage, props, dealer, cardSource, HpLossHookPhase.AfterOsty, ref modifiers);
            Hook.AfterModifyingHpLostAfterOsty(cs, modifiers);
            var unblockedDamageResult = CreatureInternals.LoseHpInternal(unblockedDamageTarget, unblockedDamage, props);
            bool wasBlockBroken = originalTarget.Block <= 0 && blockedDamage > 0m;
            bool wasFullyBlocked = (props & ValueProp.Unblockable) == 0 && (blockedDamage > 0m || originalTarget.Block > 0) && (int)unblockedDamage == 0;
            results.Add(unblockedDamageResult);
            if (ReferenceEquals(originalTarget, unblockedDamageTarget))
            {
                unblockedDamageResult.BlockedDamage = (int)blockedDamage;
                unblockedDamageResult.WasBlockBroken = wasBlockBroken;
                unblockedDamageResult.WasFullyBlocked = wasFullyBlocked;
            }
            else
            {
                modifiers = null;
                decimal originalTargetDamage = Hook.ModifyHpLost(cs, originalTarget, unblockedDamageResult.OverkillDamage, props, dealer, cardSource, HpLossHookPhase.AfterOsty, ref modifiers);
                Hook.AfterModifyingHpLostAfterOsty(cs, modifiers);
                var damageResult = originalTargetDamage > 0m
                    ? CreatureInternals.LoseHpInternal(originalTarget, originalTargetDamage, props)
                    : new DamageResult(originalTarget, props);
                damageResult.BlockedDamage = (int)blockedDamage;
                damageResult.WasBlockBroken = wasBlockBroken;
                damageResult.WasFullyBlocked = wasFullyBlocked;
                results.Add(damageResult);
            }
        }
        List<CreatureState>? killedCreatures = null;
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            var originalTarget = result.Receiver;
            if (result.WasBlockBroken)
            {
                Hook.AfterBlockBroken(cs, originalTarget, dealer);
            }
            if (result.UnblockedDamage > 0)
            {
                Hook.AfterCurrentHpChanged(cs, originalTarget, -result.UnblockedDamage);
            }
            Hook.AfterDamageGiven(cs, dealer, result, props, originalTarget, cardSource);
            if (!result.WasTargetKilled || !originalTarget.IsDead)
            {
                Hook.AfterDamageReceived(cs, originalTarget, result, props, dealer, cardSource);
            }
            else
            {
                (killedCreatures ??= new List<CreatureState>(2)).Add(originalTarget);
            }
        }
        if (killedCreatures != null)
        {
            Kill(cs, killedCreatures);
        }
        return results;
    }

    public static decimal GainBlock(CombatState cs, CreatureState creature, decimal amount, ValueProp props, CardPlay? cardPlay)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return 0m;
        }
        if (creature.IsDead)
        {
            return 0m;
        }
        var cardSource = cardPlay?.Card;
        Hook.BeforeBlockGained(cs, creature, amount, props, cardSource);
        decimal modifiedAmount = amount;
        List<AbstractModel>? modifiers = null;
        modifiedAmount = Hook.ModifyBlock(cs, creature, modifiedAmount, props, cardSource, cardPlay, ref modifiers);
        modifiedAmount = Math.Max(modifiedAmount, 0m);
        Hook.AfterModifyingBlockAmount(cs, modifiedAmount, cardSource, cardPlay, modifiers);
        if (modifiedAmount > 0m)
        {
            CreatureInternals.GainBlockInternal(creature, modifiedAmount);
        }
        Hook.AfterBlockGained(cs, creature, modifiedAmount, props, cardSource);
        return modifiedAmount;
    }

    public static void LoseBlock(CombatState cs, CreatureState target, decimal amount, CreatureState? remover)
    {
        if (CombatStatus.IsOverOrEnding(cs) || target.IsDead || amount <= 0m)
        {
            return;
        }
        int block = target.Block;
        CreatureInternals.LoseBlockInternal(target, amount);
        if (block > 0 && target.Block <= 0)
        {
            Hook.AfterBlockBroken(cs, target, remover);
        }
    }

    public static void ClearBlock(CombatState cs, CreatureState creature)
    {
        if (!Hook.ShouldClearBlock(cs, creature, out var preventer))
        {
            Hook.AfterPreventingBlockClear(cs, preventer!, creature);
            return;
        }
        CreatureInternals.SetBlock(creature, 0);
    }

    public static void BeforeTurnStart(CombatState cs, CreatureState creature, CombatSide side)
    {
        CreatureInternals.BeforeTurnStart(creature);
    }

    public static void AfterTurnStart(CombatState cs, CreatureState creature, CombatSide side)
    {
        if (side == CombatSide.Player && creature is PlayerState player && player.TurnNumber == 1)
        {
            return;
        }
        ClearBlock(cs, creature);
    }

    public static void Heal(CombatState cs, CreatureState creature, decimal amount)
    {
        if (CombatStatus.IsEnding(cs) && !creature.IsPlayer)
        {
            return;
        }
        CreatureInternals.HealInternal(creature, amount);
        if (amount > 0m && CombatStatus.IsAttached(cs, creature))
        {
            Hook.AfterCurrentHpChanged(cs, creature, amount);
        }
    }

    public static void SetCurrentHp(CombatState cs, CreatureState creature, decimal amount)
    {
        decimal oldHp = creature.CurrentHp;
        CreatureInternals.SetCurrentHpInternal(creature, amount);
        if (amount != oldHp)
        {
            Hook.AfterCurrentHpChanged(cs, creature, amount - oldHp);
        }
        if (creature.IsDead)
        {
            Kill(cs, creature);
        }
    }

    public static decimal SetMaxHp(CombatState cs, CreatureState creature, decimal amount)
    {
        int oldMaxHp = creature.MaxHp;
        CreatureInternals.SetMaxHpInternal(creature, Math.Max(0m, amount));
        int newMaxHp = creature.MaxHp;
        if (creature.MaxHp <= 0)
        {
            Kill(cs, creature);
        }
        return newMaxHp - oldMaxHp;
    }

    public static void GainMaxHp(CombatState cs, CreatureState creature, decimal amount)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use LoseMaxHp for max HP loss.");
        }
        decimal gained = SetMaxHp(cs, creature, creature.MaxHp + amount);
        Heal(cs, creature, gained);
    }

    public static void LoseMaxHp(CombatState cs, CreatureState creature, decimal amount, bool isFromCard)
    {
        if (amount < 0m)
        {
            throw new ArgumentException("amount must be non-negative. Use GainMaxHp for max HP gain.");
        }
        decimal newMaxHp = creature.MaxHp - amount;
        if (newMaxHp < creature.CurrentHp)
        {
            var props = isFromCard ? (ValueProp.Unblockable | ValueProp.Unpowered | ValueProp.Move) : (ValueProp.Unblockable | ValueProp.Unpowered);
            Damage(cs, creature, creature.CurrentHp - newMaxHp, props, null, null);
        }
        SetMaxHp(cs, creature, Math.Max(1.0m, newMaxHp));
    }

    public static void SetMaxAndCurrentHp(CombatState cs, CreatureState creature, decimal amount)
    {
        SetMaxHp(cs, creature, amount);
        SetCurrentHp(cs, creature, amount);
    }

    public static void Kill(CombatState cs, CreatureState creature, bool force = false)
    {
        Kill(cs, new[] { creature }, force);
    }

    public static void Kill(CombatState cs, IReadOnlyList<CreatureState> creatures, bool force = false)
    {
        if (creatures.Count == 0)
        {
            return;
        }
        bool anyPlayer = false;
        var snapshot = new CreatureState[creatures.Count];
        for (int i = 0; i < snapshot.Length; i++)
        {
            snapshot[i] = creatures[i];
            anyPlayer |= snapshot[i].IsPlayer;
        }
        for (int i = 0; i < snapshot.Length; i++)
        {
            KillWithoutCheckingWinCondition(cs, snapshot[i], force, 0);
        }
        if (anyPlayer && cs.Player.IsDead)
        {
            CombatStatus.LoseCombat(cs);
        }
    }

    private static void KillWithoutCheckingWinCondition(CombatState cs, CreatureState creature, bool force, int recursion)
    {
        if (!creature.IsPlayer && !CombatStatus.IsAttached(cs, creature))
        {
            return;
        }
        int currentHp = creature.CurrentHp;
        if (currentHp > 0)
        {
            CreatureInternals.LoseHpInternal(creature, currentHp, ValueProp.Unblockable | ValueProp.Unpowered);
            Hook.AfterCurrentHpChanged(cs, creature, -currentHp);
        }
        Hook.BeforeDeath(cs, creature);
        AbstractModel? preventer = null;
        if (force || creature.MaxHp <= 0 || Hook.ShouldDie(cs, creature, out preventer))
        {
            bool shouldRemoveFromCombat = Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, creature);
            Hook.AfterDeath(cs, creature, false);
            var teammates = new List<CreatureState>(4);
            CombatStatus.GetTeammatesOf(cs, creature, teammates);
            teammates.RemoveAll(static t => !t.IsAlive);
            if (shouldRemoveFromCombat && creature.Side == CombatSide.Enemy && creature is MonsterState monster && cs.Monsters.Contains(monster))
            {
                if (!(monster.HasAi && monster.Ai.IsPerformingMove))
                {
                    cs.Monsters.Remove(monster);
                }
            }
            bool isPrimaryEnemy = CombatStatus.IsPrimaryEnemy(creature);
            var removed = new List<PowerModel>(creature.Powers.Count);
            CreatureInternals.RemoveAllPowersAfterDeath(cs, creature, removed);
            for (int i = 0; i < removed.Count; i++)
            {
                removed[i].AfterRemoved(cs, creature);
            }
            if (creature.Side == CombatSide.Enemy)
            {
                if (isPrimaryEnemy && teammates.Count != 0 && AllSecondaryEnemies(teammates))
                {
                    Kill(cs, teammates);
                }
            }
            else if (creature is PlayerState player)
            {
                player.DeactivateHooks();
            }
            return;
        }
        if (recursion >= 10)
        {
            throw new InvalidOperationException("Combat is ending, but something is continually preventing the last creature from being killed!");
        }
        Hook.AfterDeath(cs, creature, true);
        Hook.AfterPreventingDeath(cs, preventer!, creature);
        if (creature.IsDead)
        {
            KillWithoutCheckingWinCondition(cs, creature, force, recursion + 1);
        }
    }

    private static bool AllSecondaryEnemies(List<CreatureState> creatures)
    {
        for (int i = 0; i < creatures.Count; i++)
        {
            if (!CombatStatus.IsSecondaryEnemy(creatures[i]))
            {
                return false;
            }
        }
        return true;
    }

    public static void Stun(CombatState cs, MonsterState creature, string? nextMoveId = null)
    {
        StunWithMove(cs, creature, static (_, _, _) => { }, nextMoveId);
    }

    public static void StunWithMove(CombatState cs, MonsterState creature, MovePerform stunMove, string? nextMoveId = null)
    {
        MonsterLifecycle.StunInternal(cs, creature, stunMove, nextMoveId);
    }
}
