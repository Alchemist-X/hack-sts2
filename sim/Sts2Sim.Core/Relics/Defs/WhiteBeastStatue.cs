namespace Sts2Sim.Core.Relics;

public sealed class WhiteBeastStatue : RelicModel
{
    public const string RelicId = "WHITE_BEAST_STATUE";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool IsAllowed(RunState rs) => IsBeforeAct3TreasureChest(rs.Players.Count, rs.TotalFloor);

    public override bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if ((uint)(roomType - 1) > 2u)
        {
            return false;
        }
        return true;
    }
}
