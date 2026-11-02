namespace Sts2Sim.Core.Combat.Loop;

public static partial class CombatLoop
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

    public static CombatState CreateCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, Action<CombatState>? populateMonsters = null)
    {
        var cs = SetUpCombat(rng, deck, encounterId, ascension, currentHp, maxHp, populateMonsters);
        StartCombat(cs);
        return cs;
    }

    public static CombatState CreateCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, CombatRunContext context, Action<CombatState>? populateMonsters = null)
    {
        var cs = SetUpCombat(rng, deck, encounterId, ascension, currentHp, maxHp, context, populateMonsters);
        StartCombat(cs);
        return cs;
    }

    public static CombatState CreateCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, EncounterDef encounter, int ascension, int? currentHp, int? maxHp, CombatRunContext context)
    {
        var cs = SetUpCombat(rng, deck, encounter, ascension, currentHp, maxHp, context);
        StartCombat(cs);
        return cs;
    }

    public static CombatState SetUpCombat(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension, Action<CombatState>? populateMonsters = null)
        => SetUpCombat(new RunRngSet(seed), deckIds, encounterId, ascension, null, null, populateMonsters);

    public static CombatState SetUpCombat(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension, int? currentHp, int? maxHp, Action<CombatState>? populateMonsters = null)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(deckIds);
        return SetUpCombat(rng, CardSpec.FromIds(DeckForCombat(deckIds, ascension)), encounterId, ascension, currentHp, maxHp, populateMonsters);
    }

    public static CombatState SetUpCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, Action<CombatState>? populateMonsters = null)
        => SetUpCombat(rng, deck, encounterId, ascension, currentHp, maxHp, CombatRunContext.Default, populateMonsters);

    public static CombatState SetUpCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp, CombatRunContext context, Action<CombatState>? populateMonsters = null)
    {
        ArgumentNullException.ThrowIfNull(encounterId);
        var run = CreateStandaloneRun(rng, deck, ascension, currentHp, maxHp);
        return SetUpCombat(run, run.Players[0], encounterId, null, context, populateMonsters);
    }

    public static CombatState SetUpCombat(RunRngSet rng, IReadOnlyList<CardSpec> deck, EncounterDef encounter, int ascension, int? currentHp, int? maxHp, CombatRunContext context)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var run = CreateStandaloneRun(rng, deck, ascension, currentHp, maxHp);
        return SetUpCombat(run, run.Players[0], encounter.Id, encounter, context, null);
    }

    public static CombatState SetUpCombat(RunState run, PlayerState player, string encounterId, CombatRunContext context, Action<CombatState>? populateMonsters = null)
        => SetUpCombat(run, player, encounterId, null, context, populateMonsters);

    public static CombatState SetUpCombat(RunState run, PlayerState player, EncounterDef encounter, CombatRunContext context)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return SetUpCombat(run, player, encounter.Id, encounter, context, null);
    }

    public static IReadOnlyList<string> DeckForCombat(IReadOnlyList<string> deckIds, int ascension)
        => StartingDecks.IsIroncladStartingDeck(deckIds) ? StartingDecks.ApplyAscensionEffectsToDeck(deckIds, ascension) : deckIds;

    public static void StartCombat(CombatState cs)
    {
        if (!cs.IsStarting || cs.IsInProgress)
        {
            throw new InvalidOperationException("StartCombat needs a combat that SetUpCombat prepared and that has not started yet.");
        }
        var creatures = SnapshotMonsterCreatures(cs);
        for (int i = 0; i < creatures.Length; i++)
        {
            MonsterLifecycle.AfterCreatureAdded(cs, creatures[i]);
        }
        cs.IsInProgress = true;
        cs.IsStarting = false;
        Hook.BeforeCombatStart(cs);
        RunTurnLoop(cs);
    }

    public static void StartPlayerTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        StartTurn(cs);
    }

    public static void EndPlayerTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Player);
        EndTurnAndSwitchSides(cs);
    }

    public static void RunEnemyTurn(CombatState cs)
    {
        RequireSide(cs, CombatSide.Enemy);
        StartTurn(cs);
    }

    public static void CheckWinCondition(CombatState cs)
    {
        ProcessWinCondition(cs);
    }

    public static void Step(CombatState cs, GameAction action)
    {
        if (!LegalActionGenerator.TryResolve(cs, action, out var target, out var error))
        {
            throw new InvalidOperationException($"Illegal action {action}: {error}");
        }
        ExecuteResolvedAction(cs, action, target);
    }

    public static void ExecuteResolvedAction(CombatState cs, GameAction action, CreatureState? target)
    {
        ArgumentNullException.ThrowIfNull(cs);
        switch (action.Kind)
        {
            case ActionKind.PlayCard:
                ActionExecutor.PlayCard(cs, action.CardIndex, target);
                CheckWinConditionAfterAction(cs);
                EndTurnIfAllPlayersReady(cs);
                break;
            case ActionKind.UsePotion:
                PotionActions.UsePotion(cs, action.CardIndex, target);
                CheckWinConditionAfterAction(cs);
                EndTurnIfAllPlayersReady(cs);
                break;
            case ActionKind.DiscardPotion:
                PotionActions.DiscardPotion(cs, action.CardIndex);
                CheckWinConditionAfterAction(cs);
                EndTurnIfAllPlayersReady(cs);
                break;
            case ActionKind.EndTurn:
                PlayerCmd.EndTurn(cs, cs.Player, canBackOut: true);
                if (EndTurnAndSwitchSides(cs))
                {
                    RunTurnLoop(cs);
                }
                break;
            case ActionKind.ChoosePick:
            case ActionKind.ChooseConfirm:
            case ActionKind.ChooseCancel:
                throw new InvalidOperationException($"{action.Kind} answers a pending prompt; CombatSim applies it through the choice engine, not CombatLoop.");
            default:
                throw new InvalidOperationException($"Unknown action kind {action.Kind}.");
        }
    }

    private static void EndTurnIfAllPlayersReady(CombatState cs)
    {
        if (!cs.IsInProgress || cs.CurrentSide != CombatSide.Player || !cs.AllPlayersReadyToEndTurn())
        {
            return;
        }
        if (EndTurnAndSwitchSides(cs))
        {
            RunTurnLoop(cs);
        }
    }

    private static void CheckWinConditionAfterAction(CombatState cs)
    {
        if (cs.IsInProgress)
        {
            ProcessWinCondition(cs);
        }
    }

    private static RunState CreateStandaloneRun(RunRngSet rng, IReadOnlyList<CardSpec> deck, int ascension, int? currentHp, int? maxHp)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(deck);
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
        var player = new PlayerState(PlayerCombatId, StartingDecks.IroncladCharacterId, resolvedCurrentHp, resolvedMaxHp, StartingDecks.IroncladMaxEnergy);
        var run = RunState.CreateStandalone(player, rng, ascension);
        BuildDeck(run, player, deck);
        return run;
    }

    private static void BuildDeck(RunState run, PlayerState player, IReadOnlyList<CardSpec> deck)
    {
        for (int i = 0; i < deck.Count; i++)
        {
            var spec = deck[i];
            var def = CardInstance.ResolveDef(spec);
            var card = new CardInstance(def, run.AllocateCardInstanceId());
            card.AttachTo(player);
            def.AfterCreated(card);
            if (spec.Upgraded)
            {
                card.Upgrade();
            }
            card.FloorAddedToDeck = 1;
            player.Deck.Add(card);
        }
    }

    private static CombatState SetUpCombat(RunState run, PlayerState player, string encounterId, EncounterDef? encounter, CombatRunContext context, Action<CombatState>? populateMonsters)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(encounterId);
        if (run.Combat != null)
        {
            throw new InvalidOperationException("Make sure to reset the combat before setting up a new one.");
        }
        if (!run.Players.Contains(player))
        {
            throw new InvalidOperationException("The player must belong to the run that hosts the combat.");
        }
        var cs = new CombatState(run, player, encounterId);
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
            if (encounter == null && EncounterRegistry.TryGet(encounterId, out var registered))
            {
                encounter = registered;
            }
        }
        else
        {
            encounter ??= EncounterRegistry.Get(encounterId);
            cs.EncounterExtra = encounter.CreateExtraState();
            MonsterSpawner.SpawnEncounter(cs, encounter, context);
        }
        if (encounter != null && cs.EncounterExtra == null)
        {
            cs.EncounterExtra = encounter.CreateExtraState();
        }
        cs.IsStarting = true;
        ResetCombatState(cs, player);
        PopulateCombatState(cs, player);
        var creatures = SnapshotMonsterCreatures(cs);
        for (int i = 0; i < creatures.Length; i++)
        {
            var monster = creatures[i];
            MonsterLifecycle.SetUpForCombat(cs, monster);
            if (monster.SlotName != null)
            {
                MonsterSpawner.SortEnemiesBySlotName(cs, encounter);
            }
        }
        return cs;
    }

    private static void ResetCombatState(CombatState cs, PlayerState player)
    {
        var piles = player.AllPiles;
        for (int i = 0; i < piles.Count; i++)
        {
            piles[i].ClearInternal();
        }
        player.Energy = 0;
        player.Stars = 0;
        player.TurnNumber = 1;
        player.Phase = PlayerTurnPhase.None;
        player.PetIds.Clear();
        player.OrbQueue.Clear();
        if (player.BaseOrbSlotCount != 0)
        {
            player.OrbQueue.AddCapacity(player.BaseOrbSlotCount);
        }
    }

    private static void PopulateCombatState(CombatState cs, PlayerState player)
    {
        var deck = player.Deck.ToArray();
        var drawPile = player.DrawPile;
        for (int i = 0; i < deck.Length; i++)
        {
            drawPile.AddInternal(deck[i].CloneForCombat(cs.AllocateCardInstanceId()));
        }
        CardPileCmd.ShuffleInitialDrawPile(cs, player);
    }

    private static MonsterState[] SnapshotMonsterCreatures(CombatState cs)
    {
        var allies = cs.Allies;
        var monsters = cs.Monsters;
        int pets = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is MonsterState)
            {
                pets++;
            }
        }
        var creatures = new MonsterState[pets + monsters.Count];
        int k = 0;
        for (int i = 0; i < allies.Count; i++)
        {
            if (allies[i] is MonsterState pet)
            {
                creatures[k++] = pet;
            }
        }
        for (int i = 0; i < monsters.Count; i++)
        {
            creatures[k++] = monsters[i];
        }
        return creatures;
    }

    private static void RequireSide(CombatState cs, CombatSide side)
    {
        if (cs.CurrentSide != side)
        {
            throw new InvalidOperationException($"Expected the {side} side, but the current side is {cs.CurrentSide}!");
        }
    }
}
