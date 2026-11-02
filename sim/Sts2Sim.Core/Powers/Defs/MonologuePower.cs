namespace Sts2Sim.Core.Powers;

public sealed class MonologuePower : PowerModel
{
    public const string PowerId = "MONOLOGUE_POWER";

    public const string StrengthAppliedKey = "StrengthApplied";

    public const decimal BaseStrength = 1m;

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => (int)StrengthApplied != 0 ? PowerStackType.Counter : PowerStackType.None;

    public override PowerInstanceType InstanceType => PowerInstanceType.Instanced;

    public decimal Strength { get; set; } = BaseStrength;

    public decimal StrengthApplied { get; private set; }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner))
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, (int)Strength);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), Owner as PlayerState)
            || !_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int amount))
        {
            return;
        }
        PowerCmd.Apply<StrengthPower>(cs, Owner, amount, Owner, null, silent: true);
        StrengthApplied += (int)Strength;
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Remove(cs, this);
        PowerCmd.Apply<StrengthPower>(cs, Owner, -StrengthApplied, Owner, null, silent: true);
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((MonologuePower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteDecimal(Strength);
        w.WriteDecimal(StrengthApplied);
        w.WriteCount(_amountsForPlayedCards.Count);
        foreach (var key in _amountsForPlayedCards.Keys.OrderBy(k => k))
        {
            w.WriteInt32(key);
            w.WriteInt32(_amountsForPlayedCards[key]);
        }
    }
}
