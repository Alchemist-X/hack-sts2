using Sts2Sim.Core;
using Sts2Sim.Core.Combat.Pipeline;
using Sts2Sim.Core.Registry;
using Sts2Sim.Core.Relics;
using Sts2Sim.Core.Run;
using Sts2Sim.Vec;

namespace Sts2Sim.Tests;

/// <summary>
/// The shop's potion purchase rule (MerchantRoom.CanBuy): the game procures the potion before charging and refuses the
/// purchase with nothing changed when the belt is full (TooFull) or Sozu forbids it (NotAllowed) — game
/// Entities.Merchant/MerchantPotionEntry.cs:203-216, Commands/PotionCmd.cs:253-267, Entities.Players/Player.cs:1020-1069.
/// Such a click is not a legal action, so no BuyEntry is listed for it (it used to be, and a greedy A10 policy looped on
/// it until the floor stall cap: 538 no-op purchases in 200 eval games, PPO trajectory diagnosis 2026-11-03 §4).
/// </summary>
public static unsafe partial class VecTests
{
    private static List<int> PotionEntries(RunSim sim)
    {
        var entries = ((MerchantRoom)RoomStack.CurrentRoom(sim.State)!).Inventory!.AllEntries;
        var list = new List<int>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] is MerchantPotionEntry { IsStocked: true })
            {
                list.Add(i);
            }
        }
        return list;
    }

    private static HashSet<int> BuyRows(RunSim sim)
        => sim.LegalActions().Where(a => a.Kind == RunActionKind.BuyEntry).Select(a => a.A).ToHashSet();

    public static void Test_VecCand_ShopPotionNeedsAFreeBeltSlot()
    {
        var sim = ShopStart("SHOPBELT");
        var rs = sim.State;
        var p = rs.Players[0];
        var room = (MerchantRoom)RoomStack.CurrentRoom(rs)!;
        var entries = room.Inventory!.AllEntries;
        var potions = PotionEntries(sim);
        Assert.Equal(3, potions.Count);
        Assert.True(p.HasOpenPotionSlot, "a fresh run starts with an empty belt");

        // free slots and 999 gold: every stocked entry is buyable
        var buy = BuyRows(sim);
        for (int i = 0; i < entries.Count; i++)
        {
            Assert.Equal(entries[i].IsStocked && entries[i].EnoughGold(rs), buy.Contains(i), $"entry {i} ({entries[i].GetType().Name}) with free slots");
        }

        // full belt: the potion rows disappear, every other entry stays buyable
        int added = 0;
        for (int s = 0; s < p.PotionSlots.Length; s++)
        {
            if (p.PotionSlots[s] == null)
            {
                Assert.True(p.AddPotionInternal(PotionRegistry.Create("BLOCK_POTION"), s).Success);
                added++;
            }
        }
        Assert.True(added >= 2 && !p.HasOpenPotionSlot, $"belt filled ({added} potions)");
        buy = BuyRows(sim);
        foreach (int i in potions)
        {
            Assert.False(buy.Contains(i), $"potion entry {i} is not buyable with a full belt");
            Assert.False(MerchantRoom.CanBuy(rs, entries[i]));
            Assert.False(room.CanPurchase(rs, i));
            Assert.False(sim.IsLegal(RunAction.Of(RunActionKind.BuyEntry, i)));
        }
        for (int i = 0; i < entries.Count; i++)
        {
            if (!potions.Contains(i))
            {
                Assert.Equal(entries[i].IsStocked && entries[i].EnoughGold(rs), buy.Contains(i), $"entry {i} with a full belt");
            }
        }
        Assert.Throws<InvalidOperationException>(() => sim.Clone().Step(RunAction.Of(RunActionKind.BuyEntry, potions[0])), "an unlisted purchase is illegal");

        // the game's refusal itself changes nothing (no gold spent, the entry stays stocked, the belt is unchanged)
        var probe = sim.Clone();
        ulong before = probe.StateHash();
        var probeRoom = (MerchantRoom)RoomStack.CurrentRoom(probe.State)!;
        Assert.False(probeRoom.TryPurchase(probe.State, potions[0]), "the merchant refuses the potion");
        Assert.Equal(999, probe.State.Players[0].Gold);
        Assert.True(probeRoom.Inventory!.AllEntries[potions[0]].IsStocked);
        Assert.Equal(before, probe.StateHash(), "a refused purchase leaves the state unchanged");

        // the Vec candidates of that state list no potion purchase either
        var env = NewEnv(1, 1, 0);
        int entry = env.Pool.AddSnapshot(sim.Clone());
        using (var nb = new NativeBuffers(1))
        {
            env.Restore(0, entry, nb.Table);
            var slot = nb.Slot(0);
            int shopW = Schema.ShopFields.Length;
            int potionCategory = Vocab.ShopCategory("potion");
            int buys = 0;
            for (int r = 0; r < slot.CandN; r++)
            {
                var row = CandRow(nb, 0, r);
                if ((RunActionKind)row[CandCol.Kind] != RunActionKind.BuyEntry)
                {
                    continue;
                }
                buys++;
                Assert.NotEqual(potionCategory, slot.Shop[row[CandCol.OptionIndex] * shopW], $"cand row {r} buys a potion");
            }
            Assert.Equal(buy.Count, buys);
            foreach (int i in potions)
            {
                Assert.Equal(1, slot.Shop[i * shopW + Schema.Col(Schema.ShopFields, "affordable")], "affordable keeps its meaning: stocked and price <= gold");
            }
        }

        // discarding one potion frees a slot: the potions are buyable again, and buying one works
        int discard = sim.LegalActions().First(a => a.Kind == RunActionKind.DiscardPotion).A;
        sim.Step(RunAction.Of(RunActionKind.DiscardPotion, discard));
        Assert.True(p.HasOpenPotionSlot);
        buy = BuyRows(sim);
        foreach (int i in potions)
        {
            Assert.True(buy.Contains(i), $"potion entry {i} is buyable with a free slot");
        }
        int price = entries[potions[0]].GetCost(rs);
        sim.Step(RunAction.Of(RunActionKind.BuyEntry, potions[0]));
        Assert.Equal(999 - price, p.Gold);
        Assert.False(p.HasOpenPotionSlot, "the bought potion filled the free slot");
        Assert.True(potions.All(i => !BuyRows(sim).Contains(i)), "the belt is full again");
    }

    public static void Test_VecCand_ShopPotionRefusedUnderSozu()
    {
        var sim = ShopStart("SHOPSOZU");
        var rs = sim.State;
        var p = rs.Players[0];
        RelicCmd.Obtain(rs, RelicRegistry.Create(Sozu.RelicId), p);
        Assert.True(p.HasOpenPotionSlot);
        var potions = PotionEntries(sim);
        Assert.Equal(3, potions.Count);
        var buy = BuyRows(sim);
        foreach (int i in potions)
        {
            Assert.False(buy.Contains(i), $"potion entry {i} under Sozu");
        }
        Assert.True(buy.Count > 0, "cards, relics and the removal stay buyable");
        var probe = sim.Clone();
        ulong before = probe.StateHash();
        Assert.False(((MerchantRoom)RoomStack.CurrentRoom(probe.State)!).TryPurchase(probe.State, potions[0]));
        Assert.Equal(before, probe.StateHash(), "the forbidden purchase leaves the state unchanged");
    }
}
