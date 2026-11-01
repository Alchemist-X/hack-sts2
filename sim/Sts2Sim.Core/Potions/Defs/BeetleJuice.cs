namespace Sts2Sim.Core.Potions;

public sealed class BeetleJuice : PotionModel
{
    public const string PotionId = "BEETLE_JUICE";

    public const string ShrinkPowerId = "SHRINK_POWER";

    public const string DamageDecreaseKey = "DamageDecrease";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DamageDecreaseKey, 30m),
        new RepeatVar(4)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(ShrinkPowerId), target, DynamicVars.Repeat.BaseValue, Owner, null);
    }
}
