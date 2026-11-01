namespace Sts2Sim.Core.Relics;

public sealed class PunchDagger : RelicModel
{
    public const string RelicId = "PUNCH_DAGGER";

    private const string _momentumKey = "Momentum";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Shop;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(_momentumKey, 5m)
    };

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1);
        var canonicalMomentum = new Momentum();
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, canonicalMomentum, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant(new Momentum(), current, DynamicVars[_momentumKey].IntValue);
        }
    }
}
