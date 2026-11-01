namespace Sts2Sim.Core.Potions;

public sealed class PoisonPotion : PotionModel
{
    public const string PotionId = "POISON_POTION";

    public const string PoisonPowerId = "POISON_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyEnemy;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(DynamicVarSet.PoisonName, 6m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(PoisonPowerId), target, DynamicVars.Poison.BaseValue, Owner, null);
    }
}
