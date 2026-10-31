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
| `Stars` setter calls `CombatManager.Instance.History.StarsModified` (`PlayerCombatState.cs:150`) | Dropped with the combat history (section 4). |
| `Creature.HoverTips`, `Name`, `LogName`, `HpDisplay`, `CreateVisuals`, `GetCreatureNode`, `SetNodeVisible`, VFX containers (`Creature.cs:475-612`, `:995-1011`, `:1341-1402`) | Dropped. |

## 4. Deferred, not in the P0 state

Each of these must join `WriteCanonical` and `DeepClone` when it is ported.

- Combat history (`Combat.History/CombatHistory.cs`): append-only log of card plays, draws, damage, block, energy, powers, moves. Cards and powers that ask "how many X this turn/combat" need it, as a value-type log or counters.
- Escaped creatures (`CombatState.cs:543`, `:733-737`), the all-cards registry (`:545`, `:666-701`), modifiers, badges and multiplayer scaling (`:564-585`).
- Pets (`PlayerCombatState.cs:20`, `:375-389`, `:432-445`), the orb queue (`:156-161`), relics, potions, gold.
- Per-monster `Rng` (`CombatState.cs:721`): seeded from run seed + map coordinates + act + combat id. In this decompile it is only read for `ToughEgg`'s skin (`Models.Monsters/ToughEgg.cs:552`). If a ported monster rolls rules with `base.Rng`, it belongs in `MonsterState` or the monster's extra state.
- `CardPile.RandomizeOrderInternal` (`CardPile.cs:241-246`, opening shuffle + `Hook.ModifyShuffleOrder`): the loop agent owns shuffling.
- `SortEnemiesBySlotName` / `SetEnemyIndex` (`CombatState.cs:958-980`), multiplayer HP scaling (`Creature.cs:986-993`).
- `PlayerCombatState.HasEnoughResourcesFor` (`:334-353`): the cards and loop agents implement playability.

## 5. Clone contract as implemented

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
