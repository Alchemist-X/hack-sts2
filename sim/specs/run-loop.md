# R6 run loop — spec index

Owner R6. Files: `sim/Sts2Sim.Core/Run/Loop/*` (`RunSim`, `RunLoop`, `RunLegalActionGenerator`, `RunActionExecutor`,
`RunLoopState`, `RunSoak`), `Sts2Sim.Bench` option `run-soak [runs=100000] [firstSeed=1]`, tests `RunLoopTests.cs`.
Format: `member — decompile file:line — dropped points / gaps`.

## New run (§3.1)

- `RunLoop.SelectActs` — `Multiplayer.Game.Lobby/StartRunLobby.cs:705-707`, `:737-748` — `Rng(XXH64(seed), "act_selection")` → `ActModel.GetRandomList` (R1); `Act1` "overgrowth"/"underdocks" overrides index 0; random-character draw on the same rng not ported (character is an input); `RunConfig.ActIds` bypasses selection.
- `RunLoop.Ironclad` (CharacterStart) — `Models.Characters/Ironclad.cs:30`, `:32`, `:54`; `Models/CharacterModel.cs:67`, `:71`, `:85` — HP 80, gold 99, energy 3, orb slots 0, relic BURNING_BLOOD, no starting potions; only Ironclad has ported starting data (others throw `NotSupportedException`).
- `RunLoop.CreateForNewRun` — `Entities.Players/Player.cs:712-717`, `:1098-1161`; `Runs/RunState.cs:550-582`; `Entities.Players/Player.cs:729-733` — 3 potion slots, starting deck + `AfterCreated`, starting relics added without `AfterObtained`; `RunOddsSet(UnknownMapPoint)`, shared bag refresh-allowed, player bag not; `PlayerRngSet(XXH64(seed)+slot)`; `FloorAddedToDeck` not modelled (no field on `CardInstance`/`RelicModel`). A4/A5 run effects are applied at creation (`RunState.CreateStandalone` TightBelt, `StartingDecks.ApplyAscensionEffectsToDeck` AscendersBane) instead of after the bag fills (`Runs/RunManager.cs:3858-3868`): no rng is touched by either, so the draw order is unchanged.
- `RunLoop.InitializeNewRun` — `Runs/RunManager.cs:3826-3869`, `:3924-3927` — shared bag `Populate(SHARED_RELIC_POOL ids ∩ ContentFilter, UpFront)`; `SharedRelicPool.GetUnlockedRelics(unlock)` gating is not applied (exact for `UnlockProfile.All`); player bag `PopulateRelicGrabBagIfNecessary(UpFront)`; `StartedWithNeow = NEOW_EPOCH revealed`; modifiers `OnRunCreated` dropped (no Standard modifier).
- `RunLoop.GenerateRooms` — `Runs/RunManager.cs:4055-4088`; `Unlocks/UnlockState.cs:56-67`; `Models/ModelDb.cs:162` — shared ancients = [DARV if DARV_EPOCH], `UnstableShuffle(UpFront)`, per act ≥1 `NextInt(n+1)` subset; `GenerateRooms` + `ApplyDiscoveryOrderModifications` (`ShouldApplyTutorialModifications` true in Standard, `:4090-4110`); A10 second boss `NextItem(AllBossEncounters − boss)`. Gap: `EncounterDef` has no `RoomType`, boss encounters are selected by the `_BOSS` id suffix (true for all 12 v0.111.0 bosses).
- `RunLoop.Begin` / `FinalizeStartingRelics` — `Nodes/NGame.cs:2091-2134`; `Runs/RunManager.cs:1942-2013` — `AfterObtained` per starting relic in order, then `EnterAct(0)`.

## Act entry (§3.2)

