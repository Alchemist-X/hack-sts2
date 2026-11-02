namespace Sts2Sim.Core.Powers;

public sealed class JugglingPower : PowerModel
{
    public const string PowerId = "JUGGLING_POWER";

    public const int AttackThreshold = 3;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public int AttacksPlayedThisTurn { get; private set; }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var owner = Owner;
        AttacksPlayedThisTurn = cs.History.CountOf<CardPlayStartedEntry>(e => e.CardPlay.CardType == CardType.Attack && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId && e.Stamp.HappenedThisTurn(cs));
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var player = Owner as PlayerState;
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, cardPlay.Card), player) || cardPlay.Card.Type != CardType.Attack)
        {
            return;
        }
        AttacksPlayedThisTurn++;
        if (AttacksPlayedThisTurn != AttackThreshold)
        {
            return;
        }
        for (int i = 0; i < Amount; i++)
        {
            var card = CreateClone(cs, cardPlay.Card);
            CardPileCmd.AddGeneratedCardToCombat(cs, card, PileType.Hand, player);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        AttacksPlayedThisTurn = 0;
    }

    private static CardInstance CreateClone(CombatState cs, CardInstance card)
    {
        if (card.GetCombatPile(cs) == null && card.IsInDeck)
        {
            throw new InvalidOperationException("Cannot create a clone of a card that is not in a combat pile.");
        }
        return card.CreateClone(cs);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(AttacksPlayedThisTurn);
    }
}
