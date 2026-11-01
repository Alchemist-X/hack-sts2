# Sts2Sim contracts

Part A (below, 2026-10-28/30) is the P0 walking-skeleton contract and remains the baseline.
Part B (end of this file, 2026-11-01) is the P1 kernel-expansion (W1) contract; where the two differ,
Part B wins.

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
    ContextScope PushContext(ChoiceContextKind kind)   // disposable struct; the context stack is transient (not cloned, not hashed): re-execution rebuilds it
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
  `Hook.IterateRunHookListeners(rs, cs?)` — deck cards each followed by its enchantment, then (only
  without an attached combat) relics and potions, then modifiers, then the combat's own listeners.
- Per-type override masks (`HookMask`): a model type is visited only for hooks it overrides; the ordered
  listener array is cached and invalidated by a mutation version counter on the state. Observable
  semantics must equal the full iteration (tests compare both paths on random states).
- `Run/*` shells (phase 0): `sealed partial class` or enum for every run-layer type a hook or Cmd
  signature mentions (`RunState`, `AbstractRoom`, `MapPoint`, `RewardsSet`, `Reward`, `CardReward`,
  `MerchantEntry`, `RestSiteOption`, `EventModel`, `ActModel`, `MapPointHistoryEntry`, `RoomType`,
  `MapPointType`, …), each with a `// W2 fills` header and only the members W1 code reads.

## B4. Cmd facades (the game's `Commands/*.cs`; 199 public overloads listed in `kernel-gaps.json → cmd_facades`)

| Facade | W1 owner | Notes |
| --- | --- | --- |
| `CreatureCmd`, `PowerCmd`, `DamageCmd` / `AttackCommand`, `EnergyCmd`, `CombatStatus` | pipeline | Osty block/HP redirection (`Commands/CreatureCmd.cs:3697-3725`), `CreatureCmd.Add` (summons), `Stun` (wrapper body missing in the decompile → IL, D9) |
| `CardPileCmd`, `PlayerCmd`, the play half of `CardCmd` (`Combat/Loop/CardCmd.Play.cs`: `AutoPlay`, `Discard`, `DiscardAndDraw`, `Exhaust`, …) | loop | deck edits write `Player.Deck` through `DeckVersionId` |
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
| Potions | `Potions/PotionModel.cs`, `Potions/Defs/*.cs`, `Potions/PotionFactory.cs` | relics-potions | `OnUse`, `Usage` (`PotionUsage`), `TargetType`, `PassesCustomUsabilityCheck`, `IsValidTarget` (`Models/PotionModel.cs:618`), `OnUseWrapper` order `BeforePotionUsed` → `OnUse` → `History.PotionUsed` → `AfterPotionUsed` → `CheckForEmptyHand`; `PotionRegistry`; `GetDistinctForCombat` on `Rng.CombatPotionGeneration` |
| Orbs | `Orbs/OrbModel.cs`, `Orbs/OrbQueue.cs`, `Orbs/Defs/*.cs` (5) | character-mechanics | `OrbQueue` (`Entities.Orbs/OrbQueue.cs`: values + `Capacity` ≤ 10 `:386`, `TryEnqueue` `:429`, `Insert`, `Remove`, `BeforeTurnEnd` `:463` / `AfterTurnStart` `:477` triggers); `OrbModel` `Passive`, `Evoke`, turn triggers, values through `Hook.ModifyOrbValue`; `OrbRegistry` |
| Enchantments | `Cards/Enchantments/EnchantmentModel.cs`, `Cards/Enchantments/Defs/*.cs` | cards-kernel | 19 virtuals of `Models/EnchantmentModel.cs`; damage/block pre-pass (`Hooks/Hook.cs:10075-10080`, `:10332-10342`); `EnchantPlayCount`; `OnPlay` after the card's; `ShouldStartAtBottomOfDrawPile` (`Combat/CombatManager.cs:4248-4262`); listener right after its card |
| Afflictions | `Cards/Afflictions/AfflictionModel.cs`, `Cards/Afflictions/Defs/*.cs` | cards-kernel | `CardCmd.Afflict` (`ShouldAfflict`, `History.CardAfflicted`), `ClearAffliction`; `OnPlay` after the card's; listener between card and enchantment |
| Modifiers | `Modifiers/ModifierModel.cs` | scaffold (stub only) | `RunState.Modifiers` is empty in standard runs; no modifier is ported (excluded mode) |
| Monster listener | `Monsters/MonsterListener.cs` | monsters-kernel | adapter that routes `AbstractModel` hooks to `MonsterDef` virtuals (`MonsterState.Listener`) |

