namespace Sts2Sim.Core.Potions;

public sealed class ShipInABottle : PotionModel
{
    public const string PotionId = "SHIP_IN_A_BOTTLE";

    public const string BlockNextTurnPowerId = "BLOCK_NEXT_TURN_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(10m, ValueProp.Unpowered)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var block = DynamicVars.Block;
        CreatureCmd.GainBlock(cs, target, block, null);
        PowerCmd.Apply(cs, PowerRegistry.Create(BlockNextTurnPowerId), target, block.IntValue, Owner, null);
    }
}
