namespace Sts2Sim.Core.Potions;

public sealed class VulnerablePotion : PotionModel
{
    public const string PotionId = "VULNERABLE_POTION";

    public const string VulnerablePowerId = "VULNERABLE_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.VulnerableName, 3m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(VulnerablePowerId), target, DynamicVars.Vulnerable.BaseValue, Owner, null);
    }
}
