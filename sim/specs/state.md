# State area: canonical state, hashing, determinism

Owner: state agent. Files: `sim/Sts2Sim.Core/State/{CanonicalWriter,StateHasher,StateCanonicalTags,CardPile,CreatureState,CombatState}.cs`,
tests `sim/Sts2Sim.Tests/DeterminismTests.cs` (`dotnet run --project sim/Sts2Sim.Tests -- State`).

Decompile root: `/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`, cited as
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped. Line numbers are the raw file's
(including the `//IL_` comment lines).

## 1. What was ported

### 1.1 CombatState (`Combat/CombatState.cs`)

| Sim member | Game source | Notes |
| --- | --- | --- |
| `RoundNumber = 1`, `CurrentSide = CombatSide.Player` at construction | `Combat/CombatState.cs:654-664` (lines 658-659) | Round increments when the side switches back to the player: `Combat/CombatManager.cs:7457-7473`. |
| `NextCreatureId`, `AllocateCreatureId()` (post-increment) | `_nextCreatureId` `Combat/CombatState.cs:539`, `AttachCreature` `:726-731` | Game field is `uint` starting at 0. The sim constructor sets `NextCreatureId = player.CombatId + 1` because the player is attached first (`AddPlayer` `:703-707`). Callers may still overwrite it. |
| `Monsters` list order, insertion at the end | `_enemies` `:537`, `AddCreature` `:993-1008` | |
| `AddMonster(m)` throws `InvalidOperationException` when already present | `AddCreature` `:1002-1005` | Message copied. The game's "created for a different combat" check has no sim analogue (no back-reference). |
| `RemoveMonster(m)` throws when absent | `RemoveCreature` `:739-768` (lines 751-760) | `unattach` / `CreaturesChanged` dropped (section 3). |
| `GetCreatureById(id)`: player first, then monsters in order, `null` if absent | `GetCreature` `:784-791` over `Creatures` = allies ++ enemies `:558` | |
| `GetCreatures(buffer)`: clears the buffer, then player, then monsters | `Creatures` `:558` | Buffer API instead of a fresh list. |
| `GetHittableEnemies(buffer)`: clears, then each monster with `!IsDead && Hook.ShouldAllowHitting(cs, m)` | `HittableEnemies` `:622`, `Creature.IsHittable` `Entities.Creatures/Creature.cs:640-654`, `Hook.ShouldAllowHitting` `Hooks/Hook.cs:11235-11253` | |
| `HasAscension`, `GetValueIfAscension` | `Entities.Ascension/AscensionManager.cs:22-24` (per CONTRACTS) | Unchanged scaffold code. |
| `Outcome`, `NextCardInstanceId`, `AllocateCardInstanceId` | sim-only | `CombatOutcome` has no game enum (CONTRACTS section 9). Card instance ids are the sim's stand-in for object identity. |
| `IsInProgress` (default false), `IsStarting` (default false) | `CombatTurnState.IsInProgress` / `IsStarting` (`Combat/CombatTurnState.cs:99-115`), read through `CombatManager.IsInProgress` / `IsStarting` (`Combat/CombatManager.cs:6113`, `:6117`), which are false while no turn state exists | A new `CombatState` stands for "no turn state yet" (encounter spawn), so both start false. The loop sets `IsStarting` where the game creates the turn state (whose own default is `IsStarting = true`), and flips both at combat start; `IsInProgress` goes false at victory or when a loss is processed (loop spec §2.2, §3.10). Cloned and hashed. Hand-built test states set `IsInProgress = true`. |

### 1.2 CreatureState / PlayerState / MonsterState (`Entities.Creatures/Creature.cs`, `Entities.Players/*`, `Models/MonsterModel.cs`)

| Sim member | Game source | Notes |
| --- | --- | --- |
| `Block` setter throws `ArgumentException("Block must be positive")` for negatives | `Creature.cs:354-374` | |
| `CurrentHp` setter throws `ArgumentException("Current HP must be positive")` for negatives | `Creature.cs:376-396` | |
| `MaxHp` setter has no check | `Creature.cs:398-413` | |
| Constructors write HP fields directly, no validation | `Creature.cs:946-970` | |
| `IsAlive` (`CurrentHp > 0`), `IsDead`, `IsEnemy` | `Creature.cs:541-543`, `:614` | |
| `Powers` list, insertion order is hook order | `_powers` `Creature.cs:350`, `ApplyPowerInternal` `:1182-1196` | Adding/removing is done by the pipeline (`PowerModel.ApplyInternal/RemoveInternal`). |
| `GetPower<T>`, `GetPower(id)`, `HasPower<T>`, `HasPower(id)`, `GetPowerAmount<T>` (first match in list order) | `Creature.cs:1142-1180` | |
| `MonsterState.IsStunned` = next move id `"STUNNED"` | `Creature.cs:672` | |
| `MonsterState.MaxHpBeforeModification` | `MonsterMaxHpBeforeModification` `Creature.cs:415-422`, set at `:983` | |
| `MonsterState.SpawnedThisTurn` | `MonsterModel.cs:346`, `:530-541`; set true in `SetUpForCombat` `:713-717`; cleared in `OnSideSwitch` `:774-778`; read by `Creature.TakeTurn` `Creature.cs:283` (a monster spawned this turn does not act) | Added to the state contract (CONTRACTS §4.4); written by `MonsterLifecycle.SetUpForCombat` / `OnSideSwitch`, read by `TakeTurn`. |
| `PlayerState.TurnNumber = 1` at construction | `Entities.Players/PlayerCombatState.cs:42-48` | Incremented on the switch back to the player side: `CombatManager.cs:7474-7486`. |
| `Phase` starts at `PlayerTurnPhase.None`; `Energy`, `Stars` start at 0 | `PlayerCombatState.cs:24-28`, `:50-68`, `:119-154` | |
| `AllPiles` order Hand, Draw, Discard, Exhaust, Play | `PlayerCombatState.cs:105-115` | Same order as the game's hook iteration over cards (`CombatState.cs:895-913`) and the game checksum (`Entities.Multiplayer/NetFullCombatState.cs:502-506`). |
| `GetPile(type)`; `None`/`Deck` throw | `CardPile.Get` `Entities.Cards/CardPile.cs:220-234` | The game returns `null` for `None` and the run deck for `Deck`; neither exists in a sim combat. |
| `GainStars`/`LoseStars` (negative amount throws, floor at 0) | `PlayerCombatState.cs:355-373` | |
| `IncrementTurnNumber()` | `PlayerCombatState.cs:299-302` | |
| `EndOfTurnCleanup()` over every card of `AllPiles` in order | `PlayerCombatState.cs:411-426`, `AllCards` `:117` | Calls `CardInstance.EndOfTurnCleanup()` (cards agent). |
| `IsActiveForHooks` (starts as `currentHp > 0`), `ActivateHooks()`, `DeactivateHooks()` | `Entities.Players/Player.cs:214-221`, `:703`, `:1278-1286`; callers `Commands/CreatureCmd.cs:2850` (player death) and `Creature.HealInternal` `Creature.cs:1067-1076` (revive) | Hook iteration skips an inactive player's powers: `CombatState.Contains` `CombatState.cs:1061`. |
| `FindPileContaining(card)` | sim-only | Replaces the game's `CardModel.Pile` back-reference. |

Not ported here because the pipeline area already owns them (one source of truth): `DamageBlockInternal`,
`LoseHpInternal`, `GainBlockInternal`, `LoseBlockInternal`, `HealInternal`, `SetCurrentHpInternal`,
`SetMaxHpInternal`, `BeforeTurnStart` (`Creature.cs:1026-1092`, `:1251-1268`) live in
`Combat/Pipeline/CreatureInternals.cs`; `ResetEnergy`, `AddMaxEnergyToCurrent`, `GainEnergy`, `LoseEnergy`,
`MaxEnergy` (`PlayerCombatState.cs:136`, `:304-332`) live in `Combat/Pipeline/EnergyCmd.cs`.

The game's `Creature.IsHittable` (`Creature.cs:640-654`) also consults `Hook.ShouldAllowHitting`, which needs the
combat, and the sim creature has no combat back-reference. It is therefore not a `CreatureState` member: use
`CombatState.GetHittableEnemies` or `CombatStatus.IsHittable(cs, creature)` (overrides of `ShouldAllowHitting` exist in
`ReattachPower`, `IllusionPower`, `DieForYouPower`, `AdaptablePower`, `Models.Powers/*.cs`, none in P0). The earlier
`[Obsolete]` `MonsterState.IsHittable` placeholder (it returned `IsAlive`) had no caller and was deleted in the
2026-10-30 contract sync.

### 1.3 CardPile (`Entities.Cards/CardPile.cs`)

| Sim member | Game source |
| --- | --- |
| `MaxCardsInHand = 10` | `CardPile.cs:35` |
| `IsCombatPile` / `IsCombatPileType(type)`: `(uint)(type - 1) <= 4` (Draw..Play) | `CardPile.cs:48`, `Entities.Cards/PileTypeExtensions.cs:25-32` |
| `AddInternal(card, index = -1)`: throws if the pile already holds the card; `Insert(index)` when `index >= 0`, else append | `CardPile.cs:248-273` |
| `RemoveInternal(card)`: throws `InvalidOperationException` if absent, else removes and returns `true` | `CardPile.cs:275-293` |
| `TryRemoveInternal(card)` | sim-only, non-throwing variant |
| `MoveToBottomInternal` (remove, append), `MoveToTopInternal` (remove, insert at 0) | `CardPile.cs:295-315` |
| `ClearInternal()` | `CardPile.cs:317-335` |
| Card identity is reference identity (`IndexOf`/`Contains` use `ReferenceEquals`) | `Enumerable.Contains` on `CardModel`, which has no value equality |

List index 0 is the pile's "top" (`MoveToTopInternal` inserts at 0). Which end the game draws from is documented
by the loop agent.

## 2. Canonical serialization and hash

### 2.1 `CanonicalWriter`

Growable little-endian byte buffer (initial capacity clamped to at least 16; doubles on growth; throws above
`Array.MaxLength`). Encodings:

| Method | Bytes |
| --- | --- |
| `WriteByte` | 1 |
| `WriteBool` | 1 byte, 0 or 1 |
| `WriteInt32` / `WriteUInt32` | 4, LE |
| `WriteInt64` / `WriteUInt64` | 8, LE |
| `WriteSingle` / `WriteDouble` | raw IEEE bit pattern as UInt32 / UInt64 LE (`-0f` and `0f` differ) |
| `WriteDecimal` | 14 bytes: scale byte, sign byte, 96-bit mantissa as three UInt32 LE (lo, mid, hi). Trailing zeros are stripped while scale > 0 (`1.5m`, `1.50m`, `3m/2m` are identical); any zero is written as `+0` scale 0. |
| `WriteString` | Int32 UTF-8 byte count, then the bytes; `null` is count `-1` |
| `WriteTag` | UInt16 LE |
| `WriteCount` | Int32 LE; negative counts throw |
| `WriteNullableInt32` | presence bool, then Int32 when present |
| `WriteBytes` | raw bytes, no length prefix |

