namespace Sts2Sim.Core.Potions;

public sealed class FyshOil : PotionModel
{
    public const string PotionId = "FYSH_OIL";

    public const string StrengthPowerId = "STRENGTH_POWER";

    public const string DexterityPowerId = "DEXTERITY_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.StrengthName, 1m),
        new DynamicVar(DynamicVarSet.DexterityName, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(StrengthPowerId), target, DynamicVars.Strength.BaseValue, Owner, null);
        PowerCmd.Apply(cs, PowerRegistry.Create(DexterityPowerId), target, DynamicVars.Dexterity.BaseValue, Owner, null);
    }
}
