# Monsters spec: `SLIMES_WEAK` (Leaf/Twig slimes) and the `SLIMED` status card

2026-10-30. Rule truth: decompile tree (game v0.111.0), cited as `<ns-dir>/<File>.cs:<line>` with the
`MegaCrit.Sts2.Core.` prefix dropped. IL checks on `output/server-bootstrap/linux-assembly/sts2.dll`
(`ilspycmd -il`). The v0.107.1 tree (`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/`,
cited `v107:`) has the same rules for every file below (checked line by line, §8), so `legacy_v18`
recordings are valid oracles too. Follows the `NIBBITS_WEAK` pattern of `monsters.md`; generic machinery
(move state machine, spawn, HP roll, lifecycle, encounter-private `Rng`) is specified there and in
`rng-encounter-private.md` and is not repeated.

## 1. Files

| Sim type | File | Game source |
| --- | --- | --- |
| `SlimesWeak : EncounterDef`, id `SLIMES_WEAK` | `Monsters/Encounters/SlimesWeak.cs` | `Models.Encounters/SlimesWeak.cs` |
| `LeafSlimeS : MonsterDef`, id `LEAF_SLIME_S` | `Monsters/Defs/LeafSlimeS.cs` | `Models.Monsters/LeafSlimeS.cs` |
| `TwigSlimeS : MonsterDef`, id `TWIG_SLIME_S` | `Monsters/Defs/TwigSlimeS.cs` | `Models.Monsters/TwigSlimeS.cs` |
| `LeafSlimeM : MonsterDef`, id `LEAF_SLIME_M` | `Monsters/Defs/LeafSlimeM.cs` | `Models.Monsters/LeafSlimeM.cs` |
| `TwigSlimeM : MonsterDef`, id `TWIG_SLIME_M` | `Monsters/Defs/TwigSlimeM.cs` | `Models.Monsters/TwigSlimeM.cs` |
| `Slimed : CardDef`, id `SLIMED`; `Slimed.AddToCombatAndPreview` | `Cards/Defs/Slimed.cs` | `Models.Cards/Slimed.cs`; `Commands/CardPileCmd.cs` (`AddToCombatAndPreview<Slimed>`) |
| tests `Test_SlimesWeak_*` | `Sts2Sim.Tests/SlimesWeakTests.cs` | §9 |

Ids are `StringHelper.Slugify(ClassName)` (`Models/ModelDb.cs:485-488`, `Helpers/StringHelper.cs:68-73`).
Move ids are the string literals of the models. No slime has per-instance fields, so none has a
`MonsterExtraState`; no slime overrides a hook, `AfterAddedToRoom` or `BeforeRemovedFromRoom`; none reads
the per-monster `Rng` (`rng-encounter-private.md` §4). `TakeDamageSfxType` (`LeafSlimeS.cs:189`,
`TwigSlimeS.cs:92`, `LeafSlimeM.cs:227`, `TwigSlimeM.cs:230`) and `HasPhobiaSpineSkin`
(`TwigSlimeM.cs:226`) are cosmetic and not ported. No slime applies a power: no power def was added.

## 2. Encounter `SLIMES_WEAK`

