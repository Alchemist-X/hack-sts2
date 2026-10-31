# Cards area spec — P0 Ironclad starting deck

Owner: cards agent. Code: `sim/Sts2Sim.Core/Cards/**`. Tests: `sim/Sts2Sim.Tests/CardTests.cs`
(`Test_Cards_*`). Rule truth: decompile tree (game v0.111.0). Citations below use
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped, as in `CONTRACTS.md`.

## 0. Status

| Item | Status |
| --- | --- |
| Starting deck list, character HP / max energy constants | ported |
| Run-start deck per ascension (`AscensionManager.ApplyEffectsTo`: Ascender's Bane at A5+) and the Neow arrival HP helper | ported (fidelity fix 2026-10-30, §1) |
| `STRIKE_IRONCLAD`, `DEFEND_IRONCLAD`, `BASH` defs (cost, type, rarity, target, tags, vars, OnPlay) | ported |
| `ASCENDERS_BANE` def (curse added by ascension 5) | ported (§3) |
| Upgrade values | carried on the defs **and** applied (`CardInstance.Upgrade()` → `OnUpgrade`), see §11 |
| Runtime card state (`CardInstance`): upgrade level, keywords, single-turn flags, replay count, dupe flag, current target/play index, star cost state, energy cost | ported |
| `CardEnergyCost` + `LocalCostModifier` (local cost modifiers, X cost, upgrade clamp) | ported |
| Star cost (`TemporaryCardCost`, `BaseStarCost`, `CurrentStarCost`, `LastStarsSpent`) | ported, including the global `Hook.ModifyStarCost` (wired at integration, §6) |
| `CanPlay` / `HasEnoughResources` / `IsValidTarget` / `CanPlayTargeting` | ported as `CardInstance` methods for the loop to call |
| Result pile for a play (`GetResultLocationForCardPlay`) | ported |
| Full card-play sequence (`OnPlayWrapper`, `SpendResources`, pile moves) | **loop agent** (`ActionExecutor`); the card-side steps it needs are listed in §9 |
| Canonical bytes / deep clone for every mutable card type | done (§13) |

## 1. Starting deck and character constants

| Constant | Value | Source |
| --- | --- | --- |
| Ironclad starting deck (order) | 5× `StrikeIronclad`, 4× `DefendIronclad`, 1× `Bash` | `Models.Characters/Ironclad.cs:40-52` |
| `StartingDecks.IroncladStartingHp` | 80 | `Models.Characters/Ironclad.cs:30` |
| `StartingDecks.IroncladMaxEnergy` | 3 (Ironclad does not override) | `Models/CharacterModel.cs:67` |
| `StartingDecks.IroncladCharacterId` | `"IRONCLAD"` = `Slugify("Ironclad")` | `Models/ModelDb.cs:485-488`, `Helpers/StringHelper.cs:68-73` |

Excluded by plan: starting relic `BurningBlood` (`Ironclad.cs:54`), starting gold 99 (`Ironclad.cs:32`).
The deck → draw pile copy and the opening shuffle are the loop agent's
(`Entities.Players/Player.cs:1214-1231`, clone of each deck card in deck order, then
`RandomizeOrderInternal`).

### 1.1 Run-start effects that reach the first combat

The run layer is out of scope, but two run-start rules decide what the first combat of an
A10 run starts with. `StartingDecks` ports them as pure functions; `CombatSim.Create` takes
the result as input (loop spec §2.1, CONTRACTS §4.2).

| Rule | Value | Sim | Source |
| --- | --- | --- | --- |
| Deck at run creation | `PopulateStartingDeck`: each `Character.StartingDeck` card in order (`FloorAddedToDeck = 1`) | `StartingDecks.Ironclad` | `Entities.Players/Player.cs:712-717`, `:735-748`, `:1098-1117` |
| Ascension effects applied once, right after the player joins the run | `RunState.AddPlayer` → `RunManager.ApplyAscensionEffects` (`Runs/RunState.cs:977-981`, `Runs/RunManager.cs:4827-4830`); also for every player at run start (`Runs/RunManager.cs:3856-3863`) | — | |
| A5 `AscendersBane` | `HasLevel(level)` is `_level >= (int)level`; at A5+ one `AscendersBane` is created and appended to the deck (`Deck.AddInternal(card, -1)` = append) | `StartingDecks.ApplyAscensionEffectsToDeck(deck, ascension)`, `IroncladAtRunStart(ascension)` = starter + `ASCENDERS_BANE` last at A5+ | `Entities.Ascension/AscensionManager.cs:22-38`; `Entities.Ascension/AscensionLevel.cs:10` (`AscendersBane` = 5); `Entities.Cards/CardPile.cs:248-263` |
| A4 `TightBelt` (one potion slot less) | potions are out of scope | not ported | `AscensionManager.cs:29-32` |
| The curse cannot leave the deck | `Eternal` → `IsRemovable` false; an Eternal card in the deck is not transformable either, so every A5+ run deck holds it | — | `Models/CardModel.cs:2100-2113` |
| Neow arrival heal (act 1's only ancient is `Neow`) | Neow first sets current HP to 0, then every ancient heals `MaxHp - CurrentHp`, × 0.8 when `HasAscension(WearyTraveler)` (A2+); `CreatureCmd.Heal` → `HealInternal` → `SetCurrentHpInternal` = `(int)Min(hp + amount, MaxHp)` | `StartingDecks.HpAfterNeowArrival(maxHp, ascension)`, `IroncladHpAfterNeowArrival(ascension)`: 80 at A0–A1, `(int)(80 × 0.8m)` = 64 at A2+; `WearyTravelerHealMultiplier = 0.8m` | `Models/AncientEventModel.cs:76-88`; `Models.Acts/Overgrowth.cs:26`; `Models/ActModel.cs:322`; `Entities.Ascension/AscensionLevel.cs:7`; `Commands/CreatureCmd.cs:2102-2110`; `Entities.Creatures/Creature.cs:1067-1081` |

The Neow helper gives HP after the arrival heal and before any Neow option; an option that
changes HP is the caller's business. `StartingDecks.IsIroncladStartingDeck(deck)` (content and
order, ordinal) lets the loop tell the bare starter deck from a run deck
(`CombatLoop.DeckForCombat`, loop spec §2.1).

## 2. Card ids and registry

- Id = `ModelId.Entry` = `StringHelper.Slugify(type.Name)` (`Models/ModelDb.cs:485-488`;
  `Helpers/StringHelper.cs:68-73`: CamelCase → `_`-separated, upper-cased, `[^A-Z0-9_]` removed).
  `StrikeIronclad` → `STRIKE_IRONCLAD`, `DefendIronclad` → `DEFEND_IRONCLAD`, `Bash` → `BASH`.
  Each def states its id as a `const string CardId` literal; a test re-derives it with
  `DeterministicHash.Slugify(nameof(T))`.
- Registration: each def is a `public sealed class Xxx : CardDef` with a public parameterless
  constructor in `Cards/Defs/`, discovered by `Registry/Registries.cs` (`CardRegistry`). One shared
  immutable `CardDef` per id = the game's canonical `CardModel` (`ModelDb.Card<T>()`).
- `CardInstance.Create(cs, id | def)` = `CombatState.CreateCard` (`Combat/CombatState.cs:671-677`):
  new mutable instance with `cs.AllocateCardInstanceId()`. `AfterCreated()` (`Models/CardModel.cs:2884`)
  is not called: no P0 card overrides it (only `SpoilsMap`).

## 3. Starter card definitions

All three use the base `CardModel(int canonicalEnergyCost, CardType, CardRarity, TargetType)`
constructor (`Models/CardModel.cs:2727-2734`). None overrides `CanonicalKeywords` (→ none),
`MaxUpgradeLevel` (→ 1, `CardModel.cs:2145`), `CanonicalStarCost` (→ -1, `CardModel.cs:1848`),
`HasEnergyCostX` (→ false, `CardModel.cs:1816`), `IsPlayable` (→ true, `CardModel.cs:2179`),
`HasTurnEndInHandEffect` (→ false, `CardModel.cs:2377`).

### STRIKE_IRONCLAD (`Models.Cards/StrikeIronclad.cs`)

| Field | Value | Line |
| --- | --- | --- |
| cost / type / rarity / target | 1 / Attack / Basic / AnyEnemy | `:100-103` |
| tags | { Strike } | `:88-96` |
| vars | `DamageVar(6m, ValueProp.Move)` (name `"Damage"`) | `:98` |
| upgrade | `Damage.UpgradeValueBy(3m)` | `:120-123` |
| OnPlay | `ThrowIfNull(cardPlay.Target)`; `DamageCmd.Attack(Damage.BaseValue).FromCard(this, cardPlay).Targeting(Target).WithHitFx(...).Execute(...)` | `:50-54` |

Sim: `DamageCmd.Attack(cs, card.Vars.Damage.BaseValue).FromCard(card, play).Targeting(target).Execute()`.

### DEFEND_IRONCLAD (`Models.Cards/DefendIronclad.cs`)

| Field | Value | Line |
| --- | --- | --- |
| cost / type / rarity / target | 1 / Skill / Basic / Self | `:95-98` |
| `GainsBlock` | true | `:81` |
| tags | { Defend } | `:83-91` |
| vars | `BlockVar(5m, ValueProp.Move)` (name `"Block"`) | `:93` |
| upgrade | `Block.UpgradeValueBy(3m)` | `:114-117` |
| OnPlay | `CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay)` | `:47` |

The `BlockVar` overload forwards `(blockVar.BaseValue, blockVar.Props, cardPlay, fast)`
(`Commands/CreatureCmd.cs:3942-3947`), so the sim calls
`CreatureCmd.GainBlock(cs, cs.Player, block.BaseValue, block.Props, play)`. Owner creature =
`cs.Player` (single player).

### BASH (`Models.Cards/Bash.cs`)

| Field | Value | Line |
| --- | --- | --- |
| cost / type / rarity / target | 2 / Attack / Basic / AnyEnemy | `:126-129` |
| tags | none | (no override) |
| vars (in order) | `DamageVar(8m, ValueProp.Move)`, `PowerVar<VulnerablePower>(2m)` (name `"VulnerablePower"`) | `:120-124` |
| upgrade | `Damage.UpgradeValueBy(2m)`; `Vulnerable.UpgradeValueBy(1m)` | `:146-150` |
| OnPlay | `ThrowIfNull(Target)`; attack (as Strike) | `:69-73` |
|  | then `PowerCmd.Apply<VulnerablePower>(ctx, cardPlay.Target, Vulnerable.BaseValue, Owner.Creature, this)` | `:89` |

Sim: attack, then `PowerCmd.Apply<VulnerablePower>(cs, target, card.Vars.Vulnerable.BaseValue, cs.Player, card)`.
`PowerVar<T>` name is `typeof(T).Name` (`Localization.DynamicVars/PowerVar.cs:12-15`), looked up by
`DynamicVarSet.Vulnerable` as `"VulnerablePower"` (`Localization.DynamicVars/DynamicVarSet.cs:70`); the
sim relies on the pipeline's power class being named exactly `VulnerablePower`.

### ASCENDERS_BANE (`Models.Cards/AscendersBane.cs`)

| Field | Value | Line |
| --- | --- | --- |
| cost / type / rarity / target | −1 / Curse / Curse / None | `:26-29` |
| keywords (canonical order) | Eternal, Unplayable, Ethereal | `:16-24`, see below |
| `MaxUpgradeLevel` | 0 (never upgradable) | `:14` |
| `CanBeGeneratedInCombat`, `CanBeGeneratedByModifiers` | false, false (defaults true, `Models/CardModel.cs:2043`, `:2045`) | `:10-12` |
| tags, vars, star cost, `OnPlay` | none; no `OnPlay` override (the default does nothing, `Models/CardModel.cs:3290`) | — |

The keyword array is built with `RuntimeHelpers.InitializeArray` from a field token the decompiler
could not print (`:21`). The data was read from the same build's IL
(`output/server-bootstrap/linux-assembly/sts2.dll`, with `System.Reflection.Metadata`):
`get_CanonicalKeywords` is `ldc.i4.3; newarr CardKeyword; dup; ldtoken <PrivateImplementationDetails>.8CD414C6…54BA;
call InitializeArray`, and the 12 bytes at that field's RVA `0x94F5E0` are the int32 values 7, 4, 2, i.e.
`Eternal`, `Unplayable`, `Ethereal` (`Entities.Cards/CardKeyword.cs:3-13`). The live v0.107.1 card dump
agrees (`knowledge/source/v0.107.1/cards.jsonl:20`: runtime keywords 永恒 / 不能被打出 / 虚无 =
Eternal / Unplayable / Ethereal).
A cross-check of the method on `CurseOfTheBell` (inline `stelem` of 7, 4) reproduces its decompiled
array (`Models.Cards/CurseOfTheBell.cs:14`).

Behaviour in combat, all through existing generic rules: `CanPlay` is false with
`HasUnplayableKeyword` (§8); at the player's turn end an Ethereal card in hand is exhausted
(`causedByEthereal`, loop spec §3.6); a negative cost spends nothing
(`GetAmountToSpend` = `Max(0, Base)`, §5); `IsRemovable` is false (Eternal).

## 4. Runtime card model

`CardDef` = canonical `CardModel` (immutable, shared); `CardInstance` = mutable `CardModel`
(`ToMutable`, `Models/CardModel.cs:2821-2859`). Clone of a canonical: fresh vars, fresh energy cost,
local keywords = canonical keywords (`CardModel.cs:2827-2859`, `1818-1828`, `1926-1938`, `1946-1958`).

| Game member | Sim member | Source |
| --- | --- | --- |
| `CanonicalEnergyCost`, `HasEnergyCostX`, `EnergyCost` (lazy) | `CardDef.CanonicalCost/CostsX`, `CardInstance.EnergyCost` (ctor) | `CardModel.cs:1810-1828` |
| `BaseReplayCount` | `BaseReplayCount` | `CardModel.cs:1830-1846` |
| `GetEnchantedReplayCount()` | `GetEnchantedReplayCount()` = `BaseReplayCount` (no enchantments) | `CardModel.cs:2760-2763` |
| `_keywords` / `AddKeyword` / `RemoveKeyword` | `KeywordMask` (bit `1 << (int)keyword`) / same | `CardModel.cs:1926-1938`, `2965-2985` |
| `Tags` (cached `CanonicalTags`, never mutated) | `HasTag` → `Def.CanonicalTags` | `CardModel.cs:1942-1944` |
| `ExhaustOnNextPlay` | `ExhaustOnNextPlay` | `CardModel.cs:1962-1973` |
| `HasSingleTurnRetain` / `ShouldRetainThisTurn` | same | `CardModel.cs:1975-1998`, `2987-2990` |
| `HasSingleTurnSly` / `IsSlyThisTurn` | same | `CardModel.cs:2000-2023`, `2992-2995` |
| `GainsBlock`, `IsBasicStrikeOrDefend` | `CardDef` virtuals, mirrored on instance | `CardModel.cs:2049-2069` |
| `IsDupe` | `IsDupe` (settable; dupe creation deferred) | `CardModel.cs:2087-2098` |
| `IsRemovable` | `IsRemovable` (no Eternal keyword) | `CardModel.cs:2100` |
| `CanBeGeneratedInCombat`, `CanBeGeneratedByModifiers` (default true) | `CardDef` virtuals; read by no P0 code (card generation is not ported) | `CardModel.cs:2043`, `:2045` |
| `CurrentUpgradeLevel` (throws past max) | `UpgradeLevel` (same message) | `CardModel.cs:2127-2143` |
| `IsUpgraded` / `IsUpgradable` | same | `CardModel.cs:2147-2159` |
| `CurrentTarget` | `CurrentTargetId` (creature `CombatId`, `NoTarget = -1`), `SetCurrentTarget`, `GetCurrentTarget(cs)` | `CardModel.cs:2246-2257` |
| `CurrentPlayIndex` | `CurrentPlayIndex` | `CardModel.cs:2259-2270` |
| `IsPlayable` | `CardDef.IsPlayable(cs, card)` | `CardModel.cs:2179` |
| `OnPlay` | `CardDef.OnPlay(cs, play)`; `CardInstance.OnPlay(cs, play)` forwards (and rejects a `CardPlay` of another card) | `CardModel.cs:3290-3293` |
| `OnUpgrade` | `CardDef.OnUpgrade(card)` | `CardModel.cs:3300-3302` |
| `OnTurnEndInHand(Wrapper)` | `CardDef.OnTurnEndInHand(cs, card)`, `CardInstance.OnTurnEndInHand(cs)` | `CardModel.cs:3304-3312` |
| `HasTurnEndInHandEffect` | same | `CardModel.cs:2377` |
| `EndOfTurnCleanup()` | same: clear `ExhaustOnNextPlay`, single-turn Retain/Sly, `EnergyCost.EndOfTurnCleanup()`, drop star costs with `ClearsWhenTurnEnds` | `CardModel.cs:3250-3271` |
| end of `OnPlayWrapper` cleanup | `AfterCardPlayedCleanup()`: `EnergyCost.AfterCardPlayedCleanup()`, drop star costs with `ClearsWhenCardIsPlayed`, target → none, play index → 0 | `CardModel.cs:1017-1034` |
| `SetToFreeThisTurn` | `EnergyCost.SetThisTurnOrUntilPlayed(0)` + `SetStarCostThisTurn(0)` | `CardModel.cs:2902-2906` |
| `SetToFreeThisCombat` | `EnergyCost.SetThisCombat(0)` + `SetStarCostThisCombat(0)` | `CardModel.cs:2908-2912` |
| `ResolveEnergyXValue` | `ResolveEnergyXValue(cs)`: throws unless X cost; `CapturedXValue` folded through every combat listener's `ModifyXValue` | `CardModel.cs:2750-2758`, `Hooks/Hook.cs:11151-11168` |
| `ResolveStarXValue` | `ResolveStarXValue(cs)` with `LastStarsSpent` | `CardModel.cs:2765-2773` |

`ResolveEnergyXValue` / `ResolveStarXValue` call the pipeline's `Hook.ModifyXValue(cs, card, value)`
(guarded combat listeners, `Hooks/Hook.cs:11151-11168`). Integration replaced the earlier local
listener walk, which allocated a list per call.

## 5. Energy cost (`CardEnergyCost`, `LocalCostModifier`)

Source: `Entities.Cards/CardEnergyCost.cs`, `LocalCostModifier.cs`, `LocalCostType.cs`,
`LocalCostModifierExpiration.cs`, `CostModifiers.cs`.

- Constructor: `CostsX = costsX`; `Canonical = costsX ? 0 : canonicalCost`; `Base = Canonical` (`:68-74`).
- Enums: `LocalCostType { None, Absolute, Relative }`; `LocalCostModifierExpiration` flags
  `{ EndOfCombat = 0, EndOfTurn = 2, WhenPlayed = 4 }`; `CostModifiers` flags
  `{ None = 0, Local = 2, Global = 4, All = -1 }` (ordinals copied).
- `GetWithModifiers(mods)` (`:76-114`): `Base < 0` or X → return `Base` unchanged; else fold local
  modifiers in insertion order when `Local` is set; then, when `Global` is set and the card is in combat,
  `(int)Hook.ModifyEnergyCostInCombat(cs, card, num)`; return `Math.Max(0, num)`.
  The sim has two overloads: `(mods)` = the "not in combat" path (`_card.CombatState == null`, local only)
  and `(cs, card, mods)` = the in-combat path including the Global hook.
- `GetAmountToSpend` (`:116-123`): X → player's current energy; else `Math.Max(0, GetWithModifiers(All))`.
  Sim: `(cs, card)` overload; the parameterless overload throws for X (it has no player to read).
- `GetResolved` (`:125-132`): X → `CapturedXValue`; else `Math.Max(0, GetWithModifiers(All))`.
- `CapturedXValue` get/set throw `InvalidOperationException("Only X-cost cards have a captured value.")`
  unless X (`:45-66`). It is written by `SpendEnergy` (`Models/CardModel.cs:1249-1252`, loop agent).
- Setters (`:134-196`): `Set*` add an `Absolute` modifier only when `cost != 0 || Canonical >= 0`;
  `Add*` add a `Relative` modifier only when `amount != 0`. Lifetimes: `UntilPlayed` = `WhenPlayed`,
  `ThisTurnOrUntilPlayed` = `EndOfTurn | WhenPlayed`, `ThisTurn` = `EndOfTurn`, `ThisCombat` = `EndOfCombat`.
- `EndOfTurnCleanup` / `AfterCardPlayedCleanup` (`:198-208`): remove modifiers whose expiration has the
  `EndOfTurn` / `WhenPlayed` flag; return whether any were removed. `EndOfCombat` (= 0) modifiers are
  never removed by either.
- `LocalCostModifier.Modify` (`LocalCostModifier.cs:46-55`): Absolute → `reduceOnly ? Min(cur, Amount) : Amount`;
  Relative → `reduceOnly ? Min(cur, cur + Amount) : cur + Amount`; other → `ArgumentOutOfRangeException`.
- `UpgradeBy(addend)` (`:210-242`): no-op for X or 0; `new = Max(Base + addend, 0)`; `WasJustUpgraded = true`;
  when `new < old`, every Absolute modifier with `Amount > new` is clamped to `new`; `Base = new`.
- `FinalizeUpgrade` (`:244-248`), `ResetForDowngrade` (`:250-255`, `Base = Canonical`),
  `SetCustomBaseCost` (`:257-262`).
- Clone (`:264-274`) copies base, captured X, just-upgraded flag and clones each modifier.

## 6. Star cost

Ironclad cards have `CanonicalStarCost = -1` and never touch stars; ported for complete per-card
state and because `HasEnoughResourcesFor` / `SpendResources` read it.

- `BaseStarCost` starts at `CanonicalStarCost`; its setter ignores writes when `HasStarCostX`
  (`CardModel.cs:1850-1879`; the game initialises lazily, the sim eagerly — same value).
- `TemporaryCardCost.UntilPlayed/ThisTurn/ThisCombat(cost)`: `Cost = Max(cost, 0)`, flags
  `(turn, played)` = `(false, true)`, `(true, true)`, `(false, false)` (`Entities.Cards/TemporaryCardCost.cs:35-63`).
  Immutable, so clones share instances and copy only the list.
- `CurrentStarCost` (`CardModel.cs:1885-1900`): last temporary cost, except a temporary 0 over a negative
  base returns the base; no temporary → base.
- `GetStarCostThisCombat` (`:2929-2932`): first temporary cost that clears neither at turn end nor on
  play, else base.
- `UpgradeStarCostBy` (`:2945-2963`): throws for star-X; no-op for 0; adds to base, sets
  `WasStarCostJustUpgraded`, and when the base went down removes temporary costs above the new base.
- `GetStarCostWithModifiers(cs)` (`:3204-3216`): star-X → player's stars; else, when the card is in a
  combat pile, `(int)Hook.ModifyStarCost(cs, card, CurrentStarCost)` (`Hooks/Hook.cs:11071-11092`,
  which returns a negative cost unchanged); else `CurrentStarCost`. "In a combat pile" =
  `cs.Player.FindPileContaining(card) != null` (the sim has no deck pile). The sim skips the pile
  lookup when `CurrentStarCost < 0`, which gives the same result because the hook returns negative
  costs unchanged.
- `LastStarsSpent` is set by `SpendStars` on every play (`CardModel.cs:1432`, loop agent).
- `CostsEnergyOrStars(cs, includeGlobal)` (`CardModel.cs:3218-3236`).

## 7. Dynamic vars

- `DynamicVar(name, baseValue)`; `BaseValue` setter clamps with `Math.Min(value, 999999999m)`
  (`Localization.DynamicVars/DynamicVar.cs:29-40`); `IntValue = (int)BaseValue` (`:75`);
  `UpgradeValueBy(addend)`: `BaseValue += addend; WasJustUpgraded = true` (`:99-103`);
  `FinalizeUpgrade` (`:105-108`); `Clone` = `MemberwiseClone` (`:110-115`, the `ResetToBase` there only
  touches UI preview values).
- `DamageVar` default name `"Damage"` (`DamageVar.cs:13`, `:24-34`); `BlockVar` default name `"Block"`
  (`BlockVar.cs:13`, `:22-32`); both carry `ValueProp Props`. The sim stores `Props` on the base
  `DynamicVar` (`None` for other vars). No model writes `Props` at runtime (grep of Cards/Powers/
  Relics/Enchantments), so it is immutable in the sim.
- `DynamicVarSet` (`DynamicVarSet.cs`): insertion-ordered, duplicate names throw `ArgumentException`
  (`:95-115`); named accessors `Block` (`:26`), `Damage` (`:38`), `Vulnerable` = `"VulnerablePower"` (`:70`)
  and the rest of the list (`:26-72`); `FinalizeUpgrade` (`:169-184`); `Clone` clones every var (`:203-208`).
  Sim stores an array (same order) and returns the same empty set object when there are no vars.

## 8. Playability, targeting, result pile

- `CardInstance.CanPlay(cs, out reason)` = `CardModel.CanPlay` (`CardModel.cs:3330-3360`), all checks
  evaluated, reasons OR-ed:
  `Unplayable` keyword → `HasUnplayableKeyword`; `HasEnoughResources` (below); `AnyAlly` target with
  `<= 1` living player creature → `NoLivingAllies` (single player: always, `Combat/CombatState.cs:560`
  counts players only); `!Hook.ShouldPlay(cs, card)` → `BlockedByHook` (`Hooks/Hook.cs:11554-11575`);
  `!Def.IsPlayable` → `BlockedByCardLogic`. The game's leading "not in combat → false" guard
  (`:3333-3338`) has no sim counterpart.
- `HasEnoughResources(cs, out reason)` = `PlayerCombatState.HasEnoughResourcesFor`
  (`Entities.Players/PlayerCombatState.cs:334-353`): energy `Max(0, EnergyCost.GetWithModifiers(cs, card, All))`
  vs `Player.Energy` → `EnergyCostTooHigh`; stars `Max(0, GetStarCostWithModifiers(cs))` vs `Player.Stars`
  → `StarCostTooHigh`. The "pay excess energy with stars" branch (`:338-342`) is ported: when the energy
  cost exceeds the player's energy and `Hook.ShouldPayExcessEnergyCostWithStars` is true, the star cost
  grows by `2 × (cost − energy)` and the energy cost becomes the player's energy. No v0.111.0 model
  overrides the gate (only the `false` default at `Models/AbstractModel.cs:1037`), so today it never
  fires; it keeps `CanPlay` in line with `ActionExecutor.SpendResources` (`Models/CardModel.cs:1345-1350`).
- `IsValidTarget(cs, target)` (`CardModel.cs:3362-3385`): null → valid unless `AnyEnemy`/`AnyAlly`;
  dead → invalid; `AnyEnemy` → other side than the player; `AnyAlly` → player side; else invalid.
- `CanPlayTargeting` (`CardModel.cs:3314-3321`) = `IsValidTarget && CanPlay`. The play action checks
  `CanPlay && IsValidTarget` before spending (`GameActions/PlayCardAction.cs:137`). The UI additionally
  filters targets with `Hook.ShouldAllowTargeting` (`Nodes.Combat/NTargetManager.cs:660`); whether legal
  actions include that filter is the loop agent's decision.
- `GetResultLocationForCardPlay(cs)` (`CardModel.cs:3491-3503`): dupe or Power → `(None, Bottom)`;
  else `ExhaustOnNextPlay` or Exhaust keyword → clear `ExhaustOnNextPlay`, `(Exhaust, Bottom)`;
  else `(Discard, Bottom)`. It mutates the card, and the game evaluates it before `OnPlay`
  (`CardModel.cs:935-936`). `CardLocation` drops the game's `player` field (single player).

## 9. Card-side steps of a card play (for the loop agent)

From `PlayCardAction` (`GameActions/PlayCardAction.cs:137-182`) and `OnPlayWrapper`
(`Models/CardModel.cs:257-1064`), the calls on the card, in order:

1. `card.CanPlay(cs)` and `card.IsValidTarget(cs, target)`.
2. Spend: energy `card.EnergyCost.GetAmountToSpend(cs, card)`, stars `Math.Max(0, card.GetStarCostWithModifiers(cs))`
   (`CardModel.cs:3404-3419`); X cards: `card.EnergyCost.CapturedXValue = amount` (`:1249-1252`);
   always `card.LastStarsSpent = stars` (`:1432`).
3. `card.SetCurrentTarget(target)`, `card.CurrentPlayIndex = 0` (`:837-838`), move to Play pile.
4. `var loc = card.GetResultLocationForCardPlay(cs)` then `Hook.ModifyCardPlayResultLocation` (`:935-936`).
5. Play count = `card.GetEnchantedReplayCount() + 1` → `ModifyCardPlayCount` (`:83-85`).
6. Per play `i`: `card.CurrentPlayIndex = i` (`:620`); `new CardPlay(card, target) { ResultPile = loc.PileType, PlayIndex = i, PlayCount = n, ... }` (`:758-768`);
   `Hook.BeforeCardPlayed` (`:769`); `card.OnPlay(cs, play)` (`:669`); `Hook.AfterCardPlayed` (`:739`).
7. Move to the result pile (`:942-979`), then `card.AfterCardPlayedCleanup()` (`:1017-1034`).

In combat always use the `(cs, card)` energy overloads so `Hook.ModifyEnergyCostInCombat` applies.

## 10. Stripped sites (Godot / VFX / coroutines)

| Site | Game | Sim |
| --- | --- | --- |
| Every `OnPlay` | `async Task` state machine with `TaskAwaiter`s | plain synchronous calls in the same order |
| `PlayerChoiceContext choiceContext` args (`StrikeIronclad.cs:53`, `Bash.cs:72`, `Bash.cs:89`) | passed to `Execute` / `PowerCmd.Apply` | dropped (no card choices in P0) |
| `.WithHitFx("vfx/vfx_attack_slash")` (`StrikeIronclad.cs:52`) | sets `HitVfx/HitSfx/TmpHitSfx` (`Commands.Builders/AttackCommand.cs:908-914`), read only by `SfxCmd`/`VfxCmd` (`:1037-1043`, `:1098-1125`), no RNG | dropped; side-effect order unchanged |
| `.WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3")` (`Bash.cs:71`) | same | dropped |
| `GainBlock(..., fast = false)` (`CreatureCmd.cs:3942-3984`) | `fast` only picks the wait length after block | dropped |
| `ExtraHoverTips` (`Bash.cs:118`), titles, portraits, descriptions, events (`EnergyCostChanged`, `Played`, `Upgraded`, ...), `UpgradePreviewType`, `DynamicVar.PreviewValue/EnchantedValue`, `UpdateCardPreview` | UI only | not ported |
| `AssertMutable` / `IsCanonical` guards | runtime assertions | replaced by the def/instance type split |

No dropped site changes the order of rule side effects.

## 11. Upgrades

Upgrade deltas are constants on each def (`DamageUpgrade`, `BlockUpgrade`, `VulnerableUpgrade`) and
applied by `CardDef.OnUpgrade`. `CardInstance.Upgrade()` = per-card body of `CardCmd.Upgrade`
(`Commands/CardCmd.cs:1887-1938`): skip when not upgradable, else `UpgradeInternal()`
(`UpgradeLevel + 1`, throws past max, then `OnUpgrade`; `CardModel.cs:3519-3530`) and
`FinalizeUpgradeInternal()` (vars, energy cost, star cost flags cleared; `CardModel.cs:3532-3537`).
`DynamicVars.RecalculateForUpgradeOrEnchant` only affects `CalculatedVar`s (none in P0).
`CardCmd.Upgrade`'s `CombatManager.IsEnding` early return (`CardCmd.cs:1890-1893`) is the caller's job.
P0 deck ids carry no upgrade marker, so combats from `CombatSim.Create` start unupgraded.

## 12. Deferred / not ported (documented gaps)

| Gap | Game | Effect in P0 |
| --- | --- | --- |
| Global keywords | `Keywords` adds `Hook.ModifyKeywordsInCombat` in combat (`CardModel.cs:2775-2814`, `Hook.cs:10458-10473`); only `HexPower` overrides (adds Ethereal to Hexed cards) | none: sim keywords are local only |
| Enchantments / afflictions | `Enchantment`, `Affliction`, `EnchantInternal`, `GetEnchantedReplayCount` with enchantment | not ported; replay count = `BaseReplayCount` |
| Dupes / clones | `CreateClone`, `CreateDupe` (`CardModel.cs:3571-3608`) | flag only (`IsDupe`), no factory |
| Downgrade | `DowngradeInternal` (`CardModel.cs:3539-3556`) | not ported |
| `AfterCreated` | `CardModel.cs:2884` | not called (only `SpoilsMap` overrides) |
| Deck-level fields | `DeckVersion`, `FloorAddedToDeck`, `HasBeenRemovedFromState`, serialization | run layer, out of scope |
| Card hook listeners | cards in combat piles receive combat hooks (`CardModel.cs:2379`) | P0 makes only powers listeners (`CONTRACTS.md` §9) |
| Multiplayer | `CardMultiplayerConstraint`, owner checks, `GiveToAnotherPlayer` | single player |

## 13. Canonical bytes and cloning

Tags (`Cards/CardCanonicalTags.cs`): `CardInstance 0xCA01`, `EnergyCost 0xCA02`,
`LocalCostModifier 0xCA03`, `VarSet 0xCA04`, `Var 0xCA05`, `TemporaryCardCost 0xCA06`.

- `CardInstance`: tag, def id, instance id, upgrade level, keyword mask, `ExhaustOnNextPlay`,
  single-turn Retain/Sly, `BaseReplayCount`, `IsDupe`, `CurrentTargetId`, `CurrentPlayIndex`,
  `BaseStarCost`, `WasStarCostJustUpgraded`, `LastStarsSpent`, temporary star costs (count + each),
  energy cost, vars.
- `CardEnergyCost`: tag, canonical, X flag, base, captured X, just-upgraded, modifiers (count + each:
  tag, amount, type, expiration, reduce-only).
- `DynamicVarSet`: tag, count, each var (tag, name, normalized decimal value, props, just-upgraded).
- `DeepClone`: `MemberwiseClone` + new vars array of cloned vars + cloned energy cost (cloned modifiers)
  + copied temporary star cost list. Defs are shared. No references back into the source.

## 14. Tests (`CardTests.cs`)

Added by the 2026-10-30 fidelity fix: `RunStartDeckAndNeowHpFollowAscension` (no curse at A0–A4,
`ASCENDERS_BANE` appended last at A5–A10, starter-deck recognition by content and order, Neow HP
80 at A0–A1 and 64 at A2–A10, `(int)` truncation, invalid max HP) and `AscendersBaneDefinition`
(id slug, every def field, keyword order, Eternal → not removable, not upgradable, cost −1 spends 0,
`CanPlay` false with `HasUnplayableKeyword`).

Card-local only. Pure tests: ids and slug rule, registry, starting deck and character constants, every
def field and var value (§3), upgrade deltas and level guard, just-upgraded flags, instance isolation,
deep-clone isolation, canonical hash equal under clone and different after mutation, energy cost
modifier lifetimes / reduce-only / negative base, `UpgradeBy` clamp, `LocalCostModifier.Modify`,
X cost, star cost overrides, free-this-turn/combat, keywords and single-turn flags, result pile,
target validity, instance id allocation, dynamic var clamp and set rules, play-state cleanup.
Added at integration: `GlobalStarCostAppliesOnlyToCardsInCombatPiles` (a test power that sets star costs
to 0 changes the cost only once the card is in the hand; a negative star cost is returned unchanged) and
`ExcessEnergyIsPaidWithStarsOnlyWhenAHookAllowsIt` (Bash at 1 and 0 energy with and without a test power
that opens the gate: 2 and 4 stars needed).
Tests that also exercise other areas (run after integration): `CanPlay` (needs `Hook.ShouldPlay`,
`Hook.ModifyEnergyCostInCombat`), `CostsEnergyOrStars`, `ResolveEnergyXValue`
(`Hook.IterateCombatHookListeners`), and the effect tests on a hand-built `CombatState` with one 40-HP
dummy monster: Strike → 34 HP, Defend → 5 block, Bash → 32 HP and Vulnerable 2 applied by the player,
upgraded Strike 9 / Defend 8 / Bash 10 + Vulnerable 3.

## 15. Dependencies on other areas

- pipeline: `VulnerablePower` class (namespace `Sts2Sim.Core.Powers`, class name exactly
  `VulnerablePower`), `DamageCmd.Attack(...).FromCard().Targeting().Execute()`, `CreatureCmd.GainBlock`,
  `PowerCmd.Apply<T>`, `Hook.ShouldPlay`, `Hook.ModifyEnergyCostInCombat`, `Hook.ModifyStarCost`,
  `Hook.ModifyXValue`, `Hook.ShouldPayExcessEnergyCostWithStars`.
- state: `CombatState.AllocateCardInstanceId`, `GetCreatureById`, `PlayerState.FindPileContaining`,
  `CanonicalWriter`, `StateHasher`.
- rng: `DeterministicHash.Slugify` (tests), `RunRngSet(Rng)` (tests).
- monsters: `MonsterDef`, `MoveStateDef`, `MonsterMoveStateMachineDef`, `MonsterAiRuntime` constructors (test dummy only).
