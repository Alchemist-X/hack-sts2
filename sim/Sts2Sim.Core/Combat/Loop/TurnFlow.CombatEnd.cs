namespace Sts2Sim.Core.Combat.Loop;

public static partial class CombatLoop
{
    private static bool ProcessWinCondition(CombatState cs)
    {
        if (cs.Outcome == CombatOutcome.Defeat && cs.IsInProgress)
        {
            ProcessPendingLoss(cs);
            return true;
        }
        if (CombatStatus.IsEnding(cs))
        {
            EndCombatInternal(cs);
            return true;
        }
        return false;
    }

    private static void ProcessPendingLoss(CombatState cs)
    {
        cs.IsInProgress = false;
    }

    private static void EndCombatInternal(CombatState cs)
    {
        cs.IsInProgress = false;
        cs.Outcome = CombatOutcome.Victory;
        cs.PlayersTakingExtraTurn.Clear();
        SetPhaseForAllPlayers(cs, PlayerTurnPhase.None);
        var player = cs.Player;
        ReviveBeforeCombatEnd(cs, player);
        cs.PlayerHpAtCombatEnd = player.CurrentHp;
        // The game passes (CombatRoom)runState.CurrentRoom to both hooks (Combat/CombatManager.cs:1652, :1708, :1781); a
        // standalone CombatSim has no room (null). E3: FISHING_ROD / WAR_HAMMER / SWORD_OF_STONE read the room type.
        var room = RoomStack.CurrentRoom(cs.Run) as CombatRoom;
        Hook.AfterCombatEnd(cs.Run, cs, room);
        cs.History.Clear();
        PlayerAfterCombatEnd(player);
        Hook.AfterCombatVictory(cs.Run, cs, room);
    }

    private static void ReviveBeforeCombatEnd(CombatState cs, PlayerState player)
    {
        if (player.IsDead)
        {
            CreatureCmd.Heal(cs, player, 1m);
        }
    }

    private static void PlayerAfterCombatEnd(PlayerState player)
    {
        var removed = new List<PowerModel>(player.Powers.Count);
        CreatureInternals.RemoveAllPowersInternal(player, removed);
        var piles = player.AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            piles[i].ClearInternal();
        }
        player.PetIds.Clear();
        CreatureInternals.LoseBlockInternal(player, player.Block);
    }
}
