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

## 12. W1 round 3 (Part B §B6; §B12 A11, A13, A15, A17, A18, A23)

This section is appended; where it differs from §1–§11, it wins (§12.1 lists the superseded P0
statements). Code: `Combat/Loop/CombatLoop.cs` (setup, building blocks, `Step` routing),
`TurnFlow.cs` (turn loop, turn start, TurnSetup emulation), `TurnFlow.TurnEnd.cs` (player and
enemy turn end, side switch, extra turns), `TurnFlow.CombatEnd.cs` (win/loss,
`EndCombatInternal`), `TurnFlow.Effects.cs` (effect depth, `CheckForEmptyHand`),
`CardCmd.Play.cs` (play half of `CardCmd`), `ActionExecutor.cs`, `CardPileCmd.cs`,
`PlayerCmd.cs`. Tests: `Sts2Sim.Tests/TurnFlowTests.cs` (§12.17).

### 12.1 P0 statements superseded

| P0 text | Now |
| --- | --- |
| §2.3 `ResetCombatState`: "nothing to reset: the state is new" | §12.2: the run's `PlayerState` is reset in place |
| §2.3 deck → draw pile: `PopulateDrawPile`, `CardInstance.Create(cs, id)` per id | §12.2: `Player.Deck` (run ids, owner attached); draw pile = `CloneForCombat` copies |
| §3.2 step 3: `[player]` on the player side, else a snapshot of `cs.Monsters` | §12.3: one A17 snapshot (pets included) shared by the start hooks and the per-creature loops |
| §3.2 step 8: `SetupPlayerTurn` runs inline before `AfterSideTurnStart` | §12.3: still so without a prompt; a first pause runs the deferred steps at the pause |
| §3.4: `AfterAutoPrePlayPhaseEntered` not dispatched; the effect-depth condition always holds | §12.4, §12.8 |
| §3.5 step 2: `AfterAutoPostPlayPhaseEntered` not dispatched | §12.4 |
| §3.6 step 1: `OrbQueue.BeforeTurnEnd` (the P0 code did not call it) | §12.5: called first |
| §3.6, §3.7, §9: Ethereal, Retain and Innate read from the card's own mask | §12.10: the in-combat keyword set |
| §3.8, §6.3, §9: the extra-turn flag is step-local; `PlayersTakingExtraTurn` not persisted | §12.6 |
| §3.10 victory bullet and §9: `EndCombatWithVictory`; the terminal state keeps hand, piles, powers and block | §12.7 |
| §4.2 step 4: `AfterStarsSpent` not dispatched | the spend is `StarsCmd.SpendStars` (character-mechanics, A16), which owns that hook |
| §4.3 step 2: `AfterCardChangedPiles` not dispatched; step 3: `ModifyCardPlayResultLocation` not dispatched; step 6: "the affliction's `OnPlay`, then the enchantment's `OnPlay`" | §12.9: both dispatched; the order is enchantment, then affliction |
| §5 `Discard` row: Sly cards throw `NotSupportedException` | §12.9: Sly auto-play |
| §5 `Add` row: no deck target, no owner or removed-card checks | §12.10 |
| §6.2: `Step` handles `PlayCard` and `EndTurn`; any other kind is unknown | §12.12 |
| §9: side-turn hooks take `(cs, side)`; `HasBeenRemovedFromState` untracked; effect depth untracked; `CombatHistory` not modelled; auto-play not ported; turn-1 enchantment bottom moves and enchantment / affliction `OnPlay` not called | §12.3–§12.13 (ported); what is still deferred is §12.15 |
| §10 terminal: "Victory: `Phase == None`. The killing card may remain in the Play pile." | §12.7: after a victory every combat pile is empty |
| §11 rows `SetUpWindowGatesCommandsLikeTheGame` (12 ids), `ScriptedStrikeFightTurnByTurn` (killing card left in Play), `OpeningDrawOrderFollowsShuffleStream` (ids 0..9) | §12.16 |

### 12.2 Combat setup, deck and combat copies

Standalone run, built by every `CreateCombat` / `SetUpCombat` overload that takes ids or specs
(B6 bullet 1):

1. HP and deck rules as §2.1 (the id overloads apply `DeckForCombat`; a `CardSpec` deck is used
   as given, as in P0).
2. `PlayerState(0, IRONCLAD, currentHp, maxHp, 3)`, then
   `RunState.CreateStandalone(player, rng, ascension)` (state; applies the run-start ascension
   effects once, A12). Only `CombatSim.Create(RunRngSet, ...)` deep-clones the set (D4).
3. Deck, per spec in order: `CardInstance.ResolveDef`, `new CardInstance(def,
   run.AllocateCardInstanceId())`, `AttachTo(player)`, `def.AfterCreated(card)`, `Upgrade()` when
   the spec says so, append to `Player.Deck`. Game: run cards come from `RunState.CreateCard`
   (`Runs/RunState.cs:703-709`: `ToMutable`, owner set by `AddCard` `:718-725`, `AfterCreated`).

Combat, `SetUpCombat(RunState, PlayerState, encounter, context)` (public, for run-layer callers):

1. Throw if `run.Combat` is set ("Make sure to reset the combat before setting up a new one.",
   `Combat/CombatManager.cs:6578-6581`) or the player is not one of the run's players.
2. `new CombatState(run, player, encounterId)`; round 1, Player side, turn 1, phase None; spawn
   (window A, §2.3).
3. `IsStarting = true` (window B, `:6582`).
4. `ResetCombatState` (`:6592` → `Entities.Players/Player.cs:1209-1212`). The game builds a new
   `PlayerCombatState` (`Entities.Players/PlayerCombatState.cs:257-277`): five new piles, energy
   and stars 0, `TurnNumber = 1` (`:41-48`), default phase, no pets, a new `OrbQueue` with
   `Clear()` and `AddCapacity(BaseOrbSlotCount)`. The sim keeps the run's `PlayerState` and resets
   the same fields in place. `AddCapacity` is skipped when the base count is 0: the call is
   `Capacity += capacity` (`Entities.Orbs/OrbQueue.cs:414-417`) right after `Clear` set the
   capacity to 0 (`:408-412`), so skipping 0 changes nothing.
5. `PopulateCombatState` (`Combat/CombatManager.cs:6599-6611` → `Entities.Players/Player.cs:1214-1233`):
   for each deck card in deck order, `CombatState.CloneCard` (`Combat/CombatState.cs:679-684`,
   owner kept) with `DeckVersion` = the deck card, `DrawPile.AddInternal`; then
   `RandomizeOrderInternal` on `Rng.Shuffle`. Sim:
   `drawPile.AddInternal(deckCard.CloneForCombat(cs.AllocateCardInstanceId()))` (cards-kernel,
   A23: owner kept, `DeckVersionId = source.InstanceId`), then `ShuffleInitialDrawPile` (§2.3).
6. `AddCreature` for every creature of `State.Creatures` (allies, then enemies, `:6613-6625`):
   `MonsterLifecycle.SetUpForCombat` for every monster creature (pets in ally order, then
   monsters), plus the slot sort (§2.3).

