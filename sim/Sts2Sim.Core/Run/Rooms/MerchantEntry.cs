namespace Sts2Sim.Core.Run;

public abstract partial class MerchantEntry : ICanonical
{
    protected int _cost;

    protected MerchantEntry(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; protected set; }

    public virtual int Cost => _cost;

    public virtual bool IsStocked => throw new NotImplementedException($"W2 R4: {GetType().Name}.IsStocked");

    public int GetCost(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        decimal num = _cost;
        if (RoomStack.CurrentRoom(rs) is MerchantRoom)
        {
            num = Hook.ModifyMerchantPrice(rs, Player, this, _cost);
        }
        return (int)num;
    }

    public bool EnoughGold(RunState rs) => GetCost(rs) <= Player.Gold;

    public virtual void CalcCost() => throw new NotImplementedException($"W2 R4: {GetType().Name}.CalcCost");

    protected virtual void UpdateEntry(RunState rs)
    {
    }

    public void OnMerchantInventoryUpdated(RunState rs) => UpdateEntry(rs);

    public virtual bool OnTryPurchaseWrapper(RunState rs, MerchantInventory inventory) => OnTryPurchaseWrapper(rs, inventory, false);

    public bool OnTryPurchaseWrapper(RunState rs, MerchantInventory? inventory, bool ignoreCost)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!IsStocked)
        {
            return false;
        }
        if (!EnoughGold(rs) && !ignoreCost)
        {
            return false;
        }
        var (success, goldSpent) = OnTryPurchase(rs, inventory, ignoreCost);
        if (success)
        {
            if (RoomStack.CurrentRoom(rs) is MerchantRoom && Hook.ShouldRefillMerchantEntry(rs, this, Player))
            {
                RestockAfterPurchase(rs, inventory);
            }
            else
            {
                ClearAfterPurchase();
            }
            Hook.AfterItemPurchased(rs, Player, this, goldSpent);
            inventory?.UpdateEntries(rs);
        }
        return success;
    }

    protected virtual (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost) => throw new NotImplementedException($"W2 R4: {GetType().Name}.OnTryPurchase");

    protected virtual void ClearAfterPurchase() => throw new NotImplementedException($"W2 R4: {GetType().Name}.ClearAfterPurchase");

    protected virtual void RestockAfterPurchase(RunState rs, MerchantInventory? inventory) => throw new NotImplementedException($"W2 R4: {GetType().Name}.RestockAfterPurchase");

    public virtual MerchantEntry CloneFor(PlayerState player) => throw new NotImplementedException($"W2 R4: {GetType().Name}.CloneFor");

    internal virtual MerchantEntry CloneFor(PlayerState player, MerchantInventory inventory) => CloneFor(player);

    public virtual void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Merchant);
        w.WriteString(GetType().Name);
        w.WriteInt32(_cost);
        w.WriteBool(IsStocked);
        WritePayload(w);
    }

    protected virtual void WritePayload(CanonicalWriter w)
    {
    }
}

public sealed class MerchantCardEntry : MerchantEntry
{
    private MerchantInventory? _inventory;

    private readonly IReadOnlyList<CardDef> _cardPool;

    private readonly CardType? _cardType;

    private readonly CardRarity? _cardRarity;

    public MerchantCardEntry(PlayerState player, MerchantInventory? inventory, IReadOnlyList<CardDef> cardPool, CardType cardType) : base(player)
    {
        _inventory = inventory;
        _cardPool = cardPool;
        _cardType = cardType;
    }

    public MerchantCardEntry(PlayerState player, MerchantInventory? inventory, IReadOnlyList<CardDef> cardPool, CardRarity cardRarity) : base(player)
    {
        _inventory = inventory;
        _cardPool = cardPool;
        _cardRarity = cardRarity;
    }

    private MerchantCardEntry(PlayerState player, MerchantInventory? inventory, IReadOnlyList<CardDef> cardPool, CardType? cardType, CardRarity? cardRarity) : base(player)
    {
        _inventory = inventory;
        _cardPool = cardPool;
        _cardType = cardType;
        _cardRarity = cardRarity;
    }

