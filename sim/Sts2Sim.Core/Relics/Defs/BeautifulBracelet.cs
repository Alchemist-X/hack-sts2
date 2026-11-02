namespace Sts2Sim.Core.Relics;

public sealed class BeautifulBracelet : RelicModel
{
    public const string RelicId = "BEAUTIFUL_BRACELET";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public const string SwiftKey = "Swift";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(4),
        new DynamicVar(SwiftKey, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        var swift = new Swift();
        var chosen = Owner.Deck.Where(c => swift.CanEnchant(c)).TakeRandom(DynamicVars.Cards.IntValue, rs.Rng.Niche).ToList();
        foreach (var current in chosen)
        {
            CardCmd.Enchant<Swift>(current, DynamicVars[SwiftKey].IntValue);
        }
    }
}
