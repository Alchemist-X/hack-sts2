namespace Sts2Sim.Core.Run;

public static class RewardsCmd
{
    public static void OfferForRoomEnd(RunState rs, PlayerState player, AbstractRoom room) => throw new NotImplementedException("W2 R3: RewardsCmd.OfferForRoomEnd");

    public static void OfferCustom(RunState rs, PlayerState player, List<Reward> rewards) => throw new NotImplementedException("W2 R3: RewardsCmd.OfferCustom");

    public static RewardsSet GenerateForRoomEnd(RunState rs, PlayerState player, AbstractRoom room) => throw new NotImplementedException("W2 R3: RewardsCmd.GenerateForRoomEnd");

    public static RewardsSet GenerateCustom(RunState rs, PlayerState player, List<Reward> rewards) => throw new NotImplementedException("W2 R3: RewardsCmd.GenerateCustom");
}
