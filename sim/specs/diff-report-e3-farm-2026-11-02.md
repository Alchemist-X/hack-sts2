# E3 triage — diff gates on the 2026-11-02 MCP coverage farm

Owner: E3 `triage`. Branch `e3/triage` from `ebc055f`. Input: the E3 farm (`docs/sim-port/e3-farm-2026-11-02.md` on
`e3/farm`): 39 recorder sessions from 4 headless v0.111.0 workers with the REC recorder (R1 save copies, R2 replay
copies, R4 reward offers, R5 option keys). Design: `docs/sim-port/design-diff-gate.md` §3 (save import), §5 (bundle v2),
§6 (G0–G3, §6.4 classes). Decompile citations drop the `MegaCrit.Sts2.Core.` prefix (v0.111.0 tree). Bundles, saves
and reports stay under the session worktree's git-ignored `tmp/farm/2026-11-02/`; nothing from the farm is committed.

## 1. Commands

```
# one sessions root for the four workers (symlinks; the corpus stays where the farm wrote it)
<farm>/sessions-all/<session> -> <farm>/worker-<i>/sessions/<session>
cd sts2rec && uv run sts2rec export-bundles-v2 <farm>/bundles --root <farm>/sessions-all --sim-core ../sim/Sts2Sim.Core
dotnet run -c Release --project sim/Sts2Sim.Diff -- <farm>/bundles --v2 --report <farm>/g12.json \
    --baseline sim/specs/diff-baseline-e3-farm.json                         # G1/G2, harness v2 (V2Runner) + ratchet
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <farm>/bundles --report <farm>/g3-plain-final.json            # G3
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <farm>/bundles --anchors --report <farm>/g3-anchors-final.json
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <farm>/bundles --anchors-every --report <farm>/g3-anchors-every-final.json
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <farm>/bundles --save-check --report <farm>/g3-save-check-final.json
dotnet run -c Release --project sim/Sts2Sim.Diff -- <sim-source-recreation-9392df>/tmp/handoff/diff-bundles/bundles      # G0
```

`<farm>` = `.claude/worktrees/sts2-eval-pipeline-a3aee7/tmp/farm/2026-11-02`. Run oracles were not exported: G3 does
not read them, and the farm's `.run` files are covered by G3's own floor-summary checks.

## 2. Eligibility (exporter)

39 lineages, all v0.111.0, 787 segments (combat 177, room 306, map 304), 4,288 decisions.

| | before (exporter at `ebc055f`) | after (this branch) |
| --- | --- | --- |
| eligible segments | combat 6 | combat 117, room 249, map 304 |
| eligible decisions | 81 | 2,806 |
| save anchors in bundles | 521 indexed, 0 bound (all `act/floor = null`, kind `room_entry`) | 521 indexed (room_entry 342, combat_exit 139, ancient_exit 40); bound: 176 combat + 166 room segments from room_entry, 139 post-combat rooms from combat_exit |
| `mcr_initial` anchors | 34 | 34 |

Skip reasons before: `anchor_missing` 718, `recorder_gap:card_reward_offer` 145, `content_not_ported:event` 45,
`pre_state_hand_unsettled` 36, `anchor_unreadable:mcr_decoder_missing` 31, `relic_not_whitelisted` 30,
`recorder_gap:choice_identity` 29, `recorder_gap:deck_order` 12, `no_decisions` 8, `relic_excluded` 5,
`decision_unresolved` 4, `hand_index_ambiguous` 4, `unresolved_pile_name` 3, `deck_upgrade_mismatch` 2,
`shop_purchase_ambiguous` 2, `neow_gain_count_mismatch` 1, `neow_relic_unmodelled` 1, `unsupported_action` 1.

Skip reasons after: `content_not_ported:event` 45, `pre_state_hand_unsettled` 36, `recorder_gap:choice_identity` 29,
`no_decisions` 8, `decision_unresolved` 4, `hand_index_ambiguous` 4, `recorder_gap:card_reward_offer` 3,
`unresolved_pile_name` 3, `anchor_missing` 1, `unsupported_action` 1. Run and map segments no longer carry
`anchor_missing` from the exporter: the harness chains them or skips them at run time.

