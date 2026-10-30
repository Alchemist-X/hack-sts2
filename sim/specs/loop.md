# Loop spec: combat setup, turn flow, card play, piles, decision API

Area owner: loop agent. Files:

| File | Contents |
| --- | --- |
| `Sts2Sim.Core/Combat/Loop/CombatSim.cs` | decision facade `Sts2Sim.Core.CombatSim` (contract §4.2) |
| `Sts2Sim.Core/Combat/Loop/CombatLoop.cs` | combat setup, turn loop, player end of turn, enemy turn, win/loss |
| `Sts2Sim.Core/Combat/Loop/LegalActionGenerator.cs` | legal action list; validation and target resolution of a `GameAction` |
| `Sts2Sim.Core/Combat/Loop/ActionExecutor.cs` | manual card play: resource spend, `OnPlayWrapper` |
| `Sts2Sim.Core/Combat/Loop/CardPileCmd.cs` | draw, shuffle, pile moves, generated cards, discard, exhaust, remove |
| `Sts2Sim.Core/Combat/Loop/PlayerCmd.cs` | contract energy commands; delegate to the pipeline `EnergyCmd` |
| `Sts2Sim.Tests/LoopTests.cs` | 20 `Test_Loop_*` tests with test-local monster fixtures (§11) |

Citations are `<namespace dir without MegaCrit.Sts2.Core.>/<File>.cs:<line>` in
`output/server-bootstrap/linux-decompiled/`. Async methods compile to state machines whose
`MoveNext` text order differs from execution order. The order given here was traced through
the `goto` labels; line numbers point at the statement inside the state machine.

The port follows the game's non-interactive mode. `NonInteractiveMode.IsActive` is true in
`TestMode` (`Helpers/NonInteractiveMode.cs:18-28`). In that mode `Cmd.CustomScaledWait` and
`Cmd.Wait` return without waiting (`Commands/Cmd.cs:75`, `:172`), so every `await` in the
combat flow completes synchronously in program order. The sim calls the same steps directly
in that order.

## 1. Constants

| Rule | Value | Sim | Citation |
| --- | --- | --- | --- |
| Base hand draw per turn | 5 | `CombatLoop.BaseHandDrawCount` | `Combat/CombatManager.cs:5978` (`baseHandDrawCount`); `SetupPlayerTurn` passes the literal `5m` to `Hook.ModifyHandDraw` (`:4206`) |
| Max cards in hand | 10 | `CardPile.MaxCardsInHand` (state), re-exported as `CardPileCmd.MaxCardsInHand` | `Entities.Cards/CardPile.cs:35` |
| Ironclad starting HP and max HP | 80; the default `currentHp`/`maxHp` of `Create` at every ascension (§2.1) | `StartingDecks.IroncladStartingHp` | `Models.Characters/Ironclad.cs:30`; `Entities.Players/Player.cs:714` |
| Run-start deck from the starter deck | starter deck, plus `ASCENDERS_BANE` appended at A5+ | `CombatLoop.DeckForCombat` → `StartingDecks.ApplyAscensionEffectsToDeck` | `Entities.Ascension/AscensionManager.cs:27-38` (cards spec §1.1) |
| Max energy (base) | 3 | `StartingDecks.IroncladMaxEnergy` → `PlayerState.BaseMaxEnergy` | `Models/CharacterModel.cs:67` (`Ironclad` does not override it) |
| First turn number | 1 | `PlayerState.TurnNumber` | `Entities.Players/PlayerCombatState.cs:41-48` |
| First round, first side | 1, Player | `CombatState.RoundNumber`, `CurrentSide` | `Combat/CombatState.cs:658-659` |
| Creature ids | player 0; monsters 1.. in spawn order | `CombatLoop.PlayerCombatId`, `CombatState.AllocateCreatureId` | players are attached first (`Rooms/CombatRoom.cs:91`), monsters afterwards (`:392-393`); id = `_nextCreatureId++` (`Combat/CombatState.cs:726-731`) |
| Draw count rounding | `ceil(count)` if `count > 0`, else 0 | `CardPileCmd` draw | `Commands/CardPileCmd.cs:4013` |
| Add to a full hand | goes to the discard pile instead; the check runs before the card leaves its old pile | `CardPileCmd.MoveCard` | `Commands/CardPileCmd.cs:3405-3409` |
| Pile insert index | Bottom → append; Top → 0; Random → `Rng.Shuffle.NextInt(count + 1)`, drawn after the card left its old pile; any other value throws | `CardPileCmd.MoveCard` | `Commands/CardPileCmd.cs:3413`, `:3427-3433` |
| Energy cost above current energy, when `ShouldPayExcessEnergyCostWithStars` | 2 stars per missing energy; energy spent = current energy | `ActionExecutor.SpendResources` | `Models/CardModel.cs:3411-3415` |
| Turn-1 innate draw | `min(max(handDraw, innateCount), 10)` | `CombatLoop.MoveInnateCardsToTop` | `Combat/CombatManager.cs:4283-4284` |
| Turn-loop guard | 10 000 turn iterations per `Step` | `CombatLoop.MaxTurnLoopIterationsPerStep` | sim-only safety guard; no game counterpart |

## 2. Combat setup

Game order: `CombatRoom.Enter` attaches the players (`Rooms/CombatRoom.cs:91`).
`CombatRoom.StartCombat` generates the encounter's monsters and creates and adds each one
(`:357`, `:381-394`; monster HP uses `Rng.Niche`, `Combat/CombatState.cs:709-724`). It then
calls `CombatManager.SetUpCombat` (`:417`), dispatches the run-level `Hook.AfterRoomEntered`
(`:418-433`), and `AfterCombatRoomLoaded` (`:435`, `Combat/CombatManager.cs:6629-6634`) starts the
turn loop, whose first step is `StartCombatInternal`.

### 2.1 Inputs: deck, HP, RNG

