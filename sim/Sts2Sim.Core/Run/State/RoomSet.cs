namespace Sts2Sim.Core.Run;

public sealed class RoomSet : ICanonical
{
    public readonly List<string> events = new();

    public int eventsVisited;

    public readonly List<EncounterDef> normalEncounters = new();

    public int normalEncountersVisited;

    public readonly List<EncounterDef> eliteEncounters = new();

    public int eliteEncountersVisited;

    public int bossEncountersVisited;

    public string? AncientId { get; set; }

    public EncounterDef? Boss { get; set; }

    public EncounterDef? SecondBoss { get; set; }

    public bool HasAncient => AncientId != null;

    public bool HasSecondBoss => SecondBoss != null;

    public string NextEventId => throw new NotImplementedException("W2 R1: RoomSet.NextEvent");

    public EncounterDef NextNormalEncounter => throw new NotImplementedException("W2 R1: RoomSet.NextNormalEncounter");

    public EncounterDef NextEliteEncounter => throw new NotImplementedException("W2 R1: RoomSet.NextEliteEncounter");

    public EncounterDef NextBossEncounter => throw new NotImplementedException("W2 R1: RoomSet.NextBossEncounter");

    public void MarkVisited(RoomType roomType) => throw new NotImplementedException("W2 R1: RoomSet.MarkVisited");

    public void EnsureNextEventIsValid(RunState runState) => throw new NotImplementedException("W2 R1: RoomSet.EnsureNextEventIsValid");

    public RoomSet DeepClone() => throw new NotImplementedException("W2 R1: RoomSet.DeepClone");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R1: RoomSet.WriteCanonical");
}
