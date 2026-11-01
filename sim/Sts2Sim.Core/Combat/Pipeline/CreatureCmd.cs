namespace Sts2Sim.Core.Combat.Pipeline;

public static class CreatureCmd
{
    public const string EscapeFidelitySite = "CreatureCmd.Escape: CombatState.EscapedCreatures (Combat/CombatState.cs:733-737) has no sim field; the escaped creature is removed but not listed";

    public const string HandlePlayerDeathFidelitySite = "CreatureCmd.HandlePlayerDeath: CardPileCmd.RemoveFromCombat(cards) batch (Commands/CardPileCmd.cs:2205-2460) replaced by per-card removal";

    public static MonsterState Add<T>(CombatState cs, string? slotName = null) where T : MonsterDef, new()
    {
        var probe = new T();
        var def = MonsterRegistry.TryGet(probe.Id, out var registered) ? registered : probe;
        return Add(cs, def, CombatSide.Enemy, slotName);
    }

    public static MonsterState Add(CombatState cs, MonsterDef monster, CombatSide side = CombatSide.Enemy, string? slotName = null)
    {
        ArgumentNullException.ThrowIfNull(monster);
        var creature = MonsterSpawner.SpawnForAdd(cs, monster, side, slotName);
        Add(cs, creature);
        return creature;
    }

    public static void Add(CombatState cs, CreatureState creature)
    {
        ArgumentNullException.ThrowIfNull(creature);
        if (!cs.IsInProgress)
        {
            throw new InvalidOperationException("Attempted to add a creature outside of combat.");
        }
        if (creature is not MonsterState monster)
        {
            throw new ArgumentException("Players join a sim combat through the CombatState constructor (Combat/CombatState.cs:703-707 AddPlayer); CreatureCmd.Add adds monsters and pets.", nameof(creature));
        }
        if (CombatStatus.IsAttached(cs, monster))
        {
            throw new InvalidOperationException("Creature is already in this combat, but AddCreature was called on it again.");
        }
        if (monster.Side == CombatSide.Player)
        {
            cs.AddAlly(monster);
        }
        else
        {
            cs.AddMonster(monster);
        }
        if (!CombatStatus.IsAttached(cs, monster))
        {
            throw new InvalidOperationException("CombatState must already contain creature.");
        }
        MonsterLifecycle.SetUpForCombat(cs, monster);
        if (monster.SlotName != null)
        {
            MonsterSpawner.SortEnemiesBySlotName(cs, EncounterOf(cs));
        }
        MonsterLifecycle.AfterCreatureAdded(cs, monster);
        if (cs.CurrentSide != CombatSide.Enemy)
        {
            MonsterLifecycle.PrepareForNextTurn(cs, monster, rollNewMove: false);
        }
        Hook.AfterCreatureAddedToCombat(cs, monster);
    }