| Fact | Citation |
| --- | --- |
| `_smallSlimes = [LeafSlimeS, TwigSlimeS]` (order matters for the draws) | `Models.Encounters/SlimesWeak.cs:12-16` |
| `_mediumSlimes = [LeafSlimeM, TwigSlimeM]` | `SlimesWeak.cs:18-22` |
| `RoomType.Monster`, `Tags = [Slimes]`, `IsWeak => true` | `SlimesWeak.cs:24`, `:26`, `:28` |
| no `Slots` override (empty), every spawn slot `null` | `Models/EncounterModel.cs:133`; `SlimesWeak.cs:58-60` |
| `small = ToList(_smallSlimes)`; `first = Rng.NextItem(small)`; `small.Remove(first)`; `second = Rng.NextItem(small)` | `SlimesWeak.cs:54-57`; IL `SlimesWeak::GenerateMonsters` `IL_0006`-`IL_0032` |
| result list `[first, Rng.NextItem(_mediumSlimes), second]`, each `ToMutable()`, slot `null`; the medium draw is the **third** draw | `SlimesWeak.cs:58-60`; IL `IL_0033`-`IL_0073` |
| `Rng` = the encounter-private `Rng` (seed `Seed + TotalFloor + XXH64(id)`; legacy `(uint)((int)Seed + TotalFloor + HashOld(id))`) | `Models/EncounterModel.cs:202-216`; `rng-encounter-private.md` §3 |
| `NextItem(IEnumerable)`: empty → `default` without a draw; else `NextInt(0, count)` | `Random/Rng.cs:213-223` |

Sim: `GenerateMonsters(cs, rng)` builds `small` as a `List<MonsterDef>` from the registry (same order),
draws `first = rng.NextItem(small)` (`NextInt(0, 2)`), removes it, draws `second = rng.NextItem(small)`
(`NextInt(0, 1)`: always the remaining slime, but one draw and `Counter + 1`), then
`middle = rng.NextItem(medium)` (`NextInt(0, 2)`), and returns `[first, middle, second]` with slot `null`
and no `Configure`. Exactly three encounter-`Rng` draws; no run stream moves. The one-argument overload
throws `EncounterRngRequired()` (the encounter cannot run without its seed). Consequence: every
`SLIMES_WEAK` has both small slimes (Leaf S and Twig S, in either order) and one medium slime, creation
order `small1, medium, small2`.

Spawn (unchanged machinery, `monsters.md` §4): creatures are created and added one at a time in list
order (`Rooms/CombatRoom.cs:381-394`), so `CombatId` 1, 2, 3 follow that order and each creature rolls its
HP with **one `Niche` draw** (`Combat/CombatState.cs:717`), excluding the `MaxHp` of the enemies already
created. The small ranges overlap at one value (A0: Leaf S 11-15 and Twig S 7-11 share 11; A8+: 12-16 and
8-12 share 12), so the second small slime never repeats the first one's HP when it rolled the shared value.

## 3. Monster constants

`cs.GetValueIfAscension(level, a, b)` = `AscensionHelper.GetValueIfAscension` (`Helpers/AscensionHelper.cs:14-21`);
`ToughEnemies = 8`, `DeadlyEnemies = 9` (`Entities.Ascension/AscensionLevel.cs:13-14`). HP switches at A8,
damage at A9 (A8 keeps base damage).

| Sim member | A0 | Asc value | Level | Citation (`Models.Monsters/`) |
| --- | --- | --- | --- | --- |
| `LeafSlimeS.MinInitialHp` | 11 | 12 | ToughEnemies | `LeafSlimeS.cs:183` |
| `LeafSlimeS.MaxInitialHp` | 15 | 16 | ToughEnemies | `LeafSlimeS.cs:185` |
| `LeafSlimeS.TackleDamage` | 3 | 4 | DeadlyEnemies | `LeafSlimeS.cs:187` |
| `LeafSlimeS.GoopAmount` (`_goopAmount`) | 1 | – | – | `LeafSlimeS.cs:181`; used as the literal `1` at `:86`, `:195` (IL `ldc.i4.1`) |
| `TwigSlimeS.MinInitialHp` | 7 | 8 | ToughEnemies | `TwigSlimeS.cs:86` |
| `TwigSlimeS.MaxInitialHp` | 11 | 12 | ToughEnemies | `TwigSlimeS.cs:88` |
| `TwigSlimeS.TackleDamage` | 4 | 5 | DeadlyEnemies | `TwigSlimeS.cs:90` |
| `LeafSlimeM.MinInitialHp` | 32 | 33 | ToughEnemies | `LeafSlimeM.cs:221` |
| `LeafSlimeM.MaxInitialHp` | 35 | 36 | ToughEnemies | `LeafSlimeM.cs:223` |
| `LeafSlimeM.ClumpDamage` | 8 | 9 | DeadlyEnemies | `LeafSlimeM.cs:225` |
| `LeafSlimeM.StickyAmount` (`_stickyAmount`) | 2 | – | – | `LeafSlimeM.cs:219`; literal at `:190`, `:233` |
| `TwigSlimeM.MinInitialHp` | 26 | 27 | ToughEnemies | `TwigSlimeM.cs:222` |
| `TwigSlimeM.MaxInitialHp` | 28 | 29 | ToughEnemies | `TwigSlimeM.cs:224` |
| `TwigSlimeM.ClumpDamage` (pokey pounce) | 11 | 12 | DeadlyEnemies | `TwigSlimeM.cs:228` |
| `TwigSlimeM.StickyAmount` (`_stickyAmount`) | 1 | – | – | `TwigSlimeM.cs:220`; literal at `:191`, `:236` |
| `TwigSlimeM.PokeyPounceMaxRepeats` | 2 | – | – | `TwigSlimeM.cs:238` |

