namespace Sts2Sim.Core.Relics;

public sealed class PandorasBox : RelicModel
{
    public const string RelicId = "PANDORAS_BOX";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var originals = Owner.Deck.Where(c => c != null && c.IsBasicStrikeOrDefend && c.IsRemovable).ToList();
        var transformations = new List<(CardInstance Original, CardInstance Replacement)>(originals.Count);
        foreach (var c in originals)
        {
            var options = CardFactory.GetDefaultTransformationOptions(rs, c, false);
            var pick = rs.Rng.Niche.NextItem(options)!;
            transformations.Add((c, CardFactory.CreateRunCard(rs, pick, Owner)));
        }
        CardCmd.TransformInDeck(rs, transformations);
    }
}
