# Version delta demo: v0.107.1 -> v0.111.0 (2026-11-01)

Output of `scripts/version_delta.py` (trimmed to the summary tables; the full report has one row per changed file,
the spec rules to re-read and a normalised diff per file). Tooling and playbook:
[`docs/version-migration-playbook.md`](version-migration-playbook.md).

```bash
python3 scripts/version_delta.py \
  /Users/Aincrad/dev-proj/hack-sts2/artifacts/knowledge/decompiled-v0.107.1 \
  /Users/Aincrad/dev-proj/hack-sts2/artifacts/knowledge/decompiled-9cb4f1ad \
  --old-version 0.107.1 --new-version 0.111.0 --out tmp/version-delta/v107-9cb
# version-delta 0.107.1 -> 0.111.0: content 546, kernel 134, out_of_scope 407; 10.0 s
python3 -m unittest discover -s scripts -p 'test_version_delta.py'   # 19 tests (21 since 2026-11-02)
```

Re-run 2026-11-02 with the kernel map (`sim/specs/kernel-map.json`, see "Kernel map coverage" below):

```bash
python3 scripts/kernel_map.py --check        # 564 kernel files; mapped 374, no_port 190 (0 unmapped)
python3 scripts/version_delta.py <same trees and versions> --out tmp/version-delta/v107-9cb
# version-delta 0.107.1 -> 0.111.0: content 546, kernel 134, out_of_scope 407; 10.8 s
# kernel map: 134 changed kernel files, 95 with sim files (49 via the map only); mapped 91, no_port 43,
#   unmapped 0; map covers 564 kernel files (mapped 374, no_port 190)
python3 -m unittest discover -s scripts -p 'test_*.py'   # 30 tests (version_delta 21, kernel_map 9)
```

Content numbers, bulk edits and the rerun plan are unchanged by the re-run; the kernel table below shows both
runs.

## Inputs

| tree | path | layout | .cs files |
| --- | --- | --- | ---: |
| old v0.107.1 | `artifacts/knowledge/decompiled-v0.107.1` | nested `MegaCrit/sts2/Core/Models/Cards/X.cs`, async/await decompiled | 3,424 |
| new v0.111.0 (macOS build) | `artifacts/knowledge/decompiled-9cb4f1ad` | flattened `MegaCrit.Sts2.Core.Models.Cards/X.cs`, async/await decompiled | 3,537 |
| cite tree (rule truth, Linux v0.111.0) | `output/server-bootstrap/linux-decompiled` | flattened, raw `IAsyncStateMachine` structs | 3,537 |

The macOS v0.111.0 decompile is used as the new tree because it was produced with the same ILSpy settings as the
v0.107.1 tree; its IL matches the Linux build method for method (`output/server-bootstrap/assembly-comparison.json`,
51602/51602, see `docs/sim-port/inventory-monsters.md`). Spec citations (`sim/specs/**/*.md`, `docs/sim-port/*.md`,
5,380 of them) cite the Linux tree's line numbers, so they are resolved there by member (method / property, with
its state machine and lambdas, overloads told apart by parameter types).

Diffing v0.107.1 against the Linux tree directly works but is not useful: 1,557 content files differ, almost all
from decompiler settings (0 % vs 100 % of async methods as `IAsyncStateMachine` structs). The script detects
this and puts a "Decompiler style mismatch" warning at the top of the report (24 s run).

No synthetic pair was needed for the main demo (a real second tree exists). A synthetic pair was still run as a
line-precision check (below).

## Summary

| scope | modified | moved | added | removed | total |
| --- | ---: | ---: | ---: | ---: | ---: |
| content | 507 | 0 | 36 | 3 | 546 |
| kernel | 110 | 0 | 23 | 1 | 134 |
| other | 0 | 0 | 0 | 0 | 0 |
| out_of_scope | 340 | 3 | 61 | 3 | 407 |

Unchanged: 2,449 identical, 8 cosmetic only (whitespace / comments / layout / `using`).

