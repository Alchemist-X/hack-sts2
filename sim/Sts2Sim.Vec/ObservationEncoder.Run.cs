using Sts2Sim.Core;
using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;
using Sts2Sim.Core.State;

namespace Sts2Sim.Vec;

// Run tables: map, room screens (rewards, card reward, shop, rest, treasure, event) and the pending prompt.
public static partial class ObservationEncoder
{
    private const int MapW = 6;

    // map_nodes[row][col]: every point of the current act's map (grid, start, boss, second boss), visited path,
    // current point, the legal next points (Map phase only; MapTravel.GetLegalMapPoints) and quest marks.
    private static void EncodeMap(RunSim sim, RunState rs, Span<int> nodes, ref bool overflow)
    {
        var map = rs.Map;
        if (map == null)
        {
            return;
        }
        int cols = map.GetColumnCount();
        int rows = map.GetRowCount();
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (map.GetPoint(c, r) is { } pt)
                {
                    WriteMapPoint(nodes, pt, ref overflow);
                }
            }
        }
        WriteMapPoint(nodes, map.StartingMapPoint, ref overflow);
        WriteMapPoint(nodes, map.BossMapPoint, ref overflow);
        if (map.SecondBossMapPoint is { } second)
        {
            WriteMapPoint(nodes, second, ref overflow);
        }
        var visited = rs.VisitedMapCoords;
        for (int i = 0; i < visited.Count; i++)
        {
            if (Cell(nodes, visited[i].row, visited[i].col) is { Length: > 0 } cell && cell[0] != 0)
            {
                cell[1] = 1;
            }
        }
        if (rs.CurrentMapCoord is { } cur && Cell(nodes, cur.row, cur.col) is { Length: > 0 } curCell && curCell[0] != 0)
        {
            curCell[2] = 1;
        }
        if (sim.Loop.Phase == RunPhase.Map && !sim.HasPendingChoice)
        {
            var points = S.MapPoints;
            MapTravel.GetLegalMapPoints(rs, points);
            for (int i = 0; i < points.Count; i++)
            {
                if (Cell(nodes, points[i].coord.row, points[i].coord.col) is { Length: > 0 } cell)
                {
                    cell[3] = 1;
                }
            }
            points.Clear();
        }
        var quests = map.AllQuests;
        for (int i = 0; i < quests.Count; i++)
        {
            if (Cell(nodes, quests[i].Coord.row, quests[i].Coord.col) is { Length: > 0 } cell)
            {
                cell[5]++;
            }
        }
    }

    private static Span<int> Cell(Span<int> nodes, int row, int col)
        => (uint)row < Caps.MapRows && (uint)col < Caps.MapCols ? nodes.Slice((row * Caps.MapCols + col) * MapW, MapW) : Span<int>.Empty;

    private static void WriteMapPoint(Span<int> nodes, MapPoint pt, ref bool overflow)
    {
        var cell = Cell(nodes, pt.coord.row, pt.coord.col);
        if (cell.IsEmpty)
        {
            overflow = true;
            return;
        }
        cell[0] = (int)pt.PointType;
        int mask = 0;
        foreach (var child in pt.Children)
        {
            if ((uint)child.coord.col < 32u)
            {
                mask |= 1 << child.coord.col;
            }
        }
        cell[4] = mask;
    }

    private static void EncodeScreens(RunSim sim, RunState rs, PlayerState p, in SlotBuffers b, Span<int> gi, ref bool overflow)
    {
        var loopPhase = sim.Loop.Phase;
        var room = RoomStack.CurrentRoom(rs);
        switch (loopPhase)
        {
            case RunPhase.Rewards:
                EncodeRewards(rs, b.Rewards, gi, ref overflow);
                break;
            case RunPhase.CardReward:
                EncodeRewards(rs, b.Rewards, gi, ref overflow);
                EncodeCardReward(rs, sim.Loop, b.CardReward, b.CardRewardAlts, gi, ref overflow);
                break;
            case RunPhase.Shop when room is MerchantRoom { Inventory: { } inventory }:
                EncodeShop(rs, p, inventory, b.Shop, gi, ref overflow);
                break;
            case RunPhase.RestSite when room is RestSiteRoom rest:
                EncodeRest(rest, b.Rest, gi, ref overflow);
                break;
            case RunPhase.Treasure when room is TreasureRoom treasure:
                gi[GI.TreasureOpened] = treasure.ChestOpened ? 1 : 0;
                // H6: the relic is shown only after the chest is opened.
                gi[GI.TreasureRelicId] = treasure.ChestOpened ? Vocab.Relic(treasure.CurrentRelicId) : 0;
                gi[GI.TreasureClaimed] = treasure.RelicClaimed ? 1 : treasure.SingleplayerSkipped ? 2 : 0;
                break;
            case RunPhase.Event:
                EncodeEvent(rs, b.EventOptions, b.EventOptionValues, gi, ref overflow);
                break;
        }
    }

    // Row i = RewardsSet.Rewards[i] (= ClaimReward A); claimed rewards leave their row zero (reward_type None).
    // H7: a card reward is listed as is_card_reward = 1 without its cards.
    private static void EncodeRewards(RunState rs, Span<int> table, Span<int> gi, ref bool overflow)
    {
        const int w = 4;
        var set = RunLegalActionGenerator.TopRewards(rs);
        if (set == null)
        {
            return;
        }
        var rewards = set.Rewards;
        int unclaimed = 0;
        for (int i = 0; i < rewards.Count; i++)
        {
            var reward = rewards[i];
            if (reward.SuccessfullySelected)
            {
                continue;
            }
            unclaimed++;
            if (i >= Caps.Rewards)
            {
                overflow = true;
                continue;
            }
            var row = table.Slice(i * w, w);
            row[0] = (int)reward.RewardType;
            switch (reward)
            {
                case GoldReward gold:
                    row[1] = Math.Max(0, gold.Amount);
                    break;
                case PotionReward potion:
                    row[2] = Vocab.Potion(potion.PotionId);
                    break;
                case RelicReward relic:
                    row[2] = Vocab.Relic(relic.RelicId);
                    break;
                case SpecialCardReward special:
                    row[2] = Vocab.Card(special.Card.Id);
                    break;
                case CardReward:
                    row[3] = 1;
                    break;
            }
        }
        gi[GI.RewardN] = unclaimed;
    }

    private static void EncodeCardReward(RunState rs, RunLoopState loop, Span<int> table, Span<int> alts, Span<int> gi, ref bool overflow)
    {
        var reward = RunLegalActionGenerator.OpenCardReward(rs, loop);
        if (reward == null)
        {
            return;
        }
        int hc = Schema.HC;
        var cards = reward.Cards;
        int n = cards.Count;
        if (n > Caps.CardReward)
        {
            overflow = true;
            n = Caps.CardReward;
        }
        for (int i = 0; i < n; i++)
        {
            WriteCard(table.Slice(i * hc, hc), cards[i].Card, null);
        }
        var alternatives = reward.Alternatives;
        for (int i = 0; i < alternatives.Count; i++)
        {
            var alt = alternatives[i];
            if (alt.OptionId == CardRewardAlternative.SkipId)
            {
                gi[GI.CardRewardCanSkip] = 1;
                gi[GI.CardRewardSkipConsumes] = alt.CompletesReward ? 1 : 0;
            }
            if (i >= Caps.CardRewardAlts)
            {
                overflow = true;
                continue;
            }
            alts[i] = Vocab.CardRewardAlt(alt.OptionId);
        }
    }

    // Row i = MerchantInventory.AllEntries[i] (= BuyEntry A).
    private static void EncodeShop(RunState rs, PlayerState p, MerchantInventory inventory, Span<int> table, Span<int> gi, ref bool overflow)
    {
        const int w = 7;
        var entries = inventory.AllEntries;
        int n = entries.Count;
        gi[GI.ShopEntryN] = n;
        if (n > Caps.Shop)
        {
            overflow = true;
            n = Caps.Shop;
        }
        int characterCards = inventory.CharacterCardEntries.Count;
        for (int i = 0; i < n; i++)
        {
            var entry = entries[i];
            var row = table.Slice(i * w, w);
            bool stocked = entry.IsStocked;
            int price = entry.GetCost(rs);
            switch (entry)
            {
                case MerchantCardEntry card:
                    row[0] = Vocab.ShopCategory(i < characterCards ? "card" : "colorless_card");
                    if (card.CreationResult is { } created)
                    {
                        row[1] = Vocab.Card(created.Card.Id);
                        row[2] = created.Card.UpgradeLevel;
                    }
                    row[5] = card.IsOnSale ? 1 : 0;
                    break;
                case MerchantRelicEntry relic:
                    row[0] = Vocab.ShopCategory("relic");
                    row[1] = Vocab.Relic(relic.Model?.Id);
                    break;
                case MerchantPotionEntry potion:
                    row[0] = Vocab.ShopCategory("potion");
                    row[1] = Vocab.Potion(potion.Model?.Id);
                    break;
                case MerchantCardRemovalEntry:
                    row[0] = Vocab.ShopCategory("card_removal");
                    gi[GI.RemovalPrice] = stocked ? price : -1;
                    break;
                default:
                    row[0] = VocabList.Unknown;
                    break;
            }
            row[3] = price;
            row[4] = stocked ? 1 : 0;
            row[6] = stocked && price <= p.Gold ? 1 : 0;
        }
    }

    // Row i = RestSiteRoom.Options[i] (= ChooseRestOption A).
    private static void EncodeRest(RestSiteRoom room, Span<int> table, Span<int> gi, ref bool overflow)
    {
        var options = room.Options;
        int n = options.Count;
        gi[GI.RestOptionN] = n;
        if (n > Caps.Rest)
        {
            overflow = true;
            n = Caps.Rest;
        }
        for (int i = 0; i < n; i++)
        {
            table[i * 2] = Vocab.RestOption(options[i].OptionId);
            table[i * 2 + 1] = options[i].IsEnabled ? 1 : 0;
        }
    }

    // Row i = EventModel.CurrentOptions[i] (= ChooseEventOption A), while the event is not finished. H11: no event
    // internals; an option is identified by the hash of "<eventId>:<TextKey>".
    private static void EncodeEvent(RunState rs, Span<int> table, Span<int> values, Span<int> gi, ref bool overflow)
    {
        const int w = 6;
        var ev = RunLegalActionGenerator.CurrentEvent(rs);
        if (ev == null)
        {
            return;
        }
        gi[GI.EventId] = Vocab.Event(ev.Id);
        gi[GI.EventFinished] = ev.IsFinished ? 1 : 0;
        if (ev.IsFinished)
        {
            return;
        }
        var options = ev.CurrentOptions;
        int n = options.Count;
        gi[GI.EventOptionN] = n;
        if (n > Caps.EventOptions)
        {
            overflow = true;
            n = Caps.EventOptions;
        }
        for (int i = 0; i < n; i++)
        {
            var opt = options[i];
            var row = table.Slice(i * w, w);
            row[0] = HashText(ev.Id, opt.TextKey);
            row[1] = i;
            row[2] = opt.IsEnabled ? 1 : 0;
            row[3] = opt.IsLocked ? 1 : 0;
            row[4] = opt.IsProceed ? 1 : 0;
            row[5] = Vocab.Relic(opt.RelicId);
        }
        EncodeEventOptionValues(ev, rs, values, ref overflow);
    }

    /// <summary>
    /// <c>event_option_values</c> row i = the numbers and items option i's text names (EventOptionVars: the game's
    /// localisation placeholders, resolved on the live event like the play server's option text), in the text's order:
    /// value_n numbers (first CAP_EVENT_VALUES in value_k), item_n items (first CAP_EVENT_ITEMS as (ItemKind, id) in
    /// item_kind_k / item_id_k). Written while the event is not finished; more values or items than the caps set
    /// TABLE_OVERFLOW (value_n / item_n keep the true counts).
    /// </summary>
    public static void EncodeEventOptionValues(EventModel ev, RunState rs, Span<int> table, ref bool overflow)
    {
        if (ev.IsFinished)
        {
            return;
        }
        int w = Schema.EventOptionValueFields.Length;
        var options = ev.CurrentOptions;
        int n = Math.Min(options.Count, Caps.EventOptions);
        for (int i = 0; i < n; i++)
        {
            var opt = options[i];
            var names = EventOptionVars.NamesOf(opt.TextKey);
            if (names == null)
            {
                continue;
            }
            var row = table.Slice(i * w, w);
            int nv = 0;
            int ni = 0;
            foreach (string name in names)
            {
                switch (EventOptionVars.Resolve(ev, opt, rs, name, out int number, out string? kind, out int id))
                {
                    case EventOptionVars.ValueKind.Number:
                        if (nv < Caps.EventValues)
                        {
                            row[EvValCol.Value0 + nv] = number;
                        }
                        nv++;
                        break;
                    case EventOptionVars.ValueKind.Item:
                        if (ni < Caps.EventItems)
                        {
                            row[EvValCol.ItemKind0 + ni * EvValCol.ItemStride] = Vocab.ItemKind(kind);
                            row[EvValCol.ItemId0 + ni * EvValCol.ItemStride] = id;
                        }
                        ni++;
                        break;
                }
            }
            row[EvValCol.ValueN] = nv;
            row[EvValCol.ItemN] = ni;
            if (nv > Caps.EventValues || ni > Caps.EventItems)
            {
                overflow = true;
            }
        }
    }

    // prompt_options row i = ChoicePrompt.Options[i] (= PromptSelect A). H13: options are identified by
    // (item_id, upgrade, ordinal), never by instance id. picked = 1-based position in PendingPicks (0 = not picked).
    private static void EncodePrompt(ChoicePrompt prompt, IReadOnlyList<int> picks, Span<int> table, Span<int> gi, ref bool overflow)
    {
        const int w = 5;
        gi[GI.PromptKind] = (int)prompt.Kind;
        gi[GI.PromptMin] = prompt.MinSelect;
        gi[GI.PromptMax] = prompt.MaxSelect;
        gi[GI.PromptPicked] = picks.Count;
        gi[GI.PromptNeedsConfirm] = prompt.RequireConfirm ? 1 : 0;
        gi[GI.PromptCanCancel] = prompt.Cancelable ? 1 : 0;
        gi[GI.PromptCanSkip] = prompt.CanSkip ? 1 : 0;
        gi[GI.PromptOrdered] = prompt.Ordered ? 1 : 0;
        gi[GI.PromptPile] = prompt.Pile is { } pile ? (int)pile : 0;
        gi[GI.PromptSiteHash] = HashText(prompt.SiteId);
        var options = prompt.Options;
        int n = options.Count;
        gi[GI.PromptOptionN] = n;
        if (n > Caps.PromptOptions)
        {
            overflow = true;
            n = Caps.PromptOptions;
        }
        bool bundle = prompt.Kind == ChoicePromptKind.Bundle;
        for (int i = 0; i < n; i++)
        {
            var o = options[i];
            var row = table.Slice(i * w, w);
            string id = o.ModelId;
            int card = Vocab.Cards.Index(id);
            if (!bundle && card > VocabList.Unknown)
            {
                row[0] = card;
                row[3] = Vocab.PromptItemKind("card");
            }
            else if (!bundle && Vocab.Relics.Index(id) is var relic && relic > VocabList.Unknown)
            {
                row[0] = relic;
                row[3] = Vocab.PromptItemKind("relic");
            }
            else if (!bundle && Vocab.Potions.Index(id) is var potion && potion > VocabList.Unknown)
            {
                row[0] = potion;
                row[3] = Vocab.PromptItemKind("potion");
            }
            else
            {
                row[0] = string.IsNullOrEmpty(id) ? 0 : HashText(id);
                row[3] = Vocab.PromptItemKind(bundle ? "bundle" : "index");
            }
            row[1] = o.UpgradeLevel;
            for (int k = 0; k < picks.Count; k++)
            {
                if (picks[k] == i)
                {
                    row[2] = k + 1;
                    break;
                }
            }
            row[4] = i;
        }
    }
}
