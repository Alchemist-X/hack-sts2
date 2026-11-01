namespace Sts2Sim.Core.Run;

public sealed class MerchantRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.Shop;

    public MerchantInventory? Inventory { get; private set; }

    public bool TryPurchase(RunState rs, int entryIndex) => throw new NotImplementedException("W2 R4: MerchantRoom.TryPurchase");
}
