# W1 phase-0 scaffold (CONTRACTS Part B §B11 row "scaffold (phase 0)")

2026-11-01 · branch `claude/sim-source-recreation-9392df` on top of `83c7ee9`. Rule truth: decompile v0.111.0
(`<ns-dir>/<File>.cs:<line>`, `MegaCrit.Sts2.Core.` dropped). This file records every member the scaffold
added, the source line of its signature, every mapping decision and every signature that could not be mapped
cleanly (§14). Bodies are stubs (`NotImplementedException` naming the owner and the source line) unless §12
lists them.

Gates at commit time: `dotnet build sim/Sts2Sim.sln` 0 warnings 0 errors; `dotnet run --project sim/Sts2Sim.Tests`
367/367; diff gate 4/4 bundles agree, 69/69 decisions. Counts: 182 `AbstractModel` hook virtuals
(100 kept + 82 new), 148 public `Hook` dispatcher names (77 kept + 71 new stubs).

## 1. Mapping rules (decisions)

| # | Decision | Source / reason |
| --- | --- | --- |
| M1 | First parameter of a **new** virtual: `CombatState cs` when its `hooks.json` scope is `combat`, `RunState rs` for `run` and `both`. The 100 existing virtuals keep `CombatState cs`, including the 21 existing `both`-scope ones (`BeforeCombatStart[Late]`, `AfterCurrentHpChanged`, `BeforeDamageReceived`, `AfterDamageReceived[Late]`, `BeforeDeath`, `AfterDeath`, `AfterModifyingDamageAmount`, `AfterModifyingHpLost{Before,After}Osty`, `AfterPreventingDeath`, `ModifyDamage{Additive,Cap,Multiplicative}`, `ModifyHpLost{Before,After}Osty[Late]`, `ShouldDie[Late]`) | brief step 2; Part B §B3 |
| M2 | The three hooks no dispatcher calls (`hooks.json` scope `unknown`): `AfterAddToDeckPrevented` → `RunState rs` (called on the preventer while adding to the deck, `Commands/CardPileCmd.cs:406`, `:3381`); `AfterModifyingCardPlayResultLocation` → `CombatState cs` (combat; overridden by `Models.Powers/NostalgiaPower.cs:54`, `ReboundPower.cs:178`); `AfterTargetingBlockedVfx` → `CombatState cs` (UI only, `Nodes.Combat/NTargetManager.cs`; declared for the count, never dispatched) | |
| M3 | Dispatcher parameters: the game's `IRunState runState, ICombatState? combatState` → `RunState rs, CombatState? cs`; `ICombatState combatState` → `CombatState cs`; every `PlayerChoiceContext` / `HookPlayerChoiceContext` dropped. Only reorder: `BeforeCombatRewardOffered(RewardsSet rewards, IRunState runState, CombatRoom room)` (`Hooks/Hook.cs:9100`) becomes `(RunState rs, RewardsSet rewards, CombatRoom room)` so the context is first | Part A §1 |
| M4 | `out IEnumerable<AbstractModel> modifiers` / `out List<AbstractModel> modifyingModels` → a plain overload plus a `ref List<AbstractModel>? <same name>` overload (Part A §4.6): `ModifyCardPlayResultLocation`, `ModifyOrbPassiveTriggerCount`, `ModifyGoldGained` (Part B §B3) and the run-layer `ModifyCardBeingAddedToDeck`, `TryModifyCardRewardOptions`. `out AbstractModel? preventer` kept on `ShouldAddToDeck` (plus a plain overload). Run-layer dispatchers that **return** their modifier list (`ModifyCardRewardAlternatives`, `ModifyRestSiteOptions`, `ModifyRestSiteHealRewards`, `ModifyRewards`) keep the return type `IEnumerable<AbstractModel>` | `Hooks/Hook.cs:10121`, `:10188`, `:10213`, `:10266`, `:10501`, `:10840`, `:10972`, `:10994`, `:11016`, `:11170` |
| M5 | Side-turn hooks (D12): the 8 virtuals (`BeforeSideTurnStart`, `AfterSideTurnStart[Late]`, `BeforeSideTurnEnd{VeryEarly,Early,}`, `AfterSideTurnEnd[Late]`) and the 4 dispatchers take `IReadOnlyList<CreatureState> participants` (the game's turn-end hooks take `IEnumerable<Creature>`; Part B §B3 fixes `IReadOnlyList`). The existing `(cs, side)` dispatchers stay as forwarding overloads that pass the live side list (`cs.Monsters` for `Enemy`, else `cs.Allies`), allocation-free. Exact for every P0 caller: no pets, no extra turns, and no ported override reads `participants` (the four power overrides got the new parameter, bodies unchanged). The loop agent replaces the calls with the game's snapshots: turn start = `PlayersTakingExtraTurn` creatures or `CreaturesOnCurrentSide` (`Combat/CombatManager.cs:4873-4882`), player turn end = the ending players' creatures only (list `:2649`, `:2860`; calls `:2457`, `:2967`), enemy turn end = a copy of `CreaturesOnCurrentSide` (`:2055-2088`); the hooks agent removes the forwarding overloads afterwards | `Models/AbstractModel.cs:638-688` |
| M6 | `ShouldPlay` virtual and dispatcher take `AutoPlayType autoPlayType` (game dispatcher order `(combatState, card, out preventer, autoPlayType)`, `Hooks/Hook.cs:11554`); the existing `(cs, card)` and `(cs, card, out preventer)` overloads forward `AutoPlayType.None`, which is what `CardModel.CanPlay` passes (`Models/CardModel.cs:3351`); `CardCmd.AutoPlay` passes its type (`Commands/CardCmd.cs:395`) | `Models/AbstractModel.cs:1042` |
| M7 | Types: `Creature`→`CreatureState`, `Player`→`PlayerState`, `CardModel`→`CardInstance`, `PowerModel`/`RelicModel`/`PotionModel`/`OrbModel`/`EnchantmentModel`/`AfflictionModel`→the sim bases, run-layer types→`Run/*` shells, `MoveState`→`MoveStateDef` (history), `Task`→`void`, `Task<T>`→`T`, `IEnumerable`/`IReadOnlyList`/`IReadOnlySet`/`ISet`/`ICollection`/`List` kept. Exceptions: `ModifyMerchantCardPool` options are canonical pool cards (`Factories/CardFactory.cs:42`, `:60`) → `IEnumerable<CardDef>`; `LocString` (only in `ModifyExtraRestSiteHealText`, rest-site UI text) → `string` (localization key); `ResourceInfo` (`Entities.Cards/ResourceInfo.cs:6`) → `readonly record struct ResourceInfo(int EnergySpent, int EnergyValue, int StarsSpent, int StarValue)` in `Enums/CardEnums.cs`; `CardLocation` → the existing `Cards/CardDef.cs` struct | |
| M8 | `AfterCombatEnd`, `AfterCombatVictoryEarly`, `AfterCombatVictory` (virtuals and dispatchers) take `CombatRoom? room`: the game's parameter is non-null, but a standalone sim combat has no room (Part B §B6) | `Models/AbstractModel.cs:318`, `:328`, `:333` |
| M9 | `Run/*` shells: one file per type headed `// W2 fills`, namespace `Sts2Sim.Core.Run`, no members except `RunState`. Abstract game types are `abstract partial` (`AbstractRoom`, `Reward`, `MerchantEntry`, `RestSiteOption`, `EventModel`, `AncientEventModel : EventModel`, `ActModel`, `ActMap`), concrete ones `sealed partial` (`CombatRoom : AbstractRoom`, `CardReward : Reward`, `RewardsSet`, `CardRewardAlternative`, `CardCreationOptions`, `CardCreationResult`, `MapPoint`, `MapPointHistoryEntry`, `RunState`). Deviation from the brief's "sealed partial class": the game passes a `CombatRoom` where `AbstractRoom` is expected and `CardReward` where `Reward` is, and W2 must be able to add subclasses in new files. `ActModel`/`EventModel` do not derive from the sim `AbstractModel` (no run listener reads them) | §6 |
| M10 | `CardSelectCmd`'s `AbstractModel source` → `ChoiceSource source` (`readonly record struct ChoiceSource(string SiteId, string SourceId, int SourceInstanceId)`, `ChoiceSource.For(card, "OnPlay")`): sim cards are not `AbstractModel`s (42 of the 61 prompt sites are cards) and `ChoicePrompt` needs exactly these three fields (Part B §B1). The three methods without a source in the game (`FromChooseACardScreen`, `FromSimpleGrid`, `FromCombatPile`) get a trailing optional `ChoiceSource source = default`; the game's parameters keep their order (§14 item 1) | `Commands/CardSelectCmd.cs:3169-3744` |
| M11 | Per-kind lifecycle methods take the context where the game reads it: `PotionModel.OnUse`/`OnUseWrapper(RunState rs, CreatureState? target)` (the wrapper runs in and out of combat, `combatState` may be null, `Models/PotionModel.cs:148`, `:245-249`); `RelicModel.AfterObtained/AfterRemoved/IsAllowed(RunState rs)`, `IsAllowedAtNeow(RunState rs, PlayerState player)`; orb, enchantment and affliction effects take `CombatState cs`. Properties without a context stay properties | |
| M12 | Clone pattern (Part A §5–§6, PowerModel): owned models expose `CloneFor(newOwner)` = `MemberwiseClone` + owner rebound + `CopyMutableReferenceState(source)`: relics, potions, orbs (`PlayerState? newOwner`), enchantments, afflictions (`CardInstance? newCard`); an unowned instance stays unowned. B5's `RelicModel.DeepClone()` is this `CloneFor`. `ModifierModel.DeepClone()` (no owner). Canonical = `WriteCanonical` (tag, id, owner id or -1, state fields) + `WriteExtraCanonical` | |
| M13 | UI-only per-kind virtuals are not declared (Part A §1 strip): relic `Title` (:60), `IconBaseName` (:146), `PackedIconPath` (:148), `PackedIconOutlinePath` (:150), `BigIconPath` (:152), `FlashSfx` (:358), `ShouldFlashOnPlayer` (:360), `ExtraHoverTips` (:383); potion `ExtraHoverTips` (:510); orb `PassiveSfx`/`EvokeSfx`/`ChannelSfx` (:255-259), `ExtraHoverTips` (:265); enchantment `HasExtraCardText` (:48), `ShowAmount` (:119), `DisplayAmount` (:121), `ShouldGlowGold`/`ShouldGlowRed` (:214/:216), `ExtraHoverTips` (:237); affliction `HasExtraCardText` (:41), `ExtraHoverTips` (:140); modifier `HoverTips`, `Title`, `Description`, `NeowOptionTitle/Description`, `IconPath` (:30-54). `PotionModel.IsQueued` (:486, action-queue UI flag) is dropped: sim decisions execute immediately | `kernel-gaps.json` `model_kind_virtuals` |
| M14 | `OrbModel.PassiveVal`/`EvokeVal` are abstract properties in the game that read `Hook.ModifyOrbValue` through `Owner.Creature.CombatState` (`Models/OrbModel.cs:216-218`, `:505`) → abstract methods `decimal PassiveVal(CombatState cs)` / `EvokeVal(CombatState cs)` | |
| M15 | `CombatHistory` (D11): 17 `readonly record struct` entries mirroring `Combat.History.Entries/*Entry.cs` field for field, references replaced by ids (`CombatHistory.NoId = -1` when absent) and model ids (`CardId`, `PowerId`, `OrbId`, `PotionId`, `AfflictionId`, `MonsterId`, `MoveId`); the `CombatHistoryEntry` base fields (`Actor`, `RoundNumber`, `CurrentSide`, `_playerTurnNumbers`, `Combat.History/CombatHistoryEntry.cs:12-36`) become `HistoryStamp(Sequence, ActorId, RoundNumber, CurrentSide, PlayerTurnNumber)`; `Sequence` is sim-only (global log order across the per-type lists); one player turn number (single player). `CardPlay` → `HistoryCardPlay` (the game's `CardPlay` fields incl. `ResourceInfo`, `Entities.Cards/CardPlay.cs:14-94`), `DamageResult` → `HistoryDamageResult` (`Entities.Creatures/DamageResult.cs:9-78`). Append methods are named `Append<Entry>` (Part B §B2) and take the game's arguments with `cs` first | `Combat.History/CombatHistory.cs:74-157` |
| M16 | One `PlayerState` shared by run and combat (D3): `cs.Player` is `cs.Run.Players[0]` for standalone combats. `CombatState.Ascension` now reads `Run.Ascension`; `CombatState.Rng`/`Choice` read `Run.Rng`/`Run.Choice` | Part B §B2, D4 |
| M17 | `CombatState.CardOrPotionEffectDepth` is one `int` (the game keeps a per-player dictionary, `Combat/CombatManager.cs:1465`); `PlayersTakingExtraTurn` is a list of player combat ids (`Combat/CombatTurnState.cs:66`) | single player |
| M18 | `UnlockProfile` is shared by clones, so it must be deeply immutable for `DeterminismTests.GraphInspector`: the id sets are balanced binary trees of `readonly` nodes (no array, no BCL collection); `Digest` (XXH64 over the sorted ids and the run count) is computed once and is the whole canonical form (D5, Part B §B2) | `Unlocks/UnlockState.cs:16-139` |
| M19 | Listener adapters (`MonsterState.Listener`, `CardInstance.Listener`): a clone calls `IOwnedListener<MonsterState>.CloneFor(clone)` / `IOwnedListener<CardInstance>.CloneFor(clone)` when the adapter implements it (`State/OwnedListener.cs`), else shares the reference, which is then required to be immutable (readonly id and def fields) (§14 item 2) | Part B §B2, §B8 |
| M20 | `CardInstance.Owner` is a reference (Part B §B2) rebound on clone: `PlayerState`'s copy constructor clones its five piles with `CardPile.CloneFor(this)` and its deck with `CardInstance.CloneFor(this)`; `CardInstance.DeepClone()` keeps the reference (unchanged behaviour for direct callers) | |
| M21 | `PlayerState.HasOpenPotionSlot` is the contract's name for the game's `HasOpenPotionSlots` (`Entities.Players/Player.cs:280`). `PlayerState.CanUseOrRemovePotions` (`Player.cs:140`, `:282`, default `true`) is added beyond B2's list because B1's `DiscardPotion` legality reads it and `State/*` is frozen for the choice and relics-potions agents | |
| M22 | `GameVersion` (D8, B10): `[VersionExclusive("<version>")]`; `GameVersions.Default = "0.111.0"`; `DefDiscovery.ConcreteSubclassesOf<T>(string gameVersion)` and `DefTable/ModelTable.Build(..., gameVersion)` filter by it; the static registries are built with the default. No static mutable configuration exists; a different version means building a table with another argument | |
| M23 | `ChoiceRuntime` state: the selector stack is cloned (list copy, same selector objects) and hashed (count, then per selector its type full name and, when it implements `ICanonical`, its payload); selectors on the stack must therefore be stateless or `ICanonical` (the only content selector, `VakuuCardSelector`, is stateless, `Models.Relics/VakuuCardSelector.cs:14`). `Answerer` and the context stack are transient: not cloned, not hashed | Part B §B1 |
| M24 | `ModifierModel.IsEquivalent` default = same runtime type (the game also compares `IsCanonical`, `Models/ModifierModel.cs:103-110`; the sim has no canonical modifier instances) | |
| M25 | `CardSelectorPrefs` is a `sealed class` (Part B §B1; a struct in the game). The `LocString Prompt` becomes `string PromptKey`; `UnpoweredPreviews`, `PretendCardsCanBePlayed`, `ShouldGlowGold` (UI) are dropped; `Comparison<CardInstance>?` kept (`CardSelection/CardSelectorPrefs.cs:64`) | |

## 2. Stubbed member groups

| Group | Sim file | Members | Game source | Body owner |
| --- | --- | --- | --- | --- |
| Hook virtuals | `Combat/Pipeline/AbstractModel.cs` | 182 virtuals in game line order (§3) | `Models/AbstractModel.cs:188-1097` | hooks (defaults are final) |
| Dispatchers | `Combat/Pipeline/Hook.cs` | 71 new stubs (§4); `IterateRunHookListeners(RunState rs, CombatState? cs, List<AbstractModel> buffer)` | `Hooks/Hook.cs:8813-11741`; `Runs/RunState.cs:829-933` | hooks |
| Side-turn / `ShouldPlay` overloads | `Combat/Pipeline/Hook.cs` | `BeforeSideTurnStart`, `AfterSideTurnStart`, `BeforeSideTurnEnd`, `AfterSideTurnEnd` `(cs, side, participants)`; `ShouldPlay(cs, card, out preventer, autoPlayType)` (bodies are the P0 bodies) | `Hooks/Hook.cs:9933`, `:9948`, `:10023`, `:10038`, `:11554` | hooks |
| Decision API | `Api/GameAction.cs` | `ActionKind` + `UsePotion`, `DiscardPotion`, `ChoosePick`, `ChooseConfirm`, `ChooseCancel` (append-only); `GameAction.UsePotion(slot, target)`, `DiscardPotion(slot)`, `Pick(option)`, `Confirm`, `Cancel`; `ToString` cases `UsePotion(s[->t])`, `DiscardPotion(s)`, `Pick(i)`, `Confirm`, `Cancel` | Part B §B1 | scaffold (final) |
| Choice types | `Choice/ChoiceTypes.cs` | `ChoicePromptKind` (13), `ChoiceResultType`, `ChoiceContextKind`, `ChoiceOption`, `ChoicePrompt`, `ChoiceAnswer`, `IChoiceAnswerer`, `ChoiceSource` (M10) | Part B §B1 | frozen |
| Choice runtime | `Choice/ChoiceRuntime.cs` | `ContextScope` (disposable struct); `PushContext(kind, onFirstPause)` stub, `Ask(prompt)` stub, internal `PopContext` stub; `PushSelector`/`PopSelector`/`Selectors`/`Answerer`, `DeepClone`, `WriteCanonical` working | Part B §B1; `Commands/CardSelectCmd.cs:3084-3150` | choice |
| `CardSelectCmd` | `Choice/CardSelectCmd.cs` | `FromChooseACardScreen`, `FromSimpleGrid`, `FromCombatPile` ×2, `FromHand`, `FromHandForDiscard`, `FromHandForUpgrade` (stubs) | `Commands/CardSelectCmd.cs:3169`, `:3295`, `:3353`, `:3361`, `:3668`, `:3726`, `:3744` | choice |
| `ChoicePendingException` | `Choice/ChoicePendingException.cs` | `Prompt` | Part B §B1 | final |
| `CardSelectorPrefs` | `Choice/CardSelectorPrefs.cs` | implemented (§12) | `CardSelection/CardSelectorPrefs.cs:8-116` | final |
| Relics | `Relics/RelicModel.cs` | §8 | `Models/RelicModel.cs` | relics-potions |
| Potions | `Potions/PotionModel.cs` | §8 | `Models/PotionModel.cs` | relics-potions |
| Orbs | `Orbs/OrbModel.cs`, `Orbs/OrbQueue.cs` | §8 | `Models/OrbModel.cs`, `Entities.Orbs/OrbQueue.cs` | character-mechanics |
| Enchantments | `Cards/Enchantments/EnchantmentModel.cs` | §8 | `Models/EnchantmentModel.cs` | cards-kernel |
| Afflictions | `Cards/Afflictions/AfflictionModel.cs` | §8 | `Models/AfflictionModel.cs` | cards-kernel |
| Modifiers | `Modifiers/ModifierModel.cs` | §8 | `Models/ModifierModel.cs` | stub only (B5) |
| Potion decisions | `Combat/Loop/PotionActions.cs` | `CanUse(cs, slot, target)`, `UsePotion(cs, slot, target)`, `DiscardPotion(cs, slot)` (stubs; `target` is the resolved `CreatureState?`, like `ActionExecutor.PlayCard`) | `GameActions/UsePotionAction.cs:96-122`, `:164`; `Models/PotionModel.cs:656`; `Commands/PotionCmd.cs:283`; `Nodes.Potions/NPotionPopup.cs:285-345` | relics-potions |
| Run state | `Run/RunState.cs` | §6, §7 | `Runs/RunState.cs:26` | state (W1 body), W2 |
| Run shells | `Run/*.cs` (16) | §6 | §6 | W2 |
| Unlocks | `Unlocks/UnlockProfile.cs` | §7 | `Unlocks/UnlockState.cs:16-139` | cards-kernel (queries over pools) |
| State members | `State/CombatState.cs`, `State/CreatureState.cs`, `State/CardPile.cs`, `Cards/CardInstance.cs` | §7 | §7 | state / cards-kernel |
| Combat history | `State/CombatHistory.cs` | 17 entry structs, `HistoryStamp`, `HistoryCardPlay`, `HistoryDamageResult`, `IHistoryEntry`; per-type read lists, `CardPlaysStarted`/`CardPlaysFinished`, `Entries<T>()`, `Count`, `Clear`, `DeepClone`, `WriteCanonical` working; 17 `Append*`, `CountThisTurn<T>`, `CountLastPlayerTurn<T>`, `ByActor<T>`, `HistoryStamp.HappenedThisTurn/HappenedLastPlayerTurn` stubs | §9 | state |
| Fidelity | `State/Fidelity.cs` | `FidelityFlag { DeferredListenerPromptInline }`, `FidelityEntry(Flag, Site)`, `FidelityLog` (`Record`, `Contains`, `Entries`, `Count`, clone, canonical) | Part B §B0 D2, §B2 | state |
| Registries | `Registry/Registries.cs` | `ModelTable<T>`; `RelicRegistry`, `PotionRegistry`, `OrbRegistry`, `EnchantmentRegistry`, `AfflictionRegistry`, `ModifierRegistry` (id → factory, like `PowerRegistry`); `VersionExclusiveAttribute`, `GameVersions` (M22) | Part B §B5, §B10 | scaffold (final) |
| Namespaces | `GlobalUsings.cs` | `.Choice`, `.Relics`, `.Potions`, `.Orbs`, `.Run`, `.Unlocks`, `.Modifiers`, `.Cards.Enchantments`, `.Cards.Afflictions` | brief step 7 | final |

## 3. `AbstractModel`: the 82 new virtuals

Every default is the game's (`no-op` = `Task.CompletedTask`). The scope column is `hooks.json`; it fixes the
first parameter (M1, M2): 46 `run` and 12 `both` take `RunState rs`, 21 `combat` take `CombatState cs`, the 3
`unknown` ones follow M2.

| L | Virtual | Scope | Sim signature | Default |
|---:|---|:-:|---|---|
| 188 | `AfterActEntered` | run | `void AfterActEntered(RunState rs)` | no-op |
| 193 | `AfterAddToDeckPrevented` | unknown | `void AfterAddToDeckPrevented(RunState rs, CardInstance card)` | no-op |
| 208 | `AfterAutoPostPlayPhaseEntered` | combat | `void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)` | no-op |
| 213 | `AfterAutoPrePlayPhaseEnteredEarly` | combat | `void AfterAutoPrePlayPhaseEnteredEarly(CombatState cs, PlayerState player)` | no-op |
| 218 | `AfterAutoPrePlayPhaseEntered` | combat | `void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)` | no-op |
| 223 | `AfterAutoPrePlayPhaseEnteredLate` | combat | `void AfterAutoPrePlayPhaseEnteredLate(CombatState cs, PlayerState player)` | no-op |
| 248 | `AfterCardChangedPiles` | both | `void AfterCardChangedPiles(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)` | no-op |
| 253 | `AfterCardChangedPilesLate` | both | `void AfterCardChangedPilesLate(RunState rs, CardInstance card, PileType oldPileType, AbstractModel? clonedBy)` | no-op |
| 288 | `BeforeCardAutoPlayed` | combat | `void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type)` | no-op |
| 318 | `AfterCombatEnd` | both | `void AfterCombatEnd(RunState rs, CombatRoom? room)` | no-op |
| 323 | `BeforeCombatRewardOffered` | run | `void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)` | no-op |
| 328 | `AfterCombatVictoryEarly` | both | `void AfterCombatVictoryEarly(RunState rs, CombatRoom? room)` | no-op |
| 333 | `AfterCombatVictory` | both | `void AfterCombatVictory(RunState rs, CombatRoom? room)` | no-op |
| 378 | `AfterDiedToDoom` | combat | `void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)` | no-op |
| 398 | `BeforeCardRemoved` | run | `void BeforeCardRemoved(RunState rs, CardInstance card)` | no-op |
| 418 | `AfterGoldGained` | run | `void AfterGoldGained(RunState rs, PlayerState player)` | no-op |
| 438 | `AfterItemPurchased` | run | `void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)` | no-op |
| 443 | `AfterMapGenerated` | run | `void AfterMapGenerated(RunState rs, ActMap map, int actIndex)` | no-op |
| 458 | `AfterModifyingCardPlayResultLocation` | unknown | `void AfterModifyingCardPlayResultLocation(CombatState cs, CardInstance card, CardLocation cardLocation)` | no-op |
| 463 | `AfterModifyingOrbPassiveTriggerCount` | combat | `void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb)` | no-op |
| 468 | `AfterModifyingCardRewardOptions` | run | `void AfterModifyingCardRewardOptions(RunState rs)` | no-op |
| 483 | `AfterModifyingGoldGained` | both | `void AfterModifyingGoldGained(RunState rs, PlayerState player, decimal amount)` | no-op |
| 518 | `AfterModifyingRewards` | run | `void AfterModifyingRewards(RunState rs)` | no-op |
| 523 | `AfterOrbChanneled` | combat | `void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)` | no-op |
| 528 | `AfterOrbEvoked` | combat | `void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)` | no-op |
| 533 | `AfterOstyRevived` | combat | `void AfterOstyRevived(CombatState cs, CreatureState osty)` | no-op |
| 538 | `BeforePotionUsed` | both | `void BeforePotionUsed(RunState rs, PotionModel potion, CreatureState? target)` | no-op |
| 543 | `AfterPotionUsed` | both | `void AfterPotionUsed(RunState rs, PotionModel potion, CreatureState? target)` | no-op |
| 548 | `AfterPotionDiscarded` | both | `void AfterPotionDiscarded(RunState rs, PotionModel potion)` | no-op |
| 553 | `AfterPotionProcured` | both | `void AfterPotionProcured(RunState rs, PotionModel potion)` | no-op |
| 578 | `AfterRestSiteHeal` | run | `void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)` | no-op |
| 583 | `AfterRestSiteSmith` | run | `void AfterRestSiteSmith(RunState rs, PlayerState player)` | no-op |
| 588 | `AfterRewardTaken` | run | `void AfterRewardTaken(RunState rs, PlayerState player, Reward reward)` | no-op |
| 593 | `BeforeRoomEntered` | run | `void BeforeRoomEntered(RunState rs, AbstractRoom room)` | no-op |
| 598 | `AfterRoomEntered` | run | `void AfterRoomEntered(RunState rs, AbstractRoom room)` | no-op |
| 608 | `AfterStarsSpent` | combat | `void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)` | no-op |
| 613 | `AfterStarsGained` | combat | `void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)` | no-op |
| 618 | `AfterForge` | combat | `void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)` | no-op |
| 623 | `AfterSummon` | combat | `void AfterSummon(CombatState cs, PlayerState summoner, decimal amount)` | no-op |
| 633 | `AfterTargetingBlockedVfx` | unknown | `void AfterTargetingBlockedVfx(CombatState cs, CreatureState blocker)` | no-op |
| 713 | `ModifyCardPlayResultLocation` | combat | `CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)` | `cardLocation` |
| 718 | `ModifyOrbPassiveTriggerCounts` | combat | `int ModifyOrbPassiveTriggerCounts(CombatState cs, OrbModel orb, int triggerCount)` | `triggerCount` |
| 723 | `ModifyCardRewardCreationOptions` | run | `CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options)` | `options` |
| 728 | `ModifyCardRewardCreationOptionsLate` | run | `CardCreationOptions ModifyCardRewardCreationOptionsLate(RunState rs, PlayerState player, CardCreationOptions options)` | `options` |
| 733 | `ModifyCardRewardUpgradeOdds` | run | `decimal ModifyCardRewardUpgradeOdds(RunState rs, PlayerState player, CardInstance card, decimal odds)` | `odds` |
| 758 | `ModifyGoldGained` | both | `decimal ModifyGoldGained(RunState rs, PlayerState player, decimal amount)` | `amount` |
| 763 | `ModifyGeneratedMap` | run | `ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)` | `map` |
| 768 | `ModifyGeneratedMapLate` | run | `ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)` | `map` |
| 808 | `ModifyMerchantCardPool` | run | `IEnumerable<CardDef> ModifyMerchantCardPool(RunState rs, PlayerState player, IEnumerable<CardDef> options)` | `options` |
| 813 | `ModifyMerchantCardRarity` | run | `CardRarity ModifyMerchantCardRarity(RunState rs, PlayerState player, CardRarity rarity)` | `rarity` |
| 818 | `ModifyMerchantCardCreationResults` | run | `void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)` | `no-op` |
| 822 | `ModifyMerchantPrice` | run | `decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal cost)` | `cost` |
| 827 | `ModifyOrbValue` | combat | `decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal value)` | `value` |
| 842 | `ModifyRestSiteHealAmount` | run | `decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount)` | `amount` |
| 851 | `ModifySummonAmount` | combat | `decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source)` | `amount` |
| 861 | `ModifyNextEvent` | run | `EventModel ModifyNextEvent(RunState rs, EventModel currentEvent)` | `currentEvent` |
| 866 | `ModifyUnknownMapPointRoomTypes` | run | `IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)` | `roomTypes` |
| 871 | `ModifyOddsIncreaseForUnrolledRoomType` | run | `float ModifyOddsIncreaseForUnrolledRoomType(RunState rs, RoomType roomType, float oddsIncrease)` | `oddsIncrease` |
| 881 | `TryModifyCardBeingAddedToDeck` | run | `bool TryModifyCardBeingAddedToDeck(RunState rs, CardInstance card, out CardInstance? newCard)` | `false` |
| 887 | `TryModifyCardBeingAddedToDeckLate` | run | `bool TryModifyCardBeingAddedToDeckLate(RunState rs, CardInstance card, out CardInstance? newCard)` | `false` |
| 893 | `TryModifyCardRewardAlternatives` | run | `bool TryModifyCardRewardAlternatives(RunState rs, PlayerState player, CardReward cardReward, List<CardRewardAlternative> alternatives)` | `false` |
| 898 | `TryModifyCardRewardOptions` | run | `bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)` | `false` |
| 903 | `TryModifyCardRewardOptionsLate` | run | `bool TryModifyCardRewardOptionsLate(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)` | `false` |
| 920 | `TryModifyKeywordsInCombat` | combat | `bool TryModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)` | `false` |
| 937 | `TryModifyRestSiteOptions` | run | `bool TryModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)` | `false` |
| 942 | `TryModifyRestSiteHealRewards` | run | `bool TryModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)` | `false` |
| 947 | `TryModifyRewards` | run | `bool TryModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)` | `false` |
| 952 | `TryModifyRewardsLate` | run | `bool TryModifyRewardsLate(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)` | `false` |
| 957 | `ModifyExtraRestSiteHealText` | run | `IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> currentExtraText)` | `currentExtraText` |
| 962 | `ShouldAddToDeck` | run | `bool ShouldAddToDeck(RunState rs, CardInstance card)` | `true` |
| 967 | `ShouldAfflict` | combat | `bool ShouldAfflict(CombatState cs, CardInstance card, AfflictionModel affliction)` | `true` |
| 972 | `ShouldAllowAncient` | run | `bool ShouldAllowAncient(RunState rs, PlayerState player, AncientEventModel ancient)` | `true` |
| 987 | `ShouldAllowSelectingMoreCardRewards` | run | `bool ShouldAllowSelectingMoreCardRewards(RunState rs, PlayerState player, CardReward cardReward)` | `false` |
| 1007 | `ShouldDisableRemainingRestSiteOptions` | run | `bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player)` | `true` |
| 1027 | `ShouldGainStars` | combat | `bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player)` | `true` |
| 1032 | `ShouldGenerateTreasure` | run | `bool ShouldGenerateTreasure(RunState rs, PlayerState player)` | `true` |
| 1052 | `ShouldProceedToNextMapPoint` | run | `bool ShouldProceedToNextMapPoint(RunState rs)` | `true` |
| 1057 | `ShouldProcurePotion` | both | `bool ShouldProcurePotion(RunState rs, PotionModel potion, PlayerState player)` | `true` |
| 1067 | `ShouldRefillMerchantEntry` | run | `bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player)` | `false` |
| 1072 | `ShouldAllowMerchantCardRemoval` | run | `bool ShouldAllowMerchantCardRemoval(RunState rs, PlayerState player)` | `true` |
| 1092 | `ShouldForcePotionReward` | run | `bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)` | `false` |
| 1097 | `ShouldAllowFreeTravel` | run | `bool ShouldAllowFreeTravel(RunState rs)` | `false` |

Existing virtuals whose signature changed (all overrides and callers in the sim updated):

| L | Virtual | Change | Overrides updated |
|---:|---|---|---|
| 638 | `BeforeSideTurnStart` | `+ IReadOnlyList<CreatureState> participants` | — |
| 643 | `AfterSideTurnStart` | `+ participants` | `PipelineTests.RecordingPower` (test double) |
| 648 | `AfterSideTurnStartLate` | `+ participants` | `PipelineTests.RecordingPower` |
| 668 | `BeforeSideTurnEndVeryEarly` | `+ participants` | — |
| 673 | `BeforeSideTurnEndEarly` | `+ participants` | — |
| 678 | `BeforeSideTurnEnd` | `+ participants` | — |
| 683 | `AfterSideTurnEnd` | `+ participants` | `WeakPower`, `VulnerablePower`, `FrailPower`, `ShrinkPower` (bodies unchanged; `ShrinkPower` keeps its `IsTurnParticipant` reconstruction until the pipeline agent re-ports it, B5) |
| 688 | `AfterSideTurnEndLate` | `+ participants` | — |
| 1042 | `ShouldPlay` | `+ AutoPlayType autoPlayType` | — |

The other 91 existing virtuals are unchanged (they already had the game's names, parameter names and order,
checked by script against `hooks.json`).

## 4. `Hook`: the 71 new dispatcher stubs

Sources as in design-combat-kernel §2.1 (one entry per pass). Each stub throws
`NotImplementedException("Hook.<Name>: hooks agent (Hooks/Hook.cs:<line>)")`; the plain overloads of the
`ref`/`out` pairs (M4) forward to the throwing one.

| L | Dispatcher | Sources | Sim signature(s) |
|---:|---|---|---|
| 8813 | `AfterActEntered` | RunOnly | `void AfterActEntered(RunState rs)` |
| 8919 | `BeforeCardAutoPlayed` | Guarded | `void BeforeCardAutoPlayed(CombatState cs, CardInstance card, CreatureState? target, AutoPlayType type)` |
| 8935 | `AfterCardChangedPiles` | Run+Combat → Run+Combat | `void AfterCardChangedPiles(RunState rs, CombatState? cs, CardInstance card, PileType oldPile, AbstractModel? clonedBy)` |
| 9057 | `BeforeCardRemoved` | RunOnly | `void BeforeCardRemoved(RunState rs, CardInstance card)` |
| 9085 | `AfterCombatEnd` | Run+Combat | `void AfterCombatEnd(RunState rs, CombatState? cs, CombatRoom? room)` |
| 9100 | `BeforeCombatRewardOffered` | RunOnly | `void BeforeCombatRewardOffered(RunState rs, RewardsSet rewards, CombatRoom room)` |
| 9115 | `AfterCombatVictory` | Run+Combat → Run+Combat | `void AfterCombatVictory(RunState rs, CombatState? cs, CombatRoom? room)` |
| 9251 | `AfterGoldGained` | RunOnly | `void AfterGoldGained(RunState rs, PlayerState player)` |
| 9265 | `AfterDiedToDoom` | Combat | `void AfterDiedToDoom(CombatState cs, IReadOnlyList<CreatureState> creatures)` |
| 9339 | `AfterForge` | Guarded | `void AfterForge(CombatState cs, decimal amount, PlayerState forger, AbstractModel? source)` |
| 9385 | `AfterItemPurchased` | RunOnly | `void AfterItemPurchased(RunState rs, PlayerState player, MerchantEntry itemPurchased, int goldSpent)` |
| 9401 | `AfterMapGenerated` | RunOnly | `void AfterMapGenerated(RunState rs, ActMap map, int actIndex)` |
| 9448 | `AfterModifyingCardRewardOptions` | RunOnly | `void AfterModifyingCardRewardOptions(RunState rs, IReadOnlyList<AbstractModel>? modifiers)` |
| 9492 | `AfterModifyingGoldGained` | Run+Combat | `void AfterModifyingGoldGained(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers, PlayerState player, decimal amount)` |
| 9553 | `AfterModifyingOrbPassiveTriggerCount` | Guarded | `void AfterModifyingOrbPassiveTriggerCount(CombatState cs, OrbModel orb, IReadOnlyList<AbstractModel>? modifiers)` |
| 9598 | `AfterModifyingRewards` | RunOnly | `void AfterModifyingRewards(RunState rs, IReadOnlyList<AbstractModel>? modifiers)` |
| 9612 | `AfterOrbChanneled` | Guarded | `void AfterOrbChanneled(CombatState cs, PlayerState player, OrbModel orb)` |
| 9628 | `AfterOrbEvoked` | Guarded | `void AfterOrbEvoked(CombatState cs, OrbModel orb, IEnumerable<CreatureState> targets)` |
| 9644 | `AfterOstyRevived` | Guarded | `void AfterOstyRevived(CombatState cs, CreatureState osty)` |
| 9673 | `AfterAutoPostPlayPhaseEntered` | Guarded | `void AfterAutoPostPlayPhaseEntered(CombatState cs, PlayerState player)` |
| 9688 | `AfterAutoPrePlayPhaseEntered` | Guarded → Guarded → Guarded | `void AfterAutoPrePlayPhaseEntered(CombatState cs, PlayerState player)` |
| 9703 | `AfterPotionDiscarded` | Run+Combat | `void AfterPotionDiscarded(RunState rs, CombatState? cs, PotionModel potion)` |
| 9718 | `AfterPotionProcured` | Run+Combat | `void AfterPotionProcured(RunState rs, CombatState? cs, PotionModel potion)` |
| 9733 | `BeforePotionUsed` | Run+Combat | `void BeforePotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)` |
| 9749 | `AfterPotionUsed` | Run+Combat | `void AfterPotionUsed(RunState rs, CombatState? cs, PotionModel potion, CreatureState? target)` |
| 9846 | `AfterRestSiteHeal` | RunOnly | `void AfterRestSiteHeal(RunState rs, PlayerState player, bool isMimicked)` |
| 9861 | `AfterRestSiteSmith` | RunOnly | `void AfterRestSiteSmith(RunState rs, PlayerState player)` |
| 9875 | `AfterRewardTaken` | RunOnly | `void AfterRewardTaken(RunState rs, PlayerState player, Reward reward)` |
| 9890 | `BeforeRoomEntered` | RunOnly | `void BeforeRoomEntered(RunState rs, AbstractRoom room)` |
| 9904 | `AfterRoomEntered` | RunOnly | `void AfterRoomEntered(RunState rs, AbstractRoom room)` |
| 9963 | `AfterStarsGained` | Guarded | `void AfterStarsGained(CombatState cs, int amount, PlayerState gainer)` |
| 9978 | `AfterStarsSpent` | Guarded | `void AfterStarsSpent(CombatState cs, int amount, PlayerState spender)` |
| 9993 | `AfterSummon` | Guarded | `void AfterSummon(CombatState cs, PlayerState summoner, decimal amount)` |
| 10121 | `ModifyCardBeingAddedToDeck` | RunOnly → RunOnly | `CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card)`<br>`CardInstance ModifyCardBeingAddedToDeck(RunState rs, CardInstance card, ref List<AbstractModel>? modifyingModels)` |
| 10188 | `ModifyCardPlayResultLocation` | Guarded | `CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation)`<br>`CardLocation ModifyCardPlayResultLocation(CombatState cs, CardInstance card, bool isAutoPlay, ResourceInfo resources, CardLocation cardLocation, ref List<AbstractModel>? modifiers)` |
| 10213 | `ModifyCardRewardAlternatives` | RunOnly | `IEnumerable<AbstractModel> ModifyCardRewardAlternatives(RunState rs, PlayerState player, CardReward cardReward, List<CardRewardAlternative> alternatives)` |
| 10235 | `ModifyCardRewardCreationOptions` | RunOnly → RunOnly | `CardCreationOptions ModifyCardRewardCreationOptions(RunState rs, PlayerState player, CardCreationOptions options)` |
| 10266 | `TryModifyCardRewardOptions` | RunOnly → RunOnly | `bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions)`<br>`bool TryModifyCardRewardOptions(RunState rs, PlayerState player, List<CardCreationResult> cardRewardOptions, CardCreationOptions creationOptions, ref List<AbstractModel>? modifiers)` |
| 10309 | `ModifyCardRewardUpgradeOdds` | RunOnly | `decimal ModifyCardRewardUpgradeOdds(RunState rs, PlayerState player, CardInstance card, decimal originalOdds)` |
| 10458 | `ModifyKeywordsInCombat` | Combat | `void ModifyKeywordsInCombat(CombatState cs, CardInstance card, ISet<CardKeyword> keywords)` |
| 10501 | `ModifyGoldGained` | Run+Combat | `decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player)`<br>`decimal ModifyGoldGained(RunState rs, CombatState? cs, decimal amount, PlayerState player, ref List<AbstractModel>? modifiers)` |
| 10527 | `ModifyExtraRestSiteHealText` | RunOnly | `IReadOnlyList<string> ModifyExtraRestSiteHealText(RunState rs, PlayerState player, IReadOnlyList<string> extraText)` |
| 10545 | `ModifyGeneratedMap` | RunOnly | `ActMap ModifyGeneratedMap(RunState rs, ActMap map, int actIndex)` |
| 10564 | `ModifyGeneratedMapLate` | RunOnly | `ActMap ModifyGeneratedMapLate(RunState rs, ActMap map, int actIndex)` |
| 10732 | `ModifyMerchantCardCreationResults` | RunOnly | `void ModifyMerchantCardCreationResults(RunState rs, PlayerState player, List<CardCreationResult> cards)` |
| 10749 | `ModifyMerchantCardPool` | RunOnly | `IEnumerable<CardDef> ModifyMerchantCardPool(RunState rs, PlayerState player, IEnumerable<CardDef> options)` |
| 10767 | `ModifyMerchantCardRarity` | RunOnly | `CardRarity ModifyMerchantCardRarity(RunState rs, PlayerState player, CardRarity rarity)` |
| 10785 | `ModifyMerchantPrice` | RunOnly | `decimal ModifyMerchantPrice(RunState rs, PlayerState player, MerchantEntry entry, decimal result)` |
| 10803 | `ModifyNextEvent` | RunOnly | `EventModel ModifyNextEvent(RunState rs, EventModel currentEvent)` |
| 10822 | `ModifyOddsIncreaseForUnrolledRoomType` | RunOnly | `float ModifyOddsIncreaseForUnrolledRoomType(RunState rs, RoomType roomType, float oddsIncrease)` |
| 10840 | `ModifyOrbPassiveTriggerCount` | Guarded | `int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount)`<br>`int ModifyOrbPassiveTriggerCount(CombatState cs, OrbModel orb, int triggerCount, ref List<AbstractModel>? modifyingModels)` |
| 10865 | `ModifyOrbValue` | Guarded | `decimal ModifyOrbValue(CombatState cs, OrbModel orb, decimal amount)` |
| 10953 | `ModifyRestSiteHealAmount` | RunOnly | `decimal ModifyRestSiteHealAmount(RunState rs, CreatureState creature, decimal amount)` |
| 10972 | `ModifyRestSiteOptions` | RunOnly | `IEnumerable<AbstractModel> ModifyRestSiteOptions(RunState rs, PlayerState player, ICollection<RestSiteOption> options)` |
| 10994 | `ModifyRestSiteHealRewards` | RunOnly | `IEnumerable<AbstractModel> ModifyRestSiteHealRewards(RunState rs, PlayerState player, List<Reward> rewards, bool isMimicked)` |
| 11016 | `ModifyRewards` | RunOnly → RunOnly | `IEnumerable<AbstractModel> ModifyRewards(RunState rs, PlayerState player, List<Reward> rewards, AbstractRoom? room)` |
| 11094 | `ModifySummonAmount` | Guarded | `decimal ModifySummonAmount(CombatState cs, PlayerState summoner, decimal amount, AbstractModel? source)` |
| 11132 | `ModifyUnknownMapPointRoomTypes` | RunOnly | `IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(RunState rs, IReadOnlySet<RoomType> roomTypes)` |
| 11170 | `ShouldAddToDeck` | RunOnly | `bool ShouldAddToDeck(RunState rs, CardInstance card)`<br>`bool ShouldAddToDeck(RunState rs, CardInstance card, out AbstractModel? preventer)` |
| 11193 | `ShouldAfflict` | Guarded | `bool ShouldAfflict(CombatState cs, CardInstance card, AfflictionModel affliction)` |
| 11214 | `ShouldAllowAncient` | RunOnly | `bool ShouldAllowAncient(RunState rs, PlayerState player, AncientEventModel ancient)` |
| 11256 | `ShouldAllowMerchantCardRemoval` | RunOnly | `bool ShouldAllowMerchantCardRemoval(RunState rs, PlayerState player)` |
| 11277 | `ShouldAllowSelectingMoreCardRewards` | RunOnly | `bool ShouldAllowSelectingMoreCardRewards(RunState rs, PlayerState player, CardReward reward)` |
| 11405 | `ShouldDisableRemainingRestSiteOptions` | RunOnly | `bool ShouldDisableRemainingRestSiteOptions(RunState rs, PlayerState player)` |
| 11491 | `ShouldGenerateTreasure` | RunOnly | `bool ShouldGenerateTreasure(RunState rs, PlayerState player)` |
| 11512 | `ShouldGainStars` | Guarded | `bool ShouldGainStars(CombatState cs, decimal amount, PlayerState player)` |
| 11598 | `ShouldProceedToNextMapPoint` | RunOnly | `bool ShouldProceedToNextMapPoint(RunState rs)` |
| 11619 | `ShouldProcurePotion` | Run+Combat | `bool ShouldProcurePotion(RunState rs, CombatState? cs, PotionModel potion, PlayerState player)` |
| 11640 | `ShouldRefillMerchantEntry` | RunOnly | `bool ShouldRefillMerchantEntry(RunState rs, MerchantEntry entry, PlayerState player)` |
| 11703 | `ShouldForcePotionReward` | RunOnly | `bool ShouldForcePotionReward(RunState rs, PlayerState player, RoomType roomType)` |
| 11722 | `ShouldAllowFreeTravel` | RunOnly | `bool ShouldAllowFreeTravel(RunState rs)` |

The 77 existing dispatchers keep their bodies; the only edits are the §2 overloads, the four side-turn bodies
passing `participants` to the virtuals and `ShouldPlay` passing `autoPlayType`. `IterateRunHookListeners(cs, buffer)`
(the P0 run-source fill) stays next to the new `(rs, cs, buffer)` stub.

## 5. Enums (game ordinals; `Enums/*`)

| Enum | File | Source |
| --- | --- | --- |
| `AutoPlayType { None, Default, SlyDiscard }` | `Enums/CardEnums.cs` | `Entities.Cards/AutoPlayType.cs` |
| `OrbEvokeType { None, Front, All }` | `Enums/CardEnums.cs` | `Entities.Cards/OrbEvokeType.cs` |
| `CardMultiplayerConstraint { None, MultiplayerOnly, SingleplayerOnly }` | `Enums/CardEnums.cs` | `Entities.Cards/CardMultiplayerConstraint.cs` (cards-kernel pools, B7) |
| `[Flags] KeywordSources { None = 0, Local = 2, Global = 4, All = -1 }` | `Enums/CardEnums.cs` | `Entities.Cards/KeywordSources.cs` (cards-kernel keywords, B7) |
| `ResourceInfo` (record struct, M7) | `Enums/CardEnums.cs` | `Entities.Cards/ResourceInfo.cs:6` |
| `PotionUsage { None, CombatOnly, AnyTime, Automatic }` | `Enums/RelicPotionEnums.cs` | `Entities.Potions/PotionUsage.cs` |
| `PotionRarity { None, Common, Uncommon, Rare, Event, Token }` | `Enums/RelicPotionEnums.cs` | `Entities.Potions/PotionRarity.cs` |
| `RelicRarity { None, Starter, Common, Uncommon, Rare, Shop, Event, Ancient }` | `Enums/RelicPotionEnums.cs` | `Entities.Relics/RelicRarity.cs` |
| `RelicStatus { Normal, Active, Disabled }` | `Enums/RelicPotionEnums.cs` | `Entities.Relics/RelicStatus.cs` |
| `EnchantmentStatus { Normal, Disabled }` | `Enums/RelicPotionEnums.cs` | `Entities.Enchantments/EnchantmentStatus.cs` |
| `RoomType { Unassigned, Monster, Elite, Boss, Treasure, Shop, Event, RestSite, Map }` | `Enums/RunEnums.cs` | `Rooms/RoomType.cs` |
| `MapPointType { Unassigned, Unknown, Shop, Treasure, RestSite, Monster, Elite, Boss, Ancient }` | `Enums/RunEnums.cs` | `Map/MapPointType.cs` |
| `RewardType { None, Card, Gold, Potion, Relic, RemoveCard, SpecialCard }` | `Enums/RunEnums.cs` | `Rewards/RewardType.cs` |
| `GoldLossType { None, Spent, Lost, Stolen }` | `Enums/RunEnums.cs` | `Entities.Gold/GoldLossType.cs` (`PlayerCmd.LoseGold`, `Commands/PlayerCmd.cs:896`) |
| `CardRarityOddsType { None, RegularEncounter, EliteEncounter, BossEncounter, Shop, Uniform }` | `Enums/RunEnums.cs` | `Runs/CardRarityOddsType.cs` (W2 rewards) |

`Enums/*` is frozen for the W1 owners, so the four enums no phase-0 signature names yet but B7/B4 owners need
(`CardMultiplayerConstraint`, `KeywordSources`, `GoldLossType`, `CardRarityOddsType`) are added now.
`PlayerChoiceType` (`Entities.Models/PlayerChoiceType.cs`: None, CanonicalCard, CombatCard, DeckCard,
MutableCard, Player, Index) is represented by B1's sim-only `ChoiceResultType { CombatCard, Index }`.
`CardPreviewStyle` is UI (dropped parameter). Choice enums live in `Choice/ChoiceTypes.cs` (B1).

## 6. `Run/*` shells (D7)

| Shell | Kind (M9) | Game type | Mentioned by |
| --- | --- | --- | --- |
| `RunState` | `sealed partial`, members §7 | `Runs/RunState.cs:26` | every run/both hook (M1) |
| `AbstractRoom` | `abstract partial` | `Rooms/AbstractRoom.cs:11` | `BeforeRoomEntered`, `AfterRoomEntered`, `TryModifyRewards[Late]`, `ModifyRewards` |
| `CombatRoom : AbstractRoom` | `sealed partial` | `Rooms/CombatRoom.cs:29` | `AfterCombatEnd`, `AfterCombatVictory[Early]`, `BeforeCombatRewardOffered` |
| `RewardsSet` | `sealed partial` | `Rewards/RewardsSet.cs:31` | `BeforeCombatRewardOffered` |
| `Reward` | `abstract partial` | `Rewards/Reward.cs:22` | `AfterRewardTaken`, `TryModifyRestSiteHealRewards`, `TryModifyRewards[Late]`, dispatchers |
| `CardReward : Reward` | `sealed partial` | `Rewards/CardReward.cs:38` | `TryModifyCardRewardAlternatives`, `ShouldAllowSelectingMoreCardRewards` |
| `CardRewardAlternative` | `sealed partial` | `Entities.CardRewardAlternatives/CardRewardAlternative.cs:14` | `TryModifyCardRewardAlternatives` |
| `CardCreationOptions` | `sealed partial` (a `record` in the game) | `Runs/CardCreationOptions.cs:14` | `ModifyCardRewardCreationOptions[Late]`, `TryModifyCardRewardOptions[Late]` |
| `CardCreationResult` | `sealed partial` | `Entities.Cards/CardCreationResult.cs:6` | `TryModifyCardRewardOptions[Late]`, `ModifyMerchantCardCreationResults` |
| `MerchantEntry` | `abstract partial` | `Entities.Merchant/MerchantEntry.cs:13` | `AfterItemPurchased`, `ModifyMerchantPrice`, `ShouldRefillMerchantEntry` |
| `RestSiteOption` | `abstract partial` | `Entities.RestSite/RestSiteOption.cs:17` | `TryModifyRestSiteOptions`, `ModifyRestSiteOptions` |
| `EventModel` | `abstract partial` | `Models/EventModel.cs:37` | `ModifyNextEvent` |
| `AncientEventModel : EventModel` | `abstract partial` | `Models/AncientEventModel.cs:27` | `ShouldAllowAncient` |
| `ActModel` | `abstract partial` | `Models/ActModel.cs:27` | Part B §B3 list (W2) |
| `ActMap` | `abstract partial` | `Map/ActMap.cs:9` | `AfterMapGenerated`, `ModifyGeneratedMap[Late]` |
| `MapPoint` | `sealed partial` | `Map/MapPoint.cs:10` | Part B §B3 list (W2) |
| `MapPointHistoryEntry` | `sealed partial` | `Runs.History/MapPointHistoryEntry.cs:16` | Part B §B3 list (W2) |

`RoomType`, `MapPointType`, `RewardType` are enums in `Enums/RunEnums.cs` (§5).

## 7. State additions (Part B §B2)

| Member | Sim | Game source | Body |
| --- | --- | --- | --- |
| `RunState.Players`, `Combat`, `Rng`, `Unlocks`, `Ascension`, `Modifiers`, `Choice`, `Fidelity` | `Run/RunState.cs` | `Runs/RunState.cs` | data |
| `RunState.CreateStandalone(player, rng, ascension, unlocks = null)` | `Run/RunState.cs` | Part B §B2 | implemented: `Players = [player]`, `Unlocks = unlocks ?? UnlockProfile.All`, empty modifiers, fresh `ChoiceRuntime` and `FidelityLog`, `Combat = null` until a `CombatState` attaches |
| `RunState.DeepClone()`, `WriteCanonical` | `Run/RunState.cs` | Part B §B2 | implemented (§10) |
| `CombatState(PlayerState, string, int, RunRngSet)` | `State/CombatState.cs` | Part A §4.4 | now `: this(RunState.CreateStandalone(player, rng, ascension), player, encounterId)` |
| `CombatState(RunState run, PlayerState player, string encounterId)` | `State/CombatState.cs` | added for W1/W2 callers | attaches itself with `run.Combat ??= this` |
| `CombatState.Run`, `Rng => Run.Rng`, `Choice => Run.Choice`, `Ascension => Run.Ascension` | `State/CombatState.cs` | D4, M16 | |
| `CombatState.Allies` (`[0]` = player, then pets) | `State/CombatState.cs` | `Combat/CombatState.cs:535`, `:554` | list; `AddAlly`/`RemoveAlly` stubs (the game's `AddCreature` `:993`, `CreateCreature` `:709-724`, `RemoveCreature` `:739-770` on `_allies`) |
| `CombatState.History`, `CardOrPotionEffectDepth`, `PlayersTakingExtraTurn` | `State/CombatState.cs` | `Combat/CombatManager.cs:6107`, `:1430-1509`; `Combat/CombatTurnState.cs:66` | data (M17) |
| `CombatState.CreateCard(def, owner)` | `State/CombatState.cs` | `Combat/CombatState.cs:671-677` | stub |
| `PlayerState.Gold` | `State/CreatureState.cs` | `Entities.Players/Player.cs:253` | data, default 0 (the run start sets the character's `StartingGold`, e.g. 99 for Ironclad, `Models.Characters/Ironclad.cs:32`) |
| `PlayerState.Relics` (`List<RelicModel>`), `GetRelic(id)`, `HasRelic(id)` | `State/CreatureState.cs` | `Player.cs:241`, `:949-957` | list + lookup by id |
| `PlayerState.PotionSlots` (`PotionModel?[]`), `MaxPotionCount`, `HasOpenPotionSlot` | `State/CreatureState.cs` | `Player.cs:142`, `:243`, `:280`; default 3 slots `:714` (`PlayerState.DefaultPotionSlotCount`) | array + queries (M21) |
| `PlayerState.CanUseOrRemovePotions` (default `true`) | `State/CreatureState.cs` | `Player.cs:140`, `:282` | data (M21) |
| `PlayerState.BaseOrbSlotCount` | `State/CreatureState.cs` | `Player.cs:373`; default 0 `Models/CharacterModel.cs:71` | data |
| `PlayerState.Deck` (`List<CardInstance>`, deck order) | `State/CreatureState.cs` | `Player.cs:312` | data; empty in P0 flows (the loop still fills the draw pile from the specs) |
| `PlayerState.OrbQueue`, `PetIds` | `State/CreatureState.cs` | `Entities.Players/PlayerCombatState.cs:157`, `:20`, `:39` | data |
| `MonsterState.PetOwnerId` (default `PowerModel.NoCreature`), `Listener` | `State/CreatureState.cs` | `Entities.Creatures/Creature.cs:520-537`; Part B §B8 | data (M19) |
| `CardInstance.Owner`, `HasOwner`, `AttachTo(owner)` | `Cards/CardInstance.cs` | `Models/CardModel.cs:1777` | reference, rebound on clone (M20) |
| `CardInstance.Enchantment`, `Affliction`, `Listener` | `Cards/CardInstance.cs` | `Models/CardModel.cs:2026`, `:2035` | cloned with the card (`CloneFor(clone)`) |
| `CardInstance.HasBeenRemovedFromState`, `DeckVersionId` (`NoDeckVersion = -1`) | `Cards/CardInstance.cs` | `Models/CardModel.cs:2286`, `:2272` | data |
| `CardInstance.CloneForCombat(newInstanceId)` | `Cards/CardInstance.cs` | `Entities.Players/Player.cs:1214-1233`, `Combat/CombatState.cs:679-684`, `Models/CardModel.cs:2827-2882` (`DeepCloneFields`/`AfterCloned` reset target, play index, deck version, removed flag and re-enchant) | stub (cards-kernel) |
| `CardInstance.CloneFor(PlayerState? newOwner)`, `CardPile.CloneFor(PlayerState? newOwner)` | `Cards/CardInstance.cs`, `State/CardPile.cs` | M20 | implemented |
| `UnlockProfile` | `Unlocks/UnlockProfile.cs` | `Unlocks/UnlockState.cs:16-139` | data (§12) |
| `IOwnedListener<TOwner>` | `State/OwnedListener.cs` | M19 | interface |

`UnlockProfile` contents: `None` (`:16`), `All` (`:18`: every epoch id of `EpochModel.AllEpochIds`, the 57 ids
of `Timeline/EpochModel.cs:211-331` in that order, every encounter of `ModelDb.AllEncounters` — the four acts'
`GenerateAllEncounters` in `ModelDb.Acts` order Overgrowth, Underdocks, Hive, Glory
(`Models/ModelDb.cs:220-241`; `Models.Acts/Overgrowth.cs:71`, `Underdocks.cs:66`, `Hive.cs:71`, `Glory.cs:67`)
then the five `EventEncounters` (`Models/ModelDb.cs:170`), 85 distinct ids (slugs from
`sim/specs/inventory/encounters.json`) — and `NumberOfRuns = 999999999`); constructor
`(unlockedEpochIds, encountersSeen, numberOfRuns)` filtering epochs through `IsValidEpoch` (`:84-89`), merge
constructor (`:98-104`), `IsValidEpoch` (`:106-109`), `IsEpochRevealed` (`:111-114`, by id string),
`EpochUnlockCount` (`:116-119`), `HasSeenEncounter` (`:79-82`, by id string), `NumberOfRuns`; plus sorted
`UnlockedEpochIds()`/`EncountersSeen()` views, `AllEpochIds`, `AllEncounterIds`, `Digest`. Not ported (pool data,
cards-kernel / W2): `Characters`, `SharedAncients`, `Relics`, `Potions`, `CharacterCardPools`, `Cards`,
`CardPools` (`:31-77`); save-layer `UnlockState(ProgressState)` (`:91-96`), `ToSerializable`/`FromSerializable`
(`:121-139`).

## 8. New listener kinds (Part B §B5)

All six derive from `AbstractModel` and implement `ICanonical`; `Id` is abstract on each.

| Kind | Declared (game line) | Stubs (non-trivial game defaults) | State fields |
| --- | --- | --- | --- |
| `RelicModel` | `Rarity` (:190), `Pool` (:192, `string` pool model id), `Owner`/`HasOwner`/`AttachTo` (:225), `IsAllowedInShops` (:223), `IsUsedUp` (:244), `HasUponPickupEffect` (:246), `SpawnsPets` (:248), `IsStackable` (:250), `AddsPet` (:280), `CanonicalVars` (:305), `MerchantCost` (:307), `ShowCounter` (:354), `DisplayAmount` (:356), `IsAllowed(rs)` (:529), `IsAllowedAtNeow(rs, player)` (:534, default `IsAllowed(rs)`), `AfterObtained(rs)` (:651), `AfterRemoved(rs)` (:656) — 14 of the 22 virtuals, 8 UI ones stripped (M13) | `Pool`, `MerchantCost` (:307-318 rarity switch) | `IsWax` (:253), `IsMelted` (:267), `StackCount` = 1 (:283), `FloorAddedToDeck` (:320), `Status` (:333), `HasBeenRemovedFromState` (:421) |
| `PotionModel` | `Rarity` (:442), `Usage` (:444), `TargetType` (:446), `Pool` (:448), `Owner` (:450), `CanonicalVars` (:483), `CanBeGeneratedInCombat` (:494), `PassesCustomUsabilityCheck` (:496), `OnUse(rs, target)` (:675, protected) — 4 of 5 virtuals, `ExtraHoverTips` stripped | `Pool`, `IsValidTarget(target)` (:618-654), `OnUseWrapper(rs, target)` (:656) | `HasBeenRemovedFromState` (:532) |
| `OrbModel` | `PassiveVal(cs)`/`EvokeVal(cs)` (abstract, :216-218, M14), `Owner` (:313), `BeforeTurnEndOrbTrigger(cs)` (:470), `AfterTurnStartOrbTrigger(cs)` (:475), `Passive(cs, target)` (:495), `Evoke(cs)` (:500, returns the evoked targets), protected `ModifyOrbValue(cs, result)` (:505, forwards to `Hook.ModifyOrbValue`), `RemoveInternal()` (:517) — 4 of 8 virtuals, 4 UI stripped | `TriggerPassive(cs, target)` (:481) | `HasBeenRemovedFromState` (:221) |
| `OrbQueue` | `MaxCapacity = 10` (:386), `Orbs` (:392), `Capacity` (:395) | `Clear` (:408), `AddCapacity` (:414), `RemoveCapacity` (:419), `TryEnqueue` (:429), `Remove` (:447), `Insert` (:452), `BeforeTurnEnd(cs)` (:463), `AfterTurnStart(cs)` (:477) | orb list + capacity; `CloneFor(owner)` |
| `EnchantmentModel` | `ShouldStartAtBottomOfDrawPile` (:127), `Card`/`HasCard` (:129-149), `IsStackable` (:175), `CanonicalVars` (:191), `CanEnchantCardType` (:289), `CanEnchant` (:294), `OnPlay(cs, cardPlay)` (:317), `RecalculateValues` (:368), `OnEnchant` (:391), `EnchantBlockAdditive` (:395), `EnchantBlockMultiplicative` (:400), `EnchantDamageAdditive` (:405), `EnchantDamageMultiplicative` (:410), `EnchantPlayCount` (:415) — 13 of 19 virtuals, 6 UI stripped | `CanEnchant` (:294-315), `ApplyInternal` (:337), `ClearInternal` (:350), `ModifyCard` (:356) | `Amount` (:151), `Status` (:193) |
| `AfflictionModel` | `Card`/`HasCard` (:71-91), `CanAfflictUnplayableCards` (:117), `IsStackable` (:119), `CanAfflictCardType` (:195), `CanAfflict` (:200), `AfterApplied` (:217), `BeforeRemoved` (:221), `OnPlay(cs, target)` (:225) — 7 of 9 virtuals, 2 UI stripped | `CanAfflict` (:200-215), `PickRandomTargets` (:245), `ClearInternal` (:252) | `Amount` (:93; the game's setter also recalculates the owner's card values, cards-kernel) |
| `ModifierModel` | `ClearsPlayerDeck` (:28), `AfterRunCreated(rs)` (:95), `AfterRunLoaded(rs)` (:99), `IsEquivalent` (:103, M24), `DeepClone()` | `OnRunCreated(rs)` (:60), `OnRunLoaded(rs)` (:83) | none; `GenerateNeowOption(EventModel)` (:90, Neow, W2) and `Pick2Good1Bad` (:135, dailies) not declared |

Not declared, owner decides: `RelicModel.IsTradable` (:194), `IncrementStackCount` (:640), `Flash` (UI);
`OrbModel.GetRandomOrb(Rng)` (:402, `OrbRegistry` + `Rng.CombatOrbGeneration`); the `ToMutable`/`ToSerializable`
family of every kind (sim instances are created by the registries' factories).

## 9. Combat history entries (D11, M15)

| Entry (sim) | Fields after `HistoryStamp Stamp` | Game entry (fields / ctor) | Game append |
| --- | --- | --- | --- |
| `BlockGainedEntry` (+ `ReceiverId`) | `int Amount, ValueProp Props, HistoryCardPlay? CardPlay` | `BlockGainedEntry.cs:13-44` | `AppendBlockGained` = `CombatHistory.cs:119` |
| `CardAfflictedEntry` | `int CardInstanceId, string CardId, string AfflictionId` | `CardAfflictedEntry.cs:11-26` | `:84` |
| `CardDiscardedEntry` | `int CardInstanceId, string CardId` | `CardDiscardedEntry.cs:11-19` | `:89` |
| `CardDrawnEntry` | `int CardInstanceId, string CardId, bool FromHandDraw` | `CardDrawnEntry.cs:11-26` | `:94` |
| `CardExhaustedEntry` | `int CardInstanceId, string CardId` | `CardExhaustedEntry.cs:11-19` | `:99` |
| `CardGeneratedEntry` | `int CardInstanceId, string CardId, int CreatorId` | `CardGeneratedEntry.cs:11-26` | `:104` |
| `CardPlayFinishedEntry` | `HistoryCardPlay CardPlay, bool WasEthereal` | `CardPlayFinishedEntry.cs:12-45` | `:79` |
| `CardPlayStartedEntry` | `HistoryCardPlay CardPlay` | `CardPlayStartedEntry.cs:12-38` | `:74` |
| `CreatureAttackedEntry` | `IReadOnlyList<HistoryDamageResult> DamageResults` | `CreatureAttackedEntry.cs:11-19` | `:109` |
| `DamageReceivedEntry` (+ `ReceiverId`) | `HistoryDamageResult Result, int DealerId, int CardSourceInstanceId, string? CardSourceId` | `DamageReceivedEntry.cs:12-47` | `:114` |
| `EnergySpentEntry` | `int Amount` | `EnergySpentEntry.cs:10-18` | `:124` |
| `MonsterPerformedMoveEntry` | `string MonsterId, string MoveId, IReadOnlyList<int>? TargetIds` | `MonsterPerformedMoveEntry.cs:16-56` | `:129` |
| `OrbChanneledEntry` | `string OrbId` | `OrbChanneledEntry.cs:11-19` | `:134` |
| `PotionUsedEntry` | `string PotionId, int TargetId` | `PotionUsedEntry.cs:13-46` | `:139` |
| `PowerReceivedEntry` | `string PowerId, decimal Amount, int ApplierId` | `PowerReceivedEntry.cs:12-44` | `:144` |
| `StarsModifiedEntry` | `int Amount` | `StarsModifiedEntry.cs:10-18` | `:149` |
| `SummonedEntry` | `int Amount` | `SummonedEntry.cs:10-18` | `:154` |

The game appends only while `combatState.IsLiveCombat()` (`CombatHistory.cs:159-170`); `Clear` (`:64-72`) runs at
combat end. Queries the 45 readers use (design-combat-kernel §5): per-type lists (the game's `OfType<T>()` over
`Entries`, `:24-28`), `HappenedThisTurn` / `HappenedLastPlayerTurn` (`CombatHistoryEntry.cs:67-125`) and filters
by actor.

## 10. Clone and canonical graph

- `CombatState.DeepClone()` = `Run.CloneWithoutCombat()` (players, rng, modifiers, choice, fidelity cloned once,
  `Unlocks` shared) + `CloneInto(run, sourceRun)` (the player re-linked by its index in `Run.Players`, monsters,
  allies with `[0]` re-linked, history, counters); the clone is attached to its run when the source was.
  `RunState.DeepClone()` clones the attached combat through the same `CloneInto`. Identity after a clone:
  `clone.Player == clone.Run.Players[0]`, `clone.Allies[0] == clone.Player`, `clone.Run.Combat == clone`,
  `clone.Rng == clone.Run.Rng`, `clone.Choice == clone.Run.Choice`.
- Owners rebound on clone: cards in piles and deck, relics, potions, orbs (`CloneFor(newPlayer)`), enchantments and
  afflictions (`CloneFor(newCard)`); listener adapters per M19.
- `CombatState.WriteCanonical` = `Run.WriteRunFields` (tag `0x5D01`, ascension, unlock digest, players, rng
  presence + rng, modifiers, choice, fidelity) + `WriteCombatFields` (the P0 combat fields without ascension,
  player and rng, then the player's index in `Run.Players`, monsters, allies (player marker or pet), effect
  depth, extra-turn ids, history). `RunState.WriteCanonical` = run fields + combat presence + combat fields.
  Each shared object is written once. The two pinned hashes of
  `EndToEndTests.Test_EndToEnd_CanonicalHashesArePinnedAcrossProcesses` were refreshed for this intended
  format change (`0xA22C1E9190E84745` → `0x2D2E7E7036D7EA8F`, `0xF16FB5A5465E6826` → `0x5908EE3EB054BB48`);
  no other test pins bytes.
- New per-player canonical blocks: `PlayerPersistent` (gold, base orb slots, `CanUseOrRemovePotions`),
  `RelicList`, `PotionSlots` (per slot presence + potion), `Deck`, `PlayerCombatExtras` (orb queue, pet ids);
  monster `MonsterKernel` (pet owner id, listener presence); card `CardKernel` (owner id, deck version, removed
  flag, enchantment, affliction, listener presence).

## 11. Canonical tag allocation

| Range | File | Tags |
| --- | --- | --- |
| state `0x53xx` | `State/StateCanonicalTags.cs` | `PlayerPersistent 0x5307`, `PlayerCombatExtras 0x5308`, `RelicList 0x5309`, `PotionSlots 0x530A`, `Deck 0x530B`, `MonsterKernel 0x530C`, `AllyList 0x530D`, `CombatKernel 0x530E` |
| cards `0xCAxx` | `Cards/CardCanonicalTags.cs` | `CardKernel 0xCA07` |
| `0x57xx` | `Relics/RelicCanonicalTags.cs`, `Potions/PotionCanonicalTags.cs`, `Cards/Enchantments/EnchantmentCanonicalTags.cs`, `Cards/Afflictions/AfflictionCanonicalTags.cs`, `Modifiers/ModifierCanonicalTags.cs` | `Relic 0x5701` (relics `0x5700-0x570F`), `Potion 0x5711` (`0x5710-0x571F`), `Enchantment 0x5721` (`0x5720-0x572F`), `Affliction 0x5731` (`0x5730-0x573F`), `Modifier 0x5741` (`0x5740-0x574F`) |
| choice `0x5Axx` | `Choice/ChoiceCanonicalTags.cs` | `Runtime 0x5A01`, `Selector 0x5A02` |
| orbs `0x5Bxx` | `Orbs/OrbCanonicalTags.cs` | `Orb 0x5B01`, `Queue 0x5B02` |
| history `0x5Cxx` | `State/HistoryCanonicalTags.cs` | `History 0x5C01`, `Stamp 0x5C02`, `CardPlay 0x5C03`, `DamageResult 0x5C04`, entries `0x5C10-0x5C20` |
| run shell / unlocks / fidelity `0x5Dxx` | `Run/RunCanonicalTags.cs`, `Unlocks/UnlockCanonicalTags.cs`, `State/FidelityCanonicalTags.cs` | `RunState 0x5D01`, `PlayerList 0x5D02`, `ModifierList 0x5D03`; `Profile 0x5D10`, digest blocks `0x5D11-0x5D12`; `Log 0x5D20` |

`0x55xx`, `0x56xx`, `0x58xx`, `0x59xx` stay reserved for W2. A script check found no duplicate tag value in Core.

## 12. Behaviour the scaffold implements (everything else is a stub)

- Clone and canonical plumbing of every new type (§10), owner rebinding, `CardPile.CloneFor`,
  `CardInstance.CloneFor`.
- `RunState.CreateStandalone`; the `CombatState` constructors' run attachment.
- `UnlockProfile` data and lookups (§7).
- `CardSelectorPrefs`: both constructors, `RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect`
  (`CardSelection/CardSelectorPrefs.cs:114`, also through the single-count constructor `:99-103`, which therefore
  yields `false`), `Cancelable`, `Comparison`, the six prompt keys (`:12-22`) and `LocTable = "card_selection"` (`:10`).
- Container/queries without rules: `ChoiceRuntime.PushSelector/PopSelector`, `FidelityLog.Record/Contains`,
  `PlayerState.GetRelic/HasRelic/HasOpenPotionSlot`, `CombatHistory.Entries<T>()`/per-type lists/`Count`/`Clear`,
  `AttachTo` on owned kinds (PowerModel pattern: an owner cannot change), `OrbModel.RemoveInternal`,
  `RelicModel.IsAllowedAtNeow` default, `ModifierModel.IsEquivalent` default, `OrbModel.ModifyOrbValue` forwarding.
- Registries for the six kinds and the version filter (M22).

## 13. Measurements

Bench (`NIBBITS_WEAK`, Release, 3 s windows, three alternating runs of the parent commit `83c7ee9` and this
scaffold on the same loaded machine): `Clone()` 3.35 / 4.04 / 2.98 µs → 4.13 / 4.58 / 3.94 µs (mean 3.45 →
4.22 µs, +0.8 µs, about 17 more small allocations per clone: run state, player lists, potion slot array, orb
queue, choice runtime, fidelity log, history, allies); `StateHash()` 2.15 / 2.14 / 1.64 µs → 1.60 / 2.10 /
2.10 µs (unchanged; the unlock profile is an 8-byte digest); playouts 253k / 381k / 367k → 445k / 335k /
358k decisions/s (no regression beyond machine noise). The W1 gate compares against P0's 705k decisions/s and
2.53 µs (quiet machine) after the W1 wave.

## 14. Open questions and signatures that could not be mapped cleanly

1. **`CardSelectCmd` source (M10).** The game's `AbstractModel source` cannot carry a sim card. `ChoiceSource`
   (site id, source id, source instance id) is a scaffold choice; the choice agent may prefer an ambient
   "current model + method" tracked by the dispatchers and the play/potion wrappers, which would make the
   parameter redundant (and is needed anyway for `FromChooseACardScreen`, which has no source in the game).
2. **Listener adapters (M19).** `MonsterState.Listener` / `CardInstance.Listener` are shared by clones unless
   the adapter implements `IOwnedListener<TOwner>`; monsters-kernel and cards-kernel must pick one of the two
   (immutable id-based adapter, or `IOwnedListener`).
3. **Side-turn participants (M5).** The `(cs, side)` dispatcher overloads pass live lists, not the game's
   snapshots; correct only while no override reads `participants`. Loop agent to switch callers; hooks agent
   to delete the overloads.
4. **`ModifyExtraRestSiteHealText`** takes and returns localization keys (`string`) instead of `LocString`
   (rest-site UI text only).
5. **`CombatRoom? room`** on the combat-end hooks (M8) deviates from the game's non-null parameter; W2 passes
   the real room, standalone combats pass `null`.
6. **`ModifyMerchantCardPool`** options are canonical cards → `IEnumerable<CardDef>` (M7), not `CardInstance`.
7. **`CardCreationOptions`** is a `record` in the game; the shell is a `sealed partial class` (W2 may turn every
   part into a `partial record`).
8. **History inner lists.** `CreatureAttackedEntry.DamageResults` and `MonsterPerformedMoveEntry.TargetIds` are
   reference-typed fields inside value entries; `CombatHistory.DeepClone` copies the per-type lists, so a
   non-empty clone shares those (immutable by convention) inner lists, which `DeterminismTests.GraphInspector`
   would flag. The state agent decides between flattening them into side lists and copy-on-write (Part B §B2
   clone budget ≤ 50 µs for 500 entries).
9. **Turn numbers.** `HistoryStamp` keeps one player turn number; the game keys them by `NetId`
   (`CombatHistoryEntry.cs:12`, `:57-63`). Single player only.
10. **Potion and relic contexts (M11).** `PotionModel.OnUse(RunState rs, …)` covers out-of-combat use;
    relics-potions may want a `CombatState` overload for the combat path.
11. **Relic/potion `Pool`** is a `string` pool model id with a throwing default; pool membership data lands with
    the pools (relics-potions / W2).
12. **Selectors (M23)** must be stateless or `ICanonical`; a stateful content selector would need a clone protocol.
13. **`PotionActions.CanUse` / `UsePotion` take the resolved target** (`CreatureState?`, like
    `ActionExecutor.PlayCard`); the `TargetIndex` encoding (≥0 enemy, `NoIndex`, ≤-2 ally) is resolved by
    `LegalActionGenerator` (choice agent).
