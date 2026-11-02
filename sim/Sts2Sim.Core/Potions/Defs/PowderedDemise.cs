namespace Sts2Sim.Core.Potions;

public sealed class PowderedDemise : PotionModel
{
    public const string PotionId = "POWDERED_DEMISE";

    public const string DemisePowerId = "DEMISE_POWER";

    public const string DemiseKey = "Demise";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DemiseKey, 9m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(DemisePowerId), target, DynamicVars[DemiseKey].BaseValue, Owner, null);
    }
}
