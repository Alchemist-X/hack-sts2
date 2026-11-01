namespace Sts2Sim.Core.Powers;

public sealed class HexPower : PowerModel
{
    public const string PowerId = "HEX_POWER";

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            return false;
        }
        if (card.Affliction is not Hexed)
        {
            return false;
        }
        return keywords.Add(CardKeyword.Ethereal);
    }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var cards = AllCards(OwnerPlayer());
        for (int i = 0; i < cards.Count; i++)
        {
            Afflict(cs, cards[i]);
        }
    }

    public override void AfterCardEnteredCombat(CombatState cs, CardInstance card)
    {
        if (!ReferenceEquals(CombatStatus.CardOwner(cs, card), Owner as PlayerState))
        {
            return;
        }
        Afflict(cs, card);
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented || ApplierId == NoCreature || creature.CombatId != ApplierId)
        {
            return;
        }
        PowerCmd.Remove(rs.Combat!, this);
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        var cards = AllCards(OwnerPlayer());
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Affliction is Hexed)
            {
                CardCmd.ClearAffliction(cards[i]);
            }
        }
    }

    private void Afflict(CombatState cs, CardInstance card)
    {
        if (card.Affliction == null)
        {
            CardCmd.Afflict<Hexed>(cs, card, Amount);
        }
    }

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's combat cards (Models.Powers/HexPower.cs:118), but its owner is not a player");

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
