# Card upgrades, `CardSpec` decks and GREED

2026-10-30, cards agent. Rule truth: decompile tree, game v0.111.0. Citations use
`<ns-dir>/<File>.cs:<line>` with the `MegaCrit.Sts2.Core.` prefix dropped, as in `CONTRACTS.md`.
This file adds to `cards.md` (not edited here). Where they disagree, this file is newer: the last
sentence of `cards.md` §11 ("P0 deck ids carry no upgrade marker…") no longer holds, because
`CardSpec` carries one.

Code touched:

| File | Change |
| --- | --- |
| `Api/CardSpec.cs` (new) | `readonly record struct CardSpec(string Id, bool Upgraded)`, `Parse`, `FromIds`, `ToString` with the `+` suffix |
| `Cards/CardInstance.cs` | `Create(cs, CardSpec)`, `ResolveDef(CardSpec)`, `Spec` |
| `Cards/Defs/StrikeIronclad.cs`, `DefendIronclad.cs`, `Bash.cs` | `Upgraded*` constants (base + delta) |
| `Cards/Defs/Greed.cs` (new) | `GREED` |
| `Combat/Loop/CombatSim.cs` | `Create(RunRngSet, IReadOnlyList<CardSpec>, string, int, int?, int?)` |
| `Combat/Loop/CombatLoop.cs` | `CreateCombat`/`SetUpCombat` overloads on `IReadOnlyList<CardSpec>`; the string overloads map to them; `PopulateDrawPile` builds from specs |

Tests: `sim/Sts2Sim.Tests/CardUpgradeTests.cs` (`Test_Cards_*`, §6).

## 1. Upgraded values

None of the three `OnUpgrade` bodies calls `EnergyCost.UpgradeBy`, so the cost is the same after
the upgrade. The value props stay `ValueProp.Move` (the upgrade only adds to `BaseValue`,
`Localization.DynamicVars/DynamicVar.cs:99-103`).

| Card | Base values | `OnUpgrade` | Upgraded values | Cost | Sim constants |
| --- | --- | --- | --- | --- | --- |
| `STRIKE_IRONCLAD+` | `DamageVar(6m, Move)` (`Models.Cards/StrikeIronclad.cs:98`) | `Damage.UpgradeValueBy(3m)` (`:120-123`) | Damage 9 | 1 (`:100-103`) | `StrikeIronclad.UpgradedDamage = BaseDamage + DamageUpgrade` = 9m |
| `DEFEND_IRONCLAD+` | `BlockVar(5m, Move)` (`Models.Cards/DefendIronclad.cs:93`) | `Block.UpgradeValueBy(3m)` (`:114-117`) | Block 8 | 1 (`:95-98`) | `DefendIronclad.UpgradedBlock` = 8m |
| `BASH+` | `DamageVar(8m, Move)`, `PowerVar<VulnerablePower>(2m)` (`Models.Cards/Bash.cs:120-124`) | `Damage.UpgradeValueBy(2m)`; `Vulnerable.UpgradeValueBy(1m)` (`:146-150`) | Damage 10, Vulnerable 3 | 2 (`:126-129`) | `Bash.UpgradedDamage` = 10m, `Bash.UpgradedVulnerable` = 3m |

The values reach the rules through the existing code: each `OnPlay` reads
`card.Vars.Damage/Block/Vulnerable.BaseValue` (`StrikeIronclad.cs:51`, `DefendIronclad.cs:47`,
`Bash.cs:70`, `:89`), so `DamageCmd.Attack`, `CreatureCmd.GainBlock` and
`PowerCmd.Apply<VulnerablePower>` get the upgraded numbers without any branch on the flag. The
costs are unchanged, so the legal actions of an upgraded deck match those of the same ids
unupgraded.

## 2. Upgrade state of a combat card

### 2.1 The flag and its canonical form

- The flag is `CardInstance.IsUpgraded` = `UpgradeLevel > 0`, as `CardModel.IsUpgraded`
  (`Models/CardModel.cs:2147`). `CardInstance.Spec` returns `CardSpec(Def.Id, IsUpgraded)`.
- `CardInstance.WriteCanonical` already writes `UpgradeLevel` and the var values (cards spec §13),
  so the state hash tells Strike from Strike+. The canonical format did not change. A deck with
  no upgrades hashes exactly as before, and every pinned hash in the suite still holds.
- A bool loses nothing in v0.111.0. `MaxUpgradeLevel` defaults to 1 (`Models/CardModel.cs:2145`).
  In `Models.Cards/` 39 models override it to 0 and none override it above 1 (grep of
  `MaxUpgradeLevel`), so the upgrade level is always 0 or 1.

