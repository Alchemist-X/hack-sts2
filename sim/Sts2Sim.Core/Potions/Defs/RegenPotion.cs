namespace Sts2Sim.Core.Potions;

public sealed class RegenPotion : PotionModel
{
    public const string PotionId = "REGEN_POTION";

    public const string RegenPowerId = "REGEN_POWER";

    public const string RegenVarName = "RegenPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    public override bool CanBeGeneratedInCombat => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(RegenVarName, 5m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(RegenPowerId), target, DynamicVars[RegenVarName].BaseValue, Owner, null);
    }
}