`bulk only` = every edit in the file is a recurring pattern shared by >= 8 files: one mechanical change covers them
all. The rest carry at least one unique edit and are the per-file port list.

| content kind | changed | bulk only | unique | with sim file | with tests | spec hits |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| card_pools | 10 | 0 | 10 | 10 | 10 | 0 |
| cards | 280 | 170 | 110 | 253 | 254 | 78 |
| characters | 6 | 0 | 6 | 5 | 6 | 3 |
| enchantments | 4 | 0 | 4 | 4 | 4 | 1 |
| encounters | 8 | 0 | 8 | 7 | 7 | 5 |
| events | 23 | 8 | 15 | 23 | 23 | 20 |
| modifiers | 2 | 0 | 2 | 2 | 2 | 0 |
| monsters | 45 | 0 | 45 | 45 | 44 | 26 |
| orbs | 5 | 0 | 5 | 5 | 5 | 6 |
| potion_pools | 2 | 0 | 2 | 2 | 2 | 0 |
| potions | 28 | 3 | 25 | 28 | 28 | 18 |
| powers | 84 | 37 | 47 | 66 | 64 | 64 |
| relic_pools | 1 | 0 | 1 | 1 | 1 | 1 |
| relics | 48 | 10 | 38 | 47 | 48 | 29 |
| **total** | 546 | 228 | 318 | 498 | 498 | 251 |

| kernel area | changed | bulk only | unique | sim files (2026-11-01 -> with the map) | spec hits |
| --- | ---: | ---: | ---: | ---: | ---: |
| combat | 9 | 0 | 9 | 1 -> 5 | 214 |
| commands | 11 | 0 | 11 | 9 -> 9 | 203 |
| dynamic_vars | 3 | 0 | 3 | 3 -> 3 | 3 |
| events_kernel | 5 | 0 | 5 | 1 -> 5 | 1 |
| factories | 1 | 0 | 1 | 1 -> 1 | 6 |
| game_actions | 14 | 0 | 14 | 2 -> 9 | 19 |
| helpers | 7 | 0 | 7 | 0 -> 2 | 9 |
| hooks | 1 | 0 | 1 | 1 -> 1 | 12 |
| map | 1 | 0 | 1 | 1 -> 1 | 0 |
| models_base | 13 | 0 | 13 | 9 -> 13 | 114 |
| monster_moves | 2 | 0 | 2 | 0 -> 2 | 3 |
| odds | 2 | 0 | 2 | 0 -> 2 | 5 |
| rewards | 2 | 0 | 2 | 2 -> 2 | 10 |
| rng | 3 | 0 | 3 | 2 -> 3 | 21 |
| rooms | 3 | 0 | 3 | 3 -> 3 | 32 |
| runs | 8 | 0 | 8 | 2 -> 6 | 53 |
| save | 33 | 0 | 33 | 1 -> 12 | 7 |
| state | 14 | 0 | 14 | 7 -> 14 | 39 |
| unlocks | 2 | 0 | 2 | 0 -> 2 | 5 |
| **total** | 134 | 0 | 134 | 45 -> 95 | 756 |

(The 2026-11-01 column is the published run; re-run on 2026-11-02 without the map the script found 46, one more
`state` file, because the sim gained a type since.)

Out of scope (counted, not mapped): nodes 232, multiplayer 46, test_mocks 45, entities.multiplayer 11, others
under 10 each.

Kernel files with the most spec rules to re-read (citations in changed members): `Combat/CombatManager.cs` (175), `Commands/CreatureCmd.cs` (75), `Commands/CardPileCmd.cs` (49), `Models/CardModel.cs` (36), `Commands/CardSelectCmd.cs` (33), `Combat/CombatState.cs` (31), `Runs/RunManager.cs` (31), `Rooms/CombatRoom.cs` (25), `Models/MonsterModel.cs` (22), `Commands/CardCmd.cs` (17), `Models/EventModel.cs` (16), `Commands.Builders/AttackCommand.cs` (13).

