namespace Sts2Sim.Core.Run;

public sealed class WelcomeToWongos : EventModel
{
    public const string EventId = "WELCOME_TO_WONGOS";

    public const string MysteryTicketId = "WONGOS_MYSTERY_TICKET";

    public const string BadgeId = "WONGO_CUSTOMER_APPRECIATION_BADGE";

    public const int WongoPointsForBadge = 2000;

    public const int BargainBinCost = 100;

    public const int MysteryBoxCost = 300;

    public const int FeaturedItemCost = 200;

    public const int MysteryBoxRelicCount = 3;

    public const int MysteryBoxCombatCount = 5;

    public const int BargainBinPoints = 32;

    public const int FeaturedItemPoints = 16;

    public const int MysteryBoxPoints = 8;

    private string? _featuredItem;

    private int _wongoPointAmount;

    private int _remainingWongoPointAmount;

    private int _totalWongoBadgeAmount;

    public override string Id => EventId;

    public string? FeaturedItemId => _featuredItem;

    // Option-text var {RandomRelic} of FEATURED_ITEM (Models.Events/WelcomeToWongos.cs: the featured relic's title).
    public string? RandomRelic => _featuredItem;

    public override bool IsAllowed(RunState runState)
    {
        if (runState.CurrentActIndex == 1)
        {
            return runState.Players.All(p => p.Gold >= BargainBinCost);
        }
        return false;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = Owner!;
        _featuredItem = RelicFactory.PullNextRelicIdFromFront(rs, owner, RelicRarity.Rare, IsAllowedInShops);
        var options = new List<EventOption>(4);
        if (owner.Gold >= BargainBinCost)
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN", BuyBargainBin));
        }
        else
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN_LOCKED", null));
        }
        if (owner.Gold >= FeaturedItemCost)
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM", BuyFeaturedItem));
        }
        else
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM_LOCKED", null));
        }
        if (owner.Gold >= MysteryBoxCost)
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX", BuyMysteryBox));
        }
        else
        {
            options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX_LOCKED", null));
        }
        options.Add(new EventOption("WELCOME_TO_WONGOS.pages.INITIAL.options.LEAVE", Leave));
        return options;
    }

    private static bool IsAllowedInShops(string relicId) => !RelicRegistry.Models.Contains(relicId) || RelicRegistry.Create(relicId).IsAllowedInShops;

    private void BuyBargainBin(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, BargainBinCost, owner, GoldLossType.Spent);
        var relic = RelicFactory.PullNextRelicFromFront(rs, owner, RelicRarity.Common, IsAllowedInShops);
        RelicCmd.Obtain(rs, relic, owner);
        SetEventFinished(CheckObtainWongoBadge(rs, BargainBinPoints));
    }

    private void BuyFeaturedItem(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, FeaturedItemCost, owner, GoldLossType.Spent);
        RelicCmd.Obtain(rs, RelicRegistry.Create(_featuredItem!), owner);
        SetEventFinished(CheckObtainWongoBadge(rs, FeaturedItemPoints));
    }

    private void BuyMysteryBox(RunState rs)
    {
        var owner = Owner!;
        PlayerCmd.LoseGold(rs, MysteryBoxCost, owner, GoldLossType.Spent);
        Events121Support.ObtainRelic(rs, MysteryTicketId, owner);
        SetEventFinished(CheckObtainWongoBadge(rs, MysteryBoxPoints));
    }

    private string CheckObtainWongoBadge(RunState rs, int pointsEarned)
    {
        int wongoPoints = (rs.Config?.Progress ?? RunProfileProgress.Veteran).WongoPoints;
        int num = wongoPoints % WongoPointsForBadge;
        int num2 = num + pointsEarned;
        int num3 = wongoPoints + pointsEarned;
        _wongoPointAmount = num2;
        _remainingWongoPointAmount = WongoPointsForBadge - num2;
        _totalWongoBadgeAmount = num3 / WongoPointsForBadge;
        Owner!.ExtraFields.WongoPoints = pointsEarned;
        if (num2 >= WongoPointsForBadge)
        {
            Events121Support.ObtainRelic(rs, BadgeId, Owner!);
            return "WELCOME_TO_WONGOS.pages.AFTER_BUY_RECEIVE_BADGE.description";
        }
        if (_totalWongoBadgeAmount > 0)
        {
            return "WELCOME_TO_WONGOS.pages.AFTER_BUY_BADGE_COUNTER.description";
        }
        return "WELCOME_TO_WONGOS.pages.AFTER_BUY.description";
    }

    private void Leave(RunState rs)
    {
        var owner = Owner!;
        var card = Rng!.NextItem(owner.Deck.Where(c => c.IsUpgraded));
        if (card != null)
        {
            CardCmd.Downgrade(rs.Combat, card);
        }
        SetEventFinished("WELCOME_TO_WONGOS.pages.LEAVE.description");
    }

    protected override void WriteEventFields(CanonicalWriter w)
    {
        w.WriteString(_featuredItem);
        w.WriteInt32(_wongoPointAmount);
        w.WriteInt32(_remainingWongoPointAmount);
        w.WriteInt32(_totalWongoBadgeAmount);
    }
}
