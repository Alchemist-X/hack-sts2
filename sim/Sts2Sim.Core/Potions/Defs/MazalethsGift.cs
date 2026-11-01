namespace Sts2Sim.Core.Potions;

public sealed class MazalethsGift : PotionModel
{
    public const string PotionId = "MAZALETHS_GIFT";

    public const string RitualPowerId = "RITUAL_POWER";

    public const string RitualPowerKey = "RitualPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(RitualPowerKey, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(RitualPowerId), target, DynamicVars[RitualPowerKey].BaseValue, Owner, null);
    }
}
