# Sts2Sim contracts

Part A (below, 2026-10-28/30) is the P0 walking-skeleton contract and remains the baseline.
Part B (2026-11-01) is the P1 kernel-expansion (W1) contract; where the two differ,
Part B wins. Part C (end of this file, 2026-11-01) is the W2 run-layer contract.

# Part A — P0 walking skeleton

Headless, deterministic port of the Slay the Spire 2 combat rules. Rule truth is the
decompile tree (read-only):
`/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/` (game v0.111.0,
one directory per namespace; written below as `<ns-dir>/<File>.cs:<line>`, with the
`MegaCrit.Sts2.Core.` prefix dropped). Plan: `docs/p0-sim-port-plan-2026-10-28.md`.

```
dotnet build sim/Sts2Sim.sln
dotnet run --project sim/Sts2Sim.Tests              # all tests
dotnet run --project sim/Sts2Sim.Tests -- Rng Hash  # only tests whose "Class.Method" contains a filter
dotnet run --project sim/Sts2Sim.Tests -- -v        # print stack traces for failures
dotnet run -c Release --project sim/Sts2Sim.Bench [encounterId] [seconds]
```

## 1. Ground rules (all agents)

- Port what the decompile says. Never use remembered StS1 numbers. Each constant or rule you
  port gets a `file:line` citation in `sim/specs/<area>.md` (your area's spec file).
- Pure BCL, net9.0. No NuGet, no Godot, no new project references. Nullable on.
- No narrative comments in code. Explanations and citations go in the spec file.
- **Keep the solution building.** Agents share one build (or merge back from isolated
  worktrees). Never leave a file that does not compile. Stubs throw `NotImplementedException`.
- **Synchronous rewrite.** Every `async Task` / `await` in the game becomes a plain
  synchronous call, in the same order. VFX, SFX, animation, waits, tweens, `TalkCmd`/`VfxCmd`
  and `choiceContext` plumbing are dropped. Record each dropped point in your spec when it
  could change the order of side effects.
- **Numbers.** The game computes modifiers in `decimal` and stores HP/block/amounts/energy as
  `int`. Keep exactly that. Keep every `(int)` truncation, `Math.Max(0m, x)`, and the order
  of additive → multiplicative → cap steps as in `Hooks/Hook.cs`.
- **Overflow.** C# default `unchecked`. `CheckForOverflowUnderflow=false` is set in every
  csproj. Seeding/hashing arithmetic must wrap exactly as in the game.
- **Context instead of globals.** Where the game reaches `CombatManager.Instance`,
  `RunManager.Instance`, `Owner.CombatState`, etc., the sim passes `CombatState cs`
  explicitly (always the first parameter). There is no static mutable state anywhere in Core
  except the lazily built, immutable registries.
- **Ascension.** `cs.HasAscension(level)` is `cs.Ascension >= (int)level`
  (`Entities.Ascension/AscensionManager.cs:22-24`). `AscensionHelper.GetValueIfAscension(level, a, b)`
  maps to `cs.GetValueIfAscension(level, a, b)`.
- **Ids.** Model ids are the game's `ModelId.Entry`: `StringHelper.Slugify(ClassName)`
  (`Models/ModelDb.cs:485-488`, `Helpers/StringHelper.cs:68-73`), e.g. `StrikeIronclad` →
  `STRIKE_IRONCLAD`, `NibbitsWeak` → `NIBBITS_WEAK`. Each def states its id as a string literal.

## 2. Ownership map

| Path under `sim/Sts2Sim.Core/` | Owner | Contents |
| --- | --- | --- |
| `Random/` | rng agent | `MegaRandom`, `Rng`, `RngSnapshot`, `DeterministicHash`, `RunRngSet`, `PlayerRngSet` |
| `Combat/Pipeline/` | pipeline agent | `AbstractModel` (hook virtuals), `Hook` (dispatcher), `DamageCmd`/`AttackCommand`, `CreatureCmd`, `PowerCmd`, `DamageResult`, `ValuePropExtensions` |
| `Powers/` | pipeline agent | `PowerModel` base + concrete powers (`Powers/Defs/*.cs`, one file per power) |
| `Cards/` | cards agent | `CardDef`, `CardInstance`, `CardEnergyCost`, `DynamicVar`, `DynamicVarSet` + concrete cards (`Cards/Defs/*.cs`, one file per card) |
| `Monsters/` | monsters agent | `MonsterDef`, `EncounterDef`, `MonsterSpawn`, `MonsterSpawner`, move-state-machine defs, `MonsterAiRuntime`, intents + concrete monsters (`Monsters/Defs/*.cs`) and encounters (`Monsters/Encounters/*.cs`) |
| `Combat/Loop/` | loop agent | `CombatSim` facade impl, `CombatLoop`, `LegalActionGenerator`, `ActionExecutor`, `CardPileCmd`, `PlayerCmd` |
| `State/` | state agent | `CanonicalWriter`, `StateHasher`, `ICanonical`, `CreatureState`/`PlayerState`/`MonsterState`, `CardPile`, `CombatState` (fields, `DeepClone`, `WriteCanonical`) |
| `Enums/`, `Api/`, `Registry/`, `GlobalUsings.cs`, `Cards/CardPlay.cs` | scaffold (frozen) | change only via the orchestrator |
| `sim/Sts2Sim.Tests/<Area>Tests.cs` | each agent its own file | `TestRunner.cs`, `Assert.cs`, `SmokeTests.cs` are frozen |
| `sim/Sts2Sim.Bench/` | orchestrator / bench step | |
| `sim/specs/<area>.md` | each agent its own file | `rng.md`, `pipeline.md`, `cards.md`, `monsters.md`, `loop.md`, `state.md`; `integration.md` (integrate step and later fidelity fixes) |

Change protocol for contract members (anything listed in section 4):
- Owners implement bodies freely and may **add** members, overloads and private helpers in
  their own files.
- Renaming, removing or changing the signature of a listed member needs the orchestrator,
  because other agents compile against it. If you believe a contract is wrong, keep it
  compiling, and report it in your `open_questions`.
- Never edit a file you do not own. If you need something from another area that does not
  exist yet, call the contract member (it throws until implemented) and test your own code
  with local test doubles.

## 3. Namespaces

`Sts2Sim.Core` (enums, `GameAction`, `CombatSim`), `.Random`, `.State`, `.Cards`, `.Powers`,
`.Monsters`, `.Combat.Pipeline`, `.Combat.Loop`, `.Registry`. `GlobalUsings.cs` imports all of
them inside Core, so Core files need no `using` for sim namespaces. Inside Core,
`Random` resolves to the namespace `Sts2Sim.Core.Random`; never use `System.Random` in Core.

## 4. Contract types

### 4.1 Enums (`Enums/`, frozen, ordinals identical to the game)

| Sim enum | Source |
| --- | --- |
| `CardType`, `TargetType`, `PileType`, `CardKeyword`, `CardRarity`, `CardTag`, `UnplayableReason` (flags), `CardPilePosition` | `Entities.Cards/<Name>.cs` |
| `PowerType`, `PowerStackType`, `PowerInstanceType` | `Entities.Powers/<Name>.cs` |
| `ValueProp` (flags) | `ValueProps/ValueProp.cs`; the sim adds `None = 0` (the game has no zero member) |
| `ModifyDamageHookType` (flags), `HpLossHookPhase` (flags) | `Hooks/<Name>.cs` |
| `CombatSide`, `PlayerTurnPhase` | `Combat/<Name>.cs` |
| `IntentType` | `MonsterMoves.Intents/IntentType.cs` |
| `MoveRepeatType` | `MonsterMoves/MoveRepeatType.cs` |
| `AscensionLevel` | `Entities.Ascension/AscensionLevel.cs` |
| `RunRngType`, `PlayerRngType` | `Entities.Rngs/<Name>.cs` |
| `CombatOutcome { InProgress, Victory, Defeat }` | sim-only |

### 4.2 Decision API (`Api/GameAction.cs` frozen; `Combat/Loop/CombatSim.cs` loop agent)

```csharp
enum ActionKind : byte { PlayCard, EndTurn }
readonly record struct GameAction(ActionKind Kind, int CardIndex, int TargetIndex)
    // GameAction.NoIndex = -1; GameAction.EndTurn; GameAction.Play(cardIndex, targetIndex = -1)
readonly record struct CardSpec(string Id, bool Upgraded)               // Api/CardSpec.cs (2026-10-30)

sealed class CombatSim
    static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0,
                            int? currentHp = null, int? maxHp = null)
    static CombatSim Create(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0,
                            int? currentHp = null, int? maxHp = null)   // rng is deep-cloned, never mutated
    static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension,
                            int? currentHp, int? maxHp)                 // CardSpec deck (upgrades); CombatRunContext.Default
    static CombatSim Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension,
                            int? currentHp, int? maxHp, CombatRunContext context)  // 2026-10-30: seeding/floor for encounter Rng
    static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension,
                            Action<CombatState> populateMonsters)     // test seam: replaces the encounter spawn
    static CombatSim FromState(CombatState state)
    CombatState State { get; }
    CombatOutcome Outcome { get; }          bool IsTerminal { get; }
    IReadOnlyList<GameAction> LegalActions()  // reused internal buffer; valid until the next call
    void LegalActions(List<GameAction> buffer)
    bool IsLegal(GameAction action)           // validation without side effects
    void Step(GameAction action)
    CombatSim Clone()                         // = FromState(State.DeepClone())
    ulong StateHash()                         // = StateHasher.Hash(State), per-instance scratch writer
    ulong StateHash(CanonicalWriter scratch)
    void WriteCanonical(CanonicalWriter w)
```

Semantics (loop agent implements, spec in `specs/loop.md`):
- `Create` builds the combat and runs it up to the first decision: combat start, player turn 1
  start, opening hand drawn, `Phase == Play`. `deckIds` are card ids in deck order (deck order
  matters for the opening shuffle). `seed` is used directly as `RunRngSet.Seed` (the already
  hashed numeric run seed, i.e. what `RunRngSet(string)` stores after hashing). The
  `RunRngSet` overload starts the combat from a copy of existing streams (for example a set
  restored with `RunRngSet.FromSnapshot` from a save, where the streams are mid-run).
- Deck rule: when `deckIds` is the Ironclad starting deck (`StartingDecks.IsIroncladStartingDeck`,
  compared by content and order), `Create` first applies the run-start ascension effect
  (`AscensionManager.ApplyEffectsTo`): at A5+ Ascender's Bane is appended last
  (`CombatLoop.DeckForCombat`, cards spec §1). Every other deck is used exactly as given, so a run
  deck that already holds the curse is not given a second one.
- HP: `maxHp` defaults to the character's starting HP (`StartingDecks.IroncladStartingHp` = 80)
  and `currentHp` to `maxHp`, at every ascension. Run-layer HP changes are passed explicitly, e.g.
  `currentHp: StartingDecks.IroncladHpAfterNeowArrival(ascension)` (64 at A2+) for a first combat
  after a Neow option that leaves HP alone. `currentHp` must be in `1..maxHp`.
- `CardIndex` indexes `State.Player.Hand.Cards` at decision time. `TargetIndex` indexes
  `State.Monsters` at decision time, or is `-1` for cards that take no creature target
  (`TargetType` other than `AnyEnemy`; others are resolved by the card/pipeline).
- `LegalActions` order is deterministic: hand index ascending, then target index ascending,
  then `EndTurn` last. `EndTurn` is legal at every decision point: `Outcome == InProgress`,
  `State.IsInProgress` and the Player side.
- `Step(PlayCard)` resolves the whole card play. `Step(EndTurn)` resolves player end of turn,
  the enemy turn, and the next player turn start, and returns at the next decision point or
  at a terminal state. Illegal actions throw `InvalidOperationException` and leave state
  unchanged.
- No card-choice prompts in P0. Superseded by Part B §B1 (W1 adds `UsePotion`, `DiscardPotion`,
  `ChoosePick`, `ChooseConfirm`, `ChooseCancel` and the `ChoicePrompt` runtime).

### 4.3 Random (`Random/`, rng agent)

```csharp
sealed class MegaRandom                     // xoshiro256** kernel, Random/MegaRandom.cs
    MegaRandom(ulong seed); MegaRandom(ulong s0, ulong s1, ulong s2, ulong s3)
    ulong S0..S3; static ulong Splitmix64(ref ulong x)
    void Reinitialise(ulong seed); void Reinitialise(ulong s0, ulong s1, ulong s2, ulong s3)
    int Next(int max); int Next(int min, int max); double NextDouble(); int NextInt(); uint NextUInt()
    ulong NextULong(); bool NextBool(); float NextFloat()
    MegaRandom DeepClone(); void WriteCanonical(CanonicalWriter w)

readonly record struct RngSnapshot(int Counter, ulong S0, ulong S1, ulong S2, ulong S3)  // = SerializableRng

enum RngSeedingScheme : byte { Xxh64, LegacyV18 }                       // 2026-10-30, Random/CombatRunContext.cs
readonly record struct CombatRunContext(int ActIndex, int TotalFloor, int? MapCol = null, int? MapRow = null,
                                        RngSeedingScheme Seeding = RngSeedingScheme.Xxh64)
    // Default (act 0, floor 2, xxh64); ForAct(actNumber, totalFloor, mapCol, mapRow, seeding); ParseSeeding(string)
    // EncounterSeed/CreateEncounterRng(runSeed, encounterIdEntry); MonsterSeed/CreateMonsterRng(runSeed, combatId)
// RngSeeds + uint LegacyV18Encounter(ulong runSeed, int totalFloor, string encounterIdEntry)

sealed class Rng                            // wrapper with _counter, Random/Rng.cs
    Rng(ulong seed = 0); Rng(ulong seed, string name); Rng(RngSnapshot s)
    int Counter; MegaRandom Kernel
    NextBool, NextInt(max), NextInt(min,max), NextUnsignedInt(..), NextUnsignedLong(..), NextFloat(..),
    NextDouble(..), NextGaussianFloat/Double/Int, NextItem<T>, WeightedNextItem<T>, Shuffle<T>(IList<T>)
    RngSnapshot ToSnapshot(); Rng DeepClone(); void WriteCanonical(CanonicalWriter w)

static class DeterministicHash              // StringHelper port
    ulong GetDeterministicHashCode(string)   // UTF-8 bytes -> XxHash64 seed 0 (Helpers/StringHelper.cs:105-118)
    int GetDeterministicHashCodeOld(string); ulong XxHash64(ReadOnlySpan<byte>, long seed = 0)
    string SnakeCase(string); string Slugify(string)

sealed class RunRngSet                      // Runs/RunRngSet.cs; one Rng per RunRngType
    RunRngSet(ulong seed)                    // streams = new Rng(seed, SnakeCase(type.ToString()))
    RunRngSet(string seed)                   // "old" prefix path + GetDeterministicHashCode
    RunRngSet(Rng shared)                    // mock seam: every stream is the same Rng instance
    string StringSeed; ulong Seed; Rng Get(RunRngType)
    UpFront, Shuffle, UnknownMapPoint, CombatCardGeneration, CombatPotionGeneration, CombatCardSelection,
    CombatEnergyCosts, CombatTargets, MonsterAi, Niche, CombatOrbGeneration, TreasureRoomRelics
    RunRngSet DeepClone(); void WriteCanonical(CanonicalWriter w)

sealed class PlayerRngSet                   // Random/PlayerRngSet.cs; Rewards, Shops, Transformations

sealed class RunRngSetSnapshot              // = SerializableRunRngSet
    string? Seed; ulong? HashedSeed; Dictionary<RunRngType, RngSnapshot> Rngs
    // RunRngSet.ToSnapshot() / FromSnapshot(s) / LoadFromSnapshot(s)
sealed class PlayerRngSetSnapshot           // = SerializablePlayerRngSet; ulong Seed; Rngs

static class RngCollectionExtensions        // Extensions/ListExtensions.cs, IEnumerableExtensions.cs
    List<T> UnstableShuffle<T>(this List<T>, Rng)
    List<T> StableShuffle<T>(this List<T>, Rng)                       // T : IComparable<T>
    List<T> StableShuffle<T>(this List<T>, Rng, IComparer<T>); StableShuffle<T>(this List<T>, Rng, Comparison<T>)
    IEnumerable<T> TakeRandom<T>(this IEnumerable<T>, int count, Rng)
```

`RngCollectionExtensions` is the only definition of these extension methods in Core (rng spec §8,
§13a); the loop's opening shuffle and reshuffle call them.

`RunRngSetSnapshot.HashedSeed` is **sim-only**; the game's `SerializableRunRngSet` has only the
string seed. `ToSnapshot` writes it; when it is present, `FromSnapshot` rebuilds the set from it
(so numeric-seed sets round-trip) and `LoadFromSnapshot` also checks it. A snapshot in the game's
format (`HashedSeed == null`) takes the game path, `new RunRngSet(save.Seed)` (rng spec §11).

Notes: `System.IO.Hashing.XxHash64` is a NuGet package, so XXH64 must be written by hand and
tested against known vectors. Every `Next*` that advances the kernel increments `Counter`
exactly as `Random/Rng.cs` does (some overloads delegate; do not double count).
`RunRngSet.DeepClone` must preserve aliasing (the mock set clones its one shared `Rng` once).

### 4.4 State (`State/`, state agent)

```csharp
interface ICanonical { void WriteCanonical(CanonicalWriter w); }

sealed class CanonicalWriter                // little-endian, growable byte buffer
    CanonicalWriter(int initialCapacity = 4096)
    int Length; ReadOnlySpan<byte> WrittenSpan; void Reset(); byte[] ToArray()
    WriteByte, WriteBool, WriteInt32, WriteUInt32, WriteInt64, WriteUInt64, WriteSingle, WriteDouble,
    WriteDecimal, WriteString(string?), WriteTag(ushort), WriteCount(int)

static class StateHasher
    ulong Hash(ReadOnlySpan<byte> canonicalBytes); ulong Hash(ICanonical state, CanonicalWriter? scratch = null)

abstract class CreatureState : ICanonical   // Entities.Creatures/Creature.cs
    int CombatId; CombatSide Side; int CurrentHp, MaxHp, Block; List<PowerModel> Powers
    bool IsAlive (CurrentHp > 0), IsDead, IsEnemy, IsPlayer, IsMonster
    T? GetPower<T>(); PowerModel? GetPower(string id); bool HasPower<T>(); int GetPowerAmount<T>()
    abstract CreatureState DeepCloneCreature()

sealed class PlayerState : CreatureState     // Entities.Players/Player.cs + PlayerCombatState.cs
    PlayerState(int combatId, string characterId, int currentHp, int maxHp, int maxEnergy)
    string CharacterId; int BaseMaxEnergy, Energy, Stars, TurnNumber; PlayerTurnPhase Phase
    CardPile DrawPile, Hand, DiscardPile, ExhaustPile, PlayPile; CardPile GetPile(PileType)
    bool IsActiveForHooks; void ActivateHooks(); void DeactivateHooks()   // Player.cs:214-221, :1278-1286
    void EndOfTurnCleanup(); void IncrementTurnNumber(); GainStars/LoseStars; CardPile? FindPileContaining(card)
    IReadOnlyList<CardPile> AllPiles                                       // Hand, Draw, Discard, Exhaust, Play
    PlayerState DeepClone()

sealed class MonsterState : CreatureState    // Creature with Monster != null
    MonsterState(int combatId, MonsterDef def, int currentHp, int maxHp, MonsterExtraState? extra, string? slotName)
    MonsterDef Def; string MonsterId; MonsterExtraState? Extra; string? SlotName; int? MaxHpBeforeModification
    MonsterAiRuntime Ai; bool HasAi; void InitAi(MonsterAiRuntime ai)   // set once, by MonsterLifecycle.SetUpForCombat
    bool SpawnedThisTurn                      // MonsterModel.SpawnedThisTurn (Models/MonsterModel.cs:530-541)
    bool IsStunned (Ai.NextMove.Id == "STUNNED", Creature.cs:672)
    MonsterState DeepClone()

sealed class CardPile : ICanonical           // Entities.Cards/CardPile.cs
    CardPile(PileType type, int capacity = 16)
    PileType Type; IReadOnlyList<CardInstance> Cards; List<CardInstance> MutableCards; int Count; bool IsEmpty
    CardInstance this[int]; IndexOf; Contains; AddInternal(card, index = -1); RemoveInternal(card);
    RemoveAtInternal(index); ClearInternal(); CardPile DeepClone()

sealed class CombatState : ICanonical        // Combat/CombatState.cs + CombatManager turn state
    CombatState(PlayerState player, string encounterId, int ascension, RunRngSet rng)
    PlayerState Player; List<MonsterState> Monsters; string EncounterId; int Ascension; RunRngSet Rng
    int RoundNumber; CombatSide CurrentSide; CombatOutcome Outcome; bool IsOver
    bool IsInProgress, IsStarting             // CombatTurnState.IsInProgress / IsStarting (Combat/CombatTurnState.cs:99-115)
    int NextCreatureId, NextCardInstanceId; int AllocateCreatureId(); int AllocateCardInstanceId()
    bool HasAscension(AscensionLevel); GetValueIfAscension(level, a, b) for int/decimal/float
    CreatureState? GetCreatureById(int); void GetCreatures(List<CreatureState>); void GetHittableEnemies(List<MonsterState>)
    void AddMonster(MonsterState); void RemoveMonster(MonsterState)
    CombatState DeepClone()
```

Turn-state flags (`CombatManager`, `Combat/CombatManager.cs:6113-6144`, `:4489-4490`, `:7133-7141`):

| Window | `IsStarting` | `IsInProgress` | `Outcome` |
| --- | --- | --- | --- |
| encounter spawn, before `SetUpCombat` (the game has no turn state yet) | false | false | `InProgress` |
| `SetUpCombat` up to the end of `StartCombat`'s `AfterCreatureAdded` loop | true | false | `InProgress` |
| combat running; a pending loss is `Outcome == Defeat` with `IsInProgress` still true | false | true | `InProgress` / `Defeat` |
| after victory or a processed loss | false | false | `Victory` / `Defeat` |

A new `CombatState` is in the first window. Hand-built test states set `IsInProgress = true`
themselves. `Rng` may be `null` in hand-built test states that need no randomness (presence flag
in the canonical form, state spec §7); combats built through `CombatLoop`/`CombatSim` always have
one.

`CardPile` order mirrors the game's `CardPile._cards` list order exactly (the loop agent
documents which end the game draws from). `Monsters` mirrors the game's `_enemies` list:
same order, same insertion/removal points (dead monsters leave it when
`Hook.ShouldCreatureBeRemovedFromCombatAfterDeath` says so).

