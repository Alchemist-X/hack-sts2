# W4 replay-combats — combat-level diff gate on real data (2026-11-01)

Owner: W4 replay-combats. Design: `docs/sim-port/design-diff-gate.md` §6 (G1 levels, §6.3 thresholds, §6.4 classes).
Tools: `sts2rec export-bundles` (v1 G0), `sts2rec export-bundles-v2` (run bundles), `Sts2Sim.Diff replay-convert`
(mcr), `Sts2Sim.Diff --v2` (harness v2). Decompile citations drop the `MegaCrit.Sts2.Core.` prefix (v0.111.0 tree);
v0.107.1 = `artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/<path>`.

## 1. Inputs (regenerated with the merged tools at `c145de0`)

| source | command | result |
| --- | --- | --- |
| v1 G0 | `uv run sts2rec export-bundles <scratch>/v1 --sim-core ../sim/Sts2Sim.Core --include-ineligible` | 135 lineages, 144 first-combat attempts, 6 eligible (v0.107.1 4, v0.111.0 2); reasons `relic_not_whitelisted` 137, `unsupported_action` 25, `relic_excluded` 19, `pre_state_hand_unsettled` 9, `neow_relic_unmodelled` 3, `potion_used` 3, `neow_card_enchanted` 2, 1 each `deck_mismatch`/`neow_summary_missing`/`no_decisions`/`turn_end_unrecorded` |
| v2 runs | `uv run sts2rec export-bundles-v2 tmp/diff-v2 --sim-core ../sim/Sts2Sim.Core` | 19 runs (v0.110.1 8, v0.111.0 11); segments combat 427, room 641, map 596; eligible combat 2 (31 decisions) |
| mcr | `dotnet run -c Release --project sim/Sts2Sim.Diff -- replay-convert <sessions> tmp/diff-v2/mcr` | 723 replays (v0.107.1 611, v0.110.1 41, v0.111.0 71), 723 decoded, 0 fully eligible, 6 pass the sim gate |

Harness input = one directory with the three sets (`g0/`, `g0-ineligible/`, `v2/` incl. `v2/mcr/`): 2531 segments,
1294 of them combat (v0.107.1 737, v0.110.1 196, v0.111.0 361). Reports: `tmp/diff-v2/report-combats.json` (gate run),
`report-combats-include-ineligible.json` (diagnostic replay), `report-combats-widened.json` and
`report-combats-mcrjoin.json` (§4 experiments, uncommitted harness patch in `tmp/diff-v2/w4-replay-combats/`).

## 2. Gate run (committed harness, `--v2`, no `--include-ineligible`)

All compared decisions agree: **131/131 (100 %)**, outcome 8/8, 0 soft diffs, 0 errors, exit 0. Compared segments:
v0.107.1 4 (G0, 69 decisions), v0.111.0 4 (the two `G0G2JMFEYHRS` first combats, once as v1 bundles and once as v2
run segments; 31 distinct decisions). `--include-ineligible` adds 8 replayed ineligible combats, all first-diverging as
expected for their exporter reason (7 mcr v0.107.1 `hand_index_unresolved`, 1 v2 later combat without RNG state).

Combat segments by version and reason (gate run):

| version | agree | character not Ironclad | relic not on whitelist | encounter not ported | rules_version ≠ game_version | exporter ineligible | unreadable ineligible segment |
| --- | --- | --- | --- | --- | --- | --- | --- |
| v0.107.1 (737) | 4 | 709 | 14 | 3 | — | 7 | — |
| v0.110.1 (196) | 0 | 48 | — | — | 148 | — | — |
| v0.111.0 (361) | 4 | 249 | 24 | — | — | 1 | 83 (79 `deck: null`, 4 `action: null`) |

Coverage (gate run, oracle-touched / agreed / §6.3 minimum met): card 12/12/10, encounter 4/4/2, monster 6/6/2,
power 4/4/4, relic 8/8/4; unobserved rate 0.

## 3. Blockers fixed on the way (harness reader/gate, cross-edits)

- **Unreadable bundles** (`exporter_bug` at bundle level, 12 of 19 v2 runs, incl. the only eligible `G0G2JMFEYHRS`):
  the exporter writes `deck: null` (`recorder_gap:deck_order`) and `action: null` (`decision_unresolved`) on
  ineligible segments and omits `seeding` on `old`-prefix runs; the strict v2 reader failed the whole bundle. Now an
  ineligible segment that does not parse is skipped per segment; eligible malformed segments still fail the bundle.
- **Run gate**: `game_mode: custom` with an empty, exporter-verified modifier list was `out_of_scope`, so the two
  eligible v2 segments never ran. Now in scope (v1 rule). Regression tests: `ReplayCombatsW4Tests`.

## 4. Widened replays (experiments, uncommitted harness patch)