    public CardCreationResult? CreationResult { get; private set; }

    public bool IsOnSale { get; private set; }

    public override bool IsStocked => CreationResult != null;

    public static bool IsColorless(CardInstance card) => CardPools.PoolOf(card.Id)?.IsColorless == true;

    private static int GetCost(CardInstance card)
    {
        int num = card.Rarity switch
        {
            CardRarity.Rare => 150,
            CardRarity.Uncommon => 75,
            _ => 50,
        };
        if (IsColorless(card))
        {
            num = (int)Math.Round((float)num * 1.15f);
        }
        return num;
    }

    public void Populate(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var inShop = new HashSet<CardDef>();
        if (_inventory != null)
        {
            foreach (var entry in _inventory.CardEntries)
            {
                if (entry.CreationResult != null)
                {
                    inShop.Add(entry.CreationResult.Card.Def);
                }
            }
        }
        var content = rs.Config?.Content;
        var options = _cardPool.Where(c => !inShop.Contains(c) && (content == null || content.AllowsCard(c.Id)));
        if (_cardType.HasValue)
        {
            CreationResult = CardFactory.CreateForMerchant(rs, Player, options, _cardType.Value);
        }
        else
        {
            if (!_cardRarity.HasValue)
            {
                throw new InvalidOperationException("A merchant card entry needs a card type or a rarity.");
            }
            CreationResult = CardFactory.CreateForMerchant(rs, Player, options, _cardRarity.Value);
        }
        Hook.ModifyMerchantCardCreationResults(rs, Player, new List<CardCreationResult>(1) { CreationResult });
        CalcCost();
    }

    protected override void UpdateEntry(RunState rs)
    {
        if (CreationResult != null)
        {
            Hook.ModifyMerchantCardCreationResults(rs, Player, new List<CardCreationResult>(1) { CreationResult });
        }
    }

    public void SetOnSale()
    {
        IsOnSale = true;
        CalcCost();
    }

    public override void CalcCost()
    {
        if (CreationResult == null)
        {
            throw new InvalidOperationException("There is no item to purchase.");
        }
        var rng = Player.PlayerRng ?? throw new InvalidOperationException("The merchant needs the player's rng set.");
        _cost = (int)Math.Round((float)GetCost(CreationResult.Card) * rng.Shops.NextFloat(0.95f, 1.05f));
        if (IsOnSale)
        {
            _cost /= 2;
        }
    }

    protected override (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost)
    {
        var card = CreationResult!.Card;
        if (!CardPileCmd.Add(rs, card, PileType.Deck).Success)
        {
            return (false, 0);
        }
        if (!ignoreCost)
        {
            PlayerCmd.LoseGold(rs, GetCost(rs), Player, GoldLossType.Spent);
        }
        return (true, !ignoreCost ? GetCost(rs) : 0);
    }

    protected override void ClearAfterPurchase()
    {
        CreationResult = null;
    }

    protected override void RestockAfterPurchase(RunState rs, MerchantInventory? inventory)
    {
        IsOnSale = false;
        Populate(rs);
    }

    public override MerchantEntry CloneFor(PlayerState player) => CloneFor(player, _inventory!);

    internal override MerchantEntry CloneFor(PlayerState player, MerchantInventory inventory)
    {
        var clone = new MerchantCardEntry(player, inventory, _cardPool, _cardType, _cardRarity)
        {
            IsOnSale = IsOnSale,
            _cost = _cost,
        };
        if (CreationResult != null)
        {
            clone.CreationResult = new CardCreationResult(CreationResult.Card.CloneFor(player));
        }
        return clone;
    }

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteNullableInt32(_cardType.HasValue ? (int)_cardType.Value : null);
        w.WriteNullableInt32(_cardRarity.HasValue ? (int)_cardRarity.Value : null);
        w.WriteBool(IsOnSale);
        var result = CreationResult;
        w.WriteBool(result != null);
        result?.Card.WriteCanonical(w);
    }
}

public sealed class MerchantRelicEntry : MerchantEntry
{
    public MerchantRelicEntry(RunState rs, RelicRarity rarity, PlayerState player) : base(player)
    {
        FillSlot(rs, rarity, null);
    }

