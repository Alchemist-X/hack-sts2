namespace Sts2Sim.Core.Relics;

public sealed class ToxicEgg : RelicModel
{
    public const string RelicId = "TOXIC_EGG";

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
        MoltenEgg.UpgradeValidCards(rs, cardRewardOptions, CardType.Skill, this);
        return true;
    }

    public override void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)
    {
        if (ReferenceEquals(player, Owner))
        {
            MoltenEgg.UpgradeValidCards(rs, cards, CardType.Skill, this);
        }
    }

    public override bool TryModifyCardBeingAddedToDeck(RunState rs, CardInstance card, out CardInstance? newCard)
    {
        newCard = null;
        if (!card.HasOwner || !ReferenceEquals(card.Owner, Owner))
        {
            return false;
        }
        if (card.Type != CardType.Skill)
        {
            return false;
        }
        if (!card.IsUpgradable)
        {
            return false;
        }
        newCard = MoltenEgg.CloneRunCard(rs, card);
        CardCmd.Upgrade(rs.Combat, newCard);
        return true;
    }
}