The committed gate compares almost nothing beyond G0 because `BundleRunner` builds only an Ironclad player without
relics, and later combats carry no RNG state. Two experiments measured what the kernel does on the rest:

- **A (widened gate)**: `STS2SIM_WIDE_GATE=1` builds the bundle's character (`CharacterRegistry`, max energy, orb slots)
  with its real relics through `CombatLoop.SetUpCombat(RunState, PlayerState, ...)` + `Hook.AfterRoomEntered` + the new
  `CombatSim.FromSetUp`; `select.mode/can_confirm/index` soft.
- **B (mcr join)**: each v2 combat segment with an `mcr_initial` anchor gets the decoded replay's run-RNG counters, deck
  (save order) and relics (`mcrjoin.py`; 112 anchored, 59 joined, 53 rejected on encounter/relic/hp/deck mismatch —
  v0.110.1 relic ids are shifted, `?` rooms resolve to `NONE`); hand-select candidates compared as a multiset.

| slice | segments | agree | decisions compared / agreed |
| --- | --- | --- | --- |
| A, v0.111.0 first combats, trusted RNG, no exporter reason except the relic whitelist | 13 | 11 | 210 / 210 |
| A, v0.110.1 first combats (same filter) | 6 | 6 | 85 / 85 |
| A, v0.107.1 first combats (same filter) | 99 | 91 | 1277 / 1273 |
| B, v0.111.0 later combats from the mcr join | 59 | 16 | 534 / 502 (94.0 %) |

Coverage in B (touched / agreed / minimum met): card 100/86/33, encounter 38/29/5, monster 54/43/8, potion 24/18/4,
power 59/50/15, relic 83/57/9; unobserved rate 4.6 %.

## 5. Divergence table (every first divergence on a trusted start)

No `port_bug` was confirmed: every first divergence traced to the oracle, the experiment's setup or a version change.
The kernel needed no fix. Rows give the segment, the first divergent decision, the class and the cause.