### 2.2 Layout of the full combat state

Superseded in W1 by §W1.8 (the run fields come first, the player is written once through the run).

Tags: `StateCanonicalTags` 0x5301-0x5306. The other areas use rng 0x52xx, powers 0x54xx, monsters 0x4Dxx and cards 0xCAxx, so no tag is shared.

```
CombatState  tag 0x5301, EncounterId:str, Ascension:i32, RoundNumber:i32, CurrentSide:i32, Outcome:u8,
             IsInProgress:bool, IsStarting:bool,
             NextCreatureId:i32, NextCardInstanceId:i32, Player, tag 0x5306 count:i32 Monster*,
             hasRng:bool RunRngSet (rng agent: every stream's Counter and S0..S3, aliasing as its own format)
Player       tag 0x5302, Creature, CharacterId:str, BaseMaxEnergy, Energy, Stars, TurnNumber, Phase (i32 each),
             IsActiveForHooks:bool, piles Hand, Draw, Discard, Exhaust, Play
Monster      tag 0x5303, Creature, MonsterId:str, SlotName:str?, MaxHpBeforeModification:int?,
             SpawnedThisTurn:bool, hasExtra:bool MonsterExtraState, hasAi:bool MonsterAiRuntime (monsters agent)
Creature     tag 0x5305, CombatId, Side, CurrentHp, MaxHp, Block (i32 each), count:i32 PowerModel* (list order,
             pipeline agent's PowerModel.WriteCanonical: id, amount, turn-start amount, flags, applier/target ids, extras)
CardPile     tag 0x5304, Type:i32, count:i32 CardInstance* (pile order; cards agent's CardInstance.WriteCanonical)
```

Every list is written in list order because order is rule state (pile order, power hook order, monster turn
order). Nothing in the state is a set or dictionary at this level. Transient data is not written: the
`PlayerState.AllPiles` cache, `CombatSim`'s legal-action buffer, `CardPlay`.

### 2.3 `StateHasher`

`Hash(bytes)` = XXH64 with seed 0 over the canonical bytes, written by hand (no `System.IO.Hashing`, which is a
NuGet package). Constants are the XXH64 primes `0x9E3779B185EBCA87`, `0xC2B2AE3D27D4EB4F`, `0x165667B19E3779F9`,
`0x85EBCA77C2B2AE63`, `0x27D4EB2F165667C5` (XXH64 specification, not the decompile). Since integration there is one
XXH64 body in Core: `StateHasher.XxHash64(data, seed)` delegates to the rng area's `DeterministicHash.XxHash64`
(the two hand-written bodies were identical); both vector tests still run against it. `Hash(ICanonical, scratch)`
resets the scratch writer (or allocates a 4 KB one), writes, hashes. `CanonicalBytes(state)` returns a fresh copy of
the bytes for diffing.

Test vectors (`Test_State_HasherMatchesXxHash64Vectors`): published XXH64 values for `""`, `"a"`, `"abc"`,
`"Nobody inspects the spammish repetition"`, plus sequential-byte inputs of length 8, 31, 32, 33, 48, 63, 64, 100, 257
that cover every tail path. The sequential values were computed with an independent Python implementation, and
their low 32 bits were checked against libzstd's XXH64 frame checksums. `Test_State_HasherAgreesWithRngXxHash64`
cross-checks against the rng agent's `DeterministicHash.XxHash64` for lengths 0-300.

Reference only, the game's own lock-step checksum: `ChecksumTracker.GenerateChecksum` hashes the `PacketWriter`
serialization of `NetFullCombatState` with `XxHash32`, seed 0 (`Multiplayer.Game/ChecksumTracker.cs:356-363`).
`NetFullCombatState.FromRun` includes the creatures in `Creatures` order (monster id or player id, hp, max hp, block,
powers as id + amount), each player's turn number, phase, energy, stars, potions, relics, orbs, gold, the five piles
in Hand/Draw/Discard/Exhaust/Play order (only while the combat is in progress), the player rng set without
Rewards/Shops, and the run rng set (`Entities.Multiplayer/NetFullCombatState.cs:420-549`, `:551-598`). The sim's
canonical form covers the same combat fields and adds every mutable card, power and monster-AI field. The two hashes
are not meant to match.

## 3. Stripped Godot/VFX/coroutine/event sites

None of these change the order of rule side effects in P0. They notify UI, history, or the network.

| Game site | Transformation |
| --- | --- |
| `Creature` events `BlockChanged`, `CurrentHpChanged`, `MaxHpChanged`, `PowerApplied/Increased/Decreased/Removed`, `Died`, `Revived` (`Creature.cs:318-342`, raised in the setters `:354-413`) | Dropped; setters keep their validation only. |
| `CombatState.CreaturesChanged` (`CombatState.cs:533`, `:624-652`, raised `:767`, `:1007`) | Dropped. |
| `CombatState.GetCreatureAsync` / `GodotTimerTask` (`CombatState.cs:793-829`, `:1064-1075`): waits for a creature id to spawn over the network | Dropped; single-player lookups are synchronous (`GetCreatureById`). |
| `CardPile` events `ContentsChanged`, `CardAdded`, `CardRemoved`, `CardAddFinished`, `CardRemoveFinished` and the `silent` parameter (`CardPile.cs:20-212`, `:248-293`, `:337-367`) | Dropped. |
| `CombatManager.Instance.StateTracker.Subscribe/Unsubscribe` on cards and piles (`CardPile.cs:264-267`, `:283-286`; `PlayerCombatState.cs:257-277`, `:279-297`) | Dropped (UI state tracking). |
| `PlayerCombatState` events `PlayerTurnPhaseChanged`, `EnergyChanged`, `StarsChanged` (`PlayerCombatState.cs:30-37`, `:50-154`) | Dropped. |
| `Stars` setter calls `CombatManager.Instance.History.StarsModified` (`PlayerCombatState.cs:150`) | Dropped with the combat history (section 4). W1: the append is `StarsCmd`'s (CONTRACTS §B12 A16), see §W1.4.6. |
| `Creature.HoverTips`, `Name`, `LogName`, `HpDisplay`, `CreateVisuals`, `GetCreatureNode`, `SetNodeVisible`, VFX containers (`Creature.cs:475-612`, `:995-1011`, `:1341-1402`) | Dropped. |

## 4. Deferred, not in the P0 state

W1 status (§W1): combat history, pets, the orb queue, relics, potions and gold are now in the state; escaped
creatures, the all-cards registry and the per-monster `Rng` stay deferred (§W1.12).

Each of these must join `WriteCanonical` and `DeepClone` when it is ported.

- Combat history (`Combat.History/CombatHistory.cs`): append-only log of card plays, draws, damage, block, energy, powers, moves. Cards and powers that ask "how many X this turn/combat" need it, as a value-type log or counters.
- Escaped creatures (`CombatState.cs:543`, `:733-737`), the all-cards registry (`:545`, `:666-701`), modifiers, badges and multiplayer scaling (`:564-585`).
- Pets (`PlayerCombatState.cs:20`, `:375-389`, `:432-445`), the orb queue (`:156-161`), relics, potions, gold.
- Per-monster `Rng` (`CombatState.cs:721`): seeded from run seed + map coordinates + act + combat id. In this decompile it is only read for `ToughEgg`'s skin (`Models.Monsters/ToughEgg.cs:552`). If a ported monster rolls rules with `base.Rng`, it belongs in `MonsterState` or the monster's extra state.
- `CardPile.RandomizeOrderInternal` (`CardPile.cs:241-246`, opening shuffle + `Hook.ModifyShuffleOrder`): the loop agent owns shuffling.
- `SortEnemiesBySlotName` / `SetEnemyIndex` (`CombatState.cs:958-980`), multiplayer HP scaling (`Creature.cs:986-993`).
- `PlayerCombatState.HasEnoughResourcesFor` (`:334-353`): the cards and loop agents implement playability.

## 5. Clone contract as implemented

Superseded in W1 by §W1.2 and §W1.3.4 (a combat clones through its run; the player is cloned once and re-linked).

