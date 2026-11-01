namespace Sts2Sim.Core.Powers;

public sealed class TangledPower : PowerModel
{
    public const string PowerId = "TANGLED_POWER";

    public const string EntangledAfflictionId = "ENTANGLED";

    public const int Energy = 1;

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var player = Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's combat cards (Models.Powers/TangledPower.cs:51), but its owner is not a player");
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Type == CardType.Attack)
            {
                CardCmd.Afflict(cs, EntangledAfflictionId, cards[i], 1m);
            }
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState) && card.Affliction == null && card.Type == CardType.Attack)
        {
            CardCmd.Afflict(cs, EntangledAfflictionId, card, 1m);
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
        var player = oldOwner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its old owner's combat cards (Models.Powers/TangledPower.cs:296), but its owner is not a player");
        var cards = AllCards(player);
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Affliction?.Id == EntangledAfflictionId)
            {
                CardCmd.ClearAffliction(cards[i]);
            }
        }
    }

    public override bool TryModifyEnergyCostInCombat(CombatState cs, CardInstance card, decimal originalCost, out decimal modifiedCost)
    {
        if (card.Affliction?.Id != EntangledAfflictionId || !ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            modifiedCost = originalCost;
            return false;
        }
        modifiedCost = originalCost + Amount;
        return true;
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
