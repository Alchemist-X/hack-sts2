namespace Sts2Sim.Core.Relics;

public sealed class BingBong : RelicModel
{
    public const string RelicId = "BING_BONG";

    private HashSet<int> _cardsToSkip = new();

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    public override void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)
    {
        if (card.GetPileType(rs.Combat) != PileType.Deck || !card.HasOwner || !ReferenceEquals(card.Owner, Owner) || clonedBy != null || _cardsToSkip.Remove(card.InstanceId))
        {
            return;
        }
        var clone = card.CloneForCombat(rs.AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        _cardsToSkip.Add(clone.InstanceId);
        CardPileCmd.Add(rs, clone, PileType.Deck, CardPilePosition.Bottom, this);
    }

    protected override void CopyMutableReferenceState(RelicModel source)
    {
        _cardsToSkip = new HashSet<int>(((BingBong)source)._cardsToSkip);
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        var ids = _cardsToSkip.ToList();
        ids.Sort();
        w.WriteInt32(ids.Count);
        foreach (var id in ids)
        {
            w.WriteInt32(id);
        }
    }
}
