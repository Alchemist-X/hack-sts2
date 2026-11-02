namespace Sts2Sim.Core.Relics;

internal static class PendingKernelCalls
{
    internal static void CardCmdUpgrade(CombatState? cs, CardInstance card)
        => CardCmd.Upgrade(cs, card);

    internal static void CardCmdDiscardAndDraw(CombatState cs, IReadOnlyList<CardInstance> cardsToDiscard, int cardsToDraw)
        => CardCmd.DiscardAndDraw(cs, cardsToDiscard, cardsToDraw);

    internal static IReadOnlyList<CardInstance> CardFactoryGetDistinctForCombat(CombatState cs, PlayerState player, CardType type, int count, Rng rng)
        => CardFactory.GetDistinctForCombat(cs, player, CardPools.ForCharacter(player.CharacterId).GetUnlockedCards(cs.Run.Unlocks, CardPools.MultiplayerConstraintFor(cs.Run)).Where(c => c.Type == type), count, rng).ToList();
}
