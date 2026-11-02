namespace Sts2Sim.Core.Potions;

public sealed class LiquidBronze : PotionModel
{
    public const string PotionId = "LIQUID_BRONZE";

    public const string ThornsPowerId = "THORNS_POWER";

    public const string ThornsPowerKey = "ThornsPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(ThornsPowerKey, 3m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(ThornsPowerId), target, DynamicVars[ThornsPowerKey].BaseValue, Owner, null);
    }
}