### 4.5 Cards (`Cards/`, cards agent; `CardPlay.cs` frozen)

```csharp
abstract class CardDef                       // static definition = game CardModel canonical instance
    protected CardDef(string id, int canonicalCost, CardType type, CardRarity rarity, TargetType targetType, bool costsX = false)
    string Id; int CanonicalCost; bool CostsX; CardType Type; CardRarity Rarity; TargetType TargetType
    virtual int CanonicalStarCost => -1; virtual int MaxUpgradeLevel => 1
    virtual IReadOnlyCollection<CardKeyword> CanonicalKeywords; virtual IReadOnlyCollection<CardTag> CanonicalTags
    protected virtual IEnumerable<DynamicVar> CanonicalVars        // fresh objects on every call
    DynamicVarSet CreateVars()
    abstract void OnPlay(CombatState cs, CardPlay play)            // effect entry point (= OnPlay)
    virtual void OnUpgrade(CardInstance card)
    virtual bool IsPlayable(CombatState cs, CardInstance card)     // card-logic gate (BlockedByCardLogic)

class DynamicVar : ICanonical                // Localization.DynamicVars/DynamicVar.cs
    DynamicVar(string name, decimal baseValue, ValueProp props = None)
    string Name; decimal BaseValue; ValueProp Props; bool WasJustUpgraded; int IntValue
    void UpgradeValueBy(decimal); void FinalizeUpgrade(); virtual DynamicVar Clone()
sealed class DynamicVarSet : ICanonical      // indexer by name; Damage ("Damage"), Block ("Block"); DeepClone()

sealed class CardEnergyCost : ICanonical     // Entities.Cards/CardEnergyCost.cs
    int Canonical, Base, CapturedXValue; bool CostsX, WasJustUpgraded
    int GetResolved(); int GetAmountToSpend(); void UpgradeBy(int); bool EndOfTurnCleanup(); bool AfterCardPlayedCleanup()
    int GetWithModifiers(CombatState cs, CardInstance card, CostModifiers)   // in-combat overloads: include the
    int GetAmountToSpend(CombatState cs, CardInstance card)                  // global Hook.ModifyEnergyCostInCombat;
    int GetResolved(CombatState cs, CardInstance card)                       // combat code always uses these

sealed class CardInstance : ICanonical       // runtime card = mutable CardModel
    CardInstance(CardDef def, int instanceId)
    CardDef Def; string Id; int InstanceId; CardType Type; TargetType TargetType; int UpgradeLevel; bool IsUpgraded
    DynamicVarSet Vars; CardEnergyCost EnergyCost; int KeywordMask (bit = 1 << (int)CardKeyword)
    bool ExhaustOnNextPlay, HasSingleTurnRetain, HasSingleTurnSly; int BaseReplayCount
    HasKeyword/AddKeyword/RemoveKeyword; HasTag; void Upgrade(); CardInstance DeepClone()

sealed class CardPlay                        // transient, never cloned or hashed (Entities.Cards/CardPlay.cs)
    CardInstance Card; CreatureState? Target; PileType ResultPile; int EnergySpent, StarsSpent
    bool IsAutoPlay; int PlayIndex, PlayCount = 1; IsFirstInSeries; IsLastInSeries
```

```csharp
static class StartingDecks                   // Models.Characters/Ironclad.cs + run-start effects
    const string IroncladCharacterId = "IRONCLAD"; const int IroncladStartingHp = 80, IroncladMaxEnergy = 3
    ReadOnlyCollection<string> Ironclad                       // 5 Strike, 4 Defend, Bash (Ironclad.cs:40-52)
    IReadOnlyList<string> IroncladAtRunStart(int ascension)   // + ASCENDERS_BANE last at A5+ (AscensionManager.cs:33-38)
    IReadOnlyList<string> ApplyAscensionEffectsToDeck(IReadOnlyList<string> deck, int ascension)
    bool IsIroncladStartingDeck(IReadOnlyList<string> deckIds)  // content and order
    int IroncladHpAfterNeowArrival(int ascension); int HpAfterNeowArrival(int maxHp, int ascension)  // run layer
```

Card effects call the Cmd facades in the same order as the game's `OnPlay`, e.g. Strike:
`DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(play.Target!).Execute();`
A `CardDef` is immutable and shared by all instances and clones; all mutable per-card state
lives on `CardInstance`. Deck ids passed to `CombatSim.Create` are `CardDef.Id`s. Registered
cards: `STRIKE_IRONCLAD`, `DEFEND_IRONCLAD`, `BASH`, `ASCENDERS_BANE` (cards spec §3).

### 4.6 Pipeline and powers (`Combat/Pipeline/`, `Powers/`, pipeline agent)

`AbstractModel` carries the hook virtuals with the game's names and default return values
(`Models/AbstractModel.cs:228-1097`), each with `CombatState cs` added as first parameter and
`Creature`→`CreatureState`, `Player`→`PlayerState`, `CardModel`→`CardInstance`. The P0
subset: turn hooks (`BeforeSideTurnStart`, `AfterSideTurnStart[Late]`,
`AfterPlayerTurnStart[Early|Late]`, `BeforeSideTurnEnd[VeryEarly|Early]`,
`AfterSideTurnEnd[Late]`), card hooks (`BeforeCardPlayed`, `AfterCardPlayed[Late]`,
`AfterCardDrawn`, `AfterCardExhausted`, `AfterCardDiscarded`, `AfterShuffle`,
`BeforeHandDraw`, `AfterEnergyReset`, `AfterEnergySpent`), damage/block/power/death hooks, the
modify hooks (`ModifyDamageAdditive/Multiplicative/Cap`, `ModifyBlockAdditive/Multiplicative`,
`ModifyHpLost{Before,After}Osty[Late]`, `ModifyHandDraw[Late]`, `ModifyEnergyGain`,
`ModifyMaxEnergy`, `ModifyPowerAmountGiven*`, `TryModifyPowerAmountReceived`,
`TryModifyEnergyCostInCombat`, `ModifyAttackHitCount`), and the `Should*` gates. Add more
hooks (additively) when a ported card/power/monster needs one.

```csharp
static class Hook        // dispatcher = Hooks/Hook.cs; one static method per hook, same names
    void IterateCombatHookListeners(CombatState cs, List<AbstractModel> buffer)   // order = game's
    decimal ModifyDamage(cs, target, dealer, damage, props, cardSource, cardPlay, ModifyDamageHookType = All)
    decimal ModifyBlock(cs, target, block, props, cardSource, cardPlay)
    decimal ModifyHpLost(cs, target, amount, props, dealer, cardSource, HpLossHookPhase)
    ... Should*/Before*/After* dispatchers as declared in Hook.cs

static class DamageCmd   AttackCommand Attack(CombatState cs, decimal damagePerHit)
sealed class AttackCommand   // Commands.Builders/AttackCommand.cs; DamageProps defaults to Move (:653)
    FromCard(card, play); FromMonster(monster); Targeting(target); TargetingAllOpponents();
    TargetingRandomOpponents(allowDuplicates = true); Unpowered(); WithHitCount(n); Execute()
    Attacker, ModelSource, CardPlay, SingleTarget, HitCount, HitsCompleted, DamageProps, Results
static class CreatureCmd Damage(..), GainBlock(..), LoseBlock(..), ClearBlock(..), Heal, SetCurrentHp, GainMaxHp, Kill, Stun
                         BeforeTurnStart(cs, creature, side)   // Creature.BeforeTurnStart: AmountOnTurnStart snapshot
                         AfterTurnStart(cs, creature, side)    // Creature.AfterTurnStart: block clear, turn-1 player exemption
static class PowerCmd    Apply<T>(cs, target, amount, applier, cardSource, silent) where T : PowerModel, new()
                         Apply(cs, power, ...), FindExistingInstanceForStacking, ModifyAmount, Decrement,
                         TickDownDuration, Remove<T>, Remove
static class EnergyCmd   GetMaxEnergy(cs, player); ResetEnergy(cs, player); AddMaxEnergyToCurrent(cs, player)
                         GainEnergy(cs, amount, player); LoseEnergy(cs, amount, player); SetEnergy(cs, amount, player)
                         SpendEnergy(cs, card, int amount); GainEnergyInternal/LoseEnergyInternal(player, amount)
static class CombatStatus                // CombatManager status queries and Creature combat predicates
    bool IsInProgress(cs); bool IsStarting(cs); bool IsAboutToLose(cs)       // PendingLoss != null
    bool IsEnding(cs); bool IsOverOrEnding(cs); void LoseCombat(cs)          // CombatManager.cs:6113-6144, :6548-6567, :7124-7131
    bool AnyPrimaryEnemyAlive(cs); bool IsPrimaryEnemy(c); bool IsSecondaryEnemy(c)
    bool IsAttached(cs, c); bool IsActiveForHooks(cs, player); bool IsHittable(cs, c); bool CanReceivePowers(cs, c)
    CreatureState CardOwnerCreature(cs, card); GetCreaturesOnSide/GetOpponentsOf/GetTeammatesOf(cs, .., buffer)
    CombatSide OppositeSide(side)
sealed class DamageResult  Receiver, Props, BlockedDamage, UnblockedDamage, OverkillDamage, TotalDamage,
                           WasBlockBroken, WasFullyBlocked, WasTargetKilled
static class ValuePropExtensions  IsPoweredAttack(), HasProp()
```

Hook overloads with outputs (callers outside the pipeline use them; the plain overloads drop the
output): the `Modify*` dispatchers whose game version returns `out IEnumerable<AbstractModel>
modifiers` take `ref List<AbstractModel>? modifiers` (allocated on the first modifier):
`ModifyDamage`, `ModifyBlock`, `ModifyHpLost`, `ModifyHandDraw`, `ModifyCardPlayCount`,
`ModifyEnergyGain`, `ModifyPowerAmountGiven`, `ModifyPowerAmountReceived`. The gates that report
the deciding model take `out AbstractModel? preventer` (`ShouldAllowTargeting`, `ShouldClearBlock`,
`ShouldDie`, `ShouldPlay`) or `out AbstractModel? modifier` (`ShouldDraw`).

```csharp
abstract class PowerModel : AbstractModel, ICanonical    // Models/PowerModel.cs
    abstract string Id; abstract PowerType Type; abstract PowerStackType StackType
    virtual PowerInstanceType InstanceType => None; virtual bool AllowNegative => false
    int Amount (set only via SetAmount); int AmountOnTurnStart; bool SkipNextDurationTick
    int ApplierId, TargetId (= PowerModel.NoCreature when absent)
    CreatureState Owner; bool HasOwner; void AttachTo(CreatureState owner)
    SetAmount(int); GetTypeForAmount(decimal); ShouldRemoveDueToAmount()
    virtual BeforeApplied / AfterApplied / AfterRemoved; ShouldPowerBeRemovedAfterOwnerDeath; ShouldOwnerDeathTriggerFatal
    PowerModel CloneFor(CreatureState newOwner)   // MemberwiseClone + rebind owner + CopyMutableReferenceState
    protected virtual void CopyMutableReferenceState(PowerModel source)  // deep-copy any reference-type fields
    void WriteCanonical(w); protected virtual void WriteExtraCanonical(w)  // subclasses append extra fields
```

Powers are one class per file under `Powers/Defs/`, with a public parameterless constructor,
created via `new T()` by `PowerCmd.Apply<T>`. Per-power constants that the game keeps in
`DynamicVars` (e.g. `VulnerablePower` "DamageIncrease" = 1.5m) become `const`/`static readonly`
values on the class, cited in `specs/pipeline.md`.

### 4.7 Monsters (`Monsters/`, monsters agent)

```csharp
abstract class MonsterDef                     // Models/MonsterModel.cs
    protected MonsterDef(string id); string Id
    abstract int MinInitialHp(CombatState cs); abstract int MaxInitialHp(CombatState cs)
    virtual MonsterExtraState? CreateExtraState()            // per-instance mutable fields (_isAlone, counters)
    abstract MonsterMoveStateMachineDef GenerateMoveStateMachine(CombatState cs, MonsterState self)
    virtual void AfterAddedToRoom(CombatState cs, MonsterState self)
abstract class MonsterExtraState : ICanonical { abstract DeepClone(); abstract WriteCanonical(w); }

abstract class EncounterDef                   // Models/EncounterModel.cs
    protected EncounterDef(string id); string Id; virtual bool IsWeak
    abstract IReadOnlyList<MonsterSpawn> GenerateMonsters(CombatState cs)
sealed record MonsterSpawn(MonsterDef Def, string? SlotName = null, Action<MonsterState>? Configure = null)

// 2026-10-30 (rng-encounter-private.md §6): MonsterDef + virtual bool UsesPrivateRng => false;
//   virtual void InitPrivateRng(cs, self, rng)   (no P0 monster opts in)
// EncounterDef + virtual IReadOnlyList<string> Slots => []; virtual GenerateMonsters(cs, rng) => GenerateMonsters(cs);
//   protected EncounterRngRequired()
// MonsterSpawner + Spawn/SpawnEncounter/CreateEncounterRng(cs, …, CombatRunContext);
//   SortEnemiesBySlotName(cs, encounter); IndexOfSlot(slots, slotName)
//   (the setup slot sort runs after each slotted monster's SetUpForCombat)
static class MonsterSpawner
    MonsterState Spawn(CombatState cs, MonsterSpawn spawn)  // allocate CombatId, CreateExtraState, Configure, roll HP (Niche)
    void SpawnEncounter(CombatState cs, EncounterDef encounter, List<MonsterState>? spawned = null)  // spawn + add, one by one
    int RollUniqueMonsterHp(cs, self, min, max, Rng)
static class MonsterLifecycle                 // the monster-side steps the loop calls, one per game method
    void SetUpForCombat(cs, m)                // MonsterModel.SetUpForCombat: InitAi(GenerateMoveStateMachine), SpawnedThisTurn = true
    void AfterCreatureAdded(cs, m)            // CombatManager.AfterCreatureAdded: Def.AfterAddedToRoom, first RollMove
    MoveStateDef RollMove(cs, m); void PrepareForNextTurn(cs, m, bool rollNewMove = true)
    void TakeTurn(cs, m); void PerformMove(cs, m); void OnSideSwitch(m)
    void SetMoveImmediate(m, state, bool forceTransition = false); void StunInternal(cs, m, MovePerform, string? nextMoveId)
    bool IntendsToAttack(m); IReadOnlyList<CreatureState> PlayerCreatures(cs)

delegate void MovePerform(CombatState cs, MonsterState self, IReadOnlyList<CreatureState> targets)
abstract class AiStateDef                     // MonsterMoves.MonsterMoveStateMachine/MonsterState.cs
    string Id; ShouldAppearInLogs; IsMove; CanTransitionAway(MonsterAiRuntime); string? GetNextState(cs, owner, rng)
sealed class MoveStateDef : AiStateDef        // MoveState.cs
    MoveStateDef(string stateId, MovePerform perform, params AbstractIntent[] intents)
    Intents; Perform; bool MustPerformOnceBeforeTransitioning { get; set; }
    string? FollowUpStateId { get; init; }; AiStateDef? FollowUpState { get; set; }   // as in MoveState.cs:102-127
sealed class RandomBranchStateDef : AiStateDef  // RandomBranchState.cs; StateWeight(StateId, RepeatType, MaxTimes, Weight, Cooldown); AddBranch overloads
sealed class ConditionalBranchStateDef : AiStateDef  // ConditionalBranchState.cs; AddState(move, Func<CombatState, MonsterState, bool>? condition)
sealed class MonsterMoveStateMachineDef       // immutable graph: States by id, InitialStateId
    MonsterMoveStateMachineDef(IEnumerable<AiStateDef> states, AiStateDef initialState)  // duplicate state ids throw
sealed class MonsterAiRuntime : ICanonical    // MonsterMoveStateMachine.cs mutable part
    Machine; CurrentStateId; PerformedFirstMove; StateLog (ids); PerformedAtLeastOnce (ids)
    MoveStateDef? NextMove                    // null = the game's UNSET_MOVE; PerformMove then throws "No move has been set for the monster"
    bool IsPerformingMove                     // MonsterModel.IsPerformingMove; CreatureCmd.Kill reads it
    MoveStateDef RollMove(cs, owner, rng); ForceCurrentState(id); OnMovePerformed(move); DeepClone()

AbstractIntent { IntentType }; AttackIntent { BaseDamage, DamageCalc, GetRepeats, GetTotalDamage, GetSingleDamage }
SingleAttackIntent (not sealed; DeathBlowIntent derives from it), MultiAttackIntent, DebuffIntent(strong),
StatusIntent(count), SimpleIntent(type), Intents.Defend/Buff/...
```

Changes the monsters agent made to the original §4.7 sketch, all kept: `FollowUpState` and
`MustPerformOnceBeforeTransitioning` are settable and `FollowUpStateId` is init-only, exactly like
the game's construction members (`MoveState.cs:102-127`); a conditional branch accepts a `null` condition,
which counts as true (`ConditionalBranchState.cs:23-30`); `SingleAttackIntent` is not sealed;
`NextMove == null` stands for the game's `UNSET_MOVE` (`MoveState.cs:145-148`,
`Models/MonsterModel.cs:515-522`); duplicate state ids throw `ArgumentException` like the game's
`Dictionary.Add` (`MoveState.cs:182-185`). `AttackIntent.BaseDamage` is sim-only (0 for an intent
built from a `Func`).

The move graph is built once per monster in `MonsterLifecycle.SetUpForCombat` and shared (not
copied) by clones. It must not capture the `MonsterState`, the `CombatState`, or any mutable
object: every lambda reads state through its `(cs, self)` parameters. Values captured at build
time (ascension-dependent damage, structure chosen from `_isAlone`) must be immutable. The setters
above are for graph construction only; nothing may change a state def after `GenerateMoveStateMachine`
returns, because clones share it. Everything that changes during combat lives in
`MonsterAiRuntime` or `MonsterExtraState`.

`MonsterDef.AfterAddedToRoom` runs during combat setup (`IsStarting`, not `IsInProgress`): commands
gated on `IsOverOrEnding` (block gain, draws, attacks, pile moves) do nothing there, while
`IsEnding`-gated ones (`PowerCmd.Apply`, energy) and guarded hooks still run (loop spec §2).

### 4.8 Loop (`Combat/Loop/`, loop agent)

