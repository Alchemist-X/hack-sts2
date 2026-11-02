namespace Sts2Sim.Core.Powers;

public sealed class ChainsOfBindingPower : PowerModel
{
    public const string PowerId = "CHAINS_OF_BINDING_POWER";

    public const string BoundAfflictionId = "BOUND";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public bool BoundCardPlayed { get; private set; }

    public override void AfterCardDrawn(CombatState cs, CardInstance card, bool fromHandDraw)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            return;
        }
        if (cs.CurrentSide != Owner.Side)
        {
            return;
        }
        if (!AfflictionRegistry.Create(BoundAfflictionId).CanAfflict(cs, card))
        {
            return;
        }
        int ownerId = Owner.CombatId;
        int num2 = cs.History.CountOf<CardAfflictedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.Stamp.ActorId == ownerId && e.AfflictionId == BoundAfflictionId);
        if (num2 < Amount)
        {
            CardCmd.AfflictAndPreview(cs, new[] { card }, BoundAfflictionId, Amount);
        }
    }

    public override void BeforeCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        var card = cardPlay.Card;
        if (card.IsDupe)
        {
            return;
        }
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return;
        }
        if (card.Affliction?.Id != BoundAfflictionId)
        {
            return;
        }
        BoundCardPlayed = true;
    }

    public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return true;
        }
        if (card.Affliction?.Id != BoundAfflictionId)
        {
            return true;
        }
        return !BoundCardPlayed;
    }

    public override void BeforeSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        BoundCardPlayed = false;
        if (Owner is not PlayerState player)
        {
            return;
        }
        var piles = player.AllPiles;
        var bound = new List<CardInstance>();
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Affliction?.Id == BoundAfflictionId)
                {
                    bound.Add(cards[i]);
                }
            }
        }
        for (int i = 0; i < bound.Count; i++)
        {
            CardCmd.ClearAffliction(bound[i]);
        }
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteBool(BoundCardPlayed);
    }
}
