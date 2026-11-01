namespace Sts2Sim.Core.Relics;

public sealed class ElectricShrymp : RelicModel
{
    public const string RelicId = "ELECTRIC_SHRYMP";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1);
        var canonicalMomentum = new Imbued();
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, canonicalMomentum, prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant(new Imbued(), current, 1m);
        }
    }
}
