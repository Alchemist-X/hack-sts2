namespace Sts2Sim.Core.Potions;

public sealed class EssenceOfDarkness : PotionModel
{
    public const string PotionId = "ESSENCE_OF_DARKNESS";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var targetPlayer = (PlayerState)target;
        int count = targetPlayer.OrbQueue.Capacity;
        for (int i = 0; i < count; i++)
        {
            OrbCmd.Channel<DarkOrb>(cs, targetPlayer);
        }
    }
}
