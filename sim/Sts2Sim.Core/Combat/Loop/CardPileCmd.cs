namespace Sts2Sim.Core.Combat.Loop;

public static class CardPileCmd
{
    public const int MaxCardsInHand = CardPile.MaxCardsInHand;

    private static readonly Comparison<CardInstance> ShuffleSortOrder = CompareForShuffle;

    public static CardInstance? Draw(CombatState cs, PlayerState player)
    {
        CardInstance? first = null;
        DrawInternal(cs, 1m, player, false, null, ref first);
        return first;
    }

    public static int Draw(CombatState cs, decimal count, PlayerState player, bool fromHandDraw = false)
    {
        CardInstance? first = null;
        return DrawInternal(cs, count, player, fromHandDraw, null, ref first);
    }

    public static IReadOnlyList<CardInstance> DrawCards(CombatState cs, decimal count, PlayerState player, bool fromHandDraw = false)
    {
        var drawn = new List<CardInstance>();
        CardInstance? first = null;
        DrawInternal(cs, count, player, fromHandDraw, drawn, ref first);
        return drawn;
    }

    public static void DrawWithoutBlockingOnOtherPlayers(CombatState cs, decimal count, PlayerState player, CardInstance source, bool fromHandDraw = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        Draw(cs, count, player, fromHandDraw);
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
        Add(cs, cards, drawPile, CardPilePosition.Bottom);
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

    public static CardPileAddResult Add(CombatState cs, CardInstance card, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
        => Add(cs, card, newPileType, position, null);

    public static CardPileAddResult Add(CombatState cs, CardInstance card, PileType newPileType, CardPilePosition position, AbstractModel? clonedBy)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        return AddCore(cs.Run, cs, new[] { card }, newPileType, null, position, clonedBy, false)[0];
    }

    public static CardPileAddResult Add(CombatState cs, CardInstance card, CardPile newPile, CardPilePosition position = CardPilePosition.Bottom, AbstractModel? clonedBy = null)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(newPile);
        return AddCore(cs.Run, cs, new[] { card }, newPile.Type, newPile, position, clonedBy, false)[0];
    }

