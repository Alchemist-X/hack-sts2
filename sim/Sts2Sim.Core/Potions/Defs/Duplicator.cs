namespace Sts2Sim.Core.Potions;

public sealed class Duplicator : PotionModel
{
    public const string PotionId = "DUPLICATOR";

    public const string DuplicationPowerId = "DUPLICATION_POWER";

    public const decimal DuplicationAmount = 1m;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(DuplicationPowerId), target, DuplicationAmount, target, null);
    }
}