    public MerchantRelicEntry(RelicModel relic, PlayerState player) : base(player)
    {
        SetModel(relic);
    }

    private MerchantRelicEntry(PlayerState player) : base(player)
    {
    }

    public RelicModel? Model { get; private set; }

    public override bool IsStocked => Model != null;

    public static bool IsAllowedInShops(string relicId) => !RelicRegistry.Models.Contains(relicId) || RelicRegistry.Create(relicId).IsAllowedInShops;

    private void FillSlot(RunState rs, RelicRarity rarity, HashSet<string>? blacklist)
    {
        SetModel(RelicFactory.PullNextRelicFromBack(rs, Player, rarity, id => (blacklist == null || !blacklist.Contains(id)) && IsAllowedInShops(id)));
    }

    public override void CalcCost()
    {
        var rng = Player.PlayerRng ?? throw new InvalidOperationException("The merchant needs the player's rng set.");
        _cost = (int)Math.Round((double)((float)Model!.MerchantCost * rng.Shops.NextFloat(0.85f, 1.15f)));
    }

    private void SetModel(RelicModel model)
    {
        Model = model;
        CalcCost();
    }

    protected override (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost)
    {
        if (!ignoreCost)
        {
            PlayerCmd.LoseGold(rs, GetCost(rs), Player, GoldLossType.Spent);
        }
        RelicCmd.Obtain(rs, Model!, Player);
        return (true, !ignoreCost ? GetCost(rs) : 0);
    }

    protected override void ClearAfterPurchase()
    {
        Model = null;
    }

    protected override void RestockAfterPurchase(RunState rs, MerchantInventory? inventory)
    {
        var blacklist = new HashSet<string>(StringComparer.Ordinal);
        if (inventory != null)
        {
            var entries = inventory.RelicEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Model != null)
                {
                    blacklist.Add(entries[i].Model!.Id);
                }
            }
        }
        FillSlot(rs, RelicFactory.RollRarity(Player), blacklist);
    }

    public override MerchantEntry CloneFor(PlayerState player) => new MerchantRelicEntry(player)
    {
        _cost = _cost,
        Model = Model?.CloneFor(null),
    };

    protected override void WritePayload(CanonicalWriter w)
    {
        var model = Model;
        w.WriteBool(model != null);
        model?.WriteCanonical(w);
    }
}

public sealed class MerchantPotionEntry : MerchantEntry
{
    public MerchantPotionEntry(PotionModel potion, PlayerState player) : base(player)
    {
        Model = potion;
        CalcCost();
    }

    public MerchantPotionEntry(RunState rs, PlayerState player) : base(player)
    {
        FillSlot(rs);
    }

    private MerchantPotionEntry(PlayerState player) : base(player)
    {
    }

    public PotionModel? Model { get; private set; }

    public override bool IsStocked => Model != null;

    private void FillSlot(RunState rs)
    {
        var rng = Player.PlayerRng ?? throw new InvalidOperationException("The merchant needs the player's rng set.");
        Model = PotionRegistry.Create(PotionFactory.CreateRandomPotionOutOfCombat(rs, Player, rng.Shops, Array.Empty<CanonicalPotion>()).Id);
        CalcCost();
    }

    private static int GetCost(PotionRarity rarity) => rarity switch
    {
        PotionRarity.Rare => 100,
        PotionRarity.Uncommon => 75,
        _ => 50,
    };

    public override void CalcCost()
    {
        if (Model == null)
        {
            throw new InvalidOperationException("There is no item to purchase.");
        }
        var rng = Player.PlayerRng ?? throw new InvalidOperationException("The merchant needs the player's rng set.");
        _cost = GetCost(Model.Rarity);
        _cost = (int)MathF.Round((float)_cost * rng.Shops.NextFloat(0.95f, 1.05f));
    }

    protected override (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost)
    {
        if (Model == null)
        {
            throw new InvalidOperationException("There is no item to purchase.");
        }
        if (!PotionCmd.TryToProcure(rs, Model, Player).Success)
        {
            return (false, 0);
        }
        if (!ignoreCost)
        {
            PlayerCmd.LoseGold(rs, GetCost(rs), Player, GoldLossType.Spent);
        }
        return (true, !ignoreCost ? GetCost(rs) : 0);
    }

