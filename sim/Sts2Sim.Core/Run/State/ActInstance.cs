namespace Sts2Sim.Core.Run;

public sealed class ActInstance : ICanonical
{
    public ActInstance(ActModel def)
    {
        Def = def;
        Rooms = new RoomSet();
        SharedAncientSubset = new List<string>();
    }

    public ActModel Def { get; }

    public RoomSet Rooms { get; }

    public List<string> SharedAncientSubset { get; }

    public EncounterDef BossEncounter => Rooms.Boss ?? throw new InvalidOperationException($"Act {Def.Id} has no boss yet.");

    public EncounterDef? SecondBossEncounter => Rooms.SecondBoss;

    public bool HasSecondBoss => Rooms.HasSecondBoss;

    public void SetSharedAncientSubset(List<string> sharedAncientSubset) => throw new NotImplementedException("W2 R1: ActInstance.SetSharedAncientSubset");

    public void GenerateRooms(Rng rng, UnlockProfile unlockState, bool isMultiplayer = false) => throw new NotImplementedException("W2 R1: ActInstance.GenerateRooms");

    public void ValidateRoomsAfterLoad(Rng rng) => throw new NotImplementedException("W2 R1: ActInstance.ValidateRoomsAfterLoad");

    public void ApplyDiscoveryOrderModifications(UnlockProfile unlockState) => throw new NotImplementedException("W2 R1: ActInstance.ApplyDiscoveryOrderModifications");

    public EventModel PullAncient() => throw new NotImplementedException("W2 R1: ActInstance.PullAncient");

    public EventModel PullNextEvent(RunState runState) => throw new NotImplementedException("W2 R1: ActInstance.PullNextEvent");

    public EncounterDef PullNextEncounter(RoomType roomType) => throw new NotImplementedException("W2 R1: ActInstance.PullNextEncounter");

    public void MarkRoomVisited(RoomType roomType) => throw new NotImplementedException("W2 R1: ActInstance.MarkRoomVisited");

    public ActInstance DeepClone() => throw new NotImplementedException("W2 R1: ActInstance.DeepClone");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R1: ActInstance.WriteCanonical");
}
