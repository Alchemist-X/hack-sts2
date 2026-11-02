namespace Sts2Sim.Core.Potions;

public sealed class StrengthPotion : PotionModel
{
    public const string PotionId = "STRENGTH_POTION";

    public const string StrengthPowerId = "STRENGTH_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(DynamicVarSet.StrengthName, 2m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(StrengthPowerId), target, DynamicVars.Strength.BaseValue, Owner, null);
    }
}
