namespace Sts2Sim.Core.Relics;

public sealed class FrozenEgg : RelicModel
{
    public const string RelicId = "FROZEN_EGG";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (creationOptions.Flags.HasFlag(CardCreationFlags.NoHookUpgrades))
        {
            return false;
        }
        UpgradeValidCards(rs, cardRewardOptions, CardType.Power, this);
        return true;
    }

    public override void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
    {
        if (ReferenceEquals(player, Owner))
        {
            UpgradeValidCards(rs, cards, CardType.Power, this);
        }
    }

    public override bool TryModifyCardBeingAddedToDeck(RunState rs, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return false;
        }
        if (card.Type != CardType.Power)
        {
            return false;
        }
        if (!card.IsUpgradable)
        {
            return false;
        }
        newCard = CloneRunCard(rs, card);
        CardCmd.Upgrade(rs.Combat, newCard);
        return true;
    }

    private static void UpgradeValidCards(RunState rs, List<CardCreationResult> cards, CardType cardType, RelicModel eggRelic)
    {
        foreach (var current in cards)
        {
            var card = current.Card;
            if (card.Type == cardType && card.IsUpgradable)
            {
                var card2 = CloneRunCard(rs, card);
                CardCmd.Upgrade(rs.Combat, card2);
                current.ModifyCard(card2, eggRelic);
            }
        }
    }

    private static CardInstance CloneRunCard(RunState rs, CardInstance card)
    {
        var clone = card.CloneForCombat(rs.AllocateCardInstanceId());
        clone.DeckVersionId = CardInstance.NoDeckVersion;
        return clone;
    }
}
