namespace Sts2Sim.Core.Powers;

public sealed class SignalBoostPower : PowerModel
{
    public const string PowerId = "SIGNAL_BOOST_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return playCount;
        }
        if (card.Type != CardType.Power)
        {
            return playCount;
        }
        return playCount + 1;
    }

    public override void AfterModifyingCardPlayCount(CombatState cs, CardInstance card)
    {
        PowerCmd.Decrement(cs, this);
    }
}
