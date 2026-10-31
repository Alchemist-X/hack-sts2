# Pipeline spec: damage / block / HP kernel, hook dispatcher, power framework

Area owner: pipeline agent. Files:

| File | Contents |
| --- | --- |
| `Sts2Sim.Core/Combat/Pipeline/AbstractModel.cs` | hook virtuals (game names, default return values) |
| `Sts2Sim.Core/Combat/Pipeline/Hook.cs` | dispatcher, one static method per game hook |
| `Sts2Sim.Core/Combat/Pipeline/ListenerSnapshot.cs` | listener snapshot + liveness filter (internal) |
| `Sts2Sim.Core/Combat/Pipeline/CombatStatus.cs` | `IsEnding` / `IsOverOrEnding` / attachment / primary-secondary helpers |
| `Sts2Sim.Core/Combat/Pipeline/AttackCommand.cs` | `DamageCmd.Attack`, `AttackCommand` builder + `Execute` |
| `Sts2Sim.Core/Combat/Pipeline/CreatureCmd.cs` | `Damage`, `GainBlock`, `LoseBlock`, `ClearBlock`, `Heal`, HP/MaxHP setters, `Kill`, turn-start helpers |
| `Sts2Sim.Core/Combat/Pipeline/CreatureInternals.cs` | `Creature.*Internal` numeric primitives |
| `Sts2Sim.Core/Combat/Pipeline/PowerCmd.cs` | `Apply`, stacking, `ModifyAmount`, `Decrement`, `TickDownDuration`, `Remove` |
| `Sts2Sim.Core/Combat/Pipeline/EnergyCmd.cs` | energy gain / lose / set / reset / max / spend kernel |
| `Sts2Sim.Core/Combat/Pipeline/DamageResult.cs` | `DamageResult`, `ValuePropExtensions` |
| `Sts2Sim.Core/Powers/PowerModel.cs` | `PowerModel` base, `PowerCanonicalTags` |
| `Sts2Sim.Core/Powers/MultiplierModifiers.cs` | seam interfaces for Vulnerable/Weak multiplier modifiers |
| `Sts2Sim.Core/Powers/Defs/{Vulnerable,Weak,Strength,Dexterity,Frail}Power.cs` | the five P0 powers |
| `Sts2Sim.Tests/PipelineTests.cs` | 50 `Test_Pipeline_*` tests (self-contained doubles, see §10) |

Citations are `<namespace dir without MegaCrit.Sts2.Core.>/<File>.cs:<line>` in
`output/server-bootstrap/linux-decompiled/`.

## 1. Listener sources and order

The game has three listener enumerations. All three are snapshots taken when the dispatch
starts (the iterator builds its whole list on the first `MoveNext`), with a per-element
liveness check evaluated lazily at yield time.

| Sim `ListenerSource` | Game | Guard |
| --- | --- | --- |
| `Guarded` | `Hook.IterateCombatHookListeners` (`Hooks/Hook.cs:8792-8810`) | yields nothing when `CombatManager.IsOverOrEnding && !IsStarting` (`Hooks/Hook.cs:8794`) |
| `Combat` | `CombatState.IterateHookListeners()` (`Combat/CombatState.cs:856-956`) | none |
| `Run` | `RunState.IterateHookListeners(combatState)` (`Runs/RunState.cs:830-…`, iterator body `:114-248`) | none |

Order inside `CombatState.IterateHookListeners`: for each creature in `_allies` then `_enemies`:
its `Powers` in list order, then (monster) the `MonsterModel` or (player) relics, potions, orbs,
cards of all piles (`Combat/CombatState.cs:859-915`); then modifiers, badges, multiplayer
scaling model (`:916-927`); then mod subscribers (`:944-955`). P0 listeners are powers only
(CONTRACTS §9), so the sim snapshot is: player powers, then each `cs.Monsters[i].Powers`.
`Run` additionally yields deck cards (+enchantments) first (`Runs/RunState.cs:130-158`) and,
when a combat is given, no relics/potions at run level (`:164-186`); P0 has no deck-card
listeners, so `Run` and `Combat` yield the same powers. Note for later: with deck-card
listeners the combat snapshot inside `Run` is only taken after the deck cards are yielded.

Liveness (`CombatState.Contains`, `Combat/CombatState.cs:1061`): a power is yielded only if
`Owner.CombatState != null && (Owner.Player?.IsActiveForHooks ?? true)`. Sim:
`CombatStatus.IsAttached(cs, owner)` (player, or a member of `cs.Monsters`) and, for the
player, `PlayerState.IsActiveForHooks` (state agent field mirroring `Player.IsActiveForHooks`,
`Entities.Players/Player.cs:215-221`; deactivated in `Kill`, re-activated by `HealInternal` on revive).
A power removed from its owner mid-dispatch but whose owner is still attached is still yielded
(game does the same).

The guard is ported literally: `ListenerSnapshot.Take(cs, Guarded)` yields nothing when
`CombatStatus.IsOverOrEnding(cs) && !cs.IsStarting`. `IsStarting` and `IsInProgress` are the
`CombatTurnState` flags stored on `CombatState` (loop spec §2.2). So guarded listeners are silent
while the encounter spawns (no turn state yet: not starting, not in progress, hence over-or-ending),
run during setup (`IsStarting` true from `SetUpCombat`, `Combat/CombatManager.cs:6582`, until
`:4490`, whatever enemies exist), run during play, and are silent once the combat is ending or over.

`LocalContext.NetId.HasValue` gates in `BeforeSideTurnStart`, `BeforeSideTurnEnd`,
`AfterSideTurnEnd`, `AfterDeath`, `BeforeFlush` (`Hooks/Hook.cs:8580, 8370, 6142, 2337,
7748`) are treated as always true (single player always has a net id).

