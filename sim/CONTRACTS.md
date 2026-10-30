# Sts2Sim contracts (P0 walking skeleton)

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
- **Keep the solution building.** Six agents share one build. Never leave a file that does
  not compile. Stubs throw `NotImplementedException`.
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

sealed class CombatSim
    static CombatSim Create(ulong seed, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0,
                            int? currentHp = null, int? maxHp = null)
    static CombatSim Create(RunRngSet rng, IReadOnlyList<string> deckIds, string encounterId, int ascension = 0,
                            int? currentHp = null, int? maxHp = null)   // rng is deep-cloned, never mutated
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
- No card-choice prompts in P0. A card that needs a player choice is out of scope until a
  `ActionKind` for choices is added by the orchestrator.

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
- `PlayerChoiceContext` (card selection prompts) is dropped in P0.
- Multiplayer scaling (`ShouldScaleInMultiplayer`, `GetScaledAmountForMultiplayer`) is out of
  scope: single player only.
- The run layer (events, Neow, map, rewards, relics) is out of scope. Its effect on a combat's
  starting state enters through `Create`'s inputs: the deck (`StartingDecks.IroncladAtRunStart`,
  or the starter-deck rule of §4.2 for Ascender's Bane), HP (`currentHp`/`maxHp`, e.g.
  `StartingDecks.IroncladHpAfterNeowArrival`), and the RNG streams (`Create(RunRngSet, ...)`).
- Sim-only data, kept on purpose: `RunRngSetSnapshot.HashedSeed` (§4.3), `CombatOutcome`,
  `AttackIntent.BaseDamage`, `AttackCommand.HitsCompleted`, `CardInstance.InstanceId`.
- `CombatState.Rng` may be `null` in hand-built test states (§4.4); every combat built by
  `CombatLoop`/`CombatSim` has a `RunRngSet`.
