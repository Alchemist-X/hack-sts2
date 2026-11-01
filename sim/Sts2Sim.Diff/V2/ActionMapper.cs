using Sts2Sim.Core.Choice;
using Sts2Sim.Core.Run;

namespace Sts2Sim.Diff.V2;

public static class ActionMapper
{
    public const string UnsupportedNote = "no sim counterpart";

    public static FieldDiff? ApplyCombat(CombatDriver drv, V2Action a)
    {
        switch (a.Type)
        {
            case V2Action.PlayCard:
            case V2Action.EndTurn:
                return ApplyPlayOrEnd(drv, a);
            case V2Action.UsePotion:
            case V2Action.DiscardPotion:
                return ApplyPotion(drv, a);
            case V2Action.Choose:
                return ApplyChoose(drv, a.Picks, a.Bool("skipped") == true, a.Bool("confirm"));
            case V2Action.UndoEndTurn:
                return new FieldDiff("action.type", a.Type, null, DiffSeverity.Hard, "undo_end_turn: " + UnsupportedNote);
            default:
                return new FieldDiff("action.type", a.Type, null, DiffSeverity.Hard, "not a combat action");
        }
    }

    private static FieldDiff? ApplyPlayOrEnd(CombatDriver drv, V2Action a)
    {
        if (drv.PendingChoice is { } prompt)
        {
            return new FieldDiff("action.prompt", null, prompt.SiteId, DiffSeverity.Hard, $"the sim waits on a {prompt.Kind} prompt but the oracle action is {a.Type}");
        }
        var cs = drv.State;
        if (cs == null)
        {
            return new FieldDiff("action.combat", a.Type, null, DiffSeverity.Hard, "the sim has no combat");
        }
        var v1 = a.ToV1()!;
        if (!BundleRunner.TryMapAction(cs, v1, out var ga, out var diff))
        {
            return diff;
        }
        if (!drv.IsLegal(ga, out var error))
        {
            return new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {ga}: {error}");
        }
        drv.Step(ga);
        return null;
    }

    private static FieldDiff? ApplyPotion(CombatDriver drv, V2Action a)
    {
        if (drv.PendingChoice is { } prompt)
        {
            return new FieldDiff("action.prompt", null, prompt.SiteId, DiffSeverity.Hard, $"the sim waits on a {prompt.Kind} prompt but the oracle action is {a.Type}");
        }
        var cs = drv.State;
        if (cs == null)
        {
            return new FieldDiff("action.combat", a.Type, null, DiffSeverity.Hard, "the sim has no combat");
        }
        int slot = a.Int("slot") ?? -1;
        var slots = cs.Player.PotionSlots;
        if (slot < 0 || slot >= slots.Length || slots[slot] == null)
        {
            return new FieldDiff("action.slot", slot, slot >= 0 && slot < slots.Length ? null : slots.Length, DiffSeverity.Hard, "no sim potion in this slot");
        }
        if (a.Str("potion_id") is { } pid && !string.Equals(RunScreenProjector.StripCategory(pid), slots[slot]!.Id, StringComparison.Ordinal))
        {
            return new FieldDiff("action.potion_id", pid, slots[slot]!.Id, DiffSeverity.Hard);
        }
        GameAction ga;
        if (a.Type == V2Action.DiscardPotion)
        {
            ga = GameAction.DiscardPotion(slot);
        }
        else
        {
            int target = GameAction.NoIndex;
            if (a.Int("target_combat_id") is int targetId && cs.Monsters.Any(m => m.CombatId == targetId))
            {
                target = BundleRunner.IndexOfMonster(cs, targetId);
            }
            ga = GameAction.UsePotion(slot, target);
        }
        if (!drv.IsLegal(ga, out var error))
        {
            return new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {ga}: {error}");
        }
        drv.Step(ga);
        return null;
    }