## 4. Move state machines

All moves target the player creatures passed by `PerformMove` (`monsters.md` §5); attacks use
`DamageCmd.Attack(dmg).FromMonster(self).Execute()`, which targets all opponents
(`Commands.Builders/AttackCommand.cs:781-792`). Status moves call `Slimed.AddToCombatAndPreview(cs, targets,
PileType.Discard, n, creator: null)` (§6). Weight rules, the `NextFloat` walk and the first-move guard are
`monsters.md` §3 (`RandomBranchState.cs:130-194`, `MonsterMoveStateMachine.cs:76-104`); every evaluated
random branch costs exactly one `MonsterAi` `NextFloat` (`Models/MonsterModel.cs:719-722`).

### 4.1 `LeafSlimeS` (`LeafSlimeS.cs:191-203`)

| State | Kind | Intents | Effect | Follow-up | Citation |
| --- | --- | --- | --- | --- | --- |
| `TACKLE_MOVE` | Move | `SingleAttackIntent(TackleDamage)` | attack `TackleDamage` | `RAND` | `:194`, `:196`; body `:143-147` |
| `GOOP_MOVE` | Move | `StatusIntent(1)` | 1 `SLIMED` → discard, bottom | `RAND` | `:195`, `:196`; body `:68`, `:84-86` |
| `RAND` | RandomBranch, **initial** | – | `AddBranch(TACKLE, CannotRepeat)`, `AddBranch(GOOP, CannotRepeat)`, both weight 1, cooldown 0 | – | `:196-198`, `:202`; IL `AddBranch(MonsterState, MoveRepeatType)` with `2` (`MoveRepeatType.cs`: `CannotRepeat = 2`) |

Registration order `TACKLE_MOVE, GOOP_MOVE, RAND` (`:199-201`). `RAND` is not logged
(`RandomBranchState.cs:51`), so `StateLog` starts empty. Behaviour:
- first `RollMove` (`AfterCreatureAdded`, combat start): the current state is `RAND`, not a move, so the
  first-move guard does not apply (`MonsterMoveStateMachine.cs:84`): **one `MonsterAi` draw at setup**,
  `num = NextFloat(2)`, `TACKLE` iff `num - 1 <= 0`, else `GOOP`.
- the round-1 `PrepareForNextTurn` re-roll draws nothing (current is a move, no move performed yet).
- every later roll: one draw, `max = 1` (the last move has weight 0), so the opener is followed by strict
  alternation `TACKLE, GOOP, TACKLE, ...`. Kept on purpose: if `NextFloat(1)` returns exactly `0f`
  (probability `2^-53`) the first branch wins with weight 0 and `TACKLE` repeats, as in the game.

