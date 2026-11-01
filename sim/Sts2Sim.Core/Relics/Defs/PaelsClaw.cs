namespace Sts2Sim.Core.Relics;

public sealed class PaelsClaw : RelicModel
{
    public const string RelicId = "PAELS_CLAW";

    public const string GoopyId = "GOOPY";

    public const int CardsCount = 3;

    public const int EnchantmentAmount = 2;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new StringVar("EnchantmentName", GoopyId)
    };

    public override void AfterObtained(RunState rs)
    {
        var canonical = EnchantmentRegistry.Create(GoopyId);
        foreach (var current in Owner.Deck.ToList())
        {
            if (canonical.CanEnchant(current))
            {
                CardCmd.Enchant(GoopyId, current, 1m);
            }
        }
    }
}
