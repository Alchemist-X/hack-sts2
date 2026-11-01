namespace Sts2Sim.Core.Combat.Loop;

public static partial class CombatLoop
{
    private static bool EndTurnAndSwitchSides(CombatState cs)
    {
        if (!cs.IsInProgress || cs.CurrentSide != CombatSide.Player)
        {
            return false;
        }
        EndPlayerTurnPhaseOne(cs);
        if (!cs.IsInProgress)
        {
            return false;
        }
        if (cs.CurrentSide == CombatSide.Player)
        {
            EndPlayerTurnPhaseTwo(cs);
            SwitchFromPlayerToEnemySide(cs);
        }
        return cs.IsInProgress;
    }

    private static void EndPlayerTurnPhaseOne(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        var playersEndingTurn = PlayersOnTurn(cs);
        for (int i = 0; i < playersEndingTurn.Length; i++)
        {
            var player = playersEndingTurn[i];
            player.Phase = PlayerTurnPhase.AutoPostPlay;
            using var scope = cs.Choice.PushContext(ChoiceContextKind.Inline);
            Hook.AfterAutoPostPlayPhaseEntered(cs, player);
        }
        for (int i = 0; i < playersEndingTurn.Length; i++)
        {
            playersEndingTurn[i].Phase = PlayerTurnPhase.End;
        }
        Hook.BeforeSideTurnEnd(cs, CombatSide.Player, CreaturesOf(playersEndingTurn));
        if (ProcessWinCondition(cs))
        {
            return;
        }
        for (int i = 0; i < playersEndingTurn.Length; i++)
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.Inline);
            DoTurnEnd(cs, playersEndingTurn[i]);
        }
        if (ProcessWinCondition(cs))
        {
            return;
        }
        for (int i = 0; i < playersEndingTurn.Length; i++)
        {
            Hook.BeforeFlush(cs, playersEndingTurn[i]);
        }
        ProcessWinCondition(cs);
    }

    private static void DoTurnEnd(CombatState cs, PlayerState player)
    {
        player.OrbQueue.BeforeTurnEnd(cs);
        if (!cs.IsInProgress || CombatStatus.IsEnding(cs))
        {
            return;
        }
        var hand = player.Hand.Cards;
        List<CardInstance>? turnEndCards = null;
        List<CardInstance>? etherealCards = null;
        HashSet<CardKeyword>? scratch = null;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (card.HasTurnEndInHandEffect)
            {
                (turnEndCards ??= new List<CardInstance>(2)).Add(card);
            }
            else if (CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Ethereal, ref scratch) && Hook.ShouldEtherealTrigger(cs, card))
            {
                (etherealCards ??= new List<CardInstance>(2)).Add(card);
            }
        }
        if (etherealCards != null)
        {
            for (int i = 0; i < etherealCards.Count; i++)
            {
                CardCmd.Exhaust(cs, etherealCards[i], causedByEthereal: true);
            }
        }
        if (turnEndCards != null)
        {
            for (int i = 0; i < turnEndCards.Count; i++)
            {
                ResolveTurnEndCard(cs, turnEndCards[i]);
            }
        }
    }

    private static void ResolveTurnEndCard(CombatState cs, CardInstance card)
    {
        CardPileCmd.Add(cs, card, PileType.Play);
        card.OnTurnEndInHand(cs);
        if (CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Ethereal))
        {
            CardCmd.Exhaust(cs, card, causedByEthereal: true);
        }
        else
        {
            CardPileCmd.Add(cs, card, CardPileCmd.OwnerOf(cs, card).DiscardPile, CardPilePosition.Bottom);
        }
    }

    private static void EndPlayerTurnPhaseTwo(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        var playersEndingTurn = PlayersOnTurn(cs);
        for (int i = 0; i < playersEndingTurn.Length; i++)
        {
            using var scope = cs.Choice.PushContext(ChoiceContextKind.Inline);
            FlushPlayerHand(cs, playersEndingTurn[i]);
        }
        Hook.AfterSideTurnEnd(cs, CombatSide.Player, CreaturesOf(playersEndingTurn));
    }

    private static void FlushPlayerHand(CombatState cs, PlayerState player)
    {
        if (player.IsDead)
        {
            return;
        }
        var hand = player.Hand.Cards;
        var flushed = new List<CardInstance>(hand.Count);
        var retained = new List<CardInstance>(2);
        bool shouldFlush = Hook.ShouldFlush(cs, player);
        HashSet<CardKeyword>? scratch = null;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (!shouldFlush || CardPileCmd.ShouldRetainThisTurn(cs, card, ref scratch))
            {
                retained.Add(card);
            }
            else
            {
                flushed.Add(card);
            }
        }
        if (flushed.Count > 0)
        {
            CardPileCmd.Add(cs, flushed, PileType.Discard);
        }
        Hook.AfterFlush(cs, player, flushed, retained);
        player.EndOfTurnCleanup();
    }

    private static void SwitchFromPlayerToEnemySide(CombatState cs)
    {
        var extra = cs.PlayersTakingExtraTurn;
        extra.Clear();
        var player = cs.Player;
        if (Hook.ShouldTakeExtraTurn(cs, player))
        {
            extra.Add(player.CombatId);
        }
        var takingExtraTurn = extra.Count == 0 ? Array.Empty<PlayerState>() : PlayersOnTurn(cs);
        SwitchSides(cs);
        for (int i = 0; i < takingExtraTurn.Length; i++)
        {
            Hook.AfterTakingExtraTurn(cs, takingExtraTurn[i]);
        }
    }

    private static void SwitchSides(CombatState cs)
    {
        bool extraTurn = cs.PlayersTakingExtraTurn.Count > 0;
        if (cs.CurrentSide == CombatSide.Player && !extraTurn)
        {
            cs.CurrentSide = CombatSide.Enemy;
        }
        else
        {
            cs.CurrentSide = CombatSide.Player;
            if (extraTurn)
            {
                var players = PlayersOnTurn(cs);
                for (int i = 0; i < players.Length; i++)
                {
                    players[i].IncrementTurnNumber();
                }
            }
            else
            {
                cs.RoundNumber++;
                cs.Player.IncrementTurnNumber();
            }
        }
        var creatures = SnapshotMonsterCreatures(cs);
        for (int i = 0; i < creatures.Length; i++)
        {
            MonsterLifecycle.OnSideSwitch(creatures[i]);
        }
    }

    private static void ExecuteEnemyTurn(CombatState cs)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        var enemies = cs.Monsters.ToArray();
        for (int i = 0; i < enemies.Length; i++)
        {
            var enemy = enemies[i];
            if (!cs.Monsters.Contains(enemy))
            {
                continue;
            }
            MonsterLifecycle.TakeTurn(cs, enemy);
            ProcessWinCondition(cs);
            if (!cs.IsInProgress)
            {
                return;
            }
        }
        EndEnemyTurn(cs);
    }

    private static void EndEnemyTurn(CombatState cs)
    {
        if (!cs.IsInProgress)
        {
            return;
        }
        RequireSide(cs, CombatSide.Enemy);
        var enemies = MonsterLifecycle.EnemySideParticipants(cs);
        Hook.BeforeSideTurnEnd(cs, CombatSide.Enemy, enemies);
        cs.Player.EndOfTurnCleanup();
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy, enemies);
        ProcessWinCondition(cs);
        if (!CombatStatus.IsEnding(cs))
        {
            SwitchSides(cs);
        }
    }
}