```csharp
static class CombatLoop
    CombatState CreateCombat(ulong seed, deckIds, encounterId, int ascension [, Action<CombatState>? populateMonsters])
    CombatState CreateCombat(RunRngSet rng, deckIds, encounterId, int ascension, int? currentHp, int? maxHp,
                             Action<CombatState>? populateMonsters = null)       // = SetUpCombat + StartCombat
    CombatState SetUpCombat(...same overloads...)   // spawn, then IsStarting: shuffle, SetUpForCombat; no decision yet
    void StartCombat(cs)                            // AfterCreatureAdded loop, IsInProgress, BeforeCombatStart, turn 1
    IReadOnlyList<string> DeckForCombat(deckIds, int ascension)   // starter-deck ascension rule (§4.2)
    StartPlayerTurn; EndPlayerTurn; RunEnemyTurn; CheckWinCondition; CheckForEmptyHand(cs, player); Step(cs, action)
static class LegalActionGenerator  Fill(cs, buffer); CanPlay(cs, card, out UnplayableReason); IsAtDecisionPoint(cs);
                                   IsLegal(cs, action); TryResolve(cs, action, out target, out error)

// 2026-10-30: CombatLoop + CreateCombat/SetUpCombat(RunRngSet, IReadOnlyList<CardSpec>, encounterId|EncounterDef,
//   ascension, currentHp, maxHp, CombatRunContext [, populateMonsters]) — existing signatures unchanged;
//   without a context they run with CombatRunContext.Default (xxh64, floor 2); see rng-encounter-private.md §6
static class ActionExecutor   PlayCard(cs, handIndex, target)
static class CardPileCmd      Draw(cs, player) / Draw(cs, count, player, fromHandDraw); Shuffle; ShuffleIfNecessary;
                              Add; AddGeneratedCardToCombat; Discard; Exhaust; RemoveFromCombat
static class PlayerCmd        GainEnergy; LoseEnergy; SetEnergy   // delegate to EnergyCmd
```

The sequence is `Combat/CombatManager.cs` (turn phases, `CombatTurnState`) and
`GameActions/ActionExecutor.cs`, with the network/lock-step and UI waits removed. The loop keeps
the game's turn-state flags on `CombatState` (§4.4): a decision exists only while `IsInProgress`,
and `Step` rejects any action while the combat is still being set up.

## 5. Identity and references

- Creatures: `CreatureState.CombatId`, allocated by `CombatState.AllocateCreatureId()`
  (mirrors `Creature.CombatId` / `CombatState._nextCreatureId`). Persistent cross-references
  (power applier/target) are stored as ids, never as object references.
- `PowerModel.Owner` is a reference; `CloneFor(newOwner)` rebinds it during clone.
- Cards: `CardInstance.InstanceId`, allocated by `CombatState.AllocateCardInstanceId()` in
  deck order at combat creation, then for generated cards. Ids are never reused.
- Defs (`CardDef`, `MonsterDef`, `EncounterDef`, move graphs, intents) are immutable and shared
  between clones. They hold no per-combat state.

## 6. Clone and canonical rules

- Every stateful type exposes `DeepClone()` (or `CloneFor` for powers,
  `DeepCloneCreature` for creatures) and `WriteCanonical(CanonicalWriter)`.
- `DeepClone` copies all mutable state, shares immutable defs, and produces an object graph
  with no reference into the source. After cloning, stepping either copy must never change
  the other. Aliasing inside one state is preserved (one object in the source → one object
  in the clone), e.g. the shared `Rng` of a mock `RunRngSet`.
- `WriteCanonical` writes every field that can influence future rules, in a fixed order,
  using only `CanonicalWriter` primitives:
  - Lists: `WriteCount(n)` then the elements in list order (order is game state).
  - Sets/dictionaries: sorted by key with `StringComparer.Ordinal` / numeric order.
  - Strings: `WriteString` (UTF-8, length-prefixed). Ids are written as strings.
  - `decimal`: `WriteDecimal` writes a scale-normalized form, so `1.5m` and `1.50m` hash the same.
  - Floats/doubles by IEEE bit pattern. Booleans as one byte.
  - Optional values: a presence `bool` first.
  - Each composite starts with a `WriteTag` so that adjacent fields cannot alias.
  - RNG streams write `Counter` and `S0..S3`.
  - Transient data (`CardPlay`, scratch buffers, caches) is not written.
- `StateHasher.Hash` is a deterministic 64-bit hash of the canonical bytes, stable across
  processes and machines (no `string.GetHashCode`, no `HashCode`).
- Invariant tests every area should have: `Hash(x) == Hash(x.DeepClone())`; mutate the clone →
  the source hash is unchanged; the same seed and action sequence → the same hash sequence.
- Budget: `CombatSim.Clone()` ≤ 1 ms, so clone only what is mutable. Prefer arrays/lists
  of value types and `MemberwiseClone` over LINQ and dictionaries on hot paths.

## 7. Registry pattern

Registries are built by reflection over the Core assembly (`Registry/Registries.cs`, frozen),
the sim analogue of the game's `ModelDb`:

- `CardRegistry` = every concrete `CardDef` subclass; `MonsterRegistry` = every concrete
  `MonsterDef`; `EncounterRegistry` = every concrete `EncounterDef`; `PowerRegistry` = every
  concrete `PowerModel` (id → factory).
- A def is registered by existing: a `public sealed class Xxx : CardDef` with a public
  parameterless constructor, anywhere in `Sts2Sim.Core`. Adding a card/monster/encounter/power
  is adding one file in your own `Defs/` folder. Nobody edits a shared list.
- Ids must be unique; duplicates throw on first registry access. Mark a type
  `[SimExclude]` to keep it out of the registry (test doubles, abstract-like helpers).
- Registries are built lazily on first access and never mutated. Def constructors must be
  cheap and side-effect free (they run once at registry build). Never put mutable state in
  a def.
- Test doubles defined in the Tests assembly are not discovered; construct them directly.

## 8. Test conventions

- Zero-dependency runner (`sim/Sts2Sim.Tests/TestRunner.cs`): every `public static`
  parameterless method named `Test_*` in a public class of the Tests assembly is run. Prints
  `PASS`/`FAIL` per test and a summary; exit code 0 = all passed, 1 = failures, 2 = no test
  matched the filter.
- One file per area. The files as they exist: `RngTests.cs` (`Test_Rng_*`), `PipelineTests.cs`
  (`Test_Pipeline_*`), `CardTests.cs` (`Test_Cards_*`), `MonsterTests.cs` (`Test_Monsters_*`),
  `LoopTests.cs` (`Test_Loop_*`), `DeterminismTests.cs` (state area, `Test_State_*`),
  `EndToEndTests.cs` (integration, `Test_EndToEnd_*`), and the frozen `SmokeTests.cs`
  (`Test_Smoke_*`). Name tests `Test_<Area>_<Behavior>` so that
  `dotnet run --project sim/Sts2Sim.Tests -- <Area>` runs only yours.
- Assertions: `Assert.Equal`, `NotEqual`, `SequenceEqual`, `True`, `False`, `Null`, `NotNull`,
  `Throws<TException>`, `Fail`.
- A test of a value ported from the decompile cites its source in the spec, not in the test.
- Tests must be deterministic (fixed seeds) and fast (whole suite in seconds).

## 9. Known gaps and decisions to revisit

- Combat outcome has no game enum; `CombatOutcome` is sim-only.
- Cards, monsters and relics are hook listeners in the game too; P0 only makes powers
  listeners. Card/monster hooks get added when a ported model needs one.
- `PlayerChoiceContext` (card selection prompts) is dropped in P0 — Part B §B1 replaces it with
  `ChoiceContextKind` and the pause emulation table.
- Multiplayer scaling (`ShouldScaleInMultiplayer`, `GetScaledAmountForMultiplayer`) is out of
  scope: single player only.
- (P0) The run layer (events, Neow, map, rewards, relics) is out of scope — Part B §B2 adds the
  `RunState` shell, shared `PlayerState`, relics and potions; W2 adds the rest. Its effect on a combat's
  starting state enters through `Create`'s inputs: the deck (`StartingDecks.IroncladAtRunStart`,
  or the starter-deck rule of §4.2 for Ascender's Bane), HP (`currentHp`/`maxHp`, e.g.
  `StartingDecks.IroncladHpAfterNeowArrival`), and the RNG streams (`Create(RunRngSet, ...)`).
- Sim-only data, kept on purpose: `RunRngSetSnapshot.HashedSeed` (§4.3), `CombatOutcome`,
  `AttackIntent.BaseDamage`, `AttackCommand.HitsCompleted`, `CardInstance.InstanceId`.
- `CombatState.Rng` may be `null` in hand-built test states (§4.4); every combat built by
  `CombatLoop`/`CombatSim` has a `RunRngSet`.


---

# Part B — P1 kernel expansion (W1) contract · 2026-11-01

Part A above is the P0 baseline and stays binding; where Part B differs, Part B wins. Reading
order for W1 agents: Part A §1 and §5–§8 → this part → your area's rows in
`docs/sim-port/design-combat-kernel.md` (every table carries decompile citations) → the
machine-readable companions `sim/specs/inventory/kernel-gaps.json` (per-hook, per-Cmd,
per-file data) and `sim/specs/inventory/hooks.json` (182 hooks with scopes and overriders).
`docs/sim-port/w0-critique.md` lists the conflicts this part settles (its B1–B6, C1–C21, G1–G20).

## B0. Decisions that settle the W0 conflicts

