namespace Sts2Sim.Core.Powers;

public sealed class ReboundPower : PowerModel
{
    public const string PowerId = "REBOUND_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return cardLocation;
        }
        if (cardLocation.PileType != PileType.Discard)
        {
            return cardLocation;
        }
        return new CardLocation(PileType.Draw, CardPilePosition.Top);
    }

    public override void AfterModifyingCardPlayResultLocation(CombatState cs, CardInstance card, CardLocation cardLocation)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        PowerCmd.Decrement(cs, this);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Remove(cs, this);
    }
}
