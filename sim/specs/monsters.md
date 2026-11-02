# Monsters spec (P0): move state machine + `NIBBITS_WEAK`

Owner: monsters agent. Rule truth: decompile tree (game v0.111.0), cited as
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped.

## 1. Encounter choice

| Fact | Citation |
| --- | --- |
| Act 1 is `Overgrowth`: `Index => 0`, `IsDefault => true` (`Underdocks` is also index 0 but `IsDefault => false`) | `Models.Acts/Overgrowth.cs:49,51`; `Models.Acts/Underdocks.cs:44,46` |
| Overgrowth weak pool: `FuzzyWurmCrawlerWeak`, `NibbitsWeak`, `ShrinkerBeetleWeak`, `SlimesWeak`; `NumberOfWeakEncounters => 3` | `Models.Acts/Overgrowth.cs:81,85,89,91,45` |
| First-ever run puts `NibbitsWeak` at normal-encounter index 0 | `Models.Acts/Overgrowth.cs:115` |

Candidates: `SlimesWeak` spawns 3 monsters through the encounter `Rng`
(`Models.Encounters/SlimesWeak.cs:48-62`); `ShrinkerBeetleWeak` applies `ShrinkPower`
(`Models.Monsters/ShrinkerBeetle.cs:155`), outside the shared power set;
`FuzzyWurmCrawlerWeak` and `NibbitsWeak` are both one monster with a deterministic cycle.
Chosen: **`NibbitsWeak`** (one `Nibbit`): it is the first encounter a new player meets, it is
the bench default, it needs only `StrengthPower` (shared set), and it exercises monster
attack, monster block, a self-buff and a `ConditionalBranchState`.

Ported since (2026-10-30): all eight weak encounters of act index 0 — Overgrowth's
`FuzzyWurmCrawlerWeak`, `NibbitsWeak`, `ShrinkerBeetleWeak`, `SlimesWeak` and Underdocks'
`CorpseSlugsWeak`, `SeapunkWeak`, `SludgeSpinnerWeak`, `ToadpolesWeak`
(`Models.Acts/Underdocks.cs:44,71,81,84,87`), specified in `monsters-wurm-beetle.md`,
`monsters-slimes-weak.md`, `monsters-corpseslugs-seapunk.md` and `monsters-underdocks-weak.md`;
`SlimesWeak` draws its composition and `CorpseSlugsWeak` its starters from the encounter `Rng`
(`rng-encounter-private.md` §3).

Ported:

| Sim type | File | Game source |
| --- | --- | --- |
| `NibbitsWeak : EncounterDef`, id `NIBBITS_WEAK` | `Monsters/Encounters/NibbitsWeak.cs` | `Models.Encounters/NibbitsWeak.cs` |
| `Nibbit : MonsterDef`, id `NIBBIT`; `NibbitExtraState` | `Monsters/Defs/Nibbit.cs` | `Models.Monsters/Nibbit.cs` |
| `AiStateDef`, `MoveStateDef`, `RandomBranchStateDef`, `ConditionalBranchStateDef`, `MonsterMoveStateMachineDef`, `MonsterAiRuntime` | `Monsters/MoveStateMachine.cs` | `MonsterMoves.MonsterMoveStateMachine/*.cs` |
| intents | `Monsters/Intents.cs` | `MonsterMoves.Intents/*.cs` |
| `MonsterDef`, `MonsterExtraState`, `EncounterDef`, `MonsterSpawn`, `MonsterSpawner` | `Monsters/MonsterDef.cs` | `Models/MonsterModel.cs`, `Models/EncounterModel.cs`, `Entities.Creatures/Creature.cs`, `Combat/CombatState.cs` |
| `MonsterLifecycle` (monster-side turn entry points for the loop) | `Monsters/MonsterLifecycle.cs` | `Models/MonsterModel.cs`, `Entities.Creatures/Creature.cs`, `Combat/CombatManager.cs` |
| `MonsterCanonicalTags` (0x4Dxx) | `Monsters/MonsterCanonicalTags.cs` | sim-only |

Ids are `StringHelper.Slugify(ClassName)` (`Models/ModelDb.cs:485-488`,
`Helpers/StringHelper.cs:68-73`): `Nibbit` → `NIBBIT`, `NibbitsWeak` → `NIBBITS_WEAK`.
Move ids are the string literals in the model.

## 2. Nibbit constants

`cs.GetValueIfAscension(level, a, b)` = `AscensionHelper.GetValueIfAscension`
(`Helpers/AscensionHelper.cs:14-21`), true when `ascension >= (int)level`
(`Entities.Ascension/AscensionManager.cs:22-24`). `ToughEnemies = 8`, `DeadlyEnemies = 9`
(`Entities.Ascension/AscensionLevel.cs:13-14`). A0 is the fallback value.

| Sim member | A0 | Ascension value | Level | Citation |
| --- | --- | --- | --- | --- |
| `MinInitialHp` | 42 | 44 | ToughEnemies (A8) | `Models.Monsters/Nibbit.cs:281` |
| `MaxInitialHp` | 46 | 48 | ToughEnemies (A8) | `Models.Monsters/Nibbit.cs:283` |
| `ButtDamage` | 12 | 13 | DeadlyEnemies (A9) | `Models.Monsters/Nibbit.cs:285` |
| `SliceBlock` | 5 | 6 | **ToughEnemies (A8)** | `Models.Monsters/Nibbit.cs:287` |
| `SliceDamage` | 6 | 7 | DeadlyEnemies (A9) | `Models.Monsters/Nibbit.cs:289` |
| `HissStrengthGain` | 2 | 3 | DeadlyEnemies (A9) | `Models.Monsters/Nibbit.cs:291` |

Per-instance fields `_isFront`, `_isAlone` (default `false`) → `NibbitExtraState.IsFront/IsAlone`
(`Models.Monsters/Nibbit.cs:277-279,297-321`); cloned and hashed (tag `0x4D10`).

### Move graph (`Models.Monsters/Nibbit.cs:323-347`)

| State | Kind | Intents | Effect (sim call) | Follow-up | Citation |
| --- | --- | --- | --- | --- | --- |
| `INIT_MOVE` | ConditionalBranch, initial state | – | – | alone: `BUTT_MOVE` if `IsAlone`; else `HISS_MOVE` if `!IsFront`, then `SLICE_MOVE` if `IsFront` | `:329-338,346` |
| `BUTT_MOVE` | Move | `SingleAttackIntent(ButtDamage)` | `DamageCmd.Attack(cs, ButtDamage).FromMonster(self).Execute()` | `SLICE_MOVE` | `:326,340`; body `:53-56` |
| `SLICE_MOVE` | Move | `SingleAttackIntent(SliceDamage)`, `DefendIntent` | attack `SliceDamage`, then `CreatureCmd.GainBlock(cs, self, SliceBlock, ValueProp.Move, null)` | `HISS_MOVE` | `:327,339`; body `:226-230,246` |
| `HISS_MOVE` | Move | `BuffIntent` | `PowerCmd.Apply<StrengthPower>(cs, self, HissStrengthGain, self, null)` | `BUTT_MOVE` | `:328,341`; body `:136,152` |

Registration order `INIT_MOVE, BUTT_MOVE, SLICE_MOVE, HISS_MOVE`, initial `INIT_MOVE`
(`:342-346`). The branch *structure* is chosen from `_isAlone` when the graph is built
(`:330`); the branch *conditions* read the live field through the creature at evaluation
time (`:332,336-337`), so the sim lambdas read `self.Extra` at roll time and capture nothing.
`FromMonster` targets all opponents (`Commands.Builders/AttackCommand.cs:781-792`).

Resulting sequence for `NIBBITS_WEAK` (alone): `BUTT, SLICE, HISS, BUTT, ...`. The
Nibbit AI draws no random numbers (no `RandomBranchState`).

`NibbitsWeak.GenerateMonsters`: one mutable `Nibbit` with `IsAlone = true`, slot `null`
(`Models.Encounters/NibbitsWeak.cs:19-24`); `IsWeak => true` (`:15`). The sim returns
`[MonsterSpawn(MonsterRegistry.Get("NIBBIT"), null, m => IsAlone = true)]`. The encounter
`Rng` (`Models/EncounterModel.cs:211-215`) is not consumed by this encounter.

## 3. Move state machine (`MonsterMoves.MonsterMoveStateMachine/`)

The game builds one mutable `MonsterMoveStateMachine` per monster whose state objects also
carry per-object mutable flags. The sim splits it: the graph (`MonsterMoveStateMachineDef`
+ state defs + intents) is built once at spawn and shared by clones; all mutable data lives
in `MonsterAiRuntime` (cloned + hashed, tag `0x4D01`). State defs keep the game's construction
members with the game's accessors: `FollowUpState` and `MustPerformOnceBeforeTransitioning` are
settable, `FollowUpStateId` is init-only (`MoveState.cs:102-127`), plus the branch lists. They are
immutable by convention once the graph is built, because every clone of a combat shares them. The hash covers ids and flags
only (never references), so independently built combats with equal histories hash equal.

