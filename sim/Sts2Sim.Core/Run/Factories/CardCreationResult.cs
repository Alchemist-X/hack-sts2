namespace Sts2Sim.Core.Run;

public sealed partial class CardCreationResult
{
    private readonly List<string> _modifyingRelicIds = new();

    private CardInstance? _modifiedCard;

    public CardCreationResult(CardInstance card)
    {
        OriginalCard = card;
    }

    public CardInstance OriginalCard { get; private set; }

    public CardInstance Card => _modifiedCard ?? OriginalCard;

    public IReadOnlyList<string> ModifyingRelicIds => _modifyingRelicIds;

    public bool HasBeenModified => _modifiedCard != null;

    public void ModifyCard(CardInstance card, RelicModel modifyingRelic)
    {
        ArgumentNullException.ThrowIfNull(modifyingRelic);
        _modifiedCard = card;
        _modifyingRelicIds.Add(modifyingRelic.Id);
    }

    public void ModifyCard(CardInstance card) => _modifiedCard = card;

    public CardCreationResult CloneFor(PlayerState? owner)
    {
        var clone = new CardCreationResult(OriginalCard.CloneFor(owner));
        if (_modifiedCard != null)
        {
            clone._modifiedCard = ReferenceEquals(_modifiedCard, OriginalCard) ? clone.OriginalCard : _modifiedCard.CloneFor(owner);
        }
        clone._modifyingRelicIds.AddRange(_modifyingRelicIds);
        return clone;
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        OriginalCard.WriteCanonical(w);
        w.WriteBool(_modifiedCard != null);
        _modifiedCard?.WriteCanonical(w);
        w.WriteCount(_modifyingRelicIds.Count);
        for (int i = 0; i < _modifyingRelicIds.Count; i++)
        {
            w.WriteString(_modifyingRelicIds[i]);
        }
    }
}
