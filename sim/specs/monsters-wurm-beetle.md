# Monsters spec: `FUZZY_WURM_CRAWLER_WEAK`, `SHRINKER_BEETLE_WEAK`, `ShrinkPower`

2026-10-30. Rule truth: decompile tree (game v0.111.0), cited as `<ns-dir>/<File>.cs:<line>` with the
`MegaCrit.Sts2.Core.` prefix dropped. The v0.107.1 tree (`artifacts/knowledge/decompiled-v0.107.1/MegaCrit/sts2/Core/`)
is cited as `v107:<dir>/<File>.cs:<line>`; it was read only to confirm that the recorded v0.107.1 fights run the
same rules (§8). Shared infrastructure (move state machine, spawner, intents, pipeline) is specified in
`monsters.md`, `pipeline.md` and `rng-encounter-private.md`; this file covers only the defs below.

## 1. Files

| Sim type | File | Game source |
| --- | --- | --- |
| `FuzzyWurmCrawlerWeak : EncounterDef`, id `FUZZY_WURM_CRAWLER_WEAK` | `Sts2Sim.Core/Monsters/Encounters/FuzzyWurmCrawlerWeak.cs` | `Models.Encounters/FuzzyWurmCrawlerWeak.cs` |
| `ShrinkerBeetleWeak : EncounterDef`, id `SHRINKER_BEETLE_WEAK` | `Sts2Sim.Core/Monsters/Encounters/ShrinkerBeetleWeak.cs` | `Models.Encounters/ShrinkerBeetleWeak.cs` |
| `FuzzyWurmCrawler : MonsterDef`, id `FUZZY_WURM_CRAWLER` | `Sts2Sim.Core/Monsters/Defs/FuzzyWurmCrawler.cs` | `Models.Monsters/FuzzyWurmCrawler.cs` |
| `ShrinkerBeetle : MonsterDef`, id `SHRINKER_BEETLE` | `Sts2Sim.Core/Monsters/Defs/ShrinkerBeetle.cs` | `Models.Monsters/ShrinkerBeetle.cs` |
| `ShrinkPower : PowerModel`, id `SHRINK_POWER` | `Sts2Sim.Core/Powers/Defs/ShrinkPower.cs` | `Models.Powers/ShrinkPower.cs` |
| tests `Test_WurmBeetle_*` | `Sts2Sim.Tests/WurmBeetleTests.cs` | §9 |

Ids are `StringHelper.Slugify(ClassName)` (`Models/ModelDb.cs:485-488`, `Helpers/StringHelper.cs:68-73`). Move ids are the
string literals in the models. No `MonsterExtraState`, no new canonical tag, no shared file was touched; all defs are
picked up by the reflection registries.

## 2. Encounters

| Fact | `FuzzyWurmCrawlerWeak` | `ShrinkerBeetleWeak` |
| --- | --- | --- |
| `IsWeak => true` | `Models.Encounters/FuzzyWurmCrawlerWeak.cs:15` | `Models.Encounters/ShrinkerBeetleWeak.cs:15` |
| `GenerateMonsters()` = one `ToMutable()` monster, slot `null` | `:19-23` (spawn `:22`) | `:19-23` (spawn `:22`) |
| `AllPossibleMonsters` | `:17` | `:17` |
| `Tags` (run-level, not ported) | `[EncounterTag.Crawler]` `:11` | `[EncounterTag.Shrinker]` `:13` |
| `RoomType => Monster` (not ported) | `:13` | `:11` |
| `Slots` | not overridden (empty, `Models/EncounterModel.cs:133`) | not overridden |

- Both are in the Overgrowth (act 1) weak pool: listed by `GenerateAllEncounters` (`Models.Acts/Overgrowth.cs:71-96`,
  entries `:81` and `:89`) and selected as weak by `RoomType == Monster && IsWeak` (`Models/ActModel.cs:123`);
  `NumberOfWeakEncounters => 3` (`Models.Acts/Overgrowth.cs:45`). A first-ever run forces `ShrinkerBeetleWeak` to
  normal-encounter index 2 (`:117`).
