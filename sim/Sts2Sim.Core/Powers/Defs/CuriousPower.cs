namespace Sts2Sim.Core.Powers;

public sealed class CuriousPower : PowerModel
{
    public const string PowerId = "CURIOUS_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        modifiedCost = originalCost;
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return false;
        }
        if (card.Type != CardType.Power)
        {
            return false;
        }
        if (originalCost <= 0m)
        {
            return false;
        }
        modifiedCost = originalCost - Amount;
        if (modifiedCost < 0m)
        {
            modifiedCost = 0m;
        }
        return true;
    }
}
