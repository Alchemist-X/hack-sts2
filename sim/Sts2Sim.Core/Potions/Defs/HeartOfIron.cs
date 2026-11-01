namespace Sts2Sim.Core.Potions;

public sealed class HeartOfIron : PotionModel
{
    public const string PotionId = "HEART_OF_IRON";

    public const string PlatingPowerId = "PLATING_POWER";

    public const string PlatingPowerKey = "PlatingPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PlatingPowerKey, 7m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(PlatingPowerId), target, DynamicVars[PlatingPowerKey].BaseValue, Owner, null);
    }
}
