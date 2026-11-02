namespace Sts2Sim.Core.Relics;

public sealed class ScrollBoxes : RelicModel
{
    public const string RelicId = "SCROLL_BOXES";

    public const string DefectCharacterId = "DEFECT";

    public const string ClawCardId = "CLAW";

    private const int _clawBundleChancePercent = 1;

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool IsAllowedAtNeow(RunState rs, PlayerState player)
    {
        if (CanGenerateBundles(rs, player))
        {
            return base.IsAllowedAtNeow(rs, player);
        }
        return false;
    }

    public override void AfterObtained(RunState rs)
    {
        var val = GenerateRandomBundles(rs, Owner);
        var val2 = new List<IReadOnlyList<CardInstance>>();
        foreach (var current in val)
        {
            val2.Add(current.Select(c => CardFactory.CreateRunCard(rs, c, Owner)).ToList());
        }
        var enumerable = RelicDeckPromptsR91.FromChooseABundleScreen(rs, val2, RelicId);
        foreach (var current2 in enumerable)
        {
            CardPileCmd.Add(rs, current2, PileType.Deck);
        }
    }

    public static bool CanGenerateBundles(RunState rs, PlayerState player)
    {
        var unlockedCards = GetCardPool(player).GetUnlockedCards(rs.Unlocks, CardPools.MultiplayerConstraintFor(rs));
        int num = unlockedCards.Count(c => c.Rarity == CardRarity.Common);
        int num2 = unlockedCards.Count(c => c.Rarity == CardRarity.Uncommon);
        if (num >= 4)
        {
            return num2 >= 2;
        }
        return false;
    }

    public static List<IReadOnlyList<CardDef>> GenerateRandomBundles(RunState rs, PlayerState player)
    {
        var rewards = (player.PlayerRng ?? throw new InvalidOperationException("ScrollBoxes: player has no PlayerRng")).Rewards;
        bool flag = player.CharacterId == DefectCharacterId;
        var cardPool = GetCardPool(player);
        var options = CardCreationOptions.ForNonCombatWithUniformOdds(new[] { cardPool }, c => c.Rarity == CardRarity.Common).WithFlags(CardCreationFlags.NoRarityModification);
        options = Hook.ModifyCardRewardCreationOptions(rs, player, options);
        var options2 = CardCreationOptions.ForNonCombatWithUniformOdds(new[] { cardPool }, c => c.Rarity == CardRarity.Uncommon).WithFlags(CardCreationFlags.NoRarityModification);
        options2 = Hook.ModifyCardRewardCreationOptions(rs, player, options2);
        var val = options.GetPossibleCards(rs, player).ToList();
        var val2 = options2.GetPossibleCards(rs, player).ToList();
        var val3 = new List<IReadOnlyList<CardDef>>();
        var usedCardIds = new HashSet<string>(StringComparer.Ordinal);
        for (int num = 0; num < 2; num++)
        {
            if (flag && rewards.NextInt(100) < _clawBundleChancePercent)
            {
                var cardModel = CardRegistry.Get(ClawCardId);
                val3.Add(new[] { cardModel, cardModel, cardModel });
                continue;
            }
            var val4 = new List<CardDef>();
            var val5 = val.Where(c => !usedCardIds.Contains(c.Id)).ToList();
            for (int num2 = 0; num2 < 2; num2++)
            {
                var cardModel2 = rewards.NextItem((IReadOnlyList<CardDef>)val5)!;
                val4.Add(cardModel2);
                usedCardIds.Add(cardModel2.Id);
                val5.Remove(cardModel2);
            }
            var items = val2.Where(c => !usedCardIds.Contains(c.Id)).ToList();
            var cardModel3 = rewards.NextItem((IReadOnlyList<CardDef>)items)!;
            val4.Add(cardModel3);
            usedCardIds.Add(cardModel3.Id);
            val3.Add(val4);
        }
        return val3;
    }

    private static CardPool GetCardPool(PlayerState player)
    {
        return CardPools.ForCharacter(player.CharacterId);
    }
}
