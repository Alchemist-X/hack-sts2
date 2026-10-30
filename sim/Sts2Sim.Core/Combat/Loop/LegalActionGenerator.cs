namespace Sts2Sim.Core.Combat.Loop;

public static class LegalActionGenerator
{
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
        buffer.Add(GameAction.EndTurn);
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
            default:
                error = $"Unknown action kind {action.Kind}.";
                return false;
        }
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