### 2.2 How the game puts an upgraded card into a combat

1. Upgrading a deck card, for example with `NEOWS_TALISMAN`, uses `CardCmd.Upgrade`
   (`Commands/CardCmd.cs:1887-1946`). It returns early when `CombatManager.IsEnding` (`:1890-1893`),
   skips cards that are not upgradable (`:1900-1903`), then calls `UpgradeInternal()` and
   `FinalizeUpgradeInternal()` (`:1909-1910`). The map-history entry (`:1904-1908`) and the
   preview VFX (`:1911-1938`) do not affect rules. `NeowsTalisman.AfterObtained` takes the
   deck's Basic-rarity cards (`Models.Relics/NeowsTalisman.cs:20`), picks the
   `LastOrDefault` with the Strike tag and the `LastOrDefault` with the Defend tag (`:23-24`), and
   upgrades each one that is not null (`:32-35`). The cards stay where they are in the deck.
2. Loading a run from a save (save & quit retries restart from the room-entry save) rebuilds
   each deck card with `CardModel.FromSerializable` (`Models/CardModel.cs:3623-3647`). It starts
   from the canonical card (`ToMutable`, `:3625`) and calls `UpgradeInternal()` then
   `FinalizeUpgradeInternal()` once per saved `CurrentUpgradeLevel` (`:3640-3644`).
   `ToSerializable` writes `CurrentUpgradeLevel` (`:3610-3621`).
3. At combat start, `Player.PopulateCombatState` (`Entities.Players/Player.cs:1214-1234`) clones
   every deck card in deck order with `state.CloneCard(current)` (`:1224`) and adds it to the
   draw pile (`:1226`). Then `RandomizeOrderInternal` shuffles the pile (`:1233`).
   `CombatState.CloneCard` (`Combat/CombatState.cs:679-684`) calls `ClonePreservingMutability`,
   which calls `MutableClone` (`Models/AbstractModel.cs:155-171`). `MemberwiseClone` keeps
   `_currentUpgradeLevel`, and `CardModel.DeepCloneFields` (`Models/CardModel.cs:2827-2859`)
   clones the vars and the energy cost:
   - `DynamicVar.Clone` is `MemberwiseClone` (`Localization.DynamicVars/DynamicVar.cs:110-115`);
     its `ResetToBase` only resets the UI preview values.
   - `CardEnergyCost.Clone` copies `_base`, `_capturedXValue` and `WasJustUpgraded`
     (`Entities.Cards/CardEnergyCost.cs:264-274`).

Every path gives the same result: the canonical card plus, for each upgrade level, one
`UpgradeInternal` and one `FinalizeUpgradeInternal`. The sim builds the card the same way.
`CardInstance.Create(cs, spec)` resolves the def, allocates `cs.AllocateCardInstanceId()`, and
calls `Upgrade()` when `spec.Upgraded`. `Upgrade()` is the per-card body of `CardCmd.Upgrade`
(cards spec §11). Every `WasJustUpgraded` flag ends false in both the game and the sim.
`UpgradeInternal` also calls `DynamicVars.RecalculateForUpgradeOrEnchant`
(`Models/CardModel.cs:3524`; `Localization.DynamicVars/DynamicVarSet.cs:186-201`). That call only
touches `CalculatedVar`s, and these cards have none.

### 2.3 Invalid specs

These checks run in `CardInstance.ResolveDef`, before an instance id is allocated:

| Input | Result | Reason |
| --- | --- | --- |
| `Upgraded` on a card with `MaxUpgradeLevel` 0 (`GREED`, `ASCENDERS_BANE`) | `ArgumentException` | `CardCmd.Upgrade` would skip it, but no deck can hold such a card, so the input is malformed. The sim refuses it instead of guessing. |
| `default(CardSpec)` (null id) | `ArgumentException` | — |
| unknown id | `KeyNotFoundException` | from `CardRegistry.Get` |

### 2.4 Upgrades and shuffles

- The opening shuffle never compares cards: it is `UnstableShuffle` over the draw pile (loop
  spec). Its result depends only on the deck size and order. An upgraded deck therefore opens with
  exactly the same ids in the same places as the same ids unupgraded, and the Shuffle stream uses
  `deck size − 1` draws either way.
- A reshuffle sorts before it shuffles (`StableShuffle`). It compares cards with
  `CardModel.CompareTo` (`Models/CardModel.cs:3649-3671`): first by id
  (`Models/AbstractModel.cs:126-137`), then by `CurrentUpgradeLevel`. So upgrades do change the
  order after a reshuffle. The loop's `CardPileCmd.CompareForShuffle` already compares
  `UpgradeLevel`, and the reshuffle test in §6 runs that path with upgraded cards.

