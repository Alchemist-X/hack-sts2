namespace Sts2Sim.Core.Run;

public sealed class TeaMaster : EventModel
{
    public const string EventId = "TEA_MASTER";

    public const string BoneTeaId = "BONE_TEA";

    public const string EmberTeaId = "EMBER_TEA";

    public const string TeaOfDiscourtesyId = "TEA_OF_DISCOURTESY";

    public const int BoneTeaCost = 50;

    public const int EmberTeaCost = 150;

    public override string Id => EventId;

    // Option-text vars (Models.Events/TeaMaster.cs:278 StringVar <Tea>Description = the relic's description).
    public static string BoneTeaDescription => BoneTeaId;

    public static string EmberTeaDescription => EmberTeaId;

    public static string TeaOfDiscourtesyDescription => TeaOfDiscourtesyId;

    public override bool IsAllowed(RunState runState)
    {
        if (runState.CurrentActIndex < 2)
        {
            return runState.Players.All(p => p.Gold >= EmberTeaCost);
        }
        return false;
    }

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        var owner = Owner!;
        var options = new List<EventOption>(3);
        if (owner.Gold >= BoneTeaCost)
        {
            options.Add(new EventOption("TEA_MASTER.pages.INITIAL.options.BONE_TEA", BoneTea));
        }
        else
        {
            options.Add(new EventOption("TEA_MASTER.pages.INITIAL.options.BONE_TEA_LOCKED", null));
        }
        if (owner.Gold >= EmberTeaCost)
        {
            options.Add(new EventOption("TEA_MASTER.pages.INITIAL.options.EMBER_TEA", EmberTea));
        }
        else
        {
            options.Add(new EventOption("TEA_MASTER.pages.INITIAL.options.EMBER_TEA_LOCKED", null));
        }
        options.Add(new EventOption("TEA_MASTER.pages.INITIAL.options.TEA_OF_DISCOURTESY", TeaOfDiscourtesy));
        return options;
    }

    private void BoneTea(RunState rs)
    {
        PlayerCmd.LoseGold(rs, BoneTeaCost, Owner!, GoldLossType.Spent);
        Events121Support.ObtainRelic(rs, BoneTeaId, Owner!);
        SetEventFinished("TEA_MASTER.pages.DONE.description");
    }

    private void EmberTea(RunState rs)
    {
        PlayerCmd.LoseGold(rs, EmberTeaCost, Owner!, GoldLossType.Spent);
        Events121Support.ObtainRelic(rs, EmberTeaId, Owner!);
        SetEventFinished("TEA_MASTER.pages.DONE.description");
    }

    private void TeaOfDiscourtesy(RunState rs)
    {
        Events121Support.ObtainRelic(rs, TeaOfDiscourtesyId, Owner!);
        SetEventFinished("TEA_MASTER.pages.TEA_OF_DISCOURTESY.description");
    }
}