- `RunLoop.EnterAct` — `Runs/RunManager.cs:142-398` — `ExitCurrentRooms`, `SetActInternal`, Neow point via `EnterMapCoord` when `StartedWithNeow` at act 0 else `EnterRoom(MapRoom)`; `Hook.AfterActEntered` after the room entry; fades, screens, `ActEntered` event dropped.
- `RunLoop.SetActInternal` — `Runs/RunManager.cs:2697-2770`; `Runs/RunState.cs:353-365`, `:996-1000` — `CurrentActIndex` (NextRoomId reset when it changes), visited coords cleared, `ActFloor = 0`, `UnknownMapPoint.ResetToBase`, `GenerateMap`.
- `RunLoop.GenerateMap` — `Runs/RunManager.cs:2064-2160`; `Runs/RunState.cs:992-1000` — `Act.CreateMap` → `ModifyGeneratedMap` → `AfterMapGenerated` (before `State.Map` is set, as in the game) → act-0 start point becomes Monster without Neow → `RemoveStaleVisitedMapCoords`. Saved-map path (`ModifyGeneratedMapLate`) is R7's (the importer sets `Map`).

## Map point entry (§3.3, §3.5)

- `RunLoop.EnterMapCoord` / `AddVisitedMapCoord` — `Runs/RunManager.cs:4124-4135`; `Runs/RunState.cs:763-772` — visited coord rejected; `NextRoomId = 0`.
- `RunLoop.EnterMapPointInternal` — `Runs/RunManager.cs:475-720` — `ActFloor`, `ExitCurrentRooms`, roll, `CreateRoom` (R4), history append, `EnterRoom`; save point A, `UpdatePlayerStatsInMapPointHistory`, sync/replay dropped; pre-finished combat with a parent event enters the event as base then the combat without exiting (`isRestoringRoomStackBase` flag not representable through `RoomStack.EnterRoom`).
- `RunLoop.BuildRoomTypeBlacklist` — `Runs/RunManager.cs:3978-3986` — previous history entry has a shop room, or every child is a shop.
- `RunLoop.RollRoomTypeFor` / `TryGetRoomTypeForTutorial` — `Runs/RunManager.cs:4207-4255` — Unknown → `UnknownMapPoint.Roll(blacklist, rs)` (R3); first-run override reads `RunConfig.Progress.NumberOfRuns`.
- `RunLoop.AppendToMapPointHistory` / `CurrentMapPointHistoryEntry` — `Runs/RunState.cs:427-437`, `:801-818` — flat list (R1) = last entry of the last act list.
- `RunLegalActionGenerator` Travel — `Nodes.Screens.Map/NMapScreen.cs:1388-1425` via R2 `MapTravel.GetLegalMapPoints` (column ascending); `Travel(i)` = `GameActions/MoveToMapCoordAction.cs:63-119`.

## Phases and legal actions (§3.4)

- `RunLoop.ResolvePhase` — node-layer screens (`Nodes.Screens/NRewardsScreen.cs`, `NMapScreen`, room nodes) — order: Defeat (`Runs/RunState.cs:446-456`) > Combat (in progress or starting) > Victory (`AbstractRoom.IsVictoryRoom`) > Rewards/CardReward (top of `RewardsStack`) > open map (`RunLoopState.MapOpen`) > current room type. `RunLoopState` (phase, map open, open card-reward index, victory) is R6 state, cloned and hashed with the run.
- `RunLegalActionGenerator.Fill` — CONTRACTS C1 — phase actions ascending, then potions (UsePotion by slot, DiscardPotion by slot), then the leave/proceed action; Combat = `LegalActionGenerator.Fill` mapped with `RunAction.FromCombat`; a pending prompt (combat or run) = `LegalActionGenerator.FillPrompt` mapped (PromptSelect/Confirm/Cancel), modal.
- Rewards — `Rewards/Reward.cs:242-258`; `Multiplayer.Game/RewardsSetSynchronizer.cs:624-771` — `ClaimReward(i)` for every not `SuccessfullySelected` reward of the top set (display order = `RewardsSet.Rewards`); a card reward opens the CardReward phase instead of `OnSelectWrapper`; `ProceedRewards` pops the set, `OnSkipped` on unclaimed rewards, terminal set → `ProceedTerminal`. Gap: a non-terminal set whose rewards are all taken still needs `ProceedRewards` (the game completes it automatically).
- CardReward — `Rewards/CardReward.cs:591-720` — `PickRewardCard(i)` → R3 `CardReward.PickCard`, `RewardAlternative(j)` → `ChooseAlternative`; the returned bool is read as "selection ended" (`endSelection`), which closes the screen. No leave action (Skip is an alternative).
- Shop — `Entities.Merchant/MerchantEntry.cs:308-321` — `BuyEntry(i)` iff `IsStocked && EnoughGold`; `LeaveShop` always.
- Rest site — `Nodes.Rooms/NRestSiteRoom.cs:188-190`, `:1056-1062` — enabled options; `LeaveRestSite` iff `ProceedAllowed || Options.Count == 0`.
- Treasure — `Nodes.Rooms/NTreasureRoom.cs:551-565`, `:640-646` — `OpenChest` until opened; then Take/Skip while a relic is offered; take/skip (or no relic) opens the map (no separate proceed action exists in `RunActionKind`).
- Event — `Events/EventOption.cs:66`, `:279`, `:292` — `ChooseEventOption(i)` for enabled options of `LocalMutableEvent ?? CanonicalEvent`; `LeaveEvent` iff `IsFinished`.

