namespace Sts2Sim.Core.Relics;

public sealed class PaelsTooth : RelicModel
{
    public const string RelicId = "PAELS_TOOTH";

    public const int cardsCount = 5;

    private const string _cardTitlesKey = "CardTitles";

    private List<CardInstance> _serializableCards = new();

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool ShowCounter => HasOwner && _serializableCards.Count > 0;

    public override int DisplayAmount => HasOwner ? _serializableCards.Count : 0;

    public override bool HasUponPickupEffect => true;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(cardsCount),
        new StringVar(_cardTitlesKey)
    };

    public IReadOnlyList<CardInstance> SerializableCards => _serializableCards;

    public override void AfterObtained(RunState rs)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, DynamicVars.Cards.IntValue);
        var result = RelicDeckPromptsR91.FromDeckForRemoval(rs, Owner, prefs, RelicId, c => c.IsUpgradable);
        var enumerable = result.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        foreach (var current in enumerable)
        {
            _serializableCards.Add(current.CloneFor(Owner));
            CardPileCmd.RemoveFromDeck(rs, current);
        }
        UpdateCardList();
    }

    public override void AfterCombatEnd(RunState rs, CombatRoom? room)
    {
        if (Owner.IsDead || _serializableCards.Count == 0)
        {
            return;
        }
        var rng = Owner.PlayerRng ?? throw new InvalidOperationException("PaelsTooth: player has no PlayerRng");
        var serializableCard = rng.Rewards.NextItem((IReadOnlyList<CardInstance>)_serializableCards)!;
        var cardModel = RelicDeckPromptsR91.CloneCard(rs, serializableCard);
        if (cardModel.IsUpgradable)
        {
            CardCmd.Upgrade(null, cardModel);
        }
        CardPileCmd.Add(rs, cardModel, PileType.Deck);
        Status = _serializableCards.Count <= 0 ? RelicStatus.Disabled : RelicStatus.Normal;
        _serializableCards.Remove(serializableCard);
        UpdateCardList();
    }

    private void UpdateCardList()
    {
        Status = _serializableCards.Count <= 0 ? RelicStatus.Disabled : RelicStatus.Normal;
        var stringVar = (StringVar)DynamicVars[_cardTitlesKey];
        stringVar.StringValue = _serializableCards.Count == 0 ? string.Empty : string.Join('\n', _serializableCards.Select(c => "- " + c.Id));
    }

    protected override void CopyMutableReferenceState(RelicModel source)
    {
        var src = (PaelsTooth)source;
        var owner = HasOwner ? Owner : null;
        _serializableCards = src._serializableCards.Select(c => c.CloneFor(owner)).ToList();
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_serializableCards.Count);
        foreach (var card in _serializableCards)
        {
            card.WriteCanonical(w);
        }
    }
}