### 4.2 `TwigSlimeS` (`TwigSlimeS.cs:94-101`)

`TACKLE_MOVE` (`SingleAttackIntent(TackleDamage)`, body `:48-52`), `FollowUpState = itself` (`:98`), the only
registered state and the initial one (`:99-100`). Logged at construction (a move). Tackles every turn and
never draws.

### 4.3 `LeafSlimeM` (`LeafSlimeM.cs:229-239`)

| State | Intents | Effect | Follow-up | Citation |
| --- | --- | --- | --- | --- |
| `CLUMP_SHOT` | `SingleAttackIntent(ClumpDamage)` | attack `ClumpDamage` | `STICKY_SHOT` | `:232`, `:235`; body `:54-58` |
| `STICKY_SHOT` (**initial**) | `StatusIntent(2)` | 2 `SLIMED` → discard, bottom | `CLUMP_SHOT` | `:233-234`, `:238`; body `:172-173`, `:189-190` |

Registration `CLUMP_SHOT, STICKY_SHOT` (`:236-237`). Sequence `STICKY, CLUMP, STICKY, ...`; never draws.

### 4.4 `TwigSlimeM` (`TwigSlimeM.cs:232-244`)

| State | Intents | Effect | Follow-up | Citation |
| --- | --- | --- | --- | --- |
| `POKEY_POUNCE_MOVE` | `SingleAttackIntent(ClumpDamage)` | attack `ClumpDamage` (method `ClumpShotMove`) | `RAND` | `:235`, `:237`; body `:55-59` |
| `STICKY_SHOT_MOVE` (**initial**) | `StatusIntent(1)` | 1 `SLIMED` → discard, bottom | `RAND` | `:236-237`, `:243`; body `:173-174`, `:190-191` |
| `RAND` | – | `AddBranch(POKEY, 2)` = `CanRepeatXTimes`, `maxTimes 2`, weight 1, cooldown 0; `AddBranch(STICKY, CannotRepeat)` | – | `:238-239`; IL `IL_0070`-`IL_007b`: `AddBranch(MonsterState, int32)` with `2` → `RandomBranchState.cs:120-123` → `:110-113` → `:90-93` → `:77-88`; then `AddBranch(MonsterState, MoveRepeatType)` with `2` = `CannotRepeat` |

Registration `POKEY_POUNCE_MOVE, STICKY_SHOT_MOVE, RAND` (`:240-242`). Behaviour:
- opener `STICKY_SHOT_MOVE` (logged at construction; the setup roll and the round-1 re-roll draw nothing).
- second move: one draw with `max = 1` (sticky cannot repeat, pokey has weight 1 while `StateLog.Count < 2`),
  so it is always `POKEY_POUNCE_MOVE`.
- afterwards one draw per roll: pokey has weight 0 only after two pokeys in a row, sticky has weight 0 only
  right after a sticky; with both at weight 1, `NextFloat(2)` picks pokey iff `num - 1 <= 0`.

The sim builds these graphs with the existing `MoveStateDef`/`RandomBranchStateDef` (`rand.AddBranch(pokey,
2)` resolves to the sim's `(AiStateDef, int maxRepeats, float weight = 1f)` overload, i.e. the game's
`(MonsterState, int maxRepeats)` chain). Damage values are captured at graph build and re-read in the move
bodies from the same ascension switch (both fixed per combat).

## 5. Setup draw budget (the `MonsterAi` validator)

At combat start, `AfterCreatureAdded` rolls each monster in creation order (`Combat/CombatManager.cs:4466-4478`,
`monsters.md` §5): Leaf S draws 1, Twig S 0, Leaf M 0, Twig M 0. Every `SLIMES_WEAK` contains exactly one
Leaf S, so **setup consumes exactly 1 `MonsterAi` draw**, at Leaf S's position in the enemy list; together
with the verified first-combat facts (all sim streams at counter 0 except `Niche`) the opener of Leaf S is
`NextFloat(2)` of a fresh `MonsterAi` stream. From player turn 2 on, `PrepareForNextTurn` runs in enemy-list
order (`Combat/CombatManager.cs:5315-5323`): Leaf S and (after its opener) Twig M draw one each, the others
none; dead slimes no longer roll. Setup also consumes 3 `Niche` draws (HP, creation order) and the encounter
`Rng`'s 3 draws, which belong to no run stream.