## Deck prompts (§3.12)

- `RunSim.Step` tape engine — CONTRACTS B1 (run scope) — basis = `RunState.DeepClone` + `RunLoopState`; run-scope `ChoicePendingException` stores `(basis, action, tape, prompt, picks)`; answers re-run the action from a fresh basis clone; Begin (new run / load) is the internal action; `Step(a, answerer)` is the rollout fast path. Phase at a prompt = Combat when the observation's combat is in progress, else Prompt. Hash at a prompt = H(basis loop ‖ basis ‖ action ‖ tape ‖ picks).

## Potions out of combat (§3.13)

- `RunLegalActionGenerator.CanUsePotionOutOfCombat` / `CanDiscardPotionOutOfCombat` — `Nodes.Potions/NPotionPopup.cs:285-345`; `GameActions/UsePotionAction.cs:96-122` — AnyTime only, owner alive, `CanUseOrRemovePotions`, `PassesCustomUsabilityCheck`, target resolved as `PotionActions.ResolveTarget` with no combat (self unless TargetedNoCreature); legal in every non-combat phase except Prompt; use under an Inline choice context; discard → `PotionCmd.Discard`. Content gap seen while porting: `BloodPotion.OnUse` requires a combat (W3), the soak will report it.

## Combat composition (§8.3)

- Room entry starts the combat inside R4 `CombatRoom` (SetUpCombat → `AfterRoomEntered` → StartCombat); `RunSim` steps it with `CombatLoop.Step` on the run's live state (D3/D4), `ChoiceUnwindException` swallowed once the combat is over (as `CombatSim`).
- `RunLoop.OnCombatVictory` — `Combat/CombatManager.cs:1648-1810` — `room.OnCombatEnded` (R4), last history room `TurnsTaken`, `MarkPreFinished`, `RunState.Combat = null`, `OfferRoomEndRewards` → Rewards phase; no set offered → `ProceedTerminal`. Gap (kernel, not R6): W1 `EndCombatInternal` passes `room: null` to `AfterCombatEnd`/`AfterCombatVictory` and does not call `OnCombatEnded` between `History.Clear` and `Player.AfterCombatEnd`; R6 calls it after `AfterCombatVictory`. WinTime, save point B, progress/achievements dropped.
- `RunSim.Combat` — CONTRACTS C4 — `CombatSim.FromState(rs.Combat)` cached per combat object; a read-only view (stepping it bypasses the run loop).

## Act transition, victory, defeat (§3.14)

- `RunLoop.ProceedTerminal` — `Nodes.Screens/NRewardsScreen.cs:668-718` — boss room or victory room: first boss with a second boss pending → map; else `MoveToNextAct`; other terminal sets → `ProceedFromTerminalRewardsScreen` (map unless the parent event resumes).
- `RunLoop.ProceedFromTerminalRewardsScreen` — `Runs/RunManager.cs:2410-2470` — `CurrentRoomCount > 1` and combat room resuming its parent event → `RoomStack.ResumePreviousRoom`.
- `RunLoop.MoveToNextAct` / `EnterNextAct` — `Multiplayer.Game/ActChangeSynchronizer.cs:77-91`; `Runs/RunManager.cs:754-928` — `ActFloor++`; last act → `EnterRoom(EventRoom(THE_ARCHITECT))` (fails while R5 has not registered it); else `EnterAct(i+1)`.
- Victory = current room `IsVictoryRoom` (`Rooms/AbstractRoom.cs:28-38`); the Architect's cosmetic page and `WinRun` are not run. Defeat = every player dead (`Runs/RunState.cs:446-456`), in or out of combat.

