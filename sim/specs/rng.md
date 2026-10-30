# RNG area spec

Decompile root: `/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/` (game v0.111.0).
Citations are `<namespace-dir without MegaCrit.Sts2.Core.>/<File>.cs:<line>`.

Status: complete port of the kernel, wrapper, serializable state, hashing, the run and player
stream registries, every seeding path that the combat sim can reach, the mock seams, and the
RNG-consuming list helpers. Bit-exactness is checked against the live game (section 12).

## 1. Files

| Sim file | Game source |
| --- | --- |
| `Sts2Sim.Core/Random/MegaRandom.cs` | `Random/MegaRandom.cs` (xoshiro256** + splitmix64) |
| `Sts2Sim.Core/Random/Rng.cs` | `Random/Rng.cs` (`Rng`), `Saves/SerializableRng.cs` (`RngSnapshot`) |
| `Sts2Sim.Core/Random/DeterministicHash.cs` | `Helpers/StringHelper.cs` (hashes, `SnakeCase`, `Slugify`) + hand-written XXH64 |
| `Sts2Sim.Core/Random/RngSeeds.cs` | every composite seed formula + `Helpers/SeedHelper.cs` constants + V18 migration kernel |
| `Sts2Sim.Core/Random/RngSets.cs` | `Runs/RunRngSet.cs` (`RunRngSet`), `Random/PlayerRngSet.cs` (`PlayerRngSet`) |
| `Sts2Sim.Core/Random/RngSetSnapshots.cs` | `Saves.Runs/SerializableRunRngSet.cs`, `Saves/SerializablePlayerRngSet.cs` |
| `Sts2Sim.Core/Random/RngCollectionExtensions.cs` | `Extensions/ListExtensions.cs`, `Extensions/IEnumerableExtensions.cs` |
| `Sts2Sim.Core/Random/RngCanonicalTags.cs` | sim-only canonical tags |
| `Sts2Sim.Tests/RngTests.cs` | known-answer, oracle, counter, round-trip, clone and canonical tests |

## 2. Kernel: `MegaRandom` (`Random/MegaRandom.cs`)

| Item | Value | Citation |
| --- | --- | --- |
| state | four `ulong` `_s0.._s3` | `MegaRandom.cs:15-21` |
| `_incrDouble` | `1.1102230246251565E-16` = exactly 2^-53 | `MegaRandom.cs:11`, used `:146` |
| `_incrFloat` | `5.9604645E-08f` = exactly 2^-24 | `MegaRandom.cs:13`, used `:172` |
| splitmix64 increment | `11400714819323198485` = `0x9E3779B97F4A7C15` | `MegaRandom.cs:35` |
| splitmix64 mul 1, shift | `13787848793156543929` = `0xBF58476D1CE4E5B9`, `>> 30` | `MegaRandom.cs:36` |
| splitmix64 mul 2, shift | `10723151780598845931` = `0x94D049BB133111EB`, `>> 27` | `MegaRandom.cs:37` |
| splitmix64 final xor-shift | `>> 31` | `MegaRandom.cs:38` |
| seeding | `s0..s3` = four successive `Splitmix64(ref seed)` on a by-value copy of the seed | `MegaRandom.cs:41-47` |
| step (xoshiro256**) | `result = rotl(s1*5, 7)*9; t = s1<<17; s2^=s0; s3^=s1; s1^=s2; s0^=s3; s2^=t; s3 = rotl(s3,45)` | `MegaRandom.cs:97-116` |
| `Next(int max)` | throws `ArgumentOutOfRangeException("maxValue", max, "maxValue must be > 0")` when `max < 1`, before any step; else `(int)(NextDouble() * max)` | `:118-126`, `:175-178` |
| `Next(int min, int max)` | throws (`"maxValue must be > minValue"`) when `min >= max`; `range = (long)max - (long)min`; `range <= 2147483647` → `(int)(NextDouble()*range) + min`; else `(int)((long)(NextDouble()*range) + min)` | `:128-141`, `:175-183` |
| `NextDouble()` | `(double)(step >> 11) * 2^-53` | `:143-147` |
| `NextInt()` | `(int)(step >> 33)` | `:149-152` |
| `NextUInt()` | `(uint)step` (low 32 bits) | `:154-157` |
| `NextULong()` | `step` | `:159-162` |
| `NextBool()` | most significant bit of `step` | `:164-167` |
| `NextFloat()` | `(float)(step >> 40) * 2^-24` | `:169-173` |
| `NextBytes(span)` | 8-byte chunks of successive outputs in native (little-endian) order; a tail of `n < 8` bytes takes the low `n` bytes of one more output and still advances one full step | `:57-95` |
| reload | `Reinitialise(SerializableRng)` copies `state0..3` (no all-zero guard) | `:49-55` |
| export | `FillSerializableState` | `:185-191` |

Consequences used by the tests: every public draw is exactly one kernel step, `NextDouble` and
`NextFloat` are exact dyadic rationals, and `NextBytes(k)` advances `ceil(k/8)` steps. There is
no jump/long-jump function in the game kernel. `MegaRandom.NextBool` (MSB) is a different rule
from `Rng.NextBool` (`Next(2) == 0`); both are ported as-is.

