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
        var replacement = CardFactory.CreateRunCard(rs, CardRegistry.Get(replacementId), original.Owner);
        return TransformSingle(rs, original, replacement);
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
        return TransformCore(cs.Run, transformations, rng);
    }

    public static IReadOnlyList<CardPileAddResult> Transform(RunState rs, IEnumerable<CardTransformation> transformations, Rng? rng)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(transformations);
        return TransformCore(rs, transformations, rng);
    }

    public static CardPileAddResult? Transform(RunState rs, CardInstance original, CardInstance replacement)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        var results = TransformCore(rs, new[] { new CardTransformation(original.IsInCombat(rs.Combat) ? rs.Combat : null, original, replacement) }, null);
        return results.Count > 0 ? results[0] : null;
    }

    public static IReadOnlyList<CardPileAddResult> TransformInDeck(RunState rs, IReadOnlyList<(CardInstance Original, CardInstance Replacement)> transformations)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(transformations);
        var arr = new CardTransformation[transformations.Count];
        for (int i = 0; i < arr.Length; i++)
        {
            var (original, replacement) = transformations[i];
            if (!original.IsTransformable(null) || !original.IsInDeck)
            {
                throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable or not in the deck.");
            }
            arr[i] = new CardTransformation(null, original, replacement);
        }
        return TransformCore(rs, arr, null);
    }

    private static CardPileAddResult TransformSingle(RunState rs, CardInstance original, CardInstance replacement)
    {
        var results = TransformCore(rs, new[] { new CardTransformation(original.IsInCombat(rs.Combat) ? rs.Combat : null, original, replacement) }, null);
        if (results.Count == 0)
        {
            throw new InvalidOperationException("CardCmd.Transform of " + original.Id + " produced no result: the combat is ending.");
        }
        return results[0];
    }

    private static IReadOnlyList<CardPileAddResult> TransformCore(RunState rs, IEnumerable<CardTransformation> transformations, Rng? rng)
    {
        var current = rs.Combat;
        if (current != null && CombatStatus.IsEnding(current))
        {
            return Array.Empty<CardPileAddResult>();
        }
        var transformationsArr = transformations.ToArray();
        if (transformationsArr.Length == 0)
        {
            return Array.Empty<CardPileAddResult>();
        }
        var combatState = current != null && transformationsArr[0].Original.IsInCombat(current) ? current : null;
        var withOriginalData = new List<(CardTransformation Transformation, CardPile? Pile, PileType PileType, int Index, CardInstance Replacement)>(transformationsArr.Length);
        for (int i = 0; i < transformationsArr.Length; i++)
        {
            var transformation = transformationsArr[i];
            var original = transformation.Original;
            var scope = current != null && original.IsInCombat(current) ? current : null;
            if (!original.IsTransformable(scope))
            {
                throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable.");
            }
            var pile = scope != null ? original.GetCombatPile(scope) : null;
            PileType pileType;
            int index;
            if (pile != null)
            {
                pileType = pile.Type;
                index = pile.IndexOf(original);
            }
            else if (original.IsInDeck)
            {
                pileType = PileType.Deck;
                index = original.Owner.Deck.IndexOf(original);
            }
            else
            {
                throw new InvalidOperationException("Can't transform " + original.Id + " because it has no pile.");
            }
            var replacement = GetTransformReplacement(rs, scope, transformation, rng)
                ?? throw new InvalidOperationException($"Attempting to transform un-transformable card {original}!");
            original.RemoveFromCurrentPile(scope);
            withOriginalData.Add((transformation, pile, pileType, index, replacement));
        }
        withOriginalData.Sort(static (a, b) => a.PileType != b.PileType ? a.PileType.CompareTo(b.PileType) : a.Index.CompareTo(b.Index));
        var results = new List<CardPileAddResult>(withOriginalData.Count);
        for (int j = 0; j < withOriginalData.Count; j++)
        {
            var (transformation, pile, pileType, index, replacement) = withOriginalData[j];
            var original = transformation.Original;
            var replacementOwner = replacement.HasOwner ? replacement.Owner : null;
            var originalOwner = original.HasOwner ? original.Owner : null;
            if (!ReferenceEquals(replacementOwner, originalOwner))
            {
                throw new InvalidOperationException($"Attempting to transform card {original} to {replacement}, but the replacement has a different owner!");
            }
            List<AbstractModel>? modifying = null;
            if (pileType == PileType.Deck)
            {
                replacement = Hook.ModifyCardBeingAddedToDeck(rs, replacement, ref modifying);
                replacement.FloorAddedToDeck = rs.TotalFloor;
                originalOwner!.Deck.Add(replacement);
            }
            else
            {
                var combat = current!;
                pile!.AddInternal(replacement, index);
                combat.History.AppendCardGenerated(combat, replacement, replacementOwner);
                Hook.AfterCardEnteredCombat(combat, replacement);
            }
            Hook.AfterCardChangedPiles(rs, combatState, replacement, pileType, null);
            original.Def.AfterTransformedFrom(combatState, original);
            replacement.Def.AfterTransformedTo(combatState, replacement);
            results.Add(new CardPileAddResult(true, replacement, null, PileType.None, modifying));
        }
        for (int j = 0; j < results.Count; j++)
        {
            var added = results[j];
            if (added.Success && current != null && added.CardAdded.IsInCombat(current))
            {
                Hook.AfterCardGeneratedForCombat(current, added.CardAdded, added.CardAdded.HasOwner ? added.CardAdded.Owner : null);
            }
            withOriginalData[j].Transformation.Original.RemoveFromState(combatState);
        }
        return results;
    }

    private static CardInstance? GetTransformReplacement(RunState rs, CombatState? scope, CardTransformation transformation, Rng? rng)
    {
        if (transformation.Replacement != null)
        {
            return transformation.Replacement;
        }
        if (scope != null)
        {
            return transformation.GetReplacement(scope, rng);
        }
        if (rng == null)
        {
            throw new ArgumentException("RNG must be passed when replacement options is set!");
        }
        if (!transformation.Original.IsTransformable(null))
        {
            return null;
        }
        return CreateRunTransformReplacement(rs, transformation.Original, transformation.ReplacementOptions, rng);
    }

    private static CardInstance CreateRunTransformReplacement(RunState rs, CardInstance original, IEnumerable<CardDef>? options, Rng rng)
    {
        var filtered = options == null ? CardFactory.GetDefaultTransformationOptions(rs, original, false) : CardFactory.GetFilteredTransformationOptions(rs, original, options, false);
        var ported = filtered.Where(c => c is not UnportedCardDef).ToList();
        if (ported.Count < filtered.Count)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, $"CardCmd.Transform (deck): {filtered.Count - ported.Count} of {filtered.Count} options are not ported; the draw runs over the ported ones");
        }
        var pick = rng.NextItem((IReadOnlyList<CardDef>)ported) ?? throw new NotImplementedException($"CardCmd.Transform (deck): none of the options for {original.Id} is ported");
        return CardFactory.CreateRunCard(rs, pick, original.Owner);
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