`StartCombat`: `AfterCreatureAdded` for the same monster-creature snapshot (the game iterates
`State.Creatures`; the players' call is a no-op, `:4466-4478`), then `IsInProgress`,
`BeforeCombatStart` and the turn loop (§2.3).

Card ids (A13): the allocator is the run's; `cs.AllocateCardInstanceId()` forwards to it. Deck cards
take the first n ids in deck order, their copies the next n in deck order (the copy of deck card k
has id n + k), generated cards follow. The allocator starts at 0, as the P0 combat allocator did,
so the 10-card starter deck is 0..9 and its copies 10..19; A13's "1..n" is read as "the first n
ids". An in-combat upgrade changes only the combat copy (A6).

### 12.3 Turn start (`TurnFlow.cs`, `StartTurn`, `Combat/CombatManager.cs:4850-5406`)

1. Return unless in progress (`:4865`); `SetPhaseForAllPlayers(None)` (`:4869`).
2. One participant snapshot (`:4873-4882`, A17): player side =
   `MonsterLifecycle.PlayerSideParticipants(cs)` (the extra-turn players' creatures, else a copy of
   `cs.Allies`: the player, then pets); enemy side = `MonsterLifecycle.EnemySideParticipants(cs)`
   (a copy of `cs.Monsters`). The starting players are the extra-turn players, else every player.
3. `CreatureCmd.BeforeTurnStart` per snapshot creature (`:4901`);
   `Hook.BeforeSideTurnStart(side, snapshot)` (`:4914`).
4. Player side: phase Start; unless an extra turn, `PrepareForNextTurn` for every enemy
   (`:5291-5323`).
5. `CreatureCmd.AfterTurnStart` per snapshot creature (`:4960`), then `Hook.AfterBlockCleared`
   per snapshot creature (`:5008`). Pets are snapshot creatures, so their block clears as in the
   game.
6. Enemy side: `Hook.AfterSideTurnStart(Enemy, snapshot)` (`:5080`; the same call site serves
   both sides, and label `IL_0809` branches on the side afterwards, `:5349-5356`), the win check
   (`:5261`), `ExecuteEnemyTurn` (`:5276`).
7. Player side, per starting player: `SetupPlayerTurn` (§3.3) inside
   `cs.Choice.PushContext(ChoiceContextKind.TurnSetup, onSetupFirstPause)`. Game: one
   `HookPlayerChoiceContext` per player, `WaitForPauseOrCompletionWithoutAssigningTask`
   (`:5059-5061`).
8. Then, each at most once: `Hook.AfterSideTurnStart(Player, snapshot)` (`:5080`);
   `OrbQueue.AfterTurnStart` per starting player, each in an `Inline` context (`:5116-5120`); the
   dead-player check (`:5146-5159`: every player who is dead or not starting is set ready; when all
   are ready, `StartTurn` returns and the turn ends; single player: the player is dead, and
   `RunTurnLoop` ends the turn at once).
9. Per starting player who is alive (`:5200`): `RunAutoPrePlayPhase` (§12.4) inside
   `PushContext(TurnSetup, onAutoPrePlayFirstPause)` (game: the same context,
   `AssignTaskAndWaitForPauseOrCompletion`, `:5204-5205`).
10. The win check (`:5226`), at most once. Then the decision point.

TurnSetup emulation (B1 pause table). The game waits for the setup task to pause *or* complete, so a
player prompt inside `SetupPlayerTurn` lets `StartTurn` run step 8 and the win check while the
prompt is open. When a prompt is the first pause of a TurnSetup frame, the choice runtime calls the
frame's `onFirstPause` before it raises or answers the prompt. `onSetupFirstPause` runs step 8 in
order and then the win check; `onAutoPrePlayFirstPause` (a first pause inside the pre-play phase)
runs only the win check, the game's step 10. `StartTurn` skips every step already done: the flags
live on a `TurnStartSteps` object that is local to the call and never stored in the state, and a
repeated `onFirstPause` call does nothing.

Two knowing approximations, both flagged with `FidelityFlag.ApproximatedRule`:

- `CombatLoop.StartTurn:combat-ended-at-turn-setup-pause`: the deferred win check ended the
  combat. The game leaves the paused setup task pending, and nothing of it runs after the combat
  end. The sim abandons the setup (a private exception, caught in `StartTurn` only once the combat
  is no longer in progress; every pushed frame is popped by its `using`). The state is terminal.
- `CombatLoop.StartTurn:dead-player-ready-during-turn-setup-pause`: the deferred dead-player check
  found every player ready. The game returns from `StartTurn` and ends the turn with the setup
  still paused; the sim lets the setup finish, then ends the turn.

Both need a combat end or a player death during the setup before its first prompt.

Guarded hook dispatchers yield no listener once the combat is over or ending (§2.2; pipeline
`ListenerSnapshot`, `Hooks/Hook.cs:8794`). With every primary enemy dead before the pause, the
deferred `AfterSideTurnStart` therefore reaches no listener, as in the game.

### 12.4 Auto pre-play and post-play phases

- `RunAutoPrePlayPhase` (`Combat/CombatManager.cs:3812-3935`, order traced through `IL_0074` and
  `IL_00f2`): phase AutoPrePlay (`:3902`), `CheckForEmptyHand` (`:3903`),
  `Hook.AfterAutoPrePlayPhaseEntered` (`:3891`; the dispatcher runs the Early, normal and Late
  passes, `Hooks/Hook.cs:9688`), phase Play (`:3914`).
- Player turn end, phase one (`:2322-2697`): per ending player, phase AutoPostPlay (`:2387`), then
  `Hook.AfterAutoPostPlayPhaseEntered` (`:2389`, `Hooks/Hook.cs:9673`) in an `Inline` context;
  then phase End (`:2426`).

### 12.5 Player turn end, side switch, enemy turn end (`TurnFlow.TurnEnd.cs`)

Phase one (`EndPlayerTurnPhaseOneInternal`, `:2322-2697`). The ending players are the extra-turn
players, else every player. After §12.4: `Hook.BeforeSideTurnEnd(Player, ending players'
creatures)` (`:2457`; pets excluded), then the win check; `DoTurnEnd` per player in an `Inline`
context (`:2503`), then the win check; `Hook.BeforeFlush` per player (`:2594`), then the win check.

`DoTurnEnd` (`:1189-1312`): `OrbQueue.BeforeTurnEnd` first (`:1198`); then, only if in progress
and not ending, classify the hand (turn-end cards; Ethereal by the in-combat keyword set and
`Hook.ShouldEtherealTrigger`); ethereal cards → `CardCmd.Exhaust(causedByEthereal: true)`;
turn-end cards one by one as in §3.6, the result pile chosen by the in-combat Ethereal keyword.

Phase two (`:2841-3003`): `FlushPlayerHand` per player in an `Inline` context (§3.7; retain by
the in-combat keyword set or single-turn Retain), then `Hook.AfterSideTurnEnd(Player, ending
players' creatures)` (`:2967`).

Side switch (`SwitchFromPlayerToEnemySide`, `:5479-5537`): clear `cs.PlayersTakingExtraTurn`; add
the id of each player for whom `Hook.ShouldTakeExtraTurn` holds (`:5486-5489`); `SwitchSides`
(`:7436-7502`), which reads the list: an extra turn stays on the Player side and increments only the
extra-turn players' turn numbers, not the round; then `Hook.AfterTakingExtraTurn` per extra-turn
player (`:5537`). `OnSideSwitch` runs for every creature of `State.Creatures` (players: empty;
pets; monsters).

Enemy turn end (`EndEnemyTurn` `:1893-2001`, `EndEnemyTurnInternal` `:2041-2109`): one
`EnemySideParticipants` snapshot taken before `BeforeSideTurnEnd(Enemy)` (`:2055-2056`) and passed
again to `AfterSideTurnEnd` (`:2088`); the player's `EndOfTurnCleanup` between them; the win check
(`:1950`); `SwitchSides` unless the combat is ending (`:1974-1976`). After a victory
`IsCombatEnding` is false (the combat is no longer in progress), so the game, and the sim, still
switch.

### 12.6 Extra turns

`cs.PlayersTakingExtraTurn` (player ids; cloned and hashed by state) is written only by the side
switch and cleared at combat end (`:1657`). An extra player turn takes the listed players as
participants and starting players (`:4873-4877`), skips the enemies' `PrepareForNextTurn` (no
`MonsterAi` draw), keeps the round, and phases one and two use the same players (`:2649`,
`:2860`). The list is visible to every hook during the extra turn; the game's
`RampartPower.AfterSideTurnStart` reads it (`Models.Powers/RampartPower.cs:54`).

