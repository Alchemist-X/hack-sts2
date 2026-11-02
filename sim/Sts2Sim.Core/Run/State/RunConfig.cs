namespace Sts2Sim.Core.Run;

public sealed record RunProfileProgress(IReadOnlyList<string> DiscoveredActIds, int NumberOfRuns, int WongoPoints)
{
    public static readonly RunProfileProgress Veteran = new(Array.Empty<string>(), UnlockProfile.AllNumberOfRuns, 0)
    {
        AllActsDiscovered = true
    };

    public bool AllActsDiscovered { get; init; }

    public bool HasDiscoveredAct(string actId) => AllActsDiscovered || DiscoveredActIds.Contains(actId, StringComparer.Ordinal);
}

public sealed record RunConfig(string SeedText, string CharacterId, int Ascension)
{
    public const string RandomAct1 = "random";

    public IReadOnlyList<string>? ActIds { get; init; }

    public string Act1 { get; init; } = RandomAct1;

    public UnlockProfile Unlocks { get; init; } = UnlockProfile.All;

    public RunProfileProgress Progress { get; init; } = RunProfileProgress.Veteran;

    public ContentFilter Content { get; init; } = ContentFilter.All;

    public void WriteCanonical(CanonicalWriter w)
    {
        w.WriteTag(RunStateCanonicalTags.RunConfig);
        w.WriteString(SeedText);
        w.WriteString(CharacterId);
        w.WriteInt32(Ascension);
        w.WriteString(Act1);
        var acts = ActIds;
        w.WriteBool(acts != null);
        if (acts != null)
        {
            w.WriteCount(acts.Count);
            for (int i = 0; i < acts.Count; i++)
            {
                w.WriteString(acts[i]);
            }
        }
        var progress = Progress;
        w.WriteBool(progress.AllActsDiscovered);
        var discovered = progress.DiscoveredActIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        w.WriteCount(discovered.Length);
        for (int i = 0; i < discovered.Length; i++)
        {
            w.WriteString(discovered[i]);
        }
        w.WriteInt32(progress.NumberOfRuns);
        w.WriteInt32(progress.WongoPoints);
        w.WriteString(Content.Name);
    }
}
