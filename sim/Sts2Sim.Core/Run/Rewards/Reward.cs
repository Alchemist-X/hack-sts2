namespace Sts2Sim.Core.Run;

public abstract partial class Reward : ICanonical
{
    protected Reward(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; protected set; }

    public abstract RewardType RewardType { get; }

    public virtual int RewardsSetIndex => throw new NotImplementedException("W2 R3: Reward.RewardsSetIndex");

    public bool IsPopulated { get; protected set; }

    public bool SuccessfullySelected { get; protected set; }

    public virtual void Populate(RunState rs) => throw new NotImplementedException("W2 R3: Reward.Populate");

    public virtual bool OnSelectWrapper(RunState rs) => throw new NotImplementedException("W2 R3: Reward.OnSelectWrapper");

    public virtual void OnSkipped(RunState rs) => throw new NotImplementedException("W2 R3: Reward.OnSkipped");

    public virtual Reward CloneFor(PlayerState player) => throw new NotImplementedException("W2 R3: Reward.CloneFor");

    public virtual void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R3: Reward.WriteCanonical");
}