    public static int ResolvePick(ChoicePrompt prompt, IReadOnlyList<int> picked, ChoosePick pick, out FieldDiff? diff)
    {
        diff = null;
        var options = prompt.Options;
        string? id = pick.CardId != null ? RunScreenProjector.StripCategory(pick.CardId) : null;
        if (pick.Index is int index)
        {
            if (index < 0 || index >= options.Count)
            {
                diff = new FieldDiff("action.picks.index", index, options.Count, DiffSeverity.Hard, "pick index outside the sim prompt (sim value = option count)");
                return -1;
            }
            if (id != null && !string.Equals(options[index].ModelId, id, StringComparison.Ordinal))
            {
                diff = new FieldDiff("action.picks.card_id", id, options[index].ModelId, DiffSeverity.Hard);
                return -1;
            }
            if (pick.Upgraded is bool up && up != options[index].UpgradeLevel > 0)
            {
                diff = new FieldDiff("action.picks.upgraded", up, options[index].UpgradeLevel > 0, DiffSeverity.Hard);
                return -1;
            }
            return index;
        }
        for (int i = 0; i < options.Count; i++)
        {
            if (picked.Contains(i) || !string.Equals(options[i].ModelId, id, StringComparison.Ordinal))
            {
                continue;
            }
            if (pick.Upgraded is bool up && up != options[i].UpgradeLevel > 0)
            {
                continue;
            }
            return i;
        }
        diff = new FieldDiff("action.picks.card_id", id, null, DiffSeverity.Hard, "no unpicked sim option has this id");
        return -1;
    }

    public static FieldDiff? ApplyChoose(CombatDriver drv, IReadOnlyList<ChoosePick> picks, bool skipped, bool? confirm)
    {
        if (drv.PendingChoice is not { } prompt)
        {
            return new FieldDiff("action.prompt", "choose", null, DiffSeverity.Hard, "the oracle chose but the sim has no open prompt");
        }
        var picked = new List<int>();
        for (int k = 0; k < picks.Count; k++)
        {
            if (drv.PendingChoice is not { } current || !ReferenceEquals(current, prompt) && picked.Count > 0)
            {
                return new FieldDiff("action.picks", picks.Count, picked.Count, DiffSeverity.Hard, "the sim prompt completed before every oracle pick was made");
            }
            int option = ResolvePick(prompt, drv.PendingPicks, picks[k], out var diff);
            if (diff != null)
            {
                return diff;
            }
            var ga = GameAction.Pick(option);
            if (!drv.IsLegal(ga, out var error))
            {
                return new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {ga}: {error}");
            }
            drv.Step(ga);
            picked.Add(option);
        }
        bool stillOpen = drv.PendingChoice != null && (picked.Count == 0 || drv.PendingPicks.Count == picked.Count);
        if (stillOpen && confirm != false)
        {
            if (drv.IsLegal(GameAction.Confirm, out _))
            {
                drv.Step(GameAction.Confirm);
            }
            else if ((skipped || picked.Count == 0) && drv.IsLegal(GameAction.Cancel, out _))
            {
                drv.Step(GameAction.Cancel);
            }
            else
            {
                return new FieldDiff("action.confirm", true, false, DiffSeverity.Hard, $"the sim prompt stays open after {picked.Count} picks and accepts neither Confirm nor Cancel");
            }
        }
        return null;
    }

    public static FieldDiff? ApplyRun(RunSim sim, V2Action a)
    {
        if (V2Action.CombatTypes.Contains(a.Type))
        {
            if (a.Type is V2Action.UsePotion or V2Action.DiscardPotion && sim.Phase != RunPhase.Combat)
            {
                return ApplyOutOfCombatPotion(sim, a);
            }
            if (a.Type == V2Action.Choose && sim.State.Combat is not { IsInProgress: true })
            {
                return ApplyChoose(new RunCombatDriver(sim), a.Picks, a.Bool("skipped") == true, a.Bool("confirm"));
            }
            return ApplyCombat(new RunCombatDriver(sim), a);
        }
        if (a.Type == V2Action.RelicSelect)
        {
            var pick = new ChoosePick(a.Int("index"), null, null, a.Str("relic_id"), null);
            return ApplyChoose(new RunCombatDriver(sim), new[] { pick }, false, null);
        }
        if (!TryMapRun(sim, a, out var action, out var diff))
        {
            return diff;
        }
        if (!sim.IsLegal(action))
        {
            return new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {action} in phase {sim.Phase}");
        }
        sim.Step(action);
        return null;
    }