### Dispatcher table (sim `Hook.X`, source, game state machine)

Guarded: `BeforeAttack` (:6806), `AfterAttack` (:122), `AfterBlockCleared` (:613),
`BeforeBlockGained` (:6896), `AfterBlockGained` (:703), `AfterCardDiscarded` (:947),
`AfterCardDrawn` = Early then normal, two snapshots (:1041, :1086, :1128),
`AfterCardEnteredCombat` (:1191), `AfterCardExhausted` (:1281), `AfterCardGeneratedForCombat`
(:1377), `AfterEnergyReset` = normal then Late (:2509), `AfterEnergySpent` (:2651),
`AfterFlush` (:2743), `AfterHandEmptied` (:3025), `AfterPlayerTurnStart` = Early, normal,
Late (:4835, :4882, :4925, :4968), `AfterPowerAmountChanged` (:5311), `AfterShuffle` (:5986),
`AfterSideTurnEnd` = normal then Late (:6080, :6146, :6265), `AfterSideTurnStart` = normal then
Late (:6292, :6335, :6375), `AfterTakingExtraTurn` (:6716), `BeforeCardPlayed` (:7086),
`BeforeFlush` = normal then Late (:7696), `BeforeHandDraw` = normal then Late (:7879),
`BeforePowerAmountChanged` (:8121), `BeforeSideTurnEnd` = VeryEarly, Early, normal (:8309),
`BeforeSideTurnStart` (:8544); modify hooks `ModifyAttackHitCount` (:10052), `ModifyBlock`
(:10071), `ModifyCardPlayCount` (:10163), `ModifyEnergyCostInCombat` (:10422),
`ModifyEnergyGain` (:10475), `ModifyHandDraw` (:10583), `ModifyMaxEnergy` (:10713),
`ModifyPowerAmountGiven` (:10884), `ModifyPowerAmountReceived` (:10928), `ModifyShuffleOrder`
(:11054), `ModifyStarCost` (:11071), `ModifyXValue` (:11151); gates `ShouldAllowHitting`
(:11235), `ShouldAllowTargeting` (:11298), `ShouldClearBlock` (:11321), `ShouldDraw` (:11426),
`ShouldEtherealTrigger` (:11449), `ShouldFlush` (:11470), `ShouldPayExcessEnergyCostWithStars`
(:11533), `ShouldPlay` (:11554), `ShouldPlayerResetEnergy` (:11577), `ShouldTakeExtraTurn`
(:11682); modifier-filtered `AfterModifyingBlockAmount` (:3305), `AfterModifyingCardPlayCount`
(:3405), `AfterModifyingEnergyGain` (:3693), `AfterModifyingHandDraw` (:3887),
`AfterModifyingPowerAmountGiven` (:4269), `AfterModifyingPowerAmountReceived` (:4365);
contains-filtered `AfterPreventingBlockClear` (:5409, check :5443), `AfterPreventingDraw`
(:5553, check :5585).

Combat (unguarded): `AfterBlockBroken` (:519, iter :553), `AfterCardPlayed` = normal then Late
(:1469, :1512, :1554), `AfterCreatureAddedToCombat` (:1853), `AfterDamageGiven` (:2037, iter
:2077), `ModifyUnblockedDamageTarget` (:11113), `ShouldCreatureBeRemovedFromCombatAfterDeath`
(:11344), `ShouldStopCombatFromEnding` (:11661), `ShouldPowerBeRemovedOnDeath` (:11741; returns
true when the owner is detached, :11743-11746).

Run: `AfterCurrentHpChanged` (:1943), `AfterDamageReceived` = normal then Late (:2139, :2192,
:2234), `AfterDeath` (:2297, :2340), `AfterModifyingDamageAmount` (:3595, modifier filter),
`AfterModifyingHpLostAfterOsty` (:3981), `AfterModifyingHpLostBeforeOsty` (:4077),
`AfterPreventingDeath` (:5480, contains check :5516), `BeforeCombatStart` = normal then Late
(:7358), `BeforeDamageReceived` (:7500), `BeforeDeath` (:7604), `ModifyDamage` internals
(:11766-11840), `ModifyHpLost` (:10627), `ShouldDie` = ShouldDie loop then ShouldDieLate loop
(:11365-11403).

"Modifier-filtered" dispatchers iterate the listener snapshot and call only models contained
in the `modifiers` list returned by the matching `Modify*` (e.g. `Hooks/Hook.cs:3362`). The sim
`Modify*` overloads take `ref List<AbstractModel>? modifiers` (allocated lazily on the first
modifier); an empty/null list skips the dispatch (no side effects lost). Modifier criteria:
damage additive `!= 0` / multiplicative `!= 1` / cap applied (:11780, :11801, :11822-11828);
block additive `!= 0` / multiplicative `!= 1` (:10089, :10107); HP-lost
`Truncate(before) != Truncate(after)` (:10641 etc.); energy gain and hand draw
`(int)before != (int)after` (:10487, :10595); card play count `num != before` (:10175); power
given additive `!= 0` / multiplicative `!= 1` (:10896, :10914); power received when
`TryModifyPowerAmountReceived` returns true (:10938).

## 2. Attack command (`Commands.Builders/AttackCommand.cs`)