- Sim: `GenerateMonsters(cs)` returns `[new MonsterSpawn(MonsterRegistry.Get(<monster id>))]` (slot `null`, no
  `Configure`). The two-argument `GenerateMonsters(cs, rng)` is not overridden: neither `GenerateMonsters` body reads
  `EncounterModel.Rng`, so the encounter-private `Rng` (`Models/EncounterModel.cs:202-216`,
  `rng-encounter-private.md` §3) is created by `SpawnEncounter` and never drawn from. The composition does not depend
  on the run seed, floor, act or seeding scheme.
- No slot name → `SortEnemiesBySlotName` never runs (`Combat/CombatManager.cs:6999-7002`).

## 3. `FuzzyWurmCrawler`

`cs.GetValueIfAscension(level, a, b)` = `AscensionHelper.GetValueIfAscension` (`Helpers/AscensionHelper.cs:14-21`);
`ToughEnemies = 8`, `DeadlyEnemies = 9` (`Entities.Ascension/AscensionLevel.cs:13-14`).

| Sim member | A0 | Ascension value | Level | Citation (`Models.Monsters/FuzzyWurmCrawler.cs`) |
| --- | --- | --- | --- | --- |
| `MinInitialHp` | 55 | 58 | ToughEnemies (A8) | `:211` |
| `MaxInitialHp` | 57 | 59 | ToughEnemies (A8) | `:213` |
| `AcidGoopDamage` | 4 | 6 | DeadlyEnemies (A9) | `:215` |
| `InhaleStrengthGain` | 7 | 7 (literal `7m`, no ascension) | – | `:176` |

### Move graph (`:230-242`)

| State | Kind | Intents | Effect (sim call) | Follow-up | Citation |
| --- | --- | --- | --- | --- | --- |
| `FIRST_ACID_GOOP` | Move, **initial state** | `SingleAttackIntent(AcidGoopDamage)` | `DamageCmd.Attack(cs, AcidGoopDamage).FromMonster(self).Execute()` | `INHALE` | `:233`, `:235`, `:241`; body `:74-79` |
| `ACID_GOOP` | Move | `SingleAttackIntent(AcidGoopDamage)` | same `AcidGoop` body | `FIRST_ACID_GOOP` | `:234`, `:237` |
| `INHALE` | Move | `BuffIntent` | `PowerCmd.Apply<StrengthPower>(cs, self, 7, self, null)` | `ACID_GOOP` | `:235-236`; body `:158-176` |

- `:235` is `MoveState moveState3 = (MoveState)(moveState.FollowUpState = new MoveState("INHALE", ...))`: it creates
  `INHALE` and sets it as `FIRST_ACID_GOOP`'s follow-up in one expression.
- Registration order `FIRST_ACID_GOOP, ACID_GOOP, INHALE` (`:238-240`); initial state `FIRST_ACID_GOOP` (`:241`).
- Cycle: `FIRST_ACID_GOOP, INHALE, ACID_GOOP, FIRST_ACID_GOOP, INHALE, ACID_GOOP, ...` (period 3). Player damage per
  enemy turn at A0: 4, 0, 11, 11, 0, 18, 18, 0, 25, ...; at A9: 6, 0, 13, 13, 0, 20, ... (Strength +7 per `INHALE`,
  stacking into one `StrengthPower`, applier = the wurm).
- The initial state is a move, so the machine logs it on construction and the first `RollMove` (and the round-1
  `PrepareForNextTurn` re-roll) returns it without stepping (`MonsterMoves.MonsterMoveStateMachine/MonsterMoveStateMachine.cs:47-52`, `:84-87`).
- Intent damage is captured at graph build (`new SingleAttackIntent(AcidGoopDamage)`), the move body reads
  `AcidGoopDamage` again at perform time (`:75`); ascension is fixed for a combat, so the two agree.

## 4. `ShrinkerBeetle`

| Sim member | A0 | Ascension value | Level | Citation (`Models.Monsters/ShrinkerBeetle.cs`) |
| --- | --- | --- | --- | --- |
| `MinInitialHp` | 38 | 40 | ToughEnemies (A8) | `:252` |
| `MaxInitialHp` | 40 | 42 | ToughEnemies (A8) | `:254` |
| `ChompDamage` | 7 | 8 | DeadlyEnemies (A9) | `:256` |
| `StompDamage` | 13 | 14 | DeadlyEnemies (A9) | `:258` |
| `ShrinkAmount` | -1 | -1 (literal `-1m`) | – | `:155` |

