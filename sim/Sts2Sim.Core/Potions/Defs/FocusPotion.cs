namespace Sts2Sim.Core.Potions;

public sealed class FocusPotion : PotionModel
{
    public const string PotionId = "FOCUS_POTION";

    public const string FocusPowerId = "FOCUS_POWER";

    public const string FocusPowerKey = "FocusPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(FocusPowerKey, 2m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(FocusPowerId), target, DynamicVars[FocusPowerKey].BaseValue, target, null);
    }
}
