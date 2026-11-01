namespace Sts2Sim.Core.Powers;

public sealed class DampenPower : PowerModel
{
    public const string PowerId = "DAMPEN_POWER";

    private List<int> _casterIds = new();

    private List<(int InstanceId, int UpgradeLevel)> _downgradedCards = new();

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
        var player = OwnerPlayer();
        var piles = player.AllPiles;
        for (int p = 0; p < piles.Count; p++)
        {
            var cards = piles[p].Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                var card = cards[i];
                if (!card.IsUpgraded)
                {
                    continue;
                }
                _downgradedCards.Add((card.InstanceId, card.UpgradeLevel));
                CardCmd.Downgrade(cs, card);
            }
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
        if (oldOwner is not PlayerState player)
        {
            return;
        }
        for (int i = 0; i < _downgradedCards.Count; i++)
        {
            var (instanceId, level) = _downgradedCards[i];
            var card = FindCard(player, instanceId);
            if (card == null)
            {
                continue;
            }
            for (int j = 0; j < level; j++)
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

    private PlayerState OwnerPlayer()
        => Owner as PlayerState ?? throw new InvalidOperationException($"{PowerId} reads its owner's player cards (Models.Powers/DampenPower.cs:115), but its owner is not a player");

    protected override void CopyMutableReferenceState(PowerModel source)
    {
        var s = (DampenPower)source;
        _casterIds = new List<int>(s._casterIds);
        _downgradedCards = new List<(int, int)>(s._downgradedCards);
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
            w.WriteInt32(_downgradedCards[i].InstanceId);
            w.WriteInt32(_downgradedCards[i].UpgradeLevel);
        }
    }
}
