# Hooks spec: listener iteration, the 148 dispatchers, override masks (W1 round 3)

Area owner: hooks agent (CONTRACTS Part B §B3, §B11 row "hooks", §B12 A8, A9, A17, A20). Base `263810d`
(phase-0b amendment fixes). Rule truth: decompile v0.111.0, cited as `<ns-dir>/<File>.cs:<line>` with the
`MegaCrit.Sts2.Core.` prefix dropped; every `:<line>` without a file in the dispatcher sections is in
`Hooks/Hook.cs`.

| File | Contents |
| --- | --- |
| `Sts2Sim.Core/Combat/Pipeline/AbstractModel.cs` | the 182 hook virtuals (unchanged defaults, §2.4) and the per-instance override-mask cache |
| `Sts2Sim.Core/Combat/Pipeline/HookMask.cs` | `HookId` (one value per virtual, game line order), `HookMask` (per-type override bits), `[assembly: InternalsVisibleTo("Sts2Sim.Tests")]` |
| `Sts2Sim.Core/Combat/Pipeline/ListenerSnapshot.cs` | `ListenerSource`, the snapshot iterator (build at the first `MoveNext`, liveness at yield), stack scratch |
| `Sts2Sim.Core/Combat/Pipeline/Hook.cs` | the 148 public dispatchers in the game's line order, the full-list fills, the private `ModifyDamageInternal` |
| `Sts2Sim.Tests/HookTests.cs` | 34 `Test_Hooks_*` tests (§8) |

## 1. Listener sources

Every game dispatcher enumerates one of three iterators. Each is a C# iterator method: its list is built at the
first `MoveNext` and every element passes a liveness filter when it is yielded. The sim's `ListenerSnapshot`
ports the three as one `ref struct` with the same two moments: `BuildNextPart` runs inside the first `MoveNext`,
`IsLive` runs per element inside `MoveNext`.

| Sim `ListenerSource` | Game | Guard |
| --- | --- | --- |
| `Guarded` (G) | `Hook.IterateCombatHookListeners` (`Hooks/Hook.cs:8792-8810`) | yields nothing when `CombatManager.IsOverOrEnding && !IsStarting` (`:8794`), else the combat list |
| `Combat` (C) | `CombatState.IterateHookListeners()` (`Combat/CombatState.cs:855-956`) | none |
| `Run` (R+C with a combat, R without) | `RunState.IterateHookListeners(childCombatState)` (`Runs/RunState.cs:829-933`, state machine `:29-319`, `MoveNext` `:114-255`) | none |

### 1.1 Combat list (`Combat/CombatState.cs:858-927`)

For each creature of `_allies`, then of `_enemies` (`:859`; the sim walks `cs.Allies`, whose first element is
the player and the rest pets in insertion order, then `cs.Monsters`):

1. its `Powers` in list order (`:862`);
2. a creature without a `Player` (enemies and pets) adds its `MonsterModel` (`:864-867`); the sim adds
   `MonsterState.Listener` when it is set (the monsters-kernel adapter, CONTRACTS §B5; a monster without a
   listener has no overriding model, so nothing is lost);