### 12.7 Combat end (`TurnFlow.CombatEnd.cs`, A18)

`ProcessWinCondition` (§3.10) → victory → `EndCombatInternal` (`Combat/CombatManager.cs:1578-1838`):

1. `IsInProgress = false` (`:1653`); sim: `Outcome = Victory`.
2. `PlayersTakingExtraTurn.Clear()` (`:1657`); `SetPhaseForAllPlayers(None)` (`:1666`).
3. `ReviveBeforeCombatEnd` per player (`:1689`; `Entities.Players/Player.cs:1237`, state machine
   `:36-99`): a dead player is healed by 1 through `CreatureCmd.Heal` (`:67-69`).
4. `Hook.AfterCombatEnd(cs.Run, cs, room: null)` (`:1708`).
5. `History.Clear()` (`:1759`).
6. `Player.AfterCombatEnd` per player (`:1771`; `Entities.Players/Player.cs:1249-1254`):
   `RemoveAllPowersInternalExcept()` (every power, `RemoveInternal` only,
   `Entities.Creatures/Creature.cs:1225-1243`); `PlayerCombatState.AfterCombatEnd` (the five piles
   and the pet list cleared, `Entities.Players/PlayerCombatState.cs:279-297`);
   `LoseBlockInternal(Block)`.
7. `Hook.AfterCombatVictory(cs.Run, cs, null)` (`:1781`; the dispatcher runs the Early pass first,
   `Hooks/Hook.cs:9085`, `:9115`).

Stripped (W2 or UI): map-point `TurnsTaken` (`:1737-1739`), `SaveRun` (`:1748`),
`room.OnCombatEnded` (`:1760`), progress and achievements (`:1793-1798`), the `CombatEnded` event
(`:1810`). A standalone combat has no room. The loss path is unchanged (§3.10).

After a victory the player's combat piles are empty, block is 0, there are no powers, and the
history is empty; the deck keeps its cards; energy and stars keep their last values (the next
`ResetCombatState` resets them, as in the game). Powers are removed before `AfterCombatVictory`, so
a power hears only `AfterCombatEnd`.

### 12.8 Effect depth and `CheckForEmptyHand` (`TurnFlow.Effects.cs`)

- `BeginCardOrPotionEffect` / `EndCardOrPotionEffect` (`Combat/CombatManager.cs:6527-6531`,
  `:6534`, body `:1430-1509`). The game keeps a per-player depth in a dictionary; the sim keeps one
  `cs.CardOrPotionEffectDepth` (one player). End decrements; at depth 0 the entry goes and, if the
  player is dead, `RemoveDeadPlayerCardsFromCombat` (`:7110`) runs; it returns while the combat is
  not in progress or no player is alive, so it is a no-op in single player. The pair is public so
  that `PotionModel.OnUseWrapper` (relics-potions, A3) brackets a potion with it.
- `IsExecutingCardOrPotionEffect(cs, player)`: depth > 0 (`:6522-6525`).
- `CheckForEmptyHand` (`:7027-7056`, `:954`): in progress, no effect executing, hand empty →
  `Hook.AfterHandEmptied`.

A nested play (an auto-play inside a discard inside a play) runs at depth 2, and only the
outermost play's `CheckForEmptyHand` can fire.

### 12.9 Card play (`ActionExecutor.cs`, `CardCmd.Play.cs`)

Manual play (`GameActions/PlayCardAction.cs:115-176`): §4.1 validation; `SpendResources` (§4.2;
energy through `EnergyCmd.SpendEnergy`, pipeline; stars through `StarsCmd.SpendStars`,
character-mechanics, with `LastStarsSpent` set first, `Models/CardModel.cs:1399-1450`);
`ResourceInfo(spent, spent, stars, stars)` (A11, `:157-165`); then `OnPlayWrapper(isAutoPlay:
false)` inside an `Inline` context (the `PlayCardAction` row of the B1 table).

`CardCmd.OnPlayWrapper` (`Models/CardModel.cs:3450`, state machine `:257-1095`, order traced
through the `goto` labels):

1. `CurrentTarget = target`, `CurrentPlayIndex = 0` (`:837-838`).
2. Auto-play: `CardPileCmd.Add(Play, Bottom)` (`:841`); manual: `AddDuringManualCardPlay`
   (`:851`). If the card is then in no combat pile (the move was refused), its `CombatState` is
   null and the wrapper returns (`:932-939`).
3. `GetResultLocationForCardPlay` (`:935`), `Hook.ModifyCardPlayResultLocation` (`:936`), then
   `AfterModifyingCardPlayResultLocation` on each modifier with the final location (`:524`).
4. `GeneratePlayCount` (`:543`; §4.3 step 4). Owner dead → return (`:1006`); else
   `BeginCardOrPotionEffect` (`:1009`).
5. While `i < playCount` and not over or ending (`:618`): `CurrentPlayIndex = i` (`:620`); the
   `CardPlay` (`:758`; A11 `PlayId` and `Resources`); `Hook.BeforeCardPlayed` (`:769`);
   `History.CardPlayStarted` while the combat is live (`:663-665`); `OnPlay` (`:669`) → dead exit
   (`:714`); `Enchantment.OnPlay` (`:719-721`) → dead exit (`:706`); `Affliction.OnPlay(target)`
   (`:687-690`) → dead exit (`:654`); `History.CardPlayFinished` while live (`:733-735`);
   `Hook.AfterCardPlayed` while in progress (`:737-739`) → dead exit (`:681`). The labels give the
   order: `IL_06af` (after `OnPlay`) calls `Enchantment.OnPlay`, `IL_0745` continues at `IL_076c`
   (affliction), `IL_07ec` at `IL_081a` (history, `AfterCardPlayed`).
6. `EndCardOrPotionEffect` (`:786`), also when the loop throws: the game's catch-all stores the
   exception (`:782`) and rethrows it after the end (`:904`). The sim ends the effect and rethrows
   for every exception except `ChoicePendingException`, which unwinds the whole `Step` to the tape
   engine; that state is discarded and re-executed from S0, so its depth is never read.
7. Dead exit (flag set at `:779`, tested at `:908`) → return without the result move,
   `CheckForEmptyHand` or the cleanup.
