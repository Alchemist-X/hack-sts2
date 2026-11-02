namespace Sts2Sim.Core.Powers;

public sealed class SmoggyPower : PowerModel
{
    public const string PowerId = "SMOGGY_POWER";

    public const string SmogAfflictionId = "SMOG";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override void AfterCardPlayed(CombatState cs, CardPlay cardPlay)
    {
        if (!ReferenceEquals(CombatStatus.CardOwnerCreature(cs, cardPlay.Card), Owner) || cardPlay.Card.Type != CardType.Skill)
        {
            return;
        }
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's combat cards (Models.Powers/SmoggyPower.cs:127), but its owner is not a player");
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            var current = cards[i];
            if (current.Type != CardType.Skill || current.Affliction != null)
            {
                continue;
            }
            CardCmd.Afflict(cs, SmogAfflictionId, current, 1m);
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState) || card.Affliction != null || card.Type != CardType.Skill)
        {
            return;
        }
        var owner = Owner;
        if (!cs.History.AnyOf<CardPlayStartedEntry>(e => e.Stamp.HappenedThisTurn(cs) && e.CardPlay.CardType == CardType.Skill && owner is PlayerState p && e.CardPlay.PlayerId == p.CombatId))
        {
            return;
        }
        CardCmd.Afflict(cs, SmogAfflictionId, card, 1m);
    }

    public override void AfterSideTurnEnd(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
    {
        if (!CombatStatus.ContainsById(participants, Owner))
        {
            return;
        }
        if (Owner is not PlayerState player)
        {
            return;
        }
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Affliction?.Id == SmogAfflictionId)
            {
                CardCmd.ClearAffliction(cards[i]);
            }
        }
    }

    public override bool ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            return true;
        }
        return card.Affliction?.Id != SmogAfflictionId;
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
