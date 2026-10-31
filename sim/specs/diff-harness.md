# Diff harness spec — `Sts2Sim.Diff` (sim side of the diff-bundle contract)

Owner: diff-harness agent. Code: `sim/Sts2Sim.Diff/**`. Tests: `sim/Sts2Sim.Tests/DiffTests.cs`
(`Test_Diff_*`). Contract: [`diff-bundle.md`](diff-bundle.md) v1 (this file only records how the
sim side implements it and the points the contract leaves open). Decompile citations use
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped (game v0.111.0);
recorder citations are paths under `mod/src/StateBuilder/`.

```
dotnet run --project sim/Sts2Sim.Diff -c Release -- <bundle-dir-or-file> [--report out.json] [--include-ineligible] [-v]
```

Exit code: 0 = every bundle that ran agrees (skipped bundles do not count), 1 = at least one hard
divergence, sim exception or bundle error, 2 = usage error or no bundle found. A directory is
searched recursively for `*.json` (ordinal path order); a JSON object without a top-level
`bundle_version` is listed under `ignored_files` (e.g. the exporter's reasons index), the report
file itself is never read back as input, and an unparsable `*.json` is an `error`.

## 1. Files

| File | Contents |
| --- | --- |
| `DiffBundle.cs` | bundle model (`DiffBundle`, `BundleDecision`, `BundleAction`, `BundleCard`, `BundleSource`) and `BundleJson` strict reader / writer |
| `Projection.cs` | projection model and `ProjectionJson` reader / writer (contract key names) |
| `JsonReadHelper.cs` | typed field access with JSON paths in errors (`BundleFormatException.Path`), unknown-key collection |
| `SimProjector.cs` | sim `CombatState` → projection (§3) |
| `ProjectionComparer.cs` | field-by-field comparison with hard / soft classes (§4) |
| `RngSetup.cs` | `RunRngSet` per `seeding`, `rng_offsets` (§2) |
| `BundleRunner.cs` | eligibility, replay loop, action mapping, final checks (§5) |
| `DiffReport.cs` | per-version / overall stats and the JSON report (§6) |
| `Program.cs` | CLI |

JSON is `System.Text.Json` (part of the shared framework in net9.0, no NuGet); Core stays free of
it. The Tests project references `Sts2Sim.Diff` for `DiffTests.cs`.

## 2. RNG construction

| Rule | Sim | Source |
| --- | --- | --- |
| `xxh64`: streams of the current constructor, `Seed = XXH64(seed)`, stream `i` = `Seed + XXH64(snake_case(type))` | `new RunRngSet(seed)` | `Runs/RunRngSet.cs:67-85` (rng spec §6.1) |
| `legacy_v18`: stream kernel = `new MegaRandom((uint)((int)seed + HashOld(snake_case(type))))`, `seed = (ulong)HashOld(seedText)` sign-extended, counter 0 | `RngSetup.CreateLegacyV18` → `RngSeeds.LegacyV18Kernel(LegacyV18RunSeed(seed), RunRngSet.StreamName(type), 0)` for all twelve `RunRngType` | `Saves.Migrations.SerializableRuns/SerializableRunV18ToV19.cs:64-66,164-173`; `Helpers/StringHelper.cs:120-134` |
| legacy set identity: `StringSeed = "old" + seed`, `Seed = (uint)HashOld(seed)` (what the game holds after the V18→V19 migration) | `RunRngSetSnapshot { Seed, HashedSeed }` → `RunRngSet.FromSnapshot` | `SerializableRunV18ToV19.cs:83`; `RunRngSet.cs:70-75` |
| a seed starting with `old` is a cross-version continuation; its streams are not derivable | `RngSetup.Create` throws; the runner skips such bundles | contract |
| `rng_offsets` `{stream: k}`: pull the stream `k` times, one kernel step and `counter + 1` per pull | `RngSetup.ApplyOffsets` → `Rng.NextUnsignedLong()` `k` times | `Random/Rng.cs:95-99` |

Stream names in `rng_offsets` are accepted as the `RunRngType` name (`Niche`, `MonsterAi`, ...) or
its snake_case stream name (`niche`, `monster_ai`, ...); anything else, a negative `k`, or a
duplicate key is a bundle format error. Parity is pinned by `Test_Diff_LegacySeedingMatchesLegacyKernel`
(seed `3URCWVGADS`: `up_front` = `MegaRandom(2206941694)`, `shuffle` after 5 pulls, the same
vectors as `Test_Rng_LegacyV18Kernel`) and `Test_Diff_OffsetsPullTheNamedStreamKTimes`.

The combat is created with the run-context overload
`CombatSim.Create(RunRngSet, IReadOnlyList<CardSpec>, encounterId, ascension, currentHp, maxHp, CombatRunContext)`
(`Combat/Loop/CombatSim.cs`; the contract's six-argument form plus the context of
[`rng-encounter-private.md`](rng-encounter-private.md) §2), deck in bundle order, `currentHp`/`maxHp` =
`player.hp`/`player.max_hp`, context = `BundleRunner.RunContext(b)` =
`CombatRunContext.ForAct(source.act ?? 1, source.floor ?? 2, null, null, ParseSeeding(seeding))`. The context seeds the
encounter-private `Rng` (`SLIMES_WEAK` composition, `CORPSE_SLUGS_WEAK` starters) from the run seed, the combat's
`TotalFloor` and the seeding scheme; the six-argument form would always use the default (floor 2, `xxh64`) seed, which is
wrong for every `legacy_v18` bundle of such an encounter (integration fix, 2026-10-30: `1788970367-QC7ZPPWXEK` spawned
`TWIG_SLIME_S` for the recorded `LEAF_SLIME_S` before it, 18/18 decisions agree after). An `act < 1` or `floor < 0` is an
`ArgumentException` (bundle `error`). Pinned by `Test_DiffIntegration_*` (`DiffIntegrationTests.cs`).

## 3. Projection (sim side)

Projection time: before each decision, i.e. at `CombatSim` decision points (opening = after
`Create`). The recorder fields the exporter projects from are listed for each rule.

| Field | Sim value | Game / recorder source |
| --- | --- | --- |
| `turn.player_turn` | `Player.TurnNumber` | recorder `battle.player_turn_number` = `PlayerCombatState.TurnNumber` (`PassiveStateBuilder.Combat.cs:41`) |
| `turn.phase` | `"player_play"` when `CurrentSide == Player` and `Player.Phase == Play`; otherwise `"player_" + snake(Phase)` or `snake(CurrentSide)` | recorder `battle.turn` + `battle.is_play_phase` (`PassiveStateBuilder.Combat.cs:37-40`); only `player_play` occurs at a decision |
| `player.hp/max_hp/block/energy` | `CurrentHp`, `MaxHp`, `Block`, `Energy` | `PassiveStateBuilder.Player.cs:30-41` |
| `player.max_energy` | `EnergyCmd.GetMaxEnergy(cs, player)` | `PlayerCombatState.MaxEnergy` = `Hook.ModifyMaxEnergy(.., MaxEnergy)` (`Entities.Players/PlayerCombatState.cs:136`); recorder `:42` |
| `hand[i].id/upgraded` | `CardInstance.Id`, `IsUpgraded`, hand order | recorder `BuildCardInfo` (`PassiveStateBuilder.Player.cs:176-190`) |
| `hand[i].cost` | `"X"` when `EnergyCost.CostsX`, else `EnergyCost.GetAmountToSpend(cs, card)` (an unplayable `-1` cost shows 0) | recorder `GetCostDisplay` (`PassiveStateBuilder.Player.cs:162-163`) → `CardEnergyCost.GetAmountToSpend` (`Entities.Cards/CardEnergyCost.cs:116-123`, `GetWithModifiers` `:76-`) |
| `hand[i].can_play` | `card.CanPlay(cs, out reason) && reason == None` | recorder `:194-200` → `CardModel.CanPlay` (`Models/CardModel.cs:3330-3360`) |
| `draw_count/discard_count/exhaust_count` | pile counts | recorder `:61-63` |
| `draw_multiset` | draw pile keys sorted ordinal (`id` + `"+"` if upgraded) | recorder sorts the draw pile by rarity then id (`:65-72`); the comparator re-sorts both sides |
| `discard`, `exhaust` | pile keys in pile order | recorder `:73-74` |
| `powers` | `Powers` in list order, only visible ones, `amount` = display amount | recorder `BuildPowersState` (`:254-300`): `IsVisible` filter, `DisplayAmount` |
| power visible | `TargetId` none, the player, or an enemy (a target no longer in combat counts as visible) | `PowerModel.IsVisible` (`Models/PowerModel.cs:161-173`): `Target == null || IsMe(Target) || Target.IsEnemy` then `IsVisibleInternal` (default true, `:173`) |
| power display amount | `Amount` | `PowerModel.DisplayAmount => Amount` (`Models/PowerModel.cs:213`) |
| `enemies` | `Monsters` with `IsEnemy && IsAlive`, sorted by `CombatId` | recorder keeps only `creature.IsAlive` enemies (`PassiveStateBuilder.Combat.cs:44-52`), `combat_id` = `Creature.CombatId` (`:72`) |
| `enemies[].monster_id` | `MonsterState.MonsterId` | recorder `entity_id` = `<MONSTER_ID>_<k>` (`:60-71`); the exporter strips the suffix |
| `intents` | intents of `Ai.NextMove` (none when `NextMove` is unset) | recorder: intents of `monster.NextMove` when it is a `MoveState` (`:81-101`) |
| intent `type` | `IntentType.ToString()` | recorder `intent.IntentType.ToString()` (`:88`) |
| intent `attack_value` | every `AttackIntent` (Attack, DeathBlow): `GetSingleDamage(cs, PlayerCreatures(cs), owner)` | label = single damage: `SingleAttackIntent.GetIntentLabel` (`MonsterMoves.Intents/SingleAttackIntent.cs:29-35`), `MultiAttackIntent.GetIntentLabel` (`MultiAttackIntent.cs:35-42`), `DeathBlowIntent : SingleAttackIntent` (`DeathBlowIntent.cs:10`); damage = `Hook.ModifyDamage` vs the local player (`AttackIntent.cs:92-101`) |
| intent `attack_times` | `GetRepeats` (1 for a single attack) | `Repeats` in the multi label; a single label has no `×` |
| intent extra `card_count` (soft) | `StatusIntent.CardCount` | the only non-attack intent with a non-empty label: `StatusIntent.GetIntentLabel` (`StatusIntent.cs:30-35`); every other intent's label is `FORMAT_EMPTY` (`AbstractIntent.cs:45-48`) |

Projection gaps fail loudly: a sim power whose game class overrides `DisplayAmount`
(`AUTOMATION_POWER`, `CACOPHONY_POWER`, `FERAL_POWER`, `HARDENED_SHELL_POWER`, `MONOLOGUE_POWER`,
`ORBIT_POWER`, `PALE_BLUE_DOT_POWER`, `PANACHE_POWER`, `SLOTH_POWER`, `SLOW_POWER`, `TAG_TEAM_POWER`,
`TENDER_POWER`, `WITHERING_PRESENCE_POWER`) or `IsVisibleInternal` (`AMBERGRIS_POWER`) throws
`ProjectionGapException` (bundle status `error`) until its rule is added to `SimProjector`
(the list is every override in `Models.Powers/*.cs`). The projector only reads state:
`Test_Diff_ProjectionIsPureAndTracksModifiers` checks the state hash before and after.

## 4. Comparison

`ProjectionComparer.Compare(oracle, sim)` returns `FieldDiff(path, oracle, sim, severity, note)` in
a fixed order; the first `Hard` one is the reported divergence, all hard diffs of that decision
are kept (`all_hard`).

| Order | Path | Class |
| --- | --- | --- |
| 1 | `turn.player_turn`, `turn.phase` | hard |
| 2 | `player.hp`, `max_hp`, `block`, `energy`, `max_energy` | hard |
| 3 | `player.hand.length`, then `player.hand[i].id/upgraded/cost/can_play` for the common prefix | hard |
| 4 | `player.draw_count`, `discard_count`, `exhaust_count` | hard |
| 5 | `player.powers[ID].amount`, `player.powers[ID]` (present on one side; note says which), `[ID#k]` for the k-th instance of an instanced power | hard |
| 6 | `enemies.combat_ids` (sorted id lists differ), then per common id `enemies[combat_id=N].monster_id/hp/max_hp/block/powers…/intents.length/intents[k].type/attack_value/attack_times` | hard |
| — | `…powers.order` (same powers, different order) | soft |
| — | `…intents[k].<extra>` (any intent key besides `type`/`attack_*`, compared by JSON value; one-sided = soft) | soft |
| 7 | `player.draw_multiset` (multiset), `player.discard`, `player.exhaust` (ordered) | soft |

Reader normalizations: an intent with `attack_value` and no `attack_times` reads as one hit (a
single-damage label carries no repeat count); absent `draw_multiset`/`discard`/`exhaust` are not
compared; unknown keys are ignored and listed in the report (`unrecognized_bundle_keys`, as
index-free path patterns). Two exporter keys are known, not unknown (`diff-bundle-exporter.md` §2; not yet in the
contract text): `decisions[].oracle` (recorder provenance: `action_seq`, `state_seq`, `settled`, `pending_events`,
`combat_card_index`, `view_state_seq`, `rolled_forward` / `unobserved_reason`, `snapshot_pre_state`; its keys are
not validated) is kept verbatim
(`BundleDecision.OracleJson`), written back by `BundleJson.Write` and copied into divergence repros, never compared;
top-level `ineligible_reasons` (string list, only in the exporter's `ineligible/` bundles) is the exporter's verdict
(§5). `null` reads as absent. `FieldDiff.PathPattern` replaces list indices with `[*]` and
`combat_id=N` with `combat_id=*` for the soft summary.

## 5. Replay (`BundleRunner.Run`)

1. **Skip** (status `skipped`, not a failure) when: `character` is not `IRONCLAD` (the contract
   `Create` builds an Ironclad player); the seed has the `old` prefix; the encounter or a deck card
   is not in the sim registries; a relic is not on the contract whitelist (`diff-bundle.md`
   exporter section, embedded as `BundleRunner.RelicWhitelist`, equal to the exporter's
   `RELIC_WHITELIST`, 27 ids). After that sim gate: the bundle carries a non-empty `ineligible_reasons`
   (the exporter's verdict, which also covers what the bundle body cannot show: `deck_mismatch`,
   `neow_summary_missing`, `potion_used`, `unsupported_action`, `pre_state_hand_unsettled`, ...), unless the
   run passes `--include-ineligible` (`BundleRunner.Run(b, includeExporterIneligible: true)`); the flag never
   bypasses the sim gate. Without the verdict an exporter `ineligible/` bundle replays and reports expected hard
   divergences (seen on `1784043137-CPSVMWJPUL`, Neow room not recorded: hand `BASH` vs `DEFEND_IRONCLAD` at
   decision 0).
2. **Bundle consistency** (status `error`): for every observed `play_card`, `pre_state.hand[hand_index]`
   must exist and match `card_id`/`upgraded`.
3. `RngSetup.CreateForBundle` + `CombatSim.Create(...)`. An `ArgumentException` from setup is a
   bundle `error` (bad inputs); any other exception is `sim_exception`.
4. For each decision `i`: if the sim is already terminal → hard divergence `terminal`
   (`sim.outcome`). Else project; if `pre_state` is present compare it (any hard diff → divergence
   `state`, stop; soft diffs are collected); map the action; `Step`; count the decision as agreed
   only when it was compared.
5. **Action mapping**: `end_turn` → `GameAction.EndTurn`. `play_card` → `hand_index` indexes the sim
   hand; `card_id`/`upgraded` (optional) must match the sim card; a card with `TargetType.AnyEnemy`
   needs `target_combat_id`, mapped to the index of the sim monster with that `CombatId`; other cards
   take no target (a stray oracle target is ignored). Then `LegalActionGenerator.TryResolve` must
   accept it. Any failure → divergence `action` (`action.hand_index`, `action.card_id`,
   `action.upgraded`, `action.target_combat_id`, `action.legal`).
6. **Final check** after the last decision: `outcome` (`win` = `Victory`, `loss` = `Defeat`,
   `abandoned` = still in progress) and `final_hp` (when present) against the sim player's HP →
   divergence `final` at decision index `decisions.Count`. Decision agreement is counted
   independently of the final check.

Statuses: `agree`, `hard_divergence`, `skipped`, `error`, `sim_exception`. A `ProjectionGapException`
is an `error`.

**Unobserved decisions (contract extension).** `pre_state: null` (or absent) marks a decision whose
pre-action snapshot is not a settled state: the action is replayed but nothing is compared, and it
counts as `decisions_unobserved`. Needed because the game queues card plays: when the player
clicks the next card while the previous one is still resolving, `actions[].state_seq` points at a
snapshot taken inside the previous play (between its `card_play_started` and `card_play_finished`
events) — energy spent and the card out of the hand, but damage, powers and the discard move not
yet applied. A bundle that keeps such snapshots shows false hard divergences.

## 6. Report (`--report out.json`)

Top level: `report_version` (1), `contract`, `inputs`, `options` (`include_ineligible`), `summary` and
`by_version.<game_version>`
(`bundles`, per-status counts, `decisions_total/compared/agreed/unobserved`,
`decision_agreement_rate` = agreed / compared, `outcome_checked/agreed`, `soft_diffs`; skipped and
error bundles stay out of the decision counts), `divergences`, `sim_exceptions`, `soft_summary`
(per path pattern: count, bundles, up to 3 examples), `skipped`, `errors`, `ignored_files`,
`unrecognized_bundle_keys`, `bundles` (one line per bundle).

Each `divergences[]` entry is a minimal repro that needs no bundle file: `bundle`, `source`,
`game_version`, `kind` (`state`/`action`/`terminal`/`final`), `decision_index`, `field_path`,
`oracle`, `sim`, `note`, `all_hard`, the oracle `action`, `setup` (seeding, seed, rng_offsets,
character, ascension, encounter, player HP, deck, relics), `actions_prefix` (the actions replayed
before the divergent decision), `oracle_provenance` (the divergent decision's exporter `oracle` object, when present:
the recorder `state_seq` / `action_seq` to open), `oracle_projection` and `sim_projection`. `sim_exceptions[]` carries
the same `setup`/`actions_prefix` plus the exception text.

## 7. Verification

- 19 `Test_Diff_*` tests, no corpus: comparator classes for every field, power matching, pile
  rules, projection of the `NIBBITS_WEAK` opening (A0 and A10) and of applied powers, offsets,
  legacy seeding vectors, bundle JSON round trip and validation paths, self-replay of sim-recorded
  bundles (xxh64 and legacy, with offsets) through JSON, injected hard / soft / action / terminal /
  final divergences, unobserved decisions, eligibility skips, report aggregation, the CLI on a
  temporary directory; the exporter keys (`oracle` recognized, round-tripped and copied into the repro as
  `oracle_provenance`; `ineligible_reasons` skipped by default, replayed with `--include-ineligible`, never past the
  sim gate, CLI exit code and `options`). 3 `Test_DiffIntegration_*` tests pin the run context (§2).
- 3 `Test_RecordedXxh64Openings_*` tests (`RecordedXxh64OpeningsTests.cs`) pin five recorded v0.110.1 / v0.111.0
  first combats (Ironclad, Necrobinder, Silent; `SLUDGE_SPINNER_WEAK`, `SLIMES_WEAK`, `CORPSE_SLUGS_WEAK`,
  `FUZZY_WURM_CRAWLER_WEAK`, `TOADPOLES_WEAK`; `Niche` offsets 0 / 2 / 7): opening hand = first draws of
  `UnstableShuffle(deck order)` on the XXH64 `Shuffle` stream, enemy projection (ids, HP, block, powers, intents) from
  the XXH64 `Niche` / `MonsterAi` / encounter streams; the legacy kernels deal none of the five hands.
- Exporter bundles (2026-10-30, after the integration fix, `sts2rec export-bundles` default `settled` mode): 4
  eligible bundles, all v0.107.1 Ironclad A10 `legacy_v18` floor 2 (`1786892446-VW67B447SR` `NIBBITS_WEAK`,
  `1786880759-X20BJH1S4R` and `1786893107-STFR70JJFB` `SEAPUNK_WEAK`, `1788970367-QC7ZPPWXEK` `SLIMES_WEAK`): 4/4
  agree, 69/69 decisions compared and agreed (24 rolled-forward pre_states), outcome and `final_hp` 4/4 (45 / 51 / 42 /
  60), 0 soft diffs, 0 unrecognized keys, exit 0. `--pre-state unobserved` export: 45/45 compared agree, outcomes 4/4.
  Whole export with `--include-ineligible` (142 attempts): default run 4 agree + 138 skipped (135 character, 2
  unported cards, 1 exporter verdict), exit 0; with `--include-ineligible` the one exporter-ineligible Ironclad bundle
  (`1784043137-CPSVMWJPUL`, `neow_summary_missing` + `deck_mismatch`) diverges at decision 0 as expected.
  Re-run after the exporter review fixes (`review-fixes-2026-10-30.md`: choice filter, combat end at the map view,
  turn-end check, instance-view hand resolution): the same four bundles, byte-identical; 4/4 agree, 69/69, outcome and
  `final_hp` 4/4, 0 soft diffs, 0 unrecognized keys, exit 0; `--pre-state unobserved` 45/45 + 24 unobserved, 4/4;
  `--pre-state snapshot` still stops all four at their first mid-resolution snapshot (`discard_count`), as above.
- Cross-character opening check (2026-10-30, scratch program on the integrated tree, not a test): for every exported
  first combat with an opening `pre_state` (140 of 142, all eight ported encounters, five characters, A0-A10, both
  seedings), the enemy projection of `CombatSim.Create` (bundle seeding, seed, `rng_offsets`, run context; Ironclad
  starter deck, which only feeds `Shuffle`) equals the recorded one in 139/140 (124 `legacy_v18`, 16 `xxh64`), and the
  recorded opening hand equals the first draws of `UnstableShuffle(bundle deck order)` in 136/140. Every miss is
  explained: `CPSVMWJPUL` (Neow not recorded: true `Niche` offset 1, deck missing `SETUP_STRIKE`) misses both; three
  hands lead with an Innate card moved to the top (`BOOT_SEQUENCE`, `MIND_BLAST`, `WRITHE`:
  `Models.Cards/BootSequence.cs:87`, `MindBlast.cs:96`, `Writhe.cs:12`), the rest of each hand matching. So the XXH64 `Niche`, `MonsterAi`, encounter and `Shuffle` streams and the exporter's deck order are
  confirmed on real data; the XXH64 path still has no full decision replay (no eligible v0.110.1+ bundle).
- Real-data smoke run (2026-10-30, prototype bundle built from the read-only corpus with a
  throwaway script, not the exporter): session `1786892446-VW67B447SR` (v0.107.1, Ironclad A10,
  `NIBBITS_WEAK` floor 2, relics `BURNING_BLOOD` + `NEOWS_TALISMAN`, deck = starter with the last
  Strike and last Defend upgraded + Ascender's Bane, `legacy_v18`, no offsets): 9 decisions, the 5
  settled ones agree on every hard and soft field, the 4 mid-resolution ones replayed unobserved,
  outcome `win` and `final_hp` 51 agree. With the 4 mid-resolution snapshots kept as `pre_state`
  the run stops at decision 1 (`player.discard_count` 0 vs 1, enemy HP 46 vs 38, Vulnerable absent):
  the snapshot was taken inside the previous Bash play.

## 8. Open points

- Resolved 2026-10-30: the run context (§2). The harness passes `BundleRunner.RunContext(b)`; bundles still carry no
  map coordinates, which only the per-monster `Rng` reads (no ported rule reads it,
  `rng-encounter-private.md` §4).
- Resolved 2026-10-30: the status-card count extra is `card_count` on both sides (`diff-bundle-exporter.md` §4); the
  real-data runs show no soft diff on it (`SLIMES_WEAK` `StatusCard` intents).
- Phase strings other than `player_play` are not pinned by the contract; only `player_play` can
  occur at a decision.
- Contract text (`diff-bundle.md`, shared, not edited here) still names the six-argument `Create` and does not list
  `pre_state: null` (§5), `decisions[].oracle` or `ineligible_reasons` (§4-§5); both sides implement them as
  additive optional keys.
