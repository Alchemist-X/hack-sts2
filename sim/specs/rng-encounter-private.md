# Encounter- and monster-private RNG, enemy slot order (combat setup)

2026-10-30. Rule truth: decompile tree (game v0.111.0), cited as `<ns-dir>/<File>.cs:<line>` with the
`MegaCrit.Sts2.Core.` prefix dropped. The v0.107.1 tree
(`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/`, only used for `legacy_v18` bundles) is
cited as `v107:<dir>/<File>.cs:<line>`. IL checks were made on
`output/server-bootstrap/linux-assembly/sts2.dll` (v0.111.0) with `ilspycmd -il`.

This spec closes three items that `monsters.md` §8 lists as deferred: the encounter `Rng`, the
per-monster `MonsterModel.Rng`, and `SortEnemiesBySlotName`. It does not change any existing rule
result: NIBBITS_WEAK draws from neither private `Rng`, neither `Rng` is combat state, and its monster has
no slot (pinned hashes in `EndToEndTests` are unchanged).

## 1. Files

| Sim | Contents |
| --- | --- |
| `Sts2Sim.Core/Random/CombatRunContext.cs` | `RngSeedingScheme { Xxh64, LegacyV18 }`; `CombatRunContext` (run facts the private seeds read) |
| `Sts2Sim.Core/Random/RngSeeds.cs` | added `LegacyV18Encounter`; `Encounter` and `Monster` were already there (rng spec §6.5) |
| `Sts2Sim.Core/Monsters/MonsterDef.cs` | `MonsterDef.UsesPrivateRng`/`InitPrivateRng`; `EncounterDef.Slots`, `GenerateMonsters(cs, rng)`, `EncounterRngRequired()`; `MonsterSpawner.Spawn(cs, spawn, context)`, `SpawnEncounter(cs, encounter, context, spawned)`, `CreateEncounterRng`, `SortEnemiesBySlotName`, `IndexOfSlot` |
| `Sts2Sim.Core/Combat/Loop/CombatLoop.cs` | context overloads of `CreateCombat`/`SetUpCombat`, `EncounterDef` overloads, slot sort inside the `SetUpForCombat` loop |
| `Sts2Sim.Core/Combat/Loop/CombatSim.cs` | `Create(RunRngSet, IReadOnlyList<CardSpec>, string, int, int?, int?, CombatRunContext)` |
| `Sts2Sim.Tests/EncounterRngTests.cs` | `Test_EncounterRng_*` (§7) |

## 2. Run context

The game reads three run facts while it builds a combat; the sim has no `RunState`, so they travel in
`CombatRunContext(int ActIndex, int TotalFloor, int? MapCol = null, int? MapRow = null, RngSeedingScheme Seeding = Xxh64)`.

| Field | Game value | Citation |
| --- | --- | --- |
| `ActIndex` | `RunState.CurrentActIndex`, 0-based | `Runs/RunState.cs:346-361` |
| `TotalFloor` | `RunState.TotalFloor` = sum of the per-act `MapPointHistory` counts | `Runs/RunState.cs:423` |
| `MapCol`, `MapRow` | `RunState.CurrentMapCoord?.col / ?.row`: last visited coordinate, `null` when none; both `int` | `Runs/RunState.cs:386-396`; `Map/MapCoord.cs:10-14` |
| `Seeding` | game version of the run: v0.110.1+ hash with XXH64, v0.107.1 with the old 32-bit hash | §3.3 |

`TotalFloor` during a combat: `EnterMapPointInternal` appends the map point to the history
(`Runs/RunManager.cs:687`, body `Runs/RunState.cs:801-818`) before `EnterRoom` (`Runs/RunManager.cs:703`),
whose `CombatRoom.StartCombat` generates the monsters (`Rooms/CombatRoom.cs:357`). So the value is the
combat's own floor, the number the recorder writes as `room_entered.data.floor` and `run.floor`
(`mod/src/Snapshots.cs:188-193`, `mod/src/StateBuilder/PassiveStateBuilder.cs:238`, both
`runState.TotalFloor`). Floor 1 is the Neow event, so the first combat has `TotalFloor = 2`.
`room_entered.data.act` is `CurrentActIndex` (0-based).

