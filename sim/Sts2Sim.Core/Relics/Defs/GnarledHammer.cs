namespace Sts2Sim.Core.Relics;

public sealed class GnarledHammer : RelicModel
{
    public const string RelicId = "GNARLED_HAMMER";

    private const string _sharpAmountKey = "SharpAmount";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(3),
        new DynamicVar(_sharpAmountKey, 3m)
    };

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 0, DynamicVars.Cards.IntValue)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };
        var canonicalEnchantment = new Sharp();
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, canonicalEnchantment, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant(new Sharp(), current, DynamicVars[_sharpAmountKey].IntValue);
        }
    }
}
