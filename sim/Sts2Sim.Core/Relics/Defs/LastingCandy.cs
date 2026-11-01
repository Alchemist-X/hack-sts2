namespace Sts2Sim.Core.Relics;

public sealed class LastingCandy : RelicModel
{
    public const string RelicId = "LASTING_CANDY";

    private int _combatRewardsSeen;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Uncommon;

    public override bool ShowCounter => true;

    public override int DisplayAmount => CombatRewardsSeen % 2;

    public int CombatRewardsSeen
    {
        get => _combatRewardsSeen;
        set => _combatRewardsSeen = value;
    }

    private bool IsInTriggeringCombat
    {
        get
        {
            if (CombatRewardsSeen > 0)
            {
                return CombatRewardsSeen % 2 == 1;
            }
            return false;
        }
    }

    public override bool IsAllowed(RunState rs)
    {
        if (rs.Players.Any(p => p != null && string.Equals(p.CharacterId, CardPools.IroncladCharacterId, StringComparison.Ordinal) && rs.Unlocks.NumberOfRuns == 0))
        {
            return false;
        }
        return IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);
    }

    public override bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)
    {
        if (!ReferenceEquals(Owner, player))
        {
            return false;
        }
        if (creationOptions.Source != CardCreationSource.Encounter)
        {
            return false;
        }
        if (!IsInTriggeringCombat)
        {
            return false;
        }
        if (!creationOptions.Flags.HasFlag(CardCreationFlags.IsCardReward))
        {
            return false;
        }
        if (!creationOptions.Flags.HasFlag(CardCreationFlags.IsFromCombat))
        {
            return false;
        }
        bool allowDupes = false;
        var val = creationOptions.GetPossibleCards(rs, player).ToList();
        IEnumerable<CardDef> enumerable = val.Where(c => CardPoolFilter(c, cardRewardOptions, allowDupes: false));
        if (!enumerable.Any())
        {
            allowDupes = true;
            enumerable = val.Where(c => CardPoolFilter(c, cardRewardOptions, allowDupes: true));
        }
        if (!enumerable.Any())
        {
            return false;
        }
        var cardPoolFilter = creationOptions.CardPoolFilter;
        var options = new CardCreationOptions(creationOptions.CardPools, CardCreationSource.Other, creationOptions.RarityOdds, c =>
            (cardPoolFilter == null || cardPoolFilter(c)) && CardPoolFilter(c, cardRewardOptions, allowDupes))
            .WithFlags(CardCreationFlags.NoModifyHooks | CardCreationFlags.NoCardPoolModifications);
        var cardModel = CardFactory.CreateForReward(rs, Owner, 1, options).FirstOrDefault()?.Card;
        if (cardModel != null)
        {
            var cardCreationResult = new CardCreationResult(cardModel);
            cardCreationResult.ModifyCard(cardModel, this);
            cardRewardOptions.Add(cardCreationResult);
        }
        return cardModel != null;
    }

    public override void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)
    {
        if (!ReferenceEquals(rewards.Player, Owner))
        {
            return;
        }
        if (rewards.Rewards.All(r => r is not CardReward))
        {
            return;
        }
        CombatRewardsSeen++;
    }

    private static bool CardPoolFilter(CardDef card, List<CardCreationResult> rewardOptions, bool allowDupes)
    {
        if (card.Type == CardType.Power)
        {
            if (!allowDupes)
            {
                return rewardOptions.TrueForAll(o => !string.Equals(o.OriginalCard.Id, card.Id, StringComparison.Ordinal));
            }
            return true;
        }
        return false;
    }

    protected override void WriteExtraCanonical(CanonicalWriter w)
    {
        w.WriteInt32(_combatRewardsSeen);
    }
}
