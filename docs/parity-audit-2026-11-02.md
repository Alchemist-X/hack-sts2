# UI parity audit: what a human sees vs. what the LLM and the NN see (2026-11-02)

User criterion 1.2 (docs/HANDOVER.md §1): the LLM receives the same information as a human player (attack intents,
visible and hidden information, card and relic effects). This audit checks every screen element of the game's UI
code against the four observation channels and fixes the gaps.

## 1. Verdict

- **No leak remains** that the audit or its regression suite can find. One leak was found and fixed (P0): the hidden
  power AMBERGRIS_POWER was shown by the play server (JSON, text, step log).
- **Every element a human sees is now in the play server's JSON and text, and in the compact LLM text**, with four
  documented, deliberate exceptions (section 5). Elements added in this wave: event option values (39 option keys
  in 17 events rendered `?`), event page text, the act's boss(es) and Ancient, map quest marks, ascension in the
  header, the three combat piles in the text, orb passive/evoke numbers in the text, rest-site option descriptions,
  upgrade previews in upgrade prompts, enchantment/affliction marks on deck and card-reward lines, game-exact power
  icon numbers (SLOTH, AUTOMATION, FERAL, ORBIT, PANACHE, PALE_BLUE_DOT, WITHERING_PRESENCE, MONOLOGUE).
- **The Vec encoder** already carried most elements (boss ids, quests, orb numbers, pile multisets, icon numbers);
  it has no leak, agrees with the server on every element both carry (checked on 1,500 sampled states), and lacks a
  short list of human-visible details that need a schema append (section 5.2). The schema and `encoder_version`
  (`fc2eb34c07bc8513`) are unchanged in this wave.
- **Known top of the draw pile**: the game never displays it (section 4). No field was added.

Channels: **JSON** = the play server's observation object (`sim/Sts2Sim.Server/ObservationBuilder*.cs`); **Text** =
the server's `text` rendering (protocol "full" mode); **Compact** = the LLM runner's compact text
(`sim/tools/eval/sts2eval/compact.py`, renderer `compact-v2`); **Vec** = the NN encoder
(`sim/Sts2Sim.Vec/ObservationEncoder*.cs`, schema in `sim/VEC.md`).

Rules truth and UI reference: the decompile at `output/server-bootstrap/linux-decompiled/` (citations below are
relative to it; `Nodes.*` = `MegaCrit.Sts2.Core.Nodes.*`).

## 2. Method

1. Read the UI node code for every screen (combat, pile screens, card selection screens, map, top bar, merchant,
   rest site, treasure, rewards, events) and the models the UI renders (intents, powers, relics, orbs, card preview).
2. For each element, located the field in each channel and classified it: **parity**, **missing** (shown to a human,
   absent from the channel), **extra = leak** (in the channel, not shown to a human).
3. Probed systematically instead of sampling only: a walk over every registered event (3 seeds, every page reachable
   within 3 choices: 61/64 events, 206 option keys, 509 page texts) and a static scan of every event option template's
   placeholders against the event classes (`{Var}` with no matching member). The handover's "7/81 options render `?`"
   was an undercount: the walk found 33 option keys, the static scan 6 more in 4 events not reachable standalone (39 in 17 events).
4. Fixed, then encoded the checklist as a regression suite (section 6).

## 3. Element table

Status: P = parity, M = missing (now fixed unless marked open), L = leak (fixed), — = not applicable.

### 3.1 Run header and top bar

