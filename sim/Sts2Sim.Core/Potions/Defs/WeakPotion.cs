namespace Sts2Sim.Core.Potions;

public sealed class WeakPotion : PotionModel
{
    public const string PotionId = "WEAK_POTION";

    public const string WeakPowerId = "WEAK_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.WeakName, 3m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(WeakPowerId), target, DynamicVars.Weak.BaseValue, Owner, null);
    }
}
