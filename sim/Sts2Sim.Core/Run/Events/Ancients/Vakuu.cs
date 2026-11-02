namespace Sts2Sim.Core.Run;

public sealed class Vakuu : AncientEventModel
{
    public const string EventId = "VAKUU";

    public static readonly IReadOnlyList<string> Pool1Ids = new[] { "BLOOD_SOAKED_ROSE", "WHISPERING_EARRING", "FIDDLE" };

    public static readonly IReadOnlyList<string> Pool2Ids = new[] { "PRESERVED_FOG", "SERE_TALON", "DISTINGUISHED_CAPE" };

    public static readonly IReadOnlyList<string> Pool3Ids = new[] { "CHOICES_PARADOX", "MUSIC_BOX", "LORDS_PARASOL", "JEWELED_MASK" };

    public override string Id => EventId;

    private List<EventOption> Pool(IReadOnlyList<string> ids)
    {
        var list = new List<EventOption>(ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            list.Add(AncientRelicOption(ids[i]));
        }
        return list;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var rng = Rng ?? throw new InvalidOperationException("Vakuu has no rng.");
        var pool1 = Pool(Pool1Ids);
        var pool2 = Pool(Pool2Ids);
        var pool3 = Pool(Pool3Ids);
        pool1.UnstableShuffle(rng);
        pool2.UnstableShuffle(rng);
        pool3.UnstableShuffle(rng);
        return new[] { pool1[0], pool2[0], pool3[0] };
    }
}
