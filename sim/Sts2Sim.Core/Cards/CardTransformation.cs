namespace Sts2Sim.Core.Cards;

public readonly struct CardTransformation
{
    public CardTransformation(CombatState? cs, CardInstance original)
    {
        AssertTransformable(cs, original);
        Original = original;
        ReplacementOptions = null;
        Replacement = null;
        IsInCombat = original.IsInCombat(cs);
    }

    public CardTransformation(CombatState? cs, CardInstance original, IEnumerable<CardDef> options)
    {
        AssertTransformable(cs, original);
        Original = original;
        ReplacementOptions = options;
        Replacement = null;
        IsInCombat = original.IsInCombat(cs);
    }

    public CardTransformation(CombatState? cs, CardInstance original, CardInstance replacement)
    {
        AssertTransformable(cs, original);
        Original = original;
        Replacement = replacement;
        ReplacementOptions = null;
        IsInCombat = original.IsInCombat(cs);
    }

    public CardInstance Original { get; }

    public CardInstance? Replacement { get; }

    public IEnumerable<CardDef>? ReplacementOptions { get; }

    public bool IsInCombat { get; }

    public CardInstance? GetReplacement(CombatState? cs, Rng? rng)
    {
        if (Replacement != null)
        {
            return Replacement;
        }
        if (rng == null)
        {
            throw new ArgumentException("RNG must be passed when replacement options is set!");
        }
        if (!Original.IsTransformable(cs))
        {
            return null;
        }
        if (ReplacementOptions == null)
        {
            return CardFactory.CreateRandomCardForTransform(cs, Original, IsInCombat, rng);
        }
        return CardFactory.CreateRandomCardForTransform(cs, Original, ReplacementOptions, IsInCombat, rng);
    }

    private static void AssertTransformable(CombatState? cs, CardInstance card)
    {
        ArgumentNullException.ThrowIfNull(card);
        if (!card.IsTransformable(cs))
        {
            throw new InvalidOperationException("Non-removable cards cannot be transformed!");
        }
    }
}
