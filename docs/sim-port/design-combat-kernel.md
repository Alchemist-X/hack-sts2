# Combat kernel expansion: gap analysis and design

2026-11-01 · Route C full port, kernel-expansion wave · English design note for the orchestrator.

Rule truth: decompile tree v0.111.0 at
`/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`, cited as
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped. Sim baseline: commit
`819d7c6` (build 0 errors, 367/367 tests, re-run for this note). Contracts and conventions:
[`sim/CONTRACTS.md`](../../sim/CONTRACTS.md); plan: [`docs/p0-sim-port-plan-2026-10-28.md`](../p0-sim-port-plan-2026-10-28.md).

Machine-readable companion: [`sim/specs/inventory/kernel-gaps.json`](../../sim/specs/inventory/kernel-gaps.json)
(every table below plus per-file override lists, per-file missing hooks/Cmds and feature flags for all
1,561 content files). The tables were produced by stdlib Python scripts over the decompile
(`tmp/sim-port/kernel/*.py`, git-ignored scratch); counts are per file, mocks excluded.

## 0. Summary

| Item | Game (v0.111.0) | Sim today | Gap |
| --- | --- | --- | --- |
| Content | 596 cards, 268 powers, 300 relics, 65 potions, 121 monsters, 91 encounters, 68 events, 23 enchantments, 7 afflictions, 5 orbs, 17 modifiers | 6 cards, 8 powers, 11 monsters, 8 encounters | content waves |
| `AbstractModel` public virtuals | 185 (182 hooks + `IsMock`, `PreviewOutsideOfCombat`, `CompareTo`) | 100 hooks | 34 combat-relevant (3 of them combat-end), 47 run layer, 1 UI-only |
| `Hook` public dispatchers | 148 | 77 names (94 public statics incl. overloads and 3 iteration helpers); listener source of all 77 matches the game (script check) | 71: 29 combat (2 of them combat-end), 42 run layer |
| Listener kinds | powers, monsters, relics, potions, orbs, cards in combat piles, afflictions, enchantments, modifiers, badges, scaling model | powers only | 8 rule-relevant kinds (badges and the scaling model are inert) |
| Cmd facade methods (20 classes + `AttackCommand`) | 199 overloads | 49 ported, 7 partial | 68 missing in combat scope (6 of them var overloads of ported methods), 21 run layer, 1 multiplayer-only, 3 test seams, 5 unused, 45 strip (VFX/SFX/waits) |
| In-combat player prompts | 61 sites (42 cards, 9 potions, 5 powers, 4 relics, 1 monster) through 6 `CardSelectCmd` entry points | none (`ActionKind` = PlayCard, EndTurn) | choice API |
| Hook parameters dropped by P0 but read by content | `participants` on 8 side-turn hooks (≈144 listener files), `AutoPlayType` (Enthralled) | dropped | restore in phase 0 |

Decisions proposed here:

1. **Choices: answer-tape re-execution (candidate a)**, built on an internal answerer seam (candidate b)
   used for fast rollouts, with exact emulation of the game's UI-path pause semantics (§4.6). Reject the
   explicit suspendable state machine (c).
2. **Listeners:** port the full game order (§2.1) with persistent per-instance listener objects; skip
   models whose type overrides no virtual (exact, because defaults are neutral) and filter per hook with
   override masks for speed.
3. **Restore `participants`** on the side-turn hooks and the `AutoPlayType` of `ShouldPlay` before
   content waves; keep `PlayerChoiceContext` out of signatures and model it as an ambient context
   stack on `CombatState` (§4.6).
4. **Ten agents**, disjoint file sets, one orchestrator-owned contract freeze first (§6).

## 1. Hook virtuals (`Models/AbstractModel.cs`)

`AbstractModel` declares 185 `public virtual` members (`Models/AbstractModel.cs:70-1100`). The sim
ports 100 of the 182 hooks with the game's names and defaults, `CombatState cs` added as first
parameter, `PlayerChoiceContext` dropped (`sim/Sts2Sim.Core/Combat/Pipeline/AbstractModel.cs`).

How to read the table: **Rel** = `combat` (dispatched with a combat listener source), `run` (only
`runState.IterateHookListeners(null)`), `infra`/`ui`/`none`. **Signature** is the game's parameter list
with `ctx` = `PlayerChoiceContext`. The kind columns count content files that override the virtual
(`Models.<Kind>/*.cs`, mocks excluded); **Other** lists Enchantments, Afflictions, Orbs, Modifiers,
Singleton and the non-rule Achievements/Badges. ⚠ marks parameters the sim drops today.

Observations:

- The 100 ported virtuals carry 672 content overrides; 11 ported virtuals have no override at all
  (`BeforeBlockGained`, `AfterDamageReceivedLate`, `BeforeFlushLate`, `BeforeHandDrawLate`,
  `AfterPlayerTurnStartEarly`, `ModifyAttackHitCount`, `ModifyHandDrawLate`, `ModifyHpLostBeforeOsty`,
  `ShouldAllowTargeting`, `ShouldEtherealTrigger`, `ShouldPayExcessEnergyCostWithStars`).
- Missing combat virtuals, by content overrides: `AfterCombatEnd` 45 (run effect at combat end),
  `AfterCardChangedPiles` 6, `AfterCombatVictory` 5, `ModifyCardPlayResultLocation` 4 and its
  `AfterModifying…` 4, `AfterAutoPostPlayPhaseEntered` 3, `AfterAutoPrePlayPhaseEntered` 3,
  `AfterStarsSpent` 3, `ModifyGoldGained`/`AfterModifyingGoldGained` 2, `AfterPotionUsed` 2,
  `ModifyOrbValue` 2, then 18 virtuals with one override and 5 with none (`BeforeCardAutoPlayed`,
  `AfterSummon`, `ModifySummonAmount`, `ShouldAfflict`, `ShouldGainStars`).
- Three virtuals are called outside a `Hook` dispatcher: `AfterModifyingCardPlayResultLocation` on the
  modifiers of `ModifyCardPlayResultLocation` (`Models/CardModel.cs:524`, `:936`),
  `AfterAddToDeckPrevented` on the preventer (`Commands/CardPileCmd.cs:406`, run layer),
  `AfterTargetingBlockedVfx` from the UI (`Nodes.Combat/NTargetManager.cs`).
