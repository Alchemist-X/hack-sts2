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
        var transformations = new List<CardTransformation>(originals.Count);
        foreach (var c in originals)
        {
            transformations.Add(new CardTransformation(null, c, CardFactory.CreateRandomCardForTransform(rs, c, false, rs.Rng.Niche)));
        }
        CardCmd.Transform(rs, transformations, null);
    }
}
