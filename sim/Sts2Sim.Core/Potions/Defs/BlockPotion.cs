namespace Sts2Sim.Core.Potions;

public sealed class BlockPotion : PotionModel
{
    public const string PotionId = "BLOCK_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new BlockVar(12m, ValueProp.Unpowered)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var block = DynamicVars.Block;
        CreatureCmd.GainBlock(cs, target, block.BaseValue, block.Props, null);
    }
}