Defaults: `CombatRunContext.Default = (ActIndex 0, TotalFloor 2, MapCol null, MapRow null, Xxh64)`, the first
combat of a standard run (the P0 bundle scope). Every overload without a context uses it
(`MonsterSpawner.Spawn(cs, spawn)`, `SpawnEncounter(cs, encounter, spawned)`, every pre-existing
`CreateCombat`/`SetUpCombat`/`Create`). `null` map coordinates are the game's `?? 0` path.

Bundle mapping (diff-bundle contract): `CombatRunContext.ForAct(source.act, source.floor, null, null,
CombatRunContext.ParseSeeding(seeding))`. `ForAct` takes the 1-based act number (`ActIndex = act - 1`,
`act < 1` and `floor < 0` throw); `ParseSeeding` accepts exactly `"xxh64"` and `"legacy_v18"`. Bundle v1
carries no map coordinate; it only matters for the per-monster `Rng`, which no rule reads (§4).

## 3. Encounter-private `Rng`

### 3.1 Game (v0.111.0)

| Fact | Citation |
| --- | --- |
| field `private Rng? _rng`, exposed to subclasses as `protected Rng Rng` | `Models/EncounterModel.cs:25`, `:37` |
| created once in `GenerateMonstersWithSlots(runState)`: throw if already generated, then `if (_rng == null)` → `seed = (ulong)((long)runState.Rng.Seed + (long)runState.TotalFloor) + StringHelper.GetDeterministicHashCode(Id.Entry)`; `_rng = new Rng(seed)` (counter 0), then `GenerateMonsters()` | `Models/EncounterModel.cs:202-216` |
| IL: `get_Seed` (u64) `+ conv.i8(TotalFloor)` `add`, `+ GetDeterministicHashCode` `add`, `newobj Rng(uint64)`; plain `add`, so it wraps | `sts2.dll`, `EncounterModel::GenerateMonstersWithSlots` `IL_0027`-`IL_004d` |
| hash = XXH64 (seed 0) of the UTF-8 bytes of the id entry, e.g. `SLIMES_WEAK` | `Helpers/StringHelper.cs:105-118` |
| only other writer: `DebugRandomizeRng` (debug console) | `Models/EncounterModel.cs:307-314` |
| caller: `CombatRoom.StartCombat`, before any creature is created | `Rooms/CombatRoom.cs:357`, creatures `:392-393` (pre-finished rooms `:501`) |

Every consumer draws inside `GenerateMonsters()`; nothing reads `_rng` afterwards, so it is not combat
state:

| Encounter | Draws | Citation (`Models.Encounters/`) |
| --- | --- | --- |
| `SlimesWeak` | `NextItem(small)`, `NextItem(rest)` (one candidate left, still one draw), `NextItem(medium)` | `SlimesWeak.cs:48-62` |
| `SlimesNormal` | `NextBool` | `SlimesNormal.cs:46` |
| `BowlbugsWeak`, `BowlbugsNormal` | `NextItem` once / twice | `BowlbugsWeak.cs:37`, `BowlbugsNormal.cs:67` |
| `FlyconidNormal`, `RubyRaidersNormal`, `SlitheringStranglerNormal` | `NextItem` | `FlyconidNormal.cs:50`, `RubyRaidersNormal.cs:25`, `SlitheringStranglerNormal.cs:65-98` |
| `CorpseSlugsWeak`/`Normal` | `CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves(monsters, Rng)` | `CorpseSlugsWeak.cs:36`, `CorpseSlugsNormal.cs:38`; `Models.Monsters/CorpseSlug.cs:480` |
| `ScrollsOfBitingWeak`/`Normal`, `TwoTailedRatsNormal`, `DecimillipedeElite` | starter move index `NextInt(3)` | `ScrollsOfBitingWeak.cs:30`, `ScrollsOfBitingNormal.cs:31`, `TwoTailedRatsNormal.cs:42`, `DecimillipedeElite.cs:49` |
| `PunchOffEventEncounter` | `StartingHpReduction = NextInt(2, 10)` twice | `PunchOffEventEncounter.cs:22`, `:24` |

