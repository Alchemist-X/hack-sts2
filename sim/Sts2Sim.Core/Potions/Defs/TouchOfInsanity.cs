namespace Sts2Sim.Core.Potions;

public sealed class TouchOfInsanity : PotionModel
{
    public const string PotionId = "TOUCH_OF_INSANITY";

    public const string SiteMethod = "OnUse";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var prefs = new CardSelectorPrefs(PotionId + ".selectionScreenPrompt", 1);
        var selected = CardSelectCmd.FromHand(cs, player, prefs, c => c.CostsEnergyOrStars(cs, includeGlobalModifiers: false) || c.CostsEnergyOrStars(cs, includeGlobalModifiers: true), ChoiceSource.For(PotionId, ChoiceSource.NoInstance, SiteMethod));
        selected.FirstOrDefault()?.SetToFreeThisCombat();
    }
}
