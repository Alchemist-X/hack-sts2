namespace Sts2Sim.Core.Potions;

public sealed class RadiantTincture : PotionModel
{
    public const string PotionId = "RADIANT_TINCTURE";

    public const string RadiancePowerId = "RADIANCE_POWER";

    public const string RadiancePowerKey = "RadiancePower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Uncommon;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new EnergyVar(1),
        new DynamicVar(RadiancePowerKey, 3m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PlayerCmd.GainEnergy(cs, DynamicVars.Energy.IntValue, (PlayerState)target);
        PowerCmd.Apply(cs, PowerRegistry.Create(RadiancePowerId), target, DynamicVars[RadiancePowerKey].BaseValue, Owner, null);
    }
}