    private static FieldDiff? ApplyOutOfCombatPotion(RunSim sim, V2Action a)
    {
        int slot = a.Int("slot") ?? -1;
        var player = sim.State.Players[0];
        if (slot < 0 || slot >= player.PotionSlots.Length || player.PotionSlots[slot] == null)
        {
            return new FieldDiff("action.slot", slot, null, DiffSeverity.Hard, "no sim potion in this slot");
        }
        if (a.Str("potion_id") is { } pid && !string.Equals(RunScreenProjector.StripCategory(pid), player.PotionSlots[slot]!.Id, StringComparison.Ordinal))
        {
            return new FieldDiff("action.potion_id", pid, player.PotionSlots[slot]!.Id, DiffSeverity.Hard);
        }
        var action = a.Type == V2Action.UsePotion ? RunAction.Of(RunActionKind.UsePotion, slot, RunAction.NoIndex) : RunAction.Of(RunActionKind.DiscardPotion, slot);
        if (!sim.IsLegal(action))
        {
            return new FieldDiff("action.legal", true, false, DiffSeverity.Hard, $"sim rejects {action} in phase {sim.Phase}");
        }
        sim.Step(action);
        return null;
    }

    public static bool TryMapRun(RunSim sim, V2Action a, out RunAction action, out FieldDiff? diff)
    {
        action = default;
        diff = null;
        var rs = sim.State;
        switch (a.Type)
        {
            case V2Action.MapMove:
            {
                var target = new MapCoord(a.Int("col") ?? -1, a.Int("row") ?? -1);
                var legal = new List<MapPoint>();
                if (rs.Map != null)
                {
                    RunLegalActionGenerator.GetLegalMapPoints(rs, legal);
                }
                int index = legal.FindIndex(p => p.coord == target);
                if (index < 0)
                {
                    diff = new FieldDiff("action.map_move", RunScreenProjector.Coord(target), new JsonArray(legal.Select(p => (JsonNode?)RunScreenProjector.Coord(p.coord)).ToArray()), DiffSeverity.Hard, "the coord is not a legal sim map point");
                    return false;
                }
                action = RunAction.Of(RunActionKind.Travel, index);
                return true;
            }
            case V2Action.NextAct:
                action = RunAction.Of(RunActionKind.ProceedRewards);
                return true;
            case V2Action.RewardsSkip:
                action = RunAction.Of(RunActionKind.ProceedRewards);
                return true;
            case V2Action.RewardTake:
                return MapRewardTake(sim, a, out action, out diff);
            case V2Action.CardRewardPick:
            {
                var reward = RunLegalActionGenerator.OpenCardReward(rs, sim.Loop);
                int index = a.Int("index") ?? -1;
                if (reward == null)
                {
                    diff = new FieldDiff("action.card_reward", index, null, DiffSeverity.Hard, "no open sim card reward");
                    return false;
                }
                string? id = a.Str("card_id") is { } cid ? RunScreenProjector.StripCategory(cid) : null;
                if (index < 0 && id != null)
                {
                    index = reward.Cards.ToList().FindIndex(c => c.Card.Id == id);
                }
                if (index < 0 || index >= reward.Cards.Count)
                {
                    diff = new FieldDiff("action.index", index, reward.Cards.Count, DiffSeverity.Hard, "card index outside the sim offer (sim value = offer size)");
                    return false;
                }
                if (id != null && reward.Cards[index].Card.Id != id)
                {
                    diff = new FieldDiff("action.card_id", id, reward.Cards[index].Card.Id, DiffSeverity.Hard);
                    return false;
                }
                action = RunAction.Of(RunActionKind.PickRewardCard, index);
                return true;
            }
            case V2Action.CardRewardSkip:
            {
                var reward = RunLegalActionGenerator.OpenCardReward(rs, sim.Loop);
                int alt = reward == null ? -1 : reward.Alternatives.ToList().FindIndex(x => x.OptionId == CardRewardAlternative.SkipId);
                if (alt < 0)
                {
                    diff = new FieldDiff("action.card_reward_skip", true, false, DiffSeverity.Hard, "the sim card reward has no Skip alternative");
                    return false;
                }
                action = RunAction.Of(RunActionKind.RewardAlternative, alt);
                return true;
            }
            case V2Action.TreasureOpen:
                action = RunAction.Of(RunActionKind.OpenChest);
                return true;
            case V2Action.TreasurePick:
                action = RunAction.Of(a.Bool("skipped") == true ? RunActionKind.SkipTreasureRelic : RunActionKind.TakeTreasureRelic);
                if (a.Bool("skipped") != true && a.Str("relic_id") is { } rid && RoomStack.CurrentRoom(rs) is TreasureRoom tr && tr.CurrentRelicId != RunScreenProjector.StripCategory(rid))
                {
                    diff = new FieldDiff("action.relic_id", rid, tr.CurrentRelicId, DiffSeverity.Hard);
                    return false;
                }
                return true;
            case V2Action.ShopBuy:
            case V2Action.ShopRemove:
                return MapShop(sim, a, out action, out diff);
            case V2Action.ShopLeave:
                action = RunAction.Of(RunActionKind.LeaveShop);
                return true;
            case V2Action.RestOption:
            {
                string id = a.Str("option_id")!;
                var options = RoomStack.CurrentRoom(rs) is RestSiteRoom room ? room.Options : new List<RestSiteOption>();
                int index = options.FindIndex(o => string.Equals(o.OptionId, id, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    diff = new FieldDiff("action.option_id", id, new JsonArray(options.Select(o => (JsonNode?)JsonValue.Create(o.OptionId)).ToArray()), DiffSeverity.Hard, "no sim rest option has this id");
                    return false;
                }
                action = RunAction.Of(RunActionKind.ChooseRestOption, index);
                return true;
            }
            case V2Action.RestLeave:
                action = RunAction.Of(RunActionKind.LeaveRestSite);
                return true;
            case V2Action.EventOption:
            case V2Action.EventProceed:
                return MapEvent(sim, a, out action, out diff);
            default:
                diff = new FieldDiff("action.type", a.Type, null, DiffSeverity.Hard, UnsupportedNote);
                return false;
        }
    }

    private static bool MapRewardTake(RunSim sim, V2Action a, out RunAction action, out FieldDiff? diff)
    {
        action = default;
        diff = null;
        var set = RunLegalActionGenerator.TopRewards(sim.State);
        if (set == null)
        {
            diff = new FieldDiff("action.reward", a.Raw.DeepClone(), null, DiffSeverity.Hard, "no sim rewards screen");
            return false;
        }
        if (a.Int("index") is int index)
        {
            if (index < 0 || index >= set.Rewards.Count)
            {
                diff = new FieldDiff("action.index", index, set.Rewards.Count, DiffSeverity.Hard, "reward index outside the sim set (sim value = set size)");
                return false;
            }
            action = RunAction.Of(RunActionKind.ClaimReward, index);
            return true;
        }
        string? type = a.Str("reward_type");
        string? item = a.Str("item_id") is { } it ? RunScreenProjector.StripCategory(it) : null;
        for (int i = 0; i < set.Rewards.Count; i++)
        {
            var reward = set.Rewards[i];
            if (reward.SuccessfullySelected || (type != null && RunScreenProjector.RewardTypeName(reward.RewardType) != type))
            {
                continue;
            }
            if (item != null)
            {
                string? simItem = reward switch { PotionReward p => p.PotionId, RelicReward r => r.RelicId, _ => null };
                if (simItem != null && simItem != item)
                {
                    continue;
                }
            }
            action = RunAction.Of(RunActionKind.ClaimReward, i);
            return true;
        }
        diff = new FieldDiff("action.reward_type", type, null, DiffSeverity.Hard, $"no unclaimed sim reward matches ({type}, {item})");
        return false;
    }

    private static bool MapShop(RunSim sim, V2Action a, out RunAction action, out FieldDiff? diff)
    {
        action = default;
        diff = null;
        if (RoomStack.CurrentRoom(sim.State) is not MerchantRoom { Inventory: { } inventory })
        {
            diff = new FieldDiff("action.shop", a.Type, null, DiffSeverity.Hard, "the sim is not in a shop");
            return false;
        }
        var entries = inventory.AllEntries;
        string? category = a.Type == V2Action.ShopRemove ? "card_removal" : a.Str("category");
        string? item = a.Str("item_id") is { } it ? RunScreenProjector.StripCategory(it) : null;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            string cat = RunScreenProjector.EntryCategory(e);
            bool categoryOk = category == null || cat == category || (category == "card" && cat == "colorless_card");
            if (!categoryOk || !e.IsStocked)
            {
                continue;
            }
            if (item != null && RunScreenProjector.EntryItemId(e) != item)
            {
                continue;
            }
            if (a.Int("price") is int price && e.GetCost(sim.State) != price)
            {
                diff = new FieldDiff("action.price", price, e.GetCost(sim.State), DiffSeverity.Hard);
                return false;
            }
            action = RunAction.Of(RunActionKind.BuyEntry, i);
            return true;
        }
        diff = new FieldDiff("action.item_id", item ?? category, null, DiffSeverity.Hard, "no stocked sim shop entry matches");
        return false;
    }

