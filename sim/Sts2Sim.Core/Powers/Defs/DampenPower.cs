namespace Sts2Sim.Core.Powers;

public sealed class DampenPower : PowerModel
{
    public const string PowerId = "DAMPEN_POWER";

    private List<int> _casterIds = new();

    private List<(int CardId, int UpgradeLevel)> _downgradedCards = new();

    public override string Id => PowerId;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.None;

    public IReadOnlyList<int> CasterIds => _casterIds;

    public void AddCaster(CreatureState creature)
    {
        if (!_casterIds.Contains(creature.CombatId))
        {
            _casterIds.Add(creature.CombatId);
        }
    }

    public override void AfterApplied(CombatState cs, CreatureState? applier, CardInstance? cardSource)
    {
        var player = (PlayerState)Owner;
        var upgraded = new List<CardInstance>();
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].IsUpgraded)
                {
                    upgraded.Add(cards[i]);
                }
            }
        }
        for (int i = 0; i < upgraded.Count; i++)
        {
            var card = upgraded[i];
            _downgradedCards.Add((card.InstanceId, card.UpgradeLevel));
            CardCmd.Downgrade(cs, card);
        }
    }

    public override void AfterDeath(RunState rs, CreatureState creature, bool wasRemovalPrevented)
    {
        if (wasRemovalPrevented)
        {
            return;
        }
        if (_casterIds.Remove(creature.CombatId) && _casterIds.Count == 0)
        {
            PowerCmd.Remove(rs.Combat!, this);
        }
    }

    public override void AfterRemoved(CombatState cs, CreatureState oldOwner)
    {
        var player = (PlayerState)oldOwner;
        for (int i = 0; i < _downgradedCards.Count; i++)
        {
            var (cardId, level) = _downgradedCards[i];
            var card = FindCard(player, cardId);
            if (card == null)
            {
                continue;
            }
            for (int u = 0; u < level; u++)
            {
                CardCmd.Upgrade(cs, card);
            }
        }
    }

    private static CardInstance? FindCard(PlayerState player, int instanceId)
    {
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].InstanceId == instanceId)
                {
                    return cards[i];
                }
            }
        }
        return null;
    }

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        var src = (DampenPower)source;
        _casterIds = new List<int>(src._casterIds);
        _downgradedCards = new List<(int, int)>(src._downgradedCards);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteCount(_casterIds.Count);
        for (int i = 0; i < _casterIds.Count; i++)
        {
            w.WriteInt32(_casterIds[i]);
        }
        w.WriteCount(_downgradedCards.Count);
        for (int i = 0; i < _downgradedCards.Count; i++)
        {
            w.WriteInt32(_downgradedCards[i].CardId);
            w.WriteInt32(_downgradedCards[i].UpgradeLevel);
        }
    }
}
