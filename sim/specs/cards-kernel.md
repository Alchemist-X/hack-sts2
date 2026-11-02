# Cards kernel (W1, CONTRACTS Part B §B7, §B5 enchantment and affliction rows, §B2 card members, §B12 A6 / A7 / A23)

2026-11-01, cards-kernel track, on top of `263810d` (W1 phase-0b amendment fixes). Rule truth: decompile v0.111.0,
cited as `<ns-dir>/<File>.cs:<line>` with `MegaCrit.Sts2.Core.` dropped. Owned files: `sim/Sts2Sim.Core/Cards/*`
(except `CardPlay.cs`), `sim/Sts2Sim.Core/Combat/Pipeline/CardCmd.cs`, `sim/Sts2Sim.Core/Unlocks/UnlockProfile.cs`,
`sim/Sts2Sim.Tests/CardKernelTests.cs` (`Test_CardKernel_*`), this file. This file supersedes the rows of `cards.md`
§4 (`GetEnchantedReplayCount`), §8 (`CanPlay` keyword source) and §12 (global keywords, enchantments, downgrade,
deck fields) that it names; `cards.md` and `cards-upgrades.md` stay valid for the P0 defs otherwise.

## 0. Status

| Item | Status | Section |
| --- | --- | --- |
| `CardInstance` B2 members: `Owner` (every creation path), `Enchantment`, `Affliction`, `Listener`, `HasBeenRemovedFromState`, `DeckVersionId`, `CloneForCombat` | ported | §1 |
| Keywords in combat (`GetKeywordsWithSources`, `Keywords`, `HasKeywordInCombat`) and their game read sites in this area | ported | §1.9 |
| Star costs, temporary costs, X captures, `HasEnoughResourcesFor` with the combat guard, the global energy hook gate | ported | §1.10 |
| `Upgrade` / `Downgrade` internals, `CompareTo`, pile helpers, extra per-card state | ported | §1.11-§1.14 |
| Card half of `CardCmd`: `Upgrade`, `Downgrade`, `Transform*`, `Enchant`, `ClearEnchantment`, `Afflict*`, `ClearAffliction`, `ApplyKeyword`, `RemoveKeyword`, `ApplySingleTurnSly/Retain` | ported (deck-pile branches: §3.6) | §3 |
| `EnchantmentModel`, `AfflictionModel` bases; pilots `GLAM`, `HEXED` | ported | §4, §5 |
| Card pools (12, `GenerateAllCards` order), per-card catalog (596 cards), epoch gates, `GetUnlockedCards`, `UnlockProfile` pool queries | ported | §6 |
| `CardFactory` combat entry points and the combat transform helpers | ported (unported pool cards: §7.4, flagged) | §7 |
| `DynamicVar` + `DynamicVarSet` + the 24 subclasses of `Localization.DynamicVars` | ported | §8 |
| Pilots `ARMAMENTS`, `ACROBATICS`, `HEADBUTT`, `SEANCE` (+ `SOUL`), `DISCOVERY`, `SEEKER_STRIKE`, `SOVEREIGN_BLADE` | ported | §9 |
| Play-path wiring (`OnPlayWrapper` calls of `Enchantment.OnPlay` / `Affliction.OnPlay`, the combat-start `ShouldStartAtBottomOfDrawPile` move) | loop (orders traced in §2) | §2 |
| Hook traversal of card listeners, the enchantment damage/block pre-pass | hooks (B3, B12 A9); this area supplies the per-card slice and the virtuals | §2, §4 |

## 1. `CardInstance`

### 1.1 Owner

| Rule | Sim | Source |
| --- | --- | --- |
| The `Owner` setter throws "already has an owner" whenever an owner is set, even to the same player | `AttachTo(owner)` throws `InvalidOperationException` when `HasOwner` | `Models/CardModel.cs:1777-1794` |
| Combat creation = `ToMutable` + `AddCard(card, owner)` (owner set) + `AfterCreated` | `cs.CreateCard(def, owner)` (state); `CardDef.AfterCreated(card)` | `Combat/CombatState.cs:671-677`, `:686-690`; `Models/CardModel.cs:2884-2886` |
| `CloneCard` = `ClonePreservingMutability` (`MemberwiseClone` keeps `_owner`) + `AddCard` | `CloneForCombat` keeps the source's owner | `Combat/CombatState.cs:679-684`; `Models/AbstractModel.cs:155-171` |
| Generated status cards are created for `target.Player ?? target.PetOwner` | `Slimed.AddToCombatAndPreview` resolves a pet target to its owner (`MonsterState.PetOwnerId`) and creates through `cs.CreateCard` | `Commands/CardPileCmd.cs:1411`, `:1450-1456` (state machine `_d__27` of the single-target `AddToCombatAndPreview<T>`, public `:4134`) |
| The owner is cleared only by `CombatState.RemoveCard` / `RunState.RemoveCard`, whose only caller is a mock card | no clear path | `Combat/CombatState.cs:692-696`; `Runs/RunState.cs:727-731`; `Models.Cards.Mocks/MockCardModel.cs:67` |

`CardInstance.Create(cs, id | def | spec)` stays the ownerless `ToMutable` step plus an id: the loop's B6 deck build calls
`AttachTo(player)` on each deck card (B12 A23, loop review note "build Player.Deck from the specs with AttachTo"), and
with the strict guard an owner set inside `Create` would make that call throw. Until the loop's B6 path lands, the P0
`CombatLoop.PopulateDrawPile` still produces ownerless combat cards; every rule method here that takes `cs` reads
`_owner ?? cs.Player` where the game reads `Owner`, which is the same player in single player. Content written as a
1:1 port reads `card.Owner` and throws for such cards (the game would dereference null), see §14.

### 1.2 Piles

The game's `Pile` is the first of `Player.Piles` (the five combat piles, then `Deck`) that contains the card
(`Models/CardModel.cs:1796-1807`, `Entities.Players/Player.cs:381-390`); `CombatState` is the owner's combat when that pile
is a combat pile (`:2362-2373`; the `UpgradePreviewType.Combat` branch is preview UI). Sim: `GetCombatPile(cs)` =
`(_owner ?? cs?.Player).FindPileContaining(this)`; `IsInCombat(cs)` = a combat pile exists (`Models/CardModel.cs:2115-2125`);
`IsInDeck` = the owner's `Deck` holds the card; `GetPileType(cs)` = the combat pile's type, else `Deck`, else `None`.
`RemoveFromCurrentPile(cs)` removes from that pile or the deck (`:3238-3242`), `RemoveFromState(cs)` then sets
`HasBeenRemovedFromState` (`:3244-3248`).

### 1.3 `CloneForCombat(newInstanceId)` (B12 A23)

Trace of `Player.PopulateCombatState` (`Entities.Players/Player.cs:1214-1233`): `state.CloneCard(current)` →
`MutableClone` (`Models/AbstractModel.cs:164-171`: `MemberwiseClone`, `DeepCloneFields`, `AfterCloned`) → `AddCard`; then
`cardModel.DeckVersion = current`; then the draw pile add and `RandomizeOrderInternal` (loop).

| Step | Game | Sim |
| --- | --- | --- |
| field copy | `MemberwiseClone` keeps owner, upgrade level, flags, star cost state, `LastStarsSpent`, replay count, dupe flag, extra fields | `MemberwiseClone`, new instance id |
| keywords | `_keywords` = a copy of the local keywords (`Models/CardModel.cs:2829-2843`) | the mask is a value |
| vars | `DynamicVars.Clone(this)` = clone each var + `InitializeWithOwner` (`:2844`; `Localization.DynamicVars/DynamicVarSet.cs:203-208`, `:117-132`) | `Vars.CloneForOwner()` (re-initialises calculated vars, §8.2) |
| energy cost | `_energyCost?.Clone(this)` (`:2845`) | `EnergyCost.DeepClone()` |
| temporary star costs | `ToList` (`:2846`) | list copy (the costs are immutable) |
| enchantment | clone (`EnchantmentModel.DeepCloneFields`: `_card = null`, vars `Clone(this)`, `Models/EnchantmentModel.cs:330-335`), `Enchantment = null`, `EnchantInternal(clone, clone.Amount)` (`:2847-2852`) | `EnchantInternal(_enchantment.MutableClone(), Amount)` |
| affliction | clone (`AfterCloned`: `_card = null`, `Models/AfflictionModel.cs:238-243`), `Affliction = null`, `AfflictInternal(clone, clone.Amount)` (`:2853-2858`) — after the enchantment | same order; the `Amount` setter sees the same value and recalculates nothing |
| `AfterCloned` | `CurrentTarget = null`, `CurrentPlayIndex = 0`, `DeckVersion = null`, `HasBeenRemovedFromState = false` (`:2861-2882`), then the subclass override (`SovereignBlade`: `CreatedThroughForge = false`, `Models.Cards/SovereignBlade.cs:324-328`) | same resets, then `CardDef.AfterCloned(clone)` |
| deck link | `DeckVersion = current` (`Player.cs:1225`) | `DeckVersionId = source.InstanceId` |
| listener | the clone is its own model | `IOwnedListener<CardInstance>.CloneFor(clone)` (§1.6) |

