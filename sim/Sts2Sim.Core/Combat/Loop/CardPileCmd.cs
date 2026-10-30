namespace Sts2Sim.Core.Combat.Loop;

public static class CardPileCmd
{
    public const int MaxCardsInHand = CardPile.MaxCardsInHand;

    private static readonly Comparison<CardInstance> ShuffleSortOrder = CompareForShuffle;

    public static CardInstance? Draw(CombatState cs, PlayerState player)
    {
        CardInstance? first = null;
        DrawInternal(cs, 1m, player, false, ref first);
        return first;
    }

    public static int Draw(CombatState cs, decimal count, PlayerState player, bool fromHandDraw = false)
    {
        CardInstance? first = null;
        return DrawInternal(cs, count, player, fromHandDraw, ref first);
    }

    public static void Shuffle(CombatState cs, PlayerState player)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var drawPile = player.DrawPile;
        var discardPile = player.DiscardPile;
        var cards = new List<CardInstance>(discardPile.Count + drawPile.Count);
        cards.AddRange(discardPile.Cards);
        cards.AddRange(drawPile.Cards);
        cards.StableShuffle(cs.Rng.Shuffle, ShuffleSortOrder);
        Hook.ModifyShuffleOrder(cs, player, cards, isInitialShuffle: false);
        AddToPile(cs, cards, drawPile, CardPilePosition.Bottom);
        if (!CombatStatus.IsOverOrEnding(cs))
        {
            Hook.AfterShuffle(cs, player);
        }
    }

    public static void ShuffleIfNecessary(CombatState cs, PlayerState player)
    {
        if (player.DrawPile.Count == 0 && player.DiscardPile.Count > 0)
        {
            Shuffle(cs, player);
        }
    }

    public static void ShuffleInitialDrawPile(CombatState cs, PlayerState player)
    {
        var cards = player.DrawPile.MutableCards;
        cards.UnstableShuffle(cs.Rng.Shuffle);
        Hook.ModifyShuffleOrder(cs, player, cards, isInitialShuffle: true);
    }

    public static void Add(CombatState cs, CardInstance card, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
    {
        AddToPile(cs, card, cs.Player.GetPile(newPileType), position);
    }

    public static void Add(CombatState cs, IReadOnlyList<CardInstance> cards, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
    {
        if (cards.Count == 0)
        {
            return;
        }
        AddToPile(cs, cards, cs.Player.GetPile(newPileType), position);
    }

    public static void AddGeneratedCardToCombat(CombatState cs, CardInstance card, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
    {
        AddGeneratedCardToCombat(cs, card, newPileType, null, position);
    }

    public static void AddGeneratedCardToCombat(CombatState cs, CardInstance card, PileType newPileType, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        if (cs.Player.FindPileContaining(card) != null)
        {
            throw new InvalidOperationException("You are not allowed to generate cards that already have a pile");
        }
        if (!CardPile.IsCombatPileType(newPileType))
        {
            throw new InvalidOperationException("You are not allowed to added generated cards to a non combat pile");
        }
        AddToPile(cs, card, cs.Player.GetPile(newPileType), position);
        Hook.AfterCardGeneratedForCombat(cs, card, creator);
    }

    public static void Discard(CombatState cs, CardInstance card)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        if (card.IsSlyThisTurn)
        {
            throw new NotSupportedException($"{card.Id}#{card.InstanceId} is Sly this turn; the Sly auto-play on discard (CardCmd.DiscardAndDraw) is not ported in P0.");
        }
        AddToPile(cs, card, cs.Player.DiscardPile, CardPilePosition.Bottom);
        Hook.AfterCardDiscarded(cs, card);
    }

    public static void Exhaust(CombatState cs, CardInstance card, bool causedByEthereal = false)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        AddToPile(cs, card, cs.Player.ExhaustPile, CardPilePosition.Bottom);
        Hook.AfterCardExhausted(cs, card, causedByEthereal);
    }

    public static void RemoveFromCombat(CombatState cs, CardInstance card)
    {
        var pile = cs.Player.FindPileContaining(card)
            ?? throw new InvalidOperationException("Card must be in a combat pile for it to be removed");
        pile.RemoveInternal(card);
    }

    public static int CompareForShuffle(CardInstance a, CardInstance b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }
        int byId = string.CompareOrdinal(a.Id, b.Id);
        if (byId != 0)
        {
            return byId;
        }
        return a.UpgradeLevel.CompareTo(b.UpgradeLevel);
    }

    internal static bool AddToPile(CombatState cs, CardInstance card, CardPile newPile, CardPilePosition position)
    {
        if (!CanMoveIntoCombatPile(cs))
        {
            return false;
        }
        MoveCard(cs, cs.Player, card, newPile, position);
        return true;
    }

    internal static void AddToPile(CombatState cs, IReadOnlyList<CardInstance> cards, CardPile newPile, CardPilePosition position)
    {
        if (!CanMoveIntoCombatPile(cs))
        {
            return;
        }
        var player = cs.Player;
        for (int i = 0; i < cards.Count; i++)
        {
            MoveCard(cs, player, cards[i], newPile, position);
        }
    }

    private static int DrawInternal(CombatState cs, decimal count, PlayerState player, bool fromHandDraw, ref CardInstance? first)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return 0;
        }
        if (!Hook.ShouldDraw(cs, player, fromHandDraw, out var preventer))
        {
            Hook.AfterPreventingDraw(cs, preventer!);
            return 0;
        }
        var hand = player.Hand;
        var drawPile = player.DrawPile;
        int drawsRequested = count > 0m ? (int)Math.Ceiling(count) : 0;
        if (drawsRequested == 0)
        {
            return 0;
        }
        int room = Math.Max(0, MaxCardsInHand - hand.Count);
        if (room == 0)
        {
            return 0;
        }
        int drawn = 0;
        for (int i = 0; i < drawsRequested; i++)
        {
            if (room <= 0 || CombatStatus.IsOverOrEnding(cs) || !IsDrawPossible(player))
            {
                break;
            }
            ShuffleIfNecessary(cs, player);
            if (!IsDrawPossible(player))
            {
                break;
            }
            if (drawPile.Count == 0 || hand.Count >= MaxCardsInHand)
            {
                break;
            }
            var card = drawPile[0];
            first ??= card;
            drawn++;
            AddToPile(cs, card, hand, CardPilePosition.Bottom);
            Hook.AfterCardDrawn(cs, card, fromHandDraw);
            room = Math.Max(0, MaxCardsInHand - hand.Count);
        }
        return drawn;
    }

    private static bool IsDrawPossible(PlayerState player)
    {
        if (player.DrawPile.Count + player.DiscardPile.Count == 0)
        {
            return false;
        }
        return player.Hand.Count < MaxCardsInHand;
    }

    private static bool CanMoveIntoCombatPile(CombatState cs)
    {
        if (CombatStatus.IsEnding(cs))
        {
            return false;
        }
        if (cs.Player.IsDead)
        {
            return false;
        }
        return cs.IsInProgress;
    }

    private static void MoveCard(CombatState cs, PlayerState player, CardInstance card, CardPile newPile, CardPilePosition position)
    {
        var oldPile = player.FindPileContaining(card);
        var targetPile = newPile;
        if (targetPile.Type == PileType.Hand && targetPile.Count >= MaxCardsInHand)
        {
            targetPile = player.DiscardPile;
        }
        oldPile?.RemoveInternal(card);
        int index = position switch
        {
            CardPilePosition.Bottom => -1,
            CardPilePosition.Top => 0,
            CardPilePosition.Random => cs.Rng.Shuffle.NextInt(targetPile.Count + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
        };
        targetPile.AddInternal(card, index);
        if (oldPile == null)
        {
            Hook.AfterCardEnteredCombat(cs, card);
        }
    }
}
