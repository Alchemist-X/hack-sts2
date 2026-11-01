namespace Sts2Sim.Core.Relics;

public sealed class MembershipCard : RelicModel
{
    public const string RelicId = "MEMBERSHIP_CARD";

    public const string DiscountKey = "Discount";

    public override string Id => RelicId;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DiscountKey, 50m)
    };

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return cost;
        }
        return cost * (DynamicVars[DiscountKey].BaseValue / 100m);
    }
}