Pilot content in W1 — the minimum that exercises every new kernel path in a test (W3 ports everything
systematically; a pilot is a complete 1:1 port with citations in the owner's spec, never a sketch):
relics-potions ports ≥6 relics and ≥4 potions covering a counter relic, an `AfterCombatEnd` reset, a
`Niche` reader, the turn-setup prompt relic `GamblingChip`, `FairyInABottle` (`ShouldDie` /
`AfterPreventingDeath`), `PaperPhrog` and `PaperKrane` (consulted by powers), the prompting potion
`AttackPotion`, the hand-prompt potion `Ashwater` and one simple stat potion; cards-kernel ports the prompt
cards the choice tests need (`Armaments`, `Acrobatics`, `Headbutt`, `Seance`, `Discovery`, `SeekerStrike`)
plus one enchantment def and one affliction def; pipeline ports `HellraiserPower`, `ToolsOfTheTradePower`,
`EntropyPower`, `DarkEmbracePower`, `FocusPower`, re-ports `VulnerablePower` / `WeakPower` with the relic
consults (`cs.Player.HasRelic("PAPER_PHROG")`, pet-owner Cruelty fallback) and `ShrinkPower` with real
participants (critique G11); character-mechanics ports the 5 orbs and `Osty` and tests stars / forge
through Cmd calls and test doubles. Cross-kind references are **by id string**, never by type, so no owner
compiles against another owner's pilots.

## B6. Loop and turn flow (`Combat/Loop/`, loop agent)

- `CombatLoop.SetUpCombat` fills the draw pile with combat copies of `Player.Deck` (`CloneForCombat`,
  ids from `cs.AllocateCardInstanceId()`), then the opening shuffle and monster setup; `StartCombat` keeps
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
  `UsePotionAction` target rules `GameActions/UsePotionAction.cs:96-122`.
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
pets as `MonsterState` on the player side with `PetOwnerId` (owner death kills pets,
`Commands/CreatureCmd.cs:2704-2717`); the run streams some monsters read (`Fabricator` MonsterAi,
`ThievingHopper` CombatCardGeneration, `ToughEgg` Niche) come from `cs.Rng`, never from a def
(`MonsterModel.Rng` is `Rng.Chaotic` for canonical models, `Models/MonsterModel.cs:395-404`).

## B9. Character mechanics (`Orbs/*`, `Combat/Pipeline/{OrbCmd,OstyCmd,ForgeCmd,StarsCmd}.cs`, `Monsters/Defs/Osty.cs` — character-mechanics)

Orbs (B5); Osty and pets (`OstyCmd.Summon`, `Commands/OstyCmd.cs:336-395`: `ModifySummonAmount`, alive →
`GainMaxHp`, else revive or create via `PlayerCmd.AddPet<Osty>` + `DieForYouPower`, `SetMaxHp`, `Heal`,
`AfterOstyRevived`, `History.Summoned`, `AfterSummon`); Stars (`PlayerCombatState.Stars` ≥ 0 with the
`StarsModified` history entry; `PlayerCmd.GainStars` / `LoseStars` / `SetStars`, the `ShouldGainStars`
gate, `AfterStarsGained` / `AfterStarsSpent`, `Hook.ModifyStarCost`, `ShouldPayExcessEnergyCostWithStars`
= 2 stars per missing energy, X star costs capture the current stars); Forge (`ForgeCmd.Forge`,
`Commands/ForgeCmd.cs:153-170`; `SovereignBlade` is a card, so character-mechanics tests forge through a
test double and cards-kernel or W3 ports the def).

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
