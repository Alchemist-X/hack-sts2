namespace Sts2Sim.Core.Potions;

public sealed class PotionShapedRock : PotionModel
{
    public const string PotionId = "POTION_SHAPED_ROCK";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Token;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Unpowered)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.Damage(cs, target, DynamicVars.Damage, Owner, null, null);
    }
}
