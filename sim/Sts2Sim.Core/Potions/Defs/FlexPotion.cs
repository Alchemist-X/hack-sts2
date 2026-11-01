namespace Sts2Sim.Core.Potions;

public sealed class FlexPotion : PotionModel
{
    public const string PotionId = "FLEX_POTION";

    public const string FlexPotionPowerId = "FLEX_POTION_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.StrengthName, 5m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(FlexPotionPowerId), target, DynamicVars.Strength.BaseValue, Owner, null);
    }
}