### Move graph (`:262-275`)

| State | Kind | Intents | Effect (sim call) | Follow-up | Citation |
| --- | --- | --- | --- | --- | --- |
| `SHRINKER_MOVE` | Move, **initial state** | `DebuffIntent(strong: true)` → `DebuffStrong` | `PowerCmd.Apply<ShrinkPower>(cs, targets, -1, self, null)` (list overload, one call per target) | `CHOMP_MOVE` | `:265`, `:268`, `:274`; body `:137-155` |
| `CHOMP_MOVE` | Move | `SingleAttackIntent(ChompDamage)` | `DamageCmd.Attack(cs, ChompDamage).FromMonster(self).Execute()` | `STOMP_MOVE` | `:266`, `:269`; body `:52-56` |
| `STOMP_MOVE` | Move | `SingleAttackIntent(StompDamage)` | `DamageCmd.Attack(cs, StompDamage).FromMonster(self).Execute()` | `CHOMP_MOVE` | `:267`, `:270`; body `:212-216` |

- Registration order `SHRINKER_MOVE, CHOMP_MOVE, STOMP_MOVE` (`:271-273`); initial `SHRINKER_MOVE` (`:274`).
- Sequence: `SHRINKER_MOVE, CHOMP_MOVE, STOMP_MOVE, CHOMP_MOVE, STOMP_MOVE, ...`: Shrink is applied once, on enemy
  turn 1; then 7/13 alternating (A9: 8/14).
- `targets` is the move's target list = `CombatState.PlayerCreatures` captured by `MonsterModel.PerformMove`
  (`Models/MonsterModel.cs:269`, `Combat/CombatState.cs:560`); the sim passes `MonsterLifecycle.PlayerCreatures(cs)`
  (`[cs.Player]`). The multi-target `PowerCmd.Apply<T>` applies to each target in order
  (`Commands/PowerCmd.cs:1185-1212`), the sim's list overload does the same.

## 5. Randomness

Neither move graph contains a `RandomBranchState` or a `ConditionalBranchState`: **0 `MonsterAi` draws per turn**,
including the first `RollMove` in `AfterCreatureAdded`. Per fight the only rule-RNG consumption by these defs is the
spawn HP roll: **1 `Niche` draw per monster** (`SetUniqueMonsterHpValue`, `Combat/CombatState.cs:717`,
`Entities.Creatures/Creature.cs:972-984`; `monsters.md` §4): `HP = candidates[Niche.NextInt(0, count)]` with
`candidates` = `[min, max]` minus the `MaxHp` of enemies already in `_enemies` (the full range for these one-monster
encounters: 3 values, or 2 for the wurm at A8+). Neither monster reads `MonsterModel.Rng`
(`UsesPrivateRng` stays `false`), and neither encounter reads the encounter `Rng` (§2).

## 6. `ShrinkPower` (`Models.Powers/ShrinkPower.cs`)

| Game member | Sim | Citation |
| --- | --- | --- |
| `Type => Debuff` | same | `:169` |
| `StackType => IsInfinite ? Single : Counter` | same (read live from `Amount`) | `:171-181` |
| `AllowNegative => true` | same | `:183` |
| `IsInfinite => Amount < 0` | public `IsInfinite` | `:185` |
| `DynamicVar("DamageDecrease", 30m)` (`const decimal damageDecrease = 30m`) | `const decimal DamageDecrease = 30m`; never changed at runtime | `:163`, `:187-191` |
| `ModifyDamageMultiplicative`: `Owner != dealer` → 1; `!props.IsPoweredAttack()` → 1; else `(100m - DamageDecrease) / 100m` = `0.7m` | same; `DamageMultiplier` = `(100m - DamageDecrease) / 100m` | `:253-264`; `ValueProps/ValuePropExtensions.cs:7` |
| `AfterSideTurnEnd(side, participants)`: `if (!IsInfinite && participants.Contains(Owner)) PowerCmd.Decrement(this)` | `AfterSideTurnEnd(cs, side)` + `IsTurnParticipant(cs, side)`, see below | `:129-131`, signature `:224-236` |
| `AfterDeath(creature, wasRemovalPrevented, _)`: `if (!wasRemovalPrevented && creature == Applier) PowerCmd.Remove(this)` | `creature.CombatId == ApplierId` (with `ApplierId != NoCreature` standing for the null check) | `:60-62`, signature `:238-251` |
| `AfterApplied`: `ScaleTo` visual, `ApplierName` string var (display) | not ported (no rule effect) | `:193-209` |
| `AfterRemoved`: `ScaleTo` visual | not ported | `:211-222` |
| `DisplayAmount => Amount` (not overridden) | the diff projection reports `Amount` (`-1` from the beetle) | `Models/PowerModel.cs:213` |

