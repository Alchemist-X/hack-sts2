namespace Sts2Sim.Core.Run;

public static class RoomStack
{
    public static AbstractRoom? CurrentRoom(RunState rs) => rs.CurrentRooms.Count == 0 ? null : rs.CurrentRooms[^1];

    public static AbstractRoom? BaseRoom(RunState rs) => rs.CurrentRooms.Count == 0 ? null : rs.CurrentRooms[0];

    public static AbstractRoom CreateRoom(RunState rs, RoomType roomType, MapPointType mapPointType) => throw new NotImplementedException("W2 R4: RoomStack.CreateRoom");

    public static void EnterRoom(RunState rs, AbstractRoom room) => throw new NotImplementedException("W2 R4: RoomStack.EnterRoom");

    public static void EnterRoomWithoutExitingCurrentRoom(RunState rs, AbstractRoom room) => throw new NotImplementedException("W2 R4: RoomStack.EnterRoomWithoutExitingCurrentRoom");

    public static void ExitCurrentRooms(RunState rs) => throw new NotImplementedException("W2 R4: RoomStack.ExitCurrentRooms");

    public static void ResumePreviousRoom(RunState rs) => throw new NotImplementedException("W2 R4: RoomStack.ResumePreviousRoom");
}