Exporter fixes (`sts2rec/sts2rec/diff_export_v2.py`, class `exporter_bug`):

1. Save anchors keyed by the `run_saved` record's own seq (the file name), kind from `anchor_kind`, act/floor from the
   event (the recorder-phase finding 1).
2. `bind_save_anchors`: a `room_entry` save (written after map travel, before `room_entered`) starts the combat or room
   segment of the next room entered in its part; a `combat_exit` save starts the post-combat room segment of its window.
   Bound segments lose the start-reconstruction reasons (`anchor_*`, `recorder_gap:deck_order`, `deck_*`,
   `relic_not_whitelisted`, `relic_excluded`, `neow_*`). A combat start gets `"from": "anchor"` and keeps its v1 fields
   for the record. `copy_save_anchors` byte-copies each save to `<out>/<ref>` (refs are relative to the bundle dir; only
   `.save` files, so the harness's JSON discovery is unchanged). Finding 3 (indexing `native/replays/*.mcr`) is not done:
   the saves cover every combat start.
3. R4: a card pick takes its card id from `reward_taken.offer.cards[index]` (145 → 3 `recorder_gap:card_reward_offer`).
4. A card pick whose last snapshot is still the combat room's (`monster/elite/boss`) is a card-reward pick, not an event
   `choose` (GREMLIN_MERC_NORMAL, 2 runs).
5. Twin-priced card purchases (`item_id: null`, two stocked slots at one price) take the slot that is unstocked in the next
   shop snapshot (HYY6UN8817 f6 bought INFERNO, the exporter said DISMANTLE).
6. R5: the option key comes from `text_key` when it is a full option key; the title key can be a relic's
   (`SEA_GLASS.IRONCLAD.title` on OROBAS).

## 3. Gates

### G0 (legacy v1 bundles, `sim-source-recreation-9392df/tmp/handoff/diff-bundles/bundles`)

4/4 bundles agree, 69/69 decisions, outcome 4/4, 0 soft diffs — before and after.

### G3 seed replay (farm, 39 runs)

| | before (`ebc055f`, exporter at `ebc055f`) | after |
| --- | --- | --- |
| runs reaching the recorded end | 21 | 30 |
| decisions agreed / compared | 2,548 / 2,559 | 3,320 / 3,324 |
| floors agreed before the first divergence, sorted | 1,1,2,2,2,2,2,3,3,4,4,4,5,5,5,5,6,6,6,6,6,6,6,7,7,7,7,8,8,8,8,8,9,9,9,10,10,12,17 | 1,2,2,2,2,3,3,4,4,4,5,6,6,6,6,7,7,7,7,8,8,8,8,8,9,9,9,9,10,11,11,11,12,14,14,17,17,17,22 |
| first divergence by class | none 21, port_bug 12, projection_gap 4, truncated 2 | none 30, port_bug 7, truncated 2 |

The one act-2 run (DEFECT WATLF71NUH, floor 22) now replays to its recorded end from the seed.

### G3 with save anchors (new, `--anchors`, `--anchors-every`)

`--anchors` resumes at the next segment with a save anchor after a divergence, so every divergence in a run is seen,
not only the first. `--anchors-every` starts every anchored segment from the game's save (the G2 check of the save
import, design §3).

| mode | segments replayed / agree | decisions agreed / compared | divergences |
| --- | --- | --- | --- |
| `--anchors`, first pass (exporter fixed, sim at `ebc055f` + importer id fix) | 769 / 739 | 3,426 / 3,444 | 30 (port_bug 20, projection_gap 9, harness_bug 1) |
| `--anchors`, after | 785 / 778 | 3,578 / 3,582 | 7 (all port_bug label; classes in §4) |
| `--anchors-every`, after | 782 / 774 | 3,601 / 3,603 | 8 (4 port_bug label, 4 out_of_scope) |

