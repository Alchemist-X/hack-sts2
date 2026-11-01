namespace Sts2Sim.Core.Relics;

public sealed class PotionBelt : RelicModel
{
    public const string RelicId = "POTION_BELT";

    private const string PotionSlotsKey = "PotionSlots";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(PotionSlotsKey, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.GainMaxPotionCount(rs, DynamicVars[PotionSlotsKey].IntValue, Owner);
    }
}
