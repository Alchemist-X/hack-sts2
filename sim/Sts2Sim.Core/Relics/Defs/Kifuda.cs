namespace Sts2Sim.Core.Relics;

public sealed class Kifuda : RelicModel
{
    public const string RelicId = "KIFUDA";

    private const int _adroitAmount = 3;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new CardsVar(3)
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 0, DynamicVars.Cards.IntValue)
        {
            Cancelable = false,
            RequireManualConfirmation = true
        };
        var canonicalEnchantment = new Adroit();
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, canonicalEnchantment, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant(new Adroit(), current, _adroitAmount);
        }
    }
}
