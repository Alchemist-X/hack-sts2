namespace Sts2Sim.Core.Run;

public static class RelicFactory
{
    public const string FallbackRelicId = "CIRCLET";

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, Rng rng) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromFront(rng)");

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromFront");

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, RelicRarity rarity) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromFront(rarity)");

    public static RelicModel PullNextRelicFromFront(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromFront(rarity, filter)");

    public static RelicModel PullNextRelicFromBack(RunState rs, PlayerState player) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromBack");

    public static RelicModel PullNextRelicFromBack(RunState rs, PlayerState player, RelicRarity rarity, Func<string, bool> filter) => throw new NotImplementedException("W2 R3: RelicFactory.PullNextRelicFromBack(rarity, filter)");

    public static RelicRarity RollRarity(PlayerState player) => throw new NotImplementedException("W2 R3: RelicFactory.RollRarity(player)");

    public static RelicRarity RollRarity(Rng rng) => throw new NotImplementedException("W2 R3: RelicFactory.RollRarity(rng)");
}
