# W1 phase-0 scaffold (CONTRACTS Part B §B11 row "scaffold (phase 0)")

2026-11-01 · branch `claude/sim-source-recreation-9392df` on top of `83c7ee9`; fix pass after the adversarial
check on top of `46d1918` (§15). Rule truth: decompile v0.111.0
(`<ns-dir>/<File>.cs:<line>`, `MegaCrit.Sts2.Core.` dropped). This file records every member the scaffold
added, the source line of its signature, every mapping decision and every signature that could not be mapped
cleanly (§14). Bodies are stubs (`NotImplementedException` naming the owner and the source line) unless §12
lists them.

Gates at commit time (scaffold and fix pass alike): `dotnet build sim/Sts2Sim.sln` 0 warnings 0 errors;
`dotnet run --project sim/Sts2Sim.Tests` 367/367; diff gate 4/4 bundles agree, 69/69 decisions. Counts: 182
`AbstractModel` hook virtuals (100 kept + 82 new; 30 of the kept ones changed signature, §3), 148 public `Hook`
dispatcher names (77 kept + 71 new stubs; 13 kept ones gained the game's run + combat context, §4).

## 1. Mapping rules (decisions)

| # | Decision | Source / reason |
| --- | --- | --- |
| M1 | First parameter of every virtual, new and existing alike: `CombatState cs` when its `hooks.json` scope is `combat`, `RunState rs` when it is `run` or `both`. Result: 100 `combat` virtuals take `cs`; 46 `run` and 33 `both` take `rs`. The 33 `both` ones are the 12 new ones and the 21 existing ones (`BeforeCombatStart[Late]`, `AfterCurrentHpChanged`, `BeforeDamageReceived`, `AfterDamageReceived[Late]`, `BeforeDeath`, `AfterDeath`, `AfterModifyingDamageAmount`, `AfterModifyingHpLost{Before,After}Osty`, `AfterPreventingDeath`, `ModifyDamage{Additive,Cap,Multiplicative}`, `ModifyHpLost{Before,After}Osty[Late]`, `ShouldDie[Late]`). The fix pass moved those 21 from `CombatState cs` to `RunState rs` (§3, §15). An override that needs the combat reads `rs.Combat`. When a combat listener is visited (power, monster, orb, combat card or its enchantment or affliction), that value is the dispatching combat and is never null. The 13 migrated dispatchers enforce `cs == rs.Combat` (M26), and the hooks agent keeps the same invariant when it fills the 10 new both-scope stubs. The P0 power overrides use `rs.Combat!` at the point of use | Part B §B3 (`RunState rs` for run-scope and both-scope hooks; the combat, when attached, is `rs.Combat`) |
| M2 | The three hooks no dispatcher calls (`hooks.json` scope `unknown`): `AfterAddToDeckPrevented` → `RunState rs` (called on the preventer while adding to the deck, `Commands/CardPileCmd.cs:406`, `:3381`); `AfterModifyingCardPlayResultLocation` → `CombatState cs` (combat; overridden by `Models.Powers/NostalgiaPower.cs:54`, `ReboundPower.cs:178`); `AfterTargetingBlockedVfx` → `CombatState cs` (UI only, `Nodes.Combat/NTargetManager.cs`; declared for the count, never dispatched) | |
| M3 | Dispatcher parameters: the game's `IRunState runState, ICombatState? combatState` → `RunState rs, CombatState? cs`; `ICombatState combatState` → `CombatState cs`; every `PlayerChoiceContext` / `HookPlayerChoiceContext` dropped. Only reorder: `BeforeCombatRewardOffered(RewardsSet rewards, IRunState runState, CombatRoom room)` (`Hooks/Hook.cs:9100`) becomes `(RunState rs, RewardsSet rewards, CombatRoom room)` so the context is first. All 23 game dispatchers with `(IRunState, ICombatState?)` have the `(RunState rs, CombatState? cs, …)` sim signature. That covers 10 new stubs (§4) and the 13 existing dispatchers that the fix pass migrated (§4, last table). For those 13, the P0 `(CombatState cs, …)` signature stays as an exact forwarding overload `(cs.Run, cs, …)`, so combat callers are unchanged. These overloads are permanent conveniences, unlike the M5 side-turn overloads | Part A §1; `Hooks/Hook.cs` (23 signatures) |
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
| M26 | The `(RunState rs, CombatState? cs, …)` body of the 13 migrated dispatchers starts with `RequireAttachedCombat(rs, cs, site)`. When `cs` is non-null and `ReferenceEquals(rs.Combat, cs)`, the P0 listener pass runs unchanged over `cs` (`ListenerSnapshot.Take(cs, ListenerSource.Run)`) and hands `rs` to the virtuals. When `cs` is null (the game's out-of-combat dispatch: HP changes, deaths, potions outside combat), it throws `NotImplementedException` naming the hooks agent: the run listeners without a combat are `IterateRunHookListeners(rs, null)`, `Runs/RunState.cs:829-933`, a stub. When `cs` is not `rs.Combat`, it throws `InvalidOperationException`: the virtuals only see `rs`, so a detached combat would give the overrides the wrong combat. No P0 or test path builds a detached combat; every combat is created standalone and attached. The three `AfterModifying*` dispatchers return before the check when `modifiers` is empty, which is exact because no listener can match an empty list. Cost: one null test and one reference compare per dispatch (§13) | Part B §B3; `Hooks/Hook.cs:9071`, `:9144`, `:9179`, `:9199`, `:9219`, `:9234`, `:9462`, `:9523`, `:9538`, `:9816`, `:10328`, `:10627`, `:11365` |
| M27 | Potion slot count. The game builds a new-run player with `initialMaxPotionSlotCount = 3` slots (`Entities.Players/Player.cs:101`, `:714`). `RunManager.SetUpNewSingleplayer` (`Runs/RunManager.cs:3647`, `:3667`) → `InitializeNewRun` (`:3826`) → `ApplyAscensionEffects(player)` per player (`:3862`, `:4827-4830`) → `AscensionManager.ApplyEffectsTo` (`Entities.Ascension/AscensionManager.cs:27-39`). At `AscensionLevel.TightBelt` (4, `Entities.Ascension/AscensionLevel.cs`) and above, that call does `player.SubtractFromMaxPotionCount(1)` (`:29-32`), so the player has 2 slots. `RunState.CreateStandalone` is the sim's new run for a standalone combat, so it applies this half: at `ascension >= TightBelt` it calls `player.SubtractFromMaxPotionCount(1)`, and every standalone combat at A4+ has 2 slots. The AscendersBane half (`:33-38`) is not applied there. It stays with the deck specs (`CombatLoop.DeckForCombat` → `StartingDecks.ApplyAscensionEffectsToDeck`): a standalone deck comes from specs that already carry the curse unless they are the bare starting deck, so adding it in `CreateStandalone` would double it. `PlayerState.AddToMaxPotionCount` / `SubtractFromMaxPotionCount` port `Player.cs:974-1017`. Growing appends empty slots. Shrinking walks from the last slot down to the new count; a potion found there moves to the first empty slot when that slot lies below the new count, else it is discarded (`DiscardPotionInternal` → `RemovePotionInternal` nulls the slot, `:1072-1096`; the `PotionDiscarded` and `MaxPotionCountChanged` events are UI-only, `Nodes.Potions/NPotionContainer.cs:299-300`, `Nodes.CommonUi/NTopBar.cs:383`). With every slot full the game indexes slot -1 and throws (`:1004-1007`); the port throws `InvalidOperationException` there. That case is unreachable in v0.111.0: the only shrink is TightBelt at run creation, on empty slots, and `PlayerCmd.LoseMaxPotionCount` (`Commands/PlayerCmd.cs:940`) has no caller. Growth is used by `PlayerCmd.GainMaxPotionCount` (`:934`; PotionBelt, PhialHolster, AlchemicalCoffer) | finding "TightBelt"; §14 item 14 |

## 2. Stubbed member groups

| Group | Sim file | Members | Game source | Body owner |
| --- | --- | --- | --- | --- |
| Hook virtuals | `Combat/Pipeline/AbstractModel.cs` | 182 virtuals in game line order (§3) | `Models/AbstractModel.cs:188-1097` | hooks (defaults are final) |
| Dispatchers | `Combat/Pipeline/Hook.cs` | 71 new stubs (§4); `IterateRunHookListeners(RunState rs, CombatState? cs, List<AbstractModel> buffer)` | `Hooks/Hook.cs:8813-11741`; `Runs/RunState.cs:829-933` | hooks |
| Side-turn / `ShouldPlay` overloads | `Combat/Pipeline/Hook.cs` | `BeforeSideTurnStart`, `AfterSideTurnStart`, `BeforeSideTurnEnd`, `AfterSideTurnEnd` `(cs, side, participants)`; `ShouldPlay(cs, card, out preventer, autoPlayType)` (bodies are the P0 bodies) | `Hooks/Hook.cs:9933`, `:9948`, `:10023`, `:10038`, `:11554` | hooks |
| Both-scope context (fix pass) | `Combat/Pipeline/Hook.cs` | 13 existing dispatchers re-keyed to `(RunState rs, CombatState? cs, …)` with `(CombatState cs, …)` forwarding overloads; `RequireAttachedCombat` / `ThrowNoAttachedCombat` (M26) | §4 last table | hooks (the `cs == null` path) |
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
| State members | `State/CombatState.cs`, `State/CreatureState.cs`, `State/CardPile.cs`, `Cards/CardInstance.cs` | §7 (fix pass: `PlayerState.AddToMaxPotionCount` / `SubtractFromMaxPotionCount`, M27) | §7 | state / cards-kernel |
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
| 308 | `BeforeCombatStart` | `CombatState cs` → `RunState rs` (fix pass, M1) | — |
| 313 | `BeforeCombatStartLate` | `cs` → `rs` | — |
| 343 | `AfterCurrentHpChanged` | `cs` → `rs` | — |
| 353 | `BeforeDamageReceived` | `cs` → `rs` | `ThornsPower` (`CreatureCmd.Damage(rs.Combat!, …)`) |
| 358 | `AfterDamageReceived` | `cs` → `rs` | `PipelineTests.RecordingPower` |
| 363 | `AfterDamageReceivedLate` | `cs` → `rs` | — |
| 368 | `BeforeDeath` | `cs` → `rs` | — |
| 373 | `AfterDeath` | `cs` → `rs` | `RavenousPower`, `ShrinkPower` (`rs.Combat!` at the point of use), `PipelineTests.RecordingPower`; direct calls in `CorpseSlugsSeapunkTests`, `WurmBeetleTests` pass `cs.Run` |
| 473 | `AfterModifyingDamageAmount` | `cs` → `rs` | `PipelineTests.FlatBonusPower` |
| 498 | `AfterModifyingHpLostBeforeOsty` | `cs` → `rs` | — |
| 503 | `AfterModifyingHpLostAfterOsty` | `cs` → `rs` | — |
| 573 | `AfterPreventingDeath` | `cs` → `rs` | `PipelineTests.PreventDeathOncePower` (`CreatureCmd.Heal(rs.Combat!, …)`) |
| 738 | `ModifyDamageAdditive` | `cs` → `rs` | `StrengthPower`, `PipelineTests.FlatBonusPower` |
| 743 | `ModifyDamageCap` | `cs` → `rs` | `PipelineTests.CapPower` |
| 748 | `ModifyDamageMultiplicative` | `cs` → `rs` | `WeakPower`, `VulnerablePower` (multiplier modifiers get `rs.Combat!`), `ShrinkPower`; direct calls in `WurmBeetleTests` pass `cs.Run` |
| 783 | `ModifyHpLostBeforeOsty` | `cs` → `rs` | — |
| 788 | `ModifyHpLostBeforeOstyLate` | `cs` → `rs` | — |
| 793 | `ModifyHpLostAfterOsty` | `cs` → `rs` | — |
| 798 | `ModifyHpLostAfterOstyLate` | `cs` → `rs` | — |
| 997 | `ShouldDie` | `cs` → `rs` | `PipelineTests.PreventDeathOncePower` |
| 1002 | `ShouldDieLate` | `cs` → `rs` | — |

The 21 `cs` → `rs` rows change only the first parameter; the override bodies are unchanged apart from reading the
combat through `rs.Combat!`. The other 70 existing virtuals are unchanged (they already had the game's names,
parameter names and order, checked by script against `hooks.json`). A script check over the final file finds the
first parameter by scope as M1 requires: `combat` 100 × `CombatState`, `run` 46 and `both` 33 × `RunState`,
`unknown` per M2.

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

The 77 existing dispatchers keep their bodies. The only edits are the §2 overloads, the four side-turn bodies
passing `participants` to the virtuals, `ShouldPlay` passing `autoPlayType`, and the fix-pass context migration
below. `IterateRunHookListeners(cs, buffer)` (the P0 run-source fill) stays next to the new `(rs, cs, buffer)` stub.

Existing both-scope dispatchers migrated to the game's context (fix pass, M3, M26). The game signature is
`(IRunState runState, ICombatState? combatState, …)` in every row. The primary sim signature holds the P0 body
with `rs` handed to the virtuals. The forwarding overload is the P0 signature, which now calls `(cs.Run, cs, …)`:

| L | Dispatcher | Primary sim signature(s) | Forwarding overload(s) |
|---:|---|---|---|
| 9071 | `BeforeCombatStart` | `void BeforeCombatStart(RunState rs, CombatState? cs)` | `(CombatState cs)` |
| 9144 | `AfterCurrentHpChanged` | `void AfterCurrentHpChanged(RunState rs, CombatState? cs, CreatureState creature, decimal delta)` | `(cs, creature, delta)` |
| 9179 | `BeforeDamageReceived` | `void BeforeDamageReceived(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource)` | `(cs, target, amount, props, dealer, cardSource)` |
| 9199 | `AfterDamageReceived` | `void AfterDamageReceived(RunState rs, CombatState? cs, CreatureState target, DamageResult result, ValueProp props, CreatureState? dealer, CardInstance? cardSource)` | `(cs, target, result, props, dealer, cardSource)` |
| 9219 | `BeforeDeath` | `void BeforeDeath(RunState rs, CombatState? cs, CreatureState creature)` | `(cs, creature)` |
| 9234 | `AfterDeath` | `void AfterDeath(RunState rs, CombatState? cs, CreatureState creature, bool wasRemovalPrevented)` (`deathAnimLength` dropped, D12) | `(cs, creature, wasRemovalPrevented)` |
| 9462 | `AfterModifyingDamageAmount` | `void AfterModifyingDamageAmount(RunState rs, CombatState? cs, CardInstance? cardSource, IReadOnlyList<AbstractModel>? modifiers)` | `(cs, cardSource, modifiers)` |
| 9523 | `AfterModifyingHpLostBeforeOsty` | `void AfterModifyingHpLostBeforeOsty(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers)` | `(cs, modifiers)` |
| 9538 | `AfterModifyingHpLostAfterOsty` | `void AfterModifyingHpLostAfterOsty(RunState rs, CombatState? cs, IReadOnlyList<AbstractModel>? modifiers)` | `(cs, modifiers)` |
| 9816 | `AfterPreventingDeath` | `void AfterPreventingDeath(RunState rs, CombatState? cs, AbstractModel preventer, CreatureState creature)` | `(cs, preventer, creature)` |
| 10328 | `ModifyDamage` | `decimal ModifyDamage(RunState rs, CombatState? cs, CreatureState? target, CreatureState? dealer, decimal damage, ValueProp props, CardInstance? cardSource, CardPlay? cardPlay, ModifyDamageHookType hookType = All)`<br>`… (…, ModifyDamageHookType hookType, ref List<AbstractModel>? modifiers)`; private `ModifyDamageInternal(RunState rs, CombatState cs, …)` | `(cs, target, dealer, damage, props, cardSource, cardPlay, hookType = All)`, `(…, hookType, ref modifiers)` |
| 10627 | `ModifyHpLost` | `decimal ModifyHpLost(RunState rs, CombatState? cs, CreatureState target, decimal amount, ValueProp props, CreatureState? dealer, CardInstance? cardSource, HpLossHookPhase phases)`<br>`… (…, HpLossHookPhase phases, ref List<AbstractModel>? modifiers)` | `(cs, target, amount, props, dealer, cardSource, phases)`, `(…, phases, ref modifiers)` |
| 11365 | `ShouldDie` | `bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature)`<br>`bool ShouldDie(RunState rs, CombatState? cs, CreatureState creature, out AbstractModel? preventer)` | `(cs, creature)`, `(cs, creature, out preventer)` |

The P0 callers (`CreatureCmd`, `CombatLoop.StartCombat`, `Intents`, tests) keep the `(cs, …)` form and are
unchanged. `ModifyDamage` still omits the game's `CardPreviewMode previewMode` and the enchantment pre-pass
(`Hooks/Hook.cs:10332-10342`), as in P0. The pre-pass belongs to the hooks and cards-kernel agents (Part B §B5).

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
| `RunState.CreateStandalone(player, rng, ascension, unlocks = null)` | `Run/RunState.cs` | Part B §B2; `Entities.Ascension/AscensionManager.cs:29-32` | implemented: at `ascension >= TightBelt` first `player.SubtractFromMaxPotionCount(1)` (fix pass, M27), then `Players = [player]`, `Unlocks = unlocks ?? UnlockProfile.All`, empty modifiers, fresh `ChoiceRuntime` and `FidelityLog`, `Combat = null` until a `CombatState` attaches |
| `RunState.DeepClone()`, `WriteCanonical` | `Run/RunState.cs` | Part B §B2 | implemented (§10) |
| `CombatState(PlayerState, string, int, RunRngSet)` | `State/CombatState.cs` | Part A §4.4 | now `: this(RunState.CreateStandalone(player, rng, ascension), player, encounterId)` |
| `CombatState(RunState run, PlayerState player, string encounterId)` | `State/CombatState.cs` | added for W1/W2 callers | attaches itself with `run.Combat ??= this` |
| `CombatState.Run`, `Rng => Run.Rng`, `Choice => Run.Choice`, `Ascension => Run.Ascension` | `State/CombatState.cs` | D4, M16 | |
| `CombatState.Allies` (`[0]` = player, then pets) | `State/CombatState.cs` | `Combat/CombatState.cs:535`, `:554` | list; `AddAlly`/`RemoveAlly` stubs (the game's `AddCreature` `:993`, `CreateCreature` `:709-724`, `RemoveCreature` `:739-770` on `_allies`) |
| `CombatState.History`, `CardOrPotionEffectDepth`, `PlayersTakingExtraTurn` | `State/CombatState.cs` | `Combat/CombatManager.cs:6107`, `:1430-1509`; `Combat/CombatTurnState.cs:66` | data (M17) |
| `CombatState.CreateCard(def, owner)` | `State/CombatState.cs` | `Combat/CombatState.cs:671-677` | stub |
| `PlayerState.Gold` | `State/CreatureState.cs` | `Entities.Players/Player.cs:253` | data, default 0 (the run start sets the character's `StartingGold`, e.g. 99 for Ironclad, `Models.Characters/Ironclad.cs:32`) |
| `PlayerState.Relics` (`List<RelicModel>`), `GetRelic(id)`, `HasRelic(id)` | `State/CreatureState.cs` | `Player.cs:241`, `:949-957` | list + lookup by id |
| `PlayerState.PotionSlots` (`PotionModel?[]`), `MaxPotionCount`, `HasOpenPotionSlot` | `State/CreatureState.cs` | `Player.cs:142`, `:243`, `:280`; default 3 slots `:101`, `:714` (`PlayerState.DefaultPotionSlotCount`), 2 from A4 on through `CreateStandalone` (M27) | array + queries (M21) |
| `PlayerState.AddToMaxPotionCount(int)`, `SubtractFromMaxPotionCount(int)` | `State/CreatureState.cs` | `Player.cs:974-1017`, `:1072-1096` | implemented (fix pass, M27): resize the slot array; a shrink moves potions into lower open slots or discards them |
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
- `RunState.CreateStandalone` (including the TightBelt slot reduction, M27); the `CombatState` constructors' run
  attachment; `PlayerState.AddToMaxPotionCount` / `SubtractFromMaxPotionCount` (M27).
- The attached-combat check of the 13 migrated both-scope dispatchers (M26); their combat path keeps the P0 body.
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

Fix pass (same bench, three alternating 3 s runs of `46d1918` and the fix pass on a quieter machine): `Clone()`
2.50 / 2.54 / 2.60 µs → 2.63 / 2.57 / 2.63 µs; `StateHash()` 1.19 / 1.24 / 1.27 µs → 1.20 / 1.18 / 1.25 µs; playouts
691k / 659k / 683k → 685k / 676k / 686k decisions/s. The attached-combat check costs nothing measurable. The bench
runs at A0, so the TightBelt change does not touch it.

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
14. **Ascension effects on the player (M27), owner record.** `CreateStandalone` applies TightBelt. The state agent
    owns that body from W1 on (`Run/RunState.cs`, Part B §B11). AscendersBane stays with the loop's deck specs
    (`CombatLoop.DeckForCombat`) and `StartingDecks` (cards-kernel). W2 run creation (R1 takes `Run/RunState.cs`
    over) applies all of `AscensionManager.ApplyEffectsTo` once per player at `InitializeNewRun`
    (`Runs/RunManager.cs:3862`). It must not pass new-run players through `CreateStandalone` as well, or TightBelt
    would apply twice. `CreateStandalone` treats its player as a new-run player. A caller that rebuilds a recorded
    mid-run player, for example a W1 diff bundle that carries potion slots (the P0 bundles carry none), sets
    `PotionSlots` after it constructs the combat.
15. **Out-of-combat dispatch of the 13 migrated dispatchers (M26).** The `cs == null` path throws until the hooks
    agent implements the run listeners without a combat (`IterateRunHookListeners(rs, null, …)`,
    `Runs/RunState.cs:829-933`). The same agent extends `ListenerSnapshot`'s `Run` source with the deck cards and
    modifiers of `rs` (Part B §B3). W2 needs both for HP changes, deaths and potions used outside combat
    (`PotionModel.OnUseWrapper(RunState, …)`).
16. **Detaching a combat (M26).** The 13 dispatchers require `cs == rs.Combat`. If W2 clears `rs.Combat` at combat
    end, it does so after the last dispatch that passes that combat, or it passes `cs = null`.

## 15. Fix pass after the adversarial check (2026-11-01)

| Finding | Resolution |
| --- | --- |
| The 21 existing both-scope virtuals and 13 both-scope dispatchers kept `CombatState cs`, against Part B §B3 and M3 | Migrated (M1, M3, M26). The virtuals take `RunState rs`. The dispatchers take the game's `(RunState rs, CombatState? cs, …)`, with exact `(CombatState cs, …)` forwarding overloads. Overrides updated: `RavenousPower`, `ThornsPower`, `WeakPower`, `StrengthPower`, `ShrinkPower`, `VulnerablePower`, and the `PipelineTests` doubles; direct calls in `CorpseSlugsSeapunkTests` and `WurmBeetleTests` pass `cs.Run`. The contract is unchanged: code and Part B §B3 now agree |
| Standalone runs at A4+ had 3 potion slots where the game has 2 (TightBelt) | `RunState.CreateStandalone` applies TightBelt through the new `PlayerState.SubtractFromMaxPotionCount` (M27). A probe confirms 3 slots at A0-A3 and 2 at A4-A10, for both `new CombatState(...)` and `CombatSim.Create` / `CombatLoop.CreateCombat`, and that clones keep the count. The four A10 P0 diff bundles still agree 69/69: they record no potions, and the canonical form changes only for A4+ states, so the pinned A0 hashes stay the same. Owner record in §14 item 14 |

Probe (scratch console project against `Sts2Sim.Core`, not committed; the scaffold row owns no test file and the
suite stays at 367): slot counts per ascension, clone, growth to 5, shrink 5 → 3 moving potions down, shrink
4 → 2 discarding, a full shrink throwing, `ModifyDamage` `(cs)` == `(rs, cs)`, `(rs, null)` →
`NotImplementedException`, a detached combat → `InvalidOperationException`, an empty-modifier `AfterModifying*`
with `cs = null` as a no-op, and overrides receiving `rs` with `rs.Combat == cs`. All pass.

## 16. Round-3 amendments, phase 0b (CONTRACTS Part B §B12, 2026-11-01)

On top of `75ec0b6`. Scaffold amendment rows A10, A11, A12, A13, A15s, A5s of Part B §B12, plus the hot-path
bodies the owners' P0 tests need while they wire the new calls. Each body is a line-by-line port with its
citation; the owner named in the last column re-verifies it and keeps the file. This section supersedes the rows of
§2, §7, §9, §10, §12 and §14 that it names (`CombatState.CreateCard`, `CardInstance.CloneForCombat`, the `OrbQueue`
and `ChoiceRuntime` stubs, the 17 `CombatHistory.Append*` stubs, the canonical layout of §10, M27's
`CreateStandalone`). Events the game raises only for UI (`silent` parameters, `Flashed`, `*Changed`) are dropped as
in the rest of the sim. §16.7 records the fix pass after the phase-0b adversarial check (on top of `a48adf5`), which
added `Hook.BeforeCardAutoPlayed`, the `CombatHistory.Append*` bodies, the A17 participant helpers and
`StarsCmd.SpendStars` to the hot-path set.

### 16.1 Members added or implemented

| # | Member | Sim file | Game source | Body | Owner afterwards |
| --- | --- | --- | --- | --- | --- |
| A10 | `PlayerState.AddRelicInternal(relic, index = -1)` | `State/CreatureState.cs` | `Entities.Players/Player.cs:883-903` | owner set through `RelicModel.AttachTo` (the game's `Owner` setter, `Models/RelicModel.cs:225-242`: a different existing owner throws), then `Add` for `-1`, else `Insert(index)`; the `Flashed` subscription and the `RelicObtained` event are dropped with `silent` (§16.6 item 2) | state |
| A10 | `PlayerState.RemoveRelicInternal(relic)` | same | `Player.cs:905-922` | `InvalidOperationException` when the player lacks it; `Remove`; `HasBeenRemovedFromState = true` (= `RelicModel.RemoveInternal`, `Models/RelicModel.cs:635-638`); the owner is kept, as in the game | state |
| A10 | `PlayerState.MeltRelicInternal(relic)` | same | `Player.cs:924-947` | the game's three checks in its order (not wax, already melted, not owned), then `IsMelted = true`, `Status = RelicStatus.Disabled`. `GetRelic` still returns melted relics (`:949-957`, unchanged) | state |
| A10 | `PlayerState.GetPotionSlotIndex(potion)` | same | `Player.cs:959-962` | `Array.IndexOf` (reference equality: no model overrides `Equals`, `Models/AbstractModel.cs`) | state |
| A10 | `PlayerState.AddPotionInternal(potion, slotIndex = -1) → PotionProcureResult` | same | `Player.cs:1020-1070` | `slotIndex < 0` → first empty slot; no empty slot or an occupied slot → `(false, potion, TooFull)` with the owner untouched; else owner through `PotionModel.AttachTo` (`Models/PotionModel.cs:450-467`), slot write, `(true, potion, None)`; `AddPotionFailed` / `PotionProcured` dropped | state |
| A10 | `PlayerState.DiscardPotionInternal(potion)`, `RemoveUsedPotionInternal(potion)` | same | `Player.cs:1072-1079`, `:1081-1085` | both `RemovePotionInternal`; `PotionDiscarded` / `UsedPotionRemoved` dropped (subscribers are UI: `Nodes.Potions/NPotionContainer.cs:296-299`, `Nodes.Multiplayer/NMultiplayerPlayerState.cs:1116-1119`) | state |
| A10 | `PlayerState.RemovePotionInternal(potion)` (private, as in the game) | same | `Player.cs:1087-1096` | `InvalidOperationException` when absent, else the slot is nulled; `HasBeenRemovedFromState` is set by the callers in the game (`PotionModel.Discard` / `RemoveBeforeUse`, `Models/PotionModel.cs:589-599`), not here | state |
| A10 | `enum PotionProcureFailureReason { None, TooFull, NotAllowed }` | `Enums/RelicPotionEnums.cs` | `Entities.Potions/PotionProcureFailureReason.cs:3-8` | game ordinals 0, 1, 2 | scaffold |
| A10 | `readonly record struct PotionProcureResult(bool Success, PotionModel Potion, PotionProcureFailureReason FailureReason)` | `Enums/RelicPotionEnums.cs` | `Entities.Potions/PotionProcureResult.cs:5-12` | the game's class with public fields `success`, `potion`, `failureReason`, in that order (§16.6 item 1) | scaffold |
| A11 | `CardPlay(CombatState cs, CardInstance card, CreatureState? target)`, `int PlayId` | `Cards/CardPlay.cs` | `Entities.Cards/CardPlay.cs:9-101` (no id; compared by reference, `Models.Powers/UnmovablePower.cs:37`) | `PlayId = cs.AllocateCardPlayId()` in the constructor; the two-argument constructor is gone | frozen (scaffold) |
| A11 | `CardPlay.Resources` (`ResourceInfo`, replaces `EnergySpent` / `StarsSpent`) | `Cards/CardPlay.cs` | `Entities.Cards/CardPlay.cs:52-60`, `Entities.Cards/ResourceInfo.cs:5-47` | settable like the other P0 members | frozen (scaffold) |
| A11 | `CombatState.NextCardPlayId`, `AllocateCardPlayId()` | `State/CombatState.cs` | B12 A11 | per-combat counter from 0, copied by the copy constructor, written in the `CombatKernel` block (`0x530E`) after `CardOrPotionEffectDepth` | state |
| A11 | `ActionExecutor.PlayCard`, `OnPlayWrapper(cs, card, target, isAutoPlay, ResourceInfo resources)` | `Combat/Loop/ActionExecutor.cs` | `GameActions/PlayCardAction.cs:157-165` (manual play: `EnergySpent = EnergyValue`, `StarsSpent = StarValue`), `Models/CardModel.cs:758-768` (one `CardPlay` per play index, carrying `resources`) | the auto-play values (`EnergySpent = 0`, `EnergyValue = GetAmountToSpend()`, `StarsSpent = 0`, `StarValue = max(0, GetStarCostWithModifiers())`, `Commands/CardCmd.cs:515-522`) come with the loop's `CardCmd.AutoPlay` | loop |
| A11 | `HistoryCardPlay(int PlayId, …)` (first field, written first), `HistoryCardPlay.From(CardPlay)` | `State/CombatHistory.cs` | `Entities.Cards/CardPlay.cs:12-94` | `From` copies `PlayId`, card instance id and model id, `PlayerId` = the card owner's id (the game's `Player = cardModel.Owner`, `Models/CardModel.cs:761`; `CombatHistory.NoId` for an ownerless test card), `TargetId` (or `NoId`), `ResultPile`, `Resources`, `IsAutoPlay`, `PlayIndex`, `PlayCount`. The `Append*` that record a play (`AppendCardPlayStarted` / `Finished`, `AppendBlockGained`) build their entry with it (hot-path row below) | state |
| A11 | test call sites | `Sts2Sim.Tests/PipelineTests.cs` (7), `CardTests.cs` (9) | | `new CardPlay(card, target)` → `new CardPlay(cs, card, target)` (the combat in scope; `CardTests` line 745 passes `other`). No test read `EnergySpent` / `StarsSpent` | test owners |
| A12 | `PlayerState.AscensionEffectsApplied` | `State/CreatureState.cs` | `Entities.Ascension/AscensionManager.cs:27-39`, applied once per player at `Runs/RunManager.cs:3856-3866` (`InitializeNewRun`) and `Runs/RunState.cs:977-981` (a player added to a run in progress) | copied by the copy constructor; written in `PlayerPersistent` (`0x5307`) after `CanUseOrRemovePotions` | state |
| A12 | `RunState.CreateStandalone` | `Run/RunState.cs` | as above | TightBelt (`SubtractFromMaxPotionCount(1)` at `ascension >= TightBelt`) only while the flag is false, then the flag is set at every ascension; a player passed through `CreateStandalone` twice keeps 2 slots at A4+ (M27 otherwise unchanged) | state |
| A13 | `RunState.NextCardInstanceId`, `AllocateCardInstanceId()` | `Run/RunState.cs` | Part A §5, B12 A13 | copied by `CloneWithoutCombat`; written once, in the run fields right after `Ascension` | state |
| A13 | `CombatState.NextCardInstanceId` (get/set), `AllocateCardInstanceId()` | `State/CombatState.cs` | B12 A13 | forward to `Run`; removed from the combat copy constructor and from `WriteCombatFields`. P0 behaviour is unchanged (every P0 combat owns a fresh standalone run, so the counter still starts at 0) | state |
| A15s | `CreatureCmd.Add(cs, MonsterDef monster, CombatSide side = Enemy, string? slotName = null) → MonsterState`; `CreatureCmd.Add(cs, CreatureState creature)` | `Combat/Pipeline/CreatureCmd.cs` | `Commands/CreatureCmd.cs:3564`, `:3575` | stub | pipeline |
| A15s | `PlayerCmd.AddPet(cs, PlayerState owner, MonsterDef def) → MonsterState` | `Combat/Loop/PlayerCmd.cs` | `Commands/PlayerCmd.cs:946-967` | stub | loop |
| A15s | `MonsterSpawner.SpawnPet(cs, def, owner)`, `MonsterSpawner.SpawnForAdd(cs, def, side, slotName)` → `MonsterState` | `Monsters/MonsterDef.cs` | `Commands/PlayerCmd.cs:946-955`, `Commands/CreatureCmd.cs:3564-3572`, `Combat/CombatState.cs:709-724` | stubs | monsters-kernel |
| A15s | `MonsterLifecycle.PlayerSideParticipants(cs)`, `EnemySideParticipants(cs)` → `IReadOnlyList<CreatureState>` | `Monsters/MonsterLifecycle.cs` | `Combat/CombatManager.cs:4873-4882`, `:2055-2088`; `Combat/CombatState.cs:620`, `:831` | implemented in the fix pass (hot-path row below) | monsters-kernel |
| A15s | `StarsCmd.GainStars(cs, player, decimal)`, `LoseStars(cs, player, decimal)`, `SetStars(cs, player, decimal)`, `SpendStars(cs, player, int)` (new file) | `Combat/Pipeline/StarsCmd.cs` | `Commands/PlayerCmd.cs:844` (state machine `:358-423`, gate `:390`, `AfterStarsGained((int)amount)` `:393`), `:857-866`, `:868` (`:695-793`, gate `:742`); `Models/CardModel.cs:3435-3446` (private `SpendStars(int)`, state machine `:1399-1466`, `AfterStarsSpent` `:1436`) | `GainStars`, `LoseStars`, `SetStars` stubs; `SpendStars` implemented in the fix pass (hot-path row below). Parameter types are the game's (`decimal` amounts, `int` for the spend); the order `(cs, player, amount)` is B12 A15s's, not the game's `(amount, player)` | character-mechanics |
| A5s | `FidelityFlag.ApproximatedRule` (= 1, appended) | `State/Fidelity.cs` | B12 A5s | enum value | state |
| hot path | `CombatState.CreateCard(def, owner)` | `State/CombatState.cs` | `Combat/CombatState.cs:671-677` (`ToMutable`, `AddCard(card, owner)` `:686-690` → `Owner` setter `Models/CardModel.cs:1777-1794`, `AfterCreated`) | a new `CardInstance(def, AllocateCardInstanceId())`, `AttachTo(owner)`, then `def.AfterCreated(card)`; the combat's `_allCards` registry (`:982-991`) has no sim counterpart and no reader | state |
| hot path | `CardDef.AfterCreated(CardInstance card)` (new virtual, empty default) | `Cards/CardDef.cs` | `Models/CardModel.cs:2884-2886` | the only v0.111.0 override is `Models.Cards/SpoilsMap.cs:146-149` (sets `SpoilsActIndex`); the card is passed because per-instance state lives on `CardInstance`. Plumbing in a cards-kernel file | cards-kernel |
| hot path | `CardInstance.CloneForCombat(newInstanceId)`; `InstanceId` setter now private | `Cards/CardInstance.cs` | `Combat/CombatState.cs:679-684` (`CloneCard` = `ClonePreservingMutability` + `AddCard`), `Models/AbstractModel.cs:155-181` (`MutableClone` = `MemberwiseClone`, `DeepCloneFields`, `AfterCloned`), `Models/CardModel.cs:2826-2859` (local keywords, dynamic vars, energy cost, temporary star costs; enchantment and affliction cloned and re-applied with their amounts, `:3137-3167`, `Models/EnchantmentModel.cs:330-348`, `Models/AfflictionModel.cs:238-243`), `:2861-2882` (`CurrentTarget` null, `CurrentPlayIndex` 0, `DeckVersion` null, `HasBeenRemovedFromState` false), `Entities.Players/Player.cs:1214-1233` (`DeckVersion` = the deck card) | `CloneFor(owner)` (the M20 clone: vars, energy cost and temporary star costs deep-copied, keyword mask and flags copied, `Enchantment` / `Affliction` `CloneFor(clone)`, listener per M19; the owner is kept as `MemberwiseClone` keeps it in the game), then the new instance id, `CurrentTargetId = NoTarget`, `CurrentPlayIndex = 0`, `HasBeenRemovedFromState = false`, `DeckVersionId = source.InstanceId` | cards-kernel |
| hot path | `OrbQueue.Clear()` | `Orbs/OrbQueue.cs` | `Entities.Orbs/OrbQueue.cs:408-412` | orbs cleared, capacity 0 | character-mechanics |
| hot path | `OrbQueue.BeforeTurnEnd(cs)`, `AfterTurnStart(cs)` | `Orbs/OrbQueue.cs` | `:462-474` (state machine `:117-210`), `:476-488` (`:20-113`) | iterate a copy of the orbs (`Enumerable.ToList`, `:149`, `:52`); before each orb, return unless the owner's creature still has a combat (`:170`, `:73`), then `OrbModel.BeforeTurnEndOrbTrigger(cs)` / `AfterTurnStartOrbTrigger(cs)` (`Models/OrbModel.cs:470`, `:475`). The queue holds no owner reference, so "owner in a combat" is "a `PlayerState` in `cs.Allies` owns this queue". No allocation when the queue is empty. The choice context argument is dropped | character-mechanics |
| hot path | `ChoiceRuntime.PushContext(kind, onFirstPause)`, `PopContext(depth)`, `Contexts`, `ContextDepth`; `readonly record struct ChoiceContextFrame(ChoiceContextKind Kind, Action? OnFirstPause)` | `Choice/ChoiceRuntime.cs` | Part B §B1 | transient stack: `PushContext` appends a frame and returns `ContextScope(depth)`, `ContextScope.Dispose` pops it; a pop that is not the top throws `InvalidOperationException`; allocated on the first push; not cloned (a clone starts empty) and not hashed. `Ask` stays a stub; no pause semantics | choice |
| hot path | eight `Hook` dispatchers (§16.2; `BeforeCardAutoPlayed` from the fix pass) | `Combat/Pipeline/Hook.cs` | §16.2 | mechanical ports over `ListenerSnapshot` (Part B §B3) | hooks |
| hot path | `CombatHistory.Append*` (all 17) | `State/CombatHistory.cs` | `Combat.History/CombatHistory.cs:74-157`; `Add` `:159-170` appends iff `combatState.IsLiveCombat()` (`:161`), `true` for every real combat (`Combat/CombatState.cs:1077-1080`; only `Combat/NullCombatState.cs:245-248` returns `false`); base entry `Combat.History/CombatHistoryEntry.cs:46-65` | a stamped list append: `HistoryStamp(Count, actor, cs.RoundNumber, cs.CurrentSide, cs.Player.TurnNumber)` (M15: `Sequence` is the global order, one player turn number), `Count` + 1, the entry built field for field (§9) into its per-type list (allocated on first use). The payload is computed before the stamp, so an append that throws leaves `Count` and the lists unchanged (the game builds the entry before `Add`). Actor per entry constructor: the receiver (`Combat.History.Entries/BlockGainedEntry.cs:44-45`, `DamageReceivedEntry.cs:47-48`), the attacker (`CreatureAttackedEntry.cs:19-20`), the card's owner (`CardAfflictedEntry.cs:26-27`, `CardDiscardedEntry.cs:19-20`, `CardDrawnEntry.cs:26-27`, `CardExhaustedEntry.cs:19-20`, `CardGeneratedEntry.cs:26-27`, `CardPlayStartedEntry.cs:38-39`, `CardPlayFinishedEntry.cs:45-46`), the player (`EnergySpentEntry.cs:18-21`, `StarsModifiedEntry.cs:18-21`, `SummonedEntry.cs:18-21`), the monster (`MonsterPerformedMoveEntry.cs:56-57`), the orb's or potion's owner (`OrbChanneledEntry.cs:19-20`, `PotionUsedEntry.cs:46-47`), the power's owner (`PowerReceivedEntry.cs:44-45`); `NoId` for an ownerless card, power, orb or potion (§16.6 item 14). Plays go through `HistoryCardPlay.From` (the game's `CardPlay` is init-only, `Entities.Cards/CardPlay.cs:12-90`, so the snapshot equals the stored reference); `WasEthereal` is the in-combat keyword set's `Ethereal` (`CardPlayFinishedEntry.cs:49`, §16.6 item 13); damage results become `HistoryDamageResult` values; `TargetIds` are materialized from the game's `PlayerCreatures` snapshot (`Models/MonsterModel.cs:269`, `:286`; `Combat/CombatState.cs:560`), null stays null; absent dealer, card source, target, applier or creator → `NoId` (`CardSourceId` null). The `Changed` event is UI | state |
| hot path | `CombatHistory.DeepClone` | `State/CombatHistory.cs` | B12 A22 | `CreatureAttackedEntry.DamageResults` and `MonsterPerformedMoveEntry.TargetIds` are copied per entry, never shared between clones; the other 15 lists copy their value entries as before. A 500-entry log clones in about 8 µs (budget 50 µs, Part B §B2; the state owner re-measures) | state |
| hot path | `MonsterLifecycle.PlayerSideParticipants(cs)`, `EnemySideParticipants(cs)` | `Monsters/MonsterLifecycle.cs` | `Combat/CombatManager.cs:4873-4882` (turn start: the `PlayersTakingExtraTurn` creatures when the player side takes an extra turn, else `ToList(CreaturesOnCurrentSide)`), `:2055` (enemy turn end: `ToList(CreaturesOnCurrentSide)`); `Combat/CombatState.cs:620`, `:831-838` (`GetCreaturesOnSide`: `Allies` unless `Enemy`) | player side: each id in `cs.PlayersTakingExtraTurn` (M17: player combat ids) resolved to its `PlayerState` in `cs.Allies` (an id not in the combat throws `InvalidOperationException`; the game holds `Player` references), else a copy of `cs.Allies` (player, then pets); enemy side: a copy of `cs.Monsters`. A new `CreatureState[]` per call (`Array.Empty` for an empty side). The caller is on the player side, as at `:4874`; the player side's turn end uses another set (§16.6 item 15) | monsters-kernel |
| hot path | `StarsCmd.SpendStars(cs, player, amount)` | `Combat/Pipeline/StarsCmd.cs` | `Models/CardModel.cs:3435-3446` (state machine `:1399-1466`: `LastStarsSpent = amount` `:1432`, `if (amount > 0)` `:1433`, `PlayerCombatState.LoseStars` `:1435`, `Hook.AfterStarsSpent` `:1436`); `Entities.Players/PlayerCombatState.cs:355-363` (`LoseStars`: a negative amount throws, clamp at 0), `:138-153` (the `Stars` setter records `StarsModified(new − old)` when the value changes, `:150`) | `amount > 0` → `player.LoseStars(amount)` (the state's port of `:355-363`), `AppendStarsModified(after − before)` when the value changed, then `Hook.AfterStarsSpent(cs, amount, player)` (a hooks stub, reached only when stars are spent); `amount ≤ 0` does nothing. No `IsEnding` gate and no `LastStarsSpent` write here (§16.6 item 12) | character-mechanics |

### 16.2 Hot-path dispatchers (list for the hooks owner)

Enumerated by reading every `Hook.*` call in `Combat/CombatManager.cs` `SetupPlayerTurn` (state machine
`:4066-4316`), `RunAutoPrePlayPhase` (`:3812-3931`), `DoTurnEnd` (`:1141-1320`) with `DoTurnEndCards`, `StartTurn`
(`:4669-5413`), `EndCombatInternal` (`:1578-1836`), `EndPlayerTurnPhaseOneInternal` (`:2189-2705`, the auto-post-play
phase) and `Models/CardModel.cs` `OnPlayWrapper` (state machine `:257-1064`), then one level into the commands and
properties they call directly (`CardPileCmd.Add` / `AddDuringManualCardPlay` / `RemoveFromCombat` / `Draw`,
`CardCmd.Exhaust`, `CheckForEmptyHand`, `CheckWinCondition`, `Creature.BeforeTurnStart` / `AfterTurnStart`,
`PlayerCombatState`, `CardModel.Keywords`). The fix pass (§16.7) adds the auto-play entry of the B6 turn flow:
`CardCmd.AutoPlay` (public `Commands/CardCmd.cs:1779`, state machine through `:551`), which re-enters `OnPlayWrapper`
and which a powers-only combat reaches from Sly discards (`CardCmd.Discard`: Sly cards collected at `:843`,
`AutoPlay(…, SlyDiscard)` at `:917`), from `CardPileCmd.AutoPlayFromDrawPile` (public `Commands/CardPileCmd.cs:4074`,
call `:1671`) and from `HellraiserPower.AfterCardDrawnEarly` (every drawn Strike-tagged card,
`Models.Powers/HellraiserPower.cs:69`, call `:93`; the W1 B2-chain gate's combat). Of the dispatchers found, these
eight were stubs; all the others were already implemented in P0.

| Dispatcher (sim) | Game | Called from | Passes, listener source | Pattern |
| --- | --- | --- | --- | --- |
| `AfterAutoPrePlayPhaseEntered(cs, player)` | `Hooks/Hook.cs:9688`, state machine `:316-515` | `RunAutoPrePlayPhase` (`Combat/CombatManager.cs:3891`) | three fresh guarded snapshots: `AfterAutoPrePlayPhaseEnteredEarly`, `AfterAutoPrePlayPhaseEntered`, `AfterAutoPrePlayPhaseEnteredLate` | plain; the `LocalContext.NetId.HasValue` gate is always true in single player (design-combat-kernel §2.2) |
| `AfterAutoPostPlayPhaseEntered(cs, player)` | `:9673`, `:214-312` | `EndPlayerTurnPhaseOneInternal` (`Combat/CombatManager.cs:2389`) | one guarded snapshot | plain; NetId gate as above |
| `BeforeCardAutoPlayed(cs, card, target, type)` | `:8919`, `:6992-7082` (iteration `:7026`, call `:7047`) | `CardCmd.AutoPlay` (`Commands/CardCmd.cs:551`): after the over-or-ending / owner-dead gate, the Unplayable keyword, `ShouldPlay` and the `AnyEnemy` / `AnyAlly` target picks, the X and star captures and the play-pile add of a card in no pile; before the auto-play `ResourceInfo` (`:515-522`) and `OnPlayWrapper` | one guarded snapshot (`IterateCombatHookListeners`) | plain (fix pass, §16.7); the only v0.111.0 overrider is `Models.Achievements/SkillSilent1Achievement.cs:31`, not a rule; `InvokeExecutionFinished` (`:7040`) is UI |
| `ModifyCardPlayResultLocation(cs, card, isAutoPlay, resources, cardLocation[, ref modifiers])` | `:10188-10211` | `OnPlayWrapper` (`Models/CardModel.cs:936`) | one guarded snapshot | ref modifier: a model joins the list when the location it returns differs (`!=` on the record struct; the game's `CardLocation` also carries the player, constant in single player, `Entities.Cards/CardLocation.cs:7-20`). The per-modifier `AfterModifyingCardPlayResultLocation` call is inline in `OnPlayWrapper` (`Models/CardModel.cs:524`), not a dispatcher: the loop calls the virtual on each listed model |
| `AfterCombatEnd(rs, cs, room)` | `:9085`, `:1617-1705` | `EndCombatInternal` (`Combat/CombatManager.cs:1708`) | one run snapshot (`runState.IterateHookListeners(combatState)`) | plain; `RequireAttachedCombat` (M26): `cs == null` → `NotImplementedException` (hooks agent), a detached `cs` → `InvalidOperationException` |
| `AfterCombatVictory(rs, cs, room)` | `:9115`, `:1709-1849` | `EndCombatInternal` (`:1781`) | two run snapshots: `AfterCombatVictoryEarly`, `AfterCombatVictory` | plain; M26 |
| `AfterCardChangedPiles(rs, cs, card, oldPile, clonedBy)` | `:8935`, `:799-943` | `CardPileCmd.Add` (`Commands/CardPileCmd.cs:444`, `:3466`), `AddDuringManualCardPlay` (`:980`), `RemoveFromCombat` (`:2421`): every card move of `OnPlayWrapper`, `DoTurnEndCards` and the turn draw | two run snapshots: `AfterCardChangedPiles`, `AfterCardChangedPilesLate` | plain; M26 (a deck-pile move has no combat and stays with the hooks agent) |
| `ModifyKeywordsInCombat(cs, card, keywords)` | `:10458-10473` | `CardModel.GetKeywordsWithSources` (`Models/CardModel.cs:2775-2814`, call `:2812`) behind `Keywords` (`:1940`), read by `DoTurnEnd` (Ethereal, `Combat/CombatManager.cs:1282`), `SetupPlayerTurn` (Innate, `:4266`), `ResolveTurnEndCardEffects` (Ethereal, `:7336`) and the result location (Exhaust) | one combat snapshot (`combatState.IterateHookListeners()`) | plain; the `TryModifyKeywordsInCombat` result is ignored, as in the game |

Not dispatchers: `Player.ReviveBeforeCombatEnd` (`Entities.Players/Player.cs:36-99`, `:1237-1247`: `CreatureCmd.Heal(creature,
1)` when the player is dead, called per player at `Combat/CombatManager.cs:1689`) and `Player.AfterCombatEnd`
(`:1249-1254`) are loop work (B12 A18) over the existing `CreatureCmd.Heal`. Reached only off these paths, left as stubs:
`AfterStarsSpent` (reached through `StarsCmd.SpendStars` only when `amount > 0`, `Models/CardModel.cs:1433-1436`: no P0
or B2-chain card has a star cost and `ShouldPayExcessEnergyCostWithStars` has no v0.111.0 overrider),
`ShouldGainStars` / `AfterStarsGained` (`PlayerCmd.GainStars`), the gold dispatchers (`ModifyGoldGained`,
`AfterModifyingGoldGained`, `AfterGoldGained`, `PlayerCmd.GainGold`), `ShouldAfflict` (`CardCmd.Afflict`), the orb
dispatchers (no orb in a power-only combat), and the deck-pile dispatchers of `CardPileCmd` and `CardCmd`
(`ShouldAddToDeck`, `ModifyCardBeingAddedToDeck`, `BeforeCardRemoved`, each behind `PileType.Deck`). Auto-play reaches no
stub besides `BeforeCardAutoPlayed`, which the fix pass ported.

### 16.3 Test infrastructure (`sim/Sts2Sim.Tests/W1Pending.cs`)

`W1Pending.Count` and `W1Pending.Run(test, dependsOnArea, body)`: the body runs; a `NotImplementedException`
(only that type and its subclasses) is caught, counted and printed as `PENDING <test> (<area>): <message>`; any other
exception propagates. An internal overload takes the `TextWriter` so the self-test does not print to the console.
`TestRunner` prints `PENDING total: N` as the last line of its summary (after the failed-test list). The self-test
`W1PendingTests.Test_W1Pending_CountsNotImplementedAndPropagatesOtherExceptions` checks a completing body (not counted,
no output), a stub body (counted, exact line) and an `InvalidOperationException` body (propagates, not counted, no
line), and restores `Count`, so it adds no PENDING line and does not change the total. A gate that greps the output
for per-test lines must match `PENDING ` followed by anything but `total:`, or read N from the total line.

### 16.4 Refreshed pins

| Pin | Old | New | Why |
| --- | --- | --- | --- |
| `EndToEndTests.PinnedSeedOneOpeningHash` | `0x2D2E7E7036D7EA8F` | `0xCA1423508C1168E3` | intended canonical layout change, no behaviour change |
| `EndToEndTests.PinnedSeedOneFinalHash` | `0x5908EE3EB054BB48` | `0xD7B055DC9B9E326E` | same |

Layout change per state, +5 bytes: +4 `RunState.NextCardInstanceId` after `Ascension` in the run fields (A13), +1
`AscensionEffectsApplied` in `PlayerPersistent` (A12; `01` for every `CreateStandalone` player), −4 the combat-field
`NextCardInstanceId` after `NextCreatureId` (A13), +4 `NextCardPlayId` in `CombatKernel` (A11). Probe (scratch console
projects against the parent tree and this tree, not committed): every registered encounter (8) × seeds 1–6 (A0 odd,
A10 even), seeded random legal play, 1 028 states. The action sequence and a per-state projection (HP, block, energy,
hand ids and instance ids, pile sizes, monsters, outcome, round, slots, `NextCardInstanceId`) are identical; a byte
alignment finds exactly these four edits in every state; the run header's new int equals `NextCardInstanceId`. The
`LoopTests` pin `NextCardInstanceId == 12` is unchanged (the loop owner moves it when deck cards get ids, A13). No other
test pins bytes.

A second probe (102 checks, scratch, not committed) covers the phase-0b bodies of §16.1 (the fix-pass rows are covered by
the probe of §16.7): relic add / insert / remove / melt and
their throws; potion slots (explicit, first empty, occupied and full → `TooFull` with no owner change), discard and used
removal; `PlayId` from the combat counter, manual-play `Resources == (1, 1, 0, 0)` for Strike, `NextCardPlayId` cloned and
hashed; `HistoryCardPlay.From`; TightBelt once per player across two `CreateStandalone` calls, the flag cloned and hashed;
the run allocator behind both counters, cloned by both clone paths, hashed at byte 6; `CreateCard` (id, owner,
`AfterCreated`, no pile); `CloneForCombat` (new id, `DeckVersionId`, owner, resets, deep copies, enchantment and affliction
re-bound, later deck edits not shared, an ownerless source); orb triggers in queue order over a snapshot, skipped for a
queue whose owner is not in the combat, `Clear`; context stack push / pop / LIFO throw / not cloned / not hashed /
`default(ContextScope)`; each dispatcher's passes and order, the modifier rule, guarded silence once the combat is over
while run and combat sources still visit, `cs == null` → `NotImplementedException`, a detached combat →
`InvalidOperationException`, a power whose owner left the combat skipped; every A15s stub throws (at `a48adf5`; the fix pass
implemented the participant helpers and `StarsCmd.SpendStars`, §16.7).

### 16.5 Gates

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` (Debug and Release, `--no-incremental`) | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` (Debug and Release) | 372 passed, 2 failed, 374 total (the 373 of round 2 plus the W1Pending self-test); the two failures are the known content gates `W1GateTests.Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered`; `PENDING total: 0`, no per-test PENDING line |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundle set) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |
| Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`, 3 s, parent tree `75ec0b6` from `git archive` and this tree interleaved; medians) | quiet session: `Clone()` 2.581 → 2.572 µs, `StateHash()` 1.169 → 1.197 µs, 648 509 → 665 776 decisions/s; final tree, two later sessions (load average 3.3–4.7, the game client running): 2.940 → 2.827 µs, 1.222 → 1.230 µs, 607 696 → 584 581 decisions/s (per-run spread 532k–679k) and 2.562 → 2.548 µs, 1.087 → 1.138 µs, 670 186 → 663 637 decisions/s. No change beyond noise: the canonical form grows by 5 bytes, a play allocates nothing it did not allocate before, and the context stack is allocated only on the first push. Two sessions where both trees fell to about 105k decisions/s and 12 µs per clone (scheduler mode under load) were discarded |

### 16.6 Open questions and contract change requests

1. **`PotionProcureResult` is not an enum.** B12 A10 says "→ `PotionProcureResult` (… game enum ordinals)". In v0.111.0
   `PotionProcureResult` is a class (`success`, `potion`, `failureReason`, `Entities.Potions/PotionProcureResult.cs:5-12`) and
   the enum with ordinals is `PotionProcureFailureReason { None, TooFull, NotAllowed }`. Ported as a record struct plus the
   enum; contract change request: A10 should read "→ `PotionProcureResult(Success, Potion, FailureReason)` with
   `PotionProcureFailureReason` (game ordinals)". `PotionCmd.TryToProcure`'s `NotAllowed` result (`Commands/PotionCmd.cs:253-266`)
   is relics-potions'.
2. **`RelicObtained` is rule-relevant in W2.** `CardReward` subscribes to `Player.RelicObtained` (`Rewards/CardReward.cs:535`)
   and lets the obtained relic modify the pending reward's options (`:574-589`, `TryModifyCardRewardOptions[Late]`).
   `AddRelicInternal` drops the event with `silent`; W2's card reward must restore the notification for non-silent adds.
3. **`RemovePotionInternal` is private**, as in the game; owners call `DiscardPotionInternal` or `RemoveUsedPotionInternal`.
   `SetMaxPotionCountInternal` (M27) still discards by truncation; it is equivalent to the game's `DiscardPotionInternal`
   there and was left untouched.
4. **`CardPlay` needs the combat.** Every directly constructed play (tests, content) allocates a `PlayId` from that combat;
   `NextCardPlayId` is hashed, so a test that builds a play changes the state hash.
5. **`HistoryCardPlay.From` and ownerless cards.** P0 combat cards have no owner yet, so `PlayerId` is `NoId` for them; the
   branch dies once every creation path sets `Owner` (B12 A23, loop and cards-kernel).
6. **Orb owner check.** `OrbQueue` finds its owner through `cs.Allies`; if character-mechanics gives the queue an owner
   reference (the game's `_owner`, `Entities.Orbs/OrbQueue.cs:388`), that replaces the scan.
7. **Context stack strictness.** An out-of-order `PopContext` throws. The `TurnSetup` first-pause steps run "with the context
   stack reset to the turn-loop root" (Part B §B1); saving and restoring the stack around them keeps the LIFO rule (choice).
8. **`CardDef.AfterCreated`** was added to a cards-kernel file because `CreateCard` must run the game's `AfterCreated` step.
9. **Line ranges in the brief.** The brief cites `OnPlayWrapper` as `Models/CardModel.cs:667-909`; in this decompile the state
   machine spans `:257-1064` and the cited lines are its loop body. The citations above use this decompile.
10. **TightBelt and W2 players.** With the flag, `CreateStandalone` no longer applies TightBelt to a player whose ascension
    effects were applied before. W2's `ApplyAscensionEffects` (`Runs/RunManager.cs:4827-4830`) sets
    `AscensionEffectsApplied` when it applies `AscensionManager.ApplyEffectsTo`; then a W2 player passed through
    `CreateStandalone` keeps its slots, which supersedes the warning of §14 item 14 (state, W2).
11. **History recording moves the state hash.** The bodies make every recording site safe to wire, but the log is
    hashed (§10), so wiring a site changes `StateHash` for every later state: B6's sites (the loop: `CardPlayStarted` /
    `Finished`, `EnergySpent`, `CardDrawn` from the opening draw on), the pipeline's (`DamageReceived`, `BlockGained`,
    `PowerReceived`, `CreatureAttacked`), monsters-kernel's `MonsterPerformedMove`, cards-kernel's (`CardExhausted`,
    `CardDiscarded`, `CardGenerated`, `CardAfflicted`) and `StarsModified` through `StarsCmd`. The only byte pins are
    `EndToEndTests.PinnedSeedOneOpeningHash` / `PinnedSeedOneFinalHash` (§16.4), which B12 A13 gives to the loop owner; an
    owner other than loop who wires a site in its isolated worktree sees exactly these two tests fail, with no
    `NotImplementedException`. The diff harness reads neither the history nor hashes and is unaffected. Contract change
    request: extend A13 so that every owner who wires a recording site refreshes the two pins in its worktree with the
    reason in its spec and the integrator recomputes them once after the merge (`integration.md`), or have the briefs
    leave the recording sites outside the loop to integrate.
12. **`StarsCmd.SpendStars` is not `LoseStars` plus the hook.** The game's spend calls `PlayerCombatState.LoseStars`
    directly (`Models/CardModel.cs:1435`), without the `IsEnding` gate of `PlayerCmd.LoseStars` (`Commands/PlayerCmd.cs:859`),
    so character-mechanics must not route the spend through `StarsCmd.LoseStars`. `LastStarsSpent = amount` is written
    before the `amount > 0` gate (`:1432`) and is a card field, so it stays with the caller: the loop keeps
    `card.LastStarsSpent = amount` (today in `ActionExecutor.SpendStars`) immediately before `StarsCmd.SpendStars`. B12 A16
    cites the spend as `:1425-1428`; in this decompile it is `:1432-1436`.
13. **`WasEthereal` reads the in-combat keywords.** `CardPlayFinishedEntry` stores `Keywords.Contains(Ethereal)`
    (`Combat.History.Entries/CardPlayFinishedEntry.cs:49`), the local keywords plus `Hook.ModifyKeywordsInCombat`
    (`Models/CardModel.cs:1940`, `:2775-2814`; the only v0.111.0 override adds Ethereal to Hexed cards,
    `Models.Powers/HexPower.cs:320-331`). `AppendCardPlayFinished` builds that set privately from `KeywordMask` (one
    `HashSet` per finished play) until cards-kernel provides the in-combat keyword API (B7); the state owner then calls it.
14. **Actors of ownerless models.** The game's card entries take `card.Owner.Creature` (a null-reference exception for an
    ownerless card) and the power, orb and potion entries take the owner as it is; the sim stamps `CombatHistory.NoId`
    for an ownerless card (the P0 combat cards, item 5) and for an ownerless power, orb or potion. The card branch dies
    with B12 A23.
15. **The player turn end is not `PlayerSideParticipants`.** The player side's turn-end hooks receive only the ending
    players' creatures (`playersEndingTurn` = the extra-turn players or all players, `Combat/CombatManager.cs:2649`, `:2860`;
    calls `:2457`, `:2967`), never pets; `PlayerSideParticipants` is the turn-start set (players and pets, `:4873-4882`). The
    loop builds the turn-end set itself (M5).
16. **Pets in the participant snapshots** come from `cs.Allies`; `CombatState.AddAlly` is still a state stub, so the
    fix-pass probe covers the player only.

### 16.7 Fix pass after the phase-0b adversarial check (2026-11-01)

On top of `a48adf5`.

| Finding | Resolution |
| --- | --- |
| `Hook.BeforeCardAutoPlayed` was still a stub although B6 puts auto-play in the loop's turn flow: `CardCmd.AutoPlay` calls it on every auto-play that passes its gates (`Commands/CardCmd.cs:551`), and a powers-only combat reaches it from Hellraiser's Strike-tagged draws, `CardPileCmd.AutoPlayFromDrawPile` and Sly discards; the W1 B2-chain gate (`DarkEmbracePower` + `HellraiserPower` + six `SeekerStrike`, no relic) is such a combat | Ported as one guarded pass, the `AfterAutoPostPlayPhaseEntered` pattern (§16.2 row); §16.2's enumeration and its "reached only off these paths" sentence corrected. Re-audit: every `Hook.*` call in `Commands/{CardCmd,CardPileCmd,CreatureCmd,PowerCmd,PlayerCmd}.cs`, `Commands.Builders/AttackCommand.cs`, `Models/{CardModel,MonsterModel,PowerModel}.cs`, `Combat/{CombatManager,CombatState}.cs`, `Entities.Players/{Player,PlayerCombatState}.cs`, `Entities.Creatures/Creature.cs`, `Entities.Cards/CardPile.cs` and `Models.Powers/{Hellraiser,DarkEmbrace}Power.cs`, intersected with the 64 dispatcher stubs left before the fix (63 after, plus the `IterateRunHookListeners(rs, cs)` stub), hits only `BeforeCardAutoPlayed` (`CardCmd.cs:551`), the deck-only `ModifyCardBeingAddedToDeck` (`CardCmd.cs:1457`, `:2055`; `CardPileCmd.cs:298`, `:3418`), `ShouldAddToDeck` (`CardPileCmd.cs:400`, `:3374`) and `BeforeCardRemoved` (`RemoveFromDeck`, `:2625`), `ShouldAfflict` (`CardCmd.cs:2248`), the gold trio of `PlayerCmd.GainGold` (`PlayerCmd.cs:284`, `:285`, `:322`), `ShouldGainStars` / `AfterStarsGained` (`:390`, `:393`) and `AfterStarsSpent` (`CardModel.cs:1436`) |
| Non-dispatcher stubs on the same hot path stopped owners from wiring the B6, A16 and A17 call sites in their worktrees without breaking the P0 suite: every `CombatHistory.Append*`, `MonsterLifecycle.PlayerSideParticipants` / `EnemySideParticipants`, `StarsCmd.SpendStars` | Ported (§16.1 hot-path rows), with A22's unshared inner lists in `CombatHistory.DeepClone`. No P0 path calls them yet, so behaviour, bytes and pins are unchanged; an owner who wires them hits no `NotImplementedException` on a P0 path. Wiring a history site still moves the two EndToEnd hash pins, which §16.6 item 11 hands to the briefs. The history readers (`CountThisTurn`, `CountLastPlayerTurn`, `ByActor`, `HistoryStamp.HappenedThisTurn` / `HappenedLastPlayerTurn`) stay state stubs: in the game files above every `History.` use is an append or `Clear`; only content reads the log |

Probe (scratch console project against this tree, not committed; the suite stays at 374), 82 checks, all passing:
`BeforeCardAutoPlayed` (player powers then monster powers, arguments passed through, a power whose creature left the combat
skipped, silent once over or ending, visiting while `IsStarting`, no listeners); the participant helpers (a copy of
`cs.Allies`, the extra-turn player, an unknown extra-turn id → `InvalidOperationException`, monsters in order, a snapshot
unaffected by a later removal, a fresh array per call, an empty side, a clone's snapshot referencing the clone's monsters);
`SpendStars` (0 and negative amounts do nothing: no hook, no history, same hash; 5 − 2 → 3 with `StarsModified(−2)` recorded
before the `AfterStarsSpent` stub throws; an overspend clamps at 0 with the clamped delta −3; at 0 stars no entry but the
hook still runs); each of the 17 appends (stamp fields, `Sequence` as the global order 0..n−1 across the lists, `Count`,
owner and `NoId` actors, absent dealer / source / target / applier / creator, `WasEthereal` from the local keywords and
from a listener that adds or removes Ethereal with the card's mask untouched, damage results as value snapshots, a lazy
target sequence materialized, null targets kept, a changed canonical form, a failed append — a null damage result or
affliction, a throwing keyword listener — leaving `Count` unchanged); clones (equal hash, inner lists not shared,
appends after the clone visible on one side only, the sequence continuing, `Clear` restarting it at 0 without touching the
clone); a 500-entry log clones in about 8 µs (7.9–8.2 µs over three runs; A22 budget 50 µs).

| Gate | Result |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` (Debug and Release, `--no-incremental`) | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` (Debug and Release) | 372 passed, 2 failed, 374 total; the failures are the known content gates `W1GateTests.Test_W1Gate_B2Chain_StepsAndRecordsDeferredListenerPromptInline` and `Test_W1Gate_PilotContentIsRegistered`; `PENDING total: 0` |
| Diff gate (`Sts2Sim.Diff -c Release`, P0 bundle set) | bundles 4 (agree 4), decisions 69/69, outcome 4/4, soft diffs 0 |
| Bench (`Sts2Sim.Bench -c Release`, `NIBBITS_WEAK`; `a48adf5` from `git archive` and the final tree, 8 interleaved runs each, load average 2.1–2.4; medians, per-run range in brackets) | `Clone()` 2.636 [2.563–2.718] → 2.622 [2.500–3.356] µs, `StateHash()` 1.184 [1.133–1.277] → 1.224 [1.123–1.271] µs, 652 314 [643 051–664 720] → 673 761 [662 177–685 547] decisions/s. No change beyond noise: no P0 path calls the new bodies, the `DeepClone` branches run only for non-empty lists, and the hash path is unchanged code. An earlier session on the pre-final tree under load 2.3–5.3 gave the same picture (2.955 → 2.996 µs, 1.458 → 1.528 µs, 484 322 → 490 199 decisions/s) |
