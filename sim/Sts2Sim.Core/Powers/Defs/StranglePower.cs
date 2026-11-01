namespace Sts2Sim.Core.Powers;

public sealed class StranglePower : PowerModel
{
    public const string PowerId = "STRANGLE_POWER";

    private Dictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override PowerInstanceType InstanceType => PowerInstanceType.InstancedPerApplier;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (GetApplier(cs) is not PlayerState applierPlayer)
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), applierPlayer))
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, Amount);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId, out int amount))
        {
            CreatureCmd.Damage(cs, Owner, amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null, null);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (CombatStatus.ContainsById(participants, Owner))
        {
            PowerCmd.Remove(cs, this);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new Dictionary<int, int>(((StranglePower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteCount(_amountsForPlayedCards.Count);
        foreach (var pair in _amountsForPlayedCards.OrderBy(p => p.Key))
        {
            w.WriteInt32(pair.Key);
            w.WriteInt32(pair.Value);
        }
    }
}