`--anchors-every` by start: seed 39/39 segments (93/93 decisions), room_entry save 303 segments / 300 agree
(3,203/3,205), combat_exit save 139 / 135 agree (197/197; the 4 others are out_of_scope: `pre_finished_room
extra_rewards are not supported` on SFY7CMFJHB), chained 301 / 300 (108/108).

Save check (`--save-check`, 437 chained segments that carry a save anchor): the chained sim and the same point loaded
from the game's save are both described with `RunExporter.Describe`. First pass: residual differences in the act
encounter lists (`acts[].normal` 16, `acts[].elite` 10 segments), the player grab bags (`relic_grab_bag.Uncommon` 26,
`.Common` 16), the shared Event bag (5), relic `floor_added_to_deck` (570 lines). After: 258 segments equal except
`unlock_state` (the sim runs `UnlockProfile.All`; the farm profile is a real one) and `map_point_history` (the sim's
history has no gold/player stats, known R1 gap); 179 not exportable (inside a combat or with a pending prompt);
0 other differences.

### G1/G2 with the harness-v2 runner (`--v2`)

| | before | after |
| --- | --- | --- |
| segments agree / hard divergence / skipped | 27 / 0 / 760 | 126 / 288 / 373 |
| decisions agreed / compared | 432 / 432 | 2,664 / 2,919 (combat 2,664 / 2,673) |
| combat outcome agreed | 27 / 27 | 126 / 159 |

Combat (177): 126 agree, 9 exporter_ineligible, 42 not agreeing. 33 of them are `final_hp` (the recorder writes the
combat's HP before end-of-combat heals, recorder_gap T7 of W4; G3 drops it), 8 `select.screen_type` (schema), 1
`turn.phase`. Room (306): 0 agree, 246 diverge at decision 0 on schema only (`screen` 131, `player.relics` objects 115):
`V2Runner` compares the exporter's run projection without the `G3Schema` adapter (W4 open item "unify G3's
run-projection schema with V2Runner's", not done here). Map (304): skipped `anchor_missing` (they chain only after an
agreeing room segment). The run-level G2 numbers are therefore the `g3 --anchors-every` ones above. Ratchet:
`sim/specs/diff-baseline-e3-farm.json` written from this run (787 segments, 2,664 agreed decisions); a rerun passes
(0 violations).

### Human corpus check (W4.1 bundles `sim-source-recreation-9392df/tmp/diff-v2-w41i`, G3, after)

1,921 / 1,924 decisions; floors per run G0G2 13, NBND 29, DSB3 35, P2UZ 13, Q3X6 2, GM28 27, K4FB 17 (end), EC03 11,
PLRU 11, G1YF 7 (truncated), old-prefix 0 — no run below its W4.1 integrate value (13, 11, 22, 13, 2, 27, 17, 11, 11,
7, 0). W4.1 reference: 1,532 / 1,537.

## 4. Divergences and triage (§6.4)

The harness labels state mismatches `port_bug`; the class column is the manual triage.

### Fixed (first `--anchors` pass: 30 divergences, plus every save-loaded combat)

