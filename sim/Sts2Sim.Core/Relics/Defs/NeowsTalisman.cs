namespace Sts2Sim.Core.Relics;

public sealed class NeowsTalisman : RelicModel
{
    public const string RelicId = "NEOWS_TALISMAN";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var basics = Owner.Deck.Where(c => c.Rarity == CardRarity.Basic).ToList();
        var targets = new[]
        {
            basics.LastOrDefault(c => c.HasTag(CardTag.Strike)),
            basics.LastOrDefault(c => c.HasTag(CardTag.Defend))
        };
        foreach (var current in targets)
        {
            if (current != null)
            {
                CardCmd.Upgrade(rs.Combat, current);
            }
        }
    }
}