- `CombatState.DeepClone`: clones the player, every monster in order, and the `RunRngSet` (aliasing inside the set is the rng agent's job). It copies the scalars and shares nothing mutable. A private copy constructor is used, so the public constructor's defaults are not re-applied.
- `PlayerState.DeepClone`: copies scalars and the five piles. `CopyCreatureFieldsInto` copies side, hp, max hp and block, and clones each power with `PowerModel.CloneFor(clone)`, so `Owner` is rebound. The `AllPiles` cache is rebuilt lazily.
- `MonsterState.DeepClone`: shares `Def` and copies scalars. It clones `Extra` with `MonsterExtraState.DeepClone()` and the AI with `MonsterAiRuntime.DeepClone()`, which shares the immutable move graph.
- `CardPile.DeepClone`: clones each `CardInstance` in order; the `CardDef` is shared.
- A card lives in exactly one pile, and cross-references are ids (`CombatId`, `InstanceId`), so the clone needs no identity map. The tests assert both invariants: unique cards and ids, and power owners rebound.
- Allocation per clone: one object per creature, pile list, card, var set, var, energy cost, and power, plus the rng set. There is no LINQ and no dictionary.

## 6. Tests (`sim/Sts2Sim.Tests/DeterminismTests.cs`)

Unit tests (state area only, plus cards' `CardInstance` and pipeline's `PowerModel` basics):
`WriterEncodesPrimitivesLittleEndian`, `WriterEncodesStrings`, `WriterNormalizesDecimalScale`,
`WriterUsesFloatBitPatterns`, `WriterGrowsAndResets`, `HasherMatchesXxHash64Vectors`, `HasherAgreesWithRngXxHash64`,
`HashOfCanonicalMatchesHashOfBytes`, `CardPileFollowsGamePileRules`, `CardPileCloneIsIndependent`,
`CreatureSettersAndPowerLookups`, `PlayerAndCombatStartValues` (a new combat state is neither in progress nor
starting), `CreatureQueriesFollowAlliesThenEnemies`,
`HittableEnemiesSkipDeadMonsters` (needs `Hook.ShouldAllowHitting`), `ManualStateCloneRoundTrips` and
`EveryFieldChangesTheHash` (40 single-field mutations of a hand-built state, including the in-progress and
starting flags; each must change the hash, leave the original untouched, and not collide with another),
`ManualCloneSharesNoMutableReferences`,
`GraphInspectorFlagsSharedMutableObjects` (self-test of the reflection walker).

Integration tests (need the whole combat; encounter `NIBBITS_WEAK`, else the first registered one; deck
`StartingDecks.Ironclad`; a seeded SplitMix64 test policy that plays a card 7/8 of the time):
1. `CombatSimCloneSharesNoMutableReferences`: a reflection walk over every object reachable from a `CombatSim` and from its clone, at combat start, mid-fight (after two end-turns), and clone-of-clone. It fails on any object reachable from both graphs unless that object is deeply immutable. The allowlisted immutable types are `string`, reflection objects, comparers, `CardDef`, `MonsterDef`, `EncounterDef`, `MonsterMoveStateMachineDef`, `AiStateDef`, `AbstractIntent`. Also deeply immutable: zero-length arrays, and objects whose fields are all `readonly` and themselves deeply immutable. Delegate closures are followed. Boxed structs are walked inline.
2. `SameSeedSameScriptSameHashes`: for three seeds, a policy run and a replay of its action script give identical hash sequences, outcomes and final canonical bytes. An independent rerun gives the same script, and a neighbouring seed gives a different initial hash.
3. `CloneBranchesDivergeWithoutTouchingOriginal`: clone mid-fight, then play a card on the clone and run it for 80 more steps. The original must keep its hash and canonical bytes. Then end the turn on the original: the hashes diverge, and the original stays in lock-step with a never-cloned control combat for 120 steps. The clone must stay unchanged while the original steps.
4. `CloneReplaysIdentically`: original, clone and clone-of-clone take the same actions until the combat ends or 400 steps. Legal actions, hashes and outcome must match at every step.
5. `CloneHashEqualsSourceHashAlongTrajectory`: along a trajectory, `Hash(x) == Hash(x.Clone())`, the scratch-writer hash and `CombatSim.WriteCanonical` all agree.
6. `CloneStaysWithinBudget`: the mean of 500 mid-fight `CombatSim.Clone()` calls, after warm-up, must stay under the P0 budget of 1 ms (`docs/p0-sim-port-plan-2026-10-28.md`). This is a regression guard; `Sts2Sim.Bench` gives the precise figure.

## 7. Notes for integration

- `StateHasher.Hash(state)` without a scratch writer allocates a 4 KB buffer per call. Since integration
  `CombatSim.StateHash()` keeps one lazily created `CanonicalWriter` per `CombatSim` instance (a clone gets its
  own, because `Clone` goes through the constructor), so repeated hashing of one sim does not allocate. A search
  loop that hashes many sims on one thread can still pass a shared writer to `StateHash(CanonicalWriter)`.
- The hash includes card instance ids and creature ids. Two states equal up to a renaming of ids hash differently. Transposition detection that ignores ids needs a separate canonicalization.
- `CombatState` tolerates a null `Rng` (presence flag in the canonical form) so that pure-rule tests can build a state
  without RNG. The game always has one. Kept as a documented test convenience (orchestrator decision 2026-10-30,
  CONTRACTS §4.4); `CombatLoop`/`CombatSim` always pass a `RunRngSet`.

## W1. State kernel v2 (2026-11-01, CONTRACTS Part B §B2 as amended by §B12 A10–A13, A19, A22)

Owner: state agent, W1 round 3. Files: `sim/Sts2Sim.Core/State/*`, `sim/Sts2Sim.Core/Run/RunState.cs` (shell body);
tests `sim/Sts2Sim.Tests/StateKernelTests.cs` (`dotnet run --project sim/Sts2Sim.Tests -- StateKernel`). Base
`263810d`. Rule truth: decompile v0.111.0, cited `<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped. The
phase-0b bodies the scaffold landed in this area (§B12 A10, A12, A13, `CombatState.CreateCard`, the 17
`CombatHistory.Append*`, the history clone) were re-verified line by line; where this section and
`scaffold-w1.md` §16 differ, this section is the owner's record.

### W1.1 Superseded P0 text

§2.2 (layout) is replaced by §W1.8; the §3 row on the `Stars` setter by §W1.4.6; §4 (deferred) by this section,
with the remaining deferrals in §W1.12; §5 (clone contract) by §W1.2 and §W1.3.4. The P0 tests and their text in §6
are unchanged and still pass.

### W1.2 Run shell (`Run/RunState.cs`, game `Runs/RunState.cs:26`)

| Sim member | Game source | Port |
| --- | --- | --- |
| `Players`, `Rng`, `Unlocks`, `Ascension`, `Modifiers`, `Choice`, `Fidelity` | `Runs/RunState.cs`; D3–D7 | shell data (W2 fills the rest of the type) |
| `CreateStandalone(player, rng, ascension, unlocks = null)` | `Entities.Ascension/AscensionManager.cs:27-39` (TightBelt `:29-32`), applied once per player at new-run setup (`Runs/RunManager.cs:3856-3866` → `ApplyAscensionEffects` `:4827-4830`) and when a player joins a run in progress (`Runs/RunState.cs:977-981`) | A12: TightBelt (`SubtractFromMaxPotionCount(1)` at ascension ≥ `TightBelt` = 4, `Entities.Ascension/AscensionLevel.cs`) runs only while `PlayerState.AscensionEffectsApplied` is false, and the flag is then set at every ascension. A second call on the same player, directly or through `new CombatState(player, …)`, leaves the slot count unchanged (tested for A0–A10). The AscendersBane half (`:33-38`) stays with the deck specs (`scaffold-w1.md` M27). `Unlocks` defaults to `UnlockProfile.All` (D5); modifiers empty; fresh `ChoiceRuntime` and `FidelityLog`; `Combat` null until a combat attaches |
| `NextCardInstanceId`, `AllocateCardInstanceId()` | §B12 A13 (sim-only stand-in for object identity, Part A §5) | post-increment; copied by both clone paths; written once, in the run fields right after `Ascension` |
| `Combat` (setter) | the run's attached combat | `null` detaches; a combat whose `Run` is another run is rejected with `InvalidOperationException`, so an attached combat always points back at its run |
| `DeepClone()` / `CloneWithoutCombat()` | D3, D4; Part A §6 | every player cloned once (`PlayerState.DeepClone`), the rng set once (`RunRngSet.DeepClone`, its own aliasing rules), each modifier (`ModifierModel.DeepClone`), the choice runtime and the fidelity log once; `Unlocks` shared (deeply immutable, M18); then the attached combat once through `CombatState.CloneInto(clone, this)`, which re-links the combat's player by its index in `Players` |
| `WriteCanonical` / `WriteRunFields` | Part A §6 | run fields (tag `0x5D01`, ascension, card-id allocator, unlock digest, player list `0x5D02`, rng presence + rng, modifier list `0x5D03`, choice runtime, fidelity log), then a combat presence byte and the combat fields. `CombatState.WriteCanonical` writes the same run fields and then the combat fields, so the run form is the combat form plus one presence byte and every shared object (player, rng, choice) is written once (`Test_State_RunCanonicalWritesEachSharedObjectOnce`) |

### W1.3 Combat state (`State/CombatState.cs`, game `Combat/CombatState.cs`)

#### W1.3.1 Creature lists

| Sim member | Game source | Port |
| --- | --- | --- |
| `Allies` (`[0]` = the player, then pets in insertion order) | `_allies` `:535`, `Allies` `:554`; the player joins first through `AddPlayer` `:703-707` | the constructor seeds `[player]`; pets join through `AddAlly` / `AddCreature` |
| `Monsters` | `_enemies` `:537`, `Enemies` `:556` | unchanged P0 list (Part A §4.4) |
| `AddCreature(creature)` | `AddCreature` `:993-1008` | duplicate check over both lists (`ContainsCreature`, message copied from `:1002-1005`), then `Side == Player` → `_allies`, else `Monsters`. The game's first check ("created for a different combat", `:997-1000`) has no sim analogue (no creature → combat back-reference). A `PlayerState` on the enemy side is rejected (`ArgumentException`): the sim's enemy list is typed `MonsterState` |
| `AddMonster(monster)` | the P0 name of `AddCreature` for monsters | forwards to `AddCreature`, so a player-side monster goes to the allies exactly as the game routes it (finding "AddMonster appends to the enemy list whatever the creature's Side") |
| `AddAlly(pet)` | `AddCreature` on `_allies` (the pet path: `PlayerCmd.AddPet` → `CreatureCmd.Add` → `AddCreature`, `Commands/PlayerCmd.cs:946-967`) | requires `Side == Player` (`ArgumentException` otherwise, because the game would route such a creature to the enemies), then `AddCreature` |
| `RemoveCreature(creature)`, `RemoveMonster`, `RemoveAlly` | `RemoveCreature` `:739-768` | enemies are searched first, then allies (`:751-762`); absent → `InvalidOperationException` with the game's message. Two sim restrictions: the game's silent return for an unattached creature (`:743-746`) has no analogue (no attachment back-reference), so a caller that may remove twice checks `ContainsCreature` first; the v0.111.0 call sites cannot remove twice (`Commands/CreatureCmd.cs:2812-2822` removes only an enemy still in `Enemies`; `Models/MonsterModel.cs:288-291` removes a monster that died during its own move, which `Kill` left in place); and the combat's own player is never removed (`Allies[0]` invariant; the game removes every creature only in the combat teardown `CombatManager.Reset`, `Combat/CombatManager.cs:7058-7074`) |
| `ContainsCreature(creature)` | `ContainsCreature` `:770-777` | reference search over allies, then enemies |
| `GetCreatureById(id)` | `GetCreature` `:784-791` over `Creatures` = allies ++ enemies `:558` | allies first (player, pets), then monsters; `-1` / unknown → `null` |
| `GetCreatures(buffer)` | `Creatures` `:558` | allies then monsters, into a cleared buffer |
| `GetCreaturesOnSide(side)` (live list) and `(side, buffer)` (snapshot) | `GetCreaturesOnSide` `:831-838` | `side != Enemy` → `Allies`, else `Monsters`; the game returns the live list, so does the sim (callers that need the game's `ToList` snapshots copy, §B12 A17) |
| `GetOpponentsOf(c)`, `GetTeammatesOf(c)` | `:840-848` | `GetCreaturesOnSide(OppositeSide(c.Side))` / `(c.Side)`; `OppositeSide` is `CombatStatus.OppositeSide` |
| `CreaturesOnCurrentSide` | `:620` | `GetCreaturesOnSide(CurrentSide)` |
| `GetHittableEnemies(buffer)` | `HittableEnemies` `:622`, `Creature.IsHittable` `Entities.Creatures/Creature.cs:640-654` | unchanged P0 port; pets are allies and never hittable enemies (tested) |
| `SetEnemyIndex(monster, index)` | `SetEnemyIndex` `:966-980` (no v0.111.0 caller; listed in §B8) | slots from `EncounterRegistry` by `EncounterId` (an unregistered encounter throws `InvalidOperationException`, where the game's null `Encounter` would throw a null reference); slotted encounter → `InvalidOperationException` (`:970-973`); not an enemy → `ArgumentException` (`:974-977`); `Remove`, then `Insert(Math.Min(index, Count - 1))` with `Count` read after the removal (`:978-979`), so an index past the end lands one before the last monster and a single enemy throws `ArgumentOutOfRangeException` at `Insert(-1)`, as in the game |
| `InvokeDiedEvent(creature)` | `Creature.InvokeDiedEvent` `Entities.Creatures/Creature.cs:1100-1103`, raised in `KillWithoutCheckingWinCondition` right after the `ShouldDie` gate (`Commands/CreatureCmd.cs:2768-2770`) | the sim has no events; this delivers the one state-owned subscription, `PlayerCombatState.OnPetDied` (subscribed in `AddPetInternal`, `Entities.Players/PlayerCombatState.cs:384`): a monster with `PetOwnerId` whose owner (resolved by id among the creatures) still lists it in `PetIds` → `owner.OnPetDied(cs, pet)`. Every other creature is a no-op. Monster-side `Died` subscribers (`Models.Monsters/SoulNexus.cs:69`, `SlumberingBeetle.cs:137`) belong to monsters-kernel. Seam for the pipeline's `Kill` (§W1.12) |

#### W1.3.2 Cards created by the combat

| Sim member | Game source | Port |
| --- | --- | --- |
| `CreateCard(def, owner)` | `CreateCard` `:671-677`: `ToMutable` (`Models/CardModel.cs:2821-2825`), `AddCard(card, owner)` `:686-690` → the `Owner` setter (`Models/CardModel.cs:1777-1794`) and the private `AddCard` `:982-991`, then `AfterCreated` (`:2884-2886`) | `new CardInstance(def, AllocateCardInstanceId())` (the run allocator, A13), `AttachTo(owner)`, `def.AfterCreated(card)`; the card is in no pile. The `_allCards` registry (`:545`) is not ported (§W1.12) |
| `CreateCard(cardId, owner)` | `CreateCard<T>` `:666-669` (by type) | sim form by id string through `CardRegistry` (cross-kind references by id, §B5) |
| `CloneCard(card)` | `CloneCard` `:679-684` = `ClonePreservingMutability` (`DeepCloneFields` + `AfterCloned`, `Models/CardModel.cs:2826-2882`) + `AddCard` | `card.CloneForCombat(AllocateCardInstanceId())` (cards-kernel's port of the field copy and the `AfterCloned` resets) with `DeckVersionId` reset to `NoDeckVersion`, because `AfterCloned` clears `DeckVersion` (`:2861-2882`) and only `Player.PopulateCombatState` sets it (`Entities.Players/Player.cs:1225`). The owner is kept, as `MemberwiseClone` keeps it |

#### W1.3.3 Turn and effect counters

`CardOrPotionEffectDepth` (one `int`; the game keeps a per-player dictionary, `Combat/CombatManager.cs:6017`,
`:6522-6531`, `:1465-1484`; single player, M17), `PlayersTakingExtraTurn` (player combat ids; `Combat/CombatTurnState.cs:66`,
read at `Combat/CombatManager.cs:2649`, `:2860`, `:4873-4877`), `NextCardPlayId` / `AllocateCardPlayId()` (§B12 A11) and
`NextCreatureId` are data: copied by the clone, written in the `CombatKernel` block. `BeginCardOrPotionEffect` /
`EndCardOrPotionEffect` / `IsExecutingCardOrPotionEffect` are `CombatManager` members and therefore the loop's (§B6).

#### W1.3.4 Clone (`DeepClone`, `CloneInto`)

`CombatState.DeepClone()` = `Run.CloneWithoutCombat()` + `CloneInto(runClone, Run)`, attached to the cloned run iff
the source was attached to its run. `CloneInto` re-links the player through its index in the source run's
`Players` (a player that is not in the run is deep-cloned on its own), clones every monster, clones every ally
(`Allies[0]` re-linked to the cloned player, pets through `MonsterState.DeepClone`), the history (§W1.6.6) and the
counters. Identity after a clone: `clone.Player == clone.Run.Players[0] == clone.Allies[0]`,
`clone.Run.Combat == clone`, `clone.Rng == clone.Run.Rng`, `clone.Choice == clone.Run.Choice`, none of them the
source's (`Test_State_RunCloneLinksEveryReachableObjectOnce`). A combat that was never attached (a second combat on
one run) clones into a run whose `Combat` stays `null`.

### W1.4 Player state (`State/CreatureState.cs`, game `Entities.Players/Player.cs`, `Entities.Players/PlayerCombatState.cs`)

#### W1.4.1 Persistent members

| Sim member | Game source | Port |
| --- | --- | --- |
| `Gold` | `Player.cs:253-271` | plain `int`; the setter's `GoldChanged` event is UI |
| `Relics` (`List<RelicModel>`) | `_relics` `:129`, `Relics` `:241` | list order is hook order (`Combat/CombatState.cs:875-882`) |
| `GetRelic(id)`, `HasRelic(id)` | `GetRelicById` `:954-957` | first relic with that id; **melted relics are not skipped** (only the listener walk skips them, `Combat/CombatState.cs:877`); `HasRelic` is sim-only (`GetRelic(id) != null`) |
| `GetRelic<T>()` (`T : class`) | `GetRelic<T>` `:949-952` | first relic that `is T`, melted or not; `T` may be an interface (the §B12 A5 multiplier interfaces) |
| `PotionSlots` (`PotionModel?[]`), `MaxPotionCount` | `_potionSlots` `:131`, `PotionSlots` `:243`, `MaxPotionCount` `:142` | `MaxPotionCount == PotionSlots.Length`; a new player has `initialMaxPotionSlotCount = 3` (`:101`, `CreateForNewRun` `:712-717`). A resize replaces the array, so callers re-read `PotionSlots` after `AddToMaxPotionCount` / `SubtractFromMaxPotionCount` (the game's list grows in place) |
| `Potions` | `:245` | occupied slots in slot order (iterator) |
| `HasOpenPotionSlot` | `HasOpenPotionSlots` `:280` | any null slot |
| `GetPotionAtSlotIndex(i)` | `:964-972` | `IndexOutOfRangeException` outside `[0, MaxPotionCount)`, message copied |
| `CanUseOrRemovePotions` (default true) | `:140`, `:282-297` | data; the change event is UI (M21) |
| `BaseOrbSlotCount` | `:373-379`, ctor `:693` | data; the combat-state reset gives the orb queue this capacity (§W1.4.4) |
| `Deck` (`List<CardInstance>`, deck order) | `Deck` (`CardPile(PileType.Deck)`) `:312-316` | contract type `List<CardInstance>`; written in the `Deck` block |
| `AscensionEffectsApplied` | §B12 A12 | sim-only flag (§W1.2) |

#### W1.4.2 Relic and potion internals (§B12 A10, re-verified)

| Sim member | Game source | Port, line by line |
| --- | --- | --- |
| `AddRelicInternal(relic, index = -1)` | `:883-903` | `AttachTo(this)` = the `Owner` setter (`Models/RelicModel.cs:225-242`: another existing owner throws, the same owner is accepted), then `index == -1` → `Add`, any other index → `Insert` (an index below -1 throws `ArgumentOutOfRangeException` after the owner is set, as in the game). The `Flashed` subscription (`:895-898`) is UI and `RelicObtained` (`:899-902`) is dropped with `silent` (`scaffold-w1.md` §16.6 item 2: rule-relevant for W2's card reward) |
| `RemoveRelicInternal(relic)` | `:905-922` | not owned → `InvalidOperationException` (`:908-911`); `Remove`; `HasBeenRemovedFromState = true` (`RelicModel.RemoveInternal`, `Models/RelicModel.cs:635-638`); the owner is kept; `Flashed` / `RelicRemoved` dropped |
| `MeltRelicInternal(relic)` | `:924-947` | the three checks in the game's order: not wax, already melted, not owned (`:929-940`); then `IsMelted = true`, `Status = Disabled` (`:945-946`) |
| `GetPotionSlotIndex(potion)` | `:959-962` | `Array.IndexOf` (reference equality: no model overrides `Equals`) |
| `AddPotionInternal(potion, slotIndex = -1)` → `PotionProcureResult` | `:1020-1070` | `slotIndex < 0` → first empty slot (`:1027-1030`); none → `(false, potion, TooFull)` (`:1056-1068`); an occupied slot → `(false, potion, TooFull)` with the owner untouched (`:1033-1046`); else the owner through `AttachTo` (the `Owner` setter, `Models/PotionModel.cs:450-467`), the slot write and `(true, potion, None)` (`:1048-1054`). An explicit slot beyond the slots throws (`IndexOutOfRangeException`; the game's list indexer throws `ArgumentOutOfRangeException`). `AddPotionFailed` / `PotionProcured` are UI |
| `DiscardPotionInternal`, `RemoveUsedPotionInternal` | `:1072-1079`, `:1081-1085` | both `RemovePotionInternal`; `PotionDiscarded` / `UsedPotionRemoved` are UI (`Nodes.Potions/NPotionContainer.cs:296-299`) |
| `RemovePotionInternal` (private, as in the game) | `:1087-1096` | absent → `InvalidOperationException` (message copied), else the slot is nulled. `HasBeenRemovedFromState` is set by the callers (`PotionModel.Discard` / `RemoveBeforeUse`, `Models/PotionModel.cs:589-599`), not here |
| `AddToMaxPotionCount(n)`, `SubtractFromMaxPotionCount(n)` → `SetMaxPotionCountInternal` | `:974-982`, `:984-1018` | **literal port** (round 3, replacing the scaffold's truncation form). Growth appends empty slots (`:986-993`); an equal count returns (`:996-999`); a shrink walks a working copy of the slot list from the last slot down to the new count (`:1000-1016`): a potion found there moves to `IndexOf(null)` when that index is below the new count (`:1004-1007`), else it is discarded through the `RemovePotionInternal` logic (`:1009-1012`); then the slot is removed (`:1014`). With every slot full `IndexOf(null)` is -1 and the write to index -1 throws `ArgumentOutOfRangeException`, the game's own crash; the working copy keeps the player's slots unchanged on that throw. A negative count also throws the game's `ArgumentOutOfRangeException` (the loop reaches index -1). The only v0.111.0 shrink is TightBelt on empty slots (M27). Worked cases (tested): `[P0, P1, _, P3]` −2 → `[P0, P1]` (slot 3 finds open slot 2, not below 2, discarded); `[_, P1, P2]` −1 → `[P2, P1]`; `[P0, _, _, P3, P4]` −3 → `[P0, P4]`; `[P0, P1, P2]` −1 → throws, slots kept |

#### W1.4.3 Pets (`PlayerCombatState` pets, `Creature.PetOwner`)

| Sim member | Game source | Port |
| --- | --- | --- |
| `PetIds` (`List<int>`, insertion order) | `_pets` `PlayerCombatState.cs:20`, `Pets` `:39` | ids instead of creature references (§B2); resolved through `cs.GetCreatureById` |
| `AddPetInternal(pet)` | `:375-387` | already listed → nothing (`:378`); `PetOwnerId != this` → `pet.SetPetOwner(this)` (the `PetOwner` setter, `Entities.Creatures/Creature.cs:520-535`: any existing owner throws "already has an owner"); then append. The `Died += OnPetDied` subscription (`:384`) is the membership itself: `CombatState.InvokeDiedEvent` delivers it while the pet is listed |
| `OnPetDied(cs, pet)` | `:433-445` | not listed → `InvalidOperationException` (`:436-439`); `Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet)` → unlist (the unsubscribe, `:440-444`). The pet stays in `Allies` (`Kill` removes only `Side == Enemy`, `Commands/CreatureCmd.cs:2812-2822`; §B12 A15) |
| `GetPet(cs, monsterId)`, `GetOsty(cs)`, `IsOstyAlive(cs)`, `IsOstyMissing(cs)` | `GetPet<T>` `:389-392`; `Player.Osty` / `IsOstyAlive` / `IsOstyMissing` `Player.cs:247-251` | first listed pet with that monster id; Osty by the id string `OSTY` (`Models.Monsters/Osty.cs`, slug; §B5 cross-kind references by id). A listed pet that is no longer in the combat is skipped (no v0.111.0 path removes a pet from `_allies`) |
| `MonsterState.PetOwnerId`, `IsPet`, `SetPetOwner(owner)` | `Creature.PetOwner` `:520-535`, `IsPet` `:537` | `PowerModel.NoCreature` when none; `SetPetOwner` throws on any existing owner, as the setter does |
| `MonsterState(id, def, side, hp, maxHp, extra, slot)` | `new Creature(monster, side, slot)` (`Combat/CombatState.cs:713`) | side-taking constructor for pets and player-side summons; the P0 constructor forwards with `Enemy` |

#### W1.4.4 Combat half of the player

| Sim member | Game source | Port |
| --- | --- | --- |
| `ResetCombatState()` | `Player.ResetCombatState` `Player.cs:1209-1212` = a new `PlayerCombatState` (`PlayerCombatState.cs:257-277`), called for every player in `CombatManager.SetUpCombat` (`Combat/CombatManager.cs:6575`, call `:6592`) before `PopulateCombatState` (`:6605`) | the five piles cleared, pets cleared, `TurnNumber = 1` (`:42-48`), `Phase = None`, `Energy = 0`, `Stars = 0` (field initialisers of the new object, so no `StarsModified`), then `OrbQueue.Clear()` and `OrbQueue.AddCapacity(BaseOrbSlotCount)` (`:274-276`). The orb call reaches character-mechanics' `AddCapacity` stub, so its test is `W1Pending`. Seam for the loop's `SetUpCombat` (§W1.12; the character-mechanics round-2 note "neither B6 nor B9 names who resets the queue") |
| `CombatStateAfterCombatEnd()` | `PlayerCombatState.AfterCombatEnd` `:279-297` (called from `Player.AfterCombatEnd` `Player.cs:1249-1254`) | the five piles cleared and the pet list cleared; powers and block are the pipeline's part of `Player.AfterCombatEnd` (§B12 A18, loop) |

#### W1.4.5 Clone and canonical additions

`PlayerState`'s copy constructor copies every persistent member, clones relics, potions and the orb queue with
`CloneFor(this)`, the deck with `CardInstance.CloneFor(this)` and copies `PetIds`. Canonical blocks after the P0
player fields: `PlayerPersistent` `0x5307` (gold, base orb slots, `CanUseOrRemovePotions`, `AscensionEffectsApplied`),
`RelicList` `0x5309`, `PotionSlots` `0x530A` (per slot presence + potion), `Deck` `0x530B`, `PlayerCombatExtras`
`0x5308` (orb queue, pet ids). Unchanged from the scaffold.

#### W1.4.6 Stars

`GainStars(decimal)` / `LoseStars(decimal)` port the clamping bodies of `PlayerCombatState.GainStars` / `LoseStars`
(`:355-373`: a negative amount throws, the result is `(int)Math.Max(stars ± amount, 0m)`). The game's `Stars` setter
also appends `History.StarsModified(new − old)` on every change (`:138-154`, append `:150`). §B12 A16 assigns that
append to `StarsCmd`, "the only writer; it records `StarsModified`", and the scaffold's `StarsCmd.SpendStars` records
it once (`scaffold-w1.md` §16.1). The state methods therefore stay context-free and record nothing, so no change is
recorded twice; a direct write to `Stars` outside `StarsCmd` records nothing either (tests and `ResetCombatState`
only). This is the resolution of the round-2 findings on `StarsModified` (§W1.10 rows 3 and 19).

### W1.5 Monster listener canonical protocol

`ListenerCanonical.Write(w, listener)` (`State/OwnedListener.cs`) writes one kind byte: `0` no listener, `1` a
stateless adapter (immutable: its content is the monster's def and id, already in the monster's form), `2` a
stateful adapter, followed by its own `ICanonical` form. Kinds 0 and 1 are the bytes of the P0 `WriteBool(Listener != null)`,
so P0 states hash as before. `MonsterState.WriteCanonical` uses it in the `MonsterKernel` block. Protocol for
monsters-kernel (`scaffold-w1.md` §14 item 2, §B12 A21): an adapter that carries state implements both
`IOwnedListener<MonsterState>` (cloned per owner by the copy constructor) and `ICanonical` (hashed); an immutable
adapter implements neither (shared by clones, kind 1) or writes a stable id through `ICanonical`.
`CardInstance.Listener` is in a cards-kernel file and still writes presence only; the helper is shared so the
cards-kernel owner can call it (byte-compatible) — contract change request §W1.12.

### W1.6 Combat history (`State/CombatHistory.cs`, game `Combat.History/CombatHistory.cs`, `Combat.History/CombatHistoryEntry.cs`, `Combat.History.Entries/*.cs`)

#### W1.6.1 Entry types (D11: field for field, ids instead of references)

Every entry starts with `HistoryStamp(Sequence, ActorId, RoundNumber, CurrentSide, PlayerTurnNumber)`: the base
`CombatHistoryEntry` fields (`Actor` `CombatHistoryEntry.cs:15-19`, `RoundNumber` `:22-26`, `CurrentSide` `:29-33`,
`_playerTurnNumbers` `:12`, filled in the constructor `:46-65`); one player turn number (single player, M15);
`Sequence` is sim-only (the global position in the game's single `_entries` list, `CombatHistory.cs:22`). The
`History` back-reference and `Description` / `HumanReadableString` are UI.

| Sim entry | Game entry (fields, constructor) | Actor (game) | Sim payload after the stamp |
| --- | --- | --- | --- |
| `BlockGainedEntry` | `Combat.History.Entries/BlockGainedEntry.cs:13-33`, `:44-50` | receiver (`:19`, `:45`) | `Amount`, `Props`, `HistoryCardPlay? CardPlay`; `ReceiverId` = actor |
| `CardAfflictedEntry` | `CardAfflictedEntry.cs:11-22`, `:26-31` | `card.Owner.Creature` (`:27`) | `CardInstanceId`, `CardId`, `AfflictionId` |
| `CardDiscardedEntry` | `CardDiscardedEntry.cs:11-15`, `:19-23` | card owner (`:20`) | `CardInstanceId`, `CardId` |
| `CardDrawnEntry` | `CardDrawnEntry.cs:11-22`, `:26-31` | card owner (`:27`) | `CardInstanceId`, `CardId`, `FromHandDraw`, **`CardType`** (snapshot, §W1.6.2) |
| `CardExhaustedEntry` | `CardExhaustedEntry.cs:11-15`, `:19-23` | card owner (`:20`) | `CardInstanceId`, `CardId` |
| `CardGeneratedEntry` | `CardGeneratedEntry.cs:11-22`, `:26-31` | card owner (`:27`) | `CardInstanceId`, `CardId`, `CreatorId` (`Player? Creator`) |
| `CardPlayFinishedEntry` | `CardPlayFinishedEntry.cs:12-23`, `:45-50` | `cardPlay.Card.Owner.Creature` (`:46`) | `HistoryCardPlay CardPlay`, `WasEthereal` = `Keywords.Contains(Ethereal)` at append (`:49`) |
| `CardPlayStartedEntry` | `CardPlayStartedEntry.cs:12-16`, `:38-42` | card owner (`:39`) | `HistoryCardPlay CardPlay` |
| `CreatureAttackedEntry` | `CreatureAttackedEntry.cs:11-15`, `:19-23` | attacker (`:20`) | `HistoryList<HistoryDamageResult> DamageResultList` (the scaffold's `IReadOnlyList<HistoryDamageResult> DamageResults` stays as a boxing accessor); `AttackerId` = actor |
| `DamageReceivedEntry` | `DamageReceivedEntry.cs:12-32`, `:47-53` | receiver (`:48`) | `HistoryDamageResult Result`, `DealerId`, `CardSourceInstanceId`, `CardSourceId`, **`DealerSide`** (snapshot); `ReceiverId` = actor |
| `EnergySpentEntry` | `EnergySpentEntry.cs:10-14`, `:18-22` | `player.Creature` (`:21`) | `Amount` |
| `MonsterPerformedMoveEntry` | `MonsterPerformedMoveEntry.cs:16-34`, `:56-62` | `monster.Creature` (`:57`) | `MonsterId`, `MoveId`, `HistoryList<int>? TargetIdList` (null when the game's `Targets` is null; `IReadOnlyList<int>? TargetIds` stays as a boxing accessor) |
| `OrbChanneledEntry` | `OrbChanneledEntry.cs:11-15`, `:19-23` | `orb.Owner.Creature` (`:20`) | `OrbId` |
| `PotionUsedEntry` | `PotionUsedEntry.cs:13-24`, `:46-51` | `potion.Owner.Creature` (`:47`) | `PotionId`, `TargetId` |
| `PowerReceivedEntry` | `PowerReceivedEntry.cs:12-30`, `:44-50` | `power.Owner` (`:45`) | `PowerId`, `Amount` (decimal), `ApplierId` |
| `StarsModifiedEntry` | `StarsModifiedEntry.cs:10-14`, `:18-22` | `player.Creature` (`:21`) | `Amount` |
| `SummonedEntry` | `SummonedEntry.cs:10-14`, `:18-22` | `player.Creature` (`:21`) | `Amount` |

`HistoryCardPlay` mirrors `Entities.Cards/CardPlay.cs:12-94` (`Card` → `CardInstanceId` + `CardId`, `Player` →
`PlayerId`, `Target` → `TargetId`, `ResultPile`, `Resources` (`ResourceInfo`, §B12 A11), `IsAutoPlay`, `PlayIndex`,
`PlayCount`, `IsFirstInSeries` / `IsLastInSeries` `:92-94`) plus `PlayId` and the snapshots **`CardType`** and
**`IsDupe`**. `HistoryDamageResult` mirrors `Entities.Creatures/DamageResult.cs:9-78` (`TotalDamage` = blocked +
unblocked). The game's `CardPlay` is init-only (`CardPlay.cs:12-90`) and is built with every field before
`BeforeCardPlayed` and `History.CardPlayStarted` (`Models/CardModel.cs:758-768`, `:665`), so the value snapshot taken
at the append equals the reference the game stores, provided the sim's settable `CardPlay` is not changed between
the Started and Finished appends (a loop obligation; the game cannot). The game's `DamageResult` setters run before
`History.DamageReceived` (`Commands/CreatureCmd.cs:3709-3737`), so its snapshot is exact too.

#### W1.6.2 Snapshotted facts (finding "id-only entries cannot answer reads about cards or creatures that left combat")

The readers dereference the stored objects later. Each fact a reader needs and that cannot change between the append
and the read is copied into the entry:

| Snapshot | Reader | Why the copy equals the live read |
| --- | --- | --- |
| `HistoryCardPlay.CardType` | Finisher, LunarBlast, MakeItSo, Pinpoint, Stomp, FeralPower, JugglingPower, LethalityPower, NostalgiaPower, SmoggyPower, HistoryCourse, RippleBasin (`e.CardPlay.Card.Type`) | `CardModel.Type` is get-only per instance (`Models/CardModel.cs:1737-1741`); the one dynamic override, `MadScience.Type => TinkerTimeType` (`Models.Cards/MadScience.cs:814`), reads a field set only by the TinkerTime event out of combat (`Models.Events/TinkerTime.cs:67`, `:157`, `:223`); history is cleared at combat end |
| `HistoryCardPlay.IsDupe` | HistoryCourse (`!e.CardPlay.Card.IsDupe`, `Models.Relics/HistoryCourse.cs:58`) | `IsDupe` has a private setter called only in `CreateDupe` right after the clone (`Models/CardModel.cs:2087-2098`, `:3605`); a dupe leaves combat after its play (`:3493`, `:175-178`), so a live lookup by id would fail |
| `CardDrawnEntry.CardType` | IterationPower (`e.Card.Type == Status`, `Models.Powers/IterationPower.cs:59`) | as above |
| `DamageReceivedEntry.DealerSide` | GangUp (`e.Dealer.Side == card.Owner.Creature.Side`, `Models.Cards/GangUp.cs:99`) | `Creature.Side` is get-only (`Entities.Creatures/Creature.cs:460-464`); the dealer may have left combat |

Card tags (PhantomBladesPower's Shiv, `Models.Powers/PhantomBladesPower.cs:77`) are def data (`Tags => _tags ??
CanonicalTags`, `Models/CardModel.cs:1942`, no instance mutation; `MockCardModel` is the only override) and are read
through `CardRegistry` by `CardId`. Card identity (Bolas, Fetch, ThrummingHatchet) compares `CardInstanceId`
(`IsPlayOf`). One reader still needs the card object itself: HistoryCourse auto-plays `CreateDupe` of the last non-dupe
attack of the last player turn (`Models.Relics/HistoryCourse.cs:58-61`). That card is normally still in a pile;
if it left combat (`CardPileCmd.RemoveFromCombat` callers: `Models.Monsters/ThievingHopper.cs:653`), the sim cannot
rebuild its state from the id (open question, §W1.12).

#### W1.6.3 Stamps and turn predicates

- `HappenedThisTurn(cs)` = `CombatHistoryEntry.HappenedThisTurn` `:67-113`: a null combat → false (`:73-76`); another
  round → false (`:77-80`); another side → false (`:81-84`); every stamped player must still be in the combat with an
  unchanged turn number (`:85-111`) — single player: `cs.Player.TurnNumber == PlayerTurnNumber`.
- `HappenedLastPlayerTurn(player)` = `:115-125`: the stamped turn equals the player's current `TurnNumber − 1`, with
  **no side check**, so the enemy turn after player turn N counts during turn N + 1 (EmotionChip,
  `Models.Relics/EmotionChip.cs:155`). The game's "player not stamped" branch (`:118-121`) cannot occur in a
  single-player log.
- Extension forms `entry.HappenedThisTurn(cs)` / `entry.HappenedLastPlayerTurn(player)` / `entry.IsByActor(creature)`
  (`HistoryEntryExtensions`, constrained generics, no boxing) let readers be ported one to one.

#### W1.6.4 Appends

The 17 `Append*` methods are the game's `CombatHistory` methods (`CombatHistory.cs:74-157`) with `cs` first. Each one
computes its payload first and then stamps (`HistoryStamp(Count, actor, cs.RoundNumber, cs.CurrentSide,
cs.Player.TurnNumber)`, `CombatHistory.cs:76-156` pass `combatState.RoundNumber`, `CurrentSide`, `Players`), so a
throwing append leaves the count, the lists and the hash unchanged (tested). The game appends only while
`combatState.IsLiveCombat()` (`:159-170`), which is `true` for every `CombatState` (`Combat/CombatState.cs:1077-1080`;
only `Combat/NullCombatState.cs` returns false), so the sim appends unconditionally; the callers' own gates
(`IsCurrentLiveCombat(effectId)`, `IsInProgress && !IsEnding`) stay at the call sites (`Models/CardModel.cs:663-666`,
`:732-735`; `Commands/CreatureCmd.cs:3735-3737`). `Changed` is UI. Actors follow §W1.6.1; an ownerless card, orb
or potion is stamped `NoId` (the game would throw a null reference there, so no game state reaches it; §B12 A23 gives
every combat card an owner), and an ownerless power is stamped `NoId` exactly as the game stores a null `Actor`
(`PowerReceivedEntry.cs:45`). `WasEthereal` builds the in-combat keyword set from `KeywordMask` plus
`Hook.ModifyKeywordsInCombat` (`Models/CardModel.cs:1940`, `:2775-2814`) until cards-kernel exposes that set
(`scaffold-w1.md` §16.6 item 13). Damage results become value snapshots; monster targets are the game's
`PlayerCreatures` snapshot (`Models/MonsterModel.cs:269`, `:286`; `Combat/CombatState.cs:560`, a `ToList`).

Recording sites (the owners that call these; from `design-subsystems.md` §4): `CardPlayStarted` / `Finished`
`Models/CardModel.cs:665`, `:735` (loop); `EnergySpent` `:1255` (loop); `CardDrawn` `Commands/CardPileCmd.cs:1919`,
`:4050` (loop); `CardDiscarded` `Commands/CardCmd.cs:829`, `CardExhausted` `:1041`, `:1877`, `CardGenerated` `:1471`,
`:2069`, `Commands/CardPileCmd.cs:1194`, `:3237`, `CardAfflicted` `Commands/CardCmd.cs:2269` (loop / cards-kernel);
`CreatureAttacked` `Commands.Builders/AttackCommand.cs:422`, `:1145`, `DamageReceived` `Commands/CreatureCmd.cs:776`,
`:3737`, `BlockGained` `:1872`, `:3973`, `PowerReceived` `Commands/PowerCmd.cs:458`, `:883`, `:1321` (pipeline);
`MonsterPerformedMove` `Models/MonsterModel.cs:286` (monsters-kernel); `OrbChanneled` `Commands/OrbCmd.cs:216`,
`Summoned` `Commands/OstyCmd.cs:269`, `:391`, `StarsModified` through `StarsCmd` (character-mechanics); `PotionUsed`
`Models/PotionModel.cs:247` (relics-potions). `Clear()` runs at combat end (`Combat/CombatManager.cs:1759`) and reset
(`:7084`; loop).

#### W1.6.5 Queries (the 45 readers)

The 45 rule readers of v0.111.0 (29 cards, 12 powers, 4 relics; `grep` of `History.Entries`, `CardPlaysStarted`,
`CardPlaysFinished`, `HappenedThisTurn`, `HappenedLastPlayerTurn` outside `Combat.History*`; `kernel-gaps.json`
subsystem "Combat history") use: a per-type view (`OfType<T>(Entries)`, `CardPlaysStarted`, `CardPlaysFinished`,
`CombatHistory.cs:24-28`), a window (this turn, last player turn, the whole combat), an actor or player filter, and
count / any / sum / last. Sim API: `Entries<T>()` and the per-type properties (`CardPlaysStarted`,
`CardPlaysFinished`, …), `CountOf` / `AnyOf` / `SumOf` / `LastOf` (whole combat), `CountThisTurn` / `AnyThisTurn` /
`SumThisTurn`, `CountLastPlayerTurn` / `AnyLastPlayerTurn`, `ByActor` / `CountByActor`, and `GetEntriesInOrder`
(global order by `Sequence`, for the recorder comparison of D11).

| Reader (site) | Entry · window · aggregate | Sim form |
| --- | --- | --- |
| BansheesCry (`Models.Cards/BansheesCry.cs:134`) | PlayFinished · C · count | `CountOf<CardPlayFinishedEntry>(e => e.WasEthereal && e.CardPlay.PlayerId == owner)` |
| BeatIntoShape (`BeatIntoShape.cs:132`) | DamageReceived · T · count | `CountThisTurn<DamageReceivedEntry>(cs, e => e.ReceiverId == target && e.DealerId == owner && e.Result.Props.IsPoweredAttack())` |
| Bolas, ThrummingHatchet (`Bolas.cs:61`, `ThrummingHatchet.cs:57`) | PlayFinished · L · any | `AnyLastPlayerTurn<CardPlayFinishedEntry>(player, e => e.CardPlay.IsPlayOf(card))` |
| DeathMarch, Murder (`DeathMarch.cs:97`, `Murder.cs:97`) | CardDrawn · T / C · count | `CountThisTurn` / `CountByActor<CardDrawnEntry>` (`!FromHandDraw` for DeathMarch) |
| DeathsDoor (`DeathsDoor.cs:146`) | PowerReceived · T · any | `AnyThisTurn<PowerReceivedEntry>(cs, e => e.PowerId == "DOOM_POWER" && e.ApplierId == owner)` |
| EvilEye, Midnight (`EvilEye.cs:138`, `Midnight.cs:129`) | CardExhausted · T · any / C · count | `AnyThisTurn` with the actor / `CountOf<CardExhaustedEntry>()` |
| Fetch (`Fetch.cs:149`) | PlayFinished · T · any | `AnyThisTurn<CardPlayFinishedEntry>(cs, e => e.CardPlay.IsPlayOf(card))` |
| Finisher, LunarBlast, MakeItSo, Pinpoint, Stomp (`Finisher.cs:103`, `LunarBlast.cs:100`, `MakeItSo.cs:58`, `Pinpoint.cs:128`, `Stomp.cs:180`) | PlayFinished · T · count | `CountThisTurn<CardPlayFinishedEntry>(cs, e => e.CardPlay.CardType == … && e.CardPlay.PlayerId == owner)` |
| Flatten, Rattle (`Flatten.cs:114`, `Rattle.cs:119`) | CreatureAttacked · T · any / 1 + count | `CountByActor<CreatureAttackedEntry>(osty.CombatId, e => e.HappenedThisTurn(cs))` with `owner.GetOsty(cs)` |
| Ftl, PaleBlueDotPower (`Ftl.cs:139`, `PaleBlueDotPower.cs:147`) | PlayFinished · T · count | `CountThisTurn<CardPlayFinishedEntry>(cs, e => e.CardPlay.PlayerId == owner)` |
| GangUp (`GangUp.cs:99`) | DamageReceived · T · count | `… e.HasDealer && e.DealerId != owner && e.DealerSide == ownerSide` |
| GoldAxe (`GoldAxe.cs:96`) | PlayFinished · C · count | `CountOf<CardPlayFinishedEntry>()` |
| HelixDrill, Radiate (`HelixDrill.cs:108`, `Radiate.cs:102`) | EnergySpent / StarsModified · T · sum | `SumThisTurn<…>(cs, e => e.Amount, e => e.Stamp.ActorId == owner && …)` (`Amount > 0` for Radiate) |
| MementoMori (`MementoMori.cs:97`) | CardDiscarded · T · count | `CountThisTurn<CardDiscardedEntry>(cs, e => e.Stamp.ActorId == owner)` |
| Normality, RingingPower, SmoggyPower, EchoFormPower, FeralPower, JugglingPower, LethalityPower, NostalgiaPower (`Normality.cs:34`, `RingingPower.cs:328`, `SmoggyPower.cs:57`, `EchoFormPower.cs:65`, `:81`, `FeralPower.cs:35`, `JugglingPower.cs:122`, `LethalityPower.cs:42`, `NostalgiaPower.cs:33`) | PlayStarted · T · count / any / none | `CountThisTurn` / `AnyThisTurn<CardPlayStartedEntry>` with `CardType`, `PlayerId`, `IsFirstInSeries`, `Resources.EnergyValue == 0` (Feral) |
| PullFromBelow, Supermassive, TearAsunder, Voltaic (`PullFromBelow.cs:104`, `Supermassive.cs:97`, `TearAsunder.cs:102`, `Voltaic.cs:140`) | PlayFinished / CardGenerated / DamageReceived / OrbChanneled · C · count | `CountOf<…>` with `WasEthereal` / `CreatorId` / `UnblockedDamage > 0` / `OrbId == "LIGHTNING_ORB"` |
| Spite (`Spite.cs:130`) | DamageReceived · T · any | `AnyThisTurn<DamageReceivedEntry>(cs, e => e.ReceiverId == id && e.Result.UnblockedDamage > 0)` |
| ChainsOfBindingPower (`ChainsOfBindingPower.cs:66`) | CardAfflicted · T · count | `CountThisTurn<CardAfflictedEntry>(cs, e => e.Stamp.ActorId == owner && e.AfflictionId == "BOUND")` |
| IterationPower (`IterationPower.cs:59`) | CardDrawn · T · count | `… e.CardType == CardType.Status` |
| UnmovablePower (`UnmovablePower.cs:37`) | BlockGained · T · count | `CountThisTurn<BlockGainedEntry>(cs, e => e.CardPlay is { } p && p.PlayerId == owner && e.Props.IsCardOrMonsterMove() && !p.IsSamePlay(cardPlay))` — plays compare by `PlayId` (§B12 A11) |
| EmotionChip (`EmotionChip.cs:155`) | DamageReceived · L · any | `AnyLastPlayerTurn<DamageReceivedEntry>(player, e => e.ReceiverId == owner && !e.Result.WasFullyBlocked)` |
| HistoryCourse (`HistoryCourse.cs:58`) | PlayFinished · L · last | `LastOf<CardPlayFinishedEntry>(e => e.CardPlay.PlayerId == owner && e.HappenedLastPlayerTurn(player) && e.CardPlay.CardType == Attack && !e.CardPlay.IsDupe)` |
| PaelsEye, RippleBasin (`PaelsEye.cs:258`, `RippleBasin.cs:59`) | PlayFinished · T · any / none | `AnyThisTurn<CardPlayFinishedEntry>` with the actor and `!IsAutoPlay` / `CardType == Attack` |

`HistoryCardPlay` equality is by `PlayId` only (`Equals` / `GetHashCode` / `==`), the sim form of the game's
reference comparison of `CardPlay` (`UnmovablePower.cs:37`); `IsSamePlay(cardPlay)` compares with a live play and
`IsPlayOf(card)` checks the card instance. The ids in the reader examples are model id strings (§B5).

#### W1.6.6 Storage and clone (§B12 A22)

Per entry type a private `List<T>` in append order (the game's `OfType<T>` order). The two entry types with an inner
list are stored as id-only records: `CreatureAttackedEntry` as (stamp, start, count) into a per-history
`List<HistoryDamageResult>` side store, `MonsterPerformedMoveEntry` as (stamp, monster id, move id, start, count or
"null") into a `List<int>` side store. Their public lists are read views that materialise the entry with a
`HistoryList<T>` (a read-only struct view of the history's own store: `Count`, indexer, struct enumerator, element-wise
equality, `ToArray`); the scaffold's interface-typed properties (`DamageResults`, `TargetIds`) remain and box the
view on access, so code written against the scaffold compiles unchanged. `DeepClone` is therefore 19 plain list copies (17 entry types and the 2 stores) plus the folded
digests: no per-entry work, no shared mutable object (a clone's views read the clone's stores), and an inner list is
never shared between clones (A22). `Clear()` replaces the stores instead of clearing them, so an entry read before
`Clear` keeps its inner list. Appends to a store roll back on a throwing element. The round-2 scaffold copied an
inner array per entry on every clone.

Measured with a Release probe (scratch console project against this tree, not committed) on 500 entries in the
`FillHistory` mix (per 10 entries: 2 draws, one play as Started + Finished, energy, damage, a two-hit attack, a
monster move, block, a power). Round 3 ran next to 20+ other agents (load average 15–68 on 12 cores), so absolute
times are inflated 2–4× and only same-run comparisons are meaningful:

| Design | Mix with inner lists | Same mix without inner lists | Load |
| --- | --- | --- | --- |
| first round-3 draft (per-entry inner-array copies, the scaffold's scheme) | 24.7 µs | 6.6 µs | ≈15 |
| side stores with per-entry rebinding | 6.8 µs | 2.6 µs | ≈15 |
| final: id-only records + views (no per-entry work) | 7.6–9.1 µs | 8.0–9.1 µs | 20–30 |

The final form clones the inner-list mix at the cost of plain entries, allocating 30.8 KB per clone. Its own guard,
`Test_State_HistoryCloneOf500EntriesStaysWithinBudget` (the same mix, best mean of 25 batches of 100 clones, fails
above 50 µs), measured **3.06 µs in Release and 5.90 µs in Debug** in the final gate run (load ≈11). Budget 50 µs
(§B2): met with a wide margin.

#### W1.6.7 Canonical form and hash

`WriteCanonical` writes tag `0x5C01`, the global count, then per entry type in a fixed order its count and, when the
count is positive, a 64-bit digest of its entries. The digest of a type is a chain over its entries in append order:
`d₀ = 0`, `dₖ = XXH64(canonical bytes of entry k, seed = dₖ₋₁)` (the entry bytes are the per-entry forms with tags
`0x5C02`–`0x5C20`, written into the caller's writer and truncated after hashing). The chain is folded lazily: the
history keeps per type (entries folded, digest), a hash folds only the entries appended since, and a clone inherits
the folded state. Because every entry is chained individually, the digest does not depend on when hashes were taken
(`Test_State_HistoryDigestIsIndependentOfWhenItIsHashed`). Every field reaches the digest
(`Test_State_HistoryDigestCoversEveryEntryField`: 57 single-field variants, pairwise distinct). Cost: an O(1) form
(≤ 210 bytes for 17 types) instead of the full entries (30 KB for the 500-entry probe, whose full write and hash took
74 µs per call at load 15); each entry is written and hashed once per lineage (a first hash of 500 unfolded entries
took 0.6 ms at load 20–30; a clone of the 500-entry log plus 5 appends plus a hash took 18 µs in the same runs, against
7.6–9.1 µs for the clone alone). `WriteEntriesCanonical` writes the full
entry form (every entry, the scaffold's layout) for diffing and debugging; it is not part of the state hash. An empty
log writes the same 74 bytes in both forms, which are the P0 bytes, so no P0 pin moved. Like `CombatSim.StateHash`'s
scratch writer, the fold cache makes concurrent hashing of one history instance unsupported (different instances,
including clones, are independent). The digest is a lossy 64-bit summary in the canonical bytes, the same trade as the
unlock profile's digest (§B2); equal histories always write equal bytes.

### W1.7 Fidelity log (`State/Fidelity.cs`)

`FidelityFlag { DeferredListenerPromptInline, ApproximatedRule }` (D2, §B12 A5s), `FidelityEntry(Flag, Site)`,
`FidelityLog.Record`, `Contains(flag)`, `Contains(flag, site)` (ordinal), `CountOf(flag)`, `Entries`, `Count`;
cloned with the run and hashed in record order (tag `0x5D20`, count, per entry the flag byte and the site string).
A flag recorded on a clone stays on the clone (tested).

### W1.8 Canonical layout (replaces §2.2)

```
RunState      tag 0x5D01, Ascension:i32, NextCardInstanceId:i32, UnlockProfile (0x5D10 digest),
              tag 0x5D02 count Player*, hasRng:bool RunRngSet, tag 0x5D03 count Modifier*, ChoiceRuntime (0x5A01),
              FidelityLog (0x5D20) | RunState.WriteCanonical then: hasCombat:bool, CombatFields
CombatFields  tag 0x5301, EncounterId:str, RoundNumber, CurrentSide (i32), Outcome:u8, IsInProgress, IsStarting (bool),
              NextCreatureId:i32, playerIndex:i32 (Player only when -1), tag 0x5306 count Monster*,
              tag 0x530D count (isPlayer:bool [Creature])*, tag 0x530E CardOrPotionEffectDepth, NextCardPlayId,
              count extraTurn:i32*, CombatHistory
CombatState   = RunState run fields + CombatFields
Player        tag 0x5302, Creature, CharacterId, BaseMaxEnergy, Energy, Stars, TurnNumber, Phase, IsActiveForHooks,
              piles Hand, Draw, Discard, Exhaust, Play, tag 0x5307 Gold, BaseOrbSlotCount, CanUseOrRemovePotions,
              AscensionEffectsApplied, tag 0x5309 count Relic*, tag 0x530A count (present:bool [Potion])*,
              tag 0x530B count Card*, tag 0x5308 OrbQueue (0x5B02) count petId:i32*
Monster       tag 0x5303, Creature, MonsterId, SlotName, MaxHpBeforeModification?, SpawnedThisTurn, Extra?, Ai?,
              tag 0x530C PetOwnerId:i32, listener kind:u8 [listener form] (§W1.5)
CombatHistory tag 0x5C01, Count:i32, per type in the order BlockGained, CardAfflicted, CardDiscarded, CardDrawn,
              CardExhausted, CardGenerated, CardPlayFinished, CardPlayStarted, CreatureAttacked, DamageReceived,
              EnergySpent, MonsterPerformedMove, OrbChanneled, PotionUsed, PowerReceived, StarsModified, Summoned:
              count:i32 [digest:u64 when count > 0] (§W1.6.7)
```

Tags used by this area: state `0x5301`–`0x530E`, history `0x5C01`–`0x5C04` and `0x5C10`–`0x5C20` (entries), fidelity
`0x5D20`; the run shell's `0x5D01`–`0x5D03` are in `Run/RunCanonicalTags.cs` (a W2 shell file) and unchanged. No new
tag value was needed in round 3.

### W1.9 Tests (`sim/Sts2Sim.Tests/StateKernelTests.cs`, 43 tests, `Test_State_*`)

Run shell: `RunCreateStandaloneAppliesTightBeltOncePerPlayer` (A0–A10, two calls, through `CombatState` too),
`RunCloneLinksEveryReachableObjectOnce` (clone-once identities for both clone entry points, owners rebound, no shared
mutable object), `RunCanonicalWritesEachSharedObjectOnce`, `RunCombatAttachmentStaysWithItsRun`.
Creatures: `AlliesStartWithThePlayerAndKeepPetsInInsertionOrder`, `AddCreatureRoutesBySideAndRejectsDuplicates`,
`RemoveCreatureSearchesEnemiesThenAllies`, `SetEnemyIndexFollowsTheGameOrderRule`,
`PetRegistrationFollowsPlayerCombatState`, `InvokeDiedEventDropsOnlySubscribedRemovablePets`.
Cards: `CreateCardAllocatesAttachesAndRunsAfterCreated`, `CloneCardTakesANewIdAndNoDeckVersion`,
`CardPileDeepCloneRefusesOwnedCards`.
Player internals: `PlayerPersistentDefaultsFollowTheGame`, `PotionSlotGrowthAppendsEmptySlots`,
`PotionSlotShrinkMovesOrDiscardsLikeTheGame`, `AddPotionInternalFillsFirstEmptyOrExplicitSlot` (full slots, explicit
index, occupied slot, foreign potion), `RemovePotionInternalsNullTheSlot`, `RelicInternalsFollowThePlayerMethods`
(insert, foreign relic, removal flag, the three melt guards, melted relics still found),
`ResetCombatStateRebuildsTheCombatHalf` (W1Pending: `OrbQueue.AddCapacity`), `CombatStateAfterCombatEndClearsPilesAndPets`.
History: `HistoryAppendsStampEveryEntryType` (all 17, stamps, actors, payloads), `HistoryActorsOfOwnerlessModelsAndAbsentArguments`,
`HistoryFailedAppendLeavesTheLogUnchanged`, `HistoryHappenedThisTurnMatchesTheGame`, `HistoryLastPlayerTurnHasNoSideCheck`,
`HistoryQueriesCountAnySumLastAndByActor`, `HistoryCardPlayComparesByPlayId`, `HistorySnapshotsDupeAndCardTypeAtAppend`,
`HistoryWasEtherealReadsTheInCombatKeywords`, `HistoryCloneCopiesEntriesAndInnerLists`, `HistoryClearMatchesAFreshLog`,
`HistoryCloneOf500EntriesStaysWithinBudget`, `HistoryDigestIsIndependentOfWhenItIsHashed`, `HistoryDigestCoversEveryEntryField`.
Canonical and clone: `MonsterListenerCanonicalProtocol`, `FidelityLogRecordsInOrderAndHashes`,
`KernelHashCoversEveryNewMember` (53 single-member probes on a rich state, each changing the hash and leaving the
source, pairwise distinct), `KernelCloneHashEqualityAtRandomPoints` (F1: 5 seeds × 160 random mutations over relics,
potions, pets, listeners, history, selectors, fidelity and counters; at random points `Hash(x) == Hash(Clone(x))`, equal
bytes, `Hash(Clone(Clone(x)))`, the run clone, then 1–4 mutations of the clone leave the source, the clone of the clone
and the run clone unchanged), `KernelCloneSharesNoMutableObjects` (a reflection walker in the style of the P0
`GraphInspector`), `KernelOrbStatesCloneAndHash` and `KernelCloneHashEqualityAtRandomPointsWithOrbs` (W1Pending:
`OrbQueue.AddCapacity` / `TryEnqueue`), `KernelPendingSelectorClonesWithTheRun`. Expected values come from the
decompile rows cited in this section. Three tests print a `PENDING` line until character-mechanics implements the
orb queue; nothing else in the suite is pending.

### W1.10 Round-2 findings (`w1-result/verify-notes-state.md`)

| # | Finding | Resolution |
| --- | --- | --- |
| 1, 14 | `Append*`, the turn predicates and the queries are stubs | appends ported in phase 0b and re-verified (§W1.6.4); predicates (§W1.6.3) and queries (§W1.6.5) implemented and tested |
| 2, 15, 16 | allies, pets, `CreateCard` missing; creature queries skip pets; `AddMonster` ignores the side; no pet registration | §W1.3.1, §W1.4.3: `AddCreature` routing, `AddAlly` / `RemoveAlly`, `ContainsCreature`, `GetCreaturesOnSide` (+ opponents, teammates, current side), allies-then-enemies lookups, `AddPetInternal` / `OnPetDied` / `SetPetOwner`, `InvokeDiedEvent`. `CreateCard` re-verified (§W1.3.2); attaching owners in the P0 creation paths is §B12 A23 (cards-kernel, loop). The `_allCards` registry is not ported (§W1.12) |
| 3, 19 | stars never record `StarsModified` | §W1.4.6: by §B12 A16 `StarsCmd` is the only writer and records the entry (the scaffold's `SpendStars` does); recording in the state too would record twice. No approximation remains, so no flag |
| 4, 17 | `HistoryCardPlay` compares by value | equality by `PlayId` (§W1.6.5) |
| 5 | `Resources` cannot be filled | §B12 A11 landed `CardPlay.Resources`; `HistoryCardPlay.From` copies it (tested with manual-play values) |
| 6 | id-only entries cannot answer reads of departed cards and creatures | snapshots of the immutable facts (§W1.6.2); HistoryCourse's card object is an open question (§W1.12) |
| 7, 18 | inner lists shared between clones | side stores per history, records + views; never shared, nothing to rebind (§W1.6.6); a walker test proves no shared mutable object |
| 8 | clone cost not measured | §W1.6.6 and the timing test |
| 9 | no W1 section, stale P0 text | this section; pointers in §2.2, §3, §4, §5; entries cited as `Combat.History.Entries/<Entry>.cs:<line>` (finding 12) |
| 10, 21 | `CreateStandalone` applies TightBelt on every call | the A12 flag (phase 0b) re-verified and tested (§W1.2) |
| 11, 20 | monster listener state not hashed | §W1.5; `CardInstance.Listener` is a contract change request |
| 13 | state track absent | delivered |
| 22 | `ChoiceRuntime.DeepClone` shares selector objects | choice-owned file; contract change request (§W1.12). The state tests check hash equality and stack independence with a pending selector, not mutation of a shared selector |
| 23 | `CardPile.DeepClone` keeps the source owner | `DeepClone` now refuses a pile that holds an owned card (`InvalidOperationException`, clone owned cards with `CloneFor(newOwner)`) and clones ownerless piles through `CloneFor(null)`; the P0 tests that use it hold ownerless cards |

### W1.11 Gates in this worktree

`dotnet build sim/Sts2Sim.sln`: 0 warnings, 0 errors. `dotnet run --project sim/Sts2Sim.Tests`: every test passes
except the two content gates that fail on the base as well (`W1GateTests.Test_W1Gate_B2Chain_…` and
`Test_W1Gate_PilotContentIsRegistered`: pilot relics, potions, cards, powers, orbs and Osty are other owners');
`PENDING total` is 3, all from this suite and all on `OrbQueue` stubs (character-mechanics). Debug and Release:
415 passed, 2 failed, 417 total (the 374 of the base plus the 43 of this suite). Diff gate (P0 bundle set): 4/4
bundles agree, 69/69 decisions. No P0 canonical byte changed (empty history and absent listener write the P0 bytes),
so the EndToEnd pins hold.

Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`, 3 s; the base `263810d` from `git archive` and this tree,
interleaved base, W1, base, W1, base, W1, load average 11–20): base clone 12.2 / 9.9 / 8.0 µs, hash 5.5 / 7.1 /
3.2 µs, 120 024 / 126 706 / 213 023 decisions/s; this tree clone 9.4 / 13.8 / 7.4 µs, hash 4.5 / 6.6 / 4.3 µs,
162 779 / 93 331 / 176 154 decisions/s. No difference beyond the (large) noise, as expected: P0 combats reach none of
the new paths except the ally walk in `GetCreatureById` (one element) and the listener kind byte; the history stays
empty until the owners wire their recording sites, after which the digest keeps the hash O(1) in the log length.

### W1.12 Open questions, seams and contract change requests

1. **Pipeline (`CombatStatus`, `CreatureCmd`)**: `CombatStatus.IsAttached` and the side queries still look at the
   player and `Monsters` only; §B12 A19 asks them to count `cs.Allies` — `cs.ContainsCreature(c)` and
   `cs.GetCreaturesOnSide(side)` are the state ports to call. `KillWithoutCheckingWinCondition` should call
   `cs.InvokeDiedEvent(creature)` right after the `ShouldDie` gate (`Commands/CreatureCmd.cs:2770`) so pet deaths unlist
   the pet (`PlayerCombatState.cs:433-445`).
2. **Loop**: `CombatLoop.SetUpCombat` should call `player.ResetCombatState()` before filling the draw pile
   (`Combat/CombatManager.cs:6592`, `:6605`), and `Player.AfterCombatEnd` (A18) should use
   `player.CombatStateAfterCombatEnd()`. `PlayerCmd.AddPet` is `owner.AddPetInternal(pet)` then `CreatureCmd.Add(pet)`
   (→ `cs.AddCreature`, which routes the pet to the allies). The sim's `CardPlay` setters must not change between the
   Started and Finished appends (§W1.6.1).
3. **Cards-kernel**: `CardInstance.WriteCanonical` should write its listener with `ListenerCanonical.Write(w, Listener)`
   (byte-compatible with today's presence bool); `CardInstance.DeepClone()` keeps the source owner like the pile clone
   used to.
4. **Choice**: `ChoiceRuntime.DeepClone` copies the selector list but shares the selector objects; a stateful
   (`ICanonical`) selector is then shared between clones (Part A §6, §B1 "cloned and hashed"). Needs a selector clone
   protocol in `Choice/*`.
5. **Relics-potions (W3 readers)**: HistoryCourse needs the card object of the attack it replays; if that card left
   combat the sim has no copy. Options: a per-combat registry of removed cards (cloned and hashed) maintained by
   `CardPileCmd.RemoveFromCombat`, or a `FidelityFlag` at that site. Not needed by any W1 pilot.
6. **Deferred state** (no W1 reader): `EscapedCreatures` (`Combat/CombatState.cs:543`, `:733-737`; read by
   `Models.Encounters/GremlinMercNormal.cs:58` and `Models/EncounterModel.cs:326`, run-layer rewards), the all-cards
   registry `_allCards` (`:545`; read only by `CardPileCmd.Add`'s validity checks, `Commands/CardPileCmd.cs:201-210`,
   `:930`), the per-monster `Rng` (`:721`; only ToughEgg's skin, §B12 A21).
7. **Multiplayer**: `HistoryStamp` keeps one player turn number (M15); `AddCreature` accepts a second player on the
   ally side, but the run's other players would then be cloned and written twice — out of scope (single player).

### W1.13 Knowing deviations

Every difference from the decompile in this area concerns an input that no v0.111.0 game state produces, so none
records a `FidelityFlag` (a flag marks a rule approximation on a reachable state, §B2): exception types where the
game crashes with another type (`AddPotionInternal` with a slot beyond the slots, `GetPotionAtSlotIndex`,
`SetEnemyIndex` on an unregistered encounter); the missing "created for a different combat" check of `AddCreature`
and the missing silent return of `RemoveCreature` for an unattached creature (the sim keeps no creature → combat
reference); the combat's player is never removed; `NoId` actors for ownerless cards, orbs and potions (the game
throws); one stamped player (single player); `GetPet` skipping a listed pet that is not in the combat; the stars
history living in `StarsCmd` (§W1.4.6, a contract assignment, not an approximation); `CardPile.DeepClone` refusing
owned cards (sim plumbing without a game counterpart).
