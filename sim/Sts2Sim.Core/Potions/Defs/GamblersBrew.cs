namespace Sts2Sim.Core.Potions;

public sealed class GamblersBrew : PotionModel
{
    public const string PotionId = "GAMBLERS_BREW";

    public const string SiteMethod = "OnUse";

    public const int MaxSelect = 999999999;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var prefs = new CardSelectorPrefs(PotionId + ".selectionScreenPrompt", 0, MaxSelect);
        var selected = CardSelectCmd.FromHandForDiscard(cs, (PlayerState)target, prefs, null, ChoiceSource.For(PotionId, ChoiceSource.NoInstance, SiteMethod)).ToList();
        CardCmd.DiscardAndDraw(cs, selected, selected.Count);
    }
}
