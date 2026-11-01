# W4 replay-runs — G3 seed replay on the v0.111.0 corpus (2026-11-01)

Owner: W4 replay-runs. Base `c145de0` (W3.5/W4 integrate 1). Inputs: `sts2rec export-bundles-v2` over the
recorder corpus (19 lineages; 11 v0.111.0 run bundles, 10 with a usable seed), the six local v0.111.0 `.run`
histories + the fixture save (`sts2rec export-run-oracles`). Outputs (git-ignored): `tmp/diff-v2/report-runs.json`
(G3), `tmp/diff-v2/report-run-oracles.json` (R8 oracles). Command:

```
cd sts2rec && uv run sts2rec export-bundles-v2 <repo>/tmp/diff-v2 --sim-core ../sim/Sts2Sim.Core
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 tmp/diff-v2 --report tmp/diff-v2/report-runs.json [-v]
```

The G3 runner (`Sts2Sim.Diff/V2/G3*.cs`, index line in `diff-harness-v2.md`) starts `RunSim.NewRun` from each
bundle's seed (Neow on, all content, `UnlockProfile.All`), chains every segment with the recorded run-level and
combat decisions, compares the screen projection before every observed decision, the room at every entry, the
`floor_summary` (map point type, rooms with model and monster ids, turns) at every room exit and the combat result,
and stops at the first divergence. The harness-v2 `V2Runner` could not be used as is: 8 of 10 bundles fail its strict
reader (`start` with `deck: null`, decisions without an action) and its run projection uses another schema than the
exporter (`event.id` vs `event_id`, `rewards.items[].gold` vs `gold_amount`, relics as ids vs `{id, counter}`,
claimed rewards listed vs removed, map without the start and boss points, `reward_type` `gold` vs `gold_reward`,
Neow option keys `NEOW.pages.INITIAL.options.X` vs `X`, combat `select.mode`). `G3Schema` adapts both sides;
`V2Runner.ReplayCombat` is reused for combat segments (made public, the only harness-v2 edit).

## Headline

| | value |
| --- | --- |
| runs replayed from the seed | 10 (+1 `old`-prefix lineage, abandoned on floor 1: out_of_scope, strict reader rejects it) |
| runs reaching the recorded end | 0 |
| floors fully agreed before the first divergence | sorted `1, 2, 3, 4, 5, 7, 7, 10, 10, 13` (median 6, mean 6.2; before this work: `0 ×10`) |
| run-level + combat decisions compared / agreed | 753 / 748 (the 5 non-agreeing are first divergences) |
| floor summaries agreed | 63 |
| combats replayed to the recorded result (unanchored, from the seed) | 32 |
| map generation (act 1 graph, boss ids) | 10 / 10 runs agree |
| Neow options + Neow rewards | 10 / 10 runs agree (after the NEOWS_BONES fix) |

Run oracles (R8, `run` mode, 1 save + 6 `.run`): neow 7/7, encounter_pulls 7/7, unknown_rolls 6/6 (+1 unsupported),
map_saved 1/1, map_path 1/1, map_generated 1/1, run_start 1/1 (all three newly supported since acts landed),
**event_pulls 2/7 agree, 5 diverge** (see ticket T1).

## Per run

