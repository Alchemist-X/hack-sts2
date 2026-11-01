namespace Sts2Sim.Core.Run;

public sealed class EventOption
{
    public EventOption(string textKey, Action<RunState>? onChosen, bool disableOnChosen = false)
    {
        TextKey = textKey;
        OnChosen = onChosen;
        DisableOnChosen = disableOnChosen;
    }

    public string TextKey { get; }

    public Action<RunState>? OnChosen { get; }

    public bool DisableOnChosen { get; }

    public bool WasChosen { get; set; }

    public bool IsLocked => OnChosen == null;

    public bool IsEnabled => !IsLocked && !(DisableOnChosen && WasChosen);
}
