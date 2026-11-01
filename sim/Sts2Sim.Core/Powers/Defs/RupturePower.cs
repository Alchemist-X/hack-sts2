namespace Sts2Sim.Core.Powers;

public sealed class RupturePower : PowerModel
{
    public const string PowerId = "RUPTURE_POWER";

    private Dictionary<int, int> _playedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner))
        {
            return;
        }
        if (cs.CurrentSide != Owner.Side)
        {
            return;
        }
        _playedCards.Add(cardPlay.Card.InstanceId, 0);
    }

    public override void AfterDamageReceived(RunState rs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)
    {
        var cs = rs.Combat!;
        if (!ReferenceEquals(target, Owner) || result.UnblockedDamage <= 0 || cs.CurrentSide != Owner.Side)
        {
            return;
        }
        if (cardSource == null || !_playedCards.ContainsKey(cardSource.InstanceId))
        {
            PowerCmd.Apply<StrengthPower>(cs, Owner, Amount, Owner, null);
            return;
        }
        _playedCards[cardSource.InstanceId] += Amount;
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner) || !_playedCards.Remove(cardPlay.Card.InstanceId, out int strength))
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, strength, Owner, null);
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _playedCards = new Dictionary<int, int>(((RupturePower)source)._playedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        WriteCardAmounts(w, _playedCards);
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
