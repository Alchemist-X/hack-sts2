using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.Run;

/// <summary>
/// Replays the <c>EventModel.IsAllowed</c> inputs of a <c>.run</c> history onto the oracle player: gold, HP, max HP,
/// potions, relics and the deck as of the end of the previous floor (the state <c>ActModel.PullNextEvent</c>
/// (Models/ActModel.cs:380-386) sees when the next room is entered, Runs/RunManager.cs:4197). Upgrades and
/// enchantments are not replayed (no ported IsAllowed reads an upgrade; the enchantable checks see unenchanted cards).
/// </summary>
public static class RunOracleReplay
{
    public static void AddStartingRelics(RunState rs, string characterId)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (!CharacterRegistry.TryGet(characterId, out var character))
        {
            return;
        }
        foreach (var id in character.StartingRelics)
        {
            AddRelic(rs.Players[0], id);
        }
    }

    public static void ApplyFloorEnd(RunState rs, RunFloorStats? stats)
    {
        ArgumentNullException.ThrowIfNull(rs);
        if (stats == null)
        {
            return;
        }
        var player = rs.Players[0];
        player.Gold = stats.Gold;
        if (stats.MaxHp > 0)
        {
            player.MaxHp = stats.MaxHp;
            player.CurrentHp = Math.Min(Math.Max(stats.CurrentHp, 0), stats.MaxHp);
        }
        foreach (var id in stats.PotionsUsed.Concat(stats.PotionsDiscarded))
        {
            var potion = player.Potions.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
            if (potion != null)
            {
                player.DiscardPotionInternal(potion);
            }
        }
        foreach (var id in stats.PotionsGained)
        {
            if (!PotionRegistry.Models.Contains(id))
            {
                continue;
            }
            if (!player.HasOpenPotionSlot)
            {
                player.AddToMaxPotionCount(1);
            }
            player.AddPotionInternal(PotionRegistry.Create(id));
        }
        foreach (var id in stats.RelicsGained)
        {
            AddRelic(player, id);
        }
        var gained = new List<string>(stats.CardsGained);
        foreach (var (original, final) in stats.CardsTransformed)
        {
            RemoveCard(player, original);
            if (!gained.Remove(final))
            {
                AddCard(rs, player, final);
            }
        }
        foreach (var id in stats.CardsRemoved)
        {
            RemoveCard(player, id);
        }
        foreach (var id in gained)
        {
            AddCard(rs, player, id);
        }
    }

    private static void AddRelic(PlayerState player, string id)
    {
        if (RelicRegistry.Models.Contains(id) && !player.HasRelic(id))
        {
            player.AddRelicInternal(RelicRegistry.Create(id));
        }
    }

    private static void AddCard(RunState rs, PlayerState player, string id)
    {
        if (!CardRegistry.TryGet(id, out var def))
        {
            return;
        }
        var card = new CardInstance(def, rs.AllocateCardInstanceId());
        card.AttachTo(player);
        player.Deck.Add(card);
    }

    private static void RemoveCard(PlayerState player, string id)
    {
        int i = player.Deck.FindIndex(c => string.Equals(c.Def.Id, id, StringComparison.Ordinal));
        if (i >= 0)
        {
            player.Deck.RemoveAt(i);
        }
    }
}
