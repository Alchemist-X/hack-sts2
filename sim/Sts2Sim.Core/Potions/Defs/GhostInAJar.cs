namespace Sts2Sim.Core.Potions;

public sealed class GhostInAJar : PotionModel
{
    public const string PotionId = "GHOST_IN_A_JAR";

    public const string IntangiblePowerId = "INTANGIBLE_POWER";

    public const string IntangiblePowerKey = "IntangiblePower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(IntangiblePowerKey, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(IntangiblePowerId), target, DynamicVars[IntangiblePowerKey].BaseValue, Owner, null);
    }
}
