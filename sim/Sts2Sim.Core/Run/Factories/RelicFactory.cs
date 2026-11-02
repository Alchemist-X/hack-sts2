namespace Sts2Sim.Core.Run;

public static class RelicFactory
{
    public const string FallbackRelicId = "CIRCLET";

    public const float CommonThreshold = 0.5f;

    public const float UncommonThreshold = 0.83f;

    private static readonly Func<string, bool> AcceptAll = static _ => true;

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, Rng rng) => PullNextRelicFromFront(rs, player, RollRarity(rng), AcceptAll);

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player) => PullNextRelicFromFront(rs, player, RollRarity(player), AcceptAll);

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, RelicRarity rarity) => PullNextRelicFromFront(rs, player, rarity, AcceptAll);

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter)
    {
        string relicId = PullNextRelicIdFromFront(rs, player, rarity, filter);
        return RelicRegistry.Create(relicId);
    }

    public static string PullNextRelicIdFromFront(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var bag = RequirePlayerBag(player);
        string relicId = bag.PullFromFront(rarity, filter, rs) ?? FallbackRelicId;
        rs.SharedRelicGrabBag?.Remove(relicId);
        return relicId;
    }

    public static RelicModel PullNextRelicFromBack(RunState rs, PlayerState player) => PullNextRelicFromBack(rs, player, RollRarity(player), AcceptAll);

    public static RelicModel PullNextRelicFromBack(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter)
    {
        string relicId = PullNextRelicIdFromBack(rs, player, rarity, filter);
        return RelicRegistry.Create(relicId);
    }

    public static string PullNextRelicIdFromBack(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var bag = RequirePlayerBag(player);
        string relicId = bag.PullFromBack(rarity, filter, rs) ?? FallbackRelicId;
        rs.SharedRelicGrabBag?.Remove(relicId);
        return relicId;
    }

    public static RelicRarity RollRarity(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var rng = player.PlayerRng ?? throw new InvalidOperationException("RelicFactory.RollRarity needs the player's rng set.");
        return RollRarity(rng.Rewards);
    }

    public static RelicRarity RollRarity(Rng rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        float num = rng.NextFloat();
        return num < CommonThreshold ? RelicRarity.Common : (!(num < UncommonThreshold) ? RelicRarity.Rare : RelicRarity.Uncommon);
    }

    private static RelicGrabBag RequirePlayerBag(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return player.RelicGrabBag ?? throw new InvalidOperationException("RelicFactory needs the player's relic grab bag (run state).");
    }
}