`NextItem(IEnumerable)` draws `NextInt(0, count)` (`Random/Rng.cs:213-223`). SlimesWeak composition:
`small = [LeafSlimeS, TwigSlimeS]`, `d1 = small[NextInt(0,2)]`, remove it, `d2 = rest[NextInt(0,1)]`,
`d3 = [LeafSlimeM, TwigSlimeM][NextInt(0,2)]`; spawn order `d1, d3, d2` (`SlimesWeak.cs:54-60`).

### 3.2 Sim

- `MonsterSpawner.SpawnEncounter(cs, encounter, context, spawned)`: `rng = CreateEncounterRng(cs, encounter, context)`
  = `context.CreateEncounterRng(cs.Rng.Seed, encounter.Id)`, then `encounter.GenerateMonsters(cs, rng)`, then
  spawn and add each monster in list order as before (`Rooms/CombatRoom.cs:381-394`). The `Rng` is a local:
  it is not stored, cloned or hashed, and no `cs.Rng` stream moves.
- `EncounterDef.GenerateMonsters(CombatState cs, Rng rng)` is virtual and defaults to the contract's
  `GenerateMonsters(cs)`, so deterministic encounters (NIBBITS_WEAK) are unchanged. An encounter that reads
  `base.Rng` overrides the two-argument overload and implements the abstract one-argument overload as
  `=> throw EncounterRngRequired();` (it cannot run without its seed).
- `CombatRunContext.EncounterSeed(runSeed, entry)`: `Xxh64` → `RngSeeds.Encounter(runSeed, TotalFloor, entry)`;
  `LegacyV18` → `RngSeeds.LegacyV18Encounter(...)` (§3.3).

### 3.3 v0.107.1 (`legacy_v18` bundles)

`uint seed = (uint)((int)runState.Rng.Seed + runState.TotalFloor + StringHelper.GetDeterministicHashCode(Id.Entry)); _rng = new Rng(seed);`
(`v107:Models/EncounterModel.cs:266-269`). In that version `RunRngSet.Seed` is `uint` =
`(uint)GetDeterministicHashCode(seedText)` (`v107:Runs/RunRngSet.cs:26`, `:104-107`), `GetDeterministicHashCode`
is the old 32-bit string hash (`v107:Helpers/StringHelper.cs:136-150`, the sim's
`GetDeterministicHashCodeOld`), and `Rng(uint)` builds the same xoshiro256**/splitmix64 kernel from the
zero-extended seed (`v107:Random/Rng.cs:28-33`, `v107:Random/MegaRandom.cs`). The arithmetic is 32-bit
`int` and wraps. Ported as `RngSeeds.LegacyV18Encounter(ulong runSeed, int totalFloor, string entry)` =
`(uint)((int)runSeed + totalFloor + HashOld(entry))`. `(int)runSeed` keeps the low 32 bits, so the
Diff harness's legacy set (`Seed = RunSeed("old" + text) = (uint)HashOld(text)`) and the V18 migration's
sign-extended seed (rng spec §6.6) give the same value (tested).

### 3.4 Oracle check against the recorder corpus

An independent Python reference (XXH64, old hash, splitmix64, xoshiro256** transcribed from the
decompile; it reproduces the rng spec §12 vectors) replayed the encounter `Rng` for every recorded fight of
the five encounters whose composition depends only on that `Rng` (SLIMES_WEAK, SLIMES_NORMAL,
BOWLBUGS_WEAK, BOWLBUGS_NORMAL, FLYCONID_NORMAL), at every floor and in every session part, with
`TotalFloor = room_entered.data.floor`, and compared the predicted monster ids with the enemies (by
`combat_id`) of the first battle snapshot after the event:

| Corpus slice | Seeding | Fights | Match |
| --- | --- | --- | --- |
| v0.107.1 | legacy (§3.3) | 162 | 162 |
| v0.110.1 + v0.111.0 | XXH64 (§3.1) | 23 | 23 |

The other formula, and the right formula with `floor - 1`, agree only at chance rate. Seven of these fights
(four XXH64 at floors 2, 3, 6, 23; three legacy at floors 2, 2, 7) are pinned as draw sequences in
`Test_EncounterRng_RecordedCompositionsMatchDraws`. The corpus itself is not read by any test.

## 4. Per-monster private `Rng` (`MonsterModel.Rng`)

