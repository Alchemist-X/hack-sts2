namespace Sts2Sim.Core.Potions;

public sealed class GigantificationPotion : PotionModel
{
    public const string PotionId = "GIGANTIFICATION_POTION";

    public const string GigantificationPowerId = "GIGANTIFICATION_POWER";

    public const string GigantificationPowerKey = "GigantificationPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(GigantificationPowerKey, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(GigantificationPowerId), target, DynamicVars[GigantificationPowerKey].BaseValue, Owner, null);
    }
}