- `participants`: the game passes, at an enemy turn end, a snapshot of `CreaturesOnCurrentSide` (= `_enemies`)
  taken at the start of `EndEnemyTurnInternal` (`Combat/CombatManager.cs:2055`, `:2088`;
  `Combat/CombatState.cs:620`, `:831-838`), and at a player turn end the creatures of the players ending the turn
  (`Combat/CombatManager.cs:2649`/`:2860`, `:2967`). The sim's side-turn hooks carry no participants
  (`pipeline.md`, "Hook parameters dropped"), so `IsTurnParticipant` reconstructs them for one player:
  `Player` side → `Owner` is `cs.Player`; `Enemy` side → `Owner` is a `MonsterState` in `cs.Monsters` (the live
  list, not the snapshot); any other side → false. Difference from the game: a monster added or removed between
  the snapshot and `AfterSideTurnEnd` (during `BeforeSideTurnEnd`) is judged by the live list. Unreachable with the
  beetle's Shrink: its amount is `-1`, so `IsInfinite` short-circuits and the participants are never consulted.
- Duration semantics kept as written: a positive Shrink (only `Models.Potions/BeetleJuice.cs:56`, amount
  `RepeatVar(4)` `:101`, target `AnyEnemy` `:96`; potions are not in P0) loses 1 at its owner's side turn end through
  `PowerCmd.Decrement`, not `TickDownDuration`, so `SkipNextDurationTick` is ignored; at 0 it is removed
  (`ShouldRemoveDueToAmount`, `AllowNegative` → `Amount == 0`, `Models/PowerModel.cs:606`).
- Applying it: `PowerCmd.Apply` (`Commands/PowerCmd.cs:1215-1239`, body `:377`, `:396`, `:455`, `:476-478`) sets
  `Applier`, adds the `-1` power, and flags `SkipNextDurationTick` because the target is on the player side and the
  power's `Type` is `Debuff` (no rule reads the flag for Shrink; it is part of the hashed state). A second application
  (two beetles; not in these encounters) stacks through `ModifyAmount` into one instance (`-2`), and the multiplier
  stays `0.7`: it does not depend on the amount.
- `GetTypeForAmount(-1)` on the fresh canonical power (amount 0 → `Counter`) is `Debuff` (`Models/PowerModel.cs:593-604`),
  so debuff-blocking hooks see it as a debuff; after the apply `StackType` is `Single`. `StackType` is read by rules only
  in `GetTypeForAmount`; the other reads are UI (`Entities.Creatures/Creature.cs:1198-1212` events,
  `Nodes.Combat/NPower.cs:199` label).
- Death of the applier: `CreatureCmd.Kill` dispatches `Hook.AfterDeath(..., wasRemovalPrevented: false, ...)`
  (`Commands/CreatureCmd.cs:2782`; prevented path `:2796`; dispatcher `Hooks/Hook.cs:9233-9234`, body `:2297-2380`).
  The sim's `CreatureCmd.Kill` runs the same dispatch (`pipeline.md`), so the player's Shrink leaves when the beetle
  dies. In `SHRINKER_BEETLE_WEAK` that is also the last enemy, so the removal happens during the victory.
- Damage examples (the pipeline multiplies in listener order, player powers first; the final `(int)` truncation is in
  the attack/intent code): Strike `6 × 0.7 = 4.2 → 4`, Bash `8 × 0.7 = 5.6 → 5`, Strike on a Vulnerable beetle
  `6 × 0.7 × 1.5 = 6.3 → 6`, `(6 + 2 Strength) × 0.7 = 5.6 → 5`. A Shrink on a monster lowers that monster's intent:
  Chomp `(int)(7 × 0.7) = 4`, Stomp `(int)(13 × 0.7) = 9`.

