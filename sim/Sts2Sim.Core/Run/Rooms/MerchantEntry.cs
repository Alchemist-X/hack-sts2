namespace Sts2Sim.Core.Run;

public abstract partial class MerchantEntry : ICanonical
{
    protected MerchantEntry(PlayerState player)
    {
        Player = player;
    }

    public PlayerState Player { get; protected set; }

    public virtual int Cost => throw new NotImplementedException("W2 R4: MerchantEntry.Cost");

    public virtual bool IsStocked => throw new NotImplementedException("W2 R4: MerchantEntry.IsStocked");

    public bool EnoughGold(RunState rs) => throw new NotImplementedException("W2 R4: MerchantEntry.EnoughGold");

    public virtual bool OnTryPurchaseWrapper(RunState rs, MerchantInventory inventory) => throw new NotImplementedException("W2 R4: MerchantEntry.OnTryPurchaseWrapper");

    public virtual MerchantEntry CloneFor(PlayerState player) => throw new NotImplementedException("W2 R4: MerchantEntry.CloneFor");

    public virtual void WriteCanonical(CanonicalWriter w) => throw new NotImplementedException("W2 R4: MerchantEntry.WriteCanonical");
}
