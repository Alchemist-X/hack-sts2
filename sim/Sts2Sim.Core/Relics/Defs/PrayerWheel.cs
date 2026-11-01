namespace Sts2Sim.Core.Relics;

public sealed class PrayerWheel : RelicModel
{
    public const string RelicId = "PRAYER_WHEEL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Rare;

    public override bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)
    {
        if (!ReferenceEquals(player, Owner))
        {
            return false;
        }
        if (room == null || room.RoomType != RoomType.Monster)
        {
            return false;
        }
        rewards.Add(new CardReward(player, CardCreationOptions.ForRoom(rs, player, RoomType.Monster), 3));
        return true;
    }
}
