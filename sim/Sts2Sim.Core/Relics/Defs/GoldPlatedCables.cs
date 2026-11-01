namespace Sts2Sim.Core.Relics;

public sealed class GoldPlatedCables : RelicModel
{
    public const string RelicId = "GOLD_PLATED_CABLES";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override int ModifyOrbPassiveTriggerCounts(CombatState cs, OrbModel orb, int triggerCount)
    {
        if (!ReferenceEquals(orb.Owner, Owner))
        {
            return triggerCount;
        }
        if (!ReferenceEquals(orb, Owner.OrbQueue.Orbs[0]))
        {
            return triggerCount;
        }
        return triggerCount + 1;
    }

    public override void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb)
    {
    }
}
