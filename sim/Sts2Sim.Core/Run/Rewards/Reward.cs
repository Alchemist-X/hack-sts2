namespace Sts2Sim.Core.Run;

public abstract partial class Reward : ICanonical
{
    protected Reward(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; protected set; }

    public abstract RewardType RewardType { get; }

    public abstract int RewardsSetIndex { get; }

    public abstract bool IsPopulated { get; }

    public bool SuccessfullySelected { get; protected set; }

    protected Rng? RngOverride { get; private set; }

    public Reward SetRng(Rng rng)
    {
        RngOverride = rng;
        return this;
    }

    public abstract void Populate(RunState rs);

    protected abstract bool OnSelect(RunState rs);

    public virtual bool OnSelectWrapper(RunState rs)
    {
        ArgumentNullException.ThrowIfNull(rs);
        bool success = OnSelect(rs);
        return CompleteSelection(rs, success);
    }

    protected bool CompleteSelection(RunState rs, bool success)
    {
        if (success)
        {
            Hook.AfterRewardTaken(rs, Player, this);
            SuccessfullySelected = true;
        }
        return success;
    }

    public virtual void OnSkipped(RunState rs)
    {
    }

    public Reward CloneFor(PlayerState player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var clone = CloneCore(player);
        clone.SuccessfullySelected = SuccessfullySelected;
        clone.RngOverride = CardCreationOptions.RemapRng(RngOverride, Player, player);
        return clone;
    }

    protected abstract Reward CloneCore(PlayerState player);

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.Reward);
        w.WriteByte((byte)RewardType);
        w.WriteBool(SuccessfullySelected);
        w.WriteBool(RngOverride != null);
        RngOverride?.WriteCanonical(w);
        WriteFields(w);
    }

    protected abstract void WriteFields(CanonicalWriter w);
}
