namespace Sts2Sim.Core.Relics;

public sealed class RoyalStamp : RelicModel
{
    public const string RelicId = "ROYAL_STAMP";

    public const string EnchantmentKey = "Enchantment";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1),
        new StringVar(EnchantmentKey, "ROYALLY_APPROVED")
    };

    public override void AfterObtained(RunState rs)
    {
        var royalStamp = new RoyallyApproved();
        var list = Owner.Deck.Where(c => royalStamp.CanEnchant(c)).ToList();
        var shuffled = list.UnstableShuffle(rs.Rng.Niche).ToList();
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1);
        var result = RelicDeckPromptsR91.FromDeckForEnchantment(rs, shuffled, royalStamp, prefs, RelicId);
        var cardModel = result.FirstOrDefault();
        if (cardModel != null)
        {
            CardCmd.Enchant<RoyallyApproved>(cardModel, 1m);
        }
    }
}
