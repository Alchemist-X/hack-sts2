namespace Sts2Sim.Core.Run;

public static class RunLegalActionGenerator
{
    [ThreadStatic]
    private static List<GameAction>? _combatScratch;

    [ThreadStatic]
    private static List<MapPoint>? _mapScratch;

    public static void Fill(RunState rs, RunLoopState loop, ChoicePrompt? prompt, IReadOnlyList<int> picks, List<RunAction> buffer)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(buffer);
        buffer.Clear();
        if (prompt != null)
        {
            var scratch = _combatScratch ??= new List<GameAction>(32);
            scratch.Clear();
            LegalActionGenerator.FillPrompt(prompt, picks, scratch);
            AppendCombat(scratch, buffer);
            return;
        }
        switch (loop.Phase)
        {
            case RunPhase.Combat:
                FillCombat(rs, buffer);
                return;
            case RunPhase.Map:
                FillMap(rs, buffer);
                FillPotions(rs, buffer);
                return;
            case RunPhase.Rewards:
                FillRewards(rs, buffer);
                FillPotions(rs, buffer);
                buffer.Add(RunAction.Of(RunActionKind.ProceedRewards));
                return;
            case RunPhase.CardReward:
                FillCardReward(rs, loop, buffer);
                FillPotions(rs, buffer);
                return;
            case RunPhase.Shop:
                FillShop(rs, buffer);
                FillPotions(rs, buffer);
                buffer.Add(RunAction.Of(RunActionKind.LeaveShop));
                return;
            case RunPhase.RestSite:
                FillRestSite(rs, buffer);
                return;
            case RunPhase.Treasure:
                FillTreasure(rs, buffer);
                return;
            case RunPhase.Event:
                FillEvent(rs, buffer);
                return;
            default:
                return;
        }
    }

    public static bool IsLegal(RunState rs, RunLoopState loop, ChoicePrompt? prompt, IReadOnlyList<int> picks, RunAction action)
    {
        if (prompt != null)
        {
            return action.TryToCombat(out var ga) && RunAction.FromCombat(ga) == action && LegalActionGenerator.TryResolvePrompt(prompt, picks, ga, out _);
        }
        if (loop.Phase == RunPhase.Combat)
        {
            var cs = rs.Combat;
            return cs != null && action.TryToCombat(out var ga) && RunAction.FromCombat(ga) == action && ga.Kind is not (ActionKind.ChoosePick or ActionKind.ChooseConfirm or ActionKind.ChooseCancel) && LegalActionGenerator.IsLegal(cs, ga);
        }
        var buffer = new List<RunAction>(16);
        Fill(rs, loop, null, Array.Empty<int>(), buffer);
        return buffer.Contains(action);
    }

    private static void AppendCombat(List<GameAction> source, List<RunAction> buffer)
    {
        for (int i = 0; i < source.Count; i++)
        {
            buffer.Add(RunAction.FromCombat(source[i]));
        }
    }

    private static void FillCombat(RunState rs, List<RunAction> buffer)
    {
        var cs = rs.Combat;
        if (cs == null)
        {
            return;
        }
        var scratch = _combatScratch ??= new List<GameAction>(32);
        scratch.Clear();
        LegalActionGenerator.Fill(cs, scratch);
        AppendCombat(scratch, buffer);
    }

    public static void GetLegalMapPoints(RunState rs, List<MapPoint> buffer)
    {
        buffer.Clear();
        MapTravel.GetLegalMapPoints(rs, buffer);
    }

    private static void FillMap(RunState rs, List<RunAction> buffer)
    {
        var points = _mapScratch ??= new List<MapPoint>(8);
        GetLegalMapPoints(rs, points);
        for (int i = 0; i < points.Count; i++)
        {
            buffer.Add(RunAction.Of(RunActionKind.Travel, i));
        }
    }

    public static RewardsSet? TopRewards(RunState rs) => rs.RewardsStack.Count == 0 ? null : rs.RewardsStack[^1];

    private static void FillRewards(RunState rs, List<RunAction> buffer)
    {
        var set = TopRewards(rs);
        if (set == null)
        {
            return;
        }
        var rewards = set.Rewards;
        for (int i = 0; i < rewards.Count; i++)
        {
            if (!rewards[i].SuccessfullySelected)
            {
                buffer.Add(RunAction.Of(RunActionKind.ClaimReward, i));
            }
        }
    }

    public static CardReward? OpenCardReward(RunState rs, RunLoopState loop)
    {
        var set = TopRewards(rs);
        int index = loop.OpenCardReward;
        if (set == null || index < 0 || index >= set.Rewards.Count)
        {
            return null;
        }
        return set.Rewards[index] as CardReward;
    }

    private static void FillCardReward(RunState rs, RunLoopState loop, List<RunAction> buffer)
    {
        var reward = OpenCardReward(rs, loop);
        if (reward == null)
        {
            return;
        }
        int cards = reward.Cards.Count;
        for (int i = 0; i < cards; i++)
        {
            buffer.Add(RunAction.Of(RunActionKind.PickRewardCard, i));
        }
        int alternatives = reward.Alternatives.Count;
        for (int i = 0; i < alternatives; i++)
        {
            buffer.Add(RunAction.Of(RunActionKind.RewardAlternative, i));
        }
    }

    private static void FillShop(RunState rs, List<RunAction> buffer)
    {
        if (RoomStack.CurrentRoom(rs) is not MerchantRoom { Inventory: { } inventory })
        {
            return;
        }
        var entries = inventory.AllEntries;
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.IsStocked && entry.EnoughGold(rs))
            {
                buffer.Add(RunAction.Of(RunActionKind.BuyEntry, i));
            }
        }
    }

    public static bool CanLeaveRestSite(RestSiteRoom room) => room.ProceedAllowed || room.Options.Count == 0;

    private static void FillRestSite(RunState rs, List<RunAction> buffer)
    {
        if (RoomStack.CurrentRoom(rs) is not RestSiteRoom room)
        {
            return;
        }
        var options = room.Options;
        for (int i = 0; i < options.Count; i++)
        {
            if (options[i].IsEnabled)
            {
                buffer.Add(RunAction.Of(RunActionKind.ChooseRestOption, i));
            }
        }
        FillPotions(rs, buffer);
        if (CanLeaveRestSite(room))
        {
            buffer.Add(RunAction.Of(RunActionKind.LeaveRestSite));
        }
    }

    public static bool CanPickTreasureRelic(TreasureRoom room)
        => room.ChestOpened && room.CurrentRelicId != null && !room.RelicClaimed && !room.SingleplayerSkipped;

    private static void FillTreasure(RunState rs, List<RunAction> buffer)
    {
        if (RoomStack.CurrentRoom(rs) is not TreasureRoom room)
        {
            return;
        }
        if (!room.ChestOpened)
        {
            buffer.Add(RunAction.Of(RunActionKind.OpenChest));
        }
        else if (CanPickTreasureRelic(room))
        {
            buffer.Add(RunAction.Of(RunActionKind.TakeTreasureRelic));
        }
        FillPotions(rs, buffer);
        if (CanPickTreasureRelic(room))
        {
            buffer.Add(RunAction.Of(RunActionKind.SkipTreasureRelic));
        }
    }

    public static EventModel? CurrentEvent(RunState rs)
        => RoomStack.CurrentRoom(rs) is EventRoom room ? room.LocalMutableEvent ?? room.CanonicalEvent : null;

    private static void FillEvent(RunState rs, List<RunAction> buffer)
    {
        var ev = CurrentEvent(rs);
        if (ev == null)
        {
            return;
        }
        bool finished = ev.IsFinished;
        if (!finished)
        {
            var options = ev.CurrentOptions;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].IsEnabled)
                {
                    buffer.Add(RunAction.Of(RunActionKind.ChooseEventOption, i));
                }
            }
        }
        FillPotions(rs, buffer);
        if (finished)
        {
            buffer.Add(RunAction.Of(RunActionKind.LeaveEvent));
        }
    }

    public static CreatureState? ResolveOutOfCombatTarget(PotionModel potion, CreatureState? target)
    {
        var targetPlayer = target as PlayerState;
        if (targetPlayer == null && potion.TargetType != TargetType.TargetedNoCreature)
        {
            return potion.Owner;
        }
        return targetPlayer;
    }

    public static bool CanUsePotionOutOfCombat(PlayerState player, int slot)
    {
        var slots = player.PotionSlots;
        if (slot < 0 || slot >= slots.Length)
        {
            return false;
        }
        var potion = slots[slot];
        if (potion == null || potion.Owner.IsDead)
        {
            return false;
        }
        if (potion.Usage != PotionUsage.AnyTime)
        {
            return false;
        }
        if (!potion.Owner.CanUseOrRemovePotions || !potion.PassesCustomUsabilityCheck)
        {
            return false;
        }
        return potion.IsValidTarget(ResolveOutOfCombatTarget(potion, null));
    }

    public static bool CanDiscardPotionOutOfCombat(PlayerState player, int slot)
    {
        var slots = player.PotionSlots;
        if (slot < 0 || slot >= slots.Length)
        {
            return false;
        }
        var potion = slots[slot];
        return potion != null && !potion.Owner.IsDead && potion.Owner.CanUseOrRemovePotions;
    }

    private static void FillPotions(RunState rs, List<RunAction> buffer)
    {
        if (rs.Players.Count == 0)
        {
            return;
        }
        var player = rs.Players[0];
        int slots = player.PotionSlots.Length;
        for (int i = 0; i < slots; i++)
        {
            if (CanUsePotionOutOfCombat(player, i))
            {
                buffer.Add(RunAction.Of(RunActionKind.UsePotion, i, RunAction.NoIndex));
            }
        }
        for (int i = 0; i < slots; i++)
        {
            if (CanDiscardPotionOutOfCombat(player, i))
            {
                buffer.Add(RunAction.Of(RunActionKind.DiscardPotion, i));
            }
        }
    }
}