## 3. `CardSpec` and the new `Create` overload (diff-bundle contract)

- `Sts2Sim.Core.CardSpec` (`Api/CardSpec.cs`) is `readonly record struct CardSpec(string Id, bool Upgraded)`.
  - `ToString()` returns `Id`, plus `"+"` when upgraded. This is the `+` encoding that
    `diff-bundle.md` uses in `draw_multiset`, `discard` and `exhaust`.
  - `Parse(text)` is the inverse. It accepts one trailing `+`, and throws `FormatException` for
    a bare `+` or for `++`.
  - `FromIds(ids)` returns an array of unupgraded specs, one per id.
- `CombatSim.Create(RunRngSet rng, IReadOnlyList<CardSpec> deck, string encounterId, int ascension, int? currentHp, int? maxHp)`
  has the exact signature from `diff-bundle.md` and no default arguments. It works on a deep copy
  of `rng` and never changes the caller's streams. The deck is used verbatim:
  - Instance ids are allocated in deck order.
  - `Upgraded` is applied when each card is created, which is before the opening shuffle.
  - **The starter-deck ascension rule does not apply** (CONTRACTS §4.2). A spec deck is the
    run's whole deck: a bundle deck at A5+ already contains `ASCENDERS_BANE`. Passing the bare
    10-card starter as specs at A10 gives a 10-card combat.
- The old string overloads now map to unupgraded specs.
  `CombatLoop.SetUpCombat(rng, deckIds, …)` returns
  `SetUpCombat(rng, CardSpec.FromIds(DeckForCombat(deckIds, ascension)), …)`. The starter-deck rule
  stays on the string path, so the old overloads build the same states as before.
  `CombatLoop` also gained `CreateCombat(RunRngSet, IReadOnlyList<CardSpec>, …, populateMonsters = null)`
  and `SetUpCombat(RunRngSet, IReadOnlyList<CardSpec>, …, populateMonsters = null)`. HP defaults
  and checks are unchanged: `maxHp ?? 80`, `currentHp ?? maxHp`, and `currentHp` in `1..maxHp`.

## 4. GREED (`Models.Cards/Greed.cs`)

| Field | Value | Line |
| --- | --- | --- |
| class | `public class Greed : CardModel` (not sealed in the game; the sim def is sealed like every def) | `:6` |
| cost / type / rarity / target | −1 / Curse / Curse / None | `:18-21` |
| keywords (canonical order) | Eternal, Unplayable (explicit array literal, no IL read needed) | `:12-16` |
| `MaxUpgradeLevel` | 0 | `:8` |
| `CanBeGeneratedByModifiers` | false | `:10` |
| `CanBeGeneratedInCombat` | true (not overridden; default at `Models/CardModel.cs:2043`), unlike `ASCENDERS_BANE` | — |
| tags, vars, star cost, `OnPlay`, `OnUpgrade`, `OnTurnEndInHand`, hooks | none. The default `OnPlay` does nothing (`Models/CardModel.cs:3290-3293`), and `HasTurnEndInHandEffect` is false (`:2377`) | — |

Id: `Slugify("Greed")` = `GREED` (`Models/ModelDb.cs:485-488`).

`grep -w Greed` finds only these references:

- `Models.Relics/CursedPearl.cs:62`: `AfterObtained` calls `CardPileCmd.AddCurseToDeck<Greed>`,
  then `PlayerCmd.GainGold` (`:78`) with `GoldVar(333)` (`:111`).
- `Models.Events/SunkenTreasury.cs:142`.
- `Models.CardPools/CurseCardPool.cs:31`.
- The type table at `Models/AbstractModelSubtypes.cs:908`.

Nothing hooks GREED in combat, so all of its combat behaviour comes from generic rules:

| Rule | Result | Source |
| --- | --- | --- |
| Playability | The `Unplayable` keyword makes `CanPlay` false with `HasUnplayableKeyword` (`Models/CardModel.cs:3330-3360`, cards spec §8), so GREED is never in the legal actions | |
| End of turn | GREED is not Ethereal and has no turn-end effect, so it is flushed to the discard pile with the hand. `ASCENDERS_BANE` is Ethereal and is exhausted | loop spec |
| Cost | −1. `GetAmountToSpend` and `GetResolved` return `Max(0, …)` = 0 (`Entities.Cards/CardEnergyCost.cs:116-132`), so it can never be too expensive | |
| Eternal | `IsRemovable` is false (`Models/CardModel.cs:2100`). This only matters outside combat | |
| Deck position | `AddCurseToDeck<T>` calls `AddCursesToDeck`, which runs `RunState.CreateCard` then `Add(card, PileType.Deck)` with the default `CardPilePosition.Bottom` (`Commands/CardPileCmd.cs:4151-4156`, `:4159-4184`, `:3251`). So GREED is appended to the deck, which is the exporter's gain order | |

