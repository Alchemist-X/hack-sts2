namespace Sts2Sim.Core.Powers;

public sealed class CalamityPower : PowerModel
{
    public const string PowerId = "CALAMITY_POWER";

    private SortedDictionary<int, int> _amountsForPlayedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner))
        {
            return;
        }
        if (cardPlay.Card.Type != CardType.Attack)
        {
            return;
        }
        _amountsForPlayedCards.Add(cardPlay.Card.InstanceId, Amount);
    }

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!_amountsForPlayedCards.Remove(cardPlay.Card.InstanceId))
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} generates cards for its owner's player (Models.Powers/CalamityPower.cs:62), but its owner is not a player");
        var rs = cs.Run;
        var pool = CardPools.ForCharacter(player.CharacterId);
        var attacks = pool.GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs)).Where(c => c.Type == CardType.Attack);
        var cards = CardFactory.GetForCombat(cs, player, attacks, Amount, rs.Rng.CombatCardGeneration);
        for (int i = 0; i < cards.Count; i++)
        {
            CardPileCmd.AddGeneratedCardToCombat(cs, cards[i], PileType.Hand, player);
        }
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        _amountsForPlayedCards = new SortedDictionary<int, int>(((CalamityPower)source)._amountsForPlayedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteCount(_amountsForPlayedCards.Count);
        foreach (var pair in _amountsForPlayedCards)
        {
            w.WriteInt32(pair.Key);
            w.WriteInt32(pair.Value);
        }
    }
}
