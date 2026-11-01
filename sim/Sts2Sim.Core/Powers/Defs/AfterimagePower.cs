namespace Sts2Sim.Core.Powers;

public sealed class AfterimagePower : PowerModel
{
    public const string PowerId = "AFTERIMAGE_POWER";

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner))
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, Amount);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner) && _amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int amount) && amount > 0)
        {
            CreatureCmd.GainBlock(cs, Owner, amount, ValueProp.Unpowered, null);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((AfterimagePower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_amountsForPlayedCards.Count);
        foreach (var key in _amountsForPlayedCards.Keys.OrderBy(k => k))
        {
            w.WriteInt32(key);
            w.WriteInt32(_amountsForPlayedCards[key]);
        }
    }
}
