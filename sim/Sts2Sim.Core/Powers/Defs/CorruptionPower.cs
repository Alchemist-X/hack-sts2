namespace Sts2Sim.Core.Powers;

public sealed class CorruptionPower : PowerModel
{
    public const string PowerId = "CORRUPTION_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool TryModifyEnergyCostInCombatLate(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner) || card.Type != CardType.Skill)
        {
            modifiedCost = originalCost;
            return false;
        }
        modifiedCost = 0m;
        return true;
    }

    public override CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return cardLocation;
        }
        if (card.Type != CardType.Skill)
        {
            return cardLocation;
        }
        return cardLocation with { PileType = PileType.Exhaust };
    }
}
