namespace Sts2Sim.Core.Run;

public sealed class MerchantRoom : AbstractRoom
{
    public override RoomType RoomType => RoomType.Shop;

    public List<MerchantInventory> Inventories { get; private set; } = new();

    public MerchantInventory? Inventory => Inventories.Count == 0 ? null : Inventories[0];

    protected override void EnterInternal(RunState rs, bool isRestoringRoomStackBase)
    {
        RejectRoomStackReconstruction(isRestoringRoomStackBase, nameof(MerchantRoom));
        var players = rs.Players;
        for (int i = 0; i < players.Count; i++)
        {
            Inventories.Add(MerchantInventory.CreateForNormalMerchant(rs, players[i]));
        }
        for (int i = 0; i < Inventories.Count; i++)
        {
            var inventory = Inventories[i];
            if (inventory.CardRemovalEntry != null && !Hook.ShouldAllowMerchantCardRemoval(rs, inventory.Player))
            {
                inventory.CardRemovalEntry.SetUsed();
            }
        }
        Hook.AfterRoomEntered(rs, this);
    }

    public override void Exit(RunState rs)
    {
    }

    public override void Resume(RunState rs, AbstractRoom exitedRoom) => throw new InvalidOperationException("MerchantRoom cannot be resumed.");

    public bool CanPurchase(RunState rs, int entryIndex)
    {
        var inventory = Inventory;
        if (inventory == null)
        {
            return false;
        }
        var entries = inventory.AllEntries;
        if ((uint)entryIndex >= (uint)entries.Count)
        {
            return false;
        }
        return CanBuy(rs, entries[entryIndex]);
    }

    /// <summary>
    /// The shop's purchase rule (the <c>BuyEntry</c> legality): the entry is stocked, the player has the gold
    /// (game Entities.Merchant/MerchantEntry.cs:312-321), and the purchase itself would go through. Only a potion purchase
    /// can be refused after those checks: <c>MerchantPotionEntry.OnTryPurchase</c> procures the potion first and, when
    /// <c>PotionCmd.TryToProcure</c> fails (full belt → TooFull, Sozu → NotAllowed), raises PurchaseFailed
    /// (FailureSpace / FailureForbidden) and returns without spending gold or clearing the entry
    /// (Entities.Merchant/MerchantPotionEntry.cs:203-216; the UI only wiggles the slot, Nodes.Screens.Shops/NMerchantSlot.cs:425).
    /// Such a click changes nothing, so it is not a legal action.
    /// </summary>
    public static bool CanBuy(RunState rs, MerchantEntry entry)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsStocked || !entry.EnoughGold(rs))
        {
            return false;
        }
        return entry is not MerchantPotionEntry { Model: { } potion } || PotionCmd.CanProcure(rs, potion, entry.Player);
    }

    public bool TryPurchase(RunState rs, int entryIndex)
    {
        ArgumentNullException.ThrowIfNull(rs);
        var inventory = Inventory ?? throw new InvalidOperationException("The merchant has no inventory before the room is entered.");
        var entries = inventory.AllEntries;
        if ((uint)entryIndex >= (uint)entries.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(entryIndex), entryIndex, $"The merchant has {entries.Count} entries.");
        }
        return entries[entryIndex].OnTryPurchaseWrapper(rs, inventory);
    }

    public override AbstractRoom CloneFor(RunState rs)
    {
        var clone = CopyBaseInto(new MerchantRoom());
        clone.Inventories = new List<MerchantInventory>(Inventories.Count);
        for (int i = 0; i < Inventories.Count; i++)
        {
            clone.Inventories.Add(Inventories[i].CloneFor(rs.Players[i]));
        }
        return clone;
    }

    protected override void WritePayload(CanonicalWriter w)
    {
        w.WriteCount(Inventories.Count);
        for (int i = 0; i < Inventories.Count; i++)
        {
            Inventories[i].WriteCanonical(w);
        }
    }
}
