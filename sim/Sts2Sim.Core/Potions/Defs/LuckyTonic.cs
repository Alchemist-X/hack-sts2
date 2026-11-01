namespace Sts2Sim.Core.Potions;

public sealed class LuckyTonic : PotionModel
{
    public const string PotionId = "LUCKY_TONIC";

    public const string BufferPowerId = "BUFFER_POWER";

    public const string BufferPowerKey = "BufferPower";

    public override string Id => PotionId;

    public override PotionRarity Rarity => PotionRarity.Rare;

    public override PotionUsage Usage => PotionUsage.CombatOnly;

    public override TargetType TargetType => TargetType.AnyPlayer;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(BufferPowerKey, 1m)
    };

    protected override void OnUse(RunState rs, CreatureState? target)
    {
        AssertValidForTargetedPotion(target);
        var cs = RequireCombat(rs);
        PowerCmd.Apply(cs, PowerRegistry.Create(BufferPowerId), target, DynamicVars[BufferPowerKey].BaseValue, Owner, null);
    }
}