| Game member | Sim | Citation |
| --- | --- | --- |
| `MonsterState.ShouldAppearInLogs => true`, `CanTransitionAway => true`, `IsMove => this is MoveState` | `AiStateDef` virtuals | `MonsterState.cs:11,13,15` |
| `MoveState._performedAtLeastOnce` (per state object) | `MonsterAiRuntime.PerformedAtLeastOnce` (set of ids); set at the start of `MoveState.PerformMove`, cleared by `OnExitState` | `MoveState.cs:46,82,171-174` |
| `MoveState.CanTransitionAway` = `MustPerformOnceBeforeTransitioning ? _performedAtLeastOnce : true` | `MoveStateDef.CanTransitionAway(runtime)` | `MoveState.cs:129-139` |
| `MoveState.GetNextState` = `(FollowUpState?.Id ?? FollowUpStateId) ?? throw "No valid followup state."` | same; `FollowUpState` and `FollowUpStateId` kept as two separate members | `MoveState.cs:176-180,112-127` |
| default `MoveState()` = `"UNSET_MOVE"` whose perform throws | `MonsterAiRuntime.NextMove == null` means unset; `PerformMove` throws the same message | `MoveState.cs:145-148,187-191`; `Models/MonsterModel.cs:515-522` |
| `RegisterStates` = `Dictionary.Add(Id, this)` (duplicate id throws `ArgumentException`) | `MonsterMoveStateMachineDef` ctor uses `Dictionary.Add` | `MoveState.cs:182-185`, `RandomBranchState.cs:196-199`, `ConditionalBranchState.cs:85-88`, `MonsterMoveStateMachine.cs:32-46` |
| ctor: `_currentState = initial`; log it when `ShouldAppearInLogs` | `MonsterAiRuntime(machine)`; the initial state is held by reference (it need not be registered) | `MonsterMoveStateMachine.cs:47-52` |
| `RollMove`: `FindNextMoveState(logMove: true)`; non-move current → throw `"<id> is not a valid move state"`; return current | `MonsterAiRuntime.RollMove(cs, owner, rng)`; also stores `NextMove` (game: `MonsterModel.RollMove`) | `MonsterMoveStateMachine.cs:55-64`; `Models/MonsterModel.cs:719-722` |
| `FindNextMoveState`: return early if `!CanTransitionAway` or (`!_performedFirstMove` and current is a move); loop `GetNextState` → empty id means initial state, unknown id throws `"no valid state found: <id>"` → `SetCurrentState`; remember the first entered state with `ShouldAppearInLogs`; until a move; append it to `StateLog` | `MonsterAiRuntime.FindNextMoveState` | `MonsterMoveStateMachine.cs:76-104` |
| `SetCurrentState`: `old.OnExitState()` then `new.OnEnterState()` (only `MoveState.OnExitState` does anything) | `SetCurrentState` removes the exited move's id from `PerformedAtLeastOnce` | `MonsterMoveStateMachine.cs:106-111` |
| `ForceCurrentState`, `OnMovePerformed` (`_performedFirstMove = true`) | same names | `MonsterMoveStateMachine.cs:66-74` |
| `StateLog : List<MonsterState>` | `List<string>` of ids; reference comparisons become ordinal id comparisons (ids are unique per machine; a forced `STUNNED` state is never logged) | `MonsterMoveStateMachine.cs:26-30` |
| `ConditionalBranchState.GetNextState`: first branch whose `Evaluate() > 0` (`null` condition → 1); none → throw `"No valid next state found."`; consumes no rng | `ConditionalBranchStateDef` | `ConditionalBranchState.cs:23-30,56-83` |
| `RandomBranchState.AddBranch` overloads (10; `CanRepeatXTimes` through the repeat-type overload throws `ArgumentException`) | same overload set (two collapsed through default `weight = 1f`) | `RandomBranchState.cs:60-128` |
| `RandomBranchState.GetNextState`: `max = States.Sum(GetStateWeight)`; `num = rng.NextFloat(max)`; walk branches `num -= w; if (num <= 0f) return id`; else throw | `RandomBranchStateDef.ResolveNextState` | `RandomBranchState.cs:130-155` |
| `GetStateWeight` (repeat rules + cooldown, see below) | `RandomBranchStateDef.GetStateWeight` (public static, tested directly) | `RandomBranchState.cs:157-194` |
| `MonsterModel.RollMove` uses `RunRng.MonsterAi` | `MonsterLifecycle.RollMove` passes `cs.Rng.MonsterAi` | `Models/MonsterModel.cs:721`; `Runs/RunRngSet.cs:49` |

Weight rules (`RandomBranchState.cs:157-194`), all in `float`:
- `UseOnlyOnce`: 0 if the state is anywhere in `StateLog`, else 1.
- `CannotRepeat` (= max 1) / `CanRepeatXTimes(n)`: 1 if `StateLog.Count < n`; else 0 unless one of
  the last `n` log entries differs from this state, then 1.
- `CanRepeatForever`: 1.
- `cooldown > 0`: if this state id is among the last `cooldown` *move* entries of `StateLog`,
  the weight is 0 regardless of the above.
- result `num * weight()` (weight lambda; a null lambda throws `"<id> doesn't have a weight"`).

Arithmetic fidelity: `Enumerable.Sum(Func<T,float>)` accumulates in `double` and casts the
total to `float`; the sim sums in `double` and casts. `Rng.NextFloat(max)` =
`(float)(NextDouble() * (double)(max - 0f) + 0f)` (`Random/Rng.cs:124-138`). Consequences kept
on purpose: one `MonsterAi` draw per evaluated random branch, even when only one branch has
weight; when every weight is 0, `NextFloat(0) == 0` and the **first** branch is returned
(tested).

## 4. Spawn and HP roll

`MonsterSpawner.Spawn(cs, spawn)` mirrors `CombatState.CreateCreature`
(`Combat/CombatState.cs:709-724`), steps 1–5 below. Step 6, `MonsterModel.SetUpForCombat`
(`Models/MonsterModel.cs:713-717`), is a separate call, `MonsterLifecycle.SetUpForCombat`, because
the game runs it later (2026-10-30 fidelity fix):

1. `CreateExtraState()` and `Configure` (the game sets encounter fields such as `IsAlone`
   on the mutable model before `CreateCreature`, `Models.Encounters/NibbitsWeak.cs:22-23`).
2. Allocate `CombatId` (`AttachCreature`, `Combat/CombatState.cs:726-731`; the game attaches
   after the roll, which draws nothing else, so the order is not observable).
3. `Creature` ctor: throw if `min > max`; HP = `MaxInitialHp` (`Entities.Creatures/Creature.cs:946-962`).
4. `SetUniqueMonsterHpValue(_enemies, RunState.Rng.Niche)` (`Combat/CombatState.cs:717`,
   `Entities.Creatures/Creature.cs:972-984`): candidates = `[min, max]` minus the `MaxHp` of
   every other creature already in `_enemies`; candidates non-empty →
   `rng.NextItem(set)` = `set[NextInt(0, count)]` (`Random/Rng.cs:213-223`), with the set in
   ascending order (`HashSet<int>` built from `Enumerable.Range`, then `ExceptWith`: .NET
   enumerates entries in insertion order skipping removed ones); empty →
   `rng.NextInt(min, max + 1)`. Result written to `MaxHp`, `CurrentHp` and
   `MaxHpBeforeModification`. **Stream: `Niche`, one draw per spawn.**
5. `ScaleMonsterHpForMultiplayer`: no-op for one player (`Entities.Creatures/Creature.cs:986-993`).
6. `MonsterLifecycle.SetUpForCombat(cs, m)`: build the graph (`GenerateMoveStateMachine`,
   `InitAi`) and `SpawnedThisTurn = true`. `SpawnedThisTurn` is stored on `MonsterState` (state
   agent's field); the runtime does not duplicate it. The game calls it from
   `CombatManager.AddCreature` (`Combat/CombatManager.cs:6998`), inside `SetUpCombat`'s loop over
   every creature (`:6613-6625`). That loop runs after all encounter monsters exist
   (`Rooms/CombatRoom.cs:381-394` precede `:417`) and after the opening shuffle (`:6599-6611`). The sim
   calls it at the same point (`CombatLoop.SetUpCombat`, loop spec §2.3), so a graph that inspects
   other enemies or slots, or draws randomness, sees the same combat as in the game.

`Spawn` does not add the monster to `cs.Monsters`. Caller contract (game order,
`Rooms/CombatRoom.cs:381-394`): create and add each monster before creating the next, so
the uniqueness set sees earlier monsters. `MonsterSpawner.SpawnEncounter(cs, encounter)`
does exactly that. Spawning happens before the combat's turn state exists (loop spec §2.2,
window A).