## Save entry (§7.1)

- `RunSim.FromSave` → R7 `RunImporter.FromSave`; `RunSim.FromLoadedState(rs, preFinishedRoom)` → `RunLoop.LoadIntoLatestMapCoord` (`Runs/RunManager.cs:2360-2381`): last visited coord re-entered without a save, no coord → `MapRoom`. A prompt at a load re-runs with the same pre-finished room object (edge case, not exercised).

## Soak (C5)

- `RunSoak.Run(RunSoakOptions)` — uniform random legal actions, `ContentFilter.PortedOnly`, step cap 6000, `Hash(x) == Hash(x.Clone())` every 101 steps, closure (every listed action steps on a clone) every 499 steps; failures `<seed>/<phase>/<step>: <Type>: <message>`; a `NotImplementedException` propagates (PENDING). Test: 10⁴ runs; Bench: `run-soak` 10⁵.

## W4 run-glue (2026-11-01)

- `RunLoop.OnCombatVictory` → `CombatRoom.AfterCombatVictory` (`OnCombatEnded` + `MarkPreFinished`) — Combat/CombatManager.cs:1648-1810; history `TurnsTaken` first; then `OfferRoomEndRewards` (terminal sets) → Rewards; no set → `ProceedTerminal`.
- Event combat resume — Runs/RunManager.cs:2410-2470 — terminal `ProceedRewards` → `ResumePreviousRoom` → `EventRoom.Resume` → `EventModel.Resume`; test `Test_RunLoop_EventCombatVictoryResumesTheEvent`.
- `RunActionExecutor.ClaimReward` on a `CardReward` calls `OnSelectWrapper` (opens the screen, `IsSelecting`) before the CardReward phase — Rewards/CardReward.cs OnSelect; without it every pick threw "screen is not open".
- `RunLoop.EnterMapPointInternal` pre-finished combat with a parent event — Runs/RunManager.cs:475-720 — `EnterRoomInternal(EventRoom(parent), isRestoringRoomStackBase: true)` then `EnterRoomInternal(combat)`; no history append (was `EnterRoom` + `EnterRoomWithoutExitingCurrentRoom`).
- `RunLoop.GenerateMapOnLoad` — Runs/RunManager.cs:2104-2160 (saved-map branch) + Nodes/NGame.cs:1549-1573 — imported `SavedActMap` → `ModifyGeneratedMapLate` → `AfterMapGenerated` → set → stale coords removed (no Neow start-point rewrite); no saved map → `GenerateMap`.
- `RunSim.FromSave` = `RunImporter.ImportState` → `GenerateMapOnLoad` → `FromLoadedState(rs, RunImporter.CreatePreFinishedRoom)` — Nodes/NGame.cs LoadRun; no longer calls `RunImporter.FromSave` (was infinite recursion).
- `ActInstance.PullNextEvent` / `PullAncient(rs)` — Models/ActModel.cs PullNextEvent — event id the run's `ContentFilter` rejects → `SimStubEvent` (fidelity-flagged PROCEED); `ContentFilter.All` still throws for unported ids.
- `RunSoak` — every registered character round-robin (`CharacterIds`), `NotImplementedException` thrown outside `Sts2Sim.Core.Run` = content/kernel gap (run stopped, tallied in `ContentGaps`, not a failure); run-layer stubs and every other exception = failure; `PropagateNotImplemented` (default false) restores the old rethrow. Test: 10⁴ runs, 0 failures, gaps printed.
- `Test_RunLoop_FullRunsReachTheArchitect` — 12 high-HP Ironclad random runs (PortedOnly) reach the Architect through every act transition; victory room = `THE_ARCHITECT`, clone hash equal.
