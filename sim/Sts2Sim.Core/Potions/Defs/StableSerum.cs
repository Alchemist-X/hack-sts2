namespace Sts2Sim.Core.Potions;

public sealed class StableSerum : PotionModel
{
    public const string PotionId = "STABLE_SERUM";

    public const string RetainHandPowerId = "RETAIN_HAND_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new RepeatVar(2)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(RetainHandPowerId), target, DynamicVars.Repeat.BaseValue, Owner, null);
    }
}
