namespace Sts2Sim.Core.Potions;

public sealed class FirePotion : PotionModel
{
    public const string PotionId = "FIRE_POTION";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DamageVar(20m, ValueProp.Unpowered)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        var damage = DynamicVars.Damage;
        CreatureCmd.Damage(cs, target, damage.BaseValue, damage.Props, Owner, null, null);
    }
}
