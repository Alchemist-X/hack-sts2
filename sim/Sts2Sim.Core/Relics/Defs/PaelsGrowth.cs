namespace Sts2Sim.Core.Relics;

public sealed class PaelsGrowth : RelicModel
{
    public const string RelicId = "PAELS_GROWTH";

    public const string EnchantmentNameKey = "EnchantmentName";

    public const int CloneAmount = 4;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new StringVar(EnchantmentNameKey, "CLONE")
    };

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt, 1);
        var enumerable = RelicDeckPromptsR91.FromDeckForEnchantment(rs, Owner, new Clone(), prefs, RelicId);
        foreach (var current in enumerable)
        {
            CardCmd.Enchant<Clone>(current, CloneAmount);
        }
    }

    public override bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        options.Add(new CloneRestSiteOption(player));
        return true;
    }
}
