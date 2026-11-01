namespace Sts2Sim.Core.Relics;

public sealed class WarPaint : RelicModel
{
    public const string RelicId = "WAR_PAINT";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(2)
    };

    public override void AfterObtained(RunState rs)
    {
        var candidates = Owner.Deck.Where(c => c != null && c.Type == CardType.Skill && c.IsUpgradable).ToList();
        var chosen = candidates.StableShuffle(rs.Rng.Niche, CardPileCmd.CompareForShuffle).Take(DynamicVars.Cards.IntValue);
        foreach (var current in chosen)
        {
            CardCmd.Upgrade(rs.Combat, current);
        }
    }
}
