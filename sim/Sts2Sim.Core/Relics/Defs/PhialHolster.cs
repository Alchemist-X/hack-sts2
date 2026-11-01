namespace Sts2Sim.Core.Relics;

public sealed class PhialHolster : RelicModel
{
    public const string RelicId = "PHIAL_HOLSTER";

    private const string PotionSlotsKey = "PotionSlots";

    private const string PotionCountKey = "Potions";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(PotionSlotsKey, 1m),
        new DynamicVar(PotionCountKey, 2m)
    };

    public override void AfterObtained(RunState rs)
    {
        PlayerCmd.GainMaxPotionCount(rs, DynamicVars[PotionSlotsKey].IntValue, Owner);
        foreach (var current in PotionFactory.CreateRandomPotionsOutOfCombat(rs, Owner, DynamicVars[PotionCountKey].IntValue, rs.Rng.CombatPotionGeneration))
        {
            PotionCmd.TryToProcure(rs, current.ToMutable(), Owner);
        }
    }
}
