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
