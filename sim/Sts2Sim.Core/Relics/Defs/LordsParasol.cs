namespace Sts2Sim.Core.Relics;

public sealed class LordsParasol : RelicModel
{
    public const string RelicId = "LORDS_PARASOL";

    public override string Id => RelicId;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    public override void AfterRoomEntered(RunState rs, AbstractRoom room)
    {
        if (room is not MerchantRoom merchantRoom)
        {
            return;
        }
        var inventory = merchantRoom.Inventories.FirstOrDefault(i => ReferenceEquals(i.Player, Owner)) ?? merchantRoom.Inventory;
        if (inventory != null)
        {
            PurchaseEverything(rs, inventory);
        }
    }

    private void PurchaseEverything(RunState rs, MerchantInventory inventory)
    {
        if (!ReferenceEquals(inventory.Player, Owner))
        {
            return;
        }
        foreach (var entry in inventory.CharacterCardEntries)
        {
            if (!entry.IsStocked)
            {
                continue;
            }
            entry.OnTryPurchaseWrapper(rs, inventory, ignoreCost: true);
        }
        foreach (var entry in inventory.ColorlessCardEntries)
        {
            if (!entry.IsStocked)
            {
                continue;
            }
            entry.OnTryPurchaseWrapper(rs, inventory, ignoreCost: true);
        }
        foreach (var entry in inventory.RelicEntries)
        {
            entry.OnTryPurchaseWrapper(rs, inventory, ignoreCost: true);
        }
        foreach (var entry in inventory.PotionEntries)
        {
            entry.OnTryPurchaseWrapper(rs, inventory, ignoreCost: true);
        }
        if (inventory.CardRemovalEntry != null && !RunLoop.IsGameOver(rs))
        {
            inventory.CardRemovalEntry.OnTryPurchaseWrapper(rs, inventory, ignoreCost: true, cancelable: false);
        }
    }
}