| lineage | char | recorded floor | floors agreed | segments agreed | decisions agreed | first divergence | class (§6.4) | cause |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1792878363-G0G2JMFEYHRS | IRONCLAD A10 custom | 49 | 1 (floor 2 room agreed) | 4 | 21/21 | s5 map → floor 3 Unknown | port_bug (event pull) + content_not_ported | sim pulls CRYSTAL_SPHERE (unported) where the game rolled SUNKEN_STATUE; same family as T1 |
| 1792989304-NBNDT3S61AUM | SILENT A4 custom | 48 | 7 | 17 | 92/93 | s17 combat f8 FOSSIL_STALKER_NORMAL, decision 19 | combat | `select.candidates[0]` STRIKE_SILENT vs NEUTRALIZE |
| 1793075862-DSB363J2LQWZ | SILENT A4 custom | 48 | 4 | 12 | 39/39 | s12 room f5 GREMLIN_MERC_NORMAL floor summary | port_bug (combat kernel, ticket T2) | spawned SNEAKY_GREMLIN / FAT_GREMLIN missing from `monster_ids` |
| 1793220474-P2UZ19X1GGNQ | SILENT A4 | 48 | 5 | 13 | 56/57 | s13 shop f6, decision 0 | exporter_bug / recorder_gap (R8) | `shop_remove` pre_state is post-payment (gold 54 = 129 − 75, removal slot unstocked) |
| 1793233995-Q3X6PF8S37GA | SILENT A5 | 48 | 2 | 5 | 47/48 | s5 combat f3 NIBBITS_WEAK, decision 19 | combat | `player.discard_count` 9 vs 10 |
| 1793298254-GM28D28DX794 | SILENT A6 | 48 | 13 | 31 | 140/140 | s32 combat f14 SKULKING_COLONY_ELITE | combat (projection_gap) | HARDENED_SHELL_POWER DisplayAmount rule missing in the combat projector |
| 1793383126-K4FB4086NTRE | SILENT A7 | 17 | 7 | 18 | 77/78 | s21 combat f8 TERROR_EEL_ELITE, decision 15 | combat | enemy VIGOR_POWER 6 in the sim, absent in the oracle |
| 1793384475-EC039PTFQWV5 | SILENT A7 | 48 | 10 | 25 | 126/126 | s27 room f11 LIVING_FOG_NORMAL floor summary | port_bug (combat kernel, T2) | spawned-then-removed GAS_BOMB missing from `monster_ids` |
| 1793496137-PLRUV49CRFQW | SILENT A8 | 31 | 10 | 24 | 101/101 | s29 combat f11 BYGONE_EFFIGY_ELITE | combat (projection_gap) | SLOW_POWER DisplayAmount rule missing |
| 1793561474-G1YFC196B2H2 | SILENT A8 | 17 | 3 | 8 | 49/50 | s9 event f4 ROOM_FULL_OF_CHEESE, decision 1 | recorder_gap (R8 snapshot timing) | `event_proceed` pre_state lacks CHOSEN_CHEESE that the SEARCH option already granted (floor summary `relic_choices` has it) |

## Fixed here (port_bug, run layer / run-side content)

1. **Map point history never recorded monster ids** — `RunLoop.AppendToMapPointHistory` creates the room entry with
   `Array.Empty<string>()` and `CombatRoom.StartCombat` only appended when the list `is List<string>`, so
   `monster_ids` was always empty (every combat floor summary diverged). Now `CombatRoom.RecordMonsterIds`:
   Rooms/CombatRoom.cs:381-398 (all encounter monsters, no dedupe — CORPSE_SLUGS_WEAK records two CORPSE_SLUG) at
   start; Commands/CreatureCmd.cs:302-307 (later adds, deduped) approximated at combat end from `cs.Monsters` and
   `cs.EscapedCreatures`; copy-on-write keeps clones isolated. Test
   `Test_ReplayRuns_CombatRoomRecordsEncounterMonstersInHistory` (seed GM28D28DX794, Neow DOWSING_ROD, move (4,1)).
2. **NEOWS_BONES rolled its curse before the relic rewards** — the game awaits `RewardsSet.Offer()`
   (Models.Relics/NeowsBones.cs:82) before rolling the curse with `Rng.Niche` (:118-128); the sim rolled it at once,
   so KALEIDOSCOPE's Niche pool shuffle (Models.Relics/Kaleidoscope.cs:56) saw a shifted stream (wrong card offers in
   3 runs) and the curse landed before the relic cards in the deck (wrong shuffles later). New
   `RewardsSet.ContinuationRelicId` + `IRewardsSetContinuation`, resumed by `RunActionExecutor.ProceedRewards`.
   Test `Test_ReplayRuns_NeowsBonesRollsItsCurseAfterTheRewardsScreen` (seed G0G2JMFEYHRS A10: LEAFY_POULTICE then
   KALEIDOSCOPE → the six recorded offers ARSENAL, GLACIER, LIGHTNING_ROD, RESONANCE, SEVERANCE, UP_MY_SLEEVE; GUILTY
   after the screen closes).

Both change the canonical form of runs that reach those states: F2 run pins refreshed
(`SelfConsistencyTests.PinnedRunStreams`, step counts unchanged 136/114/160/400, Release = Debug verified).

