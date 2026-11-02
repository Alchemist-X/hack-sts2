namespace Sts2Sim.Core.Powers;

public sealed class RagePower : PowerModel
{
    public const string PowerId = "RAGE_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner) && cardPlay.Card.Type == CardType.Attack)
        {
            CreatureCmd.GainBlock(cs, Owner, Amount, ValueProp.Unpowered, null);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }
}