`MonsterDef.MinInitialHp(cs, self)` / `MaxInitialHp(cs, self)` are additive virtual overloads
(default: the contract's `(cs)` versions) for monsters whose HP reads instance fields
(e.g. `+ RespawnMaxHpBonus` in some models); `Spawn` calls them after `Configure`.

## 5. Monster-side turn entry points (`MonsterLifecycle`)

For the loop agent; each mirrors one game method, synchronous.

| Sim | Game | Citation |
| --- | --- | --- |
| `SetUpForCombat(cs, m)`: `InitAi(new MonsterAiRuntime(GenerateMoveStateMachine(cs, m)))`, `SpawnedThisTurn = true` | `MonsterModel.SetUpForCombat` via `CombatManager.AddCreature` | `Models/MonsterModel.cs:713-717`; `Combat/CombatManager.cs:6990-7005` |
| `AfterCreatureAdded(cs, m)`: `Def.AfterAddedToRoom` (enemy side only), then `RollMove` iff `cs.CurrentSide == Player`. Runs during setup (`IsStarting`, not `IsInProgress`): `IsOverOrEnding`-gated commands such as `GainBlock` do nothing there, `PowerCmd.Apply` and guarded hooks work (loop spec §2.2; e.g. `CubexConstruct` gets its Artifact 1 but not its 13 block, `Models.Monsters/CubexConstruct.cs:104`, `:115`) | `CombatManager.AfterCreatureAdded` + `Creature.AfterAddedToRoom` | `Combat/CombatManager.cs:509-543`; `Entities.Creatures/Creature.cs:67-69` |
| `RollMove(cs, m)` | `MonsterModel.RollMove` | `Models/MonsterModel.cs:719-722` |
| `PrepareForNextTurn(cs, m, rollNewMove = true)` | `Creature.PrepareForNextTurn` | `Entities.Creatures/Creature.cs:1128-1139` |
| `TakeTurn(cs, m)`: throw unless enemy side; act only if `!SpawnedThisTurn` | `Creature.TakeTurn` | `Entities.Creatures/Creature.cs:279-283,1297` |
| `PerformMove(cs, m)`: `IsPerformingMove = true` → move = `NextMove` → targets = player creatures → mark `PerformedAtLeastOnce` → perform → `OnMovePerformed` → `IsPerformingMove = false` → if dead and `Hook.ShouldCreatureBeRemovedFromCombatAfterDeath` remove from `cs.Monsters` | `MonsterModel.PerformMove`, `MoveState.PerformMove` | `Models/MonsterModel.cs:234-291`; `MoveState.cs:46-48` |
| `OnSideSwitch(m)`: `SpawnedThisTurn = false` | `Creature/MonsterModel.OnSideSwitch` | `Entities.Creatures/Creature.cs:1284-1294`; `Models/MonsterModel.cs:774-778` |
| `SetMoveImmediate(m, state, force)`: if `NextMove.CanTransitionAway` (unset counts as true) or `force` → `NextMove = state`, `ForceCurrentState(state)` | `MonsterModel.SetMoveImmediate` | `Models/MonsterModel.cs:724-736` |
| `StunInternal(cs, m, perform, nextMoveId)`: if attached and alive: default `nextMoveId` = last `StateLog` id (empty log throws); `NextMove` = new `STUNNED` move (`Intents.Stun`, `FollowUpStateId = nextMoveId`, `MustPerformOnceBeforeTransitioning = true`) via `SetMoveImmediate` | `Creature.StunInternal` | `Entities.Creatures/Creature.cs:1105-1126`; `Models/MonsterModel.cs:332` |
| `IntendsToAttack(m)`: any `Attack`/`DeathBlow` intent on `NextMove` | `MonsterModel.IntendsToAttack` | `Models/MonsterModel.cs:524-528` |
| `PlayerCreatures(cs)` = `[cs.Player]` | `CombatState.PlayerCreatures` | `Combat/CombatState.cs:560` |

Where the game calls them (loop agent's sequence):
- room entry: per encounter monster `CreateCreature` + `AddCreature` (`Rooms/CombatRoom.cs:381-394`);
  `SetUpCombat` → `CombatManager.AddCreature` → `SetUpForCombat` for every creature
  (`Combat/CombatManager.cs:6612-6624,6998`).
- combat start: `AfterCreatureAdded` for every creature (`Combat/CombatManager.cs:4469`)
  **before** `Hook.BeforeCombatStart` (`:4491`) → this is the first `RollMove`.
- every player-side `StartTurn` (not extra turns): `PrepareForNextTurn` for every enemy
  (`Combat/CombatManager.cs:5315-5323`). On round 1 this re-roll is a no-op because no move
  has been performed yet (`MonsterMoveStateMachine.cs:84`).
- enemy turn: iterate a snapshot `ToList(Enemies)` (`Combat/CombatManager.cs:3263`), skip
  monsters no longer in combat (`:3160`), `TakeTurn` (`:3172`), then win check.
- `SwitchSides` → `OnSideSwitch` for every creature (`Combat/CombatManager.cs:7436,7494`).

## 6. Intents (`MonsterMoves.Intents/`)

| Sim | Game | Citation |
| --- | --- | --- |
| `AttackIntent(int)` stores `DamageCalc = (_, _) => damage` (and the sim-only `BaseDamage`, 0 for an intent built from a `Func`; the game keeps the value only in the closure) | `SingleAttackIntent(int)`, `MultiAttackIntent(int, ..)` | `SingleAttackIntent.cs:14-17`; `MultiAttackIntent.cs:18-28` |
| `GetSingleDamage`: `num = DamageCalc()`; if the owner is attached (stands for `LocalContext.GetMe(owner.CombatState) != null`), `num = Hook.ModifyDamage(cs, target: cs.Player, dealer: owner, DamageCalc(), ValueProp.Move, null, null, All)` with a **second** `DamageCalc()` call as in the game; `Math.Max(0, (int)num)`; `targets` unused, as in the game (single player: `LocalContext.GetMe` = the player) | `AttackIntent.GetSingleDamage` | `AttackIntent.cs:92-101` |
| `GetRepeats`: single 1, multi `_repeatCalc?.Invoke() ?? _repeat`; `GetTotalDamage`: single = single damage, multi = single × repeats | `Repeats`, `GetTotalDamage` | `AttackIntent.cs:33`; `SingleAttackIntent.cs:10,24-27`; `MultiAttackIntent.cs:16,30-33` |
| `DeathBlowIntent : SingleAttackIntent` with type `DeathBlow` | `DeathBlowIntent` | `DeathBlowIntent.cs:10,16,29` |
| `DebuffIntent(strong)` → `DebuffStrong`/`Debuff`; `StatusIntent(count)`; `SimpleIntent` singletons in `Intents` for Defend/Buff/Stun/... | intent classes | `DebuffIntent.cs:5-25`; `StatusIntent.cs:10,25`; `DefendIntent.cs:5`; `BuffIntent.cs:5`; `StunIntent.cs:18` |

`Hook.ModifyDamage(..)` (pipeline) is side-effect free (modifiers are only collected), so
intent previews do not disturb state. The scaffold's `Intents.DeathBlow` simple intent was
removed: a death-blow intent must carry damage.

## 7. Stripped sites (Godot / VFX / SFX / coroutines)

All `async` moves become synchronous calls in the same order; nothing below draws from a
rule RNG stream or changes state.

| Site | Game | Transformation |
| --- | --- | --- |
| `.WithAttackerAnim("Attack", 0.15f).WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_attack_slash")` on BUTT/SLICE | `Models.Monsters/Nibbit.cs:53-55,226-228` | dropped; `Execute(null)` → `Execute()` |
| `CreatureCmd.TriggerAnim(Creature, "Cast", 0.6f)` before the Strength apply | `Models.Monsters/Nibbit.cs:136` | dropped (await on an animation; ordering unchanged) |
| `new ThrowingPlayerChoiceContext()` argument | `Models.Monsters/Nibbit.cs:152` | dropped (choice context) |
| `GenerateAnimator`, `ShouldShowMoveInBestiary`, `DeathSfx`, `TakeDamageSfxType` | `Models.Monsters/Nibbit.cs:293-295,388-409` | not ported (UI only) |
| `Cmd.CustomScaledWait(0.1f, 0.2f)` / `(0.1f, 0.4f)` around the move | `Models/MonsterModel.cs:237,292` | dropped |
| `Log.Info(...)` in `PerformMove`; `CombatManager.Instance.History.MonsterPerformedMove(...)` | `Models/MonsterModel.cs:270-273,286` | dropped; combat history is not modelled in P0 (no ported rule reads it) |
| `NCreature.RefreshIntents()` in `PrepareForNextTurn` / `SetMoveImmediate` | `Entities.Creatures/Creature.cs:1135-1138`; `Models/MonsterModel.cs:730-734` | dropped |
| `StateTracker.Subscribe`, `CreaturesChanged` events | `Combat/CombatManager.cs:7003-7004`; `Combat/CombatState.cs:1007` | dropped |
| `_encounter?.OnCreatureSpawned(creature)` (gold-reward bookkeeping) | `Combat/CombatState.cs:722`; `Models/EncounterModel.cs:343-359` | dropped (rewards out of scope) |

## 8. Deferred / not ported

- Per-monster `MonsterModel.Rng`, encounter `Rng` and `SortEnemiesBySlotName`: ported 2026-10-30
  (`rng-encounter-private.md` §3-§5). The encounter `Rng` is seeded through `CombatRunContext`
  (seeding scheme, `TotalFloor`); the per-monster `Rng` is a seam
  (`MonsterDef.UsesPrivateRng`/`InitPrivateRng`) that no P0 monster uses — the bundle path passes
  no map coordinate and the v0.107.1 formula is not ported (`rng-encounter-private.md` §8); the
  setup slot sort runs after each slotted monster's `SetUpForCombat`.
- Multiplayer HP scaling, bestiary, `GetAllMoves`.
- `CombatManager.RemoveCreature` (`Combat/CombatManager.cs:7171-7180`, called by `Kill` at
  `Commands/CreatureCmd.cs:2817` even while the monster is performing its move) runs
  `MonsterModel.BeforeRemovedFromRoom()` and `ResetStateMachine()` (`Models/MonsterModel.cs:609-611`,
  `:693-696`). Neither exists in the sim: a monster killed during its own move still gets
  `OnMovePerformed` afterwards (the game skips it because the machine is gone,
  `Models/MonsterModel.cs:285`), and its AI state survives until `PerformMove` removes it. Not
  observable in P0; matters for monsters that override `BeforeRemovedFromRoom`.
- Monsters as hook listeners (`MonsterModel.ShouldReceiveCombatHooks => true`): Nibbit
  overrides no hook.
- `EncounterDef` carries only `Id`, `IsWeak` and `GenerateMonsters`. `Tags` (`NibbitsWeak`:
  `[EncounterTag.Nibbit]`, `Models.Encounters/NibbitsWeak.cs:11`), `RoomType` (`:13`),
  `AllPossibleMonsters` (`:17`), `Slots`/`HasScene` and the gold bounds (`Models/EncounterModel.cs:45-81`,
  `:131-133`) are run-level data with no combat-rule effect in P0.
- `UNSET_MOVE` path: the game's `PerformMove` marks the `UNSET_MOVE` object performed before
  its perform throws (`Models/MonsterModel.cs:267-274`; `MoveState.cs:46-48`, `:187-191`); the sim
  throws the same message from `NextMove == null` before marking anything. Only the exception
  path differs.

## 9. Tests (`sim/Sts2Sim.Tests/MonsterTests.cs`, filter `Monsters`)

`Rng` is sealed, so "scripted" draws are reproduced with a probe `Rng` built from the same
seed (or `DeepClone()` of the live stream) and replayed through the ported formula.

- ids/registry: slugified ids, `MONSTER/ENCOUNTER` registry lookups, spawn list (`IsAlone`, slot, `SpawnedThisTurn`, initial state).
- HP: bounds per ascension 0-10; roll equals `min + NextInt(0,5)` on the shared stream for
  200 seeds (every value reached, exactly one draw); with real `RunRngSet` only `Niche`
  advances; uniqueness against an existing enemy; full-range fallback when all values are taken.
- move values per ascension 0-10 (A8 block / A9 damage split), graph shape, intents.
- AI: alone cycle `BUTT, SLICE, HISS, ...` with zero `MonsterAi` draws and the round-1
  re-roll guard; not-alone openers by `IsFront`; conditions read live extra state; no
  initial roll on the enemy side; random-branch weight table (CannotRepeat,
  CanRepeatXTimes(2), UseOnlyOnce, cooldown 2); 40 seeds × 60 rolls never violate the rules
  and draw exactly one `NextFloat` per roll; the pick equals a replayed weighted draw for 300
  seeds; all-zero weights pick the first branch; construction errors (duplicate id,
  `CanRepeatXTimes` via repeat-type overload, dangling follow-up).
- stun: `STUNNED` holds until performed, then resumes the last logged move; forced vs
  unforced `SetMoveImmediate`.
- `PerformMove`: the move runs with `IsPerformingMove` set and the player as target; a
  monster that died during its own move leaves `cs.Monsters` afterwards; `TakeTurn` rejects
  a non-enemy side.
- clone/hash: runtime clone independence, canonical hash tracks state, extra state, full
  `MonsterState.DeepClone`.
- integration (needs pipeline/loop): intent preview with Strength; BUTT/SLICE/HISS damage,
  block and Strength at A0 and A10; `CombatSim.Create("NIBBITS_WEAK")` starts with BUTT and
  cycles BUTT, SLICE, HISS, BUTT over four `EndTurn`s (player loses 12 + 6 + 14).

## 10. Integration notes (2026-10-28)

- Loop wiring requested in §5 is in place and was checked against the decompile:
  `CombatLoop.SetUpCombat` spawns through `MonsterSpawner.SpawnEncounter` (create and add one monster
  at a time, `Rooms/CombatRoom.cs:381-394`), then, after the opening shuffle, calls
  `MonsterLifecycle.SetUpForCombat` for every monster (`Combat/CombatManager.cs:6613-6625`; moved out of
  `Spawn` on 2026-10-30, §4); `StartCombat` calls `AfterCreatureAdded` for every monster
  before `Hook.BeforeCombatStart` (`Combat/CombatManager.cs:4466-4491`, body `:509-543`);
  `StartTurn` calls `PrepareForNextTurn` for every enemy on non-extra player turns, after
  `Hook.BeforeSideTurnStart` and before `Creature.AfterTurnStart` (`:5291-5323`, which runs at label
  `IL_024d`, before `IL_0433`); `ExecuteEnemyTurn` iterates a `ToList` snapshot of the enemies
  (`:3263`), skips creatures no longer in the combat (`:3160`), calls `TakeTurn` (`:3172`), then the
  win check (`:3219`); `SwitchSides` calls `OnSideSwitch` for every creature (`:7494`).
- `CreatureCmd.Kill` honours `IsPerformingMove` (pipeline spec §5), so the end-of-move removal in
  `PerformMove` is the only removal for a monster that dies during its own move.
- `EndToEndTests` (integration spec) plays the real `NIBBITS_WEAK` encounter against an independent
  reference model at A0 and A10, which re-checks the HP roll, the alone cycle, the move values, SLICE
  block timing (gained on the enemy turn, cleared at the next enemy turn start) and the Strength
  stacking end to end.

## 11. W1 monsters kernel (CONTRACTS Part B §B8, §B12 A15, A17, A21; 2026-11-01)

Owner: monsters-kernel (Part B §B11). Base `263810d` (phase 0b). Rule truth: decompile v0.111.0, cited as
`<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped; async methods were traced through their state machines.
This section supersedes §5 (rows `PerformMove`, `StunInternal`), §7 (row `History.MonsterPerformedMove`) and the §8
bullets on `CombatManager.RemoveCreature`, monster listeners, the per-monster `Rng` seam and the `UNSET_MOVE` path.

### 11.1 Files

| File | Contents |
| --- | --- |
| `Monsters/MonsterDef.cs` | `MonsterExtraState`; `PrivateRngExtraState` (new, §11.10); `MonsterDef` with the W1 virtuals (§11.2); `MonsterSpawn`; `EncounterDef` + `GetNextSlot` (§11.5) |
| `Monsters/MonsterSpawner.cs` (new; `MonsterSpawner` moved here from `MonsterDef.cs`, P0 members unchanged) | `CreateCreature`, `AddToCombat`, `SpawnForAdd`, `SpawnPet`, `AddPet`, `AddPetInternal`, `EncounterOf`, `SetEnemyIndex`; P0 `Spawn`, `SpawnEncounter`, `CreateEncounterRng`, `SortEnemiesBySlotName`, `IndexOfSlot`, `RollUniqueMonsterHp` |
| `Monsters/MonsterListener.cs` (new) | the hook adapter (§11.3) |
| `Monsters/MonsterLifecycle.cs` | listener install in `SetUpForCombat`; `PerformMove` (§11.9); `RemoveCreature`, `ResetStateMachine`, `RemoveFromCombatState`, `HasMoveStateMachine` (§11.8); `InvokeDiedEvent`, `OnPetDied`, `GetPet`, `GetPets` (§11.6); `IsInCombat`; the participant helpers (§11.7); the other P0 entry points keep their Part A signatures |
| `Monsters/MoveStateMachine.cs` | `MonsterAiRuntime.IsMachineReset` (cloned, hashed through a variant tag), `ResetMachine`, `RequireMachine` guards (§11.8) |
| `Monsters/MonsterCanonicalTags.cs` | every monster tag, including the two moved out of content files (§11.11) |
| `Monsters/Defs/Toadpole.cs`, `Monsters/Defs/CorpseSlug.cs` | tag constants replaced by `MonsterCanonicalTags` (values unchanged) |
| `Sts2Sim.Tests/MonsterKernelTests.cs` (new) | §11.14 |

### 11.2 `MonsterDef` virtuals

`MonsterModel` overrides only `ShouldReceiveCombatHooks => true` (`Models/MonsterModel.cs:350`) and inherits every other
hook default from `AbstractModel`. The rule-relevant `MonsterModel` virtuals (`kernel-gaps.json → model_kind_virtuals.MonsterModel`)
and the six `AbstractModel` hooks that v0.111.0 monsters override (`hooks.json → overridden_by_kinds.monsters`; checked by a
grep over `Models.Monsters/*.cs`) become `MonsterDef` virtuals. Defaults are the game's (`Task.CompletedTask` → empty body).
Signatures: the sim hook signature (Part B §B3, scaffold M1: `RunState rs` for both-scope hooks, `CombatState cs` for combat
scope) with the monster inserted second as `MonsterState self`, the convention of the P0 `AfterAddedToRoom(cs, self)`.

| `MonsterDef` virtual | Game | Overriders in v0.111.0 |
| --- | --- | --- |
| `AfterAddedToRoom(cs, self)` (P0) | `Models/MonsterModel.cs:604-607` | 61 files |
| `BeforeRemovedFromRoom(cs, self)` | `:609-611` (`void`, no parameters) | `CubexConstruct.cs:657`, `KnowledgeDemon.cs:643`, `Ovicopter.cs:543`, `OwlMagistrate.cs:489`, `Queen.cs:803`, `Rocket.cs:723`, `SnappingJaxfruit.cs:215`, `SoulNexus.cs:386`, `TestSubject.cs:1239`, `ThievingHopper.cs:945`, `WaterfallGiant.cs:1108` (all `Models.Monsters/`) |
| `AfterCardChangedPilesLate(rs, self, card, oldPileType, clonedBy)` | `Models/AbstractModel.cs:253` (scope both) | `SoulFysh.cs:777` |
| `AfterCardGeneratedForCombat(cs, self, card, creator)` | `:278` (combat) | `Aeonglass.cs:624` |
| `AfterCurrentHpChanged(rs, self, creature, delta)` | `:343` (both) | `Crusher.cs:800`, `Rocket.cs:693` |
| `AfterDamageReceived(rs, self, target, result, props, dealer, cardSource)` | `:358` (both; `choiceContext` dropped) | `LagavulinMatriarch.cs:808` |
| `BeforeDeath(rs, self, creature)` | `:368` (both) | `Crusher.cs:810`, `Rocket.cs:703` |
| `AfterDeath(rs, self, creature, wasRemovalPrevented)` | `:373` (both; `choiceContext`, `deathAnimLength` dropped, D12) | `Aeonglass.cs:559`, `DecimillipedeSegment.cs:901` (and its three subclasses), `KinPriest.cs:451`, `LagavulinMatriarch.cs:826`, `Queen.cs:936`, `SoulFysh.cs:767`, `TestSubject.cs:1279`, `TheInsatiable.cs:592`, `Vantom.cs:795`, `WaterfallGiant.cs:1093` |
| `UsesPrivateRng`, `InitPrivateRng(cs, self, rng)` (seam of `rng-encounter-private.md` §4.2) | `Models/MonsterModel.cs:396-411` | none (§11.10); the default now stores the `Rng` in the monster's extra state |

Not ported, with the reason: `ShouldDisappearFromDoom` (`:460`) and `OnDieToDoom` (`:780-782`) are VFX only (critique C15;
pinned by a test); `Title`, `IsHealthBarVisible`, `ExtraDeathVfxPadding`, `HpBarSizeReduction`, `VisualsPath`, `AssetPaths`,
the SFX members, `HasPhobiaSpineSkin`, `ShouldFadeAfterDeath`, `HurtAnimationTrackOffsetForDoom`, `ShouldShowInCompendium`,
`DeathAnimLengthOverride`, `CanChangeScale`, `GenerateBestiaryMoveList`, `ShouldShowMoveInBestiary`, `SetupSkins`,
`GenerateAnimator` (`:352-474`, `:613-691`, `:751-772`) are UI, bestiary or animation. `ShouldReceiveCombatHooks` has no reader
outside its declarations. `Creature.Died` subscriptions in `SlumberingBeetle.cs:137` and `SoulNexus.cs:69` only stop VFX
(`SlumberingBeetle.cs:450-455`, `SoulNexus.cs:380-384`); the one rule-relevant `Died` subscriber is the pet list (§11.6).

### 11.3 `MonsterListener`

The game puts each monster creature's `MonsterModel` into the combat listener list right after that creature's powers, allies
before enemies (`Combat/CombatState.cs:859-867`), and keeps it while `Creature.CombatState != null` (`:1047`), so a dead monster
still in `_enemies` and a dead pet (pets never leave `_allies`, §11.6) stay listeners. `MonsterListener : AbstractModel,
IOwnedListener<MonsterState>` stores only its owner (`readonly MonsterState _owner`; `Def` reads `Owner.Def`) and overrides
exactly the six hooks of §11.2, each forwarding to the def virtual with `self = Owner` and the arguments unchanged. Every other
hook keeps the `AbstractModel` default, so per-type hook masks (Part B §B3) visit it only for those six dispatchers.

- Install: `MonsterLifecycle.SetUpForCombat` (the game's `MonsterModel.SetUpForCombat`, `Models/MonsterModel.cs:713-717`, called
  from `CombatManager.AddCreature`, `Combat/CombatManager.cs:6998`) sets `MonsterState.Listener = new MonsterListener(monster)`
  after the AI runtime. The game's model is a listener from list insertion on; installing at `SetUpForCombat` is equivalent
  because nothing dispatches one of the six hooks in between: at setup the encounter monsters are inserted at room entry and set
  up in `SetUpCombat` after the opening shuffle, which moves cards with `AddInternal` and raises no hook
  (`Entities.Players/Player.cs:1214-1233`, `Combat/CombatManager.cs:6599-6625`); mid-combat, `CreatureCmd.Add` runs
  `CombatManager.AddCreature` right after `CombatState.AddCreature` (`Commands/CreatureCmd.cs:279-280`).
- Clone (scaffold M19, §14 item 2): `MonsterState`'s copy constructor calls `IOwnedListener<MonsterState>.CloneFor(clone)`
  before it copies the clone's powers and AI (`State/CreatureState.cs:558-559`); `CloneFor` therefore only stores the new owner
  and reads nothing. Canonical: the listener carries no state, so `MonsterState` writes only its presence (B12 A22);
  `Hash(x) == Hash(Clone(x))` and the clone's listener routes to the cloned monster (tests).
- Liveness and order belong to the hooks owner's listener snapshot (§11.13 row 1).

### 11.4 Mid-combat spawn path

`CreatureCmd.Add(MonsterModel, cs, side, slot)` (`Commands/CreatureCmd.cs:3564-3572`, state machine `:144-215`) is
`CombatState.CreateCreature` then `CreatureCmd.Add(Creature)` (`:3575-3585`, state machine `:219-330`). The sim splits it the
same way:

| Sim | Game | Steps |
| --- | --- | --- |
| `MonsterSpawner.CreateCreature(cs, def, side, slotName[, configure, context])` | `Combat/CombatState.cs:709-724`, `Creature(MonsterModel, side, slot)` `Entities.Creatures/Creature.cs:946-962`, `SetUniqueMonsterHpValue` `:972-984` | `CreateExtraState`; allocate the combat id; `Side = side`; `configure` (the game sets mutable-model fields before `CreateCreature`, e.g. `Models.Powers/InfestedPower.cs:83`); throw when min > max (`Creature.cs:951-954`); HP = `MaxInitialHp` (`:955-961`); **only `side == Enemy`**: unique HP roll on `Niche` over the other enemies (`CombatState.cs:715-719`), written to `MaxHp`, `CurrentHp` and `MaxHpBeforeModification` (`Creature.cs:983`); `ScaleMonsterHpForMultiplayer` is the identity for one player (`:986-993`); then the private `Rng` for an opt-in def (`CombatState.cs:721`, §11.10). The id is allocated before the roll, the game attaches after it (`:720`); the roll reads no id, so the order is not observable (§4 step 2) |
| `MonsterSpawner.AddToCombat(cs, monster[, encounter])` | `CreatureCmd.Add(Creature)` | throw `"Attempted to add a creature outside of combat."` unless `cs.IsInProgress` (`Commands/CreatureCmd.cs:268-271`); `CombatState.AddCreature` (`Combat/CombatState.cs:993-1008`): throw `"Creature is already in this combat, but AddCreature was called on it again."` when the creature is in either list, then `cs.AddAlly` for the player side, else `cs.AddMonster`; `CombatManager.AddCreature` (`Combat/CombatManager.cs:6990-7005`): `MonsterLifecycle.SetUpForCombat`, then `SortEnemiesBySlotName` when `SlotName != null` (`:6999-7002`); `MonsterLifecycle.AfterCreatureAdded` (`:7007-7010`, state machine `:495-556`: `AfterAddedToRoom` for the enemy side `Entities.Creatures/Creature.cs:67-69`, `RollMove` when `IsEnemy && CurrentSide == Player` `Combat/CombatManager.cs:540-543`); `PrepareForNextTurn(rollNewMove: false)` when `CurrentSide != Enemy` (`Commands/CreatureCmd.cs:298-301`; no rule effect, it only refreshes intents, `Entities.Creatures/Creature.cs:1128-1139`); `Hook.AfterCreatureAddedToCombat(cs, monster)` (`Commands/CreatureCmd.cs:308`) |
| `MonsterSpawner.SpawnForAdd(cs, def, side, slotName[, configure[, encounter]])` | `CreatureCmd.Add(MonsterModel, …)` | `CreateCreature` then `AddToCombat`. So the id and the `Niche` draw are consumed before the in-progress check throws, as in the game (`:178-179` before `:268`) |

`AddToCombat(cs, monster)` resolves the encounter with `MonsterSpawner.EncounterOf(cs)` (registry entry for `cs.EncounterId`,
null when unregistered, like the game's `Encounter == null`); the overloads with an explicit `EncounterDef` serve combats built
from an unregistered def. Not ported, with the reason: `NCombatRoom.AddCreature` (UI), `StateTracker.Subscribe` and
`CreaturesChanged` (UI/network); the map-point history `MonsterIds` append (`Commands/CreatureCmd.cs:302-307`; read only by the
bestiary, the run-history screen and progress saves, `Nodes.Screens.Bestiary/*`, `Saves.Managers/ProgressSaveManager.cs:306`;
W2 run history); `EncounterModel.OnCreatureSpawned` (`Combat/CombatState.cs:722`, `Models/EncounterModel.cs:343-359`; feeds the
gold-reward proportion `:324-327`, W2 rewards). Not representable: the attachment checks `"Attempted to add a creature with no
combat state."` (`Commands/CreatureCmd.cs:273-276`) and `"Creature was created for a different combat."`
(`Combat/CombatState.cs:997-1000`) — a sim creature is attached by being in a list of the `cs` that created it; the
`"CombatState must already contain creature."` check (`Combat/CombatManager.cs:6994-6997`) cannot fail right after the add and
is omitted.

### 11.5 Slots

- `EncounterDef.GetNextSlot(cs)` = `Models/EncounterModel.cs:195-198`: the first slot (in `Slots` order) that no creature in
  `cs.Monsters` holds (ordinal), else `string.Empty`. An empty-string slot is non-null, so a summon placed there still triggers the
  slot sort (with index -1).
- `MonsterSpawner.SetEnemyIndex(cs[, encounter], creature, index)` = `Combat/CombatState.cs:966-980`, no caller in v0.111.0 rule
  code (`Combat/ICombatState.cs:84` declares it): throw `InvalidOperationException("Cannot modify turn order of a combat with
  pre-set slots")` when `Slots` is non-empty (`:970-973`); throw `ArgumentException("Creature must be a valid enemy to change its
  turn order.")` when the creature is not in `Enemies` (`:974-977`); `Remove`, then `Insert(Math.Min(index, Count - 1))` with
  `Count` read after the removal (`:978-979`): an index at or past the end puts the creature second to last, never last, and with
  a single enemy the insert at -1 throws `ArgumentOutOfRangeException` after the removal. A null encounter throws
  `ArgumentNullException` (the game dereferences `Encounter` without a check, a `NullReferenceException`).

### 11.6 Pets (B12 A15)

| Sim | Game | Rule |
| --- | --- | --- |
| `MonsterSpawner.SpawnPet(cs, def, owner)` | `PlayerCmd.AddPet<T>(player)` `Commands/PlayerCmd.cs:946-955` (state machine `:22-86`) | `CreateCreature(cs, def, owner.Side, null)` (`:51`, `:951`): player side, so no HP roll, no `Niche` draw, HP = `MaxInitialHp`, `MaxHpBeforeModification` stays null; then `AddPet` |
| `MonsterSpawner.AddPet(cs, pet, owner)` | `PlayerCmd.AddPet(Creature, Player)` `:957-967` (state machine `:90-150`) | `AddPetInternal` (`:123`), then `CreatureCmd.Add(pet)` = `AddToCombat` (`:124`). The `"Pet must already be added to a combat state."` check (`:119-122`) is not representable (§11.4) |
| `MonsterSpawner.AddPetInternal(owner, pet)` | `PlayerCombatState.AddPetInternal` `Entities.Players/PlayerCombatState.cs:375-387`; `Creature.PetOwner` `Entities.Creatures/Creature.cs:520-535` | nothing when already in `Pets`; set `PetOwnerId` when it differs (a different existing owner throws `"… already has an owner."`, `Creature.cs:531`); append to `PetIds` (`:385`). The `Died += OnPetDied` subscription (`:384`) is represented by membership in `PetIds` (subscribed iff listed, `:442-443`) |
| `MonsterLifecycle.InvokeDiedEvent(cs, creature)` | `Creature.InvokeDiedEvent` `Creature.cs:1100-1103`, raised by `Kill` at `Commands/CreatureCmd.cs:2770` | the only rule-relevant subscriber: a pet listed in its owner's `PetIds` runs `OnPetDied` |
| `MonsterLifecycle.OnPetDied(cs, owner, pet)` | `PlayerCombatState.OnPetDied` `:433-445` | throw `"Player does not have pet …"` when unlisted; remove from `PetIds` only if `Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, pet)` (`:440-444`) |
| `MonsterLifecycle.GetPet(cs, owner, monsterId)`, `GetPets(cs, owner, buffer)` | `PlayerCombatState.GetPet<T>` `:389-392`, `Pets` `:39`; `Player.Osty` `Entities.Players/Player.cs:247`, `IsOstyAlive` `:249` | the first pet in `PetIds` order whose model id matches (ordinal); `Pets` resolved in `cs.Allies`. The game's `Monster is T` becomes an id comparison (cross-kind references by id, Part B §B5); `IsOstyAlive` = `GetPet(cs, player, "OSTY")?.IsAlive == true` |

Lifecycle differences, all derived from the code above: a pet never gets `AfterAddedToRoom` (enemy side only,
`Entities.Creatures/Creature.cs:67-69`) and never rolls (`IsEnemy` gate `Combat/CombatManager.cs:540`; the player-turn
`PrepareForNextTurn` loop covers `Enemies` only, `:5315-5323`); `TakeTurn` throws `"Only enemy monsters can take automated
turns."` (`Creature.cs:279-283`); pets are player-side turn participants (§11.7); `Kill` removes only enemy-side creatures from
the lists (`Commands/CreatureCmd.cs:2812-2822`), so a dead pet stays in `Allies` and stays a listener (§11.3) while `OnPetDied` may
drop it from `PetIds`; `OstyCmd.Summon` revives such a pet from `Allies` and re-adds it with `AddPetInternal` (same owner, no
throw, `Commands/OstyCmd.cs:350-364`). Owner death (`Commands/CreatureCmd.cs:2701-2717`): `OrbQueue.Clear()`, then `Kill(Osty)`
only if `IsOstyAlive`, then `DeactivateHooks` and `HandlePlayerDeath` (`:2850-2858`); no other pet dies (Byrdpip, PaelsLegion
live on). That code is the pipeline's `Kill` (§11.13). Combat end clears the pet list (`PlayerCombatState.AfterCombatEnd`,
`:279-297`, `_pets.Clear()` `:296`; loop A18).

### 11.7 Side-turn participants (B12 A17)

`MonsterLifecycle.PlayerSideParticipants(cs)` and `EnemySideParticipants(cs)` (phase 0b bodies, owned here and re-verified):
turn start = the `PlayersTakingExtraTurn` creatures when the player side takes an extra turn, else
`ToList(CreaturesOnCurrentSide)` (`Combat/CombatManager.cs:4873-4882`); `CreaturesOnCurrentSide` = `GetCreaturesOnSide(CurrentSide)`
= `Allies` unless `Enemy` (`Combat/CombatState.cs:620`, `:831-838`), so the player side is the player then its pets in insertion
order; the enemy turn end uses `ToList(CreaturesOnCurrentSide)` (`Combat/CombatManager.cs:2055`). Each call returns a fresh
array; dead creatures still listed are included. The player side's turn end is a different set (the ending players only,
`:2649`, `:2860`, scaffold §16.6 item 15) and is built by the loop. Part A's entry points (`SetUpForCombat`,
`AfterCreatureAdded`, `RollMove`, `PrepareForNextTurn`, `TakeTurn`, `PerformMove`, `OnSideSwitch`, `SetMoveImmediate`,
`StunInternal`, `IntendsToAttack`, `PlayerCreatures`) keep their signatures.

### 11.8 Leaving the room: `RemoveCreature`, `ResetStateMachine`

`CombatManager.RemoveCreature` (`Combat/CombatManager.cs:7171-7180`) runs `MonsterModel.BeforeRemovedFromRoom()` then
`ResetStateMachine()` (`_moveStateMachine = null`, `Models/MonsterModel.cs:693-696`); it does not touch the creature lists.
`Kill` calls it for every enemy it removes, also while that enemy performs its move, and only defers the list removal
(`Commands/CreatureCmd.cs:2812-2822`); `Escape` calls it too (`:3936`). Sim: `MonsterLifecycle.RemoveCreature(cs, monster)` =
`Def.BeforeRemovedFromRoom(cs, monster)` + `ResetStateMachine(monster)`.

The game's model keeps `NextMove` and `IsPerformingMove` as model fields (`Models/MonsterModel.cs:431-442`, `:516-522`) that
survive the reset; the sim keeps them on `MonsterAiRuntime`, so the reset is a flag on the runtime
(`MonsterAiRuntime.IsMachineReset`) rather than dropping the runtime. Readers of the game's machine and their sim
counterparts:

| Game read of `MoveStateMachine` | After the reset | Sim |
| --- | --- | --- |
| `PerformMove`: `MoveStateMachine?.OnMovePerformed(move)` (`Models/MonsterModel.cs:285`) | skipped | skipped when `IsMachineReset` |
| `PrepareForNextTurn`: `rollNewMove && MoveStateMachine != null` (`Entities.Creatures/Creature.cs:1131`) | no roll | `MonsterLifecycle.HasMoveStateMachine` (`HasAi && !IsMachineReset`) |
| `RollMove` (`Models/MonsterModel.cs:721`), `SetMoveImmediate`'s `ForceCurrentState` (`:729`, after `NextMove = state`), `StunInternal`'s `StateLog` (`Creature.cs:1116`), `OnMovePerformed` | `NullReferenceException` | `InvalidOperationException` from `RequireMachine` at the same point (`SetMoveImmediate` still assigns `NextMove` first); a crash path in both |

The flag is cloned and part of the canonical form: a reset runtime starts with tag `AiRuntimeReset` (0x4D03) instead of
`AiRuntime` (0x4D01), so the bytes of every non-reset state are unchanged. It matters only while a reset monster stays listed: a
monster killed during its own move until the end of that move, or one kept by `ShouldCreatureBeRemovedFromCombatAfterDeath`.

### 11.9 `PerformMove`

`Models/MonsterModel.cs:234-291` with `MoveState.PerformMove` (`MonsterMoves.MonsterMoveStateMachine/MoveState.cs:46-48`):
`IsPerformingMove = true` (`:267`); `move = NextMove` (`:268`); `targets = PlayerCreatures` (`:269`); (`Log.Info`, `:270-273`,
stripped); `MoveState.PerformMove`: mark the move performed (`MoveState.cs:46`), then its perform (`:48`);
`MoveStateMachine?.OnMovePerformed(move)` (`Models/MonsterModel.cs:285`, §11.8); `History.MonsterPerformedMove(combatState,
monster, move, targets)` (`:286`) = `cs.History.AppendMonsterPerformedMove(cs, monster, move, targets)` (state's contract member;
recorded whether or not the machine was reset); `IsPerformingMove = false` (`:287`); if dead and
`ShouldCreatureBeRemovedFromCombatAfterDeath`, `CombatState.RemoveCreature` (`:288-291`) =
`MonsterLifecycle.RemoveFromCombatState` (`Combat/CombatState.cs:739-770`: an unattached creature returns, else it leaves
`Enemies` or `Allies`). The waits at `:237` and `:292` are stripped. The outer `CombatState != null` guard (`:234`) is always true
(`CombatState` falls back to `NullCombatState`, `:495`), so it has no sim counterpart.

`UNSET_MOVE` is now exact: the default `NextMove` is `new MoveState()` = `("UNSET_MOVE", UnsetMove)` (`:522`, `MoveState.cs:145-146`),
so the game marks that state performed before its perform throws `"No move has been set for the monster"` (`MoveState.cs:46`,
`:187-191`); the sim adds `UNSET_MOVE` to `PerformedAtLeastOnce`, then throws the same message with `IsPerformingMove` left set.
This replaces the P0 deviation of §8.

### 11.10 RNG access (B12 A21, B8)

- Spawn and move rolls draw only from `cs.Rng` streams: the enemy HP roll from `Niche` (`Combat/CombatState.cs:717`), the
  move roll from `MonsterAi` (`Models/MonsterModel.cs:721`, `RunRng` = `RunState.Rng`, `Combat/CombatState.cs:712`). Content that
  reads other run streams (`Fabricator` `MonsterAi`, `Models.Monsters/Fabricator.cs:416`; `ThievingHopper`
  `CombatCardGeneration`, `ThievingHopper.cs:652`; `ToughEgg` `Niche`, `ToughEgg.cs:219`) reads them through `cs.Rng`. A def
  holds no randomness (a reflection test over every registered monster and encounter def); `MonsterModel.Rng` returns
  `Rng.Chaotic` for canonical models (`Models/MonsterModel.cs:396-411`), never modelled.
- No v0.111.0 rule reads `MonsterModel.Rng`: its only reader is `ToughEgg.SetupSkins` (`Models.Monsters/ToughEgg.cs:549-555`,
  visual). The seam is kept because other owners' tests use it (`EncounterRngTests`, `WurmBeetleTests`,
  `CorpseSlugsSeapunkTests`) and is given per-instance storage: a def that opts in creates an extra state deriving from
  `PrivateRngExtraState`, whose sealed `DeepClone` and `WriteCanonical` clone and hash the `Rng` (tag `PrivateRngExtra`
  0x4D04, presence byte, the `Rng`'s counter and state, then the subclass fields). The default `InitPrivateRng` stores the `Rng`
  there and throws `InvalidOperationException` when the extra state is not a `PrivateRngExtraState`.
- The seed needs the map coordinate and act index (`Combat/CombatState.cs:721`), which neither `CombatState` nor the `RunState`
  shell stores. Encounter spawns receive the `CombatRunContext` from the loop; a mid-combat spawn (`SpawnForAdd`, `SpawnPet`,
  `CreateCreature` without a context) of an opt-in def seeds from `CombatRunContext.Default` and records
  `FidelityFlag.ApproximatedRule` with site `MonsterSpawner.UnknownRunContextSite`. Unreachable in v0.111.0 (no def opts in);
  open question 1.

### 11.11 Canonical tags (`MonsterCanonicalTags`, 0x4Dxx)

`AiRuntime 0x4D01`, `AiStateRef 0x4D02`, `AiRuntimeReset 0x4D03` (new), `PrivateRngExtra 0x4D04` (new), `NibbitExtra 0x4D10`,
`ToadpoleExtra 0x4D31` and `CorpseSlugExtra 0x4D41` (moved from `ToadpoleExtraState.CanonicalTag` / `CorpseSlugExtraState.CanonicalTag`,
values unchanged, so no hash moves). A test checks that every tag is in 0x4D00-0x4DFF, unique, and that no other type of the
namespace declares a `const ushort`.

### 11.12 Deviations, flags and stripped sites

| Site | Game | Sim | Flag |
| --- | --- | --- | --- |
| private `Rng` of a mid-combat spawn | seeded with the current map coordinate and act index (`Combat/CombatState.cs:721`) | `CombatRunContext.Default` | `ApproximatedRule` (`MonsterSpawner.UnknownRunContextSite`); no def opts in |
| reads of a reset machine (§11.8), `SetEnemyIndex` with a null encounter (§11.5) | `NullReferenceException` | `InvalidOperationException` / `ArgumentNullException` at the same point | none: crash paths |
| attachment checks of §11.4 / §11.6 | `Creature.CombatState` checks | not representable (attachment = list membership) | none: unreachable for creatures created through `CreateCreature` |
| `MonsterIds` map history, `OnCreatureSpawned`, `NCombatRoom`, `StateTracker`, `CreaturesChanged`, `Log.Info`, `Cmd.CustomScaledWait`, `RefreshIntents` | §11.4, §11.9, §7 | dropped | none: no combat rule reads them (W2 restores the run-history and reward data) |

### 11.13 Calls other owners make (wiring requests)

| # | Owner | Request | Pinned by |
| --- | --- | --- | --- |
| 1 | hooks | `ListenerSnapshot` yields `MonsterState.Listener` right after each creature's powers, for pets in `cs.Allies` and for `cs.Monsters`, in that order (`Combat/CombatState.cs:859-867`); liveness = the owner is in `cs.Monsters` or `cs.Allies` (`:1047`). `MonsterListener.Owner` exposes the owner | `Test_MonsterKernel_ListenerFollowsItsCreaturesPowersInDispatchOrder` (PENDING "hooks" until then) |
| 2 | pipeline | `CreatureCmd.Kill`: call `MonsterLifecycle.InvokeDiedEvent(cs, creature)` where the game raises `Died`, before `shouldRemoveFromCombat` (`Commands/CreatureCmd.cs:2770-2771`); for an enemy it removes, call `MonsterLifecycle.RemoveCreature(cs, monster)` before the `IsPerformingMove`-gated list removal (`:2812-2822`); owner death per §11.6 with `MonsterLifecycle.GetPet(cs, player, "OSTY")`; count `cs.Allies` as attached (B12 A19). `CreatureCmd.Escape` (`:3915-3939`) also calls `RemoveCreature`. `CreatureCmd.Add(cs, def, side, slot)` = `MonsterSpawner.SpawnForAdd`; `CreatureCmd.Add(cs, creature)` for a `MonsterState` = `MonsterSpawner.AddToCombat` | `Test_MonsterKernel_KillDuringItsOwnMoveRunsBeforeRemovedFromRoom`, `Test_MonsterKernel_DeadPetsStayInAlliesAndLeaveThePetList` (PENDING "pipeline" until then) |
| 3 | loop | `PlayerCmd.AddPet(cs, owner, def)` = `MonsterSpawner.SpawnPet(cs, def, owner)`, which already runs `CreatureCmd.Add(pet)`; calling `CreatureCmd.Add` again throws "already in this combat". `SwitchSides` calls `OnSideSwitch` for every creature, pets included (`Combat/CombatManager.cs:7488-7495`). Turn start and enemy turn end use the §11.7 helpers | — |
| 4 | state | `CombatState.AddAlly` / `RemoveAlly` (B12 A19) | four PENDING "state" tests |
| 5 | loop / integrate | refresh `EndToEndTests.PinnedSeedOneOpeningHash` and `PinnedSeedOneFinalHash` (§11.16) | `Test_EndToEnd_CanonicalHashesArePinnedAcrossProcesses` |

### 11.14 Tests (`Sts2Sim.Tests/MonsterKernelTests.cs`, filter `MonsterKernel`)

28 tests, all deterministic, hand-built states unless named. Listener: install and owner, a second `SetUpForCombat` throws; the
listener overrides exactly the six hooks and `MonsterDef` declares them with the monster second, plus `AfterAddedToRoom` /
`BeforeRemovedFromRoom`, and declares no `ShouldDisappearFromDoom` / `OnDieToDoom`; routing of each hook with its exact
arguments through a test-double def, other hooks inert; clone routing to the cloned monster, equal hash and bytes, mutation
independence, `MonsterState.DeepClone` rebinding; dispatch order through `Hook.IterateHookListeners` / `Hook.AfterDeath`
(player power, monster power, its listener, next monster) with liveness (PENDING hooks). Spawn: `SpawnForAdd` id, unique `Niche`
roll replayed on a cloned stream, one `Niche` and one `MonsterAi` draw on the player side, the order SetUpForCombat →
`AfterAddedToRoom` → `AfterCreatureAddedToCombat`, append to the enemies; enemy-side spawn rolls no move; the in-progress check
throws after the id and the `Niche` draw; duplicate add throws; slot sort through an explicit encounter and `GetNextSlot`;
`SetEnemyIndex` order, clamp, both throws, null encounter, the single-enemy quirk. Pets: player-side `CreateCreature` (no roll,
no draw, `MaxHpBeforeModification` null); `AddPetInternal` (owner once, no duplicate, revive re-add, foreign owner throws);
no `AfterAddedToRoom`, no roll, `TakeTurn` throws; `OnPetDied` gated by a vetoing listener; `SpawnPet` order, `Allies`,
duplicate add (PENDING state); a pet killed through `CreatureCmd.Kill` stays in `Allies` and leaves `PetIds` (PENDING state,
pipeline); `GetPet` / `GetPets` order (PENDING state). Participants: without pets, snapshots, dead enemies, extra turn,
unknown id, empty side; with pets in insertion order, a dead pet, extra turn without pets (PENDING state). RNG: only `Niche` and
`MonsterAi` move across encounter spawns, a mid-combat spawn, a player-side creation and 15 rolls (one draw per roll), no def
holds randomness, no registered def opts in; the private `Rng` stored in `PrivateRngExtraState`, seeded per §11.10, cloned,
hashed, flagged for a mid-combat spawn, and an opt-in def without the storage throws. Moves: the history entry after the move
with its fields; `UNSET_MOVE`; `RemoveCreature` (hook, flag, `NextMove` kept, lists untouched, hash variant, clone, guarded
reads, no roll); a death during the own move skips `OnMovePerformed`, records history and leaves the list at the end, or stays
listed when vetoed; the same through `CreatureCmd.Kill` (PENDING pipeline); `CombatSim` `NIBBITS_WEAK`: listeners, clone
lockstep hashes over three enemy turns, `BUTT, SLICE, HISS` history. Tags: range, uniqueness, single file.

PENDING bodies were run against stand-in implementations of the four requests (a scratch copy, not committed: `AddAlly` /
`RemoveAlly` as list operations; the listener slot and owner liveness in `ListenerSnapshot`; `Kill` calling `InvokeDiedEvent`
and `RemoveCreature` and counting allies as attached): 28/28 pass, 0 PENDING, the rest of the suite unchanged, diff gate 4/4.

### 11.15 Round-2 findings (`w1-result/verify-notes-monsters-kernel.md`)

| Finding | Resolution |
| --- | --- |
| [high] ×2 No W1 monsters-kernel work at `ef7c7b9` | Delivered: §11.1-11.14 |
| [medium] ×2 No `MonsterListener`, no `MonsterDef` hook virtuals, `Listener` never installed; `ListenerSnapshot` enumerates powers only | §11.2, §11.3; the snapshot slot is the hooks owner's file, requested in §11.13 row 1 and pinned by a PENDING test |
| [medium] Pet lifecycle missing; `Spawn` would draw `Niche` for pets; owner death over-generalised | §11.6: `SpawnPet` uses the player-side `CreateCreature` (no draw); the contract already reads "kills only an alive Osty" (B8, B12 A15); the `Kill` code is the pipeline's (§11.13 row 2) |
| [medium] ×2 `BeforeRemovedFromRoom` / `ResetStateMachine` missing; `OnMovePerformed` after a mid-move death; no `MonsterState` reset member | §11.8. Rebuttal of the requested `MonsterState.ResetAi()`: dropping the runtime would also drop `NextMove` and `IsPerformingMove`, which are `MonsterModel` fields in the game and survive `ResetStateMachine` (`Models/MonsterModel.cs:431-442`, `:516-522`, `:693-696`); a flag on `MonsterAiRuntime` (this owner's file) is the faithful mapping, so no state change is needed. The `Kill` call site is requested (§11.13 row 2) |
| [medium] Side-turn participant helpers absent | Implemented in phase 0b and re-verified (§11.7); tests with and without pets and on an extra turn |
| [low] ×2 `MonsterPerformedMove` not recorded; no `FidelityFlag` for deviations | Recorded (§11.9). The `UNSET_MOVE` path is now exact and the mid-move `OnMovePerformed` is fixed (§11.8), so neither needs a flag; the remaining deviations are listed in §11.12 with their flag or the reason none applies |
| [low] `SetEnemyIndex` not ported | §11.5 with both throws and the post-removal `Count` quirk |
| [low] ×2 RNG access rules unpinned; `InitPrivateRng` hands a per-monster `Rng` to a shared def; no run context for a mid-combat seed | §11.10: storage in `PrivateRngExtraState` (cloned, hashed), the default stores there, flagged mid-combat seeding, reflection and stream tests; open question 1 |
| [low] Canonical tags in content files | §11.11 |
| [low] Listener clone/hash constraints unpinned | `IOwnedListener` storing only its owner, `CloneFor` reads nothing; tests pin hash equality and routing (§11.3, §11.14) |
| scaffold §14 item 2 (listener design), §16.6 items 15-16 | §11.3; §11.7 (the player turn end is the loop's set); pet snapshots wait for `AddAlly` (PENDING) |

### 11.16 Gates and measurements

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 399 passed, 3 failed, 402 total; `PENDING total: 6` (all from this file: 4 state, 1 hooks, 1 pipeline). The failures are the two known content gates (`W1GateTests.Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline`, `Test_W1Gate_PilotContentIsRegistered`, failing at the base as well) and `EndToEndTests.Test_EndToEnd_CanonicalHashesArePinnedAcrossProcesses` |
| Diff gate (P0 bundle set) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |

The pinned-hash test moves for intended canonical changes only (scaffold §16.6 item 11): the opening hash of seed 1 because every
monster now carries its listener (the `MonsterKernel` presence byte, `0xCA1423508C1168E3` → `0x870EF2BE2AE3F04D`), the final
hash because `PerformMove` records `MonsterPerformedMove` (`0xD7B055DC9B9E326E` → `0xF07E158ED5F0B079`); without the listener the
opening hash stays `0xCA1423508C1168E3` (probe). The action sequence and the victory snapshot of seed 1
(`Test_EndToEnd_GreedySeedOneEndsInAPinnedVictory`) are unchanged. The pins live in a loop-owned test (B12 A13); the loop or the
integrator recomputes them once after the merge, when the loop's own history sites move them again.

Allocation probe (scratch, `NIBBITS_WEAK`, the bench's mid-fight state and 2 000 playouts with the bench's policy seed; base
`263810d` vs this tree, deterministic): canonical form 2 373 → 2 429 bytes (one `MonsterPerformedMove` entry); `Clone()`
6 648 → 6 808 bytes (+160: the listener, the history entry and its target list); 1 008.9 → 1 057.2 bytes per decision over the
same 40 299 decisions (identical trajectories). Bench timings (`Sts2Sim.Bench -c Release`, interleaved with the base tree, load
average 4.3-5.0 from other agents): `Clone()` medians 2.84 µs (base) and 2.82 µs (this tree), playouts 610k and 516k
decisions/s with a per-run spread of 453k-639k for both trees; runs under load 11-17 were discarded. No timing change is
resolvable on this machine; the allocation figures bound it.

### 11.17 Open questions

1. **Run context for mid-combat private `Rng` seeds.** `CombatState` and the `RunState` shell store no map coordinate or act
   index, so a mid-combat spawn of an opt-in def seeds from `CombatRunContext.Default` (flagged, §11.10). W2 (or the state owner)
   should keep the combat's `CombatRunContext` on `RunState`; `MonsterSpawner.CreateCreature` then passes it.
2. **`configure` for mid-combat spawns.** Content that sets mutable-model fields before `CreatureCmd.Add`
   (`Models.Powers/InfestedPower.cs:81-83` `StartStunned`, `StockPower.cs:57-61` `StockAmount`) needs the `configure` overloads
   of `SpawnForAdd`; the pipeline's `CreatureCmd.Add(cs, def, side, slot)` has no such parameter. Content can call
   `MonsterSpawner.SpawnForAdd` directly, or the pipeline adds an overload. `SurprisePower` creates first and adds later
   (`SurprisePower.cs:80`, `:150`): `CreateCreature` then `AddToCombat`.
3. **Hook signatures for monster content.** The six def virtuals take `RunState rs` for both-scope hooks (scaffold M1); monster
   content reads the combat as `rs.Combat!`, which is never null when a monster listener is visited.