3. a player stops here when `!IsActiveForHooks` (`:870-873`); otherwise its relics in list order without the
   melted ones (`:875-881`), its potion slots in slot order without the empty ones (`:883-889`), then — when
   `PlayerCombatState != null` (`:890-893`; a sim combat player always has its piles and orb queue) — its orbs in
   queue order (`:894`) and, for each pile of `AllPiles` = Hand, Draw, Discard, Exhaust, Play
   (`Entities.Players/PlayerCombatState.cs:105-115`), each card (`:903`), then its affliction (`:904-907`), then
   its enchantment (`:908-911`). The sim visits `player.Hand`, `DrawPile`, `DiscardPile`, `ExhaustPile`,
   `PlayPile` in that order (the order of the sim's `AllPiles`) and adds a card through its
   `CardInstance.Listener` adapter when it is set (cards-kernel, §B5).

Then the run modifiers (`:916-919`; the combat's `Modifiers` are the run's, `Rooms/CombatRoom.cs:666`, so the sim
reads `cs.Run.Modifiers`), the badges (`:920-923`) and the multiplayer scaling model (`:924-927`), then mod
subscribers (`:944-955`). Not modelled, all inert: badges only update badge counters (their three hook overrides
in `Models.Badges` issue no command); `MultiplayerScalingModel.ModifyBlockMultiplicative` returns the player
count, 1, in single player (`Models.Singleton/MultiplayerScalingModel.cs:41-55`), and a multiplier of 1 is not a
modifier; vanilla has no mod subscriber.

### 1.2 Liveness at yield (`Combat/CombatState.cs:1010-1062`, `Runs/RunState.cs:1037-1073`)

| Kind | Game rule | Sim |
| --- | --- | --- |
| power | `Owner.CombatState != null && (Owner.Player?.IsActiveForHooks ?? true)` (`:1061`) | the owner is in `cs.Allies` or `cs.Monsters` (`Creature.CombatState` is set by `AttachCreature`, `Combat/CombatState.cs:726-731`, and cleared only by `RemoveCreature`, `:739-768`, whose every caller passes `unattach = true`), and an owning `PlayerState` is active; pets and monsters have no `Player` (`Entities.Creatures/Creature.cs:441`, `:520-537`). A power without an owner is not live (sim-only state) |
| monster | `Creature.CombatState != null` (`:1047`) | the `MonsterState` is in `cs.Allies` or `cs.Monsters` |
| relic, potion, orb | `!HasBeenRemovedFromState && Owner.IsActiveForHooks` (`:1049`, `:1057`, `:1059`) | same; `Owner` throws for an ownerless model, where the game dereferences null |
| card | `!HasBeenRemovedFromState && Owner.IsActiveForHooks` (`:1055`) | the rule on the `CardInstance` whose `Listener` was listed; a card without an owner (P0 combat copies, scaffold-w1 §16.6 item 5) is checked against the player whose pile or deck held it, which is its owner in every game state |
| affliction, enchantment | `HasCard && !Card.HasBeenRemovedFromState && Card.Owner.IsActiveForHooks` (`:1051`, `:1053`) | same, with the ownerless-card rule above |
| modifier (badge, achievement, scaling) | true | true |

Consequences that the tests pin (§8): a power taken off its creature's list during a pass keeps its owner
(`Models/PowerModel.cs:685-694` → `Entities.Creatures/Creature.cs:1214-1223` removes it from `_powers` only), so the
rest of that pass still calls it; a relic melted during a pass is still called in that pass (`IsMelted` filters at
build, `:877`; the yield rule does not read it); a player deactivated during a pass silences its remaining
listeners in that pass; a monster removed from the combat silences its powers and listener.

### 1.3 Guarded rule

`IterateCombatHookListeners` evaluates `IsOverOrEnding && !IsStarting` once, at its first `MoveNext`
(`Hooks/Hook.cs:8794-8797`); the sim evaluates `CombatStatus.IsOverOrEnding(cs) && !cs.IsStarting` in the same
place. A listener that ends the combat does not stop its own pass; the next guarded pass of a multi-pass
dispatcher re-evaluates the guard and yields nothing.

### 1.4 Run list (`Runs/RunState.cs:829-933`)

1. For each player of `Players` that `IsActiveForHooks` (`:839-842`): each deck card (`:849`), then its
   enchantment (`:850-853`), never an affliction. The sim walks `rs.Players[i].Deck` and adds `card.Listener` and
   `card.Enchantment`.
2. Only when `childCombatState == null` (`:866`): per active player its relics without the melted ones (`:876`)
   and its potions, the non-empty slots (`:877`; `Player.Potions`, `Entities.Players/Player.cs:245`); then the run's
   modifiers (`:885`), badges and scaling model (`:886-887`, inert as in §1.1). Modifiers stay when every player is
   inactive.
3. Yield filter `RunState.Contains` (`:895`, `:1037-1073`): the relic, potion, card and enchantment rules of §1.2.
4. Then, when a combat is given, the combat list (`:917-921`). It is a second iterator whose list is built when
   the run part is exhausted, i.e. after the deck listeners ran. The sim builds the combat part at that moment
   (`PhaseRunPartThenCombat`), so a power a deck listener adds during the pass is visited in the same pass.
   With a combat, relics, potions and modifiers come only from the combat list, so nothing is visited twice
   (CONTRACTS §B12 A8).

The 42 run-only dispatchers call `runState.IterateHookListeners(null)` even while a combat is attached (for example
`AfterGoldGained`, `:2965`); the sim passes `cs = null` to the snapshot in all of them.

### 1.5 Full-list fills

`Hook.IterateCombatHookListeners(cs, buffer)` (G), `IterateHookListeners(cs, buffer)` (C) and
`IterateRunHookListeners(rs, cs, buffer)` (R+C or R; the P0 `(cs, buffer)` overload forwards to `(cs.Run, cs, …)`)
fill `buffer` with every live listener, unmasked, in order. They run no listener code, so the deferred build of
§1.4 item 4 cannot be observed through them. `Hook.IterateListenersFor(hook, source, rs, cs, buffer)` (internal)
is the masked fill the tests compare with them.

## 2. Override masks (`HookMask`, CONTRACTS §B3, §B12 A20)

### 2.1 Masks

`HookId` has one value per `AbstractModel` virtual (182, in `Models/AbstractModel.cs` line order); `HookMask` is a
deeply immutable 192-bit set. `HookMask.Compute(type)` sets bit `h` when some class between `type` and
`AbstractModel` overrides virtual `h` (reflection: a virtual method whose base definition is declared by
`AbstractModel` and which is not declared by `AbstractModel` itself; `sealed override` counts, a hiding `new`
method does not). `HookMask.For(type)` reads a table of every concrete `AbstractModel` subclass of the Core
assembly, built lazily once (`Lazy<FrozenDictionary<Type, HookMask>>`, the registries' pattern, Part A §7), and
computes the mask for any other type (test doubles). Each model caches its mask in a private field on first use
(`AbstractModel.OverriddenHooks`); `MemberwiseClone` copies the reference, so clones share one immutable object
(not part of the canonical form; `Test_State_CombatSimCloneSharesNoMutableReferences` accepts it because its
fields are `readonly ulong`).

### 2.2 Masked iteration

Every dispatcher pass passes its virtual's `HookId`; `ListenerSnapshot.Append` keeps a model only when its mask has
that bit. The order of the kept models is the order of the full list, and liveness is evaluated at yield exactly as
for the full list. The masked path is observably equal to the full one because a non-overriding listener runs
`AbstractModel`'s default, and every default is neutral for its dispatcher (§2.4): a no-op; an identity value
(`ModifyX` returns its input, `TryModifyX` returns false and its input); `0` / `1` / `decimal.MaxValue` for the
additive / multiplicative / cap passes, which never join a modifier list (`!= 0`, `!= 1`, `c < cap`); the gate
default that does not decide (`true` for all-true gates, `false` for any-true gates and `flag ||` folds);
`ContainsModel` / `Contains` filters can only select overriders. Skipped liveness checks are side-effect free. A
per-listener context scope pushed for a non-overrider would be pushed and popped with no prompt, which is
unobservable (the context stack is transient, §B1). The only difference is on malformed states the game cannot
reach (an ownerless relic, potion or orb in a player's list), where the full path's liveness check throws and the
masked path skips a non-overrider without checking it.

### 2.3 No cached listener array

The ordered listener array is **not** cached: it would have to be invalidated by every structural change, and the
structure is held in public mutable containers other owners and tests write directly (`PlayerState.Relics` and
`Deck` lists, the `PotionSlots` array, `CardPile.MutableCards`, `CreatureState.Powers`, `cs.Monsters`,
`rs.Modifiers`, `CardInstance.Listener/Affliction/Enchantment`, `MonsterState.Listener`, `RelicModel.IsMelted`,
`PlayerState.IsActiveForHooks`; `W1GateTests.EquipPlayer` writes `Relics` and `PotionSlots`), none of which can
signal a change. A complete validity check would compare all of them and cost as much as the walk. Each pass
therefore walks the structure and keeps the masked models (§B12 A20, "otherwise compute the masked order per
dispatch").

### 2.4 Virtual defaults (re-check)

A script compared the 182 sim virtuals with `Models/AbstractModel.cs:188-1097`: every default matches (`Task`
no-ops → `void`, `79228162514264337593543950335m` → `decimal.MaxValue`; the `out` parameters of
`TryModifyEnergyCostInCombat[Late]`, `TryModifyStarCost` and `TryModifyPowerAmountReceived` default to the input,
those of `TryModifyCardBeingAddedToDeck[Late]` to null). The three other game virtuals (`IsMock`,
`PreviewOutsideOfCombat`, `CompareTo`) are not hooks. `AbstractModel.InvokeExecutionFinished`
(`Models/AbstractModel.cs:183-186`) raises an event whose only subscriber is UI (`Nodes.Combat/NPlayerHand.cs:1064`);
every dispatcher's call to it is dropped.

## 3. Snapshot storage (`ArrayPool` removed, §B12 A20)

The snapshot keeps its entries in an `[InlineArray(4)]` buffer inside the `ref struct`, i.e. on the dispatcher's
stack frame, and moves to a heap array (doubling) beyond four kept entries. Nothing is pooled, nothing is static,
nothing is shared between snapshots, so nested and re-entrant dispatches each own their storage and no state is
cloned or hashed. This is stronger than per-state scratch and needs no field on another owner's type. An entry
holds the model, its kind, the liveness subject (the `CardInstance` of a card listener or the `MonsterState` of a
monster listener) and the holding player (§1.2, ownerless cards); it is 32 bytes.

The capacity trades frame size against overflow allocations. NIBBITS_WEAK bench, one session, same tree otherwise:
8 → 400k, 4 → 435-438k, 2 → 470-485k, 1 → 241-266k decisions/s. That encounter keeps 0.6 models per decision over
56 passes per decision (counted with a temporary probe), yet at capacity 1 every pass that keeps two listeners
allocates, which already costs more than the zeroing saved. W1 content (relics, card listeners) keeps more listeners
per pass, so four is the hedge; the integrator's bench with W1 content decides whether to move it (one constant,
`ListenerSnapshot.InlineCapacity`).

The only static fields in the area are the two `Lazy<FrozenDictionary>` tables of `HookMask` (built once from code,
never mutated after, like the registries) and the immutable `HookMask.None` / `All`
(`Test_Hooks_PipelineKeepsNoStaticMutableState`).

## 4. Dispatch patterns

### 4.1 Multi-pass dispatchers

A dispatcher with `X` / `XLate` (or `Early` / normal / `Late`, `VeryEarly` / `Early` / normal) passes creates one
iterator per pass; each pass builds a fresh list and, for G, re-evaluates the guard. A listener added in one pass
runs in the next.

### 4.2 Modifier lists (`ref List<AbstractModel>?`, Part A §4.6)

The game's `out` lists are always new lists (`:10073`, `:10123`, `:10165`, `:10190`, `:10269`, `:10330`, `:10478`,
`:10504`, `:10586`, `:10630`, `:10842`, `:10887`, `:10931`). Every sim `ref` dispatcher sets `modifiers = null`
at entry and allocates on the first modifier, so a caller's previous list is never appended to or cleared; "no
modifier" is `null` where the game has an empty list. Every caller in the tree already passes `null`. The membership
rule per dispatcher is in the table (§6). `ModifyDamage`'s list is the one `ModifyDamageInternal` returns (`:10413`
overwrites the outer list).

### 4.3 Gates, preventers, filters

All-true gates return `false` at the first listener that says no, any-true gates return `true` at the first that
says yes; the four `out` gates (`ShouldAllowTargeting`, `ShouldClearBlock`, `ShouldDie`, `ShouldPlay`) report that
listener, `ShouldDraw` reports it as `modifier`, `ShouldAddToDeck` as `preventer`. `ShouldForcePotionReward` and
`ShouldAllowFreeTravel` fold with `flag = flag || …` (`:11712`, `:11731`): after the first `true` no listener is
called but the iteration continues. `TryModifyCardRewardOptions` calls every listener and folds afterwards
(`:10277`). The 13 `AfterModifying*` dispatchers call only listed models (`Enumerable.Contains`, reference
equality: `AbstractModel` does not override `Equals`); the sim returns at once for a null or empty list, which is
exact because no listener can match and the skipped work has no side effect. The three `AfterPreventing*`
dispatchers call the preventer alone when it is in the live list (`:5443`, `:5516`, `:5585`).

### 4.4 Enchantment pre-pass (§B12 A9)

`ModifyBlock`: when `cardSource?.Enchantment != null`, `num += EnchantBlockAdditive(num)`, then
`num *= EnchantBlockMultiplicative(num)` on the running value (`:10075-10080`), before both listener passes.
`ModifyDamage`: the additive term when `HasFlag(Additive)` (`:10334-10337`), then the multiplicative term when
`HasFlag(Multiplicative)` (`:10338-10341`), before `ModifyDamageInternal`. Neither term enters the modifier list.
The pre-pass reads `cardSource.Enchantment` only: it does not require a bound enchantment (`HasCard`), unlike the
enchantment's own listener liveness. The flags are single bits (`Hooks/ModifyDamageHookType.cs`), so
`(type & F) == F` is `HasFlag(F)`.

`CardPreviewMode previewMode` is dropped: its `MultiCreatureTargeting` branch (`:10343-10410`, a preview value over
all hittable enemies) is reached only from the card-text dynamic vars (`Localization.DynamicVars/DamageVar.cs:51`,
`OstyDamageVar.cs:53`, `CalculatedDamageVar.cs:67`), which are UI; every rules caller passes `CardPreviewMode.None`
(`Commands/CreatureCmd.cs:850`, `:3694`; `Models.Cards/Thrash.cs:113`; `MonsterMoves.Intents/AttackIntent.cs:98`;
`Models.Powers/PoisonPower.cs:251`), which takes `ModifyDamageInternal` directly (`:10411-10415`).

### 4.5 Per-listener choice contexts (§B1, D2, §B12 A20)

Six dispatchers build a `HookPlayerChoiceContext` per listener and wait only for the listener to pause or
complete (`AssignTaskAndWaitForPauseOrCompletion`, `GameActions.Multiplayer/HookPlayerChoiceContext.cs`). The sim
runs the listener inline inside `using var scope = cs.Choice.PushContext(ChoiceContextKind.PerListener)`
(`rs.Choice` in `AfterDeath`, which may run without a combat); the choice runtime records
`FidelityFlag.DeferredListenerPromptInline` when a prompt opens under that frame (D2). The join structure of the
game, for the deferred-emulation backlog:

| Dispatcher | Context per listener | Join of paused listeners |
| --- | --- | --- |
| `AfterDeath` | `:2364` (`GameActionType.Combat` or `NonCombat` by `combatState`) | none: a paused listener finishes after the dispatcher returned |
| `AfterDiedToDoom` | `:2467` | none |
| `AfterSideTurnEnd` | `:6170`, `:6225` | `Task.WhenAll` after each pass (`:6191`, `:6246`): pass 2's list is built after pass 1's paused listeners completed |
| `BeforeFlush` | `:7776`, `:7819` | one `WhenAll` after both passes (`:7840`) |
| `BeforeSideTurnEnd` | `:8398`, `:8441`, `:8484` | one `WhenAll` after the three passes (`:8505`) |
| `BeforeSideTurnStart` | `:8607` | none |

`design-combat-kernel.md` §2.2 says all six join at the end of each pass; the table above is what the decompile
does.

### 4.6 `LocalContext.NetId` gates

Eight dispatchers do nothing when `LocalContext.NetId` has no value (the six above and the two auto-play-phase
dispatchers, lines in §6). `RunManager.Launch` sets it to `NetService.NetId` (`Runs/RunManager.cs:4035`), which is
`1` for the single-player service (`Multiplayer/NetSingleplayerGameService.cs:33`), and it is cleared only on run
cleanup (`Runs/RunManager.cs:4678`): always true during a run, not ported.

### 4.7 Other dropped steps

`PlayerChoiceContext.PushModel` / `PopModel` around a listener call (the "last involved model" read only by
`GameActions/GenericHookGameAction.cs:217` `ToString` and `Nodes.Multiplayer/NMultiplayerPlayerIntentHandler.cs:397-405`)
and `InvokeExecutionFinished` (§2.4) are UI. `ModifyAttackHitCount` returns `decimal.op_Implicit(num)` of an int
(`:10068`); the sim returns the int, the same value.

## 5. Context parameters

### 5.1 Both-scope dispatchers

The 23 dispatchers whose game signature is `(IRunState, ICombatState?, …)` take `(RunState rs, CombatState? cs, …)`
(scaffold M3; the 13 P0 ones keep exact `(CombatState cs, …)` forwarding overloads to `(cs.Run, cs, …)`):
`AfterCardChangedPiles`, `BeforeCombatStart`, `AfterCombatEnd`, `AfterCombatVictory`, `AfterCurrentHpChanged`,
`BeforeDamageReceived`, `AfterDamageReceived`, `BeforeDeath`, `AfterDeath`, `AfterModifyingDamageAmount`,
`AfterModifyingGoldGained`, `AfterModifyingHpLostBeforeOsty`, `AfterModifyingHpLostAfterOsty`, `AfterPotionDiscarded`,
`AfterPotionProcured`, `BeforePotionUsed`, `AfterPotionUsed`, `AfterPreventingDeath`, `ModifyDamage`, `ModifyGoldGained`,
`ModifyHpLost`, `ShouldDie`, `ShouldProcurePotion`. `cs == null` is the game's out-of-combat dispatch: the run list
without a combat (§1.4); it replaces the scaffold's `NotImplementedException` (scaffold-w1 M26, §14 item 15). A
non-null `cs` that is not `rs.Combat` still throws `InvalidOperationException`: the virtuals receive `rs` only, so a
detached combat would hand overrides the wrong combat (`rs.Combat`). The full-list fill accepts any `cs` (it runs no
listener code).

### 5.2 Side-turn forwarding overloads (§B12 A17)

The scaffold's `(cs, side)` overloads of `BeforeSideTurnStart`, `AfterSideTurnStart`, `BeforeSideTurnEnd`,
`AfterSideTurnEnd` are kept unchanged: they pass the live `cs.Monsters` (enemy side) or `cs.Allies` as
`participants`, where the game passes snapshots (turn start `Combat/CombatManager.cs:4873-4882`, shared by `:4914`
and `:5080`; player turn end the ending players' creatures `:2649`, `:2860`, calls `:2457`, `:2967`; enemy turn end
one copy `:2055` shared by `:2056` and `:2088`). The loop moves its callers to the three-argument overloads with the
`MonsterLifecycle` snapshots and the integrator deletes overloads without callers (A17); this track adds no caller
(`HookTests` uses only the three-argument overloads). The two-argument callers at this commit are the P0 loop (sim
`Combat/Loop/CombatLoop.cs:247`, `:272`, `:391`, `:460`, `:558`, `:560`) and two other owners' test files
(`Sts2Sim.Tests/PipelineTests.cs:546`, `:560`, `:767-822`; `Sts2Sim.Tests/WurmBeetleTests.cs:498-525`), so the A17
deletion also moves those test calls (§10 item 8). For every one of them the difference is unobservable: no override
in the tree reads `participants` (the four P0 overrides, `WeakPower`, `VulnerablePower`, `FrailPower`, `ShrinkPower`,
and the test doubles ignore it), `cs.Allies` holds only the player (`CombatState.AddAlly` is a stub) and no
registered listener overrides `ShouldTakeExtraTurn`. No `FidelityFlag` is recorded: the deviation cannot occur in this
tree, and a record on this every-turn path would change the canonical form of every combat.

## 6. Dispatcher table (148 public dispatchers, `Hooks/Hook.cs`)

L = the public method's line; *State machine* = the async body (`sync` for the 67 synchronous dispatchers, whose
body is at L). Sources: G, C, R+C, R as in §1, one entry per pass, with the line where the pass's iterator is
created. Every sim dispatcher passes the pass's virtual as its `HookId` (§2.2) and calls the virtual with the
game's arguments in the game's order, `cs` / `rs` first. *Sim*: "P0, re-checked" = 77 dispatchers ported in P0,
compared line by line again in this round and moved to the masked snapshot; "phase 0b, re-checked" = the 8 bodies
the scaffold amendment ported (scaffold-w1 §16.2, §16.7); "ported (round 3)" = the 63 former stubs. Counts per
source: G 73, R 42, R+C 23 (incl. `ModifyDamage`), C 10.

