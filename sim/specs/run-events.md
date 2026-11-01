# R5 events — spec index (W2, 2026-11-01)

One line per rule: `member — decompile file:line — dropped points / gaps`. Paths are under the decompile tree without the `MegaCrit.Sts2.Core.` prefix.

## Framework

- `EventOption` ctor / `IsLocked` / `DisableOnChosen` default true — `Events/EventOption.cs:272-308` — title, description, hover tips, loc vars, `HistoryName`, `WillKillPlayer` (`ThatDoesDamage`/`ThatDecreasesMaxHp`, UI warning) dropped.
- `EventOption.Chosen` — `Events/EventOption.cs:66-98` — `BeforeChosen` event dropped (no in-game subscriber reaches rules in single player).
- `EventOption.WithRelic` / `RelicId` — `Events/EventOption.cs:324-346` — relic kept by id; `Relic` is null while the relic def is unported (W3).
- `EventOption.CloneFor` (sim) — handlers must be instance methods of the event (or capture nothing); a handler capturing locals throws on clone. Per-option data lives on the option (`Relic`, `StringArg`, `DonePage`); the handler reads `EventModel.ChosenOption`.
- `EventModel.BeginEvent` — `Models/EventModel.cs:68-144` — private Rng `Seed + (IsShared ? 0 : slot) + XXH64(Id)` (`:91`, `RngSeeds.Event`); combat-synchronizer check dropped; dead player → `GENERIC.youAreDead` finish.
- `EventModel.SetInitialEventState` / `GenerateInitialOptionsWrapper` / `ReplaceNullOptions` — `Models/EventModel.cs:576-611`.
- `EventModel.SetEventState` (empty options ⇒ finished; `StateChanged`) — `Models/EventModel.cs:790-810` — description kept as loc key only.
- `EventModel.SetEventFinished` / `EnsureCleanup` / `OnEventFinished` — `Models/EventModel.cs:760-788`.
- `EventModel.ChooseOption` — `Multiplayer.Game/EventSynchronizer.cs:376-393` — choice-history save (`SaveEventOptionToHistory`) dropped (log only).
- `EventModel.EnterCombatWithoutExitingEvent` — `Models/EventModel.cs:817-839` + `Multiplayer.Game/EventCombatSynchronizer.cs:131-210` — `CombatRoom { ParentEventId, ShouldResumeParentEventAfterCombat, ExtraRewards }` → `RoomStack.EnterRoomWithoutExitingCurrentRoom` (R4); `ShouldCreateCombat` (combat-layout events) and `EnteringEventCombat` UI event dropped.
- `EventModel.Resume` default no-op — `Models/EventModel.cs:755-758`.
- `EventModel.RelicOption` / `OptionKey` — `Models/EventModel.cs:841-863` — key `{Id}.pages.{page}.options.{RELIC_ID}`.
- `EventModel.ToMutable` — `Models/EventModel.cs:680-686` — sim: fresh instance of the same type (`Activator`); `SimStubEvent` overrides.
- `EventModel.GenerateInitialOptions` default throws `NotImplementedException("W3 events: …")` (abstract in the game; kept virtual so the HookTests double compiles).
- `EventRoom.Enter` — `Rooms/AbstractRoom.cs:40-44`, `Rooms/EventRoom.cs:44-210`, `Multiplayer.Game/EventSynchronizer.cs:177-225` — order: room id = `NextRoomId++`, cleanup of unfinished previous event, `ToMutable` + `BeginEvent`, subscribe `StateChanged`, finished-at-begin check, `Hook.AfterRoomEntered`, `AfterEventStarted`; asset preloads, `NEventRoom`, `GenerateInternalCombatStateIfNecessary` (layout combat state) dropped. Single player: one mutable event for `Players[0]`.
- `EventRoom.Exit` — `Rooms/EventRoom.cs:231-295` — pending-task await, checksum, combat-synchronizer reset dropped; unsubscribe + `EnsureCleanup` kept.
- `EventRoom.Resume` — `Rooms/EventRoom.cs:373-379` → `EventModel.Resume`.
- `EventRoom.OnEventStateChanged` — `Rooms/EventRoom.cs:394-418` — ancient finished ⇒ `MarkPreFinished` (save point C); the `SaveRun` call is R6/R7's.
- `SimStubEvent` (sim only, `[SimExclude]`) — C5 R6 soak: unported event id → one `PROCEED` option that finishes the event; `CreateOrStub(id)`; fidelity-flagged.

## Ancients

