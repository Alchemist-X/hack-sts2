namespace Sts2Sim.Core.Cards;

public static partial class CardFactory
{
    public const decimal MerchantUpgradeBaseChance = -999999999m;

    public static decimal UpgradedCardOddScaling(RunState rs) => rs.Ascension >= (int)AscensionLevel.Scarcity ? 0.125m : 0.25m;

    public static CardCreationResult CreateForMerchant(RunState rs, PlayerState player, IEnumerable<CardDef> options, CardType type)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(options);
        options = Hook.ModifyMerchantCardPool(rs, player, options);
        options = options.Where(c => c.Rarity != CardRarity.Basic);
        options = FilterForPlayerCount(rs, options);
        CardDef[] optionsArr = options.ToArray();
        var odds = RequireOdds(player);
        CardRarity rarity = Hook.ModifyMerchantCardRarity(rs, player, odds.CardRarity.RollWithoutChangingFutureOdds(CardRarityOddsType.Shop));
        CardRarity rarity2 = GetNextAllowedRarity(rarity, r => optionsArr.Any(c => c.Rarity == r && c.Type == type));
        if (rarity2 == CardRarity.None)
        {
            throw new InvalidOperationException($"Can't generate valid rarity for merchant card type {type} with card options: {string.Join(",", optionsArr.Select(c => c.Id))}");
        }
        var items = optionsArr.Where(c => c.Rarity == rarity2 && c.Type == type).ToList();
        var rng = RequireRng(player);
        var cardModel = CreateRunCard(rs, rng.Shops.NextItem((IEnumerable<CardDef>)items), player);
        RollForUpgrade(rs, player, cardModel, MerchantUpgradeBaseChance, rng.Rewards);
        return new CardCreationResult(cardModel);
    }

    public static CardCreationResult CreateForMerchant(RunState rs, PlayerState player, IEnumerable<CardDef> options, CardRarity rarity)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(options);
        options = Hook.ModifyMerchantCardPool(rs, player, options);
        options = options.Where(c => c.Rarity != CardRarity.Basic);
        options = FilterForPlayerCount(rs, options);
        CardDef[] array = options.ToArray();
        CardRarity modifiedRarity = Hook.ModifyMerchantCardRarity(rs, player, rarity);
        var items = array.Where(c => c.Rarity == modifiedRarity);
        var rng = RequireRng(player);
        var cardModel = CreateRunCard(rs, rng.Shops.NextItem(items), player);
        RollForUpgrade(rs, player, cardModel, MerchantUpgradeBaseChance, rng.Rewards);
        return new CardCreationResult(cardModel);
    }

    public static IEnumerable<CardCreationResult> CreateForReward(RunState rs, PlayerState player, int cardCount, CardCreationOptions options)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(options);
        var val = new List<CardDef>();
        var val2 = new List<CardCreationResult>();
        for (int i = 0; i < cardCount; i++)
        {
            var cardModel = CreateForReward(rs, player, val, options);
            val.Add(cardModel.Def);
            val2.Add(new CardCreationResult(cardModel));
            if (!options.Flags.HasFlag(CardCreationFlags.NoUpgradeRoll))
            {
                Rng rng = options.RngOverride ?? RequireRng(player).Rewards;
                RollForUpgrade(rs, player, cardModel, 0m, rng);
            }
        }
        if (!options.Flags.HasFlag(CardCreationFlags.NoModifyHooks))
        {
            List<AbstractModel>? modifiers = null;
            if (Hook.TryModifyCardRewardOptions(rs, player, val2, options, ref modifiers))
            {
                Hook.AfterModifyingCardRewardOptions(rs, modifiers);
            }
        }
        return val2;
    }

    public static CardInstance CreateRunCard(RunState rs, CardDef? def, PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(player);
        var card = new CardInstance(def, rs.AllocateCardInstanceId());
        card.AttachTo(player);
        def.AfterCreated(card);
        return card;
    }

    public static CardRarity GetNextHighestRarityWithWrapping(CardRarity cardRarity) => cardRarity switch
    {
        CardRarity.None => CardRarity.None,
        CardRarity.Basic => CardRarity.Common,
        CardRarity.Common => CardRarity.Uncommon,
        CardRarity.Uncommon => CardRarity.Rare,
        CardRarity.Rare => CardRarity.Common,
        CardRarity.Status => CardRarity.None,
        CardRarity.Curse => CardRarity.None,
        CardRarity.Event => CardRarity.None,
        CardRarity.Token => CardRarity.None,
        CardRarity.Quest => CardRarity.None,
        CardRarity.Ancient => CardRarity.None,
        _ => throw new ArgumentOutOfRangeException(nameof(cardRarity), cardRarity, null),
    };

    private static CardInstance CreateForReward(RunState rs, PlayerState player, IEnumerable<CardDef> blacklist, CardCreationOptions options)
    {
        options = Hook.ModifyCardRewardCreationOptions(rs, player, options);
        IEnumerable<CardDef> options2 = options.GetPossibleCards(rs, player).Except(blacklist).ToList();
        options2 = FilterForPlayerCount(rs, options2).ToArray();
        CardRarity? selectedRarity = null;
        IEnumerable<CardDef> items;
        if (options.RarityOdds == CardRarityOddsType.Uniform)
        {
            items = options2.Where(c => c.Rarity != CardRarity.Basic && c.Rarity != CardRarity.Ancient);
        }
        else
        {
            var allowedRarities = options2.Select(c => c.Rarity).ToHashSet();
            selectedRarity = RollForRarity(player, options.RarityOdds, options.Source, allowedRarities, options.Flags.HasFlag(CardCreationFlags.ForceRarityOddsChange));
            if (selectedRarity == CardRarity.None)
            {
                throw new InvalidOperationException($"Tried to create a card for a reward, but we couldn't generate a valid rarity! Odds: {options.RarityOdds} Card pool: {string.Join(",", options2.Select(c => c.Id))}, blacklist: {string.Join(",", blacklist.Select(c => c.Id))}");
            }
            items = options2.Where(card => card.Rarity == selectedRarity);
        }
        Rng rng = options.RngOverride ?? RequireRng(player).Rewards;
        var cardModel = rng.NextItem(items);
        if (cardModel == null)
        {
            throw new InvalidOperationException($"Tried to create a card for a reward, but we couldn't generate a valid card! Selected rarity: {selectedRarity}, card pool: {string.Join(",", options2.Select(c => c.Id))}, blacklist: {string.Join(",", blacklist.Select(c => c.Id))}, odds: {options.RarityOdds}");
        }
        return CreateRunCard(rs, cardModel, player);
    }

    private static CardRarity RollForRarity(PlayerState player, CardRarityOddsType rollMethod, CardCreationSource source, HashSet<CardRarity> allowedRarities, bool forceRarityOddsChange)
    {
        bool flag2 = forceRarityOddsChange || (source == CardCreationSource.Encounter && (uint)(rollMethod - 1) <= 2u);
        var odds = RequireOdds(player).CardRarity;
        CardRarity rarity = !flag2 ? odds.RollWithBaseOdds(rollMethod) : odds.Roll(rollMethod);
        return GetNextAllowedRarity(rarity, allowedRarities.Contains);
    }

    private static CardRarity GetNextAllowedRarity(CardRarity rarity, Func<CardRarity, bool> isAllowed)
    {
        var val = new List<CardRarity>(1) { rarity };
        while (!isAllowed(rarity) && rarity != CardRarity.None)
        {
            rarity = GetNextHighestRarityWithWrapping(rarity);
            if (val.Contains(rarity))
            {
                return CardRarity.None;
            }
        }
        return rarity;
    }

    private static void RollForUpgrade(RunState rs, PlayerState player, CardInstance card, decimal baseChance, Rng rng)
    {
        decimal num = (decimal)rng.NextFloat();
        if (card.IsUpgradable)
        {
            decimal num2 = baseChance;
            if (card.Rarity != CardRarity.Rare)
            {
                int currentActIndex = rs.CurrentActIndex;
                num2 += currentActIndex * UpgradedCardOddScaling(rs);
            }
            num2 = Hook.ModifyCardRewardUpgradeOdds(rs, player, card, num2);
            if (num <= num2)
            {
                CardCmd.Upgrade(rs.Combat, card);
            }
        }
    }

    private static PlayerOddsSet RequireOdds(PlayerState player)
        => player.PlayerOdds ?? throw new InvalidOperationException("Card rewards need the player's odds (run state).");

    private static PlayerRngSet RequireRng(PlayerState player)
        => player.PlayerRng ?? throw new InvalidOperationException("Card rewards need the player's rng set (run state).");
}
