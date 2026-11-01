namespace Sts2Sim.Core.Powers;

public sealed class StormPower : PowerModel
{
    public const string PowerId = "STORM_POWER";

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState))
        {
            return;
        }
        if (cardPlay.Card.Type != CardType.Power)
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, Amount);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var player = Owner as PlayerState;
        if (player == null || !ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), player))
        {
            return;
        }
        if (!_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int lightning) || lightning <= 0)
        {
            return;
        }
        for (int i = 0; i < lightning; i++)
        {
            OrbCmd.Channel<LightningOrb>(cs, player);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((StormPower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        var keys = new List<int>(_amountsForPlayedCards.Keys);
        keys.Sort();
        w.WriteInt32(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            w.WriteInt32(keys[i]);
            w.WriteInt32(_amountsForPlayedCards[keys[i]]);
        }
    }
}