8. If the card is still in the Play pile (`:942-943`): None → `RemoveFromCombat` (`:970`);
   Exhaust → `CardCmd.Exhaust(causedByEthereal: false)` (`:950`); else `Add(pile, position)`
   (`:960`). (`GiveToAnotherPlayer`, `:987`, is multiplayer.)
9. `CheckForEmptyHand(original owner)` (`:882`); cleanup (`:1017-1036`; cards-kernel
   `AfterCardPlayedCleanup`).

`CardCmd.AutoPlay` (`Commands/CardCmd.cs:1779`, state machine `:284-720`):

1. Return if over or ending or the owner is dead (`:380`).
2. In-combat Unplayable (`:383`) or `!Hook.ShouldPlay(card, type)` (`:395`) →
   `MoveToResultPileWithoutPlaying`.
3. `AnyEnemy` without a target: `Rng.CombatTargets.NextItem(HittableEnemies)` (`:407-411`); still
   none → move without playing. `AnyAlly` without a target: `NextItem` over the other living
   players (`:426-431`); none → move without playing.
4. `CapturedXValue = energy` for an X cost unless `skipXCapture` (`:447-449`); unless
   `skipXCapture`, `LastStarsSpent` = the stars for a star-X card, else
   `max(0, GetStarCostWithModifiers())` (`:451-459`).
5. A card in no pile → `Add(Play)` (`:462-464`); `Hook.BeforeCardAutoPlayed(card, target, type)`
   (`:551`).
6. `ResourceInfo(0, EnergyCost.GetAmountToSpend(), 0, max(0, GetStarCostWithModifiers()))`
   (`:516-521`, A11); `OnPlayWrapper(isAutoPlay: true)` (`:523`).

`MoveToResultPileWithoutPlaying` (`Commands/CardCmd.cs:1079-1150` →
`Models/CardModel.cs:122-256`, `:3505`): `Add(Play)`; then, if the card is in Play: a dupe →
`RemoveFromCombat`; `ExhaustOnNextPlay` or in-combat Exhaust → `CardCmd.Exhaust`; else
`Add(Discard)`.

`Discard(card)` → `Discard(cards)` → `DiscardAndDraw(cards, 0)` (`Commands/CardCmd.cs:1811`,
`:1825`, `:1839`; body `:721-964`): return if over or ending or the list is empty (`:794-797`);
the discard pile is the first card's owner's (`:801`); per card: Sly this turn (in-combat keyword
or single-turn Sly, read before the move, `:843`) → remembered, `Add(discard pile)` (`:847`),
`History.CardDiscarded` (`:829`), `Hook.AfterCardDiscarded` (`:830`); then `Draw(cardsToDraw)`
when > 0 (`:875`); then `AutoPlay(card, null, SlyDiscard)` per remembered card (`:917`).

`Exhaust(card, causedByEthereal)` (`:1867`; `:965-1078`): null if over or ending (`:1023`);
`Add(Exhaust)` (`:1026`); `History.CardExhausted` (`:1041`); `Hook.AfterCardExhausted` (`:1042`).

The play half is `public static partial class CardCmd` in namespace
`Sts2Sim.Core.Combat.Pipeline`, so it merges with cards-kernel's `Combat/Pipeline/CardCmd.cs`
(B4 row: "partial class CardCmd across the two owners' files"). `CardPileCmd.Discard` and
`CardPileCmd.Exhaust` forward to it.

### 12.10 Piles (`CardPileCmd.cs`)

`Add`, every overload (`Commands/CardPileCmd.cs:3251-3283`; body `:3283-3476`):

1. No cards → empty. A combat pile while ending → every card fails (`:3291-3298`).
2. Per card: removed from state or owner dead → failed, without the owner check
   (`:3312-3323`); otherwise success, and all successful cards share one owner (`:3352-3361`).
3. Deck target: `Hook.ShouldAddToDeck` over every result (`:3368-3384`); a prevented card →
   `AfterAddToDeckPrevented` on the preventer, failed. The map-point `CardsGained` and
   `FloorAddedToDeck` writes (`:3376-3377`) are W2.
4. A combat pile while not in progress → return (`:3386-3389`); no success → return.
5. Per successful card: full-hand redirect to the discard pile (`:3405-3409`); remove it from its
   old pile, or, for a deck add of a card with no pile, `Hook.ModifyCardBeingAddedToDeck` (its
   result replaces the card and records the modifiers, `:3411-3425`); insert by position
   (`:3427-3433`; Random → `Rng.Shuffle.NextInt(count + 1)`); `AfterCardEnteredCombat` for a card
   with no old pile entering a combat pile, unless `isChangingOwners` (`:3434-3437`).
6. After every move: `Hook.AfterCardChangedPiles(rs, combat or null, card, old pile, clonedBy)` per
   successful card that had no pile or whose pile type changed (`:3455-3470`).

Not ported: the `RunState.ContainsCard` / `CombatState.ContainsCard` / preview throws
(`:3328-3342`; the sim does not register cards with a state), the HAND_FULL bubble, the tweens.

Owner: the game throws for a card without an owner (`:3307-3310`). Every sim creation path sets the
owner (A23: the deck build, `CloneForCombat`, `cs.CreateCard`). P0 test fixtures and cards-kernel's
`Slimed` still build ownerless cards with `CardInstance.Create`; `OwnerOf` treats such a card as the
combat's player instead of throwing. No state built through the creation paths reaches that branch.

`AddDuringManualCardPlay` (`:3837`; `:877-1011`): return if over or ending; remove the card from its
pile, append it to Play, `AfterCardChangedPiles(old pile)` (`:980`).

`AddGeneratedCardToCombat` / `AddGeneratedCardsToCombat` (`:3196-3249`): empty, or not in progress →
empty (`:3208-3216`); a card that has a pile, or a non-combat target, throws (`:3217-3224`); per
card: `History.CardGenerated` (`:3237`), `Add(owner's pile)`, `AfterCardGeneratedForCombat`
(`:3240`). The single-card overload indexes the result (`:3200`) and throws on the empty list; the
sim returns a failed result and records `FidelityFlag.ApproximatedRule` site
`CardPileCmd.AddGeneratedCardToCombat:empty-result-outside-running-combat`. P0's window tests and
window-B monster setup reach it; the game would throw there.

`RemoveFromCombat` (`:3151`, `:3165`; `:2205-2466`): a card in no combat pile throws (`:2260-2263`);
remove every card; then per card `AfterCardChangedPiles(old pile)` (`:2421`) and `RemoveFromState`
(`:2411`; `Models/CardModel.cs:3244-3248`: off its pile, `HasBeenRemovedFromState = true`).

