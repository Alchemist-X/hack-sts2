# run-state (W2 R1) — spec index

One line per rule: `member — decompile file:line — dropped points / gaps`. Paths drop the `MegaCrit.Sts2.Core.` prefix.

- `ActModel.GetNumberOfRooms/GetNumberOfFloors` — Models/ActModel.cs:185-198 — none.
- `ActModel.NumberOfWeakEncounters` (3) / `BaseNumberOfRooms` (abstract) — Models/ActModel.cs:117-119 — W3 act defs must override `BaseNumberOfRooms`.
- `ActModel.AllEncounters/AllWeak/AllRegular/AllElite/AllBossEncounters` — Models/ActModel.cs:121-129 — lazy per-def caches; filters need `EncounterDef.RoomType` (kernel edit, default `Monster`).
- `ActModel.ApplyActDiscoveryOrderModifications` — Models/ActModel.cs:360 — abstract in game, virtual no-op here; takes the `ActInstance` because mutable rooms live there (Overgrowth override is W3).
- `ActModel.CreateMap` — Models/ActModel.cs:465-468 — forwards to R2 `StandardActMap.CreateFor`.
- `ActModel.GetRandomList` — Models/ActModel.cs:470-507 — `SaveManager.Progress.DiscoveredActs` → `RunProfileProgress.HasDiscoveredAct`; `TestMode.IsOff` taken as true. Overload taking `actsByIndex` for tests.
- `ActModel.GetDefaultList` — Models/ActModel.cs:509-541 — none.
- `ActModel.ActsByIndex` — Models/ModelDb.cs:220-275 — game order Overgrowth, Underdocks, Hive, Glory (`GameActOrder`), then any other registered act.
- `ActModel.AllSharedEventIds` (18) — Models/ModelDb.cs:138-158 — ids by Slugify.
- `ActModel.GetSharedAncientIds` (Darv, DARV_EPOCH) — Unlocks/UnlockState.cs:56-67, Models/ModelDb.cs:162 — none.
- Epoch event gates EVENT1/2/3 → TRASH_HEAP / REFLECTIONS / COLORFUL_PHILOSOPHERS — Timeline.Epochs/Event{1,2,3}Epoch.cs:21-33 — none.
- `ActInstance.GenerateRooms` — Models/ActModel.cs:241-323 — order kept (event filter + UnstableShuffle, weak bag, regular bag, 15 elites, boss NextItem, ancient NextItem over unlocked ++ shared subset); needs R3 `GrabBag` (test PENDING).
- `ActInstance.AddWithoutRepeatingTags` — Models/ActModel.cs:362-373 — tags are strings (`EncounterDef.Tags`, kernel edit), not the `EncounterTag` enum.
- `ActInstance.ValidateRoomsAfterLoad` — Models/ActModel.cs:325-336 — sim has no `DeprecatedEncounter`: a `null` Boss is treated as deprecated; deprecated SecondBoss is not representable (R7 gap).
- `ActInstance.ApplyDiscoveryOrderModifications` — Models/ActModel.cs:338-358 — none.
- `ActInstance.SetBossEncounter/SetSecondBossEncounter/RemoveEventFromSet` — Models/ActModel.cs:410-436 — none.
- `ActInstance.SetSharedAncientSubset` — Models/ActModel.cs:216-221 — ids, list replaced in place.
- `ActInstance.PullAncient/PullNextEvent/PullNextEncounter/MarkRoomVisited` — Models/ActModel.cs:375-403 — events returned as fresh `EventRegistry.Create(id)` instances (game returns canonical); unregistered ids throw (PortedOnly stub replacement is R5/R6).
- `RoomSet.Next*`, `MarkVisited` — Rooms/RoomSet.cs:74-112 — none.
- `RoomSet.EnsureNextEventIsValid` — Rooms/RoomSet.cs:114-129 — `IsAllowed` on a fresh instance; unregistered ids count as allowed; `Log.Warn` dropped.
- `RunState.CurrentActIndex` setter — Runs/RunState.cs:346-362 — clears visited coords, `ActFloor = 0`, `NextRoomId = 0` on change; clone writes the backing field.
- `RunState.AddVisitedMapCoord` — Runs/RunState.cs:763-772 — none.
- `RunState.AddVisitedEvent` — Runs/RunState.cs:796-799 — none.
- `RunState.AppendToMapPointHistory` / `CurrentMapPointHistoryEntry` / `GetHistoryEntryFor` / `TotalFloor` — Runs/RunState.cs:423-438, :801-827 — flat list with `ActIndex` per entry instead of a list per act; player stats block not ported except `GoldGained`.
- `MapPointHistoryEntry.HasRoomOfType` — Runs.History/MapPointHistoryEntry.cs:236-255 — none.
- `RunState.CreateForNewRun` — Runs/RunState.cs:550-582, :621-681 — `CardModel.AfterCreated` per deck card dropped (sim cards run `AfterCreated` at creation); multiplayer scaling / badge models not ported (SP no-op); needs R3 `RunOddsSet`/`PlayerOddsSet` (test PENDING).
- `RunState.InitializeSeed` — Entities.Players/Player.cs:729-733 — `PlayerRngSet.ForPlayer(seed, slot)`.
- `RunState.SetStartedWithNeowFlag` — Runs/RunManager.cs:3924-3927 — NEOW_EPOCH.
- `RunState.ApplyAscensionEffects` (A4 −1 potion slot, A5 ASCENDERS_BANE appended) — Entities.Ascension/AscensionManager.cs:27-39, Runs/RunManager.cs:3856-3868, :4827-4830 — idempotent via `AscensionEffectsApplied`; `FloorAddedToDeck = 1` dropped (CardInstance has no such field yet, cards-kernel gap).
- `RunState.GenerateRooms` — Runs/RunManager.cs:4055-4088 — `ShouldApplyTutorialModifications` (:4090-4109) is true (Standard, TestMode off); A10 second boss on the last act.
- `RunState.HasAscension` — Entities.Ascension/AscensionManager.cs:22-25 — none. Other ascension sites (A1–A3, A6–A10) stay with their owners (design §6.4).
- Floor numbering §6.3 — Runs/RunState.cs:423 — `TotalFloor = MapPointHistory.Count`; `ActFloor` bumps are R6.
- `EncounterDef.RoomType/Tags/SharesTagsWith` (kernel edit) — Models/EncounterModel.cs:39, :85, :232-239 — defaults `Monster` / empty.
- Clone/canonical §2.6 — `ActInstance`, `RoomSet`, `MapPointHistoryEntry` deep copies; defs shared; canonical tags 0x550C–0x550E.
- Run listener order §2.7 — Runs/RunState.cs:830-933 — verified against `ListenerSnapshot` (deck + enchantments, then relics/potions only without combat, modifiers, combat part); no change.