## Tickets left (not fixed here)

| # | class | area | finding | evidence |
| --- | --- | --- | --- | --- |
| T1 | port_bug (to confirm) | run layer: event pulls (`RoomSet.EnsureNextEventIsValid` / `EventModel.IsAllowed`) | the sim pulls another event than the game | R8 event_pulls 5/7 `.run` diverge (e.g. 1793233995 act 0 floor 4: THE_FUTURE_OF_POTIONS vs TABLET_OF_TRUTH); G3 G0G2 floor 3 SUNKEN_STATUE vs CRYSTAL_SPHERE with the real gold/HP/deck state, so not only the oracle's missing IsAllowed inputs |
| T2 | port_bug | combat kernel (`Combat/Pipeline/CreatureCmd.Add`) | game appends each added enemy's id to the room history entry (Commands/CreatureCmd.cs:302-307); the sim kernel does not, so spawned-and-removed monsters are lost | DSB3 f5, EC03 f11; owner: kernel (not edited here) |
| T3 | combat | combat projector | HARDENED_SHELL_POWER, SLOW_POWER DisplayAmount rules | GM28 f14, PLRU f11 |
| T4 | combat | combat replay | NIBBITS_WEAK discard count, TERROR_EEL_ELITE enemy VIGOR, FOSSIL_STALKER select candidates | Q3X6 f3, K4FB f8, NBND f8 (segment ids above) |
| T5 | content_not_ported | events | CRYSTAL_SPHERE | G0G2 f3 |
| T6 | exporter_bug | exporter-v2 | strict reader failures (`start.deck: null`, decisions without `action`, top-level `seeding` missing on `old` lineages); nested-reward decision order (Neow / Kaleidoscope `Index` sequences, `card_reward_skip` mis-detected); duplicated `event_proceed`; `shop_remove` post-action pre_state | G3 works around them (reorder ≤ 8, dropped skips, dropped duplicate proceeds, stale pre_states not compared); counts per run in the report |
| T7 | recorder_gap | recorder | snapshot timing (R8): stale pre_states (screen ≠ decision screen, 2–18 per run), relic `counter` during `IsActivating` (21 relics, not compared), combat `final_hp` before end-of-combat heals (BURNING_BLOOD), choice identity of identical copies (hand picks re-resolved from the next hand), card-reward offers (only post hoc in the floor summary; compared as a multiset) | report counters `stale_pre_states`, `resolved_hand_picks`, `pick_order_fixes`, `hoisted_decisions` |

Treatment of retries: QuickSL / save-and-quit attempts of a room are replaced by the last attempt (the game reloads
the room-entry save); a `post_combat` room followed by `post_combat_resume` is replaced by the resume (the reload
restores the pre-finished room). 1792878363 combat retry and K4FB's resumed rewards agree on this basis.
`custom` mode without modifiers runs as standard: `GameMode` only gates epochs/achievements
(Runs/GameModeExtension.cs) and the tutorial act modifications (Runs/RunManager.cs:4089-4108), which are no-ops under
a full unlock profile.

## Run-layer areas certified by at least one run (G3, agreeing decisions)

Map generation act 1 (10 runs), map travel (6), Neow options and Neow rewards incl. NEOWS_BONES / KALEIDOSCOPE /
LOST_COFFER / SILVER_CRUCIBLE / PHIAL_HOLSTER (10), combat rewards: gold, potion, card offers incl. upgraded offers
(10), room entry / unknown rolls (10), floor summaries (10), rest sites HEAL/SMITH (4), treasure (3), shop entries
and prices (P2UZ before the removal, GM28/EC03 later), deck-op selects (2), events AROMA_OF_CHAOS, MORPHIC_GROVE,
ROOM_FULL_OF_CHEESE, SELF_HELP_BOOK, TABLET_OF_TRUTH, TEA_MASTER, THIS_OR_THAT, TRASH_HEAP; 32 combats from the
seed. Not reached by any run yet: act transition (`next_act`), act 2–3 maps and pools, boss rewards, ancients after
Neow. What blocks the rest: T1 (event pulls), T2 (kernel monster history), T3/T4 (combat), and the recorder/exporter
gaps T6/T7; R1 anchors would let G2 replay rooms after these points instead of stopping the run.
