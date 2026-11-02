namespace Sts2Sim.Core.Potions;

public sealed class Ashwater : PotionModel
{
    public const string PotionId = "ASHWATER";

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
        var selected = CardSelectCmd.FromHand(cs, (PlayerState)target, prefs, null, ChoiceSource.For(PotionId, ChoiceSource.NoInstance, SiteMethod)).ToList();
        for (int i = 0; i < selected.Count; i++)
        {
            CardPileCmd.Exhaust(cs, selected[i]);
        }
    }
}
