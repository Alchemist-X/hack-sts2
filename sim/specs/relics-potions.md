# Relics and potions (W1, CONTRACTS Part B §B4, §B5, §B12 A1-A5, A10)

2026-11-01 · relics-potions track, round 3, on top of `263810d` (phase-0b amendment fixes). Rule truth: decompile
v0.111.0 (`<ns-dir>/<File>.cs:<line>`, `MegaCrit.Sts2.Core.` dropped). Rules follow the `TestMode.IsOff` branch,
timing follows `NonInteractiveMode` (design-subsystems §2: `Cmd.Wait` is skipped, `Commands/Cmd.cs:75`).

Files (all owned by this track): `sim/Sts2Sim.Core/Relics/{RelicModel,RelicCanonicalTags,RelicPools,PendingKernelCalls}.cs`,
`Relics/Defs/{HappyFlower,CentennialPuzzle,Whetstone,GamblingChip,PaperPhrog,PaperKrane,Nunchaku}.cs`,
`sim/Sts2Sim.Core/Potions/{PotionModel,PotionCanonicalTags,PotionPools,PotionFactory}.cs`,
`Potions/Defs/{AttackPotion,Ashwater,FairyInABottle,StrengthPotion,BlockPotion,FirePotion}.cs`,
`Combat/Pipeline/{RelicCmd,PotionCmd}.cs`, `Combat/Loop/PotionActions.cs`, `sim/Sts2Sim.Tests/RelicPotionTests.cs`, this file.

## 1. `RelicModel` (`Relics/RelicModel.cs`, game `Models/RelicModel.cs`)

The 22 virtuals of the game base: 14 ported, 8 UI-only stripped (M13): `Title` (:60), `IconBaseName` (:146),
`PackedIconPath` (:148), `PackedIconOutlinePath` (:150), `BigIconPath` (:152), `FlashSfx` (:358),
`ShouldFlashOnPlayer` (:360), `ExtraHoverTips` (:383).