## 6. `SLIMED` status card and how slimes add it

### 6.1 Card values (`Models.Cards/Slimed.cs`)

| Fact | Sim | Citation |
| --- | --- | --- |
| cost 1, `CardType.Status`, `CardRarity.Status`, `TargetType.None` | `base(CardId, 1, Status, Status, None)` | `Slimed.cs:95-98` |
| `MaxUpgradeLevel => 0` (cannot be upgraded; an upgraded `CardSpec` is rejected by `CardInstance.ResolveDef`) | override | `Slimed.cs:89` |
| `CanonicalVars = [CardsVar(1)]`; `CardsVar` is a `DynamicVar` named `"Cards"` | `new DynamicVar(DynamicVarSet.CardsName, 1m)` (the sim has no `CardsVar` class; name and value are what matter) | `Slimed.cs:91`; `Localization.DynamicVars/CardsVar.cs:5-11` |
| `CanonicalKeywords = [Exhaust]` | `Keywords` | `Slimed.cs:93` |
| no tags; `CanBeGeneratedInCombat`/`ByModifiers` keep the `CardModel` defaults (`true`) | defaults | `Models/CardModel.cs:2043`, `:2045` |
| `OnPlay`: VFX, then `CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner)` | `CardPileCmd.Draw(cs, card.Vars.Cards.BaseValue, cs.Player)` | `Slimed.cs:50-55` |
| no `AfterCreated` override (the base is empty; only `SpoilsMap` overrides it) | – | `Models/CardModel.cs:2884-2886`; `Models.Cards/SpoilsMap.cs:146` |
| result pile: `Exhaust` keyword → exhaust pile (sim `CardDef.GetResultLocationForCardPlay`) | inherited | cards spec |

Playable like any card (status cards are not unplayable, `Models/CardModel.cs:3330-3360`). Playing it costs 1
energy, draws 1 card (no `Rng` unless the draw pile is empty and a reshuffle happens) and exhausts it.

### 6.2 `CardPileCmd.AddToCombatAndPreview<Slimed>` → `Slimed.AddToCombatAndPreview`

Call sites (all `PileType.Discard` = 3, `creator = null`, default position `Bottom` = 1, IL-checked):
`LeafSlimeS.cs:86` (count 1, IL `IL_00a8`-`IL_00ac`), `LeafSlimeM.cs:190` (count 2, IL `IL_0165`-`IL_0169`),
`TwigSlimeM.cs:191` (count 1, IL `IL_0165`-`IL_0169`).

