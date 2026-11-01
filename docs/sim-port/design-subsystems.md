# Sim full port — remaining subsystems, test seams, prompts, history, unlocks

2026-11-01 · W0 design study · game v0.111.0 · machine-readable companion:
[`sim/specs/inventory/subsystems.json`](../../sim/specs/inventory/subsystems.json)
(same content plus per-pattern usage counts and file lists).

Rule truth: `/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`.
Citations are `<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped.
Scope of every decision below: **standard single-player runs** (`GameMode.Standard`, no
modifiers, one player, no mods). Content counts are files under `Models.<Category>/`
that match a pattern (Mocks sub-namespaces excluded); async methods appear twice in the
decompile, so call-site counts are per file, not per call.

Per-model details (hooks overridden, RNG use, prompts) live in the sibling inventories
`sim/specs/inventory/{cards,powers,relics,potions,monsters,encounters,events,enchantments,afflictions,orbs,modifiers,pools}.json`.

## 0. Decisions at a glance

| Subsystem | Decision | Reason | Key refs |
| --- | --- | --- | --- |
| Test seams (`TestSupport`, `NonInteractiveMode`, mocks) | sim-native seams | infrastructure, but it shows the synchronous order; rules must follow the `TestMode.IsOff` branch | `TestSupport/*`, `Helpers/NonInteractiveMode.cs:10-28` |
| Player-choice prompts (`CardSelectCmd`, selectors, card rewards) | port | every prompt is a decision point; auto-resolution decides whether a decision exists | `Commands/CardSelectCmd.cs:3084-3840` |
| Enchantments | port | 16 relics + 9 events + BladeOfInk apply them; they change damage/block/play count/cost | `Models/EnchantmentModel.cs:289-418` |
| Afflictions | port | applied by 7 monster powers in 7 encounters | `Models/AfflictionModel.cs:117-252` |
| Orbs (Defect) | port | 43 files call `OrbCmd`; any character can channel | `Commands/OrbCmd.cs:146-745` |
| Osty / pets (Necrobinder) | port | 15 files summon; damage redirection is in the pipeline | `Commands/OstyCmd.cs:336-394` |
| Stars (Regent) | port | 44 files; star costs gate playability | `Entities.Players/PlayerCombatState.cs:138-373` |
| Forge (Regent) | port | 15 files | `Commands/ForgeCmd.cs:153-209` |
| Ancients | port; dialogue excluded; `TheArchitect` = terminal | relic/upgrade choices on every act; dialogue uses `Rng.Chaotic` | `Models/AncientEventModel.cs:78-289` |
| Run modifiers | exclude | standard runs pass an empty list | `Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:966`, `:1794-1799` |
| Daily | exclude | UI-side date seeding, network time | `Nodes.Screens.DailyRun/NDailyRunScreen.cs:917-950` |
| GameMode | parameter (constant Standard) | only effect: discovery-order modifications are ON in standard runs | `Runs/RunManager.cs:4090-4108` |
| Badges, achievements, stats, timeline UI | exclude | write-only bookkeeping | `Models.Badges/*`, `Runs/ScoreUtility.cs:127` |
| Unlock state / epochs | **parameter** | changes pools, bosses, ancients, first floor | `Unlocks/UnlockState.cs:16-139` |
| Multiplayer scaling | stub (single-player branch) | identity at 1 player, but the SP card filter must stay | `Models.Singleton/MultiplayerScalingModel.cs:41-77`, `Runs/IRunState.cs:74-84` |
| LocalContext | stub | `IsMe(player)` always true in SP | `Context/LocalContext.cs` |
| Combat history | port (ordered log) | 45 query sites in 45 files | `Combat.History/*` |
| `Entities.Cards` helpers | port | card piles, costs, transformations, enums | `Entities.Cards/*` |
| Helpers / extensions | port the rule-bearing ones | shuffles, hashing, ascension, GrabBag | `Extensions/ListExtensions.cs:10-62` |
| DynamicVars | port | all content constants | `Localization.DynamicVars/*` |
| ModelDb | port (registry exists) | ids and pool order | `Models/ModelDb.cs:112-488` |
| Saves (profile) | parameter | five live profile reads (§5 L1-L5) + the UnlockState snapshot | §5 |

## 1. Subsystems

### 1.1 Player-choice prompts

`CardSelectCmd` has 13 entry points. Each one, in order (`Commands/CardSelectCmd.cs:3169-3840`):

1. grid, combat-pile, bundle and hand prompts return empty once the combat is ending
   (`IsEnding` / `IsOverOrEnding`); deck prompts and choose-a-card do not check;
2. `UndoEndTurnIfNecessary` for combat prompts;
3. **auto-resolution**: 0 options → empty; `count <= MinSelect && !RequireManualConfirmation`
   → all options, no decision (`FromDeckForEnchantment` skips the `RequireManualConfirmation`
   check, `:3566`; `FromHandForUpgrade` auto-resolves at `<= 1`, `:3765`);
4. `Selector` (global stack top) answers synchronously, without reserving a choice id or
   signalling `SignalPlayerChoiceBegun/Ended`;
5. else `LocalSelector`, else the Godot screen + `PlayerChoiceSynchronizer`.

`CardSelectorPrefs.RequireManualConfirmation = MinSelect >= 0 && MinSelect != MaxSelect`
(`CardSelection/CardSelectorPrefs.cs`). `Cancelable` is set by Smith/Cook rest options and
the reward card-removal path (`Entities.RestSite/SmithRestSiteOption.cs:142`, `:260`;
`Entities.RestSite/CookRestSiteOption.cs:74`, `:211`; `Multiplayer.Game/RewardSynchronizer.cs:157`, `:525`).

Facts the decision API must reproduce:

- Draw-pile prompts sort options by `(Rarity, Id)` on the selector path (`:3403-3405`).
- Deck removal sorts curses first (`-999999999`), then by deck index (`:3607-3612`).
- `FromChooseACardScreen` passes `minSelect = 0` to selectors regardless of `canSkip`
  (`:3186`, `:3196`); skipping is only legal when `canSkip`.
- `FromChooseABundleScreen` has **no selector seam** (TestMode takes `bundles[0]`, `:3813`).
- `RelicSelectCmd.FromChooseARelicScreen` has no callers in v0.111.0 (`Commands/RelicSelectCmd.cs:162-200`).
- Card rewards (`Rewards/CardReward.cs:590-700`): index into offered cards, then into
  `CardRewardAlternative.Generate` (Skip if `CanSkip`, `REROLL` if `CanReroll`, then
  `Hook.ModifyCardRewardAlternatives`; more than 2 throws); `null` closes without completing;
  after a pick the loop continues while `Hook.ShouldAllowSelectingMoreCardRewards`.
- Content pushes selectors: `WhisperingEarring` pushes `VakuuCardSelector` (take the first
  `maxSelect` options; first card reward) while it auto-plays turn 1 (`Models.Relics/WhisperingEarring.cs:86`,
  `Models.Relics/VakuuCardSelector.cs`). The sim needs a selector stack whose bottom is the agent.
- Prompts can open inside hook dispatchers that build a `HookPlayerChoiceContext`:
  `AfterDeath`, `AfterDiedToDoom`, `AfterSideTurnEnd`, `BeforeFlush`, `BeforeSideTurnEnd`,
  `BeforeSideTurnStart` (`Hooks/Hook.cs:2364`, `:2467`, `:6170`, `:7776`, `:8398`, `:8607`) and
  `CombatManager` end-turn phases and turn start (`Combat/CombatManager.cs:2388`, `:2502`, `:2900`, `:5059`, `:5118`).
  Monster code passes `ThrowingPlayerChoiceContext` to many of its own commands (a prompt
  triggered from those calls would throw), but monster attacks run with a null context that
  `AttackCommand` replaces by `BlockingPlayerChoiceContext`, whose signals are no-ops
  (`Commands.Builders/AttackCommand.cs:423`, `:434`, `:1132`, `:1146`): a player listener that
  prompts in reaction to an enemy attack opens a decision point during the enemy turn.

Usage: `CardSelectCmd` in 107 content files (42 cards, 5 powers, 29 relics, 9 potions,
1 monster, 20 events, 1 modifier).

### 1.2 Enchantments

22 concrete models (+`DeprecatedEnchantment`, +`MockFreeEnchantment`). One per card; a second
application only stacks `Amount` on the same type (`Commands/CardCmd.cs:2151-2180`).
`CanEnchant` base rule: not Status/Curse/Quest, not an Unplayable deck card, no other
enchantment unless stackable and same type (`Models/EnchantmentModel.cs:294-315`).
`ApplyInternal` truncates the amount to `int` (`:337-348`); `ModifyCard` runs `OnEnchant`,
`RecalculateValues`, then `Card.DynamicVars.RecalculateForUpgradeOrEnchant` (`:356-366`).

Integration points:

| Hook | Where |
| --- | --- |
| `EnchantBlockAdditive/Multiplicative` | first step of `Hook.ModifyBlock` (`Hooks/Hook.cs:10071-10080`) |
| `EnchantDamageAdditive/Multiplicative` | inside `Hook.ModifyDamage` (`Hooks/Hook.cs:10336`, `:10340`) |
| `EnchantPlayCount` | replay count (`Models/CardModel.cs:2762`) |
| `OnPlay` | after the card effect (`Models/CardModel.cs:721`) |
| `ShouldStartAtBottomOfDrawPile` | combat start (`Combat/CombatManager.cs:4249`) |
| ordinary hooks | listener whenever its card is a listener (`Models/EnchantmentModel.cs:125`), right after the card and its affliction (`Combat/CombatState.cs:903-910`) |

Per-enchantment behaviour (from the models): Adroit (block = Amount on play), Clone (marker
for PaelsGrowth), Corrupted (attacks x1.5 powered damage, 2 unblockable self-damage on play),
Glam (+Times plays once per combat), Goopy (Defend only, adds Exhaust, +1 block per play,
also increments the deck copy), Imbued (skill, starts at draw-pile bottom, auto-played turn 1),
Inky (Weak to target or all), Instinct (attacks x2 powered), Momentum (+Amount damage per
play, cumulative), Nimble (+Amount block on block cards), PerfectFit (moved to top on
non-initial shuffles via `ModifyShuffleOrder`), RoyallyApproved (Innate + Retain), Sharp
(+Amount powered damage), Slither (cost re-rolled on draw with `Rng.CombatEnergyCosts.NextInt(4)`),
SlumberingEssence (−1 cost until played at `BeforeFlush` while in hand), SoulsPower (removes
local Exhaust), Sown (gain Amount energy once), Spiral (Basic Strike/Defend, +Times plays),
Steady (Retain), Swift (draw Amount once), TezcatarasEmber (cost → 0, Eternal, +3 damage),
Vigorous (+Amount damage on the first play).

Sim notes: combat cards must keep their deck link (`Card.DeckVersion`, used by Goopy);
`Status = Disabled` lives on the combat copy only.

### 1.3 Afflictions

Bound, Entangled, Galvanized, Hexed, Ringing, Smog, Tainted (+3 mocks). Applied only by
monster powers (ChainsOfBinding, Galvanic, Hex, Ringing, Smoggy, Tangled, VitalSpark) used in
CeremonialBeastBoss, GlobeHeadNormal, InfestedPrismsElite, KnightsElite, LivingFogNormal,
QueenBoss, VineShamblerNormal. `CardCmd.Afflict` checks `Hook.ShouldAfflict` and `CanAfflict`,
stacks same-type amounts and records `CardAfflictedEntry` (`Commands/CardCmd.cs:2235-2271`).
Random targets: `AfflictionModel.PickRandomTargets` shuffles with `Rng.CombatCardGeneration`,
then `RemoveRange(Clamp(n-1, 0, count), Max(0, n-count))` keeps the first `min(count, n)`
(`Models/AfflictionModel.cs:245-250`). Most behaviour is in the applying power; afflictions are
listeners right after their card.

### 1.4 Orbs (Defect)

| Orb | Passive | Evoke | Ref |
| --- | --- | --- | --- |
| Lightning | 3 damage to a random hittable opponent (`Rng.CombatTargets`, no draw if none) before turn end | 8, same targeting | `Models.Orbs/LightningOrb.cs:63-72`, `:336-338`, `:387` |
| Frost | 2 block before turn end | 5 block | `Models.Orbs/FrostOrb.cs:349-351` |
| Dark | adds its passive (6) to its own evoke value (starts at 6) | evoke value to the hittable enemy with the lowest current HP (`MinBy`: first in enemy order on ties) | `Models.Orbs/DarkOrb.cs:164-214` |
| Plasma | 1 energy after turn start | 2 energy | `Models.Orbs/PlasmaOrb.cs:220-259` |
| Glass | 4 to all hittable enemies, then −1 (min 0) | 2x passive to all | `Models.Orbs/GlassOrb.cs:234-283` |

Lightning, Frost and Glass values and Dark's passive go through `Hook.ModifyOrbValue`; Dark's accumulated evoke value and Plasma's values do not. `OrbCmd.Channel`
(`Commands/OrbCmd.cs:146-219`): if the character has `BaseOrbSlotCount == 0` and capacity is 0,
add 1 slot; if full, `EvokeNext` (first orb); enqueue; `History.OrbChanneled`;
`Hook.AfterOrbChanneled`. `EvokeLast` uses the last orb (`:714`). `AddSlots` caps capacity at 10
(`:647-657`); `RemoveSlots` evicts from the back (`Entities.Orbs/OrbQueue.cs:419-427`). A new
`OrbQueue` with `Player.BaseOrbSlotCount` capacity is created per combat
(`Entities.Players/PlayerCombatState.cs:274-276`; Defect = 3, `Models.Characters/Defect.cs:63`).
Turn triggers: `OrbQueue.BeforeTurnEnd` (`Combat/CombatManager.cs:1198`) and
`OrbQueue.AfterTurnStart` under a hook choice context (`Combat/CombatManager.cs:5119`).
`OrbModel.GetRandomOrb(Rng.CombatOrbGeneration)` draws from 5 ids including Glass
(`Models/OrbModel.cs:197-205`, `:402-405`) although `ModelDb.Orbs` lists only 4
(`Models/ModelDb.cs:212-218`); callers: Chaos, TrashToTreasurePower. Orbs are listeners after
potions and before cards. Mutable per-orb fields (Dark `_evokeVal`, Glass `_passiveVal`) must be
cloned and hashed.

### 1.5 Osty and pets (Necrobinder)

`Osty` is a pet monster (1 HP, `NOTHING_MOVE`, `Models.Monsters/Osty.cs`) found via
`Player.Osty` (`Entities.Players/Player.cs:247-251`). `OstyCmd.Summon`
(`Commands/OstyCmd.cs:336-394`): amount = `Hook.ModifySummonAmount`; 0 → nothing; Osty alive →
`GainMaxHp(amount)`; else revive (re-add the dead pet) or create (`PlayerCmd.AddPet<Osty>` +
`DieForYouPower` 1), then `SetMaxHp(amount)`, `Heal(amount)`, `Hook.AfterOstyRevived` on revive;
always `History.Summoned` then `Hook.AfterSummon`. `DieForYouPower` redirects powered attacks
aimed at the owner to Osty (`Models.Powers/DieForYouPower.cs:15-30`), keeps Osty in combat after
death and stays on it (`:37-49`); damage resolution has explicit `BeforeOsty`/`AfterOsty` phases
with overkill returned to the owner (`Commands/CreatureCmd.cs:3699-3719`); Osty dies with the
player (`:2705-2707`). Related content: `TargetType.Osty`, `CardTag.OstyAttack`, `OstyDamageVar`,
`CalculatedDamageVar.FromOsty`, history `CreatureAttacked` by Osty (Flatten, Rattle). Other pets:
monsters Fabricator/Fogmog/LivingFog/Ovicopter/TheObscura/ToughEgg/TwoTailedRat and relics
Byrdpip/PaelsLegion/Phylacteries. Pets are allies but not `PlayerCreatures`
(`Combat/CombatState.cs:560`).

### 1.6 Stars (Regent)

Per-combat integer on `PlayerCombatState` (new each combat, `Entities.Players/Player.cs:1211`).
The setter records `History.StarsModified(delta)` (`Entities.Players/PlayerCombatState.cs:138-154`).
`GainStars` is gated by `Hook.ShouldGainStars` and followed by `Hook.AfterStarsGained`
(`Commands/PlayerCmd.cs:390-393`, `:844`); `LoseStars` is a no-op once the combat is ending
(`:857-866`); both clamp at 0 (`Entities.Players/PlayerCombatState.cs:355-373`). Card star cost:
`CanonicalStarCost` (−1 = none), `BaseStarCost`, `TemporaryStarCost`, `HasStarCostX`,
`LastStarsSpent` (`Models/CardModel.cs:1848-1904`), adjusted by `Hook.ModifyStarCost`
(`Hooks/Hook.cs:11071`). `HasEnoughResourcesFor`: when energy is short and
`Hook.ShouldPayExcessEnergyCostWithStars`, each missing energy costs 2 stars
(`Entities.Players/PlayerCombatState.cs:334-353`). X from stars: `Hook.ModifyXValue(LastStarsSpent)`
(`Models/CardModel.cs:2772`). Usage: 44 files (32 cards, 4 powers, 7 relics, 1 potion).

### 1.7 Forge (Regent)

`ForgeCmd.Forge(amount)` (`Commands/ForgeCmd.cs:153-171`): nothing once over/ending; if no
non-dupe `SovereignBlade` outside the exhaust pile exists, create one (`CreatedThroughForge`)
into hand via `AddGeneratedCardToCombat`; then every non-dupe blade including exhausted ones gets
`AddDamage(amount)` + `AfterForged` (`:174-209`); then `Hook.AfterForge`. The rest of the file is
VFX. Usage: 15 files (11 cards, 2 powers, FencingManual, KingsCourage).

### 1.8 Ancients

Acts: Neow in Overgrowth and Underdocks; Orobas/Pael/Tezcatara in Hive; Nonupeipe/Tanx/Vakuu
in Glory; Darv shared. On enter (`Models/AncientEventModel.cs:78-105`): Neow first sets HP to 0;
heal `MaxHp − CurrentHp`, x0.8 at `WearyTraveler`. Options: `GenerateInitialOptions` unless
`Hook.ShouldAllowAncient` blocks (single PROCEED, `:275-289`). Dialogue
(`Entities.Ancients/*`) is chosen in UI code from profile visit counts with `Rng.Chaotic`
(`Nodes.Rooms/NEventRoom.cs:208-213`): no rule effect. `TheArchitect` is a plain `EventModel`
entered after the last act (`Runs/RunManager.cs:872`) with a 9999-HP `Architect` doing nothing
(`Models.Monsters/Architect.cs:13-20`); its dialogue and VFX consume the event `Rng`
(`Models.Events/TheArchitect.cs:285-286`, `:1042-1048`) after the run is won → treat as terminal.

### 1.9 Modifiers, Daily, GameMode

Standard runs start with `System.Array.Empty<ModifierModel>()` and `GameMode.Standard`
(`Nodes.Screens.CharacterSelect/NCharacterSelectScreen.cs:966`; non-empty lists are logged and
ignored, `:1794-1799`). Modifiers (Good: Draft, SealedDeck, Hoarder, Specialized, Insanity,
AllStar, Flight, Vintage, CharacterCards; Bad: DeadlyEvents, CursedRun, BigGameHunter, Midas,
Murderous, NightTerrors, Terminal; `Models/ModelDb.cs:317-351`) exist only in Custom/Daily runs;
no content adds one mid-run. Keep `RunState.Modifiers` empty so Neow's `Modifiers.Count <= 0`
branches take the standard path (`Models.Events/Neow.cs:120`, `:297`, `:365`). Daily seeding is
UI-side (`Nodes.Screens.DailyRun/NDailyRunScreen.cs:917-922`). `GameMode`'s only rule effect:
`ShouldApplyTutorialModifications` is true for Standard (`Runs/RunManager.cs:4090-4108`), so the
discovery-order logic of §5 runs in every standard run.

### 1.10 Badges, achievements, timeline UI

`BadgePool` evaluates 23 badges on the finished `SerializableRun` for the score
(`Models.Badges/BadgePool.cs`, `Runs/ScoreUtility.cs:127`). The two `BadgeModel` listeners
(`CccComboModel`, `DebufferModel`) only write `ExtraFields.CccomboBadgeUnlocked/DebuffsApplied`,
read only by badges. Achievements, Steam stats, metrics and epoch-reveal flow only write the
profile. Exclude all.

### 1.11 Multiplayer scaling and LocalContext

Single player is an identity for `MultiplayerScalingModel` (`ModifyBlockMultiplicative` returns
`Players.Count` when `<= 2`, i.e. 1, and multipliers equal to 1 are not recorded as modifiers,
`Models.Singleton/MultiplayerScalingModel.cs:41-57`, `Hooks/Hook.cs:10096-10110`), for monster
HP scaling (`Entities.Creatures/Creature.cs:986-993`, `:1332-1339`) and power scaling
(`Commands/PowerCmd.cs:550-552`). Not a pure no-op: `IRunState.CardMultiplayerConstraint` is
`SingleplayerOnly`, which removes the 37 `MultiplayerOnly` cards from every pool
(`Runs/IRunState.cs:74-84`, `Models/CardPoolModel.cs:68-81`), and `TargetType.AnyAlly` cards are
unplayable (`NoLivingAllies`, `Models/CardModel.cs:3347-3350`). Rooms per act are one fewer only
in multiplayer (`Models/ActModel.cs:185-193`). `LocalContext.IsMe(player)` is true, `IsMe` of a
non-player creature false; one rule-relevant gate is MembershipCard's price discount
(`Models.Relics/MembershipCard.cs:18-29`).

### 1.12 Card entity helpers (`Entities.Cards`)

Rule-bearing: `CardPile` (list order, top = index 0, `MaxCardsInHand` 10,
`RandomizeOrderInternal` = `UnstableShuffle` + test override + `Hook.ModifyShuffleOrder(isInitialShuffle: true)`,
`Entities.Cards/CardPile.cs:35`, `:220-320`), `CardPlay` (`Player`, `Target`, `Resources`,
`PlayIndex`/`IsFirstInSeries`, `IsAutoPlay`), `CardEnergyCost` with `LocalCostModifier`
(Absolute/Relative, reduce-only; expiration EndOfCombat=0, EndOfTurn=2, WhenPlayed=4) and
`TemporaryCardCost`/`TemporaryCardCostOffset` (UntilPlayed/ThisTurn/ThisCombat),
`CardTransformation` (fixed replacement, option list, or `CardFactory.CreateRandomCardForTransform`,
`Entities.Cards/CardTransformation.cs:140-195`), `CardCreationResult` (relic-modified reward card),
`ResourceInfo`, `CardLocation`, `CardPileAddResult`, enums and `IsSingleTarget`, `IsCombatPile`,
`GetNextHighestRarityWithWrapping`, `HasResourceCostReason`. UI-only: preview modes, cost colors,
keyword ordering, pile screen positions.

### 1.13 Helpers and extensions

Rule-bearing (with content usage):

| Helper | Semantics | Usage |
| --- | --- | --- |
| `AscensionHelper` | `GetValueIfAscension` reads `RunManager.Instance.HasAscension` (`Helpers/AscensionHelper.cs:14-36`) | 102 monster files |
| `ListExtensions.UnstableShuffle` | Fisher–Yates from the end, `NextInt(i+1)` (`Extensions/ListExtensions.cs:21-33`) | 9 files + engine |
| `ListExtensions.StableShuffle` | `List.Sort()` with `IComparable`, then `UnstableShuffle` (`:10-19`) | 17 files + reshuffle (`Commands/CardPileCmd.cs:2759`) |
| `IEnumerableExtensions.TakeRandom` | shuffle a full copy, then `Take(count)` (`Extensions/IEnumerableExtensions.cs:9-12`) | 4 files + `CardFactory.GetDistinctForCombat` |
| `GrabBag<T>` | weighted roulette, predicate by rejection resampling (`Helpers/GrabBag.cs:52-80`) | `ActModel` encounters |
| `StringHelper` | `Slugify`, `SnakeCase`, `GetDeterministicHashCode` (XXH64), `…Old` (`Helpers/StringHelper.cs:63-135`) | ids, seeds |
| `SeedHelper` | `CanonicalizeSeed`; `GetRandomSeed` loops while `BadWordChecker.ContainsBadWord` (`Helpers/SeedHelper.cs:13-43`) | seeds |
| `EggRelicHelper` | upgrade reward cards of one type via `RunState.CloneCard` | FrozenEgg, MoltenEgg, ToxicEgg |
| `TaskHelper.RunSafely` | fire-and-forget tasks inside rule code (`Helpers/TaskHelper.cs:210`) | 27 files (§6) |
| `Really.bigNumber` | 999999999 | clamps |

UI-only: `CardCostHelper`, `Ease`, `EnergyIconHelper`, `GlyphConverter`, `GodotTreeExtensions`,
`HandLayoutHelper`, `HandPosHelper`, `ImageHelper`, `MathHelper`, `SceneHelper`, `ScrollHelper`,
`SpineNodeExtensions`, `StsColors`, `TimeFormatting`, `DisplayVars`, `CommandLineHelper`,
`SignalAwaiterExtensions`; `ReflectionHelper` only discovers types (and mods).

### 1.14 DynamicVars

`DynamicVar.BaseValue` clamps to 999999999 (`Localization.DynamicVars/DynamicVar.cs:37`);
`EnchantedValue`/`PreviewValue` are display. `CalculatedVar` = `CalculationBase.BaseValue +
CalculationExtra.BaseValue × multiplier(card, target)`; the multiplier must be a static lambda
and is 0 unless `CombatManager.IsInProgress` and the card is in combat
(`Localization.DynamicVars/CalculatedVar.cs:28-48`) — most history queries enter the rules here.
Types: `DamageVar`, `BlockVar` (value + `ValueProp`), `CalculatedDamageVar`/`CalculatedBlockVar`,
`CalculationBaseVar`, `CalculationExtraVar`, `ExtraDamageVar`, `OstyDamageVar`, `PowerVar<T>`,
`SummonVar`, `CardsVar`, `EnergyVar`, `StarsVar`, `ForgeVar`, `GoldVar`, `HealVar`, `HpLossVar`,
`MaxHpVar`, `RepeatVar`, `IntVar`, `BoolVar`, `IfUpgradedVar` (display), `StringVar` (text);
`DynamicVarSet` named accessors (`Localization.DynamicVars/DynamicVarSet.cs:26-72`). Content
constructions: `DamageVar` 210, `PowerVar` 194, plain `DynamicVar` 182, `CardsVar` 171,
`BlockVar` 105, `EnergyVar` 86 (raw call sites).

### 1.15 ModelDb

Id = `ModelId(Category, Entry)`: Category = `Slugify` of the first subclass under
`AbstractModel` minus `_MODEL`, Entry = `Slugify(class name)` (`Models/ModelDb.cs:460-488`,
`Models/ModelId.cs:39-91`, `Helpers/StringHelper.cs:68-73`). Canonical instances are shared;
`ToMutable` clones. Pools: characters Ironclad, Silent, Regent, Necrobinder, Defect; shared card
pools Colorless, Curse, Deprecated, Event, Quest, Status, Token; shared relic pools Deprecated,
Event, Fallback, Shared; shared potion pools Deprecated, Event, **Mock**, Shared, Token
(`Models/ModelDb.cs:112-218`). Pool order = the `GenerateAll*` arrays (+ mods); epoch filters use
`RemoveAll`, which keeps order. Acts: Overgrowth (index 0, default), Underdocks (0), Hive (1),
Glory (2) (`:220-275`). `AllRelics`/`AllPotions` are sorted with the culture-sensitive default
string comparer (`:179`, `:198`) but no rule iterates them; `ModelId.CompareTo` is ordinal.

ModelDb itself has no randomized lookup; content draws through the factories:
`CardFactory.GetDistinctForCombat` (player-count filter, then `FilterForCombat` = distinct,
`CanBeGeneratedInCombat`, not Basic/Ancient/Event, then `TakeRandom`), `GetForCombat` (`NextItem`
with replacement per card), `CreateForReward` (rarity roll, then `NextItem` over that rarity,
`PlayerRng.Rewards` unless overridden), `CreateRandomCardForTransform` (`NextItem` over the
filtered pool) (`Factories/CardFactory.cs:25-200`); `PotionFactory` (character + Shared unlocked
potions, rarity then `NextItem`, `Factories/PotionFactory.cs:22-66`); `RelicFactory` (grab-bag
front/back pulls, `RollRarity` 0.5/0.83 on `PlayerRng.Rewards`, fallback Circlet,
`Factories/RelicFactory.cs:13-67`); `OrbModel.GetRandomOrb`. The single-player filter in
`CardFactory.FilterForPlayerCount` drops `MultiplayerOnly` cards (`Factories/CardFactory.cs:25-32`).

### 1.16 Hook listener order (cross-cutting)

`CombatState.IterateHookListeners` (`Combat/CombatState.cs:856-956`): for each creature, allies
then enemies: its powers; a monster adds its model; an active player adds relics (not melted),
potions in slots, orbs, then for every pile every card followed by its affliction and its
enchantment; after all creatures: modifiers, badge models, the multiplayer scaling model, then mod
subscribers. Enchantments, afflictions and orbs therefore have fixed positions in every dispatch.

## 2. Official test seams and the execution order they imply

Switches: `TestMode.IsOn` (static, `TestSupport/TestMode.cs:11-48`) and
`NonInteractiveMode.IsActive = TestMode.IsOn || AutoSlayerCheck()` (`Helpers/NonInteractiveMode.cs:10-28`).

Injection points:

| Seam | Ref | What it controls |
| --- | --- | --- |
| `ICardSelector` stacks in `CardSelectCmd` | `TestSupport/ICardSelector.cs:9-14`, `Commands/CardSelectCmd.cs:3084-3150` | every card prompt except bundles, plus card rewards |
| `TestCardSelector` | `TestSupport/TestCardSelector.cs:221-319` | queued answers (by card or index; unknown card throws); no answer → empty; default reward = first card; `PrepareToBlock` |
| `TestRngInjector` | `TestSupport/TestRngInjector.cs:8-70` | one-shot overrides of relic pulls (`Factories/RelicFactory.cs:32`, `:44`), relic rarity (`:56`, skips the `NextFloat`), combat card generation (`Factories/CardFactory.cs:95`), initial shuffle (`Entities.Cards/CardPile.cs:244`) |
| `RunRngSet.MockRng`, `GetMockInstance` | `Runs/RunRngSet.cs:57-65`, `:191-194` | replace one named stream |
| `RunState.CreateForTest` | `Runs/RunState.cs:597-604` | Deprived test character, `UnlockState.all`, default acts |
| `ModelDb.Init/Inject/Remove/ResetForTest` | `Models/ModelDb.cs:353-384` | register mock models |
| SaveManager mock | `Saves/SaveManager.cs:950-981`, `:1097-1104` | in-memory saves |
| `RewardsSet.testSelector`, `RestSiteOption.generateForTests` | `Rewards/RewardsSet.cs:290-302`; `Entities.RestSite/RestSiteOption.cs:19`, `:49-52` | rewards screen and rest options |
| per-model overrides | `Models.Relics/SneckoEye.cs:269-275`, `Models.Powers/ConfusedPower.cs:25`, `Models.Potions/SneckoOil.cs:132`, `Models.Enchantments/Slither.cs:23`, `Models.Cards/MadScience.cs:431` | fixed costs / fixed chaos card |

The four `TestRngInjector` hooks mark the high-entropy draws the developers needed to pin:
relic grab-bag pulls, relic rarity, in-combat card generation, the opening shuffle. The sim's
tests should offer the same four seams.

**TestMode branches that change rules or RNG consumption** (the sim must take the shipping branch):

| Ref | TestMode | Shipping |
| --- | --- | --- |
| `Commands/CardSelectCmd.cs:3813` | bundle prompt picks `bundles[0]` | player chooses |
| `Runs/RunManager.cs:4096` | no discovery-order modifications | applied in every standard run |
| `Runs/RunManager.cs:4232` | no tutorial room type | first-ever run: `Unassigned` point → Event |
| `Models/ActModel.cs:487` | no forced act | undiscovered unlocked act is forced, no draw for that index |
| `Entities.Merchant/MerchantPotionEntry.cs:196-199` | no price jitter, `PlayerRng.Shops` untouched | `(int)Mathf.Round(cost × Shops.NextFloat(0.95f, 1.05f))` |
| `Models.Powers/RollingBoulderPower.cs:98-115` | one multi-target `CreatureCmd.Damage` | one damage call per creature, triggered by the VFX `HitCreature` signal, awaited with `WhenAll` |
| `Models.Relics/Cauldron.cs:114`, `Models.Relics/CallingBell.cs:162` | fixed test rewards | random rewards |
| `Rewards/RewardsSet.cs:290` | `testSelector` | rewards screen |

All other TestMode branches (about 30 content files: monster VFX/audio, card previews, node
lookups, logging) have no rule effect.

Execution-order facts:

1. `NonInteractiveMode`: each `GameAction` is awaited to completion before the next is dequeued
   (`GameActions/ActionExecutor.cs:197-209`); interactively an action paused for a choice lets the
   executor run other ready actions (`:151-161`). In single player the open prompt blocks the
   only input source, so no other action should become ready meanwhile (to confirm in the
   combat-kernel study); the synchronous model then matches.
2. After every action except `EndPlayerTurnAction` and `ReadyToBeginEnemyTurnAction`,
   `CheckWinCondition` runs while the combat is in progress (`GameActions/ActionExecutor.cs:130-139`).
3. A non-interactive action that finishes paused throws (`GameActions/GameAction.cs:107`): every
   choice resolves synchronously inside the action.
4. `Cmd.Wait`/`CustomScaledWait` are skipped (`Commands/Cmd.cs:75`, `:172`); pausing is disabled
   (`GameActions/ActionExecutor.cs:505-512`, `Combat/CombatManager.cs:7504-7518`).
5. Selector answers bypass choice-id reservation and `SignalPlayerChoiceBegun/Ended`; auto-
   resolution happens before any selector (`Commands/CardSelectCmd.cs:3236-3796`).
6. In TestMode nothing may subscribe to `CombatStateChanged` (`Combat/CombatStateTracker.cs:360-366`):
   rule code never depends on UI state notifications.
7. Prompts arise from player actions, the hook dispatchers listed in §1.1, and listeners
   reacting to enemy attacks (`BlockingPlayerChoiceContext` fallback in `AttackCommand`); commands
   that monster code issues with `ThrowingPlayerChoiceContext` can never prompt.

Rule for the port: **rules = `TestMode.IsOff`, timing = `NonInteractiveMode`**.

## 3. AutoSlay as a legal-action reference

AutoSlay drives the real Godot UI, but its combat logic reads model-level legality.

Setup (`AutoSlay/AutoSlayer.cs:1911-1920`): seed override, FastMode Fast, FTUEs off, reveals
Silent1/Regent1/Defect1/Necrobinder1 epochs, own `Rng(GetDeterministicHashCode(seed))` (separate
from game streams), `CardSelectCmd.UseSelector(new AutoSlayCardSelector(rng))`. Handlers by room
type and by overlay screen type (`:2534-2563`); loop while `TotalFloor < 49`
(`:2012`, `AutoSlay/AutoSlayConfig.cs:27`), draining the overlay stack after each room.

`AutoSlayCardSelector` (`AutoSlay.Helpers/AutoSlayCardSelector.cs:23-51`): `n = min(maxSelect,
count)`, raised to `min(minSelect, count)` if below `minSelect`; shuffle; take `n`. Card rewards:
uniform card, never an alternative.

| Room / screen | AutoSlay behaviour | Model-level legality it reveals |
| --- | --- | --- |
| Combat (`AutoSlay.Handlers.Rooms/CombatRoomHandler.cs:278-408`, `:602-652`) | cheats (Plating 999, Regen 999, +200 Strength from turn 3); uses every potion each turn; plays random `CanPlay` cards (≤ 50/turn) with a random `HittableEnemies` target for AnyEnemy, else no target; ends turn (`PlayerCmd.EndTurn(canBackOut: false)`) | card: `CardModel.CanPlay(out reason, out preventer)`; potion target by `TargetType` |
| Event (`…/EventRoomHandler.cs:532-640`) | random option with `!Option.IsLocked`, preferring `WillKillPlayer` false; clicks through ancient dialogue; kills event-combat enemies | option legal iff `OnChosen != null` (`Events/EventOption.cs:279`, `:292`) |
| Rest site (`…/RestSiteRoomHandler.cs:167-172`) | random `Option.IsEnabled` | `RestSiteOption.IsEnabled` |
| Shop (`…/ShopRoomHandler.cs:216-221`) | buy random `IsStocked && EnoughGold` (skips removal) until none | `MerchantEntry.IsStocked && EnoughGold` |
| Treasure (`…/TreasureRoomHandler.cs:187-195`) | open chest, click enabled holders | holder `IsEnabled` |
| Map (`AutoSlay.Handlers.Screens/MapScreenHandler.cs:269-286`) | first row-0 point, else first child of the last visited point | children of the current point |
| Rewards (`…/RewardsScreenHandler.cs:51-57`, `:155-179`) | click each enabled reward once (potions only with a free slot), proceed | claimable rewards |
| Card reward / Choose a card / bundle / relic (`…/CardRewardScreenHandler.cs:104-109`, `…/ChooseACardScreenHandler.cs:57-63`, `…/ChooseABundleScreenHandler.cs:98-149`, `…/ChooseARelicScreenHandler.cs:51-57`) | random option | any option |
| Deck screens (`…/DeckUpgradeScreenHandler.cs:150-205` and siblings) | UI fallback, up to min(count, 5) random picks | normally answered by the selector |
| Crystal sphere (`…/CrystalSphereScreenHandler.cs:398-417`) | random visible hidden cell, default tool | `cell.IsHidden`; tool `Small`/`Big` (`Events.Custom.CrystalSphereEvent/CrystalSphereMinigame.cs:25-31`) |

Prompt reference for the sim's `LegalActionGenerator`:

| Prompt | Options | Count | Auto-resolves | Skip / cancel | Option order |
| --- | --- | --- | --- | --- | --- |
| combat main | `CanPlay` hand cards (+targets), potions, end turn | 1 | — | EndTurn always | hand, target, EndTurn last |
| choose a card | ≤ 3 cards | 0..1 | 0 options | only if `canSkip` | given |
| simple grid / rewards grid | given | `[Min, Max]` | `count ≤ Min && !RMC` → all | `Min == 0` | given |
| combat pile | filtered pile | `[Min, Max]` | as above | `Min == 0` | Draw: `(Rarity, Id)`; else pile order |
| hand / hand discard | filtered hand | `[Min, Max]` | as above | `Min == 0` | hand |
| hand upgrade | upgradable hand cards | 1 | `≤ 1` option | no | hand |
| deck upgrade / transform / enchant / removal / generic | filtered deck | `[Min, Max]` | as above (enchant: `count ≤ Min`) | `Min == 0`; `Cancelable` (Smith, Cook, reward removal) | deck; removal: curses first |
| choose a bundle | bundles | 1 | 0 bundles | no | given |
| card reward | cards + ≤ 2 alternatives | loop while more picks allowed | — | Skip alternative, or close | cards, then alternatives |
| rewards screen | unclaimed rewards | any subset | — | proceed | list |
| event | `!IsLocked` options | 1 | — | `IsProceed` options | option order |
| rest site | `IsEnabled` options | 1 | — | proceed | option order |
| shop | `IsStocked && EnoughGold` entries | any | — | leave | inventory |
| treasure / map / crystal sphere | as in the table above | | | | |

(RMC = `RequireManualConfirmation`. Refs per row in `subsystems.json → legal_action_reference`.)
Do not copy AutoSlay's cheats or its own RNG.

## 4. Combat history

Store: `CombatManager.Instance.History`, append-only, cleared at combat end and reset
(`Combat/CombatManager.cs:1759`, `:7084`); entries are added only for live combats
(`Combat.History/CombatHistory.cs:159-168`; `CombatState.IsLiveCombat()` is always true).
Each entry stores `Actor`, `RoundNumber`, `CurrentSide` and every player's `TurnNumber`
(`Combat.History/CombatHistoryEntry.cs:46-60`).

- `HappenedThisTurn(state)`: same round, same side, and every snapshotted player's turn number
  unchanged (`:67-112`).
- `HappenedLastPlayerTurn(player)`: that player's snapshot equals current `TurnNumber − 1`
  (`:115-125`).

Entry types (writer → readers): `CardPlayStarted` (`Models/CardModel.cs:665`, 8 readers),
`CardPlayFinished` (`:735`, 17; stores `WasEthereal`), `CardAfflicted` (`Commands/CardCmd.cs:2269`, 1),
`CardDiscarded` (`:829`, 1), `CardDrawn` (`Commands/CardPileCmd.cs:1919`, `:4050`, 3; stores
`FromHandDraw`), `CardExhausted` (`Commands/CardCmd.cs:1041`, `:1877`, 2), `CardGenerated`
(`:1471`, `:2069`; `Commands/CardPileCmd.cs:1194`, `:3237`, 1; stores `Creator`),
`CreatureAttacked` (`Commands.Builders/AttackCommand.cs:422`, `:1145`, 2), `DamageReceived`
(`Commands/CreatureCmd.cs:776`, `:3737`, 5), `BlockGained` (`:1872`, `:3973`, 1), `EnergySpent`
(`Models/CardModel.cs:1255`, 1), `OrbChanneled` (`Commands/OrbCmd.cs:216`, 1), `PowerReceived`
(`Commands/PowerCmd.cs:458`, `:883`, `:1321`, 1), `StarsModified`
(`Entities.Players/PlayerCombatState.cs:150`, 1). Write-only: `MonsterPerformedMove`
(`Models/MonsterModel.cs:286`), `PotionUsed` (`Models/PotionModel.cs:247`), `Summoned`
(`Commands/OstyCmd.cs:269`, `:391`).

All content queries (window: T = this turn, L = last player turn, C = whole combat):

| Site | Entry | Agg | Win | Filter | Used for |
| --- | --- | --- | --- | --- | --- |
| `Models.Cards/BansheesCry.cs:134` | PlayFinished | count | C | `WasEthereal`, own | cost −n×Energy |
| `Models.Cards/BeatIntoShape.cs:132` | DamageReceived | count | T | receiver = target, dealer = owner, powered attack | forge multiplier |
| `Models.Cards/Bolas.cs:61` | PlayFinished | any | L | card = this instance | turn-start trigger |
| `Models.Cards/DeathMarch.cs:97` | CardDrawn | count | T | actor = owner, `!FromHandDraw` | damage multiplier |
| `Models.Cards/DeathsDoor.cs:146` | PowerReceived | any | T | `DoomPower`, applier = owner | condition |
| `Models.Cards/EvilEye.cs:138` | CardExhausted | any | T | actor = owner | condition |
| `Models.Cards/Fetch.cs:149` | PlayFinished | any | T | card = this instance | condition |
| `Models.Cards/Finisher.cs:103` | PlayFinished | count | T | Attack, own | hits multiplier |
| `Models.Cards/Flatten.cs:114` | CreatureAttacked | any | T | actor = Osty | condition |
| `Models.Cards/Ftl.cs:139` | PlayFinished | count | T | own | `< PlayMax` |
| `Models.Cards/GangUp.cs:99` | DamageReceived | count | T | receiver = target, powered, dealer ≠ owner, same side | damage multiplier |
| `Models.Cards/GoldAxe.cs:96` | PlayFinished | count | C | all | damage multiplier |
| `Models.Cards/HelixDrill.cs:108` | EnergySpent | sum | T | own | energy computation |
| `Models.Cards/LunarBlast.cs:100` | PlayFinished | count | T | Skill, own | hits multiplier |
| `Models.Cards/MakeItSo.cs:58` | PlayFinished | count | T | Skill, own | every N-th skill returns it |
| `Models.Cards/MementoMori.cs:97` | CardDiscarded | count | T | actor = owner | damage multiplier |
| `Models.Cards/Midnight.cs:129` | CardExhausted | count | C | all | cost reduction on entering combat |
| `Models.Cards/Murder.cs:97` | CardDrawn | count | C | actor = owner | damage multiplier |
| `Models.Cards/Normality.cs:34` | PlayStarted | count | T | own | play limit |
| `Models.Cards/Pinpoint.cs:128` | PlayFinished | count | T | Skill, own | cost reduction |
| `Models.Cards/PullFromBelow.cs:104` | PlayFinished | count | C | own, `WasEthereal` | hits multiplier |
| `Models.Cards/Radiate.cs:102` | StarsModified | sum | T | `Amount > 0`, actor = owner | hits multiplier |
| `Models.Cards/Rattle.cs:119` | CreatureAttacked | 1+count | T | actor = Osty | hits multiplier |
| `Models.Cards/Spite.cs:130` | DamageReceived | any | T | receiver, `UnblockedDamage > 0` | condition |
| `Models.Cards/Stomp.cs:180` | PlayFinished | count | T | Attack, own | cost reduction |
| `Models.Cards/Supermassive.cs:97` | CardGenerated | count | C | creator = owner | damage multiplier |
| `Models.Cards/TearAsunder.cs:102` | DamageReceived | 1+count | C | receiver = owner, `UnblockedDamage > 0` | hits multiplier |
| `Models.Cards/ThrummingHatchet.cs:57` | PlayFinished | any | L | card = this instance | turn-start trigger |
| `Models.Cards/Voltaic.cs:140` | OrbChanneled | count | C | own, `LightningOrb` | channels multiplier |
| `Models.Powers/ChainsOfBindingPower.cs:66` | CardAfflicted | count | T | actor = owner, `Bound` | afflict while `< Amount` |
| `Models.Powers/EchoFormPower.cs:65` | PlayStarted | count | T | actor = owner, `IsFirstInSeries` | +1 play while `< Amount` |
| `Models.Powers/FeralPower.cs:35` | PlayStarted | count | T | Attack, own, `EnergyValue == 0` | counter init |
| `Models.Powers/IterationPower.cs:59` | CardDrawn | count | T | actor = owner, Status | draw if `≤ 1` |
| `Models.Powers/JugglingPower.cs:122` | PlayStarted | count | T | Attack, own | counter init |
| `Models.Powers/LethalityPower.cs:42` | PlayStarted | count | T | Attack, own | first-attack multiplier |
| `Models.Powers/NostalgiaPower.cs:33` | PlayStarted | count | T | Attack or Skill, own | result to draw top while `< Amount` |
| `Models.Powers/PaleBlueDotPower.cs:147` | PlayFinished | count | T | own (no type filter) | threshold |
| `Models.Powers/PhantomBladesPower.cs:77` | PlayFinished | count | T | Shiv tag, own | first-Shiv bonus |
| `Models.Powers/RingingPower.cs:328` | PlayStarted | none | T | own | Ringing card only as first play |
| `Models.Powers/SmoggyPower.cs:57` | PlayStarted | any | T | Skill, own | afflict later skills |
| `Models.Powers/UnmovablePower.cs:37` | BlockGained | count | T | own card play, card/monster-move block, other play | double block while `< Amount` |
| `Models.Relics/EmotionChip.cs:155` | DamageReceived | any | L | receiver = owner, not fully blocked | condition |
| `Models.Relics/HistoryCourse.cs:58` | PlayFinished | last | L | own Attack, not dupe | replay that card |
| `Models.Relics/PaelsEye.cs:258` | PlayFinished | any | T | actor = owner, not auto-play | condition |
| `Models.Relics/RippleBasin.cs:59` | PlayFinished | none | T | Attack, own | block if no attack |

State model: keep the ordered log, not counters. Bolas/ThrummingHatchet/Fetch need card-instance
identity, HistoryCourse needs the last matching card, Midnight/Pinpoint/Stomp/BansheesCry read the
whole combat when a generated card enters it. Entries are immutable, so clones can share the
backing array and keep their own length; snapshot every player's `TurnNumber` exactly as
`CombatHistoryEntry.cs:58` does.

## 5. Unlock and profile dependence

`UnlockState` = {revealed epoch ids, encounters seen, `NumberOfRuns`} snapshotted per player at
run creation (`Nodes/NGame.cs:3138`, `Entities.Players/Player.cs:707-716`) and saved in
`SerializablePlayer.UnlockState` (`Saves.Runs/SerializablePlayer.cs:169`); `null` → `UnlockState.all`
(`Unlocks/UnlockState.cs:131-136`); `all` = every epoch, every encounter, `NumberOfRuns`
999999999 (`:18`). The multiplayer daily lobby uses `all` (`Multiplayer.Game.Lobby/StartRunLobby.cs:1115-1121`).

Epoch-gated content (`Timeline.Epochs/*`): per character tiers 2/5/7 = 3 cards each, 3/6 = 3
relics each, 4 = 3 potions; Colorless1–5 = 3 cards each; Relic1–5 = 3 relics each; Potion1–2 =
3 potions each; Event1–3 = TrashHeap, Reflections, ColorfulPhilosophers; Silent1/Regent1/
Necrobinder1/Defect1 = characters; NeowEpoch, UnderdocksEpoch, DarvEpoch, OrobasEpoch gate rules
without lists; Act2B/Act3B/CustomAndSeeds/DailyRun have no rule effect in v0.111.0.

Every rule path that reads unlock or profile state:

| Id | Source | Effect | Sites |
| --- | --- | --- | --- |
| U1 | UnlockState | card pool membership (all random card generation, rewards, shop, transforms) | `Models/CardPoolModel.cs:68-91`, `Models.CardPools/*CardPool.cs` `FilterThroughEpochs`, `Runs/CardCreationOptions.cs:121`, `Entities.Merchant/MerchantInventory.cs:119`, `:134`, `Factories/CardFactory.cs:126`; direct `UnlockState` use in 16 cards, 5 powers, 13 relics, 6 potions, 7 events, 2 modifiers (list in the JSON) |
| U2 | UnlockState | relic pools and grab bags | `Models.RelicPools/SharedRelicPool.cs:139-162`, character pools `:33-44`, `Runs/RunManager.cs:3828`, `Runs/RelicGrabBag.cs:72-73` |
| U3 | UnlockState | potion pools | `Models.PotionPools/SharedPotionPool.cs:66-100`, character pools `:21-27`, `Factories/PotionFactory.cs:65`, 5 events |
| U4 | UnlockState | event pool per act | `Models/ActModel.cs:241-257` |
| U5 | UnlockState | Neow / Orobas / Darv availability | `Models.Acts/Overgrowth.cs:100-108`, `Models.Acts/Underdocks.cs:93-101`, `Models.Acts/Hive.cs:98-106`, `Unlocks/UnlockState.cs:56-67`, `Runs/RunManager.cs:4055-4072`, `Models/ActModel.cs:322` |
| U6 | UnlockState | Underdocks as act 1 | `Models.Acts/Underdocks.cs:107-110`, `Models/ActModel.cs:470-505` |
| U7 | UnlockState | character set (other-character picks) | `Unlocks/UnlockState.cs:31-54`, `Models.Events/Orobas.cs:200`, `Models.Relics/Kaleidoscope.cs:56`, `:120`, `Models.Cards/Splash.cs:76`, `Models.Relics/PrismaticGem.cs:47`, `Models.Events/ColorfulPhilosophers.cs:109`, `:116` |
| U8 | UnlockState | boss = first unseen `BossDiscoveryOrder` entry, in every standard run | `Models/ActModel.cs:338-358`, `Runs/RunManager.cs:4078-4081` |
| U9 | UnlockState | first-ever run: fixed Overgrowth order, first unknown rooms Event/Event/Monster, Ironclad tutorial rewards | `Models.Acts/Overgrowth.cs:110-127`, `Odds/UnknownMapPointOdds.cs:109-120`, `Rewards/RewardsSet.cs:527-560` |
| U10 | UnlockState | Neow start vs. a Monster first room | `Runs/RunManager.cs:3924-3927`, `:327-343`, `:2146-2149` |
| L1 | live profile | undiscovered act forced, no draw for that index | `Models/ActModel.cs:487`; act RNG `Multiplayer.Game.Lobby/StartRunLobby.cs:705-707` |
| L2 | live profile | first-ever run tutorial room | `Runs/RunManager.cs:4229-4258` |
| L3 | live profile | WelcomeToWongos badge relic at 2000 cumulative points | `Models.Events/WelcomeToWongos.cs:419-433`, `:634-646` |
| L4 | live profile | TheArchitect dialogue (post-victory) | `Models.Events/TheArchitect.cs:1042-1048` |
| L5 | live profile | SP ascension clamped to the character's max | `Multiplayer.Game.Lobby/StartRunLobby.cs:717-731` |
| D1 | live profile, display only | Vakuu visits, TestSubject title, LoomingFruit icon, game-over ascension text, ancient dialogue | `Models.Events/Vakuu.cs:49-56`, `Models.Monsters/TestSubject.cs:1114-1121`, `Models.Relics/LoomingFruit.cs:80-104`, `Entities.Players/Player.cs:704`, `Nodes.Rooms/NEventRoom.cs:208-213` |

Recommendation: a run parameter `RunProfile { per-player UnlockState snapshot, DiscoveredActs,
ProfileNumberOfRuns, WongoPoints }`. Default `AllUnlocked` (`UnlockState.all`, all acts
discovered, WongoPoints 0) — every tutorial and discovery path becomes a no-op, which matches an
experienced A10 profile. For differential replay load each `SerializablePlayer.UnlockState` from
the save (acts, bosses and room sets are already in the save). Ascension is an explicit parameter.

## 6. Traps and open questions

Traps:

1. Static run state: selector stacks (`Commands/CardSelectCmd.cs:3084-3086`), `TestRngInjector`,
   `RunManager/CombatManager/SaveManager.Instance`, `LocalContext.NetId`, `TestMode.IsOn`,
   `NonInteractiveMode.AutoSlayerCheck`, `RewardsSet.testSelector`, `RestSiteOption.generateForTests`.
   The sim keeps all of it in cloneable state.
2. `CardRarityOdds.regularCommonOdds` is a static initializer (`Odds/CardRarityOdds.cs:13`), but the
   roll methods only read Rare and Uncommon odds (`:75-76`, `:91-92`): inert in v0.111.0.
3. Godot inside rules: `RollingBoulderPower` per-creature damage driven by a VFX signal
   (`Models.Powers/RollingBoulderPower.cs:98-115`); `Mathf.Round/RoundToInt/FloorToInt` in shop
   prices and GalacticDust (`Entities.Merchant/MerchantPotionEntry.cs:198`,
   `Entities.Merchant/MerchantCardEntry.cs:181`, `:260`, `Models.Relics/GalacticDust.cs:64`);
   `deathAnimLength` passed into `AfterDeath` hooks (visibility only).
4. Fire-and-forget tasks in rule code (`TaskHelper.RunSafely`): `Models.Relics/LordsParasol.cs:555`,
   `Rooms/TreasureRoom.cs:129`, `Rooms/CombatRoom.cs:262`,
   `Events.Custom.CrystalSphereEvent/CrystalSphereMinigame.cs:656`, `Rewards/CardReward.cs:556`,
   `:583`, `:587`, `Factories/CardFactory.cs:88`. The synchronous port runs them inline.
5. Data-dependent draw counts: `GrabBag` rejection resampling, `TakeRandom` shuffles everything,
   `Rng.NextItem` draws even for one item and not for zero (`Random/Rng.cs:213-223`),
   `SeedHelper` bad-word rejection, `PickRandomTargets`, `GetRandomOrb` over 5 ids, Slither
   `NextInt(4)`, relic rarity thresholds 0.5/0.83 (`Factories/RelicFactory.cs:56-67`).
6. `StableShuffle` uses `List.Sort` (introsort, unstable for equal keys) with the model comparer
   (ModelId ordinal, then upgrade level, `Models/CardModel.cs:3649-3671`): keep `List.Sort`, never
   `OrderBy`.
7. TestMode changes RNG consumption (`MerchantPotionEntry`, relic rarity override, act discovery):
   game tests are not oracles there.
8. Culture-sensitive `OrderBy` on `AllRelics`/`AllPotions` (`Models/ModelDb.cs:179`, `:198`) and
   per-process string hashing of `ModelId`: never use them for ordered rule decisions.
9. Mods: `ModHelper.ConcatModelsFromMods` in pools (`Models/CardPoolModel.cs:42`,
   `Models/RelicPoolModel.cs:28`, `Models/PotionPoolModel.cs:30`) and mod listeners
   (`Combat/CombatState.cs:944`): assume none.
10. `MockPotionPool` is a registered shared potion pool (`Models/ModelDb.cs:187-194`); rules never
    read `AllPotionPools`, but a naive sim pool list would include it.
11. `PaleBlueDotPower.AttacksPlayedThisTurn` counts every card (`Models.Powers/PaleBlueDotPower.cs:147`).
12. `TheArchitect` consumes event RNG after victory (terminal state).
13. Deck link: Goopy updates `Card.DeckVersion` from combat.

Open questions (also in the JSON):

1. Hit order and grouping of RollingBoulder in the shipping game (needs `NRollingBoulderVfx` or an
   oracle trace).
2. Exact GodotSharp `Mathf` rounding semantics (not in the decompile tree).
3. Do our recorded saves carry `SerializablePlayer.UnlockState`? The exporter must forward it.
4. `WelcomeToWongos` needs profile WongoPoints, which recordings lack.
5. When a prompt opens inside a `HookPlayerChoiceContext` dispatcher, do the remaining listeners
   run before or after the choice (combat-kernel study)?
6. Which point is entered as `MapPointType.Unassigned` on a first-ever run?
7. Are all 15 AnyAlly cards/potions `MultiplayerOnly`?
8. Clone cost of the shared history log in long boss fights.
9. Do any of the fire-and-forget tasks change rule state after the caller continues?
10. Prompt seams differ between v0.111.0 and the v0.107.1 corpus? (bundle prompt, RelicSelectCmd).
