namespace Sts2Sim.Core.Combat.Loop;

public static class CombatLoop
{
    public const int BaseHandDrawCount = 5;

    public const int PlayerCombatId = 0;

    public const int MaxTurnLoopIterationsPerStep = 10_000;

    public static CombatState CreateCombat(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension)
        => CreateCombat(new RunRngSet(seed), deckIds, encounterId, ascension, null, null, null);

    public static CombatState CreateCombat(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension, Action<CombatState>? populateMonsters)
        => CreateCombat(new RunRngSet(seed), deckIds, encounterId, ascension, null, null, populateMonsters);

    public static CombatState CreateCombat(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension, int? currentHp, int? maxHp, Action<CombatState>? populateMonsters = null)
    {
        var cs = SetUpCombat(rng, deckIds, encounterId, ascension, currentHp, maxHp, populateMonsters);
        StartCombat(cs);
        return cs;
    }

    public static CombatState SetUpCombat(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension, Action<CombatState>? populateMonsters = null)
        => SetUpCombat(new RunRngSet(seed), deckIds, encounterId, ascension, null, null, populateMonsters);

    public static CombatState SetUpCombat(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension, int? currentHp, int? maxHp, Action<CombatState>? populateMonsters = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(deckIds);
        ArgumentNullException.ThrowIfNull(encounterId);
        int resolvedMaxHp = maxHp ?? StartingDecks.IroncladStartingHp;
        int resolvedCurrentHp = currentHp ?? resolvedMaxHp;
        if (resolvedMaxHp <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxHp), resolvedMaxHp, "Max HP must be positive.");
        }
        if (resolvedCurrentHp <= 0 || resolvedCurrentHp > resolvedMaxHp)
        {
            throw new ArgumentOutOfRangeException(nameof(currentHp), resolvedCurrentHp, $"Current HP must be between 1 and the max HP {resolvedMaxHp}.");
        }
        var deck = DeckForCombat(deckIds, ascension);
        var player = new PlayerState(PlayerCombatId, StartingDecks.IroncladCharacterId, resolvedCurrentHp, resolvedMaxHp, StartingDecks.IroncladMaxEnergy);
        var cs = new CombatState(player, encounterId, ascension, rng);
        cs.RoundNumber = 1;
        cs.CurrentSide = CombatSide.Player;
        if (cs.NextCreatureId <= player.CombatId)
        {
            cs.NextCreatureId = player.CombatId + 1;
        }
        player.TurnNumber = 1;
        player.Phase = PlayerTurnPhase.None;
        if (populateMonsters != null)
        {
            populateMonsters(cs);
        }
        else
        {
            MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(encounterId));
        }
        cs.IsStarting = true;
        PopulateDrawPile(cs, player, deck);
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterLifecycle.SetUpForCombat(cs, monsters[i]);
        }
        return cs;
    }

    public static IReadOnlyList<string> DeckForCombat(IReadOnlyList<string> deckIds, int ascension)
        => StartingDecks.IsIroncladStartingDeck(deckIds) ? StartingDecks.ApplyAscensionEffectsToDeck(deckIds, ascension) : deckIds;

    public static void StartCombat(CombatState cs)
    {
        if (!cs.IsStarting || cs.IsInProgress)
        {
            throw new InvalidOperationException("StartCombat needs a combat that SetUpCombat prepared and that has not started yet.");
        }
        var monsters = cs.Monsters.ToArray();
        for (int i = 0; i < monsters.Length; i++)
        {
            MonsterLifecycle.AfterCreatureAdded(cs, monsters[i]);
        }
        cs.IsInProgress = true;
        cs.IsStarting = false;
        Hook.BeforeCombatStart(cs);
        RunTurnLoop(cs, extraTurn: false);
    }

    public static void StartPlayerTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        StartTurn(cs, extraTurn: false);
    }

    public static void EndPlayerTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        EndTurnAndSwitchSides(cs, out _);
    }

    public static void RunEnemyTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Enemy);
        StartTurn(cs, extraTurn: false);
    }

    public static void CheckWinCondition(CombatState cs)
    {
        ProcessWinCondition(cs);
    }

    public static void CheckForEmptyHand(CombatState cs, PlayerState player)
    {
        if (cs.IsInProgress && player.Hand.Count == 0)
        {
            Hook.AfterHandEmptied(cs, player);
        }
    }

    public static void Step(CombatState cs, GameAction action)
    {
        if (!LegalActionGenerator.TryResolve(cs, action, out var target, out var error))
        {
            throw new InvalidOperationException($"Illegal action {action}: {error}");
        }
        switch (action.Kind)
        {
            case ActionKind.PlayCard:
                ActionExecutor.PlayCard(cs, action.CardIndex, target);
                if (cs.IsInProgress)
                {
                    ProcessWinCondition(cs);
                }
                break;
            case ActionKind.EndTurn:
                if (EndTurnAndSwitchSides(cs, out bool extraTurn))
                {
                    RunTurnLoop(cs, extraTurn);
                }
                break;
            default:
                throw new InvalidOperationException($"Unknown action kind {action.Kind}.");
        }
    }

    private static void PopulateDrawPile(CombatState cs, PlayerState player, IReadOnlyList<string> deckIds)
    {
        var drawPile = player.DrawPile;
        for (int i = 0; i < deckIds.Count; i++)
        {
            drawPile.AddInternal(CardInstance.Create(cs, deckIds[i]));
        }
        CardPileCmd.ShuffleInitialDrawPile(cs, player);
    }

    private static void RunTurnLoop(CombatState cs, bool extraTurn)
    {
        for (int iteration = 0; cs.IsInProgress; iteration++)
        {
            if (iteration >= MaxTurnLoopIterationsPerStep)
            {
                throw new InvalidOperationException($"The turn loop ran {MaxTurnLoopIterationsPerStep} turns without reaching a player decision.");
            }
            if (cs.CurrentSide == CombatSide.Player)
            {
                if (!StartTurn(cs, extraTurn))
                {
                    return;
                }
                if (!EndTurnAndSwitchSides(cs, out extraTurn))
                {
                    return;
                }
                continue;
            }
            StartTurn(cs, extraTurn: false);
            extraTurn = false;
        }
    }

    private static bool StartTurn(CombatState cs, bool extraTurn)
    {
        if (!cs.IsInProgress)
        {
            return false;
        }
        var player = cs.Player;
        var side = cs.CurrentSide;
        bool isPlayerSide = side == CombatSide.Player;
        player.Phase = PlayerTurnPhase.None;
        var starting = CreaturesStartingTurn(cs, isPlayerSide);
        for (int i = 0; i < starting.Length; i++)
        {
            CreatureCmd.BeforeTurnStart(cs, starting[i], side);
        }
        Hook.BeforeSideTurnStart(cs, side);
        if (isPlayerSide)
        {
            player.Phase = PlayerTurnPhase.Start;
            if (!extraTurn)
            {
                var monsters = cs.Monsters;
                for (int i = 0; i < monsters.Count; i++)
                {
                    MonsterLifecycle.PrepareForNextTurn(cs, monsters[i]);
                }
            }
        }
        for (int i = 0; i < starting.Length; i++)
        {
            CreatureCmd.AfterTurnStart(cs, starting[i], side);
        }
        for (int i = 0; i < starting.Length; i++)
        {
            Hook.AfterBlockCleared(cs, starting[i]);
        }
        if (isPlayerSide)
        {
            SetupPlayerTurn(cs, player);
        }
        Hook.AfterSideTurnStart(cs, side);
        if (isPlayerSide)
        {
            if (player.IsDead)
            {
                return true;
            }
            RunAutoPrePlayPhase(cs, player);
            ProcessWinCondition(cs);
            return false;
        }
        ProcessWinCondition(cs);
        if (cs.IsInProgress)
        {
            ExecuteEnemyTurn(cs);
        }
        return false;
    }

    private static CreatureState[] CreaturesStartingTurn(CombatState cs, bool isPlayerSide)
    {
        if (isPlayerSide)
        {
            return new CreatureState[] { cs.Player };
        }
        var monsters = cs.Monsters;
        var starting = new CreatureState[monsters.Count];
        for (int i = 0; i < starting.Length; i++)
        {
            starting[i] = monsters[i];
        }
        return starting;
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
            handDraw = MoveInnateCardsToTop(player, handDraw);
        }
        CardPileCmd.Draw(cs, handDraw, player, fromHandDraw: true);
        Hook.AfterPlayerTurnStart(cs, player);
    }

    private static decimal MoveInnateCardsToTop(PlayerState player, decimal handDraw)
    {
        var pile = player.DrawPile;
        var cards = pile.Cards;
        List<CardInstance>? innate = null;
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].HasKeyword(CardKeyword.Innate))
            {
                (innate ??= new List<CardInstance>(2)).Add(cards[i]);
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
        player.Phase = PlayerTurnPhase.Play;
    }

    private static bool EndTurnAndSwitchSides(CombatState cs, out bool extraTurn)
    {
        extraTurn = false;
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
            extraTurn = SwitchFromPlayerToEnemySide(cs);
        }
        return cs.IsInProgress;
    }

    private static void EndPlayerTurnPhaseOne(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        var player = cs.Player;
        player.Phase = PlayerTurnPhase.AutoPostPlay;
        player.Phase = PlayerTurnPhase.End;
        Hook.BeforeSideTurnEnd(cs, CombatSide.Player);
        if (ProcessWinCondition(cs))
        {
            return;
        }
        DoTurnEnd(cs, player);
        if (ProcessWinCondition(cs))
        {
            return;
        }
        Hook.BeforeFlush(cs, player);
        ProcessWinCondition(cs);
    }

    private static void DoTurnEnd(CombatState cs, PlayerState player)
    {
        if (!cs.IsInProgress || CombatStatus.IsEnding(cs))
        {
            return;
        }
        var hand = player.Hand.Cards;
        List<CardInstance>? turnEndCards = null;
        List<CardInstance>? etherealCards = null;
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (card.HasTurnEndInHandEffect)
            {
                (turnEndCards ??= new List<CardInstance>(2)).Add(card);
            }
            else if (card.HasKeyword(CardKeyword.Ethereal) && Hook.ShouldEtherealTrigger(cs, card))
            {
                (etherealCards ??= new List<CardInstance>(2)).Add(card);
            }
        }
        if (etherealCards != null)
        {
            for (int i = 0; i < etherealCards.Count; i++)
            {
                CardPileCmd.Exhaust(cs, etherealCards[i], causedByEthereal: true);
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
        CardPileCmd.Add(cs, card, PileType.Play, CardPilePosition.Bottom);
        card.OnTurnEndInHand(cs);
        if (!card.HasKeyword(CardKeyword.Ethereal))
        {
            CardPileCmd.Add(cs, card, PileType.Discard, CardPilePosition.Bottom);
        }
        else
        {
            CardPileCmd.Exhaust(cs, card, causedByEthereal: true);
        }
    }

    private static void EndPlayerTurnPhaseTwo(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        FlushPlayerHand(cs, cs.Player);
        Hook.AfterSideTurnEnd(cs, CombatSide.Player);
    }

    private static void FlushPlayerHand(CombatState cs, PlayerState player)
    {
        if (player.IsDead)
        {
            return;
        }
        bool shouldFlush = Hook.ShouldFlush(cs, player);
        var hand = player.Hand.Cards;
        var flushed = new List<CardInstance>(hand.Count);
        var retained = new List<CardInstance>(2);
        for (int i = 0; i < hand.Count; i++)
        {
            var card = hand[i];
            if (!shouldFlush || card.ShouldRetainThisTurn)
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
            CardPileCmd.Add(cs, flushed, PileType.Discard, CardPilePosition.Bottom);
        }
        Hook.AfterFlush(cs, player, flushed, retained);
        player.EndOfTurnCleanup();
    }

    private static bool SwitchFromPlayerToEnemySide(CombatState cs)
    {
        var player = cs.Player;
        bool extraTurn = Hook.ShouldTakeExtraTurn(cs, player);
        SwitchSides(cs, extraTurn);
        if (extraTurn)
        {
            Hook.AfterTakingExtraTurn(cs, player);
        }
        return extraTurn;
    }

    private static void SwitchSides(CombatState cs, bool extraTurn)
    {
        if (cs.CurrentSide == CombatSide.Player && !extraTurn)
        {
            cs.CurrentSide = CombatSide.Enemy;
        }
        else
        {
            cs.CurrentSide = CombatSide.Player;
            if (!extraTurn)
            {
                cs.RoundNumber++;
            }
            cs.Player.IncrementTurnNumber();
        }
        var monsters = cs.Monsters;
        for (int i = 0; i < monsters.Count; i++)
        {
            MonsterLifecycle.OnSideSwitch(monsters[i]);
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
        Hook.BeforeSideTurnEnd(cs, CombatSide.Enemy);
        cs.Player.EndOfTurnCleanup();
        Hook.AfterSideTurnEnd(cs, CombatSide.Enemy);
        ProcessWinCondition(cs);
        if (!CombatStatus.IsEnding(cs))
        {
            SwitchSides(cs, extraTurn: false);
        }
    }

    private static bool ProcessWinCondition(CombatState cs)
    {
        if (cs.Outcome == CombatOutcome.Defeat && cs.IsInProgress)
        {
            ProcessPendingLoss(cs);
            return true;
        }
        if (CombatStatus.IsEnding(cs))
        {
            EndCombatWithVictory(cs);
            return true;
        }
        return false;
    }

    private static void ProcessPendingLoss(CombatState cs)
    {
        cs.IsInProgress = false;
    }

    private static void EndCombatWithVictory(CombatState cs)
    {
        cs.IsInProgress = false;
        cs.Outcome = CombatOutcome.Victory;
        cs.Player.Phase = PlayerTurnPhase.None;
    }

    private static void RequireSide(CombatState cs, CombatSide side)
    {
        if (cs.CurrentSide != side)
        {
            throw new InvalidOperationException($"Expected the {side} side, but the current side is {cs.CurrentSide}!");
        }
    }
}