| Element | What the human sees (citation) | JSON | Text | Compact | Vec | Status |
| --- | --- | --- | --- | --- | --- | --- |
| HP / max HP, gold | `TopBar/NTopBarHp.cs`, `NTopBarGold.cs` | `player.hp/max_hp/gold` | header | header | `global_i.hp/max_hp/gold` | P |
| Act / floor | `TopBar/NTopBarFloorIcon.cs:49` floor label | `act`, `floor` | header | header | `act_index`, `total_floor`, `act_floor` | P |
| Ascension | `TopBar/NTopBarPortraitTip.cs:55-60` (ascension hover tip) | `ascension` **(new)** | header `\| ascension N` **(new)** | run context `ascension N` | `ascension` | M → fixed (server) |
| Boss of the act | `TopBar/NTopBarBossIcon.cs:81-160`, `Screens.Map/NBossMapPoint.cs:122` (encounter art, second boss at A10) | `bosses[{id,name}]` **(new)** | `boss: ID (Name)[, then ...]` **(new)** | run-context block 3 | `boss_id`, `second_boss_id` | M → fixed (server, compact) |
| Ancient of the act | `Screens.Map/NAncientMapPoint.cs:124` (`Act.Ancient.MapIcon`) | `ancient{id,name}` **(new)** | `\| ancient: ID (Name)` **(new)** | same line | — | M → fixed (server, compact); **open in Vec** |
| Energy, stars | `Combat/NEnergyCounter.cs`, `Combat/NStarCounter.cs` | `player.energy/max_energy/stars` | header | header | `energy`, `max_energy`, `stars` | P |
| Potion slots and potions | `Nodes.Potions/NPotionContainer.cs`, hover text | `player.potions[]` (+ name/text) | `potions:` | run-context block 3 | `potions[]` (+usable, target) | P |
| Relics, counters | `Nodes.Relics/NRelicInventoryHolder.cs:364` (`DisplayAmountChanged`) | `relics[{id,name,text,counter}]` | `relics:` with `ID(n)` | context + `relic counters:` | `relics[]` (counter = `DisplayAmount`) | P |
| Deck (deck view) | `Screens/NDeckViewScreen.cs` / `NCardsViewScreen.cs` (cards with enchantment/affliction marks) | `player.deck` ids; `/piles` nodes | `deck(N):` **now with `[ench X n]` / `[AFFLICTION]` marks** | deck block (from `/piles`) | `deck[]` (id, upgrade, enchant, affliction) | M (text marks) → fixed |
| Deck view "show upgrades" toggle | `Screens/NCardsViewScreen.cs:129,173` | `/catalog`, `/piles` | — | — | vocab (static) | **open** (5.1) |

### 3.2 Combat

