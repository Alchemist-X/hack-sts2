namespace Sts2Sim.Core.Run;

public sealed partial class RewardsSet : ICanonical
{
    public RewardsSet(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; private set; }

    public List<Reward> Rewards { get; } = new();

    public AbstractRoom? Room { get; private set; }

    public bool IsTerminal { get; set; }

    public RewardsSet WithRewardsFromRoom(RunState rs, AbstractRoom room) => throw new NotImplementedException("W2 R3: RewardsSet.WithRewardsFromRoom");

    public RewardsSet WithCustomRewards(List<Reward> rewards) => throw new NotImplementedException("W2 R3: RewardsSet.WithCustomRewards");

    public List<Reward> GenerateWithoutOffering(RunState rs) => throw new NotImplementedException("W2 R3: RewardsSet.GenerateWithoutOffering");

    public RewardsSet CloneFor(PlayerState player, AbstractRoom? room) => throw new NotImplementedException("W2 R3: RewardsSet.CloneFor");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: RewardsSet.WriteCanonical");
}