- Dropped parameters that content reads: `participants` (the creatures starting or ending the turn,
  `Combat/CombatManager.cs:4914`, `:5080`, `:2457`, `:2967`, `:2055-2088`) is read by 26
  `BeforeSideTurnStart`, 47 `AfterSideTurnStart`, 12 `BeforeSideTurnEnd`, 52 `AfterSideTurnEnd`,
  3 + 3 + 1 Early/VeryEarly/Late listeners, typically `participants.Contains(Owner)`
  (`Models.Powers/PoisonPower.cs:57`, `Models.Relics/Kunai.cs:238`, `Models.Powers/ShrinkPower.cs:129`;
  the sim's `ShrinkPower.IsTurnParticipant` is a live reconstruction). `AutoPlayType` of `ShouldPlay` is
  read by `Models.Cards/Enthralled.cs:36`. `deathAnimLength` of `AfterDeath` only feeds VFX waits
  (`Models.Powers/StockPower.cs:76`, `InfestedPower.cs:93`).
- `ShouldReceiveCombatHooks` (`Models/AbstractModel.cs:74`) is abstract but never read by rules code;
  do not port it as a gate.

Per-kind base classes have their own virtual surfaces (full lists with override counts in the JSON,
`model_kind_virtuals`): `CardModel` 44 (`OnPlay` 561, `OnUpgrade` 538, `GainsBlock` 80,
`OnTurnEndInHand` 11, `AfterDowngraded` 9, `GetResultLocationForCardPlay` 3, …), `PowerModel` 21
(`InitInternalData` 34, `AfterApplied` 23, `InstanceType` 23, `AfterRemoved` 15, `DisplayAmount` 13,
`ShouldScaleInMultiplayer` 12, …), `RelicModel` 22 (`AfterObtained` 84, `IsUsedUp` 10, …), `PotionModel` 5
(`OnUse` 64), `OrbModel` 8 (`Passive`/`Evoke` 5, `BeforeTurnEndOrbTrigger` 4, `AfterTurnStartOrbTrigger` 1),
`EnchantmentModel` 19 (`OnPlay` 6, `EnchantDamageAdditive` 4, `EnchantPlayCount` 2, …),
`AfflictionModel` 9, `MonsterModel` 29 (`GenerateMoveStateMachine` 117, `AfterAddedToRoom` 61,
`BeforeRemovedFromRoom` 11, `ShouldDisappearFromDoom` 9, `OnDieToDoom` 1; the rest are VFX/SFX/bestiary).

| L | Virtual | Sim | Rel | Signature (game; sim adds `cs`, drops `ctx`) | Default | Cards | Powers | Relics | Potions | Monsters | Other | Σ |
|---:|---|:-:|:-:|---|---|--:|--:|--:|--:|--:|---|--:|
| 70 | `IsMock` | – | infra | `() → bool` | `false` |  |  |  |  |  |  | 0 |
| 72 | `PreviewOutsideOfCombat` | – | infra | `() → bool` | `false` |  |  |  |  |  |  | 0 |
| 126 | `CompareTo` | – | infra | `(AbstractModel?) → int` | `0` |  |  |  |  |  |  | 0 |
| 188 | `AfterActEntered` | – | run | `() → Task` | `CompletedTask` |  |  |  |  |  | Modif 1 | 1 |
| 193 | `AfterAddToDeckPrevented` | – | run | `(CardModel) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 198 | `BeforeAttack` | Y | combat | `(AttackCommand) → Task` | `CompletedTask` |  | 3 |  |  |  |  | 3 |
| 203 | `AfterAttack` | Y | combat | `(ctx, AttackCommand) → Task` | `CompletedTask` | 1 | 5 | 1 |  |  |  | 7 |
| 208 | `AfterAutoPostPlayPhaseEntered` | – | combat | `(ctx, Player) → Task` | `CompletedTask` | 2 | 1 |  |  |  |  | 3 |
| 213 | `AfterAutoPrePlayPhaseEnteredEarly` | – | combat | `(ctx, Player) → Task` | `CompletedTask` | 1 |  |  |  |  |  | 1 |
| 218 | `AfterAutoPrePlayPhaseEntered` | – | combat | `(ctx, Player) → Task` | `CompletedTask` |  | 1 | 1 |  |  | Encha 1 | 3 |
| 223 | `AfterAutoPrePlayPhaseEnteredLate` | – | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 228 | `AfterBlockCleared` | Y | combat | `(Creature) → Task` | `CompletedTask` |  | 3 | 3 |  |  |  | 6 |
| 233 | `BeforeBlockGained` | Y | combat | `(Creature, decimal, ValueProp, CardModel?) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 238 | `AfterBlockGained` | Y | combat | `(Creature, decimal, ValueProp, CardModel?) → Task` | `CompletedTask` |  | 2 |  |  |  |  | 2 |
| 243 | `AfterBlockBroken` | Y | combat | `(ctx, Creature, Creature?) → Task` | `CompletedTask` |  | 1 | 1 |  |  |  | 2 |
| 248 | `AfterCardChangedPiles` | – | combat | `(CardModel, PileType, AbstractModel?) → Task` | `CompletedTask` | 1 |  | 4 |  |  | Modif 1 | 6 |
| 253 | `AfterCardChangedPilesLate` | – | combat | `(CardModel, PileType, AbstractModel?) → Task` | `CompletedTask` |  |  |  |  | 1 |  | 1 |
| 258 | `AfterCardDiscarded` | Y | combat | `(ctx, CardModel) → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 263 | `AfterCardDrawnEarly` | Y | combat | `(ctx, CardModel, bool fromHandDraw) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 268 | `AfterCardDrawn` | Y | combat | `(ctx, CardModel, bool fromHandDraw) → Task` | `CompletedTask` | 3 | 8 |  |  |  | Encha 1 | 12 |
| 273 | `AfterCardEnteredCombat` | Y | combat | `(CardModel) → Task` | `CompletedTask` | 5 | 8 | 1 |  |  | Affli 1 | 15 |
| 278 | `AfterCardGeneratedForCombat` | Y | combat | `(CardModel, Player?) → Task` | `CompletedTask` | 1 | 5 | 1 |  | 1 |  | 8 |
| 283 | `AfterCardExhausted` | Y | combat | `(ctx, CardModel, bool causedByEthereal) → Task` | `CompletedTask` | 2 | 2 | 4 |  |  | non-rule: Achie 1 | 8 |
| 288 | `BeforeCardAutoPlayed` | – | combat | `(CardModel, Creature?, AutoPlayType) → Task` | `CompletedTask` |  |  |  |  |  | non-rule: Achie 1 | 0 |
| 293 | `BeforeCardPlayed` | Y | combat | `(CardPlay) → Task` | `CompletedTask` | 1 | 22 | 5 |  |  | non-rule: Achie 1 | 28 |
| 298 | `AfterCardPlayed` | Y | combat | `(ctx, CardPlay) → Task` | `CompletedTask` | 2 | 29 | 27 |  |  | Encha 3; non-rule: Achie 2, Badge 1 | 61 |
| 303 | `AfterCardPlayedLate` | Y | combat | `(ctx, CardPlay) → Task` | `CompletedTask` | 2 |  |  |  |  |  | 2 |
| 308 | `BeforeCombatStart` | Y | combat | `() → Task` | `CompletedTask` |  | 2 | 19 |  |  |  | 21 |
| 313 | `BeforeCombatStartLate` | Y | combat | `() → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 318 | `AfterCombatEnd` | – | combat | `(CombatRoom) → Task` | `CompletedTask` | 1 | 4 | 40 |  |  |  | 45 |
| 323 | `BeforeCombatRewardOffered` | – | run | `(RewardsSet, CombatRoom) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 328 | `AfterCombatVictoryEarly` | – | combat | `(CombatRoom) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 333 | `AfterCombatVictory` | – | combat | `(CombatRoom) → Task` | `CompletedTask` |  |  | 5 |  |  |  | 5 |
| 338 | `AfterCreatureAddedToCombat` | Y | combat | `(Creature) → Task` | `CompletedTask` |  | 1 | 2 |  |  | Modif 1 | 4 |
| 343 | `AfterCurrentHpChanged` | Y | combat | `(Creature, decimal) → Task` | `CompletedTask` |  | 1 | 2 |  | 2 |  | 5 |
| 348 | `AfterDamageGiven` | Y | combat | `(ctx, Creature?, DamageResult, ValueProp, Creature, CardModel?) → Task` | `CompletedTask` |  | 8 |  |  |  | non-rule: Achie 1 | 8 |
| 353 | `BeforeDamageReceived` | Y | combat | `(ctx, Creature, decimal, ValueProp, Creature?, CardModel?) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 358 | `AfterDamageReceived` | Y | combat | `(ctx, Creature, DamageResult, ValueProp, Creature?, CardModel?) → Task` | `CompletedTask` |  | 14 | 6 |  | 1 |  | 21 |
| 363 | `AfterDamageReceivedLate` | Y | combat | `(ctx, Creature, DamageResult, ValueProp, Creature?, CardModel?) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 368 | `BeforeDeath` | Y | combat | `(Creature) → Task` | `CompletedTask` |  | 2 |  |  | 2 |  | 4 |
| 373 | `AfterDeath` | Y | combat | `(ctx, Creature, bool wasRemovalPrevented, float deathAnimLength) → Task` ⚠ sim drops `deathAnimLength (VFX only)` | `CompletedTask` | 1 | 19 | 1 |  | 10 |  | 31 |
| 378 | `AfterDiedToDoom` | – | combat | `(ctx, IReadOnlyList<Creature>) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 383 | `AfterEnergyReset` | Y | combat | `(Player) → Task` | `CompletedTask` |  | 6 | 3 |  |  |  | 9 |
| 388 | `AfterEnergyResetLate` | Y | combat | `(Player) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 393 | `AfterEnergySpent` | Y | combat | `(CardModel, int) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 398 | `BeforeCardRemoved` | – | run | `(CardModel) → Task` | `CompletedTask` | 1 |  |  |  |  |  | 1 |
| 403 | `BeforeFlush` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  |  |  |  | Encha 1 | 1 |
| 408 | `BeforeFlushLate` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 413 | `AfterFlush` | Y | combat | `(ctx, Player, IReadOnlyCollection<CardModel>, IReadOnlyCollection<CardModel>) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 418 | `AfterGoldGained` | – | run | `(Player) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 423 | `BeforeHandDraw` | Y | combat | `(Player, ctx, ICombatState combatState) → Task` | `CompletedTask` | 2 | 8 | 8 |  |  |  | 18 |
| 428 | `BeforeHandDrawLate` | Y | combat | `(Player, ctx, ICombatState combatState) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 433 | `AfterHandEmptied` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 438 | `AfterItemPurchased` | – | run | `(Player, MerchantEntry, int) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 443 | `AfterMapGenerated` | – | run | `(ActMap, int) → Task` | `CompletedTask` | 1 |  |  |  |  |  | 1 |
| 448 | `AfterModifyingBlockAmount` | Y | combat | `(decimal, CardModel?, CardPlay?) → Task` | `CompletedTask` |  | 1 | 2 |  |  |  | 3 |
| 453 | `AfterModifyingCardPlayCount` | Y | combat | `(CardModel) → Task` | `CompletedTask` |  | 6 | 1 |  |  |  | 7 |
| 458 | `AfterModifyingCardPlayResultLocation` | – | combat | `(CardModel, CardLocation) → Task` | `CompletedTask` |  | 4 |  |  |  |  | 4 |
| 463 | `AfterModifyingOrbPassiveTriggerCount` | – | combat | `(OrbModel) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 468 | `AfterModifyingCardRewardOptions` | – | run | `() → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 473 | `AfterModifyingDamageAmount` | Y | combat | `(CardModel?) → Task` | `CompletedTask` |  | 3 |  |  |  |  | 3 |
| 478 | `AfterModifyingEnergyGain` | Y | combat | `() → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 483 | `AfterModifyingGoldGained` | – | combat | `(Player, decimal) → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 488 | `AfterModifyingHandDraw` | Y | combat | `() → Task` | `CompletedTask` |  | 1 | 2 |  |  |  | 3 |
| 493 | `AfterPreventingDraw` | Y | combat | `() → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 498 | `AfterModifyingHpLostBeforeOsty` | Y | combat | `() → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 503 | `AfterModifyingHpLostAfterOsty` | Y | combat | `() → Task` | `CompletedTask` |  | 2 | 3 |  |  |  | 5 |
| 508 | `AfterModifyingPowerAmountReceived` | Y | combat | `(PowerModel) → Task` | `CompletedTask` |  | 1 | 1 |  |  |  | 2 |
| 513 | `AfterModifyingPowerAmountGiven` | Y | combat | `(PowerModel) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 518 | `AfterModifyingRewards` | – | run | `() → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 523 | `AfterOrbChanneled` | – | combat | `(ctx, Player, OrbModel) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 528 | `AfterOrbEvoked` | – | combat | `(ctx, OrbModel, IEnumerable<Creature>) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 533 | `AfterOstyRevived` | – | combat | `(Creature) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 538 | `BeforePotionUsed` | – | combat | `(PotionModel, Creature?) → Task` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 543 | `AfterPotionUsed` | – | combat | `(PotionModel, Creature?) → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 548 | `AfterPotionDiscarded` | – | combat | `(PotionModel) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 553 | `AfterPotionProcured` | – | combat | `(PotionModel) → Task` | `CompletedTask` |  |  | 1 |  |  |  | 1 |
| 558 | `BeforePowerAmountChanged` | Y | combat | `(PowerModel, decimal, Creature, Creature?, CardModel?) → Task` | `CompletedTask` |  | 1 | 1 |  |  |  | 2 |
| 563 | `AfterPowerAmountChanged` | Y | combat | `(ctx, PowerModel, decimal, Creature?, CardModel?) → Task` | `CompletedTask` |  | 11 |  |  |  | non-rule: Achie 3, Badge 1 | 11 |
| 568 | `AfterPreventingBlockClear` | Y | combat | `(AbstractModel, Creature) → Task` | `CompletedTask` |  | 1 | 1 |  |  |  | 2 |
| 573 | `AfterPreventingDeath` | Y | combat | `(Creature) → Task` | `CompletedTask` |  |  | 1 | 1 |  |  | 2 |
| 578 | `AfterRestSiteHeal` | – | run | `(Player, bool isMimicked) → Task` | `CompletedTask` |  |  | 2 |  |  | Modif 1 | 3 |
| 583 | `AfterRestSiteSmith` | – | run | `(Player) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 588 | `AfterRewardTaken` | – | run | `(Player, Reward) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 593 | `BeforeRoomEntered` | – | run | `(AbstractRoom) → Task` | `CompletedTask` | 1 |  |  |  |  |  | 1 |
| 598 | `AfterRoomEntered` | – | run | `(AbstractRoom) → Task` | `CompletedTask` |  |  | 33 |  |  | Modif 2; non-rule: Achie 2 | 35 |
| 603 | `AfterShuffle` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  | 1 | 2 |  |  |  | 3 |
| 608 | `AfterStarsSpent` | – | combat | `(int, Player) → Task` | `CompletedTask` |  | 1 | 2 |  |  |  | 3 |
| 613 | `AfterStarsGained` | – | combat | `(int, Player) → Task` | `CompletedTask` |  | 1 |  |  |  | non-rule: Achie 1 | 1 |
| 618 | `AfterForge` | – | combat | `(decimal, Player, AbstractModel?) → Task` | `CompletedTask` |  | 1 |  |  |  | non-rule: Achie 1 | 1 |
| 623 | `AfterSummon` | – | combat | `(ctx, Player, decimal) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 628 | `AfterTakingExtraTurn` | Y | combat | `(Player) → Task` | `CompletedTask` |  | 1 | 1 |  |  |  | 2 |
| 633 | `AfterTargetingBlockedVfx` | – | ui | `(Creature) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 638 | `BeforeSideTurnStart` | Y | combat | `(ctx, CombatSide, IReadOnlyList<Creature> participants, ICombatState combatState) → Task` ⚠ sim drops `participants` (26 readers) | `CompletedTask` |  | 6 | 21 |  |  |  | 27 |
| 643 | `AfterSideTurnStart` | Y | combat | `(CombatSide, IReadOnlyList<Creature> participants, ICombatState combatState) → Task` ⚠ sim drops `participants` (47 readers) | `CompletedTask` |  | 19 | 29 |  |  | non-rule: Achie 1, Badge 1 | 48 |
| 648 | `AfterSideTurnStartLate` | Y | combat | `(CombatSide, IReadOnlyList<Creature> participants, ICombatState combatState) → Task` ⚠ sim drops `participants` | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 653 | `AfterPlayerTurnStartEarly` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  |  |  |  |  | 0 |
| 658 | `AfterPlayerTurnStart` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  | 9 | 11 |  |  |  | 20 |
| 663 | `AfterPlayerTurnStartLate` | Y | combat | `(ctx, Player) → Task` | `CompletedTask` |  |  | 2 |  |  |  | 2 |
| 668 | `BeforeSideTurnEndVeryEarly` | Y | combat | `(ctx, CombatSide, IEnumerable<Creature> participants) → Task` ⚠ sim drops `participants` (3 readers) | `CompletedTask` |  | 1 | 2 |  |  |  | 3 |
| 673 | `BeforeSideTurnEndEarly` | Y | combat | `(ctx, CombatSide, IEnumerable<Creature> participants) → Task` ⚠ sim drops `participants` (3 readers) | `CompletedTask` |  | 2 | 1 |  |  |  | 3 |
| 678 | `BeforeSideTurnEnd` | Y | combat | `(ctx, CombatSide, IEnumerable<Creature> participants) → Task` ⚠ sim drops `participants` (12 readers) | `CompletedTask` | 1 | 5 | 7 |  |  |  | 13 |
| 683 | `AfterSideTurnEnd` | Y | combat | `(ctx, CombatSide, IEnumerable<Creature> participants) → Task` ⚠ sim drops `participants` (52 readers) | `CompletedTask` |  | 61 | 5 |  |  |  | 66 |
| 688 | `AfterSideTurnEndLate` | Y | combat | `(ctx, CombatSide, IEnumerable<Creature> participants) → Task` ⚠ sim drops `participants` (1 readers) | `CompletedTask` |  | 1 |  |  |  |  | 1 |
| 693 | `ModifyAttackHitCount` | Y | combat | `(AttackCommand, int) → int` | `hitCount` |  |  |  |  |  |  | 0 |
| 698 | `ModifyBlockAdditive` | Y | combat | `(Creature, decimal, ValueProp, CardModel?, CardPlay?) → decimal` | `0m` |  | 2 |  |  |  |  | 2 |
| 703 | `ModifyBlockMultiplicative` | Y | combat | `(Creature, decimal, ValueProp, CardModel?, CardPlay?) → decimal` | `1m` |  | 4 | 3 |  |  | Singl 1 | 8 |
| 708 | `ModifyCardPlayCount` | Y | combat | `(CardModel, Creature?, int) → int` | `playCount` |  | 6 | 1 |  |  |  | 7 |
| 713 | `ModifyCardPlayResultLocation` | – | combat | `(CardModel, bool isAutoPlay, ResourceInfo, CardLocation) → CardLocation` | `cardLocation` |  | 4 |  |  |  |  | 4 |
| 718 | `ModifyOrbPassiveTriggerCounts` | – | combat | `(OrbModel, int) → int` | `triggerCount` |  |  | 1 |  |  |  | 1 |
| 723 | `ModifyCardRewardCreationOptions` | – | run | `(Player, CardCreationOptions) → CardCreationOptions` | `options` |  |  | 2 |  |  | Modif 2 | 4 |
| 728 | `ModifyCardRewardCreationOptionsLate` | – | run | `(Player, CardCreationOptions) → CardCreationOptions` | `options` |  |  |  |  |  |  | 0 |
| 733 | `ModifyCardRewardUpgradeOdds` | – | run | `(Player, CardModel, decimal) → decimal` | `odds` |  |  |  |  |  |  | 0 |
| 738 | `ModifyDamageAdditive` | Y | combat | `(Creature?, decimal, ValueProp, Creature?, CardModel?, CardPlay?) → decimal` | `0m` |  | 8 | 4 |  |  |  | 12 |
| 743 | `ModifyDamageCap` | Y | combat | `(Creature?, ValueProp, Creature?, CardModel?, CardPlay?) → decimal` | `decimal.MaxValue` |  | 2 |  |  |  |  | 2 |
| 748 | `ModifyDamageMultiplicative` | Y | combat | `(Creature?, decimal, ValueProp, Creature?, CardModel?, CardPlay?) → decimal` | `1m` |  | 20 | 3 |  |  |  | 23 |
| 753 | `ModifyEnergyGain` | Y | combat | `(Player, decimal) → decimal` | `amount` |  | 1 |  |  |  |  | 1 |
| 758 | `ModifyGoldGained` | – | combat | `(Player, decimal) → decimal` | `amount` |  |  | 2 |  |  |  | 2 |
| 763 | `ModifyGeneratedMap` | – | run | `(IRunState, ActMap, int) → ActMap` | `map` | 1 |  | 1 |  |  | Modif 1 | 3 |
| 768 | `ModifyGeneratedMapLate` | – | run | `(IRunState, ActMap, int) → ActMap` | `map` | 1 |  | 1 |  |  |  | 2 |
| 773 | `ModifyHandDraw` | Y | combat | `(Player, decimal) → decimal` | `count` |  | 7 | 11 |  |  |  | 18 |
| 778 | `ModifyHandDrawLate` | Y | combat | `(Player, decimal) → decimal` | `count` |  |  |  |  |  |  | 0 |
| 783 | `ModifyHpLostBeforeOsty` | Y | combat | `(Creature, decimal, ValueProp, Creature?, CardModel?) → decimal` | `amount` |  |  |  |  |  |  | 0 |
| 788 | `ModifyHpLostBeforeOstyLate` | Y | combat | `(Creature, decimal, ValueProp, Creature?, CardModel?) → decimal` | `amount` |  | 1 |  |  |  |  | 1 |
| 793 | `ModifyHpLostAfterOsty` | Y | combat | `(Creature, decimal, ValueProp, Creature?, CardModel?) → decimal` | `amount` |  | 2 | 2 |  |  |  | 4 |
| 798 | `ModifyHpLostAfterOstyLate` | Y | combat | `(Creature, decimal, ValueProp, Creature?, CardModel?) → decimal` | `amount` |  | 1 | 1 |  |  |  | 2 |
| 803 | `ModifyMaxEnergy` | Y | combat | `(Player, decimal) → decimal` | `amount` |  | 4 | 12 |  |  |  | 16 |
| 808 | `ModifyMerchantCardPool` | – | run | `(Player, IEnumerable<CardModel>) → IEnumerable<CardModel>` | `options` |  |  |  |  |  | Modif 1 | 1 |
| 813 | `ModifyMerchantCardRarity` | – | run | `(Player, CardRarity) → CardRarity` | `rarity` |  |  |  |  |  |  | 0 |
| 818 | `ModifyMerchantCardCreationResults` | – | run | `(Player, List<CardCreationResult>) → void` | `` |  |  | 4 |  |  |  | 4 |
| 822 | `ModifyMerchantPrice` | – | run | `(Player, MerchantEntry, decimal) → decimal` | `cost` |  |  | 2 |  |  |  | 2 |
| 827 | `ModifyOrbValue` | – | combat | `(OrbModel, decimal) → decimal` | `value` |  | 1 | 1 |  |  |  | 2 |
| 832 | `ModifyPowerAmountGivenAdditive` | Y | combat | `(PowerModel, Creature, decimal, Creature?, CardModel?) → decimal` | `0m` |  |  | 1 |  |  |  | 1 |
| 837 | `ModifyPowerAmountGivenMultiplicative` | Y | combat | `(PowerModel, Creature, decimal, Creature?, CardModel?) → decimal` | `1m` |  |  | 1 |  |  |  | 1 |
| 842 | `ModifyRestSiteHealAmount` | – | run | `(Creature, decimal) → decimal` | `amount` |  |  | 1 |  |  | Modif 1 | 2 |
| 847 | `ModifyShuffleOrder` | Y | combat | `(Player, List<CardModel>, bool isInitialShuffle) → void` | `` |  |  |  |  |  | Encha 1 | 1 |
| 851 | `ModifySummonAmount` | – | combat | `(Player, decimal, AbstractModel?) → decimal` | `amount` |  |  |  |  |  |  | 0 |
| 856 | `ModifyUnblockedDamageTarget` | Y | combat | `(Creature, decimal, ValueProp, Creature?) → Creature` | `target` |  | 1 |  |  |  |  | 1 |
| 861 | `ModifyNextEvent` | – | run | `(EventModel) → EventModel` | `currentEvent` | 1 |  |  |  |  |  | 1 |
| 866 | `ModifyUnknownMapPointRoomTypes` | – | run | `(IReadOnlySet<RoomType>) → IReadOnlySet<RoomType>` | `roomTypes` | 1 |  | 2 |  |  |  | 3 |
| 871 | `ModifyOddsIncreaseForUnrolledRoomType` | – | run | `(RoomType, float) → float` | `oddsIncrease` |  |  |  |  |  | Modif 1 | 1 |
| 876 | `ModifyXValue` | Y | combat | `(CardModel, int) → int` | `originalValue` |  |  | 1 |  |  |  | 1 |
| 881 | `TryModifyCardBeingAddedToDeck` | – | run | `(CardModel, out CardModel?) → bool` | `false` |  |  | 4 |  |  |  | 4 |
| 887 | `TryModifyCardBeingAddedToDeckLate` | – | run | `(CardModel, out CardModel?) → bool` | `false` |  |  |  |  |  |  | 0 |
| 893 | `TryModifyCardRewardAlternatives` | – | run | `(Player, CardReward, List<CardRewardAlternative>) → bool` | `false` |  |  | 1 |  |  |  | 1 |
| 898 | `TryModifyCardRewardOptions` | – | run | `(Player, List<CardCreationResult>, CardCreationOptions) → bool` | `false` |  |  | 1 |  |  |  | 1 |
| 903 | `TryModifyCardRewardOptionsLate` | – | run | `(Player, List<CardCreationResult>, CardCreationOptions) → bool` | `false` |  |  | 9 |  |  |  | 9 |
| 908 | `TryModifyEnergyCostInCombat` | Y | combat | `(CardModel, decimal, out decimal) → bool` | `false` |  | 3 | 1 |  |  |  | 4 |
| 914 | `TryModifyEnergyCostInCombatLate` | Y | combat | `(CardModel, decimal, out decimal) → bool` | `false` |  | 6 | 1 |  |  |  | 7 |
| 920 | `TryModifyKeywordsInCombat` | – | combat | `(CardModel, ISet<CardKeyword>) → bool` | `false` |  | 1 |  |  |  |  | 1 |
| 925 | `TryModifyStarCost` | Y | combat | `(CardModel, decimal, out decimal) → bool` | `false` |  | 1 | 1 |  |  |  | 2 |
| 931 | `TryModifyPowerAmountReceived` | Y | combat | `(PowerModel, Creature, decimal, Creature?, out decimal) → bool` | `false` |  | 1 | 1 |  |  |  | 2 |
| 937 | `TryModifyRestSiteOptions` | – | run | `(Player, ICollection<RestSiteOption>) → bool` | `false` | 1 |  | 5 |  |  | Modif 1 | 7 |
| 942 | `TryModifyRestSiteHealRewards` | – | run | `(Player, List<Reward>, bool isMimicked) → bool` | `false` |  |  | 2 |  |  |  | 2 |
| 947 | `TryModifyRewards` | – | run | `(Player, List<Reward>, AbstractRoom?) → bool` | `false` |  |  | 6 |  |  |  | 6 |
| 952 | `TryModifyRewardsLate` | – | run | `(Player, List<Reward>, AbstractRoom?) → bool` | `false` |  |  | 1 |  |  | Modif 2 | 3 |
| 957 | `ModifyExtraRestSiteHealText` | – | run | `(Player, IReadOnlyList<LocString>) → IReadOnlyList<LocString>` | `currentExtraText` |  |  | 4 |  |  | Modif 1 | 5 |
| 962 | `ShouldAddToDeck` | – | run | `(CardModel) → bool` | `true` |  |  |  |  |  |  | 0 |
| 967 | `ShouldAfflict` | – | combat | `(CardModel, AfflictionModel) → bool` | `true` |  |  |  |  |  |  | 0 |
| 972 | `ShouldAllowAncient` | – | run | `(Player, AncientEventModel) → bool` | `true` |  |  |  |  |  |  | 0 |
| 977 | `ShouldAllowHitting` | Y | combat | `(Creature) → bool` | `true` |  | 4 |  |  |  |  | 4 |
| 982 | `ShouldAllowTargeting` | Y | combat | `(Creature) → bool` | `true` |  |  |  |  |  |  | 0 |
| 987 | `ShouldAllowSelectingMoreCardRewards` | – | run | `(Player, CardReward) → bool` | `false` |  |  |  |  |  |  | 0 |
| 992 | `ShouldClearBlock` | Y | combat | `(Creature) → bool` | `true` |  | 3 | 1 |  |  |  | 4 |
| 997 | `ShouldDie` | Y | combat | `(Creature) → bool` | `true` |  |  |  | 1 |  |  | 1 |
| 1002 | `ShouldDieLate` | Y | combat | `(Creature) → bool` | `true` |  |  | 1 |  |  |  | 1 |
| 1007 | `ShouldDisableRemainingRestSiteOptions` | – | run | `(Player) → bool` | `true` |  |  | 1 |  |  |  | 1 |
| 1012 | `ShouldDraw` | Y | combat | `(Player, bool fromHandDraw) → bool` | `true` |  | 1 | 1 |  |  |  | 2 |
| 1017 | `ShouldEtherealTrigger` | Y | combat | `(CardModel) → bool` | `true` |  |  |  |  |  |  | 0 |
| 1022 | `ShouldFlush` | Y | combat | `(Player) → bool` | `true` |  | 2 | 2 |  |  |  | 4 |
| 1027 | `ShouldGainStars` | – | combat | `(decimal, Player) → bool` | `true` |  |  |  |  |  |  | 0 |
| 1032 | `ShouldGenerateTreasure` | – | run | `(Player) → bool` | `true` |  |  | 1 |  |  |  | 1 |
| 1037 | `ShouldPayExcessEnergyCostWithStars` | Y | combat | `(Player) → bool` | `false` |  |  |  |  |  |  | 0 |
| 1042 | `ShouldPlay` | Y | combat | `(CardModel, AutoPlayType autoPlayType) → bool` ⚠ sim drops `autoPlayType (read by Cards/Enthralled)` | `true` | 2 | 4 | 1 |  |  |  | 7 |
| 1047 | `ShouldPlayerResetEnergy` | Y | combat | `(Player) → bool` | `true` |  |  | 1 |  |  |  | 1 |
| 1052 | `ShouldProceedToNextMapPoint` | – | run | `() → bool` | `true` |  |  |  |  |  |  | 0 |
| 1057 | `ShouldProcurePotion` | – | combat | `(PotionModel, Player) → bool` | `true` |  |  | 1 |  |  |  | 1 |
| 1062 | `ShouldPowerBeRemovedOnDeath` | Y | combat | `(PowerModel) → bool` | `true` |  | 1 |  |  |  |  | 1 |
| 1067 | `ShouldRefillMerchantEntry` | – | run | `(MerchantEntry, Player) → bool` | `false` |  |  | 1 |  |  |  | 1 |
| 1072 | `ShouldAllowMerchantCardRemoval` | – | run | `(Player) → bool` | `true` |  |  |  |  |  | Modif 1 | 1 |
| 1077 | `ShouldCreatureBeRemovedFromCombatAfterDeath` | Y | combat | `(Creature) → bool` | `true` |  | 6 |  |  |  |  | 6 |
| 1082 | `ShouldStopCombatFromEnding` | Y | combat | `() → bool` | `false` |  | 5 |  |  |  |  | 5 |
| 1087 | `ShouldTakeExtraTurn` | Y | combat | `(Player) → bool` | `false` |  | 1 | 1 |  |  |  | 2 |
| 1092 | `ShouldForcePotionReward` | – | run | `(Player, RoomType) → bool` | `false` |  |  | 1 |  |  |  | 1 |
| 1097 | `ShouldAllowFreeTravel` | – | run | `() → bool` | `false` |  |  | 1 |  |  | Modif 1 | 2 |

## 2. Hook dispatchers (`Hooks/Hook.cs`)

### 2.1 Listener sources and the full iteration order

Every dispatcher iterates one of three enumerations; each is a snapshot built at the iterator's first
`MoveNext`, with a liveness filter evaluated lazily per element at yield time.

| Sim source | Game | Rule |
| --- | --- | --- |
| G (`Guarded`) | `Hook.IterateCombatHookListeners` (`Hooks/Hook.cs:8792-8810`) | yields nothing when `CombatManager.IsOverOrEnding && !IsStarting` (`:8794`), else the combat list |
| C (`Combat`) | `CombatState.IterateHookListeners()` (`Combat/CombatState.cs:855-956`) | the combat list, unguarded |
| R+C / R (`Run`) | `RunState.IterateHookListeners(combatState)` (`Runs/RunState.cs:829-933`) | deck cards (+enchantment) of active players (`:843-855`); only when no combat is given: relics, potions, modifiers, badges, scaling model (`:866-880`); `RunState.Contains` at yield (`:895`, `:1037-1073`); run mod subscribers (`:905`); then the combat list (`:917-921`), built only after the deck listeners ran |

Combat list order (`Combat/CombatState.cs:858-955`):

1. For each creature of `_allies`, then `_enemies` (`:858-860`):
   1. its `Powers` in list order (`:862`);
   2. a monster creature (enemies and pets on the player side, e.g. Osty) adds its `MonsterModel` (`:864-867`);
   3. a player adds nothing more when `!IsActiveForHooks` (`:870-873`), else: relics in list order
      without melted ones (`:874-881`), potion slots in slot order without empty ones (`:882-889`);
      nothing more when `PlayerCombatState == null` (`:890-893`); orbs in queue order (`:894`); for each
      pile of `AllPiles` = Hand, Draw, Discard, Exhaust, Play (`Entities.Players/PlayerCombatState.cs`
      `AllPiles`), each card, then its affliction, then its enchantment (`:895-914`).
2. Run modifiers (`:916-919`), badges (`:920-923`), the multiplayer scaling model (`:924-927`).
3. Filter `Contains` at yield (`:928-940`, `:1010-1062`): power — `Owner.CombatState != null &&
   (Owner.Player?.IsActiveForHooks ?? true)`; relic, potion, card, orb — `!HasBeenRemovedFromState &&
   Owner.IsActiveForHooks`; affliction, enchantment — `HasCard && !Card.HasBeenRemovedFromState &&
   Card.Owner.IsActiveForHooks`; monster — `Creature.CombatState != null`; others — true.
4. Mod subscribers (`:944-955`; none in vanilla).

Inert in single-player combat (verified, so the sim may omit them without changing results):

- Deck-card listeners of the run source: among cards, only `Melancholy.AfterDeath` overrides a
  Run+Combat hook and it returns unless `Pile.IsCombatPile` (`Models.Cards/Melancholy.cs:114-123`); the
  other deck-card overrides are run-only hooks; deck enchantments override no Run-dispatched hook.
- Badges and achievements: no Cmd call in `Models.Badges`/`Models.Achievements` (stats only).
- `MultiplayerScalingModel.ModifyBlockMultiplicative` returns the player count, 1
  (`Models.Singleton/MultiplayerScalingModel.cs`); a multiplier of 1 is not a modifier.

Content that becomes listeners (files overriding at least one combat hook): powers 237, relics 177,
cards 23, monsters 12, enchantments 7, modifiers 2, potions 1 (`FairyInABottle`), afflictions 1
(`Hexed`). A listener that overrides no virtual can be skipped exactly: every default is neutral
(identity value, no-op, `true`/`false` gate default, `decimal.MaxValue` cap) and modifier lists only
collect models that changed a value.

### 2.2 Dispatch patterns that matter for the port

- **Two-pass and three-pass dispatchers** (`X` then `XLate`, `Early/normal/Late`, `VeryEarly/Early/normal`)
  take a fresh snapshot per pass, so a listener added in the first pass runs in the next.
- **Modifier/preventer filtering:** `AfterModifying*` and `AfterPreventing*` iterate a fresh snapshot
  and call only models contained in the `modifiers` list or equal to the preventer (e.g.
  `Hooks/Hook.cs:3362`). The sim's `ref List<AbstractModel>? modifiers` / `out AbstractModel? preventer`
  overloads cover `ModifyDamage`, `ModifyBlock`, `ModifyHpLost`, `ModifyHandDraw`,
  `ModifyCardPlayCount`, `ModifyEnergyGain`, `ModifyPowerAmountGiven`, `ModifyPowerAmountReceived`,
  `ShouldAllowTargeting`, `ShouldClearBlock`, `ShouldDie`, `ShouldPlay`, `ShouldDraw`. Missing, combat:
  `ModifyCardPlayResultLocation` (out modifiers → `AfterModifyingCardPlayResultLocation` called directly,
  `Models/CardModel.cs:524`), `ModifyOrbPassiveTriggerCount` (out list → `AfterModifyingOrbPassiveTriggerCount`),
  `ModifyGoldGained` (out → `AfterModifyingGoldGained`). Run layer: `ModifyCardBeingAddedToDeck`,
  `TryModifyCardRewardOptions`, `ShouldAddToDeck`, `ModifyRewards`, `ModifyRestSiteOptions`,
  `ModifyRestSiteHealRewards`, `ModifyCardRewardAlternatives` (return the modifier list).
- **Enchantment pre-pass:** `ModifyDamage` and `ModifyBlock` apply the card source's enchantment
  additive then multiplicative term before any listener (`Hooks/Hook.cs:10075-10080`, `:10332-10342`);
  `ModifyDamage` also has a `CardPreviewMode.MultiCreatureTargeting` UI path (`:10343-10410`, not ported).
- **Choice contexts** (column *Choice ctx*): most dispatchers forward the caller's context and push/pop
  the listener as `LastInvolvedModel` (the recorder's sticky `context_model`). Six dispatchers create a
  `HookPlayerChoiceContext` per listener and `Task.WhenAll` the paused ones at the end of the pass:
  `AfterDeath` (`:2364`), `AfterDiedToDoom` (`:2467`), `AfterSideTurnEnd` (`:6170`, `:6225`), `BeforeFlush`
  (`:7776`, `:7819`), `BeforeSideTurnEnd` (`:8398`, `:8441`, `:8484`), `BeforeSideTurnStart` (`:8607`); a
  paused listener's remainder would run after the rest of its pass (§4.6).
- `LocalContext.NetId.HasValue` gates are always true in single player.

### 2.3 Dispatcher table

**Sim** = a sim dispatcher with the same name exists. **Listener source**: G / C / R+C / R as in §2.1,
one entry per pass. **Calls** = virtuals in pass order.

| L | Dispatcher | Sim | Kind | Listener source (passes) | Calls (pass order) | Choice ctx | out/ref | Rel |
|---:|---|:-:|:-:|---|---|---|---|:-:|
| 8813 | `AfterActEntered` | – | async | R | AfterActEntered | — |  | run |
| 8826 | `BeforeAttack` | Y | async | G | BeforeAttack | — |  | combat |
| 8840 | `AfterAttack` | Y | async | G | AfterAttack | caller ctx |  | combat |
| 8855 | `AfterBlockBroken` | Y | async | C | AfterBlockBroken | caller ctx |  | combat |
| 8871 | `AfterBlockCleared` | Y | async | G | AfterBlockCleared | — |  | combat |
| 8885 | `BeforeBlockGained` | Y | async | G | BeforeBlockGained | — |  | combat |
| 8902 | `AfterBlockGained` | Y | async | G | AfterBlockGained | — |  | combat |
| 8919 | `BeforeCardAutoPlayed` | – | async | G | BeforeCardAutoPlayed | — |  | combat |
| 8935 | `AfterCardChangedPiles` | – | async | R+C → R+C | AfterCardChangedPiles,AfterCardChangedPilesLate | — |  | combat |
| 8952 | `AfterCardDiscarded` | Y | async | G | AfterCardDiscarded | caller ctx (Push/PopModel) |  | combat |
| 8967 | `AfterCardDrawn` | Y | async | G → G | AfterCardDrawnEarly,AfterCardDrawn | caller ctx (Push/PopModel) |  | combat |
| 8983 | `AfterCardEnteredCombat` | Y | async | G | AfterCardEnteredCombat | — |  | combat |
| 8997 | `AfterCardExhausted` | Y | async | G | AfterCardExhausted | caller ctx (Push/PopModel) |  | combat |
| 9013 | `AfterCardGeneratedForCombat` | Y | async | G | AfterCardGeneratedForCombat | — |  | combat |
| 9028 | `BeforeCardPlayed` | Y | async | G | BeforeCardPlayed | — |  | combat |
| 9042 | `AfterCardPlayed` | Y | async | C → C | AfterCardPlayed,AfterCardPlayedLate | caller ctx (Push/PopModel) |  | combat |
| 9057 | `BeforeCardRemoved` | – | async | R | BeforeCardRemoved | — |  | run |
| 9071 | `BeforeCombatStart` | Y | async | R+C → R+C | BeforeCombatStart,BeforeCombatStartLate | — |  | combat |
| 9085 | `AfterCombatEnd` | – | async | R+C | AfterCombatEnd | — |  | combat |
| 9100 | `BeforeCombatRewardOffered` | – | async | R | BeforeCombatRewardOffered | — |  | run |
| 9115 | `AfterCombatVictory` | – | async | R+C → R+C | AfterCombatVictoryEarly,AfterCombatVictory | — |  | combat |
| 9130 | `AfterCreatureAddedToCombat` | Y | async | C | AfterCreatureAddedToCombat | — |  | combat |
| 9144 | `AfterCurrentHpChanged` | Y | async | R+C | AfterCurrentHpChanged | — |  | combat |
| 9160 | `AfterDamageGiven` | Y | async | C | AfterDamageGiven | caller ctx (Push/PopModel) |  | combat |
| 9179 | `BeforeDamageReceived` | Y | async | R+C | BeforeDamageReceived | caller ctx (Push/PopModel) |  | combat |
| 9199 | `AfterDamageReceived` | Y | async | R+C → R+C | AfterDamageReceived,AfterDamageReceivedLate | caller ctx (Push/PopModel) |  | combat |
| 9219 | `BeforeDeath` | Y | async | R+C | BeforeDeath | — |  | combat |
| 9234 | `AfterDeath` | Y | async | R+C | AfterDeath · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 9251 | `AfterGoldGained` | – | async | R | AfterGoldGained | — |  | run |
| 9265 | `AfterDiedToDoom` | – | async | C | AfterDiedToDoom · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 9279 | `AfterEnergyReset` | Y | async | G → G | AfterEnergyReset,AfterEnergyResetLate | — |  | combat |
| 9293 | `AfterEnergySpent` | Y | async | G | AfterEnergySpent | — |  | combat |
| 9308 | `BeforeFlush` | Y | async | G → G | BeforeFlush,BeforeFlushLate · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 9322 | `AfterFlush` | Y | async | G | AfterFlush | caller ctx (Push/PopModel) |  | combat |
| 9339 | `AfterForge` | – | async | G | AfterForge | — |  | combat |
| 9355 | `BeforeHandDraw` | Y | async | G → G | BeforeHandDraw,BeforeHandDrawLate | caller ctx (Push/PopModel) |  | combat |
| 9370 | `AfterHandEmptied` | Y | async | G | AfterHandEmptied | caller ctx (Push/PopModel) |  | combat |
| 9385 | `AfterItemPurchased` | – | async | R | AfterItemPurchased | — |  | run |
| 9401 | `AfterMapGenerated` | – | async | R | AfterMapGenerated | — |  | run |
| 9416 | `AfterModifyingBlockAmount` | Y | async | G | AfterModifyingBlockAmount · modifier/preventer-filtered | — |  | combat |
| 9433 | `AfterModifyingCardPlayCount` | Y | async | G | AfterModifyingCardPlayCount · modifier/preventer-filtered | — |  | combat |
| 9448 | `AfterModifyingCardRewardOptions` | – | async | R | AfterModifyingCardRewardOptions · modifier/preventer-filtered | — |  | run |
| 9462 | `AfterModifyingDamageAmount` | Y | async | R+C | AfterModifyingDamageAmount · modifier/preventer-filtered | — |  | combat |
| 9478 | `AfterModifyingEnergyGain` | Y | async | G | AfterModifyingEnergyGain · modifier/preventer-filtered | — |  | combat |
| 9492 | `AfterModifyingGoldGained` | – | async | R+C | AfterModifyingGoldGained · modifier/preventer-filtered | — |  | combat |
| 9509 | `AfterModifyingHandDraw` | Y | async | G | AfterModifyingHandDraw · modifier/preventer-filtered | — |  | combat |
| 9523 | `AfterModifyingHpLostBeforeOsty` | Y | async | R+C | AfterModifyingHpLostBeforeOsty · modifier/preventer-filtered | — |  | combat |
| 9538 | `AfterModifyingHpLostAfterOsty` | Y | async | R+C | AfterModifyingHpLostAfterOsty · modifier/preventer-filtered | — |  | combat |
| 9553 | `AfterModifyingOrbPassiveTriggerCount` | – | async | G | AfterModifyingOrbPassiveTriggerCount · modifier/preventer-filtered | — |  | combat |
| 9568 | `AfterModifyingPowerAmountGiven` | Y | async | G | AfterModifyingPowerAmountGiven · modifier/preventer-filtered | — |  | combat |
| 9583 | `AfterModifyingPowerAmountReceived` | Y | async | G | AfterModifyingPowerAmountReceived · modifier/preventer-filtered | — |  | combat |
| 9598 | `AfterModifyingRewards` | – | async | R | AfterModifyingRewards · modifier/preventer-filtered | — |  | run |
| 9612 | `AfterOrbChanneled` | – | async | G | AfterOrbChanneled | caller ctx (Push/PopModel) |  | combat |
| 9628 | `AfterOrbEvoked` | – | async | G | AfterOrbEvoked | caller ctx |  | combat |
| 9644 | `AfterOstyRevived` | – | async | G | AfterOstyRevived | — |  | combat |
| 9658 | `AfterPlayerTurnStart` | Y | async | G → G → G | AfterPlayerTurnStartEarly,AfterPlayerTurnStart,AfterPlayerTurnStartLate | caller ctx (Push/PopModel) |  | combat |
| 9673 | `AfterAutoPostPlayPhaseEntered` | – | async | G | AfterAutoPostPlayPhaseEntered · NetId gate | caller ctx (Push/PopModel) |  | combat |
| 9688 | `AfterAutoPrePlayPhaseEntered` | – | async | G → G → G | AfterAutoPrePlayPhaseEnteredEarly,AfterAutoPrePlayPhaseEntered,AfterAutoPrePlayPhaseEnteredLate · NetId gate | caller ctx (Push/PopModel) |  | combat |
| 9703 | `AfterPotionDiscarded` | – | async | R+C | AfterPotionDiscarded | — |  | combat |
| 9718 | `AfterPotionProcured` | – | async | R+C | AfterPotionProcured | — |  | combat |
| 9733 | `BeforePotionUsed` | – | async | R+C | BeforePotionUsed | — |  | combat |
| 9749 | `AfterPotionUsed` | – | async | R+C | AfterPotionUsed | — |  | combat |
| 9765 | `BeforePowerAmountChanged` | Y | async | G | BeforePowerAmountChanged | — |  | combat |
| 9783 | `AfterPowerAmountChanged` | Y | async | G | AfterPowerAmountChanged | caller ctx |  | combat |
| 9801 | `AfterPreventingBlockClear` | Y | async | G | AfterPreventingBlockClear · modifier/preventer-filtered | — |  | combat |
| 9816 | `AfterPreventingDeath` | Y | async | R+C | AfterPreventingDeath · modifier/preventer-filtered | — |  | combat |
| 9832 | `AfterPreventingDraw` | Y | async | G | AfterPreventingDraw · modifier/preventer-filtered | — |  | combat |
| 9846 | `AfterRestSiteHeal` | – | async | R | AfterRestSiteHeal | — |  | run |
| 9861 | `AfterRestSiteSmith` | – | async | R | AfterRestSiteSmith | — |  | run |
| 9875 | `AfterRewardTaken` | – | async | R | AfterRewardTaken | — |  | run |
| 9890 | `BeforeRoomEntered` | – | async | R | BeforeRoomEntered | — |  | run |
| 9904 | `AfterRoomEntered` | – | async | R | AfterRoomEntered | — |  | run |
| 9918 | `AfterShuffle` | Y | async | G | AfterShuffle | caller ctx (Push/PopModel) |  | combat |
| 9933 | `BeforeSideTurnStart` | Y | async | G | BeforeSideTurnStart · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 9948 | `AfterSideTurnStart` | Y | async | G → G | AfterSideTurnStart,AfterSideTurnStartLate | — |  | combat |
| 9963 | `AfterStarsGained` | – | async | G | AfterStarsGained | — |  | combat |
| 9978 | `AfterStarsSpent` | – | async | G | AfterStarsSpent | — |  | combat |
| 9993 | `AfterSummon` | – | async | G | AfterSummon | caller ctx (Push/PopModel) |  | combat |
| 10009 | `AfterTakingExtraTurn` | Y | async | G | AfterTakingExtraTurn | — |  | combat |
| 10023 | `BeforeSideTurnEnd` | Y | async | G → G → G | BeforeSideTurnEndVeryEarly,BeforeSideTurnEndEarly,BeforeSideTurnEnd · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 10038 | `AfterSideTurnEnd` | Y | async | G → G | AfterSideTurnEnd,AfterSideTurnEndLate · NetId gate | **per-listener Hook ctx + WhenAll (deferral)** |  | combat |
| 10052 | `ModifyAttackHitCount` | Y | sync | G | ModifyAttackHitCount | — |  | combat |
| 10071 | `ModifyBlock` | Y | sync | G → G | ModifyBlockAdditive,ModifyBlockMultiplicative · enchantment pre-pass | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10121 | `ModifyCardBeingAddedToDeck` | – | sync | R → R | TryModifyCardBeingAddedToDeck,TryModifyCardBeingAddedToDeckLate | — | out List<AbstractModel> modifyingModels | run |
| 10163 | `ModifyCardPlayCount` | Y | sync | G | ModifyCardPlayCount | — | out List<AbstractModel> modifyingModels | combat |
| 10188 | `ModifyCardPlayResultLocation` | – | sync | G | ModifyCardPlayResultLocation | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10213 | `ModifyCardRewardAlternatives` | – | sync | R | TryModifyCardRewardAlternatives | — |  | run |
| 10235 | `ModifyCardRewardCreationOptions` | – | sync | R → R | ModifyCardRewardCreationOptions,ModifyCardRewardCreationOptionsLate | — |  | run |
| 10266 | `TryModifyCardRewardOptions` | – | sync | R → R | TryModifyCardRewardOptions,TryModifyCardRewardOptionsLate | — | out List<AbstractModel> modifiers | run |
| 10309 | `ModifyCardRewardUpgradeOdds` | – | sync | R | ModifyCardRewardUpgradeOdds | — |  | run |
| 10328 | `ModifyDamage` | Y | sync | R+C ×3 (via ModifyDamageInternal :11766) | ModifyDamageAdditive,ModifyDamageMultiplicative,ModifyDamageCap · enchantment pre-pass | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10422 | `ModifyEnergyCostInCombat` | Y | sync | G → G | TryModifyEnergyCostInCombat,TryModifyEnergyCostInCombatLate | — |  | combat |
| 10458 | `ModifyKeywordsInCombat` | – | sync | C | TryModifyKeywordsInCombat | — |  | combat |
| 10475 | `ModifyEnergyGain` | Y | sync | G | ModifyEnergyGain | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10501 | `ModifyGoldGained` | – | sync | R+C | ModifyGoldGained | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10527 | `ModifyExtraRestSiteHealText` | – | sync | R | ModifyExtraRestSiteHealText | — |  | run |
| 10545 | `ModifyGeneratedMap` | – | sync | R | ModifyGeneratedMap | — |  | run |
| 10564 | `ModifyGeneratedMapLate` | – | sync | R | ModifyGeneratedMapLate | — |  | run |
| 10583 | `ModifyHandDraw` | Y | sync | G → G | ModifyHandDraw,ModifyHandDrawLate | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10627 | `ModifyHpLost` | Y | sync | R+C → R+C → R+C → R+C | ModifyHpLostBeforeOsty,ModifyHpLostBeforeOstyLate,ModifyHpLostAfterOsty,ModifyHpLostAfterOstyLate | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10713 | `ModifyMaxEnergy` | Y | sync | G | ModifyMaxEnergy | — |  | combat |
| 10732 | `ModifyMerchantCardCreationResults` | – | sync | R | ModifyMerchantCardCreationResults | — |  | run |
| 10749 | `ModifyMerchantCardPool` | – | sync | R | ModifyMerchantCardPool | — |  | run |
| 10767 | `ModifyMerchantCardRarity` | – | sync | R | ModifyMerchantCardRarity | — |  | run |
| 10785 | `ModifyMerchantPrice` | – | sync | R | ModifyMerchantPrice | — |  | run |
| 10803 | `ModifyNextEvent` | – | sync | R | ModifyNextEvent | — |  | run |
| 10822 | `ModifyOddsIncreaseForUnrolledRoomType` | – | sync | R | ModifyOddsIncreaseForUnrolledRoomType | — |  | run |
| 10840 | `ModifyOrbPassiveTriggerCount` | – | sync | G | ModifyOrbPassiveTriggerCounts | — | out List<AbstractModel> modifyingModels | combat |
| 10865 | `ModifyOrbValue` | – | sync | G | ModifyOrbValue | — |  | combat |
| 10884 | `ModifyPowerAmountGiven` | Y | sync | G → G | ModifyPowerAmountGivenAdditive,ModifyPowerAmountGivenMultiplicative | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10928 | `ModifyPowerAmountReceived` | Y | sync | G | TryModifyPowerAmountReceived | — | out IEnumerable<AbstractModel> modifiers | combat |
| 10953 | `ModifyRestSiteHealAmount` | – | sync | R | ModifyRestSiteHealAmount | — |  | run |
| 10972 | `ModifyRestSiteOptions` | – | sync | R | TryModifyRestSiteOptions | — |  | run |
| 10994 | `ModifyRestSiteHealRewards` | – | sync | R | TryModifyRestSiteHealRewards | — |  | run |
| 11016 | `ModifyRewards` | – | sync | R → R | TryModifyRewards,TryModifyRewardsLate | — |  | run |
| 11054 | `ModifyShuffleOrder` | Y | sync | G | ModifyShuffleOrder | — |  | combat |
| 11071 | `ModifyStarCost` | Y | sync | G | TryModifyStarCost | — |  | combat |
| 11094 | `ModifySummonAmount` | – | sync | G | ModifySummonAmount | — |  | combat |
| 11113 | `ModifyUnblockedDamageTarget` | Y | sync | C | ModifyUnblockedDamageTarget | — |  | combat |
| 11132 | `ModifyUnknownMapPointRoomTypes` | – | sync | R | ModifyUnknownMapPointRoomTypes | — |  | run |
| 11151 | `ModifyXValue` | Y | sync | G | ModifyXValue | — |  | combat |
| 11170 | `ShouldAddToDeck` | – | sync | R | ShouldAddToDeck | — | out AbstractModel? preventer | run |
| 11193 | `ShouldAfflict` | – | sync | G | ShouldAfflict | — |  | combat |
| 11214 | `ShouldAllowAncient` | – | sync | R | ShouldAllowAncient | — |  | run |
| 11235 | `ShouldAllowHitting` | Y | sync | G | ShouldAllowHitting | — |  | combat |
| 11256 | `ShouldAllowMerchantCardRemoval` | – | sync | R | ShouldAllowMerchantCardRemoval | — |  | run |
| 11277 | `ShouldAllowSelectingMoreCardRewards` | – | sync | R | ShouldAllowSelectingMoreCardRewards | — |  | run |
| 11298 | `ShouldAllowTargeting` | Y | sync | G | ShouldAllowTargeting | — | out AbstractModel? preventer | combat |
| 11321 | `ShouldClearBlock` | Y | sync | G | ShouldClearBlock | — | out AbstractModel? preventer | combat |
| 11344 | `ShouldCreatureBeRemovedFromCombatAfterDeath` | Y | sync | C | ShouldCreatureBeRemovedFromCombatAfterDeath | — |  | combat |
| 11365 | `ShouldDie` | Y | sync | R+C → R+C | ShouldDie,ShouldDieLate | — | out AbstractModel? preventer | combat |
| 11405 | `ShouldDisableRemainingRestSiteOptions` | – | sync | R | ShouldDisableRemainingRestSiteOptions | — |  | run |
| 11426 | `ShouldDraw` | Y | sync | G | ShouldDraw | — | out AbstractModel? modifier | combat |
| 11449 | `ShouldEtherealTrigger` | Y | sync | G | ShouldEtherealTrigger | — |  | combat |
| 11470 | `ShouldFlush` | Y | sync | G | ShouldFlush | — |  | combat |
| 11491 | `ShouldGenerateTreasure` | – | sync | R | ShouldGenerateTreasure | — |  | run |
| 11512 | `ShouldGainStars` | – | sync | G | ShouldGainStars | — |  | combat |
| 11533 | `ShouldPayExcessEnergyCostWithStars` | Y | sync | G | ShouldPayExcessEnergyCostWithStars | — |  | combat |
| 11554 | `ShouldPlay` | Y | sync | G | ShouldPlay | — | out AbstractModel? preventer | combat |
| 11577 | `ShouldPlayerResetEnergy` | Y | sync | G | ShouldPlayerResetEnergy | — |  | combat |
| 11598 | `ShouldProceedToNextMapPoint` | – | sync | R | ShouldProceedToNextMapPoint | — |  | run |
| 11619 | `ShouldProcurePotion` | – | sync | R+C | ShouldProcurePotion | — |  | combat |
| 11640 | `ShouldRefillMerchantEntry` | – | sync | R | ShouldRefillMerchantEntry | — |  | run |
| 11661 | `ShouldStopCombatFromEnding` | Y | sync | C | ShouldStopCombatFromEnding | — |  | combat |
| 11682 | `ShouldTakeExtraTurn` | Y | sync | G | ShouldTakeExtraTurn | — |  | combat |
| 11703 | `ShouldForcePotionReward` | – | sync | R | ShouldForcePotionReward | — |  | run |
| 11722 | `ShouldAllowFreeTravel` | – | sync | R | ShouldAllowFreeTravel | — |  | run |
| 11741 | `ShouldPowerBeRemovedOnDeath` | Y | sync | C | ShouldPowerBeRemovedOnDeath | — |  | combat |

## 3. Cmd facades (`Commands/*.cs`, `Commands.Builders/AttackCommand.cs`)

Status: **ported** (sim method with the same behaviour), **partial** (some overloads or side effects
missing), **missing**, **strip** (VFX, SFX, animation, waits; no rule state). Uses = call sites in
`Models.*` content per method name (all overloads of a name share the count). Scope follows the callers
(combat, run, multiplayer, unused).

Cross-cutting notes:

- **Async ordering.** Every Cmd awaits its sub-steps in source order; the sim calls them sequentially.
  `Cmd.Wait`/`Cmd.CustomScaledWait` return immediately in non-interactive mode (`Commands/Cmd.cs:75`,
  `:172`) and are stripped. The only rule-relevant reordering in single player comes from paused choice
  contexts (§4.6). `TaskHelper.RunSafely` fire-and-forget tasks in combat content are visual
  (`DoActivateVisuals` in 20 relics, death-animation reveals in `StockPower`/`InfestedPower`).
- **choiceContext plumbing.** Cmds that take a context only pass it on: to hooks (`AfterCardDrawn`,
  `AfterShuffle`, `AfterCardDiscarded`, `AfterCardExhausted`, `AfterDamageReceived`,
  `AfterPowerAmountChanged`, …), to `CardSelectCmd` and to `OnPlayWrapper` for auto-play. The sim keeps
  signatures without it and uses the ambient context stack (§4.6).
- **Combat history.** `CombatManager.History` recording sits inside Cmds (damage, block, power,
  attack, energy spent, card drawn/discarded/exhausted/generated, card play start/finish, orb
  channeled, potion used, stars modified, summoned, affliction). It is rule state for 49 content files
  (§5) and must be recorded at the same points.
- **AttackCommand builder.** 11 fluent methods are VFX only (`WithAttackerAnim`, `WithNoAttackerAnim`,
  `AfterAttackerAnim` — all four callers only shake the screen or fire beams —, `WithAttackerFx` ×2,
  `WithWaitBeforeHit`, `WithHitFx`, `SpawningHitVfxOnEachCreature`, `WithHitVfxSpawnedAtBase`,
  `WithHitVfxNode`, `OnlyPlayAnimOnce`); port them as no-ops or drop them at call sites. `FromOsty`
  (19 cards) and the `CalculatedDamageVar` overload of `DamageCmd.Attack` (19 cards) are missing.
- **Partial in the sim:** `CreatureCmd.Damage` and `GainBlock` lack the `DamageVar`/`BlockVar`
  overloads (27 files pass a var); `CardPileCmd.Add` lacks the `CardPile` overload, `clonedBy` and
  `AfterCardChangedPiles`; `CardCmd.Discard` exists only as single-card `CardPileCmd.Discard` and throws
  on Sly cards (Sly auto-play on discard, `Commands/CardCmd.cs:843`, `:917`).
- Highest-use missing combat Cmds (files): `CardCmd.Upgrade` 59, `OrbCmd.Channel` 30,
  `CreatureCmd.Damage(DamageVar)` 27, `CardSelectCmd.FromHand` 23, `CardPileCmd.AddGeneratedCardsToCombat` 19,
  `DamageCmd.Attack(CalculatedDamageVar)` 19, `CardSelectCmd.FromCombatPile` 18,
  `CardPileCmd.AddToCombatAndPreview` 17, `PlayerCmd.GainStars` 16, `OstyCmd.Summon` 15,
  `CardCmd.AutoPlay` 14, `ForgeCmd.Forge` 14, `CardSelectCmd.FromChooseACardScreen` 13,
  `AttackCommand.FromOsty` 13.


**CardPileCmd** (`Commands/CardPileCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 3123 | `RemoveFromDeck(CardModel, bool)` | missing | combat | 21 | Cards 4, Events 10, Powers 1, Relics 6 | deck (run state); reachable in combat: DeprecatedCard.OnPlay removes its DeckVersion, SwipePower |
| 3137 | `RemoveFromDeck(IReadOnlyList<CardModel>, bool)` | missing | combat | 21 | Cards 4, Events 10, Powers 1, Relics 6 | deck (run state); reachable in combat: DeprecatedCard.OnPlay removes its DeckVersion, SwipePower |
| 3151 | `RemoveFromCombat(CardModel, bool)` | ported | combat | 3 | Models(base) 2, Monsters 1 |  |
| 3165 | `RemoveFromCombat(IEnumerable<CardModel>, bool)` | ported | combat | 3 | Models(base) 2, Monsters 1 |  |
| 3179 | `GiveToAnotherPlayer(CardModel, Player, PileType, CardPilePosition, AbstractModel?)` | missing | mp | 1 | Models(base) 1 | multiplayer only |
| 3196 | `AddGeneratedCardToCombat(CardModel, PileType, Player?, CardPilePosition)` | ported | combat | 53 | Cards 29, Monsters 6, Potions 5, Powers 6, Relics 7 |  |
| 3204 | `AddGeneratedCardsToCombat(IEnumerable<CardModel>, PileType, Player?, CardPilePosition)` | missing | combat | 21 | Cards 11, Potions 1, Powers 4, Relics 5 | batch variant; History.CardGenerated per card |
| 3251 | `Add(CardModel, PileType, CardPilePosition, AbstractModel?, bool)` | partial | combat | 77 | Cards 22, Events 14, Models(base) 4, Modifiers 6, Potions 3, Powers 3, Relics 25 | batched; AfterCardChangedPiles (Run+Combat, normal+Late) per moved card not dispatched by sim (:3466) |
| 3263 | `Add(CardModel, CardPile, CardPilePosition, AbstractModel?, bool)` | partial | combat | 77 | Cards 22, Events 14, Models(base) 4, Modifiers 6, Potions 3, Powers 3, Relics 25 | batched; AfterCardChangedPiles (Run+Combat, normal+Late) per moved card not dispatched by sim (:3466) |
| 3271 | `Add(IEnumerable<CardModel>, PileType, CardPilePosition, AbstractModel?, bool)` | partial | combat | 77 | Cards 22, Events 14, Models(base) 4, Modifiers 6, Potions 3, Powers 3, Relics 25 | batched; AfterCardChangedPiles (Run+Combat, normal+Late) per moved card not dispatched by sim (:3466) |
| 3283 | `Add(IEnumerable<CardModel>, CardPile, CardPilePosition, AbstractModel?, bool, bool)` | partial | combat | 77 | Cards 22, Events 14, Models(base) 4, Modifiers 6, Potions 3, Powers 3, Relics 25 | batched; AfterCardChangedPiles (Run+Combat, normal+Late) per moved card not dispatched by sim (:3466) |
| 3478 | `GetTweenForCardsChangingPiles(IEnumerable<CardPileAddResult>, bool)` | strip | strip | 0 |  |  |
| 3837 | `AddDuringManualCardPlay(CardModel)` | partial | combat | 1 | Models(base) 1 | sim inlines in ActionExecutor; hook AfterCardChangedPiles (:980) not dispatched |
| 3976 | `Draw(ctx, Player)` | ported | combat | 68 | Cards 50, Enchantments 1, Potions 6, Powers 5, Relics 6 |  |
| 3983 | `Draw(ctx, decimal, Player, bool fromHandDraw)` | ported | combat | 68 | Cards 50, Enchantments 1, Potions 6, Powers 5, Relics 6 |  |
| 3988 | `DrawWithoutBlockingOnOtherPlayers(ctx, decimal, Player, CardModel, bool fromHandDraw)` | missing | combat (mp variant) | 1 | Cards 1 | multiplayer draw; single player ≡ Draw (verify) |
| 4060 | `Shuffle(ctx, Player)` | ported | combat | 2 | Cards 1, Potions 1 | AfterShuffle carries ctx → StratagemPower prompt |
| 4074 | `AutoPlayFromDrawPile(ctx, Player, int, CardPilePosition, bool)` | missing | combat | 5 | Cards 3, Potions 1, Powers 1 | draws top N and CardCmd.AutoPlay(forceExhaust); prompts inside use caller ctx |
| 4091 | `ShuffleIfNecessary(ctx, Player)` | ported | combat | 1 | Powers 1 |  |
| 4117 | `AddToCombatAndPreview<T>(IEnumerable<Creature>, PileType, int, Player?, CardPilePosition)` | missing | combat | 17 | Monsters 14, Powers 2, Relics 1 | create N copies of T into a pile (statuses from monsters); preview stripped |
| 4134 | `AddToCombatAndPreview<T>(Creature, PileType, int, Player?, CardPilePosition)` | missing | combat | 17 | Monsters 14, Powers 2, Relics 1 | create N copies of T into a pile (statuses from monsters); preview stripped |
| 4151 | `AddCurseToDeck<T>(Player)` | missing | combat | 16 | Events 12, Relics 4 | run layer (deck) |
| 4159 | `AddCursesToDeck(IEnumerable<CardModel>, Player)` | missing | run | 4 | Events 4 | run layer (deck) |

**CardCmd** (`Commands/CardCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 1779 | `AutoPlay(ctx, CardModel, Creature?, AutoPlayType, bool, bool)` | missing | combat | 15 | Cards 9, Enchantments 1, Powers 3, Relics 2 | ShouldPlay(AutoPlayType); random AnyEnemy target via Rng.CombatTargets; X capture; OnPlayWrapper(isAutoPlay) |
| 1811 | `Discard(ctx, CardModel)` | partial | combat | 9 | Cards 8, Powers 1 | via DiscardAndDraw: Sly cards auto-play (AutoPlayType.SlyDiscard, Commands/CardCmd.cs:843,917) |
| 1825 | `Discard(ctx, IEnumerable<CardModel>)` | partial | combat | 9 | Cards 8, Powers 1 | via DiscardAndDraw: Sly cards auto-play (AutoPlayType.SlyDiscard, Commands/CardCmd.cs:843,917) |
| 1839 | `DiscardAndDraw(ctx, IEnumerable<CardModel>, int)` | missing | combat | 3 | Cards 1, Potions 1, Relics 1 | Sly auto-play then draw |
| 1853 | `Downgrade(CardModel)` | missing | combat | 3 | Events 2, Powers 1 | IsEnding gate; DowngradeInternal |
| 1867 | `Exhaust(ctx, CardModel, bool causedByEthereal, bool)` | ported | combat | 20 | Cards 13, Models(base) 2, Potions 2, Powers 1, Relics 2 | sim: CardPileCmd.Exhaust |
| 1882 | `Upgrade(CardModel, CardPreviewStyle)` | missing | combat | 63 | Cards 22, Events 12, Potions 3, Powers 3, Relics 23 | sync void; combat + deck |
| 1887 | `Upgrade(IEnumerable<CardModel>, CardPreviewStyle)` | missing | combat | 63 | Cards 22, Events 12, Potions 3, Powers 3, Relics 23 | sync void; combat + deck |
| 1949 | `TransformToRandom(CardModel, Rng, CardPreviewStyle)` | missing | combat | 8 | Events 6, Powers 1, Relics 1 | explicit Rng parameter |
| 1957 | `TransformTo<T>(CardModel, CardPreviewStyle)` | missing | combat | 6 | Cards 3, Events 2, Relics 1 |  |
| 1966 | `Transform(CardModel, CardModel, CardPreviewStyle)` | missing | combat | 9 | Cards 4, Relics 5 | replacement card; combat and deck scopes |
| 1988 | `Transform(IEnumerable<CardTransformation>, Rng?, CardPreviewStyle)` | missing | combat | 9 | Cards 4, Relics 5 | replacement card; combat and deck scopes |
| 2146 | `Enchant<T>(CardModel, decimal)` | missing | combat | 27 | Cards 1, Events 10, Relics 16 | attaches EnchantmentModel (mostly run layer) |
| 2151 | `Enchant(EnchantmentModel, CardModel, decimal)` | missing | combat | 27 | Cards 1, Events 10, Relics 16 | attaches EnchantmentModel (mostly run layer) |
| 2182 | `ClearEnchantment(CardModel)` | missing | unused | 0 |  |  |
| 2188 | `AfflictAndPreview<T>(IEnumerable<CardModel>, decimal, CardPreviewStyle)` | missing | combat | 1 | Powers 1 |  |
| 2228 | `Afflict<T>(CardModel, decimal)` | missing | combat | 11 | Powers 11 | ShouldAfflict gate; History.CardAfflicted |
| 2235 | `Afflict(AfflictionModel, CardModel, decimal)` | missing | combat | 11 | Powers 11 | ShouldAfflict gate; History.CardAfflicted |
| 2273 | `ClearAffliction(CardModel)` | missing | combat | 8 | Afflictions 1, Powers 7 |  |
| 2278 | `ApplyKeyword(CardModel, params CardKeyword[])` | missing | combat | 10 | Cards 2, Powers 4, Relics 4 |  |
| 2287 | `RemoveKeyword(CardModel, params CardKeyword[])` | missing | combat | 1 | Enchantments 1 |  |
| 2296 | `ApplySingleTurnSly(CardModel)` | missing | combat | 1 | Cards 1 |  |
| 2302 | `ApplySingleTurnRetain(CardModel)` | missing | combat | 1 | Cards 1 |  |
| 2308 | `Preview(CardModel, float, CardPreviewStyle)` | strip | strip | 12 | Cards 2, Events 3, Relics 7 | UI |
| 2313 | `Preview(IReadOnlyList<CardModel>, float, CardPreviewStyle)` | strip | strip | 12 | Cards 2, Events 3, Relics 7 | UI |
| 2334 | `PreviewCardPileAdd(CardPileAddResult, float, CardPreviewStyle)` | strip | strip | 66 | Cards 16, Events 14, Models(base) 1, Modifiers 6, Monsters 4, Powers 2, Relics 23 | UI |
| 2344 | `PreviewCardPileAdd(IReadOnlyList<CardPileAddResult>, float, CardPreviewStyle)` | strip | strip | 66 | Cards 16, Events 14, Models(base) 1, Modifiers 6, Monsters 4, Powers 2, Relics 23 | UI |

**CardSelectCmd** (`Commands/CardSelectCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 3112 | `Reset()` | missing | infra | 0 |  |  |
| 3122 | `UseSelector(MegaCrit.Sts2.Core.TestSupport.ICardSelector, bool)` | missing | infra (test seam) | 0 |  |  |
| 3134 | `PushSelector(MegaCrit.Sts2.Core.TestSupport.ICardSelector, bool)` | missing | combat (WhisperingEarring) | 1 | Relics 1 |  |
| 3141 | `SuspendSelectorForTest(bool)` | missing | infra (test seam) | 0 |  |  |
| 3169 | `FromChooseACardScreen(ctx, IReadOnlyList<CardModel>, Player, bool)` | missing | combat | 13 | Cards 4, Monsters 1, Potions 4, Relics 4 |  |
| 3236 | `FromSimpleGridForRewards(ctx, List<CardCreationResult>, Player, CardSelectorPrefs)` | missing | run | 3 | Models(base) 1, Modifiers 1, Relics 1 |  |
| 3295 | `FromSimpleGrid(ctx, IReadOnlyList<CardModel>, Player, CardSelectorPrefs)` | missing | combat | 1 | Relics 1 |  |
| 3353 | `FromCombatPile(ctx, CardPile, Player, CardSelectorPrefs)` | missing | combat | 18 | Cards 14, Potions 2, Powers 2 |  |
| 3361 | `FromCombatPile(ctx, CardPile, Player, CardSelectorPrefs, Func<CardModel, bool>?)` | missing | combat | 18 | Cards 14, Potions 2, Powers 2 |  |
| 3441 | `FromDeckForUpgrade(Player, CardSelectorPrefs)` | missing | run | 6 | Events 4, Relics 2 |  |
| 3485 | `FromDeckForTransformation(Player, CardSelectorPrefs, Func<CardModel, CardTransformation>?)` | missing | run | 9 | Events 6, Relics 3 |  |
| 3533 | `FromDeckForEnchantment(Player, EnchantmentModel, int, CardSelectorPrefs)` | missing | run | 17 | Events 10, Relics 7 |  |
| 3541 | `FromDeckForEnchantment(Player, EnchantmentModel, int, Func<CardModel?, bool>?, CardSelectorPrefs)` | missing | run | 17 | Events 10, Relics 7 |  |
| 3550 | `FromDeckForEnchantment(IReadOnlyList<CardModel>, EnchantmentModel, int, CardSelectorPrefs)` | missing | run | 17 | Events 10, Relics 7 |  |
| 3607 | `FromDeckForRemoval(Player, CardSelectorPrefs, Func<CardModel, bool>?)` | missing | run | 13 | Events 7, Relics 6 |  |
| 3614 | `FromDeckGeneric(Player, CardSelectorPrefs, Func<CardModel, bool>?, Func<CardModel, int>?)` | missing | run | 3 | Events 2, Relics 1 |  |
| 3668 | `FromHand(ctx, Player, CardSelectorPrefs, Func<CardModel, bool>?, AbstractModel)` | missing | combat | 23 | Cards 18, Potions 2, Powers 2, Relics 1 | see §4 |
| 3726 | `FromHandForDiscard(ctx, Player, CardSelectorPrefs, Func<CardModel, bool>?, AbstractModel)` | missing | combat | 8 | Cards 5, Potions 1, Powers 1, Relics 1 |  |
| 3744 | `FromHandForUpgrade(ctx, Player, AbstractModel)` | missing | combat | 1 | Cards 1 |  |
| 3798 | `FromChooseABundleScreen(Player, IReadOnlyList<IReadOnlyList<CardModel>>)` | missing | run | 1 | Relics 1 |  |

**CreatureCmd** (`Commands/CreatureCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 3554 | `Add<T>(ICombatState combatState, string?)` | missing | combat | 10 | Monsters 6, Powers 4 | mid-combat monster spawn (CreateCreature/AddCreature/SetUpForCombat/AfterCreatureAdded) |
| 3564 | `Add(MonsterModel, ICombatState combatState, CombatSide, string?)` | missing | combat | 10 | Monsters 6, Powers 4 | mid-combat monster spawn (CreateCreature/AddCreature/SetUpForCombat/AfterCreatureAdded) |
| 3575 | `Add(Creature)` | missing | combat | 10 | Monsters 6, Powers 4 | mid-combat monster spawn (CreateCreature/AddCreature/SetUpForCombat/AfterCreatureAdded) |
| 3588 | `Damage(ctx, Creature, DamageVar, CardModel, CardPlay?)` | missing | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3596 | `Damage(ctx, Creature, decimal, ValueProp, CardModel?, CardPlay?)` | ported | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3606 | `Damage(ctx, IEnumerable<Creature>, DamageVar, Creature)` | missing | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3614 | `Damage(ctx, IEnumerable<Creature>, decimal, ValueProp, Creature)` | ported | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3622 | `Damage(ctx, Creature, DamageVar, Creature)` | missing | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3630 | `Damage(ctx, Creature, decimal, ValueProp, Creature)` | ported | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3638 | `Damage(ctx, Creature, DamageVar, Creature?, CardModel?, CardPlay?)` | missing | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3646 | `Damage(ctx, Creature, decimal, ValueProp, Creature?, CardModel?, CardPlay?)` | ported | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3654 | `Damage(ctx, IEnumerable<Creature>?, DamageVar, Creature?, CardModel?, CardPlay?)` | missing | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3662 | `Damage(ctx, IEnumerable<Creature>?, decimal, ValueProp, Creature?, CardModel?, CardPlay?)` | ported | combat | 92 | Cards 19, Enchantments 1, Events 17, Orbs 7, Potions 4, Powers 28, Relics 16 | decimal overloads ported; DamageVar overloads (props from the var) missing |
| 3873 | `Kill(Creature, bool)` | ported | combat | 8 | Cards 2, Events 1, Monsters 2, Powers 3 |  |
| 3887 | `Kill(IReadOnlyCollection<Creature>, bool)` | ported | combat | 8 | Cards 2, Events 1, Monsters 2, Powers 3 |  |
| 3915 | `Escape(Creature, bool)` | missing | combat | 3 | Monsters 2, Powers 1 | monster leaves combat without dying |
| 3942 | `GainBlock(Creature, BlockVar, CardPlay?, bool)` | missing | combat | 154 | Cards 80, Enchantments 1, Monsters 23, Orbs 6, Potions 3, Powers 21, Relics 20 | decimal overload ported; BlockVar overload missing |
| 3950 | `GainBlock(Creature, decimal, ValueProp, CardPlay?, bool)` | ported | combat | 154 | Cards 80, Enchantments 1, Monsters 23, Orbs 6, Potions 3, Powers 21, Relics 20 | decimal overload ported; BlockVar overload missing |
| 3988 | `LoseBlock(ctx, Creature, decimal, Creature?)` | ported | combat | 3 | Cards 1, Powers 1, Relics 1 |  |
| 4004 | `Heal(Creature, decimal, bool)` | ported | combat | 36 | Cards 2, Events 9, Models(base) 1, Monsters 3, Potions 3, Powers 3, Relics 15 |  |
| 4019 | `SetCurrentHp(Creature, decimal)` | ported | combat | 2 | Relics 2 |  |
| 4033 | `GainMaxHp(Creature, decimal)` | ported | combat | 20 | Cards 1, Events 6, Potions 1, Relics 12 |  |
| 4047 | `LoseMaxHp(ctx, Creature, decimal, bool)` | ported | combat | 10 | Cards 1, Events 4, Modifiers 2, Powers 1, Relics 2 |  |
| 4063 | `SetMaxHp(Creature, decimal)` | ported | combat | 1 | Monsters 1 |  |
| 4078 | `SetMaxAndCurrentHp(Creature, decimal)` | ported | combat | 3 | Monsters 3 |  |
| 4092 | `Stun(Creature, string?)` | ported | combat | 11 | Cards 1, Monsters 1, Powers 9 | Wrapper state machine body missing in decompile (pipeline spec §9) |
| 4105 | `Stun(Creature, Func<IReadOnlyList<Creature>, Task>, string?)` | ported | combat | 11 | Cards 1, Monsters 1, Powers 9 | Wrapper state machine body missing in decompile (pipeline spec §9) |
| 4128 | `TriggerAnim(Creature, string, float)` | strip | strip | 373 | Cards 237, Events 9, Monsters 111, Powers 13, Relics 3 | animation (strip) |

**PowerCmd** (`Commands/PowerCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 1185 | `Apply<T>(ctx, IEnumerable<Creature>?, decimal, Creature?, CardModel?, bool)` | ported | combat | 585 | Cards 278, Enchantments 1, Modifiers 3, Monsters 189, Potions 29, Powers 49, Relics 36 |  |
| 1215 | `Apply<T>(ctx, Creature, decimal, Creature?, CardModel?, bool)` | ported | combat | 585 | Cards 278, Enchantments 1, Modifiers 3, Monsters 189, Potions 29, Powers 49, Relics 36 |  |
| 1242 | `Apply(ctx, PowerModel, Creature, decimal, Creature?, CardModel?, bool)` | ported | combat | 585 | Cards 278, Enchantments 1, Modifiers 3, Monsters 189, Potions 29, Powers 49, Relics 36 |  |
| 1260 | `FindExistingInstanceForStacking(PowerModel, Creature, Creature?)` | ported | combat | 1 | Cards 1 |  |
| 1273 | `Decrement(PowerModel)` | ported | combat | 42 | Powers 42 |  |
| 1286 | `TickDownDuration(PowerModel)` | ported | combat | 3 | Powers 3 |  |
| 1299 | `ModifyAmount(ctx, PowerModel, decimal, Creature?, CardModel?, bool)` | ported | combat | 5 | Cards 3, Powers 2 |  |
| 1361 | `Remove<T>(Creature)` | ported | combat | 67 | Cards 1, Monsters 7, Powers 59 |  |
| 1374 | `Remove(PowerModel?)` | ported | combat | 67 | Cards 1, Monsters 7, Powers 59 |  |

**PlayerCmd** (`Commands/PlayerCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 802 | `GainEnergy(decimal, Player)` | ported | combat | 51 | Cards 24, Enchantments 1, Orbs 3, Potions 3, Powers 5, Relics 15 |  |
| 815 | `LoseEnergy(decimal, Player)` | ported | combat | 2 | Cards 1, Relics 1 |  |
| 830 | `SetEnergy(decimal, Player)` | ported | combat | 0 |  |  |
| 844 | `GainStars(decimal, Player)` | missing | combat | 16 | Cards 9, Potions 1, Powers 3, Relics 3 | ShouldGainStars gate (no overrides), AfterStarsGained |
| 857 | `LoseStars(decimal, Player)` | missing | unused | 0 |  |  |
| 868 | `SetStars(decimal, Player)` | missing | combat (mp death path) | 0 |  |  |
| 882 | `GainGold(decimal, Player, bool)` | missing | combat | 24 | Cards 3, Events 14, Potions 1, Relics 6 | run resource touched in combat (ModifyGoldGained/AfterGoldGained) |
| 896 | `LoseGold(decimal, Player, GoldLossType)` | missing | combat | 19 | Cards 1, Events 15, Powers 1, Relics 2 |  |
| 921 | `SetGold(decimal, Player)` | missing | unused | 0 |  |  |
| 934 | `GainMaxPotionCount(int, Player)` | missing | run | 3 | Relics 3 |  |
| 940 | `LoseMaxPotionCount(int, Player)` | missing | unused | 0 |  |  |
| 947 | `AddPet<T>(Player)` | missing | combat | 2 | Relics 2 | creates pet creature on the player side (Osty) |
| 957 | `AddPet(Creature, Player)` | missing | combat | 2 | Relics 2 | creates pet creature on the player side (Osty) |
| 971 | `MimicRestSiteHeal(Player, bool)` | missing | run | 1 | Events 1 | event |
| 984 | `EndTurn(Player, bool, Func<Task>?)` | missing | combat | 1 | Cards 1 | card-forced end of turn |
| 996 | `CompleteQuest(CardModel)` | missing | combat | 5 | Cards 3, Events 2 | quest cards |

**DamageCmd** (`Commands/DamageCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 8 | `Attack(decimal)` | ported | combat | 405 | Cards 196, Monsters 209 | decimal overload ported; CalculatedDamageVar overload missing |
| 13 | `Attack(CalculatedDamageVar)` | missing | combat | 405 | Cards 196, Monsters 209 | decimal overload ported; CalculatedDamageVar overload missing |

**OrbCmd** (`Commands/OrbCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 647 | `AddSlots(Player, int)` | missing | combat | 4 | Cards 2, Potions 1, Relics 1 |  |
| 659 | `RemoveSlots(Player, int)` | missing | combat | 1 | Cards 1 |  |
| 670 | `Channel<T>(ctx, Player)` | missing | combat | 32 | Cards 24, Potions 1, Powers 4, Relics 3 | evoke front when full; TryEnqueue; AfterOrbChanneled; History.OrbChanneled |
| 684 | `Channel(ctx, OrbModel, Player)` | missing | combat | 32 | Cards 24, Potions 1, Powers 4, Relics 3 | evoke front when full; TryEnqueue; AfterOrbChanneled; History.OrbChanneled |
| 699 | `EvokeNext(ctx, Player, bool)` | missing | combat | 6 | Cards 6 | AfterOrbEvoked |
| 714 | `EvokeLast(ctx, Player, bool)` | missing | combat | 1 | Powers 1 |  |
| 745 | `Passive(ctx, OrbModel, Creature?, bool)` | missing | combat | 5 | Cards 3, Powers 1, Relics 1 | ModifyOrbPassiveTriggerCount (+AfterModifying…) |

**OstyCmd** (`Commands/OstyCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 336 | `Summon(ctx, Player, decimal, AbstractModel?)` | missing | combat | 16 | Cards 9, Potions 1, Powers 3, Relics 3 | ModifySummonAmount; GainMaxHp or revive/create Osty + DieForYouPower; AfterOstyRevived; History.Summoned; AfterSummon |

**PotionCmd** (`Commands/PotionCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 245 | `TryToProcure<T>(Player)` | missing | combat | 7 | Cards 1, Potions 1, Relics 5 | slot capacity; ShouldProcurePotion; AfterPotionProcured |
| 253 | `TryToProcure(PotionModel, Player, int)` | missing | combat | 7 | Cards 1, Potions 1, Relics 5 | slot capacity; ShouldProcurePotion; AfterPotionProcured |
| 283 | `Discard(PotionModel)` | missing | combat | 3 | Events 3 | AfterPotionDiscarded |

**RelicCmd** (`Commands/RelicCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 395 | `Obtain<T>(Player)` | missing | run | 31 | Events 29, Relics 2 | AfterObtained (run-layer prompts) |
| 403 | `Obtain(RelicModel, Player, int)` | missing | run | 31 | Events 29, Relics 2 | AfterObtained (run-layer prompts) |
| 428 | `Remove(RelicModel)` | missing | run | 2 | Events 2 |  |
| 441 | `Replace(RelicModel, RelicModel)` | missing | run | 2 | Relics 2 |  |
| 454 | `Melt(RelicModel)` | missing | combat-capable (verify) | 1 | Relics 1 | melted relics leave listener set |

**RelicSelectCmd** (`Commands/RelicSelectCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 163 | `FromChooseARelicScreen(Player, IReadOnlyList<RelicModel>)` | missing | unused | 0 |  |  |

**ForgeCmd** (`Commands/ForgeCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 153 | `Forge(decimal, Player, AbstractModel?)` | missing | combat | 14 | Cards 10, Potions 1, Powers 2, Relics 1 | create SovereignBlade in hand if none; add damage to all blades (incl. exhausted); AfterForge |
| 249 | `PlayCombatRoomForgeVfx(Player, CardModel)` | strip | strip | 1 | Cards 1 | VFX (strip) |

**Cmd** (`Commands/Cmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 273 | `Wait(float, bool)` | strip | strip | 128 | Cards 23, Events 12, Models(base) 2, Monsters 42, Powers 15, Relics 34 | NonInteractiveMode → immediate (strip) |
| 281 | `Wait(float, CancellationToken, bool)` | strip | strip | 128 | Cards 23, Events 12, Models(base) 2, Monsters 42, Powers 15, Relics 34 | NonInteractiveMode → immediate (strip) |
| 314 | `CustomScaledWait(float, float, bool, CancellationToken)` | strip | strip | 56 | Cards 6, Events 14, Models(base) 7, Modifiers 4, Monsters 12, Potions 1, Powers 8, Relics 4 | strip |

**RewardsCmd** (`Commands/RewardsCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 295 | `OfferForRoomEnd(Player, AbstractRoom)` | missing | run | 0 |  |  |
| 309 | `OfferCustom(Player, List<Reward>)` | missing | run | 22 | Events 14, Relics 8 | run layer |
| 323 | `GenerateForRoomEnd(Player, AbstractRoom)` | missing | run | 0 |  |  |
| 333 | `GenerateCustom(Player, List<Reward>)` | missing | run | 0 |  |  |

**MapCmd** (`Commands/MapCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 11 | `SetBossEncounter(IRunState, EncounterModel)` | missing | run | 0 |  |  |

**VfxCmd** (`Commands/VfxCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 116 | `PlayFullScreenInCombat(string, Creature?)` | strip | strip | 2 | Cards 2 |  |
| 136 | `GetSideCenter(CombatSide, ICombatState combatState)` | strip | strip | 3 | Monsters 3 |  |
| 184 | `GetSideCenterFloor(CombatSide, ICombatState combatState)` | strip | strip | 1 | Cards 1 |  |
| 234 | `PlayOnSide(CombatSide, string, ICombatState combatState)` | strip | strip | 0 |  |  |
| 247 | `PlayOnCreatureCenters(IEnumerable<Creature>, string)` | strip | strip | 12 | Monsters 8, Powers 2, Relics 2 |  |
| 264 | `PlayOnCreatureCenter(Creature, string)` | strip | strip | 26 | Cards 10, Events 4, Monsters 6, Powers 2, Relics 4 |  |
| 277 | `PlayOnCreatures(IEnumerable<Creature>, string)` | strip | strip | 0 |  |  |
| 294 | `PlayOnCreature(Creature, string)` | strip | strip | 6 | Cards 2, Events 1, Orbs 2, Powers 1 |  |
| 307 | `PlayVfx(Vector2, string, Control?)` | strip | strip | 1 | Cards 1 |  |
| 319 | `PlayNonCombatVfx(Node, Vector2, string)` | strip | strip | 3 | Events 3 |  |

**SfxCmd** (`Commands/SfxCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 14 | `Play(string, float)` | strip | strip | 129 | Cards 8, Models(base) 3, Monsters 108, Powers 10 |  |
| 22 | `Play(string, string, float, float)` | strip | strip | 129 | Cards 8, Models(base) 3, Monsters 108, Powers 10 |  |
| 33 | `PlayLoop(string, bool)` | strip | strip | 8 | Monsters 8 |  |
| 41 | `PlayLoop(Creature, string)` | strip | strip | 8 | Monsters 8 |  |
| 49 | `PlayLoop(Creature, string, string, float)` | strip | strip | 8 | Monsters 8 |  |
| 57 | `StopLoop(string)` | strip | strip | 13 | Monsters 11, Powers 2 |  |
| 65 | `StopLoop(Creature, string)` | strip | strip | 13 | Monsters 11, Powers 2 |  |
| 73 | `SetParam(string, string, float)` | strip | strip | 6 | Monsters 6 |  |
| 81 | `PlayDamage(MonsterModel?, int)` | strip | strip | 0 |  |  |
| 93 | `PlayDeath(MonsterModel?)` | strip | strip | 0 |  |  |
| 101 | `PlayDeath(Player)` | strip | strip | 0 |  |  |
| 109 | `PlayCardSwooshSfx(CardPile, CardPile?)` | strip | strip | 0 |  |  |

**TalkCmd** (`Commands/TalkCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 15 | `Play(LocString, Creature, VfxColor, VfxDuration)` | strip | strip | 17 | Events 1, Monsters 15, Relics 1 |  |

**ThinkCmd** (`Commands/ThinkCmd.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 15 | `Play(LocString, Creature, double)` | strip | strip | 2 | Monsters 1, Powers 1 |  |

**AttackCommand** (`Commands.Builders/AttackCommand.cs`)

| L | Method | Status | Scope | Uses (models) | By kind | Notes |
|---:|---|:-:|:-:|--:|---|---|
| 738 | `FromCard(CardModel, CardPlay?)` | ported | combat | 190 | Cards 190 |  |
| 765 | `FromOsty(Creature, CardModel, CardPlay?)` | missing | combat | 19 | Cards 19 | Osty as attacker with card source |
| 781 | `FromMonster(MonsterModel)` | ported | combat | 209 | Monsters 209 |  |
| 794 | `Targeting(Creature)` | ported | combat | 163 | Cards 163 |  |
| 811 | `TargetingAllOpponents(ICombatState combatState)` | ported | combat | 28 | Cards 28 |  |
| 833 | `TargetingRandomOpponents(ICombatState combatState, bool)` | ported | combat | 7 | Cards 7 |  |
| 856 | `Unpowered()` | ported | unused | 0 |  |  |
| 862 | `WithAttackerAnim(string?, float, Creature?)` | strip | strip | 215 | Cards 22, Monsters 192, Powers 1 |  |
| 875 | `WithNoAttackerAnim()` | strip | strip | 6 | Cards 2, Monsters 4 |  |
| 881 | `AfterAttackerAnim(Func<Task>)` | strip | strip | 4 | Monsters 4 |  |
| 887 | `WithAttackerFx(string?, string?, string?)` | strip | strip | 185 | Cards 4, Monsters 181 |  |
| 895 | `WithAttackerFx(Func<Node2D?>)` | strip | strip | 185 | Cards 4, Monsters 181 |  |
| 901 | `WithWaitBeforeHit(float, float)` | strip | strip | 4 | Monsters 4 |  |
| 908 | `WithHitFx(string?, string?, string?)` | strip | strip | 368 | Cards 179, Monsters 188, Powers 1 |  |
| 916 | `SpawningHitVfxOnEachCreature()` | strip | strip | 6 | Cards 4, Monsters 1, Powers 1 |  |
| 922 | `WithHitVfxSpawnedAtBase()` | strip | strip | 16 | Cards 11, Monsters 4, Powers 1 |  |
| 928 | `WithHitVfxNode(Func<Creature, Node2D?>)` | strip | strip | 31 | Cards 15, Monsters 16 |  |
| 934 | `OnlyPlayAnimOnce()` | strip | strip | 47 | Cards 1, Monsters 46 |  |
| 940 | `WithHitCount(int)` | ported | combat | 90 | Cards 38, Monsters 52 |  |
| 946 | `BeforeDamage(Func<Task>)` | ported | combat | 8 | Cards 6, Monsters 2 |  |
| 961 | `Execute(PlayerChoiceContext?)` | ported | combat | 405 | Cards 196, Monsters 209 | ported (choiceContext dropped) |
| 1150 | `IncrementHitsInternal()` | ported | combat | 0 |  |  |
| 1155 | `AddResultsInternal(IEnumerable<DamageResult>)` | ported | combat | 0 |  |  |

## 4. Player choices

### 4.1 How the game asks and waits

Every in-combat prompt goes through `CardSelectCmd` (`Commands/CardSelectCmd.cs:3084-3855`).
`RelicSelectCmd.FromChooseARelicScreen` exists but has no caller. Targets of cards and potions are not
prompts: they are part of the `PlayCardAction`/`UsePotionAction` (the decision API already carries them).

Each `CardSelectCmd.From*` method follows the same skeleton (UI path, `Selector == null`):

1. early-outs: `IsEnding` (`FromCombatPile`, `FromSimpleGrid`, `FromSimpleGridForRewards`,
   `FromChooseABundleScreen`) or `IsOverOrEnding` (`FromHand`, `FromHandForUpgrade`) → empty;
   `FromChooseACardScreen`: more than 3 cards throws, 0 cards → `ReportSoftlock`, `null` (`:3173-3180`);
2. `UndoEndTurnIfNecessary` (multiplayer ready-state; no-op for the sim);
3. `PlayerChoiceSynchronizer.ReserveChoiceId` and **`context.SignalPlayerChoiceBegun(player, options)`**
   (`:3191`, `:3254`, `:3309`, `:3378`, `:3685`, `:3761`). For `FromHand`, `FromSimpleGrid`,
   `FromCombatPile`, `FromHandForUpgrade` this happens **before** the options are counted;
4. options and the automatic rules: no option → empty; `!RequireManualConfirmation && n <= MinSelect`
   → all options (`:3316`, `:3397`, `:3693`); `FromHandForUpgrade` with `n <= 1` → that card or null (`:3765-3768`);
   `RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect` unless set explicitly
   (`CardSelection/CardSelectorPrefs.cs:114`);
5. the screen (`NCombatPileCardSelectScreen`, `NPlayerHand.SelectCards`, `NChooseACardSelectionScreen`,
   `NSimpleCardSelectScreen`), then `PlayerChoiceSynchronizer.SyncLocalChoice(player, choiceId, result)`
   — the hook the recorder patches — and `context.SignalPlayerChoiceEnded()`.

With a selector installed (`CardSelectCmd.Selector`, a static stack, `:3084-3150`) steps 2-3 and 5
are skipped and `ICardSelector.GetSelectedCards(options, min, max)` answers synchronously; the draw
pile is then sorted by rarity and `ModelId` first (`:3405`). Selectors are the game's official test
seam (`TestSupport/ICardSelector.cs`; `TestCardSelector` answers from queued picks and returns an
empty selection when nothing is queued, `TestSupport/TestCardSelector.cs`), AutoSlay's bot
(`AutoSlay/AutoSlayer.cs:1919-1920`) and one content effect: `WhisperingEarring` pushes
`VakuuCardSelector` (take the first `MaxSelect` options) for its turn-1 auto-played turn
(`Models.Relics/WhisperingEarring.cs:83-140`, `Models.Relics/VakuuCardSelector.cs:14`).

UI semantics the sim must copy. Hand selection (`Nodes.Combat/NPlayerHand.cs`): the result is in click
order (`:1173`, `:1257-1260`); the player confirms, and confirm is enabled iff `MinSelect <= picked <=
MaxSelect` (`:1270-1281`); picking beyond `MaxSelect` replaces the last pick (`:1167-1177`); a hand with
no selectable card and nothing picked returns empty (`:1247-1250`). Grid screens
(`Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:322-364`, `NSimpleCardSelectScreen.cs:319-354`):
a click toggles a card; without manual confirmation the selection completes at `MaxSelect` picks (the
combat-pile screen clips to the shown count); with it, confirm is enabled at `MinSelect` picks (clipped
to the shown count on the combat-pile screen only). The draw-pile screen sorts by rarity then localized
title (`NCombatPileCardSelectScreen.cs:411-432`); the choose-a-card screen lays cards out in list order
(`NChooseACardSelectionScreen.cs:280-284`) and completes on the first click.

### 4.2 Choice contexts and what `SignalPlayerChoiceBegun` does

| Context | Created at | `SignalPlayerChoiceBegun` | Effect in single player |
| --- | --- | --- | --- |
| `GameActionPlayerChoiceContext` | `PlayCardAction` (`GameActions/PlayCardAction.cs:166-167`), `UsePotionAction` (`GameActions/UsePotionAction.cs:121`) | `ActionQueueSet.PauseActionForPlayerChoice(action)` (`GameActions.Multiplayer/GameActionPlayerChoiceContext.cs:120`) | the action pauses in place; nothing else runs |
| `BlockingPlayerChoiceContext` | `KnowledgeDemon.ChooseCurse` (`Models.Monsters/KnowledgeDemon.cs:124`), relic `AfterObtained`, event grids, AutoSlay; also the fallback of `AttackCommand.Execute(null)` for its damage and `AfterAttack` (`Commands.Builders/AttackCommand.cs:1132`, `:1146`), so a listener prompting in reaction to an enemy attack would open a decision during the enemy turn | no-op (`BlockingPlayerChoiceContext.cs:11-13`) | the awaiting code blocks; nothing else runs |
| `ThrowingPlayerChoiceContext` | commands issued by monster code (96 monster files) | throws `NotImplementedException` (`ThrowingPlayerChoiceContext.cs`) | a prompt there is impossible; the sim asserts |
| `BranchingPlayerChoiceContext` | around `CardModel.OnPlay` (`Models/CardModel.cs:667-669`), `PotionModel.OnUse` (`Models/PotionModel.cs:180`), `DrawWithoutBlockingOnOtherPlayers` | same chooser as owner → the original context (`BranchingPlayerChoiceContext.cs:140-171`) | transparent |
| `HookPlayerChoiceContext(player, CombatPlayPhaseOnly)` at turn start | `SetupPlayerTurn` (`Combat/CombatManager.cs:5059-5061`), then reused for `RunAutoPrePlayPhase` (`:5204-5205`) | first pause: generate a hook action, release `WaitForPauseOrCompletionWithoutAssigningTask`, wait for task assignment, enqueue the hook action, release `AssignTaskAndWaitForPauseOrCompletion`, wait until the hook action starts (deferred to the play phase), pause it (`GameActions.Multiplayer/HookPlayerChoiceContext.cs:225-274`, `:636-659`) | **reordering** (§4.6) |
| `HookPlayerChoiceContext` at end of turn | `AfterAutoPostPlayPhaseEntered` (`Combat/CombatManager.cs:2387-2389`) | as above | `WaitForCompletion` follows immediately (`:2435`) → inline |
| `HookPlayerChoiceContext` per listener | the six dispatchers of §2.2 | as above | a paused listener's remainder runs after the rest of the pass; **no v0.111.0 prompt site is reachable there** |

### 4.3 Prompt catalogue

In-combat prompt types (v0.111.0):

| Type | Game entry | Options (canonical order) | Selection | Recorder `choice_type` | How AutoSlay answers |
| --- | --- | --- | --- | --- | --- |
| HandSelect | `FromHand`, `FromHandForDiscard` (`:3668-3742`) | hand cards passing the filter, hand order | ordered subset, `MinSelect..MaxSelect` | CombatCard | `AutoSlayCardSelector`: shuffle with AutoSlay's own `Rng(GetDeterministicHashCode(seed))`, take `min(max, n)`, raised to `min(min, n)` when that is below `MinSelect` (`AutoSlay.Helpers/AutoSlayCardSelector.cs:22-38`) |
| HandUpgrade | `FromHandForUpgrade` (`:3744-3796`) | upgradable hand cards | exactly 1; automatic when `n <= 1` | CombatCard | selector, 1 random |
| PileSelect | `FromCombatPile` (`:3353-3439`) | Draw: sorted by (rarity, id); Discard: pile order; filter | ordered subset | CombatCard | selector, random subset of the maximum size |
| ChooseCard | `FromChooseACardScreen` (`:3169-3234`) | 1-3 generated cards (curses for KnowledgeDemon), generation order | pick 1, or skip when `canSkip` | Index | selector (first of shuffled); screen handler random |
| SimpleGrid | `FromSimpleGrid` (`:3295-3351`) | generated cards (`ChoicesParadox`) | `MinSelect..MaxSelect` | Index | selector random |

Effect families by the game's prompt keys (`CardSelection/CardSelectorPrefs.cs`): discard
(`DiscardSelectionPrompt`: Acrobatics, DaggerThrow, HiddenDaggers, Prepared, Survivor,
ToolsOfTheTradePower), exhaust (`ExhaustSelectionPrompt`: Brand, BurningPact, Cleanse, Scavenge, TrueGrit,
TyrannyPower, ToastyMittens), transform (`TransformSelectionPrompt`: Begone, Charge, Seance,
EntropyPower), upgrade (`FromHandForUpgrade`: Armaments); every other site uses its own
`SelectionScreenPrompt` (duplicate, put back on the draw pile, fetch, play, …; see each model).
"Any number" is `CardSelectorPrefs(prompt, 0, 999999999)` (Guards, Ashwater, GamblersBrew, GamblingChip);
`(0, N)` sites are NeowsFury and Purity. `Cancelable` stays false at every in-combat site, so the only way to
decline is a 0-pick confirm on a `MinSelect = 0` prompt or the skip of a `canSkip` choose-a-card screen.

There is no orb, scry, retain or stance prompt in v0.111.0; retain is a keyword and orbs are evoked by
position. Run-layer prompts (deck upgrade/transform/enchant/remove/generic, reward grids, bundles,
card rewards with alternatives, rewards screen, events, rest site, shop, map, treasure, Crystal Sphere)
reuse the same mechanism outside combat; AutoSlay answers their screens with `random.NextItem` over the
holders and confirms (`AutoSlay.Handlers.Screens/*.cs`), takes a random card reward and never skips.
AutoSlay plays combat by `CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, randomTarget)` on
random playable cards and uses every potion at turn start (`AutoSlay.Handlers.Rooms/CombatRoomHandler.cs`),
so its runs exercise the selector path only.

Character-specific prompts: Regent — Begone, Charge, CosmicIndifference, DecisionsDecisions, Glimmer,
Guards, HeirloomHammer, PhotonCut, Quasar, Tutor; Necrobinder — Cleanse, Dredge, Graveblast,
SculptingStrike, Seance, Snap, Transfigure; Silent — Acrobatics, DaggerThrow, HandTrick, HiddenDaggers,
Nightmare, Prepared, Survivor; Ironclad — Armaments, Brand, BurningPact, Headbutt, TrueGrit; Defect —
Hologram, Scavenge; colorless/event — Abundance, Discovery, DualWield, NeowsFury, Purity,
SecretTechnique, SecretWeapon, SeekerStrike, Splash, ThinkingAhead, Wish.

All 61 in-combat sites (relic `AfterObtained` deck prompts are run layer and listed in the JSON only):

| Kind | Model | Site (hook) | CardSelectCmd | Pile/options | min | max | Skip | Filter | Category |
|---|---|---|---|---|---|---|:-:|---|---|
| Cards | Abundance | OnPlay | FromChooseACardScreen | generated |  |  |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Acrobatics | OnPlay | FromHandForDiscard | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Armaments | OnPlay | FromHandForUpgrade | Hand |  |  |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Begone | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Brand | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | BurningPact | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Charge | OnPlay | FromCombatPile | Draw | Cards | Cards |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Cleanse | OnPlay | FromCombatPile | Draw | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | CosmicIndifference | OnPlay | FromCombatPile | Discard | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | DaggerThrow | OnPlay | FromHandForDiscard | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | DecisionsDecisions | OnPlay | FromHand | Hand | 1 | 1 |  | c.Type == CardType.Skill && !c.Keywords.Contai | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Discovery | OnPlay | FromChooseACardScreen | generated |  |  | Y |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Dredge | OnPlay | FromCombatPile | Discard | num2 | num2 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | DualWield | OnPlay | FromHand | Hand | 1 | 1 |  | CardType type = c.Type; return (type == CardTy | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Glimmer | OnPlay | FromHand | Hand | PutBack | PutBack |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Graveblast | OnPlay | FromCombatPile | Discard | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Guards | OnPlay | FromHand | Hand | 0 | ∞ |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | HandTrick | OnPlay | FromHand | Hand | 1 | 1 |  | card.Type == CardType.Skill && !card.IsSlyThis | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Headbutt | OnPlay | FromCombatPile | Discard | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | HeirloomHammer | OnPlay | FromHand | Hand | 1 | 1 |  | c.VisualCardPool.IsColorless, source: heirloom | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | HiddenDaggers | OnPlay | FromHandForDiscard | Hand | Cards | Cards |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Hologram | OnPlay | FromCombatPile | Discard | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | NeowsFury | OnPlay | FromCombatPile | Discard | 0 | num2 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Nightmare | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | PhotonCut | OnPlay | FromHand | Hand | PutBack | PutBack |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Prepared | OnPlay | FromHandForDiscard | Hand | cardCount | cardCount |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Purity | OnPlay | FromHand | Hand | 0 | Cards |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Quasar | OnPlay | FromChooseACardScreen | generated |  |  | Y |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Scavenge | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | SculptingStrike | OnPlay | FromHand | Hand | 1 | 1 |  | !c.GetKeywordsWithSources(KeywordSources.Local | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Seance | OnPlay | FromCombatPile | Draw | Cards | Cards |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | SecretTechnique | OnPlay | FromCombatPile | Draw | 1 | 1 |  | c.Type == CardType.Skill | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | SecretWeapon | OnPlay | FromCombatPile | Draw | 1 | 1 |  | c.Type == CardType.Attack | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | SeekerStrike | OnPlay | FromCombatPile | Draw | 1 | 1 |  | Enumerable.Contains<CardModel>(_003C_003E8__1. | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Snap | OnPlay | FromHand | Hand | 1 | 1 |  | !c.Keywords.Contains(CardKeyword.Retain), sour | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Splash | OnPlay | FromChooseACardScreen | generated |  |  | Y |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Survivor | OnPlay | FromHandForDiscard | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | ThinkingAhead | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Transfigure | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | TrueGrit | OnPlay | FromHand | Hand | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Tutor | OnPlay | FromCombatPile | Draw | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Cards | Wish | OnPlay | FromCombatPile | Draw | 1 | 1 |  |  | inline (PlayCardAction ctx; caller ctx when auto-played) |
| Monsters | KnowledgeDemon | ChooseCurse | FromChooseACardScreen | generated |  |  |  |  | inline (BlockingPlayerChoiceContext) |
| Potions | Ashwater | OnUse | FromHand | Hand | 0 | ∞ |  |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | AttackPotion | OnUse | FromChooseACardScreen | generated |  |  | Y |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | ColorlessPotion | OnUse | FromChooseACardScreen | generated |  |  | Y |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | DropletOfPrecognition | OnUse | FromCombatPile | Draw | 1 | 1 |  |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | GamblersBrew | OnUse | FromHandForDiscard | Hand | 0 | ∞ |  |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | LiquidMemories | OnUse | FromCombatPile | Discard | 1 | 1 |  |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | PowerPotion | OnUse | FromChooseACardScreen | generated |  |  | Y |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | SkillPotion | OnUse | FromChooseACardScreen | generated |  |  | Y |  | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Potions | TouchOfInsanity | OnUse | FromHand | Hand | 1 | 1 |  | c.CostsEnergyOrStars(includeGlobalModifiers: f | inline (UsePotionAction GameActionPlayerChoiceContext) |
| Powers | EntropyPower | AfterPlayerTurnStart | FromHand | Hand | Amount | Amount |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Powers | ForegoneConclusionPower | BeforeHandDraw | FromCombatPile | Draw | Amount | Amount |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Powers | StratagemPower | AfterShuffle | FromCombatPile | Draw | Amount | Amount |  |  | caller ctx: inline in card play, turn-setup deferral when the shuffle is in the turn-start draw |
| Powers | ToolsOfTheTradePower | AfterPlayerTurnStart | FromHandForDiscard | Hand | Amount | Amount |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Powers | TyrannyPower | AfterPlayerTurnStart | FromHand | Hand | Amount | Amount |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Relics | ChoicesParadox | AfterPlayerTurnStart | FromSimpleGrid | generated | 1 | 1 |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Relics | GamblingChip | AfterPlayerTurnStart | FromHandForDiscard | Hand | 0 | ∞ |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Relics | ToastyMittens | AfterPlayerTurnStart | FromHand | Hand | 1 | 1 |  |  | turn-setup deferral (SetupPlayerTurn ctx) |
| Relics | Toolbox | BeforeHandDraw | FromChooseACardScreen | generated |  |  |  |  | turn-setup deferral (SetupPlayerTurn ctx) |

### 4.4 Candidates

| | (a) answer-tape re-execution | (b) callback policy in `Step` | (c) suspendable state machine per site |
| --- | --- | --- | --- |
| Prompt sites in ported code | one synchronous call, like the game's `await CardSelectCmd.X` | same | every enclosing frame becomes data: `OnPlayWrapper` multi-play loop, auto-play chains, hook dispatch loops with their snapshots, `SetupPlayerTurn`, enemy-turn loop, `Kill` cascades |
| Prompt is a search node | yes (`LegalActions`, `Step`, `Clone`, `StateHash`) | no; branching needs one full action replay per answer, i.e. (a) done by the caller | yes |
| Determinism | by construction (all state and RNG in `CombatState`, re-run from a clone) | yes | yes, if every continuation is cloned exactly |
| Cost | one `DeepClone` per decision step (≈2.5 µs) plus one replay of the action per completed prompt; an exception only when a prompt is exposed | none | none at runtime |
| Porting risk | low; the only extra rule is the pause emulation (§4.6), which every option needs | low | very high: hand-compiling the game's async state machines for ~60 sites in 5 nesting contexts; the C#-async trick cannot clone compiler state machines |
| Fit for the diff harness and for Python/LLM agents | recorded answers are decisions in the stream | needs a synchronous callback into the agent | yes |

**Recommendation: (a), implemented over a (b)-style seam.** All prompt sites call one runtime; the
runtime asks an `IChoiceAnswerer`. The tape answerer turns prompts into decisions (the public
`Step(action)` API); a policy answerer gives fast rollouts with no clone and no exception
(`Step(action, answerer)`); the content selector stack (`VakuuCardSelector`) and the diff harness's
recorded answers are further answerers. (c) is rejected for cost and risk.

### 4.5 Design of the sim choice API

**Decision kinds** (`Api/GameAction.cs`, append only, ordinals of existing kinds unchanged):

```csharp
public enum ActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, ChoosePick, ChooseConfirm }
// GameAction(Kind, CardIndex, TargetIndex)
//   UsePotion:     CardIndex = potion slot; TargetIndex >= 0 enemy index (State.Monsters), -1 none/self,
//                  <= -2 ally Allies[-2 - TargetIndex] (0 = the player, then pets)
//   DiscardPotion: CardIndex = slot
//   ChoosePick:    CardIndex = option index
//   ChooseConfirm: no indices (finish a multi-select, or skip)
// static GameAction.UsePotion(slot, target = NoIndex), DiscardPotion(slot), Pick(option), Confirm
```

**Prompt state** (new namespace `Sts2Sim.Core.Combat.Choice`):

```csharp
public enum ChoicePromptKind : byte { HandSelect, HandUpgrade, PileSelect, ChooseCard, SimpleGrid }
public enum ChoiceResultType : byte { CombatCard, Index }          // recorder PlayerChoiceType
public readonly record struct ChoiceOption(int CardInstanceId, string CardId, int UpgradeLevel);
public sealed record ChoicePrompt(ChoicePromptKind Kind, string SiteId, string SourceId, int SourceInstanceId,
    PileType? Pile, IReadOnlyList<ChoiceOption> Options, int MinSelect, int MaxSelect,
    bool RequireConfirm, bool CanSkip, bool Ordered, ChoiceResultType ResultType);
public readonly record struct ChoiceAnswer(ImmutableArray<int> Picks);    // option indices, pick order
public interface IChoiceAnswerer { ChoiceAnswer Answer(CombatState cs, ChoicePrompt prompt); }

// CombatSim additions
ChoicePrompt? PendingChoice { get; }      IReadOnlyList<int> PendingPicks { get; }
void Step(GameAction action, IChoiceAnswerer answerer);   // fast path: prompts answered inline
```

`SiteId` = `<model id>:<method>` (e.g. `ACROBATICS:OnPlay`, `GAMBLING_CHIP:AfterPlayerTurnStart`),
stable across runs; used for logs, the diff mapping and the per-site `Ordered` flag. `MinSelect` and
`MaxSelect` are clipped to the option count. `CanSkip` only for `ChooseCard(canSkip: true)`.

**Option order** = what agents and the harness index: hand order (HandSelect, HandUpgrade); draw pile
sorted by (rarity, `ModelId`) — the selector branch's order, which also hides the draw order from
agents; discard pile order; generation order (ChooseCard, SimpleGrid).

**Legality at a prompt** (`LegalActions` order: picks by ascending option index, then confirm):

- only `ChoosePick`/`ChooseConfirm` are legal; potions, plays and end turn are not (the game's
  selection UI is modal; hand selections also cancel queued play-card actions,
  `PlayerChoiceOptions.CancelPlayCardActions`, `Commands/CardSelectCmd.cs:3685`);
- `Pick(i)`: `0 <= i < n`, `i` not picked, picks `< MaxSelect`; when `Ordered` is false only `i` greater than
  the last pick is listed (one path per subset), and `Step` accepts any order but sorts the answer before
  it enters the tape, so transpositions hash equal;
- the prompt completes automatically when `!RequireConfirm && picks == MaxSelect`, or on `Confirm`,
  which is legal iff `(RequireConfirm || CanSkip) && MinSelect <= picks <= MaxSelect`. Without manual
  confirmation `MinSelect == MaxSelect`, so the hand screen's mandatory confirm press can only accept
  exactly that count: completing automatically yields the same results with one decision less;
- a prompt is exposed iff the game opens UI: at least one option and no automatic rule; a ChooseCard with
  one card and no skip is still exposed (forced move), to keep the decision stream aligned with recordings.

`Ordered` defaults to true. A site may declare it false only with a spec note showing that result order
cannot matter; the PutBack sites (Glimmer, PhotonCut) stay ordered because they set the draw order.

**Engine.** `CombatState` never stores the tape. At a decision point `Step(a)` keeps the current state
`S0` untouched, runs `a` on `work = S0.DeepClone()` with a tape answerer over an empty tape, and:
completes → `State = work`; or meets a prompt with no taped answer → the runtime first performs the pause
semantics of §4.6, then throws `ChoicePendingException(prompt)`; `Step` catches it and stores
`(S0, a, tape, prompt, picks = [], observation = work)`. Each completing `Pick`/`Confirm` appends the
answer and re-runs `a` from a fresh `S0.DeepClone()`; the result is again a completed state or the next
prompt. `CombatSim.Create` can itself stop at a turn-1 prompt (Toolbox in `BeforeHandDraw`,
GamblingChip in `AfterPlayerTurnStart`, …): there `S0` is the post-`SetUpCombat` state and the action is
an internal `Begin`.

- `State` at a prompt = the observation (partial state up to the prompt, including deferred siblings);
  read-only by contract.
- `StateHash` at a prompt = hash of `S0`, the action, the tape and the picks; the observation alone is
  not enough (two actions can reach identical observations with different continuations).
- `Clone` at a prompt copies `S0`, tape, picks and prompt; the observation is cloned or regenerated.
- A debug invariant checks on every re-run that the k-th prompt equals the prompt recorded with tape
  entry k (kind, `SiteId`, option ids).
- Prompt inside the enemy turn (KnowledgeDemon `ChooseCurse`, Inline context): exposed while
  `CurrentSide == Enemy`; the answer re-runs `EndTurn` from `S0` through the rest of the enemy turn and
  the next player turn start (which may expose further prompts).

### 4.6 Pause emulation (the UI path's ordering)

The sim emulates `SignalPlayerChoiceBegun` per context kind. Contexts live on an ambient stack in
`cs.Choice` (transient, not hashed), pushed where the game constructs a context; signatures stay without
`PlayerChoiceContext`. This is equivalent because the game passes contexts lexically down the await chain.

- `Inline` (`GameActionPlayerChoiceContext`, `BlockingPlayerChoiceContext`, the end-of-turn hook
  context): no effect.
- `TurnSetup` (StartTurn's `HookPlayerChoiceContext`): on the first pause while `SetupPlayerTurn` runs,
  execute the steps the game runs before the answer — `Hook.AfterSideTurnStart(Player, participants)`
  (`Combat/CombatManager.cs:5080`), each player's `OrbQueue.AfterTurnStart` with its own context
  (`:5118-5120`), the dead-player auto-ready check (`:5143-5160`), the assignment of
  `RunAutoPrePlayPhase` (no rule effect; it first awaits the setup task, `:3863`), and `CheckWinCondition`
  (`:5226`) — with the context stack reset to the turn-loop root;
  then answer and continue `SetupPlayerTurn`; `StartTurn` later skips the steps already done, runs the
  body of `RunAutoPrePlayPhase` (`Phase = AutoPrePlay`, `CheckForEmptyHand`,
  `AfterAutoPrePlayPhaseEntered`, `Phase = Play`, `:3902-3914`) and does not repeat the win check. A first
  pause inside `RunAutoPrePlayPhase` (auto-played prompting cards from Mayhem, HistoryCourse, Imbued…)
  runs only `CheckWinCondition` first. If that deferred win check ends the combat, unwind (the game
  cancels the queued `CombatPlayPhaseOnly` hook action at combat end — open question) and leave the
  terminal state.
- The pause happens even when no decision is exposed: `FromHand`, `FromSimpleGrid`, `FromCombatPile`
  and `FromHandForUpgrade` signal before counting options, so an empty or automatic selection inside
  `SetupPlayerTurn` (e.g. `ToolsOfTheTradePower` with no more cards than its amount) still moves
  `AfterSideTurnStart` and the orb triggers ahead of the rest of the setup. `FromChooseACardScreen` with
  no card returns before signalling.
- `PerListener`: unreachable with v0.111.0 content (no prompt site and no auto-play in those six hooks);
  throw `NotSupportedException` so a future content port cannot silently get the order wrong.
- An installed selector (VakuuCardSelector) suppresses the pause, as in the game.

Turn-setup prompt sites: `BeforeHandDraw` (Toolbox; ForegoneConclusionPower), the turn-start draw's
`AfterShuffle` (StratagemPower), `AfterPlayerTurnStart` (EntropyPower, ToolsOfTheTradePower,
TyrannyPower, ChoicesParadox, GamblingChip, ToastyMittens). With Toolbox the pause precedes the hand
draw, so `AfterSideTurnStart` listeners run before the opening draw.

### 4.7 Diff harness mapping

- Recorder: `player_choice` from the `PlayerChoiceSynchronizer.SyncLocalChoice` prefix
  (`mod/src/HumanFunnels.cs:662-680`, decoding `:691-735`): `choice_type` Index (`indexes`, `-1` = skip)
  or CombatCard (`cards` = model ids in result order); `context_model` is sticky and unusable
  (`mod/src/HumanFunnels.cs:747-757`, `sim/specs/review-fixes-2026-10-30.md` item 1). Selection screens
  are snapshotted as `state_type` `hand_select` / `card_select` (`mod/src/StateBuilder/PassiveStateBuilder.cs:98-104`, `:170-177`;
  `PassiveStateBuilder.Selections.cs`, `PassiveStateBuilder.Combat.cs:113-160`).
- Bundle v2 (`sim/specs/diff-bundle.md` successor): a decision `{"type": "choose", "choice_id", "choice_type",
  "cards": [{"id", "upgraded"}] | "indexes": [..]}` at its position in the action stream (turn-start
  choices follow `end_turn`); its `pre_state` is the selection-screen snapshot, compared against the
  sim observation; the screen's option ids are a soft field against `PendingChoice.Options`.
- Sim side: CombatCard → for each recorded card in order, `Pick` of the first not-yet-picked option with
  equal `(CardId, upgraded)`; equal options that differ in any canonical field (cost, keywords,
  enchantment) → take the first and flag soft `choice_ambiguous`. Index → `Pick(index)`; skip → `Confirm`
  with no pick. After the picks, `Confirm` if the prompt requires it.
- Automatic or empty selections produce no `player_choice` (no `SyncLocalChoice` call) and no sim
  decision, so the streams stay aligned. New ineligibility codes: `choice_unmappable`,
  `choice_prompt_mismatch`.
- Only human UI-path recordings are valid oracles for prompt ordering; TestMode and AutoSlay take the
  selector path (no pause, different draw-pile order).

## 5. New subsystems in Core

General pattern for every new runtime kind (as `PowerModel` today): one class per content file, the
runtime object is the `AbstractModel` listener, immutable per-type data in `const`/`static readonly`,
per-instance mutable state in fields cloned by `MemberwiseClone` + `CopyMutableReferenceState`, written
by `WriteCanonical` + `WriteExtraCanonical`; cross-references as ids (creature `CombatId`, card
`InstanceId`), never object references (the game's `HashSet<CardModel>` internal data, e.g.
`Models.Powers/HellraiserPower.cs:21-28`, becomes a set of instance ids). Registries are built by
reflection like `CardRegistry` (orchestrator adds `RelicRegistry`, `PotionRegistry`, `OrbRegistry`,
`EnchantmentRegistry`, `AfflictionRegistry`, `ModifierRegistry` to the frozen `Registry/Registries.cs`).

| Subsystem | Game facts | Sim design |
| --- | --- | --- |
| Relics | `Models/RelicModel.cs`: 22 virtuals (`AfterObtained` 84, `IsUsedUp` 10, `DisplayAmount` 36, `IsStackable` 2), `IsMelted` (`:267`), `Status` (`:333`), `StackCount`, `HasBeenRemovedFromState`; `Player.Relics` (`Entities.Players/Player.cs:241`); listeners after player powers, melted ones skipped. 177 relic files override combat hooks; 73 have per-instance fields | `Relics/RelicModel.cs : AbstractModel`; `PlayerState.Relics : List<RelicModel>`; `RelicCmd` (obtain/remove/replace/melt) for the few combat paths; run-start relic effects stay in `Create` inputs until the run layer exists |
| Potions | `Models/PotionModel.cs`: `OnUse` (64), `Usage` (`Entities.Potions/PotionUsage.cs`: None, CombatOnly, AnyTime, Automatic), `TargetType`, `PassesCustomUsabilityCheck`, `IsValidTarget` (`:618`), `OnUseWrapper` (BeforePotionUsed, OnUse with a branching context, History.PotionUsed, AfterPotionUsed, CheckForEmptyHand); `UsePotionAction` is `CombatPlayPhaseOnly` in combat (`GameActions/UsePotionAction.cs`); slots `Player.PotionSlots` with nulls (`Player.cs:142-245`); `PotionCmd.TryToProcure`/`Discard`; `FairyInABottle` is a `ShouldDie`/`AfterPreventingDeath` listener | `Potions/PotionModel.cs`; `PlayerState.PotionSlots : PotionModel?[]` (+ capacity); `Combat/Loop/PotionActions.cs` executes `UsePotion`/`DiscardPotion` decisions; `Rng.CombatPotionGeneration` for in-combat procurement |
| Orbs (Defect) | `Entities.Orbs/OrbQueue.cs`: `Capacity` (max 10, `:386`), `TryEnqueue` (`:429`), `Insert`, `Remove`, `BeforeTurnEnd`/`AfterTurnStart` triggers (`:463`, `:477`); `Models/OrbModel.cs`: `Passive`, `Evoke`, `BeforeTurnEndOrbTrigger`, `AfterTurnStartOrbTrigger`, values through `Hook.ModifyOrbValue`; `Commands/OrbCmd.cs`: `Channel` evokes the front orb when full, `AddSlots`/`RemoveSlots`, `EvokeNext`/`EvokeLast`, `Passive` with `ModifyOrbPassiveTriggerCount`; capacity starts at `BaseOrbSlotCount` (`Entities.Players/PlayerCombatState.cs:276`) | `Orbs/OrbModel.cs`, `Orbs/OrbQueue.cs` (value list + capacity, cloned), `Combat/Pipeline/OrbCmd.cs`; orb listeners after potions; turn hooks wired by the loop at `DoTurnEnd` and `StartTurn` (`Combat/CombatManager.cs:5118-5120`) |
| Osty and pets (Necrobinder) | `OstyCmd.Summon` (`Commands/OstyCmd.cs:336-395`): `ModifySummonAmount`; alive → `GainMaxHp`; else revive (re-add pet) or create (`PlayerCmd.AddPet<Osty>` + `DieForYouPower`), `SetMaxHp`, `Heal`, `AfterOstyRevived`; `History.Summoned`; `AfterSummon`. Pets: `PlayerCombatState` pet list with death subscription (`AddPetInternal`, `OnPetDied`), `Creature.PetOwner` (`Entities.Creatures/Creature.cs:520-537`), `Player.Osty`/`IsOstyAlive` (`Player.cs:247-249`); damage: block taken from `PetOwner?.Creature ?? target` (`Commands/CreatureCmd.cs:3697`), `ModifyUnblockedDamageTarget`, HP-loss phases before/after Osty, redirected overkill (`:3716-3725`); `AttackCommand.FromOsty`, `OstyDamageVar`, `TargetType.Osty`; owner death kills pets (`Commands/CreatureCmd.cs:2704-2717`) | `CombatState.Allies` (player + pets, insertion order) used by listener order and `GetCreaturesOnSide(Player)`; `MonsterState` gets `PetOwnerId` and may sit on the player side; `PlayerState.PetIds`; `OstyCmd`, `Monsters/Defs/Osty.cs` |
| Stars (Regent) | `PlayerCombatState.Stars` (>= 0; the setter records `History.StarsModified`); `PlayerCmd.GainStars` (16 sources; `ShouldGainStars` gate, no override; `AfterStarsGained`: BlackHolePower), `LoseStars`, `SetStars` (0 on a multiplayer death); spending in `CardModel.SpendStars` (`AfterStarsSpent`: ChildOfTheStarsPower, GalacticDust, MiniRegent); star costs: `CanonicalStarCost` (22 cards), `HasStarCostX` (Stardust), temporary star costs, `Hook.ModifyStarCost` (VoidFormPower, BrilliantScarf), `ShouldPayExcessEnergyCostWithStars` (2 stars per missing energy; no override); resource check `HasEnoughResourcesFor`; X star cost captures the current stars (`CardCmd.AutoPlay`, `LastStarsSpent`); Radiate reads `StarsModifiedEntry` history; `StarsVar` | fields exist (`PlayerState.Stars`, `CardInstance` star costs, `EnergyCmd` spend); add `Combat/Pipeline/StarsCmd.cs` with the four hooks and the history entry; stars start at 0 each combat and persist across turns |
| Forge (Regent) | `ForgeCmd.Forge` (`Commands/ForgeCmd.cs:153-170`): no-op when over/ending; if the player owns no non-exhausted `SovereignBlade` (dupes excluded), create one in hand (`CreatedThroughForge`); add the amount to every blade including exhausted ones (`AddDamage`, `AfterForged`); `Hook.AfterForge`. `ForgeVar` | `Combat/Pipeline/ForgeCmd.cs`; `SovereignBlade` card def (own `AfterCardChangedPiles`) |
| Summons (monsters) | `CreatureCmd.Add<T>`/`Add` (`Commands/CreatureCmd.cs:3554-3586`), `CombatState.CreateCreature` (`Combat/CombatState.cs:709-724`), `SetEnemyIndex` for slotless encounters (`:966-980`); 6 monsters and 4 powers spawn | mid-combat path in `MonsterSpawner` (allocate id, HP roll, `SetUpForCombat`, `AfterCreatureAdded` order, `AfterCreatureAddedToCombat`) |
| Doom | `Models.Powers/DoomPower.cs`: at side turn end kills doomed creatures (`DoomKill` → `CreatureCmd.Kill` each → `Hook.AfterDiedToDoom`, per-listener context); `MonsterModel.ShouldDisappearFromDoom` (9), `OnDieToDoom` (TorchHeadAmalgam) | power content + dispatcher; monster hook members in `MonsterDef` |
| Enchantments | `Models/EnchantmentModel.cs` (19 virtuals): damage/block pre-pass in `ModifyDamage`/`ModifyBlock` (`Hooks/Hook.cs:10075-10080`, `:10332-10342`), `EnchantPlayCount` (replay count), `OnPlay` after the card's `OnPlay`, `ShouldStartAtBottomOfDrawPile` (turn 1, `Combat/CombatManager.cs:4248-4262`), `RecalculateValues`; listener right after its card; mostly applied by events/relics | `Cards/Enchantments/EnchantmentModel.cs`; `CardInstance.Enchantment` cloned with the card; deck cards carry it into combat |
| Afflictions | `Models/AfflictionModel.cs`; `CardCmd.Afflict` (`ShouldAfflict`, `History.CardAfflicted`), `ClearAffliction`; `OnPlay` after the card's `OnPlay`; listener between card and enchantment; applied by 11 powers | `Cards/Afflictions/AfflictionModel.cs`; `CardInstance.Affliction` |
| Run modifiers | `CombatState.Modifiers`; only Hoarder (`AfterCardChangedPiles`) and Murderous (`AfterCreatureAddedToCombat`) override combat hooks; custom/daily runs only | `CombatState.Modifiers : List<ModifierModel>`, empty by default |
| Multiplayer scaling | scaling model returns 1 for one player; power scaling gated by `Players.Count > 1` (`Commands/PowerCmd.cs:550`); monster HP scaling gated by `playerCount != 1` (`Entities.Creatures/Creature.cs:986-993`, `:1332-1339`) | stub as constants; no listener object |
| Card creation | `CombatState.CreateCard` = `ToMutable` + owner + `AfterCreated` (`Combat/CombatState.cs:671-677`); `CardFactory.GetDistinctForCombat` (`TakeRandom`) / `GetForCombat` (`NextItem`) over `FilterForCombat` (`CanBeGeneratedInCombat`) (`Factories/CardFactory.cs:93-121`); pools from `CardPoolModel.GetUnlockedCards(UnlockState, multiplayer constraint)` (`Models/CardPoolModel.cs:68-86`, 37 `MultiplayerOnly` cards removed in single player, epochs filter unlocks); `Runs/CardCreationFlags.cs`/`CardCreationOptions` are reward-side | `Cards/CardFactory.cs`; ordered pool data per pool (`GenerateAllCards` order); an `UnlockState` input (default: everything unlocked) |
| Temporary powers | `Models/ITemporaryPower.cs` (`OriginModel`, `InternallyAppliedPower`) on 5 powers; read by Misery, Rend, UnsettlingLamp | interface on the power classes, ids instead of references |
| DynamicVar subclasses | 26 types in `Localization.DynamicVars/` (files using `new X`: DamageVar 209, CardsVar 170, PowerVar 169, BlockVar 104, EnergyVar 82, CalculationBaseVar 43, StringVar 42, RepeatVar 30, GoldVar 27, CalculationExtraVar 24, HealVar 24, MaxHpVar 21, CalculatedDamageVar 19, ExtraDamageVar 19, HpLossVar 18, CalculatedVar 17, StarsVar 17, SummonVar 14, ForgeVar 13, OstyDamageVar 11, IntVar 8, CalculatedBlockVar 7); `CalculatedVar` = `CalculationBase + CalculationExtra × multiplier(card, target)` when the combat is in progress, multiplier must be static (`Localization.DynamicVars/CalculatedVar.cs`); props on Damage/Block vars feed the var overloads of `CreatureCmd` | `Cards/DynamicVars/*`; multiplier as `static Func<CombatState, CardInstance, CreatureState?, decimal>` on the def |
| Combat history | `Combat.History/CombatHistory.cs` (17 entry types stamped with round, side and player turn numbers; queries by type, actor, `HappenedThisTurn`, `HappenedLastPlayerTurn`); read by 29 cards, 12 powers, 8 relics | `State/CombatHistory.cs`: append-only log of value structs with ids; clones share the buffer (copy-on-write when a clone appends behind another clone's frontier); hashed |
| Side-turn participants | creatures starting the turn (`Combat/CombatManager.cs:4873-4882`) or ending it (`:2457`, `:2967`, `:2055-2088`) | snapshot lists passed to the 8 side-turn hooks (signature change) |
| Run-state effects inside combat | deck mutations through a card's `DeckVersion` (TheScythe, DeprecatedCard, GeneticAlgorithm, Goopy, ThievingHopper, SwipePower), `CardPileCmd.RemoveFromDeck` (DeprecatedCard.OnPlay, SwipePower), gold (`PlayerCmd.GainGold`/`LoseGold`: 3 cards, 1 power; `ModifyGoldGained`), max HP, relic/potion procurement, `AfterCombatEnd` (45 overriders) and `AfterCombatVictory` hooks at combat end | `CardInstance.DeckVersionId` link plus a hashed `RunEffects` log on `CombatState` (deck add/remove/var change, gold delta, max-HP delta, procured items) for the run layer to apply; `PlayerState.Gold` as an input; combat-end hooks run by the future run layer |
| Effect depth, extra turns | `BeginCardOrPotionEffect`/`EndCardOrPotionEffect` gate `CheckForEmptyHand` (`Combat/CombatManager.cs:6517-6531`, `:1430-1509`); `PlayersTakingExtraTurn` read by RampartPower (`Models.Powers/RampartPower.cs:54`) | counters/flags on `CombatState` (cloned, hashed) |

## 6. Ownership split for the kernel-expansion wave

Paths are under `sim/Sts2Sim.Core/` unless stated. File sets are disjoint; `partial` classes split a
facade across owners where the game has one static class (`CardCmd`).

| Agent | Owns (create or edit) | Compiles against (frozen in phase 0) | Est. LOC (code + tests) |
| --- | --- | --- | --- |
| choice | `Combat/Choice/*` (new: prompt types, contexts, runtime, `CardSelectCmd`, answerers, `ChoicePendingException`), `Combat/Loop/CombatSim.cs`, `Combat/Loop/LegalActionGenerator.cs`, `Sts2Sim.Tests/ChoiceTests.cs`, `sim/specs/choice.md` | `ActionKind`/`GameAction` additions; `CombatState.Choice`; `CombatLoop.Step`/`StartCombat`; `CardInstance`/`CardPile` read API | 1,600 |
| hooks | `Combat/Pipeline/AbstractModel.cs`, `Combat/Pipeline/Hook.cs`, `Combat/Pipeline/ListenerSnapshot.cs`, `Combat/Pipeline/HookMask.cs` (new), `Sts2Sim.Tests/HookTests.cs`, `sim/specs/hooks.md` | listener containers on `CombatState`/`PlayerState`/`CardInstance`/`MonsterState`; `ChoiceContext` push API | 3,200 |
| state | `State/*`, `State/CombatHistory.cs` and `State/RunEffects.cs` (new), `Sts2Sim.Tests/StateKernelTests.cs`, `sim/specs/state-kernel.md` | base stubs `RelicModel`, `PotionModel`, `OrbModel`, `EnchantmentModel`, `AfflictionModel`, `ModifierModel` | 1,300 |
| pipeline | `Combat/Pipeline/{CreatureCmd,PowerCmd,AttackCommand,EnergyCmd,CombatStatus,CreatureInternals,DamageResult}.cs`, `Powers/{PowerModel,MultiplierModifiers}.cs` | new dispatcher names; `CombatState.Allies`; `CombatHistory.Append*`; var types | 1,100 |
| cards-kernel | `Cards/{CardDef,CardInstance,CardEnergyCost,DynamicVar,TemporaryCardCost}.cs`, `Cards/DynamicVars/*`, `Cards/Enchantments/*`, `Cards/Afflictions/*`, `Cards/CardFactory.cs`, `Cards/Pools/*` (new), `Combat/Pipeline/CardCmd.cs` (new, partial: upgrade, downgrade, transform, enchant, afflict, keywords) | `Hook.ModifyKeywordsInCombat`; `CombatState.CreateCard` | 2,000 |
| loop | `Combat/Loop/{CombatLoop,ActionExecutor,CardPileCmd,PlayerCmd}.cs`, `Combat/Loop/CardCmd.Play.cs` (new, partial: `AutoPlay`, `Discard`, `DiscardAndDraw`, `Exhaust`), `Combat/Loop/TurnFlow*.cs` (new) | context kinds; `PotionActions` entry points; `OrbQueue` triggers; dispatchers | 1,700 |
| relics-potions | `Relics/RelicModel.cs`, `Relics/Defs/*` (pilot relics), `Potions/PotionModel.cs`, `Potions/Defs/*` (pilots), `Combat/Pipeline/{RelicCmd,PotionCmd}.cs`, `Combat/Loop/PotionActions.cs` | `PlayerState.Relics`/`PotionSlots`; potion dispatchers | 1,300 |
| character-mechanics | `Orbs/*`, `Orbs/Defs/*` (5 orbs), `Combat/Pipeline/{OrbCmd,OstyCmd,ForgeCmd,StarsCmd}.cs`, `Monsters/Defs/Osty.cs` | `PlayerState.OrbQueue`/`PetIds`; `CombatState.Allies`; orb/star/forge/summon dispatchers | 1,400 |
| monsters-kernel | `Monsters/{MonsterDef,MonsterLifecycle,MoveStateMachine,Intents}.cs`, `Monsters/MonsterListener.cs` (new) | `MonsterState` listener slot; `CreatureCmd.Add` | 800 |
| diff-v2 | `sim/Sts2Sim.Diff/*`; `sts2rec` exporter bundle v2 in the following wave | choice API; bundle v2 contract | 1,500 |

Phase-0 contract members (landed by the orchestrator before the parallel step; bodies may throw
`NotImplementedException` until their owner lands them):

```csharp
// Api/GameAction.cs (orchestrator)
enum ActionKind : byte { PlayCard, EndTurn, UsePotion, DiscardPotion, ChoosePick, ChooseConfirm }
static GameAction UsePotion(int slot, int target = NoIndex); DiscardPotion(int slot); Pick(int option); Confirm

// State (state agent)
CombatState: IReadOnlyList<CreatureState> Allies        // [0] = Player, then pets in insertion order
             List<ModifierModel> Modifiers; CombatHistory History; RunEffects RunEffects; ChoiceRuntime Choice
             int CardOrPotionEffectDepth; List<int> PlayersTakingExtraTurn; UnlockState Unlocks
             void AddAlly(MonsterState pet); void RemoveAlly(MonsterState pet); CardInstance CreateCard(CardDef def)
PlayerState: List<RelicModel> Relics; PotionModel?[] PotionSlots; OrbQueue OrbQueue; List<int> PetIds; int Gold
MonsterState: int PetOwnerId (NoCreature when none); AbstractModel? Listener   // MonsterDef hooks adapter
CardInstance: EnchantmentModel? Enchantment; AfflictionModel? Affliction; AbstractModel? Listener
              bool HasBeenRemovedFromState; int DeckVersionId (-1 when none)

// Hooks (hooks agent): all 182 virtuals with game names and defaults, cs first, ctx dropped, plus
//   side-turn hooks: (CombatState cs, CombatSide side, IReadOnlyList<CreatureState> participants)
//   ShouldPlay(CombatState cs, CardInstance card, AutoPlayType autoPlayType)
//   Hook.X for all 148 dispatchers (same names); ref-modifier / out-preventer overloads as in §2.2

// Choice runtime (choice agent)
ChoiceRuntime: IDisposable PushContext(ChoiceContextKind kind, Action? onFirstPause = null)
               void PushSelector(IChoiceAnswerer selector) / PopSelector(); IChoiceAnswerer? Answerer
static class CardSelectCmd: FromHand, FromHandForDiscard, FromHandForUpgrade, FromCombatPile,
               FromChooseACardScreen, FromSimpleGrid (game names; cs first; ctx from the ambient stack)

// New kinds (stubs; bodies by their owners)
abstract class RelicModel, PotionModel, OrbModel, EnchantmentModel, AfflictionModel, ModifierModel : AbstractModel
sealed class OrbQueue; sealed class CombatHistory; sealed class RunEffects; sealed class UnlockState
```

Shared files and their single owner: `Combat/Pipeline/Hook.cs` and `Combat/Pipeline/AbstractModel.cs` —
hooks (the signature list is phase 0, landed by the orchestrator); `State/CombatState.cs` and
`State/CreatureState.cs` — state; `Combat/Loop/CombatLoop.cs` — loop; `Combat/Loop/CombatSim.cs` —
choice; `Api/GameAction.cs`, `Registry/Registries.cs`, `GlobalUsings.cs`, `Enums/*`, `Cards/CardPlay.cs`,
`sim/CONTRACTS.md` — orchestrator (frozen).

Integration order:

0. **Contract freeze (orchestrator):** append `ActionKind`s and enums (`OrbEvokeType`, `PotionUsage`,
   `AutoPlayType`, `PlayerChoiceType`, rarities), base-class stubs for the new kinds, all 182 hook
   virtual signatures in `AbstractModel` (with `participants` and `AutoPlayType` restored), new
   registries and namespaces, member stubs on `CombatState`/`PlayerState`/`CardInstance`/`MonsterState`,
   choice runtime stubs (throwing `NotImplementedException`).
1. **Parallel:** hooks, state, pipeline, cards-kernel, monsters-kernel, character-mechanics,
   relics-potions, and the choice runtime (`CardSelectCmd`, contexts, answerers), each tested with local
   doubles.
2. **Loop wiring:** turn flow (auto-pre/post-play phases, orb triggers, participants, effect depth,
   extra turns), auto-play and Sly discard, potion decisions in `Step`, the turn-setup pause callbacks,
   history recording sites.
3. **Choice facade:** `CombatSim` re-execution, `LegalActions` at prompts, `Clone`/`StateHash` at prompts.
4. **Integration gate:** existing 367 tests and every new suite green, diff gate 69/69 unchanged,
   benchmark within budget, adversarial verification.
5. **diff-v2**, then content waves (file-per-card/relic/potion/monster pipelines on this kernel).

## 7. Risks

Synchronous rewrite:

- **Turn-setup reordering** (§4.6) — the only single-player reordering, and it also triggers on empty or
  automatic selections. Validate with recordings that contain GamblingChip, Toolbox and
  ToolsOfTheTradePower.
- **Listener snapshots** — the list is built at the first `MoveNext`, liveness is checked at yield;
  listeners added during a pass are not visited, removed powers still are while their owner is attached;
  each pass of a multi-pass dispatcher takes a new snapshot; the run source builds the combat list only
  after the deck listeners ran (`Combat/CombatState.cs:855-956`, `Runs/RunState.cs:829-933`). Per-hook
  filtering must keep the order and the lazy liveness check.
- **Mid-resolution death and win** — guarded listeners go silent once the last primary enemy dies
  (`Hooks/Hook.cs:8794`); moves into combat piles are refused while ending (the killing card stays in
  Play); the `OnPlayWrapper` dead-player exit skips the result pile and cleanup
  (`Models/CardModel.cs:779-909`); a monster dying during its own move stays listed until the move ends
  (`Commands/CreatureCmd.cs:2815-2822`); win checks run only at fixed points (after each action, after
  each enemy, at turn-phase boundaries).
- **Nested effects** — auto-play re-enters `OnPlayWrapper` from hooks (Mayhem, HistoryCourse,
  Hellraiser, ImitationLearning, Imbued, Stampede, WhisperingEarring) and from Sly discards
  (`Commands/CardCmd.cs:843`, `:917`); effect depth gates `CheckForEmptyHand`; prompts of auto-played
  cards use the caller's context.
- **Dropped parameters** — `participants` and `AutoPlayType` must be restored before content waves.
- **Fire-and-forget tasks** — visual only in combat content; strip and note.

RNG consumption to preserve:

- Every combat stream with the game's call order and list order: `CombatTargets` (random targets,
  auto-play targets, WhisperingEarring), `CombatCardGeneration` (`TakeRandom`/`NextItem` over ordered,
  unlock- and multiplayer-filtered pools), `CombatCardSelection`, `CombatEnergyCosts`,
  `CombatPotionGeneration`, `CombatOrbGeneration`, `Niche`, `Shuffle` (shuffles and random pile insert),
  encounter and monster private `Rng`.
- No VFX-dependent draw on a run stream in combat: cosmetic randomness (skins, screen shake, dialogue)
  uses the static `Rng.Chaotic` (21 sites); never model it. `MonsterModel.Rng` returns `Rng.Chaotic` for
  canonical models (`Models/MonsterModel.cs:395-404`): never read RNG from a def.
- Run layer: TheArchitect shuffles a character-specific VFX list with the event `Rng`
  (`Models.Events/TheArchitect.cs:284-285`, `:1104-1105`; `Models/CharacterModel.cs:215`); the list
  lengths per character must be ported as data or later event draws shift.
- AutoSlay's RNG is separate (`new Rng(GetDeterministicHashCode(seed))`); its runs are not oracles for
  game RNG use in prompts.

Static-state traps:

- `CardSelectCmd` selector stacks are static (`Commands/CardSelectCmd.cs:3084-3150`); the sim keeps the
  stack on `CombatState` (cloned) because `WhisperingEarring` pushes `VakuuCardSelector` mid-combat.
- `CardRarityOdds.regularCommonOdds` is a static field initialized from the ascension at type
  initialization (`Odds/CardRarityOdds.cs:13`); `YummyCookie._cachedRandomCharacter` is static
  (`Models.Relics/YummyCookie.cs:100`). Run layer; compute per run.
- Canonical versus mutable models: content reads canonical instances (`ModelDb.Card<T>()` options
  before `CreateCard`, `canonicalPower` in `TryModifyPowerAmountReceived`, `Hooks/Hook.cs:10928`); the
  def/instance split must keep those reads on defs.
- `CombatManager.Instance.History` and `IsCurrentLiveCombat(effectCombatId)` guards are global in the
  game; per `CombatState` in the sim.

Performance:

- Listener counts grow from a handful of powers to 100+ (relics, cards in piles) with ~50 dispatches
  per card play. Use per-type override masks, cached ordered listener arrays invalidated by a mutation
  version counter, and keep the liveness check lazy. One `DeepClone` per decision `Step` for the tape
  engine; rollouts use the answerer path without clones.

## 8. Open questions

1. Turn-setup pause: the code forces `AfterSideTurnStart` and the orb triggers before the paused
   setup resumes (the setup waits for task assignment, `GameActions.Multiplayer/HookPlayerChoiceContext.cs:229`,
   which happens at `Combat/CombatManager.cs:5204-5205`); confirm on recordings.
2. When the deferred `CheckWinCondition` (`Combat/CombatManager.cs:5226`) ends the combat before a
   turn-setup prompt is answered, is the queued `CombatPlayPhaseOnly` hook action cancelled so the rest of
   `SetupPlayerTurn` never runs?
3. `RequireManualConfirmation` prompts with fewer options than `MinSelect`: the combat-pile screen clips
   `MinSelect` to the shown count (`Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:346`), the
   hand and simple-grid screens do not (`Nodes.Combat/NPlayerHand.cs:1270-1281`,
   `NSimpleCardSelectScreen.cs:338`) and would leave confirm disabled. Reachable in v0.111.0? The sim clips
   `MinSelect` to `n` everywhere.
4. Per-site `Ordered` flags: which multi-select results are order-sensitive besides the PutBack sites?
5. Potion usability gating in the UI (`Nodes.Potions`) and single-player `AnyAlly`/`AnyPlayer`/`Osty`
   targets for cards and potions (`Models/CardModel.cs:3362-3384` accepts any same-side creature for `AnyAlly`).
6. `UnlockState` for card generation: where do recordings expose the unlocked epochs?
7. `CardPileCmd.DrawWithoutBlockingOnOtherPlayers` in single player — identical to `Draw`?
8. `CreatureCmd.Stun` wrapper body is missing in the decompile (pipeline spec §9).
9. Can relics be obtained, replaced or melted during combat in v0.111.0 content (`RelicCmd`)?
10. Bundle v2: duplicate CombatCard options with hidden differences need post-state disambiguation.

## Appendix A. Kernel readiness by content kind

"Overridden hooks all ported" = every `AbstractModel` virtual the file overrides exists in the sim;
"Cmd calls all ported" = every Cmd/builder call is ported (partial overloads counted missing when the
file uses them). This measures only the hook and Cmd surfaces: listener kinds other than powers are not
wired yet, and card-model APIs (keywords, costs, vars) are separate work. Feature flags count files
touching a subsystem (`private_rng` is noisy for events and encounters).

| Kind | Files | Overridden hooks all ported | Cmd calls all ported | Both | Feature flags (files) |
|---|--:|--:|--:|--:|---|
| Cards | 596 | 587 | 424 | 421 | calculated_var 43, prompt 42, orbs 33, stars 32, history 29, card_generation 28, osty 23, rng_cardgen 15, doom 14, forge 13, autoplay 11, summon 10, rng_cardsel 7, gold 3, rng_shuffle 3, rng_targets 3, temporary_power 2, enchant 1, player_end_turn 1, rng_orb 1, rng_potion 1 |
| Powers | 268 | 250 | 219 | 206 | history 12, osty 12, orbs 10, afflict 8, card_generation 7, doom 7, prompt 5, rng_cardgen 5, rng_targets 5, temporary_power 5, autoplay 4, forge 4, stars 4, rng_cardsel 3, spawn_monster 3, summon 3, gold 1, rng_energy 1, rng_orb 1, rng_shuffle 1 |
| Relics | 300 | 183 | 185 | 99 | card_generation 34, prompt 28, enchant 17, rng_niche 15, gold 8, history 8, orbs 7, osty 7, stars 7, rng_cardgen 6, rng_cardsel 5, rng_targets 5, rng_potion 3, autoplay 2, doom 2, summon 2, forge 1, temporary_power 1 |
| Potions | 65 | 65 | 42 | 42 | prompt 9, card_generation 6, rng_cardgen 6, orbs 2, autoplay 1, doom 1, forge 1, gold 1, osty 1, rng_energy 1, rng_potion 1, stars 1, summon 1 |
| Monsters | 121 | 120 | 98 | 97 | osty 7, spawn_monster 6, card_generation 4, enchant 1, prompt 1, rng_cardgen 1, private_rng 1, rng_niche 1 |
| Encounters | 91 | 91 | 91 | 91 | private_rng 14, afflict 7 |
| Enchantments | 23 | 22 | 21 | 21 | enchant 2, autoplay 1, rng_energy 1 |
| Afflictions | 7 | 7 | 6 | 6 | afflict 1 |
| Orbs | 5 | 5 | 5 | 5 | orbs 5, rng_targets 1 |
| Modifiers | 17 | 6 | 16 | 5 | card_generation 5, prompt 1, rng_niche 1 |
| Events | 68 | 68 | 16 | 16 | private_rng 35, gold 20, prompt 20, card_generation 13, enchant 12, history 1, rng_shuffle 1 |

Most blocking missing virtuals (files): `AfterCombatEnd` 45, `AfterRoomEntered` 35 (run),
`TryModifyCardRewardOptionsLate` 9 (run), `TryModifyRestSiteOptions` 7 (run), `AfterCardChangedPiles` 6,
`TryModifyRewards` 6 (run), `AfterCombatVictory` 5, `ModifyExtraRestSiteHealText` 5 (run),
`ModifyCardPlayResultLocation` 4. Full per-file lists: `content_readiness.files` in the JSON.

## Appendix B. Data and scripts

- `sim/specs/inventory/kernel-gaps.json`: `hook_virtuals` (185, with override files per kind),
  `model_kind_virtuals`, `hook_dispatchers` (148), `listener_order`, `cmd_facades` (199),
  `choice_mechanics`, `prompt_types`, `prompt_sites` (85 incl. run-layer relic sites),
  `design_choice_api`, `subsystems`, `ownership`, `risks`, `open_questions`, `content_readiness`.
- Scratch scripts (not committed, Python 3 stdlib): `tmp/sim-port/kernel/{virtuals,hooks,cmds,kindvirt,prompts,prefs,sites,readiness,tables,build_json}.py`.