- `DamageProps` defaults to `ValueProp.Move` (:653-659); `Unpowered()` ORs `Unpowered` (:856-860).
- `FromCard`: attacker = card owner creature (P0: `cs.Player`), throws if attacker/source/play already set (:738-763).
- `FromMonster`: attacker = monster, source Monster, then `TargetingAllOpponents` (:781-792).
- `Targeting`: single target, `TargetSide = target.Side` (:794-809). `TargetingAllOpponents`: `TargetSide` = opposite of attacker (:811-831). `TargetingRandomOpponents(allowDuplicates)` (:833-854) does not set `TargetSide`.
- Possible targets (:703-724): single target; else monster source → `PlayerCreatures` (players only, :560); else `GetOpponentsOf(attacker)` = `GetCreaturesOnSide(opposite)` (`Combat/CombatState.cs:831-843`; non-Enemy side → allies).
- `Execute` (:961-1148):
  1. no attacker → throw (:966-969); `IsOverOrEnding` → return (:970-973; `CombatState.IsLiveCombat()` is always true, `Combat/CombatState.cs:1077-1080`); attacker not in the combat → throw "No combat state even though combat is not over" (:974-977); attacker dead → return (:978-981); no targets → throw (:982-985).
  2. `Hook.BeforeAttack` (:986); `attackCount = Hook.ModifyAttackHitCount(cs, this, hitCount)` (:987).
  3. per hit (:988-1144): attacker dead → break; `validTargets = possible.Where(IsAlive)`; none → break (:994-998); non-random: `singleTarget = count == 1 ? [0] : null` (:1049); random: optionally drop already-hit creatures, throw if none left (:1053-1060), `singleTarget = RunRng.CombatTargets.NextItem(validTargets)` (:1061-1062); `BeforeDamage` callback (:1127-1130); `CreatureCmd.Damage(singleTarget ?? validTargets, damagePerHit, DamageProps, attacker, card, cardPlay)` (:1131-1143); results appended per hit (:1155-1158).
  4. `Hook.AfterAttack` (:1146).
