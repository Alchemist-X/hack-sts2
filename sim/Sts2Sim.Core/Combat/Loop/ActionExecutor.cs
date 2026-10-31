namespace Sts2Sim.Core.Combat.Loop;

public static class ActionExecutor
{
    public static void PlayCard(CombatState cs, int handIndex, CreatureState? target)
    {
        var hand = cs.Player.Hand;
        if ((uint)handIndex >= (uint)hand.Count)
        {
            throw new InvalidOperationException($"Card index {handIndex} is outside the hand (size {hand.Count}).");
        }
        var card = hand[handIndex];
        if (!LegalActionGenerator.CanPlay(cs, card, out var reason))
        {
            throw new InvalidOperationException($"{card.Id} (hand index {handIndex}) cannot be played: {reason}.");
        }
        if (!card.IsValidTarget(cs, target))
        {
            throw new InvalidOperationException($"{card.Id} (hand index {handIndex}) cannot target {(target == null ? "nothing" : "creature " + target.CombatId)}.");
        }
        var (energySpent, starsSpent) = SpendResources(cs, card);
        OnPlayWrapper(cs, card, target, isAutoPlay: false, energySpent, starsSpent);
    }

    public static (int EnergySpent, int StarsSpent) SpendResources(CombatState cs, CardInstance card)
    {
        var player = cs.Player;
        int energy = player.Energy;
        int energyToSpend = card.EnergyCost.GetAmountToSpend(cs, card);
        int starsToSpend = Math.Max(0, card.GetStarCostWithModifiers(cs));
        if (energyToSpend > energy && Hook.ShouldPayExcessEnergyCostWithStars(cs, player))
        {
            starsToSpend += (energyToSpend - energy) * 2;
            energyToSpend = energy;
        }
        EnergyCmd.SpendEnergy(cs, card, energyToSpend);
        SpendStars(cs, card, starsToSpend);
        return (energyToSpend, starsToSpend);
    }

    public static void OnPlayWrapper(CombatState cs, CardInstance card, CreatureState? target, bool isAutoPlay, int energySpent, int starsSpent)
    {
        var player = cs.Player;
        card.SetCurrentTarget(target);
        card.CurrentPlayIndex = 0;
        if (isAutoPlay)
        {
            CardPileCmd.Add(cs, card, PileType.Play, CardPilePosition.Bottom);
        }
        else
        {
            AddDuringManualCardPlay(cs, card);
        }
        var resultLocation = card.GetResultLocationForCardPlay(cs);
        int playCount = GeneratePlayCount(cs, card, target);
        if (player.IsDead)
        {
            return;
        }
        for (int i = 0; i < playCount && !CombatStatus.IsOverOrEnding(cs); i++)
        {
            card.CurrentPlayIndex = i;
            var play = new CardPlay(card, target)
            {
                ResultPile = resultLocation.PileType,
                EnergySpent = energySpent,
                StarsSpent = starsSpent,
                IsAutoPlay = isAutoPlay,
                PlayIndex = i,
                PlayCount = playCount
            };
            Hook.BeforeCardPlayed(cs, play);
            card.OnPlay(cs, play);
            if (player.IsDead)
            {
                return;
            }
            if (cs.IsInProgress)
            {
                Hook.AfterCardPlayed(cs, play);
                if (player.IsDead)
                {
                    return;
                }
            }
        }
        if (player.PlayPile.Contains(card))
        {
            switch (resultLocation.PileType)
            {
                case PileType.None:
                    CardPileCmd.RemoveFromCombat(cs, card);
                    break;
                case PileType.Exhaust:
                    CardPileCmd.Exhaust(cs, card, causedByEthereal: false);
                    break;
                default:
                    CardPileCmd.Add(cs, card, resultLocation.PileType, resultLocation.Position);
                    break;
            }
        }
        CombatLoop.CheckForEmptyHand(cs, player);
        card.AfterCardPlayedCleanup();
    }

    private static void AddDuringManualCardPlay(CombatState cs, CardInstance card)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var player = cs.Player;
        player.FindPileContaining(card)?.RemoveInternal(card);
        player.PlayPile.AddInternal(card);
    }

    private static int GeneratePlayCount(CombatState cs, CardInstance card, CreatureState? target)
    {
        int playCount = card.GetEnchantedReplayCount() + 1;
        List<AbstractModel>? modifiers = null;
        playCount = Hook.ModifyCardPlayCount(cs, card, playCount, target, ref modifiers);
        Hook.AfterModifyingCardPlayCount(cs, card, modifiers);
        return playCount;
    }

    private static void SpendStars(CombatState cs, CardInstance card, int amount)
    {
        card.LastStarsSpent = amount;
        if (amount > 0)
        {
            cs.Player.LoseStars(amount);
        }
    }
}
