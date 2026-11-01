namespace Sts2Sim.Core.Run;

public static class RunActionExecutor
{
    public static RunPhase Execute(RunState rs, RunLoopState loop, RunAction action)
    {
        ArgumentNullException.ThrowIfNull(rs);
        ArgumentNullException.ThrowIfNull(loop);
        if (loop.Phase == RunPhase.Combat)
        {
            ExecuteCombat(rs, action);
        }
        else
        {
            ExecuteOutOfCombat(rs, loop, action);
        }
        RunLoop.AfterAction(rs, loop);
        return loop.Phase;
    }

    private static void ExecuteCombat(RunState rs, RunAction action)
    {
        var cs = rs.Combat ?? throw new InvalidOperationException("Combat phase without a combat.");
        if (!action.TryToCombat(out var ga))
        {
            throw new InvalidOperationException($"Illegal action {action}: not a combat action.");
        }
        try
        {
            CombatLoop.Step(cs, ga);
        }
        catch (ChoiceUnwindException) when (!cs.IsInProgress)
        {
        }
    }

    private static void ExecuteOutOfCombat(RunState rs, RunLoopState loop, RunAction action)
    {
        switch (action.Kind)
        {
            case RunActionKind.UsePotion:
                UsePotion(rs, action.A);
                return;
            case RunActionKind.DiscardPotion:
                DiscardPotion(rs, action.A);
                return;
            case RunActionKind.Travel:
                Travel(rs, loop, action.A);
                return;
            case RunActionKind.ClaimReward:
                ClaimReward(rs, loop, action.A);
                return;
            case RunActionKind.ProceedRewards:
                ProceedRewards(rs, loop);
                return;
            case RunActionKind.PickRewardCard:
            {
                var reward = RequireOpenCardReward(rs, loop);
                if (reward.PickCard(rs, action.A))
                {
                    loop.OpenCardReward = RunLoopState.NoCardReward;
                }
                return;
            }
            case RunActionKind.RewardAlternative:
            {
                var reward = RequireOpenCardReward(rs, loop);
                if (reward.ChooseAlternative(rs, action.A))
                {
                    loop.OpenCardReward = RunLoopState.NoCardReward;
                }
                return;
            }
            case RunActionKind.BuyEntry:
                Require<MerchantRoom>(rs).TryPurchase(rs, action.A);
                return;
            case RunActionKind.LeaveShop:
                Require<MerchantRoom>(rs);
                loop.MapOpen = true;
                return;
            case RunActionKind.ChooseRestOption:
                Require<RestSiteRoom>(rs).ChooseOption(rs, action.A);
                return;
            case RunActionKind.LeaveRestSite:
                Require<RestSiteRoom>(rs);
                loop.MapOpen = true;
                return;
            case RunActionKind.OpenChest:
            {
                var room = Require<TreasureRoom>(rs);
                room.OpenChest(rs);
                if (room.CurrentRelicId == null)
                {
                    loop.MapOpen = true;
                }
                return;
            }
            case RunActionKind.TakeTreasureRelic:
                Require<TreasureRoom>(rs).TakeRelic(rs);
                loop.MapOpen = true;
                return;
            case RunActionKind.SkipTreasureRelic:
                Require<TreasureRoom>(rs).SkipRelic(rs);
                loop.MapOpen = true;
                return;
            case RunActionKind.ChooseEventOption:
                (RunLegalActionGenerator.CurrentEvent(rs) ?? throw new InvalidOperationException("No event room.")).ChooseOption(rs, action.A);
                return;
            case RunActionKind.LeaveEvent:
                loop.MapOpen = true;
                return;
            default:
                throw new InvalidOperationException($"Illegal action {action} in phase {loop.Phase}.");
        }
    }

    private static T Require<T>(RunState rs) where T : AbstractRoom
        => RoomStack.CurrentRoom(rs) as T ?? throw new InvalidOperationException($"The current room is not a {typeof(T).Name}.");

    private static CardReward RequireOpenCardReward(RunState rs, RunLoopState loop)
        => RunLegalActionGenerator.OpenCardReward(rs, loop) ?? throw new InvalidOperationException("No card reward is open.");

    private static void Travel(RunState rs, RunLoopState loop, int index)
    {
        var points = new List<MapPoint>(8);
        RunLegalActionGenerator.GetLegalMapPoints(rs, points);
        if (index < 0 || index >= points.Count)
        {
            throw new InvalidOperationException($"Travel({index}): {points.Count} legal map points.");
        }
        loop.MapOpen = false;
        loop.OpenCardReward = RunLoopState.NoCardReward;
        RunLoop.EnterMapCoord(rs, points[index].coord);
    }

    private static void ClaimReward(RunState rs, RunLoopState loop, int index)
    {
        var set = RunLegalActionGenerator.TopRewards(rs) ?? throw new InvalidOperationException("No rewards screen is open.");
        var reward = set.Rewards[index];
        if (reward is CardReward)
        {
            loop.OpenCardReward = index;
            return;
        }
        reward.OnSelectWrapper(rs);
    }

    private static void ProceedRewards(RunState rs, RunLoopState loop)
    {
        var set = RunLegalActionGenerator.TopRewards(rs) ?? throw new InvalidOperationException("No rewards screen is open.");
        rs.RewardsStack.RemoveAt(rs.RewardsStack.Count - 1);
        loop.OpenCardReward = RunLoopState.NoCardReward;
        var rewards = set.Rewards;
        for (int i = 0; i < rewards.Count; i++)
        {
            if (!rewards[i].SuccessfullySelected)
            {
                rewards[i].OnSkipped(rs);
            }
        }
        if (set.IsTerminal)
        {
            RunLoop.ProceedTerminal(rs, loop);
        }
    }

    private static void UsePotion(RunState rs, int slot)
    {
        var player = rs.Players[0];
        var potion = PotionActions.GetPotionAtSlotIndex(player, slot);
        if (potion == null)
        {
            return;
        }
        var target = RunLegalActionGenerator.ResolveOutOfCombatTarget(potion, null);
        if (!potion.IsValidTarget(target))
        {
            return;
        }
        using var context = rs.Choice.PushContext(ChoiceContextKind.Inline);
        potion.OnUseWrapper(rs, target);
    }

    private static void DiscardPotion(RunState rs, int slot)
    {
        var player = rs.Players[0];
        var potion = PotionActions.GetPotionAtSlotIndex(player, slot);
        if (potion == null)
        {
            return;
        }
        PotionCmd.Discard(rs, potion);
    }
}