| Element | What the human sees (citation) | JSON | Text | Compact | Vec | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Intent type | `Combat/NCreature.cs:1011-1033` (`NextMove.Intents`), `MonsterMoves.Intents/*` (Attack, MultiAttack, DeathBlow, Defend, Buff, Debuff, CardDebuff, StatusCard, Summon, Heal, Escape, Sleep, Stun, Unknown; Hidden has no tip and no sprite) | `intents[].type` | `intent: Attack 6 + Buff` | same | `enemy_intents.type` | P |
| Intent damage / hits | `Intents/AttackIntent.cs:92-101` (`GetSingleDamage` through `Hook.ModifyDamage`), `MultiAttackIntent.cs` (`Repeats`) | `damage`, `times` | `Attack 5x3` | same | `damage`, `times` | P |
| Status-card count | `Intents/StatusIntent.cs` (`CardCount`) | `card_count` | `StatusCard(2 cards)` | same | `card_count` | P |
| Move identity | never shown (intent tip = type title + description) | `move`/`move_name` null (debug flag `reveal_moves` only) | none | none | none | P (no leak; existing tests) |
| Enemy HP / block, flank | `Combat/NHealthBar.cs`, `NCreatureStateDisplay.cs` | `enemies[]` | enemy line | same | `enemies[]` | P |
| Visible power set | `Models/PowerModel.cs:161-173` (`IsVisible` = `IsVisibleInternal` and target rule) | was: strict check threw for AMBERGRIS, catch showed it → **now `PlayerView.IsVisibleToPlayer`** (model's own `IsVisible`) | same | same | `IsVisibleToPlayer` | **L → fixed** |
| Power icon number | `Combat/NPower.cs:199` (`DisplayAmount`, only for `StackType.Counter`) | was: projection rule, raw amount + `amount_raw` for 10 overrides → **now `PlayerView.DisplayAmountToPlayer`** (shared with Vec) | `ID n [detail]` | same | `amount` | M (wrong numbers for SLOTH etc.) → fixed |
| Power description | `PowerModel.HoverTips` (smart description with `{Amount}`) | `text` | indented `ID — text` | once per step | id (vocab) | P |
| Card cost, base cost, star cost | `Nodes.Cards/NCard.cs` | `cost`, `base_cost`, `star_cost` | `1e (base 2e) 2*` | same | `cost_current`, `cost_base` | P |
| Card preview numbers | `Localization.DynamicVars/DynamicVar.cs:95` (`UpdateCardPreview`) | `effective{damage per uid, times, block}` | `(eff ...)` | same | `hand_f`, `hand_dmg` | P |
| Upgrades, enchantments, afflictions, dampened | `Nodes.Cards/NCard.cs:600-603` (enchantment tab), affliction overlay | `upgraded`, `enchantments`, `affliction`, `dampened` | `+`, `[ench X n]`, `[BOUND]`, `[dampened]` | same | upgrade, enchant id/amount, affliction id | P (Vec: no affliction amount / dampened flag, open 5.2) |
| Draw pile contents | `Screens/NCardPileScreen.cs:185-187` (sorted by rarity, then id: order hidden) | `draw_pile` (sorted keys), **`pile_view.draw` (new)**; `/piles` | **`draw pile (sorted): ...` (new)** | same | `draw_counts` (multiset) | M (text) → fixed |
| Discard pile contents and order | `NCardPileScreen.cs:190-197` (`SortingOrders.Ascending` = pile order) | `discard_pile`, `pile_view.discard` | **`discard pile (oldest first): ...` (new)** | same | `discard_counts` (no order) | M (text) → fixed; Vec order open (5.2) |
| Exhaust pile contents and order | same screen | **`exhaust_pile`, `pile_view.exhaust` (new)** | **`exhaust pile (oldest first): ...` (new)** | same | `exhaust_counts` | M → fixed; Vec order open |
| Pile counts | `Combat/NCombatCardPile.cs:188` | `*_count` | hand header | same | `draw_n` ... | P |
| Known top of draw pile | not displayed anywhere (section 4) | — | — | — | — | P (nothing to show) |
| Orbs | `Nodes.Orbs/NOrb.cs:127-128,213-214` (passive and evoke labels) | `orbs[{id,passive,evoke}]` | was ids only → **`orbs (3 slots; passive/evoke): LIGHTNING_ORB 3/8`** | same | `orbs[]` | M (text) → fixed |
| Stars / forge (Regent) | `NStarCounter`; forge shows as Sovereign Blade damage on the card | `stars`; card `vars` | header; card line | same | `stars`; card vars | P |
| Pets / allies (Osty) | `Combat/NCreature.cs` | `allies[]` | `ally [k] ...` | same | `allies[]` | P |
| Incoming damage summary | derived (sum of shown intents) | `danger` | `incoming:` | same | `incoming_damage/hits` | P (derived) |

### 3.3 Map, rooms, screens

| Element | What the human sees (citation) | JSON | Text | Compact | Vec | Status |
| --- | --- | --- | --- | --- | --- | --- |
| Map nodes, edges, visited, current | `Screens.Map/NMapScreen.cs`, `NNormalMapPoint.cs` | `map.nodes[]` | act map grid | same | `map_nodes` | P |
| Quest marks | `Screens.Map/NNormalMapPoint.cs:240` (`%QuestIcon` visible when `Point.Quests` non-empty) | **`quest` (new)** | **`!` after the room letter (new)** | same | `map_nodes.quest` | M → fixed (server, compact) |
| Legal next rooms | map highlight | `map.options` | `-- map: next rooms` | same | `legal_next` | P |
| Shop prices, sale, removal price | `Screens.Shops/NMerchantSlot.cs:468`, `NMerchantCardRemoval.cs:181` | `shop.entries[]`, `removal_price` | shop lines | same | `shop[]`, `removal_price` | P (`base_price` is reconstructed, documented) |
| Rest options | `RestSite/NRestSiteButton.cs:400` (title), `:530` (description), `Entities.RestSite/HealRestSiteOption.cs:286-307` (heal amount), `LiftRestSiteOption.cs:90-99` (lifts left) | `rest.options[].text` **(new)** | ` — Heal 24 HP ...` **(new)** | same | option id, enabled | M → fixed (server, compact); Vec: amounts derivable |
| Treasure | `Nodes.TreasureRooms/NTreasureButton.cs` (relic shown after opening) | relic only when `chest_opened` | `-- treasure` | same | `treasure_relic_id` only when opened | P |
| Rewards | `Screens/NRewardsScreen.cs` | `rewards[]` | `-- rewards` | same | `rewards[]` | P |
| Card reward | `Screens.CardSelection/NCardRewardSelectionScreen.cs` | `card_reward.cards[]` | card lines **now with enchantment/affliction marks** | same | `card_reward[]` | M (marks) → fixed |
| Event page text | `Nodes.Events/NEventLayout.cs:276` (`%EventDescription`) | **`event.page_text` (new)** | **indented line under the event header (new)** | same | — (flavour; event id + page) | M → fixed (server, compact) |
| Event option text and values | `Nodes.Events/NEventOptionButton.cs` (title + description with the event's DynamicVars) | `options[].text` | option lines | same | key hash, enabled, locked, relic id | M (39 keys rendered `?`) → fixed (server, compact); Vec values open (5.2) |
| Prompts: options, min/max, confirm/cancel | `Screens.CardSelection/NCardGridSelectionScreen.cs`, `NChooseACardSelectionScreen.cs`, `NSimpleCardSelectScreen.cs` | `prompt{...}` | `-- prompt` | same | `prompt_*`, `prompt_options[]` | P |
| Draw-pile prompt option order | `NCombatPileCardSelectScreen.cs:411-432` (Draw: rarity, then title) and `Nodes.Cards/NCardGrid.cs:1106-1131` (tie-break by id, `List.Sort`) | `CardSelectCmd.SortByRarityThenId` (ties by pile index) | same | same | `prompt_options` ordinal | P (5.3) |
| Upgrade preview in upgrade prompts | `Screens.CardSelection/NDeckUpgradeSelectScreen.cs:87-107` (`NUpgradePreview`) | **`options[].upgrade_text` (new)** | **`     upgraded: ...` (new)** | same | — (upgrade + vocab) | M → fixed (server, compact) |

## 4. "Known top of the draw pile" (task 2b)

The game never shows draw order: the draw pile screen sorts its cards (`Screens/NCardPileScreen.cs:185-187`, rarity
then id; the grid then keeps that order), the draw-pile selection screen sorts too
(`Screens.CardSelection/NCombatPileCardSelectScreen.cs:411-432`), and no relic, power or node renders the top card
(searched the decompile for draw-order reveals: none; `Combat/NDrawPileButton.cs` shows only the count). A human knows
the top card only from memory of an action it took (Headbutt-style "put a card on top": the human picked the card).
The LLM has the same memory channel: the prompt it answered and its recent history. So no `combat.draw_known_top`
field and no Vec field were added; adding one would give the LLM/NN a perfect memory a human does not get from the
screen. A memoryless NN policy cannot use that knowledge; a recurrent one can.

## 5. Remaining differences (documented, not fixed in this wave)

### 5.1 Server / compact

1. **Deck view "show upgrades"**: a human can toggle upgraded versions of all deck cards at any time. The LLM sees
   upgraded text only in upgrade prompts (new) and through `/catalog`. Adding every deck card's upgraded text to the
   (cached) deck block is a cheap follow-up if wanted.
2. **Flavour placeholders**: 3 event page texts keep `?` for values from the `characters` localisation table, which
   `knowledge/source` does not export (`{pronounObject}`/`{pronounSubject}` in REFLECTIONS.SHATTER, `{Monologue}` in
   SUNKEN_TREASURY.SECOND_CHEST) or for the dish just eaten (ENDLESS_CONVEYOR, `{LastDishTitle}`, which is the option
   the player picked). No decision content.
3. **Sim-worded texts**: enchantment titles are the id in title case (`PERFECT_FIT` → "Perfect Fit"; no enchantment
   localisation is exported) and rest-site descriptions are the sim's wording with the game's numbers (the
   `rest_site_ui` table is not exported). Extending `scripts/export_sts2_localization.py` to these tables would make
   them verbatim.
4. **Upgrade preview** uses the card's base vars at the next upgrade level, not the live instance (an enchantment's
   bonus is not added to the preview number; the current text shows it).
5. **Unported events** (CRYSTAL_SPHERE, FAKE_MERCHANT, ... run as `SimStubEvent`, `Run/State/ActInstance.cs:161`)
   have no option values to show; their texts are out of scope until they are ported.

### 5.2 Vec (needs a schema append; deferred so this wave keeps `encoder_version`)

| Human-visible detail | Vec today | Proposed append |
| --- | --- | --- |
| Event option values (gold, HP loss, the card/relic/potion an option names, e.g. RELIC_TRADER's trade pair) | key hash + relic id | `event_options`: `value_0`, `value_1` (numbers), `item_kind`, `item_id` |
| The act's Ancient | — | `global_i.ancient_id` (events vocab) |
| Discard / exhaust pile order | counts | optional `discard_order[CAP]` card ids (rarely decision-relevant) |
| Enchantment / affliction of pile cards (non-deck: generated cards, BOUND in draw) | counts by (id, upgrade) | per-pile `(enchant, affliction)` count channels |
| Affliction amount, `dampened` on hand cards | affliction id only | hand: `affliction_amount`, `dampened` |

Everything else Vec lacks is static per id (card text, power/relic descriptions) and learnable from the vocab ids,
or derived (rest heal = 30 % max HP after relics).

### 5.3 Where the sim shows less than the game (by choice)

- **Tie order in sorted pile views.** The game sorts with `List.Sort`, which is an insertion sort (stable) for up to
  16 cards, so in the game's draw-pile screen copies with the same id (e.g. an enchanted and a plain Strike) keep
  their relative draw order. The sim's draw-pile text and `/piles` sort ties by their whole visible node, so the sim
  reveals nothing there. The draw-pile *selection prompt* keeps the kernel's ported order
  (`CardSelectCmd.SortByRarityThenId`, ties by pile index, the same as the game for small piles) because option
  indices are part of the answer tape and the differential gates.
- **Non-counter power icons** (`StackType.Single`/`None`) show no number in the game (`NPower.cs:199`); the JSON still
  carries `amount`. Only REATTACH (its hover text shows the amount) and HEX (its amount is the HEXED affliction shown
  on the cards) use the amount, so nothing hidden is exposed.

## 6. Fixes in this wave

| Fix | Files |
| --- | --- |
| P0 leak: hidden powers (AMBERGRIS) shown | `Sts2Sim.Core/Combat/Pipeline/PlayerView.cs` (`IsVisibleToPlayer` reads the model's `IsVisible`), `Sts2Sim.Server/Describe.cs` (`IsShown`, fail closed; `IsHiddenPowerId` for the step log), `StepLog.cs` |
| Game-exact power icon numbers in the server | `Describe.DisplayAmount(cs, power, out known)` → `PlayerView.DisplayAmountToPlayer` (shared with Vec) |
| Event option values (39 keys, 17 events) | read-only accessors on Amalgamator, BattlewornDummy, Bugslayer, ColossalFlower, DenseVegetation (`HealFor(RunState)`), FieldOfManSizedHoles, GraveOfTheForgotten, RelicTrader, SapphireSeed, SelfHelpBook, SlipperyBridge, StoneOfAllTime, Symbiote, TeaMaster, TinkerTime, WelcomeToWongos, WoodCarvings; `RulesText` `EventScope` (`<Name>For(RunState)` vars, `<Relic>Description` vars → relic text, enchantment titles) |
| Event page text | `RulesText.EventPageText`, `event.page_text` |
| Bosses, Ancient, ascension, quests, piles, orb numbers, rest text, card-reward / deck marks, upgrade preview | `ObservationBuilder.Parity.cs` (new), `ObservationBuilder*.cs`, `compact.py` (`compact-v2`) |
| `/piles` draw ties | `ObservationBuilder.Cards.cs` (`SortedDrawNodes`: id, upgrade, whole node) |
| Docs | `sim/PLAY.md` (Observation table, Hidden information) |

No kernel rule changed: the Core edits are a visibility helper and read-only event accessors (no state, no RNG), so
G0 (4/4 bundles, 69/69 decisions), the F2 hash pins and the F7 clone-then-step soak are unchanged.

## 7. Regression suite

`dotnet run -c Release --project sim/Sts2Sim.Tests -- ParityAudit` (`sim/Sts2Sim.Tests/ParityAuditTests.cs`):

- `EventOptionTextsResolved`: the event walk (61/64 events, 206 option keys, 509 page texts); no option renders an
  unresolved `?`; page texts only on the flavour allowlist of 5.1.2.
- `EventValuesNeedingSetup`: the four events the walk cannot reach (RELIC_TRADER with six tradable relics names both
  relics of each trade, STONE_OF_ALL_TIME the potion it takes, WELCOME_TO_WONGOS the featured relic,
  GRAVE_OF_THE_FORGOTTEN its enchantment).
- `HiddenPowerNeverShown`: AMBERGRIS applied to the player is absent from the observation, the step log and the Vec
  power table.
- `ServerDrawOrderHidden`: 250 combat states with a reshuffled draw pile give a byte-identical observation (JSON,
  text, legal actions) and `/piles`.
- `SampledStatesChecklist`: 1,500 states of 45 random runs (all characters, A0 and A10; 873 combat, 268 map, 111
  event, 83 rest, 54 shop, 42 treasure, 65 with orbs, upgrade prompts) assert the section-3 checklist: header
  (ascension, HP), bosses and Ancient (JSON = text = Vec), relic counters and potions (= Vec), map nodes with quest
  marks (= Vec per cell), visible power sets and icon numbers for the player and every enemy (= Vec), intents
  without move identity, orb passive/evoke (JSON = text = Vec), pile views (sorted draw, discard in pile order, counts
  = header = Vec), rest descriptions (ids = Vec), event option texts resolved (count = Vec), shop prices / stock / sale
  (= Vec), treasure relic hidden until opened, upgrade previews.

The compact channel is covered by the runner's own parity checker (`compact.parity_problems`: every id, number,
word, rules text and line of the server text must reach the compact text): `test_parity_live_random_games` in the
runner's pytest suite, and a 20-game run of the same checker: 2,446 observations, 0 failures.

## 8. Cost of the additions

Measured over 20 random runs (2,407 observations, 1,696 in combat): the new lines add 185 characters per observation
to the server text (12.5 %: pile lines 112, boss/ancient line 61, event page text 7, upgrade preview 2, orb numbers
2). In the compact mode the boss/ancient line sits in the cached run-context block, so the per-step cost is the pile
lines (~115 characters on a ~780-character compact step, ~14 %).

## 9. Notes for the integrator and the leaderboard

- The full-mode prompt builder and its hash (`e01daedf52519a6c`) are unchanged, but the server `text` it carries now
  has the new lines; the compact renderer is `compact-v2` (new compact prompt hash). Leaderboard rows measured before
  this change (Sonnet 5.5: A0 0.499, A10 0.310) used the pre-audit observation and should be labelled as such.
- The Vec schema, vocab and `encoder_version` are unchanged; the trained smoke policy stays loadable.
