namespace Sts2Sim.Core.Potions;

public sealed class Fortifier : PotionModel
{
    public const string PotionId = "FORTIFIER";

    public const int BlockMultiplier = 2;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.GainBlock(cs, target, (decimal)(target.Block * BlockMultiplier), ValueProp.Unpowered, null);
    }
}