| segment | decision | class | cause | fix / ticket |
| --- | --- | --- | --- | --- |
| G0 v0.107.1 ×4 (`X20BJH1S4R`, `VW67B447SR`, `STFR70JJFB`, `QC7ZPPWXEK`), v0.111.0 `G0G2JMFEYHRS` combat-a2 / s3, B s24 | final | `projection_gap` (A/B only) | BURNING_BLOOD `AfterCombatVictory` heals 6 (`Models.Relics/BurningBlood.cs:52`); the bundle `final_hp` is the last in-combat snapshot, before post-victory heals (`sim/specs/diff-bundle.md` `final_hp`) | harness: read final HP before `AfterCombatVictory` once relics are built (committed path builds none) |
| 23 Regent v0.107.1 first combats | 0–18 | `harness_bug` (A, fixed in A) | DIVINE_RIGHT gains stars in `AfterRoomEntered(CombatRoom)` (`Models.Relics/DivineRight.cs:53,90`); a standalone combat never enters a room, so FALLING_STAR (`CanonicalStarCost` 2, `Models.Cards/FallingStar.cs:145`) was unplayable | experiment fires `Hook.AfterRoomEntered`; the real fix is a room-entered combat start in the harness |
| 7 Silent v0.111.0 first combats (`NBNDT3S61AUM` s2, `P2UZ19X1GGNQ` s2, `Q3X6PF8S37GA` s2, `GM28D28DX794` s2, `K4FB4086NTRE` s2, `PLRUV49CRFQW` s3, `G1YFC196B2H2` s2) | 2–16 | `projection_gap` | oracle `select` carries `mode`, `can_confirm`, `candidates[].index`; the sim projection has none, so they compare hard | projector/comparer ticket (soft or projected); with them soft all 7 agree to the end |
| `1785601822-7V872RS6LB` combat-a1 (Necrobinder, v0.107.1) | 5 | `version_drift` | SETUP_STRIKE Strength 2 in v0.107.1 (`Models/Cards/SetupStrike.cs:23`), 3 in v0.111.0 (`Models.Cards/SetupStrike.cs:133`); sim = v0.111.0 | exclude (legacy) |
| `1785942864-4EM2EE982H` combat-a1, `1788184560-QLECM4RQR3` combat-a1 (v0.107.1) | 9, 3 | `version_drift` | after an ally CORPSE_SLUG dies, v0.107.1 keeps the Debuff intent, v0.111.0 shows Stun (recorded v0.111.0 `GM28D28DX794` s2 decision 17); same `RavenousPower.AfterDeath` source order in both trees (`Models.Powers/RavenousPower.cs:222`), so the difference is in the v0.107.1 stun/intent path; sim = v0.111.0 | exclude (legacy) |
| `1787886805-DXLU6W7LCA` combat-a1 (Defect, v0.107.1) | 7 | `recorder_gap` | rolled-forward pre_state taken mid SUPERCRITICAL (`TriggerAnim` delay before `GainEnergy(4)`, `Models.Cards/Supercritical.cs:60,76`): energy 1, next decision 4 = 5 − 1 | exporter roll-forward: wait past `card_play_finished` |
| B `PLRUV49CRFQW` s65, s90, s94 | 7, 3, 13 | `projection_gap` | HIDDEN_DAGGERS / HAND_TRICK hand selects: the recorder lists candidates in UI order; the game's prompt is hand order filtered (`Commands/CardSelectCmd.cs:3687`), as in the sim | exporter: candidates in hand order (or harness: multiset) and pick by identity |
| B `PLRUV49CRFQW` s90 | 18 | `harness_bug` | THE_HUNT acts only when `CurrentRoom is CombatRoom` (`Models.Cards/TheHunt.cs:85`); a standalone combat has no room stack, so no damage/power | harness: start combats inside a room |
| B `K4FB4086NTRE` s13, s40, `G1YFC196B2H2` s29, `EC039PTFQWV5` s76, `NBNDT3S61AUM` s39/s76/s109, `G0G2JMFEYHRS` s63/s84/s102 and others | 0–27 | `exporter_ineligible` | mcr converter reason `enchanted_card_in_deck`: the v1 deck shape has no enchantments (e.g. Glam on DAGGER_SPRAY doubled its damage, 16 vs 8 per enemy) | mcr/v2 start must carry enchantments |
| B `GM28D28DX794` s26/s54/s65/s74/s39/s101 | 0 | `harness_bug` (B, cause not verified per relic) | start energy +2, an extra DAZED and draw count +2 at decision 0: start-of-combat relic state (obtain-time effects, saved properties) that a bundle-built player lacks | build the start from the decoded `SerializableRun` (`RunImporter`), not from bundle fields |
| B `NBNDT3S61AUM` s140 | 25 | `anchor_error` | MAD_SCIENCE `TinkerTime*` are saved properties (`Models.Cards/MadScience.cs`); the joined deck has none, so `OnPlay` hits the `default` throw | same as above |
| B `Q3X6PF8S37GA` s99/s109/s118 | 0 | `exporter_ineligible` | potion slot 0: start says POTION_SHAPED_ROCK, the decision-0 snapshot FYSH_OIL (segments also carry `card_upgrade_not_ported:HIDDEN_DAGGERS`); not investigated | exporter: start potions from the decision-0 snapshot |
| B `P2UZ19X1GGNQ` ×3, `GM28D28DX794` s142 | −1 | `harness_bug` (B) | `AddPotionInternal` index out of range: the player's max potion slots are not set from the start (`max_potion_slots`) | set slots before adding potions |
| HARDENED_SHELL / SLOW / TENDER powers (A 15 + B 3 segments) | 0–5 | `projection_gap` | `DisplayAmount` override without a projection rule | projector ticket |
| remaining A later combats (no RNG state) | 0 | `anchor_missing` | later combats have `rng_offsets: {}` without an anchor | R1 saves or the mcr join |

## 6. What blocks the rest

1. **Harness character gate** (largest): 1006 of 1294 combat segments stop at "the sim builds an Ironclad player"
   although every character, the starter relics and 594 relics are ported. Experiment A shows that a character-aware
   start agrees on all trusted v0.111.0 / v0.110.1 first combats (295/295 decisions) and on 1273/1277 v0.107.1 ones.
   Needed in `BundleRunner`: character from `CharacterRegistry`, real relics, `AfterRoomEntered` inside a combat room,
   final HP before post-victory heals, max potion slots.
2. **Later combats have no RNG state**: 152 retries and every non-first combat. The decoded `replay.mcr` holds the
   full run RNG at map-point entry; joining it to the v2 segment (experiment B) runs 59 later combats at 94 % decision
   agreement with zero port bugs. The proper G1 start is `McrReader` → `RunImporter.ImportState` → enter the room
   (`CombatRoom.StartCombat`), so obtain-time relic state, saved properties, enchantments, potion slots and the room
   stack come from the save (fixes every B `harness_bug` and `anchor_error` row).
3. **Projection/recorder**: hand-select candidate order (UI order), `select.mode/can_confirm/index`, three
   `DisplayAmount` powers, and the roll-forward taken mid-resolution (SUPERCRITICAL).
4. **Exporter**: 83 ineligible v0.111.0 combat segments that do not parse (`deck: null`, `action: null`, now skipped per
   segment); mcr-joined decks lack enchantments; `?` rooms resolve to `NONE` in the mcr converter.
5. **Versions**: v0.110.1 run segments are out of scope (rules_version ≠ game_version) and their mcr relic table is
   shifted. v0.107.1 diverges only where v0.111.0 changed a rule (2 classes above).
