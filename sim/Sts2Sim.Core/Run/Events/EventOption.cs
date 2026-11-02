using System.Reflection;

namespace Sts2Sim.Core.Run;

public sealed class EventOption : ICanonical
{
    public EventOption(string textKey, Action<RunState>? onChosen, bool disableOnChosen = true, bool isProceed = false)
    {
        ArgumentNullException.ThrowIfNull(textKey);
        TextKey = textKey;
        OnChosen = onChosen;
        DisableOnChosen = disableOnChosen;
        IsProceed = isProceed;
        IsLocked = onChosen == null;
    }

    private EventOption(EventOption source, Action<RunState>? onChosen, RelicModel? relic)
    {
        TextKey = source.TextKey;
        OnChosen = onChosen;
        DisableOnChosen = source.DisableOnChosen;
        IsProceed = source.IsProceed;
        IsLocked = source.IsLocked;
        WasChosen = source.WasChosen;
        RelicId = source.RelicId;
        Relic = relic;
        StringArg = source.StringArg;
        DonePage = source.DonePage;
        ShouldSaveChoiceToHistory = source.ShouldSaveChoiceToHistory;
    }

    public string TextKey { get; }

    public Action<RunState>? OnChosen { get; }

    public bool DisableOnChosen { get; }

    public bool IsProceed { get; }

    public bool WasChosen { get; set; }

    public bool IsLocked { get; }

    public bool IsEnabled => !IsLocked && !(DisableOnChosen && WasChosen);

    public string? RelicId { get; private set; }

    public RelicModel? Relic { get; private set; }

    public string? StringArg { get; set; }

    public string? DonePage { get; set; }

    public bool ShouldSaveChoiceToHistory { get; private set; } = true;

    public EventOption WithRelic(string relicId, RelicModel? relic)
    {
        ArgumentNullException.ThrowIfNull(relicId);
        RelicId = relicId;
        Relic = relic;
        return this;
    }

    public EventOption ThatWontSaveToChoiceHistory()
    {
        ShouldSaveChoiceToHistory = false;
        return this;
    }

    public void Chosen(RunState rs)
    {
        var onChosen = OnChosen;
        if (onChosen != null && (!DisableOnChosen || !WasChosen))
        {
            WasChosen = true;
            onChosen(rs);
        }
    }

    public EventOption CloneFor(EventModel source, EventModel clone, PlayerState? owner)
    {
        return new EventOption(this, Rebind(OnChosen, source, clone), Relic?.CloneFor(owner));
    }

    internal static Action<RunState>? Rebind(Action<RunState>? handler, EventModel source, EventModel clone)
    {
        if (handler == null)
        {
            return null;
        }
        var target = handler.Target;
        if (target == null)
        {
            return handler;
        }
        if (ReferenceEquals(target, source))
        {
            return (Action<RunState>)Delegate.CreateDelegate(typeof(Action<RunState>), clone, handler.Method);
        }
        if (target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Length == 0)
        {
            return handler;
        }
        throw new InvalidOperationException($"Event {source.Id}: option handler {handler.Method.Name} captures state other than the event; use an instance method of the event and per-option data on the option.");
    }

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(EventsCanonicalTags.EventOption);
        w.WriteString(TextKey);
        w.WriteBool(IsLocked);
        w.WriteBool(DisableOnChosen);
        w.WriteBool(IsProceed);
        w.WriteBool(WasChosen);
        w.WriteString(RelicId);
        var relic = Relic;
        w.WriteBool(relic != null);
        relic?.WriteCanonical(w);
        w.WriteString(StringArg);
        w.WriteString(DonePage);
    }
}