## Kernel map coverage (2026-11-02)

`sim/specs/kernel-map.json` maps every kernel file of the rule-truth tree (Linux v0.111.0, 564 files, 138,981
lines) to the sim files that port it, built by `scripts/kernel_map.py`:

| source | files | how |
| --- | ---: | --- |
| `type` | 201 | sim Core declares the file's type, or a fixed renaming (`Creature` -> `CreatureState`, `CardModel` -> `CardDef`) |
| `spec` | 79 | >= 2 lines of `sim/specs/**/*.md` / `docs/sim-port/*.md` cite the file and name a sim symbol on the same line |
| `cite` | 23 | a sim comment cites the file as `file:line` |
| `pattern` | 116 | hand-curated namespace globs (`Saves.Migrations.SerializableRuns/*` -> the save reader, `Saves/*` -> no_port, ...) |
| `curated` | 66 | hand-curated single files with a reason |

Result: mapped 374 files (104,637 lines), `no_port` 190 (34,344 lines: save I/O and profile / settings
persistence, multiplayer queues and network actions, UI / engine helpers, presentation commands, the unported
CRYSTAL_SPHERE minigame, timeline stories), unmapped 0. The last six reviewed by hand (no caller in v0.111.0 or
multiplayer-only): `RelicSelectCmd`, `DeterministicModelComparer`, `LinkedRewardSet`, `MapLocation`,
`RunLocation`, `EnchantmentOption`. `scripts/kernel_map.py --check` fails when the map is stale, a curated record
no longer resolves, or a kernel file of >= 50 lines is unmapped.