| L | Dispatcher | State machine | Passes (virtual) | Source per pass (iteration line) | Pattern | Sim |
|---:|---|---|---|---|---|---|
| 8813 | `AfterActEntered` | :34-121 | `AfterActEntered` | R :62 | void, each listener once | ported (round 3) |
| 8826 | `BeforeAttack` | :6806-6895 | `BeforeAttack` | G :6836 | void, each listener once | P0, re-checked |
| 8840 | `AfterAttack` | :122-213 | `AfterAttack` | G :154 | void, each listener once | P0, re-checked |
| 8855 | `AfterBlockBroken` | :519-612 | `AfterBlockBroken` | C :553 | void, each listener once | P0, re-checked |
| 8871 | `AfterBlockCleared` | :613-702 | `AfterBlockCleared` | G :643 | void, each listener once | P0, re-checked |
| 8885 | `BeforeBlockGained` | :6896-6991 | `BeforeBlockGained` | G :6932 | void, each listener once | P0, re-checked |
| 8902 | `AfterBlockGained` | :703-798 | `AfterBlockGained` | G :739 | void, each listener once | P0, re-checked |
| 8919 | `BeforeCardAutoPlayed` | :6992-7085 | `BeforeCardAutoPlayed` | G :7026 | void, each listener once | phase 0b, re-checked |
| 8935 | `AfterCardChangedPiles` | :799-946 | `AfterCardChangedPiles`, `AfterCardChangedPilesLate` | R+C :846 → R+C :886 | void, one fresh snapshot per pass | phase 0b, re-checked |
| 8952 | `AfterCardDiscarded` | :947-1040 | `AfterCardDiscarded` | G :979 | void, each listener once | P0, re-checked |
| 8967 | `AfterCardDrawn` | :1041-1190 | `AfterCardDrawnEarly`, `AfterCardDrawn` | G :1086 → G :1128 | void, one fresh snapshot per pass | P0, re-checked |
| 8983 | `AfterCardEnteredCombat` | :1191-1280 | `AfterCardEnteredCombat` | G :1221 | void, each listener once | P0, re-checked |
| 8997 | `AfterCardExhausted` | :1281-1376 | `AfterCardExhausted` | G :1315 | void, each listener once | P0, re-checked |
| 9013 | `AfterCardGeneratedForCombat` | :1377-1468 | `AfterCardGeneratedForCombat` | G :1409 | void, each listener once | P0, re-checked |
| 9028 | `BeforeCardPlayed` | :7086-7175 | `BeforeCardPlayed` | G :7116 | void, each listener once | P0, re-checked |
| 9042 | `AfterCardPlayed` | :1469-1616 | `AfterCardPlayed`, `AfterCardPlayedLate` | C :1512 → C :1554 | void, one fresh snapshot per pass | P0, re-checked |
| 9057 | `BeforeCardRemoved` | :7176-7265 | `BeforeCardRemoved` | R :7206 | void, each listener once | ported (round 3) |
| 9071 | `BeforeCombatStart` | :7358-7499 | `BeforeCombatStart`, `BeforeCombatStartLate` | R+C :7399 → R+C :7439 | void, one fresh snapshot per pass | P0, re-checked; `cs == null` path added |
| 9085 | `AfterCombatEnd` | :1617-1708 | `AfterCombatEnd` | R+C :1649 | void, each listener once | phase 0b, re-checked |
| 9100 | `BeforeCombatRewardOffered` | :7266-7357 | `BeforeCombatRewardOffered` | R :7298 | void, each listener once | ported (round 3) |
| 9115 | `AfterCombatVictory` | :1709-1852 | `AfterCombatVictoryEarly`, `AfterCombatVictory` | R+C :1752 → R+C :1792 | void, one fresh snapshot per pass | phase 0b, re-checked |
| 9130 | `AfterCreatureAddedToCombat` | :1853-1942 | `AfterCreatureAddedToCombat` | C :1883 | void, each listener once | P0, re-checked |
| 9144 | `AfterCurrentHpChanged` | :1943-2036 | `AfterCurrentHpChanged` | R+C :1977 | void, each listener once | P0, re-checked; `cs == null` path added |
| 9160 | `AfterDamageGiven` | :2037-2138 | `AfterDamageGiven` | C :2077 | void, each listener once | P0, re-checked |
| 9179 | `BeforeDamageReceived` | :7500-7603 | `BeforeDamageReceived` | R+C :7542 | void, each listener once | P0, re-checked; `cs == null` path added |
| 9199 | `AfterDamageReceived` | :2139-2296 | `AfterDamageReceived`, `AfterDamageReceivedLate` | R+C :2192 → R+C :2234 | void, one fresh snapshot per pass | P0, re-checked; `cs == null` path added |
| 9219 | `BeforeDeath` | :7604-7695 | `BeforeDeath` | R+C :7636 | void, each listener once | P0, re-checked; `cs == null` path added |
| 9234 | `AfterDeath` | :2297-2405 | `AfterDeath` | R+C :2340 | `LocalContext.NetId.HasValue` gate :2337 (always true); per-listener `HookPlayerChoiceContext` :2364; no join; void, each listener once | P0, re-checked; PerListener scope added, `cs == null` path added |
| 9251 | `AfterGoldGained` | :2935-3024 | `AfterGoldGained` | R :2965 | void, each listener once | ported (round 3) |
| 9265 | `AfterDiedToDoom` | :2406-2508 | `AfterDiedToDoom` | C :2443 | `LocalContext.NetId.HasValue` gate :2440 (always true); per-listener context :2467; no join; void, each listener once | ported (round 3); PerListener scope added |
| 9279 | `AfterEnergyReset` | :2509-2650 | `AfterEnergyReset`, `AfterEnergyResetLate` | G :2550 → G :2590 | void, one fresh snapshot per pass | P0, re-checked |
| 9293 | `AfterEnergySpent` | :2651-2742 | `AfterEnergySpent` | G :2683 | void, each listener once | P0, re-checked |
| 9308 | `BeforeFlush` | :7696-7878 | `BeforeFlush`, `BeforeFlushLate` | G :7752 → G :7797 | `LocalContext.NetId.HasValue` gate :7748 (always true); per-listener contexts :7776, :7819; one `WhenAll` after both passes :7840; void, one fresh snapshot per pass | P0, re-checked; PerListener scope added |
| 9322 | `AfterFlush` | :2743-2840 | `AfterFlush` | G :2779 | void, each listener once | P0, re-checked |
| 9339 | `AfterForge` | :2841-2934 | `AfterForge` | G :2875 | void, each listener once | ported (round 3) |
| 9355 | `BeforeHandDraw` | :7879-8026 | `BeforeHandDraw`, `BeforeHandDrawLate` | G :7922 → G :7964 | void, one fresh snapshot per pass | P0, re-checked |
| 9370 | `AfterHandEmptied` | :3025-3118 | `AfterHandEmptied` | G :3057 | void, each listener once | P0, re-checked |
| 9385 | `AfterItemPurchased` | :3119-3212 | `AfterItemPurchased` | R :3153 | void, each listener once | ported (round 3) |
| 9401 | `AfterMapGenerated` | :3213-3304 | `AfterMapGenerated` | R :3245 | void, each listener once | ported (round 3) |
| 9416 | `AfterModifyingBlockAmount` | :3305-3404 | `AfterModifyingBlockAmount` | G :3341 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9433 | `AfterModifyingCardPlayCount` | :3405-3500 | `AfterModifyingCardPlayCount` | G :3437 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9448 | `AfterModifyingCardRewardOptions` | :3501-3594 | `AfterModifyingCardRewardOptions` | R :3531 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | ported (round 3) |
| 9462 | `AfterModifyingDamageAmount` | :3595-3692 | `AfterModifyingDamageAmount` | R+C :3629 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked; `cs == null` path added |
| 9478 | `AfterModifyingEnergyGain` | :3693-3786 | `AfterModifyingEnergyGain` | G :3723 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9492 | `AfterModifyingGoldGained` | :3787-3886 | `AfterModifyingGoldGained` | R+C :3823 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | ported (round 3) |
| 9509 | `AfterModifyingHandDraw` | :3887-3980 | `AfterModifyingHandDraw` | G :3917 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9523 | `AfterModifyingHpLostBeforeOsty` | :4077-4172 | `AfterModifyingHpLostBeforeOsty` | R+C :4109 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked; `cs == null` path added |
| 9538 | `AfterModifyingHpLostAfterOsty` | :3981-4076 | `AfterModifyingHpLostAfterOsty` | R+C :4013 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked; `cs == null` path added |
| 9553 | `AfterModifyingOrbPassiveTriggerCount` | :4173-4268 | `AfterModifyingOrbPassiveTriggerCount` | G :4205 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | ported (round 3) |
| 9568 | `AfterModifyingPowerAmountGiven` | :4269-4364 | `AfterModifyingPowerAmountGiven` | G :4301 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9583 | `AfterModifyingPowerAmountReceived` | :4365-4460 | `AfterModifyingPowerAmountReceived` | G :4397 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | P0, re-checked |
| 9598 | `AfterModifyingRewards` | :4461-4554 | `AfterModifyingRewards` | R :4491 | calls listeners contained in `modifiers` (`Enumerable.Contains`); the sim returns early on an empty list (no listener can match) | ported (round 3) |
| 9612 | `AfterOrbChanneled` | :4555-4650 | `AfterOrbChanneled` | G :4589 | void, each listener once | ported (round 3) |
| 9628 | `AfterOrbEvoked` | :4651-4744 | `AfterOrbEvoked` | G :4685 | void, each listener once | ported (round 3) |
| 9644 | `AfterOstyRevived` | :4745-4834 | `AfterOstyRevived` | G :4775 | void, each listener once | ported (round 3) |
| 9658 | `AfterPlayerTurnStart` | :4835-5032 | `AfterPlayerTurnStartEarly`, `AfterPlayerTurnStart`, `AfterPlayerTurnStartLate` | G :4882 → G :4925 → G :4968 | void, one fresh snapshot per pass | P0, re-checked |
| 9673 | `AfterAutoPostPlayPhaseEntered` | :214-315 | `AfterAutoPostPlayPhaseEntered` | G :250 | `LocalContext.NetId.HasValue` gate :248 (always true); void, each listener once | phase 0b, re-checked |
| 9688 | `AfterAutoPrePlayPhaseEntered` | :316-518 | `AfterAutoPrePlayPhaseEnteredEarly`, `AfterAutoPrePlayPhaseEntered`, `AfterAutoPrePlayPhaseEnteredLate` | G :365 → G :410 → G :453 | `LocalContext.NetId.HasValue` gate :363 (always true); void, one fresh snapshot per pass | phase 0b, re-checked |
| 9703 | `AfterPotionDiscarded` | :5033-5124 | `AfterPotionDiscarded` | R+C :5065 | void, each listener once | ported (round 3) |
| 9718 | `AfterPotionProcured` | :5125-5216 | `AfterPotionProcured` | R+C :5157 | void, each listener once | ported (round 3) |
| 9733 | `BeforePotionUsed` | :8027-8120 | `BeforePotionUsed` | R+C :8061 | void, each listener once | ported (round 3) |
| 9749 | `AfterPotionUsed` | :5217-5310 | `AfterPotionUsed` | R+C :5251 | void, each listener once | ported (round 3) |
| 9765 | `BeforePowerAmountChanged` | :8121-8218 | `BeforePowerAmountChanged` | G :8159 | void, each listener once | P0, re-checked |
| 9783 | `AfterPowerAmountChanged` | :5311-5408 | `AfterPowerAmountChanged` | G :5349 | void, each listener once | P0, re-checked |
| 9801 | `AfterPreventingBlockClear` | :5409-5479 | `AfterPreventingBlockClear` | G :5443 | `Enumerable.Contains(list, preventer)` then the preventer alone :5443 | P0, re-checked |
| 9816 | `AfterPreventingDeath` | :5480-5552 | `AfterPreventingDeath` | R+C :5516 | `Enumerable.Contains(list, preventer)` then the preventer alone :5516 | P0, re-checked; `cs == null` path added |
| 9832 | `AfterPreventingDraw` | :5553-5621 | `AfterPreventingDraw` | G :5585 | `Enumerable.Contains(list, modifier)` then the modifier alone :5585 | P0, re-checked |
| 9846 | `AfterRestSiteHeal` | :5622-5713 | `AfterRestSiteHeal` | R :5654 | void, each listener once | ported (round 3) |
| 9861 | `AfterRestSiteSmith` | :5714-5803 | `AfterRestSiteSmith` | R :5744 | void, each listener once | ported (round 3) |
| 9875 | `AfterRewardTaken` | :5804-5895 | `AfterRewardTaken` | R :5836 | void, each listener once | ported (round 3) |
| 9890 | `BeforeRoomEntered` | :8219-8308 | `BeforeRoomEntered` | R :8249 | void, each listener once | ported (round 3) |
| 9904 | `AfterRoomEntered` | :5896-5985 | `AfterRoomEntered` | R :5926 | void, each listener once | ported (round 3) |
| 9918 | `AfterShuffle` | :5986-6079 | `AfterShuffle` | G :6018 | void, each listener once | P0, re-checked |
| 9933 | `BeforeSideTurnStart` | :8544-8647 | `BeforeSideTurnStart` | G :8583 | `LocalContext.NetId.HasValue` gate :8580 (always true); per-listener context :8607; no join; void, each listener once | P0, re-checked; PerListener scope added |
| 9948 | `AfterSideTurnStart` | :6292-6435 | `AfterSideTurnStart`, `AfterSideTurnStartLate` | G :6335 → G :6375 | void, one fresh snapshot per pass | P0, re-checked |
| 9963 | `AfterStarsGained` | :6436-6527 | `AfterStarsGained` | G :6468 | void, each listener once | ported (round 3) |
| 9978 | `AfterStarsSpent` | :6528-6619 | `AfterStarsSpent` | G :6560 | void, each listener once | ported (round 3) |
| 9993 | `AfterSummon` | :6620-6715 | `AfterSummon` | G :6654 | void, each listener once | ported (round 3) |
| 10009 | `AfterTakingExtraTurn` | :6716-6805 | `AfterTakingExtraTurn` | G :6746 | void, each listener once | P0, re-checked |
| 10023 | `BeforeSideTurnEnd` | :8309-8543 | `BeforeSideTurnEndVeryEarly`, `BeforeSideTurnEndEarly`, `BeforeSideTurnEnd` | G :8374 → G :8419 → G :8462 | `LocalContext.NetId.HasValue` gate :8370 (always true); per-listener contexts :8398, :8441, :8484; one `WhenAll` after the three passes :8505; void, one fresh snapshot per pass | P0, re-checked; PerListener scope added |
| 10038 | `AfterSideTurnEnd` | :6080-6291 | `AfterSideTurnEnd`, `AfterSideTurnEndLate` | G :6146 → G :6265 | `LocalContext.NetId.HasValue` gate :6142 (always true); per-listener contexts :6170, :6225; `WhenAll` after each pass :6191, :6246; void, one fresh snapshot per pass | P0, re-checked; PerListener scope added |
| 10052 | `ModifyAttackHitCount` | sync | `ModifyAttackHitCount` | G :10055 | int chain; the game returns `decimal.op_Implicit(num)` :10068, the sim the int (exact) | P0, re-checked |
| 10071 | `ModifyBlock` | sync | `ModifyBlockAdditive`, `ModifyBlockMultiplicative` | G :10081 → G :10099 | enchantment pre-pass (additive then multiplicative on the running value) :10075-10080; then add (`!= 0` joins) and mul (`!= 1` joins) passes; `Math.Max(0, x)` :10118; `ref` modifiers | P0, re-checked; pre-pass added |
| 10121 | `ModifyCardBeingAddedToDeck` | sync | `TryModifyCardBeingAddedToDeck`, `TryModifyCardBeingAddedToDeckLate` | R :10124 → R :10142 | `Try…` true and `newCard != null` → card replaced and joins, ×2 :10130, :10148; `ref` modifying models | ported (round 3) |
| 10163 | `ModifyCardPlayCount` | sync | `ModifyCardPlayCount` | G :10167 | chain; joins when the int changes :10175; `ref` modifiers | P0, re-checked |
| 10188 | `ModifyCardPlayResultLocation` | sync | `ModifyCardPlayResultLocation` | G :10191 | chain; joins when the `CardLocation` record differs :10199; `ref` modifiers | phase 0b, re-checked |
| 10213 | `ModifyCardRewardAlternatives` | sync | `TryModifyCardRewardAlternatives` | R :10216 | returns the list of listeners whose `Try…` returned true :10222 | ported (round 3) |
| 10235 | `ModifyCardRewardCreationOptions` | sync | `ModifyCardRewardCreationOptions`, `ModifyCardRewardCreationOptionsLate` | R :10237 → R :10250 | chain ×2 | ported (round 3) |
| 10266 | `TryModifyCardRewardOptions` | sync | `TryModifyCardRewardOptions`, `TryModifyCardRewardOptionsLate` | R :10270 → R :10288 | `flag = flag \|\| modified` with every listener called; joins when true, ×2 :10276, :10294; `ref` modifiers | ported (round 3) |
| 10309 | `ModifyCardRewardUpgradeOdds` | sync | `ModifyCardRewardUpgradeOdds` | R :10312 | chain | ported (round 3) |
| 10328 | `ModifyDamage` | sync | `ModifyDamageAdditive`, `ModifyDamageMultiplicative`, `ModifyDamageCap` | R+C ×3 (in `ModifyDamageInternal` :11772, :11793, :11815) | enchantment pre-pass gated by `HasFlag(Additive)` / `HasFlag(Multiplicative)` :10332-10342; `CardPreviewMode.MultiCreatureTargeting` branch :10343-10410 dropped (rules callers pass `None`); `ModifyDamageInternal` :11766 add (`!= 0`), mul (`!= 1`), cap (min cap, joins when it lowers the value); `Math.Max(0, x)` :10416; `ref` modifiers = the internal list | P0, re-checked; pre-pass added, `cs == null` path added |
| 10422 | `ModifyEnergyCostInCombat` | sync | `TryModifyEnergyCostInCombat`, `TryModifyEnergyCostInCombatLate` | G :10429 → G :10442 | `originalCost < 0` returns it :10424-10427; `Try…(cost, out cost)` chain ×2 | P0, re-checked |
| 10458 | `ModifyKeywordsInCombat` | sync | `TryModifyKeywordsInCombat` | C :10460 | void; `Try…` result ignored :10466 | phase 0b, re-checked |
| 10475 | `ModifyEnergyGain` | sync | `ModifyEnergyGain` | G :10479 | chain; joins when `(int)` changes :10487; `ref` modifiers | P0, re-checked |
| 10501 | `ModifyGoldGained` | sync | `ModifyGoldGained` | R+C :10505 | chain; joins when `(int)` changes :10513; `ref` modifiers | ported (round 3) |
| 10527 | `ModifyExtraRestSiteHealText` | sync | `ModifyExtraRestSiteHealText` | R :10529 | chain (`LocString` → localization key) | ported (round 3) |
| 10545 | `ModifyGeneratedMap` | sync | `ModifyGeneratedMap` | R :10547 | chain, then returns `ModifyGeneratedMapLate(...)` :10561 (the dispatcher) | ported (round 3) |
| 10564 | `ModifyGeneratedMapLate` | sync | `ModifyGeneratedMapLate` | R :10566 | chain | ported (round 3) |
| 10583 | `ModifyHandDraw` | sync | `ModifyHandDraw`, `ModifyHandDrawLate` | G :10587 → G :10605 | chain ×2; joins when `(int)` changes :10595, :10613; `ref` modifiers | P0, re-checked |
| 10627 | `ModifyHpLost` | sync | `ModifyHpLostBeforeOsty`, `ModifyHpLostBeforeOstyLate`, `ModifyHpLostAfterOsty`, `ModifyHpLostAfterOstyLate` | R+C :10633 → R+C :10651 → R+C :10672 → R+C :10690 | chain per phase flag; joins when `decimal.Truncate` changes :10641; `ref` modifiers | P0, re-checked; `cs == null` path added |
| 10713 | `ModifyMaxEnergy` | sync | `ModifyMaxEnergy` | G :10716 | chain | P0, re-checked |
| 10732 | `ModifyMerchantCardCreationResults` | sync | `ModifyMerchantCardCreationResults` | R :10734 | void over the list argument | ported (round 3) |
| 10749 | `ModifyMerchantCardPool` | sync | `ModifyMerchantCardPool` | R :10751 | chain | ported (round 3) |
| 10767 | `ModifyMerchantCardRarity` | sync | `ModifyMerchantCardRarity` | R :10769 | chain | ported (round 3) |
| 10785 | `ModifyMerchantPrice` | sync | `ModifyMerchantPrice` | R :10787 | chain | ported (round 3) |
| 10803 | `ModifyNextEvent` | sync | `ModifyNextEvent` | R :10806 | chain | ported (round 3) |
| 10822 | `ModifyOddsIncreaseForUnrolledRoomType` | sync | `ModifyOddsIncreaseForUnrolledRoomType` | R :10824 | chain | ported (round 3) |
| 10840 | `ModifyOrbPassiveTriggerCount` | sync | `ModifyOrbPassiveTriggerCounts` | G :10844 | chain over `ModifyOrbPassiveTriggerCounts`; joins when the int changes :10852; `ref` modifiers | ported (round 3) |
| 10865 | `ModifyOrbValue` | sync | `ModifyOrbValue` | G :10868 | chain | ported (round 3) |
| 10884 | `ModifyPowerAmountGiven` | sync | `ModifyPowerAmountGivenAdditive`, `ModifyPowerAmountGivenMultiplicative` | G :10888 → G :10906 | add (`!= 0` joins) :10894 then mul (`!= 1` joins) :10912; `ref` modifiers | P0, re-checked |
| 10928 | `ModifyPowerAmountReceived` | sync | `TryModifyPowerAmountReceived` | G :10932 | `TryModifyPowerAmountReceived` true → value replaced and joins :10938-10941; `ref` modifiers | P0, re-checked |
| 10953 | `ModifyRestSiteHealAmount` | sync | `ModifyRestSiteHealAmount` | R :10956 | chain | ported (round 3) |
| 10972 | `ModifyRestSiteOptions` | sync | `TryModifyRestSiteOptions` | R :10975 | returns the list of listeners whose `Try…` returned true :10981 | ported (round 3) |
| 10994 | `ModifyRestSiteHealRewards` | sync | `TryModifyRestSiteHealRewards` | R :10997 | returns the list of listeners whose `Try…` returned true :11003 | ported (round 3) |
| 11016 | `ModifyRewards` | sync | `TryModifyRewards`, `TryModifyRewardsLate` | R :11019 → R :11035 | returns the list of listeners whose `Try…` / `Try…Late` returned true, one list over both passes :11025, :11041 | ported (round 3) |
| 11054 | `ModifyShuffleOrder` | sync | `ModifyShuffleOrder` | G :11056 | void over the list argument | P0, re-checked |
| 11071 | `ModifyStarCost` | sync | `TryModifyStarCost` | G :11078 | `originalCost < 0` returns it :11073-11076; `Try…(cost, out cost)` chain | P0, re-checked |
| 11094 | `ModifySummonAmount` | sync | `ModifySummonAmount` | G :11097 | chain | ported (round 3) |
| 11113 | `ModifyUnblockedDamageTarget` | sync | `ModifyUnblockedDamageTarget` | C :11116 | creature chain | P0, re-checked |
| 11132 | `ModifyUnknownMapPointRoomTypes` | sync | `ModifyUnknownMapPointRoomTypes` | R :11135 | chain from a copy `new HashSet<RoomType>(roomTypes)` :11134 | ported (round 3) |
| 11151 | `ModifyXValue` | sync | `ModifyXValue` | G :11154 | int chain | P0, re-checked |
| 11170 | `ShouldAddToDeck` | sync | `ShouldAddToDeck` | R :11172 | all-true gate, `out` preventer | ported (round 3) |
| 11193 | `ShouldAfflict` | sync | `ShouldAfflict` | G :11195 | all-true gate | ported (round 3) |
| 11214 | `ShouldAllowAncient` | sync | `ShouldAllowAncient` | R :11216 | all-true gate | ported (round 3) |
| 11235 | `ShouldAllowHitting` | sync | `ShouldAllowHitting` | G :11237 | all-true gate | P0, re-checked |
| 11256 | `ShouldAllowMerchantCardRemoval` | sync | `ShouldAllowMerchantCardRemoval` | R :11258 | all-true gate | ported (round 3) |
| 11277 | `ShouldAllowSelectingMoreCardRewards` | sync | `ShouldAllowSelectingMoreCardRewards` | R :11279 | any-true gate | ported (round 3) |
| 11298 | `ShouldAllowTargeting` | sync | `ShouldAllowTargeting` | G :11300 | all-true gate, `out` preventer | P0, re-checked |
| 11321 | `ShouldClearBlock` | sync | `ShouldClearBlock` | G :11323 | all-true gate, `out` preventer | P0, re-checked |
| 11344 | `ShouldCreatureBeRemovedFromCombatAfterDeath` | sync | `ShouldCreatureBeRemovedFromCombatAfterDeath` | C :11346 | all-true gate | P0, re-checked |
| 11365 | `ShouldDie` | sync | `ShouldDie`, `ShouldDieLate` | R+C :11367 → R+C :11384 | all-true gate ×2 (`ShouldDie`, `ShouldDieLate`), `out` preventer | P0, re-checked; `cs == null` path added |
| 11405 | `ShouldDisableRemainingRestSiteOptions` | sync | `ShouldDisableRemainingRestSiteOptions` | R :11407 | all-true gate | ported (round 3) |
| 11426 | `ShouldDraw` | sync | `ShouldDraw` | G :11428 | all-true gate, `out` modifier | P0, re-checked |
| 11449 | `ShouldEtherealTrigger` | sync | `ShouldEtherealTrigger` | G :11451 | all-true gate | P0, re-checked |
| 11470 | `ShouldFlush` | sync | `ShouldFlush` | G :11472 | all-true gate | P0, re-checked |
| 11491 | `ShouldGenerateTreasure` | sync | `ShouldGenerateTreasure` | R :11493 | all-true gate | ported (round 3) |
| 11512 | `ShouldGainStars` | sync | `ShouldGainStars` | G :11514 | all-true gate | ported (round 3) |
| 11533 | `ShouldPayExcessEnergyCostWithStars` | sync | `ShouldPayExcessEnergyCostWithStars` | G :11535 | any-true gate | P0, re-checked |
| 11554 | `ShouldPlay` | sync | `ShouldPlay` | G :11556 | all-true gate, `out` preventer, `AutoPlayType` | P0, re-checked |
| 11577 | `ShouldPlayerResetEnergy` | sync | `ShouldPlayerResetEnergy` | G :11579 | all-true gate | P0, re-checked |
| 11598 | `ShouldProceedToNextMapPoint` | sync | `ShouldProceedToNextMapPoint` | R :11600 | all-true gate | ported (round 3) |
| 11619 | `ShouldProcurePotion` | sync | `ShouldProcurePotion` | R+C :11621 | all-true gate | ported (round 3) |
| 11640 | `ShouldRefillMerchantEntry` | sync | `ShouldRefillMerchantEntry` | R :11642 | any-true gate | ported (round 3) |
| 11661 | `ShouldStopCombatFromEnding` | sync | `ShouldStopCombatFromEnding` | C :11663 | any-true gate | P0, re-checked |
| 11682 | `ShouldTakeExtraTurn` | sync | `ShouldTakeExtraTurn` | G :11684 | any-true gate | P0, re-checked |
| 11703 | `ShouldForcePotionReward` | sync | `ShouldForcePotionReward` | R :11706 | `flag = flag \|\| …`: listeners after the first true are not called, the iteration continues :11712 | ported (round 3) |
| 11722 | `ShouldAllowFreeTravel` | sync | `ShouldAllowFreeTravel` | R :11725 | `flag = flag \|\| …` as `ShouldForcePotionReward` :11731 | ported (round 3) |
| 11741 | `ShouldPowerBeRemovedOnDeath` | sync | `ShouldPowerBeRemovedOnDeath` | C :11747 | `Owner.CombatState == null` → true :11743-11746; all-true gate over the owner's combat | P0, re-checked |