## 7. Stripped sites (Godot / VFX / SFX / animation)

All dropped sites are presentation; none draws from a rule RNG stream or changes rule state.

| Site | Game | Transformation |
| --- | --- | --- |
| `_isPuffed` / `IsPuffed` (set `false` in `AcidGoop`, `true` in `Inhale`) | `Models.Monsters/FuzzyWurmCrawler.cs:209`, `:217-228`, `:74`, `:158` | not ported: read only by the animator lambdas (`:288-291`); no `MonsterExtraState` |
| `GetSpecialNode<Node2D>("Visuals/SpineBoneNode")` reposition (`TestMode.IsOff` only) | `FuzzyWurmCrawler.cs:62-73` | dropped |
| `.WithAttackerAnim("Attack", 1f).WithAttackerFx(null, AttackSfx).WithHitFx("vfx/vfx_attack_slash")` | `FuzzyWurmCrawler.cs:75-78` | dropped; `Execute(null)` → `Execute()` |
| `SfxCmd.Play(CastSfx)`, `CreatureCmd.TriggerAnim(Creature, "Inhale", 0.6f)` before the Strength apply | `FuzzyWurmCrawler.cs:159-160` | dropped (ordering unchanged) |
| `GenerateAnimator`, `ShouldShowMoveInBestiary` (`FIRST_ACID_GOOP` hidden) | `FuzzyWurmCrawler.cs:271-298` | not ported (UI) |
| `.WithAttackerAnim("Attack", 0.25f).WithAttackerFx(null, AttackSfx).WithHitFx(...)` on CHOMP / STOMP | `ShrinkerBeetle.cs:52-55`, `:212-215` | dropped |
| `SfxCmd.Play(CastSfx)`, `TriggerAnim(Creature, "Cast", 0.5f)`, `NCombatRoom.Instance?.RadialBlur(VfxPosition.Left)` before the Shrink apply | `ShrinkerBeetle.cs:137-138`, `:154` | dropped |
| `TakeDamageSfxType => Insect`, `ShouldShowMoveInBestiary` (`STOMP_MOVE` hidden) | `ShrinkerBeetle.cs:260`, `:317-320` | not ported (UI) |
| `new ThrowingPlayerChoiceContext()` arguments | `FuzzyWurmCrawler.cs:176`, `ShrinkerBeetle.cs:155` | dropped (choice context) |
| `ShrinkPower.AfterApplied` / `AfterRemoved` scale tweens, `ApplierName` var | `ShrinkPower.cs:193-222` | dropped |

## 8. Version check and corpus validation

- v0.107.1 has the same constants, graphs and bodies: `v107:Models/Monsters/FuzzyWurmCrawler.cs:29-33`, `:48-89`;
  `v107:Models/Monsters/ShrinkerBeetle.cs:21-27`, `:31-68`; `v107:Models/Powers/ShrinkPower.cs:18-106` (the only
  difference is the extra `CardPlay?` parameter of `ModifyDamageMultiplicative` in v0.111.0); encounters
  `v107:Models/Encounters/FuzzyWurmCrawlerWeak.cs:18-21`, `v107:Models/Encounters/ShrinkerBeetleWeak.cs:18-21`.
