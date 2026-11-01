# Kernel gaps W3.5 (owner: kernel-gaps)

Decompile paths are relative to `output/server-bootstrap/linux-decompiled/`, namespace prefix `MegaCrit.Sts2.Core.` dropped.

## 1. End turn ready set

- `CombatState.PlayersReadyToEndTurn` (player combat ids; cloned, hashed under `StateCanonicalTags.CombatEncounter`) is `CombatTurnState.PlayersReadyToEndTurn` (Combat/CombatTurnState.cs:52).
- `PlayerCmd.EndTurn(cs, player, canBackOut)`: sets ready only when not already ready (Commands/PlayerCmd.cs:984-994). `PlayerCmd.SetReadyToEndTurn` adds once, no-op without a live combat (Combat/CombatManager.cs:6811-6857, `_turnState == null`). `UndoReadyToEndTurn` (Combat/CombatManager.cs:6859-6875). `IsPlayerReadyToEndTurn` (Combat/CombatManager.cs:6921-6939). `CombatState.AllPlayersReadyToEndTurn` compares the set size with the combat's players (Combat/CombatManager.cs:6951-6965).
- The ready set is cleared at the player turn start, together with the phase reset (Combat/CombatManager.cs:5292-5301).
- The end-turn signal is consumed after the player-driven action that raised it finishes (`AwaitTurnEndAndSwitchSides` waits for `RunningAction.CompletionTask`, Combat/CombatManager.cs:6715-6746). Sim: `CombatLoop.ExecuteResolvedAction` ends the turn after `PlayCard` / `UsePotion` / `DiscardPotion` when every player is ready (`EndTurnIfAllPlayersReady`); `StartTurn` returns "end the turn" when the set fills during turn setup (auto-play).
- The `EndTurn` action calls `PlayerCmd.EndTurn(canBackOut: true)` first (GameActions/EndPlayerTurnAction.cs:32); `EndTurnAndSwitchSides` marks the player ready, so every path that ends the player turn leaves the player in the set until the next player turn start, as in the game.
- `CreatureCmd.Kill`: a dead player on the player side while the combat continues calls `PlayerCmd.EndTurn(canBackOut: false)` (Commands/CreatureCmd.cs:2414-2438), replacing the former multiplayer-only throw.
- `VOID_FORM` un-excluded (Models.Cards/VoidForm.cs: apply VoidFormPower then `PlayerCmd.EndTurn(owner, false)`).

## 2. Escaped creatures and gold proportion

