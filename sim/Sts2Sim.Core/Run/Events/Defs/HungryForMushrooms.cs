namespace Sts2Sim.Core.Run;

public sealed class HungryForMushrooms : EventModel
{
    public const string EventId = "HUNGRY_FOR_MUSHROOMS";

    public const string BigMushroomId = "BIG_MUSHROOM";

    public const string FragrantMushroomId = "FRAGRANT_MUSHROOM";

    public override string Id => EventId;

    protected override IReadOnlyList<EventOption> GenerateInitialOptions(RunState rs)
    {
        return new[]
        {
            RelicOption(BigMushroomId, BigMushroom),
            RelicOption(FragrantMushroomId, FragrantMushroom)
        };
    }

    private void BigMushroom(RunState rs)
    {
        Events121Support.ObtainRelic(rs, BigMushroomId, Owner!);
        SetEventFinished("HUNGRY_FOR_MUSHROOMS.pages.BIG_MUSHROOM.description");
    }

    private void FragrantMushroom(RunState rs)
    {
        Events121Support.ObtainRelic(rs, FragrantMushroomId, Owner!);
        SetEventFinished("HUNGRY_FOR_MUSHROOMS.pages.FRAGRANT_MUSHROOM.description");
    }
}