## 7. Round-2 findings (`verify-notes-hooks.md`, review of `ef7c7b9`)

| # | Finding | Resolution |
| --- | --- | --- |
| 1 | [high] Combat listener list is powers-only; `IsLive` knows only powers | §1.1–§1.2 ported in full: allies then enemies, powers, monster listener, the active player's relics / potions / orbs / pile cards with affliction and enchantment, then run modifiers; per-kind liveness at yield. Tests with a double of every kind (§8) |
| 2 | [high] `ModifyBlock` / `ModifyDamage` skip the enchantment pre-pass | Ported verbatim (§4.4, §B12 A9); arithmetic and the `ModifyDamageHookType` gating tested with an enchantment double |
| 3 | [medium] 71 dispatcher stubs and `IterateRunHookListeners(rs, cs, buffer)` throw; `ThrowNoAttachedCombat` throws for `cs == null` | All 148 dispatchers ported (§6); the run iterator implemented (§1.4) including `cs == null`; the both-scope dispatchers dispatch the run list without a combat for `cs == null` (§5.1). `Hook.cs` has no `NotImplementedException` left |
| 4 | [medium] The six per-listener dispatchers open no `PerListener` context; join structure unrecorded | `using var scope = …PushContext(ChoiceContextKind.PerListener)` around each listener call in all six (§4.5, phase 0b made `PushContext` real); the game's per-dispatcher joins recorded in §4.5; test checks kind and depth for every listener of the six and that other dispatchers keep the caller's context |
| 5 | [medium] Side-turn forwarding overloads pass live side lists | Kept by contract (§B12 A17: the loop passes snapshots, the integrator deletes overloads without callers); this track adds no caller; why it is unobservable at this commit in §5.2 |
| 6 | [low] `hooks.md` missing; dropped parameters undocumented: (1) `CardPreviewMode`, (2) `out` lists are new lists while the sim's `ref` lists append, (3) `ModifyAttackHitCount` int vs decimal, (4) the run source's deck prefix and deferred combat build | This file: (1) §4.4, (2) the `ref` dispatchers now reset to `null` at entry, so they never append to a caller's list (§4.2, test), (3) §4.7, (4) ported exactly rather than argued inert (§1.4, test) |
| 7 | [low] `ArrayPool<AbstractModel>.Shared` is static mutable state | Removed; entries live in a stack inline buffer with heap overflow (§3); a test asserts the area has no static field other than the immutable mask tables and masks |
| 8 | [high] Hooks deliverables absent (`HookMask.cs`, `HookTests.cs`, `hooks.md`) | Delivered |
| 9 | [high] Only powers receive hooks; (1) `IsLive` uses `CombatStatus.IsAttached`, which ignores pets; (2) the run source omits the deck prefix | Same as 1; (1) liveness uses its own attachment test over `cs.Allies` and `cs.Monsters` (`ListenerSnapshot.IsInCombat`, also used by `ShouldPowerBeRemovedOnDeath`), so pets count without waiting for the pipeline's A19 change; (2) §1.4 |
| 10 | [high] Stubbed dispatchers on hot paths (`AfterCardChangedPiles`, `ModifyOrbValue`, potions, orbs, stars, summons, afflictions); the P0 `IterateRunHookListeners(CombatState, buffer)` overload shares its name with the contract member | All ported; the P0 overload now forwards to `(cs.Run, cs, buffer)` and fills the same run list |
| 11 | [medium] Per-listener contexts missing, blocked by the `PushContext` stub | `PushContext` / `ContextScope` landed in phase 0b (scaffold-w1 §16.1); wraps added (row 4) |
| 12 | [medium] Test doubles of orbs, pets, enchantments and afflictions cannot be placed in a state; `ListenerSnapshot` is internal | Orbs (`OrbQueue.AddCapacity` / `TryEnqueue`), pets (`CombatState.AddAlly`) and bound enchantments (`EnchantmentModel.ApplyInternal`) cross other owners' stubs: those tests are written in full inside `W1Pending.Run` (4 PENDING lines until character-mechanics, state and cards-kernel land) and were run once against local stand-in bodies in a scratch copy of the tree, all passing (§8). No affliction attach member exists in the contract (the game's is `CardModel.AfflictInternal`, `Models/CardModel.cs:3150-3167`); `HookTests.BindAffliction` sets the affliction's one `CardInstance` field by reflection and asserts there is exactly one, so the affliction order and liveness tests run now. `[assembly: InternalsVisibleTo("Sts2Sim.Tests")]` is declared in `HookMask.cs` |
| 13 | [low] B3 wording would visit modifiers twice in combat | Contract fixed by §B12 A8; the code follows `Runs/RunState.cs:866-880` (test) |
| 14 | [low] Live-list overloads record no `FidelityFlag` | §5.2 |
| 15 | [low] `ListenerSnapshot` uses the process-wide `ArrayPool` | Same as 7 |

