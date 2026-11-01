namespace Sts2Sim.Core.Combat.Loop;

public static class LegalActionGenerator
{
    public const int PlayerAllyTargetIndex = -2;

    public const int FirstPetTargetIndex = -3;

    public static void Fill(CombatState cs, List<GameAction> buffer)
    {
        if (!IsAtDecisionPoint(cs))
        {
            return;
        }
        var hand = cs.Player.Hand;
        var monsters = cs.Monsters;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (!CanPlay(cs, card, out _))
            {
                continue;
            }
            if (card.TargetType == TargetType.AnyEnemy)
            {
                for (int t = 0; t < monsters.Count; t++)
                {
                    if (IsLegalEnemyTarget(cs, card, monsters[t]))
                    {
                        buffer.Add(GameAction.Play(i, t));
                    }
                }
            }
            else if (card.IsValidTarget(cs, null))
            {
                buffer.Add(GameAction.Play(i));
            }
        }
        FillPotions(cs, buffer);
        buffer.Add(GameAction.EndTurn);
    }

    public static void Fill(CombatState cs, ChoicePrompt? prompt, IReadOnlyList<int> picks, List<GameAction> buffer)
    {
        if (prompt == null)
        {
            Fill(cs, buffer);
        }
        else
        {
            FillPrompt(prompt, picks, buffer);
        }
    }

    public static void FillPrompt(ChoicePrompt prompt, IReadOnlyList<int> picks, List<GameAction> buffer)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(picks);
        int n = prompt.Options.Count;
        int count = picks.Count;
        int start = buffer.Count;
        if (count < prompt.MaxSelect)
        {
            if (prompt.Ordered)
            {
                for (int i = 0; i < n; i++)
                {
                    if (!IsPicked(picks, i))
                    {
                        buffer.Add(GameAction.Pick(i));
                    }
                }
            }
            else
            {
                int last = count == 0 ? -1 : picks[count - 1];
                int target = prompt.RequireConfirm ? prompt.MinSelect : prompt.MaxSelect;
                for (int i = last + 1; i < n; i++)
                {
                    if (IsPicked(picks, i))
                    {
                        continue;
                    }
                    int need = target - (count + 1);
                    if (need > 0 && UnpickedAbove(picks, i, n) < need)
                    {
                        continue;
                    }
                    buffer.Add(GameAction.Pick(i));
                }
            }
        }
        if (ChoiceRuntime.CanConfirm(prompt, count))
        {
            buffer.Add(GameAction.Confirm);
        }
        if (prompt.Cancelable)
        {
            buffer.Add(GameAction.Cancel);
        }
        if (buffer.Count == start && count < prompt.MaxSelect)
        {
            for (int i = 0; i < n; i++)
            {
                if (!IsPicked(picks, i))
                {
                    buffer.Add(GameAction.Pick(i));
                }
            }
        }
    }

    public static bool CanPlay(CombatState cs, CardInstance card, out UnplayableReason reason) => card.CanPlay(cs, out reason);

    public static bool IsAtDecisionPoint(CombatState cs)
        => cs.Outcome == CombatOutcome.InProgress && cs.IsInProgress && cs.CurrentSide == CombatSide.Player;

    public static bool IsLegalEnemyTarget(CombatState cs, CardInstance card, MonsterState target)
    {
        if (!card.IsValidTarget(cs, target))
        {
            return false;
        }
        if (!Hook.ShouldAllowHitting(cs, target))
        {
            return false;
        }
        return Hook.ShouldAllowTargeting(cs, target);
    }

    public static bool IsLegal(CombatState cs, GameAction action) => TryResolve(cs, action, out _, out _);

    public static bool IsLegal(CombatState cs, ChoicePrompt? prompt, IReadOnlyList<int> picks, GameAction action)
        => TryResolve(cs, prompt, picks, action, out _, out _);

    public static bool TryResolve(CombatState cs, ChoicePrompt? prompt, IReadOnlyList<int> picks, GameAction action, out CreatureState? target, out string? error)
    {
        if (prompt == null)
        {
            return TryResolve(cs, action, out target, out error);
        }
        target = null;
        return TryResolvePrompt(prompt, picks, action, out error);
    }

    public static bool TryResolve(CombatState cs, GameAction action, out CreatureState? target, out string? error)
    {
        target = null;
        if (cs.Outcome != CombatOutcome.InProgress)
        {
            error = $"The combat is over ({cs.Outcome}).";
            return false;
        }
        if (!cs.IsInProgress)
        {
            error = cs.IsStarting ? "The combat is still being set up." : "The combat is not in progress.";
            return false;
        }
        if (cs.CurrentSide != CombatSide.Player)
        {
            error = $"The combat is not at a player decision point (current side {cs.CurrentSide}).";
            return false;
        }
        switch (action.Kind)
        {
            case ActionKind.EndTurn:
                if (action.CardIndex != GameAction.NoIndex || action.TargetIndex != GameAction.NoIndex)
                {
                    error = $"EndTurn takes no card or target index ({action.CardIndex}, {action.TargetIndex}).";
                    return false;
                }
                error = null;
                return true;
            case ActionKind.PlayCard:
                return TryResolvePlay(cs, action, out target, out error);
            case ActionKind.UsePotion:
                return TryResolveUsePotion(cs, action, out target, out error);
            case ActionKind.DiscardPotion:
                return TryResolveDiscardPotion(cs, action, out error);
            case ActionKind.ChoosePick:
            case ActionKind.ChooseConfirm:
            case ActionKind.ChooseCancel:
                error = $"{action} answers a choice prompt, and no prompt is pending.";
                return false;
            default:
                error = $"Unknown action kind {action.Kind}.";
                return false;
        }
    }

    public static bool TryResolvePrompt(ChoicePrompt prompt, IReadOnlyList<int> picks, GameAction action, out string? error)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(picks);
        switch (action.Kind)
        {
            case ActionKind.ChoosePick:
            {
                if (action.TargetIndex != GameAction.NoIndex)
                {
                    error = $"Pick takes no target index (got {action.TargetIndex}).";
                    return false;
                }
                int option = action.CardIndex;
                int n = prompt.Options.Count;
                if ((uint)option >= (uint)n)
                {
                    error = $"Option {option} is outside the {n} options of {prompt.SiteId}.";
                    return false;
                }
                if (IsPicked(picks, option))
                {
                    error = $"Option {option} of {prompt.SiteId} is already picked.";
                    return false;
                }
                if (picks.Count >= prompt.MaxSelect)
                {
                    error = $"{prompt.SiteId} allows at most {prompt.MaxSelect} picks.";
                    return false;
                }
                error = null;
                return true;
            }
            case ActionKind.ChooseConfirm:
                if (action.CardIndex != GameAction.NoIndex || action.TargetIndex != GameAction.NoIndex)
                {
                    error = $"Confirm takes no card or target index ({action.CardIndex}, {action.TargetIndex}).";
                    return false;
                }
                if (!ChoiceRuntime.CanConfirm(prompt, picks.Count))
                {
                    error = prompt.RequireConfirm || prompt.CanSkip
                        ? $"{prompt.SiteId} needs {prompt.MinSelect}..{prompt.MaxSelect} picks before Confirm (has {picks.Count})."
                        : $"{prompt.SiteId} completes on its last pick and has no Confirm.";
                    return false;
                }
                error = null;
                return true;
            case ActionKind.ChooseCancel:
                if (action.CardIndex != GameAction.NoIndex || action.TargetIndex != GameAction.NoIndex)
                {
                    error = $"Cancel takes no card or target index ({action.CardIndex}, {action.TargetIndex}).";
                    return false;
                }
                if (!prompt.Cancelable)
                {
                    error = $"{prompt.SiteId} cannot be cancelled.";
                    return false;
                }
                error = null;
                return true;
            default:
                error = $"A choice prompt is pending ({prompt.SiteId}); {action} is not legal until it is answered.";
                return false;
        }
    }

    public static bool TryResolvePotionTarget(CombatState cs, PotionModel potion, int targetIndex, out CreatureState? target, out string? error)
    {
        ArgumentNullException.ThrowIfNull(potion);
        target = null;
        if (targetIndex == GameAction.NoIndex)
        {
            target = IsSingleTarget(potion.TargetType) ? cs.Player : null;
            error = null;
            return true;
        }
        if (targetIndex >= 0)
        {
            var monsters = cs.Monsters;
            if (targetIndex >= monsters.Count)
            {
                error = $"Target index {targetIndex} is outside the monster list (size {monsters.Count}).";
                return false;
            }
            target = monsters[targetIndex];
            error = null;
            return true;
        }
        if (targetIndex == PlayerAllyTargetIndex)
        {
            error = $"The player is targeted with {GameAction.NoIndex}; target index {PlayerAllyTargetIndex} (Allies[0]) is not a separate target.";
            return false;
        }
        int allyIndex = PlayerAllyTargetIndex - targetIndex;
        var allies = cs.Allies;
        if (allyIndex >= allies.Count)
        {
            error = $"Target index {targetIndex} names ally {allyIndex}, outside the ally list (size {allies.Count}).";
            return false;
        }
        target = allies[allyIndex];
        error = null;
        return true;
    }

    public static bool IsSingleTarget(TargetType targetType)
        => targetType is TargetType.Self or TargetType.AnyEnemy or TargetType.AnyPlayer or TargetType.AnyAlly or TargetType.TargetedNoCreature;

    private static void FillPotions(CombatState cs, List<GameAction> buffer)
    {
        var player = cs.Player;
        var slots = player.PotionSlots;
        if (!PotionActionsOpen(player, out _))
        {
            return;
        }
        for (int slot = 0; slot < slots.Length; slot++)
        {
            var potion = slots[slot];
            if (potion is null || !IsPlayerUsable(potion.Usage))
            {
                continue;
            }
            var self = IsSingleTarget(potion.TargetType) ? (CreatureState)player : null;
            if (PotionActions.CanUse(cs, slot, self))
            {
                buffer.Add(GameAction.UsePotion(slot));
            }
            var monsters = cs.Monsters;
            for (int t = 0; t < monsters.Count; t++)
            {
                if (PotionActions.CanUse(cs, slot, monsters[t]))
                {
                    buffer.Add(GameAction.UsePotion(slot, t));
                }
            }
            var allies = cs.Allies;
            for (int a = 1; a < allies.Count; a++)
            {
                if (PotionActions.CanUse(cs, slot, allies[a]))
                {
                    buffer.Add(GameAction.UsePotion(slot, PlayerAllyTargetIndex - a));
                }
            }
        }
        for (int slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot] is not null)
            {
                buffer.Add(GameAction.DiscardPotion(slot));
            }
        }
    }

    private static bool TryResolveUsePotion(CombatState cs, GameAction action, out CreatureState? target, out string? error)
    {
        target = null;
        if (!TryGetPotion(cs, action.CardIndex, out var potion, out error))
        {
            return false;
        }
        if (!IsPlayerUsable(potion.Usage))
        {
            error = $"{potion.Id} (slot {action.CardIndex}) has usage {potion.Usage} and cannot be used by the player.";
            return false;
        }
        if (!TryResolvePotionTarget(cs, potion, action.TargetIndex, out var resolved, out error))
        {
            return false;
        }
        if (!PotionActions.CanUse(cs, action.CardIndex, resolved))
        {
            error = $"{potion.Id} (slot {action.CardIndex}) cannot be used on {(resolved == null ? "no target" : "creature " + resolved.CombatId)}.";
            return false;
        }
        target = resolved;
        error = null;
        return true;
    }

    private static bool TryResolveDiscardPotion(CombatState cs, GameAction action, out string? error)
    {
        if (action.TargetIndex != GameAction.NoIndex)
        {
            error = $"DiscardPotion takes no target index (got {action.TargetIndex}).";
            return false;
        }
        return TryGetPotion(cs, action.CardIndex, out _, out error);
    }

    private static bool TryGetPotion(CombatState cs, int slot, out PotionModel potion, out string? error)
    {
        potion = null!;
        var player = cs.Player;
        var slots = player.PotionSlots;
        if ((uint)slot >= (uint)slots.Length)
        {
            error = $"Potion slot {slot} is outside the {slots.Length} potion slots.";
            return false;
        }
        var held = slots[slot];
        if (held is null)
        {
            error = $"Potion slot {slot} is empty.";
            return false;
        }
        if (!PotionActionsOpen(player, out error))
        {
            return false;
        }
        potion = held;
        return true;
    }

    private static bool PotionActionsOpen(PlayerState player, out string? error)
    {
        if (player.Phase != PlayerTurnPhase.Play)
        {
            error = $"Potions are used and discarded only in the play phase (phase {player.Phase}).";
            return false;
        }
        if (player.IsDead)
        {
            error = "The potions' owner is dead.";
            return false;
        }
        if (!player.CanUseOrRemovePotions)
        {
            error = "The player cannot use or remove potions right now.";
            return false;
        }
        error = null;
        return true;
    }

    private static bool IsPlayerUsable(PotionUsage usage) => usage is PotionUsage.CombatOnly or PotionUsage.AnyTime;

    private static bool IsPicked(IReadOnlyList<int> picks, int option)
    {
        for (int i = 0; i < picks.Count; i++)
        {
            if (picks[i] == option)
            {
                return true;
            }
        }
        return false;
    }

    private static int UnpickedAbove(IReadOnlyList<int> picks, int option, int n)
    {
        int free = n - 1 - option;
        for (int i = 0; i < picks.Count; i++)
        {
            if (picks[i] > option)
            {
                free--;
            }
        }
        return free;
    }

    private static bool TryResolvePlay(CombatState cs, GameAction action, out CreatureState? target, out string? error)
    {
        target = null;
        var hand = cs.Player.Hand;
        if ((uint)action.CardIndex >= (uint)hand.Count)
        {
            error = $"Card index {action.CardIndex} is outside the hand (size {hand.Count}).";
            return false;
        }
        var card = hand[action.CardIndex];
        if (card.TargetType == TargetType.AnyEnemy)
        {
            var monsters = cs.Monsters;
            if ((uint)action.TargetIndex >= (uint)monsters.Count)
            {
                error = $"{card.Id} needs an enemy target; target index {action.TargetIndex} is outside the monster list (size {monsters.Count}).";
                return false;
            }
            var monster = monsters[action.TargetIndex];
            if (!IsLegalEnemyTarget(cs, card, monster))
            {
                error = $"{monster.MonsterId} (index {action.TargetIndex}) is not a legal target for {card.Id}.";
                return false;
            }
            target = monster;
        }
        else if (action.TargetIndex != GameAction.NoIndex)
        {
            error = $"{card.Id} has target type {card.TargetType} and takes no target index (got {action.TargetIndex}).";
            return false;
        }
        if (!CanPlay(cs, card, out var reason))
        {
            error = $"{card.Id} (hand index {action.CardIndex}) cannot be played: {reason}.";
            target = null;
            return false;
        }
        if (!card.IsValidTarget(cs, target))
        {
            error = $"{card.Id} (hand index {action.CardIndex}) has no valid target.";
            target = null;
            return false;
        }
        error = null;
        return true;
    }
}
