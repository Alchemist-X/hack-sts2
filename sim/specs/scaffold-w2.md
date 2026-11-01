# W2 phase-0 scaffold (2026-11-01)

Contract: `sim/CONTRACTS.md` Part C. Design: `docs/sim-port/design-run-layer.md`. Stubs throw
`NotImplementedException("W2 <owner>: <Type>.<Member>")`; none is reached by the suite (PENDING 0).

## What exists

| Owner | Files | Implemented now | Stubbed |
| --- | --- | --- | --- |
| scaffold | `Api/RunAction.cs`, `Registry/RunRegistries.cs` | `RunPhase`, `RunActionKind`, `RunAction` (+ `FromCombat` / `TryToCombat`), `ActRegistry` (DefTable), `EventRegistry` (ModelTable) | — |
| R1 | `Run/RunState.cs` (fields), `Run/State/*` | new `RunState` fields with defaults + clone + canonical; `PlayerState` run fields (`PlayerState.Run.cs`); `ExtraRunFields`, `ExtraPlayerFields`, `RunConfig`, `RunProfileProgress`, `ContentFilter` (`All`, `PortedOnly`) | `ActModel` members (`Models/ActModel.cs:185-198`, `:465-509`), `ActInstance` pulls (`:241-436`), `RoomSet` (`Rooms/RoomSet.cs:74-162`), `MapPointHistoryEntry` clone/canonical |
| R2 | `Run/Map/*` | `MapCoord` (`Map/MapCoord.cs:44-75`), `MapPoint` structure (`Map/MapPoint.cs:10-115`), `ActMap` abstract shape (`Map/ActMap.cs:266-285`) | `ActMap` queries (`:287-370`), `StandardActMap` (`Map/StandardActMap.cs:75-102`), `MapPointTypeCounts.StandardRandomUnknownCount` (`Map/MapPointTypeCounts.cs:57-60`), `MapTravel` (`Map/MapTravel.cs:9-16`, `Nodes.Screens.Map/NMapScreen.cs:1388-1425`) |
| R3 | `Run/Odds/Odds.cs`, `Run/Rewards/*`, `Run/Factories/*`, `Run/Relics/RelicGrabBag.cs`, `Run/Util/GrabBag.cs` | ctors and data shape | all of `Odds/*.cs`, `RelicGrabBag` (`Runs/RelicGrabBag.cs:26-458`), `GrabBag` (`Helpers/GrabBag.cs:16-50`), `RelicFactory` (`Factories/RelicFactory.cs:15-63`), `RewardsCmd` (`Commands/RewardsCmd.cs:295-340`), `Reward`/`RewardsSet`/`CardReward`, `CardFactory.CreateForMerchant/CreateForReward` (`Factories/CardFactory.cs:34-91`), `CardCreationOptions.For*` (`Runs/CardCreationOptions.cs:78-119`) |
| R4 | `Run/Rooms/*` | room classes, `RoomStack.CurrentRoom/BaseRoom` | `Enter/Exit/Resume/CloneFor/WriteCanonical`, `CombatRoom.StartCombat/OnCombatEnded/OfferRoomEndRewards`, merchant, rest site, treasure, `RoomStack.Create/Enter/Exit` |
| R5 | `Run/Events/*` | `EventOption` legality (`Events/EventOption.cs:66`, `:279`, `:292`), `EventRoom` shape | `EventModel` lifecycle (`Models/EventModel.cs:68-144`, `:790-839`), `AncientEventModel` (`Models/AncientEventModel.cs:76-105`, `:340-377`) |
| R6 | `Run/Loop/*` | — | `RunSim` (every member), `RunLoop`, `RunLegalActionGenerator`, `RunActionExecutor` |
| R7 | `Run/Save/*` | `SavePoint`, `SavedRun` shell, `ImportOptions`, `RunImportResult` | `RunSaveReader`, `RunImporter` |

Moves (`git mv`, content replaced by the stubs above): `Run/{MapPoint,ActMap}.cs` → `Run/Map/`,
`Run/{MapPointHistoryEntry,ActModel}.cs` → `Run/State/`, `Run/{Reward,RewardsSet,CardReward,CardRewardAlternative}.cs`
→ `Run/Rewards/`, `Run/{CardCreationOptions,CardCreationResult}.cs` → `Run/Factories/`,
`Run/{AbstractRoom,CombatRoom,MerchantEntry,RestSiteOption}.cs` → `Run/Rooms/`, `Run/{EventModel,AncientEventModel}.cs`
→ `Run/Events/`. `EventModel` now derives `AbstractModel` (as `Models/EventModel.cs`).

## Cross-owner edits

- `State/CreatureState.cs`: `PlayerState` made `partial`; copy ctor calls `CopyRunFieldsFrom(source)`; `WriteCanonical`
  ends with `WriteRunCanonical(w)`.
- `Sts2Sim.Tests/HookTests.cs`: the four shell doubles (`HookMerchantEntry`, `HookRoom`, `HookMap`, `HookEvent`) and
  two `new CardReward()` / `new CardCreationOptions()` calls follow the new abstract members and constructors.
- `Sts2Sim.Tests/EndToEndTests.cs`: pinned hashes moved because `RunState` / `PlayerState` canonical forms gained the
  run-layer blocks (tags 0x5501, 0x550A): opening `0x07F5FF6679103FF9` → `0xD9F811A7035088B2`, final
  `0x77FA4D8665BA0CC3` → `0xA8037EFF0501E0C4`.

## Content soak (W3 gate)

`Sts2Sim.Tests/ContentSoakTests.cs`: `ContentSoak.Run(ContentSoakOptions)` with per-kind filters. Current tree:
encounters 24 cases (8 × 3), cards 28 (14 × 2), relics 14 (7 × 2), potions 12 (6 × 2), 0 failures, < 0.2 s.
Not covered: relics are obtained and potions procured after `CombatSim.Create` (turn 1 already started), so
`BeforeCombatStart` / first-turn start hooks of relics are not exercised; out-of-combat potion use waits for R6.