| Game step | Sim | Citation (`Commands/CardPileCmd.cs`) |
| --- | --- | --- |
| `IEnumerable<Creature>` overload: per target, in order, the single-target overload | loop over `targets` | `:4116-4131`; body `:1290-1318` |
| `player = target.Player ?? target.PetOwner` | `targets[t] as PlayerState`, else `NotSupportedException` (pets are not modelled) | `:1411` |
| player dead → nothing, not even card creation | `if (player.IsDead) continue;` | `:1412`, `:1418` |
| for `i < count`: `card = target.CombatState?.CreateCard<T>(player)`; non-null → `await AddGeneratedCardToCombat(card, pileType, creator, position)` (create and add interleave) | `CardInstance.Create(cs, SLIMED)` (allocates the next instance id), then `CardPileCmd.AddGeneratedCardToCombat(cs, card, pileType, creator, position)` | `:1448-1466`, `:1456`; `CreateCard` = `ToMutable`, `AddCard(card, owner)`, `AfterCreated` (`Combat/CombatState.cs:666-677`, `:686-690`, `:982-991`) |
| preview (`PreviewCardPileAdd`, `Cmd.Wait(1f)`; hand: `Cmd.Wait(0.1f)`) | dropped | `:1468-1494` |
| `AddGeneratedCardsToCombat`: return unless `IsInProgress` (the card was already created); throw if it has a pile or the pile is not a combat pile; `History.CardGenerated`; `Add`; `Hook.AfterCardGeneratedForCombat` | existing loop `CardPileCmd.AddGeneratedCardToCombat` (loop spec); history dropped | `:3196-3249` (`:3213`, `:3219`, `:3223`, `:3237`, `:3239`, `:3240`) |
| `Add`: combat pile while `IsEnding` (e.g. no primary enemy alive) → not added; `Bottom` → `AddInternal(card, -1)` (append); only `Random` would draw (`Shuffle`) | existing `CardPileCmd.AddToPile`/`MoveCard` | `:3291-3297`, `:3427-3433` |

So the injection consumes **no `Rng` stream**; the cards land at the bottom (end) of the discard pile in
creation order, after whatever the discard pile held, and become drawable after the next reshuffle.
`Slimed.AddToCombatAndPreview` is the sim port of the `<Slimed>` instantiation only; it lives on the card
because `Combat/Loop/CardPileCmd.cs` belongs to the loop agent (§10).

## 7. Stripped sites (Godot / VFX / SFX / waits)

Nothing below draws from a rule stream or changes rule state; order of rule effects is unchanged.

| Site | Game | Transformation |
| --- | --- | --- |
| `.WithAttackerAnim("Attack", 0.15f).WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_slime_impact")`, `Execute(null)` | `LeafSlimeS.cs:143-146`, `TwigSlimeS.cs:48-51`, `LeafSlimeM.cs:54-57`, `TwigSlimeM.cs:55-58` | dropped; `Execute()` |
| `CreatureCmd.TriggerAnim(Creature, "Cast", 0.5f / 1f / 0.75f)` before the status add | `LeafSlimeS.cs:68`, `LeafSlimeM.cs:173`, `TwigSlimeM.cs:174` | dropped (await on an animation) |
| `SfxCmd.Play(AttackSfx/CastSfx)`, `VfxCmd.PlayOnCreatureCenters(targets, "vfx/vfx_slime_impact")` | `LeafSlimeS.cs:84-85`, `LeafSlimeM.cs:172`, `:189`, `TwigSlimeM.cs:173`, `:190` | dropped |
| `if (TestMode.IsOff)` spit-target node positioning (reads `NCreature` positions) | `LeafSlimeM.cs:142-171`, `TwigSlimeM.cs:143-172` | dropped (UI only) |
| `NGoopyImpactVfx.Create(...)` in `Slimed.OnPlay` | `Slimed.cs:50-54` | dropped |
| card preview and waits in `AddToCombatAndPreview` | `Commands/CardPileCmd.cs:1468-1494` | dropped |
| `CombatManager.Instance.History.CardGenerated` | `Commands/CardPileCmd.cs:3237` | dropped (combat history not modelled, `monsters.md` §7) |
| `ThrowingPlayerChoiceContext`/`PlayerChoiceContext` plumbing | `Slimed.cs:30`, `:55` | dropped |

## 8. v0.107.1 parity

`v107:Models/Monsters/LeafSlimeS.cs:18-39`, `:55`; `TwigSlimeS.cs:15-29`; `LeafSlimeM.cs:20-39`, `:72`;
`TwigSlimeM.cs:21-44`, `:77`; `v107:Models/Encounters/SlimesWeak.cs:14-21`, `:52-57`;
`v107:Models/Cards/Slimed.cs:15-33`: identical HP, damage, amounts, graphs, initial states, branch rules,
composition draws and card values. Only the encounter seed formula differs (`rng-encounter-private.md` §3.3).