    private static bool MapEvent(RunSim sim, V2Action a, out RunAction action, out FieldDiff? diff)
    {
        action = default;
        diff = null;
        var ev = RunLegalActionGenerator.CurrentEvent(sim.State);
        if (ev == null)
        {
            diff = new FieldDiff("action.event", a.Type, null, DiffSeverity.Hard, "the sim is not in an event");
            return false;
        }
        if (ev.IsFinished)
        {
            if (a.Type == V2Action.EventProceed)
            {
                action = RunAction.Of(RunActionKind.LeaveEvent);
                return true;
            }
            diff = new FieldDiff("action.event_option", a.Str("text_key"), null, DiffSeverity.Hard, "the sim event is finished");
            return false;
        }
        var options = ev.CurrentOptions;
        string? key = a.Str("text_key");
        int index = -1;
        if (key != null)
        {
            for (int i = 0; i < options.Count; i++)
            {
                if (string.Equals(options[i].TextKey, key, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                diff = new FieldDiff("action.text_key", key, new JsonArray(options.Select(o => (JsonNode?)JsonValue.Create(o.TextKey)).ToArray()), DiffSeverity.Hard, "no sim event option has this key");
                return false;
            }
        }
        else if (a.Int("index") is int i0)
        {
            index = i0;
        }
        else if (a.Type == V2Action.EventProceed)
        {
            index = options.ToList().FindIndex(o => o.IsProceed);
        }
        if (index < 0 || index >= options.Count)
        {
            diff = new FieldDiff("action.index", index, options.Count, DiffSeverity.Hard, "event option outside the sim options (sim value = option count)");
            return false;
        }
        action = RunAction.Of(RunActionKind.ChooseEventOption, index);
        return true;
    }
}