- `AncientEventModel.BeforeEventStarted` arrival heal — `Models/AncientEventModel.cs:55-116` — Neow sets HP 0 first; heal `(MaxHp − CurrentHp) × 0.8m` at A2 `WearyTraveler`; out of combat `CreatureCmd.Heal` = `HealInternal` (no hook: `Commands/CreatureCmd.cs:2205`); history `HpHealed`, SFX/VFX, `LerpAtNeow` dropped.
- `AncientEventModel.GenerateInitialOptionsWrapper` — `Models/AncientEventModel.cs:275-289` — `Hook.ShouldAllowAncient` false ⇒ single `PROCEED` option (sim: finishes the event so R6 offers `LeaveEvent`; the game leaves through `NEventRoom.Proceed` without finishing — room is then not marked pre-finished in the game, it is in the sim); `DebugOption` dropped.
- `AncientEventModel.SetInitialEventState` (empty or pre-finished ⇒ `StartPreFinished`) — `Models/AncientEventModel.cs:291-302`.
- `AncientEventModel.Done` / `StartPreFinished` — `Models/AncientEventModel.cs:328-351` — `UpdateRunHistory` (ancient choice history) dropped.
- `AncientEventModel.RelicOption` handler — `Models/AncientEventModel.cs:358-377` + IL of `<<RelicOption>g__OnChosen|0>d` — order `RelicCmd.Obtain(relic, Owner)` → `CustomDonePage = customDonePage` → `Done()`; the generic overload `RelicOption<T>(page, customDonePage)` drops `customDonePage` (`:353-356`, kept). Unported relic: obtain skipped + fidelity flag.
- `SetupRelicForPlayer` (sim) — relic implements `IEventRelicSetup` (TouchOfOrobas, ArchaicTooth, DustyTome: W3); unported ⇒ treated as eligible + fidelity flag (DustyTome's `PlayerRng.Rewards` draw, `Models.Relics/DustyTome.cs:119-123`, is then missing). SeaGlass character goes through `ICharacterBoundRelic` (and `EventOption.StringArg`).
- `Neow.GenerateInitialOptions` — `Models.Events/Neow.cs:295-363` — lists `:145-187`; curse `NextItem` after `!IsAllowedAtNeow` filter; exclusions; LargeCapsule skips the LavaRock/SmallCapsule `NextBool`; two more `NextBool`; filter; `UnstableShuffle` take 2 + curse. Modifier path (`:365-387`) throws `NotImplementedException` (custom/daily modes not ported).
- `Neow` `IsAllowedAtNeow` fallback for unported relics — `Models.Relics/MassiveScroll.cs:158-161` (players > 1), `WingedBoots.cs:48-51` / `SilverCrucible.cs:82-85` (players == 1), `Kaleidoscope.cs:116-123` (all character pools unlocked); ScrollBoxes `CanGenerateBundles` (`ScrollBoxes.cs:165-197`) approximated as true — gap until the relic is ported. Registered relics use `RelicModel.IsAllowedAtNeow`.
- `Orobas.GenerateInitialOptions` — `Models.Events/Orobas.cs:197-223` (pools `:30-103`) — `NextItem(other unlocked characters)`, `NextFloat() < 0.3333333f` PrismaticGem else SeaGlass, then `NextItem` ×3; pool 3 locked option `OPTION_POOL_3_LOCKED`.
- `Pael.GenerateInitialOptions` — `Models.Events/Pael.cs:157-179` — Goopy-enchantable ≥ 3 ⇒ Claw, removable ≥ 5 ⇒ Tooth, `val.AddRange(val)` self-duplication kept, then Growth; `HasEventPet` = `Entities.Players/Player.cs:675-682`.
- `Tezcatara.GenerateInitialOptions` — `Models.Events/Tezcatara.cs:184-194` — basic Strike in deck ⇒ NutritiousSoup in pool 1.
- `Nonupeipe.GenerateInitialOptions` — `Models.Events/Nonupeipe.cs:140-150` — Swift-enchantable ≥ 4 ⇒ BeautifulBracelet; shuffle take 3.
- `Tanx.GenerateInitialOptions` — `Models.Events/Tanx.cs:135-142` — Instinct-enchantable ≥ 3 ⇒ TriBoomerang; shuffle take 3.
- `Vakuu.GenerateInitialOptions` — `Models.Events/Vakuu.cs:150-162` — three shuffles, first of each; `CalculateVars` (profile visit count, text only) dropped; SereTalon `ThatDecreasesMaxHp(9)` UI-only dropped.
- `Darv.GenerateInitialOptions` — `Models.Events/Darv.cs:147-196` — sets filtered in order, `NextItem` per set (1-element arrays still draw), shuffle, `NextBool` ⇒ take 2 + DustyTome (after `SetupForPlayer`) else take 3.
- `TheArchitect` — `Models.Events/TheArchitect.cs:881-1090` — dialogue selection (`LoadDialogue` `NextItem` on the private Rng over profile-dependent dialogues) and the per-line `CONTINUE/RESPOND` options collapsed into one `PROCEED` option; `WinRun` sets `RunWon` and finishes the event (R6 maps it to Victory); score, animations dropped.

## Not here

- The other ~60 events (W3, `Run/Events/Defs/`). Choice history / run history entries. `EventSynchronizer` multiplayer voting. Saving at point C (R6/R7).