`RemoveFromDeck` (`:3123`, `:3137`; state machine `:2530-2663`): a card not in the deck throws ("You cannot remove
a card that is not in the deck.", `:2622`); `Hook.BeforeCardRemoved` (`:2625`); `RemoveFromState`
(`:2613`).

`AddToCombatAndPreview` (`:4117`, `:4134`): per target (a player, or a pet's owner)
`cs.CreateCard(def, player)` then `AddGeneratedCardToCombat`; a dead player gets nothing; the
preview is stripped. The generic `<T>` becomes a card id or a `CardDef` (B5: cross-kind references
by id).

`AutoPlayFromDrawPile` (`:4074`; `:1522-1700`): return if over or ending; per count:
`ShuffleIfNecessary`, take the bottom, the top, or `Rng.CombatCardSelection.NextItem` (`:1585-1587`),
stop when there is none, `Add(Play)`; then per taken card whose owner lives:
`ExhaustOnNextPlay = forceExhaust`, `AutoPlay(card, null)` (`:1643-1647`).

`AddCurseToDeck` / `AddCursesToDeck` (`:4151`, `:4159-4183`): a non-curse throws
`ArgumentException`; `RunState.CreateCard` (sim: run allocator id, `AttachTo`, `AfterCreated`),
then `Add(Deck)`; the preview is stripped.

In-combat keywords (`CardModel.Keywords`, `Models/CardModel.cs:2775-2814`, `:1940`): a card in a
combat pile has its own keywords plus `Hook.ModifyKeywordsInCombat`; any other card has its own.
Read for Unplayable and Exhaust in auto-play, Sly on discard, Ethereal and Retain at turn end, and
Innate at turn 1. The turn-1 enchantment bottom moves (`Combat/CombatManager.cs:4248-4262`) run
before the Innate moves, and a card moved to the bottom is not moved to the top again.

`Draw` records `History.CardDrawn` after the move and before `AfterCardDrawn` (`:4050`).

### 12.11 `PlayerCmd` (`Commands/PlayerCmd.cs:802-999`)

| Member | Game | Sim |
| --- | --- | --- |
| `GainEnergy` / `LoseEnergy` / `SetEnergy` | `:802-843` | `EnergyCmd` (pipeline) |
| `GainStars` / `LoseStars` / `SetStars` | `:844-881` | `StarsCmd` (character-mechanics, A16) |
| `GainGold(amount, player, wasStolenBack)` | `:882`; `:237-357`: `ModifyGoldGained` with modifiers, `AfterModifyingGoldGained`, then if amount > 0: `Gold += (int)amount`, `AfterGoldGained` | the same through `cs.Run`; map history and SFX stripped |
| `LoseGold(amount, player, type)` | `:896-918`: map history by type, `Gold = max(0, Gold - (int)amount)` | the same without the map history (W2) |
| `SetGold` | `:921`; `:596-694`: gain or lose the difference | the same |
| `GainMaxPotionCount` / `LoseMaxPotionCount` | `:934-945` | `PlayerState.AddToMaxPotionCount` / `SubtractFromMaxPotionCount` |
| `AddPet(cs, owner, def)` | `:947-967` (A15): `CreateCreature` on the owner's side, slot null; `AddPetInternal`; `CreatureCmd.Add` | `MonsterSpawner.SpawnPet` (monsters-kernel), `AddPetInternal`, `CreatureCmd.Add` (pipeline) |
| `AddPet(cs, pet, player)` | `:957`; `Entities.Players/PlayerCombatState.cs:375-387`: a listed pet is skipped; `PetOwner` is set, and a different existing owner throws (`Entities.Creatures/Creature.cs:526-534`); `Died → OnPetDied` (`:433-445`) | the same; `OnPetDied` (drop the pet from the list when `ShouldCreatureBeRemovedFromCombatAfterDeath`) belongs to the death path (pipeline `Kill`) |
| `MimicRestSiteHeal` | `:971` (rest site) | throws `NotImplementedException` (W2) |
| `EndTurn(player, canBackOut)` | `:984-994` → `SetReadyToEndTurn` (`Combat/CombatManager.cs:6811-6857`); callers `Models.Cards/VoidForm.cs:91`, `Commands/CreatureCmd.cs:2427` | throws `NotImplementedException`: the end-turn request needs a ready flag on `CombatState` (§12.18) |
| `CompleteQuest` | `:996-999`: map-point history only | argument checks only (W2) |

### 12.12 `Step` routing

`CombatLoop.Step` = `LegalActionGenerator.TryResolve`, then `ExecuteResolvedAction(cs, action,
target)`:

- `PlayCard` → `ActionExecutor.PlayCard`; `UsePotion` → `PotionActions.UsePotion(cs, slot, target)`;
  `DiscardPotion` → `PotionActions.DiscardPotion(cs, slot)` (relics-potions); each is followed by a
  win check while in progress (`GameActions/ActionExecutor.cs:130-139`: every action except
  `EndPlayerTurnAction` and `ReadyToBeginEnemyTurnAction`).
- `EndTurn` → phase one, phase two, side switch, turn loop.
- `ChoosePick` / `ChooseConfirm` / `ChooseCancel` throw: the choice engine applies them (B1).

Potion target resolution belongs to the choice owner's `TryResolve` (A4). At base `263810d` it
still rejects the potion kinds, so `ExecuteResolvedAction` is public for tests and for the facade.

Win checks run only at the game's fixed points: after each action above, after each enemy's
turn (`:3088-3276`), at the turn-end phase boundaries (§12.5), at turn start (§12.3), at the
enemy turn end.

### 12.13 History recording sites

| Entry | Game site | Sim site |
| --- | --- | --- |
| CardPlayStarted, CardPlayFinished | `Models/CardModel.cs:663-665`, `:733-735` (while the effect's combat is live, `Combat/CombatManager.cs:6503-6520`) | the play loop, while in progress |
| CardDrawn | `Commands/CardPileCmd.cs:4050` | `DrawInternal` |
| CardGenerated | `:3237` | `AddGeneratedCardsToCombat` |
| CardDiscarded | `Commands/CardCmd.cs:829` | `DiscardAndDraw` |
| CardExhausted | `Commands/CardCmd.cs:1041` | `CardCmd.Exhaust` |
| clear | `Combat/CombatManager.cs:1759` | `EndCombatInternal` |
| EnergySpent | `Models/CardModel.cs:1255`, inside `SpendEnergy` | not a loop site: the sim's port of `SpendEnergy` is the pipeline's `EnergyCmd.SpendEnergy`, which owns the append (verify-notes-pipeline, round 2); the loop calls it once per manual play, so integration yields one entry |

The hand flush at turn end is a batched pile move, not a discard, and records no `CardDiscarded`
(`FlushPlayerHand` calls `CardPileCmd.Add`, `:3330-3432`).

### 12.14 Choice contexts the loop pushes

| Site | Kind | Game |
| --- | --- | --- |
| `ActionExecutor.PlayCard`, around `OnPlayWrapper` | Inline | the `PlayCardAction` context |
| `SetupPlayerTurn`, `RunAutoPrePlayPhase` | TurnSetup, with the §12.3 callbacks | `HookPlayerChoiceContext` (`:5059`, `:5204`) |
| `OrbQueue.AfterTurnStart`, per player | Inline | a context of its own (`:5118-5120`) |
| `AfterAutoPostPlayPhaseEntered`, `DoTurnEnd`, `FlushPlayerHand` | Inline | the end-of-turn contexts (B1 table) |

Every scope is a `using`, so the frame is popped on every exit, including the §12.3 unwind.

### 12.15 Still deferred or approximated

- `PlayerCmd.EndTurn`, `MimicRestSiteHeal` and the `CompleteQuest` map history (§12.11).
- `OnPetDied` (§12.11), owned by the death path.
- The `Add` registration throws and the ownerless-card tolerance (§12.10).
- The flag sites of §12.3 and §12.10.
- Multiplayer: the per-player effect depth, `GiveToAnotherPlayer`, ready locks,
  `actionDuringEnemyTurn` (§7).
- `room` is null in a standalone combat; `AfterRoomEntered` and the room callbacks are W2.

### 12.16 Refreshed P0 pins

| Test | Old | New | Reason |
| --- | --- | --- | --- |
| `LoopTests.SetUpWindowGatesCommandsLikeTheGame` | `NextCardInstanceId == 12` | 22 | A13: the 10 deck cards take ids 0..9, their copies 10..19, the two refused generated cards 20 and 21 |
| `LoopTests.OpeningDrawOrderFollowsShuffleStream` | expected ids `Range(0, 10)` | `Range(10, 10)` | the draw pile holds the copies 10..19 in deck order before the shuffle |
| `LoopTests.ScriptedStrikeFightTurnByTurn` | after the kill: hand 4, play 1, draw 5 | hand, play, discard and draw 0; deck 10 | A18: `Player.AfterCombatEnd` clears the combat piles. The discard still refuses the killing card (the combat is ending), then the play pile is cleared |
| `EndToEndTests` reference model and `PinnedSeedOneVictory` | terminal piles `DS` / `BSDDD` / `SS` / `S` | all empty; the model also zeroes block at victory | A18 |
| `EndToEndTests.PinnedSeedOneOpeningHash` / `PinnedSeedOneFinalHash` | `0xCA1423508C1168E3` / `0xD7B055DC9B9E326E` | `0x79C886C6235C2D9B` / `0x77FA4D8665BA0CC3` | the canonical form now carries the deck (A13), the history entries of §12.13 (B6) and the cleared terminal player (A18); the opening value was reproduced in a second process |

The terminal-pile edits go beyond ids and hashes: those P0 asserts pinned the P0 deviation that A18
removes. `CardUpgradeTests.Test_Cards_UpgradedDeckReplaysDeterministicallyThroughReshuffles`
(cards-kernel's file) asserts after every step, including the terminal one, that the combat piles
hold every card; it now fails at its first victory (seed 3, step 12: 11 expected, 0 found). Its
owner should skip that assert once `sim.IsTerminal`, or count `Player.Deck` there.

### 12.17 Tests (`Sts2Sim.Tests/TurnFlowTests.cs`, 22 tests)

Test-local fixtures: `Probe : PowerModel` (instanced; logs every hook it overrides and can veto
`ShouldPlay`, redirect the result location, request an extra turn and block hitting), `ProbeCard :
CardDef`, `Dummy : MonsterDef` (wait or bite), `CombatEndRelic`, `ProbePotion`, `ProbeOrb`,
`LoggingAnswerer`. Combats use encounter id `TURN_FLOW_TEST_ENCOUNTER` with the populate seam. A part
that crosses another owner's stub runs inside `W1Pending.Run` with that owner.

| Test | Asserts | Pending on |
| --- | --- | --- |
| `DeckCardsPrecedeTheirCombatCopies` | deck ids 0..9 in deck order, owner attached, no deck link; one copy per deck card, `DeckVersionId` a bijection, copy id = n + `DeckVersionId`, own object, same id and upgrade, owner kept; `NextCardInstanceId` 20 on the combat and the run; an in-combat upgrade leaves the deck card unchanged; a clone copies the deck with its owner; an upgraded spec gives an upgraded deck card and copy | |
| `SideTurnHooksReceiveTheGamesParticipantSnapshots` | the eight side-turn hooks in order; the player turn end passes the player; the enemy start snapshot predates a spawn in `BeforeSideTurnStart` and is shared by both start hooks; the enemy end snapshot holds both enemies, is shared, and keeps an enemy killed in `BeforeSideTurnEnd`; never the live lists | pet part: state (`AddAlly`) |
| `EffectDepthGatesTheEmptyHandCheck` | a play that discards the rest of the hand, including a Sly card: depth 1, the auto-play at depth 2, one `AfterHandEmptied` at depth 0 after the effect | |
| `TurnSetupFirstPauseRunsTheDeferredStartSteps` | a pause in `BeforeHandDraw`: TurnSetup frame; `AfterSideTurnStart` runs at the pause, once; then the rest of the setup and the three pre-play passes; frames popped; no flag | |
| `FirstPauseInTheAutoPrePlayPhaseRunsOnlyTheWinCheck` | a pause in `AfterAutoPrePlayPhaseEntered` repeats no side-turn step; with the enemy killed before the pause: Victory, the late pass and the move to Play abandoned, the flag recorded | |
| `ACombatEndAtTheDeferredWinCheckUnwindsTheSetup` | the enemy killed inside the deferred `AfterSideTurnStart`: Victory at the deferred win check; the hook sequence; the setup abandoned (no `AfterPlayerTurnStart`, empty hand); the flag; frames popped; no legal actions | |
| `TurnSetupPromptPausesThroughTheChoiceRuntime` | a real prompt in `AfterPlayerTurnStart` through `CardSelectCmd.FromSimpleGrid`: `AfterSideTurnStart` between the prompt and the answer, once | choice (`FromSimpleGrid`) |
| `ExtraTurnKeepsThePlayerSideAndSkipsTheEnemyTurn` | Player side, round 1, turn 2, the list holds the player; no enemy move and no `MonsterAi` draw; `AfterTakingExtraTurn` between the turn end and the next start; clone and hash carry the list; the next turn end clears it and the enemy acts | |
| `CombatEndDispatchesInTheGamesOrderAndClearsThePlayer` | `AfterCombatEnd` sees the ended combat with piles, powers, block and history intact; the power hears no victory hook; afterwards every pile empty, block 0, no powers, history 0, deck 10 | relic part: hooks (relic listeners) |
| `ReviveBeforeCombatEndHealsADeadPlayer` | a 0 HP player at the win check ends the victory with 1 HP | |
| `SlyDiscardAutoPlaysTheCard` | the target comes from `Rng.CombatTargets`; hook order discard → `ShouldPlay(SlyDiscard)` → `BeforeCardAutoPlayed` → play; `ResourceInfo(0, 1, 0, 0)`; no energy spent; the card back in the discard; the discard entry before the auto-play's | |
| `AutoPlayMovesUnplayableAndBlockedCardsWithoutPlaying` | Unplayable → discard; Unplayable + Exhaust → exhaust; `ShouldPlay` false → discard; no hittable enemy → discard with no RNG draw; no `BeforeCardAutoPlayed` | |
| `AutoPhaseHooksSeeTheirPhases` | turn end: post-play hook in AutoPostPlay, `BeforeSideTurnEnd` in End, `BeforeFlush`, `AfterSideTurnEnd`; with an empty deck, `AfterHandEmptied` before the three pre-play passes | |
| `ResultLocationHookRedirectsAPlay` | a redirected Strike is exhausted; the modifier hears the final location | |
| `HistoryRecordsTheLoopSites` | five hand draws in order; play start and finish share one `PlayId` with `ResourceInfo(1, 1, 0, 0)`; a generated card with its creator; the ethereal exhaust; no discard entry from the flush; the victory clears the log | |
| `AfterCardChangedPilesFollowsEveryMove` | Hand → Play → Discard; Draw → Hand; a batch reports each card after the whole batch; a shuffle reports only discard-origin cards | |
| `GeneratedCardsAreRecordedOwnedAndRefusedOutsideTheCombat` | results, history and hook order; throws for a card with a pile and for a non-combat pile; refusal outside the running combat with the flag | |
| `AutoPlayFromDrawPilePlaysTheTopCards` | forced exhaust; both Strikes hit; one target draw each | |
| `PotionStepsRouteThroughPotionActions` | `UsePotion` / `DiscardPotion` empty the slot; `PotionUsed` history | relics-potions (`PotionActions`) |
| `AddPetRegistersThePetOnItsOwner` | a pet with another owner throws before it is registered; the pet joins the owner's side and list | second part: monsters-kernel (`SpawnPet`) |
| `DeckAddAndRemoveWriteThePlayersDeck` | removing a combat copy from the deck throws; a curse add of a non-curse throws; the curse is appended to the deck with its owner and no combat pile; the removed card is marked | second part: hooks (`ShouldAddToDeck`) |
| `OrbQueueTriggersRunAtTheGamesPoints` | `BeforeTurnEnd` before `BeforeFlush`; `AfterTurnStart` after `AfterSideTurnStart` and before the pre-play phase | character-mechanics (`OrbQueue.AddCapacity`, `TryEnqueue`) |

The relic part of `CombatEndDispatchesInTheGamesOrderAndClearsThePlayer` throws
`NotImplementedException` itself while `Hook.IterateHookListeners` does not visit `Player.Relics`,
so it reports PENDING (hooks) instead of a false failure.

### 12.18 Dependencies and contract notes

- cards-kernel: `CloneForCombat` (A23); the keyword mask and single-turn Sly / Retain flags;
  `HasTurnEndInHandEffect` / `OnTurnEndInHand`; `GetResultLocationForCardPlay(cs)`;
  `ExhaustOnNextPlay`, `IsDupe`, `EnergyCost.CostsX` / `CapturedXValue` / `GetAmountToSpend`,
  `Def.HasStarCostX`, `GetStarCostWithModifiers`, `LastStarsSpent`, `AfterCardPlayedCleanup`;
  `Enchantment.OnPlay(cs, play)` / `ShouldStartAtBottomOfDrawPile`, `Affliction.OnPlay(cs, target)`;
  `CardDef.AfterCreated`. Requests: declare `Combat/Pipeline/CardCmd.cs` as `public static partial
  class CardCmd` in namespace `Sts2Sim.Core.Combat.Pipeline`; create `Slimed`'s cards with
  `cs.CreateCard(def, owner)` (A23); fix the terminal assert of §12.16.
- state: `RunState.CreateStandalone`, the run allocator, `CombatState(run, player, encounterId)`,
  `cs.CreateCard`, `cs.AddAlly` (still a stub), `PlayersTakingExtraTurn`,
  `CardOrPotionEffectDepth`, `History.Append*` / `Clear`, `PlayerState.Deck` / `PetIds` /
  `OrbQueue` / `BaseOrbSlotCount`, `CardPile.ClearInternal` / `MoveToBottomInternal`. Request: a
  cloned and hashed end-turn request on `CombatState` for `PlayerCmd.EndTurn`
  (`SetReadyToEndTurn`, `Combat/CombatManager.cs:6811-6857`), honoured by the loop after the
  current action.
- hooks: the dispatchers named above. Request: visit `Player.Relics` (and potions) in the combat
  listener snapshot (B3) so that relic `AfterCombatEnd` / `AfterCombatVictory` overrides run.
- choice: `PushContext(kind, onFirstPause)` must call `onFirstPause` exactly once per frame, at
  that frame's first pause, before the prompt is raised or answered, also on the tape re-execution;
  a frame must be popped by `Dispose` on every exit; `ChoicePendingException` must not be swallowed
  by the runtime. Request: `TryResolve` resolves `UsePotion` / `DiscardPotion` (A4).
- monsters-kernel: `PlayerSideParticipants` / `EnemySideParticipants` (A17), `SpawnPet` (A15),
  `SetUpForCombat` / `AfterCreatureAdded` / `PrepareForNextTurn` / `TakeTurn` / `OnSideSwitch`.
- pipeline: `CreatureCmd.Heal` on a dead player at combat end (the combat is no longer in
  progress); `CreatureCmd.Add(cs, creature)`; `CreatureInternals.RemoveAllPowersInternal` /
  `LoseBlockInternal`; `EnergyCmd.SpendEnergy` with the `EnergySpent` append. Request: run
  `OnPetDied` in the death path.
- character-mechanics: `StarsCmd`, `OrbQueue.BeforeTurnEnd` / `AfterTurnStart` / `AddCapacity`.
- relics-potions: `PotionActions.UsePotion` / `DiscardPotion`; `PotionModel.OnUseWrapper` brackets
  with the public effect pair of §12.8.

### 12.19 Performance

The machine was shared with other agents' builds and tests (load average 10 to 31 on 12 cores), so
wall-clock numbers swing by 2x between consecutive runs. The P0 tree (`819d7c6`), the base tree
(`263810d`) and this branch were each exported with `git archive`, built in Release and benched
interleaved in one session. A process-CPU harness (non-concurrent workstation GC, the bench's seeds
and policy, 4,000 episodes) gives load-robust ratios. All three trees play exactly the same 80,252
decisions, so the W1 port changes no outcome for P0 content.

| Metric | P0 tree | base tree | this branch | branch / base | branch / P0 |
| --- | --- | --- | --- | --- | --- |
| bench playouts, median of 8 interleaved runs | 189,793 decisions/s | 194,602 | 150,422 | 0.77 | 0.79 |
| bench clone, best of 8 | 4.62 us | 4.11 us | 6.68 us | 1.63 | 1.45 |
| bench `StateHash`, best of 8 | 2.24 us | 1.89 us | 4.63 us | 2.45 | 2.07 |
| CPU per decision (harness) | 6.42 us | 6.60 us | 8.38 us | 1.27 | 1.31 |
| CPU per clone (harness) | 2.00 us | 2.62 us | 3.72 us | 1.42 | 1.86 |
| CPU per `StateHash` (harness) | 2.71 us | 3.70 us | 4.53 us | 1.22 | 1.67 |
| allocated bytes per decision | 937 | 1,011 | 2,237 | 2.21 | 2.39 |
| allocated bytes per clone | 5,384 | 6,648 | 10,896 | 1.64 | 2.02 |
| canonical bytes, bench mid-fight state | 2,042 | 2,373 | 5,102 | 2.15 | 2.50 |

Against the documented P0 figures (705,048 decisions/s and 2.53 us per clone, measured at load 2 to
4), the same-session ratios project to about 540k decisions/s and 3.7 to 4.7 us per clone on a
quiet machine. The W1 gates (at least 300 decisions/s per core, at most 1 ms per clone) hold by more
than 400x.

Where the cost comes from, all of it required by the contract:

- The mid-fight state's canonical form grows by 2,729 bytes: 1,355 for the 10 deck cards (A13/D3;
  the run deck is hashed and cloned) and 1,374 for its 20 history entries (B6 sites, D11). The clone
  copies those cards and entries (+4,248 bytes).
- Per decision: the history appends; `AfterCardChangedPiles` after every pile move; the
  `ModifyCardPlayResultLocation`, auto-phase and keyword hook dispatches (one
  `ModifyKeywordsInCombat` per hand card at the Ethereal and Retain checks, as
  `CardModel.Keywords` does in the game); the A17 participant snapshots; the choice frames; and,
  per episode, 10 more cards (the deck and its copies) plus the run shell.

## 13. Round-2 findings (`verify-notes-loop.md`, scaffold tree `ef7c7b9`)

| # | Finding | Response |
| --- | --- | --- |
| F1 | [high] Loop W1 track absent: no B6 code, tests or spec | Done in this round: every file of the loop row, `TurnFlowTests.cs` and this section, committed on `worktree-wf_8d9c3264-732-9` / `w1-r3/loop`. |
| F2 | [high] Victory skips `EndCombatInternal`; the player keeps block, powers and piles | Ported in game order (§12.7); `CombatEndDispatchesInTheGamesOrderAndClearsThePlayer`, `ReviveBeforeCombatEndHealsADeadPlayer`; the P0 pins that encoded the leftovers are refreshed (§12.16). |
| F3 | [medium] `SetUpCombat` ignores `Player.Deck`; no copies; no owner | §12.2: the deck is built with owners from run ids, the draw pile from `CloneForCombat`; `PileType.Deck` goes through `Player.Deck` (§12.10); `DeckCardsPrecedeTheirCombatCopies`. |
| F4 | [medium] Auto pre-play and post-play phase hooks never dispatched | §12.4; `AutoPhaseHooksSeeTheirPhases`, `TurnSetupFirstPauseRunsTheDeferredStartSteps`. |
| F5 | [medium] Side-turn participants are live lists | A17 snapshots at the game's points (§12.3, §12.5); `SideTurnHooksReceiveTheGamesParticipantSnapshots` covers a spawn and a death. |
| F6 | [medium] Orb queue turn triggers not called | `BeforeTurnEnd` first in `DoTurnEnd`, `AfterTurnStart` after `AfterSideTurnStart` (in the deferred list of §12.3); `OrbQueueTriggersRunAtTheGamesPoints` (pending on character-mechanics' `AddCapacity` / `TryEnqueue`). |
| F7 | [medium] Effect depth untracked; `CheckForEmptyHand` ungated | §12.8; the end runs on normal exit and on exceptions; `ChoicePendingException` is the one exception it lets through untouched, because the tape engine discards that state (§12.9 step 6); `EffectDepthGatesTheEmptyHandCheck`. |
| F8 | [medium] `OnPlayWrapper` and `AutoPlay` incomplete | §12.9, including the result-location hook, enchantment and affliction `OnPlay` (enchantment first), history and the `CombatTargets` draw; `SlyDiscardAutoPlaysTheCard`, `AutoPlayMovesUnplayableAndBlockedCardsWithoutPlaying`, `ResultLocationHookRedirectsAPlay`. |
| F9 | [medium] Sly discard throws; `Discard` / `DiscardAndDraw` / `Exhaust` play half missing | §12.9; the `NotSupportedException` is gone. |
| F10 | [medium] `PlayersTakingExtraTurn` never maintained | §12.6; `ExtraTurnKeepsThePlayerSideAndSkipsTheEnemyTurn` (including clone and hash). |
| F11 | [medium] `UsePotion` / `DiscardPotion` not routed through `PotionActions` | §12.12; the resolution of the potion kinds in `TryResolve` is the choice owner's (A4, §12.18); `PotionStepsRouteThroughPotionActions` (pending on relics-potions). |
| F12 | [medium] Loop-owned facade members missing or partial | `CardPileCmd`: `RemoveFromDeck` (combat and run overloads), `AddGeneratedCardsToCombat`, `AutoPlayFromDrawPile`, `AddToCombatAndPreview` (by id or `CardDef`), `AddCurseToDeck`, `AddCursesToDeck`, the deck path of `Add`, owner and removed-card checks, `AfterCardChangedPiles`, generated cards through `cs.CreateCard` (§12.10). `PlayerCmd`: all 15 game overloads (§12.11; two still throw, with reasons). |
| F13 | [low] Known deviations record no flag; no W1 section | The remaining knowing deviations are flagged (§12.3, §12.10); this section. |
| F14 | [high] W1 deliverables absent (same as F1) | As F1. |
| F15 | [high] Deck copy semantics missing (same as F3) | As F3. The combat piles now hold exactly the copies plus generated cards, and the deck is separate (`DeckCardsPrecedeTheirCombatCopies`). |
| F16 | [high] Combat end not ported (same as F2) | As F2. The 49 content overrides of `AfterCombatEnd` / `AfterCombatVictory` are now dispatched; the relics among them need the hooks change of §12.18. |
| F17 | [high] TurnSetup and end-of-turn contexts never pushed; no first-pause skip logic | §12.3, §12.14; `TurnSetupFirstPauseRunsTheDeferredStartSteps`, `FirstPauseInTheAutoPrePlayPhaseRunsOnlyTheWinCheck`, `ACombatEndAtTheDeferredWinCheckUnwindsTheSetup`, `TurnSetupPromptPausesThroughTheChoiceRuntime`. `PushContext` is implemented at base `263810d`. |
| F18 | [medium] Live lists at the side-turn hooks; the turn start iterates only `cs.Player` | As F5. Pets are snapshot creatures, so their `BeforeTurnStart`, `AfterTurnStart` and `AfterBlockCleared` run and their block clears. |
| F19 | [medium] Orb triggers and phase hooks not dispatched (same as F4, F6) | As F4 and F6. |
| F20 | [medium] Effect depth untracked; no result-location hook; no public effect API for potions | As F7 and F8; `BeginCardOrPotionEffect`, `EndCardOrPotionEffect`, `IsExecutingCardOrPotionEffect` and `CheckForEmptyHand` are public on `CombatLoop` for `PotionModel.OnUseWrapper`. |
| F21 | [medium] No history recording site; no `History.Clear` | §12.13; `HistoryRecordsTheLoopSites`. `EnergySpent` is the pipeline's append inside `EnergyCmd.SpendEnergy`. |
| F22 | [medium] `PlayersTakingExtraTurn` hashed but never written (same as F10) | As F10; `RampartPower`'s read is served (§12.6). |
| F23 | [medium] Potion steps not routed (same as F11) | As F11. |
| F24 | [medium] Facade members that other owners call do not exist; no `CardCmd` type | As F12; the play half of `CardCmd` is the partial class of §12.9. |
| F25 | [medium] Knowingly approximated sites record no flag (same as F13) | As F13. The deviations it lists are ported, not flagged: terminal state, participants, phase hooks, orb triggers, effect depth, `HasBeenRemovedFromState`, Sly. |
| F26 | [low] Sly discard throws (same as F9) | As F9. |
| F27 | [low] Card-id allocation unspecified; conflicts with a pinned P0 test | A13 decides it (run allocator, deck first); §12.2 and §12.16. |
| F28 | [low] The B6 port depends on many throwing stubs on the hot path; merge order matters; `CloneForCombat` cannot be test-doubled | Partly rebutted at base `263810d`: `CloneForCombat`, `PushContext`, `cs.CreateCard`, the history appends and the per-turn dispatchers have bodies, so the P0 path reaches no stub (suite green apart from `CardUpgradeTests` of §12.16 and the two content gates; diff gate 4/4 bundles, 69/69 decisions). The stubs still reached sit off the P0 path (`OrbQueue.AddCapacity` for orb characters, `PotionActions`, `SpawnPet`, `ShouldAddToDeck`, `AddAlly`, relic listeners); the tests that reach them are wrapped in `W1Pending.Run` with the owner. |
| F29 | [low] `loop.md` stale; no W1 append | §12.1 lists every superseded P0 statement, including the enchantment / affliction order of §4.3. |
