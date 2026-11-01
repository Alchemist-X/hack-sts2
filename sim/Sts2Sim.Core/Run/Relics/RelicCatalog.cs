using System.Collections.Frozen;

namespace Sts2Sim.Core.Run;

public static partial class RelicCatalog
{
    private const int MultiplayerAct3TreasureFloor = 38;

    private const int SingleplayerAct3TreasureFloor = 41;

    private static readonly Lazy<FrozenDictionary<string, RelicRarity>> Rarities = new(() =>
        RarityRows!.ToFrozenDictionary(r => r.Id, r => r.Rarity, StringComparer.Ordinal));

    private static readonly FrozenSet<string> BeforeAct3TreasureChestIds = new[]
    {
        "AMETHYST_AUBERGINE", "BOOK_OF_FIVE_RINGS", "BOWLER_HAT", "DRAGON_FRUIT", "FROZEN_EGG", "GIRYA",
        "JUZU_BRACELET", "LUCKY_FYSH", "MEAL_TICKET", "MOLTEN_EGG", "OLD_COIN", "PLANISPHERE", "SHOVEL",
        "TOXIC_EGG", "WHITE_STAR", "WHITE_BEAST_STATUE",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Lazy<Dictionary<string, RelicModel>> CanonicalModels = new(() => new Dictionary<string, RelicModel>(StringComparer.Ordinal));

    public static bool Contains(string relicId) => Rarities.Value.ContainsKey(relicId);

    public static RelicRarity RarityOf(string relicId)
        => Rarities.Value.TryGetValue(relicId, out var rarity)
            ? rarity
            : throw new KeyNotFoundException($"Relic {relicId} is not a v0.111.0 relic");

    public static IReadOnlyList<string> GetUnlockedRelics(string poolId, UnlockProfile unlocks)
    {
        ArgumentNullException.ThrowIfNull(unlocks);
        var list = new List<string>(RelicPools.Get(poolId).AllRelicIds);
        for (int g = 0; g < EpochGateRows.Length; g++)
        {
            var gate = EpochGateRows[g];
            if (!GateAppliesTo(poolId, gate.EpochId) || unlocks.IsEpochRevealed(gate.EpochId))
            {
                continue;
            }
            list.RemoveAll(id => Array.IndexOf(gate.RelicIds, id) >= 0);
        }
        return list;
    }

    public static string RelicPoolForCharacter(string characterId) => characterId + "_RELIC_POOL";

    public static bool IsBeforeAct3TreasureChest(RunState rs)
    {
        int num = rs.Players.Count > 1 ? MultiplayerAct3TreasureFloor : SingleplayerAct3TreasureFloor;
        return rs.TotalFloor < num;
    }

    public static bool IsAllowed(string relicId, RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (RelicRegistry.Models.Contains(relicId))
        {
            return Canonical(relicId).IsAllowed(rs);
        }
        return IsAllowedFallback(relicId, rs);
    }

    public static bool IsAllowedFallback(string relicId, RunState rs)
    {
        if (BeforeAct3TreasureChestIds.Contains(relicId))
        {
            return IsBeforeAct3TreasureChest(rs);
        }
        switch (relicId)
        {
            case "LASTING_CANDY":
                for (int i = 0; i < rs.Players.Count; i++)
                {
                    if (string.Equals(rs.Players[i].CharacterId, Cards.CardPools.IroncladCharacterId, StringComparison.Ordinal) && rs.Unlocks.NumberOfRuns == 0)
                    {
                        return false;
                    }
                }
                return IsBeforeAct3TreasureChest(rs);
            case "MASSIVE_SCROLL":
                return rs.Players.Count > 1;
            case "SILVER_CRUCIBLE":
            case "WINGED_BOOTS":
                return rs.Players.Count == 1;
            default:
                return true;
        }
    }

    private static RelicModel Canonical(string relicId)
    {
        var cache = CanonicalModels.Value;
        lock (cache)
        {
            if (!cache.TryGetValue(relicId, out var model))
            {
                model = RelicRegistry.Create(relicId);
                cache[relicId] = model;
            }
            return model;
        }
    }

    private static bool GateAppliesTo(string poolId, string epochId)
    {
        if (string.Equals(poolId, RelicPools.SharedRelicPool, StringComparison.Ordinal))
        {
            return epochId.StartsWith("RELIC", StringComparison.Ordinal);
        }
        string prefix = poolId.EndsWith("_RELIC_POOL", StringComparison.Ordinal) ? poolId[..^"_RELIC_POOL".Length] : poolId;
        return !epochId.StartsWith("RELIC", StringComparison.Ordinal) && epochId.StartsWith(prefix, StringComparison.Ordinal)
            && epochId.Length > prefix.Length && char.IsDigit(epochId[prefix.Length]);
    }
}
