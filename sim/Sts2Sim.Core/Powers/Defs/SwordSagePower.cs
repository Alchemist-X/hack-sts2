namespace Sts2Sim.Core.Powers;

public sealed class SwordSagePower : PowerModel
{
    public const string PowerId = "SWORD_SAGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterPowerAmountChanged(CombatState cs, PowerModel power, decimal amount, CreatureState? applier, CardInstance? cardSource)
    {
        if (power is not SwordSagePower)
        {
            return;
        }
        if (!ReferenceEquals(power.Owner, Owner))
        {
            return;
        }
        if (Owner is not PlayerState player)
        {
            return;
        }
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                TryAddReplays(cs, cards[i], (int)amount);
            }
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (card.IsClone)
        {
            return;
        }
        TryAddReplays(cs, card, Amount);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        if (oldOwner is not PlayerState player)
        {
            return;
        }
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (string.Equals(cards[i].Id, ForgeCmd.SovereignBladeId, StringComparison.Ordinal))
                {
                    cards[i].BaseReplayCount -= Amount;
                }
            }
        }
    }

    private void TryAddReplays(CombatState cs, CardInstance card, int amount)
    {
        if (ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState) && string.Equals(card.Id, ForgeCmd.SovereignBladeId, StringComparison.Ordinal))
        {
            card.BaseReplayCount += amount;
        }
    }
}