- `CombatState.EscapedCreatures` (deep-cloned, full canonical per creature) and `CombatState.CreatureEscaped` (add, then `RemoveCreature`; Combat/CombatState.cs:733-737).
- `CreatureCmd.Escape`: dead or detached → no-op; `RemoveAllPowersInternal`; `MonsterLifecycle.RemoveCreature` (CombatManager.RemoveCreature); `cs.CreatureEscaped` (Commands/CreatureCmd.cs:3915-3939). The fidelity flag `EscapeFidelitySite` is gone. A player escaping still throws (sim never removes the player).
- `CombatState.SpawnedEnemyIds`: distinct monster ids created on the enemy side (`EncounterModel.OnCreatureSpawned`, Models/EncounterModel.cs:343-359, called from `CombatState.CreateCreature`, Combat/CombatState.cs:722). Recorded in `MonsterSpawner.CreateCreature`.
- `EncounterDef.CalculateGoldProportion(cs)` virtual: `1 - escaped / spawned` (Models/EncounterModel.cs:324-327). `GremlinMercNormal.CalculateGoldProportion`: 1 if no FAT_GREMLIN escaped, else 0.5, or 0 once gold was stolen (Models.Encounters/GremlinMercNormal.cs:56-69).
- Run-glue hand-off: `CombatRoom.CalculateGoldProportion` (Run/Rooms/CombatRoom.cs:107) should call `EncounterRegistry.Get(cs.EncounterId).CalculateGoldProportion(cs)`; not edited here (Run/** belongs to run-glue).

## 3. Mutable encounter instance state

- `EncounterExtraState` (abstract, `ICanonical`, `DeepClone`) mirrors `MonsterExtraState`; `EncounterDef.CreateExtraState()` (default null); `CombatState.EncounterExtra` (cloned, hashed) is created in `CombatLoop.SetUpCombat` before the encounter spawns its monsters (the game's mutable `EncounterModel` lives on the room before the combat). `CombatState.GetEncounterExtra<T>()`.
- `GremlinMercNormal.Extra.GoldWasStolen`, `MarkGoldStolen(cs)`, `GoldWasStolen(cs)` (Models.Encounters/GremlinMercNormal.cs:33-54). Caller: SurprisePower (powers batch 8, Models.Powers/SurprisePower.cs:161-164).
- `BattlewornDummyEventEncounter.Extra.RanOutOfTime`, `GetRanOutOfTime` / `SetRanOutOfTime` / `IsBattlewornDummyEncounter` (Models.Encounters/BattlewornDummyEventEncounter.cs). `BattlewornDummyTimeLimitPower.AfterSideTurnEnd` sets it before escaping (Models.Powers/BattlewornDummyTimeLimitPower.cs:83-86); its fidelity flag is gone. Reader for run-glue: the BATTLEWORN_DUMMY event reads `RanOutOfTime` from the room's encounter after the combat (Models.Events/BattlewornDummy.cs:62-67); in the sim read it from the finished `CombatState` (keep the reference before `CombatRoom.Exit` clears `rs.Combat`).
- Un-excluded: `BATTLEWORN_DUMMY_EVENT_V1/V2/V3_ENCOUNTER` (monsters present, soak green). Still `[SimExclude]`: `GREMLIN_MERC_NORMAL` (GREMLIN_MERC throws for SURPRISE_POWER / THIEVERY_POWER, batch 8) and `THIEVING_HOPPER_WEAK` (SWIPE_POWER, batch 8); their encounter state is ported.
- Canonical tags: `StateCanonicalTags.CombatEncounter = 0x530F`, `MonsterCanonicalTags.GremlinMercNormalEncounterExtra = 0x4DFA`, `BattlewornDummyEventEncounterExtra = 0x4DFB`.

## 4. Run-scope card ops and creature commands

- `CardCmd.Transform` is one core (`TransformCore`) for every overload (combat, run, `TransformInDeck`, `TransformDeckCardTo`, `CardCmd.Deck` overloads), following Commands/CardCmd.cs:1988-2147: un-transformable / no-pile throws; replacements resolved before any removal; sort by pile type then index (`PileIndexSort`, Commands/CardCmd.cs:1973-1987); deck: `ModifyCardBeingAddedToDeck`, `FloorAddedToDeck = TotalFloor`, append (Commands/CardCmd.cs:2052-2065); combat piles: insert at index, `History.CardGenerated`, `AfterCardEnteredCombat`; then `AfterCardChangedPiles`, `AfterTransformedFrom/To`; finally `AfterCardGeneratedForCombat` for combat-pile results and `RemoveFromState`. The combat state handed to hooks is the first original's (null for deck cards), as in the game.
- Not ported: `CurrentMapPointHistoryEntry.CardsTransformed` (no field on the sim's `MapPointHistoryEntry`; Run/**).
- `CardFactory.CreateRandomCardForTransform(RunState, ...)` overloads: run-scope card creation when the original is not in combat (`original.CardScope.CreateCard`, Factories/CardFactory.cs:130-140); unported options are dropped with a fidelity record.
- `CreatureCmd.Kill(RunState, creature, force)` / out-of-combat kill: players only (Commands/CreatureCmd.cs:2595); multiplayer heal-1 guard; lose current HP + `AfterCurrentHpChanged`; `BeforeDeath`; `ShouldDie` → `AfterDeath(false)`, orb clear, `DeactivateHooks`; else `AfterDeath(true)`, `AfterPreventingDeath`, recurse while dead (Commands/CreatureCmd.cs:2750-2860). `CreatureCmd.Damage(RunState)` now kills a target it killed (Commands/CreatureCmd.cs:3845-3867). Game over is `RunLoop.IsGameOver` (all players dead).
- Un-excluded: `ASTROLABE` (Models.Relics/Astrolabe.cs:102-104), `NEW_LEAF` (Models.Relics/NewLeaf.cs:102), `CLAWS` (Models.Relics/Claws.cs:87-88, `PlayerRng.Transformations`), `PRECARIOUS_SHEARS` (Models.Relics/PrecariousShears.cs:123, run-scope damage out of combat).
- `PANDORAS_BOX`: replacements through `CreateRandomCardForTransform(rs, c, false, Niche)` and `CardCmd.Transform` (Models.Relics/PandorasBox.cs:50-51); it used to pick unported cards.

## 5. Card instance fields

- `CardInstance.FloorAddedToDeck` (`int?`, hashed as -1 when null) (Models/CardModel.cs:2233). Set: deck add when `ShouldAddToDeck` passes (Commands/CardPileCmd.cs:3373-3378 and :399-404), deck transform (Commands/CardCmd.cs:2058), standalone starting deck = 1 (Entities.Players/Player.cs:1108). Copied by both clone paths (memberwise). Run-glue: the run player's starting deck (Player.cs:1108) and save import of `floor_added_to_deck` are Run/**.
- `CardInstance.IsClone` now hashed; `CardInstance.CreateClone(cs)` = combat-pile check, `cs.CloneCard`, `IsClone = true`, `ExhaustOnNextPlay = false` (Models/CardModel.cs:3571-3583); `CreateCloneForPlayer` (Models/CardModel.cs:3585-3590). Callers switched to it where the game calls `CreateClone`: AdaptiveStrike, Anger, Undeath, DualWield, HeirloomHammer, JugglingPower, NightmarePower, MusicBox, BurningSticks. HISTORY_COURSE's dupe (`CreateDupe` → `CreateCloneForPlayer`) also sets `IsClone`.

## 6. Content throws removed

- `CRIMSON_MANTLE`, `INFERNO`: typed `IncrementSelfDamage()` call instead of reflection; `OUTBREAK`: typed `PoisonPower.Trigger(cs)`.
- `HISTORY_COURSE`: when the last attack has left every pile, the sim dupes a fresh card of the same id and records `HistoryCourse.LeftEveryPileSite` (approximation; the game keeps the `CardModel` in `CardPlayFinishedEntry`, Models.Relics/HistoryCourse.cs:58-62).
- `BOOMING_CONCH`: `RoomStack.CurrentRoom(rs)?.RoomType == Elite` (Models.Relics/BoomingConch.cs:72-73, :26-27); standalone combats have no room → no bonus.
- `PANTOGRAPH`: `AfterRoomEntered` sets Status Active when the current map point is a parent of the boss point, else Normal (Models.Relics/Pantograph.cs:51-60); `BeforeCombatStart` heals 25 in a Boss room when alive.
- `SLING_OF_COURAGE`: Elite room entered → +2 Strength (Models.Relics/SlingOfCourage.cs:23-26).
- `LIZARD_TAIL`: heal through `CreatureCmd.Heal(rs, ...)`, so out-of-combat death prevention works.
- `FOUL_POTION` out of combat: MerchantRoom → `GainGold(100)` (Models.Potions/FoulPotion.cs:60-65); FAKE_MERCHANT event room throws (event not ported).
- `BYRDPIP`: deck eggs outside combat transform through `CardCmd.TransformTo(rs, ...)`.
- `SPOILS_MAP.AfterMapGenerated`: documented no-op with fidelity `SpoilsMap.QuestOverlaySite` (the quest marker is map UI; `ActMap.WithQuestAdded` is copy-on-write and `RunLoop.GenerateMap` assigns `rs.Map` after the hook, Run/**).
- `EventKit3456.TransformToRandomInDeck` delegates to `CardCmd.TransformToRandom(rs, ...)` (cross-owner edit); the only remaining throw there is "no ported option".
- InfestedPower / StockPower: no gap left.

## 7. Tests

- `KernelGapsW35Tests`: VoidForm ends the turn; ready set clone/hash; escape list + base gold proportion; GremlinMerc proportion + theft; encounter extra at set-up.
- `Content_relics_9_1Tests`: Astrolabe, NewLeaf, Claws, PrecariousShears (incl. LizardTail prevention out of combat).
- `CardKernelTests`: in-combat deck transform now succeeds and stamps `FloorAddedToDeck`.
- `PipelineTests`: escape records instead of flagging.
- `W1GateTests`: SOZU excluded from the pilot relic draw (it blocks the potion slots the gate fills).
- `EndToEndTests`: pinned seed-1 hashes refreshed (canonical format change: ready set, escaped, spawned ids, encounter extra, `IsClone`, `FloorAddedToDeck`).

## W4.1 (combat-fixes, 2026-11-01)

- T2 room history for spawned enemies: `Commands/CreatureCmd.cs:302-307` appends each enemy added mid-combat (once per
  id) to the current map point's last room `MonsterIds`; ported as `CreatureCmd.AppendToRoomHistory`, called from
  `CreatureCmd.Add(cs, creature)` and `MonsterSpawner.AddToCombat` (the add path the spawners use), copy-on-write on the
  shared room record. Test `Test_W41_SpawnedEnemiesAppendToTheRoomHistory` (seed EC039PTFQWV5, LIVING_FOG_NORMAL, end
  turns until GAS_BOMB). `CombatRoom.OnCombatEnded`'s end-of-combat approximation (run layer) is now redundant (dedupe)
  and left in place.
- VIGOR_POWER on monsters: `Models.Powers/VigorPower.cs` BeforeAttack skips `ModelSource != null && !(ModelSource is
  CardModel)`; `Commands.Builders/AttackCommand.cs:781-791` FromMonster sets no ModelSource, so a monster attack is
  modified and consumes the Vigor (the sim skipped `IsFromMonster`). Test `Test_W41_MonsterAttackConsumesVigor` (seed
  K4FB4086NTRE, TERROR_EEL_ELITE A7, Vigor 6, end turn).
- `CombatState.PlayerHpAtCombatEnd` (harness observation, not canonical): the player HP when the combat is decided, set
  in `EndCombatInternal` before `AfterCombatEnd` / `AfterCombatVictory`; the diff harness compares the recorder's
  `final_hp` with it. Test `Test_W41_FinalHpIsReadBeforeVictoryHeals`.
