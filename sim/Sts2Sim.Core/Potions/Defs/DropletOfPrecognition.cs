namespace Sts2Sim.Core.Potions;

public sealed class DropletOfPrecognition : PotionModel
{
    public const string PotionId = "DROPLET_OF_PRECOGNITION";

    public const string SiteMethod = "OnUse";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var player = (PlayerState)target;
        var prefs = new CardSelectorPrefs(PotionId + ".selectionScreenPrompt", 1);
        var cardModel = CardSelectCmd.FromCombatPile(cs, player.DrawPile, player, prefs, ChoiceSource.For(PotionId, ChoiceSource.NoInstance, SiteMethod)).FirstOrDefault();
        if (cardModel != null)
        {
            CardPileCmd.Add(cs, cardModel, PileType.Hand);
        }
    }
}
