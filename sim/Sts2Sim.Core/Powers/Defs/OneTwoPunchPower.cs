namespace Sts2Sim.Core.Powers;

public sealed class OneTwoPunchPower : PowerModel
{
    public const string PowerId = "ONE_TWO_PUNCH_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return playCount;
        }
        if (card.Type != CardType.Attack)
        {
            return playCount;
        }
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(CombatState cs, CardInstance card)
    {
        PowerCmd.Decrement(cs, this);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