| Input | Sim | Rule |
| --- | --- | --- |
| deck | `CombatLoop.DeckForCombat(deckIds, ascension)` | if `deckIds` is the Ironclad starting deck (`StartingDecks.IsIroncladStartingDeck`: same ids in the same order), apply the run-start ascension effect, i.e. append `ASCENDERS_BANE` at A5+ (`Entities.Ascension/AscensionManager.cs:33-38`, cards spec §1.1); any other list is the player's deck as given. A real A5+ run deck always holds the curse, which is Eternal (`Models/CardModel.cs:2100-2113`), so the bare starter deck at A5+ can only mean "the deck before the run-start effect", and a deck that already holds the curse never gets a second one. |
| HP | `maxHp ?? 80`, `currentHp ?? maxHp`; `currentHp` must be in `1..maxHp`, `maxHp > 0` | the combat itself does not change HP on entry. Run-layer HP (Neow's arrival heal, 64 at A2+, `StartingDecks.IroncladHpAfterNeowArrival`, cards spec §1.1; events; relics) is passed by the caller. |
| RNG | `Create(ulong seed, ...)` → `new RunRngSet(seed)`; `Create(RunRngSet rng, ...)` → `rng.DeepClone()` | the game draws the combat from the live run streams (`Combat/CombatManager.cs:6605`), which are mid-stream in a real run. The `RunRngSet` overload starts from such streams (e.g. a set from `RunRngSet.FromSnapshot`) without mutating the caller's set. |

### 2.2 Turn-state windows

The game keeps `IsInProgress` and `IsStarting` on `CombatTurnState` (`Combat/CombatTurnState.cs:99-115`:
`IsInProgress` starts false, `IsStarting` starts **true**). `CombatManager.IsInProgress`/`IsStarting`
read them and are false while no turn state exists (`Combat/CombatManager.cs:6113-6117`). The sim
stores both flags on `CombatState` (state spec §1.1). Three windows precede the first decision:

| Window | Game | `IsStarting` / `IsInProgress` | Consequences (same in game and sim) |
| --- | --- | --- | --- |
| A: encounter spawn | `CreateCreature`/`AddCreature` per monster before `SetUpCombat`; `SetUpCombat` throws if a turn state exists (`Combat/CombatManager.cs:6578-6582`), so there is none yet | false / false | `IsEnding` false, `IsOverOrEnding` true (`:6121-6144`): guarded hooks yield nothing (`Hooks/Hook.cs:8794`); every `IsOverOrEnding` gate is closed; `IsEnding` gates (`PowerCmd.Apply`, energy) are open |
| B: setup | `SetUpCombat` creates the turn state (`:6582`) and runs `ResetCombatState`, the opening shuffle and `AddCreature`; then `StartCombatInternal` runs `AfterCreatureAdded` for every creature | true / false | `IsEnding` false (`:6550-6553`), `IsOverOrEnding` true; guarded hooks **do** run (the `!IsStarting` exception); `IsOverOrEnding` gates are closed: `CreatureCmd.GainBlock` returns 0 (`Commands/CreatureCmd.cs:3954-3957`), `CardPileCmd.Draw` returns (`Commands/CardPileCmd.cs:4000`), `AttackCommand.Execute` returns (`Commands.Builders/AttackCommand.cs:970-973`); `Add` to a combat pile is refused (`:3387-3390`) and `AddGeneratedCardsToCombat` returns empty (`:3213-3216`); `IsEnding` gates are open: `PowerCmd.Apply` (`Commands/PowerCmd.cs:1219`), energy commands (`Commands/PlayerCmd.cs:194`) |
| C: running | `IsInProgress = true; IsStarting = false` right after the `AfterCreatureAdded` loop (`Combat/CombatManager.cs:4489-4490`, the only place `IsInProgress` becomes true), then `Hook.BeforeCombatStart` (`:4491`) | false / true | normal play |

Example: `CubexConstruct.AfterAddedToRoom` calls `CreatureCmd.GainBlock(13)`
(`Models.Monsters/CubexConstruct.cs:115`). It runs in window B and gives 0 block, in the game and
in the sim (`LoopTests.SetUpWindowGatesCommandsLikeTheGame`). A player death in windows A or B
records no loss: `Kill` calls `LoseCombat` only while `CombatManager.IsInProgress`
(`Commands/CreatureCmd.cs:2397-2404`).

### 2.3 Steps

| Step | Game | Sim (`CombatLoop`) |
| --- | --- | --- |
| player creature, HP, energy | `Player` built by the run | `SetUpCombat`: `new PlayerState(0, "IRONCLAD", currentHp, maxHp, 3)` (§2.1) |
| combat state | `new CombatState(...)`: `RoundNumber = 1`, `CurrentSide = Player` (`Combat/CombatState.cs:654-664`) | `new CombatState(player, encounterId, ascension, rng)` (window A); `RoundNumber = 1`, `CurrentSide = Player`, `NextCreatureId >= 1`, `TurnNumber = 1`, `Phase = None` |
| monsters (window A) | `GenerateMonstersWithSlots` + `CreateCreature`/`AddCreature` (`Rooms/CombatRoom.cs:357-393`) | `populateMonsters(cs)` test seam, else `MonsterSpawner.SpawnEncounter(cs, EncounterRegistry.Get(encounterId))` (monsters agent) |
| turn state | `new CombatTurnState(state)` (`Combat/CombatManager.cs:6582`) | `cs.IsStarting = true` (window B begins) |
| `ResetCombatState` | `Combat/CombatManager.cs:6592` | nothing to reset: the state is new |
| deck → draw pile | `Player.PopulateCombatState(RunState.Rng.Shuffle, state)` (`Combat/CombatManager.cs:6599-6611`): clone each deck card in deck order and `DrawPile.AddInternal` it (`Entities.Players/Player.cs:1214-1233`) | `PopulateDrawPile`: `CardInstance.Create(cs, id)` per id of the §2.1 deck in order (instance ids in deck order), `DrawPile.AddInternal` |
| opening shuffle | `DrawPile.RandomizeOrderInternal`: `_cards.UnstableShuffle(rng)`, then the test-only `TestRngInjector` override, then `Hook.ModifyShuffleOrder(state, player, cards, isInitialShuffle: true)` (`Entities.Cards/CardPile.cs:241-246`) | `CardPileCmd.ShuffleInitialDrawPile`: `DrawPile.MutableCards.UnstableShuffle(cs.Rng.Shuffle)`, then `Hook.ModifyShuffleOrder(..., true)` |
| creature registration | `AddCreature` per creature of `State.Creatures` (`Combat/CombatManager.cs:6613-6625`): `Monster?.SetUpForCombat()` builds the move machine and sets `SpawnedThisTurn = true` (`:6990-7005`, `Models/MonsterModel.cs:713-717`), after the shuffle and once every enemy exists | `MonsterLifecycle.SetUpForCombat` per monster in list order (monsters agent) |
| `AfterRoomEntered` | run-level hook (`Rooms/CombatRoom.cs:420`; listeners `runState.IterateHookListeners(null)`, `Hooks/Hook.cs:5926`: relics, potions, modifiers) | not dispatched: no run-level listeners in P0 |
| `StartCombatInternal` creature loop | `AfterCreatureAdded` for each of a snapshot of `State.Creatures`, allies then enemies (`Combat/CombatManager.cs:4466-4478`). It runs `Creature.AfterAddedToRoom` (enemy side only, `Entities.Creatures/Creature.cs:36-99`), then `RollMove` if the creature is an enemy and the side is Player (`Combat/CombatManager.cs:497-560`) | `StartCombat`: `MonsterLifecycle.AfterCreatureAdded` for each monster of a snapshot of `cs.Monsters`. The player's call is a no-op in the game. |
| combat start | `IsInProgress = true`, `IsStarting = false`, `Hook.BeforeCombatStart` (`Combat/CombatManager.cs:4489-4491`) | the same three steps (window C) |
| turn loop | `while (turnState.IsLive)`: Player side → `StartTurn`, then `AwaitTurnEndAndSwitchSides`; Enemy side → `StartTurn(actionDuringEnemyTurn)` (`Combat/CombatManager.cs:4413-4658`, label `IL_05ce`) | `RunTurnLoop` (§3.1) |

`CombatLoop.SetUpCombat` and `CombatLoop.StartCombat` are public so that tests can inspect the
state between the two (window B). `StartCombat` throws unless the state is in window B.
`CreateCombat` = `SetUpCombat` + `StartCombat`. `Create` returns at the first decision point:
player turn 1 started, opening hand drawn, `Phase == Play`. A state left in window B has no
legal actions, and `Step` rejects every action with "The combat is still being set up."

`RunRngSet(seed)` takes the numeric run seed directly (contract §4.2). The opening shuffle is
the first use of the `Shuffle` stream. Monster HP (`Niche`) and the first `RollMove`
(`MonsterAi`) use other streams, so their relative order does not matter.

## 3. Turn flow

### 3.1 Turn loop (sim)

`RunTurnLoop(cs, extraTurn)` repeats while `cs.IsInProgress`:

- Player side: `StartTurn`. It returns false at a player decision point, and the loop
  returns. It returns true only when the player is dead at turn start and so is
  "ready to end turn" (§3.2, step 9). In that case `EndTurnAndSwitchSides` runs immediately,
  as the game's auto-ready does (`Combat/CombatManager.cs:5149-5157`).
- Enemy side: `StartTurn` runs the whole enemy turn, including the switch back to Player in
  `EndEnemyTurn`, and the loop continues.

`Step(EndTurn)` = `EndTurnAndSwitchSides`, then `RunTurnLoop` if still in progress. This
mirrors `AwaitTurnEndAndSwitchSides` (`Combat/CombatManager.cs:6687-6762`): phase one
(`AfterAllPlayersReadyToEndTurn`, `:7229`), then phase two and the side switch
(`AfterAllPlayersReadyToBeginEnemyTurn`, `:7367`). The end-turn and begin-enemy-turn
signals, the ready locks and the multiplayer synchronizer are removed.

### 3.2 `StartTurn` (`Combat/CombatManager.cs:4850-5406`, method `:6765`)

1. Return if the combat is not in progress (`:4865`).
2. `SetPhaseForAllPlayers(None)` (`:4869`) → `player.Phase = None`.
3. Creatures starting the turn: the extra-turn players, or `CreaturesOnCurrentSide`
   (`:4873-4882`). Sim: `[player]` on the player side, else a snapshot of `cs.Monsters`.
   Extra turns are always the single player in P0.
4. `Creature.BeforeTurnStart(side)` for each (`:4901`) → `CreatureCmd.BeforeTurnStart`
   (pipeline).
5. `Hook.BeforeSideTurnStart(side)` (`:4914`).
6. Player side only (`:5291-5323`): `SetPhaseForAllPlayers(Start)`. Unless this is an extra
   turn, `PrepareForNextTurn` runs for every enemy, which rolls the next move →
   `MonsterLifecycle.PrepareForNextTurn`. The ready flags and signal sources are reset
   (not modelled).
7. `Creature.AfterTurnStart(side)` for each (`:4960`). This clears block, except for the player
   on the Player side when `TurnNumber == 1` (`Entities.Creatures/Creature.cs:136-145`). Then
   `Hook.AfterBlockCleared(creature)` for each (`:5008`), which also fires for the turn-1
   player whose block was kept. Sim: `CreatureCmd.AfterTurnStart` (pipeline) implements the
   turn-1 exemption; the loop calls `Hook.AfterBlockCleared` for each starting creature.
8. Player side: `SetupPlayerTurn` (`:5060`, §3.3). Then `Hook.AfterSideTurnStart(side)` (`:5080`).
   The game starts `SetupPlayerTurn` as a task and awaits it in `RunAutoPrePlayPhase`. In
   non-interactive mode it has completed by then, so the sim runs it inline before
   `AfterSideTurnStart`.
9. Player side: `OrbQueue.AfterTurnStart` (`:5119`, no orbs in P0). A dead player is set ready
   to end the turn (`:5149-5157`; sim: `StartTurn` returns true). Otherwise
   `RunAutoPrePlayPhase` (`:5204`, §3.4), then `CheckWinCondition` (`:5226`).
10. Enemy side: `CheckWinCondition` (`:5261`); if still in progress, `ExecuteEnemyTurn`
    (`:5276`, §3.9).

### 3.3 `SetupPlayerTurn` (`Combat/CombatManager.cs:4135-4308`)

The first step is `if (!IsDead)` (`:4148`); a dead player skips the whole setup. Then:

1. `Hook.ShouldPlayerResetEnergy` (`:4153`): `ResetEnergy` (energy = max) or
   `AddMaxEnergyToCurrent` (`:4156-4160`, `Entities.Players/PlayerCombatState.cs:304-312`)
   → `EnergyCmd.ResetEnergy` / `EnergyCmd.AddMaxEnergyToCurrent` (pipeline).
2. `Hook.AfterEnergyReset` (`:4162`), then `Hook.BeforeHandDraw` (`:4220`).
3. `Hook.ModifyHandDraw(state, player, 5m, out modifiers)`, then `Hook.AfterModifyingHandDraw`
   (`:4206-4207`).
4. Turn 1 only (`:4246-4285`):
   - Cards whose `Enchantment.ShouldStartAtBottomOfDrawPile` is true move to the bottom
     (`:4248-4262`). Not ported: there are no enchantments in P0.
   - Every other `Innate` draw-pile card, in pile order, gets `MoveToTopInternal` (`:4266-4281`).
     The last one in pile order ends on top.
   - `handDraw = min(max(handDraw, innateCount), 10)` (`:4283-4284`).
5. `CardPileCmd.Draw(handDraw, fromHandDraw: true)` (`:4286`), then
   `Hook.AfterPlayerTurnStart` (`:4233`, run after the draw).

### 3.4 `RunAutoPrePlayPhase` (`Combat/CombatManager.cs:3853-3915`)

`Phase = AutoPrePlay` (`:3902`) → `CheckForEmptyHand` (`:3903`) →
`Hook.AfterAutoPrePlayPhaseEntered` (`:3891`, not dispatched in the sim, see §9) →
`Phase = Play` (`:3914`). The sim then returns to the caller: this is the decision point.

`CheckForEmptyHand` (`Combat/CombatManager.cs:917-989`, `:7026-7056`): if the combat is in
progress, no card/potion effect is executing for the player, and the hand is empty, run
`Hook.AfterHandEmptied` (`:954`). Sim: `CombatLoop.CheckForEmptyHand`. The effect-depth
condition always holds at the two sim call sites, because effects are not nested in P0 (§9).

### 3.5 Player end of turn, phase one (`EndPlayerTurnPhaseOneInternal`, `Combat/CombatManager.cs:2322-2697`)

1. Throw unless the current side is Player (`:2339-2341`).
2. `Phase = AutoPostPlay` (`:2387`), `Hook.AfterAutoPostPlayPhaseEntered` (`:2389`, not
   dispatched), `Phase = End` (`:2426`).
3. `Hook.BeforeSideTurnEnd(Player)` (`:2457`), then `CheckWinCondition`; stop if it returns
   true (label `IL_048a`/`IL_04ee`).
4. `DoTurnEnd` per player (`:2503`, §3.6), then `CheckWinCondition`; stop if true (`:2562`,
   label `IL_073f`).
5. `Hook.BeforeFlush` per player (`:2594`), then `CheckWinCondition` (`:2623`).

### 3.6 `DoTurnEnd` (`Combat/CombatManager.cs:1189-1312`) and turn-end cards

1. `OrbQueue.BeforeTurnEnd` runs first (no orbs in P0).
2. Only if the combat is in progress and not ending: classify the hand in order. Cards with
   `HasTurnEndInHandEffect` go to the turn-end list; other `Ethereal` cards with
   `Hook.ShouldEtherealTrigger` go to the ethereal list.
3. Each ethereal card → `CardCmd.Exhaust(card, causedByEthereal: true)`.
4. `DoTurnEndCards` (`:1324-1429`) for the turn-end list. For each card:
   `AddTurnEndCardToPlayPileWithDelay` (Add to Play, bottom), then
   `ResolveTurnEndCardEffects` (`card.OnTurnEndInHand`), then
   `TweenTurnEndCardToResultPile`: Add to Discard bottom, or `Exhaust(causedByEthereal: true)`
   if the card is Ethereal.

   The game starts these as overlapping tasks with staggered delays. In non-interactive mode
   the waits are skipped and every task completes synchronously, so the order is card by
   card: add to Play → effect → result pile. Sim: `ResolveTurnEndCard`, sequential.

### 3.7 Player end of turn, phase two (`EndPlayerTurnPhaseTwoInternal`, `Combat/CombatManager.cs:2841-3003`)

Require the Player side, `FlushPlayerHand`, then `Hook.AfterSideTurnEnd(Player)`.

`FlushPlayerHand` (`Combat/CombatManager.cs:3330-3432`):

1. Return if the player is dead.
2. `shouldFlush = Hook.ShouldFlush`.
3. Split the hand. A card is retained when `!shouldFlush || card.ShouldRetainThisTurn`;
   every other card is flushed.
4. One batched `CardPileCmd.Add(flushed, Discard)`.
5. `Hook.AfterFlush(player, flushed, retained)`, then
   `PlayerCombatState.EndOfTurnCleanup()` (`Entities.Players/PlayerCombatState.cs:411-...`)
   → `PlayerState.EndOfTurnCleanup`.

### 3.8 Side switch

`SwitchFromPlayerToEnemySide` (`Combat/CombatManager.cs:5456-5578`):
`Hook.ShouldTakeExtraTurn` per player decides `PlayersTakingExtraTurn`, then `SwitchSides`,
then `Hook.AfterTakingExtraTurn` for each extra-turn player. Sim: the extra-turn flag is
step-local (§9).

`SwitchSides` (`Combat/CombatManager.cs:7436-7502`):

- Player side and no extra turn → Enemy side.
- Otherwise → Player side. `RoundNumber++` unless this is an extra turn.
  `IncrementTurnNumber` for the extra-turn players or all players
  (`Entities.Players/PlayerCombatState.cs:299-302`).
- Then `OnSideSwitch` for every creature (`Entities.Creatures/Creature.cs:1284-1294`). This is
  empty for players (`Entities.Players/Player.cs:1274-1276`) and clears `SpawnedThisTurn` for
  monsters (`Models/MonsterModel.cs:774-778`). Sim: `MonsterLifecycle.OnSideSwitch` per
  monster.

### 3.9 Enemy turn

`ExecuteEnemyTurn` (`Combat/CombatManager.cs:3088-3276`):

- For each enemy of a snapshot of `Enemies`, skipping enemies removed meanwhile:
  `Creature.TakeTurn` (`Entities.Creatures/Creature.cs:247-...`: throws unless an enemy
  monster; performs the move unless `SpawnedThisTurn`), then `CheckWinCondition`. Return if
  the combat is no longer in progress; `EndEnemyTurn` is then not run.
- After the loop: `EndEnemyTurn`.
- `actionDuringEnemyTurn` (a multiplayer callback) is not modelled.

Sim: `MonsterLifecycle.TakeTurn` (monsters agent), one `CheckWinCondition` per enemy.

`EndEnemyTurn` / `EndEnemyTurnInternal` (`Combat/CombatManager.cs:1893-2001`, `:2041-2109`):
`Hook.BeforeSideTurnEnd(Enemy)` → `PlayerCombatState.EndOfTurnCleanup()` →
`Hook.AfterSideTurnEnd(Enemy)` → `CheckWinCondition` → `SwitchSides` unless the combat is
ending.

### 3.10 Win and loss (`Combat/CombatManager.cs:7124-7211`, `:6548-6567`)

- `IsCombatEnding` (`:6548-6567`): false unless the combat is in progress; then true when a loss
  is pending, or when no primary enemy is alive and `Hook.ShouldStopCombatFromEnding` is false.
  Sim: `CombatStatus.IsEnding` (pipeline), with the same first test on `cs.IsInProgress`.
- `LoseCombat` (`:7124-7131`) only records `PendingLoss`; `IsInProgress` stays true. Its only
  caller, `CreatureCmd.Kill`, calls it only while `CombatManager.IsInProgress`
  (`Commands/CreatureCmd.cs:2397-2404`).
- `CheckWinCondition` (`:7196-7211`):
  - A pending loss → `ProcessPendingLoss` clears it and sets `IsInProgress = false` (`:7133-7142`);
    return true.
  - Otherwise, if ending → `EndCombatInternal` (victory); return true.
  - Otherwise return false.
- Sim representation (persisted on `CombatState`, so it survives `Step` boundaries and clones):
  `CombatStatus.LoseCombat` sets `Outcome = Defeat` when `IsInProgress` and `Outcome == InProgress`.
  A pending loss is `Outcome == Defeat && IsInProgress` (`CombatStatus.IsAboutToLose`);
  `ProcessWinCondition` processes it by setting `IsInProgress = false`, after which `IsEnding` is
  false and `IsOverOrEnding` true, as in the game. Every game `IsInProgress` gate between the
  death and the next win check reads `cs.IsInProgress` and behaves as in the game.
- Victory: `EndCombatWithVictory` sets `IsInProgress = false` (`:1653`), `Outcome = Victory` and
  `Phase = None` (`SetPhaseForAllPlayers(None)`, `:1666`). The rest of `EndCombatInternal` is not
  ported (§9): the run-layer steps (rewards, room exit, relic/run hooks) and `Player.AfterCombatEnd`
  (`:1765-1772`; `Entities.Players/Player.cs:1249-1254`), which removes the player's powers and block
  and clears the combat piles. The terminal sim state therefore keeps hand, piles, powers and block.
- After each executed action other than end turn, the action executor runs
  `CheckWinCondition` if the combat is in progress (`GameActions/ActionExecutor.cs:129-148`).
  Sim: `Step(PlayCard)` does the same.

Card moves into combat piles are refused while the combat is ending (§5). A card that kills
the last enemy therefore stays in the play pile, as in the game.

## 4. Card play

### 4.1 Action (`GameActions/PlayCardAction.cs:115-176`)

- If the card is not in the hand, cancel.
- If `CanPlay` and `IsValidTarget(target)` both hold, then `SpendResources` and
  `OnPlayWrapper(target, isAutoPlay: false, resources)`.
- Otherwise cancel: a no-op in the game, an `InvalidOperationException` in the sim (contract).

Sim: `LegalActionGenerator.TryResolve` validates without mutation; then
`ActionExecutor.PlayCard` re-checks `CanPlay` and `IsValidTarget` before mutating.
`CanPlay`/`IsValidTarget` are the cards agent's ports of `CardModel.CanPlay` and
`CardModel.IsValidTarget` (`Models/CardModel.cs:3323-3385`).

### 4.2 `SpendResources` (`Models/CardModel.cs:3404-3419`, `SpendStars` `:1399-1450`)

1. `energyToSpend = EnergyCost.GetAmountToSpend()`;
   `starsToSpend = max(0, GetStarCostWithModifiers())`.
2. If `energyToSpend > energy` and `Hook.ShouldPayExcessEnergyCostWithStars`:
   `starsToSpend += (energyToSpend - energy) * 2` and `energyToSpend = energy`.
3. `SpendEnergy(energyToSpend)` → `EnergyCmd.SpendEnergy` (pipeline: captures X, loses
   energy, fires `AfterEnergySpent`).
4. `SpendStars`: `LastStarsSpent = amount`; if `amount > 0`, `LoseStars` (`:1432-1436`).
   `Hook.AfterStarsSpent` follows in the game; it is not dispatched (§9).

### 4.3 `OnPlayWrapper` (`Models/CardModel.cs:3450`, state machine `:257-1056`)

1. `CurrentTarget = target`, `CurrentPlayIndex = 0`.
2. Move the card to the Play pile:
   - auto-play: `CardPileCmd.Add(Play, Bottom)`;
   - manual: `AddDuringManualCardPlay` (`Commands/CardPileCmd.cs:877-1011`, `:3837`), which
     returns if the combat is over or ending, else removes the card from its pile and appends
     it to Play. `Hook.AfterCardChangedPiles` follows in the game; it is not dispatched (§9).
3. `resultLocation = GetResultLocationForCardPlay()` (`:935`): Power card or dupe → None;
   `Exhaust` keyword or `ExhaustOnNextPlay` → Exhaust; otherwise Discard
   (`Models/CardModel.cs:3491-3503`, cards agent).
   `Hook.ModifyCardPlayResultLocation` and the `AfterModifying…` follow-ups are not dispatched (§9).
4. `GeneratePlayCount` (`:50-121`): `GetEnchantedReplayCount() + 1`, then
   `Hook.ModifyCardPlayCount`, then `Hook.AfterModifyingCardPlayCount`.
5. If the player is dead, return (`:1006`). Otherwise `BeginCardOrPotionEffect` (`:1009`).
6. For `i` from 0 while `i < playCount` and the combat is not over or ending (`:618-620`):
   - `CurrentPlayIndex = i`.
   - Build `CardPlay { Target, ResultPile, Resources, IsAutoPlay, PlayIndex, PlayCount }`.
   - `Hook.BeforeCardPlayed`, then `card.OnPlay`.
   - If the player is dead, leave the loop (see below). Then the affliction's `OnPlay`, then
     the enchantment's `OnPlay`, each followed by the same dead check. Neither exists in P0.
   - If `CombatManager.IsInProgress`, `Hook.AfterCardPlayed`; then the same dead check.

   Leaving the loop because the player died sets the "dead exit" flag. After
   `EndCardOrPotionEffect` the method then returns without the result-pile move, without
   `CheckForEmptyHand` and without the cleanup (flag `<>7__wrap9` set to 1 at `:779` and tested
   at `:908-909`). Sim: `return` at each dead check.
7. Normal exit: the post-play visual wait (stripped), then, if the card is still in the Play
   pile, move it to the result pile: None → `CardPileCmd.RemoveFromCombat`;
   Exhaust → `CardCmd.Exhaust(causedByEthereal: false)`; otherwise
   `CardPileCmd.Add(pileType, position)` (`:942-975`). The multiplayer
   `GiveToAnotherPlayer` branch (`:987`) is not modelled.
8. `CheckForEmptyHand` (`:882`).
9. Cleanup (`:1017-1036`): `EnergyCost.AfterCardPlayedCleanup`, then drop temporary star costs
   that clear when played, then `CurrentTarget = null`, `CurrentPlayIndex = 0`. Sim:
   `CardInstance.AfterCardPlayedCleanup` (cards agent).

Win check after the play: §3.10.

## 5. Piles (`CardPileCmd`)

| Command | Game | Sim behaviour |
| --- | --- | --- |
| `Add` (single/batch) | `Commands/CardPileCmd.cs:3283-3476` | Refused while the combat is ending (`:3291`), for a dead owner (`:3312`) and when not in progress (`:3387`, which also covers setup, §2.2). Sim gate `CanMoveIntoCombatPile`: not `CombatStatus.IsEnding`, player alive, `cs.IsInProgress`. Per card: full-hand redirect, remove from the old pile, insert by position (§1). `Hook.AfterCardEnteredCombat` when the card had no pile (`:3434-3437`). A batch moves the cards in list order. |
| `Draw` / `DrawInternal` | `Commands/CardPileCmd.cs:3976-4057` | Return 0 if over or ending. If `Hook.ShouldDraw` is false, `Hook.AfterPreventingDraw` and return 0. `requested = ceil(count)`; return if `room = 10 - hand` is 0. Then loop per requested draw: stop if `room <= 0`, the combat is over or ending, or no draw is possible (`draw + discard == 0` or hand full, `:4186-4199`); `ShuffleIfNecessary`; check draw possible again; take `drawPile[0]` (top) and stop if the hand is full; Add to the hand; `Hook.AfterCardDrawn(card, fromHandDraw)`; recompute `room`. Returns the number of cards drawn (game: the list); `Draw(cs, player)` returns the single card or null. |
| `ShuffleIfNecessary` | `Commands/CardPileCmd.cs:3028-3122`, `:4091` | Shuffle iff the draw pile is empty and the discard pile is not. |
| `Shuffle` | `Commands/CardPileCmd.cs:2664-2960`, `:4060` | Return if over or ending. `list = discard cards + draw cards`; `list.StableShuffle(Rng.Shuffle)` (`Extensions/ListExtensions.cs:10-19`): sort a copy with `CardModel.CompareTo`, write it back, then `UnstableShuffle` (`:21-36`). Then `Hook.ModifyShuffleOrder(..., false)`, the debug forced-top card (not modelled), a batched Add to the draw pile bottom, and `Hook.AfterShuffle` if the combat is not over or ending. Sim sort key `CompareForShuffle`: same reference → 0, then ordinal card id, then upgrade level. This mirrors `CardModel.CompareTo` (`Models/CardModel.cs:3649-3671`) → `AbstractModel.CompareTo` (`Models/AbstractModel.cs:126-137`) → `ModelId.CompareTo`, ordinal category then entry (`Models/ModelId.cs:66-74`); all cards share one category. |
| `AddGeneratedCardToCombat` | `Commands/CardPileCmd.cs:3196-3249` | Return unless `cs.IsInProgress` (`:3213-3216`; true during a pending loss, false during setup). Throw if the card already has a pile or the target is not a combat pile. Add, then `Hook.AfterCardGeneratedForCombat(card, creator)`. |
| `Discard` | `CardCmd.Discard` → `DiscardAndDraw` (`Commands/CardCmd.cs:721-964`, `:1811-1851`) | Return if over or ending; Add to discard; `Hook.AfterCardDiscarded`. Sly cards throw `NotSupportedException` (§9). |
| `Exhaust` | `CardCmd.Exhaust` (`Commands/CardCmd.cs:965-1078`, `:1867-1880`) | Return if over or ending; Add to exhaust bottom; `Hook.AfterCardExhausted(causedByEthereal)`, even when the Add was refused. |
| `RemoveFromCombat` | `Commands/CardPileCmd.cs:2205-2466`, `:3151-3177` | Remove from its pile; throw if the card is in no pile. |

Pile order: index 0 is the top of the draw pile; draws take index 0; Bottom appends.

## 6. Decision API

### 6.1 Legal actions (`LegalActionGenerator.Fill`)

Nothing is listed unless `Outcome == InProgress`, `cs.IsInProgress` and `CurrentSide == Player`
(`LegalActionGenerator.IsAtDecisionPoint`). Otherwise, for each hand index in ascending order,
skip unless `card.CanPlay(cs, out _)`, then:

- `TargetType.AnyEnemy`: `Play(i, t)` for each monster index `t` (ascending) where
  `IsValidTarget`, `Hook.ShouldAllowHitting` and `Hook.ShouldAllowTargeting` all hold. This
  mirrors the UI:
  - `CardModel.CanPlayTargeting` = `IsValidTarget && CanPlay` (`Models/CardModel.cs:3314-3321`,
    `TryManualPlay` `:3387-3395`);
  - targeting cancels on a creature that is not `IsHittable`, i.e. `!IsDead &&
    ShouldAllowHitting` (`Nodes.Combat/NTargetManager.cs:403-416`,
    `Entities.Creatures/Creature.cs:640-655`);
  - targets are alive enemies (`NTargetManager.cs:547-570`) that pass
    `Hook.ShouldAllowTargeting` (`NTargetManager.cs:655-671`).
- Any other target type: `Play(i)` with target null, if `IsValidTarget(null)`. Non-`AnyEnemy`
  cards are played without a creature target (`Nodes.Combat/NCardPlay.cs:147-190`).
- `EndTurn` last.

`Fill` appends to the buffer. `CombatSim.LegalActions()` clears and reuses an internal buffer
(valid until the next call); `LegalActions(List<GameAction>)` clears the caller's buffer
first.

### 6.2 `Step`

- `TryResolve` rejects any of the following with `InvalidOperationException`. The state is
  unchanged: validation reads only, and nothing mutates before it passes.
  - the combat is over, still being set up (§2.2), not in progress, or the side is not Player;
  - `EndTurn` with a card or target index other than `NoIndex`;
  - `PlayCard` with a hand index out of range;
  - an `AnyEnemy` card with a target index out of range or an illegal target;
  - a non-`AnyEnemy` card with a target index other than `NoIndex`;
  - `CanPlay` false, or `IsValidTarget` false;
  - an unknown `ActionKind`.
- `PlayCard`: `ActionExecutor.PlayCard`, then `CheckWinCondition` if in progress.
- `EndTurn`: phase one, phase two, side switch, then the turn loop until the next decision
  point or a terminal state.
- `IsLegal(action)` = `TryResolve` without side effects.

### 6.3 Facade

- `CombatSim.Create(seed, deckIds, encounterId, ascension = 0, currentHp = null, maxHp = null)`;
  `CombatSim.Create(RunRngSet rng, deckIds, encounterId, ascension = 0, currentHp = null, maxHp = null)`
  (the set is deep-cloned first); and a test seam overload that takes
  `Action<CombatState> populateMonsters` in place of the encounter spawn. Inputs: §2.1.
- `FromState`, `State`, `Outcome`, `IsTerminal` (`Outcome != InProgress`).
- `Clone()` = `new(State.DeepClone())`.
- `StateHash()` = `StateHasher.Hash(State, writer)` with a per-instance writer created on first use
  (integration change; `Clone` builds a new `CombatSim`, so clones never share it);
  `StateHash(CanonicalWriter scratch)` takes a caller-owned writer for search loops (state spec §7).
- `WriteCanonical`.

The loop keeps no state outside `CombatState`: the turn-state flags `IsInProgress` and
`IsStarting` live there (cloned and hashed), and the extra-turn flag is a local of one `Step`
(§9), so `Clone`/`DeepClone` covers everything.

### 6.4 Building blocks (contract §4.8)

`StartPlayerTurn` (requires the Player side), `EndPlayerTurn` (Player side: phase one, phase
two, switch), `RunEnemyTurn` (Enemy side: the whole enemy turn including the switch back),
`CheckWinCondition`, `CheckForEmptyHand`, `SetUpCombat`, `StartCombat`, `DeckForCombat`. They are
for tests and tools. After `EndPlayerTurn` the side is Enemy, and `Step` refuses actions until
`RunEnemyTurn` and `StartPlayerTurn` have run. Apart from their side check, the turn building
blocks do nothing while the combat is not in progress (setup or terminal), like `StartTurn`'s
first check (`:4865`).

## 7. Stripped Godot/VFX/coroutine/network sites

| Site | Game | Transformation |
| --- | --- | --- |
| `Cmd.Wait` / `Cmd.CustomScaledWait` (combat start 0.5–1 s, card play 0.25–0.35 s and 0.15–0.3 s, shuffle per-card waits) | `Commands/Cmd.cs:75,172`; `Combat/CombatManager.cs:4611`; `Models/CardModel.cs:867,915`; `Commands/CardPileCmd.cs:2755-2757,2827` | removed (non-interactive semantics) |
| async `Task` chains, `TaskCompletionSource` signals, `ReadyLock`, cancellation tokens, `RunTurnLoopAfter`/`WaitForPreviousTurnLoopToFinish`, pause/unpause | `Combat/CombatManager.cs` throughout | direct sequential calls; one `Step` = one decision |
| end-turn / begin-enemy-turn signals, `SetReadyToEndTurn`, `PlayersReadyToEndTurn`, `ActionQueueSynchronizer`, `ActionExecutor` queue | `Combat/CombatManager.cs:6687-6762,6811-6975`; `GameActions/ActionExecutor.cs` | `Step(EndTurn)`; the action queue becomes the `Step` call |
| `HookPlayerChoiceContext` / `BranchingPlayerChoiceContext` / `AssignTaskAndWaitForPauseOrCompletion` | turn start, card play | removed; P0 has no player choices |
| card nodes, tweens, `NCard.FindOnTable`, `AnimMultiCardPlay`, `PlayPowerCardFlyVfx`, the play-pile lerp | `Commands/CardPileCmd.cs:3478-3975`; `Models/CardModel.cs:619-640,1068-1220` | removed |
| `ThinkCmd.Play` (NO_DRAW / HAND_FULL bubbles), `SfxCmd`, `NDebugAudioManager`, music, banners (`NCombatStartBanner`, `NPlayerTurnBanner`), FTUE | `Commands/CardPileCmd.cs:3438-3441,4186-4199`; `Combat/CombatManager.cs:4155,4546,4603-4607,5313` | removed (the draw-possible checks keep their boolean result) |
| `card.InvokeDrawn`, `Played`, `EnergyCostChanged`, `StarCostChanged`, `InvokeExecutionFinished`, `CreaturesChanged`, `TurnEnded`, `CombatSetUp`, `CombatBegan`, `InvokeContentsChanged` events | various | removed; the only subscribers are UI/state-tracker nodes (e.g. `Combat/CombatStateTracker.cs:194`) |
| `RunManager.ChecksumTracker.GenerateChecksum`, `Log.*`, `CombatManager.History.*` | various | removed (history: see §9) |
| `TestRngInjector.ConsumeInitialShuffleOverride`, `DebugForcedTopCardOnNextShuffle` | `Entities.Cards/CardPile.cs:244`; `Commands/CardPileCmd.cs:2761-2769` | removed (test/debug only) |
| `LocalContext.NetId` / `IsMe` gates, `GiveToAnotherPlayer`, `actionDuringEnemyTurn`, `RemoveDeadPlayerCardsFromCombat` (only when some player is alive) | multiplayer paths | single player assumed |
| `StartTurn`'s `CustomScaledWait(0.5f, 0.8f)`; `Shuffle`'s closing `CustomScaledWait(0.2f, 0.5f)` and its `Rng.Chaotic` timing jitter; `AddTurnEndCardToPlayPileWithDelay`'s `Cmd.Wait(delay)`; `Task.Yield` in `AfterAllPlayersReadyToBeginEnemyTurn`; the `ActionExecutor` TestMode pause loop (`Task.Delay(50)`, paused/unpaused around combat start) | `Combat/CombatManager.cs:5340`, `:144`, `:263`, `:4434-4443`, `:5376`; `Commands/CardPileCmd.cs:2821`, `:2892`; `GameActions/ActionExecutor.cs:303-322` | removed: under non-interactive semantics each is a no-op or does not reorder rule code; `Rng.Chaotic` is cosmetic (rng spec §7) |

## 8. Dependencies on other areas (beyond the frozen contract)

The loop calls these members of other areas. Since the 2026-10-30 contract sync the pipeline,
monster, state, `StartingDecks` and RNG members below are listed in `CONTRACTS.md` §4 (frozen);
the `CardInstance` members are the cards agent's public API (cards spec §9):

- Pipeline:
  - `CombatStatus.IsEnding/IsOverOrEnding/LoseCombat/IsAboutToLose`;
  - `CreatureCmd.BeforeTurnStart/AfterTurnStart` (turn-1 block exemption);
  - `EnergyCmd.ResetEnergy/AddMaxEnergyToCurrent/SpendEnergy/GainEnergy/LoseEnergy/SetEnergy`;
  - the `Hook` overloads with `ref List<AbstractModel>? modifiers` (`ModifyHandDraw`,
    `ModifyCardPlayCount`) and `out AbstractModel? preventer` (`ShouldDraw`);
  - `Hook.ShouldAllowHitting/ShouldAllowTargeting`.
- Monsters: `MonsterLifecycle.SetUpForCombat/AfterCreatureAdded/PrepareForNextTurn/TakeTurn/OnSideSwitch`,
  `MonsterSpawner.SpawnEncounter`, `EncounterRegistry.Get`.
- State:
  - `PlayerState.EndOfTurnCleanup/IncrementTurnNumber/LoseStars/FindPileContaining/GetPile/AllPiles`;
  - `CardPile.AddInternal/RemoveInternal/MoveToTopInternal/MutableCards/IsCombatPileType`;
  - `MonsterState.SpawnedThisTurn`; `CombatState.NextCreatureId/IsInProgress/IsStarting`.
- Cards:
  - `StartingDecks.IroncladStartingHp/IroncladMaxEnergy/IroncladCharacterId`,
    `StartingDecks.IsIroncladStartingDeck/ApplyAscensionEffectsToDeck`;
  - `CardInstance.CanPlay/IsValidTarget/OnPlay/OnTurnEndInHand/GetResultLocationForCardPlay`;
  - `CardInstance.GetEnchantedReplayCount/GetStarCostWithModifiers/SetCurrentTarget`;
  - `CardInstance.AfterCardPlayedCleanup/ShouldRetainThisTurn/HasTurnEndInHandEffect/IsSlyThisTurn/UpgradeLevel`;
  - `CardEnergyCost.GetAmountToSpend(cs, card)`.
- RNG: `RngCollectionExtensions.UnstableShuffle` and `StableShuffle(list, rng, Comparison<T>)`.

## 9. Deferred, approximated, not dispatched

- Hooks that the pipeline dispatcher does not declare are not called:
  - `AfterAutoPrePlayPhaseEntered` (`Combat/CombatManager.cs:3891`) and
    `AfterAutoPostPlayPhaseEntered` (`:2389`);
  - `AfterCardChangedPiles` (`Commands/CardPileCmd.cs:3466`, `:980`);
  - `ModifyCardPlayResultLocation` and its after-hook (`Models/CardModel.cs:936-...`);
  - `AfterStarsSpent` (`:1436`);
  - orb-queue turn hooks (`Combat/CombatManager.cs:5119`, `DoTurnEnd`).

  None has a P0 listener. They become relevant with retain/exhaust relics, Defect, and
  star cards.
- Turn-1 `Enchantment.ShouldStartAtBottomOfDrawPile` bottom moves
  (`Combat/CombatManager.cs:4248-4262`), affliction and enchantment `OnPlay` in the play loop,
  and `HasBeenRemovedFromState` in `Add` (`Commands/CardPileCmd.cs:3312`). There are no
  enchantments or afflictions in P0; a removed card that is re-added is treated as entering
  combat.
- `PendingLoss` is not a separate field: a pending loss is `Outcome == Defeat` while
  `IsInProgress` is true (§3.10). Because the sim only records a loss while the combat is in
  progress, as the game's only caller does, the two encodings agree on every reachable state. The
  game could also hold a `PendingLoss` after a victory or during setup if `LoseCombat` were called
  directly there; nothing in the decompile does that.
- `PlayersTakingExtraTurn` is not persisted: the extra-turn flag lives for one `Step`
  (`SwitchFromPlayerToEnemySide` → next `StartTurn`). A power that reads the set outside that
  window would see nothing. `RampartPower.AfterSideTurnStart` reads it
  (`Models.Powers/RampartPower.cs:54`); no P0 power does.
- The side-turn hooks take `(cs, side)` only. The game passes the creatures starting or ending
  the turn as `participants` (`Combat/CombatManager.cs:4914`, `:5080`, `:2457`, `:2967`,
  `:2055-2088`; `Hooks/Hook.cs:8608`, `:6356`), which differs from "every creature on the side"
  during extra turns and after a mid-turn spawn. The P0 powers ignore it.
- Keywords are read with `CardInstance.HasKeyword` (the card's own mask) for turn-1 Innate draws,
  turn-end Ethereal and Retain. The game reads `card.Keywords`, which adds
  `Hook.ModifyKeywordsInCombat` (`Models/CardModel.cs:1940`, `:2775-2814`); only `HexPower` uses it,
  and it is not ported.
- `Player.AfterCombatEnd` after a victory (powers removed, piles cleared, block lost;
  `Entities.Players/Player.cs:1249-1254`, `PlayerCombatState.cs:279-297`) and the rest of
  `EndCombatInternal` are not run (§3.10). The outcome is the same; the terminal snapshot and hash
  differ from the game's post-combat state.
- `HasBeenRemovedFromState` after `RemoveFromCombat` (`Commands/CardPileCmd.cs:2411`, read by
  `Add` at `:3312`) is not tracked.
- Card/potion effect depth (`Combat/CombatManager.cs:6517-6531`, `EndCardOrPotionEffect`
  `:1430-1509`) is not tracked. `CheckForEmptyHand` would mis-fire only for nested auto-play,
  which P0 does not have.
- Sly auto-play on discard (`CardCmd.DiscardAndDraw`, `Commands/CardCmd.cs:721-964`):
  `CardPileCmd.Discard` throws `NotSupportedException` for a Sly card before mutating.
- `EncounterDef` has no slots, so `SortEnemiesBySlotName` (`Combat/CombatManager.cs:6999-7002`,
  `Combat/CombatState.cs:958-964`) is not applied. Monster order is spawn order.
- `CombatHistory` (`History.CardDrawn/Discarded/Exhausted/Generated/CardPlayStarted/...`) is
  not modelled. Cards or powers that count history ("cards played this turn") need it later.
- Post-victory `EndCombatInternal` (rewards, room, run hooks) and auto-play
  (`CardCmd.AutoPlay`, `CardPileCmd.AutoPlayFromDrawPile`) are not ported. `OnPlayWrapper`
  accepts `isAutoPlay` for later use.
- `Step` and `Fill` require `CurrentSide == Player`, `Outcome == InProgress` and
  `cs.IsInProgress`; `Phase` is not checked (it is always `Play` at a decision point).
- The game's `List.Sort()` with `CardModel.CompareTo` and the sim's
  `List.Sort(Comparison)` use the same .NET introsort, so ties (same id and upgrade level)
  land in the same order given the same input order.

## 10. Invariants at a decision point

At a decision point (`Outcome == InProgress`):

- `IsInProgress` is true and `IsStarting` false;
- `CurrentSide == Player`, `Phase == Play`, and the Play pile is empty unless a card
  effect left a card there;
- `Hand.Count <= 10`;
- `RoundNumber` and `TurnNumber` count from 1;
- `LegalActions()` ends with `EndTurn`.

At a terminal state:

- `IsInProgress` is false (victory or processed loss).
- Victory: `Phase == None`. The killing card may remain in the Play pile.
- Defeat during the enemy turn: the side stays Enemy, as in the game, where the turn loop
  stops.

## 11. Tests (`Sts2Sim.Tests/LoopTests.cs`)

The fixture `LoopDummy : MonsterDef` is test-local: fixed HP, and one self-looping move. The
move is `LOOP_BITE` (a single attack of N through `DamageCmd.Attack(cs, N).FromMonster(self).Execute()`,
logged) or `LOOP_WAIT`. Integration fix: the bite first chained `.Targeting(targets[0])` after
`FromMonster`, which throws "Already set to target opponents of attacker" in the game as in the sim,
because `FromMonster` already calls `TargetingAllOpponents` (`Commands.Builders/AttackCommand.cs:781-792`,
`:794-809`); monster moves target through `FromMonster` alone (e.g. `Models.Monsters/Nibbit.cs:53`). A test
spawns dummies through the `CombatSim.Create(..., populateMonsters)` seam, so the tests do not
depend on the monsters agent's catalogue. `SetupProbeMonster` (fixed HP, one wait move) runs a
probe from `AfterAddedToRoom`.

| Test | Asserts |
| --- | --- |
| `SetUpThenStartReachesFirstDecision` | Before start: whole deck in the draw pile, energy 0, turn 1, round 1, monster id 1, spawned flag set, move graph built but no move rolled; `IsStarting` true, `IsInProgress` false, not ending, over-or-ending; no legal actions and `Step` throws. After start: `IsStarting` false, `IsInProgress` true, a second `StartCombat` throws, `Play` phase, energy 3, HP 80/80, hand 5, draw 5, first move rolled, spawned flag still set. |
| `SetUpWindowGatesCommandsLikeTheGame` | §2.2 windows A and B. The same probe runs during the spawn (window A, after a Dexterity 3 apply) and from `AfterAddedToRoom` (window B). A: flags false/false, guarded `ModifyBlock(5)` = 5 (listeners silenced), `GainBlock(13)` = 0, Strength applied, no draw, generated card refused. B: flags true/false, `ModifyBlock(5)` = 8 (Dexterity runs), `GainBlock(13)` = 0 (the `CubexConstruct` case), Strength applied, `Draw` refused with a full draw pile, generated card refused. After start: monster block 0, Strength 4, Dexterity 3, normal hand 5 / draw 5, and the same `GainBlock(13)` now gives 16. |
| `PlayerDeathOutsideAnActiveCombatRecordsNoLoss` | A player killed during setup leaves `Outcome == InProgress` (`Kill` → `LoseCombat` only while in progress). In progress: `Defeat` is pending (`IsInProgress`, `IsEnding`, `IsAboutToLose` true) until `CheckWinCondition`, which sets `IsInProgress` false, after which the combat is over-or-ending but not ending. |
| `ScriptedStrikeFightTurnByTurn` | 10 Strikes vs a passive 40 HP dummy. Per play: energy, hand, discard and monster HP. Exact legal actions. Only `EndTurn` at 0 energy (`EnergyCostTooHigh`). Round 2: draw 0, discard 5, spawned flag cleared. Round 3: reshuffle (hand 5, draw 5). The killing Strike gives Victory: monster removed, killing card left in Play, phase None, no legal actions, `Step` throws. |
| `EnemyAttackHitsBlockThenHpAndBlockClearsNextTurn` | Defend 5 block vs bite 7: HP 78, block cleared at turn 2; 15 block absorbs the bite; HP 71 after an unblocked bite. |
| `PlayerBlockIsNotClearedAtFirstTurnStart` | Block 9 survives turn 1 start and is cleared at turn 2. |
| `EnemiesActInListOrderEachTurn` | Enemies act in list order every turn: A, B, A, B; HP 73 then 66. |
| `PlayerDeathEndsEnemyTurnWithDefeat` | A lethal bite gives Defeat. The second enemy does not act; the side stays Enemy; round 1; no legal actions. |
| `UnaffordableCardIsNotLegal` | 2-cost Bash at 1 energy is unplayable (`EnergyCostTooHigh`), unlisted and illegal; `Step` throws and leaves the state unchanged. |
| `InvalidTargetsAndIndicesThrow` | Missing, out-of-range or negative targets; out-of-range hand indices; `EndTurn` with indices; an unknown kind; a target index on a self-target card. Each throws and leaves the state unchanged (fingerprint and hash). |
| `KilledTargetLeavesTheTargetList` | A killed monster is removed and the indices shift. The combat continues while another primary enemy lives, and the card goes to the discard pile. |
| `LegalActionsOrderHandThenTargetThenEndTurn` | 5 cards × 3 targets in order, then `EndTurn`; the buffer overload clears; every listed action `IsLegal`. |
| `OpeningDrawOrderFollowsShuffleStream` | Hand and draw-pile instance ids equal an independent Fisher–Yates on `RunRngSet(seed).Shuffle`; 9 draws for 10 cards. |
| `ReshuffleSortsByCardIdThenContinuesShuffleStream` | The turn-3 reshuffle equals sort by id (Defend before Strike) then shuffle on the continuing stream; counter 18. |
| `ShuffleComparerOrdersByIdThenUpgrade` | Ordinal id, then upgrade level, then equality. |
| `PileMovesFollowGameRules` | Top insert; 10.5 draws round up and cap at the hand limit; a full hand draws nothing; an add to a full hand goes to the discard bottom; generated-card rules; `RemoveFromCombat` throws for a card in no pile; `Discard` and `Exhaust`. |
| `DrawReshufflesOnlyWhenTheDrawPileIsEmpty` | A reshuffle happens only when the draw pile is empty (rng counter 2 → 4 → 4); drawing stops when both piles are empty. |
| `SameSeedAndActionsGiveSameStates` | Two runs give identical fingerprints and hashes and end in Victory; another seed gives a different opening. |
| `CloneStepsIndependently` | Clone hash = source hash, including through a reused scratch writer. Stepping the clone leaves the source unchanged; the same actions converge. |
| `EndPlayerTurnAndRunEnemyTurnBuildingBlocks` | `EndPlayerTurn` → Enemy side, phase End, hand flushed. `StartPlayerTurn` throws on the Enemy side. `RunEnemyTurn` → bite, round 2, phase None. `StartPlayerTurn` → Play, energy 3, hand 5. |
