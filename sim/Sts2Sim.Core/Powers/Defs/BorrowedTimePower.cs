namespace Sts2Sim.Core.Powers;

public sealed class BorrowedTimePower : PowerModel
{
    public const string PowerId = "BORROWED_TIME_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return false;
        }
        modifiedCost = originalCost + Amount;
        return true;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