### 4.1 Game

| Fact | Citation |
| --- | --- |
| `CreateCreature`: `RunRng` set, `new Creature`, HP roll on `Niche` (enemies), `AttachCreature` (`CombatId = _nextCreatureId++`), then `monster.Rng = new Rng((ulong)((long)Seed + (long)(CurrentMapCoord?.col ?? 0) + (CurrentMapCoord?.row ?? 0) + CurrentActIndex + creature.CombatId.Value))` | `Combat/CombatState.cs:709-724` (formula `:721`), `:726-731` |
| IL: `conv.i8` of col, row and act index, `conv.u8` of the `uint` combat id, plain `add`s (wraps), `newobj Rng(uint64)` | `sts2.dll`, `CombatState::CreateCreature` `IL_0083`-`IL_00f1` |
| canonical (immutable) models return `Rng.Chaotic` from the getter | `Models/MonsterModel.cs:336`, `:396-411` |
| also built for mid-combat spawns | `Commands/CreatureCmd.cs:104`, `:179`, `:3558`, `:3569`; `Models.Powers/SurprisePower.cs:80`; pets `Commands/PlayerCmd.cs:51`, `:951` |
| only reader in v0.111.0: `ToughEgg.SetupSkins` (cosmetic skin pick) | `Models.Monsters/ToughEgg.cs:549-555` |

No rule reads it. Act index 0 has two weak pools: Overgrowth's (`monsters.md` §1: Nibbit, LeafSlimeS,
TwigSlimeS, LeafSlimeM, TwigSlimeM, ShrinkerBeetle, FuzzyWurmCrawler) and Underdocks' (`Models.Acts/Underdocks.cs:44`
`Index => 0`; `CorpseSlugsWeak`, `SeapunkWeak`, `SludgeSpinnerWeak`, `ToadpolesWeak` at `:71`, `:81`, `:84`, `:87`:
CorpseSlug, Seapunk, SludgeSpinner, Toadpole). None of these monsters reads `MonsterModel.Rng`: the Overgrowth model
files and `Seapunk.cs`/`SludgeSpinner.cs` never mention `Rng`; `CorpseSlug.EnsureCorpseSlugsStartWithDifferentMoves`
takes the encounter `Rng` as a parameter (`Models.Monsters/CorpseSlug.cs:480`, called with `base.Rng` of the encounter,
`Models.Encounters/CorpseSlugsWeak.cs:36`); `Toadpole.SetupSkins` draws its cosmetic skins from `Rng.Chaotic`
(`Models.Monsters/Toadpole.cs:320-321`). **P0 monsters that consume it: none.** v0.107.1 is the same
(`v107:Models/Monsters/ToughEgg.cs:118`).

### 4.2 Sim seam

