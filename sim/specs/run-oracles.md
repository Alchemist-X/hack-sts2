# R8 run oracles — spec index

Code: `sim/Sts2Sim.Diff/Run/*`, CLI `Sts2Sim.Diff run <dir-or-file> [--report out.json] [--any-build] [--strict] [-v]`
(combat bundle mode unchanged). Inputs: `sts2rec export-run-oracles OUT` (`sts2rec/sts2rec/run_oracles.py`): copies
single-player schema-20 `current_run.save`, recorder `<session>/native/*.save`, and schema-10 `.run` of build
`v0.111.0` (standard, no modifiers) + `manifest.json`. Counts are reported, not gated (Part C §C5); exit 1 only with `--strict`.
Status per check: agree / diverge / unsupported (sim stub, unported content, approximation) / error.

Local result 2026-11-01 (1 save, 6 `.run`): neow 7/7 agree; unknown_rolls 6 agree, 1 unsupported (no map); map_saved 1/1;
map_path 1/1; run_start, map_generated, encounter_pulls, event_pulls unsupported (act defs, encounters, events not ported).

- `NeowOracle` — `Models.Events/Neow.cs` via R5 `Neow.GenerateInitialOptions`; seed `RngSeeds.Event` — compares `ancient_choice[].TextKey` order of floor 1 with `CurrentOptions` (`RelicId ?? TextKey`). Unlocks: save `unlock_state`, else `UnlockProfile.All` (Kaleidoscope gate). Gap: non-Ironclad decks empty (no ported Neow relic reads the deck).
- `UnknownRollOracle` — `Odds/UnknownMapPointOdds.cs:99-177`, `Runs/RunManager.cs:680-681` (blacklist `:3978-3986`, `CurrentMapPointHistoryEntry` `Runs/RunState.cs:427`), `ResetToBase` `:2749` — one `Roll` per unknown floor, act-start reset; save also checks the `unknown_map_point` counter and the four float32 odds. Gaps: all-shop-children blacklist needs the map (only the save's current act has it; a mismatch after a map-less roll is unsupported); runs holding JUZU_BRACELET / GOLDEN_COMPASS / LANTERN_KEY are unsupported (obtain floor not replayed).
- `RoomPullOracle.encounter_pulls` — `Rooms/RoomSet.cs` next/mark-visited via R1 `RoomSet`; walk `rooms[0]` per floor (event-combat second rooms are not pulls, `Runs/RunManager.cs:4178-4205`); save checks the visited counters. Unsupported while a room-set encounter is unported.
- `RoomPullOracle.event_pulls` — `RoomSet.EnsureNextEventIsValid` + ancient = `AncientId`; floor (`TotalFloor`) and act index replayed. Gap: gold/potions/hp not replayed for `IsAllowed`; a mismatch involving an unported event (`IsAllowed` unknown) is unsupported.
- `MapOracle.map_saved` — `Map/SavedActMap.cs:110-153` via R2 `SavedActMap` — saved_map → `SerializableActMap` → `SavedActMap` → `FromActMap` compact text, exact incl. child order.
- `MapOracle.map_generated` — `Models/ActModel.CreateMap` → `StandardActMap.CreateFor` vs saved_map; unsupported until W3 act defs.
- `MapOracle.map_path` — `Nodes.Screens.Map/NMapScreen.cs:1388-1425` via R2 `MapTravel.GetLegalMapPoints` — each visited coord legal, point type = history type. WINGED_BOOTS runs unsupported.
- `RunStartOracle` — `Runs/RunManager.cs` new-run order via R6 `RunLoop.CreateForNewRun` with the save's act ids — room sets, `up_front` counter, both grab bags as ordered subsets (pulls after start). Unsupported until W3 acts and non-Ironclad starts.
- Not done (time): recorder map snapshots (design §4.5 item 2), offers oracle (card/relic/potion choices), `.run` encounter/event pulls (need run-start generation, wired through `FromGeneration` once acts exist).
