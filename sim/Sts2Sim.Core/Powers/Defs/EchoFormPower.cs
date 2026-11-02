namespace Sts2Sim.Core.Powers;

public sealed class EchoFormPower : PowerModel
{
    public const string PowerId = "ECHO_FORM_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override int ModifyCardPlayCount(CombatState cs, CardInstance card, CreatureState? target, int playCount)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return playCount;
        }
        int ownerId = Owner.CombatId;
        int num = cs.History.CountOf<CardPlayStartedEntry>(e => e.Stamp.ActorId == ownerId && e.CardPlay.IsFirstInSeries && e.Stamp.HappenedThisTurn(cs));
        if (num >= Amount)
        {
            return playCount;
        }
        return playCount + 1;
    }
}
