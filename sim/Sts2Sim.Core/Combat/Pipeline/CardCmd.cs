namespace Sts2Sim.Core.Combat.Pipeline;

public static partial class CardCmd
{
    public static void Downgrade(CombatState? cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (cs != null && CombatStatus.IsEnding(cs))
        {
            return;
        }
        if (card.IsInDeck)
        {
            OnDeckCardDowngraded(cs, card);
        }
        card.DowngradeInternal();
    }

    public static void Upgrade(CombatState? cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (cs != null && CombatStatus.IsEnding(cs))
        {
            return;
        }
        UpgradeOne(cs, card);
    }

    public static void Upgrade(CombatState? cs, IEnumerable<CardInstance> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cs != null && CombatStatus.IsEnding(cs))
        {
            return;
        }
        foreach (var current in cards)
        {
            UpgradeOne(cs, current);
        }
    }

    public static CardPileAddResult TransformToRandom(CombatState cs, CardInstance original, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        return Transform(cs, new[] { new CardTransformation(cs, original) }, rng).First();
    }

    public static CardPileAddResult? TransformTo(CombatState cs, CardInstance original, string replacementId)
        => TransformTo(cs, original, CardRegistry.Get(replacementId));

    public static CardPileAddResult? TransformTo(CombatState cs, CardInstance original, CardDef replacementDef)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacementDef);
        var replacement = cs.CreateCard(replacementDef, original.Owner);
        return Transform(cs, original, replacement);
    }

    public static CardPileAddResult TransformDeckCardTo(RunState rs, CardInstance original, string replacementId)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        if (!original.IsTransformable(null))
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable.");
        }
        if (!original.IsInDeck)
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it has no pile.");
        }
        var owner = original.Owner;
        var replacement = new CardInstance(CardRegistry.Get(replacementId), rs.AllocateCardInstanceId());
        replacement.AttachTo(owner);
        replacement.Def.AfterCreated(replacement);
        original.RemoveFromCurrentPile(null);
        replacement = Hook.ModifyCardBeingAddedToDeck(rs, replacement);
        owner.Deck.Add(replacement);
        Hook.AfterCardChangedPiles(rs, null, replacement, PileType.Deck, null);
        original.Def.AfterTransformedFrom(null, original);
        replacement.Def.AfterTransformedTo(null, replacement);
        original.RemoveFromState(null);
        return new CardPileAddResult(true, replacement, null, PileType.None, null);
    }

    public static CardPileAddResult? Transform(CombatState cs, CardInstance original, CardInstance replacement)
    {
        var results = Transform(cs, new[] { new CardTransformation(cs, original, replacement) }, null);
        return results.Count > 0 ? results[0] : null;
    }

    public static IReadOnlyList<CardPileAddResult> Transform(CombatState cs, IEnumerable<CardTransformation> transformations, Rng? rng)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(transformations);
        if (CombatStatus.IsEnding(cs))
        {
            return Array.Empty<CardPileAddResult>();
        }
        var transformationsArr = transformations.ToArray();
        if (transformationsArr.Length == 0)
        {
            return Array.Empty<CardPileAddResult>();
        }
        var withOriginalData = new List<(CardTransformation Transformation, CardPile Pile, int Index, CardInstance Replacement)>(transformationsArr.Length);
        for (int i = 0; i < transformationsArr.Length; i++)
        {
            var transformation = transformationsArr[i];
            var original = transformation.Original;
            if (!original.IsTransformable(cs))
            {
                throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable.");
            }
            var pile = original.GetCombatPile(cs);
            if (pile == null)
            {
                if (original.IsInDeck)
                {
                    throw new NotImplementedException("CardCmd.Transform of a deck card: run layer (W2: ModifyCardBeingAddedToDeck, FloorAddedToDeck, map history; Commands/CardCmd.cs:2052-2065)");
                }
                throw new InvalidOperationException("Can't transform " + original.Id + " because it has no pile.");
            }
            int index = pile.IndexOf(original);
            var replacement = transformation.GetReplacement(cs, rng)
                ?? throw new InvalidOperationException($"Attempting to transform un-transformable card {original}!");
            original.RemoveFromCurrentPile(cs);
            withOriginalData.Add((transformation, pile, index, replacement));
        }
        withOriginalData.Sort(static (a, b) => a.Pile.Type != b.Pile.Type ? a.Pile.Type.CompareTo(b.Pile.Type) : a.Index.CompareTo(b.Index));
        var results = new List<CardPileAddResult>(withOriginalData.Count);
        for (int j = 0; j < withOriginalData.Count; j++)
        {
            var (transformation, pile, index, replacement) = withOriginalData[j];
            var original = transformation.Original;
            var result = new CardPileAddResult(true, replacement, null, PileType.None, null);
            var replacementOwner = replacement.HasOwner ? replacement.Owner : null;
            var originalOwner = original.HasOwner ? original.Owner : null;
            if (!ReferenceEquals(replacementOwner, originalOwner))
            {
                throw new InvalidOperationException($"Attempting to transform card {original} to {replacement}, but the replacement has a different owner!");
            }
            pile.AddInternal(replacement, index);
            cs.History.AppendCardGenerated(cs, replacement, replacementOwner);
            Hook.AfterCardEnteredCombat(cs, replacement);
            Hook.AfterCardChangedPiles(cs.Run, cs, replacement, pile.Type, null);
            original.Def.AfterTransformedFrom(cs, original);
            replacement.Def.AfterTransformedTo(cs, replacement);
            results.Add(result);
        }
        for (int j = 0; j < results.Count; j++)
        {
            var added = results[j];
            if (added.Success && added.CardAdded.IsInCombat(cs))
            {
                Hook.AfterCardGeneratedForCombat(cs, added.CardAdded, added.CardAdded.HasOwner ? added.CardAdded.Owner : null);
            }
            withOriginalData[j].Transformation.Original.RemoveFromState(cs);
        }
        return results;
    }

    public static EnchantmentModel? Enchant<T>(CardInstance card, decimal amount) where T : EnchantmentModel, new()
        => Enchant(new T(), card, amount);

    public static EnchantmentModel? Enchant(string enchantmentId, CardInstance card, decimal amount)
        => Enchant(EnchantmentRegistry.Create(enchantmentId), card, amount);

    public static EnchantmentModel? Enchant(EnchantmentModel enchantment, CardInstance card, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(enchantment);
        ArgumentNullException.ThrowIfNull(card);
        if (!enchantment.CanEnchant(card))
        {
            throw new InvalidOperationException($"Cannot enchant {card.Id} with {enchantment.Id}.");
        }
        var existing = card.Enchantment;
        if (existing == null)
        {
            card.EnchantInternal(enchantment, amount);
            enchantment.ModifyCard();
        }
        else
        {
            if (existing.GetType() != enchantment.GetType())
            {
                throw new InvalidOperationException($"Cannot enchant {card.Id} with {enchantment.Id} because it already has enchantment {existing.Id}.");
            }
            existing.Amount += (int)amount;
        }
        card.FinalizeUpgradeInternal();
        if (card.IsInDeck)
        {
            OnDeckCardEnchanted(card, enchantment.Id);
        }
        return card.Enchantment;
    }

    public static void ClearEnchantment(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.ClearEnchantmentInternal();
    }

    public static IReadOnlyList<AfflictionModel> AfflictAndPreview(CombatState cs, IEnumerable<CardInstance> cards, string afflictionId, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var afflictions = new List<AfflictionModel>();
        var cardList = new List<CardInstance>();
        foreach (var card in cards)
        {
            var affliction = Afflict(cs, afflictionId, card, amount);
            if (affliction != null)
            {
                afflictions.Add(affliction);
                cardList.Add(card);
            }
        }
        if (cardList.Count > 0)
        {
            var first = cardList[0].HasOwner ? cardList[0].Owner : null;
            for (int i = 0; i < cardList.Count; i++)
            {
                if (!ReferenceEquals(cardList[i].HasOwner ? cardList[i].Owner : null, first))
                {
                    throw new InvalidOperationException("All cards passed to AfflictAndPreview must have the same owner!");
                }
            }
        }
        return afflictions;
    }

    public static AfflictionModel? Afflict<T>(CombatState cs, CardInstance card, decimal amount) where T : AfflictionModel, new()
        => Afflict(cs, new T(), card, amount);

    public static AfflictionModel? Afflict(CombatState cs, string afflictionId, CardInstance card, decimal amount)
        => Afflict(cs, AfflictionRegistry.Create(afflictionId), card, amount);

    public static AfflictionModel? Afflict(CombatState cs, AfflictionModel affliction, CardInstance card, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(affliction);
        ArgumentNullException.ThrowIfNull(card);
        bool inCombatPile = card.IsInCombat(cs);
        if (CombatStatus.IsOverOrEnding(cs) && inCombatPile)
        {
            return null;
        }
        if (!inCombatPile && !OwnerIsInCombat(cs, card))
        {
            return null;
        }
        if (!Hook.ShouldAfflict(cs, card, affliction))
        {
            return null;
        }
        if (!affliction.CanAfflict(cs, card))
        {
            return null;
        }
        var existing = card.Affliction;
        if (existing == null)
        {
            card.AfflictInternal(affliction, amount);
            affliction.AfterApplied();
        }
        else
        {
            if (existing.GetType() != affliction.GetType())
            {
                throw new InvalidOperationException($"Cannot afflict {card.Id} with {affliction.Id} because it already has affliction {existing.Id}.");
            }
            existing.Amount += (int)amount;
        }
        cs.History.AppendCardAfflicted(cs, card, affliction);
        return card.Affliction;
    }

    public static void ClearAffliction(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.ClearAfflictionInternal();
    }

    public static void ApplyKeyword(CardInstance card, params CardKeyword[] keywords)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(keywords);
        foreach (var keyword in keywords)
        {
            card.AddKeyword(keyword);
        }
    }

    public static void RemoveKeyword(CardInstance card, params CardKeyword[] keywords)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(keywords);
        foreach (var keyword in keywords)
        {
            card.RemoveKeyword(keyword);
        }
    }

    public static void ApplySingleTurnSly(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.GiveSingleTurnSly();
    }

    public static void ApplySingleTurnRetain(CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        card.GiveSingleTurnRetain();
    }

    static partial void OnDeckCardUpgraded(CombatState? cs, CardInstance card);

    static partial void OnDeckCardDowngraded(CombatState? cs, CardInstance card);

    static partial void OnDeckCardEnchanted(CardInstance card, string enchantmentId);

    private static void UpgradeOne(CombatState? cs, CardInstance current)
    {
        if (!current.IsUpgradable)
        {
            return;
        }
        if (current.IsInDeck)
        {
            OnDeckCardUpgraded(cs, current);
        }
        current.UpgradeInternal();
        current.FinalizeUpgradeInternal();
    }

    private static bool OwnerIsInCombat(CombatState cs, CardInstance card)
    {
        if (!card.HasOwner)
        {
            return false;
        }
        var owner = card.Owner;
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (ReferenceEquals(allies[i], owner))
            {
                return true;
            }
        }
        return false;
    }
}
