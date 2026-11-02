namespace Sts2Sim.Core.Relics;

public sealed class NutritiousSoup : RelicModel
{
    public const string RelicId = "NUTRITIOUS_SOUP";

    public const string TezcatarasEmberId = "TEZCATARAS_EMBER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterObtained(RunState rs)
    {
        var canonical = EnchantmentRegistry.Create(TezcatarasEmberId);
        foreach (var current in Owner.Deck.ToList())
        {
            if (current.Rarity == CardRarity.Basic && current.HasTag(CardTag.Strike) && canonical.CanEnchant(current))
            {
                CardCmd.Enchant(TezcatarasEmberId, current, 1m);
            }
        }
    }
}
