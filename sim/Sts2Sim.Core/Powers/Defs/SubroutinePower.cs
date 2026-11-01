namespace Sts2Sim.Core.Powers;

public sealed class SubroutinePower : PowerModel
{
    public const string PowerId = "SUBROUTINE_POWER";

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner))
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
        var player = CombatStatus.CardOwner(cs, cardPlay.Card);
        if (!ReferenceEquals(player, Owner) || !_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int energy) || energy <= 0)
        {
            return;
        }
        for (int i = 0; i < energy; i++)
        {
            PlayerCmd.GainEnergy(cs, 1m, player);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((SubroutinePower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        WriteCardAmounts(w, _amountsForPlayedCards);
    }

    private static void WriteCardAmounts(CanonicalWriter w, Dictionary<int, int> amounts)
    {
        var keys = new List<int>(amounts.Keys);
        keys.Sort();
        w.WriteInt32(keys.Count);
        for (int i = 0; i < keys.Count; i++)
        {
            w.WriteInt32(keys[i]);
            w.WriteInt32(amounts[keys[i]]);
        }
    }
}
