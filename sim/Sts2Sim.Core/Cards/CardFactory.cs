namespace Sts2Sim.Core.Cards;

public static partial class CardFactory
{
    public static IEnumerable<CardInstance> GetDistinctForCombat(CombatState cs, PlayerState player, IEnumerable<CardDef> cards, int count, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rng);
        cards = FilterForPlayerCount(cs.Run, cards);
        var options = PortedOptions(cs, FilterForCombat(cards), "CardFactory.GetDistinctForCombat");
        return CreateForCombat(cs, player, options.TakeRandom(count, rng));
    }

    public static IReadOnlyList<CardInstance> GetForCombat(CombatState cs, PlayerState player, IEnumerable<CardDef> cards, int count, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(cs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(cards);
        ArgumentNullException.ThrowIfNull(rng);
        var options = FilterForCombat(cards).ToList();
        options = FilterForPlayerCount(cs.Run, options).ToList();
        options = PortedOptions(cs, options, "CardFactory.GetForCombat");
        var created = new List<CardInstance>(Math.Max(0, count));
        for (int i = 0; i < count; i++)
        {
            var canonicalCard = rng.NextItem((IReadOnlyList<CardDef>)options)
                ?? throw new InvalidOperationException("CardFactory.GetForCombat has no card to create (Factories/CardFactory.cs:111-112 passes the null pick to CreateCard).");
            created.Add(cs.CreateCard(canonicalCard, player));
        }
        return created;
    }

    public static IEnumerable<CardDef> FilterForCombat(IEnumerable<CardDef> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        return cards.Where(c => c.CanBeGeneratedInCombat && c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient && c.Rarity != CardRarity.Event).Distinct();
    }

    public static IEnumerable<CardDef> FilterForPlayerCount(RunState rs, IEnumerable<CardDef> options)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(options);
        if (rs.Players.Count > 1)
        {
            return options.Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.SingleplayerOnly);
        }
        return options.Where(c => c.MultiplayerConstraint != CardMultiplayerConstraint.MultiplayerOnly);
    }

    public static IReadOnlyList<CardDef> GetDefaultTransformationOptions(RunState rs, CardInstance original, bool isInCombat)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        var cardPool = original.Type != CardType.Quest && original.Rarity != CardRarity.Event && original.Rarity != CardRarity.Ancient && original.Rarity != CardRarity.Token
            ? original.Def.Pool
            : CardPools.Colorless;
        var unlockedCards = cardPool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        return GetFilteredTransformationOptions(rs, original, unlockedCards, isInCombat);
    }

    public static CardInstance CreateRandomCardForTransform(CombatState? cs, CardInstance original, bool isInCombat, Rng rng)
    {
        var scope = RequireCombatScope(cs, original);
        var options = GetDefaultTransformationOptions(scope.Run, original, isInCombat);
        return CreateTransformReplacement(scope, original, options, rng);
    }

    public static CardInstance CreateRandomCardForTransform(CombatState? cs, CardInstance original, IEnumerable<CardDef> options, bool isInCombat, Rng rng)
    {
        var scope = RequireCombatScope(cs, original);
        var filtered = GetFilteredTransformationOptions(scope.Run, original, options, isInCombat);
        return CreateTransformReplacement(scope, original, filtered, rng);
    }

    public static CardInstance CreateRandomCardForTransform(RunState rs, CardInstance original, bool isInCombat, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        if (rs.Combat is { } combat && original.IsInCombat(combat))
        {
            return CreateRandomCardForTransform(combat, original, isInCombat, rng);
        }
        var options = GetDefaultTransformationOptions(rs, original, isInCombat);
        return CreateRunTransformReplacement(rs, original, options, rng);
    }

    public static CardInstance CreateRandomCardForTransform(RunState rs, CardInstance original, IEnumerable<CardDef> options, bool isInCombat, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        if (rs.Combat is { } combat && original.IsInCombat(combat))
        {
            return CreateRandomCardForTransform(combat, original, options, isInCombat, rng);
        }
        var filtered = GetFilteredTransformationOptions(rs, original, options, isInCombat);
        return CreateRunTransformReplacement(rs, original, filtered, rng);
    }

    private static CardInstance CreateRunTransformReplacement(RunState rs, CardInstance original, IReadOnlyList<CardDef> options, Rng rng)
    {
        var ported = new List<CardDef>(options.Count);
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i] is not UnportedCardDef)
            {
                ported.Add(options[i]);
            }
        }
        if (ported.Count < options.Count)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, $"CardFactory.CreateRandomCardForTransform (run scope): {options.Count - ported.Count} of {options.Count} options are not ported; the draw runs over the ported ones");
        }
        if (ported.Count == 0)
        {
            throw new NotImplementedException($"CardFactory.CreateRandomCardForTransform: none of the {options.Count} options for {original.Id} is ported");
        }
        var pick = rng.NextItem((IReadOnlyList<CardDef>)ported)!;
        return CreateRunCard(rs, pick, original.Owner);
    }

    public static IReadOnlyList<CardDef> GetFilteredTransformationOptions(RunState rs, CardInstance original, IEnumerable<CardDef> originalOptions, bool isInCombat)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(originalOptions);
        var options = originalOptions;
        var rarity = original.Rarity;
        if ((uint)(rarity - CardRarity.Status) > 1u)
        {
            options = options.Where(c => (uint)(c.Rarity - CardRarity.Common) <= 2u);
        }
        if (isInCombat)
        {
            options = options.Where(c => c.CanBeGeneratedInCombat);
        }
        string originalId = original.Id;
        options = options.Where(c => !string.Equals(c.Id, originalId, StringComparison.Ordinal)).ToList();
        var array = FilterForPlayerCount(rs, options).ToArray();
        if (array.Length == 0)
        {
            throw new InvalidOperationException("All transformation options provided are invalid! Original options: " + string.Join(",", originalOptions.Select(c => c.Id)));
        }
        return array;
    }

    private static IEnumerable<CardInstance> CreateForCombat(CombatState cs, PlayerState player, IEnumerable<CardDef> defs)
    {
        foreach (var def in defs)
        {
            yield return cs.CreateCard(def, player);
        }
    }

    private static CardInstance CreateTransformReplacement(CombatState cs, CardInstance original, IReadOnlyList<CardDef> options, Rng rng)
    {
        var ported = PortedOptions(cs, options, "CardFactory.CreateRandomCardForTransform");
        if (ported.Count == 0)
        {
            throw new NotImplementedException($"CardFactory.CreateRandomCardForTransform: none of the {options.Count} options for {original.Id} is ported");
        }
        var pick = rng.NextItem((IReadOnlyList<CardDef>)ported)!;
        return cs.CreateCard(pick, original.Owner);
    }

    private static CombatState RequireCombatScope(CombatState? cs, CardInstance original)
    {
        ArgumentNullException.ThrowIfNull(original);
        if (cs is null)
        {
            throw new NotImplementedException("CardFactory.CreateRandomCardForTransform outside a combat: the card scope is the run (RunState.CreateCard, W2; Models/CardModel.cs:2375)");
        }
        return cs;
    }

    private static List<CardDef> PortedOptions(CombatState cs, IEnumerable<CardDef> options, string site)
    {
        var list = options.ToList();
        int unported = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is UnportedCardDef)
            {
                unported++;
            }
        }
        if (unported > 0)
        {
            cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, $"{site}: {unported} of {list.Count} options are not ported; the draw runs over the ported ones");
            list.RemoveAll(c => c is UnportedCardDef);
        }
        return list;
    }
}
