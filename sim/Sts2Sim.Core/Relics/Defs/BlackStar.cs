namespace Sts2Sim.Core.Relics;

public sealed class BlackStar : RelicModel
{
    public const string RelicId = "BLACK_STAR";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room == null || room.RoomType != RoomType.Elite)
        {
            return false;
        }
        rewards.Add(new RelicReward(player));
        return true;
    }
}
