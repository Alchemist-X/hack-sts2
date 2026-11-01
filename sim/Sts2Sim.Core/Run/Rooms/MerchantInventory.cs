namespace Sts2Sim.Core.Run;

public sealed class MerchantInventory : ICanonical
{
    public MerchantInventory(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; private set; }

    public IReadOnlyList<MerchantEntry> AllEntries => throw new NotImplementedException("W2 R4: MerchantInventory.AllEntries");

    public static MerchantInventory CreateForNormalMerchant(RunState rs, PlayerState player) => throw new NotImplementedException("W2 R4: MerchantInventory.CreateForNormalMerchant");

    public MerchantInventory CloneFor(PlayerState player) => throw new NotImplementedException("W2 R4: MerchantInventory.CloneFor");

    public void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R4: MerchantInventory.WriteCanonical");
}