| # | Decision | Settles |
| --- | --- | --- |
| D1 | One `ChoiceRuntime` for combat (W1) and run (W2): answer-tape re-execution over an `IChoiceAnswerer` seam; one action vocabulary `ChoosePick` / `ChooseConfirm` / `ChooseCancel`; one `ChoicePromptKind` enum covering combat and run prompts; `Ordered = true` at every site unless a spec note proves the result order cannot matter | B1, B3, C1, C4, G19 |
| D2 | A prompt that pauses inside a per-listener hook context runs **inline** and records `FidelityFlag.DeferredListenerPromptInline(site)`; never an exception. Exact emulation of the game's deferral is a backlog item gated on a recording of such a chain | B2, C2, G3 |
| D3 | One `PlayerState` shared by run and combat (the game's `Player`): relics, potion slots, gold, HP, deck, max energy and base orb slots live on it; combat piles hold copies linked by `DeckVersionId` (`Entities.Players/Player.cs:1214-1233`). No `RunEffects` log | B4 rows 1–2, C5 |
| D4 | Every `CombatState` has a `Run` (a `RunState`; standalone combats own a minimal one). `CombatState.Rng` is `Run.Rng`; the loop never clones it; the `CombatSim.Create(RunRngSet)` facade clones its argument once, so Part A §4.2 still holds for callers | B4 row 3, K6 |
| D5 | The unlock input has one name, `UnlockProfile` (port of `Unlocks/UnlockState.cs`), on `RunState.Unlocks`, default `UnlockProfile.All` (veteran profile) | B4 row 4 |
| D6 | Factories follow the game: one `CardFactory`, one `PotionFactory`, each a `partial class` split by file — combat entry points in W1 (`Cards/CardFactory.cs`, `Potions/PotionFactory.cs`), run entry points in W2 (`*.Run.cs`). `RelicFactory` is W2 | B4 row 5, C7 |
| D7 | Run-scope dispatchers land in W1 against **shell** run types (`Run/*`: partial classes that hold only the members W1 signatures need); W2 fills them in place | B5, G2 |
| D8 | Rules are v0.111.0 only. `GameVersion` reaches the sim through two seams: `RngSeedingScheme` (exists) and a registry filter for version-exclusive classes. No per-version rule branch (hard cap ≤30, each shipped with a legacy bundle that proves it) | C9 |
| D9 | `output/server-bootstrap/linux-assembly/sts2.dll` is the IL truth for decompile losses; the nine arrays recovered in `docs/sim-port/w0-critique.md` §7 are accepted and cited as `IL:<Type>.<member>` | critique §7 |
| D10 | Counts: 182 hook virtuals, 148 public dispatchers, 199 Cmd overloads (45 strip), ascension A0–A10, no orb / scry / retain / stance prompt exists in v0.111.0 | C10–C12 |
| D11 | `CombatHistory` entries mirror the game's 17 entry types field-for-field (ids instead of references) so the recorder's `events.jsonl` can be compared per entry | G14 |
| D12 | Side-turn hooks regain `participants`; `ShouldPlay` regains `AutoPlayType`; `deathAnimLength` is dropped; the automatically resolved result of a pile prompt is in **pile order**, not sorted option order (`Commands/CardSelectCmd.cs:3397-3399` vs `:3403-3405`) | G4, kernel §7 |

## B1. Decision API v2 (`Api/GameAction.cs` scaffold-frozen; `Combat/Loop/CombatSim.cs` choice agent)

```csharp
enum ActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, ChoosePick, ChooseConfirm, ChooseCancel }  // append-only
readonly record struct GameAction(ActionKind Kind, int CardIndex, int TargetIndex)
//  PlayCard:      CardIndex = hand index; TargetIndex = enemy index (State.Monsters) or NoIndex
//  UsePotion:     CardIndex = potion slot; TargetIndex >= 0 enemy index, NoIndex none/self, <= -2 ally Allies[-2 - TargetIndex]
//  DiscardPotion: CardIndex = slot
//  ChoosePick:    CardIndex = option index           ChooseConfirm, ChooseCancel: no indices
//  static UsePotion(slot, target = NoIndex), DiscardPotion(slot), Pick(option), Confirm, Cancel
```

Prompt types (`Choice/*`, namespace `Sts2Sim.Core.Choice`; types frozen in phase 0, bodies by the choice agent):

```csharp
enum ChoicePromptKind : byte {
    HandSelect, HandUpgrade, PileSelect, ChooseCard, SimpleGrid,        // combat (W1): FromHand/FromHandForDiscard, FromHandForUpgrade, FromCombatPile, FromChooseACardScreen, FromSimpleGrid
    DeckUpgrade, DeckTransform, DeckEnchant, DeckRemove, DeckGeneric,   // run (W2): FromDeckForUpgrade/Transformation/Enchantment/Removal, FromDeckGeneric
    RewardsGrid, Bundle, CardReward }                                   // run (W2): FromSimpleGridForRewards, FromChooseABundleScreen, CardReward.Select (offered cards, then alternatives)
enum ChoiceResultType : byte { CombatCard, Index }                      // recorder PlayerChoiceType
enum ChoiceContextKind : byte { Inline, TurnSetup, PerListener, Throwing }
readonly record struct ChoiceOption(int InstanceId, string ModelId, int UpgradeLevel)
sealed record ChoicePrompt(ChoicePromptKind Kind, string SiteId, string SourceId, int SourceInstanceId, PileType? Pile,
    IReadOnlyList<ChoiceOption> Options, int MinSelect, int MaxSelect, bool RequireConfirm, bool CanSkip, bool Cancelable,
    bool Ordered, ChoiceResultType ResultType)
readonly record struct ChoiceAnswer(IReadOnlyList<int> Picks, bool Cancelled)   // option indices in pick order
interface IChoiceAnswerer { ChoiceAnswer Answer(ChoicePrompt prompt); }
sealed class ChoicePendingException : Exception { ChoicePrompt Prompt { get; } }
sealed class CardSelectorPrefs      // CardSelection/CardSelectorPrefs.cs: MinSelect, MaxSelect, RequireManualConfirmation
                                    //   (default: MinSelect >= 0 && MinSelect != MaxSelect, :114), Cancelable, prompt key
sealed class ChoiceRuntime          // one per RunState; CombatState.Choice == Run.Choice
    ContextScope PushContext(ChoiceContextKind kind, Action? onFirstPause = null)   // disposable struct; the context stack is transient (not cloned,
                                                      //   not hashed): re-execution rebuilds it. onFirstPause = the TurnSetup steps the loop supplies (B1 table)
    void PushSelector(IChoiceAnswerer s); void PopSelector()   // content selectors (VakuuCardSelector); part of the state: cloned and hashed
    ChoiceAnswer Ask(ChoicePrompt prompt)             // selector top answers first, else Answerer; the tape answerer throws ChoicePendingException when its tape is exhausted
    IChoiceAnswerer? Answerer { get; set; }           // installed by CombatSim for the duration of one Step
static class CardSelectCmd          // Choice/CardSelectCmd.cs; game names and parameter order, cs first, choiceContext dropped
    FromHand, FromHandForDiscard, FromHandForUpgrade, FromCombatPile, FromChooseACardScreen, FromSimpleGrid        // W1
```

`SiteId` = `<model id>:<method>` (`ACROBATICS:OnPlay`, `GAMBLING_CHIP:AfterPlayerTurnStart`), stable across
runs. `MinSelect`/`MaxSelect` are clipped to the option count. Option order = what agents and the harness
index: hand order (HandSelect, HandUpgrade); draw pile sorted by (rarity, `ModelId`) — the selector branch's
order, `Commands/CardSelectCmd.cs:3403-3405`, which also hides the draw order from agents; discard pile
order; generation order (ChooseCard, SimpleGrid). A prompt is **exposed** iff the game would open UI: at
least one option and no automatic rule (`!RequireConfirm && n <= MinSelect` → all options;
`FromHandForUpgrade` with `n <= 1` → that card or null; 0 options → empty). A `ChooseCard` with one card
and no skip is still exposed (a forced move keeps the decision stream aligned with recordings). Automatic
selections return their options in pile order (D12).

Prompt vs phase (binding for W2 as well): a `ChoicePrompt` is any point where the game awaits a selection
screen **inside an effect** (`CardSelectCmd.*`, the card-reward pick inside `CardReward.Select`, the
bundle screen). A **phase** is a top-level screen shown between effects (the combat play phase; in W2 map,
rewards list, shop, rest site, treasure, event page) whose actions are top-level action kinds.

`CombatSim` additions (choice agent):

```csharp
ChoicePrompt? PendingChoice { get; }     IReadOnlyList<int> PendingPicks { get; }
void Step(GameAction action)                             // decision API: tape engine, one DeepClone per Step
void Step(GameAction action, IChoiceAnswerer answerer)   // rollout fast path: no clone, no exception, prompts answered inline
```

Engine: at a decision point `Step(a)` keeps the current state `S0` untouched, runs `a` on
`work = S0.DeepClone()` with a tape answerer over the stored tape, and either completes (`State = work`,
tape cleared) or meets a prompt without a taped answer: the runtime first performs the pause semantics
below, then throws `ChoicePendingException(prompt)`; `Step` catches it and stores
`(S0, a, tape, prompt, picks = [], observation = work)`. Each `Pick`/`Confirm`/`Cancel` that completes the
prompt appends the answer to the tape and re-runs `a` from a fresh `S0.DeepClone()`; the result is a
completed state or the next prompt. `Create` may itself stop at a turn-1 prompt (Toolbox in
`BeforeHandDraw`, GamblingChip in `AfterPlayerTurnStart`): there `S0` is the post-`SetUpCombat` state and
the action is an internal `Begin`. A debug invariant checks on every re-run that prompt k equals the
prompt recorded with tape entry k (kind, `SiteId`, option ids).

- `State` at a prompt is the observation (the partial state up to the prompt, including deferred
  siblings), read-only by contract; `IsTerminal` is false; `State.IsInProgress` is true.
- `StateHash` at a prompt = `H(S0 ‖ action ‖ tape ‖ picks)`; `Clone` copies `S0`, action, tape, picks and
  prompt; the observation is cloned or regenerated.
- Legal actions at a prompt, in order: `Pick(i)` for every unpicked `i` ascending while
  `picks < MaxSelect` (when `Ordered` is false only `i` greater than the last pick is listed, and `Step`
  sorts the answer before it enters the tape so transpositions hash equal); then `Confirm` iff
  `(RequireConfirm || CanSkip) && MinSelect <= picks <= MaxSelect`; then `Cancel` iff `Cancelable`
  (the answer is empty with `Cancelled = true`; the ported caller treats it as the game treats a cancelled
  screen). The prompt completes automatically when `!RequireConfirm && picks == MaxSelect`. Plays, potions
  and `EndTurn` are illegal at a prompt (the game's selection UI is modal; hand selections also cancel
  queued play-card actions, `Commands/CardSelectCmd.cs:3685`).
- Legal actions outside a prompt, in order: plays (hand index ascending, target ascending), then
  `UsePotion` (slot ascending, target ascending), then `DiscardPotion` (slot ascending), then `EndTurn`
  last. `UsePotion(slot, t)` is legal iff `IsInProgress`, the player is in the play phase
  (`UsePotionAction` is `CombatPlayPhaseOnly`), the slot holds a potion whose `Usage` is `CombatOnly` or
  `AnyTime`, `PassesCustomUsabilityCheck` holds and `t` is valid for its `TargetType`;
  `DiscardPotion(slot)` iff the slot holds a potion and `CanUseOrRemovePotions` (the node-layer rule,
  `Nodes.Potions/NPotionPopup.cs:285-345`, ported as a rule).
- A prompt inside the enemy turn (KnowledgeDemon `ChooseCurse`; a player listener that prompts in
  reaction to an enemy attack, `Commands.Builders/AttackCommand.cs:1132`, `:1146`) is exposed while
  `CurrentSide == Enemy`; the answer re-runs `EndTurn` from `S0` through the rest of the enemy turn and
  the next player turn start, which may expose further prompts.

Pause emulation (kernel design §4.6). The game signals `SignalPlayerChoiceBegun` **before** counting
options for `FromHand`, `FromSimpleGrid`, `FromCombatPile` and `FromHandForUpgrade`
(`Commands/CardSelectCmd.cs:3681-3686`, `:3306-3310`, `:3375-3379`), so the reordering below happens even
for empty or automatic selections; `FromChooseACardScreen` with no card returns before signalling.

| `ChoiceContextKind` | Where the game builds the context | Sim effect at the first pause |
| --- | --- | --- |
| `Inline` | `PlayCardAction`, `UsePotionAction` (`GameActionPlayerChoiceContext`), every `BlockingPlayerChoiceContext` site, the end-of-turn hook context | none |
| `TurnSetup` | `SetupPlayerTurn` and `RunAutoPrePlayPhase` (`Combat/CombatManager.cs:5059-5061`, `:5204-5205`) | run `Hook.AfterSideTurnStart(Player, participants)` (`:5080`), every player's `OrbQueue.AfterTurnStart` (`:5118-5120`), the dead-player auto-ready check (`:5143-5160`) and `CheckWinCondition` (`:5226`) with the context stack reset to the turn-loop root; then answer and continue `SetupPlayerTurn`; `StartTurn` later skips the steps already done, runs the body of `RunAutoPrePlayPhase` (`:3902-3914`) and does not repeat the win check. A first pause inside `RunAutoPrePlayPhase` runs only `CheckWinCondition` first. If that deferred win check ends the combat, unwind to the terminal state |
| `PerListener` | the six dispatchers that build a `HookPlayerChoiceContext` per listener (`Hooks/Hook.cs:2364`, `:2467`, `:6170`, `:7776`, `:8398`, `:8607`) | **inline**, plus `FidelityFlag.DeferredListenerPromptInline(SiteId)` on `cs.Run.Fidelity` (D2) |
| `Throwing` | commands issued by monster code with `ThrowingPlayerChoiceContext` | `InvalidOperationException` (a prompt there is impossible in the game as well) |

An installed selector suppresses the pause, as in the game. Turn-setup prompt sites in v0.111.0:
`BeforeHandDraw` (Toolbox, ForegoneConclusionPower), the turn-start draw's `AfterShuffle`
(StratagemPower), `AfterPlayerTurnStart` (EntropyPower, ToolsOfTheTradePower, TyrannyPower,
ChoicesParadox, GamblingChip, ToastyMittens); auto-play chains (Mayhem, HistoryCourse, Imbued,
Hellraiser in the opening draw) reach prompts through the caller's context.

## B2. State kernel v2 (`State/*`, state agent; `Run/RunState.cs` shell body: state agent in W1)

```csharp
sealed partial class RunState                   // Run/RunState.cs — W1 shell of Runs/RunState.cs; W2 (R1) takes the file over in place
    List<PlayerState> Players; CombatState? Combat; RunRngSet Rng; UnlockProfile Unlocks; int Ascension
    List<ModifierModel> Modifiers; ChoiceRuntime Choice; FidelityLog Fidelity
    static RunState CreateStandalone(PlayerState player, RunRngSet rng, int ascension, UnlockProfile? unlocks = null)
    RunState DeepClone(); void WriteCanonical(CanonicalWriter w)      // clones players, rng, choice and the combat once each and re-links

sealed class CombatState                        // additions
    RunState Run (never null); RunRngSet Rng => Run.Rng; ChoiceRuntime Choice => Run.Choice
    IReadOnlyList<CreatureState> Allies         // [0] = Player, then pets in insertion order (the game's _allies)
    void AddAlly(MonsterState pet); void RemoveAlly(MonsterState pet)
    CombatHistory History; int CardOrPotionEffectDepth; List<int> PlayersTakingExtraTurn
    CardInstance CreateCard(CardDef def, PlayerState owner)   // ToMutable + owner + AfterCreated (Combat/CombatState.cs:671-677)
    CombatState DeepClone()                     // standalone: clones Run (which clones Player and Rng) and re-links; embedded: Run.DeepClone() is the entry

sealed class PlayerState                        // persistent members added (Entities.Players/Player.cs)
    int Gold; List<RelicModel> Relics; PotionModel?[] PotionSlots (Length = MaxPotionCount, :142); int BaseOrbSlotCount (:373)
    List<CardInstance> Deck                     // deck order (Deck.Cards, :312)
    bool HasRelic(string id); RelicModel? GetRelic(string id); bool HasOpenPotionSlot (:280)
    OrbQueue OrbQueue; List<int> PetIds         // combat members added
sealed class MonsterState                       // additions
    int PetOwnerId (= PowerModel.NoCreature when none); AbstractModel? Listener   // MonsterDef hook adapter (monsters-kernel)
sealed class CardInstance                       // additions (cards-kernel owns the file; these members are frozen here)
    PlayerState Owner; EnchantmentModel? Enchantment; AfflictionModel? Affliction; AbstractModel? Listener
    bool HasBeenRemovedFromState; int DeckVersionId (-1 when none)
    CardInstance CloneForCombat(int newInstanceId)   // Player.PopulateCombatState: state.CloneCard(deck card) + DeckVersion (Entities.Players/Player.cs:1214-1233)

sealed class CombatHistory : ICanonical         // State/CombatHistory.cs — Combat.History/CombatHistory.cs
    // 17 entry types mirrored field-for-field from Combat.History/*Entry.cs (ids instead of references),
    // stamped with round, side and player turn; Append* per type; the queries the 45 readers use
    // (by type, by actor, HappenedThisTurn, HappenedLastPlayerTurn); Clear() at combat end
sealed class FidelityLog : ICanonical           // State/Fidelity.cs — (FidelityFlag, string Site) list, hashed
enum FidelityFlag : byte { DeferredListenerPromptInline, /* owners append when a rule is knowingly approximated */ }
sealed class UnlockProfile                      // Unlocks/UnlockProfile.cs — fields of Unlocks/UnlockState.cs:16-139; UnlockProfile.All; immutable, shared by clones; canonical = digest of sorted ids
```

Clone and canonical: Part A §6 applies to every new type. Objects reachable twice (`Player` is both
`cs.Player` and `rs.Players[0]`; `Rng`; `Choice`) are cloned **once** and re-linked, and the canonical form
writes each once (through `RunState`). Defs and `UnlockProfile` are shared, not cloned. `CombatHistory`
clone must stay ≤ 50 µs on a 500-entry log (copy-on-write or array copy; the state agent measures and
records the number in `state.md`). Hand-built test states may still omit `Rng` (Part A §4.4) but always
have a `Run` (`RunState.CreateStandalone`).

Canonical tag ranges (one `*CanonicalTags.cs` per area; existing: rng 0x52xx, state 0x53xx, powers
0x54xx, monsters 0x4Dxx, cards 0xCAxx): **W1** relics / potions / enchantments / afflictions / modifiers
0x57xx, choice 0x5Axx, orbs 0x5Bxx, history 0x5Cxx, unlocks / fidelity / run shell 0x5Dxx; **reserved for
W2** run state 0x55xx, map 0x56xx, rewards / merchant / rest / treasure 0x58xx, events 0x59xx.

No approximation without a flag: whenever an owner knowingly deviates from the decompile (a VFX-driven
order, an unreadable body), the code records a `FidelityFlag` at the site and the spec explains it. The
diff harness maps flags to ineligibility reasons; a flag is never a silent pass.

## B3. Listeners and hooks (`Combat/Pipeline/{AbstractModel,Hook,ListenerSnapshot,HookMask}.cs`, hooks agent)

- `AbstractModel` declares all **182** hook virtuals with the game's names, parameters and default
  return values (`Models/AbstractModel.cs`; names, scopes and overriders in `hooks.json`). First
  parameter: `CombatState cs` for hooks dispatched only in combat; `RunState rs` for run-scope and
  both-scope hooks (the combat, when attached, is `rs.Combat`). `PlayerChoiceContext` parameters are
  dropped; `deathAnimLength` is dropped; side-turn hooks take `IReadOnlyList<CreatureState> participants`;
  `ShouldPlay` takes `AutoPlayType`. Game types map `Creature`→`CreatureState`, `Player`→`PlayerState`,
  `CardModel`→`CardInstance`, `PowerModel`/`RelicModel`/`PotionModel`/`OrbModel` → the sim bases, run
  types → `Run/*` shells (D7).
- `Hook` has all **148** public dispatchers (same names; `ModifyDamageInternal` stays private). Dispatch
  patterns per kernel design §2.2: `ref List<AbstractModel>? modifiers` on the `Modify*` dispatchers that
  return modifiers (`ModifyCardPlayResultLocation`, `ModifyOrbPassiveTriggerCount`, `ModifyGoldGained`
  join Part A's list); `out AbstractModel? preventer` on `Should*` gates; a fresh snapshot per pass of a
  multi-pass dispatcher; guarded iteration yields nothing once `IsOverOrEnding && !IsStarting`
  (`Hooks/Hook.cs:8792-8810`).
- Listener order is `kernel-gaps.json → listener_order.combat_list` (`Combat/CombatState.cs:855-956`):
  allies then enemies; per creature its powers in list order, then (monster) the monster listener; for the
  player: stop if `!IsActiveForHooks`, relics in list order skipping `IsMelted`, potion slots in order
  skipping null, orbs in queue order, then for each pile in `[Hand, Draw, Discard, Exhaust, Play]` each
  card, then its affliction, then its enchantment; then `Modifiers`; badges and the scaling model are
  inert. The list is built at the first `MoveNext`; liveness is checked per element at yield
  (`listener_order.liveness`). Run order is `design-run-layer.md` §2.7 (`Runs/RunState.cs:830-933`):
  `Hook.IterateRunHookListeners(rs, cs?)` — deck cards each followed by its enchantment, then, **only
  without an attached combat**, relics (skipping `IsMelted`), potions, modifiers, badges and the scaling
  model (`Runs/RunState.cs:866-880`); with an attached combat the combat list supplies relics, potions and
  modifiers (`Combat/CombatState.cs:916-919`), so nothing is visited twice (§B12 A8).
- Per-type override masks (`HookMask`): a model type is visited only for hooks it overrides; the ordered
  listener array may be cached only if every structural change, including direct list writes by tests,
  invalidates it (§B12 A20). Observable
  semantics must equal the full iteration (tests compare both paths on random states).
- `Run/*` shells (phase 0): `sealed partial class` or enum for every run-layer type a hook or Cmd
  signature mentions (`RunState`, `AbstractRoom`, `MapPoint`, `RewardsSet`, `Reward`, `CardReward`,
  `MerchantEntry`, `RestSiteOption`, `EventModel`, `ActModel`, `MapPointHistoryEntry`, `RoomType`,
  `MapPointType`, …), each with a `// W2 fills` header and only the members W1 code reads.

## B4. Cmd facades (the game's `Commands/*.cs`; 199 public overloads listed in `kernel-gaps.json → cmd_facades`)

| Facade | W1 owner | Notes |
| --- | --- | --- |
| `CreatureCmd`, `PowerCmd`, `DamageCmd` / `AttackCommand`, `EnergyCmd`, `CombatStatus` | pipeline | Osty block/HP redirection (`Commands/CreatureCmd.cs:3697-3725`), `CreatureCmd.Add` (summons), `Stun` (wrapper body missing in the decompile → IL, D9) |
| `CardPileCmd`, `PlayerCmd`, the play half of `CardCmd` (`Combat/Loop/CardCmd.Play.cs`: `AutoPlay`, `Discard`, `DiscardAndDraw`, `Exhaust`, …) | loop | `CardPileCmd.Add(…, PileType.Deck)` / `AddToDeck` / `RemoveFromDeck` write `Player.Deck` (`Commands/CardPileCmd.cs:3123-3151`, `:3368-3418`); `DeckVersionId` is only a back-link (§B12 A6) |
| the card half of `CardCmd` (`Combat/Pipeline/CardCmd.cs`: `Upgrade`, `Downgrade`, `Transform*`, `Enchant`, `Afflict`, keywords, costs) | cards-kernel | `partial class CardCmd` across the two owners' files |
| `CardSelectCmd` | choice | B1 |
| `RelicCmd`, `PotionCmd`, `PotionActions` | relics-potions | `PotionCmd.TryToProcure` / `Discard`; `RelicCmd.Obtain` / `Remove` / `Replace` / `Melt` on their combat paths |
| `OrbCmd`, `OstyCmd`, `ForgeCmd`, `StarsCmd` | character-mechanics | |
| `MapCmd`, `RewardsCmd`, `RelicSelectCmd` | W2 | `RelicSelectCmd.FromChooseARelicScreen` has no caller in v0.111.0 |
| `SfxCmd`, `TalkCmd`, `ThinkCmd`, `VfxCmd`, `Cmd.Wait` / `CustomScaledWait` | strip | never ported; every overload marked `strip` in `cmd_facades` is listed once in its owner's spec |

Method names, parameter order and default values follow the game; `cs` first; `choiceContext` and
`showPreview`/`skipVisuals` style parameters are dropped (the drop is noted in the spec when it reorders
side effects).

## B5. New listener kinds (bases stubbed in phase 0; bodies by their owners)

| Kind | Files | Owner | Contract |
| --- | --- | --- | --- |
| Relics | `Relics/RelicModel.cs`, `Relics/Defs/*.cs`, `Relics/RelicCanonicalTags.cs` | relics-potions | `RelicModel : AbstractModel, ICanonical` — `Id`, `Rarity`, `Pool`, the 22 virtuals of `Models/RelicModel.cs` (`AfterObtained`, `IsUsedUp`, `DisplayAmount`, `IsStackable`, …), `IsMelted` (`:267`), `Status` (`:333`), `StackCount`, `HasBeenRemovedFromState`; per-instance fields cloned by `DeepClone()` (MemberwiseClone + `CopyMutableReferenceState`) and written by `WriteCanonical` + `WriteExtraCanonical`; one class per content file, public parameterless ctor; `RelicRegistry` |
| Potions | `Potions/PotionModel.cs`, `Potions/Defs/*.cs`, `Potions/PotionFactory.cs` | relics-potions | `OnUse`, `Usage` (`PotionUsage`), `TargetType`, `PassesCustomUsabilityCheck`, `IsValidTarget` (`Models/PotionModel.cs:618`), `OnUseWrapper` in the traced order of §B12 A3; `PotionRegistry`; `PotionFactory` entry points with the game's names (§B12 A1) |
| Orbs | `Orbs/OrbModel.cs`, `Orbs/OrbQueue.cs`, `Orbs/Defs/*.cs` (5) | character-mechanics | `OrbQueue` (`Entities.Orbs/OrbQueue.cs`: values + `Capacity` ≤ 10 `:386`, `TryEnqueue` `:429`, `Insert`, `Remove`, `BeforeTurnEnd` `:463` / `AfterTurnStart` `:477` triggers); `OrbModel` `Passive`, `Evoke`, turn triggers, values through `Hook.ModifyOrbValue`; `OrbRegistry` |
| Enchantments | `Cards/Enchantments/EnchantmentModel.cs`, `Cards/Enchantments/Defs/*.cs` | cards-kernel | 19 virtuals of `Models/EnchantmentModel.cs`; the four damage/block pre-pass virtuals (the pre-pass itself lives in `Hook.ModifyBlock` / `ModifyDamage`, hooks owner, §B12 A9); `EnchantPlayCount`; `OnPlay` after the card's; `ShouldStartAtBottomOfDrawPile` (`Combat/CombatManager.cs:4248-4262`); listener right after its card |
| Afflictions | `Cards/Afflictions/AfflictionModel.cs`, `Cards/Afflictions/Defs/*.cs` | cards-kernel | `CardCmd.Afflict` (`ShouldAfflict`, `History.CardAfflicted`), `ClearAffliction`; `OnPlay` after the card's; listener between card and enchantment |
| Modifiers | `Modifiers/ModifierModel.cs` | scaffold (stub only) | `RunState.Modifiers` is empty in standard runs; no modifier is ported (excluded mode) |
| Monster listener | `Monsters/MonsterListener.cs` | monsters-kernel | adapter that routes `AbstractModel` hooks to `MonsterDef` virtuals (`MonsterState.Listener`) |

Pilot content in W1 — the minimum that exercises every new kernel path in a test (W3 ports everything
systematically; a pilot is a complete 1:1 port with citations in the owner's spec, never a sketch):
relics-potions ports ≥6 relics and ≥4 potions covering a counter relic, an `AfterCombatEnd` reset, a
`Niche` reader, the turn-setup prompt relic `GamblingChip`, `PaperPhrog` and `PaperKrane` (consulted by
powers through the multiplier interfaces, §B12 A5), and the potions `AttackPotion` (prompting), `Ashwater`
(hand prompt), `FairyInABottle` (an `Automatic` potion with `ShouldDie` / `AfterPreventingDeath`, §B12 A2)
and one simple stat potion; cards-kernel ports the prompt
cards the choice tests need (`Armaments`, `Acrobatics`, `Headbutt`, `Seance`, `Discovery`, `SeekerStrike`)
plus `SovereignBlade`, one enchantment def and one affliction def; pipeline ports `HellraiserPower`,
`ToolsOfTheTradePower`, `EntropyPower`, `DarkEmbracePower`, `FocusPower`, `DieForYouPower`, re-ports
`VulnerablePower` / `WeakPower` with the relic consults (dealer-as-player / target-as-player lookups through
the multiplier interfaces, §B12 A5) and `ShrinkPower` with real participants (critique G11); character-mechanics ports the 5 orbs and `Osty` and tests stars / forge
through Cmd calls and test doubles. Cross-kind references are **by id string**, never by type, so no owner
compiles against another owner's pilots.

## B6. Loop and turn flow (`Combat/Loop/`, loop agent)

- `CombatLoop.SetUpCombat` fills the draw pile with combat copies of `Player.Deck` (`CloneForCombat`,
  ids from `cs.AllocateCardInstanceId()`, which forwards to the run allocator; deck cards are allocated
  first, §B12 A13), then the opening shuffle and monster setup; `StartCombat` keeps
  its role. Every `CreateCombat` overload builds the `PlayerState` (deck from the specs, HP, energy) and a
  standalone `RunState` around it (`RunState.CreateStandalone`); only the `CombatSim.Create(RunRngSet)`
  facade clones the set (D4).
- Turn flow (`Combat/Loop/TurnFlow*.cs`): auto pre/post play phases (`Phase = AutoPrePlay`,
  `CheckForEmptyHand`, `AfterAutoPrePlayPhaseEntered[Early|Late]`, `AfterAutoPostPlayPhaseEntered`,
  `Combat/CombatManager.cs:3902-3914`, `:2387-2389`); orb triggers at `DoTurnEnd` and `StartTurn`
  (`:5118-5120`); side-turn `participants` snapshots (`:4873-4882`, `:2457`, `:2967`, `:2055-2088`);
  effect depth (`BeginCardOrPotionEffect` / `EndCardOrPotionEffect`, `:6517-6531`, `:1430-1509`); extra
  turns; Sly discards; auto-play (`CardCmd.AutoPlay` re-entering `OnPlayWrapper`, `Commands/CardCmd.cs:843`,
  `:917`); the `TurnSetup` pause callbacks (B1); history recording sites; `AfterCombatEnd` and
  `AfterCombatVictory[Early]` at combat end through `cs.Run` (`room` is null in standalone combats).
- `Step(UsePotion)` / `Step(DiscardPotion)` go through `PotionActions` (relics-potions);
  `UsePotionAction` in-combat target rules `GameActions/UsePotionAction.cs:80-95`, `:117-132` (§B12 A4).
- Win checks only at the game's fixed points: after each action except `EndPlayerTurn` /
  `ReadyToBeginEnemyTurn` (`GameActions/ActionExecutor.cs:130-139`), after each enemy, at phase boundaries.

## B7. Cards kernel (`Cards/*` except `CardPlay.cs`, `Combat/Pipeline/CardCmd.cs`, `Unlocks/UnlockProfile.cs` body — cards-kernel)

`Cards/DynamicVars/*` (the 26 subclasses of `Localization.DynamicVars/`; `CalculatedVar` =
`CalculationBase + CalculationExtra × multiplier(card, target)` with the multiplier as a
`static Func<CombatState, CardInstance, CreatureState?, decimal>` on the def); `CardFactory` combat
entry points (`GetDistinctForCombat` = `TakeRandom`, `GetForCombat` = `NextItem`, both over
`FilterForCombat` / `CanBeGeneratedInCombat`, `Factories/CardFactory.cs:93-121`); `Cards/Pools/*`
(ordered pool data in `GenerateAllCards` order from `sim/specs/inventory/pools.json`;
`GetUnlockedCards(UnlockProfile, multiplayer constraint)` removes the 37 `MultiplayerOnly` cards and
applies the epoch gates, `Models/CardPoolModel.cs:68-86`); keywords in combat (`Hook.ModifyKeywordsInCombat`,
`TryModifyKeywordsInCombat`); star costs and temporary costs; the card half of `CardCmd` (B4);
enchantment and affliction bases (B5); the `CardInstance` additions (B2). Content reads canonical instances
(`ModelDb.Card<T>()` options before `CreateCard`, `canonicalPower` in `TryModifyPowerAmountReceived`):
keep those reads on the defs.

## B8. Monsters kernel (`Monsters/*` except `Defs/Osty.cs` — monsters-kernel)

`MonsterListener` adapter; the `MonsterDef` virtuals content overrides (`ShouldDisappearFromDoom` and
`OnDieToDoom` are VFX-only and are **not** ported, critique C15); the mid-combat spawn path
(`CreatureCmd.Add` → `MonsterSpawner`: id, HP roll, `SetUpForCombat`, `AfterCreatureAdded`,
`AfterCreatureAddedToCombat`; `SetEnemyIndex` for slotless encounters, `Combat/CombatState.cs:966-980`);
pets as `MonsterState` on the player side with `PetOwnerId` (owner death clears the `OrbQueue` and kills
only an alive Osty, `Commands/CreatureCmd.cs:2701-2717`; pet lifecycle per §B12 A15); the run streams some monsters read (`Fabricator` MonsterAi,
`ThievingHopper` CombatCardGeneration, `ToughEgg` Niche) come from `cs.Rng`, never from a def
(`MonsterModel.Rng` is `Rng.Chaotic` for canonical models, `Models/MonsterModel.cs:395-404`).

## B9. Character mechanics (`Orbs/*`, `Combat/Pipeline/{OrbCmd,OstyCmd,ForgeCmd,StarsCmd}.cs`, `Monsters/Defs/Osty.cs` — character-mechanics)

Orbs (B5); Osty and pets (`OstyCmd.Summon`, `Commands/OstyCmd.cs:336-395`: `ModifySummonAmount`, alive →
`GainMaxHp`, else revive or create via `PlayerCmd.AddPet<Osty>` + `DieForYouPower`, `SetMaxHp`, `Heal`,
`AfterOstyRevived`, `History.Summoned`, `AfterSummon`); Stars (`PlayerCombatState.Stars` ≥ 0 with the
`StarsModified` history entry; `PlayerCmd.GainStars` / `LoseStars` / `SetStars`, the `ShouldGainStars`
gate, `AfterStarsGained` / `AfterStarsSpent`, `Hook.ModifyStarCost`, `ShouldPayExcessEnergyCostWithStars`
= 2 stars per missing energy, X star costs capture the current stars); Forge (`ForgeCmd.Forge`,
`Commands/ForgeCmd.cs:153-216`; `SovereignBlade` is a cards-kernel pilot reached by id, §B12 A16).

## B10. Version and rule truth (D8, D9)

- Registry filter: a `[VersionExclusive("<game version>")]` attribute (scaffold) marks classes that exist
  in one version only; `Registries` include a class iff its version equals the configured `GameVersion`
  (default `0.111.0`). No other version logic exists in Core; a legacy bundle whose content needs a v0.107.1
  rule is `version_tainted`, not emulated.
- Decompile losses: cite `IL:<Type>.<member>` with the value recovered in `w0-critique.md` §7; a new loss is
  an open question in the spec, never a guess.

## B11. W1 ownership, files, phase 0, gates

| Agent | Owns (create or edit; paths under `sim/Sts2Sim.Core/` unless stated) | Tests (`sim/Sts2Sim.Tests/`) | Spec (`sim/specs/`) |
| --- | --- | --- | --- |
| scaffold (phase 0) | `Api/GameAction.cs`; `Enums/*` (every enum a W1 signature needs, game ordinals); `Registry/Registries.cs` (`RelicRegistry`, `PotionRegistry`, `OrbRegistry`, `EnchantmentRegistry`, `AfflictionRegistry`, `ModifierRegistry`, `VersionExclusive`); `GlobalUsings.cs`; base stubs `Relics/RelicModel.cs`, `Potions/PotionModel.cs`, `Orbs/OrbModel.cs`, `Orbs/OrbQueue.cs`, `Cards/Enchantments/EnchantmentModel.cs`, `Cards/Afflictions/AfflictionModel.cs`, `Modifiers/ModifierModel.cs`; `Run/*` shells; `Unlocks/UnlockProfile.cs`; `Choice/*` types and `ChoiceRuntime` / `CardSelectCmd` signatures; the `State/*` additions of B2 with clone and canonical wired; `State/CombatHistory.cs` (entry types + `Append*` signatures) and `State/Fidelity.cs`; all 182 virtuals on `AbstractModel`; all 148 dispatcher stubs on `Hook`; tag ranges; `Combat/Loop/PotionActions.cs` entry signatures | the existing suite stays green | `scaffold-w1.md` (every stub with its signature's source line) |
| choice | `Choice/*`, `Combat/Loop/CombatSim.cs`, `Combat/Loop/LegalActionGenerator.cs` | `ChoiceTests.cs` | `choice.md` |
| hooks | `Combat/Pipeline/{AbstractModel,Hook,ListenerSnapshot,HookMask}.cs` | `HookTests.cs` | `hooks.md` |
| state | `State/*`, `Run/RunState.cs` (shell body) | `StateKernelTests.cs` | `state.md` (append) |
| pipeline | `Combat/Pipeline/{CreatureCmd,PowerCmd,AttackCommand,EnergyCmd,CombatStatus,CreatureInternals,DamageResult}.cs`, `Powers/*` | `PipelineTests.cs` (append) | `pipeline.md` (append) |
| cards-kernel | `Cards/*` except `CardPlay.cs`; `Combat/Pipeline/CardCmd.cs`; `Unlocks/UnlockProfile.cs` body | `CardKernelTests.cs` | `cards-kernel.md` |
| loop | `Combat/Loop/{CombatLoop,ActionExecutor,CardPileCmd,PlayerCmd}.cs`, `Combat/Loop/CardCmd.Play.cs`, `Combat/Loop/TurnFlow*.cs` | `TurnFlowTests.cs` | `loop.md` (append) |
| relics-potions | `Relics/*`, `Potions/*`, `Combat/Pipeline/{RelicCmd,PotionCmd}.cs`, `Combat/Loop/PotionActions.cs` | `RelicPotionTests.cs` | `relics-potions.md` |
| character-mechanics | `Orbs/*`, `Combat/Pipeline/{OrbCmd,OstyCmd,ForgeCmd,StarsCmd}.cs`, `Monsters/Defs/Osty.cs` | `CharacterMechanicsTests.cs` | `character-mechanics.md` |
| monsters-kernel | `Monsters/*` except `Defs/Osty.cs` | `MonsterKernelTests.cs` | `monsters.md` (append) |
| integrate | any file, with the smallest change that makes merged work compile and pass; every cross-area edit is listed in `integration.md` with its reason | all | `integration.md` (append) |

Rules: never edit a file you do not own (owners of a `partial class` edit only their own file);
cross-kind references by id string; stubs throw `NotImplementedException`; every ported rule cites
`file:line` in the spec; rules follow the `TestMode.IsOff` branch and timing follows
`NonInteractiveMode` (design-subsystems §2); `Rng.Chaotic` is never modelled; no static mutable state
(selector stacks and injector-style test seams live on the state); no narrative comments in code.

W1 gates (all must hold before the wave is committed): build 0 warnings 0 errors; every test green
(the Part A 367 plus every new suite); diff harness 4/4 bundles and 69/69 decisions on the P0 bundle set;
bench ≥300 decisions/s/core and `Clone()` ≤1 ms on `NIBBITS_WEAK`, with the regression ratio against P0
(705k decisions/s, 2.53 µs) reported and explained; F1 clone/hash equality at random points of random
playouts that include pilot relics, potions and prompt cards; F4 legal-action closure including prompts
(every listed action steps without exception; every exposed prompt has a legal answer; an illegal action
leaves the state unchanged); F5 invariants after every step (HP in `[0, max]`, block ≥ 0, energy ≥ 0,
gold ≥ 0, potions ≤ slots, card conservation across piles plus the deck, unique creature ids, consistent
power owners, non-decreasing RNG counters); the B2 chain (`GremlinHorn` or `DarkEmbracePower` +
`HellraiserPower` + `SeekerStrike`) steps without an exception and records the fidelity flag.

## B12. Round-3 amendments (2026-11-01, after the round-2 adversarial review)

W1 rounds 1–2 landed the phase-0 scaffold (`46d1918`, `94424e5`), the INV (`687077a`) and REC (`4094c1e`)
tracks, the gate suite `W1GateTests.cs` and `integration.md` §7–8 (`ef7c7b9`, `e74907d`). The nine area
tracks did not run (their isolated worktrees were cut from `origin/main`, which lacks the scaffold, and the
owners stopped at the base check as briefed). The round-2 verification lenses reviewed the scaffold, the
contract and the briefs against the decompile; the findings below are accepted and **override the earlier
Part B text where they differ**. Per-area detail is in the round-2 notes handed to each owner.

Scaffold amendment (phase 0b, in place, before the owners start; stubs and plumbing only):

| # | Change | Source | Owner afterwards |
| --- | --- | --- | --- |
| A10 | `PlayerState` gains the ported relic / potion internals: `AddRelicInternal(relic, index = -1)` (`Entities.Players/Player.cs:883-903`), `RemoveRelicInternal(relic)` (`:905-922`), `MeltRelicInternal(relic)` (`:924-947`), `GetPotionSlotIndex(potion)` (`:959-962`), `AddPotionInternal(potion, slotIndex = -1) → PotionProcureResult` (`:1020-1070`, game enum ordinals), `DiscardPotionInternal(potion)` (`:1072`), `RemoveUsedPotionInternal(potion)` (`:1081`), `RemovePotionInternal(potion)` (`:1087-1096`); `silent` parameters dropped. `GetRelic` does not skip melted relics (`:949-952`) | `Player.cs` | state |
| A11 | `CardPlay` (Part A frozen, amended here) gains `int PlayId` (per-combat counter `cs.AllocateCardPlayId()`, cloned and hashed) and `ResourceInfo Resources` (replaces `EnergySpent` / `StarsSpent`; manual plays set value = spent, `GameActions/PlayCardAction.cs:158-165`; auto-play sets `EnergySpent = 0`, `EnergyValue = EnergyCost.GetAmountToSpend()`, `StarsSpent = 0`, `StarValue = max(0, GetStarCostWithModifiers())`, `Commands/CardCmd.cs:515-522`). `HistoryCardPlay` carries `PlayId`; plays are compared by it (the game compares `CardPlay` by reference, `Models.Powers/UnmovablePower.cs:37`) | `Entities.Cards/CardPlay.cs` | frozen (scaffold) |
| A12 | `PlayerState.AscensionEffectsApplied`; `RunState.CreateStandalone` applies the ascension effects (TightBelt) at most once per player (`Entities.Ascension/AscensionManager.cs:27-39`; applied once at `Runs/RunManager.cs:3856-3866` / `Runs/RunState.cs:977-981`) | | state |
| A13 | The card-instance id allocator lives on `RunState` (`NextCardInstanceId`, `AllocateCardInstanceId()`), written once in the run's canonical form; `CombatState.AllocateCardInstanceId()` / `NextCardInstanceId` forward to it. Deck cards are allocated when the deck is built (1..n), combat copies after them, generated cards after those. P0 pins that encode instance ids or hashes (`LoopTests.cs` `NextCardInstanceId == 12`, `EndToEndTests` hashes) are refreshed by the loop owner with the reason in `loop.md`; the diff gate maps cards by hand index (`Sts2Sim.Diff/BundleRunner.cs:357`) and is unaffected | Part A §5 | state (allocator), loop (pins) |
| A15s | Stubs so owners compile against each other: `CreatureCmd.Add(cs, MonsterDef monster, CombatSide side = Enemy, string? slotName = null) → MonsterState` and `CreatureCmd.Add(cs, CreatureState creature)` (`Commands/CreatureCmd.cs:3564`, `:3575`; pipeline); `PlayerCmd.AddPet(cs, PlayerState owner, MonsterDef def) → MonsterState` (`Commands/PlayerCmd.cs:946-967`; loop); `MonsterSpawner.SpawnPet(cs, def, owner)` and `MonsterSpawner.SpawnForAdd(cs, def, side, slotName)` (monsters-kernel); `MonsterLifecycle.PlayerSideParticipants(cs) / EnemySideParticipants(cs)` snapshot helpers (monsters-kernel, A17); `Combat/Pipeline/StarsCmd.cs` with `GainStars / LoseStars / SetStars / SpendStars(cs, player, amount)` (character-mechanics, A16) | | as listed |
| A5s | `FidelityFlag.ApproximatedRule` (generic; the site string carries the detail) so any owner can flag a knowing deviation without editing `State/Fidelity.cs`; named values are added at integrate | | state |

Rule amendments (binding for the owners):

- **A1 (relics-potions, B5).** `PotionFactory` has no `GetDistinctForCombat`. Port the v0.111.0 members with the game's names and the caller-supplied `Rng`: `CreateRandomPotionOutOfCombat(player, rng, blacklist)` (`Factories/PotionFactory.cs:22`), `CreateRandomPotionsOutOfCombat(player, count, rng, blacklist)` (`:27`), `CreateRandomPotionInCombat(player, rng, blacklist)` (`:37-45`, filters `CanBeGeneratedInCombat`), private `CreateRandomPotions` (`:47-61`: per pick one `NextFloat` → Rare ≤ 0.1, Uncommon ≤ 0.35, else Common; then `NextItem` over that rarity's options; the pick is removed from the options), `GetPotionOptions(player)` (`:63-66`: the character pool's unlocked potions, then `SharedPotionPool`). In-combat callers pass `Rng.CombatPotionGeneration` (`Models.Cards/Alchemize.cs:78`, `Models.Potions/EntropicBrew.cs:65`). `GetDistinctForCombat` is `CardFactory`'s (`Factories/CardFactory.cs:93`).
- **A2 (relics-potions, B5).** `FairyInABottle` is a potion (`Models.Potions/FairyInABottle.cs:14`): Rare, `Usage = Automatic`, `TargetType = Self`, `CanBeGeneratedInCombat = false`; `ShouldDie` false only for its owner's creature (`:163-170`); `AfterPreventingDeath` runs `OnUseWrapper` (`:46`) and `OnUse` heals `max(MaxHp × 0.3m, 1m)` (`:108`); `CanUse` is false for `Automatic` potions (`Nodes.Potions/NPotionPopup.cs:322-324`).
- **A3 (relics-potions, B5).** `OnUseWrapper` traced order (`Models/PotionModel.cs:147-338`): `RemoveBeforeUse` (slot nulled, `HasBeenRemovedFromState = true`, `:147`, `:595-599`) → `Hook.BeforePotionUsed(rs, cs, potion, target)` (`:262`) → [VFX / wait stripped] → `effectId = BeginCardOrPotionEffect(owner)` (`:332`) → `OnUse` inline, exception captured (`:180-203`) → `EndCardOrPotionEffect(effectId)` (`:204`) → rethrow (`:232-240`) → only if `!Owner.Creature.IsDead` (`:242`): `History.PotionUsed` iff `cs != null && IsCurrentLiveCombat(effectId)` (`:245-248`), `Hook.AfterPotionUsed` (`:249`), map-point history (W2, `:321`), `CheckForEmptyHand(effectId)` (`:322`); `PopModel` (`:338`). Record the stripped steps in the spec.
- **A4 (choice + relics-potions, B1/B6).** In-combat target resolution (`GameActions/UsePotionAction.cs:80-95`): with a target id, or for a non-single-target `TargetType`, the target is `GetCreature(id)` (null id → null); otherwise the owner's creature (`:94`); then the `IsValidTarget` gate (`:117`), then `OnUseWrapper` (`:121-122`), else cancel (`:132`). `LegalActionGenerator` resolves `NoIndex` to the owner for single-target types and to null for multi-target types before `IsValidTarget`, lists one action per `(slot, resolved target)`, and gates `UsePotion` as well as `DiscardPotion` on `CanUseOrRemovePotions` (`Nodes.Potions/NPotionPopup.cs:330-334`, `:560-564`). `PotionActions.CanUse` is the node-layer rule `:285-345` (potion present, owner alive, `Usage` `CombatOnly` or `AnyTime`, not `Automatic`, `CanUseOrRemovePotions`, `PassesCustomUsabilityCheck`, valid target).
- **A5 (pipeline + relics-potions, B5).** `VulnerablePower` consults the **dealer's** relic (`dealer.Player?.GetRelic<PaperPhrog>()`, `Models.Powers/VulnerablePower.cs:111`, `:114`) and `WeakPower` the **target's** (`target?.Player?.GetRelic<PaperKrane>()`, `Models.Powers/WeakPower.cs:109`, `:112`); `Creature.Player` is null for pets and monsters, and only Cruelty has a pet-owner fallback (`VulnerablePower.cs:116`). Mechanism: `PaperPhrog` / `PaperKrane` implement `IVulnerableMultiplierModifier` / `IWeakMultiplierModifier` (`Powers/MultiplierModifiers.cs`) with their own guards (`Models.Relics/PaperPhrog.cs:18-26`: unchanged when `target == owner`, else `+0.25m`; `PaperKrane.cs:18-26`); the powers scan `(dealer as PlayerState)?.Relics` / `(target as PlayerState)?.Relics` for the interface. Never `cs.Player.HasRelic(...)`.
- **A6 (cards-kernel, B4/B7).** `CardCmd.Upgrade` (`Commands/CardCmd.cs:1882-1947`) returns when `IsEnding`, skips cards that cannot be upgraded, adds the map history entry only when the card's pile is `Deck`, then runs `UpgradeInternal` + `FinalizeUpgradeInternal` on the passed card only. An in-combat upgrade **leaves the deck card unchanged**; `DeckVersion` is read only by `TheScythe.cs:73`, `GeneticAlgorithm.cs:66`, `Goopy.cs:37-39`, `SwipePower.cs:57-59`/`:131-137`, `DeprecatedCard.cs:78-93`, `ThievingHopper.cs:636`. Test that Armaments upgrades the combat copy and not the deck card.
- **A7 (cards-kernel, B7).** `pools.json` card pools have `unlock_gates = []`; the gates are the `FilterThroughEpochs` overrides (`Models.CardPools/IroncladCardPool.cs:122-138`, the other four character pools `:123-138`, `ColorlessCardPool.cs:97-120`). Port them from the decompile with citations. `GetUnlockedCards(All, SingleplayerOnly)` yields 85 Ironclad / 86 per other character / 53 colorless (the 37 `MultiplayerOnly` cards are 5 per character pool plus 12 colorless); 90 / 91 / 65 are `AllCards` sizes. Tests say which count they assert.
- **A8 (hooks, B3).** Run listener order corrected above: modifiers, badges and the scaling model only without an attached combat (`Runs/RunState.cs:866-880`).
- **A9 (hooks, B3/B5).** The enchantment pre-pass is ported verbatim in `Hook.ModifyBlock` (`Hooks/Hook.cs:10075-10080`: additive then multiplicative on the running value, whenever `cardSource?.Enchantment != null`) and `Hook.ModifyDamage` (`:10332-10342`: each term gated by its `ModifyDamageHookType` flag), before any listener pass; the terms never enter the modifier lists.
- **A14 (choice, B1).** The pending tuple's canonical form writes a presence byte before the action; `Begin` is the absent action, so a `Begin` tuple and any `GameAction` tuple hash differently (`ActionKind` stays frozen). The debug invariant and all tests compare prompts element-wise (`Kind`, `SiteId`, each option's `InstanceId` and `ModelId`): record equality on `IReadOnlyList` fields is reference equality. Selectors are written to the canonical form by a stable string id (never `Type.FullName`, which carries assembly versions for generics); a stateful selector is `ICanonical` and cloned by `ChoiceRuntime.DeepClone` (never shared). `default(ChoiceSource)` is rejected. `CardSelectCmd` details: `UndoEndTurnIfNecessary` runs **before** the selector check in all five methods (`Commands/CardSelectCmd.cs:3182`, `:3303`, `:3373`, `:3680`, `:3756`) and mutates the end-turn readiness (`Combat/CombatManager.cs:6859-6881`) — port it or justify its omission in the spec; `FromCombatPile` throws `InvalidOperationException` for a non-combat pile after the `IsEnding` check (`:3369-3372`); `FromChooseACardScreen` has no early-out (`:3169-3182`); `FromHand` / `FromHandForUpgrade` gate on `IsOverOrEnding` (`:3672`, `:3748`), `FromSimpleGrid` / `FromCombatPile` on `IsEnding` (`:3299`, `:3365`); the draw-pile order is a **stable** `OrderBy(Rarity).ThenBy(ModelId)` with ordinal `ModelId` comparison (`Models/ModelId.cs:66-73`).
- **A15 (monsters-kernel + pipeline + loop, B8).** Owner death: `OrbQueue.Clear()`, then `Kill(Osty)` only if `IsOstyAlive` (`Commands/CreatureCmd.cs:2701-2717`), before `DeactivateHooks` / `HandlePlayerDeath`; no other pet dies. `PlayerCmd.AddPet` (`Commands/PlayerCmd.cs:946-967`): `CreateCreature` on the owner's side with slot null — no HP roll, no `Niche` draw, HP = `MaxInitialHp`, `MonsterMaxHpBeforeModification` null; `AddPetInternal` sets `PetOwner` (a different existing owner throws) and subscribes `Died → OnPetDied`, which drops the pet from `Pets` only if `ShouldCreatureBeRemovedFromCombatAfterDeath`; then `CreatureCmd.Add(pet)`. Pets never receive `AfterAddedToRoom`, never `RollMove` (`IsEnemy` gate), `TakeTurn` throws for them, they are player-side turn participants (`CreaturesOnCurrentSide = _allies`) and stay in `_allies` after death (`Kill` removes only `Side == Enemy`).
- **A16 (character-mechanics, B9).** `ForgeCmd` spans `Commands/ForgeCmd.cs:153-216`: `GetSovereignBlades` (`:197-216`) counts only non-dupe blades (`!IsDupe`) for the "none owned" check and for `AddDamage`; the "none owned" check ignores the exhaust pile (`:161-168`, `:178-186`). `OrbQueue.AddCapacity` has no cap (`Entities.Orbs/OrbQueue.cs:414-417`); `OrbCmd.AddSlots` applies `Math.Min(10 - Capacity, amount)` (`Commands/OrbCmd.cs:653`) and `RemoveSlots` clamps to `Capacity` (`:663`). `GainStars` / `SetStars` are gated by `!IsEnding` (`Commands/PlayerCmd.cs:390`, `:742`); `AfterStarsGained` receives `(int)amount` (`:393`); the history delta is the clamped integer change (`Entities.Players/PlayerCombatState.cs:150`). `OrbCmd.Passive` consults `ModifyOrbPassiveTriggerCount` only when `countAffectedByHooks` (default false, `:596-608`); the orbs' turn triggers use `TriggerPassive`. `OrbCmd.Evoke` removes the orb before its `Evoke` and calls `RemoveInternal` after `AfterOrbEvoked`, skipping both when the owner's creature has no combat (`:318-364`). `OstyCmd.Summon` has no `IsOverOrEnding` gate. `GetRandomOrb` draws over the game's order Lightning, Frost, Dark, Plasma, Glass (`Models/OrbModel.cs:197-204`, `:402-405`) on `Rng.CombatOrbGeneration` from an immutable list. **Every star write goes through `StarsCmd`** (the only writer; it records `StarsModified`), including the loop's `SpendResources` → `StarsCmd.SpendStars` with `AfterStarsSpent` (`Models/CardModel.cs:1425-1428`). Osty creation applies `DieForYouPower` by id (`PowerRegistry.Create("DIE_FOR_YOU_POWER")` + `PowerCmd.Apply`); Forge creates `SOVEREIGN_BLADE` through `CardRegistry` + `cs.CreateCard`. The creation branches are exercised by the integrator's gate with registered content; the owner's unit tests cover the other branches with local doubles that carry those ids.
- **A17 (monsters-kernel + loop + hooks, B6/B8).** Side-turn participants are snapshots from `MonsterLifecycle` helpers: player side = the extra-turn players' creatures, else a copy of `cs.Allies` (player, then pets in insertion order); enemy side = a copy of `cs.Monsters` taken at the game's points (`Combat/CombatManager.cs:4873-4882`, `:2055-2088`; `Combat/CombatState.cs:620`, `:831`). The loop passes them; hooks keeps the scaffold's live-list forwarding overloads until integrate, where the integrator deletes any overload without a caller.
- **A18 (loop, B6).** Combat end is `EndCombatInternal` in full: `ReviveBeforeCombatEnd`, `AfterCombatEnd`, `History.Clear`, `Player.AfterCombatEnd` (powers removed, combat piles cleared, block lost, `Entities.Players/Player.cs:1249-1254`), `AfterCombatVictoryEarly` / `AfterCombatVictory`; a victory never leaves block, powers or combat piles on the player.
- **A19 (pipeline + state, B2).** `CombatStatus` attachment and side queries count `cs.Allies`; `GetCreatureById` / `GetCreatures` include pets; `CombatState.AddAlly` / `RemoveAlly` / `CreateCard` (`Combat/CombatState.cs:993`, `:739`, `:671-677`) are state's.
- **A20 (hooks, B3).** `HookMask` cache rule as amended above; the six per-listener dispatchers (`Hooks/Hook.cs:2364`, `:2467`, `:6170`, `:7776`, `:8398`, `:8607`) wrap each listener call in `cs.Choice.PushContext(ChoiceContextKind.PerListener)`; `IterateRunHookListeners(rs, null, …)` is implemented (the 13 both-scope dispatchers' `cs == null` path, `Runs/RunState.cs:829-933`); the `ArrayPool<AbstractModel>.Shared` use in `ListenerSnapshot` is either replaced by per-state transient scratch (neither cloned nor hashed) or recorded as the one static exception in `hooks.md`.
- **A21 (monsters-kernel, B8).** No rule reads `MonsterModel.Rng` in v0.111.0 (only `ToughEgg`'s skin pick, `Models.Monsters/ToughEgg.cs:552`, visual); the `InitPrivateRng` seam is removed or given per-instance storage that is cloned and hashed; a test pins that spawn and move rolls draw only from `cs.Rng` streams. `MonsterListener` is immutable (def + owner id) or an `IOwnedListener` that stores only its owner, with `Hash(x) == Hash(Clone(x))` and the clone routing to the cloned monster pinned by tests.
- **A22 (state, B2).** History inner lists (`CreatureAttackedEntry.DamageResults`, `MonsterPerformedMoveEntry.TargetIds`) are never shared between clones; `MonsterState.Listener` and `CardInstance.Listener` are written to the canonical form through `ICanonical` when they carry state; history entries cite `Combat.History.Entries/<Entry>.cs:<line>`; the 500-entry clone budget (≤ 50 µs) is measured and recorded.
- **A23 (cards-kernel, B2/B7).** `CloneForCombat` = `CombatState.CloneCard` (`Combat/CombatState.cs:679-684`) + `Player.PopulateCombatState` (`Entities.Players/Player.cs:1214-1233`): owner kept, local keywords / vars / energy cost / temporary star costs copied, `Enchantment` and `Affliction` re-bound to the clone (`Models/CardModel.cs:2827-2859`), `CurrentTarget` / `CurrentPlayIndex` / `HasBeenRemovedFromState` reset (`:2861-2882`), `DeckVersionId = source.InstanceId`. Every creation path sets `Owner` (`cs.CreateCard`, `CloneForCombat`, the loop's deck build with `AttachTo`); enchantment and affliction `Card` getters return null where the game does; `GetEnchantedReplayCount` reads the card's enchantment.

Gate suite seams (`integration.md` §8.3) bind the round-3 integrator: equip through `RelicCmd.Obtain` / `PotionCmd.TryToProcure` once they exist; the B2 chain applies powers with `PowerCmd.Apply` outside a `Step`; F4 adds `Confirm` / `Cancel` / `DiscardPotion` with stray indices once B1 says they are validated; F5's "a card that left every pile never returns" holds for every v0.111.0 path read so far.

---

# Part C — W2 run-layer contract · 2026-11-01

Process (user, 2026-11-01): speed over polish. No adversarial review stage; owners self-check against
the decompile, keep build 0 warnings and the suite green, commit. Specs are one-line indexes
(`id — decompile file:line — dropped points / gaps`).

## C0. Status of the design doc

`docs/sim-port/design-run-layer.md` stays the spec (rules, draw orders, citations). Part C fixes only the
API, the ownership and the places where the design predates Part B D3–D7:

| Design text | Binding form (Part C) |
| --- | --- |
| `RunPlayer` (§2.2, §7.3, §8) | there is no `RunPlayer`: the run fields live on `PlayerState` (D3), partial file `Run/State/PlayerState.Run.cs` |
| "`RunState` + `RunPlayer` new" (§1, §8.2 R1) | `Run/RunState.cs` already exists (W1 shell, D7); R1 fills it in place |
| `PromptSelect/PromptConfirm/PromptCancel` (§3.12) | kept as `RunActionKind` names; they map 1:1 onto `ChoosePick/ChooseConfirm/ChooseCancel` of the one `ChoiceRuntime` (D1) — combat prompts and run prompts use the same runtime |
| `ActInstance { ActDef, RoomSet, SharedAncientSubset }` (§2.1) | `ActModel` = immutable def (registry `ActRegistry`), `ActInstance` = mutable act (rooms, subset, pulls); the game's `ActModel.ToMutable` state lives in `ActInstance` |
| `EventDef` / `EventInstance` (§8.2 R5) | one `EventModel : AbstractModel` per event (registry `EventRegistry`, a `ModelTable`: `Create(id)` = fresh mutable instance, the game's `ToMutable`); `RoomSet.events` holds ids |
| relic / potion / card factories (§5) | D6: `CardFactory` partial (`Run/Factories/CardFactory.Run.cs`), `PotionFactory` run entry points already exist (W1, `Potions/PotionFactory.cs`), `RelicFactory` is R3 |
| odds hold an `Rng` (game `AbstractOdds(initialValue, rng)`) | kept: odds bind the owner's stream; clones re-bind with `CloneFor(clonedRng)` |
| `RunSim` phases A/B/C entry (§7.1) | `SavePoint` enum in `Run/Save/SavedRun.cs` |

Namespace: every type under `Run/**` is in `Sts2Sim.Core.Run` (folders are ownership, not namespaces);
partial classes of other namespaces keep theirs (`PlayerState.Run.cs` → `Sts2Sim.Core.State`,
`CardFactory.Run.cs` → `Sts2Sim.Core.Cards`). Context rule: run code passes `RunState rs` first (the run
analogue of Part A `CombatState cs` first); combat-reaching code still passes `cs` first.

## C1. Frozen API (`Api/RunAction.cs`, scaffold; `Run/Loop/RunSim.cs`, R6)

```csharp
enum RunPhase : byte { Map, Combat, Rewards, CardReward, Shop, RestSite, Treasure, Event, Prompt, Victory, Defeat }
enum RunActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, Travel, ClaimReward, ProceedRewards,
    PickRewardCard, RewardAlternative, BuyEntry, LeaveShop, ChooseRestOption, LeaveRestSite, OpenChest,
    TakeTreasureRelic, SkipTreasureRelic, ChooseEventOption, LeaveEvent, PromptSelect, PromptConfirm, PromptCancel }
readonly record struct RunAction(RunActionKind Kind, int A, int B)    // NoIndex = -1; FromCombat(GameAction); TryToCombat(out GameAction)
sealed class RunSim {
    static RunSim NewRun(RunConfig config);
    static RunSim FromSave(SavedRun save, ImportOptions options);
    RunPhase Phase; bool IsTerminal; RunState State; CombatSim? Combat;
    IReadOnlyList<RunAction> LegalActions(); void LegalActions(List<RunAction> buffer);
    bool IsLegal(RunAction a); void Step(RunAction a);
    RunSim Clone(); ulong StateHash(); void WriteCanonical(CanonicalWriter w);
}
```

| Kind | A | B | Phase |
| --- | --- | --- | --- |
| `PlayCard`, `UsePotion` | hand index / potion slot | target index (combat creature index) or `NoIndex` | Combat (`UsePotion`/`DiscardPotion` also every non-combat phase, B = `NoIndex`) |
| `EndTurn` | — | — | Combat |
| `DiscardPotion` | slot | — | any |
| `Travel` | index into the legal map points (column ascending, §3.5) | — | Map |
| `ClaimReward` | index in the displayed (sorted) rewards set | — | Rewards |
| `PickRewardCard` / `RewardAlternative` | card index / alternative index | — | CardReward |
| `BuyEntry` | index in `MerchantInventory.AllEntries` | — | Shop |
| `ChooseRestOption` | option index | — | RestSite |
| `ChooseEventOption` | index in `CurrentOptions` | — | Event |
| `PromptSelect` | candidate index (strictly increasing within one prompt) | — | Prompt, or Combat with a pending combat prompt |
| `ProceedRewards`, `LeaveShop`, `LeaveRestSite`, `OpenChest`, `TakeTreasureRelic`, `SkipTreasureRelic`, `LeaveEvent`, `PromptConfirm`, `PromptCancel` | — | — | their phase |

Legal-action order (generalises P0 "`EndTurn` last"): (1) the phase's own indexed actions by ascending A
(then B); (2) potion actions — `UsePotion` by slot ascending then target, then `DiscardPotion` by slot;
(3) the leave / proceed / confirm / cancel action last (`PromptConfirm` before `PromptCancel`). Combat phase
is exactly `CombatSim.LegalActions()` mapped with `RunAction.FromCombat` (same order). `Step` runs every
automatic step to the next decision. An illegal action throws and leaves the state unchanged.
`RunSim.WriteCanonical` = phase byte + `RunState.WriteCanonical` (+ pending prompt tape, as `CombatSim`).

Additive (2026-11-02, fast path; `Run/Loop/RunSim.FastPath.cs`, `Choice/PromptSites{,.g}.cs`, `Choice/FastPathAnswerer.cs`):

```csharp
bool MayPrompt(RunAction a);          // false only when `a` provably cannot reach ChoiceRuntime.Ask (conservative)
string? FastPathBlocker(RunAction a); // null = clone-free safe, else the reason (RunSim.Block* constants)
bool StepFast(RunAction a);           // MayPrompt ? Step(a) : Step(a, FastPathAnswerer.Instance); true = clone-free
void StepFastUnchecked(RunAction a);  // clone-free without the predicate (tests: exercises the fail-closed path)
bool IsPoisoned;                      // a clone-free step met a prompt: the run is mid-action; Step/Clone throw
```

The predicate reads the generated table `PromptSites` (`sim/tools/inventory/gen_prompt_sites.py`; `--check` keeps it
current): every model whose code reaches a prompt directly, through a hazardous model it references (closure), by
auto-playing cards, or by granting Sly; grouped by what makes the hazard live (card on play / anywhere, potion on use,
relic on obtain / while owned, power, monster, enchantment, affliction, orb, modifier). Both paths give the same state
and `StateHash` when no prompt occurs. A prompt on the clone-free path throws `FastPathPromptException` (an
`InvalidOperationException`: Vec reports SIM_ERROR) naming the site; it is never answered.

## C2. Run-state fields

Part A §6 (clone and canonical) applies to every field; every field below is already present with its
default, its `DeepClone` line and its `WriteCanonical` line (scaffold). Owner types implement the
`DeepClone`/`CloneFor`/`WriteCanonical` they own; nullable fields are `null` in standalone combats.

| Field (on `RunState`) | Type (owner) | Default | Clone | Canonical | Design |
| --- | --- | --- | --- | --- | --- |
| `Config` | `RunConfig` record (R1) | null | shared (immutable) | presence + `RunConfig.WriteCanonical` | §3.1 |
| `Acts` | `List<ActInstance>` (R1) | empty | `ActInstance.DeepClone` | count + each | §2.3, §6.2 |
| `CurrentActIndex`, `ActFloor`, `NextRoomId` | int (R1; setter side effects of `Runs/RunState.cs:353-360` are R1's) | 0 | value | i32 ×3 | §2.1 |
| `Map` | `ActMap` (R2) | null | **shared** (immutable topology) | presence + `ActMap.WriteCanonical` (topology + quest overlay) | §2.6, §4 |
| `VisitedMapCoords` | `List<MapCoord>` (R2 type) | empty | copy | list | §2.1 |
| `MapPointHistory` | `List<MapPointHistoryEntry>` (R1) | empty | entry `DeepClone` | list | §2.5 |
| `VisitedEventIds` | `HashSet<string>` ordinal | empty | copy | sorted ids | §2.1 |
| `ExtraFields` | `ExtraRunFields` (R1, implemented) | zeros | memberwise | bool, i32, bool | §2.1 |
| `Odds` | `RunOddsSet` (R3) | null | `CloneFor(clone.Rng)` | presence + own | §5 |
| `SharedRelicGrabBag` | `RelicGrabBag` (R3) | null | `DeepClone` | presence + own | §5.4 |
| `CurrentRooms` | `List<AbstractRoom>` (R4; `EventRoom` R5) | empty | `CloneFor(clone)` | list | §2.4 |
| `RewardsStack` | `List<RewardsSet>` (R3) | empty | `CloneFor(player, room)` re-linked by index | list | §2.4 |
| existing | `Players`, `Combat`, `Rng`, `Unlocks`, `Ascension`, `Modifiers`, `Choice`, `Fidelity`, `NextCardInstanceId` | — | Part B | Part B | — |
| derived | `Act => Acts[CurrentActIndex]`, `TotalFloor => MapPointHistory.Count` | — | — | — | §6.3 |

| Field (on `PlayerState`, `Run/State/PlayerState.Run.cs`, R1) | Type (owner) | Default | Clone | Canonical |
| --- | --- | --- | --- | --- |
| existing (W1) | `CharacterId`, `CurrentHp`, `MaxHp`, `Gold`, `Deck`, `Relics`, `PotionSlots`, `BaseMaxEnergy`, `BaseOrbSlotCount`, `CanUseOrRemovePotions`, `AscensionEffectsApplied`, `IsActiveForHooks` | — | — | — |
| `PlayerRng` | `PlayerRngSet` (exists) | null | `DeepClone` | presence + tag 0x5204 |
| `PlayerOdds` | `PlayerOddsSet` (R3) | null | `CloneFor(cloned PlayerRng)` | presence + own |
| `RelicGrabBag` | `RelicGrabBag` (R3), `refreshAllowed: false` | null | `DeepClone` | presence + own |
| `ExtraFields` | `ExtraPlayerFields` (R1, implemented: `CardShopRemovalsUsed`, `WongoPoints`) | zeros | memberwise | i32 ×2 |

Not ported (log or profile only): `_allCards`, `Discovered*`, `MaxAscensionWhenRunStarted`, per-player
`UnlockState` (the run's `Unlocks` is the union, one player). Profile fields absent from the save live in
`RunConfig.Progress` (`RunProfileProgress { DiscoveredActIds, NumberOfRuns, WongoPoints, AllActsDiscovered }`,
default `Veteran`). Canonical tags: run state 0x55xx (`Run/State/RunStateCanonicalTags.cs`), map 0x56xx
(`Run/Map/MapCanonicalTags.cs`), rewards / merchant / rest / treasure / rooms 0x58xx
(`Run/Rewards/RewardsCanonicalTags.cs`), events 0x59xx (`Run/Events/EventsCanonicalTags.cs`).
Pin note: adding these lines moved the two pinned hashes in `EndToEndTests` (opening `0xD9F811A7035088B2`,
final `0xA8037EFF0501E0C4`); any owner who changes the canonical form of a field that is non-null in
standalone combats refreshes them and says so in the commit.

## C3. Ownership (paths under `sim/Sts2Sim.Core/` unless stated)

| Owner | Owns (create / edit) | Delivers (design §) | Tests / spec |
| --- | --- | --- | --- |
| R1 run-state | `Run/RunState.cs`, `Run/State/*` (`ActModel`, `ActInstance`, `RoomSet`, `MapPointHistoryEntry`, `ExtraFields`, `RunConfig`, `ContentFilter`, `PlayerState.Run.cs`, tags), `Run/Acts/*` (the 4 act defs: Overgrowth, Underdocks, Hive, Glory), ascension run effects (A1–A4, A6, A7, A10 sites stay with their owners; R1 owns `AscensionManager.ApplyEffectsTo` order) | §2, §6.1–6.4, §3.1 steps 2–5 state side | `RunStateTests.cs` / `run-state.md` |
| R2 map | `Run/Map/*` (`MapCoord`, `MapPoint`, `ActMap`, `StandardActMap`, `SpoilsActMap`, `GoldenPathActMap`, `SavedActMap`, `MapPathPruning`, `MapPostProcessing`, `MapPointTypeCounts`, `MapTravel`, projections) | §4, §3.5 option set | `MapTests.cs` / `run-map.md` |
| R3 odds-rewards | `Run/Odds/*`, `Run/Rewards/*` (incl. `RewardsCmd`, `Reward` subclasses, `CardRewardAlternative`), `Run/Factories/*` (`CardFactory.Run.cs`, `RelicFactory`, `CardCreationOptions`, `CardCreationResult`), `Run/Relics/RelicGrabBag.cs`, `Run/Util/GrabBag.cs` | §5, §3.6 generation and claim | `RewardsTests.cs` / `run-rewards.md` |
| R4 rooms | `Run/Rooms/*` (`AbstractRoom`, `RoomStack`, `MapRoom`, `CombatRoom` glue §8.3, `MerchantRoom`/`MerchantInventory`/`MerchantEntry` + entries, `RestSiteRoom`/`RestSiteOption` + options, `TreasureRoom` + relic picking) | §3.7–3.9, §8.3, §3.3 steps 3, 6, 8 | `RoomTests.cs` / `run-rooms.md` |
| R5 events | `Run/Events/*` (`EventModel`, `AncientEventModel`, `EventOption`, `EventRoom`, pages, event combat + resume, Neow, TheArchitect, option generation of Orobas, Pael, Tezcatara, Nonupeipe, Tanx, Vakuu, Darv); W3 adds the other events one file each under `Run/Events/Defs/` | §3.10, §3.11 | `EventTests.cs` / `run-events.md` |
| R6 run loop | `Run/Loop/*` (`RunSim`, `RunLoop`, `RunLegalActionGenerator`, `RunActionExecutor`); `Sts2Sim.Bench` run-soak option | §3.1–3.5, §3.12–3.14, §8.3 composition | `RunLoopTests.cs` (incl. the soak) / `run-loop.md` |
| R7 save import | `Run/Save/*` (`SavedRun` DTOs, `RunSaveReader`, `RunImporter`, `ImportOptions`, `RunImportResult`, `SavePoint`); `sts2rec/sts2rec/run_save.py` + its tests; fixture under `sim/Sts2Sim.Tests/Fixtures/` | §7 | `RunSaveTests.cs` / `run-save-import.md` |
| R8 run oracles | `sim/Sts2Sim.Diff/Run/*`, `sts2rec export-run-oracles` (after R6, R7) | §7.5 | `RunOracleTests.cs` / `run-oracles.md` |
| scaffold (frozen) | `Api/RunAction.cs`, `Registry/RunRegistries.cs` (`ActRegistry` DefTable, `EventRegistry` ModelTable) | C1 | — |

Rules: never edit a file you do not own; a needed member in another owner's type is requested, and until it
lands the caller's test wraps the call in `W1Pending.Run(test, "<owner>", body)`. Stubs throw
`NotImplementedException("W2 <owner>: <Type>.<Member>")`. Signatures in the skeleton are the call surface;
an owner may add members freely but changes an existing signature only together with every caller.

Cross-owner call surface (who calls what):

| Caller | Callee (owner) |
| --- | --- |
| R6 `RunLoop.CreateForNewRun` | R1 `ActModel.GetRandomList`, `ActInstance.GenerateRooms`, `ExtraRunFields`; R3 `RelicGrabBag.Populate`, `RunOddsSet`, `PlayerOddsSet`; R5 Neow entry |
| R6 `EnterAct` / `EnterMapCoord` | R1 `ActModel.CreateMap` → R2 `StandardActMap`; R3 `UnknownMapPointOdds.Roll`/`ResetToBase`; R4 `RoomStack.CreateRoom`/`EnterRoom`/`ExitCurrentRooms`; R1 `ActInstance.Pull*`/`MarkRoomVisited` |
| R6 legal actions | R2 `MapTravel.GetLegalMapPoints`; R3 `RewardsSet`, `CardReward.Cards/Alternatives`; R4 `MerchantInventory.AllEntries`, `RestSiteRoom.Options`, `TreasureRoom`; R5 `EventModel.CurrentOptions`; `PotionActions` (W1) for D9 |
| R4 `CombatRoom.StartCombat` | `CombatLoop.SetUpCombat(rs, player, encounter, context)` + `StartCombat` (W1); `Hook.AfterRoomEntered` between them (§8.3) |
| R4 `CombatRoom.OfferRoomEndRewards`, `RestSite` heal, R5 events | R3 `RewardsCmd.GenerateForRoomEnd`/`OfferCustom` |
| R4 merchant | R3 `CardFactory.CreateForMerchant`, `RelicGrabBag.PullFromBack`; W1 `PotionFactory.CreateRandomPotionsOutOfCombat` |
| R4 treasure, R3 elite relic, R5 events | R3 `RelicFactory.*`, `RelicGrabBag.PullFromFront`; W1 `RelicCmd.Obtain` |
| R7 importer | R1 state constructors, R2 `SavedActMap`, R3 `RelicGrabBag`/odds load, R6 `RunSim` entry per `SavePoint` |
| R8 | R6 `RunSim`, R7 `RunImporter`, R2 projections |

## C4. Standalone combats

`CombatSim.Create(...)` and `CombatLoop.SetUpCombat(...)` keep their behaviour: they build
`RunState.CreateStandalone` (all new fields at their defaults, nullable ones null). `RunSim` embeds a combat
through `RunState.Combat` (R4 `CombatRoom.StartCombat` sets it via `CombatLoop.SetUpCombat(rs, player, …)`
on the run's live `Rng`, D4); `RunSim.Combat` exposes it as a `CombatSim` over the same `RunState`
(`CombatSim.FromState`, with prompt handling as in `CombatSim`). HP, deck, relics, potions and gold are
the shared `PlayerState` (D3): no copy-in / copy-out.

## C5. Gates

- Build 0 warnings 0 errors (Debug and Release); suite green; PENDING only through `W1Pending` for
  cross-owner stubs (0 at merge of the wave); diff gate unchanged (4/4 bundles, 69/69 decisions).
- W3 content gate: `sim/Sts2Sim.Tests/ContentSoakTests.cs` (`ContentSoak.Run(ContentSoakOptions)`, filters
  per kind) — every encounter ×3 seeds ×300 steps, every card ×2 seeds ×150 steps, every relic and potion
  ×2 seeds; uniform-random legal playouts; failures listed `<id>/<seed>: <Type>: <message>` (first 30 + total).
- R6: random full-run soak, 10⁴ runs in tests, 10⁵ as a Bench option, with `RunConfig.Content =
  ContentFilter.PortedOnly` (pulls restricted to registered content; unported events replaced by a stub
  event owned by R5) — no exception, `Hash(x) == Hash(x.Clone())` at random points, legal-action closure.
- R7/R8: the oracles of design §7.5 on the local saves and `.run` files (counts reported, not gated, until
  W3 content covers the runs).

# Part D — E2 training interface contract (`Sts2Sim.Vec`) · 2026-11-01

Process (user): speed over polish — brief → parallel owners (isolated worktrees, reset to the scaffold
commit) → one integrator with automated gates → commit. No adversarial review. Engineering agents on Opus.
Inputs: the kernel inventory and binding measurements of 2026-11-01 (scratchpad reports
`e2-kernel-inventory.md`, `e2-prior-art-and-bindings.md`; numbers quoted below come from them).

## D0. Decisions

| # | Decision | Why |
| --- | --- | --- |
| D1 | **In-process C ABI.** `sim/Sts2Sim.Vec` is a `net9.0` class library (`AllowUnsafeBlocks`, `EnableDynamicLoading`) that references **only** `Sts2Sim.Core` and exports `[UnmanagedCallersOnly(EntryPoint = "sts2vec_…")]` functions with blittable arguments (handles, int32 counts, pointers into caller-owned buffers). Python loads it through hostfxr with a shipped `sts2vec.runtimeconfig.json` (`System.GC.Server = true`) and calls the exports with ctypes. NativeAOT (`NativeLib=Shared`, `TrimmerRootAssembly Sts2Sim.Core`) is a publish option with the same exports, not a gate. pythonnet is a debug/test tool only. | Measured: 0.05 µs per call, zero copies, GIL released during calls; 1,000 games × 512 floats stepped in 1.8 ms on 8 threads with Server GC (4.8× faster than Workstation GC). Pipes/shared memory cost 0.1–0.5 ms per 5 MB batch and stay as later options. |
| D2 | **Batch-only, struct-of-arrays, caller-owned buffers.** Every call steps or resets *all* selected slots; observations are written in place into numpy arrays allocated by Python from the schema. No per-env Python work. | Python-driven per-step loops run at 19–30k steps/s against 141k native. |
| D3 | **Entity tables, fixed capacities, ids + numeric features + counts.** No hashing of ids, no normalising ids into scalars. Vocabularies are built from the Core registries (sorted ordinal) and published in the schema with an `encoder_version` hash. | A10 review §4/§6 (entity encoding), hack-balatro's lesson. |
| D4 | **Candidate list in `LegalActions()` order** with `(kind, A, B)` and pointer columns into the entity tables; the policy returns a candidate index. No flat action table. | 21 action kinds with variable option sets; prompt option lists are unbounded. |
| D5 | **Reward = 1 at a Victory terminal, else 0; γ = 1 lives in the learner.** Floor, act, HP, gold, bosses go to `aux`, never into the reward. `terminated` on Victory/Defeat, `truncated` on the step cap or the floor stall cap (`floor_step_cap`, 2026-11-03). **Status codes are separate from outcomes**: content gaps and simulator errors are never losses, an illegal action never triggers a silent fallback. | Approved plan; fail-closed convention of this repo. |
| D6 | **Starts come from a pool of snapshots.** Fresh runs (character, ascension, seed text) are created once per pool entry and cached as a `RunSim` template; resets `Clone()` the template (15 µs) instead of `NewRun` (16.5 ms, map pruning). Imported saves (`RunImporter.FromSave`, checked) and live snapshots (`Clone()` of a slot) are pool entries too. Per-slot deterministic start sequence, independent of thread scheduling. | `RunExporter.ToSave` of a live run is faithful only at act start, so curriculum snapshots are clones, not saves. |
| D7 | **Tape engine for stepping** (`RunSim.Step(a)`, one deep clone per step, prompt-safe), **with a clone-free fast path** (2026-11-02): `config.fast_path` (default true) steps with `RunSim.StepFast`, which skips the clone when the static prompt-site predicate `RunSim.MayPrompt` proves no prompt can occur (≈ 82 % of random-policy steps) and fails closed (SIM_ERROR) if one does. Outputs are byte-identical to `fast_path: false`. `StateHash` (14–53 µs) is never called inside the step loop. | Prompts are decision points; the batched policy cannot answer them inline. |
| D8 | **Player view is filtered in C#** (§D3.3) and verified by a leak test: reshuffling the draw pile of a cloned state must not change the encoded observation. The next move id (`move`/`move_name` in the Server observation) is a leak and is **not** encoded; only intents are. | Hidden-information inventory H1–H15. |
| D9 | **Vec never touches Server/Diff behaviour.** The projection helpers Vec needs (`SimProjector.IsVisible`, `DisplayAmount`, `Intent`, `CostOf`, `CanPlay`, ≈80 lines) move into Core as `Sts2Sim.Core/Combat/Pipeline/PlayerView.cs`; Diff and Server call the Core versions (zero behaviour change; G0 and the Server tests prove it). | One source of truth for the player view; Vec must not depend on the Diff assembly. |

## D1. Projects, files, ownership

| Owner | Creates / edits | Must not touch |
| --- | --- | --- |
| scaffold (phase 0, in the wave worktree) | `sim/Sts2Sim.Vec/` project (csproj, `Schema.cs`, `Vocab.cs`, `VecEnv.cs` skeleton, `StartPool.cs` skeleton, `CandidateTable.cs` stub, `ObservationEncoder.cs` stub, `Exports.cs` with every entry point catching exceptions, `Status.cs`, `sts2vec.runtimeconfig.json`), solution entry, `sim/Sts2Sim.Tests/VecTests.cs` skeleton, `sim/tools/vec/` Python package skeleton (`pyproject.toml`, `sts2vec/{loader,schema,env,cli}.py`, tests skeleton), `sim/VEC.md` skeleton | kernel, Server, Diff |
| V1 encoder | `Sts2Sim.Core/Combat/Pipeline/PlayerView.cs` (moved helpers) + the one-line re-pointing in `Sts2Sim.Diff/SimProjector.cs` and `Sts2Sim.Server/Describe.cs`; `Sts2Sim.Vec/ObservationEncoder.cs` (+ partials per table); `VecTests.cs` encoder/leak/parity tests; `sim/VEC.md` field reference | `VecEnv.cs`, `StartPool.cs`, `CandidateTable.cs`, `Exports.cs`, Python |
| V2 env-core | `Sts2Sim.Vec/{VecEnv,StartPool,CandidateTable,Exports,Status}.cs` bodies; `Sts2Sim.Bench` `vec` subcommand; `VecTests.cs` env/candidate/pool/determinism/thread tests | `ObservationEncoder*.cs`, `PlayerView.cs`, Python |
| V3 python | `sim/tools/vec/**` (loader, schema, env, pool, bench, check, tests, README); `scripts/vec_publish_aot.sh` (optional) | any C# |
| integrator | merges V1 → V2 → V3, gates, `sim/BENCH.md` rows, `sim/VEC.md` final, `sim/specs/integration.md` section | — |

Namespace `Sts2Sim.Vec`. The scaffold freezes `Schema.cs` (tables, capacities, dtypes, buffer order), `Status.cs`
and the export signatures (§D5); owners add members freely but change a frozen signature only together with every
caller, in their own branch, and say so in their report.

## D2. Environment model

- `VecEnv(config)` owns `N` **slots**. A slot holds a `RunSim` (or nothing before the first reset), `episode_id`
  (int64, unique per env instance, increasing), `step_index`, `start_entry`, `rng_slot_seed`.
- `reset(mask, start_ids)`: every slot with `mask[i] != 0` is (re)started from pool entry `start_ids[i]`, or from the
  slot's deterministic start sequence when `start_ids[i] < 0`: `entry = order[(slot_seed + episode_count) mod pool_count]`
  where `order` is the pool in insertion order and `slot_seed = XXH64(config.seed_text ‖ slot)`. Observations of the
  reset slots are written; other slots are left untouched (their buffers are not rewritten).
- `step(actions)`: for every slot with `actions[i] >= 0` apply `candidates[i][actions[i]]` (an index outside
  `[0, cand_n[i])` → status `ILLEGAL_ACTION`, slot unchanged, nothing else happens to it); then write *every* slot's
  observation, candidates and signals. Slots with `actions[i] < 0` are no-ops (status bit `NOOP`). The candidates are
  `RunSim.LegalActions()` except on the rewards screen (appended 2026-11-04, `sim/VEC.md` "Rewards-screen macros"):
  a card reward is listed as one `TakeRewardCard` (21) row per offered card plus one `TakeRewardAlternative` (22) row per
  REROLL / SACRIFICE, each applied as the game's ClaimReward + PickRewardCard / RewardAlternative in one step (one
  decision), and a potion claim the game would refuse (full belt) is not listed.
- **Auto-reset in the same step** (config `auto_reset = true`, default): when a step ends an episode (terminal,
  truncated, content gap or sim error), the step writes `reward`, `terminated`/`truncated`, `status` and the
  `last_episode` block for the finished episode, then immediately resets the slot from its start sequence and writes
  the **new** episode's first observation and candidates. `episode_id[i]` changes on that step. With
  `auto_reset = false` a finished slot stays terminal (`cand_n = 0`) until `reset`.
- Threads: `Parallel.For` over slots with `config.threads` (default 8) worker threads; one slot is only ever touched by
  one thread per call; no shared mutable state (verified: 1,000 runs on 1/4/12 threads hash identically).
- Episode end rules: `terminated = 1` when `RunSim.IsTerminal` (Victory → `reward = 1`, Defeat → `0`);
  `truncated = 1` when `step_index >= config.step_cap` (default 3,000 decisions) — reward 0; **floor stall cap**
  (appended 2026-11-03, `config.floor_step_cap`, default 80, 0 = off): `truncated = 1`, reward 0, status `FLOOR_STALL`
  (256), `last_episode.end_kind = FloorStall` (5) on the step that leaves the episode at a non-combat decision point on
  a floor where it has already taken `floor_step_cap` decisions at non-combat points (decisions = actions `>= 0` on a
  live slot, legal or illegal — an illegal index leaves the run unchanged but counts here, as in the protocol
  evaluator; no-ops do not count; the count restarts whenever `RunState.TotalFloor` changes and at every start;
  combat decisions never count). Precedence terminal → step cap → floor stall; auto-reset as for the step cap. It is
  the protocol's `stuck` limit (v0.2 §5) and `sts2vec eval` uses it; `CONTENT_GAP`
  (`NotImplementedException`/`NotSupportedException` from the sim) and `SIM_ERROR` (any other exception) end the
  episode with `reward = 0`, `terminated = 0`, `truncated = 1` and the status bit set; the exception text is kept in
  the slot's last error (`sts2vec_last_error(h, slot)`), the step never throws across the ABI.
- Determinism: same pool, same `config.seed_text`, same action sequence per slot ⇒ byte-identical observations and
  equal `StateHash` per slot, for any thread count and across processes.

## D3. Observation schema (frozen in `Schema.cs`; published by `sts2vec_schema`)

Dtypes: ids and counts `int32`, computed numbers `float32`, flags `uint8`. Every table has a fixed capacity; rows beyond
the table's count are zero-filled; `*_n` holds the count. An id is an index into the schema's vocabulary list for
that kind (`0` = none/padding, `1` = unknown, real ids from 2). Card identity is `(card_id, upgrade)` in two columns.
Capacities (CAP_*): HAND 10 · DECK 96 · RELICS 32 · POTIONS 10 · PLAYER_POWERS 24 · ENEMIES 6 · ENEMY_INTENTS 3 ·
ENEMY_POWERS 12 · ALLIES 2 · ALLY_POWERS 8 · ORBS 10 · MAP_ROWS 18 · MAP_COLS 7 · REWARDS 8 · CARD_REWARD 4 ·
CARD_REWARD_ALTS 2 · SHOP 14 · REST 9 · EVENT_OPTIONS 8 · PROMPT_OPTIONS 128 · CANDIDATES 192 · HAND_VARS 4 · appended
2026-11-03: DRAW_MARKED 32 · PILE_ORDER 64 · EVENT_VALUES 3 · EVENT_ITEMS 2 · appended 2026-11-04: REWARD_CHOICES 16.
When a table overflows its capacity the first CAP rows are written and the status bit `TABLE_OVERFLOW`
(candidates: `CANDIDATE_OVERFLOW`) is set for the slot.

### D3.1 Buffers (per env; Python allocates `(N, *shape)` for each, passes pointers in this order)

| # | Name | dtype | shape | Content |
| --- | --- | --- | --- | --- |
| 0 | `global_i` | int32 | [GI] | phase (RunPhase), character, ascension, act_index, act_id, total_floor, act_floor, map_row, map_col, hp, max_hp, gold, energy, max_energy, stars, block, turn, round, potion_slots, deck_n, draw_n, discard_n, exhaust_n, orb_slots, relic_n, hand_n, enemy_n, ally_n, boss_id, second_boss_id, card_removals_used, removal_price, incoming_damage, incoming_hits, prompt_kind, prompt_min, prompt_max, prompt_picked, prompt_needs_confirm, prompt_can_cancel, prompt_can_skip, prompt_ordered, prompt_option_n, prompt_pile, prompt_site_hash, reward_n, card_reward_can_skip, card_reward_skip_consumes, treasure_opened, treasure_relic_id, treasure_claimed, event_id, event_finished, event_option_n, rest_option_n, shop_entry_n, decision_kind (= phase, or `Prompt` while a prompt is pending), is_combat (`State.Combat.IsInProgress`). The exact order is the schema's `global_i.fields` list; GI is its length. |
| 1 | `global_f` | float32 | [GF] | hp_frac, gold_log1p, floor_frac (total_floor / 48 or 49), incoming_net_hp (hp − max(0, incoming − block)), and any other derived scalar the encoder adds (listed in the schema). |
| 2 | `hand` | int32 | [10, HC] | per card: card_id, upgrade, cost_current (−1 = X, −2 = unplayable), cost_base, type, target, rarity, keyword_mask, enchant_id, enchant_amount, affliction_id, playable, playable_reason_id, hits, var_id[4], var_val[4] (first four dynamic vars in canonical order, base values) |
| 3 | `hand_f` | float32 | [10, 2] | block_eff, damage_eff_self_or_all (effective block; effective damage for AllEnemies/RandomEnemy/None targets) via `CardPreview` |
| 4 | `hand_dmg` | float32 | [10, 6] | effective damage vs enemy slot j (0 when not applicable / dead) via `CardPreview.Damage` |
| 5 | `deck` | int32 | [96, 5] | card_id, upgrade, enchant_id, enchant_amount, affliction_id (pickup order; `deck_n` in global) |
| 6 | `draw_counts`, 7 `discard_counts`, 8 `exhaust_counts` | int16 | [V_cards × 2] | multiset counts per (card_id, upgrade) — draw order hidden by construction; discard/exhaust order dropped (documented simplification) |
| 9 | `relics` | int32 | [32, 5] | relic_id, counter (DisplayAmount or −1), status (0 Normal / 1 Active / 2 Disabled), used_up, stack |
| 10 | `potions` | int32 | [10, 3] | potion_id (0 = empty), usable_now, target_type |
| 11 | `powers_player` | int32 | [24, 3] | power_id, amount (DisplayAmount), is_debuff |
| 12 | `orbs` | int32 | [10, 3] | orb_id, passive, evoke |
| 13 | `enemies` | int32 | [6, 9] | present, uid, monster_id, hp, max_hp, block, flank, behind, intent_n (slot j = `Monsters[j]`, dead ones present = 0; this is the action target index) |
| 14 | `enemy_intents` | int32 | [6, 3, 4] | type (IntentType), damage (after modifiers, as the UI shows), times, card_count |
| 15 | `enemy_powers` | int32 | [6, 12, 2] | power_id, amount |
| 16 | `allies` | int32 | [2, 5] | present, monster_id, hp, max_hp, block (pets only; `Allies[1..]`) |
| 17 | `ally_powers` | int32 | [2, 8, 2] | power_id, amount |
| 18 | `map_nodes` | int32 | [18, 7, 6] | point_type (MapPointType, 0 = no node), visited, current, legal_next, child_mask (bit c = edge to column c of the next row), quest |
| 19 | `rewards` | int32 | [8, 4] | reward_type, amount (gold), item_id (potion/relic/special card), is_card_reward |
| 20 | `card_reward` | int32 | [4, HC] | same columns as `hand` without playability; `card_reward_alts` int32 [2]: alt_id (Skip/REROLL/SACRIFICE) |
| 21 | `shop` | int32 | [14, 7] | category, item_id, upgrade, price, stocked, on_sale, affordable |
| 22 | `rest` | int32 | [9, 2] | option_id, enabled |
| 23 | `event_options` | int32 | [8, 6] | key_hash (XXH32 of `"<eventId>:<TextKey>"`), ordinal, enabled, locked, proceed, relic_id |
| 24 | `prompt_options` | int32 | [128, 5] | item_id (card/relic/potion id; bundles: hash of the id list), upgrade, picked, kind_of_item (card/relic/potion/bundle/index), ordinal |
| 25 | `cand` | int32 | [192, 9] | kind (RunActionKind), A, B, hand_slot, target_slot, potion_slot, option_index, card_id (plays/picks), target_uid |
| 26 | `cand_n` | int32 | [] | number of candidates (0 when terminal) |
| 27 | `reward` | float32 | [] | 1 at a Victory terminal, else 0 |
| 28 | `terminated`, 29 `truncated` | uint8 | [] | episode-end flags for this step |
| 30 | `status` | int32 | [] | bit flags: OK 0, NOOP 1, ILLEGAL_ACTION 2, CONTENT_GAP 4, SIM_ERROR 8, CANDIDATE_OVERFLOW 16, TABLE_OVERFLOW 32, RESET 64 (slot was reset this call), POOL_EMPTY 128, FLOOR_STALL 256 (appended: truncated by `floor_step_cap`) |
| 31 | `episode` | int64 | [3] | episode_id, step_index, start_entry |
| 32 | `aux` | float32 | [A] | total_floor, act_index, hp, max_hp, gold, bosses_killed, combats_won, deck_n, relic_n, is_combat, act_floor, map_row, map_col, act_rooms (listed in the schema; the last four appended in E3 for the protocol progress metric, A = 14) |
| 33 | `last_episode` | int32 | [8] | written when an episode ends: win, floor, act, steps, final_hp, final_max_hp, end_kind (terminal/truncated/content_gap/sim_error/floor_stall), last_encounter_id |
| 34 | `global_x` | int32 | [2] | appended 2026-11-03 (player-view schema append, docs/parity-audit-2026-11-02.md §5.2): ancient_id (events vocab; the current act's Ancient), draw_marked_n |
| 35 | `hand_x` | int32 | [10, 2] | appended: affliction_amount, dampened (row i = `hand` row i) |
| 36 | `draw_marked` | int32 | [DRAW_MARKED, 6] | appended: card_id, upgrade, enchant_id, enchant_amount, affliction_id, affliction_amount of the draw-pile cards with an enchantment or affliction, rows sorted by content (H1) |
| 37 | `discard_order`, 38 `exhaust_order` | int32 | [PILE_ORDER, 6] | appended: the same columns in pile order (oldest first; the pile screens show it) |
| 39 | `event_option_values` | int32 | [EVENT_OPTIONS, 9] | appended: value_n, value_0..2, item_n, item_kind_0, item_id_0, item_kind_1, item_id_1 — the numbers and items option i's text names (localisation placeholders resolved on the live event; enum `ItemKind`) |
| 40 | `reward_choices` | int32 | [REWARD_CHOICES, 3 + HC] | appended 2026-11-04: reward_index, option, alt_id, then the `hand` columns — row k = the target of the k-th rewards-screen macro candidate (the cards of the unopened card rewards, then their REROLL / SACRIFICE) |
| 41 | `map_routes` | int32 | [MAP_ROWS, MAP_COLS, 5] | appended 2026-11-04: per `map_nodes` cell, elites_min, elites_max (Elite rooms on a path from the node to the act's boss, node included; −1 = unreachable), rests, shops (distinct nodes reachable), boss_dist (moves to the boss) — computed from `map_nodes`' point_type / child_mask |

`HC` and the field orders are published in the schema; the Python side never hard-codes offsets. Appending rule (as
for `aux` in E3 and the six buffers of 2026-11-03): new data goes into new buffers (or new last columns of `aux`)
appended after the existing ones, so every earlier buffer keeps its index, shape and meaning; `encoder_version` changes.

### D3.2 Schema JSON (`sts2vec_schema`)

`{"encoder_version": "<xxh64 hex of this JSON without the version field>", "buffers": [{"name","dtype","shape","fields": [...]}],
"vocab": {"cards": [...], "relics": [...], "potions": [...], "powers": [...], "monsters": [...], "encounters": [...],
"events": [...], "orbs": [...], "enchantments": [...], "afflictions": [...], "acts": [...], "characters": [...],
"enums": {"RunPhase": [...], "RunActionKind": [...], "IntentType": [...], "MapPointType": [...], "RewardType": [...],
"ChoicePromptKind": [...], "CardType": [...], "TargetType": [...], "CardRarity": [...], "RestOption": [...],
"CardRewardAlt": [...], "ShopCategory": [...]}}, "caps": {...}, "card_vars": [...131 names...], "playable_reasons": [...]}`.
Vocabularies are the registries' `AllIds` (sorted ordinal) with `""` at 0 and `"?"` at 1; cards additionally list the
`CardCatalog` ids that the registry lacks (unported) so the catalog is version-stable. `V_cards` = `vocab.cards.length`.

### D3.3 Player-view rules (the encoder MUST follow; the leak test enforces H1)

H1 draw pile as counts only (appended `draw_marked`: its marked cards sorted by content, never in pile order); H2 no
seed, RNG stream or counter anywhere; H3 no future rooms/events/encounters — the
current act's boss ids are visible (the map shows them); H4/H5 no odds or grab bags; H6 treasure relic only when
`ChestOpened`; H7 card-reward cards: `card_reward` only in the `CardReward` phase (the rewards list says "card reward"),
and since 2026-11-04 the unopened rewards' cards in `reward_choices` on the rewards screen (one free click away in the
game: opening and closing a card reward leaves it unclaimed); H8 **no move
ids** — intents only (type, damage, times, card_count); H9 no monster/encounter extra state; H10 powers through
`PlayerView.IsVisible` and `DisplayAmount`; H11 no event internals (appended `event_option_values` reads only the event
members an option's text names); H13 no instance ids except enemy `uid`
(= `CombatId`, which the player can also track); prompt options are identified by `(item_id, upgrade, ordinal)`.
Effective numbers come from `CardPreview` (pure, measured 0 hash changes) and the intent projection, never from
re-implemented formulas.

## D4. Start pool (`StartPool.cs`)

- Entry kinds: `Fresh(character, ascension, seedText, act1, content)` (template created on first use with
  `RunSim.NewRun`, or ahead of time with `sts2vec_pool_warm`, and cached; resets clone it), `Save(path)`
  (`RunImporter.FromSave` with `ImportOptions.Default`, `Content = PortedOnly`; unsupported → error with the reasons),
  `Snapshot(RunSim clone)` (from `sts2vec_snapshot(slot)`).
- `config.pool` may pre-populate fresh entries: `{"fresh": {"characters": [...], "ascension": 0, "count": 4096,
  "seed_prefix": "TRAIN"}}` → seed texts `"<prefix>-<character>-<i>"`; entries are also added at run time with
  `sts2vec_pool_add_fresh/save`. Template cache size is bounded by `config.pool_cache` (default 4,096 templates ≈ 0.4 GB);
  beyond it, fresh entries are created on demand without caching.
- `sts2vec_restore(slot, entry)` = reset one slot from one entry (E4 search). `sts2vec_snapshot(slot)` returns a new
  entry id holding a `Clone()` of the slot's current state (works at prompts too: the clone carries the tape).

## D5. C ABI (frozen by the scaffold; names exactly as below)

```c
int32_t sts2vec_version(uint8_t* buf, int32_t cap);                       // writes "sts2vec/<encoder_version>"; returns needed length
int32_t sts2vec_schema(uint8_t* buf, int32_t cap);                        // JSON of §D3.2; returns needed length (call with cap 0 first)
int64_t sts2vec_create(const uint8_t* config_json, int32_t len);          // > 0 handle, or -1 (see sts2vec_last_error(0, …))
int32_t sts2vec_destroy(int64_t h);
int32_t sts2vec_set_threads(int64_t h, int32_t threads);
int32_t sts2vec_pool_add_fresh(int64_t h, const uint8_t* json, int32_t len);   // {"character","ascension","seed","act1","content"} → entry id or -1
int32_t sts2vec_pool_add_save(int64_t h, const uint8_t* path_utf8, int32_t len); // entry id or -1 (reasons in last_error)
int32_t sts2vec_pool_count(int64_t h);
int32_t sts2vec_snapshot(int64_t h, int32_t slot);                        // entry id or -1
int32_t sts2vec_restore(int64_t h, int32_t slot, int32_t entry, void** buffers);
int32_t sts2vec_reset(int64_t h, const uint8_t* mask, const int32_t* start_ids, void** buffers);
int32_t sts2vec_step(int64_t h, const int32_t* actions, void** buffers);
int32_t sts2vec_state_hash(int64_t h, const int32_t* slots, int32_t n, uint64_t* out);   // test/debug only (14–53 µs each)
int32_t sts2vec_last_error(int64_t h, int32_t slot, uint8_t* buf, int32_t cap);         // slot -1 = env-level; h 0 = global
// additive exports (appended after the freeze; the frozen signatures above are unchanged)
int32_t sts2vec_pool_warm(int64_t h, int32_t max_entries);                // E2c: fresh templates created now → count, or -1
int32_t sts2vec_clone_determinized(int64_t h, int32_t slot, uint64_t seed); // pool entry id of RunSim.CloneDeterminized(seed) of the slot, or -1
```

`sts2vec_clone_determinized` appends a pool entry holding a determinized clone of the slot's current state (prompts
included): identical player view, hidden state (draw-pile order, every RNG stream and the hashed run seed, relic
grab-bag and unvisited event order) resampled from `seed`; same state and seed ⇒ same entry state. `sts2vec_restore`
starts a slot from it. Details and citations: `sim/VEC.md` "Determinized clones".

`sts2vec_pool_warm` creates the templates of up to `max_entries` fresh entries that have none yet (`max_entries < 0`: all),
in entry-id order and only while the template cache (`config.pool_cache`) has room, on the env's worker threads; it
returns the number created by the call (0 when all are warm or the cache is full). A warm template is the `NewRun` the
entry's first start would create, so warming changes no start, only when the ≈ 16 ms per entry is paid. Additive exports
are appended to the export list; a loader treats them as optional for library builds that predate them.

`buffers` is an array of `void*` in the order of `schema.buffers`, each pointing at a C-contiguous array of shape
`(N, *shape)`. Return values: 0 ok, −1 error (text in last_error); per-slot problems are reported in `status`, never as
a return code. Every export is wrapped in `try/catch`: an exception never crosses the ABI. `config_json`:
`{"num_envs": N, "threads": 8, "step_cap": 3000, "auto_reset": true, "seed_text": "VEC", "content": "ported_only",
"pool": {...}, "pool_cache": 4096, "fast_path": true, "floor_step_cap": 80}`. `floor_step_cap` (additive config key,
2026-11-03; default 80, 0 = off, < 0 rejected): the floor stall cap of §D2. Config keys are appended like exports: a
library built before a key rejects it as unknown, so a binding sends it only when the schema's `EndKind` enum lists
`FloorStall` (no new export; the version string `sts2vec/<encoder_version>` changes with the enum). `fast_path` (additive, default true) steps a slot with
`RunSim.StepFast` (clone-free when `RunSim.MayPrompt` is false); outputs are identical to `fast_path: false`, and a prompt
on the clone-free path ends the episode as SIM_ERROR (`FastPathPromptException` in the slot's last error), never an answer.

## D6. Python package (`sim/tools/vec`, package `sts2vec`, uv project, deps: numpy; dev: pytest)

- Loader: locate `Sts2Sim.Vec.dll` + `sts2vec.runtimeconfig.json` (env `STS2VEC_DLL`, or the Release build dir
  relative to the repo root), find `libhostfxr` (`DOTNET_ROOT`, `/opt/homebrew/opt/dotnet@9/libexec`, `dotnet --list-runtimes`),
  `hostfxr_initialize_for_runtime_config` → `load_assembly_and_get_function_pointer` with
  `UNMANAGEDCALLERSONLY_METHOD` for every export (the working prototype is in the session scratchpad
  `e2-bindings/scripts/raw_bench.py` and `e2-bindings/probe/`), wrap with ctypes. Assert Server GC at create
  (`sts2vec_create` fails with a clear message if `GCSettings.IsServerGC` is false). Optional `.dylib` path for the AOT build.
- `VecEnv(num_envs, *, threads=8, step_cap=3000, auto_reset=True, seed_text="VEC", content="ported_only", pool=PoolSpec(...), floor_step_cap=FLOOR_STEP_CAP (80), dll=None)`:
  `.schema` (parsed JSON, vocab helpers `vocab.cards[i]`, reverse maps), `.reset(mask=None, start_ids=None) -> obs`,
  `.step(actions) -> (obs, reward, terminated, truncated, status, info)` where `obs` is a dict of the env-owned numpy
  arrays (views, valid until the next call; `copy=True` to copy), `info = {"episode_id", "step_index", "start_entry",
  "last_episode"}`; `.pool.add_fresh(...)`, `.pool.add_save(path)`, `.pool.warm(max_entries=None)` (E2c), `.snapshot(slot)`,
  `.restore(slot, entry)`, `.state_hash(slots)`, `.close()`, context manager. `.candidates(i)` returns the slot's
  candidate rows decoded to `RunActionKind` names for debugging.
- CLIs: `sts2vec bench [--envs 1024 --threads 8 --steps 500 --policy random --warm/--no-warm]` (env-steps/s, ms per
  batch, MB obs per batch, GC mode; the pool is warmed before timing by default and the warm time is printed separately)
  and `sts2vec check` (determinism across two envs and two thread counts; leak test via the C# export when present;
  schema/buffer consistency).

## D7. Tests and gates

C# (`sim/Sts2Sim.Tests/VecTests.cs`; all run in the normal suite):
- T1 determinism: same pool + same actions on 1 and 8 threads, and two env instances ⇒ identical `StateHash` per slot and
  byte-identical buffers (256 envs × 300 steps, random candidate policy seeded per slot).
- T2 leak: for ≥ 500 sampled combat decision points, encode `S` and a clone with the draw pile reshuffled ⇒ identical bytes.
- T3 schema: every buffer's size matches the schema; every table respects its capacity; overflow sets the status bit
  (construct a prompt with > 128 options via a large deck removal and a combat with > 192 legal actions or skip with a note).
- T4 candidates: for ≥ 10,000 decision points, `cand[i] ↔ LegalActions()[i]` (same order, same `(kind, A, B)`); stepping
  by candidate index hashes equal to stepping the `RunAction`; every listed candidate is legal; illegal indices leave
  the state unchanged and set `ILLEGAL_ACTION`.
- T5 episodes: reward 1 only at Victory (use a cloned state with boosted HP to reach it), 0 at Defeat; truncation at
  `step_cap`; auto-reset writes the new episode's first observation and bumps `episode_id`; `last_episode` is filled.
  Floor stall cap (2026-11-03): a shop buy/cancel loop is cut at exactly `floor_step_cap` (FLOOR_STALL, FloorStall),
  combat decisions never count, and the env agrees step for step with the evaluator's rule recomputed from observations.
- T6 starts: the fixture save starts at `Rewards` with 3 candidates and hashes equal to `RunImporter.FromSave`;
  `snapshot`/`restore` round-trip hashes equal, also at a pending prompt.
- T7 threads: 1,024 envs × 200 steps on 1 vs 12 threads ⇒ identical hashes.
- T8 ABI: invalid handle/slot/entry → −1 + last_error, never a crash; a slot whose sim throws gets `SIM_ERROR` and is reset.
- T9 PlayerView move: Server and Diff tests unchanged; G0 4/4 · 69/69.
Python (`uv run --project sim/tools/vec pytest -q`): loader + Server GC assert; schema ↔ buffer consistency; random soak
(1,024 envs × 300 steps, 0 `SIM_ERROR`/`CONTENT_GAP` with `ported_only`); determinism across two processes; `bench`
smoke; `add_save` on the fixture.
Gates (integrator): build 0 warnings; full suite green; Python tests green; G0 unchanged; Server tests unchanged; bench rows
in `sim/BENCH.md`: C# `Sts2Sim.Bench vec` and Python `sts2vec bench` at N ∈ {64, 1024, 4096} × threads {1, 8}
(target ≥ 50k env-steps/s at N = 1024 / 8 threads on this machine with a random policy, A0; report whatever is measured);
`sim/VEC.md` complete; status log + `sim/specs/integration.md` section.
