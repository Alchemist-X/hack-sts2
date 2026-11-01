namespace Sts2Sim.Core.Combat.Loop;

public static partial class CombatLoop
{
    internal const string DeadPlayerReadyAtPauseSite = "CombatLoop.StartTurn:dead-player-ready-during-turn-setup-pause";

    internal const string CombatEndedAtPauseSite = "CombatLoop.StartTurn:combat-ended-at-turn-setup-pause";

    private static void RunTurnLoop(CombatState cs)
    {
        for (int iteration = 0; cs.IsInProgress; iteration++)
        {
            if (iteration >= MaxTurnLoopIterationsPerStep)
            {
                throw new InvalidOperationException($"The turn loop ran {MaxTurnLoopIterationsPerStep} turns without reaching a player decision.");
            }
            if (cs.CurrentSide == CombatSide.Player)
            {
                if (!StartTurn(cs))
                {
                    return;
                }
                if (!EndTurnAndSwitchSides(cs))
                {
                    return;
                }
                continue;
            }
            StartTurn(cs);
        }
    }

    private static bool StartTurn(CombatState cs)
    {
        if (!cs.IsInProgress)
        {
            return false;
        }
        SetPhaseForAllPlayers(cs, PlayerTurnPhase.None);
        var side = cs.CurrentSide;
        bool playerSide = side == CombatSide.Player;
        bool isExtraPlayerTurn = cs.PlayersTakingExtraTurn.Count > 0;
        var creaturesStartingTurn = playerSide ? MonsterLifecycle.PlayerSideParticipants(cs) : MonsterLifecycle.EnemySideParticipants(cs);
        var playersStartingTurn = playerSide ? PlayersOnTurn(cs) : Array.Empty<PlayerState>();
        for (int i = 0; i < creaturesStartingTurn.Count; i++)
        {
            CreatureCmd.BeforeTurnStart(cs, creaturesStartingTurn[i], side);
        }
        Hook.BeforeSideTurnStart(cs, side, creaturesStartingTurn);
        if (playerSide)
        {
            SetPhaseForAllPlayers(cs, PlayerTurnPhase.Start);
            cs.PlayersReadyToEndTurn.Clear();
            if (!isExtraPlayerTurn)
            {
                var monsters = cs.Monsters;
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterLifecycle.PrepareForNextTurn(cs, monsters[i]);
                }
            }
        }
        for (int i = 0; i < creaturesStartingTurn.Count; i++)
        {
            CreatureCmd.AfterTurnStart(cs, creaturesStartingTurn[i], side);
        }
        for (int i = 0; i < creaturesStartingTurn.Count; i++)
        {
            Hook.AfterBlockCleared(cs, creaturesStartingTurn[i]);
        }
        if (!playerSide)
        {
            Hook.AfterSideTurnStart(cs, side, creaturesStartingTurn);
            ProcessWinCondition(cs);
            if (cs.IsInProgress)
            {
                ExecuteEnemyTurn(cs);
            }
            return false;
        }
        var steps = new TurnStartSteps(cs, side, creaturesStartingTurn, playersStartingTurn);
        try
        {
            for (int i = 0; i < playersStartingTurn.Length; i++)
            {
                using var scope = cs.Choice.PushContext(ChoiceContextKind.TurnSetup, steps.OnSetupFirstPause);
                SetupPlayerTurn(cs, playersStartingTurn[i]);
            }
            steps.RunAfterSideTurnStart();
            steps.RunOrbTriggers();
            if (steps.RunDeadPlayerCheck())
            {
                return true;
            }
            for (int i = 0; i < playersStartingTurn.Length; i++)
            {
                var player = playersStartingTurn[i];
                if (player.IsDead)
                {
                    continue;
                }
                using var scope = cs.Choice.PushContext(ChoiceContextKind.TurnSetup, steps.OnAutoPrePlayFirstPause);
                RunAutoPrePlayPhase(cs, player);
            }
            steps.RunWinCheck();
            return cs.IsInProgress && cs.CurrentSide == CombatSide.Player && cs.AllPlayersReadyToEndTurn();
        }
        catch (TurnSetupUnwindException) when (!cs.IsInProgress)
        {
            return false;
        }
    }

    private static void SetupPlayerTurn(CombatState cs, PlayerState player)
    {
        if (player.IsDead)
        {
            return;
        }
        if (Hook.ShouldPlayerResetEnergy(cs, player))
        {
            EnergyCmd.ResetEnergy(cs, player);
        }
        else
        {
            EnergyCmd.AddMaxEnergyToCurrent(cs, player);
        }
        Hook.AfterEnergyReset(cs, player);
        Hook.BeforeHandDraw(cs, player);
        List<AbstractModel>? modifiers = null;
        decimal handDraw = Hook.ModifyHandDraw(cs, player, BaseHandDrawCount, ref modifiers);
        Hook.AfterModifyingHandDraw(cs, modifiers);
        if (player.TurnNumber == 1)
        {
            handDraw = PrepareFirstTurnDrawPile(cs, player, handDraw);
        }
        CardPileCmd.Draw(cs, handDraw, player, fromHandDraw: true);
        Hook.AfterPlayerTurnStart(cs, player);
    }

    private static decimal PrepareFirstTurnDrawPile(CombatState cs, PlayerState player, decimal handDraw)
    {
        var pile = player.DrawPile;
        List<CardInstance>? bottom = null;
        var cards = pile.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].Enchantment?.ShouldStartAtBottomOfDrawPile ?? false)
            {
                (bottom ??= new List<CardInstance>(2)).Add(cards[i]);
            }
        }
        if (bottom != null)
        {
            for (int i = 0; i < bottom.Count; i++)
            {
                pile.MoveToBottomInternal(bottom[i]);
            }
        }
        List<CardInstance>? innate = null;
        HashSet<CardKeyword>? scratch = null;
        cards = pile.Cards;
        for (int i = 0; i < cards.Count; i++)
        {
            var card = cards[i];
            if (CardPileCmd.HasKeywordInCombat(cs, card, CardKeyword.Innate, ref scratch) && (bottom == null || !bottom.Contains(card)))
            {
                (innate ??= new List<CardInstance>(2)).Add(card);
            }
        }
        int innateCount = 0;
        if (innate != null)
        {
            for (int i = 0; i < innate.Count; i++)
            {
                pile.MoveToTopInternal(innate[i]);
            }
            innateCount = innate.Count;
        }
        handDraw = Math.Max(handDraw, innateCount);
        return Math.Min(handDraw, CardPileCmd.MaxCardsInHand);
    }

    private static void RunAutoPrePlayPhase(CombatState cs, PlayerState player)
    {
        player.Phase = PlayerTurnPhase.AutoPrePlay;
        CheckForEmptyHand(cs, player);
        Hook.AfterAutoPrePlayPhaseEntered(cs, player);
        player.Phase = PlayerTurnPhase.Play;
    }

    private static bool AllPlayersReadyAtTurnStart(CombatState cs, PlayerState[] playersStartingTurn)
    {
        var player = cs.Player;
        if (!player.IsDead && Array.IndexOf(playersStartingTurn, player) >= 0)
        {
            return false;
        }
        return true;
    }

    private static PlayerState[] PlayersOnTurn(CombatState cs)
    {
        var extra = cs.PlayersTakingExtraTurn;
        if (extra.Count == 0)
        {
            return new[] { cs.Player };
        }
        var players = new PlayerState[extra.Count];
        for (int i = 0; i < players.Length; i++)
        {
            players[i] = PlayerById(cs, extra[i]);
        }
        return players;
    }

    private static PlayerState PlayerById(CombatState cs, int combatId)
    {
        var allies = cs.Allies;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is PlayerState player && player.CombatId == combatId)
            {
                return player;
            }
        }
        throw new InvalidOperationException($"Player {combatId} is listed in PlayersTakingExtraTurn but is not in this combat.");
    }

    private static CreatureState[] CreaturesOf(PlayerState[] players)
    {
        var creatures = new CreatureState[players.Length];
        for (int i = 0; i < players.Length; i++)
        {
            creatures[i] = players[i];
        }
        return creatures;
    }

    private static void SetPhaseForAllPlayers(CombatState cs, PlayerTurnPhase phase)
    {
        cs.Player.Phase = phase;
    }

    private sealed class TurnSetupUnwindException : Exception
    {
        public TurnSetupUnwindException()
            : base("The combat ended at a deferred turn-setup step; the paused setup is abandoned.")
        {
        }
    }

    private sealed class TurnStartSteps
    {
        private readonly CombatState _cs;
        private readonly CombatSide _side;
        private readonly IReadOnlyList<CreatureState> _participants;
        private readonly PlayerState[] _players;
        private bool _paused;
        private bool _sideTurnStartDone;
        private bool _orbsDone;
        private bool _deadCheckDone;
        private bool _deadCheckEndsTurn;
        private bool _winCheckDone;

        public TurnStartSteps(CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants, PlayerState[] players)
        {
            _cs = cs;
            _side = side;
            _participants = participants;
            _players = players;
            OnSetupFirstPause = SetupFirstPause;
            OnAutoPrePlayFirstPause = AutoPrePlayFirstPause;
        }

        public Action OnSetupFirstPause { get; }

        public Action OnAutoPrePlayFirstPause { get; }

        public void RunAfterSideTurnStart()
        {
            if (_sideTurnStartDone)
            {
                return;
            }
            _sideTurnStartDone = true;
            Hook.AfterSideTurnStart(_cs, _side, _participants);
        }

        public void RunOrbTriggers()
        {
            if (_orbsDone)
            {
                return;
            }
            _orbsDone = true;
            for (int i = 0; i < _players.Length; i++)
            {
                using var scope = _cs.Choice.PushContext(ChoiceContextKind.Inline);
                _players[i].OrbQueue.AfterTurnStart(_cs);
            }
        }

        public bool RunDeadPlayerCheck()
        {
            if (!_deadCheckDone)
            {
                _deadCheckDone = true;
                _deadCheckEndsTurn = AllPlayersReadyAtTurnStart(_cs, _players);
            }
            return _deadCheckEndsTurn;
        }

        public void RunWinCheck()
        {
            if (_winCheckDone)
            {
                return;
            }
            _winCheckDone = true;
            ProcessWinCondition(_cs);
        }

        private void SetupFirstPause()
        {
            if (_paused)
            {
                return;
            }
            _paused = true;
            RunAfterSideTurnStart();
            RunOrbTriggers();
            if (RunDeadPlayerCheck())
            {
                _cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, DeadPlayerReadyAtPauseSite);
                return;
            }
            RunWinCheck();
            UnwindIfOver();
        }

        private void AutoPrePlayFirstPause()
        {
            if (_paused)
            {
                return;
            }
            _paused = true;
            RunWinCheck();
            UnwindIfOver();
        }

        private void UnwindIfOver()
        {
            if (_cs.IsInProgress)
            {
                return;
            }
            _cs.Run.Fidelity.Record(FidelityFlag.ApproximatedRule, CombatEndedAtPauseSite);
            throw new TurnSetupUnwindException();
        }
    }
}