Also addressed: scaffold-w1 §14 item 15 (the `cs == null` path and the run source's deck cards and modifiers) and the
phase-0b hot-path list (§16.2): every listed body re-checked against its state machine, unchanged apart from the masked
snapshot.

## 8. Tests (`sim/Sts2Sim.Tests/HookTests.cs`, filter `Hooks`)

Doubles: `PowerProbe`, `QuietPowerProbe` (powers with different override sets), `RelicProbe`, `PotionProbe`,
`OrbProbe`, `CardListenerProbe` (card listener adapter), `MonsterListenerProbe`, `ModifierProbe`,
`EnchantmentProbe` (pre-pass virtuals and hooks), `AfflictionProbe`; each override logs `tag:virtual` and some take
side effects through delegates that receive the dispatching state.

| Test | Checks |
| --- | --- |
| `CombatListOrderFollowsTheGame` | the full combat list and a two-pass dispatch over powers, relics (one melted), potion slots (one empty), cards in all five piles (one without listener), two monsters with powers and listeners, two modifiers |
| `InactivePlayerContributesOnlyItsPowersWhichAreNotLive` | an inactive player lists only its powers, which fail liveness |
| `RemovedPowerRelicCardAndMonsterAreSkippedWithinThePass` | removed relic, used potion, removed card, monster removed from the combat are skipped at yield; a power added in pass 1 runs only in pass 2 |
| `PowerRemovedFromItsListIsStillLiveWhileItsOwnerIsAttached` | the game's power rule (§1.2) |
| `PlayerDeactivatedMidPassSkipsItsRemainingListeners` | liveness re-evaluated per element |
| `RelicMeltedMidPassIsStillVisitedUntilTheNextSnapshot` | `IsMelted` is a build-time filter |
| `GuardedSourceIsSilentOnceTheCombatIsOverOrEnding` | guarded silence, `IsStarting` exemption, unguarded C and R sources, the guarded and combat fills |
| `GuardIsEvaluatedOncePerPass` | the pass that ends the combat finishes, the next guarded pass is silent |
| `EachPassTakesAFreshSnapshot` | listeners added in pass 1 run in pass 2 |
| `ModifyBlockAppliesTheEnchantmentPrePassFirst` | (5 + 3) × 1.5, + 2, × 0.75 = 10.5; call order and arguments; the enchantment never joins the fresh modifier list; an unbound enchantment still applies |
| `ModifyDamagePrePassIsGatedByHookType` | All / Additive / Multiplicative / Cap / Additive+Cap |
| `ModifyDamageListenerPassesFollowThePrePass` | pre-pass, additive, multiplicative, cap, modifier list |
| `SixPerListenerDispatchersPushAPerListenerContext` | context kind and depth per listener in all six, popped afterwards, caller's context elsewhere |
| `RunListWithCombatVisitsDeckCardsThenTheCombatListOnce` | deck listeners, then the combat list; no duplicate relics, potions, modifiers |
| `RunListWithoutCombatVisitsDeckRelicsPotionsModifiers` | the no-combat list; run-only dispatchers with an attached combat; the `cs == null` path; inactive players |
| `RunSourceBuildsTheCombatListAfterTheDeckListeners` | the deferred combat build (§1.4 item 4) |
| `PreventerAndModifierFiltersUseLiveMembership` | `ShouldDie` preventer, `AfterPreventingDeath` membership, `AfterModifyingDamageAmount` filter |
| `RefModifierListsStartFresh` | `ref` lists reset at entry, no allocation without a modifier |
| `GatePatternsFollowTheGame` | `flag \|\|` folds, any-true and all-true early returns |
| `RunOnlyValueChainsAndModifierLists` | run-only chains in run-list order and the run-layer shells |
| `DetachedCombatIsRejectedByBothScopeDispatchers` | `cs` must be `null` or `rs.Combat` |
| `OverflowBeyondTheInlineBufferKeepsOrder` | 23 listeners through the heap overflow |
| `NestedDispatchesKeepTheirOwnSnapshots` | re-entrant dispatches nested two deep |
| `OwnerlessCardListenersFollowTheirPileHolder` | §1.2 card row |
| `AfflictionListenerFollowsItsCard` | card → affliction order, affliction liveness, an unbound affliction |
| `MaskBitsMatchTheOverriddenVirtuals` | 182 virtuals ↔ 182 `HookId`s; base-class, sealed and hiding cases; every concrete model type of Core and Tests against an independent reflection check |
| `MaskIsCachedPerInstanceAndSharedByClones` | Core table lookup, clone sharing, readonly fields, hash unchanged |
| `PipelineKeepsNoStaticMutableState` | §3 |
| `MaskedSequenceEqualsFilteredFullSequence` | 8 random states × 6 rounds of direct list writes (relics, potion slots, piles, powers, melt flags, activity, modifiers) × 4 sources × 182 hooks: the masked list equals the full list filtered by the independent override check |
| `MaskedEqualsFullOnRandomStates` | 24 random states, each built twice, one with every listener forced to `HookMask.All` (the full iteration); 40 random dispatches over 22 dispatcher patterns with interleaved direct writes and side-effecting listeners: equal call logs, results and state hashes |
| `CardThenAfflictionThenEnchantment` (W1Pending, cards-kernel) | card → affliction → enchantment order, liveness via the card, deck card + enchantment in the run list |
| `OrbsFollowPotionsInQueueOrder` (W1Pending, character-mechanics) | orbs after potions in queue order, orb liveness, `ModifyOrbValue` chain |
| `PetsComeAfterThePlayerAndBeforeTheEnemies` (W1Pending, state) | allies before enemies, a pet's monster listener, pet powers independent of the player's activity |
| `MaskedEqualsFullWithOrbsEnchantmentsAndPets` (W1Pending, all three) | the masked/full comparison with orbs, a bound enchantment and a pet |

The four W1Pending tests were run in a scratch copy of the tree in which `OrbQueue.AddCapacity` / `TryEnqueue`
(`Entities.Orbs/OrbQueue.cs:414-417`, `:429-445`), `EnchantmentModel.ApplyInternal` (`Models/EnchantmentModel.cs:337-348`) and
`CombatState.AddAlly` had stand-in bodies: all hook tests passed, 0 PENDING. Three deliberate mutations of the snapshot
(mask the wrong hook for one pass, drop the power attachment test, build the combat part of a run iteration before the
deck part is exhausted) each made the expected tests fail (8, 1 and 1 failures).

A script cross-checked the port mechanically: for each of the 148 game dispatchers it extracted, from the state
machine or sync body, the source and virtual of every pass (the `GetEnumerator()` / `Enumerable.Contains` sites) and
compared them with the `ListenerSnapshot.Guarded/Combat/Run(…, HookId.X)` sequence of the sim dispatcher
(`ModifyDamageInternal` for `ModifyDamage`, one pass for `ModifyGeneratedMap`, whose late pass is the
`ModifyGeneratedMapLate` dispatcher) and checked that each pass calls its virtual: 148 checked, 0 differences.

## 9. Gates (this worktree, on top of `263810d`)

| Gate | Result |
| --- | --- |
| `dotnet build --no-incremental sim/Sts2Sim.sln`, Debug and Release | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 406 passed, 2 failed, 408 total (the 374 of phase 0b plus the 34 `Test_Hooks_*`); the two failures are the known content gates `W1GateTests.Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered` (other owners' pilots); `PENDING total: 4`, all four from this file's `W1Pending.Run` tests (§8) |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundle set) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |
| Bench | §9.1 |

W1 gate-suite seam 1 (`integration.md` §8.3: `EquipPlayer` writes `Player.Relics` and `PotionSlots` directly) needs no
change: nothing is cached (§2.3), so directly written relics and potions are listeners from the next pass on.

### 9.1 Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`, 1 thread)

`263810d` from `git archive` and this tree, 12 interleaved runs each with 4 s of playouts, one session with load
average 5.2–9.4 on 12 cores (other processes running); medians, per-run range in brackets:

| Tree | decisions/s | `Clone()` µs | `StateHash()` µs |
| --- | --- | --- | --- |
| `263810d` | 228 176 [162 113–290 666] | 5.444 [2.848–16.156] | 2.700 [1.404–5.692] |
| this tree | 153 967 [117 753–200 471] | 6.132 [3.075–9.890] | 2.516 [2.117–3.369] |

The machine was far from quiet (the parent's best run is 0.41× of the recorded P0 705 048 decisions/s, 2.53 µs
clone, 0.87 µs hash), so only same-round ratios carry information: decisions/s 0.66× (median of the 12 per-round
ratios, range 0.49–0.85; ratio of medians 0.67, of best runs 0.69). An earlier, quieter session in this worktree gave
the same picture (679k decisions/s for the parent, 435–438k for this tree at the final inline capacity, §3).
`Clone()` and `StateHash()` show no change beyond the noise: the only state change is one reference per model
(`AbstractModel._overriddenHooks`, copied by `MemberwiseClone`, not hashed).

The throughput cost is the walk the game does. The parent listed only the player's and the monsters' powers (and
nothing at all when there were none); this tree visits every listener slot the game visits on every pass: relics,
potion slots, orbs, the card, affliction and enchantment slots of every card in the five piles, monster listeners and
modifiers (§1.1). A P0 decision runs about 56 passes (§3) and with P0 content nearly every slot is empty, so the added
time is the walk itself (about 0.8 µs per decision at the quieter session's rates), not listener calls. The exact ways
to remove it need other owners (§10 item 7). The Part B gates (≥ 300 decisions/s/core, `Clone()` ≤ 1 ms) pass by far.

## 10. Open questions and contract change requests

1. **Affliction attach member.** No contract member binds an affliction to a card (the scaffold's `AfflictionModel`
   sets its card only through `CloneFor` of a bound model). The game's path is `CardModel.AfflictInternal`
   (`Models/CardModel.cs:3150-3167`: `Affliction = affliction; Affliction.Card = this; Amount = (int)amount`).
   Request: cards-kernel ports it with the game's name on `CardInstance` (and `EnchantInternal`, `:3137-3148`, beside
   `EnchantmentModel.ApplyInternal`); the integrator may then switch `HookTests.BindAffliction` from its reflection
   binding to it.
2. **`design-combat-kernel.md` §2.2** says the six per-listener dispatchers `WhenAll` their paused listeners at the end
   of each pass; only `AfterSideTurnEnd` does, `BeforeFlush` and `BeforeSideTurnEnd` join once after all passes, and
   `AfterDeath`, `AfterDiedToDoom`, `BeforeSideTurnStart` never join (§4.5). Doc fix for its owner.
3. **Listener-adapter masks.** A listener adapter (`MonsterState.Listener`, `CardInstance.Listener`) gets the mask of
   its adapter type, i.e. every hook the adapter forwards, whether or not the wrapped def overrides it. Exact, but a
   monsters-kernel or cards-kernel adapter that forwards all hooks makes every pass visit every adapted monster and
   card. If W3 content makes this measurable, `AbstractModel` can gain a protected virtual that narrows the mask per
   instance (computed once, cached like the type mask).
4. **Ownerless combat cards** (§1.2) are a sim-only state that disappears once every creation path sets `Owner`
   (§B12 A23); the holder fallback then never runs.
5. **Detaching a combat** (scaffold-w1 §14 item 16): both-scope dispatchers require `cs == null` or `cs == rs.Combat`
   (§5.1); W2 clears `rs.Combat` only after the last dispatch that passes the combat.
6. **Deferred listener prompts** (D2): the sim runs them inline under the `PerListener` frame; exact emulation of the
   game's continuation order (§4.5 joins) stays a backlog item gated on a recording.
7. **Walk cost.** Each pass walks every listener slot (§2.3); with P0 content the cost is the pile walk (about ten
   cards × three slots per pass). Two exact speed-ups need other owners: (a) a structural version on every listener
   container (powers, relics, potion slots, orb queue, piles, deck, monsters, allies, modifiers, the three card slots,
   the monster listener, `IsMelted`, `IsActiveForHooks`), which requires replacing the public `List` / array members
   with owned types that count writes, so that a cached ordered array per state can be validated in O(1) (§B12 A20's
   condition); (b) skipping a pass outright when no loaded model type overrides its virtual, which needs a type
   universe that stays complete when assemblies load later, i.e. static state beyond the registries' build-once
   pattern. Neither is done here.
8. **Two-argument side-turn callers in tests.** Deleting the `(cs, side)` overloads under A17 breaks
   `Sts2Sim.Tests/PipelineTests.cs` (13 calls, `:546-822`) and `Sts2Sim.Tests/WurmBeetleTests.cs` (7 calls,
   `:498-525`) besides the loop (§5.2). Request: the integrator moves them to the three-argument overloads together
   with the loop (`cs.Allies` / `cs.Monsters` reproduces today's behaviour exactly), or keeps the overloads until those
   owners do.
