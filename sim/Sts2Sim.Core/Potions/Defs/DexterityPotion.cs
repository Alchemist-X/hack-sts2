namespace Sts2Sim.Core.Potions;

public sealed class DexterityPotion : PotionModel
{
    public const string PotionId = "DEXTERITY_POTION";

    public const string DexterityPowerId = "DEXTERITY_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.DexterityName, 2m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(DexterityPowerId), target, DynamicVars.Dexterity.BaseValue, Owner, null);
    }
}