The game's `_allCards` registration (`Combat/CombatState.cs:982-991`) has no sim counterpart and no rule reader.

### 1.4 State clones

`CloneFor(newOwner)` is the exact snapshot used by `PlayerState`'s copy constructor (scaffold M20): vars `DeepClone()` (no
re-initialisation), energy cost, star costs, extra state, enchantment and affliction `CloneFor(clone)`, listener per §1.6,
owner rebound (an ownerless card stays ownerless). `DeepClone()` = `CloneFor(_owner)`: a detached copy that keeps the
owner reference, for tests and callers that keep the owner (see §13, finding "DeepClone keeps the source owner").

### 1.5 Enchantment and affliction members

`Enchantment` / `Affliction` keep the B2 public setters for other owners' tests; a set binds the model to this card
(`BindCard`: a model bound to another card throws "cannot be moved", `Models/EnchantmentModel.cs:136-147`,
`Models/AfflictionModel.cs:78-89`), so one model can never sit on two cards and clone aliasing is preserved. Rule code
uses the game's internals:

| Method | Game | Sim |
| --- | --- | --- |
| `EnchantInternal(e, amount)` | `Enchantment = e; e.ApplyInternal(this, amount)` | same (field write, then `ApplyInternal`) | 
| `AfflictInternal(a, amount)` | throws when afflicted; `Affliction = a; a.Card = this; a.Amount = (int)amount` | same; the `Amount` setter recalculates (§5) |
| `ClearEnchantmentInternal()` | `Enchantment.ClearInternal(); Enchantment = null` | same |
| `ClearAfflictionInternal()` | `Affliction.ClearInternal(); Affliction = null; Owner.PlayerCombatState.RecalculateCardValues()` | same; `CardInstance.RecalculateCardValues(owner)` ports `Entities.Players/PlayerCombatState.cs:394-409` (each combat pile card's `Enchantment?.RecalculateValues()`, piles in `AllPiles` order Hand, Draw, Discard, Exhaust, Play, `:105-117`) |

Sources: `Models/CardModel.cs:3137-3148`, `:3150-3167`, `:3169-3182`, `:3184-3198`. `GetEnchantedReplayCount()` =
`Enchantment?.EnchantPlayCount(BaseReplayCount) ?? BaseReplayCount` (`:2760-2763`), read by the play count (`:3472`).

### 1.6 Listener protocol (scaffold §14 item 2)

The game yields the card model itself as a hook listener (`Combat/CombatState.cs:902-903`); the sim card holds an
optional adapter in `Listener`. Protocol chosen: a card listener is an `IOwnedListener<CardInstance>` adapter — base class `CardListener`
(`Cards/CardListener.cs`) holding only its card — created by `CardDef.CreateListener(card)` in the constructor and rebound by
`CloneFor` / `CloneForCombat`. All per-card state lives on the `CardInstance` (vars, `Extra`), never on the adapter. A
listener that is not owned is shared by clones and must be immutable (readonly fields). Canonical form: presence, then a
`CardListener` tag (`0xCA08`), the adapter's type name, and, if the adapter implements `ICanonical`, its payload; a card
without a listener writes the P0 bytes. No W1 pilot card needs a listener (the only listener override among the pilots,
`SovereignBlade.AfterCardChangedPiles`, is VFX, `Models.Cards/SovereignBlade.cs:342-357`).

Per-card hook slice for the hooks owner: `AppendHookListeners(buffer)` appends the card's listener, then its affliction,
then its enchantment (`Combat/CombatState.cs:902-911`). Liveness (`Combat/CombatState.cs:1010-1062`): card
`!HasBeenRemovedFromState && Owner.IsActiveForHooks` (`:1055`); affliction (`:1053`) and enchantment (`:1051`) additionally
`HasCard`. Sim: `CardInstance.IsLiveHookListener`, `AfflictionModel.IsLiveHookListener`, `EnchantmentModel.IsLiveHookListener`
(an ownerless card is never live).

### 1.7 Extra per-card state

Card subclasses with mutable fields (`SovereignBlade._currentDamage` etc.) keep them in a `CardExtraState`
(`CardDef.CreateExtraState()`, created in the constructor, never replaced), deep-cloned by both clone paths and written after
the listener. Its presence follows from the def id written first, so cards without extra state keep the P0 bytes.

### 1.8 Deck link

`DeckVersionId` (`NoDeckVersion = -1`) stores `DeckVersion` by id (`Models/CardModel.cs:2272-2283`); `FindDeckVersion()`
resolves it through the owner's deck. Readers in v0.111.0 (all W3 content): `Models.Cards/TheScythe.cs:73`,
`Models.Cards/GeneticAlgorithm.cs:66`, `Models.Enchantments/Goopy.cs:37-39`, `Models.Powers/SwipePower.cs:57-59`, `:131-137`,
`Models.Cards/DeprecatedCard.cs:78-93`, `Models.Monsters/ThievingHopper.cs:636`. Difference: the game keeps a reference to a
deck card that has left the deck; the id lookup returns null then (open question 3).

### 1.9 Keywords in combat

`GetKeywordsWithSources(sources)` (`Models/CardModel.cs:2775-2814`): without the `Global` flag, for a canonical card or when
`CombatState == null`, the local set (`Local` flag) or empty; otherwise a copy of the local set (when `Local`) passed to
`Hook.ModifyKeywordsInCombat` (`Hooks/Hook.cs:10458-10473`). `Keywords` = `All` (`:1940`). Sim:
`GetKeywordMaskWithSources(cs, sources)` builds a `CardKeywordSet` (an `ISet<CardKeyword>` over the bit mask) only for a
card in a combat pile; `Keywords(cs)`, `GetKeywordsWithSources(cs, sources)`, `HasKeywordInCombat(cs, keyword)`. Keyword
order is membership only (the game's `HashSet` order is read only by hover tips).

Game read sites ported here: `CanPlay` Unplayable (`:3339`), `GetResultLocationForCardPlay` Exhaust (`:3497`),
`AfflictionModel.CanAfflict` Unplayable (`Models/AfflictionModel.cs:206`), `IsRemovable` / `IsTransformable`
(`:2100-2113`) as `IsRemovableIn(cs)` / `IsTransformable(cs)`, `ShouldRetainThisTurn` / `IsSlyThisTurn` (`:1988-2023`) as
`ShouldRetainThisTurnIn(cs)` / `IsSlyThisTurnIn(cs)`. `EnchantmentModel.CanEnchant` reads `Keywords` only for a deck card,
where `CombatState` is null, so it reads the local mask (`Models/EnchantmentModel.cs:305-309`). The context-free
`HasKeyword`, `ShouldRetainThisTurn`, `IsSlyThisTurn` and `IsRemovable` remain local for their P0 callers; they are exact
in v0.111.0 because the only `TryModifyKeywordsInCombat` override, `HexPower`, adds only `Ethereal`
(`Models.Powers/HexPower.cs:320-331`). The loop's `Innate` and `Ethereal` reads (`CombatLoop.cs:340`, `:421`, `:446`;
game `Combat/CombatManager.cs:4266`, `:1282`, `:7336`) are not exact once `HexPower` is ported (§14).

### 1.10 Resources

| Rule | Sim | Source |
| --- | --- | --- |
| `HasEnoughResourcesFor`: energy `Max(0, GetWithModifiers(All))`, stars `Max(0, GetStarCostWithModifiers())`; excess energy is paid with 2 stars per missing energy only when `card.CombatState != null` and `ShouldPayExcessEnergyCostWithStars` | `HasEnoughResourcesFor(cs, out reason)` (`HasEnoughResources` forwards); the guard is `IsInCombat(cs)` | `Entities.Players/PlayerCombatState.cs:334-353` |
| `CardEnergyCost.GetWithModifiers`: the global hook applies only when the card has a combat state | `GetWithModifiers(cs, card, mods)` gates `Hook.ModifyEnergyCostInCombat` on `card.IsInCombat(cs)`; the context-free overloads are the `CombatState == null` path | `Entities.Cards/CardEnergyCost.cs:76-114`, `:116-132` |
| X energy: `GetAmountToSpend` returns the owner's energy | owner (or `cs.Player`) energy | `:116-123` |
| `GetStarCostWithModifiers`: X stars → owner's stars; in a combat pile → `Hook.ModifyStarCost`; else `CurrentStarCost` | same | `Models/CardModel.cs:3204-3216` |
| Star cost state: `BaseStarCost` (writes ignored for X), `CurrentStarCost` (last temporary cost; a temporary 0 over a negative base returns the base), `GetStarCostThisCombat`, `UpgradeStarCostBy` (removes temporary costs above a lowered base), `SetStarCost*`, `SetToFree*` | P0 port, unchanged | `:1850-1900`, `:2902-2963` |
| Temporary costs: `TemporaryCardCost.UntilPlayed/ThisTurn/ThisCombat`, cleared by `EndOfTurnCleanup` / the play cleanup | P0 port, unchanged | `Entities.Cards/TemporaryCardCost.cs:35-63`; `Models/CardModel.cs:3250-3271`, `:1017-1034` |
| X captures of an auto-play: `CapturedXValue = Energy` for X energy; `LastStarsSpent` = stars for X stars, else `Max(0, GetStarCostWithModifiers())`; skipped with `skipXCapture` | `CaptureXValuesForAutoPlay(cs, skipXCapture)` for the loop's `CardCmd.AutoPlay` | `Commands/CardCmd.cs:446-460` |
| Manual play captures | `CapturedXValue = amount` in the energy spend, `LastStarsSpent = amount` in the star spend (loop) | `Models/CardModel.cs:1249-1252`, `:1432` |

### 1.11 `CanPlay`, targets, result pile

`CanPlay(cs, out reason, out preventer)` (`Models/CardModel.cs:3330-3360`): the guard "no combat state → false" holds when
the owner is not among `cs.Allies` (the game's `CombatState ?? _owner?.Creature.CombatState`); then `Unplayable` from the
in-combat keywords, `HasEnoughResourcesFor`, `AnyAlly` with ≤ 1 living player creature, `Hook.ShouldPlay(…,
AutoPlayType.None)` with its preventer, `IsPlayable`. `IsValidTarget` compares sides with the owner's creature (`:3362-3385`).
`TargetType` and `GainsBlock` are per instance (`CardDef.GetTargetType(card)`, `GetGainsBlock(card)`) because content
overrides them dynamically (`SovereignBlade`, `Models.Cards/SovereignBlade.cs:223-235`). `GetResultLocationForCardPlay`
(`:3491-3503`): dupe or Power → none; `ExhaustOnNextPlay` or the in-combat `Exhaust` keyword → exhaust (clearing the flag);
else discard.

### 1.12 Upgrade internals

`UpgradeInternal` (`Models/CardModel.cs:3519-3530`): level + 1 (throws past `MaxUpgradeLevel`, `:2133-2143`), `OnUpgrade`,
`DynamicVars.RecalculateForUpgradeOrEnchant()`. `FinalizeUpgradeInternal` (`:3532-3537`): vars, energy cost, star cost flag.
`DowngradeInternal` (`:3539-3556`): level 0; vars replaced by a fresh canonical clone (`CardDef.CreateVars()` = new set +
initialisation, the same as `ToMutable().DynamicVars.Clone(this)`); `EnergyCost.ResetForDowngrade()`; `_baseStarCost =
CanonicalStarCost` (a field write that bypasses the X guard); local keywords = canonical keywords; `AfterDowngraded`
(`CardDef.AfterDowngraded(card)`); `Enchantment?.ModifyCard()`; `Affliction?.AfterApplied()`. The `Upgraded` event is UI.

### 1.13 Order

`CardInstance : IComparable<CardInstance>` ports `CardModel.CompareTo` (`Models/CardModel.cs:3649-3671`): ordinal model id
(`Models/AbstractModel.cs:126-137`, `Models/ModelId.cs:66-73`, same category for every card), then `CurrentUpgradeLevel`. Used
by `StableShuffle` (`Extensions/ListExtensions.cs:10-19`), e.g. `SeekerStrike`.

### 1.14 Canonical layout

`CardInstance` (tag `0xCA01`) is the P0 layout, then `CardKernel` (`0xCA07`): owner combat id or `-1`, `DeckVersionId`,
`HasBeenRemovedFromState`, enchantment presence + enchantment, affliction presence + affliction, listener presence +
(`0xCA08`, type name, payload if `ICanonical`), then the extra state when the def has one. Every new byte appears only for
cards that carry the new state, so P0 states hash as before (the two `EndToEndTests` pins hold). Tags: `CardListener 0xCA08`,
`SovereignBladeState 0xCA10` (cards range `0xCAxx`); `Enchantment 0x5721`, `Affliction 0x5731` (W1 `0x57xx`).

## 2. Play order and listener order (for the loop and hooks owners)

`OnPlayWrapper`'s per-play loop, traced by goto label (`Models/CardModel.cs` state machine; the textual order differs):
`IL_08f1` (`:617-650`, loop guard `i < playCount && !IsOverOrEnding`, `CurrentPlayIndex = i`) → `IL_051e` (`:757-777`, new
`CardPlay`, `Hook.BeforeCardPlayed`) → `IL_05eb` (`:661-678`, `History.CardPlayStarted`, the card's `OnPlay`) → `IL_06af`
(`:712-731`, owner-dead exit, then `Enchantment.OnPlay(cardPlay)` when enchanted) → `IL_0745` (`:704-711`, owner-dead exit)
→ `IL_076c` (`:686-700`, `Affliction.OnPlay(target)` when afflicted) → `IL_07ec` (`:652-660`, owner-dead exit) → `IL_081a`
(`:732-749`, `History.CardPlayFinished`, `Hook.AfterCardPlayed` while in progress) → `IL_08bf` (`:679-685`, owner-dead exit) →
`IL_08d8` (`:750-753`, next play). So the play order is **card, enchantment, affliction**, while the listener order is **card,
affliction, enchantment** (`Combat/CombatState.cs:902-911`). On turn 1 the player-turn setup moves the draw-pile cards
whose enchantment says `ShouldStartAtBottomOfDrawPile` to the bottom (`Combat/CombatManager.cs:4246-4257`), before the
`Innate` move (`:4266`).

## 3. `CardCmd`, card half (`Combat/Pipeline/CardCmd.cs`, `public static partial class CardCmd`)

The loop's play half (`AutoPlay`, `Discard`, `DiscardAndDraw`, `Exhaust`, B4) lives in `Combat/Loop/CardCmd.Play.cs`.
`cs` comes first and is nullable where the game's command also runs outside a combat (`CombatManager.Instance.IsEnding` is
false there); `CardPreviewStyle` and every preview/VFX step are dropped (§11).

### 3.1 Upgrade (B12 A6)

`Upgrade(cs, card)` = `Upgrade(cs, [card])` (`Commands/CardCmd.cs:1882-1885`); `Upgrade(cs, cards)` (`:1887-1946`): return when
`IsEnding`; per card skip `!IsUpgradable`; a card in the `Deck` pile adds its id to the current map point's `UpgradedCards`
(`:1904-1908`; `OnDeckCardUpgraded`, §3.6); `UpgradeInternal` + `FinalizeUpgradeInternal` on the passed card only
(`:1909-1910`). An in-combat upgrade therefore leaves the deck card unchanged (`DeckVersion` is read only by the content of
§1.8).

### 3.2 Downgrade

`Downgrade(cs, card)` (`:1853-1864`): unless `IsEnding`, a deck card adds `DowngradedCards` (`OnDeckCardDowngraded`), then
`DowngradeInternal` (§1.12).

### 3.3 Transform

`TransformToRandom(cs, original, rng)` (`:1949-1954`) = batch over `new CardTransformation(original)`; `TransformTo(cs,
original, def | id)` (`:1957-1963`) creates the replacement with `CardScope.CreateCard(original.Owner)` (combat scope:
`cs.CreateCard`) and calls `Transform(cs, original, replacement)` (`:1966-1971`). Batch `Transform(cs, transformations, rng)`
(`:1988-2144`, written as plain async code in the decompile, so text order is execution order):

1. return empty when `IsEnding` or no transformation;
2. per transformation in order: `IsTransformable` else throw; `Pile` null → throw "has no pile"; index in the pile;
   `GetReplacement(rng)` (creates the replacement, allocating its instance id) else throw; `RemoveFromCurrentPile`;
3. sort by (pile type, index) with `List.Sort` (`PileIndexSort`, `:1973-1985`);
4. per entry: result `{success, cardAdded}`; owners must match; combat pile → `AddInternal(replacement, index)`,
   `History.CardGenerated`, `Hook.AfterCardEnteredCombat`; then `Hook.AfterCardChangedPiles(rs, cs, replacement, pile.Type, null)`
   (the pile the card was added to, as the game passes it); `AfterTransformedFrom` / `AfterTransformedTo` (`:2052-2076`);
5. (UI: hand-card shine VFX, play-queue cancellation, previews, `:2083-2133`);
6. per result: `Hook.AfterCardGeneratedForCombat` for a card now in a combat pile; `Original.RemoveFromState()`
   (`:2134-2142`).

`CardTransformation` (`Entities.Cards/CardTransformation.cs:140-199`): the constructors assert `IsTransformable` and record
`IsInCombat = CombatState != null`; `GetReplacement(rng)`: the fixed replacement, else rng required, else null when no longer
transformable, else `CardFactory.CreateRandomCardForTransform` with the default or the given options. The rng is the caller's:
`EntropyPower` passes `Rng.CombatCardSelection` (`Models.Powers/EntropyPower.cs:106`), `NewLeaf` `Rng.Niche`
(`Models.Relics/NewLeaf.cs:102`), events their own `Rng`.

### 3.4 Enchant

`Enchant<T>` / `Enchant(id)` create a fresh model (`ModelDb.Enchantment<T>().ToMutable()`, `:2146-2149`; sim `new T()` /
`EnchantmentRegistry.Create`). `Enchant(e, card, amount)` (`:2151-2180`): `CanEnchant` else throw; no enchantment →
`EnchantInternal` + `ModifyCard`; same type → `Amount += (int)amount`; other type → throw; `FinalizeUpgradeInternal`; a deck
card records `CardsEnchanted` (`OnDeckCardEnchanted`); returns the card's enchantment. `ClearEnchantment` (`:2182-2185`).

### 3.5 Afflict, keywords

`Afflict(cs, a, card, amount)` (`:2235-2271`): over or ending with the card in a combat pile → null; combat state = the card's
or the owner's (null → null); `Hook.ShouldAfflict` false → null; `CanAfflict` false → null; no affliction → `AfflictInternal`
+ `AfterApplied`; same type → `Amount += (int)amount`; other type → throw; `History.CardAfflicted(cs, card, a)` (the passed
model, also when stacking); returns the card's affliction. `Afflict<T>` / `Afflict(id)` (`:2228-2233`). `AfflictAndPreview`
(`:2188-2225`): one fresh model per card, keeps the successes, throws when the afflicted cards have different owners; the
preview and `Cmd.Wait(1.25f)` are UI. The game runs the owner check only with a preview style other than `None`; the sim has
no style and always checks, which is equivalent in v0.111.0: the only caller, `ChainsOfBindingPower`
(`Models.Powers/ChainsOfBindingPower.cs:69`), passes one card. `ClearAffliction` (`:2273-2276`). `ApplyKeyword` / `RemoveKeyword` /
`ApplySingleTurnSly` / `ApplySingleTurnRetain` (`:2278-2306`): the card mutations; `NCard.UpdateVisuals` is UI.

### 3.6 Deck-pile branches

The map-history adds of `Upgrade`, `Downgrade` and `Enchant` are run-layer records
(`RunState.CurrentMapPointHistoryEntry?.GetEntry(netId)`, null-conditional): a standalone W1 run has no map point, so recording
nothing is the game's result there. They are declared as `static partial void OnDeckCardUpgraded / OnDeckCardDowngraded /
OnDeckCardEnchanted` hooks of the partial class, which W2 implements in its own `CardCmd` file without editing this one. A
transform of a deck card (`ModifyCardBeingAddedToDeck`, `FloorAddedToDeck`, `CardsTransformed`, `:2052-2065`) throws
`NotImplementedException` (W2).

## 4. `EnchantmentModel` (`Cards/Enchantments/EnchantmentModel.cs`)

19 virtuals in `Models/EnchantmentModel.cs`; 13 are declared, the 6 UI ones (`HasExtraCardText :48`, `ShowAmount :119`,
`DisplayAmount :121`, `ShouldGlowGold :214`, `ShouldGlowRed :216`, `ExtraHoverTips :237`) are stripped (scaffold M13; the glow
flags are read only by `CardModel.ShouldGlowGold/Red`, `Models/CardModel.cs:2188-2210`, UI).

| Member | Rule | Source |
| --- | --- | --- |
| `Card` / `HasCard` | `Card` returns null until applied | `:129-149` |
| `Amount`, `Status`, `IsStackable`, `ShouldStartAtBottomOfDrawPile` | plain state / defaults (`Status` event is UI) | `:151-162`, `:193-212`, `:175`, `:127` |
| `DynamicVars` | per instance, from `CanonicalVars` and initialised (`InitializeWithOwner`), created on first access (deterministic, so hash and clone materialise it); no v0.111.0 enchantment or affliction declares a calculated var | `:177-191` |
| `CanEnchantCardType` | true | `:289-292` |
| `CanEnchant(card)` | Status/Curse/Quest → false (`(uint)(type - Status) <= 2`); `CanEnchantCardType`; a deck card with local `Unplayable` → false; an existing enchantment unless stackable and the same type → false | `:294-315` |
| `OnPlay(cs, cardPlay)` | no-op; called after the card's `OnPlay` (§2) | `:317-320` |
| `ApplyInternal(card, amount)` | throws if applied; `Amount = (int)amount`; binds | `:337-348` |
| `ClearInternal()` | unbinds | `:350-354` |
| `ModifyCard()` | throws without a card; `OnEnchant`, `RecalculateValues`, `card.DynamicVars.RecalculateForUpgradeOrEnchant()` | `:356-366` |
| `RecalculateValues`, `OnEnchant` | no-ops | `:368-370`, `:391-393` |
| `EnchantBlockAdditive/Multiplicative`, `EnchantDamageAdditive/Multiplicative` | 0, 1, 0, 1; the pre-pass that reads them is `Hook.ModifyBlock` / `ModifyDamage` (hooks, B12 A9) | `:395-413` |
| `EnchantPlayCount` | identity; read by `GetEnchantedReplayCount` | `:415-418` |
| clone | `MutableClone()` = the game's clone (`DeepCloneFields`: card cleared, vars `Clone(this)`, `:330-335`), used by `CloneForCombat`; `CloneFor(card)` = exact snapshot | |
| canonical | tag, id, card instance id, amount, status, vars, `WriteExtraCanonical` | |

`ShouldReceiveCombatHooks` (`:125`) is declared abstract in `AbstractModel` and read by no rule; liveness is §1.6.

### 4.1 Pilot `GLAM` (`Models.Enchantments/Glam.cs`)

Vars `DynamicVar("Times", 1m)` (`:17`); `_usedThisCombat` (`:15`, written by `WriteExtraCanonical`); `EnchantPlayCount` =
original + `Times` until used (`:34-41`); `AfterCardPlayed`: when unused and the play is its card's, mark used and
`Status = Disabled` (`:43-56`). It exercises per-instance vars, extra state, the replay count and a hook override.

## 5. `AfflictionModel` (`Cards/Afflictions/AfflictionModel.cs`)

9 virtuals in `Models/AfflictionModel.cs`; `HasExtraCardText` (`:41`) and `ExtraHoverTips` (`:140`) are UI and stripped.

| Member | Rule | Source |
| --- | --- | --- |
| `Card` / `HasCard` | null until afflicted | `:71-91` |
| `Amount` setter | on change, with a card: `Owner.PlayerCombatState.RecalculateCardValues()` (§1.5) | `:93-113` |
| `CanAfflictUnplayableCards`, `IsStackable`, `CanAfflictCardType` | true, false, true | `:117`, `:119`, `:195-198` |
| `CanAfflict(cs, card)` | `CanAfflictCardType`; in-combat `Unplayable` unless allowed; an existing affliction unless stackable and the same type | `:200-215` |
| `AfterApplied`, `BeforeRemoved`, `OnPlay(cs, target)` | no-ops; `OnPlay` runs after the enchantment's (§2) | `:217-228` |
| `PickRandomTargets(cs, rngSet, cards, count)` | `Where(CanAfflict)`, `UnstableShuffle(rngSet.CombatCardGeneration)`, `RemoveRange(Clamp(n - 1, 0, count), Max(0, n - count))` = the first `min(n, count)` | `:245-250` |
| `ClearInternal()` | `BeforeRemoved`, unbind | `:252-256` |
| clone | `MutableClone()` (`AfterCloned`: card cleared, `:238-243`), `CloneFor(card)` snapshot | |

`CanAfflict` and `PickRandomTargets` take `CombatState? cs` first because the game reads `card.Keywords` (in-combat set) —
contract change request 1.

### 5.1 Pilot `HEXED` (`Models.Afflictions/Hexed.cs`)

`AfterCardEnteredCombat(card)`: only for its own card; returns when the owner has `HexPower` (by id `HEX_POWER`); otherwise
`CardCmd.ClearAffliction` (`:14-26`). The `Ethereal` it implies comes from `HexPower.TryModifyKeywordsInCombat`
(`Models.Powers/HexPower.cs:320-331`, pipeline W3).

## 6. Pools, catalog, unlocks (`Cards/Pools/*`, `Unlocks/UnlockProfile.cs`)

### 6.1 Pools

`CardPools` holds the 12 non-mock pools in `ModelDb.AllCardPools` order — the character pools in `AllCharacters` order
(Ironclad, Silent, Regent, Necrobinder, Defect), then `AllSharedCardPools` (Colorless, Curse, Deprecated, Event, Quest,
Status, Token) (`Models/ModelDb.cs:114-135`). Each `CardPool` carries its `GenerateAllCards` id list in source order
(`CardPools.Data.cs`, generated from `sim/specs/inventory/pools.json` and checked member for member against each
`Models.CardPools/<Pool>.cs` array: character pools and Colorless `:25-…`, shared pools `:18-…`/`:20-…`), `Title`, `IsColorless`
(per pool, e.g. Deprecated is titled "token" and colorless, Curse/Quest/Status are not), `AllCardIds` (`Models/CardPoolModel.cs:48`).
Sizes (`AllCards` family): Ironclad 90, Silent/Regent/Necrobinder/Defect 91, Colorless 65, Curse 18, Deprecated 1, Event 28,
Quest 4, Status 12, Token 14. Mock pools (`MockCardPool`, `DeprivedCardPool`) are test-only and absent. `PoolOf(cardId)` is the
first pool containing the id (`Models/CardModel.cs:1752-1773`; a card in no pool throws `InvalidProgramException`; the mock
fallback is test-only); `CardDef.Pool` reads it. `ForCharacter(characterId)` maps `CharacterModel.CardPool`
(`Models.Characters/Ironclad.cs:34` and the other four).

### 6.2 `GetUnlockedCards(profile, constraint)` (`Models/CardPoolModel.cs:68-81`)

`FilterThroughEpochs(profile, AllCards)` (base: a copy, `:83-86`), then `MultiplayerOnly` removes `SingleplayerOnly` cards and
`SingleplayerOnly` removes `MultiplayerOnly` cards (`RemoveAll`, order kept). `pools.json` has no gates (`unlock_gates = []`), so
the gates are ported from the overrides (B12 A7):

| Pool | Override | Epoch → cards (epoch `Cards` lists) |
| --- | --- | --- |
| Ironclad | `Models.CardPools/IroncladCardPool.cs:122-138` | `IRONCLAD2_EPOCH` → MOLTEN_FIST, CRUELTY, DOMINATE; `IRONCLAD5_EPOCH` → CINDER, PACTS_END, DRUM_OF_BATTLE; `IRONCLAD7_EPOCH` → BLOOD_WALL, TEAR_ASUNDER, INFERNO (`Timeline.Epochs/Ironclad{2,5,7}Epoch.cs:20-36`) |
| Silent | `SilentCardPool.cs:123-138` | 2: SNAKEBITE, BUBBLE_BUBBLE, ACCELERANT; 5: REFLEX, MASTER_PLANNER, HAND_TRICK; 7: HIDDEN_DAGGERS, BLADE_OF_INK, PHANTOM_BLADES |
| Regent | `RegentCardPool.cs:123-138` | 2: SPOILS_OF_BATTLE, SWORD_SAGE, FURNACE; 5: BEGONE, ARSENAL, SUPERMASSIVE; 7: PATTER, HEAVENLY_DRILL, LUNAR_BLAST |
| Necrobinder | `NecrobinderCardPool.cs:123-138` | 2: SCOURGE, OBLIVION, COUNTDOWN; 5: AFTERLIFE, SACRIFICE, CALCIFY; 7: SCULPTING_STRIKE, VEILPIERCER, BANSHEES_CRY |
| Defect | `DefectCardPool.cs:123-138` | 2: LOOP, NULL, CONSUMING_SHADOW; 5: BARRAGE, FLAK_CANNON, SMOKESTACK; 7: TURBO, SCAVENGE, HELIX_DRILL |
| Colorless | `ColorlessCardPool.cs:97-120` | `COLORLESS1_EPOCH` → AUTOMATION, ENTROPY, CATASTROPHE; 2 → ETERNAL_ARMOR, JACKPOT, PREP_TIME; 3 → REND, BEAT_DOWN, PROWESS; 4 → ALCHEMIZE, NOSTALGIA, SCRAWL; 5 → SPLASH, ANOINTED, CALAMITY |

Each gate runs `RemoveAll(card id ∈ epoch cards)` when the epoch is not revealed, in the override's order. Counts asserted by
the tests: unlocked single-player with `UnlockProfile.All` 85 Ironclad, 86 for each other character, 53 colorless (the 37
`MultiplayerOnly` cards are 5 per character pool plus 12 colorless; the Ironclad five are BLAZE, DEMONIC_SHIELD, MIDNIGHT,
OUTRAGE, TANK); no card is `SingleplayerOnly`, so the multiplayer and `None` constraints give the `AllCards` sizes; with
`UnlockProfile.None` 76 Ironclad, 77 per other character, 38 colorless. The run constraint is `SingleplayerOnly` for ≤ 1 player
(`Runs/IRunState.cs:74-84`, `CardPools.MultiplayerConstraintFor(rs)`; it belongs on `RunState`, contract change request 4).

### 6.3 Catalog (`Cards/Pools/CardCatalog*.cs`)

Rules over pools read card metadata of every pool card (rarity and `CanBeGeneratedInCombat` for `FilterForCombat`, the
multiplayer constraint, type filters in content), most of which is not ported. `CardCatalog` lists the 596 pool cards with id,
type, rarity, target, canonical cost, X cost, multiplayer constraint, `CanBeGeneratedInCombat`, `CanBeGeneratedByModifiers`,
generated from `sim/specs/inventory/cards.json` and checked against each `Models.Cards/<Card>.cs`: the constructor
`base(cost, CardType, CardRarity, TargetType)`, `override CardMultiplayerConstraint MultiplayerConstraint => …` (37 files),
`override bool CanBeGeneratedInCombat => false` (19 files, all constant), `CanBeGeneratedByModifiers`, `HasEnergyCostX => true`
(10). Exception: `MAD_SCIENCE` overrides `Type` with its unset `TinkerTimeType` field, so its canonical type is `None`
(`Models.Cards/MadScience.cs:790`, `:814`). Four cards override `TargetType` (Malaise, MadScience, Shiv, SovereignBlade); the
catalog records the constructor's target. `CardCatalog.Resolve(id)` returns the registered def, else an `UnportedCardDef`
placeholder (`[SimExclude]`) that reports only the catalog fields, throws `NotImplementedException` for every other member
and can never become a `CardInstance`. A test checks that every registered def agrees with its catalog entry.

### 6.4 `UnlockProfile` additions (`Unlocks/UnlockState.cs:31-77`)

`Characters` (`:31-54`): the five characters minus Silent/Regent/Necrobinder/Defect unless `SILENT1_EPOCH` / `REGENT1_EPOCH` /
`NECROBINDER1_EPOCH` / `DEFECT1_EPOCH` is revealed (`List.Remove`, order kept); `CharacterCardPools` (`:73`); `CardPools`
(`:77`, distinct concat with the shared pools). Not ported: `SharedAncients` (W2, Darv), `Relics`, `Potions` (relics-potions
pools), `Cards` (`:75`, no rule reader). The profile stays deeply immutable (computed properties, no new fields).

## 7. `CardFactory` (`Cards/CardFactory.cs`, `public static partial class`, D6)

### 7.1 Combat entry points

| Member | Rule | Source |
| --- | --- | --- |
| `FilterForPlayerCount(rs, options)` | more than one player → drop `SingleplayerOnly`, else drop `MultiplayerOnly` | `Factories/CardFactory.cs:25-32` |
| `FilterForCombat(cards)` | `Distinct(Where(CanBeGeneratedInCombat && rarity ∉ {Basic, Ancient, Event}))` | `:118-121` |
| `GetDistinctForCombat(cs, player, cards, count, rng)` | (the `TestRngInjector` override is a test seam, dropped) `FilterForPlayerCount` → `FilterForCombat` → `TakeRandom(count, rng)` (copy + `UnstableShuffle` at the call, `Extensions/IEnumerableExtensions.cs:9-12`) → lazy `CreateCard(c, player)` per enumerated card | `:93-102` |
| `GetForCombat(cs, player, cards, count, rng)` | `FilterForCombat` → list → `FilterForPlayerCount` → list; per card `rng.NextItem(options)` (with replacement) + `CreateCard` | `:104-116` |

`GetDistinctForCombat` returns the lazy sequence: `BundleOfJoy` and `CosmicConcoction` enumerate it one card at a time between
pile adds (`Models.Cards/BundleOfJoy.cs:48-49`, `Models.Potions/CosmicConcoction.cs:55-56`) and `FirstOrDefault` callers create
only the first card (`Models.Cards/InfernalBlade.cs:50` etc.), so instance-id order depends on it. All 34 call sites of
the two methods in v0.111.0 pass `Rng.CombatCardGeneration` (e.g. `Models.Cards/Discovery.cs:70`; `CallOfTheVoidPower`
through a local, `Models.Powers/CallOfTheVoidPower.cs:68`).

### 7.2 Combat transforms

`GetDefaultTransformationOptions(rs, original, isInCombat)` (`:123-128`): the original's pool unless Quest type or Event,
Ancient or Token rarity (then Colorless), unlocked for the run's profile and constraint. `GetFilteredTransformationOptions`
(`:142-166`): unless the original is Status or Curse rarity, keep Common/Uncommon/Rare; in combat keep
`CanBeGeneratedInCombat`; drop the original's id; `FilterForPlayerCount`; empty → throw. `CreateRandomCardForTransform`
(`:130-140`): `CardScope.CreateCard(rng.NextItem(options), original.Owner)`; outside a combat the scope is the run (W2,
`NotImplementedException`).

### 7.3 Run entry points

`CreateForReward`, `CreateForMerchant`, `RollForUpgrade` (`:34-91`, `:168-261`) are W2 (`CardFactory.Run.cs`).

### 7.4 Unported pool cards (approximation, flagged)

When the filtered option list contains catalog placeholders, the draw cannot create the game's pick. The factory then records
`FidelityFlag.ApproximatedRule` with site `"<method>: <k> of <n> options are not ported; the draw runs over the ported ones"`
(`GetDistinctForCombat`, `GetForCombat`, `CreateRandomCardForTransform`) and draws over the ported options only, so the RNG
consumption and the pick differ from the game for that combat and the diff harness marks it ineligible. With every option
ported the path is exact and records nothing. If no option is ported, the transform throws `NotImplementedException`. In the
integrated W1 tree the Ironclad options ported are ARMAMENTS and HEADBUTT, the colorless ones DISCOVERY and SEEKER_STRIKE.

## 8. Dynamic vars (`Cards/DynamicVars/*`, namespace `Sts2Sim.Core.Cards`)

### 8.1 Base and set

`DynamicVar(name, baseValue, props = None)` (`Localization.DynamicVars/DynamicVar.cs:77-82`): `BaseValue` clamps at 999999999
(`:29-40`), `IntValue = (int)BaseValue` (`:75`), `UpgradeValueBy` / `FinalizeUpgrade` (`:99-108`), `Clone` = `MemberwiseClone`
(`:110-115`; `ResetToBase` and `EnchantedValue` / `PreviewValue` are preview state, `:42-64`, `:84-88`). `Props` lives on the base
(P0 decision; `None` for vars without props). `DynamicVarSet` (`DynamicVarSet.cs`): insertion order, duplicate names throw
(`:95-115`); typed accessors cast like the game's (`:26-72`; the `PowerVar` accessors stay untyped because most power types are
other owners' or unported); `InitializeWithOwner()` (`:117-132`, only `CalculatedVar` reacts); `FinalizeUpgrade` (`:169-184`);
`RecalculateForUpgradeOrEnchant` (`:186-201`); `CloneForOwner()` = the game's `Clone(owner)` (`:203-208`), `DeepClone()` = exact
snapshot for state clones.

### 8.2 Calculated vars

`CalculatedVar` (`CalculatedVar.cs`): `WithMultiplier` once (`:24-38`; the game rejects a delegate bound to a model, the sim one
bound to a `CardDef`, `CardInstance` or `AbstractModel`); the multiplier is `Func<CombatState, CardInstance, CreatureState?,
decimal>` held by the var (a static delegate from the def, shared by clones, immutable); `Calculate(cs, card, target)` =
`base.BaseValue + extra.BaseValue × (IsInProgress && card in a combat pile ? multiplier : 0)` (`:40-50`);
`RecalculateForUpgradeOrEnchant` copies the base var and marks `WasJustUpgraded` on change (`:52-60`); `SetOwner` →
`UpdateValues` copies the base var (`:18-22`, `:87-93`) — sim `UpdateValues(set)`, run by `InitializeWithOwner`.
`GetBaseVar` = `CalculationBase` (`:67-70`); `GetExtraVar` = `CalculationExtra` (`:72-75`), for `CalculatedDamageVar` =
`ExtraDamage` (`CalculatedDamageVar.cs:81-84`). `CalculatedDamageVar` / `CalculatedBlockVar` carry `Props`
(`CalculatedDamageVar.cs:33-37`, `CalculatedBlockVar.cs:22-26`); `FromOsty` (`:39-43`) is read only by previews.

### 8.3 The other subclasses

`BlockVar` "Block" + props, `CardsVar` "Cards" (int), `DamageVar` "Damage" + props, `EnergyVar` "Energy" (int; `ColorPrefix` is a
UI string, dropped), `ExtraDamageVar` "ExtraDamage" (+ `FromOsty`), `ForgeVar` "Forge", `GoldVar` "Gold", `HealVar` "Heal",
`HpLossVar` "HpLoss", `IfUpgradedVar` "IfUpgraded" (value = `(int)UpgradeDisplay`, enum `Localization/UpgradeDisplay.cs`),
`IntVar`, `MaxHpVar` "MaxHp", `OstyDamageVar` "OstyDamage" + props, `PowerVar<T>` (name = `typeof(T).Name`), `RepeatVar`
"Repeat", `StarsVar` "Stars", `StringVar` (`StringValue`, display text set by events and powers, not hashed), `SummonVar` "Summon",
`BoolVar` (`BoolVal` = `BaseValue != 0`, `Convert.ToBoolean` / `ToDecimal`), `CalculationBaseVar`, `CalculationExtraVar`. Every
`UpdateCardPreview` override is preview UI. 24 subclasses + `DynamicVar` + `DynamicVarSet` = the 26 files of
`Localization.DynamicVars` (B7 and the brief say 26 subclasses; the count is 24).

## 9. Pilot cards (`Cards/Defs/*`)

Cross-kind references are by id: powers `PARRY_POWER`, `SEEKING_EDGE_POWER`, `HEX_POWER` are read with `HasPower(id)` /
`GetPower(id)`. Every prompt passes `ChoiceSource.For(card, "OnPlay")` (site id `<ID>:OnPlay`). Card-specific prompts use the
key `<ID>.selectionScreenPrompt` (`Models/CardModel.cs:1571-1584`).

| Card | Definition | `OnPlay` (synchronous, game order) | Upgrade |
| --- | --- | --- | --- |
| `ARMAMENTS` (`Models.Cards/Armaments.cs`) | 1, Skill, Common, Self (`:143-146`); `BlockVar(5, Move)` (`:141`); `GainsBlock` (`:139`) | `GainBlock(owner, Block, play)`; not upgraded → `FromHandForUpgrade(owner)` → `CardCmd.Upgrade(card)` if any; upgraded → `CardCmd.Upgrade` each upgradable hand card (`:68-119`) | none (no `OnUpgrade`) |
| `ACROBATICS` (`Acrobatics.cs`) | 1, Skill, Uncommon, Self (`:147-150`); `CardsVar(3)` (`:145`) | `Draw(Cards, owner)`; `FromHandForDiscard(owner, prefs(TO_DISCARD, 1), no filter)` → first → discard (`:70-125`) | Cards + 1 (`:166-169`) |
| `HEADBUTT` (`Headbutt.cs`) | 1, Attack, Common, AnyEnemy (`:157-160`); `DamageVar(9, Move)` (`:155`) | target required; attack; `FromCombatPile(discard, owner, prefs(prompt, 1))` → first → `CardPileCmd.Add(Draw, Top)` (`:76-135`) | Damage + 3 (`:177-180`) |
| `SEANCE` (`Seance.cs`) | 1, Skill, Rare, Self (`:178`); `CardsVar(1)` (`:171`); Ethereal (`:173`) | `FromCombatPile(draw, owner, prefs(TO_TRANSFORM, Cards))` → list; (cast anim, UI); `CardCmd.TransformTo<Soul>` each (`:77-132`) | cost − 1 (`:196-199`) |
| `SOUL` (`Soul.cs`) | 0, Skill, Token, Self (`:161`); `CardsVar(2)` (`:158`); Exhaust (`:156`); `Create(owner, amount, cs)` (`:175-183`) | `Draw(Cards, owner)` (`:122`) | Cards + 1 (`:199-202`) |
| `DISCOVERY` (`Discovery.cs`) | 1, Skill, Uncommon, Self (`:130`); Exhaust (`:127`) | (mock card seam dropped) `GetDistinctForCombat(owner, ownerPool.GetUnlockedCards(profile, constraint), 3, Rng.CombatCardGeneration)` → list; `FromChooseACardScreen(cards, owner, canSkip: true)`; if chosen: `SetToFreeThisTurn`, `AddGeneratedCardToCombat(Hand, owner)` (`:63-101`) | remove Exhaust (`:148-151`) |
| `SEEKER_STRIKE` (`SeekerStrike.cs`) | 1, Attack, Uncommon, AnyEnemy (`:190`); Strike tag (`:173-181`); `DamageVar(9, Move)`, `CardsVar(3)` (`:183-187`) | target required; attack; options = first `Cards` of `StableShuffle(copy of the draw pile, Rng.CombatCardSelection)`; `FromCombatPile(draw, owner, prefs(prompt, 1), c ∈ options)` → first → `CardPileCmd.Add(Hand)` (`:88-141`) | Damage + 3 (`:209-212`) |
| `SOVEREIGN_BLADE` (`SovereignBlade.cs`) | 2, Attack, Token, AnyEnemy (`:300`); Retain (`:276`); `DamageVar(10, Move)`, `CalculationBaseVar(0)`, `CalculationExtraVar(1)`, `CalculatedBlockVar(Move)` × owner's Parry, `RepeatVar(1)` (`:278-285`); target `AllEnemies` with `SeekingEdgePower` (`:223-233`, `:287-297`); `GainsBlock` = Parry > 0 (`:235`); `GetOwnerParryAmount`: 0 without owner or outside a combat pile (`:388-407`) | attack `Damage` × `Repeat` hits, all opponents with Seeking Edge else the target (required); then, with Parry, `GainBlock(owner, CalculatedBlock.Calculate(target), Props, play)` (`:121-181`) | cost − 1 (`:319-322`) |

`SovereignBlade` state (`:215-274`): `CurrentDamage` 10, `CurrentRepeats` 1, `CreatedThroughForge` in `CardExtraState`;
`AfterCloned` resets `CreatedThroughForge` (`:324-328`); `AfterDowngraded` restores Damage and Repeat from the state (`:330-335`);
`AddDamage` / `SetRepeats` write the var then the state (`:359-371`). `ForgeCmd` (character-mechanics,
`Commands/ForgeCmd.cs:153-216`) reaches these through the `IForgeBladeDef` interface (`Cards/IForgeBladeDef.cs`):
`SetCreatedThroughForge(card, true)` after `CreateCard`, `AddDamage(card, amount)` per blade. `CardModel.AfterForged` raises only
the `Forged` event (`Models/CardModel.cs:3281-3288`, UI). `AfterTransformedFrom` and `AfterCardChangedPiles` are VFX
(`:337-357`).

## 10. Fidelity flags and knowing deviations

| Site | Kind | Flag |
| --- | --- | --- |
| `CardFactory.GetDistinctForCombat` / `GetForCombat` / `CreateRandomCardForTransform` with unported pool cards | draw over the ported options (§7.4) | `ApproximatedRule`, site with counts |
| Ownerless combat cards (P0 loop path) read `cs.Player` as owner in rule methods with `cs` | exact in single player | none |
| Context-free `HasKeyword` / `ShouldRetainThisTurn` / `IsSlyThisTurn` / `IsRemovable` | exact in v0.111.0 (§1.9) | none |
| Map-history adds for deck cards | nothing to record without a map point (§3.6) | none |
| Deck-card transform | `NotImplementedException` (W2) | none (loud) |
| `ACROBATICS` discards through the P0 `CardPileCmd.Discard` | the loop-owned facade the contract offers today; the game calls `CardCmd.Discard` (B4 loop play half, not yet present), which also records `CardDiscarded` and auto-plays Sly cards — P0 throws `NotSupportedException` for Sly | none here; contract change request 2 |

## 11. Stripped sites

UI and test seams with no rule effect: every `CardPreviewStyle` branch, `NCard*Vfx`, `NRun` / `NCombatRoom` containers,
`Preview*`, `Cmd.Wait` in `AfflictAndPreview`, `NCard.UpdateVisuals` in the keyword commands, `LocalContext.IsMine` (single
player), the play-queue cancellation in `Transform` (non-interactive: no queued play), `CreatureCmd.TriggerAnim` in `Seance`,
the attacker / hit VFX of the pilots' attacks and `SovereignBlade`'s `BeforeDamage` VFX delegates, `Discovery._mockSelectedCard`,
`CardFactory`'s `TestRngInjector` override, events (`KeywordsChanged`, `EnchantmentChanged`, `AfflictionChanged`, `Upgraded`,
`StatusChanged`, `AmountChanged`, `Forged`, …), `EnergyVar.ColorPrefix`, preview values. None changes the order of rule side
effects.

## 12. Tests (`CardKernelTests.cs`, 45 tests, 8 through `W1Pending`)

| Group | Tests |
| --- | --- |
| `CloneForCombat` | owner kept, resets, deck link, independence; enchantment then affliction re-bound; canonical bytes equal to an independently built card; calculated-var re-initialisation vs exact `DeepClone` |
| Owner | creation paths, strict `AttachTo`, `CloneFor` rebinding, `Slimed` ownership |
| Upgrade | `ARMAMENTS+` upgrades the combat copies and leaves the deck cards unchanged (runs without a prompt); the single-option `ARMAMENTS` prompt (pending: choice); `IsEnding` and upgradability; recalculation and finalize; downgrade restores the canonical card and re-runs `ModifyCard` / `AfterApplied`; `SOVEREIGN_BLADE` downgrade |
| Transform | slot kept, original removed, history; batch sort; `TransformToRandom` stream, ported-option draw and flag; Eternal / no pile / deck |
| Enchantment | base virtual defaults with a double, `CanEnchant` rules, apply / truncate / `ModifyCard` order / finalize / stack / mismatch / clear, replay count with `GLAM` and a double |
| Affliction | base rules, `Amount` recalculation, clear order, `PickRandomTargets` stream, `HEXED`; `Afflict` stacking and history (pending: `Hook.ShouldAfflict`) |
| Listeners | per-card order card → affliction → enchantment and liveness; hook dispatch order (pending: hooks traversal) |
| Keywords | in-combat keywords only in combat piles, `CanPlay` / result pile read them; `CardCmd` keyword commands; `CardKeywordSet` |
| Resources | excess energy with stars and the global energy hook only in combat piles; star costs, temporary costs, X captures; `CompareTo` and `FindDeckVersion` |
| Factory | `GetDistinctForCombat` filter, one shuffle, lazy creation; `GetForCombat` one draw per card; unported options flagged; `DISCOVERY` (pending: choice) |
| Pools | `GenerateAllCards` order and sizes; unlocked counts per profile and constraint (85/86/53 vs 90/91/65; `None` 76/77/38), epoch gates, 37 `MultiplayerOnly`; `UnlockProfile` characters and pools; catalog agreement and placeholders |
| Dynamic vars | names, values, props, typed accessors; calculated vars in and out of combat |
| Pilots | every definition field and upgrade; `SOVEREIGN_BLADE` targets, block, forge state; `HEADBUTT`, `SEEKER_STRIKE`, `ACROBATICS`, `SEANCE` single-option plays (pending: choice) |
| Hash | `Hash(x) == Hash(Clone(x))`; 31 per-field mutations (instance id, every P0 field, owner, deck link, removal flag, enchantment amount / status / var / extra state / presence, affliction amount / presence, listener payload / type / presence, extra state) each change the hash and leave the source unchanged; the star-cost upgrade flag; the combat clone |

The pending bodies run for real once the named stub exists. A scratch probe (not committed) of this tree in which the choice
stubs return their first option and the loop builds an owned `Player.Deck` (`AttachTo`) and fills the draw pile with
`CloneForCombat` ran the whole suite: all 45 card-kernel tests passed, 2 of them pending on hooks, and `W1Gate F1/F4/F5`
passed with the six prompt pilots (ARMAMENTS, ACROBATICS, HEADBUTT, SEANCE, DISCOVERY, SEEKER_STRIKE) in every deck (32 + 24 +
32 playouts). In the same probe the pinned-hash test and two `LoopTests` that encode the P0 deck path (`Opening draw order`,
`SetUpWindow` card count) failed: the owner id enters the canonical form and the deck cards take instance ids, so the loop
owner refreshes them with B6 (contract change request 3).

## 13. Round-2 findings

| Finding | Resolution |
| --- | --- |
| [high] No cards-kernel work exists | delivered: this file, `CardCmd.cs`, `DynamicVars/*`, `CardFactory.cs`, `Pools/*`, enchantment / affliction bases and pilots, 8 pilot cards, `CardKernelTests.cs` |
| [medium] Stubs still throw; `EnchantmentModel` has no `DynamicVars` | bases ported (§4, §5); per-instance vars created from `CanonicalVars`, cloned by both clone paths, written in `WriteCanonical`; `CloneForCombat` re-verified (§1.3) |
| [medium] `GetEnchantedReplayCount` ignores the enchantment (×2) | ported (§1.5), tests with `GLAM` and a double |
| [medium] Combat cards never get an owner (×2) | `cs.CreateCard` (state) and `CloneForCombat` set it; `Slimed` creates through `CreateCard`; `CardInstance.Create` stays ownerless because the loop's B6 deck build attaches the owner and the game's guard throws on a second set (§1.1); the loop's `PopulateDrawPile` / `AddGeneratedCardToCombat` paths are the loop's (contract change request 3) |
| [medium] The brief's deck persistence of upgrades (×2) | superseded by B12 A6; `CardCmd.Upgrade` changes only the passed card (§3.1); test with `ARMAMENTS+` |
| [low] `Card` getters throw | both return null (§4, §5) |
| [low] `AfflictionModel.Amount` skips `RecalculateCardValues` | ported (§5), tested |
| [low] `AttachTo` differs from the setter (×2) | strict guard (§1.1); clearing to null is not ported: only `RemoveCard` clears, called only by a mock card |
| [low] Listener canonical form is presence only; clone protocol undecided (×2) | protocol fixed (§1.6): owned adapters rebound on clone, payload hashed through `ICanonical`, type name hashed |
| [low] `OnPlay` order card → enchantment → affliction by goto order | confirmed by an independent trace (§2); listener order card → affliction → enchantment; the play path is the loop's, the traversal the hooks', the per-card slice is tested here |
| [low] Pre-pass ownership | B12 A9 assigns the pre-pass to hooks; this area supplies the four virtuals (§4) |
| [low] Single-argument `CardEnergyCost` overloads ignore `Global` | rebutted: they are the game's `CombatState == null` path (`Entities.Cards/CardEnergyCost.cs:109`), which skips the global hook; P0 tests use them; combat code uses the `(cs, card)` overloads, which now gate the hook on the card being in a combat pile like the game |
| [low] Spec, citation and count gaps | this file; 24 subclasses + 2 base files; 13 of 19 enchantment virtuals with the 6 UI ones listed (§4) |
| [high] No `CardKernelTests.cs`, no spec | delivered (§12) |
| [high] `CloneForCombat` throws | implemented in phase 0b, re-verified and corrected here: vars re-initialised like `Clone(owner)`, enchantment and affliction re-applied through `EnchantInternal` / `AfflictInternal` on fresh clones, `CardDef.AfterCloned`, extra state (§1.3) |
| [medium] Bases cannot hold or bind state | bound through `ApplyInternal` / `AfflictInternal`; public setters bind and refuse a model of another card (§1.5) |
| [medium] `pools.json` has no epoch gates | gates ported from the overrides with citations (§6.2), counts asserted with their family named |
| [low] `DeepClone()` keeps the source owner | rebutted: `DeepClone()` is a detached same-owner copy for tests and same-owner callers; state clones use `CloneFor(newOwner)` (M20), and `DeterminismTests`' graph inspector reports any shared reference into a source state; `CardPile.DeepClone` is the state owner's |
| [low] In-combat keywords ignore `Hook.ModifyKeywordsInCombat` | ported (§1.9) and used at the game's read sites in this area |
| [low] Static arrays reachable as mutable state | every def's keyword / tag array is a `ReadOnlyCollection` |
| [low] `HasEnoughResources` drops the combat-state guard | ported (§1.10) |

## 14. Dependencies, contract change requests, open questions

Contract change requests:

1. `AfflictionModel.CanAfflict(CardInstance)` (scaffold §8) became `CanAfflict(CombatState? cs, CardInstance card)` and
   `PickRandomTargets(RunRngSet, cards, count)` became `PickRandomTargets(CombatState? cs, RunRngSet, cards, count)`: the game reads
   the in-combat keyword set, which needs the combat. No other owner calls them.
2. B4 lists `CardCmd.Discard` (loop, `Combat/Loop/CardCmd.Play.cs`) but no stub exists, so `ACROBATICS` calls the P0
   `CardPileCmd.Discard`. At integrate, switch `Acrobatics.OnPlay` to `CardCmd.Discard(cs, card)` (the game's
   `Commands/CardCmd.cs:1811`), or have the loop's `CardPileCmd.Discard` forward to it.
3. Loop (B6 / A23): build `Player.Deck` with `CardInstance.Create` + `AttachTo(player)` and fill the draw pile with
   `CloneForCombat` (never attach twice: the guard throws); create generated cards with `cs.CreateCard`; read in-combat keywords
   at `CombatLoop.cs:340`, `:421`, `:446` (`HasKeywordInCombat`), `ShouldRetainThisTurnIn` / `IsSlyThisTurnIn` at the flush; call
   `Enchantment.OnPlay` / `Affliction.OnPlay` in the §2 order; move `ShouldStartAtBottomOfDrawPile` cards on turn 1 (§2); use
   `CaptureXValuesForAutoPlay` in `CardCmd.AutoPlay`; refresh the `EndToEndTests` pins and the two `LoopTests` expectations
   that encode the ownerless P0 deck path (§12). Until then the W1 gate tests F1, F4 and F5 fail in this worktree on the
   ownerless P0 cards (`Card ARMAMENTS#10 has no owner`), then on the choice stubs: the gate adds every registered prompt pilot
   to its decks (`W1GateTests.CreateGateSim`), so at the base, with no pilot registered, it ran the starter deck only.
4. `RunState.CardMultiplayerConstraint` (`Runs/IRunState.cs:74-84`) belongs on `RunState` (state); `CardPools.MultiplayerConstraintFor(rs)`
   serves until then.
5. `CardPileAddResult` (`Entities.Cards/CardPileAddResult.cs:6-17`) is defined here (`Cards/CardPileAddResult.cs`, fields
   `Success, CardAdded, OldPile (PileType?), TargetPile, ModifyingModels`); the loop's `CardPileCmd.Add` should return this type
   rather than define another.
6. character-mechanics: `ForgeCmd` reaches `SOVEREIGN_BLADE` through `IForgeBladeDef` (`card.Def is IForgeBladeDef`).
7. hooks: `ListenerSnapshot` / `IterateCombatHookListeners` should add `card.AppendHookListeners(...)` per pile card and use the
   three `IsLiveHookListener` predicates (§1.6); the dispatch test is pending on it.
8. state: `CombatHistory.AppendCardPlayFinished` can call `card.HasKeywordInCombat(cs, Ethereal)` (scaffold §16.6 item 13).
9. hooks (performance, optional): `Hook.ModifyKeywordsInCombat` now runs about seven times per decision (§15); a fast path that
   skips the listener snapshot when no live listener overrides `TryModifyKeywordsInCombat` keeps the results and removes the
   ~10 % playout cost.

Open questions:

1. `Soul.CreateInHand` (`Models.Cards/Soul.cs:166-173`) needs the plural `CardPileCmd.AddGeneratedCardsToCombat` (loop, missing);
   not ported, no W1 caller.
2. `CreateClone` / `CreateDupe` (`Models/CardModel.cs:3571-3608`) need a `CloneOf` link by id (a canonical-layout change); not
   ported, no W1 caller; `IsDupe` stays settable.
3. `FindDeckVersion` resolves through the owner's deck; the game keeps a reference to a deck card that has left the deck. No W1
   reader exists. Of the W3 readers (§1.8), `SwipePower` needs the reference after the card left the deck: it removes the deck
   version (`Models.Powers/SwipePower.cs:57-59`) and later re-adds it to another player (`:131-137`); no pile resolves its id
   then, so W3 has to hold that card as power-owned state (cloned with the power) rather than through `DeckVersionId`.
4. The catalog records constructor targets for the four `TargetType` overriders; a rule that filters a pool by target type
   would need the overriders' canonical values.

## 15. Gates and bench

| Gate | Result in this worktree |
| --- | --- |
| `dotnet build sim/Sts2Sim.sln` | 0 warnings, 0 errors |
| `dotnet run --project sim/Sts2Sim.Tests` | 414 passed, 5 failed, 419 total, `PENDING total: 8` (all eight from `CardKernelTests` through `W1Pending`). Failures: the two content gates that fail at the base for other areas' content (`B2Chain`: `DARK_EMBRACE_POWER` or `GREMLIN_HORN`, `HELLRAISER_POWER`; `PilotContentIsRegistered`: relics, potions, powers, orbs, Osty) and `F1`, `F4`, `F5` (contract change request 3; they pass in the probe of §12) |
| Diff gate (P0 bundle set) | 4/4 bundles agree, 69/69 decisions, outcome 4/4, 0 soft diffs |
| Bench (`NIBBITS_WEAK`, Release) | far above the W1 gate (≥ 300 decisions/s, clone ≤ 1 ms); ratio against the base tree below |

Bench, 2026-11-01, Apple M4 Pro, .NET 9.0.16, with other agents' builds running (load varied by more than 2x within minutes, so
single runs are not comparable). The base tree (`263810d`, the W1 phase-0b scaffold) was rebuilt in a scratch directory and run
interleaved with this tree:

| Metric | Base tree | This tree | Ratio |
| --- | --- | --- | --- |
| `Sts2Sim.Bench` playouts, 6 interleaved pairs, wall clock (decisions/s) | 210k-290k, median 227k | 194k-247k, median 204k | 0.90 (ratio of medians; pairs 0.78-0.98) |
| CPU time per decision, fixed workload of 40,000 episodes (803,505 decisions, identical in both trees), 6 interleaved rounds | 1.72-3.93 µs | 1.93-4.38 µs | 1.11 (rounds within 1.08-1.12, one noisy round dropped) |
| `Clone()` (bench, clone + hash minus hash) | median 5.2 µs | median 5.3 µs | 1.02 |
| `StateHash()` | median 2.97 µs | median 3.01 µs | 1.01; same sink value (`283861034CA36B40`), so the canonical bytes of a P0 state are unchanged |

Against the P0 reference (705k decisions/s, 2.53 µs clone, measured on a quiet machine) the same-session ratio above is the
meaningful one. Cause of the ~10 % playout cost, measured with a variant of this tree whose in-combat keyword query skips
`Hook.ModifyKeywordsInCombat` (CPU time then within -4 % to +6 % of the base): `CanPlay` (once per hand card in `LegalActions` and
once in the executor) and `GetResultLocationForCardPlay` now read the game's in-combat keyword set (`Models/CardModel.cs:3339`,
`:3497`), which runs the keyword hook (one listener snapshot) per call, about seven per decision. The port is exact; a hooks-side
fast path (no snapshot when no live listener overrides `TryModifyKeywordsInCombat`) would remove the cost without changing results.
