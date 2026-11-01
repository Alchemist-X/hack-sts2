namespace Sts2Sim.Core.Relics;

internal static class PendingKernelCalls
{
    internal static void CardCmdUpgrade(CombatState? cs, CardInstance card)
        => throw new NotImplementedException("cards-kernel: CardCmd.Upgrade(card) (Commands/CardCmd.cs:1882-1946) is not in this tree; integrate routes PendingKernelCalls.CardCmdUpgrade to it (relics-potions.md section 9)");

    internal static void CardCmdDiscardAndDraw(CombatState cs, IReadOnlyList<CardInstance> cardsToDiscard, int cardsToDraw)
        => throw new NotImplementedException("loop: CardCmd.DiscardAndDraw(cards, count) (Commands/CardCmd.cs:1839, state machine :721-960) is not in this tree; integrate routes PendingKernelCalls.CardCmdDiscardAndDraw to it (relics-potions.md section 9)");

    internal static IReadOnlyList<CardInstance> CardFactoryGetDistinctForCombat(CombatState cs, PlayerState player, CardType type, int count, Rng rng)
        => throw new NotImplementedException("cards-kernel: CardFactory.GetDistinctForCombat(player, character pool GetUnlockedCards(unlocks, constraint).Where(c => c.Type == type), count, rng) (Factories/CardFactory.cs:93-102, Models.Potions/AttackPotion.cs:71) is not in this tree; integrate routes PendingKernelCalls.CardFactoryGetDistinctForCombat to it (relics-potions.md section 9)");
}