- Recorder corpus, read-only scan of every session part (113 fights: 59 `FUZZY_WURM_CRAWLER_WEAK` on v0.107.1 / v0.110.1 /
  v0.111.0, 54 `SHRINKER_BEETLE_WEAK` on v0.107.1 / v0.110.1; scripts and outputs only in the scratchpad):
  - First-snapshot `max_hp`: wurm A0-7 ∈ {55, 56, 57}, A8+ ∈ {58, 59}; beetle A0-7 ∈ {38, 39, 40}, A8+ ∈ {40, 41, 42}.
    The A8 fights (5 wurm, 5 beetle) already have the raised HP but still the A0 damage (wurm opener `Attack 4`, beetle
    `Attack 7`), the A9+ fights the raised damage: HP splits at ToughEnemies, damage at DeadlyEnemies.
  - Intents at each player-turn start: wurm `Attack 4|6, Buff, Attack 11|13, Attack 11|13, Buff, Attack 18|20` with
    `STRENGTH_POWER` 7 then 14 on the wurm; beetle `DebuffStrong, Attack 7|8, Attack 13|14, Attack 7|8, Attack 13|14`
    (labels lowered only where the beetle carried the player's Weak).
  - Player `SHRINK_POWER`: every one of 1177 snapshots shows `amount -1`, type `Debuff`; it first appears during enemy
    turn 1, never changes, and never leaves while the beetle lives.
  - Ironclad card damage on the beetle (`creature_attacked` events, blocked + unblocked) without other modifiers: from
    round 2 Strike 4, Bash 5, Strike after Bash 6; in round 1 (no Shrink yet) Strike 6, Bash 8, Strike after Bash 9.
    The other recorded values come from Weak, Strength or other characters' cards.

## 9. Tests (`Sts2Sim.Tests/WurmBeetleTests.cs`, filter `WurmBeetle`)

Expected values are written from the decompile (literals), not from the C# constants. "MockRng" = `RunRngSet(Rng shared)`
(every stream is one `Rng`), with expected draws replayed on a fresh `Rng` of the same seed.

- ids (slugified class names, move literals); registries (`MONSTER`/`ENCOUNTER`/`POWER`), `IsWeak`, empty `Slots`, no private
  `Rng`, no extra state.
- spawn lists: one unslotted monster, combat id 1, initial move logged, no next move before the first roll.
- encounter `Rng`: `GenerateMonsters(cs, rng)` draws nothing; `CombatSim.Create` with contexts for floors 1-8 gives equal
  monster, player and run-RNG-set hashes.
- HP: bounds A0-A10 (A8 split); MockRng roll `min + NextInt(0, count)` for 200 seeds at A0 and A8, exactly one draw, every
  value reached; real `RunRngSet`: only `Niche` advances.
- move values A0-A10 (A9 split), Inhale 7 flat, Shrink -1, multiplier 0.7.
- graphs: registration order, follow-ups, initial state, intents (types, damage at A0/A9, repeats), no branch states.
- cycles on MockRng: wurm 10 rolls, beetle 9 rolls, round-1 re-roll guard, `StateLog`, zero draws.
- moves: wurm damage and Strength stacking 7/14/21 with intent preview, A0/A8/A9/A10; beetle Shrink fields (amount, applier,
  `IsInfinite`, `Single`, `Debuff`, `SkipNextDurationTick`), then 7/13 (8/14) not reduced by the player's Shrink.
- `ShrinkPower`: owner/dealer/powered gates; `Hook.ModifyDamage` values with Strength and Vulnerable; type/stack-type by sign;
  second application stacks to -2 with the same multiplier; applier death removes it, a prevented death or another
  creature's death does not; participants: infinite never ticks, positive ticks only on its owner's side; Shrink on a
  monster lowers its intents.
- end to end (`CombatSim`): both encounters start with the right opener, one `Niche` draw, no `MonsterAi` draw; wurm and
  beetle `EndTurn` cycles with HP loss at A0/A9; Shrink stays -1 across turn ends; card damage 6 → 4 (Strike), Bash 5,
  Vulnerable Strike 6 (cloned branches of one seed); a greedy fight ends in victory with the Shrink gone.
- two-monster spawn through a test-only encounter (beetle + wurm in both orders, the composition of `OvergrowthCrawlers`):
  combat ids follow creation order, one `Niche` draw per monster in that order, no `MonsterAi` draw for the openers;
  duplicate beetles roll distinct HP.
- clone/hash: clone equals source, the cloned Shrink is independent, stepping a clone leaves the source alone, identical
  histories hash equal.

## 10. Not ported / open points

- `OvergrowthCrawlers` (normal pool, `Models.Encounters/OvergrowthCrawlers.cs:25-36`: `ShrinkerBeetle` then
  `FuzzyWurmCrawler`, slots `null`, no `Rng`; listed in `Models.Acts/Overgrowth.cs:86`) needs nothing beyond these defs but is
  outside this task; not registered here to avoid an id clash with whoever owns normal encounters.
- Encounter `Tags`, `RoomType`, `AllPossibleMonsters`: run-level data, as for `NibbitsWeak` (`monsters.md` §8).
- The participants reconstruction of §6 (live list instead of the turn-end snapshot) only matters for a positive Shrink on a
  creature that joins or leaves during `BeforeSideTurnEnd`.