On this delta: 134 changed kernel files; 95 now list sim files (45 before; 49 only through the map, e.g.
`Combat/CombatManager.cs` -> `Combat/Loop/CombatLoop.cs` + the `TurnFlow` partials, `MonsterLifecycle`,
`MonsterSpawner`, `RunLoop`; `Entities.Creatures/Creature.cs` -> `State/CreatureState.cs`; `Models/ModelDb.cs` ->
`Registry/Registries.cs`); the other 39 are `no_port` and the report lists each with its reason (largest:
`Saves/MegaCritSerializerContext.cs` 13,999 lines, `Saves/SaveManager.cs` 1,127, `Saves.Migrations/MigrationManager.cs`
693, `Saves/CloudSaveStore.cs` 686, `GameActions.Multiplayer/ActionQueueSet.cs` 655). Unmapped changed kernel
files: 0. The removed `Saves.Runs/SavedPropertiesTypeCache.cs` (v0.107.1 only, so absent from the map's tree)
resolves through the `Saves.Runs/*` pattern.

## Bulk edits

| id | files | edit (class name masked as `<C>`) | examples |
| --- | ---: | --- | --- |
| B1 | 178 | `( this ` -> `( this , cardPlay` | `AdaptiveStrike`, `AllForOne`, `Anger` |
| B2 | 37 | `? cardSource ` -> `? cardSource , CardPlay ? cardPlay` | `CreatureCmd`, `CalcifyPower`, `ColossusPower` |
| B3 | 23 | + `PotionModel . AssertValidForTargetedPotion ( target ) ;` | `Ashwater`, `AttackPotion`, `BlessingOfTheForge` |
| B4 | 23 | `, this ` -> `, this , cardPlay` | `BloodWall`, `Bloodletting`, `BoneShards` |
| B5 | 23 | `TargetType . Self` -> `TargetType . AnyPlayer` | `Ashwater`, `AttackPotion`, `BlessingOfTheForge` |
| B6 | 20 | `, null ` -> `, null , null` | `CreatureCmd`, `AbyssalBaths`, `BrainLeech` |
| B7 | 16 | + `return Task . CompletedTask ;` | `CombatManager`, `CorruptionPower`, `DemonFormPower` |
| B8 | 14 | `. Owner , null` -> `. Owner ` | `BlackHolePower`, `ConstrictPower`, `CrimsonMantlePower` |
| B9 | 13 | + `return ;` | `CombatManager`, `CombatState`, `CardPileCmd` |
| B10 | 11 | `CardPlay . Card . Owner` -> `CardPlay . Player` | `BansheesCry`, `Finisher`, `Ftl` |
| B11 | 10 | + `Player player = target . Player ;` | `AttackPotion`, `ColorlessPotion`, `CosmicConcoction` |
| B12 | 10 | + `return null ;` | `CombatManager`, `CardCmd`, `CardPileCmd` |
| B13 | 8 | - `return Task . CompletedTask ;` | `MoveToMapCoordAction`, `Guardbot`, `KinPriest` |
| B14 | 8 | `, this ` -> `, this , null` | `BadLuck`, `Beckon`, `Burn` |

B1 / B4 (`FromCard(this)` -> `FromCard(this, cardPlay)`) is one attack-builder signature change; 170 card files
carry nothing else.

Examples of unique edits the report lists (values read off its diffs): `Relax` block 15 -> 16, `Anticipate`
Dexterity upgrade +1 -> +2, `BiasedCognition` Focus 4 -> 5, `Colossus` block 5 -> 4, `PactsEnd` damage 17 -> 18,
`Null` Weak 2 -> 1, `Alignment` star cost 3 -> 2, `Accelerant` / `Cruelty` / `Ignition` rarity Rare -> Uncommon,
`Dominate` Uncommon -> Rare, `Mirage` upgrade removes Exhaust instead of -1 cost, `DistinguishedCape` max-HP loss
replaced by curses, `Swift` (enchantment) disables itself before drawing, pools gain `TheBall`, `Abundance`,
`Dowsing`.

## Rerun plan (from the manifest)

| step | needed | detail |
| --- | --- | --- |
| full test suite | **yes** | kernel files changed |
| filtered tests | **yes** | 101 test classes reference a changed model (the report lists them) |
| content soak | **yes** | `Test_ContentSoak_EveryCard` `Test_ContentSoak_EveryEncounter` `Test_ContentSoak_EveryPotion` `Test_ContentSoak_EveryRelic` |
| run soak | **yes** | run-layer areas changed |
| server selftest | **yes** | |
| G0 combat diff gate | **yes** | combat kernel and combat content changed |
| G3 seed replay | **yes** | rng and save changed: check `Saves.Migrations` first (the v18 -> v19 seeding change, HashOld -> XXH64) |
| registry version filter | **yes** | `GameVersions.Default` -> `0.111.0`; 39 VersionExclusive candidates (36 added + 3 removed content models) |
| knowledge export | **yes** | `python3 scripts/export_sts2_localization.py --output knowledge/source/v0.111.0` |
| inventory regen | **yes** | `extract_models.py --root <new>` then `classify/run_all.py` |

## Cross-check against the W0 version-delta list

`sim/specs/inventory/version-deltas.json` (hand-triaged in W0) has 518 content entries: 171 `changed`, 347
`refactor_only`.

- All 171 `changed` entries are flagged by the script, and none of them is bulk-only: the bulk heuristic hides no
  W0-confirmed rule change.
- 181 of the 347 `refactor_only` entries are flagged; 62 of those are bulk-only (B1 etc.), 119 carry a unique edit
  that W0 judged unobservable in single player (multiplayer branches, history logging, mocks).
- 28 content files with a unique edit were absent from the W0 list (its classifiers read them as `same`, the diff
  gate did not flag them). Triaged by hand on 2026-11-02 and added to the W0 list: `triage.py` `MANUAL_VD`, merged
  by `version-deltas/build.py` with `sources: version_delta`; `version-deltas.json` now has 546 content entries
  (changed 172, refactor_only 374). Verdicts:

| file | verdict | reason |
| --- | --- | --- |
| `Relics/MusicBox` | **changed** | `CardBeingPlayed` is now also cleared at turn start and combat end; in v0.107.1 a play that never reached `AfterCardPlayed` (combat ended mid-play) left it set and disabled the relic for the rest of the run |
| `Cards/EvilEye` | refactor_only | `e.Actor == Owner.Creature` replaces `e.Card.Owner == Owner`; the entry's actor is `card.Owner.Creature` (`CardExhaustedEntry.cs:20`), so the predicate is the same |
| `Enchantments/Swift`, `Enchantments/Sown` | refactor_only | `Status = Disabled` moved before the draw / energy gain: differs only if that effect re-entered the same enchantment, which nothing in single player does |
| `Cards/Deathbringer`, `HighFive`, `MeteorShower`, `Thunderclap` | refactor_only | `CombatState?.HittableEnemies` null-safety, plus bulk B1 / B4 |
| `Cards/Thrash`, `Enchantments/Corrupted`, `Relics/CharonsAshes`, `Relics/LostWisp` | refactor_only | signature changes (`cardPlay` argument added, trailing `null` dropped) |
| `Relics/GhostSeed` | refactor_only | new `card.Owner != Owner` early return: always the owner in single player |
| `Monsters/DecimillipedeSegment`, `Fabricator`, `GremlinMerc`, `LivingShield` | refactor_only | `base.Creature.CombatState` -> `base.CombatState` (same object) |
| `Monsters/Guardbot`, `Stabbot` | refactor_only | now awaits `base.AfterAddedToRoom()`, which returns `Task.CompletedTask` |
| `Monsters/Crusher`, `Rocket` | refactor_only | KaiserCrab background node nullable (UI) |
| `Monsters/LeafSlimeM`, `TwigSlimeM` | refactor_only | spit-target VFX position |
| `Monsters/CubexConstruct`, `FakeMerchantMonster`, `SlitheringStrangler` | refactor_only | bestiary list, `OnlyPlayAnimOnce`, animator trigger |
| `Encounters/DeprecatedEncounter` | refactor_only | `IsDebugEncounter` override removed; no monsters, in no act |
| `PotionPools/MockPotionPool` | refactor_only | `IsMock => true` on a test pool |

  Evidence lines for both trees are in `version-deltas.json` (checked by `build.py` and `test_classify.py`);
  `docs/sim-port/inventory-*.md` name them per kind. Only `MusicBox` widens the v0.107.1 taint filter.

## Synthetic pair (line-precision check)

The Linux v0.111.0 tree copied to the session scratchpad with five edits: `Bash` damage 8 -> 9, `LeafSlimeM`
ClumpDamage 9 -> 10 (A9+ value of a move), `Anchor` block 10 -> 12, `PowerCmd.Apply<T>(single target)` returns on
`amount == 0m`, `StandardActMap.maxElites` 15 -> 16. Old tree = the cite tree, so citations resolve by line.

| changed file | changed member | sim file | tests | spec rules hit |
| --- | --- | --- | ---: | --- |
| `Models.Cards/Bash.cs` | `CanonicalVars` | `Cards/Defs/Bash.cs` | 50 | `cards-upgrades.md:32` (line) |
| `Models.Monsters/LeafSlimeM.cs` | `ClumpDamage` | `Monsters/Defs/LeafSlimeM.cs` | 7 | `monsters-slimes-weak.md:75` (line) |
| `Models.Relics/Anchor.cs` | `CanonicalVars` | `Relics/Defs/Anchor.cs` | 3 | none (its one citation is another member) |
| `Commands/PowerCmd.cs` | `Apply(PlayerChoiceContext,Creature,decimal,Creature?,CardModel?,bool)` | `Combat/Pipeline/PowerCmd.cs`, `Powers/PowerModel.cs` | 88 | 4 line + 1 member of 20 (other overloads not hit) |
| `Map/StandardActMap.cs` | `maxElites` | `Run/Map/StandardActMap.cs` | 7 | none of 8 |

5 files found, 3,532 identical; 1.0-1.7 s. Plan: full suite (kernel), content soak cards / encounters / relics,
G0 and G3 yes. `scripts/version_update.sh ... --run` on this pair ran the automatable steps (see the playbook).