| Member | Game | Sim |
| --- | --- | --- |
| `Rarity` | :190 abstract | abstract |
| `Pool` | :192 `First(ModelDb.AllRelicPools, p => p.AllRelicIds.Contains(Id))` | `RelicPools.PoolOf(Id)`: the first pool in `ModelDb.AllRelicPools` order containing the id (§5); `InvalidOperationException` when none (the game's `First`) |
| `IsTradable` | :194-221 | verbatim: `IsUsedUp`, `HasUponPickupEffect`, `IsMelted`, `SpawnsPets` each give false; rarity `Starter` or `(uint)(rarity - Event) <= 1` (Event, Ancient) false; else true |
| `IsAllowedInShops` | :223 `true` | virtual `true` |
| `Owner` | :225-242 setter throws on a different owner | `AttachTo` (same message); `Owner` throws when unowned |
| `IsUsedUp`, `HasUponPickupEffect`, `SpawnsPets`, `IsStackable`, `AddsPet` | :244, :246, :248, :250, :280 `false` | virtual `false` |
| `IsWax`, `IsMelted` | :252-278, public setters | public auto-properties |
| `StackCount` | :282-289 `= 1`, private setter | `{ get; private set; } = 1` |
| `IncrementStackCount` | :640-649 throws unless `IsStackable` | verbatim (message kept) |
| `DynamicVars` | :291-303 lazy per instance from `CanonicalVars`; cloned in `DeepCloneFields` :616-620 | built once in the base constructor from `CanonicalVars`; `CloneFor` deep-clones it; written in `WriteCanonical` (§8) |
| `CanonicalVars` | :305 empty | virtual empty |
| `MerchantCost` | :307-318 | the rarity switch verbatim: Common 175, Uncommon 225, Rare 275, Shop 200, Ancient/Starter/Event 999999999, None 1, otherwise `InvalidOperationException` |
| `FloorAddedToDeck` | :320-331 | public auto-property (set by `RelicCmd.Obtain` in the game, §3) |
| `Status` | :333-352 (setter fires the UI event `StatusChanged`) | public auto-property |
| `ShowCounter`, `DisplayAmount` | :354, :356 | virtual `false`, `0` |
| `HasBeenRemovedFromState` | :420-427, private setter | `{ get; internal set; }` (contract change request 1, §11) |
| `IsAllowed(rs)` | :529-532 `true` | virtual `true` |
| `IsAllowedAtNeow(rs, player)` | :534-537 `IsAllowed(player.RunState)` | virtual `IsAllowed(rs)` |
| `IsBeforeAct3TreasureChest` | :539-543 `TotalFloor < (Players.Count > 1 ? 38 : 41)` | `protected static (RunState)` throws `NotImplementedException` (W2: `RunState.TotalFloor`); `protected static (int playerCount, int totalFloor)` carries the rule and constants |
| `RemoveInternal` | :635-638 | verbatim |
| `AfterObtained(rs)`, `AfterRemoved(rs)` | :651-659 no-op | virtual no-op (M11) |
| `AfterCloned` | :622-633 resets `HasBeenRemovedFromState` and UI events on a new mutable model | not ported: a sim clone is a state copy and keeps the flag; a new instance from the registry starts at `false` |

Stripped as UI: `Flash` (:545-553), `UpdateTexture` (:564-608), `HoverTip(s)`, `CanonicalInstance`, the
serialization members (:661-681), `RelicIconChanged` (:683-686), `InvokeDisplayAmountChanged` (:555-562).
`ShouldReceiveCombatHooks => true` (:429; potions :529) is not ported: the abstract member
(`Models/AbstractModel.cs:74`) has no reader in v0.111.0.

Content constraint for W3 porters: `CanonicalVars` runs in the base constructor, so it may read only constants and
field initializers (true for every v0.111.0 relic and potion; `Models.Relics/BoneTea.cs:19`, `:29` reads a field with
an initializer).

## 2. `PotionModel` (`Potions/PotionModel.cs`, game `Models/PotionModel.cs`)

| Member | Game | Sim |
| --- | --- | --- |
| `Rarity`, `Usage`, `TargetType` | :442, :444, :446 abstract | abstract |
| `Pool` | :448 `First(ModelDb.AllPotionPools, …)` | `PotionPools.PoolOf(Id)` (§5) |
| `CanonicalInstance` | :512-527 | `PotionPools.Canonical(Id)` (§5) |
| `Owner` | :450-467 | `AttachTo` / `Owner` as for relics |
| `DynamicVars` | :469-481 lazy; potions have no `DeepCloneFields` override and no v0.111.0 potion writes a var | per instance from the base constructor, deep-cloned and hashed like relics |
| `IsQueued` | :485-492 (UI queue flag set by `EnqueueManualUse`, cleared by `AfterUsageCanceled` :670-673) | not ported (M13): sim decisions execute at once |
| `CanBeGeneratedInCombat`, `PassesCustomUsabilityCheck` | :494, :496 `true` | virtual `true` |
| `HasBeenRemovedFromState` | :531-538 private setter | `{ get; private set; }` |
| `Discard` | :589-593 `Owner.DiscardPotionInternal(this)`, flag | verbatim (B12 A10 internals) |
| `RemoveBeforeUse` | :595-599 `Owner.RemoveUsedPotionInternal(this)`, flag | verbatim |
| `ManualUseTarget(target)` | `EnqueueManualUse` :609-612: a null target becomes the owner when `IsValidTarget(Owner.Creature)` | that rule as a query; the rest of `EnqueueManualUse` (:601-616: `BeforeUse` UI event, `IsQueued`, `RequestEnqueue`) is node and queue plumbing |
| `IsValidTarget` | :618-653 | verbatim; `IsSingleTarget` is `Entities.Cards/ActionTargetExtensions.cs:5-18` (Self, AnyEnemy, AnyPlayer, AnyAlly, TargetedNoCreature) as `PotionModel.IsSingleTarget` (a static, not an extension, so no clash with a cards-kernel extension) |
| `OnUseWrapper(rs, target)` | :655-668, state machine :42-358 | §2.1 |
| `OnUse(rs, target)` | :675-678 no-op | protected virtual no-op (M11) |
| `AssertValidForTargetedPotion` | :695-702 `ArgumentNullException("target", …)` | verbatim |
| `CanThrowAtAlly(rs)` | :704-711 `TargetType == AnyPlayer && Players.Count > 1 ? CombatManager.IsInProgress : false` | verbatim with `rs.Players`, `rs.Combat?.IsInProgress` |
| `RequireCombat(rs)` | sim helper | `rs.Combat` or `NotImplementedException` (W2: a potion effect outside combat needs run-layer command overloads) |

### 2.1 `OnUseWrapper` traced order (B12 A3)

Goto order of `<OnUseWrapper>d__75.MoveNext` (`Models/PotionModel.cs:64-351`), single player:

| # | Game | Sim |
| --- | --- | --- |
| 1 | `RemoveBeforeUse()` :147 | same |
| 2 | `combatState = Owner.Creature.CombatState` :148 | `rs.Combat` when the owner is attached to it, else null |
| 3 | `choiceContext.PushModel(this)` :149 | stripped (prompt source stack; `ChoiceSource` is explicit in the sim) |
| 4 | `WaitForUnpause` :150 | stripped (pausing is disabled in NonInteractiveMode, design-subsystems §2 item 4) |
| 5 | `Hook.BeforePotionUsed(rs, cs, this, target)` :262 | same |
| 6 | `TestMode.IsOff && cs != null`: `NItemThrowVfx` + `Cmd.Wait(0.5f)` :276-317 | stripped (VFX; the wait is skipped) |
| 7 | `effectCombatId = BeginCardOrPotionEffect(Owner)` :332 (`Combat/CombatManager.cs:6527-6531`: depth + 1, returns `CurrentCombatId`) | `cs.CardOrPotionEffectDepth++` when `cs != null` (M17: one depth for the one player); the effect id is the combat |
| 8 | `OnUse` inside a `BranchingPlayerChoiceContext` :180-198 | `OnUse(rs, target)` inline: in single player the branching context keeps the original context and waits for completion (`GameActions.Multiplayer/BranchingPlayerChoiceContext.cs:64-91`, `:147-153`) |
| 9 | exception captured :200-203 | `catch (Exception)` captured (a `ChoicePendingException` too) |
| 10 | `EndCardOrPotionEffect(effectCombatId, Owner)` :204 (`:1465-1485`: depth − 1, `<= 0` removes the entry; a dead player's cards are removed only if not all players are dead, `:3611-3612`) | depth − 1 floored at 0; the dead-player branch is a no-op in single player |
| 11 | rethrow :232-240 | `ExceptionDispatchInfo.Capture(captured).Throw()` |
| 12 | `if (!Owner.Creature.IsDead)` :242, else end (:259) | same; a dead owner skips 13-17 |
| 13 | `InvokeExecutionFinished` :244 | stripped (subscriber `Nodes.Combat/NPlayerHand.cs:1064`) |
| 14 | `History.PotionUsed` iff `cs != null && IsCurrentLiveCombat(effectCombatId)` :245-248 (`:6503-6520`: the turn state of that id is in progress) | `cs.History.AppendPotionUsed(cs, this, target)` iff `cs != null && cs.IsInProgress` |
| 15 | `Hook.AfterPotionUsed(rs, cs, this, target)` :249 | same |
| 16 | map-point history `PotionUsed.Add(Id)` :321 | W2 (null outside a map point; standalone combats have none) |
| 17 | `CheckForEmptyHand(effectCombatId, …, Owner)` :322 (`:7026-7040` live turn state; `:954-956` in progress, not executing a card or potion effect, empty hand → `Hook.AfterHandEmptied`) | private port with exactly these three gates over `cs.CardOrPotionEffectDepth` |
| 18 | `PopModel` :338 | stripped (with 3) |

Consequence of 1 + the listener liveness check (`Combat/CombatState.cs:1057`: `!HasBeenRemovedFromState`): the used
potion never receives its own `BeforePotionUsed` / `AfterPotionUsed` (test `UsedPotionIsNotItsOwnListener`).

## 3. `RelicCmd` (`Combat/Pipeline/RelicCmd.cs`, game `Commands/RelicCmd.cs`)

`rs` first (both-scope commands, M11); the combat path reads `rs.Combat`.

| Method | Game | Sim body |
| --- | --- | --- |
| `Obtain(rs, relicId, player)` | `Obtain<T>(player)` :394-400 (`:110`: `ModelDb.Relic<T>().ToMutable()`) | `RelicRegistry.Create(id)` then `Obtain` (cross-kind by id) |
| `Obtain(rs, relic, player, index = -1)` | :402-425 (state machine :161-219, :177-193) | `AddRelicInternal(relic, index)` (B12 A10, `Entities.Players/Player.cs:883-903`); `FidelityFlag.ApproximatedRule` with `RelicCmd.ObtainRunLayerSite` (§10); `AfterObtained(rs)`; returns the relic |
| `Remove(rs, relic)` | :427-438 (:255-257) | `relic.Owner.RemoveRelicInternal(relic)` (`Player.cs:905-922`), `AfterRemoved(rs)` |
| `Replace(rs, original, replace)` | :440-451 (:342-362) | index of the original, `Remove`, `Obtain(…, index)` |
| `Melt(rs, relic)` | :453-464 (:47-48) | `relic.Owner.MeltRelicInternal(relic)` (`Player.cs:924-947`: not wax, already melted, not owned → `InvalidOperationException`, then `IsMelted`, `Status = Disabled`), `AfterRemoved(rs)` |

Steps of `Obtain` not executed in W1: map-point `RelicChoices` (:409, null without a map point); `RelicGrabBag.Remove`
and `SharedRelicGrabBag.Remove` for non-stackable relics (:411-415, run state W2 adds); `FloorAddedToDeck =
TotalFloor` (:422, `RunState` has no `TotalFloor` yet); stripped UI: `AnimateRelic`, `relic_get.mp3`,
`MarkRelicAsSeen` (:416-421). `Remove` skips the map-point `RelicsRemoved` (:255). `AssertMutable` (:407, :445-446)
has no sim counterpart (sim models are always mutable instances).

## 4. `PotionCmd` (`Combat/Pipeline/PotionCmd.cs`, game `Commands/PotionCmd.cs`)

| Method | Game | Sim body |
| --- | --- | --- |
| `TryToProcure(rs, potionId, player)` | `TryToProcure<T>(player)` :244-250 | `PotionRegistry.Create(id)` then `TryToProcure` |
| `TryToProcure(rs, potion, player, slotIndex = -1)` | :252-280 (state machine :174-235) | `Hook.ShouldProcurePotion(rs, cs, potion, player)` false → `(false, potion, NotAllowed)` (:258-266); `AddPotionInternal(potion, slotIndex)` (`Player.cs:1020-1070`); on success `Hook.AfterPotionProcured(rs, cs, potion)` (:273); returns the result |
| `Discard(rs, potion)` | :282-293 (state machine :32-74, :47-49) | `potion.Discard()`, then `Hook.AfterPotionDiscarded(rs, cs, potion)` |

`cs` is the owner's combat (`player.Creature.CombatState`): `rs.Combat` when the player is attached to it, else null.
Not executed: map-point `PotionChoices` (:270) and `PotionDiscarded` (:47) (W2, null in standalone combats); stripped
UI: `gain_potion.mp3`, `AnimatePotion` (:271-272), `MarkPotionAsSeen` (:274-277).

## 5. Pools and `PotionFactory`

Pool ids are `ModelId.Entry` of the pool models (`IRONCLAD_RELIC_POOL`, `SHARED_POTION_POOL`, …). Members in the
game's `GenerateAll*` order, extracted from the decompile and cross-checked against `sim/specs/inventory/pools.json`
(no difference).

Relic pools (`Relics/RelicPools.cs`), `ModelDb.AllRelicPools` order = the character pools in `AllCharacters` order
(Ironclad, Silent, Regent, Necrobinder, Defect, `Models/ModelDb.cs:129-136`, `:200-202`) then Deprecated, Event,
Fallback, Shared (`:204-210`). Sources: `Models.RelicPools/IroncladRelicPool.cs:18-31` (8), `SilentRelicPool.cs:18`
(8), `RegentRelicPool.cs:18` (8), `NecrobinderRelicPool.cs:18` (8), `DefectRelicPool.cs:18` (8),
`DeprecatedRelicPool.cs:10-13` (1), `EventRelicPool.cs:10` (142), `FallbackRelicPool.cs:10-13` (1),
`SharedRelicPool.cs:14` (118). `LASTING_CANDY`, `RAZOR_TOOTH`, `SPARKLING_ROUGE` are in Event and Shared; `Pool`
answers Event (first). The epoch gates of `GetUnlockedRelics` are W2 (`RelicFactory`).

Potion pools (`Potions/PotionPools.cs`), `ModelDb.AllPotionPools` order = character pools then Deprecated, Event,
Mock, Shared, Token (`Models/ModelDb.cs:181-194`). Character pools return their epoch's list, all or nothing:
`Models.PotionPools/IroncladPotionPool.cs:16-28` (`Timeline.Epochs/Ironclad4Epoch.cs:21-34`: BloodPotion,
SoldiersStew, Ashwater), Silent `SilentPotionPool.cs:16-28` (`Silent4Epoch`), Regent (`Regent4Epoch`), Necrobinder
(`Necrobinder4Epoch`), Defect (`Defect4Epoch`), each `:16-28`. Shared: `SharedPotionPool.cs:14-63` (45 potions);
`GetUnlockedPotions` (:66-106) removes `Potion1Epoch.Potions` (BeetleJuice, MazalethsGift, DropletOfPrecognition,
`Timeline.Epochs/Potion1Epoch.cs:20-33`) unless that epoch is revealed, then `Potion2Epoch.Potions` (PowderedDemise,
ShipInABottle, TouchOfInsanity, `Potion2Epoch.cs:20-33`), each by `List.Remove` (first occurrence, order kept).
Event, Token, Deprecated, Mock use the base `AllPotions` (`Models/PotionPoolModel.cs:40-43`). Character → pool:
`Models.Characters/Ironclad.cs:36`, `Silent.cs:38`, `Regent.cs:42`, `Necrobinder.cs:40`, `Defect.cs:35`; `Deprived`,
`DeprecatedCharacter`, `RandomCharacter` use the Ironclad pool (`Deprived.cs:47`, `DeprecatedCharacter.cs:37`,
`RandomCharacter.cs:29`). The sim keys the map by `PlayerState.CharacterId` (the character's `ModelId.Entry`).

`CanonicalPotion` (id, rarity, `CanBeGeneratedInCombat`) stands for the canonical `PotionModel` of every pool member,
ported or not, so the draws see the game's option lists. Rarity and the three `CanBeGeneratedInCombat => false`
overrides (`Models.Potions/FairyInABottle.cs:148`, `FruitJuice.cs:89`, `RegenPotion.cs:93`) come from the 66 potion
files (`Models.Potions/<Name>.cs` `Rarity` lines, e.g. `AttackPotion.cs:124`; mock `Models.Potions.Mocks/MockDiscardAndAddShivsPotion.cs:150`).
`ToMutable()` = `PotionRegistry.Create(id)` (the game's callers call `ToMutable()` on the factory result,
`Models.Cards/Alchemize.cs:78`, `Models.Potions/EntropicBrew.cs:65`). A test checks that every registered potion's
rarity and flag equal its canonical row.

`PotionFactory` (`Potions/PotionFactory.cs`, `partial class`, D6), game `Factories/PotionFactory.cs:12-67`, with `rs`
first for the unlock profile (`player.UnlockState` → `rs.Unlocks`, D5):

| Member | Game | Notes |
| --- | --- | --- |
| constants | :14-20 `_rareChance 0.1f`, `_rareThreshold 0.1f`, `_uncommonChance 0.25f`, `_uncommonThreshold 0.35f` | public consts; only the thresholds are read |
| `CreateRandomPotionOutOfCombat(rs, player, rng, blacklist)` | :22-25 | `First` of the batch of one |
| `CreateRandomPotionsOutOfCombat(rs, player, count, rng, blacklist)` | :27-35 | options, `Except(blacklist)`, draw |
| `CreateRandomPotionInCombat(rs, player, rng, blacklist)` | :37-45 | options `Where(CanBeGeneratedInCombat)`, `Except`, `First` of one |
| `CreateRandomPotions` (private) | :47-61 | per pick: `rng.NextFloat()`; `<= 0.1f` Rare, else `<= 0.35f` Uncommon, else Common; `rng.NextItem` over the remaining options of that rarity (one `NextInt(0, n)`; an empty set returns null without a draw, which the game adds to the result, kept); the pick is removed from the options |
| `GetPotionOptions(rs, player)` | :63-66 | the character pool's unlocked potions, then Shared's |

Callers pass the stream: `Rng.CombatPotionGeneration` in combat (`Alchemize.cs:78`, `EntropicBrew.cs:65`,
`Models.Relics/DelicateFrond.cs:60`, `PhialHolster.cs:77`, `AlchemicalCoffer.cs:82`), `PlayerRng.Rewards` /
`Shops` in the run layer (`Rewards/PotionReward.cs:158`, `Entities.Merchant/MerchantInventory.cs:163`). The game's
`blacklist` holds canonical models compared by reference (`MerchantPotionEntry.cs:173`); the sim compares
`CanonicalPotion` references (one object per id). There is no `PotionFactory.GetDistinctForCombat` (B12 A1).

## 6. `PotionActions` (`Combat/Loop/PotionActions.cs`)

| Member | Game | Rule |
| --- | --- | --- |
| `GetPotionAtSlotIndex(player, index)` | `Entities.Players/Player.cs:964-972` | `IndexOutOfRangeException` outside the slots (the game's message), else the slot |
| `ResolveTarget(cs, potion, target)` | `GameActions/UsePotionAction.cs:77-105` | in combat (`CombatManager.IsInProgress` → `cs.IsInProgress`): a target, or a non-single-target type, gives `target` (`GetCreatureAsync(TargetId)`, null id → null, `Combat/CombatState.cs:794-801`), else the owner (:94); out of combat: no target player and not `TargetedNoCreature` → the owner (:96-98), a target player → it (:100-102), else null. `target` is the creature the action carries (`TargetId` = its combat id, `TargetPlayerId` = its player, ctor :230-246) |
| `CanUse(cs, slot, target)` | node layer `Nodes.Potions/NPotionPopup.cs:285-351`, `:535-566`; target per `UsePotionAction.cs:117` | slot in range and filled; owner alive (:285); `Usage`: None throws (:294-295), CombatOnly needs `cs.IsInProgress`, the owner's side to act and the owner alive (`RefreshButtons` :548), AnyTime enabled (:319-320), Automatic false (:322-323); `CanUseOrRemovePotions` (:330-334); `PassesCustomUsabilityCheck` (:335-338); `IsValidTarget(ResolveTarget(cs, potion, target))` (the action cancels otherwise, :117, :132). `IsQueued` and `InACardSelectScreen` / `PlayerActionsDisabled` (:548) do not exist at a sim decision point: actions never queue, prompts are modal (B1: potions are illegal at a prompt), and the player's actions are enabled from the turn start (`Combat/CombatManager.cs:5294`) until the turn ends (`:6883-6886`) |
| `CanDiscard(cs, slot)` | `NPotionPopup.cs:285-289`, `:542`, `:556-565` | slot in range and filled, owner alive, `CanUseOrRemovePotions`; no side gate |
| `UsePotion(cs, slot, target)` | `UsePotionAction.ExecuteAction` :55-145 | `GetPotionAtSlotIndex` (:75); empty slot → cancel (:106-108); `ResolveTarget`; `IsValidTarget` (:117) → `OnUseWrapper(cs.Run, creature)` inside `ChoiceContextKind.Inline` (`GameActionPlayerChoiceContext`, :121-122, B1 table), else cancel (:132). Cancel = `AfterUsageCanceled` (`Models/PotionModel.cs:670-673`, `IsQueued = false`) and a UI refresh (:271-279): no state change |
| `DiscardPotion(cs, slot)` | `GameActions/DiscardPotionGameAction.cs:55-75` | slot `>= Count` → `IndexOutOfRangeException` (:55-58; a negative slot is impossible for the game's `uint` and throws the same here); a potion → `PotionCmd.Discard(cs.Run, potion)` (:63); empty → cancel (:73-74) |

Caller duties (loop, choice): both actions are `CombatPlayPhaseOnly` in combat (`UsePotionAction.cs:156-166`,
`DiscardPotionGameAction.cs:103-113`), so they are legal only at a play-phase decision point; after each the caller
runs the win check, as after a card play (`GameActions/ActionExecutor.cs:130-139`). `CanUse` resolves `NoIndex` the
way the action does, so a generator that lists one action per `(slot, resolved target)` (B12 A4) and asks
`CanUse(cs, slot, resolvedTarget)` gets the game's set; `ManualUseTarget` gives the node layer's default target
(`Models/PotionModel.cs:609-612`), which yields the same legal set.

## 7. Pilot content

Ids are `ModelId.Entry` (`Models/ModelDb.cs:485-488`). Dynamic vars keep the game's names (`CardsVar` "Cards",
`Localization.DynamicVars/CardsVar.cs:5-8`; `EnergyVar` "Energy", `EnergyVar.cs:7-21`; `BlockVar` "Block",
`BlockVar.cs:13-26`; `PowerVar<T>` = `typeof(T).Name`, `PowerVar.cs:13`; `DamageVar` "Damage", `DamageVar.cs:13`);
the sim builds them as base `DynamicVar` / `BlockVar` / `DamageVar` with those names (the subclasses differ only in UI
fields). Cross-kind references are by id (`STRENGTH_POWER` through `PowerRegistry`).

Relics:

| Relic | Role | Game | Port |
| --- | --- | --- | --- |
| `HAPPY_FLOWER` | counter relic | `Models.Relics/HappyFlower.cs`: Common :170, `ShowCounter` :172, `DisplayAmount` :174-184, vars Energy 1, Turns 3 :186-190, `TurnsSeen` saved :208-221, `AfterSideTurnStart` :223-235 (state machine :57-72), `DoActivateVisuals` :125-143, `AfterCombatEnd` :250-254 | participants contain the owner → `TurnsSeen = (TurnsSeen + 1) % Turns`, `Status = TurnsSeen == Turns − 1 ? Active : Normal`, at 0 `DoActivateVisuals` then `PlayerCmd.GainEnergy(Energy)`; `AfterCombatEnd` sets `Normal`; the counter persists across combats |
| `CENTENNIAL_PUZZLE` | `AfterCombatEnd` reset | `CentennialPuzzle.cs`: Common :107, Cards 3 :111, `UsedThisCombat` :113-127, `AfterDamageReceived` :129-143 (state machine :61-85), `AfterCombatEnd` :145-149 | in progress, target is the owner, `UnblockedDamage > 0`, not used → used, then 3 single draws (`CardPileCmd.Draw`, the game's `CardPileCmd.Draw(choiceContext, Owner)`); `AfterCombatEnd` clears the flag |
| `WHETSTONE` | `Niche` reader | `Whetstone.cs`: Common :16, `HasUponPickupEffect` :18, Cards 2 :20, `AfterObtained` :22-39 | deck attacks that are upgradable (deck order), `StableShuffle(rs.Rng.Niche)` (`Extensions/ListExtensions.cs:10-19`: sort by `CardModel.CompareTo` = ordinal id then upgrade level, `Models/CardModel.cs:3649-3671`, `Models/ModelId.cs:66-74`; the sim sorts with the loop's `CardPileCmd.CompareForShuffle`, the same order), take 2, `CardCmd.Upgrade` each (cards-kernel; §9) |
| `GAMBLING_CHIP` | turn-setup prompt | `GamblingChip.cs`: Rare :122, `AfterPlayerTurnStart` :124-137 (state machine :72-90) | owner and `TurnNumber <= 1` → `CardSelectCmd.FromHandForDiscard(owner, prefs(0, 999999999), null, source)`, the result listed; non-empty → `CardCmd.DiscardAndDraw(cards, count)` (loop; §9). Site `GAMBLING_CHIP:AfterPlayerTurnStart` (B1) |
| `PAPER_PHROG` | Vulnerable modifier | `PaperPhrog.cs`: Uncommon :12, `ModifyVulnerableMultiplier` :16-27 | `IVulnerableMultiplierModifier`: target is the owner → unchanged; not a powered attack → unchanged; else `+ 0.25m`. No melted check (the game's method has none; `VulnerablePower` finds it with `Player.GetRelic<T>`, which does not skip melted relics, `Entities.Players/Player.cs:949-952`) |
| `PAPER_KRANE` | Weak modifier | `PaperKrane.cs`: Rare :12, `ModifyWeakMultiplier` :16-27 | `IWeakMultiplierModifier`: target is not the owner → unchanged; not powered → unchanged; else `− 0.15m` |
| `NUNCHAKU` | counter relic (card hook) | `Nunchaku.cs`: Uncommon :165, `ShowCounter` :167, `DisplayAmount` :169-179, Cards 10, Energy 1 :181-185, `IsActivating` :189-201, `AttacksPlayed` saved :203-216, `UpdateDisplay` :218-230, `AfterCardPlayed` :232-244 (state machine :56-72), `DoActivateVisuals` :124-142 | the owner's attack → `AttacksPlayed++` (setter runs `UpdateDisplay`: Active at `Cards − 1` mod `Cards`); in progress and a multiple of 10 → `DoActivateVisuals`, `PlayerCmd.GainEnergy(Energy)`. The owner test is `card.HasOwner && card.Owner == Owner`: identical to the game for owned cards; P0 combat cards are still ownerless until B12 A23 lands |

`DoActivateVisuals` (HappyFlower, Nunchaku) is started with `TaskHelper.RunSafely` before the energy gain; under
NonInteractiveMode its `Cmd.Wait(1f)` completes at once, so it runs `IsActivating = true`, `Flash` (UI, stripped),
`IsActivating = false` synchronously; the sim keeps the two assignments (Nunchaku's setter recomputes `Status`).

Potions:

| Potion | Role | Game | Port |
| --- | --- | --- | --- |
| `ATTACK_POTION` | `ChooseCard` prompt | `Models.Potions/AttackPotion.cs`: Common :124, CombatOnly :126, AnyPlayer :128, `OnUse` state machine :69-101 | `AssertValidForTargetedPotion`; `player = target.Player`; `CardFactory.GetDistinctForCombat(player, character pool GetUnlockedCards(...).Where(Attack), 3, Rng.CombatCardGeneration)` (cards-kernel; §9); `CardSelectCmd.FromChooseACardScreen(cards, player, canSkip: true)`; a pick → `SetToFreeThisTurn`, `CardPileCmd.AddGeneratedCardToCombat(card, Hand, Owner)` |
| `ASHWATER` | hand prompt | `Ashwater.cs`: Uncommon :148, CombatOnly :150, AnyPlayer :152, `OnUse` :70-118 | assert; `PlaySplashVfx` stripped (:71); `CardSelectCmd.FromHand(prefs(0, 999999999), target.Player, null, source)`; `CardCmd.Exhaust` each selected card in result order (:106-117) through the P0 port `CardPileCmd.Exhaust` (§11 item 8). The result is listed first (the game's result is a materialized list, `Commands/CardSelectCmd.cs:2072-2127`) |
| `FAIRY_IN_A_BOTTLE` | `Automatic` potion | `FairyInABottle.cs`: Rare :142, Automatic :144, Self :146, `CanBeGeneratedInCombat` false :148, `ShouldDie` :163-170, `AfterPreventingDeath` :172-184 (:46), `OnUse` :107-108 | `ShouldDie` false only for the owner's creature; `AfterPreventingDeath` runs `OnUseWrapper(creature)` inside `ChoiceContextKind.Throwing` (`ThrowingPlayerChoiceContext`, which throws on a prompt, `GameActions.Multiplayer/ThrowingPlayerChoiceContext.cs:12-16`); `OnUse` heals `Math.Max(MaxHp × 0.3m, 1m)` (`CreatureCmd.Heal`, truncated by `HealInternal`). `CanUse` is false (Automatic, B12 A2) |
| `STRENGTH_POTION` | stat potion | `StrengthPotion.cs`: Common :91, CombatOnly :93, AnyPlayer :95, `StrengthPower` 2 :97, `OnUse` :55-57 | assert; splash stripped; `PowerCmd.Apply(STRENGTH_POWER, target, 2, Owner, null)`. The game calls `Apply<StrengthPower>` (`Commands/PowerCmd.cs:1214-1239`); the sim applies by id through the non-generic `Apply` (A16's pattern), identical for a non-zero amount (both check `IsEnding` and `CanReceivePowers`, then stack on the existing instance or apply a new one) |
| `BLOCK_POTION` | stat potion | `BlockPotion.cs`: Common :85, CombatOnly :87, AnyPlayer :89, `BlockVar(12, Unpowered)` :91, `OnUse` :50-51 | `CreatureCmd.GainBlock(target, block.BaseValue, block.Props, null)` (`Commands/CreatureCmd.cs:3942-3947`) |
| `FIRE_POTION` | enemy-target potion | `FirePotion.cs`: Common :92, CombatOnly :94, AnyEnemy :96, `DamageVar(20, Unpowered)` :98, `OnUse` :55-58 | `CreatureCmd.Damage(target, 20, Unpowered, Owner.Creature, null, null)`; `NGroundFireVfx` stripped (:57) |

## 8. Clone and canonical

`CloneFor(newOwner)` = `MemberwiseClone`, owner rebound (an unowned instance stays unowned), `DynamicVars`
deep-cloned, then the subclass's `CopyMutableReferenceState`. The base's part of the game's `DeepCloneFields`
(`Models/RelicModel.cs:616-620`) sits in the non-virtual `CloneFor`, so an override (the gate's `GateCounterRelic`)
cannot skip it. Canonical form, tags `0x57xx` (relics `0x5701-0x5703`, potions `0x5711-0x5713`):

- relic: `Relic 0x5701`, id, owner combat id or −1, `IsWax`, `IsMelted`, `StackCount`, `FloorAddedToDeck`, `Status`,
  `HasBeenRemovedFromState`, `DynamicVars 0x5702` + the var set, `Extra 0x5703` + `WriteExtraCanonical`;
- potion: `Potion 0x5711`, id, owner id, `HasBeenRemovedFromState`, `DynamicVars 0x5712` + the set, `Extra 0x5713` +
  `WriteExtraCanonical`;
- pilot extras: HappyFlower `IsActivating`, `TurnsSeen`; CentennialPuzzle `UsedThisCombat`; Nunchaku `IsActivating`,
  `AttacksPlayed`.

The vars are built eagerly, so reading them never changes a hash. Every field above is covered by a mutation probe
(`CloneAndCanonicalCoverRelicAndPotionState`).

## 9. Calls into areas that are not in this tree (`Relics/PendingKernelCalls.cs`)

Three pilots call members that other tracks create in parallel and that do not exist at `263810d`. Each call goes
through one internal method that throws `NotImplementedException` naming the owner; integrate routes it to the real
member (one line each) and the W1Pending tests then run for real:

| Seam | Real member | Caller |
| --- | --- | --- |
| `CardCmdUpgrade(cs, card)` | `CardCmd.Upgrade(card)` (cards-kernel, `Commands/CardCmd.cs:1882-1946`) | `Whetstone.AfterObtained` |
| `CardCmdDiscardAndDraw(cs, cards, count)` | `CardCmd.DiscardAndDraw(cards, count)` (loop, `Combat/Loop/CardCmd.Play.cs`; `Commands/CardCmd.cs:1839`, state machine :721-961: discard each with history and `AfterCardDiscarded`, draw `count`, then auto-play the Sly cards) | `GamblingChip.AfterPlayerTurnStart` |
| `CardFactoryGetDistinctForCombat(cs, player, type, count, rng)` | `CardFactory.GetDistinctForCombat(player, pool.GetUnlockedCards(rs.Unlocks, constraint).Where(c => c.Type == type), count, rng)` (cards-kernel, `Factories/CardFactory.cs:93-102`) | `AttackPotion.OnUse` |

Tests whose path needs a hook listener that `ListenerSnapshot` does not list yet probe the listener list first
(`RequireListeners`) and throw `NotImplementedException` naming hooks, so they are PENDING until the hooks track lists
relics and potion slots, then run for real; `PaperPhrogAndPaperKraneThroughVulnerableAndWeak` probes the pipeline's
re-ported powers the same way.

## 10. Fidelity flags

| Site | Flag | Why |
| --- | --- | --- |
| `RelicCmd.Obtain` (`RelicCmd.ObtainRunLayerSite`) | `ApproximatedRule`, once per obtain | the grab-bag removals (:411-415) and `FloorAddedToDeck = TotalFloor` (:422) need run state that W1 does not have; no rule reads `FloorAddedToDeck` (only save and log code), and no relic is generated in a W1 combat |

No other knowing deviation: every other omitted step is UI, a map-point history write (null without a map point) or
listed in §1-§7 as identical.

## 11. Contract change requests and open questions

1. State: `PlayerState.RemoveRelicInternal` assigns `relic.HasBeenRemovedFromState = true`; the game calls
   `relic.RemoveInternal()` (`Entities.Players/Player.cs:913`). With that call the relic setter can become private as
   in the game (`Models/RelicModel.cs:420-427`); until then it is `internal`.
2. Hooks: `ListenerSnapshot` must list, for an active player, the unmelted relics in list order and then the filled
   potion slots in slot order, after the player's powers and before orbs and cards (`Combat/CombatState.cs:874-889`),
   with the per-element liveness `!HasBeenRemovedFromState && Owner.IsActiveForHooks` (`:1057`, `:1059`); and
   implement `BeforePotionUsed`, `AfterPotionUsed`, `AfterPotionDiscarded`, `AfterPotionProcured`,
   `ShouldProcurePotion` (`Hooks/Hook.cs:9703-9762`, `:11619-11638`).
3. Pipeline (B12 A5): find the relic as the game does, `(dealer as PlayerState)?.GetRelic("PAPER_PHROG") as
   IVulnerableMultiplierModifier` and `(target as PlayerState)?.GetRelic("PAPER_KRANE") as IWeakMultiplierModifier`
   (`GetRelic<T>` is the first relic of that type, melted or not); a scan for "any relic implementing the interface"
   would also pick up a later relic with the same interface.
4. Integrate: route the three `PendingKernelCalls` (§9).
5. Loop / choice: after `PotionActions.UsePotion` / `DiscardPotion` run the win check (§6); list potion actions with
   `CanUse(cs, slot, resolvedTarget)` and `CanDiscard`; potions are illegal at a prompt. If the loop adds
   `BeginCardOrPotionEffect` / `EndCardOrPotionEffect` / a depth-gated `CheckForEmptyHand` for card plays,
   `PotionModel`'s private copies (§2.1 steps 7, 10, 17) should call them instead.
6. W2: `RunState.TotalFloor`, relic grab bags and map-point history (then drop the §10 flag); out-of-combat potion
   use (`RequireCombat`, the `cs == null` hook paths); `IsBeforeAct3TreasureChest(RunState)`.
7. `ChoiceSource` for relics and potions uses `ChoiceSource.NoInstance`: relics and potions have no instance id
   (B1's `SourceInstanceId`); the site id is unique per relic or potion type.
8. Ashwater calls the P0 `CardPileCmd.Exhaust`, the port of `CardCmd.Exhaust` (`Commands/CardCmd.cs:1866-1880`); if the
   loop moves exhaust to `CardCmd.Exhaust` (B4), integrate updates the one call.
9. `PotionProcureResult` is a record struct plus `PotionProcureFailureReason` (scaffold §16.6 item 1), used as is;
   `TryToProcure` produces the `NotAllowed` result (`Commands/PotionCmd.cs:258-266`).
10. Scaffold §14 item 10 (contexts, M11): `OnUse` keeps `(RunState rs, …)`; combat content takes `RequireCombat(rs)`,
    so no second overload exists and out-of-combat use fails loudly until W2.
11. Scaffold §14 item 11 (`Pool`): the pool tables of §5 answer it for every relic and potion of v0.111.0 (`Pool` is no
    longer virtual, as in the game).
12. Scaffold §14 item 13 (resolved targets): kept; `CanUse` / `UsePotion` also apply the action's own resolution, which
    is idempotent for an already resolved target.
13. Scaffold §16.6 item 2 (`RelicObtained`): W2's card reward needs the event that `AddRelicInternal` drops; `RelicCmd`
    is the only non-silent caller (`Commands/RelicCmd.cs:410`), so W2 can raise it there.
14. Integration risk: `AttackPotion` draws from the whole Ironclad attack pool (`Factories/CardFactory.cs:93-102`); in
    W1 only a few cards are registered, so once the gates use the potion, cards-kernel's `GetDistinctForCombat` must
    either create every pool member or flag the draw. Same for the cross-area APIs the pilots call
    (`IVulnerableMultiplierModifier`, `IWeakMultiplierModifier`, `DynamicVar.Props`, `CardPileCmd.Draw` / `Exhaust` /
    `AddGeneratedCardToCombat` / `CompareForShuffle`, `PlayerCmd.GainEnergy`): a signature change in a parallel track
    breaks this track's compile at integrate.

## 12. Round-2 findings (`verify-notes-relics-potions.md`)

| Finding | Resolution |
| --- | --- |
| [high] W1 track absent | Delivered: `RelicCmd`, `PotionCmd`, `PotionActions`, `PotionFactory`, the bases, 7 relics, 6 potions, 47 tests, this spec |
| [medium] Contract entry points still throw (`PotionActions`, `IsValidTarget`, `OnUseWrapper`, `Pool`, `MerchantCost`) | All implemented (§1, §2, §6); `Pool` through the pool tables (§5) |
| [low] B5 names `PotionFactory.GetDistinctForCombat` | Ported the v0.111.0 members with the caller's `Rng` and the one-float-one-item draw (§5); `GetDistinctForCombat` is `CardFactory`'s (AttackPotion, §9) |
| [low] FairyInABottle counted as a relic | It is a potion (§7); 7 relics without it |
| [low] `OnUseWrapper` order incomplete | §2.1, with the slot removal before `BeforePotionUsed`, the effect-depth bracket, both gates and the strip list |
| [low] `UsePotionAction` citation / default targets | `ResolveTarget` ports :77-105, `CanUse` and `UsePotion` the :117 / :132 gate, `ManualUseTarget` :609-612 (§2, §6) |
| [low] PaperPhrog consult must be dealer/target, not `cs.Player` | The relics implement the interfaces with their own guards (§7); the lookup is the pipeline's (§11 item 3); a probe test covers both powers |
| [low] Scaffold base deviations (setters, `DynamicVars`, `IsTradable`, `IsBeforeAct3TreasureChest`) | `StackCount` private behind `IncrementStackCount`; potion flag private behind `Discard` / `RemoveBeforeUse`; relic flag `internal` (§11 item 1); `DynamicVars` per instance, cloned and hashed (§8); `IsTradable` and `IsBeforeAct3TreasureChest` ported (§1) |
| [low] RelicCmd/PotionCmd need Player internals | Phase 0b added them (B12 A10); the commands call them (§3, §4) |
| [low] Spec missing / strip list | This file; strip lists in §1-§7 and §13 |
| [high] No `Test_RelicPotion_*` suite | 47 tests (§14) |
| [high] Potion decision path stubbed | `PotionActions` implemented; listing them is the choice track's `LegalActionGenerator` (§6, §11 item 5) |
| [medium] Bases not in full | §1, §2 |
| [medium] Relics and potions are no listeners; hooks / history / depth stubs | Listener order and the dispatchers are the hooks track's (§11 item 2); `AppendPotionUsed` is implemented (phase 0b); effect depth via `cs.CardOrPotionEffectDepth` (§2.1); the tests that need the listeners probe and stay PENDING until hooks lands (§9) |
| [low] No PlayerState relic/potion mutators | B12 A10 (phase 0b) |
| [low] Contract text omits potion-path gates | Followed B12 A3/A4 (§2.1, §6) |

## 13. Stripped VFX-only overloads and steps

No `RelicCmd` / `PotionCmd` overload is marked `strip` in `kernel-gaps.json → cmd_facades`. Stripped steps inside
ported methods: `RelicCmd.Obtain` `AnimateRelic`, `relic_get.mp3`, `MarkRelicAsSeen` (`Commands/RelicCmd.cs:416-421`);
`PotionCmd.TryToProcure` `gain_potion.mp3`, `AnimatePotion`, `MarkPotionAsSeen` (`Commands/PotionCmd.cs:271-277`);
`OnUseWrapper` `PushModel` / `PopModel`, `WaitForUnpause`, `NItemThrowVfx` + `Cmd.Wait(0.5f)`, `InvokeExecutionFinished`
(`Models/PotionModel.cs:149`, `:150`, `:276-317`, `:244`, `:338`); `EnqueueManualUse`'s `BeforeUse`, `IsQueued`,
`RequestEnqueue` (:601-616); `UsePotionAction` / `DiscardPotionGameAction` logging and `CancelAction` UI refresh
(`UsePotionAction.cs:106`, `:119-120`, `:271-279`; `DiscardPotionGameAction.cs:62`, `:73`, `:142-150`); relic `Flash`
calls (HappyFlower, CentennialPuzzle :63, Nunchaku) and the potions' `PlaySplashVfx` / `NGroundFireVfx`
(`StrengthPotion.cs:56`, `Ashwater.cs:71`, `FirePotion.cs:57`); the `Status` / `DisplayAmountChanged` UI events. Not
executed in W1 (run layer, W2 adds): the map-point history writes (`RelicCmd.cs:409`, `:255`; `PotionCmd.cs:47`,
`:270`; `PotionModel.cs:321`).

## 14. Tests (`sim/Sts2Sim.Tests/RelicPotionTests.cs`, filter `RelicPotion`)

47 tests. 35 run now; 12 are written in full and wrapped in `W1Pending.Run` (they print `PENDING` until the named
track lands):

| PENDING test | Waits for |
| --- | --- |
| `OnUseWrapperRunsTheTracedOrder`, `UsePotionDrinksTheSlotAndRecordsHistory` | hooks: `BeforePotionUsed` / `AfterPotionUsed` |
| `TryToProcureFillsTheFirstEmptySlot`, `DiscardEmptiesTheSlotBeforeTheHook` | hooks: `ShouldProcurePotion`, `AfterPotionProcured`, `AfterPotionDiscarded` |
| `RelicsAndPotionSlotsAreCombatListenersInGameOrder`, `UsedPotionIsNotItsOwnListener`, `FairyInABottlePreventsTheOwnersDeathThroughCreatureCmd` | hooks: relics and potion slots in the listener list |
| `PaperPhrogAndPaperKraneThroughVulnerableAndWeak` | pipeline: the B12 A5 re-port of Vulnerable / Weak |
| `GamblingChipDiscardsThePickedCardsAndDrawsAsMany` | choice (`FromHandForDiscard`), then loop (`DiscardAndDraw`) |
| `AshwaterExhaustsTheChosenHandCards` | choice (`FromHand`) |
| `AttackPotionOffersThreeAttacksAndMakesThePickFree` | hooks, cards-kernel (`GetDistinctForCombat`), choice |
| `WhetstoneUpgradesTwoNicheShuffledDeckAttacks` | cards-kernel (`CardCmd.Upgrade`) |

Running now: base defaults, `MerchantCost`, `IsTradable`, the private mutators, `IsBeforeAct3TreasureChest`; pool
order, sizes, first-pool resolution, epoch gates, canonical rows against the registered types; potion options, the
draw order on 40 seeds against an independent mirror (one float, one item, pick removal, 2 RNG draws per pick), the
rarity thresholds on 300 seeds, the in-combat filter and blacklist on 200 seeds, the game's null pick when a blacklist
empties the rolled rarity; the `IsValidTarget` truth table (10 types × 6 targets), `IsSingleTarget`,
`ManualUseTarget`, `ResolveTarget` in and out of combat; the `CanUse` and
`CanDiscard` truth tables (slots, side, progress, the four usages, `CanUseOrRemovePotions`,
`PassesCustomUsabilityCheck`, dead owner, targets) and for the pilot potions; the cancel paths of `UsePotion` and
`DiscardPotion` (state unchanged) and their bounds; `Discard` / `RemoveBeforeUse` internals; `CanThrowAtAlly`;
`OnUseWrapper` removing the potion before any hook; `RelicCmd` obtain / insert / remove / replace / melt with their
throws, the fidelity flag, clone isolation; the clone and canonical coverage of every relic and potion field; the
pilots' ids, rarities, pools, vars; HappyFlower over two combats, CentennialPuzzle's gates and reset, Nunchaku's tenth
attack, PaperPhrog / PaperKrane guards (including a melted PaperPhrog), GamblingChip's owner and turn gates, Strength
(2, stacking to 4), Block (12), Fire (20 unpowered, ignoring Strength), Fairy's heal (80 → 24, 25 → 7, 3 → 1, 1 → 1) and
`ShouldDie` guard; out-of-combat potion effects throw `NotImplementedException` (W2).

## 15. Gates and measurements

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 419 passed, 2 failed, 421 total; `PENDING total: 12`, every one through `W1Pending`. The 2 failures are the known content gates `Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered` (other tracks' content; the relic and potion entries are no longer missing) |
| `-- RelicPotion` | 47 passed, 12 PENDING |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundles) | 4/4 bundles agree, 69/69 decisions, outcome 4/4, 0 soft diffs |
| F1 / F4 / F5 | pass with the 7 registered relics and 6 registered potions equipped (clone, canonical and invariants); potion and relic hooks do not fire in this tree (listeners: hooks track) |
| Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`), base `263810d` (`git archive`) and this tree interleaved, 3 runs each, medians | machine under heavy load from parallel tracks (load average 34-80): base `Clone()` 15.96 µs, `StateHash()` 5.76 µs, 93 487 decisions/s; this tree 13.66 µs, 6.61 µs, 97 096 decisions/s; same playout sink hash `283861034CA36B40`. The bench combat holds no relic or potion, so this track adds no work on its path; the absolute numbers are load (the integration bench recorded 2.65 µs / 1.13 µs / 668k on a quiet machine). Gates: ≥ 300 decisions/s and `Clone()` ≤ 1 ms hold |
