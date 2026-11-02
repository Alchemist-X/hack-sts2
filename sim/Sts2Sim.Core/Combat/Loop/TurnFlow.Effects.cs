namespace Sts2Sim.Core.Combat.Loop;

public static partial class CombatLoop
{
    public static bool IsExecutingCardOrPotionEffect(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        return cs.CardOrPotionEffectDepth > 0;
    }

    public static void BeginCardOrPotionEffect(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        cs.CardOrPotionEffectDepth++;
    }

    public static void EndCardOrPotionEffect(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        int depth = cs.CardOrPotionEffectDepth - 1;
        if (depth <= 0)
        {
            cs.CardOrPotionEffectDepth = 0;
            if (player.IsDead)
            {
                RemoveDeadPlayerCardsFromCombat(cs, player);
            }
        }
        else
        {
            cs.CardOrPotionEffectDepth = depth;
        }
    }

    public static void RemoveDeadPlayerCardsFromCombat(CombatState cs, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        if (!cs.IsInProgress || AllPlayersDead(cs))
        {
            return;
        }
        var cards = new List<CardInstance>();
        cards.AddRange(player.Hand.Cards);
        cards.AddRange(player.DrawPile.Cards);
        cards.AddRange(player.DiscardPile.Cards);
        cards.AddRange(player.ExhaustPile.Cards);
        cards.AddRange(player.PlayPile.Cards);
        CardPileCmd.RemoveFromCombat(cs, cards);
    }

    public static void CheckForEmptyHand(CombatState cs, PlayerState player)
    {
        if (cs.IsInProgress && !IsExecutingCardOrPotionEffect(cs, player) && player.Hand.Count == 0)
        {
            Hook.AfterHandEmptied(cs, player);
        }
    }

    private static bool AllPlayersDead(CombatState cs)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.IsAlive)
            {
                return false;
            }
        }
        return true;
    }
}
