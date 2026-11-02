namespace Sts2Sim.Core.Relics;

public sealed class FresnelLens : RelicModel
{
    public const string RelicId = "FRESNEL_LENS";

    public const string NimbleAmountKey = "NimbleAmount";

    private static readonly Nimble CanonicalNimble = new();

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Event;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new DynamicVar(NimbleAmountKey, 2m)
    };

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        EnchantValidCards(rs, cardRewardOptions);
        return true;
    }

    public override void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
    {
        if (ReferenceEquals(player, Owner))
        {
            EnchantValidCards(rs, cards);
        }
    }

    public override bool TryModifyCardBeingAddedToDeck(RunState rs, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return false;
        }
        if (!CanonicalNimble.CanEnchant(card))
        {
            return false;
        }
        newCard = EnchantCard(rs, card);
        return true;
    }

    private void EnchantValidCards(RunState rs, List<CardCreationResult> options)
    {
        foreach (var current in options)
        {
            var card = current.Card;
            if (CanonicalNimble.CanEnchant(card))
            {
                current.ModifyCard(EnchantCard(rs, card), this);
            }
        }
    }

    private CardInstance EnchantCard(RunState rs, CardInstance card)
    {
        var cardModel = CloneRunCard(rs, card);
        CardCmd.Enchant<Nimble>(cardModel, DynamicVars[NimbleAmountKey].BaseValue);
        return cardModel;
    }

    private static CardInstance CloneRunCard(RunState rs, CardInstance card)
    {
        var clone = card.CloneForCombat(rs.AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
    }
}