    protected override void ClearAfterPurchase()
    {
        Model = null;
    }

    protected override void RestockAfterPurchase(RunState rs, MerchantInventory? inventory)
    {
        FillSlot(rs);
    }

    public override MerchantEntry CloneFor(PlayerState player) => new MerchantPotionEntry(player)
    {
        _cost = _cost,
        Model = Model?.CloneFor(null),
    };

    protected override void WritePayload(CanonicalWriter w)
    {
        var model = Model;
        w.WriteBool(model != null);
        model?.WriteCanonical(w);
    }
}

public sealed class MerchantCardRemovalEntry : MerchantEntry
{
    public MerchantCardRemovalEntry(RunState rs, PlayerState player) : base(player)
    {
        CalcCost(rs.Ascension);
    }

    private MerchantCardRemovalEntry(PlayerState player) : base(player)
    {
    }

    public bool Used { get; private set; }

    public override bool IsStocked => !Used;

    private int _ascension;

    public static int BaseCost(int ascension) => ascension >= (int)AscensionLevel.Inflation ? 100 : 75;

    public static int PriceIncrease(int ascension) => ascension >= (int)AscensionLevel.Inflation ? 50 : 25;

    public override void CalcCost() => CalcCost(_ascension);

    private void CalcCost(int ascension)
    {
        _ascension = ascension;
        _cost = BaseCost(ascension) + PriceIncrease(ascension) * Player.ExtraFields.CardShopRemovalsUsed;
    }

    public override bool OnTryPurchaseWrapper(RunState rs, MerchantInventory inventory) => OnTryPurchaseWrapper(rs, inventory, false, true);

    public bool OnTryPurchaseWrapper(RunState rs, MerchantInventory? inventory, bool ignoreCost, bool cancelable)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!EnoughGold(rs) && !ignoreCost)
        {
            return false;
        }
        var (success, goldSpent) = OnTryPurchase(rs, inventory, ignoreCost, cancelable);
        if (success)
        {
            Hook.AfterItemPurchased(rs, Player, this, goldSpent);
            inventory?.UpdateEntries(rs);
        }
        return success;
    }

    protected override (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost) => OnTryPurchase(rs, inventory, ignoreCost, true);

    private (bool Success, int GoldSpent) OnTryPurchase(RunState rs, MerchantInventory? inventory, bool ignoreCost, bool cancelable)
    {
        if (Used)
        {
            return (false, 0);
        }
        int goldToSpend = !ignoreCost ? GetCost(rs) : 0;
        bool flag = DoMerchantCardRemoval(rs, Player, goldToSpend, cancelable);
        if (flag)
        {
            SetUsed();
        }
        return (flag, goldToSpend);
    }

    public static bool DoMerchantCardRemoval(RunState rs, PlayerState player, int goldCost, bool cancelable = true)
    {
        var prefs = new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt, 1)
        {
            Cancelable = cancelable,
            RequireManualConfirmation = true,
        };
        var selected = RoomPrompts.FromDeckForRemoval(rs, player, prefs, RoomPrompts.MerchantRemovalSite, "MERCHANT_CARD_REMOVAL");
        var card = selected.Count > 0 ? selected[0] : null;
        if (card != null)
        {
            PlayerCmd.LoseGold(rs, goldCost, player, GoldLossType.Spent);
            CardPileCmd.RemoveFromDeck(rs, card);
            player.ExtraFields.CardShopRemovalsUsed++;
        }
        return card != null;
    }

    protected override void ClearAfterPurchase()
    {
    }

    protected override void RestockAfterPurchase(RunState rs, MerchantInventory? inventory)
    {
    }

    public void SetUsed()
    {
        Used = true;
    }

    public override MerchantEntry CloneFor(PlayerState player) => new MerchantCardRemovalEntry(player)
    {
        _cost = _cost,
        _ascension = _ascension,
        Used = Used,
    };

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteInt32(_ascension);
        w.WriteBool(Used);
    }
}