- Sim additions: `Results` (flat list, CONTRACTS), `HitResults` (game's per-hit `List<List<DamageResult>>`), `HitsCompleted` (sim-only count of executed hits). `CalculatedDamageVar` constructor path (:732-736, :1131) not ported (no P0 card uses it). `BeforeDamage` is `Action<CombatState, AttackCommand>`.

## 3. Damage kernel (`CreatureCmd.Damage`, `Commands/CreatureCmd.cs:3662-3870`)

Per call:
1. dealer non-null and dead → one empty `DamageResult` per target, nothing else (:3670-3673).
2. Snapshot the target list (:3675); skip dead targets (:3689-3692).
3. For each live target:
   - `modified = Hook.ModifyDamage(...)` (:3694) = `Max(0, ModifyDamageInternal(...))` (`Hooks/Hook.cs:10416`). Internal (:11766-11840), three passes, each a fresh `Run` snapshot:
     additive `num += model.ModifyDamageAdditive(target, num, …)`; multiplicative
     `num *= model.ModifyDamageMultiplicative(target, num, …)`; cap
     `cap = decimal.MaxValue; foreach c: if c < cap { cap = c; if num > c { num = c } }`.
     All `decimal`, **no rounding anywhere in ModifyDamage**. Enchantment pre-pass (:10332-10342) and the
     `CardPreviewMode.MultiCreatureTargeting` path (:10343-10410) are not ported (no enchantments; preview is UI).
   - `Hook.AfterModifyingDamageAmount(modifiers)` (:3695), `Hook.BeforeDamageReceived(target, modified, …)` (:3696).
   - Block absorption `DamageBlockInternal` (`Entities.Creatures/Creature.cs:1026-1031`):
     `blocked = Unblockable ? 0 : Min(Block, modified)` (decimal), `Block -= (int)blocked`.
     The pet-owner redirection (`PetOwner?.Creature ?? target`, :3697) is identity in P0 (no pets).
   - `unblocked = Hook.ModifyHpLost(target, Max(modified - blocked, 0), BeforeOsty)` (:3699) → after-hook (:3700);
     `unblockedTarget = Hook.ModifyUnblockedDamageTarget(...)` (:3701);
     `unblocked = Hook.ModifyHpLost(unblockedTarget, unblocked, AfterOsty)` (:3702) → after-hook (:3703).
     `ModifyHpLost` (`Hooks/Hook.cs:10627-10711`) runs BeforeOsty, BeforeOstyLate, AfterOsty, AfterOstyLate passes per phase flag; no clamp.
   - `LoseHpInternal(unblocked)` (`Creature.cs:1033-1045`): `killed = CurrentHp > 0 && amount >= CurrentHp` (decimal compare);
     `num = (int)Clamp(amount, 0, 999999999)`; `CurrentHp = Max(CurrentHp - num, 0)`;
     `UnblockedDamage = oldHp - CurrentHp`; `OverkillDamage = killed ? Max(num - oldHp, 0) : 0`.
   - `WasBlockBroken = target.Block <= 0 && blocked > 0`;
     `WasFullyBlocked = !Unblockable && (blocked > 0 || target.Block > 0) && (int)unblocked == 0` (:3708-3709);
     `BlockedDamage = (int)blocked` (:3712). Redirected-target branch (:3716-3725) ported verbatim.
4. After all targets, per result in order (:3821-3866): `AfterBlockBroken(target, dealer)` if broken;
   `AfterCurrentHpChanged(target, -Unblocked)` if `Unblocked > 0`; `AfterDamageGiven`;
   `AfterDamageReceived` unless (`WasTargetKilled && target.IsDead`), in which case the target is queued;
   then `Kill(queued)` (:3867).

Rounding consequence (hand-checked, see tests): truncation happens only at `(int)blocked` on
the block and at `(int)amount` on HP loss, so e.g. Weak 7 → 5.25 vs block 10 leaves block 5, vs
block 5 breaks it and loses 0 HP, vs block 3 loses 2 HP.

Hand-computed examples used by tests (Strike-like `ValueProp.Move`, listener order player then monster):

| Setup | Formula | HP loss |
| --- | --- | --- |
| none | 6 | 6 |
| Strength 2 (attacker), Vulnerable (target) | (6+2)×1.5 = 12 | 12 |
| Weak (attacker) | 6×0.75 = 4.5 | 4 |
| Weak + Vulnerable | 6×0.75×1.5 = 6.75 | 6 |
| Strength 3 + Weak + Vulnerable | (6+3)×0.75×1.5 = 10.125 | 10 |
| Strength −10 | max(0, 6−10) = 0 | 0 |
| Unpowered (`Move|Unpowered`) with all of the above | 6 | 6 |
| monster Strength 5 + Weak vs player Vulnerable, base 10 | (10+5)×1.5×0.75 = 16.875 | (intent value) |

## 4. Block kernel

`CreatureCmd.GainBlock` (`Commands/CreatureCmd.cs:3950-3985`): `IsOverOrEnding` → 0; dead → 0;
`BeforeBlockGained(creature, amount, props, cardPlay?.Card)`; `modified = Hook.ModifyBlock(...)`
(`Hooks/Hook.cs:10071-10119`: enchantment pre-pass skipped; additive pass then multiplicative
pass, both Guarded; `Max(0, num)`); `modified = Max(modified, 0)`; `AfterModifyingBlockAmount`;
if `modified > 0` → `GainBlockInternal` (`Creature.cs:1047-1055`:
`Block = (int)Min(Block + amount, 999999999)`, throws on negative); `AfterBlockGained`; returns
the decimal `modified`. The card source passed to hooks is `cardPlay?.Card`.

Examples: Dexterity 2 → 5+2 = 7; Frail → 5×0.75 = 3.75 → +3 block; Dex 2 + Frail →
(5+2)×0.75 = 5.25; Dex −7 → max(0, −2) = 0 (no block); `Unpowered` block ignores both.
Truncation is on `Block + amount`, so 3 + 3.75 = 6.75 → 6.

`LoseBlock` (state machine `:2950-2966`): skip if `IsOverOrEnding`, target dead or `amount <= 0`;
`LoseBlockInternal` (`Creature.cs:1057-1065`: `Block = (int)Max(Block - amount, 0)`); if block went
from `> 0` to `<= 0` → `AfterBlockBroken(target, remover)`.

`ClearBlock` (`Creature.cs:210-222`, private `Creature.ClearBlock`): if `!Hook.ShouldClearBlock(…, out preventer)`
→ `Hook.AfterPreventingBlockClear(preventer, creature)`; else `Block = 0`.
`CreatureCmd.AfterTurnStart(cs, creature, side)` mirrors `Creature.AfterTurnStart`
(`Creature.cs:136-155`): clear block unless `side == Player` and the creature is a player whose
`TurnNumber == 1` (`PlayerCombatState.TurnNumber` starts at 1, `Entities.Players/PlayerCombatState.cs:42-48`,
and is incremented when sides switch back to the player, `Combat/CombatManager.cs:7480`, so block
gained before the first player turn survives it). `CreatureCmd.BeforeTurnStart` mirrors `Creature.BeforeTurnStart`
(`Creature.cs:1251-1268`): `AmountOnTurnStart = Amount` for every power. Call sites in the game:
`CombatManager.StartTurn` `BeforeTurnStart` per creature (`Combat/CombatManager.cs:4901`) →
`Hook.BeforeSideTurnStart` (:4914) → `AfterTurnStart` per creature (:4960) → `Hook.AfterBlockCleared`
per creature (:5008) → player turn setup → `Hook.AfterSideTurnStart` (:5080). (Loop agent owns the sequence.)

`Block` / `CurrentHp` setters throw on negative values (`Creature.cs:363-366, 385-388`); ported in
`CreatureInternals.SetBlock` / `SetCurrentHp`.

## 5. HP, death

- `Heal` (state machine `:2102-2220`): skipped when `IsEnding && !creature.IsPlayer`;
  `HealInternal` = `SetCurrentHpInternal(CurrentHp + amount)` (`Creature.cs:1067-1076`,
  `SetCurrentHpInternal` :1078-1081 = `(int)Min(amount, MaxHp)`); then
  `AfterCurrentHpChanged(+amount)` if `amount > 0` and attached. The last enemy therefore cannot be
  healed by a death-prevention hook (combat is already "ending"; test
  `LastEnemyCannotBeHealedWhileEnding`); real monster revives use `ShouldStopCombatFromEnding`.
- `SetCurrentHp` (`:3148-3188`): `delta = amount - oldHp` uses the requested amount; kill if dead.
- `SetMaxHp` (`:4063-4075`) / `SetMaxHpInternal` (`Creature.cs:1083-1092`: throws on negative,
  `MaxHp = Min((int)amount, 999999999)`, clamp current HP); `GainMaxHp` (`:1987-2013`: throws on
  negative, heals by the max-HP delta); `LoseMaxHp` (`:3043-3075`: damage down to the new max with
  `Unblockable|Unpowered[|Move if from card]`, then `SetMaxHp(Max(1, newMax))`);
  `SetMaxAndCurrentHp` (`:3257-3273`).
- `Kill(creatures)` (state machine `:2353-2440`): snapshot list; `KillWithoutCheckingWinCondition`
  for each; if any killed creature was a player (the run state comes from the first player in the
  list, `:2355`) and all players are dead, then **only while `CombatManager.IsInProgress`**
  (`:2401-2404`) → `LoseCombat` (`Combat/CombatManager.cs:7124-7131` sets `PendingLoss` unless one
  is already set). Sim: `CombatStatus.LoseCombat` sets `Outcome = Defeat` when `cs.IsInProgress`
  and `Outcome == InProgress`; a player death during setup (loop spec §2.2) or after the end
  records nothing. Win is not checked here (loop's `CheckWinCondition`). Multiplayer
  `PlayerCmd.EndTurn` branch (:2414-2438) dropped.
- `KillWithoutCheckingWinCondition` (`:2594-2884`): skip if detached and not a player; if
  `CurrentHp > 0` → `LoseHpInternal(CurrentHp, Unblockable|Unpowered)` + `AfterCurrentHpChanged`;
  `BeforeDeath`; if `force || MaxHp <= 0 || ShouldDie(out preventer)`:
  `ShouldCreatureBeRemovedFromCombatAfterDeath`; `AfterDeath(wasRemovalPrevented: false)`;
  `teammates = GetTeammatesOf(creature).Where(IsAlive)` (`Combat/CombatState.cs:845-848`);
  enemy + should-remove + still listed → removed from `cs.Monsters` (`CombatState.RemoveCreature`
  :739-768) unless the monster is performing its own move (`!monster.IsPerformingMove`, :2818-2822;
  sim: `MonsterAiRuntime.IsPerformingMove`, removal then happens in `MonsterLifecycle.PerformMove`
  after the move as in `Models/MonsterModel.cs:287-291`); `isPrimaryEnemy` captured;
  `RemoveAllPowersAfterDeath` (`Creature.cs:1246-1249`: keep a
  power if `!ShouldPowerBeRemovedAfterOwnerDeath() || !Hook.ShouldPowerBeRemovedOnDeath(power)`,
  predicates evaluated for all powers in order before any removal) then `AfterRemoved` for each removed
  power; if a primary enemy died and every living teammate is secondary → `Kill(teammates)`.
  Otherwise (prevented): recursion ≥ 10 → throw; `AfterDeath(true)`; `AfterPreventingDeath(preventer)`;
  if still dead → recurse.
- Player death: `player.DeactivateHooks()` (:2850, `Entities.Players/Player.cs:1278-1281`) is ported;
  orb queue clear and Osty kill (:2704-2717) have nothing to act on in P0; `HandlePlayerDeath` is a
  no-op in single player (returns immediately when all players are dead,
  `Combat/CombatManager.cs:3492-3493`).
- `Stun` = `Creature.StunInternal` (`Creature.cs:1105-1126`) with a no-op move
  (`Commands/CreatureCmd.cs:4092-4125`), delegated to the monsters agent's
  `MonsterLifecycle.StunInternal` (transient `STUNNED` `MoveStateDef`, follow-up = given id or last
  state-log entry, `MustPerformOnceBeforeTransitioning`, `SetMoveImmediate`
  `Models/MonsterModel.cs:724-736`). `StunWithMove` passes a custom stun move.
- Secondary enemy: `IsSecondaryEnemy = Side == Enemy && Powers.Any(p => p.OwnerIsSecondaryEnemy)`
  (`Creature.cs:628-638`); `IsPrimaryEnemy` (:616-626). `IsHittable` (:640-654),
  `CanReceivePowers` (:656-670).

`CombatStatus.IsEnding` = `CombatManager.IsCombatEnding` (`Combat/CombatManager.cs:6548-6567`):
not `IsInProgress` → false (checked first, so a processed loss, a victory and the setup window are
never "ending"); a pending loss (`Outcome == Defeat` while in progress) → true; any living primary
enemy → false; else `!ShouldStopCombatFromEnding`. `IsOverOrEnding` (`:6134-6144`) =
`IsEnding || !IsInProgress`. `IsAboutToLose` (`:6119`) = a pending loss. `IsInProgress` /
`IsStarting` read the `CombatState` flags (`:6113`, `:6117`).

## 6. Power framework (`Models/PowerModel.cs`, `Commands/PowerCmd.cs`)

- `SetAmount` clamps to ±999999999 (`Models/PowerModel.cs:651`). UI events
  (`DisplayAmountChanged`, `InvokePowerModified`, :656-661) dropped.
- `GetTypeForAmount` (:593-604): Counter + AllowNegative + negative → Debuff; !AllowNegative +
  Debuff + negative → Buff; else `Type`. `ShouldRemoveDueToAmount` (:606-617): AllowNegative →
  remove iff `Amount == 0`; else remove iff `Amount <= 0`.
- `ApplyInternal(owner, amount)` (:674-683): no-op on 0; set owner (throws when moving between
  owners, :259-262); `SetAmount((int)amount)`; `Creature.ApplyPowerInternal` (`Creature.cs:1182-1196`)
  throws on a second instance of the same type for `InstanceType.None`. `RemoveInternal`
  (:685-694 + `Creature.cs:1214-1223`) removes from the owner's list; the owner reference is kept.
- `PowerCmd.Apply<T>` (`Commands/PowerCmd.cs:1215-1239`): null when `IsEnding` or the target cannot
  receive powers; find an existing instance (`FindExistingInstanceForStacking`, :1260-1270: None →
  first power with the same id; InstancedPerApplier → same id and same applier; Instanced → never);
  existing → `ModifyAmount`, returning null when the new amount is 0; else apply a fresh instance
  (returned even if it ends up unattached, e.g. amount 0).
- `PowerCmd.Apply(power, …)` (state machine `:377-562`): skip on `IsEnding`, `amount == 0`, or target
  cannot receive powers; existing instance → `ModifyAmount` (the `silent` flag is not forwarded,
  :385); `Applier = applier`; `BeforePowerAmountChanged`; `ModifyPowerAmountGiven` only when the
  applier is in combat (:545-548); `ModifyPowerAmountReceived`; (multiplayer scaling :550-553 dropped);
  `power.BeforeApplied`; re-check `CanReceivePowers`; `ApplyInternal(target, modified)`;
  **if `target.Side == Player && power.Type == Debuff` → `SkipNextDurationTick = true` (:476-479)**;
  `AfterModifyingPowerAmountGiven` / `Received`; if `modified != 0` → `power.AfterApplied`,
  `AfterPowerAmountChanged`.
- `ModifyAmount` (`:1299-1358`): 0 when `IsEnding` or the owner is detached;
  `BeforePowerAmountChanged`; given/received modifiers; `newAmount = Amount + (int)modifiedOffset`;
  `SetAmount`; after-modifier hooks; `AfterPowerAmountChanged` if `(int)modifiedOffset != 0`;
  `Remove` if `ShouldRemoveDueToAmount`; returns the unclamped `newAmount`. Stacking never re-arms
  `SkipNextDurationTick`.
- `Decrement` = `ModifyAmount(-1, applier null, card null)` (state machine `:614`).
- `TickDownDuration` (`:1149-1161`): if `SkipNextDurationTick` → clear it, else `Decrement`.
- `Remove(power)` (`:1070-1089`): `RemoveInternal`, then `power.AfterRemoved(power.Owner)`;
  `Remove<T>(creature)` (`:987`) removes `creature.GetPower<T>()`.
- `StackType` only affects display / `GetTypeForAmount` / UI events; stacking always sums amounts.
- Canonical form (`PowerModel.WriteCanonical`): tag `0x5401`, `Id`, `Amount`, `AmountOnTurnStart`,
  `SkipNextDurationTick`, `ApplierId`, `TargetId`, then `WriteExtraCanonical`. The owner is implied by
  the containing creature. `CloneFor` = `MemberwiseClone` + owner rebind + `CopyMutableReferenceState`.
- Game `DynamicVars` on powers (e.g. Vulnerable `DamageIncrease`) are only read, never mutated, by the
  ported code, so they are `const`s.

### The five P0 powers

| Power (id) | Type / stack / negative | Rule | Duration |
| --- | --- | --- | --- |
| `VulnerablePower` (`VULNERABLE_POWER`) | Debuff / Counter / no (`Models.Powers/VulnerablePower.cs:92-94`) | `ModifyDamageMultiplicative`: target is owner and powered attack → `DamageIncrease = 1.5m` (:96, :98-128), then Cruelty on dealer and Debilitate on target via the seam (below) | ticks in `AfterSideTurnEnd` when `side == Enemy` (:56-58) |
| `WeakPower` (`WEAK_POWER`) | Debuff / Counter / no (`WeakPower.cs:92-94`) | dealer is owner and powered attack → `DamageDecrease = 0.75m` (:96, :98-120), then Debilitate on dealer (`ModifyWeakMultiplier(dealer, …)`, :114-118) | `AfterSideTurnEnd(Enemy)` (:56-58) |
| `StrengthPower` (`STRENGTH_POWER`) | Buff / Counter / AllowNegative (`StrengthPower.cs:10-14`) | `ModifyDamageAdditive`: dealer is owner and powered attack → `+Amount` (:16-27) | none |
| `DexterityPower` (`DEXTERITY_POWER`) | Buff / Counter / AllowNegative (`DexterityPower.cs:12-16`) | `ModifyBlockAdditive`: card source owned by the power owner (or no card and owner is the target) and `IsPoweredCardOrMonsterMoveBlock` → `+Amount` (:20-38) | none |
| `FrailPower` (`FRAIL_POWER`) | Debuff / Counter / no (`FrailPower.cs:89-91`) | `ModifyBlockMultiplicative`: owner is target and powered card/move block → `0.75m` (:95-106) | `AfterSideTurnEnd(Enemy)` (:55-57) |

`IsPoweredAttack` / `IsPoweredCardOrMonsterMoveBlock` = `Move && !Unpowered`;
`IsCardOrMonsterMove` = `Move` (`ValueProps/ValuePropExtensions.cs:7-28`).

**Duration timing.** Vulnerable/Weak/Frail decrement via `PowerCmd.TickDownDuration` from their
own `AfterSideTurnEnd(side == Enemy)`, i.e. once per round at the end of the **enemy** turn, for
every owner. `Hook.AfterSideTurnEnd` is called from `CombatManager.EndEnemyTurnInternal`
(`Combat/CombatManager.cs:2088`) after `Hook.BeforeSideTurnEnd` (:2056) and
`PlayerCombatState.EndOfTurnCleanup` (:2078); the player-side call (:2967) does not tick them.
Consequences (tests `EnemyDebuffTicksAtEnemyTurnEnd`, `PlayerDebuffSkipsFirstTick`,
`StackingPlayerDebuffDoesNotRearmSkip`):
- debuff applied to a monster with N stacks lasts through N enemy-turn ends (Bash's Vulnerable 2
  on the player's turn: 2 → 1 at that round's enemy end → removed at the next one);
- a **newly applied** debuff on the **player** skips its first tick, so it survives one extra enemy
  turn end; adding stacks to an existing one does not re-arm the skip.

Seam: `IVulnerableMultiplierModifier` / `IWeakMultiplierModifier` (`Powers/MultiplierModifiers.cs`)
mirror `CrueltyPower.ModifyVulnerableMultiplier` (`Models.Powers/CrueltyPower.cs:17-28`:
`+Amount/100` when target is not the owner) and `DebilitatePower.ModifyVulnerableMultiplier` /
`ModifyWeakMultiplier` (`DebilitatePower.cs:99-123`). Vulnerable/Weak look them up by id
(`CRUELTY_POWER`, `DEBILITATE_POWER`) in the game's order. Those powers are not ported yet.

## 7. Energy (`EnergyCmd`)

- `GetMaxEnergy` = `(int)Hook.ModifyMaxEnergy(player.BaseMaxEnergy)` (`Entities.Players/PlayerCombatState.cs:136`).
- `ResetEnergy`: `Energy = MaxEnergy` (:304-307); `AddMaxEnergyToCurrent` (:309-312).
- `GainEnergyInternal` / `LoseEnergyInternal`: throw on negative, `Energy = (int)Clamp(Energy ± amount, 0, 999999999)` (:314-332).
- `GainEnergy` (`Commands/PlayerCmd.cs` state machine :194-215): no-op if `amount <= 0` or `IsEnding`; `final = Hook.ModifyEnergyGain`; `AfterModifyingEnergyGain`; gain if `final > 0`.
- `LoseEnergy` (:815-827): no-op if `amount <= 0` or `IsEnding`.
- `SetEnergy` (:541-567): if not ending, `GainEnergy(amount - energy)` or `LoseEnergy(energy - amount)` (the gain path goes through `ModifyEnergyGain`).
- `SpendEnergy(card, amount)` (`Models/CardModel.cs:1249-1258`): X-cost cards capture `amount`; if `amount > 0` lose it via the internal clamp (no `IsEnding` check); `Hook.AfterEnergySpent`.
  `CardModel.SpendResources` (:1343-1378: energy-to-spend, star overflow when `ShouldPayExcessEnergyCostWithStars`) is card-play logic left to the loop agent.

## 8. Stripped sites (async/VFX/UI) and the transformation

All `async Task` became synchronous calls in the same order; `PlayerChoiceContext` /
`HookPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion` / `Task.WhenAll` collapse to
sequential calls (no hook in P0 pauses for a choice).
- `Cmd.CustomScaledWait` (CreatureCmd :3868, :3976-3981, Heal :2190; PowerCmd :462, :921/:1355, :1073) — dropped, no state.
- VFX/SFX/animation: damage numbers, hit sparks, `TriggerAnim("Hit")`, hurt SFX, screen shake, vignette, block break/hit SFX (CreatureCmd :3743-3807, :3853-3860), block gain SFX/VFX (:3970-3971), heal VFX/revive anim (:2106-2160, :2205, :3157), attacker VFX/SFX/anim, hit VFX, `_waitBeforeHit` (AttackCommand :999-1126) — dropped, no state.
- `InvokeExecutionFinished` on each listener — UI only (subscriber `Nodes.Combat/NPlayerHand.cs:1064`), dropped.
- `PowerModel` events (`Flashed`, `DisplayAmountChanged`, `Removed`, pulsing) and `Creature` events (`BlockChanged`, `CurrentHpChanged`, `PowerApplied/Increased/Decreased/Removed`, `Died`, `Revived`) — UI only, dropped.
- `CombatManager.History.*` recording — **dropped in P0 but rule-relevant later**: `DamageReceived` (CreatureCmd :3735-3738), `BlockGained` (:3973), `PowerReceived` (PowerCmd :458, :1321), `CreatureAttacked` (AttackCommand :1145), `EnergySpent` (CardModel :1255). Cards such as Spite, Stomp, GangUp and powers such as IterationPower read `CombatManager.Instance.History` (e.g. `Models.Powers/IterationPower.cs:59`).
- Run statistics (`MapPointHistoryEntry.DamageTaken/HpHealed/MaxHpGained/MaxHpLost`, `ExtraFields.DamageDealt`) — dropped (run layer).
- `NCreature.UpdateIntent` refresh after `ModifyAmount` (PowerCmd :1337-1352) — UI, dropped.
- Multiplayer: power amount scaling (PowerCmd :550-553), killed-outside-combat heal (CreatureCmd :2597-2609), `PlayerCmd.EndTurn` on dead player (:2414-2438), `HandlePlayerDeath` (no-op in single player) — dropped.
- `RunManager.IsInProgress && !IsCleaningUp` gate around the loss check in `Kill` (:2397) — always true inside a sim combat; game-over UI/music (:2405-2412) dropped.

## 9. Approximations and deferrals

- Cross-area members used (added by sibling agents; frozen in CONTRACTS §4 since the 2026-10-30
  sync): `PlayerState.IsActiveForHooks` / `ActivateHooks()` / `DeactivateHooks()` and
  `CombatState.IsInProgress` / `IsStarting` (state), `MonsterAiRuntime.IsPerformingMove` and
  `MonsterLifecycle.StunInternal` (monsters).
  Integration (2026-10-28) checked the wiring against the decompile: `Kill` deactivates player hooks
  (`Commands/CreatureCmd.cs:2850`), `HealInternal` re-activates them on revive
  (`Entities.Creatures/Creature.cs:1067-1076`), listener liveness reads the flag
  (`Combat/CombatState.cs:1061`), and `Kill` leaves a monster that dies during its own move in
  `cs.Monsters` (`Commands/CreatureCmd.cs:2815-2822`) until `MonsterLifecycle.PerformMove` removes it
  (`Models/MonsterModel.cs:287-291`, which calls `CombatState.RemoveCreature`, not
  `CombatManager.RemoveCreature`, so `BeforeRemovedFromRoom` does not run on that path).
- `CreatureCmd.Stun(creature, nextMoveId)` wraps the stun move in a local async `Wrapper`
  (`Commands/CreatureCmd.cs:4105-4124`) whose state machine
  (`<>c__DisplayClass27_0.<<Stun>g__Wrapper|0>d`) is referenced but has no body in this decompile, so
  what the `STUNNED` move does around `stunMove` (animation only, or rule effects) is unknown. The sim
  runs `stunMove` directly (default: no-op, `:3409`). Open question; no P0 model stuns.
- A3 `CombatManager.RemoveCreature` (`Combat/CombatManager.cs:7171-7180`) calls `MonsterModel.BeforeRemovedFromRoom()` and `ResetStateMachine()`; the sim `MonsterDef` has no such hook yet (monsters agent). Only matters for monsters that override it (e.g. `Models.Monsters/SoulNexus.cs:386-392`, visual only).
- Hook parameters dropped from the sim signatures: `participants` of the side-turn hooks, `deathAnimLength` of `AfterDeath`, `AutoPlayType` of `ShouldPlay`, every `PlayerChoiceContext`. None of the five P0 powers reads them.
- Listeners other than powers (cards in piles, relics, potions, orbs, `MonsterModel`, modifiers) are not collected (CONTRACTS §9); a monster that overrides hooks will need a per-instance listener object appended after its powers (`Combat/CombatState.cs:866`, liveness `:1047`).
- Not ported: enchantment and affliction hooks, `CalculatedDamageVar`, preview damage, Osty/pets (code paths kept but identity), relic modifiers PaperPhrog (+0.25 Vulnerable, `Models.Relics/PaperPhrog.cs:16-27`) and PaperKrane (−0.15 Weak, `Models.Relics/PaperKrane.cs:16-27`), `ModifyCardPlayResultLocation`, `AfterCardChangedPiles`, `AfterDiedToDoom`, orb/star/forge/summon hooks.
- `CreatureCmd.Damage` (list overload) always dispatches through `cs`. The game takes the combat
  state from `targetList[0]` (`Commands/CreatureCmd.cs:3680`): if that first target is detached, it
  skips `ModifyUnblockedDamageTarget` and `AfterDamageGiven` and iterates run-level listeners only
  (no combat powers) for every target. Reachable only when a caller passes a dead, removed creature
  first; `AttackCommand` filters `IsAlive`, so no P0 path does.
- `AttackCommand.Execute` throws "No combat state even though combat is not over" for a detached
  attacker (`Commands.Builders/AttackCommand.cs:974-977`), as ported. Random targeting by a monster
  attacker throws `InvalidOperationException` before drawing; the game dereferences
  `(Attacker.Player ?? Attacker.PetOwner).RunState` (`:1061`) and fails with a null reference.
- `Creature.InvokeDiedEvent` (`Commands/CreatureCmd.cs:2770`) has no sim counterpart. Its rule
  subscribers (`Models.Monsters/SlumberingBeetle.cs:137`, `Models.Monsters/SoulNexus.cs:69`, pet death
  `Entities.Players/PlayerCombatState.cs:384`) are not P0 content.
- `VulnerablePower` looks up Cruelty by id with no pet-owner fallback (`Models.Powers/VulnerablePower.cs:116`
  uses the type and `dealer.PetOwner`); Cruelty and pets are not ported.

## 10. Tests (`Sts2Sim.Tests/PipelineTests.cs`)

Self-contained: private nested doubles (`MonsterDouble`, `AttackDouble`, `SkillDouble`,
`RecordingPower`, `FlatBonusPower`, `CapPower`, `PreventDeathOncePower`, `MinionPower`), a hand-built
`CombatState` (player 80 HP / 3 energy, monsters 50 HP, mock `RunRngSet(new Rng(12345))`). Depends only
on contract constructors plus `CanonicalWriter`. Coverage:
- damage formula and order (§3 table), unpowered attacks, ownership checks of each modifier, the
  `ModifyDamage` return value (non-truncated decimal), damage cap (smallest cap wins);
- block absorption with fractional damage (block loses `(int)blocked`), block broken / fully blocked
  flags, unblockable damage, multi-hit block consumption, overkill and kill bookkeeping;
- attack targeting: single, all opponents (dead skipped, `TargetSide`), random (deterministic counter,
  no-duplicates exhaustion throws), dead attacker / ended combat, dead dealer;
- death: removal from `cs.Monsters`, power stripping with `AfterRemoved`, secondary enemies dying with
  the last primary, death prevention + heal (and the last-enemy `IsEnding` heal block), player death →
  `Defeat`;
- block gain: Dexterity, Frail, both, negative Dexterity clamp, unpowered block, Dexterity ownership for
  monster block, truncation of `Block + amount`, `LoseBlock`, `ClearBlock`, turn-1 player block keep;
- powers: apply/stack into the same instance, zero-amount apply, enemy debuff ticking at enemy turn end
  only, player debuff first-tick skip, stacking does not re-arm the skip, buffs do not tick, negative
  Strength type and removal at 0, removal-by-amount rules and clamps, `IsEnding` blocks apply/modify,
  `Remove`/`Remove<T>`, duplicate non-instanced power and owner moves throw, `CloneFor` + canonical
  bytes independence, `AmountOnTurnStart` snapshot;
- listener order (player powers before monsters, normal pass before Late pass), guarded vs unguarded
  iteration while ending, `AfterModifyingDamageAmount` only for actual modifiers;
- energy gain/lose/set/reset/max/spend incl. X-cost capture; `CreatureInternals` clamps and throws.
