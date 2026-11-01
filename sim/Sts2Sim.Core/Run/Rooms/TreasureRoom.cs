namespace Sts2Sim.Core.Run;

public sealed class TreasureRoom : AbstractRoom
{
    public TreasureRoom(int actIndex)
    {
        ActIndex = actIndex;
    }

    public int ActIndex { get; }

    public override RoomType RoomType => RoomType.Treasure;

    public string? CurrentRelicId { get; private set; }

    public bool ChestOpened { get; private set; }

    public bool SingleplayerSkipped { get; private set; }

    public bool RelicClaimed { get; private set; }

    public void OpenChest(RunState rs) => throw new NotImplementedException("W2 R4: TreasureRoom.OpenChest");

    public void TakeRelic(RunState rs) => throw new NotImplementedException("W2 R4: TreasureRoom.TakeRelic");

    public void SkipRelic(RunState rs) => throw new NotImplementedException("W2 R4: TreasureRoom.SkipRelic");
}
