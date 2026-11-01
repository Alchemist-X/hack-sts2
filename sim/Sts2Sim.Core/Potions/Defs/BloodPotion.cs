namespace Sts2Sim.Core.Potions;

public sealed class BloodPotion : PotionModel
{
    public const string PotionId = "BLOOD_POTION";

    public const string HealPercentKey = "HealPercent";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(HealPercentKey, 20m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.Heal(cs, target, (decimal)target.MaxHp * DynamicVars[HealPercentKey].BaseValue / 100m);
    }
}
