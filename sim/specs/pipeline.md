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

## 11. W1 kernel expansion, round 3 (2026-11-01)

Owner: pipeline (CONTRACTS Part B §B4, §B5 pipeline rows, §B11 pipeline row; amendments §B12 A5, A15, A19).
Base `263810d` (phase-0b scaffold). Citations are decompile v0.111.0 (`<ns-dir>/<File>.cs:<line>`,
`MegaCrit.Sts2.Core.` dropped, line numbers of the unfiltered file; for async methods both the readable
body and the `<Name>d__N` state machine are cited where the line order matters). This section supersedes
the statements of §2-§9 listed in §11.18; everything else in §1-§10 still holds.

### 11.1 Files

| File | Change |
| --- | --- |
| `Combat/Pipeline/CombatStatus.cs` | attachment and side queries over `cs.Allies` (§11.2); pet, Osty, player-count helpers |
| `Combat/Pipeline/CreatureCmd.cs` | `Add` ×3, the `DamageVar` / `BlockVar` overloads, pet-owner block, history sites, Kill / player death / pet bookkeeping, `HandlePlayerDeath`, `Escape`; `Stun` unchanged (IL closure §11.8) |
| `Combat/Pipeline/AttackCommand.cs` | `FromOsty`, calculated-damage attacks, pet-owner random-target stream, the `BlockingPlayerChoiceContext` of monster attacks, `CreatureAttacked` history |
| `Combat/Pipeline/PowerCmd.cs` | `Apply<T>(targets)` returns the powers, multiplayer gate, two `PowerReceived` sites, `Decrement` under a `Throwing` context |
| `Combat/Pipeline/EnergyCmd.cs` | `SpendEnergy`: `EnergySpent` history, the card owner's energy |
| `Powers/PowerModel.cs` | `ShouldScaleInMultiplayer`, `GetScaledAmountForMultiplayer`, `GetApplier` / `GetTarget` (ids → creatures) |
| `Powers/ITemporaryPower.cs` (new) | `OriginModelId`, `InternallyAppliedPowerId` (ids instead of the game's model references) |
| `Powers/MultiplierModifiers.cs` | relic scans `FirstVulnerableRelic` / `FirstWeakRelic`, relic ids |
| `Powers/CardCmdForwarders.cs` (new) | three forwarding stubs for `CardCmd` members no scaffold provides (§11.15) |
| `Powers/Defs/{Hellraiser,ToolsOfTheTrade,Entropy,DarkEmbrace,Focus,DieForYou}Power.cs` (new) | pilot powers (§11.13) |
| `Powers/Defs/Temporary{Strength,Dexterity,Focus}Power.cs` (new) | the three abstract `ITemporaryPower` bases (§11.10) |
| `Powers/Defs/{Vulnerable,Weak,Shrink}Power.cs` | re-ports (§11.14) |
| `Sts2Sim.Tests/PipelineTests.cs` | 37 tests appended (§11.19) |

`CreatureInternals.cs` and `DamageResult.cs` needed no change: `RemoveAllPowersInternal` is the game's
`RemoveAllPowersInternalExcept()` with no exception list (`Entities.Creatures/Creature.cs:1225-1244`), used by
`Escape`.

### 11.2 `CombatStatus` (B12 A19)

| Sim | Game | Rule |
| --- | --- | --- |
| `IsAttached(cs, c)` | `Creature.CombatState != null` for a creature of this combat; `CombatState.ContainsCreature` (`Combat/CombatState.cs:770-777`) | `c` is in `cs.Allies` (player, then pets) or `cs.Monsters`. A removed creature is unattached, as `RemoveCreature(c, unattach: true)` nulls its `CombatState` (`:739-768`) |
| `CombatOf(cs, c)` | `c.CombatState` | `cs` when attached, else `null` |
| `FindCreature(cs, id)` | `CombatState.GetCreature(combatId)` (`:784-791`) over `Creatures` = allies then enemies (`:558`) | scans `cs.Allies` then `cs.Monsters`; independent of the state-owned `GetCreatureById`, which does not list pets yet (A19 is the state owner's) |
| `GetCreaturesOnSide` / `GetOpponentsOf` / `GetTeammatesOf` | `:831-848` | non-`Enemy` side → `Allies` (player and pets), `Enemy` → `Monsters` |
| `GetPlayerCreatures` | `PlayerCreatures` (`:560`) | allies that are players; pets are allies but not player creatures (design-subsystems §1.5) |
| `CanReceivePowers` | `Creature.CanReceivePowers` (`Entities.Creatures/Creature.cs:656-669`) | attached and `Hook.ShouldAllowHitting` |
| `CardOwnerCreature` / `CardOwner` | `card.Owner.Creature` | `card.Owner` when the card has one, else `cs.Player` (P0 cards are still ownerless until B12 A23 lands; identical in single player) |
| `PetOwnerOf(cs, c)` / `IsPet` | `Creature.PetOwner` / `IsPet` (`Creature.cs:520-537`) | a `MonsterState` with `PetOwnerId` set resolves to that player (fast path `cs.Player`) |
| `GetPet(cs, player, id)`, `GetOsty`, `IsOstyAlive`, `IsOstyMissing` | `PlayerCombatState.GetPet<T>` (`Entities.Players/PlayerCombatState.cs:389-392`), `Player.Osty` / `IsOstyAlive` / `IsOstyMissing` (`Entities.Players/Player.cs:247-251`) | first id in `player.PetIds` (the game's `_pets` in insertion order) whose creature has model id `OSTY` (`OstyMonsterId`; the game tests `Monster is Osty`); a dead Osty that stayed in the pet list is still `Osty` |
| `PlayerCount` | `combatState.Players.Count` (`Combat/CombatState.cs:562`) | players among the allies; 1 in single player (the multiplayer gates, §11.6, §11.10) |
| `AllPlayersDead` / `AllCombatPlayersDead` | `runState.Players.All(dead)` (`Commands/CreatureCmd.cs:2399`) / `State.Players.All(dead)` (`Combat/CombatManager.cs:3492`) | over `cs.Run.Players` / over the allies; the same single player |
| `ContainsById(participants, c)` | `participants.Contains(Owner)` | membership by `CombatId`, the brief's rule for the side-turn `participants` snapshots (unique ids make it equal to the game's reference test inside one state) |

TargetType.Osty has no rule branch in the pipeline: `CardModel.IsValidTarget` treats it as an untargeted card
(`Models/CardModel.cs:3362-3384`: a null target is valid for every type but `AnyEnemy` / `AnyAlly`, any non-null
target is invalid), its only other reader is the reticle (`Nodes.Combat/NCardPlay.cs:348`), and Osty cards reach
Osty through `Owner.Osty` (`CombatStatus.GetOsty`) and `AttackCommand.FromOsty` (e.g. `Models.Cards/Poke.cs`
`OnPlay`). The card-side target rule is cards-kernel's.

### 11.3 `CreatureCmd.Add` (summons and pets)

| Sim | Game |
| --- | --- |
| `Add<T>(cs, slotName)` where `T : MonsterDef, new()` | `Commands/CreatureCmd.cs:3553-3561`: `CreateCreature(ModelDb.Monster<T>().ToMutable(), Enemy, slot)` then `Add(creature)`. The sim resolves the registered def by the new instance's id (defs are shared and immutable; the mutable part lives on `MonsterState`) |
| `Add(cs, MonsterDef, side = Enemy, slotName = null) → MonsterState` | `:3563-3572`: `CombatState.CreateCreature` (`Combat/CombatState.cs:709-724`) is `MonsterSpawner.SpawnForAdd` (monsters-kernel), then `Add(creature)` |
| `Add(cs, CreatureState creature)` | `:3575` + state machine `<Add>d__2` `:219-330`, in this order: throw `"Attempted to add a creature outside of combat."` unless `IsInProgress` (`:270`); `CombatState == null` throws (`:275`, a sim creature belongs to `cs` by construction); `IsLiveCombat()` is always true (`Combat/CombatState.cs:1077-1080`); `CombatState.AddCreature` (`:279`; `:993-1006`: allies for side `Player`, else enemies, `"Creature is already in this combat, but AddCreature was called on it again."`) → `cs.AddAlly` (state) or `cs.AddMonster`; `CombatManager.AddCreature` (`:280`; `Combat/CombatManager.cs:6990-7005`: contains check, `Monster.SetUpForCombat()`, `SortEnemiesBySlotName()` when the creature has a slot) → `MonsterLifecycle.SetUpForCombat`, `MonsterSpawner.SortEnemiesBySlotName(cs, encounter)` (encounter = `EncounterRegistry` entry of `cs.EncounterId`, none for an unregistered id, as the game's `Encounter == null`); `CombatManager.AfterCreatureAdded` (`:282`; state machine `Combat/CombatManager.cs:497-555`: `Creature.AfterAddedToRoom` only for side `Enemy` (`Entities.Creatures/Creature.cs:36-90`), then `RollMove` when `IsEnemy && CurrentSide == Player`) → `MonsterLifecycle.AfterCreatureAdded`; `PrepareForNextTurn(players, rollNewMove: false)` when `CurrentSide != Enemy` (`:300`; `Creature.cs:1128-1138`: no roll, intent refresh only) → `MonsterLifecycle.PrepareForNextTurn(cs, m, false)`; the map-point monster list (`:301-305`, run statistics) is dropped; `Hook.AfterCreatureAddedToCombat` (`:308`) |

Division of labour with monsters-kernel: `SpawnForAdd` must be `CreateCreature` only (id, `Niche` HP roll for
enemies, the multiplayer HP gate, monster `Rng`, `OnCreatureSpawned`); it must neither add the creature to a list
nor set it up, or `Add` throws its "already in this combat" error. Pets come through `PlayerCmd.AddPet` (loop,
`Commands/PlayerCmd.cs:946-967`), which runs `AddPetInternal` and then `CreatureCmd.Add(pet)`: a pet joins the
allies, is set up, never gets `AfterAddedToRoom` or a roll (B12 A15). `Add` rejects a `PlayerState` with an
`ArgumentException` (players join through the `CombatState` constructor, the game's `AddPlayer`,
`Combat/CombatState.cs:703-707`; no v0.111.0 caller passes a player to `Add`).

### 11.4 Damage kernel (`Commands/CreatureCmd.cs:3662-3870`)

- Overloads (all forward, `:3588-3660`): `(target, DamageVar, card, play)`; `(target, amount, props, card, play)`
  (dealer = the card owner's creature, `:3596-3603`); `(targets, DamageVar, dealer)`; `(targets, amount, props,
  dealer)`; `(target, DamageVar, dealer)`; `(target, amount, props, dealer)`; `(target, DamageVar, dealer, card,
  play)`; `(target, amount, props, dealer, card, play)`; `(targets?, DamageVar, dealer, card, play)`; the list body.
  A `DamageVar` forwards `BaseValue` and `Props`.
- `targets == null` → no results (`:3666-3669`); a dead dealer → one empty result per target (`:3670-3673`).
- The combat of the whole call is `targetList[0].CombatState` (`:3680`): `CombatOf(cs, first)`. With an attached
  first target this is `cs` (every P0 and W1 path). With a detached first target the game dispatches every hook
  to run listeners only (`(rs, null)`, a hooks-owned path that is a stub at the base), skips
  `ModifyUnblockedDamageTarget` (`:3701`) and `AfterDamageGiven` (`:3841-3844`), and, in a live combat that is not
  ending, records `DamageReceived` against a null combat state (`:3737`), which dereferences null: the sim throws
  `InvalidOperationException` there. This replaces the P0 approximation of §9.
- Block (`:3697-3698`): the absorbing creature is `originalTarget.PetOwner?.Creature ?? originalTarget` →
  `CombatStatus.PetOwnerOf(cs, target) ?? target`. The flags keep reading the original target (`:3708-3709`), so a
  hit on a pet with 0 block that the owner's block absorbs is `WasBlockBroken` and `WasFullyBlocked` (test
  `HitOnPetTakesItsOwnersBlock`).
- HP phases: `ModifyHpLost(BeforeOsty)` on the original target (`:3699-3700`), `ModifyUnblockedDamageTarget`
  (`:3701`, `DieForYouPower` is its only v0.111.0 rule override), `ModifyHpLost(AfterOsty)` on the receiving
  creature (`:3702-3703`), redirected overkill back to the original target through `ModifyHpLost(AfterOsty)`
  (`:3716-3725`); unchanged from P0 and now reachable.
- `DamageReceived` history (`:3733-3738`): once per damage result of a target (the receiving creature's, then
  the original target's in the redirect case), right after that target's HP loss, iff `IsInProgress &&
  !IsEnding`. The killing blow on the last primary enemy is therefore not recorded (test
  `KillingBlowOnTheLastEnemyIsNotRecorded`). Hit VFX, damage numbers, SFX, screen shake and
  `MapPointHistoryEntry.DamageTaken` (`:3739-3810`) are dropped; `Task.WhenAll(hitTriggers)` waits on
  `TriggerAnim` only.
- Post-loop per result (`:3821-3866`): `AfterBlockBroken` (the original target's combat; a detached receiver would
  dereference null in the game, the sim keeps `cs`), `AfterCurrentHpChanged(rs, combatState, …)`,
  `ExtraFields.DamageDealt` (run statistics, dropped), `AfterDamageGiven` only with a combat, `AfterDamageReceived`
  unless killed, then `Kill(killed)` (`:3867`).

### 11.5 Block and HP

- `GainBlock(creature, BlockVar, play)` (`:3942-3948`) forwards `BaseValue` / `Props`; the `fast` flag only
  selects a wait (`:3974-3981`).
- `BlockGained` history after `GainBlockInternal`, inside `modifiedAmount > 0`, with `(int)modifiedAmount`
  (`:3970-3973`, state machine `:1872`).
- `Heal` (`:2064-2245`): unchanged; the `AfterCurrentHpChanged` gate `creature.CombatState != null` (`:2209`) is
  now `IsAttached` over the allies, so a pet's heal dispatches.
- `SetCurrentHp` (`:3118-3205`): `AfterCurrentHpChanged(rs, creature.CombatState, …)` → `CombatOf(cs, creature)`.

### 11.6 Death (`Kill`, `KillWithoutCheckingWinCondition`)

`Kill(creatures, force)` (state machine `:2316-2456`): snapshot, `KillWithoutCheckingWinCondition` per creature;
`RunManager.IsInProgress && !IsCleaningUp` is always true in a sim combat (`:2397`); when the list held a player
and every run player is dead → `LoseCombat` (only while `IsInProgress`, `:2399-2404`); else, in progress, a dead
player of the list ends its turn through `PlayerCmd.EndTurn` (`:2412-2430`), which needs another living player:
the sim throws there (unreachable with one player: a dead listed player means every player is dead). The
game-over UI and music (`:2405-2411`) are dropped.

`KillWithoutCheckingWinCondition` (state machine `:2464-2906`), traced order:

1. Return unless `CombatState != null || IsPlayer` (`:2595`).
2. Multiplayer gate (`:2597-2609`): `!IsInProgress && IsPlayer && Players.Count > 1 && !force` → `Heal(1)` and
   return; ported literally with `PlayerCount(cs) > 1` (false in single player).
3. `LoseHpInternal(CurrentHp, Unblockable | Unpowered)` + `AfterCurrentHpChanged(−hp)` when `CurrentHp > 0`
   (`:2612-2627`); `BeforeDeath` (`:2753`).
4. Not prevented (`force || MaxHp <= 0 || ShouldDie`, `:2768`):
   - `InvokeDiedEvent()` (`:2770`; `Creature.cs:1100-1103`). The only rule subscriber is
     `PlayerCombatState.OnPetDied` (`Entities.Players/PlayerCombatState.cs:433-445`; subscribed in
     `AddPetInternal` `:375-387`): a pet still in its owner's pet list leaves it iff
     `ShouldCreatureBeRemovedFromCombatAfterDeath(pet)`. The subscription exists exactly while the pet is in
     `_pets`, so the sim tests `owner.PetIds.Contains(pet)` instead of an event. The other subscribers are VFX:
     `Models.Monsters/SlumberingBeetle.cs:137` / `:450-455`, `Models.Monsters/SoulNexus.cs:69` / `:380-384`, and
     nodes.
   - `shouldRemoveFromCombat = combatState != null && ShouldCreatureBeRemovedFromCombatAfterDeath` (`:2771`); the
     death animation (`:2772-2781`) is dropped with `deathAnimLength` (D12); `AfterDeath(false)` (`:2782`).
   - Living teammates (`:2811`; `GetTeammatesOf` → allies for the player side).
   - An enemy that should be removed and is still listed: `CombatManager.RemoveCreature` (`:2817`;
     `Combat/CombatManager.cs:7171-7180`: `BeforeRemovedFromRoom`, `ResetStateMachine`, tracker) then
     `CombatState.RemoveCreature` unless it is performing its move (`:2819-2822`). `BeforeRemovedFromRoom` is
     rule-inert in v0.111.0 (overriders: SFX loops, music, spine animation, and `TestSubject`'s
     `PowerApplied` / `PowerRemoved` unsubscription, `Models.Monsters/TestSubject.cs:1239-1243`, on a creature that
     leaves the combat), and `ResetStateMachine` (`Models/MonsterModel.cs:693-696`) nulls the move machine of a
     creature that is removed then or after its move (`Models/MonsterModel.cs:286-291`), so neither is observable;
     neither has a sim member (monsters-kernel), which is recorded as an open question, not a flag.
   - `isPrimaryEnemy` (`:2825`), `RemoveAllPowersAfterDeath` (`:2826`; `Creature.cs:1246-1249`, predicates
     evaluated before any removal) and `AfterRemoved` per removed power (`:2660-2680`).
   - Enemy: the last primary enemy takes its living secondary teammates with it (`:2688-2698`).
   - Player (B12 A15): `PlayerCombatState?.OrbQueue.Clear()` (`:2704`), then `Kill(player.Osty, force)` iff
     `IsOstyAlive` (`:2705-2707`) — no other pet dies, and Osty's death hooks run while the player's listeners are
     still active —, then `DeactivateHooks()` (`:2850`), then `HandlePlayerDeath` when the player has a combat
     (`:2851-2853`).
5. Prevented: recursion ≥ 10 throws (`:2792-2795`); `AfterDeath(true)` (`:2796`), `AfterPreventingDeath`
   (`:2837`), and again while still dead (`:2869-2871`).

`HandlePlayerDeath` (`Combat/CombatManager.cs:7095-7106`, state machine `:3444-3560`): nothing unless the combat
is live and not every combat player is dead (`:3492`), which with one player needs the dead player to have been
healed between its death and this call (by an `AfterDeath` / `AfterRemoved` listener or Osty's death hooks).
Then, unless a card or potion effect is running (`:3496`, `cs.CardOrPotionEffectDepth`),
`RemoveDeadPlayerCardsFromCombat` (`:3576-3650`: hand, draw, discard, exhaust, play in that order through the
batch `CardPileCmd.RemoveFromCombat`, which removes all cards before it dispatches `AfterCardChangedPiles` per card,
`Commands/CardPileCmd.cs:2205-2460`), `SetEnergy(0)`, `SetStars(0)`. The sim has only the loop's single-card
`RemoveFromCombat`, so it removes card by card and records `FidelityFlag.ApproximatedRule` with
`CreatureCmd.HandlePlayerDeathFidelitySite`; `StarsCmd.SetStars` is a character-mechanics stub.

Mid-resolution rules (design-combat-kernel §7) on this side: guarded dispatches (`AfterAttack`, `BeforeAttack`,
block and card hooks) go silent once the last primary enemy is dead, so the killing attack's `AfterAttack` does
not run (test `KillingBlowOnTheLastEnemyIsNotRecorded`) while the death hooks, which are run-sourced, do; the
killing blow is not recorded as `DamageReceived`; a monster dying during its own move stays listed until the move
ends; the win check is the loop's.

### 11.7 `Escape` (`Commands/CreatureCmd.cs:3915-3939`)

Dead → return; not in a live combat → return (`:3917-3924`); `RemoveAllPowersInternalExcept()` (no
`AfterRemoved`, `:3925`; `Creature.cs:1225-1244`); the node removal (`:3926-3935`, `removeCreatureNode`) is UI;
`CombatManager.RemoveCreature` (`:3936`, rule-inert as in §11.6); `CombatState.CreatureEscaped` (`:3937`;
`Combat/CombatState.cs:733-737`: append to `_escapedCreatures`, then `RemoveCreature`, which removes from the
enemies, else the allies, else throws, `:739-768`). The sim has no escaped-creature list (state owner); its only
readers are reward gold (`Models/EncounterModel.cs:324-327`, `Models.Encounters/GremlinMercNormal.cs:56-67`, W2),
so `Escape` records `FidelityFlag.ApproximatedRule` with `CreatureCmd.EscapeFidelitySite`. Callers (not ported):
`Models.Monsters/ThievingHopper.cs:198`, `Models.Monsters/FatGremlin.cs:141`,
`Models.Powers/BattlewornDummyTimeLimitPower.cs:87`.

### 11.8 `Stun` — open question closed

`Stun(creature, stunMove, nextMoveId)` (`Commands/CreatureCmd.cs:4105-4125`) calls `Creature.StunInternal(Wrapper,
nextMoveId)` (`Creature.cs:1105-1125`). The wrapper's state machine has no body in the decompile; the IL
(`IL:CreatureCmd.<>c__DisplayClass27_0.<<Stun>g__Wrapper|0>d.MoveNext`, 255 bytes, read with the scratch
`System.Reflection.Metadata` probe of round 2 against `output/server-bootstrap/linux-assembly/sts2.dll`) is:
`IL_0014-IL_0065` `NStunnedVfx.Create(creature)`, `Creature.GetVfxContainer`, `Godot.Callable.From(…).CallDeferred()`
(VFX only), then `IL_006a-IL_0076` `stunMove.Invoke(c)`, awaited (`IL_007b-IL_00cb`). That is the v0.107.1 body
(`Commands/CreatureCmd.cs:884-904` of that tree). The sim's direct pass-through of `stunMove`
(`MonsterLifecycle.StunInternal`) is exact; §9's open question and design-combat-kernel §8 Q8 are closed.

### 11.9 `AttackCommand` (`Commands.Builders/AttackCommand.cs`)

- `FromOsty(osty, card, play)` (`:765-779`): `ArgumentException("Creature is not Osty")` unless the creature is a
  `MonsterState` with model id `OSTY` (the game: `osty.Monster is Osty`); attacker = Osty, model source = card,
  card play, source type Card; no "already set" guards (unlike `FromCard`); the attacker sound and animation are
  UI.
- Calculated damage: the game's second constructor (`:732-736`) sets `_damagePerHit = -1` and keeps a
  `CalculatedDamageVar`; each hit computes `_calculatedDamageVar.Calculate(singleTarget)` after target selection
  (`:1131`; `Localization.DynamicVars/CalculatedVar.cs`: `CalculationBase + CalculationExtra × multiplier(card,
  target)` while the combat is in progress). `DamageCmd.Attack(CombatState, Func<CombatState, CreatureState?,
  decimal>)` (`Commands/DamageCmd.cs:13`) takes that per-hit calculation; `DamagePerHit` is
  `CalculatedDamagePerHit = -1`, `IsCalculated` is true; a multi-target hit passes a null target. cards-kernel's
  `CalculatedVar` (B7) supplies the function (`(cs, t) => var.Calculate(cs, card, t)`), so the pipeline never names
  a cards-owned type.
- Random targets draw `(Attacker.Player ?? Attacker.PetOwner).RunState.Rng.CombatTargets` (`:1061`): the
  combat's `CombatTargets` for a player or a pet attacker; a monster attacker throws, as the game dereferences
  null.
- Monster attacks: every monster attack in v0.111.0 is `Execute(null)` (209 call sites in `Models.Monsters`, none
  elsewhere; cards pass their context, 196 sites), so its damage and `AfterAttack` run under a
  `BlockingPlayerChoiceContext` (`:1132`, `:1146`). The sim pushes `ChoiceContextKind.Inline` (the B1 kind of every
  `BlockingPlayerChoiceContext` site) around the hits and `AfterAttack` of a `FromMonster` attack, so a listener that
  prompts in reaction to an enemy attack opens its prompt inline, also under a monster command's `Throwing`
  context (test `MonsterAttacksRunUnderAnInlineContext`).
- `Execute` (`:961-1148`): the order of §2 is unchanged; `IsOverOrEnding` returns only for a live combat, which
  is every sim combat (`:970-973`); the attacker check is the attacker's combat (`:966-977`); `attackCount` is the
  decimal result of the integer dispatcher (`:987`; `Hooks/Hook.cs:10052-10069`); `validTargets.Count == 0 &&
  IsLiveCombat()` breaks (`:995`). `CreatureAttacked` history with the flattened results of all hits, after the
  loop and before `AfterAttack` (`:1145`), also when no hit landed.
- `AttackCommand.CreateContextAsync` / `AttackContext` (`Commands.Builders/AttackContext.cs`, Omnislice and
  EchoingSlash) are not in `cmd_facades` and not ported.

### 11.10 `PowerCmd` and `PowerModel`

- `Apply<T>(targets)` returns the non-null powers it applied, in target order (`Commands/PowerCmd.cs:1185-1212`);
  a null list → empty. Callers that ignore the result are unaffected.
- `Apply(power)` (state machine `:270-562`, readable form absent): the multiplayer scaling gate
  `Players.Count > 1 && (IsPrimaryEnemy || IsSecondaryEnemy) && ShouldScaleInMultiplayer` (`:550-553`) is ported
  with `PlayerCount(cs) > 1`; `PowerModel.ShouldScaleInMultiplayer` (default false, `Models/PowerModel.cs:309`) and
  `GetScaledAmountForMultiplayer` (`:619-622`, players × the multiplayer scaling model; throws
  `NotSupportedException` in the sim, reachable only with more than one player). `PowerReceived` history after
  `ApplyInternal`, iff `modifiedAmount != 0` (`:455-459`).
- `ModifyAmount` (`:1299-1358`): `PowerReceived(modifiedOffset)` unconditionally, before `SetAmount` (`:1321`).
- `Decrement` runs `ModifyAmount(new ThrowingPlayerChoiceContext(), power, -1, null, null)` (`:614`): the sim pushes
  `ChoiceContextKind.Throwing` for the call (B1: a prompt there throws, as `ThrowingPlayerChoiceContext.
  SignalPlayerChoiceBegun` does, `GameActions.Multiplayer/ThrowingPlayerChoiceContext.cs`). It is the only
  `ThrowingPlayerChoiceContext` in `Commands/*.cs`.
- `PowerModel.GetApplier(cs)` / `GetTarget(cs)` resolve the `Applier` / `Target` references
  (`Models/PowerModel.cs:269-293`) from the stored ids.
- `ITemporaryPower` (`Models/ITemporaryPower.cs`): `OriginModel` and `InternallyAppliedPower` become
  `OriginModelId` and `InternallyAppliedPowerId` (model id strings). Its implementors are the three abstract bases
  `TemporaryStrengthPower`, `TemporaryDexterityPower`, `TemporaryFocusPower`
  (`Models.Powers/Temporary{Strength,Dexterity,Focus}Power.cs:18`), ported 1:1 and identical but for the inner
  power: `Type` Buff when `IsPositive` (default true) else Debuff (`:268-278`), Counter (`:280`); `BeforeApplied`
  applies `Sign × amount` of the inner power silently under a `ThrowingPlayerChoiceContext` (`:234`);
  `AfterPowerAmountChanged(power, amount)` applies `Sign × amount` when `amount != Amount && power == this`
  (`:63-65`), i.e. on stacking, not on the first application; `AfterSideTurnEnd` with the owner among the
  participants removes itself, then applies `−Sign × Amount` with the owner as applier (`:151-170`). The 21
  concrete subclasses (`AnticipatePower` … `SynchronizePower`) only override `OriginModel` and, for the "down"
  variants, `IsPositive => false`, so each is a two-line W3 port. The 5 files that mention `ITemporaryPower` in
  `Models.Powers` are these 3 bases plus the readers `IllusionPower.cs:300` and `SleightOfFleshPower.cs:59`; the
  other readers are `Models.Cards/Misery.cs:116`, `Models.Cards/Rend.cs:128`, `Models.Relics/UnsettlingLamp.cs:165`
  and the badge `DebufferModel.cs:12`.

### 11.11 `EnergyCmd.SpendEnergy` (`Models/CardModel.cs:3421-3431`, state machine `:1221-1271`)

X-cost capture; when `amount > 0`: `EnergySpent(amount, card.Owner)` history (`:1255`) before
`card.Owner.PlayerCombatState.LoseEnergy` (`:1256`) — the card owner's energy (`CombatStatus.CardOwner`); then
`AfterEnergySpent` (`:1258`).

### 11.12 Combat history sites (D11)

| Entry | Site | Gate | Sim |
| --- | --- | --- | --- |
| `DamageReceived(receiver, dealer, result, cardSource)` | `Commands/CreatureCmd.cs:3737` (state machine `:776`) | `IsInProgress && !IsEnding`, per damage result | `CreatureCmd.Damage` |
| `BlockGained(receiver, (int)amount, props, cardPlay)` | `:3973` (`:1872`) | `modifiedAmount > 0` | `CreatureCmd.GainBlock` |
| `PowerReceived(power, modifiedAmount, applier)` | `Commands/PowerCmd.cs:458` | `modifiedAmount != 0`, after `ApplyInternal` | `PowerCmd.Apply` |
| `PowerReceived(power, modifiedOffset, applier)` | `:1321` (`:883`) | none, before `SetAmount` | `PowerCmd.ModifyAmount` |
| `CreatureAttacked(attacker, results)` | `Commands.Builders/AttackCommand.cs:1145` (`:422`) | none, after the hits | `AttackCommand.Execute` |
| `EnergySpent(amount, owner)` | `Models/CardModel.cs:1255` | `amount > 0` | `EnergyCmd.SpendEnergy` |

`CombatHistory.Add` appends iff `IsLiveCombat()` (`Combat.History/CombatHistory.cs:159-170`), always true. The
log is part of the canonical state (§10 of scaffold-w1), so wiring these sites moves `StateHash` for every later
state: the final-state pin of `EndToEndTests.Test_EndToEnd_CanonicalHashesArePinnedAcrossProcesses` (owned by the
loop owner per B12 A13) becomes `0xB899C4BCA7235886` in this worktree (§11.20); the opening pin is unchanged
because nothing is damaged, blocked, buffed or spent before the first decision of seed 1.

### 11.13 Pilot powers (complete 1:1 ports)

| Power (id) | Game | Rule |
| --- | --- | --- |
| `HellraiserPower` (`HELLRAISER_POWER`) | `Models.Powers/HellraiserPower.cs` | Buff, Single (`:131-133`). `AfterCardDrawnEarly`: only the owner's Strike-tagged cards (`:69`); when every hittable enemy shows infinite HP (`HittableEnemies.All(HpDisplay.IsInfinite)`, `:73`, vacuously true with no hittable enemy) the count is checked against 9 (`:75`, the literal of `_infiniteAutoPlayCap`, `:129`; at or above it the card is not auto-played, the cap message is UI, `:78-82`) and then incremented (`:84`), otherwise the count resets (`:88`); then `CardCmd.AutoPlay(card, null)` (`:93`). `AfterSideTurnEnd`: reset when the owner is a participant (`:155-162`). `BeforeAttack` (`:165-174`) only restyles the hit VFX of its auto-played cards, so the `autoPlayingCards` set (`:92`, `:107`) and `showedCapReachedMessage` (`:81`) are UI state and not ported; `InfiniteAutoPlaysThisTurn` is cloned and hashed. `HpDisplay` (`Entities.Creatures/HpDisplay.cs`, set by `HardenedShellPower.cs:66`/`:75` and `WaterfallGiant.cs:918`, neither ported) has no sim field: every creature reads as `Normal`, which is exact for every ported creature (open question) |
| `ToolsOfTheTradePower` (`TOOLS_OF_THE_TRADE_POWER`) | `Models.Powers/ToolsOfTheTradePower.cs` | Buff, Counter (`:122-124`). `ModifyHandDraw`: `+Amount` for the owner's player (`:126-133`). `AfterPlayerTurnStart` for the owner's player (`:72`): `CardSelectCmd.FromHandForDiscard(player, prefs(TO_DISCARD, Amount), null, this)` (`:74`), then `CardCmd.Discard(selected)` when any (`:90-93`). Site id `TOOLS_OF_THE_TRADE_POWER:AfterPlayerTurnStart`, a turn-setup prompt site (B1) |
| `EntropyPower` (`ENTROPY_POWER`) | `Models.Powers/EntropyPower.cs` | Buff, Counter (`:150-152`). `AfterPlayerTurnStart` for the owner's player (`:75`): `CardSelectCmd.FromHand(prefs(TO_TRANSFORM, Amount), filter null, source this)` (`:77`), then `CardCmd.TransformToRandom(card, Rng.CombatCardSelection)` per selected card in selection order (`:100-108`) |
| `DarkEmbracePower` (`DARK_EMBRACE_POWER`) | `Models.Powers/DarkEmbracePower.cs` | Buff, Counter (`:179-181`). `AfterCardExhausted` of a card owned by its owner (`:64`): not ethereal → `CardPileCmd.Draw(Amount)` (`:66-68`), ethereal → `etherealCount++` (`:78`). `AfterSideTurnEnd` with the owner a participant (`:141`): `Draw(Amount × etherealCount)` (`:144`, `int` product) then `etherealCount = 0` (`:157`). `EtherealCount` is cloned and hashed |
| `FocusPower` (`FOCUS_POWER`) | `Models.Powers/FocusPower.cs:8-21` | Buff, Counter, AllowNegative. `ModifyOrbValue`: unchanged unless the orb's owner is the power owner's player (`:16`, a reference compare, so an ownerless orb is unchanged), else `Max(value + Amount, 0)` (`:20`) |
| `DieForYouPower` (`DIE_FOR_YOU_POWER`) | `Models.Powers/DieForYouPower.cs:9-49` | Buff, Single; `ShouldPlayVfx` false (UI). `ModifyUnblockedDamageTarget`: a powered attack on its owner's pet owner is redirected to the owner while the owner lives (`:15-30`); `ShouldAllowHitting(creature) => creature.IsAlive` for every creature (`:32-35`); `ShouldCreatureBeRemovedFromCombatAfterDeath` false only for its owner (`:37-44`), which also keeps a dead Osty in the pet list; `ShouldPowerBeRemovedAfterOwnerDeath` false (`:46-49`). Applied by `OstyCmd.Summon` (`Commands/OstyCmd.cs:377`, character-mechanics, by id) |

Prompts go through `CardSelectCmd` (choice) with `ChoiceSource.For(<power id>, NoInstance, "AfterPlayerTurnStart")`
(powers have no instance id). The pause and the per-listener context of `AfterPlayerTurnStart` are the
dispatcher's and the choice runtime's (B1); no pilot pushes a context.

### 11.14 Re-ported powers

- `VulnerablePower` (`Models.Powers/VulnerablePower.cs:98-128`, B12 A5): target is the owner and the attack is
  powered (`:100-107`) → `1.5` (`:96`, `:108`); with a dealer: the dealer's relic (`dealer.Player?.GetRelic
  <PaperPhrog>()`, `:111-115`) → the first relic of `(dealer as PlayerState)?.Relics` that implements
  `IVulnerableMultiplierModifier` (only `PaperPhrog` does in v0.111.0: `+0.25` unless the target is its owner and
  for powered attacks only, `Models.Relics/PaperPhrog.cs:16-27`; melted relics are not skipped, as `GetRelic`);
  then Cruelty from the dealer, else from the dealer's pet owner (`:116-120`; by id `CRUELTY_POWER`,
  `Models.Powers/CrueltyPower.cs:17-28`: `+Amount/100` unless the target is its owner); then the target's Debilitate
  (`:122-126`; `DEBILITATE_POWER`, `DebilitatePower.cs:99-110`: `+(num − 1)`). A pet has no `Player`, so an Osty
  dealer consults no relic but its owner's Cruelty.
- `WeakPower` (`Models.Powers/WeakPower.cs:98-120`): dealer is the owner and the attack is powered → `0.75`; the
  target player's relic (`target?.Player?.GetRelic<PaperKrane>()`, `:109-113`) → the first relic of
  `(target as PlayerState)?.Relics` implementing `IWeakMultiplierModifier` (`PaperKrane`: `−0.15` for powered attacks
  on its owner, `Models.Relics/PaperKrane.cs:16-27`); then the dealer's Debilitate with the dealer as target
  (`:114-118`; `DebilitatePower.cs:112-123`: `−(1 − num)`).
- `ShrinkPower` (`Models.Powers/ShrinkPower.cs`): `AfterSideTurnEnd` decrements a finite Shrink iff the owner is
  among the participants (`:129-131`), whatever the side; `AfterDeath` removes it when its applier dies
  (`:60-62`); the multiplier is `(100 − 30) / 100` (`:163`, `:255-263`); `AfterApplied` / `AfterRemoved` only scale
  the node (UI). The P0 `IsTurnParticipant(cs, side)` reconstruction is no longer used by the rule; it stays public,
  unchanged, because `WurmBeetleTests.Test_WurmBeetle_ShrinkDurationFollowsTurnParticipants` (monsters-owned)
  calls it, and can be deleted at integrate.
- `RavenousPower` keeps `CorpseSlug.GetExtra(monster)`: the game casts `(CorpseSlug)Owner.Monster`
  (`Models.Powers/RavenousPower.cs:95`, `:164`), so the type use mirrors the decompile; the flag lives in the
  monsters-owned `CorpseSlugExtraState` and has no id-based seam. Accepted exception to "cross-kind references by
  id" until monsters-kernel offers one.

### 11.15 Cross-area seams

`Powers/CardCmdForwarders.cs`: the scaffold stubs no `CardCmd` member (neither `Combat/Pipeline/CardCmd.cs` of
cards-kernel nor `Combat/Loop/CardCmd.Play.cs` of loop exists at `263810d`), so the pilots call three local
forwarders that throw `NotImplementedException` naming the owner and the game line:
`AutoPlay(cs, card, target, type = Default)` (loop, `Commands/CardCmd.cs:1779`, used by Hellraiser),
`Discard(cs, cards)` (loop, `:1825` → `DiscardAndDraw(cards, 0)`, used by ToolsOfTheTrade),
`TransformToRandom(cs, card, rng)` (cards-kernel, `:1949`, used by Entropy). Integrate replaces each body with the
call to the real member. Until then the B2-chain gate (Hellraiser auto-play) throws inside `Step`.

Other members called before their owners land (they throw `NotImplementedException` at the base and are reached in
tests only through `W1Pending`): `CombatState.AddAlly` / `RemoveAlly` (state), `MonsterSpawner.SpawnForAdd`
(monsters-kernel), `CardSelectCmd.FromHand` / `FromHandForDiscard` (choice), `Hook.ModifyOrbValue` and the
`(rs, null)` run-listener path (hooks), `StarsCmd.SetStars` (character-mechanics). The listener snapshot still
visits only the player's and the monsters' powers (hooks owner, B3); pets' powers (`DieForYouPower` on Osty) are
visited once it walks `cs.Allies`. A scratch run with `AddAlly`, an allies-walking snapshot, `SpawnForAdd` and a
minimal `AutoPlay` filled in locally (not committed) ran all eleven state-, monsters- and loop-dependent tests for
real: 87 passed, 0 failed.

### 11.16 Fidelity flags and deliberate non-flags

Recorded (`FidelityFlag.ApproximatedRule`, site string in the code): `CreatureCmd.EscapeFidelitySite` (no
escaped-creature list) and `CreatureCmd.HandlePlayerDeathFidelitySite` (per-card instead of batch removal, a branch
that needs a revived dead player). No P0 path reaches either (the diff gate stays 4/4).

Not flagged, because no reachable state differs: `HpDisplay` read as `Normal` (no ported creature sets it);
`BeforeRemovedFromRoom` / `ResetStateMachine` (§11.6); the multiplayer branches (`PlayerCount(cs) > 1`, the dead
player's `EndTurn`), which throw if ever reached; the detached-first-target `DamageReceived` crash, which throws as
the game fails. The P0 approximations listed in §9 are ported now: history recording, the Cruelty pet-owner
fallback, the Died-event pet bookkeeping, the detached first target, PaperPhrog / PaperKrane.

### 11.17 Round-2 findings (`verify-notes-pipeline.md`)

| Finding | Resolution |
| --- | --- |
| [high] Vulnerable: PaperPhrog step and pet-owner Cruelty fallback missing | Ported in the game's order (§11.14); tests `VulnerableConsultsTheDealersMultiplierRelic` (10 → 17.5, HP loss 17), `VulnerableAndWeakApplyRelicThenCrueltyThenDebilitate` (20, 30, Osty dealer 17.5) |
| [high] Weak: PaperKrane consult missing | Ported (§11.14); `WeakConsultsTheTargetsMultiplierRelic` (10 → 6), Debilitate after the relic (→ 2) |
| [high] Damage takes block from the pet, not its owner | `PetOwnerOf(cs, target) ?? target` (§11.4); flags on the original target; `HitOnPetTakesItsOwnersBlock` |
| [high] CombatStatus ignores `cs.Allies` | All attachment and side queries walk the allies (§11.2); the dependent gates (`CanReceivePowers`, Heal's hook gate, `PowerCmd` owner/applier checks, the Kill gate, the attacker check) follow; `PetsAreAttachedAlliesOnThePlayerSide` |
| [medium] `CreatureCmd.Add` not ported, `AfterCreatureAddedToCombat` never dispatched | Ported (§11.3) through `MonsterSpawner.SpawnForAdd`; `AddCreatureSetsUpRollsAndDispatches`, `AddMonsterDefSpawnsThroughTheSpawner`, `AddPetJoinsTheAllies` |
| [medium] Player death skips `OrbQueue.Clear` and the Osty kill; Died-event bookkeeping missing | Ported (§11.6); `PlayerDeathKillsOnlyAnAliveOstyBeforeDeactivatingHooks`, `DeadOstyIsNotKilledAgainWithItsOwner`, `PetDeathDropsItFromPetIdsUnlessKeptInCombat` |
| [medium] / [high] History recorded at none of the sites | All six sites (§11.12); `HistoryRecordsDamageBlockPowerAndEnergyAtTheGameSites`, `KillingBlowOnTheLastEnemyIsNotRecorded` |
| [medium] Shrink uses `IsTurnParticipant` | Participants by combat id (§11.14); `ShrinkTicksByParticipantMembership` |
| [medium] `FromOsty`, the Osty random-target stream, `DamageCmd.Attack(CalculatedDamageVar)` missing | Ported (§11.9); `FromOstyAcceptsOnlyOsty`, `OstyRandomAttackDrawsThePetOwnersTargetRng`, `CalculatedAttackComputesEachHitAfterTargetSelection` |
| [medium] `Escape` missing | Ported with a flag (§11.7); `EscapeStripsPowersSilentlyRemovesAndFlags` |
| [low] Stun IL | Closed (§11.8) |
| [low] DamageVar / BlockVar overloads | Ported (§11.4, §11.5); `DamageVarAndBlockVarOverloadsForwardValueAndProps` |
| [low] Multiplayer gates dropped | Encoded with `PlayerCount(cs) > 1` (PowerCmd scaling, Kill heal) or as a throwing unreachable branch (dead player's `EndTurn`, §11.6) |
| [low] pipeline.md has no W1 section | This section; superseded statements §11.18 |
| [low] Death removal never runs `CombatManager.RemoveCreature`'s monster steps | Rebutted: rule-inert in v0.111.0 (§11.6, every `BeforeRemovedFromRoom` override read; `ResetStateMachine` only on removed creatures); recorded as an open question for monsters-kernel |
| [low] `Decrement` not under a Throwing context | Ported (§11.10); `DecrementRunsUnderAThrowingContext` |
| [high] None of the W1 pipeline work exists; pilot powers missing | Delivered (§11.1, §11.13); the five pilots plus `DieForYouPower` are registered, `PilotContentIsRegistered` no longer lists them |
| [medium] No FidelityFlag at any knowing approximation | §11.16 |
| [low] No state-level clone/hash test | `CloneAndHashAreStableAcrossPipelineOperations` (twin determinism, clone hash, clone independence, with history) |
| [low] RavenousPower uses the monsters-owned `CorpseSlug` | Accepted exception (§11.14) |

### 11.18 Superseded statements

§2 "`CalculatedDamageVar` constructor path … not ported" and "Random targeting by a monster attacker throws …
`Attacker is not PlayerState`" (pets now draw too); §3 "The pet-owner redirection … is identity in P0"; §5 "orb
queue clear and Osty kill … have nothing to act on in P0" and the `Stun` open question; §6 "(multiplayer scaling
:550-553 dropped)"; §8 the history bullet ("dropped in P0") and the multiplayer bullet; §9 the bullets on dropped
hook parameters (D12 restored `participants` and `AutoPlayType`, `deathAnimLength` stays dropped), on
`PaperPhrog` / `PaperKrane` / Osty / `CalculatedDamageVar`, on the detached first target, on `InvokeDiedEvent` and
on the Cruelty fallback. §1's "P0 listeners are powers only" still describes the snapshot at the base (hooks
owner). The power table of §6 lists 5 P0 powers; the P0 tree also holds `RavenousPower`, `ShrinkPower`,
`ThornsPower` (ported with the monsters, `monsters-corpseslugs-seapunk.md`, `monsters-wurm-beetle.md`).

### 11.19 Tests (37 appended to `PipelineTests.cs`)

Non-pending (23): `CombatStatusAttachmentWithoutPets`, `RedirectedUnblockedDamageReturnsOverkillToTheOriginalTarget`
(a player-owned redirect double: 10 damage, 2 block, a 3-HP redirect target takes 3 with overkill 5, the player
5), `DieForYouPowerRules`, `AddCreatureSetsUpRollsAndDispatches`, `EscapeStripsPowersSilentlyRemovesAndFlags`,
`CalculatedAttackComputesEachHitAfterTargetSelection`, `FromOstyAcceptsOnlyOsty`,
`MonsterAttacksRunUnderAnInlineContext`, `DecrementRunsUnderAThrowingContext`,
`HistoryRecordsDamageBlockPowerAndEnergyAtTheGameSites`, `KillingBlowOnTheLastEnemyIsNotRecorded`,
`VulnerableConsultsTheDealersMultiplierRelic`, `WeakConsultsTheTargetsMultiplierRelic`,
`VulnerableAndWeakApplyRelicThenCrueltyThenDebilitate`, `ShrinkTicksByParticipantMembership`,
`PilotPowerIdsAndTypes`, `HellraiserIgnoresCardsThatAreNotItsOwnersStrikes`,
`ToolsOfTheTradeAddsToItsOwnersHandDraw`, `DarkEmbraceDrawsOnExhaustAndAfterEtherealTurnEnd`,
`TemporaryStrengthAppliesAndRevertsItsInternalPower`, `ApplyToTargetsReturnsTheAppliedPowers`,
`DamageVarAndBlockVarOverloadsForwardValueAndProps`, `CloneAndHashAreStableAcrossPipelineOperations`.
Relic consults use test-double relics implementing the multiplier interfaces placed in `Player.Relics`
(`PhrogDouble`, `KraneDouble`, the guards of `PaperPhrog.cs:16-27` / `PaperKrane.cs:16-27`); Cruelty and
Debilitate are id-carrying doubles of `CrueltyPower.cs:17-28` / `DebilitatePower.cs:99-123`; pets are
`MonsterState`s on the player side with `PetOwnerId` and a local `MonsterDef` double (`OSTY` for Osty).

Through `W1Pending` (14 PENDING lines at the base; each runs for real once its owner lands): state —
`PetsAreAttachedAlliesOnThePlayerSide`, `HitOnPetTakesItsOwnersBlock`, `DieForYouRedirectsPoweredAttacksOnTheOwnerToOsty`,
`PlayerDeathKillsOnlyAnAliveOstyBeforeDeactivatingHooks`, `DeadOstyIsNotKilledAgainWithItsOwner`,
`PetDeathDropsItFromPetIdsUnlessKeptInCombat`, `AddPetJoinsTheAllies`, `OstyRandomAttackDrawsThePetOwnersTargetRng`;
monsters-kernel — `AddMonsterDefSpawnsThroughTheSpawner`; loop — `HellraiserAutoPlaysDrawnStrikes`,
`HellraiserCapsAutoPlaysWithoutHittableEnemiesAtNine` (nine auto-plays, the 10th and 11th Strike stay in hand, the
count reaches 11 and resets at the owner's turn end); choice — `ToolsOfTheTradePromptsItsOwnerToDiscard`,
`EntropyPromptsItsOwnerToTransform` (empty hands: nothing selected); hooks — the `Hook.ModifyOrbValue` half of
`FocusModifiesOnlyItsOwnersOrbValues`. `DieForYouRedirects…` also needs the hooks owner's allies-walking snapshot.

### 11.20 Gates and measurements

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 408 passed, 3 failed, 411 total; `PENDING total: 14`. Failures: the two known content gates (`W1Gate_B2Chain…`: only `SEEKER_STRIKE` missing now; `W1Gate_PilotContentIsRegistered`: 18 missing, none of them pipeline's) and `EndToEnd_CanonicalHashesArePinnedAcrossProcesses`, whose final-state pin moves to `0xB899C4BCA7235886` because the history sites are wired (§11.12; pin owned by the loop owner, B12 A13, recomputed at integrate) |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundles) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |
| Behaviour | 2 000 random `NIBBITS_WEAK` playouts (seeds 1000-2999): 40 447 steps on both the base and this tree (identical trajectories) |
| Deterministic cost (`NIBBITS_WEAK` bench mid-fight state, Release) | canonical form 2 373 → 3 212 bytes (15 history entries); allocation per `Clone()` 6 648 → 7 760 B; allocation per playout step 1 005 → 1 343 B; 50.2 history entries per finished episode (the P0 loop clears nothing at combat end; B12 A18's `History.Clear` is the loop's) |

The timing bench is reported in §11.21 (the machine was loaded during this round).

### 11.21 Open questions and contract change requests

1. `CardCmd` has no stub: `AutoPlay`, `Discard(cards)`, `TransformToRandom` are reached through
   `Powers/CardCmdForwarders.cs` (§11.15); integrate forwards them. The B2 chain needs `AutoPlay`.
2. `MonsterSpawner.SpawnForAdd` must stop at `CombatState.CreateCreature` (§11.3).
3. `CreatureState.HpDisplay` (state) is needed by Hellraiser once `HardenedShellPower` or `WaterfallGiant` is
   ported (§11.13).
4. An escaped-creature list on `CombatState` (state; `Combat/CombatState.cs:618`, `:733-737`) for W2's reward gold;
   `Escape` flags its absence (§11.7).
5. `MonsterDef` members for `CombatManager.RemoveCreature` (`BeforeRemovedFromRoom`, `ResetStateMachine`), if
   content ever makes them observable (§11.6).
6. The `(CombatState cs, …)` overloads of the hooks used here pass `cs`; the both-scope `(rs, cs?)` overloads are
   used where the game passes a creature's combat (`Damage`, `Kill`, `SetCurrentHp`), so a detached creature reaches
   the hooks owner's `(rs, null)` path.
7. A batch `CardPileCmd.RemoveFromCombat(cards)` (loop) would remove the `HandlePlayerDeath` flag (§11.6).
8. `ShrinkPower.IsTurnParticipant` can be deleted once `WurmBeetleTests` stops calling it (§11.14).
