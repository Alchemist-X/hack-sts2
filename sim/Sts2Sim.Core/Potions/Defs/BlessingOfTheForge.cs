namespace Sts2Sim.Core.Potions;

public sealed class BlessingOfTheForge : PotionModel
{
    public const string PotionId = "BLESSING_OF_THE_FORGE";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var hand = ((PlayerState)target).Hand.Cards;
        for (int i = 0; i < hand.Count; i++)
        {
            var current = hand[i];
            if (current.IsUpgradable)
            {
                CardCmd.Upgrade(cs, current);
            }
        }
    }
}
