namespace Sts2Sim.Core.Relics;

public sealed class AlchemicalCoffer : RelicModel
{
    public const string RelicId = "ALCHEMICAL_COFFER";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool HasUponPickupEffect => true;

    public const string PotionSlotsKey = "PotionSlots";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar(PotionSlotsKey, 4m)
    };

    public override void AfterObtained(RunState rs)
    {
        int originalSlotCount = Owner.MaxPotionCount;
        PlayerCmd.GainMaxPotionCount(rs, DynamicVars[PotionSlotsKey].IntValue, Owner);
        var potions = PotionFactory.CreateRandomPotionsOutOfCombat(rs, Owner, DynamicVars[PotionSlotsKey].IntValue, rs.Rng.CombatPotionGeneration);
        int i = 0;
        foreach (var current in potions)
        {
            PotionCmd.TryToProcure(rs, PotionRegistry.Create(current.Id), Owner, originalSlotCount + i);
            i++;
        }
    }
}
