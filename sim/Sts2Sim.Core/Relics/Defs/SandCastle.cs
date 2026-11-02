namespace Sts2Sim.Core.Relics;

public sealed class SandCastle : RelicModel
{
    public const string RelicId = "SAND_CASTLE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(6)
    };

    public override void AfterObtained(RunState rs)
    {
        var candidates = Owner.Deck.Where(c => c?.IsUpgradable ?? false).ToList();
        var chosen = candidates.StableShuffle(rs.Rng.Niche, CardPileCmd.CompareForShuffle).Take(DynamicVars.Cards.IntValue);
        foreach (var current in chosen)
        {
            CardCmd.Upgrade(rs.Combat, current);
        }
    }
}
