namespace Sts2Sim.Core.Run;

public sealed class RestSiteRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.RestSite;

    public List<RestSiteOption> Options { get; private set; } = new();

    public bool ProceedAllowed { get; private set; }

    public bool IsCompleted { get; private set; }

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        RejectRoomStackReconstruction(isRestoringRoomStackBase, nameof(RestSiteRoom));
        BeginRestSite(rs);
        Hook.AfterRoomEntered(rs, this);
    }

    private void BeginRestSite(RunState rs)
    {
        Options = RestSiteOption.Generate(rs, rs.Players[0]);
        IsCompleted = false;
        ProceedAllowed = Options.Count == 0;
    }

    public bool ChooseOption(RunState rs, int optionIndex)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (IsCompleted)
        {
            throw new InvalidOperationException($"Rest site option index {optionIndex} chosen after the rest site was completed.");
        }
        if ((uint)optionIndex >= (uint)Options.Count)
        {
            throw new InvalidOperationException($"Rest site option index {optionIndex} chosen, but there are only {Options.Count} options.");
        }
        var option = Options[optionIndex];
        if (!option.OnSelect(rs))
        {
            return false;
        }
        ProceedAllowed = true;
        if (IsCompleted)
        {
            return true;
        }
        if (Hook.ShouldDisableRemainingRestSiteOptions(rs, option.Owner))
        {
            Options.Clear();
        }
        else
        {
            Options.RemoveAt(optionIndex);
        }
        if (Options.Count == 0)
        {
            IsCompleted = true;
        }
        return true;
    }

    public override void Exit(RunState rs)
    {
        if (Options.Count > 0)
        {
            Options.Clear();
            IsCompleted = true;
        }
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom)
    {
    }

    public override AbstractRoom CloneFor(RunState rs)
    {
        var clone = CopyBaseInto(new RestSiteRoom
        {
            ProceedAllowed = ProceedAllowed,
            IsCompleted = IsCompleted,
        });
        var owner = rs.Players[0];
        clone.Options = new List<RestSiteOption>(Options.Count);
        for (int i = 0; i < Options.Count; i++)
        {
            clone.Options.Add(Options[i].CloneFor(owner));
        }
        return clone;
    }

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteTag(RewardsCanonicalTags.RestSite);
        w.WriteBool(ProceedAllowed);
        w.WriteBool(IsCompleted);
        w.WriteCount(Options.Count);
        for (int i = 0; i < Options.Count; i++)
        {
            w.WriteString(Options[i].OptionId);
            w.WriteBool(Options[i].IsEnabled);
        }
    }
}