    private static EncounterDef? EncounterOf(CombatState cs)
        => EncounterRegistry.TryGet(cs.EncounterId, out var encounter) ? encounter : null;

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, DamageVar damageVar, CardInstance cardSource, CardPlay? cardPlay)
        => Damage(cs, target, damageVar.BaseValue, damageVar.Props, cardSource, cardPlay);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, decimal amount, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay)
    {
        var dealer = cardSource == null ? null : CombatStatus.CardOwnerCreature(cs, cardSource);
        return Damage(cs, new[] { target }, amount, props, dealer, cardSource, cardPlay);
    }

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, IReadOnlyList<CreatureState> targets, DamageVar damageVar, CreatureState dealer)
        => Damage(cs, targets, damageVar.BaseValue, damageVar.Props, dealer);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, IReadOnlyList<CreatureState> targets, decimal amount, ValueProp props, CreatureState dealer)
        => Damage(cs, targets, amount, props, dealer, null, null);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, DamageVar damageVar, CreatureState dealer)
        => Damage(cs, target, damageVar.BaseValue, damageVar.Props, dealer);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState dealer)
        => Damage(cs, new[] { target }, amount, props, dealer, null, null);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, DamageVar damageVar, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        => Damage(cs, new[] { target }, damageVar.BaseValue, damageVar.Props, dealer, cardSource, cardPlay);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        => Damage(cs, new[] { target }, amount, props, dealer, cardSource, cardPlay);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, IReadOnlyList<CreatureState>? targets, DamageVar damageVar, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
        => Damage(cs, targets, damageVar.BaseValue, damageVar.Props, dealer, cardSource, cardPlay);

    public static IReadOnlyList<DamageResult> Damage(CombatState cs, IReadOnlyList<CreatureState>? targets, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, CardPlay? cardPlay)
    {
        if (targets == null)
        {
            return Array.Empty<DamageResult>();
        }
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
        var rs = cs.Run;
        var combatState = CombatStatus.CombatOf(cs, targetList[0]);
        for (int t = 0; t < targetList.Length; t++)
        {
            var originalTarget = targetList[t];
            if (originalTarget.IsDead)
            {
                continue;
            }
            List<AbstractModel>? modifiers = null;
            decimal modifiedAmount = Hook.ModifyDamage(rs, combatState, originalTarget, dealer, amount, props, cardSource, cardPlay, ModifyDamageHookType.All, ref modifiers);
            Hook.AfterModifyingDamageAmount(rs, combatState, cardSource, modifiers);
            Hook.BeforeDamageReceived(rs, combatState, originalTarget, modifiedAmount, props, dealer, cardSource);
            var blocker = CombatStatus.PetOwnerOf(cs, originalTarget) ?? originalTarget;
            decimal blockedDamage = CreatureInternals.DamageBlockInternal(blocker, modifiedAmount, props);
            modifiers = null;
            decimal unblockedDamage = Hook.ModifyHpLost(rs, combatState, originalTarget, Math.Max(modifiedAmount - blockedDamage, 0m), props, dealer, cardSource, HpLossHookPhase.BeforeOsty, ref modifiers);
            Hook.AfterModifyingHpLostBeforeOsty(rs, combatState, modifiers);
            var unblockedDamageTarget = combatState == null ? originalTarget : Hook.ModifyUnblockedDamageTarget(combatState, originalTarget, unblockedDamage, props, dealer);
            modifiers = null;
            unblockedDamage = Hook.ModifyHpLost(rs, combatState, unblockedDamageTarget, unblockedDamage, props, dealer, cardSource, HpLossHookPhase.AfterOsty, ref modifiers);
            Hook.AfterModifyingHpLostAfterOsty(rs, combatState, modifiers);
            var unblockedDamageResult = CreatureInternals.LoseHpInternal(unblockedDamageTarget, unblockedDamage, props);
            int firstResult = results.Count;
            results.Add(unblockedDamageResult);
            bool wasBlockBroken = originalTarget.Block <= 0 && blockedDamage > 0m;
            bool wasFullyBlocked = (props & ValueProp.Unblockable) == 0 && (blockedDamage > 0m || originalTarget.Block > 0) && (int)unblockedDamage == 0;
            if (ReferenceEquals(originalTarget, unblockedDamageTarget))
            {
                unblockedDamageResult.BlockedDamage = (int)blockedDamage;
                unblockedDamageResult.WasBlockBroken = wasBlockBroken;
                unblockedDamageResult.WasFullyBlocked = wasFullyBlocked;
            }
            else
            {
                modifiers = null;
                decimal originalTargetDamage = Hook.ModifyHpLost(rs, combatState, originalTarget, unblockedDamageResult.OverkillDamage, props, dealer, cardSource, HpLossHookPhase.AfterOsty, ref modifiers);
                Hook.AfterModifyingHpLostAfterOsty(rs, combatState, modifiers);
                var damageResult = originalTargetDamage > 0m
                    ? CreatureInternals.LoseHpInternal(originalTarget, originalTargetDamage, props)
                    : new DamageResult(originalTarget, props);
                damageResult.BlockedDamage = (int)blockedDamage;
                damageResult.WasBlockBroken = wasBlockBroken;
                damageResult.WasFullyBlocked = wasFullyBlocked;
                results.Add(damageResult);
            }
            for (int r = firstResult; r < results.Count; r++)
            {
                if (cs.IsInProgress && !CombatStatus.IsEnding(cs))
                {
                    var historyCombat = combatState ?? throw new InvalidOperationException("CreatureCmd.Damage: the first target is not in the combat, so the game records CombatHistory.DamageReceived against a null combat state (Commands/CreatureCmd.cs:3679, :3735-3738).");
                    historyCombat.History.AppendDamageReceived(historyCombat, results[r].Receiver, dealer, results[r], cardSource);
                }
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
                Hook.AfterCurrentHpChanged(rs, combatState, originalTarget, -result.UnblockedDamage);
            }
            if (combatState != null)
            {
                Hook.AfterDamageGiven(combatState, dealer, result, props, originalTarget, cardSource);
            }
            if (!result.WasTargetKilled || !originalTarget.IsDead)
            {
                Hook.AfterDamageReceived(rs, combatState, originalTarget, result, props, dealer, cardSource);
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

    public static decimal GainBlock(CombatState cs, CreatureState creature, BlockVar blockVar, CardPlay? cardPlay)
        => GainBlock(cs, creature, blockVar.BaseValue, blockVar.Props, cardPlay);

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
            cs.History.AppendBlockGained(cs, creature, (int)modifiedAmount, props, cardPlay);
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
            Hook.AfterCurrentHpChanged(cs.Run, cs, creature, amount);
        }
    }

    public static void SetCurrentHp(CombatState cs, CreatureState creature, decimal amount)
    {
        decimal oldHp = creature.CurrentHp;
        CreatureInternals.SetCurrentHpInternal(creature, amount);
        if (amount != oldHp)
        {
            Hook.AfterCurrentHpChanged(cs.Run, CombatStatus.CombatOf(cs, creature), creature, amount - oldHp);
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
        if (anyPlayer && CombatStatus.AllPlayersDead(cs))
        {
            CombatStatus.LoseCombat(cs);
        }
        else if (cs.IsInProgress)
        {
            for (int i = 0; i < snapshot.Length; i++)
            {
                var creature = snapshot[i];
                if (creature.IsPlayer && creature.IsDead && CombatStatus.IsAttached(cs, creature) && cs.CurrentSide == CombatSide.Player)
                {
                    throw new InvalidOperationException("CreatureCmd.Kill: a dead player while another player lives ends that player's turn through PlayerCmd.EndTurn (Commands/CreatureCmd.cs:2414-2438), a multiplayer-only path.");
                }
            }
        }
    }

    private static void KillWithoutCheckingWinCondition(CombatState cs, CreatureState creature, bool force, int recursion)
    {
        var combatState = CombatStatus.CombatOf(cs, creature);
        if (combatState == null && !creature.IsPlayer)
        {
            return;
        }
        if (!cs.IsInProgress && creature.IsPlayer && CombatStatus.PlayerCount(cs) > 1 && !force)
        {
            Heal(cs, creature, 1m);
            return;
        }
        var rs = cs.Run;
        int currentHp = creature.CurrentHp;
        if (currentHp > 0)
        {
            CreatureInternals.LoseHpInternal(creature, currentHp, ValueProp.Unblockable | ValueProp.Unpowered);
            Hook.AfterCurrentHpChanged(rs, combatState, creature, -currentHp);
        }
        Hook.BeforeDeath(rs, combatState, creature);
        AbstractModel? preventer = null;
        if (force || creature.MaxHp <= 0 || Hook.ShouldDie(rs, combatState, creature, out preventer))
        {
            InvokeDiedEvent(cs, creature);
            bool shouldRemoveFromCombat = combatState != null && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, creature);
            Hook.AfterDeath(rs, combatState, creature, false);
            var teammates = new List<CreatureState>(4);
            if (combatState != null)
            {
                CombatStatus.GetTeammatesOf(combatState, creature, teammates);
                teammates.RemoveAll(static t => !t.IsAlive);
            }
            if (shouldRemoveFromCombat && creature.Side == CombatSide.Enemy && creature is MonsterState monster && cs.Monsters.Contains(monster))
            {
                if (!(monster.HasAi && monster.Ai.IsPerformingMove))
                {
                    cs.RemoveMonster(monster);
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
                player.OrbQueue.Clear();
                if (CombatStatus.IsOstyAlive(cs, player))
                {
                    Kill(cs, CombatStatus.GetOsty(cs, player)!, force);
                }
                player.DeactivateHooks();
                if (combatState != null)
                {
                    HandlePlayerDeath(cs, player);
                }
            }
            return;
        }
        if (recursion >= 10)
        {
            throw new InvalidOperationException("Combat is ending, but something is continually preventing the last creature from being killed!");
        }
        Hook.AfterDeath(rs, combatState, creature, true);
        Hook.AfterPreventingDeath(rs, combatState, preventer!, creature);
        if (creature.IsDead)
        {
            KillWithoutCheckingWinCondition(cs, creature, force, recursion + 1);
        }
    }

    private static void InvokeDiedEvent(CombatState cs, CreatureState creature)
    {
        var owner = CombatStatus.PetOwnerOf(cs, creature);
        if (owner == null)
        {
            return;
        }
        int index = owner.PetIds.IndexOf(creature.CombatId);
        if (index < 0)
        {
            return;
        }
        if (Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, creature))
        {
            owner.PetIds.RemoveAt(index);
        }
    }

    private static void HandlePlayerDeath(CombatState cs, PlayerState player)
    {
        if (!cs.IsInProgress || CombatStatus.AllCombatPlayersDead(cs))
        {
            return;
        }
        cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, HandlePlayerDeathFidelitySite);
        if (cs.CardOrPotionEffectDepth <= 0)
        {
            var cards = new List<CardInstance>();
            cards.AddRange(player.Hand.Cards);
            cards.AddRange(player.DrawPile.Cards);
            cards.AddRange(player.DiscardPile.Cards);
            cards.AddRange(player.ExhaustPile.Cards);
            cards.AddRange(player.PlayPile.Cards);
            for (int i = 0; i < cards.Count; i++)
            {
                CardPileCmd.RemoveFromCombat(cs, cards[i]);
            }
        }
        EnergyCmd.SetEnergy(cs, 0m, player);
        StarsCmd.SetStars(cs, player, 0m);
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

    public static void Escape(CombatState cs, CreatureState creature)
    {
        if (creature.IsDead)
        {
            return;
        }
        if (!CombatStatus.IsAttached(cs, creature))
        {
            return;
        }
        var removed = new List<PowerModel>(creature.Powers.Count);
        CreatureInternals.RemoveAllPowersInternal(creature, removed);
        cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, EscapeFidelitySite);
        if (creature is MonsterState monster && cs.Monsters.Contains(monster))
        {
            cs.RemoveMonster(monster);
        }
        else if (creature is MonsterState ally)
        {
            cs.RemoveAlly(ally);
        }
        else
        {
            throw new InvalidOperationException("CreatureCmd.Escape: the sim cannot remove a player from its combat (Combat/CombatState.cs:739-768 removes it from _allies and unattaches it).");
        }
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
