namespace Sts2Sim.Core.Combat.Pipeline;

public static partial class CardCmd
{
    public const string DeckTransformUnportedSite = "CardCmd.TransformToRandom (deck): unported transformation options dropped; the draw runs over the ported ones";

    public static CardPileAddResult TransformToRandom(RunState rs, CardInstance original, Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(rng);
        if (rs.Combat != null && !original.IsInDeck)
        {
            return TransformToRandom(rs.Combat, original, rng);
        }
        if (!original.IsTransformable(null))
        {
            throw new InvalidOperationException($"Attempting to transform un-transformable card {original.Id}!");
        }
        var options = CardFactory.GetDefaultTransformationOptions(rs, original, false);
        var ported = options.Where(c => c is not UnportedCardDef).ToList();
        if (ported.Count < options.Count)
        {
            rs.Fidelity.Record(FidelityFlag.ApproximatedRule, DeckTransformUnportedSite);
        }
        if (ported.Count == 0)
        {
            throw new NotImplementedException($"CardCmd.TransformToRandom (deck): none of the {options.Count} options for {original.Id} is ported");
        }
        var pick = rng.NextItem((IReadOnlyList<CardDef>)ported)!;
        var replacement = CardFactory.CreateRunCard(rs, pick, original.Owner);
        return TransformInDeck(rs, original, replacement);
    }

    public static CardPileAddResult TransformTo(RunState rs, CardInstance original, string replacementId)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        var replacement = CardFactory.CreateRunCard(rs, CardRegistry.Get(replacementId), original.Owner);
        return TransformInDeck(rs, original, replacement);
    }

    public static CardPileAddResult TransformInDeck(RunState rs, CardInstance original, CardInstance replacement)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(replacement);
        if (!original.IsTransformable(null))
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it's un-transformable.");
        }
        if (!original.IsInDeck)
        {
            throw new InvalidOperationException("Can't transform " + original.Id + " because it has no pile.");
        }
        return TransformSingle(rs, original, replacement);
    }
}