**Recorder cross-check (for the projection, not a rule).** The recorder writes a hand card's cost
as the string of `EnergyCost.GetAmountToSpend()`, or `"X"` for X-cost cards
(`mod/src/StateBuilder/PassiveStateBuilder.Player.cs:162-163`). In the corpus, GREED hand
entries read cost `"0"`, `can_play` false, `unplayable_reason` `HasUnplayableKeyword`, with keyword
tips Eternal then Unplayable (`1785123719-L9EFT8UJ5J-part2/states.jsonl`). The sim's matching
value is `card.EnergyCost.GetAmountToSpend(cs, card)`, which gives 0 for GREED and
`ASCENDERS_BANE`. `Base` and `Canonical` are −1 and must not be used for the projection.

## 5. Dropped or not ported

| Item | Reason |
| --- | --- |
| Upgrade preview and VFX, the `Upgraded` event, the map-history `UpgradedCards` entry (`Commands/CardCmd.cs:1904-1908`, `:1911-1938`) | UI and run history only |
| `DowngradeInternal` (`Models/CardModel.cs:3539-3556`) | No ported card downgrades |
| Upgrade levels above 1 | `CardSpec` cannot express them, and no v0.111.0 card has one (§2.1) |

## 6. Tests (`CardUpgradeTests.cs`)

- `UpgradedValuesMatchTheModelUpgrades`: the constants 9 / 8 / 10 / 3. `CardInstance.Create(cs, spec)`
  gives the upgraded vars, level 1, not upgradable, `Spec` round trip, unchanged cost (1 / 1 / 2),
  every `WasJustUpgraded` false, and sequential instance ids.
- `UpgradeIsPartOfTheCanonicalCard`: a spec-built card has the same canonical bytes as
  `UpgradeInternal` + `FinalizeUpgradeInternal`, which is the save-load path. Its hash differs from
  the unupgraded card's, and a clone keeps both the hash and the flag.
- `CardSpecParsesAndFormatsTheUpgradeSuffix`: `Parse`, `ToString`, `FromIds`, and the bad inputs.
- `InvalidSpecsAreRejectedBeforeAnIdIsAllocated`: `GREED+`, `ASCENDERS_BANE+`, `default`, and an
  unknown id are rejected, and no instance id is consumed. Null arguments and bad HP throw from the
  overload.
- `SpecDeckKeepsDeckOrderAndTheIdDeckShuffle`: a `NEOWS_TALISMAN`-style A10 deck, with the last
  Strike and the last Defend upgraded and the Bane appended, over 30 seeds:
  - the same ids sit in the same places in every pile as with the string starter deck;
  - the RNG streams are byte-identical;
  - the legal actions are identical;
  - instance `i` is deck entry `i`;
  - the state hash differs, a clone has the same hash, and a rebuild has the same hash;
  - the caller's `RunRngSet` is unchanged.
- `IdOverloadsEqualAllUnupgradedSpecDecks`: at A0, A2, A4, A5 and A10, the string overloads and
  unupgraded spec decks give equal hashes at every step of a random-policy combat. The default HP
  is the same in both. The spec overload does not append the Bane.
- `UpgradedStarterCardsHitHarderInCombat`: twin combats with the same seed, one with every starter
  card upgraded and one without:
  - Bash+ deals 10 (Bash 8) and applies Vulnerable 3 (2), which ticks down to 2 (1);
  - Strike+ into Vulnerable deals `(int)(9 × 1.5)` = 13 (Strike 9);
  - Defend+ gains 8 block (Defend 5);
  - the costs are unchanged.
- `UpgradedDeckReplaysDeterministicallyThroughReshuffles`: three upgraded cards over 25 seeds:
  - a clone stepped at every decision reaches the same hash;
  - no card is lost, and the upgrades stay on their instances;
  - some seeds reshuffle;
  - a second run repeats every hash.
- `GreedDefinition`: every def field from §4, the keyword mask, not removable, `Upgrade()` is a
  no-op, cost −1 spends 0, `CanPlay` is false with exactly `HasUnplayableKeyword` even at 0 energy,
  and the result pile is the discard pile.
- `GreedIsDeadWeightInCombat`: an A10 deck of starter + Bane + `GREED`. When GREED and the Bane
  open together:
  - GREED is never legal, and an attempt to play it throws and leaves the hash unchanged;
  - the opening shuffle uses 11 draws;
  - at end of turn GREED is discarded and the Bane is exhausted.