| finding | where | class | fix |
| --- | --- | --- | --- |
| enemy combat ids +1 in every save-loaded combat (168 of 168 in the first `--v2` run) | all | port_bug | `RunImporter.ImportedPlayerCombatId` 1 → 0: the game attaches the player first to a fresh `CombatState` (Combat/CombatState.cs:703-706, :726-731); monster Rng seeds add the combat id (:721) |
| SEWER_CLAM_NORMAL vs SEAPUNK_NORMAL at f6; act encounter lists differ (save check) | LJGLSHFUAU | port_bug | 6 weak encounters had no `Tags` (CORPSE_SLUGS, FUZZY_WURM_CRAWLER, NIBBITS, SEAPUNK, SHRINKER_BEETLE, SLIMES; Models.Encounters/*Weak.cs); `AddWithoutRepeatingTags` (Models/ActModel.cs:357-369) let SEAPUNK_NORMAL follow SEAPUNK_WEAK |
| FISHING_ROD counter 0/1/2 vs 1/2/0 (3 rooms), later a different upgraded Strike | 545XJP726S | port_bug | `CombatLoop.EndCombatInternal` passed `room: null` to `AfterCombatEnd`/`AfterCombatVictory`; the game passes `(CombatRoom)runState.CurrentRoom` (Combat/CombatManager.cs:1652, :1708, :1781). Also fixes WAR_HAMMER and SWORD_OF_STONE (elite checks) |
| relic grab bags still hold obtained relics (save check: 26 Uncommon, 16 Common, 5 shared Event) | K4P3, B6SS, R9K1, SFY7, … | port_bug | `RelicCmd.Obtain` now removes a non-stackable relic from the player's and the shared bag (Commands/RelicCmd.cs:411-415) and sets `FloorAddedToDeck = TotalFloor` (:422); starting relics get floor 1 (Entities.Players/Player.cs:1140-1152). The `ApproximatedRule` flag for this site is gone |
| AUTOMATION_POWER DisplayAmount (6 combats) | 9AH885L2ES | projection_gap | `PlayerView.TryMappedDisplayAmount` maps it (Models.Powers/AutomationPower.cs:111 `cardsLeft`) |
| shop `on_sale` on a sold card slot (3 runs) | SDR1, WATL, 1VPB | projection_gap | G3 compares the sale flag of stocked slots only, like the price |
| shop slot 0 vs 4 bought | HYY6UN8817 | exporter_bug | §2 fix 5 |
| GREMLIN_MERC_NORMAL card pick read as an event `choose` (2 runs) | BDAF, 1MQX | exporter_bug | §2 fix 4 |
| OROBAS option key `SEA_GLASS.IRONCLAD` | WATLF71NUH f18 | exporter_bug | §2 fix 6 |
| THE_FUTURE_OF_POTIONS discards slot 0 instead of 1 | 1Y2EU3TQKY f7 | harness_bug | the event lists one `POTION` option per potion (one key); `ActionMapper` takes the recorded index when it carries the key |
| JUNGLE_MAZE_ADVENTURE (2), DENSE_VEGETATION, THIS_OR_THAT (2): proceed pre_state shows pre-option HP/gold or the option page | SFY7, 9AH8, BQDC, UUR2, WATL | recorder_gap (R8 snapshot timing) | G3 settles an `event_proceed` pre_state from the next observed player state and drops a page without a proceed option, only when that settled state agrees (counter `lagging_event_proceeds`); the floor summaries confirm the sim's values |

### Open (7 with `--anchors`)

| run / segment | floor, room | class | finding | next step |
| --- | --- | --- | --- | --- |
| VSQP6ESN1F s17 | f7 PUNCH_OFF_EVENT_ENCOUNTER | port_bug | the event starts a combat in the game, the sim stays in `Event` (also G0G2 on the human corpus) | event → combat transition in the run layer |
| UF51XE6ZV1 s25 | f11 FOGMOG_NORMAL | projection_gap | GAMBLING_CHIP start-of-turn hand select: oracle `turn.phase player_not_play`, sim `player_start` | phase name during a start-of-turn prompt |
| UUR25UNBL5 s10 | f5 BRAIN_LEECH | exporter_bug | a dismissed card-reward choice (Index 3 = an alternative) before the real pick is emitted as an event `choose` | read an Index ≥ offer size before a card pick as the alternative |
| 3ZGJFKTD64 s20 | f9 FOGMOG_NORMAL, decision 18 | recorder_gap (candidate) | STRATAGEM_POWER pile select: same cards, other order | check the grid order the recorder lists (cf. the W4.1 hand-select fix) |
| K4P3FWUZYN s29 | f13 SKULKING_COLONY_ELITE | port_bug (chain only) | `hand[4].upgraded` true vs false; agrees from the f13 save | hidden state not in the save description: compare the combat-start draw pile of chained vs loaded sims |
| 545XJP726S s17 | f8 RUBY_RAIDERS_NORMAL | port_bug (chain only) | which Strike is upgraded in the opening hand; agrees from the save | same as K4P3 |
| SDR1TFEZ2U s36 | f15 SLITHERING_STRANGLER_NORMAL | port_bug (chain only) | floor summary `turns_taken` 4 vs 5, combat decisions agree | turn counter after a chained combat |

Also open: 4 combat_exit saves with `extra_rewards` are out_of_scope for the importer; 45 event segments
`content_not_ported:event`; ancient_exit saves are indexed but not bound (the sim resumes pre-finished combat rooms
only).

## 5. Coverage (farm bundles, ids in compared pre_states and actions; agreeing = in a segment that agrees under `--anchors-every`)

| area | touched | in agreeing segments |
| --- | --- | --- |
| cards | 332 | 320 |
| relics | 58 | 58 |
| potions | 35 | 35 |
| powers | 76 | 74 |
| monsters | 51 | 50 |
| encounters | 40 | 39 |
| events | 29 | 29 |

Compared with W4.1 (human corpus): combat gate 117 segments, 1,599/1,607 decisions; G3 1,532/1,537 over 11 runs, 1
run at its end. The farm alone now gives G3 3,320/3,324 over 39 runs with 30 at their recorded end, and 3,601/3,603
when every room starts from its save.

## 6. Pins, tests

- F2 run pins (`SelfConsistencyTests.PinnedRunStreams`) refreshed: step counts unchanged (136/114/160/400), hashes
  change through the relic `FloorAddedToDeck` and the grab-bag removals; Release and Debug agree.
- `RelicPotionTests.Test_RelicPotion_RelicCmdObtainRemoveReplaceMelt`: the approximation flag is no longer recorded; the
  test asserts that and the floor stamp.
- New: `sim/Sts2Sim.Tests/E3TriageTests.cs` (8 tests), `sts2rec/tests/test_diff_export_v2.py` (4 tests; the run-segment
  `anchor_missing` assertion now checks its absence).
- Gates: `dotnet build sim/Sts2Sim.sln` Release and Debug 0 warnings 0 errors; full suite 1,122/1,122 (PENDING 0); run
  soak 10⁴ runs × 5 characters, 687,175 steps (687,034 before: the encounter tags and grab-bag removals change some
  runs), 0 failures, 0 content-gap stops; content soak green; `-- Server Vec Parity EndToEnd SelfConsistency E3`
  109/109; sts2rec 705 passed, 4 skipped.

## 7. Follow-up 2026-11-04 (branch `train/fidelity`): the 7 open divergences closed

Base `ef70ee6`. Each row: the rule, what was wrong, the fix (commit). Farm inputs: the farm re-exported by this
branch's exporter (`export-bundles-v2 --root <farm>/sessions-all`, 39 runs, 787 segments; bundles and save copies kept
under the worktree's git-ignored `tmp/fid/`, because the farm sessions now live on the external drive). Human: the
W4.1 inputs, plus the 10 v0.111.0 human lineages re-exported (no saves: recorder 0.2.0).

| divergence | rule (decompile) | was wrong | fix | class |
| --- | --- | --- | --- | --- |
| SDR1TFEZ2U f15 `turns_taken` 4 vs 5 | Combat/CombatManager.cs:1650 captures TurnNumber when EndCombatInternal starts; a win at the enemy turn's end still runs SwitchSides (:1950-1976, :6550-6553) | `RunLoop.OnCombatVictory` read the live TurnNumber, after that switch | `CombatState.TurnsTakenAtCombatEnd` (`37c6ac3`) | port_bug |
| UF51XE6ZV1 f11 `turn.phase` | the recorder keeps side + `Phase == Play` only (mod/src/StateBuilder/PassiveStateBuilder.Combat.cs:37, :40; diff_bundle.py:574-581); GAMBLING_CHIP prompts in PlayerTurnPhase.Start (:5293) | the projector spelled out `player_start` | phase projected as `player_play` / `player_not_play` / `<side>_turn` (`8ddab4e`) | projection_gap |
| 3ZGJFKTD64 f9 STRATAGEM order | rules: rarity then ModelId (Commands/CardSelectCmd.cs:897); screen: rarity then localized title (Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:412-430, Nodes.Cards/NCardGrid.cs:619) | ordered comparison of a language-dependent order | draw-pile grids compare as a multiset, picks by identity (`fc44296`) | projection_gap |
| VSQP6ESN1F f7 PUNCH_OFF (and human G0G2 f14, GM28 THE_LANTERN_KEY) | EnterCombatWithoutExitingEvent inside the option handler (Models.Events/PunchOff.cs:522-535); SaveEventOptionToHistory logs after the combat's `room_entered` | the FIGHT option fell into the combat window and was dropped | leading `event_option` of such a combat window moved back to the event (`97b2904`) | exporter_bug |
| UUR25UNBL5 f5 BRAIN_LEECH | Index N.. of a card reward = alternatives; Skip = EndSelectionAndDoNotCompleteReward (Rewards/CardReward.cs:604-658, CardRewardAlternative.cs Generate) | the closing Index was an event `choose` | open + `card_reward_skip`, then the completing pick (`6d74951`) | exporter_bug |
| K4P3FWUZYN f13, 545XJP726S f8 (chained only) | deck order: upgrade in place, transform = remove + append (Commands/CardCmd.cs:2017-2024, :2061-2064) | an ambiguous DeckCard pick took the first copy | `deck_index` from the saves around the room (deck alignment), resolved through the prompt option's card instance (`668fbe4`) | recorder_gap (choice identity) |

Also closed: human EC039PTFQWV5 / PLRUV49CRFQW f12 (a backed-out SMITH selection paired with the next HEAL:
SmithRestSiteOption.cs:263-266, RestSiteSynchronizer.cs:640-647; `02b3e1f`); the human combat gate's GM28D28DX794
s26/s54/s65 (relic run-time state: the mcr join carried relic ids only; `b9426db`, then `select.screen_type` /
`can_skip` / `can_cancel` soft like `mode`, `9bd09d2`); G1 `final_hp` read after BURNING_BLOOD's victory heal by the
run driver, and compared for abandoned combats (`dbcd128`).

| gate | before (`ef70ee6`) | after |
| --- | --- | --- |
| G3 farm, from the seed | 30/39 runs at the recorded end, 3,320/3,324 | 36/39 (the other 3 stop at a `decision_unresolved` truncation), 3,522/3,522 |
| G3 farm `--anchors` | 778/785 segments, 3,578/3,582, 7 divergences | 787/787, 3,690/3,690, 0 |
| G3 farm `--anchors-every` (G2 from the saves) | 3,601/3,603 (99.94 %) | 3,676/3,676; only the 4 out_of_scope `extra_rewards` imports remain |
| G3 farm `--save-check` | 258 segments equal except unlock_state / map history | the same, 0 other differences |
| G1/G2 farm (`--v2`, ratchet) | 2,664/2,919 decisions, 126/177 combats agree | 2,810/3,059, 168/177 (outcome 168/168; 9 exporter-ineligible); baseline re-written (`18f2dc9`) |
| human combat gate (G1) | 1,599/1,607 (99.50 %), 8 rows | 1,666/1,671 (99.70 %), 5 rows: 4 v0.107.1 exporter_ineligible (version drift) and Q3X6PF8S37GA s5 |
| human G3 (W4.1 bundles) | 1,921/1,924 | unchanged (old exporter's bundles) |
| human G3 (re-exported) | 1,921/1,924 | 2,348/2,352 (99.83 %); G0G2 floors 13 -> 35, EC03 11 -> 15, PLRU 11 -> 17 |

Still open: human Q3X6PF8S37GA s5 (HIDDEN_DAGGERS+ not yet in the discard pile at the next play: snapshot timing, R8),
GM28D28DX794 s74 (MUMMIFIED_HAND made a different card free), P2UZ19X1GGNQ s31 (opening hand order after a f13 SMITH
whose copy is unknown: no saves in recorder 0.2.0), NBNDT3S61AUM s81 (shop, card-pack rewards with repeated skips),
PLRUV49CRFQW f18 OROBAS (GLASS_EYE not obtained in the sim; GlassEye.cs is an LdMemberToken decompile gap), the
FAKE_MERCHANT stub (G0G2 f36, DSB3 f36), and the 4 `extra_rewards` combat_exit imports (out of scope).
