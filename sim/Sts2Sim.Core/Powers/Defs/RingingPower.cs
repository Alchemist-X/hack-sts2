namespace Sts2Sim.Core.Powers;

public sealed class RingingPower : PowerModel
{
    public const string PowerId = "RINGING_POWER";

    public const string RingingAfflictionId = "RINGING";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's combat cards (Models.Powers/RingingPower.cs:51), but its owner is not a player");
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Affliction != null)
            {
                continue;
            }
            CardCmd.Afflict(cs, RingingAfflictionId, cards[i], 1m);
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState) && card.Affliction == null)
        {
            CardCmd.Afflict(cs, RingingAfflictionId, card, 1m);
        }
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        PowerCmd.Remove(cs, this);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        if (oldOwner is not PlayerState player)
        {
            return;
        }
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Affliction?.Id == RingingAfflictionId)
            {
                CardCmd.ClearAffliction(cards[i]);
            }
        }
    }

    public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, card), Owner))
        {
            return true;
        }
        if (card.Affliction?.Id != RingingAfflictionId)
        {
            return true;
        }
        var owner = Owner;
        return !cs.History.AnyOf<CardPlayStartedEntry>(e => e.Stamp.HappenedThisTurn(cs) && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId);
    }

    private static List<CardInstance> AllCards(PlayerState player)
    {
        var all = new List<CardInstance>();
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            all.AddRange(piles[p].Cards);
        }
        return all;
    }
}