    public static IReadOnlyList<CardPileAddResult> Add(CombatState cs, IReadOnlyList<CardInstance> cards, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
        => Add(cs, cards, newPileType, position, null);

    public static IReadOnlyList<CardPileAddResult> Add(CombatState cs, IReadOnlyList<CardInstance> cards, PileType newPileType, CardPilePosition position, AbstractModel? clonedBy)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cards);
        return AddCore(cs.Run, cs, cards, newPileType, null, position, clonedBy, false);
    }

    public static IReadOnlyList<CardPileAddResult> Add(CombatState cs, IReadOnlyList<CardInstance> cards, CardPile newPile, CardPilePosition position = CardPilePosition.Bottom, AbstractModel? clonedBy = null, bool isChangingOwners = false)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(newPile);
        return AddCore(cs.Run, cs, cards, newPile.Type, newPile, position, clonedBy, isChangingOwners);
    }

    public static CardPileAddResult Add(RunState rs, CardInstance card, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom, AbstractModel? clonedBy = null)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(card);
        return AddCore(rs, rs.Combat, new[] { card }, newPileType, null, position, clonedBy, false)[0];
    }

    public static IReadOnlyList<CardPileAddResult> Add(RunState rs, IReadOnlyList<CardInstance> cards, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom, AbstractModel? clonedBy = null)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(cards);
        return AddCore(rs, rs.Combat, cards, newPileType, null, position, clonedBy, false);
    }

    public static void AddDuringManualCardPlay(CombatState cs, CardInstance card)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var owner = OwnerOf(cs, card);
        var oldPile = PileTypeOf(owner, card);
        RemoveFromCurrentPile(owner, card, oldPile);
        owner.PlayPile.AddInternal(card);
        Hook.AfterCardChangedPiles(cs.Run, cs, card, oldPile, null);
    }

    public static CardPileAddResult AddGeneratedCardToCombat(CombatState cs, CardInstance card, PileType newPileType, CardPilePosition position = CardPilePosition.Bottom)
        => AddGeneratedCardToCombat(cs, card, newPileType, null, position);

    public static CardPileAddResult AddGeneratedCardToCombat(CombatState cs, CardInstance card, PileType newPileType, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(card);
        var results = AddGeneratedCardsToCombat(cs, new[] { card }, newPileType, creator, position);
        if (results.Count == 0)
        {
            cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, GeneratedCardOutsideCombatSite);
            return new CardPileAddResult(false, card, null, PileType.None, null);
        }
        return results[0];
    }

    public static IReadOnlyList<CardPileAddResult> AddGeneratedCardsToCombat(CombatState cs, IEnumerable<CardInstance> cards, PileType newPileType, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cards);
        var list = new List<CardInstance>(cards);
        if (list.Count == 0)
        {
            return Array.Empty<CardPileAddResult>();
        }
        if (!cs.IsInProgress)
        {
            return Array.Empty<CardPileAddResult>();
        }
        for (int i = 0; i < list.Count; i++)
        {
            if (PileTypeOf(OwnerOf(cs, list[i]), list[i]) != PileType.None)
            {
                throw new InvalidOperationException("You are not allowed to generate cards that already have a pile");
            }
        }
        if (!CardPile.IsCombatPileType(newPileType))
        {
            throw new InvalidOperationException("You are not allowed to added generated cards to a non combat pile");
        }
        var results = new List<CardPileAddResult>(list.Count);
        for (int i = 0; i < list.Count; i++)
        {
            var card = list[i];
            cs.History.AppendCardGenerated(cs, card, creator);
            results.Add(Add(cs, card, OwnerOf(cs, card).GetPile(newPileType), position));
            Hook.AfterCardGeneratedForCombat(cs, card, creator);
        }
        return results;
    }

    public static void RemoveFromCombat(CombatState cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        RemoveFromCombat(cs, new[] { card });
    }

    public static void RemoveFromCombat(CombatState cs, IReadOnlyList<CardInstance> cards)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(cards);
        int n = cards.Count;
        if (n == 0)
        {
            return;
        }
        var oldPiles = new PileType[n];
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            var owner = OwnerOf(cs, card);
            var pile = PileTypeOf(owner, card);
            if (!CardPile.IsCombatPileType(pile))
            {
                throw new InvalidOperationException("Card must be in a combat pile for it to be removed");
            }
            oldPiles[i] = pile;
            RemoveFromCurrentPile(owner, card, pile);
        }
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            Hook.AfterCardChangedPiles(cs.Run, cs, card, oldPiles[i], null);
            RemoveFromCurrentPile(OwnerOf(cs, card), card, PileTypeOf(OwnerOf(cs, card), card));
            card.HasBeenRemovedFromState = true;
        }
    }

    public static void RemoveFromDeck(CombatState cs, CardInstance card) => RemoveFromDeck(cs.Run, card);

    public static void RemoveFromDeck(CombatState cs, IReadOnlyList<CardInstance> cards) => RemoveFromDeck(cs.Run, cards);

    public static void RemoveFromDeck(RunState rs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        RemoveFromDeck(rs, new[] { card });
    }

    public static void RemoveFromDeck(RunState rs, IReadOnlyList<CardInstance> cards)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(cards);
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            var owner = OwnerOf(rs, card);
            if (PileTypeOf(owner, card) != PileType.Deck)
            {
                throw new InvalidOperationException("You cannot remove a card that is not in the deck.");
            }
            Hook.BeforeCardRemoved(rs, card);
            RemoveFromCurrentPile(owner, card, PileTypeOf(owner, card));
            card.HasBeenRemovedFromState = true;
        }
    }

    public static void AddToCombatAndPreview(CombatState cs, IReadOnlyList<CreatureState> targets, string cardId, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
        => AddToCombatAndPreview(cs, targets, CardRegistry.Get(cardId), pileType, count, creator, position);

    public static void AddToCombatAndPreview(CombatState cs, IReadOnlyList<CreatureState> targets, CardDef def, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(targets);
        for (int i = 0; i < targets.Count; i++)
        {
            AddToCombatAndPreview(cs, targets[i], def, pileType, count, creator, position);
        }
    }

    public static void AddToCombatAndPreview(CombatState cs, CreatureState target, string cardId, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
        => AddToCombatAndPreview(cs, target, CardRegistry.Get(cardId), pileType, count, creator, position);

    public static void AddToCombatAndPreview(CombatState cs, CreatureState target, CardDef def, PileType pileType, int count, PlayerState? creator, CardPilePosition position = CardPilePosition.Bottom)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(def);
        var player = target as PlayerState ?? PetOwnerOf(cs, target)
            ?? throw new InvalidOperationException($"Creature {target.CombatId} is neither a player nor a pet, so it has no pile to receive {def.Id}.");
        if (player.IsDead)
        {
            return;
        }
        for (int i = 0; i < count; i++)
        {
            var card = cs.CreateCard(def, player);
            AddGeneratedCardToCombat(cs, card, pileType, creator, position);
        }
    }

    public static void AutoPlayFromDrawPile(CombatState cs, PlayerState player, int count, CardPilePosition position, bool forceExhaust)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return;
        }
        var cards = new List<CardInstance>(Math.Max(0, count));
        var drawPile = player.DrawPile;
        for (int i = 0; i < count; i++)
        {
            ShuffleIfNecessary(cs, player);
            var pileCards = drawPile.Cards;
            CardInstance? card = position switch
            {
                CardPilePosition.Bottom => pileCards.Count > 0 ? pileCards[pileCards.Count - 1] : null,
                CardPilePosition.Top => pileCards.Count > 0 ? pileCards[0] : null,
                CardPilePosition.Random => cs.Rng.CombatCardSelection.NextItem(pileCards),
                _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
            };
            if (card == null)
            {
                break;
            }
            cards.Add(card);
            Add(cs, card, PileType.Play);
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (!OwnerOf(cs, card).IsDead)
            {
                card.ExhaustOnNextPlay = forceExhaust;
                CardCmd.AutoPlay(cs, card, null);
            }
        }
    }

    public static CardInstance? AddCurseToDeck(RunState rs, string curseId, PlayerState owner) => AddCurseToDeck(rs, CardRegistry.Get(curseId), owner);

    public static CardInstance? AddCurseToDeck(RunState rs, CardDef curse, PlayerState owner)
    {
        var results = AddCursesToDeck(rs, new[] { curse }, owner);
        return results.Count > 0 ? results[0].CardAdded : null;
    }

    public static IReadOnlyList<CardPileAddResult> AddCursesToDeck(RunState rs, IReadOnlyList<CardDef> curses, PlayerState owner)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(curses);
        ArgumentNullException.ThrowIfNull(owner);
        var results = new List<CardPileAddResult>(curses.Count);
        for (int i = 0; i < curses.Count; i++)
        {
            var curse = curses[i];
            if (curse.Type != CardType.Curse)
            {
                throw new ArgumentException(curse.Id + " is not a curse");
            }
            var card = CreateRunCard(rs, curse, owner);
            results.Add(Add(rs, card, PileType.Deck));
        }
        return results;
    }

    public static void Discard(CombatState cs, CardInstance card) => CardCmd.Discard(cs, card);

    public static void Exhaust(CombatState cs, CardInstance card, bool causedByEthereal = false) => CardCmd.Exhaust(cs, card, causedByEthereal);

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

    public static PlayerState OwnerOf(CombatState cs, CardInstance card) => card.HasOwner ? card.Owner : cs.Player;

    public static PlayerState OwnerOf(RunState rs, CardInstance card) => card.HasOwner ? card.Owner : rs.Players[0];

    public static PileType PileTypeOf(PlayerState owner, CardInstance card)
    {
        var pile = owner.FindPileContaining(card);
        if (pile != null)
        {
            return pile.Type;
        }
        var deck = owner.Deck;
        for (int i = 0; i < deck.Count; i++)
        {
            if (ReferenceEquals(deck[i], card))
            {
                return PileType.Deck;
            }
        }
        return PileType.None;
    }

    public static bool IsInCombatPile(CombatState cs, CardInstance card) => OwnerOf(cs, card).FindPileContaining(card) != null;

    public static bool HasKeywordInCombat(CombatState cs, CardInstance card, CardKeyword keyword)
    {
        HashSet<CardKeyword>? scratch = null;
        return HasKeywordInCombat(cs, card, keyword, ref scratch);
    }

    public static bool HasKeywordInCombat(CombatState cs, CardInstance card, CardKeyword keyword, ref HashSet<CardKeyword>? scratch)
    {
        if (!IsInCombatPile(cs, card))
        {
            return card.HasKeyword(keyword);
        }
        var keywords = scratch ??= new HashSet<CardKeyword>();
        keywords.Clear();
        uint mask = (uint)card.KeywordMask;
        for (int k = 0; mask != 0; k++, mask >>= 1)
        {
            if ((mask & 1u) != 0)
            {
                keywords.Add((CardKeyword)k);
            }
        }
        Hook.ModifyKeywordsInCombat(cs, card, keywords);
        return keywords.Contains(keyword);
    }

    public static bool IsSlyThisTurn(CombatState cs, CardInstance card)
        => HasKeywordInCombat(cs, card, CardKeyword.Sly) || card.HasSingleTurnSly;

    public static bool ShouldRetainThisTurn(CombatState cs, CardInstance card, ref HashSet<CardKeyword>? scratch)
        => HasKeywordInCombat(cs, card, CardKeyword.Retain, ref scratch) || card.HasSingleTurnRetain;

    internal const string GeneratedCardOutsideCombatSite = "CardPileCmd.AddGeneratedCardToCombat:empty-result-outside-running-combat";

    private static int DrawInternal(CombatState cs, decimal count, PlayerState player, bool fromHandDraw, List<CardInstance>? result, ref CardInstance? first)
    {
        if (CombatStatus.IsOverOrEnding(cs))
        {
            return 0;
        }
        if (!Hook.ShouldDraw(cs, player, fromHandDraw, out var modifier))
        {
            Hook.AfterPreventingDraw(cs, modifier!);
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
            if (room <= 0)
            {
                break;
            }
            if (CombatStatus.IsOverOrEnding(cs))
            {
                break;
            }
            if (!IsDrawPossible(player))
            {
                break;
            }
            ShuffleIfNecessary(cs, player);
            if (!IsDrawPossible(player))
            {
                break;
            }
            var card = drawPile.Count > 0 ? drawPile[0] : null;
            if (card == null || hand.Count >= MaxCardsInHand)
            {
                break;
            }
            first ??= card;
            result?.Add(card);
            drawn++;
            Add(cs, card, hand);
            cs.History.AppendCardDrawn(cs, card, fromHandDraw);
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

    private static CardPileAddResult[] AddCore(RunState rs, CombatState? cs, IReadOnlyList<CardInstance> cards, PileType newPileType, CardPile? newPile, CardPilePosition position, AbstractModel? clonedBy, bool isChangingOwners)
    {
        int n = cards.Count;
        if (n == 0)
        {
            return Array.Empty<CardPileAddResult>();
        }
        bool toCombatPile = CardPile.IsCombatPileType(newPileType);
        if (!toCombatPile && newPileType != PileType.Deck)
        {
            throw new ArgumentOutOfRangeException(nameof(newPileType), newPileType, "Cards can only be added to a combat pile or the deck.");
        }
        if (toCombatPile && cs is null)
        {
            throw new InvalidOperationException($"Adding a card to the {newPileType} pile needs a combat.");
        }
        var results = new CardPileAddResult[n];
        if (toCombatPile && CombatStatus.IsEnding(cs!))
        {
            for (int i = 0; i < n; i++)
            {
                results[i] = new CardPileAddResult(false, cards[i], null, PileType.None, null);
            }
            return results;
        }
        PlayerState? player = null;
        for (int i = 0; i < n; i++)
        {
            var card = cards[i];
            ArgumentNullException.ThrowIfNull(card);
            var owner = OwnerOf(rs, cs, card);
            var oldPileType = PileTypeOf(owner, card);
            PileType? oldPile = oldPileType == PileType.None ? null : oldPileType;
            if (card.HasBeenRemovedFromState || owner.IsDead)
            {
                results[i] = new CardPileAddResult(false, card, oldPile, newPileType, null);
                continue;
            }
            results[i] = new CardPileAddResult(true, card, oldPile, newPileType, null);
            if (player == null)
            {
                player = owner;
            }
            else if (!ReferenceEquals(player, owner))
            {
                throw new InvalidOperationException("Tried to add cards with different owners to the same pile!");
            }
        }
        if (newPileType == PileType.Deck)
        {
            for (int i = 0; i < n; i++)
            {
                var card = results[i].CardAdded;
                if (Hook.ShouldAddToDeck(rs, card, out var preventer))
                {
                    card.FloorAddedToDeck = rs.TotalFloor;
                }
                else
                {
                    preventer!.AfterAddToDeckPrevented(rs, card);
                    results[i] = results[i] with { Success = false };
                }
            }
        }
        if (toCombatPile && !cs!.IsInProgress)
        {
            return results;
        }
        if (!AnySuccess(results))
        {
            return results;
        }
        for (int i = 0; i < n; i++)
        {
            var result = results[i];
            if (!result.Success)
            {
                continue;
            }
            var card = result.CardAdded;
            var owner = OwnerOf(rs, cs, card);
            var targetType = newPileType;
            var target = toCombatPile ? newPile ?? owner.GetPile(newPileType) : null;
            bool isFullHandAdd = targetType == PileType.Hand && target!.Count >= MaxCardsInHand;
            if (isFullHandAdd)
            {
                targetType = PileType.Discard;
                target = owner.DiscardPile;
            }
            var added = card;
            if (result.OldPile is { } oldPile)
            {
                RemoveFromCurrentPile(owner, card, oldPile);
            }
            else if (targetType == PileType.Deck)
            {
                List<AbstractModel>? modifying = null;
                added = Hook.ModifyCardBeingAddedToDeck(rs, card, ref modifying);
                if (modifying != null && modifying.Count > 0)
                {
                    results[i] = result with { CardAdded = added, ModifyingModels = modifying };
                }
            }
            if (targetType == PileType.Deck)
            {
                var deck = owner.Deck;
                InsertAt(deck, added, PositionIndex(rs, position, deck.Count));
            }
            else
            {
                target!.AddInternal(added, PositionIndex(rs, position, target.Count));
            }
            if (result.OldPile == null && CardPile.IsCombatPileType(targetType) && !isChangingOwners)
            {
                Hook.AfterCardEnteredCombat(cs!, card);
            }
        }
        for (int i = 0; i < n; i++)
        {
            var result = results[i];
            if (!result.Success)
            {
                continue;
            }
            var cardAdded = result.CardAdded;
            var now = PileTypeOf(OwnerOf(rs, cs, cardAdded), cardAdded);
            if (result.OldPile == null || result.OldPile != now)
            {
                Hook.AfterCardChangedPiles(rs, CardPile.IsCombatPileType(now) ? cs : null, cardAdded, result.OldPile ?? PileType.None, clonedBy);
            }
        }
        return results;
    }

    private static int PositionIndex(RunState rs, CardPilePosition position, int count) => position switch
    {
        CardPilePosition.Bottom => -1,
        CardPilePosition.Top => 0,
        CardPilePosition.Random => rs.Rng.Shuffle.NextInt(count + 1),
        _ => throw new ArgumentOutOfRangeException(nameof(position), position, null)
    };

    private static void InsertAt(List<CardInstance> list, CardInstance card, int index)
    {
        if (index >= 0)
        {
            list.Insert(index, card);
        }
        else
        {
            list.Add(card);
        }
    }

    private static bool AnySuccess(CardPileAddResult[] results)
    {
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].Success)
            {
                return true;
            }
        }
        return false;
    }

    private static PlayerState OwnerOf(RunState rs, CombatState? cs, CardInstance card)
        => card.HasOwner ? card.Owner : cs?.Player ?? rs.Players[0];

    private static void RemoveFromCurrentPile(PlayerState owner, CardInstance card, PileType pile)
    {
        switch (pile)
        {
            case PileType.None:
                return;
            case PileType.Deck:
                owner.Deck.Remove(card);
                return;
            default:
                owner.GetPile(pile).RemoveInternal(card);
                return;
        }
    }

    private static PlayerState? PetOwnerOf(CombatState cs, CreatureState creature)
    {
        if (creature is not MonsterState monster || monster.PetOwnerId == PowerModel.NoCreature)
        {
            return null;
        }
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.CombatId == monster.PetOwnerId)
            {
                return player;
            }
        }
        return null;
    }

    private static CardInstance CreateRunCard(RunState rs, CardDef def, PlayerState owner)
    {
        var card = new CardInstance(def, rs.AllocateCardInstanceId());
        card.AttachTo(owner);
        def.AfterCreated(card);
        return card;
    }
}
