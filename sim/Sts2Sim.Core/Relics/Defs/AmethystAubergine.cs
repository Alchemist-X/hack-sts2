namespace Sts2Sim.Core.Relics;

public sealed class AmethystAubergine : RelicModel
{
    public const string RelicId = "AMETHYST_AUBERGINE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Common;

    public override bool IsAllowedInShops => false;

    protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new GoldVar(15)
    };

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room == null)
        {
            return false;
        }
        if (room.RoomType is not (RoomType.Monster or RoomType.Elite or RoomType.Boss))
        {
            return false;
        }
        if (room.RoomType == RoomType.Boss && rs.CurrentActIndex >= rs.Acts.Count - 1)
        {
            return false;
        }
        rewards.Add(new GoldReward(DynamicVars.Gold.IntValue, player));
        return true;
    }
}
