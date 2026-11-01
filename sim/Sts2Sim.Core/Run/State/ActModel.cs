namespace Sts2Sim.Core.Run;

public abstract partial class ActModel
{
    public abstract string Id { get; }

    public abstract int Index { get; }

    public abstract bool IsDefault { get; }

    public abstract IEnumerable<EncounterDef> BossDiscoveryOrder { get; }

    public abstract IEnumerable<string> AllAncientIds { get; }

    public abstract IEnumerable<string> AllEventIds { get; }

    public abstract IEnumerable<EncounterDef> GenerateAllEncounters();

    public abstract bool IsUnlocked(UnlockProfile unlockState);

    public abstract IEnumerable<string> GetUnlockedAncientIds(UnlockProfile state);

    public abstract MapPointTypeCounts GetMapPointTypes(Rng mapRng);

    public int GetNumberOfRooms(bool isMultiplayer) => throw new NotImplementedException("W2 R1: ActModel.GetNumberOfRooms");

    public int GetNumberOfFloors(bool isMultiplayer) => throw new NotImplementedException("W2 R1: ActModel.GetNumberOfFloors");

    public ActMap CreateMap(RunState runState, bool replaceTreasureWithElites) => throw new NotImplementedException("W2 R1: ActModel.CreateMap");

    public static IEnumerable<ActModel> GetRandomList(Rng rng, UnlockProfile unlockState, RunProfileProgress progress, bool isMultiplayer) => throw new NotImplementedException("W2 R1: ActModel.GetRandomList");

    public static IReadOnlyList<ActModel> GetDefaultList() => throw new NotImplementedException("W2 R1: ActModel.GetDefaultList");
}