- `MonsterDef.UsesPrivateRng` (default `false`) and `MonsterDef.InitPrivateRng(cs, self, rng)` (default no-op).
- `MonsterSpawner.Spawn(cs, spawn, context)`: after the HP roll (the game's order), when the def opts in,
  `def.InitPrivateRng(cs, monster, context.CreateMonsterRng(cs.Rng.Seed, monster.CombatId))` =
  `new Rng(RngSeeds.Monster(Seed, MapCol ?? 0, MapRow ?? 0, ActIndex, (uint)CombatId))`. A monster that uses it
  keeps it in its `MonsterExtraState`, which clones and hashes it.
- Nothing is created or stored for a monster that does not opt in, so the canonical form of every current
  combat is unchanged.
- Legacy: `v107:Combat/CombatState.cs:244` decompiles as
  `(uint)((Seed + CurrentMapCoord?.col) ?? ((long?)CurrentMapCoord?.row) ?? (CurrentActIndex + CombatId))`,
  i.e. `??` binds looser than `+` in the source and the value is `Seed + col` whenever a coordinate exists.
  Not ported (no rule reads it; no v0.107.1 IL to confirm): `CombatRunContext.MonsterSeed` throws
  `NotSupportedException` under `LegacyV18`, which only a def that opts in can reach.

## 5. Enemy slot order

### 5.1 Game

| Fact | Citation |
| --- | --- |
| `EncounterModel.Slots` defaults to empty; 19 encounters override it | `Models/EncounterModel.cs:133` |
| `SortEnemiesBySlotName`: nothing when `Encounter == null`; else `_enemies.Sort((a, b) => Encounter.Slots.IndexOf(a.SlotName) - Encounter.Slots.IndexOf(b.SlotName))` | `Combat/CombatState.cs:958-964`, `Encounter` `:605-616` |
| IL of the comparison: `ListExtensions.IndexOf<string>` twice, `sub`; `List<Creature>.Sort(Comparison<Creature>)` | `sts2.dll`, `CombatState::<SortEnemiesBySlotName>b__70_0`, `SortEnemiesBySlotName` `IL_001b` |
| `IndexOf`: an `IList<T>` uses `IList<T>.IndexOf`, else a loop with `EqualityComparer<T>.Default`; first ordinal match, -1 when absent (a `null` slot name is -1) | `Extensions/ListExtensions.cs:38-52` |
| only caller: `CombatManager.AddCreature`: `Monster?.SetUpForCombat()`, then `if (creature.SlotName != null) state.SortEnemiesBySlotName()` | `Combat/CombatManager.cs:6990-7005` (`:6998`, `:6999-7002`) |
| `SetUpCombat` calls `AddCreature` for each element of `state.Creatures`, a fresh `ToList(Concat(_allies, _enemies))`: the loop walks the creation order while `_enemies` is re-sorted after every slotted creature | `Combat/CombatManager.cs:6613-6625`; `Combat/CombatState.cs:558` |
| mid-combat spawns: `CreatureCmd.Add` → `CombatState.AddCreature` + `CombatManager.AddCreature` | `Commands/CreatureCmd.cs:279-280` |
| players have no slot name (only the monster constructor sets it) | `Entities.Creatures/Creature.cs:946-970` |
| `StartCombatInternal` runs `AfterCreatureAdded` (first `RollMove`, `MonsterAi`) over a fresh `State.Creatures`, i.e. in slot order; enemy turns iterate `_enemies` | `Combat/CombatManager.cs:4548`, loop `:4466-4478` |

`List<T>.Sort(Comparison<T>)` is .NET introsort and is not stable: 2 elements → one `SwapIfGreater`; 3 →
the network `(0,1), (0,2), (1,2)`, which swaps equal keys (`[b1, b2, a]` → `[a, b2, b1]`); 4-16 → insertion
sort (stable); more → median-of-three quicksort, which moves equal keys (rng spec §8 has the same
analysis). `sts2.dll` targets `.NETCoreApp,Version=v9.0` (its `TargetFrameworkAttribute`), the sim's
runtime, so the sim calls the same `List<T>.Sort(Comparison<T>)` with the same comparison results and gets
the same permutation, ties included.

Not ported: `SetEnemyIndex` (`Combat/CombatState.cs:966-979`, no caller in rule code) and `GetNextSlot`
(`Models/EncounterModel.cs:195-198`, needed with the first summoning monster).

### 5.2 Sim

- `EncounterDef.Slots` (virtual, empty by default).
- `MonsterSpawner.SortEnemiesBySlotName(cs, encounter)`: `null` encounter → nothing; else
  `cs.Monsters.Sort((a, b) => IndexOfSlot(slots, a.SlotName) - IndexOfSlot(slots, b.SlotName))`.
  `IndexOfSlot` = first ordinal match or -1.
- `CombatLoop.SetUpCombat`, after the opening shuffle: take `cs.Monsters.ToArray()`; for each monster
  `MonsterLifecycle.SetUpForCombat`, then, if its `SlotName != null`, `SortEnemiesBySlotName`. The encounter is the
  `EncounterDef` passed to the new overloads, else the registry entry for the id; with the `populateMonsters`
  test seam an id that is not registered gives `null` (no sort), like the game's `Encounter == null`.
- `StartCombat` is unchanged: its snapshot of `cs.Monsters` is already in slot order, so the first
  `RollMove` draws follow slot order.
- Future mid-combat spawns must do the same: `SetUpForCombat`, then the conditional sort.

### 5.3 Effect in v0.111.0

The act-1 weak encounters of both act-index-0 pools (Overgrowth: NibbitsWeak, SlimesWeak, ShrinkerBeetleWeak,
FuzzyWurmCrawlerWeak; Underdocks: CorpseSlugsWeak, SeapunkWeak, SludgeSpinnerWeak, ToadpolesWeak) define no `Slots`
and spawn with `null` slot names (`Models.Encounters/CorpseSlugsWeak.cs:32,34`, `SeapunkWeak.cs:22`,
`SludgeSpinnerWeak.cs:19`, `ToadpolesWeak.cs:28-29`), so the sort never runs for them. Every encounter that overrides
`Slots` lists its setup monsters in `Slots` order (FabricatorNormal, FogmogNormal, LivingFogNormal,
OvicopterNormal, PhrogParasiteElite, TheObscuraNormal spawn only their main monster), and the encounters
that name slots without overriding `Slots` (BowlbugsWeak, BowlbugsNormal, GremlinMercNormal, KnightsElite,
SlumberingBeetleNormal) compare every key equal with at most 3 creatures, which no sort network moves. So the
setup sort changes no current encounter; it decides the position of creatures summoned mid-combat into
earlier slots (bots, illusions, bombs, eggs, wrigglers).

## 6. Contract additions (for CONTRACTS.md §4.3, §4.7, §4.8)

```csharp
enum RngSeedingScheme : byte { Xxh64, LegacyV18 }
readonly record struct CombatRunContext(int ActIndex, int TotalFloor, int? MapCol = null, int? MapRow = null,
                                        RngSeedingScheme Seeding = Xxh64)
    const int DefaultActIndex = 0, DefaultTotalFloor = 2; const string SeedingXxh64Name = "xxh64", SeedingLegacyV18Name = "legacy_v18"
    static CombatRunContext Default; static CombatRunContext ForAct(int actNumber, int totalFloor, int? mapCol = null,
                                                                   int? mapRow = null, RngSeedingScheme seeding = Xxh64)
    static RngSeedingScheme ParseSeeding(string)
    ulong EncounterSeed(ulong runSeed, string encounterIdEntry); Rng CreateEncounterRng(ulong runSeed, string encounterIdEntry)
    ulong MonsterSeed(ulong runSeed, int combatId);              Rng CreateMonsterRng(ulong runSeed, int combatId)
static class RngSeeds  + uint LegacyV18Encounter(ulong runSeed, int totalFloor, string encounterIdEntry)

abstract class MonsterDef   + virtual bool UsesPrivateRng => false; virtual void InitPrivateRng(CombatState cs, MonsterState self, Rng rng)
abstract class EncounterDef + virtual IReadOnlyList<string> Slots => []
                            + virtual IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs, Rng rng) => GenerateMonsters(cs)
                            + protected InvalidOperationException EncounterRngRequired()
static class MonsterSpawner + Spawn(cs, spawn, CombatRunContext); SpawnEncounter(cs, encounter, CombatRunContext, spawned = null)
                            + Rng CreateEncounterRng(cs, encounter, CombatRunContext)
                            + void SortEnemiesBySlotName(cs, EncounterDef? encounter); int IndexOfSlot(IReadOnlyList<string>, string?)

static class CombatLoop     + CreateCombat/SetUpCombat(RunRngSet, IReadOnlyList<CardSpec>, string encounterId, int, int?, int?,
                                                      CombatRunContext, Action<CombatState>? populateMonsters = null)
                            + CreateCombat/SetUpCombat(RunRngSet, IReadOnlyList<CardSpec>, EncounterDef, int, int?, int?, CombatRunContext)
sealed class CombatSim      + Create(RunRngSet, IReadOnlyList<CardSpec>, string encounterId, int ascension, int? currentHp,
                                     int? maxHp, CombatRunContext context)   // rng deep-cloned, like the other RunRngSet overload
```

The existing signatures are unchanged; each now runs with `CombatRunContext.Default`.

## 7. Tests (`Sts2Sim.Tests/EncounterRngTests.cs`, filter `EncounterRng`)

Expected values come from the independent Python reference (§3.4), not from the C# port.

- hash vectors: XXH64 and old hash of encounter ids.
- encounter seed: four XXH64 vectors from recorded seeds, 64-bit wraparound, context dispatch, first
  output of a fresh encounter `Rng`, floor sensitivity, act and map coordinate not used.
- legacy encounter seed: three recorded seeds, sign- vs zero-extended run seed, 32-bit wraparound, scheme dispatch.
- recorded compositions: seven corpus fights replayed as `NextInt` draw sequences on the context's encounter `Rng`.
- monster seed: vectors, wraparound, `null` coordinates as 0, first output, legacy throws.
- context: defaults, `ForAct` mapping and validation, `ParseSeeding`.
- plumbing: `SpawnEncounter` hands the encounter a fresh `Rng` equal to the formula for both schemes and
  floors 1-8; only `Niche` moves; default overload = default context; the one-argument overload of an
  `Rng` encounter throws; deterministic encounters ignore the `Rng`.
- not state: an encounter that draws from its `Rng` but spawns the same monsters hashes equal at floors 0-12;
  NIBBITS_WEAK hashes equal under six contexts; `CombatSim.Create` with a context does not advance the
  caller's streams and equals the id and `EncounterDef` overloads.
- private monster `Rng`: only opted-in monsters receive it, after the HP roll, seeded by the formula with
  their combat id; legacy + opt-in throws, legacy without opt-in spawns; no act-1 weak-pool monster opts in.
- slots: `IndexOfSlot` semantics; sort by `Slots` index (not by name), `null`/unknown slots first, `null`
  encounter and empty `Slots` leave the order; the 3-element network swaps equal keys and the 5-element
  insertion sort keeps them (checked against `List<T>.Sort` on plain keys).
- setup path (20 seeds): `SetUpForCombat` runs once per monster in creation order, and the list is re-sorted
  after each slotted monster (the second monster already sees the sorted list); after setup the monsters are
  in slot order with their creation-order combat ids; `AfterCreatureAdded` and the first `RollMove` run in
  slot order, one `MonsterAi` draw each, and each monster's move equals the next draw of a replayed stream.
- 17 unslotted monsters keep their order (a sort would move them); one slotted monster triggers a sort that
  equals `List<T>.Sort` over 17 equal keys (not the identity); an unregistered id with the populate seam
  does not sort.

## 8. Open points

- The context is not stored on `CombatState` (state area). Mid-combat spawns would seed opted-in monsters
  with the default context; harmless while no rule reads the private `Rng`, but the first summoning monster
  port that opts in needs `CombatRunContext` on the state (clone + hash).
- Resolved 2026-10-30 (integration): `Sts2Sim.Diff` passes
  `CombatRunContext.ForAct(source.act ?? 1, source.floor ?? 2, null, null, CombatRunContext.ParseSeeding(seeding))`
  (`BundleRunner.RunContext`, `diff-harness.md` §2). Before that the legacy `SLIMES_WEAK` bundle
  `1788970367-QC7ZPPWXEK` spawned `TWIG_SLIME_S` for the recorded `LEAF_SLIME_S`; it now agrees on all 18 decisions.
  Real-data openings through the harness path: `SLIMES_WEAK` 19/20 `legacy_v18` + 1/1 `xxh64`, `CORPSE_SLUGS_WEAK`
  14/14 + 2/2 (the miss is `1784043137-CPSVMWJPUL`, Neow not recorded, true `Niche` offset 1).
- `monsters.md` §8 and CONTRACTS §4.7 still list these three items as deferred (shared files; §6 holds the text to
  merge, `review-fixes-2026-10-30.md` §3 the replacement paragraphs).
- Per-monster `Rng` inputs the bundle path does not have yet (review 2026-10-30; unreachable in P0, no ported rule
  reads it, §4.1): the game seeds it from the real `CurrentMapCoord` (non-null in every map combat,
  `Combat/CombatState.cs:721`), while `BundleRunner.RunContext` passes `null` coordinates (= 0) and `LegacyV18` throws.
  Before the first monster that opts in: (a) carry the room's map coordinate in the bundle `source` — the recorder
  has it as the `destination` of the `move_to_map_coord` action before the room's `room_entered`, and as
  `source.coord` of the next `vote_for_map_coord` (e.g. `1784043137-CPSVMWJPUL`: first combat at (1, 1)); (b) port
  the v0.107.1 formula with an IL check (§4.2: the decompile reads as `Seed + col` whenever a coordinate exists).
