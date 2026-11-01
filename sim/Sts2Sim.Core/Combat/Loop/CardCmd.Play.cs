namespace Sts2Sim.Core.Combat.Pipeline;

public static partial class CardCmd
{
    public static void AutoPlay(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type = AutoPlayType.Default, bool skipXCapture = false)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        var owner = CardPileCmd.OwnerOf(cs, card);
        if (CombatStatus.IsOverOrEnding(cs) || owner.IsDead)
        {
            return;
        }
        if (CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Unplayable))
        {
            MoveToResultPileWithoutPlaying(cs, card);
            return;
        }
        if (!Hook.ShouldPlay(cs, card, out _, type))
        {
            MoveToResultPileWithoutPlaying(cs, card);
            return;
        }
        if (card.TargetType == TargetType.AnyEnemy)
        {
            if (target == null)
            {
                var hittable = new List<MonsterState>(cs.Monsters.Count);
                cs.GetHittableEnemies(hittable);
                target = cs.Rng.CombatTargets.NextItem(hittable);
            }
            if (target == null)
            {
                MoveToResultPileWithoutPlaying(cs, card);
                return;
            }
        }
        if (card.TargetType == TargetType.AnyAlly)
        {
            if (target == null)
            {
                var allies = cs.Allies;
                var items = new List<CreatureState>(allies.Count);
                for (int i = 0; i < allies.Count; i++)
                {
                    var ally = allies[i];
                    if (ally.IsAlive && ally.IsPlayer && !ReferenceEquals(ally, owner))
                    {
                        items.Add(ally);
                    }
                }
                target = cs.Rng.CombatTargets.NextItem(items);
            }
            if (target == null)
            {
                MoveToResultPileWithoutPlaying(cs, card);
                return;
            }
        }
        if (card.EnergyCost.CostsX && !skipXCapture)
        {
            card.EnergyCost.CapturedXValue = owner.Energy;
        }
        if (!skipXCapture)
        {
            card.LastStarsSpent = card.Def.HasStarCostX ? owner.Stars : Math.Max(0, card.GetStarCostWithModifiers(cs));
        }
        if (CardPileCmd.PileTypeOf(owner, card) == PileType.None)
        {
            CardPileCmd.Add(cs, card, PileType.Play);
        }
        Hook.BeforeCardAutoPlayed(cs, card, target, type);
        var resources = new ResourceInfo(
            EnergySpent: 0,
            EnergyValue: card.EnergyCost.GetAmountToSpend(cs, card),
            StarsSpent: 0,
            StarValue: Math.Max(0, card.GetStarCostWithModifiers(cs)));
        OnPlayWrapper(cs, card, target, isAutoPlay: true, resources);
    }

    public static void OnPlayWrapper(CombatState cs, CardInstance card, CreatureState? target, bool isAutoPlay, ResourceInfo resources)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        card.SetCurrentTarget(target);
        card.CurrentPlayIndex = 0;
        if (isAutoPlay)
        {
            CardPileCmd.Add(cs, card, PileType.Play);
        }
        else
        {
            CardPileCmd.AddDuringManualCardPlay(cs, card);
        }
        if (!CardPileCmd.IsInCombatPile(cs, card))
        {
            return;
        }
        var resultLocation = card.GetResultLocationForCardPlay(cs);
        List<AbstractModel>? modifiers = null;
        resultLocation = Hook.ModifyCardPlayResultLocation(cs, card, isAutoPlay, resources, resultLocation, ref modifiers);
        if (modifiers != null)
        {
            for (int i = 0; i < modifiers.Count; i++)
            {
                modifiers[i].AfterModifyingCardPlayResultLocation(cs, card, resultLocation);
            }
        }
        int playCount = GeneratePlayCount(cs, card, target);
        var owner = CardPileCmd.OwnerOf(cs, card);
        if (owner.IsDead)
        {
            return;
        }
        CombatLoop.BeginCardOrPotionEffect(cs, owner);
        bool deadExit;
        try
        {
            deadExit = PlayLoop(cs, card, target, isAutoPlay, resources, resultLocation, playCount);
        }
        catch (ChoicePendingException)
        {
            throw;
        }
        catch
        {
            CombatLoop.EndCardOrPotionEffect(cs, CardPileCmd.OwnerOf(cs, card));
            throw;
        }
        CombatLoop.EndCardOrPotionEffect(cs, CardPileCmd.OwnerOf(cs, card));
        if (deadExit)
        {
            return;
        }
        var originalOwner = CardPileCmd.OwnerOf(cs, card);
        if (originalOwner.PlayPile.Contains(card))
        {
            switch (resultLocation.PileType)
            {
                case PileType.None:
                    CardPileCmd.RemoveFromCombat(cs, card);
                    break;
                case PileType.Exhaust:
                    Exhaust(cs, card, causedByEthereal: false);
                    break;
                default:
                    CardPileCmd.Add(cs, card, resultLocation.PileType, resultLocation.Position);
                    break;
            }
        }
        CombatLoop.CheckForEmptyHand(cs, originalOwner);
        card.AfterCardPlayedCleanup();
    }

    public static int GeneratePlayCount(CombatState cs, CardInstance card, CreatureState? target)
    {
        int playCount = card.GetEnchantedReplayCount() + 1;
        List<AbstractModel>? modifiers = null;
        playCount = Hook.ModifyCardPlayCount(cs, card, playCount, target, ref modifiers);
        Hook.AfterModifyingCardPlayCount(cs, card, modifiers);
        return playCount;
    }

    public static void Discard(CombatState cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        Discard(cs, new[] { card });
    }

    public static void Discard(CombatState cs, IEnumerable<CardInstance> cards) => DiscardAndDraw(cs, cards, 0);

    public static void DiscardAndDraw(CombatState cs, IEnumerable<CardInstance> cardsToDiscard, int cardsToDraw)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cardsToDiscard);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var discardCards = new List<CardInstance>(cardsToDiscard);
        if (discardCards.Count == 0)
        {
            return;
        }
        var owner = CardPileCmd.OwnerOf(cs, discardCards[0]);
        List<CardInstance>? slyCards = null;
        var discardPile = owner.DiscardPile;
        for (int i = 0; i < discardCards.Count; i++)
        {
            var card = discardCards[i];
            if (CardPileCmd.IsSlyThisTurn(cs, card))
            {
                (slyCards ??= new List<CardInstance>(2)).Add(card);
            }
            CardPileCmd.Add(cs, card, discardPile);
            cs.History.AppendCardDiscarded(cs, card);
            Hook.AfterCardDiscarded(cs, card);
        }
        if (cardsToDraw > 0)
        {
            CardPileCmd.Draw(cs, cardsToDraw, owner);
        }
        if (slyCards != null)
        {
            for (int i = 0; i < slyCards.Count; i++)
            {
                AutoPlay(cs, slyCards[i], null, AutoPlayType.SlyDiscard);
            }
        }
    }

    public static CardPileAddResult? Exhaust(CombatState cs, CardInstance card, bool causedByEthereal = false)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return null;
        }
        var result = CardPileCmd.Add(cs, card, PileType.Exhaust);
        cs.History.AppendCardExhausted(cs, card);
        Hook.AfterCardExhausted(cs, card, causedByEthereal);
        return result;
    }

    private static bool PlayLoop(CombatState cs, CardInstance card, CreatureState? target, bool isAutoPlay, ResourceInfo resources, CardLocation resultLocation, int playCount)
    {
        for (int i = 0; i < playCount && !CombatStatus.IsOverOrEnding(cs); i++)
        {
            card.CurrentPlayIndex = i;
            var play = new CardPlay(cs, card, target)
            {
                ResultPile = resultLocation.PileType,
                Resources = resources,
                IsAutoPlay = isAutoPlay,
                PlayIndex = i,
                PlayCount = playCount
            };
            Hook.BeforeCardPlayed(cs, play);
            if (cs.IsInProgress)
            {
                cs.History.AppendCardPlayStarted(cs, play);
            }
            card.OnPlay(cs, play);
            if (CardPileCmd.OwnerOf(cs, card).IsDead)
            {
                return true;
            }
            var enchantment = card.Enchantment;
            if (enchantment != null)
            {
                enchantment.OnPlay(cs, play);
                if (CardPileCmd.OwnerOf(cs, card).IsDead)
                {
                    return true;
                }
            }
            var affliction = card.Affliction;
            if (affliction != null)
            {
                affliction.OnPlay(cs, target);
                if (CardPileCmd.OwnerOf(cs, card).IsDead)
                {
                    return true;
                }
            }
            if (cs.IsInProgress)
            {
                cs.History.AppendCardPlayFinished(cs, play);
            }
            if (cs.IsInProgress)
            {
                Hook.AfterCardPlayed(cs, play);
                if (CardPileCmd.OwnerOf(cs, card).IsDead)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void MoveToResultPileWithoutPlaying(CombatState cs, CardInstance card)
    {
        CardPileCmd.Add(cs, card, PileType.Play);
        var owner = CardPileCmd.OwnerOf(cs, card);
        if (!owner.PlayPile.Contains(card))
        {
            return;
        }
        if (card.IsDupe)
        {
            CardPileCmd.RemoveFromCombat(cs, card);
            return;
        }
        if (card.ExhaustOnNextPlay || CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Exhaust))
        {
            Exhaust(cs, card);
            return;
        }
        CardPileCmd.Add(cs, card, PileType.Discard);
    }
}
