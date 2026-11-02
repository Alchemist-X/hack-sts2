namespace Sts2Sim.Core.Relics;

public sealed class TheCourier : RelicModel
{
    public const string RelicId = "THE_COURIER";

    private const string DiscountKey = "Discount";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowedInShops => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DiscountKey, 20m)
    };

    public override decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return cost;
        }
        return cost * (1m - DynamicVars[DiscountKey].BaseValue / 100m);
    }

    public override bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player) => ReferenceEquals(player, Owner);
}
