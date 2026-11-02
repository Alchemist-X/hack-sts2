namespace Sts2Sim.Core.Relics;

public sealed class WingCharm : RelicModel
{
    public const string RelicId = "WING_CHARM";

    private const string SwiftAmountKey = "SwiftAmount";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(SwiftAmountKey, 1m)
    };

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        var canonicalSwift = new Swift();
        var val = cardRewardOptions.Where(r => canonicalSwift.CanEnchant(r.Card)).ToList();
        if (val.Count == 0)
        {
            return false;
        }
        var cardCreationResult = rs.Rng.Niche.NextItem(val);
        if (cardCreationResult == null)
        {
            return false;
        }
        var card = MoltenEgg.CloneRunCard(rs, cardCreationResult.Card);
        CardCmd.Enchant<Swift>(card, DynamicVars[SwiftAmountKey].BaseValue);
        cardCreationResult.ModifyCard(card, this);
        return true;
    }
}
