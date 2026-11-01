namespace Sts2Sim.Core.Potions;

public sealed class SpeedPotion : PotionModel
{
    public const string PotionId = "SPEED_POTION";

    public const string SpeedPotionPowerId = "SPEED_POTION_POWER";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Common;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<DexterityPower>(5m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(SpeedPotionPowerId), target, DynamicVars.Dexterity.BaseValue, Owner, null);
    }
}