## 9. Tests (`Sts2Sim.Tests/SlimesWeakTests.cs`, filter `SlimesWeak`)

Expected values come from the decompile (and, for the recorded cases, from the recorder corpus), not from
the port. `Rng` is sealed, so scripted paths use a mock `RunRngSet(shared Rng)` and a probe clone replayed
through an independent reference (weights per §4 and the game's `NextFloat` walk).

- ids = slugified class names; registry entries; `IsWeak`, no slots, the one-argument overload throws.
- HP bounds A0-A10 (A8 switch); HP roll = `min + NextInt(0, range)` with exactly one draw, every value
  reachable, at A0 and A8, for each slime; the overlapping small-slime value is skipped.
- damage A0-A10 (A9 switch, A8 base), status amounts; graph shape, initial states, follow-ups, branch repeat
  types/max times/weights, intents (attack values through `GetSingleDamage`, status card counts), initial logs.
- composition = three encounter-`Rng` draws (300 seeds, all four combinations), spawn order, slot `null`.
- `SpawnEncounter`: creation order = combat ids 1-3, HP replayed on a probe `Niche` with the uniqueness rule,
  only `Niche` moves (3 draws), A0/A8, floors 2 and 5.
- recorded compositions (seven corpus fights, both seedings) through `GenerateMonsters` and `SpawnEncounter`.
- recorded fights turn by turn (five corpus fights, both seedings, A8-A10, one with `Niche` offset 1): HP of
  each slime and every slime's intent on every recorded turn, with the alive set taken from the recording.
- Leaf S: setup roll = one `NextFloat(2)`, round-1 re-roll draws nothing, both openers occur, strict
  alternation afterwards with one draw per roll (120 seeds × 12 turns vs the reference).
- Twig S always tackles, Leaf M alternates sticky-first, both draw nothing.
- Twig M: sticky opener without a draw, forced pokey second, at most two pokeys, no double sticky, one draw
  per roll, equals the reference on 200 seeds × 15 turns; both the forced-sticky and pokey-sticky paths occur.
- every move at A0/A8/A9: player damage, `SLIMED` count and position (bottom of discard, consecutive
  instance ids), no power applied, no block, no run stream consumed.
- `AddToCombatAndPreview`: creation without addition outside a running combat, nothing for a dead player,
  nothing added while the combat is ending, no stream use, non-player target rejected.
- `SLIMED` values; playing it costs 1, draws the top card, exhausts, draws no stream; it is legal without a
  target and illegal with one.
- `CombatSim.Create(... SLIMES_WEAK ...)` with `EndTurn` only, A0 (4 turns) and A10 (3 turns), 40 seeds:
  monsters, intents and `MonsterAi` counter equal the reference every turn; player HP and the number of
  `SLIMED` in the piles follow the moves. Clone/hash independence over three turns.
- random-policy playouts (30 seeds, A0-A10, `SLIMED` gets played): replaying the action script from a fresh
  `Create` and continuing a mid-fight clone give the same hash sequence.

## 10. Validation against the recorder corpus (2026-10-30, not part of the test suite)

Throwaway harness in the scratchpad: `sts2rec export-bundles --include-ineligible` (read-only corpus), then
every `SLIMES_WEAK` first-combat bundle replayed with `RngSetup.CreateForBundle` and
`CombatRunContext.ForAct(source.act, source.floor, null, null, seeding)`; the monster AIs were stepped with the
recorded alive set per player turn.

| Check | Result |
| --- | --- |
| first-combat `SLIMES_WEAK` attempts (20 × v0.107.1 legacy, 1 × v0.110.1 XXH64; 5 characters, A0-A10; save-and-quit retries counted per attempt) | 21 |
| composition and combat ids | 21/21 |
| HP and max HP of all three slimes | 20/21; the miss (`CPSVMWJPUL`, Neow room not recorded, bundle `Niche` offset unknown and set to 0) matches exactly with `Niche` offset 1 |
| opening intents (Leaf S opener from the one setup `MonsterAi` draw: 12 tackle, 9 goop) | 21/21 |
| intents on every later observed turn (Leaf S alternation, Twig M pokey/sticky branch incl. forced sticky after two pokeys, Leaf M alternation, draw order = enemy order, dead slimes skipped) | 63/63 (5 of these turns belong to `CPSVMWJPUL` and differ only in max HP) |
| full decision replay of the only eligible bundle (`QC7ZPPWXEK`, Ironclad A10 legacy) with the context | 14/14 observed decisions agree (hard and soft fields, `SLIMED` in discard/draw multisets), outcome win, final HP 60 = recorded |
| recorded `SLIMED` play (`6NFZE3VW1R` decision 15) | energy 2→1, hand 5→5, draw 10→9, exhaust +`SLIMED` |

## 11. Open points

- Resolved 2026-10-30 (integration): the diff harness context. `Sts2Sim.Diff` `BundleRunner.CreateSim` now
  creates every combat with `BundleRunner.RunContext(b)` = `CombatRunContext.ForAct(source.act ?? 1,
  source.floor ?? 2, null, null, CombatRunContext.ParseSeeding(seeding))` (`diff-harness.md` §2,
  `rng-encounter-private.md` §8). Before that the stock harness gave every `legacy_v18` `SLIMES_WEAK` bundle the
  XXH64 composition and reported `QC7ZPPWXEK` as a hard divergence at decision 0 (`enemies[combat_id=1].monster_id`
  oracle `LEAF_SLIME_S`, sim `TWIG_SLIME_S`); the stock harness now agrees on all 18 of its decisions (outcome win,
  final HP 60), as the context replay of §10 did.
- Pet targets of `SLIMED` (§6.2: the sim rejects a non-player target): not reachable from a slime move, not only
  for the Ironclad-only sim. Monster moves get `CombatState.PlayerCreatures` as targets (`Models/MonsterModel.cs:269`),
  which keeps creatures with `Player != null` (`Combat/CombatState.cs:560`, `Entities.Creatures/Creature.cs:509`);
  a pet is a monster creature (`Commands/PlayerCmd.cs:51`, `:951`: `CombatState.CreateCreature(monster, side, null)`)
  whose `PetOwner` is set (`Entities.Players/PlayerCombatState.cs:375-387`) and whose `Player` stays `null`. The game's
  `target.Player ?? target.PetOwner` fallback (`Commands/CardPileCmd.cs:1411`) only matters for a caller that passes
  pets explicitly; port it together with such a caller.
- **Generic `AddToCombatAndPreview`**: the game method is generic and also used by Aeonglass, Chomper,
  EyeWithTeeth, HauntedShip, MechaKnight, Myte, PhrogParasite, SlimedBerserker (10 `SLIMED`), TestSubject,
  Vantom, Wriggler. It belongs in `Combat/Loop/CardPileCmd.cs` (loop owner) as
  `AddToCombatAndPreview(cs, CardDef, targets, pileType, count, creator, position)`; `Slimed.AddToCombatAndPreview`
  should then delegate to it.
- The sim has no `CardsVar` class; `SLIMED` uses a plain `DynamicVar` named `"Cards"`. If the cards area adds
  `CardsVar`, switch to it (no behaviour change).
- `Hook.AfterCardChangedPiles` (`Commands/CardPileCmd.cs:3455-3468`) also follows each added `SLIMED` in the
  game; the sim does not dispatch it (known loop gap, `loop.md` §9, `integration.md`), harmless while no
  listener exists.
- `monsters.md` §1 "Candidates" and §8 still describe `SlimesWeak` as not ported (shared file; replacement text in
  `review-fixes-2026-10-30.md` §3).
