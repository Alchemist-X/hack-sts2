namespace Sts2Sim.Core.Potions;

public sealed class Ambergris : PotionModel
{
    public const string PotionId = "AMBERGRIS";

    public const string AmbergrisPowerId = "AMBERGRIS_POWER";

    public const string HealPercentKey = "HealPercent";

    public const decimal AmbergrisAmount = 1m;

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Event;

    public override PotionUsage Usage => PotionUsage.AnyTime;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(HealPercentKey, 50m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        CreatureCmd.Heal(cs, target, (decimal)target.MaxHp * DynamicVars[HealPercentKey].BaseValue / 100m);
        if (cs.IsInProgress)
        {
            PowerCmd.Apply(cs, PowerRegistry.Create(AmbergrisPowerId), target, AmbergrisAmount, Owner, null);
        }
    }
}