## 3. Wrapper: `Rng` (`Random/Rng.cs`)

`_counter` counts calls, not kernel steps; it is serialized next to the state
(`Rng.cs:268-276`). The order of validation versus `_counter++` is part of the contract:

| Method | Counter | Validation before `_counter++` | Formula | Citation |
| --- | --- | --- | --- | --- |
| `Rng(ulong seed = 0)` | 0 | | `new MegaRandom(seed)` | `Rng.cs:26-29` |
| `Rng(SerializableRng)` → `Rng(RngSnapshot)` | from snapshot | | state from snapshot | `:31-35` |
| `Rng(ulong seed, string name)` | 0 | | seed `seed + GetDeterministicHashCode(name)` (wraps) | `:42-45` |
| `Rng(Player, ModelId, mixin)` → `Rng.ForPlayerModel` | 0 | | section 6.4 | `:37-40` |
| `LoadFromSerializable` → `LoadFromSnapshot` | from snapshot | | in place, keeps the instance | `:47-51` |
| `NextBool()` | +1 | none | `kernel.Next(2) == 0` | `:53-57` |
| `NextInt(max = int.MaxValue)` | +1 | **none**: `NextInt(max < 1)` increments the counter and then the kernel throws without stepping | `kernel.Next(max)` | `:59-63` |
| `NextInt(min, max)` | +1 | `min >= max` throws `("minInclusive", "Minimum must be lower than maximum.")` | `kernel.Next(min, max)` | `:65-74` |
| `NextUnsignedInt(max = uint.MaxValue)` | +1 (delegates, counted once) | via 2-arg | `NextUnsignedInt(0, max)` | `:76-79` |
| `NextUnsignedInt(min, max)` | +1 | `min >= max` throws | `min + (uint)(NextDouble() * (double)(max - min))` | `:81-93` |
| `NextUnsignedLong()` | +1 | none | `kernel.NextULong()` | `:95-99` |
| `NextUnsignedLong(max)` | +1 | via 2-arg | `max == ulong.MaxValue` → raw `NextULong()`; else `NextUnsignedLong(0, max)` | `:101-108` |
| `NextUnsignedLong(min, max)` | +1 | `min >= max` throws | `min + (ulong)(NextDouble() * (double)(max - min))` | `:110-122` |
| `NextFloat(max = 1f)` | +1 | via 2-arg | `NextFloat(0f, max)` | `:124-127` |
| `NextFloat(min, max)` | +1 | `min > max` throws (equal allowed) | `(float)(NextDouble() * (double)(max - min) + (double)min)`, `max - min` in float | `:129-138` |
| `NextDouble()` | +1 | none | `kernel.NextDouble()` | `:140-144` |
| `NextDouble(min, max)` | +1 | `min > max` throws | `NextDouble() * (max - min) + min` | `:146-155` |
| `NextGaussianFloat(...)` | +2 per attempt | via double | `(float)NextGaussianDouble(...)` | `:157-160` |
| `NextGaussianDouble(mean=0, std=1, min=0, max=1)` | +2 per attempt | `min > max`, `mean ∉ [0,1]` throw | Box-Muller with `Math.Log`/`Math.Sqrt`/`Math.Cos`, reject `n ∉ [0,1]`, return `n*(max-min)+min` | `:162-186` |
| `NextGaussianInt(mean, std, min, max)` | +2 per attempt | `min > max`, `mean ∉ [min,max]` throw | Box-Muller with `Math.Sin`, `(int)Math.Round` (banker's), reject outside `[min,max]` | `:188-211` |
| `NextItem<T>(IEnumerable<T>)` | +1, or 0 when empty | | `items as T[] ?? ToArray()`, index `NextInt(0, count)` | `:213-223` |
| `WeightedNextItem<T>(items, w)` | +1 (also when empty) | | `WeightedNextItem(NextFloat(), items, w, default)` | `:225-228` |
| static `WeightedNextItem(r, items, w, fallback)` | 0 | | `total = Enumerable.Sum(items, w)` (float sum accumulated in double, see 9.3), `x = r*total` (float), subtract weights in order, first `x <= 0f` wins, else `fallback` | `:230-252` |
| `Shuffle<T>(IList<T>)` | +1 per `i = n-1..1` | | `j = NextInt(i+1)`, swap `list[i]`, `list[j]` | `:254-266` |

`SerializableRng` (`Saves/SerializableRng.cs:10-58`, JSON names `counter`, `s0..s3`) is ported as
`readonly record struct RngSnapshot(int Counter, ulong S0..S3)` with the same `ToString` text
(`SerializableRng.cs:55-58`) and value equality (`:78-89`).

## 4. Hashing (`Helpers/StringHelper.cs`)

| Item | Rule | Citation |
| --- | --- | --- |
| `GetDeterministicHashCode(str)` | UTF-8 bytes of `str` → `XxHash64.HashToUInt64(bytes, seed 0)` | `StringHelper.cs:105-118` |
| XXH64 | written by hand from the reference algorithm (`System.IO.Hashing` is a NuGet package). Primes `0x9E3779B185EBCA87`, `0xC2B2AE3D27D4EB4F`, `0x165667B19E3779F9`, `0x85EBCA77C2B2AE63`, `0x27D4EB2F165667C5`; 32-byte stripes, 8/4/1-byte tail, avalanche `>>33 *P2 >>29 *P3 >>32`. Verified on published vectors (section 12) | `:117` |
| `GetDeterministicHashCodeOld(str)` | `h1 = h2 = 352654597`; over UTF-16 code units in pairs: `h1 = ((h1<<5)+h1) ^ s[i]`, stop if last, `h2 = ((h2<<5)+h2) ^ s[i+1]`; return `h1 + h2*1566083941`, all `int` wrapping | `:120-134` |
| `SnakeCase(txt)` | `Regex("([A-Za-z0-9]|\G(?!^))([A-Z])").Replace(txt.Trim(), "$1_$2").ToLowerInvariant()` | `:35-40`, `:63-66` |
| `Slugify(txt)` | camel regex as above, `ToUpperInvariant`, `\s+` → `_`, strip `[^A-Z0-9_]` | `:49-61`, `:68-73` |

## 5. Stream registry

### 5.1 Run streams (`Runs/RunRngSet.cs`, enum `Entities.Rngs/RunRngType.cs:3-17`)

Each stream is `new Rng(Seed, SnakeCase(type.ToString()))` (`RunRngSet.cs:98-102`), i.e. seeded
with `Seed + XXH64(name)` (wrapping). Name hashes were computed by the reference and are asserted
in `Test_Rng_StreamNameHashesSeedNamedStreams`.

| Ord | `RunRngType` | Stream name | XXH64(name) | Property (citation) |
| --- | --- | --- | --- | --- |
| 0 | `UpFront` | `up_front` | `0x1A8A7B60DA243E80` | `UpFront` (`:33`) |
| 1 | `Shuffle` | `shuffle` | `0x2AD1D778C87C9714` | `Shuffle` (`:35`) |
| 2 | `UnknownMapPoint` | `unknown_map_point` | `0x9800EF3164594356` | `UnknownMapPoint` (`:37`) |
| 3 | `CombatCardGeneration` | `combat_card_generation` | `0xA2A21EEF44216B8E` | `CombatCardGeneration` (`:39`) |
| 4 | `CombatPotionGeneration` | `combat_potion_generation` | `0x9EA3163CFEB05AC2` | `CombatPotionGeneration` (`:41`) |
| 5 | `CombatCardSelection` | `combat_card_selection` | `0x54A326EC000E7A0A` | `CombatCardSelection` (`:43`) |
| 6 | `CombatEnergyCosts` | `combat_energy_costs` | `0xB2AEE0C22BB1887F` | `CombatEnergyCosts` (`:45`) |
| 7 | `CombatTargets` | `combat_targets` | `0x5FF62452AC6B3AC2` | `CombatTargets` (`:47`) |
| 8 | `MonsterAi` | `monster_ai` | `0x5ACEEC82DAC3A1A7` | `MonsterAi` (`:49`) |
| 9 | `Niche` | `niche` | `0x1713D477D45AFAC5` | `Niche` (`:51`) |
| 10 | `CombatOrbs` | `combat_orbs` | `0x917D243ADDB36D52` | `CombatOrbGeneration` (`:53`, note the name mismatch) |
| 11 | `TreasureRoomRelics` | `treasure_room_relics` | `0x5E4180A0A992AED6` | `TreasureRoomRelics` (`:55`) |

The hash input is the enum member name, so `Enums/CombatEnums.cs` names must never change; the
test asserts the literal names.

### 5.2 Player streams (`Random/PlayerRngSet.cs`, enum `Entities.Rngs/PlayerRngType.cs:3-8`)

Same construction with the player seed (`PlayerRngSet.cs:27-41`).

| Ord | `PlayerRngType` | Name | XXH64(name) | Property |
| --- | --- | --- | --- | --- |
| 0 | `Rewards` | `rewards` | `0x20F3BF5504146BB3` | `PlayerRngSet.cs:14` |
| 1 | `Shops` | `shops` | `0xDA13890C3383CB6B` | `:16` |
| 2 | `Transformations` | `transformations` | `0xB8623D6522D877F5` | `:18` |

### 5.3 Set behavior

- Storage: the game keeps `Dictionary<XRngType, Rng>` (`RunRngSet.cs:17`, `PlayerRngSet.cs:12`),
  filled in `Enum.GetValues` order. The sim uses an array indexed by the enum ordinal (dense
  0..n-1). Enumeration order (insertion = enum order) is preserved in `ToSnapshot`. Only a
  malformed snapshot with an undefined enum key behaves differently: the game's `FromSave` adds an
  extra entry (`RunRngSet.cs:151`) and `LoadFromSerializable`/`GetRng` throw
  `KeyNotFoundException` (`:182`, `:198`; `PlayerRngSet.cs:90`, `:121`, `:132`), where the sim throws
  `IndexOutOfRangeException` on all three paths.
- `ToSerializable` (`RunRngSet.cs:104-132`, `PlayerRngSet.cs:43-71`) → `ToSnapshot`.
- `FromSave`/`FromSerializable` (`RunRngSet.cs:134-159`, `PlayerRngSet.cs:73-98`): build a fresh
  set from the seed, then **replace** the listed streams with `new Rng(snapshot)` → `FromSnapshot`.
- `LoadFromSerializable` (`RunRngSet.cs:161-189`, `PlayerRngSet.cs:100-128`): seed must match or
  `NotImplementedException("RngSet seed should not change during the run!")`; then each listed
  stream is loaded **in place** (aliasing is kept; with a shared stream the last entry wins) →
  `LoadFromSnapshot`.
- `GetRng(type)` (`RunRngSet.cs:196-199`, `PlayerRngSet.cs:130-133`) → `GetRng` and the contract
  alias `Get`.

## 6. Seeding paths

All arithmetic is `unchecked`. The decompile shows plain casts (no `checked`, which ILSpy would
render), so the game IL wraps; the sim keeps the same conversion sequence.

### 6.1 Run seed (`RunRngSet(string seed)`, `RunRngSet.cs:67-85`)

- `seed.StartsWith("old")` → `Seed = (uint)GetDeterministicHashCodeOld(seed.Substring(3))`,
  zero-extended to `ulong` (`:70-75`). The game call is culture-sensitive `StartsWith(string)`;
  the sim uses `StringComparison.Ordinal`, identical for every seed alphabet the game produces
  (and `InvariantGlobalization` is on in the sim).
- else `Seed = GetDeterministicHashCode(seed)` (`:78`).
- `StringSeed = seed` (`:69`). New runs: `RunState.CreateForNewRun` → `new RunRngSet(seed)`
  (`Runs/RunState.cs:552`).
- Seed text helpers (`Helpers/SeedHelper.cs`): alphabet `"0123456789ABCDEFGHJKLMNPQRSTUVWXYZ"`
  (`:9`), default length 12 (`:11`), `CanonicalizeSeed` = upper-invariant, `O`→`0`, `I`→`1`,
  trim (`:35-42`) → `RngSeeds.CanonicalizeSeed`.

### 6.2 Sim numeric seed (`RunRngSet(ulong seed)`, sim-only)

`CombatSim.Create(ulong seed, ...)` passes the already hashed run seed (CONTRACTS 4.2). The set
stores `StringSeed = ""`, `Seed = seed` and builds the twelve streams exactly as
`CreateRng` does. For a non-`old` string seed `s`, `new RunRngSet(s)` and
`new RunRngSet(XXH64(s))` have identical streams.

These streams are at counter 0, while a real combat draws from the live run streams
(`Combat/CombatManager.cs:6605`; in the oracle save `Shuffle` was at 282, `MonsterAi` at 11 and
`Niche` at 10, §12.1). `CombatSim.Create(RunRngSet rng, ...)` starts a combat from such a set, e.g.
`RunRngSet.FromSnapshot` of a saved `SerializableRunRngSet`; it deep-clones the set first, so the
caller's streams do not advance (loop spec §2.1).

### 6.3 Player set seed (`Entities.Players/Player.cs:729-733`)

`InitializeSeed(seed)` → `new PlayerRngSet(GetDeterministicHashCode(seed) + (ulong)slotIndex)`,
called for each player at run creation (`Runs/RunState.cs:561`, `:611`, `:946`). The hash is on
the raw string seed, so an `old…` seed does **not** take the old-hash path here. `(ulong)` of the
`int` slot index sign-extends (`-1` → `0xFFFF…FFFF`; `GetPlayerSlotIndex` is `IndexOf`,
`RunState.cs:683-686`). The player constructor default is `new PlayerRngSet(0)` (`Player.cs:696`).
Ported as `RngSeeds.PlayerSet` / `PlayerRngSet.ForPlayer`. `RngSeeds.PlayerSetFromHashedRunSeed`
(sim-only) adds the slot to an already hashed seed; it equals the game value for non-`old` seeds.

### 6.4 Composite `Rng(Player, ModelId, mixin)` (`Random/Rng.cs:37-40`)

`(ulong)((long)RunState.Rng.Seed + (long)GetPlayerSlotIndex(player) + (long)XXH64(id.Entry)) + mixin`
→ `RngSeeds.PlayerModel(runSeed, slot, entry, mixin)` and `Rng.ForPlayerModel(runSeed | runRngSet, slot, entry, mixin)`.
Callers: `Models.Relics/Byrdpip.cs:72`, `Models.Relics/FurCoat.cs:297`, `Models.Relics/PaelsLegion.cs:134`.

### 6.5 Per-entity seeds

| Use | Formula | Citation | Sim |
| --- | --- | --- | --- |
| monster `Rng` (per creature, set at spawn) | `(ulong)((long)Seed + (long)(mapCol ?? 0) + (mapRow ?? 0) + CurrentActIndex + CombatId)`; `CombatId` is `uint` (`Entities.Creatures/Creature.cs:425`), assigned by `AttachCreature` just before (`Combat/CombatState.cs:726-731`, `_nextCreatureId` `uint` `:539`) | `Combat/CombatState.cs:721` | `RngSeeds.Monster` |
| encounter `Rng` | `(ulong)((long)Seed + (long)TotalFloor) + XXH64(encounter.Id.Entry)`, only if not already set | `Models/EncounterModel.cs:211-215` | `RngSeeds.Encounter` |
| event `Rng` | `(ulong)((long)Seed + (long)(IsShared ? 0 : slotIndex)) + XXH64(event.Id.Entry)` | `Models/EventModel.cs:91` | `RngSeeds.Event` |
| named ad-hoc stream | `Seed + XXH64(name)` | `Random/Rng.cs:42-45` | `RngSeeds.Named` |

`MapCoord.col/row` are `int` (`Map/MapCoord.cs:10-14`); with no current map point both are 0.

### 6.6 Save migration V18→V19 (`Saves.Migrations.SerializableRuns/SerializableRunV18ToV19.cs`)

Pre-V19 saves stored only counters. `GenerateRng(seed, type, counter)` (`:164-173`):
`new MegaRandom((uint)((int)seed + GetDeterministicHashCodeOld(type)))` then `counter` raw
`NextULong()` calls. Run streams use `seed = (ulong)GetDeterministicHashCodeOld(text)`
(sign-extended, `:66`) and the run seed text becomes `"old" + text` (`:83`); player streams use
the stored player seed (`:139`). Ported as `RngSeeds.LegacyV18Kernel`; the full JSON migration is
not ported.

## 7. Mock and test seams

| Game | Sim | Note |
| --- | --- | --- |
| `RunRngSet(Rng rng)`: all twelve entries are the same instance (`RunRngSet.cs:87-96`) | `RunRngSet(Rng shared)` | `Seed = 0`, `StringSeed = ""` |
| `MockRng(type, seed)`: replace one entry with `new Rng(seed)` (`:191-194`) | `MockRng` | |
| `GetMockInstance()`: static `_mockInstance = new RunRngSet("")`, throws unless `TestMode.IsOn` (`:15`, `:57-65`) | `CreateMockInstance()` returns a fresh `new RunRngSet("")` | no static mutable state in Core; the game shares one mutable instance across callers |
| `TestSupport/TestRngInjector.cs` (static overrides: relic, relic rarity, initial shuffle, combat card generation) | not ported | static mutable state. Default path (override `null`) is the only one the sim runs: `Entities.Cards/CardPile.cs:244`, `Factories/CardFactory.cs:95`, `Factories/RelicFactory.cs:32,44,56` |
| `Rng.Chaotic` (static, seeded from `DateTimeOffset.Now.ToUnixTimeSeconds()`, `Rng.cs:19-24`, `:278-284`) | not ported | consumers are cosmetic (skins, screen shake, animation, log anonymizer, Trial entrant number text, `MonsterModel.Rng` getter of canonical models `Models/MonsterModel.cs:396-411`) |

## 8. Collection helpers (`Extensions/ListExtensions.cs`, `Extensions/IEnumerableExtensions.cs`)

| Helper | Rule | Citation |
| --- | --- | --- |
| `UnstableShuffle(list, rng)` | `n = Count; while (n > 1) { n--; j = rng.NextInt(n+1); swap(list[j], list[n]) }`; same draws and swaps as `Rng.Shuffle` | `ListExtensions.cs:21-36` |
| `StableShuffle(list, rng)` (`T : IComparable<T>`) | copy, `List.Sort()`, write back, then `UnstableShuffle` | `ListExtensions.cs:10-19` |
| `TakeRandom(collection, count, rng)` | `ToList().UnstableShuffle(rng)` runs eagerly (consumes `n-1` draws regardless of `count`), then lazy `Take(count)` | `IEnumerableExtensions.cs:9-12` |

`StableShuffle` is not a stable sort. `List<T>.Sort` is .NET introsort; the order of elements that
compare equal is a deterministic function of the input order. The sim sorts the list in place
(same input sequence and algorithm → same permutation as the game's copy-sort-copy). Checked by
decompiling the installed runtime (`Microsoft.NETCore.App 9.0.16`, `ArraySortHelper<T>` and
`GenericArraySortHelper<T>`): partitions ≤16 use a 2-element swap, a 3-element network
`SwapIfGreater(0,1), (0,2), (1,2)` (not stable: `[b1,b2,a]` → `[a,b2,b1]`, asserted in
`Test_Rng_StableShuffleSortsThenShuffles`), otherwise insertion sort; larger partitions use
median-of-three quicksort with heapsort fallback. The `IComparable<T>` path and the
`IComparer<T>`/`Comparison<T>` path make the same element moves for any antisymmetric comparer and
non-null elements, so the sim adds `StableShuffle(list, rng, IComparer<T>)` and
`StableShuffle(list, rng, Comparison<T>)` for types that cannot implement `IComparable<T>`.

Comparator the loop agent needs for cards (`CardPileCmd.Shuffle` reshuffle,
`Commands/CardPileCmd.cs:2759`), `AbstractModel.CompareTo` (`Models/AbstractModel.cs:126-137`):
same reference → 0, null → 1, else `ModelId.CompareTo` = ordinal `Category` then ordinal `Entry`
(`Models/ModelId.cs:66-74`); `CardModel.CompareTo` (`Models/CardModel.cs:3649-3673`) then compares
`CurrentUpgradeLevel`. For cards `Category` is constant, so: ordinal id, then upgrade level.

## 9. Transformations and stripped sites

1. `MegaRandom.NextBytes` writes with `Unsafe.WriteUnaligned<ulong>` and copies the tail through a
   `byte*` (`MegaRandom.cs:65`, `:78-82`), i.e. native byte order. The sim writes little-endian
   explicitly; every platform the game ships on is little-endian.
2. `StringHelper.GetDeterministicHashCode` uses a `[ThreadStatic]` 1024-byte cache that grows to
   `round(byteCount*1.5)` (`StringHelper.cs:32-33`, `:107-115`). The sim uses a 256-byte
   `stackalloc` or a heap array. Buffer handling has no effect on the hash.
3. `[GeneratedRegex]` source-generated regexes (`StringHelper.cs:35-61`) → `new Regex(pattern)`
   with the same patterns and options.
4. `Enumerable.Sum<TSource>(Func<TSource, float>)` accumulates in `double` and returns `(float)sum`
   (checked in the installed `System.Linq.dll` 9.0.16: `Sum<TSource, float, double>`). The sim's
   `IReadOnlyList<T>` overload of `WeightedNextItem` reproduces it with a `double` loop. The
   `IEnumerable<T>` overload calls LINQ exactly like the game.
5. `NextItem` and `WeightedNextItem` gain `IReadOnlyList<T>` overloads (no `ToArray` allocation).
   They draw the same index (`NextInt(0, count)`), call the weight fetcher the same number of
   times in the same order, and count the same. C# overload resolution picks them for lists and
   arrays.
6. `Rng(Player, ModelId, mixin)` takes explicit `(runSeed | RunRngSet, slotIndex, entry, mixin)`.
7. `SerializableRng` (mutable record class with JSON/packet serialization) → immutable
   `RngSnapshot`; JSON and `PacketWriter` serialization are dropped.
8. No `async`, VFX or wait sites exist in this area.

## 10. Clone and canonical layout

- `MegaRandom`: tag `0x5201`, `S0..S3`. `DeepClone` copies the four words.
- `Rng`: tag `0x5202`, `Counter` (`int32`), `S0..S3`. `RngSnapshot.WriteCanonical` writes the same
  bytes, so a live stream and its snapshot hash identically. `DeepClone` = `new Rng(ToSnapshot())`.
- `RunRngSet`: tag `0x5203`, `StringSeed`, `Seed`, `WriteCount(12)`, then for each stream in
  ordinal order `alias` (`int32`, the lowest ordinal holding the same `Rng` instance) and, only
  when `alias == ordinal`, that stream's `Rng` bytes. The alias table is part of the hash because
  a shared stream behaves differently from twelve equal independent streams. `DeepClone` clones
  each distinct instance once and preserves the alias structure.
- `PlayerRngSet`: tag `0x5204`, `Seed`, `WriteCount(3)`, three `Rng` records (no aliasing path in the game).
- `RunRngSetSnapshot` (`0x5205`) / `PlayerRngSetSnapshot` (`0x5206`): DTOs, entries sorted by ordinal.
- `StringSeed` and `Seed` are hashed because mid-combat spawns seed new monster `Rng`s from `Seed`
  (section 6.5).

## 11. Sim-only additions

- `RunRngSet(ulong seed)` (section 6.2); `RunRngSet.StreamName/StreamCount`, `PlayerRngSet.StreamName/StreamCount`.
- `RunRngSetSnapshot.HashedSeed`: written by `ToSnapshot`; when present `FromSnapshot` rebuilds the
  set from it instead of re-hashing the string seed, so numeric-seed sets round-trip. It is also
  checked by `LoadFromSnapshot`. When absent, the game path (`new RunRngSet(save.Seed)`) runs.
  Sim-only by decision (CONTRACTS §4.3). Consequence: the game's `FromSave` always re-derives
  `Seed` from the string (`RunRngSet.cs:134-140`), so a mock or numeric set (string seed `""`)
  comes back with `Seed = XXH64("") = 0xEF46DB3751D8E999` in the game but keeps its `Seed` in the sim,
  and `LoadFromSnapshot` throws on a `HashedSeed` mismatch where the game's `LoadFromSerializable`
  compares only the string seeds (`:168`). Snapshots in the game's format behave identically.
- `RunRngSet.AliasOf(type)` (alias table used by canonical writing and tests).
- `PlayerRngSet.ForPlayer(stringSeed, slot)`, `Rng.ForPlayerModel` overloads, `MegaRandom.ToSnapshot(counter)`.
- `RngSeeds`: one static method per formula in section 6, plus `LegacyV18Kernel`.

## 12. Verification and known-answer derivation

### 12.1 Independent reference

An independent Python reference (arbitrary-precision integers masked to the C# width; IEEE
doubles; float32 rounding through `struct`) was transcribed from the decompile, not from the C#
port. It produced every expected value in `RngTests.cs`. It was validated three ways:

1. Published vectors: XXH64 of `""` = `0xEF46DB3751D8E999`, `"a"` = `0xD24EC4F1A98C6E5B`,
   `"abc"` = `0x44BC2CF5AD770999`, `"Nobody inspects the spammish repetition"` (39 bytes, stripe
   path) = `0xFBCEA83C8A378BF1`, `"xxhash"` with seed 20141025 = `0xB559B98D844E0635`; splitmix64
   from 0 = `E220A8397B1DCDAF, 6E789E6AA1B965F4, 06C45D188009454F, F88BB8A8724C81EC`.
2. Live game oracle, current format: the user's save
   `~/Library/Application Support/SlayTheSpire2/steam/<id>/modded/profile1/saves/current_run.save`
   (read only, `schema_version` 20), seed `Q3X6PF8S37GA`. For all twelve run streams and all
   three player streams, `XXH64(seed)` → named stream → `counter` kernel steps reproduces the saved
   `s0..s3` exactly. It was observed twice during a live run (counters Shuffle 221→282,
   MonsterAi 3→11, Niche 8→10, Rewards 79→90) and matched both times. This also shows that every
   counted call is exactly one kernel step, and that the player set seed is `XXH64(seed) + slot`
   (saved `8966703988143329197` = `0x7C7021C5035C8FAD`). The second observation is frozen in
   `Test_Rng_GameSaveOracleRunStreams` / `Test_Rng_GameSaveOraclePlayerStreams`.
3. Live game oracle, old format: `current_run_mp.save` (`schema_version` 16, counters only) has run
   seed `3URCWVGADS` and player seeds `3441688739`, `3441688740`.
   `(uint)GetDeterministicHashCodeOld("3URCWVGADS")` = `3441688739` (`int` −853278557), so the old
   hash and the pre-V19 `+ slot` player seeding are confirmed.

### 12.2 Hand trace, `MegaRandom(0)`

splitmix64 #1: `x = 0 + 0x9E3779B97F4A7C15`; `z ^= z>>30` → `0x9E3779BB07979AF0`;
`*0xBF58476D1CE4E5B9` → `0x6F68261B57E7A770`; `z ^= z>>27` → `0x6F682616BAE3641A`;
`*0x94D049BB133111EB` → `0xE220A838BF5C9DDE`; `z ^ z>>31` → **s0 = `0xE220A8397B1DCDAF`**.
Then s1 = `0x6E789E6AA1B965F4`, s2 = `0x06C45D188009454F`, s3 = `0xF88BB8A8724C81EC`.

Step 1: `s1*5` = `0x285B1815289EFDC4`; `rotl 7` = `0x2D8C0A944F7EE214`; `*9` = output
**`0x99EC5F36CB75F2B4`**. `t = s1<<17` = `0x3CD54372CBE80000`; `s2^=s0` → `0xE4E4F521FB1488E0`;
`s3^=s1` → `0x96F326C2D3F5E418`; `s1^=s2` → `0x8A9C6B4B5AADED14`; `s0^=s3` →
`0x74D38EFBA8E829B7`; `s2^=t` → `0xD831B65330FC88E0`; `s3 = rotl(s3,45)` → `0xBC8312DE64D85A7E`.

Derived from output `v = 0x99EC5F36CB75F2B4`: `NextDouble` = `(v>>11)/2^53` =
`5415695640260286/2^53` = 0.6012629994179048 (bits `0x3FE33D8BE6D96EBE`); `NextFloat` =
`(v>>40)/2^24` = `10087519/2^24` (bits `0x3F19EC5F`); `NextInt` = `v>>33` = 1291202459;
`NextUInt` = `0xCB75F2B4`; `NextBool` = MSB = true; `Next(10)` = `(int)(6.0126…)` = 6;
`Next(-5,5)` = 6 − 5 = 1; `Next(int.MinValue, int.MaxValue)` takes the `long` path:
`(int)((long)(0.60126…·4294967295) − 2147483648)` = 434921270. `Rng(0).NextBool()` on the same
output is `Next(2) == 0` → `(int)1.2025… = 1` → false (different rule from the kernel's MSB).

### 12.3 Test inventory

Kernel: splitmix64, seeding (seeds 0, `0x0123456789ABCDEF`, 42), first-step trace, six raw outputs
for two seeds with final state, `NextDouble`/`NextFloat` bit patterns, `NextInt`/`NextUInt`/
`NextBool`, `Next(max)`, `Next(min,max)` including the `long` path, `NextBytes` (13, 16, 0 bytes),
throw-without-stepping. Wrapper (seed 42): bool, int, unsigned int/long (including the
`ulong.MaxValue` shortcut), float/double ranges, gaussian values and per-attempt consumption,
shuffle permutation and final state, `UnstableShuffle` = `Shuffle`, `StableShuffle` (three
overloads and the 3-element sort network), `TakeRandom` eagerness, `NextItem` and
`WeightedNextItem` on both overload families, empty and fallback cases, counter rules (delegation
counted once, validation before increment, `NextInt(0)` increments then throws, one kernel step
per counted call). Snapshot round trips for `Rng`, `RunRngSet` (string, game-style and numeric),
`PlayerRngSet`; seed-mismatch throws. Hash vectors, old hash, determinism across calls (stack and
heap buffer paths, multi-byte UTF-8). Stream names, slugs, name hashes, all seed formulas with
wraparound, legacy V18 kernel, mock seams, alias-preserving clone, canonical bytes (clone equal,
mutation different, shared ≠ unshared).

Gaussian doubles are asserted with a 1e-12 tolerance: `Math.Log/Cos/Sin` come from the platform
libm and are not correctly rounded everywhere. Integer gaussian results and counters are exact.

### 12.4 Runtime checks and isolated compile (2026-10-28)

- .NET behavior the port relies on was checked on the installed runtime 9.0.16 (F# interactive,
  no project build): the `SnakeCase`/`Slugify` regex results for all stream names (and `ABC` →
  `a_b_c`), the gaussian values and counters above (bit-identical to the Python reference on this
  machine), and the 3-element `List.Sort` network.
- An in-memory Roslyn compile of the Random files, `State/CanonicalWriter.cs` and
  `Enums/CombatEnums.cs` (scratch copies, nullable on, unchecked) gave 0 errors and 0 warnings;
  all 35 `RngTests` passed. A scratch compile of the whole current Core + Tests snapshot also gave
  0 errors. Neither touched `sim/` build outputs; the integrate agent's real build is still the gate.

## 13. Consumers outside this area (for the loop and monster agents)

| Consumer | Stream and call | Citation |
| --- | --- | --- |
| opening draw pile shuffle | `Shuffle` via `UnstableShuffle`, then `Hook.ModifyShuffleOrder(isInitialShuffle: true)` | `Combat/CombatManager.cs:6605` → `Entities.Players/Player.cs:1214-1234` → `Entities.Cards/CardPile.cs:241-246` |
| reshuffle discard into draw | `Shuffle` via `StableShuffle` on discard cards (pile order) followed by draw cards (pile order, through `ToHashSet` with no removals), then `Hook.ModifyShuffleOrder(isInitialShuffle: false)`; comparator in section 8 | `Commands/CardPileCmd.cs:2754-2760` |
| insert card at random pile position | `Shuffle.NextInt(count + 1)` | `Commands/CardPileCmd.cs:311`, `:3431` |
| random card from draw pile | `CombatCardSelection.NextItem` | `Commands/CardPileCmd.cs:1612` |
| random attack target | `CombatTargets.NextItem(validTargets)` | `Commands.Builders/AttackCommand.cs:1061-1062`, `Commands/CardCmd.cs:411`, `:431` |
| monster move roll | `RunRng.MonsterAi`; `RandomBranchState` draws `NextFloat(totalWeight)` where `totalWeight` is a LINQ float `Sum` (double accumulator) | `Models/MonsterModel.cs:721`, `MonsterMoves.MonsterMoveStateMachine/RandomBranchState.cs:135-136` |
| monster HP roll | `Niche`: values `[min, max]` not used by other enemies' `MaxHp`, from a `HashSet<int>` built from an ascending range with only removals, so enumeration is ascending; `NextItem(IEnumerable)` on it, or `NextInt(min, max+1)` when empty | `Combat/CombatState.cs:717`, `Entities.Creatures/Creature.cs:972-984` |
| per-monster `Rng` | section 6.5 (cosmetic uses only in P0 monsters) | `Combat/CombatState.cs:721` |
| encounter `Rng` | section 6.5 (e.g. `SlimesNormal` `NextBool`) | `Models/EncounterModel.cs:211-215`, `Models.Encounters/SlimesNormal.cs:46` |

## 13a. Integration notes (2026-10-28)

- `RngCollectionExtensions` is the only definition of `UnstableShuffle` / `StableShuffle` /
  `TakeRandom` in Core (checked at integration; the loop calls these, and `LoopTests` keeps its own
  private reference shuffle under a different name), so no extension ambiguity exists.
- `DeterministicHash.XxHash64` is now the single XXH64 body in Core: `StateHasher.XxHash64` delegates
  to it.

## 14. Deferred (not needed for P0 combat)

- `Helpers/GrabBag.cs` (weighted bag on `NextDouble() * totalWeight`, cumulative `<`, rejection
  loop with predicate, `GrabAndRemove`), used by `Models/ActModel.cs` encounter selection.
- `SeedHelper.GetRandomSeed` (`Helpers/SeedHelper.cs:13-33`) needs `BadWordChecker`.
- Odds and grab bags: `RunOddsSet`, `PlayerOddsSet`, `RelicGrabBag`, card rarity odds.
- Ad-hoc named or derived streams outside combat: `act_{n}_map` (`Map/StandardActMap.cs:101`,
  `Models.Modifiers/BigGameHunter.cs:16`), `spoils_map` (`Map/SpoilsActMap.cs:64`),
  `map_point_selection` (`Multiplayer.Game/MapSelectionSynchronizer.cs:150`), `event_synchronizer`
  (`Multiplayer.Game/EventSynchronizer.cs:163`), `act_selection` seeded from `XXH64(seed text)`
  (`Multiplayer.Game.Lobby/StartRunLobby.cs:705`), daily-run child streams
  (`Nodes.Screens.DailyRun/NDailyRunScreen.cs:919-922`), and cosmetic ones (`map_jitter_{act}`,
  `bg_music`, `NCombatRoom.GenerateBackgroundRngForCurrentPoint`, split-vote animations,
  `NMapCircleVfx`). All of them are `new Rng(seed[, name])` and can be built with the ported API.
- `NextGaussianInt` is only used by map generation (`Models.Acts/Overgrowth.cs:136`,
  `Underdocks.cs:114`, `Hive.cs:119`, `Map/MapPointTypeCounts.cs:59`).
