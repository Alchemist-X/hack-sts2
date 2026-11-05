# STS2 environment spec (NN / programmatic policies)

One page for anyone — human or LLM optimizer — who writes or trains a policy against the Slay the Spire 2 simulator
**without reading its code**. The column tables, enum lists and status bits in §5 are generated from the live schema
by `sim/tools/vec/tools/gen_env_spec.py` and checked by the vec test suite, so they cannot drift from the code.
Deeper references: `sim/VEC.md` (encoder internals), `sim/CONTRACTS.md` Part D (frozen contract),
`docs/sts2-bench-protocol-v0.2-2026-11-01.md` (metric and seed sets).

## 1. Task

- **Game**: Slay the Spire 2 v0.111.0 rules, a headless C# re-implementation verified decision-by-decision against the
  real game. One episode = one full run: 3 acts (≈ 48 rooms; 49 at A10), 5 characters, ascension 0–10.
- **Goal**: win the run. **Metric**: protocol v0.2 *progress* (§7), mean over a frozen seed set at A0 or A10.
- **Interface**: `N` games stepped in one native call; observations are fixed-shape numpy tables; an action is an
  **index into the current list of legal candidates** (`cand`), so a policy can never pick an illegal move by design.

## 2. Quick start

```bash
dotnet build sim/Sts2Sim.sln -c Release                     # once (needs the .NET 9 SDK)
uv run --project sim/tools/vec sts2vec check                # library, schema, determinism
uv run --project sim/tools/vec sts2vec eval --policy heuristic --seedset std-a0-v1      # protocol score of a policy
uv run --project sim/tools/vec sts2vec eval --policy my_policy.py:act --seedset std-a10-v1 --out tmp/eval/mine
```

A policy is any `act(obs, ctx) -> int array (N,)`; template: `sim/tools/vec/examples/policy_template.py`
(README "Write your own policy in 20 lines").

## 3. Episode loop

```python
from sts2vec import VecEnv, PoolSpec, STATUS
with VecEnv(1024, threads=8, step_cap=3000, floor_step_cap=80, auto_reset=True, seed_text="VEC",
            pool=PoolSpec(characters=("IRONCLAD", "SILENT"), ascension=10, count=4096)) as env:
    obs = env.reset()                                     # dict: buffer name -> (N, *shape) numpy view
    while True:
        a = my_policy(obs)                                # int32 (N,): row of obs["cand"][i], -1 = no-op
        obs, reward, terminated, truncated, status, info = env.step(a)
```

- `reset(mask=None, start_ids=None)` restarts the masked slots (None = all) from pool entry `start_ids[i]` (None or < 0
  = the slot's deterministic start sequence). Other slots are untouched.
- `step(actions)`: `a < 0` = no-op (`NOOP`); `a` outside `[0, cand_n)` = `ILLEGAL_ACTION`, slot unchanged; otherwise the
  chosen action runs and every automatic effect up to the **next decision** resolves (enemy turns, draws, triggers).
- **Terminal**: `terminated = 1` at Victory or Defeat; `reward = 1` only at Victory, 0 on every other step (sparse,
  undiscounted). **Truncation**: `truncated = 1` when `step_index` reaches `step_cap`, on a floor stall (below), or when
  the slot hit a `CONTENT_GAP` / `SIM_ERROR` (the run is then discarded; `env.slot_errors()` gives the text).
- **Floor stall cap** (`floor_step_cap`, default 80 = `sts2vec.FLOOR_STEP_CAP`, 0 = off): the step that makes the
  episode's `floor_step_cap`-th decision at a non-combat point on one floor (`total_floor` unchanged; legal or illegal
  index, no-ops excluded) and leaves it at a non-combat point on that floor truncates it with status `FLOOR_STALL` and
  `last_episode.end_kind` FloorStall. Combat decisions never count, so long fights are never cut; a shop
  buy/cancel loop is. Precedence: terminal, then `step_cap`, then the stall cap. This is the protocol's `stuck` limit;
  `sts2vec eval` uses the env's cap, so training and evaluation stop a stall at the same decision.
- **Auto-reset** (default): the step that ends an episode writes `last_episode` and already returns the **next**
  episode's first observation (status `RESET`). Score the finished one from `info["last_episode"]`. With
  `auto_reset=False` a finished slot stays terminal with `cand_n = 0`.
- Arrays returned are views valid until the next call (`copy=True` or copy yourself to keep them).
- Every step returns `status` bits; check them (`SIM_ERROR`/`CONTENT_GAP` should be 0; see the generated table).

## 4. Action

`obs["cand"][i, r]` describes legal action `r` of slot `i` for `r < cand_n[i]` (≤ 192; more legal actions set
`CANDIDATE_OVERFLOW`, only the first 192 are selectable). Rows are in the simulator's legal-action order: the phase's
indexed actions by ascending `A` (then `B`), then potion actions, then the leave / proceed / confirm / cancel action
last; on the rewards screen a card reward is one row per offered card. Decode a row with its columns:

| `kind` | what it does | pointer columns |
| --- | --- | --- |
| PlayCard | play hand card `hand_slot` at enemy `target_slot` (-1 = no enemy target) | `hand_slot`, `target_slot`, `card_id`, `target_uid` |
| EndTurn | end the player turn | — |
| UsePotion / DiscardPotion | use (at `target_slot`) / discard the potion in belt slot `potion_slot` | `potion_slot`, `target_slot` |
| Travel | move to a map node: `option_index` = rank of the destination among the legal nodes sorted by (col, row) | `option_index` |
| ClaimReward / ProceedRewards | claim `rewards` row `option_index` (gold, relic, potion, special card, removal; a potion only while the belt has room) / leave the rewards screen, skipping what is unclaimed | `option_index` |
| TakeRewardCard / TakeRewardAlternative | rewards screen: take card `B` of card reward `A` / choose its alternative `B` (REROLL, SACRIFICE) in one decision, the game's two clicks (open, take); no Skip row (the game's Skip leaves the reward on screen): decline with ProceedRewards | `option_index` (`reward_choices` row), `card_id` |
| PickRewardCard / RewardAlternative | open card screen (after a REROLL, or a reward that allows more picks): take `card_reward` row / choose `card_reward_alts` row (Skip closes the screen) | `option_index`, `card_id` |
| BuyEntry / LeaveShop | buy `shop` row / leave (a potion is listed only when it fits: a free belt slot and no Sozu) | `option_index` |
| ChooseRestOption / LeaveRestSite | `rest` row / leave | `option_index` |
| OpenChest / TakeTreasureRelic / SkipTreasureRelic | treasure room | — |
| ChooseEventOption / LeaveEvent | `event_options` row / leave | `option_index` |
| PromptSelect / PromptConfirm / PromptCancel | pick `prompt_options` row (strictly increasing within one prompt) / confirm / cancel | `option_index`, `card_id` |

Prompts (choose a card to exhaust, upgrade, transform, …) are ordinary decisions: `global_i.decision_kind = Prompt`,
the options are in `prompt_options`, picks accumulate (`picked`), and `PromptConfirm` appears once the selection is
valid. Policies need no special handling beyond reading the candidate kinds.

## 5. Observation

Player-view tables (the human's information, §6). Tables are indexed **by column name** through the schema
(`schema.cols("hand").card_id`, `ctx.cols("cand").kind`), never by hard-coded offsets. Rows past a table's count
are zero; mask by the matching `*_n` column or `id != 0`. Ids index vocabularies (`schema.vocab.cards[id]` → the
game id, e.g. `STRIKE_IRONCLAD`; 0 = none, 1 = unknown); card / relic / power texts per id are in
`knowledge/source/v0.111.0/*.jsonl`. Enum columns hold the enum value. `obs` contains every buffer below except
`reward`, `terminated`, `truncated`, `status`, `episode`, `last_episode`, which `step` returns separately.

<!-- BEGIN GENERATED by sim/tools/vec/tools/gen_env_spec.py from the schema; do not edit by hand -->

Schema: `encoder_version` **`3b73317739c650bd`** (verified: True), schema JSON sha256 `caddf802fff5f221`, 43 buffers, 35,062 bytes per env (34,996 in `obs`). Vocabularies (id 0 = none, 1 = unknown): `cards` 598, `relics` 299, `potions` 66, `powers` 245, `monsters` 113, `encounters` 91, `events` 68, `orbs` 7, `enchantments` 23, `afflictions` 9, `acts` 6, `characters` 7; `card_vars` 133.

| # | buffer | dtype [shape per env] | column(s) | range | meaning |
| --- | --- | --- | --- | --- | --- |
| 0 | `global_i` | int32 [58] — run, room and combat scalars | `phase` | `RunPhase` 0..10 | loop phase (screen) |
| | | | `decision_kind` | `RunPhase` 0..10 | `Prompt` while a choice prompt is pending, else `phase`: dispatch on this |
| | | | `is_combat` | 0/1 | a combat is in progress |
| | | | `character` | `characters` id 0..6 | player character |
| | | | `ascension` | 0..10 | ascension level |
| | | | `act_index`, `act_id` | 0..2 / `acts` id 0..5 | current act |
| | | | `total_floor`, `act_floor` | 0..~50 | rooms entered in the run / in the act (`total_floor` = the protocol's floor) |
| | | | `map_row`, `map_col` | -1 or 0..17 / 0..6 | current map coordinate (-1 before the first move) |
| | | | `hp`, `max_hp`, `gold`, `stars` | >= 0 | player values (stars: Regent resource) |
| | | | `energy`, `max_energy` | >= 0 | energy now (combat only) / max energy (base value outside combat) |
| | | | `block`, `turn`, `round` | >= 0 | combat only: player block, player turn number, combat round |
| | | | `potion_slots`, `orb_slots` | >= 0 | potion belt size; orb capacity |
| | | | `deck_n`, `relic_n` | >= 0 | true counts (tables keep the first CAP rows) |
| | | | `draw_n`, `discard_n`, `exhaust_n`, `hand_n` | >= 0 | combat pile sizes |
| | | | `enemy_n`, `ally_n` | 0..6 / 0..2 | live enemies / live pets |
| | | | `boss_id`, `second_boss_id` | `encounters` id 0..90 | this act's boss (second boss: A10 act 3 only; 0 = none) |
| | | | `card_removals_used`, `removal_price` | >= 0 / -1 | removals bought; next removal price (-1 once sold in this shop) |
| | | | `incoming_damage`, `incoming_hits` | >= 0 | sum of damage x times / times over the live enemies' attack intents |
| | | | `prompt_kind` | `ChoicePromptKind` 0..12 | prompt type; meaningful only when decision_kind = Prompt |
| | | | `prompt_min`, `prompt_max`, `prompt_picked` | >= 0 | selection bounds and picks made so far |
| | | | `prompt_needs_confirm`, `prompt_can_cancel`, `prompt_can_skip`, `prompt_ordered` | 0/1 | prompt flags |
| | | | `prompt_option_n` | >= 0 | number of options (true count; `prompt_options` holds the first 128) |
| | | | `prompt_pile` | `PileType` 0..6 | pile the prompt selects from (0 = none) |
| | | | `prompt_site_hash` | int32 | low 32 bits of XXH64(site id): which card/relic/event opened the prompt |
| | | | `reward_n` | >= 0 | unclaimed rewards on the rewards screen |
| | | | `card_reward_can_skip`, `card_reward_skip_consumes` | 0/1 | card reward: a Skip alternative exists / skipping completes it |
| | | | `treasure_opened`, `treasure_relic_id`, `treasure_claimed` | 0/1, `relics` id 0..298, 0/1/2 | chest opened; its relic (0 until opened); 1 taken, 2 skipped |
| | | | `event_id`, `event_finished`, `event_option_n` | `events` id 0..67, 0/1, >= 0 | current event; finished flag; options while not finished |
| | | | `rest_option_n`, `shop_entry_n` | >= 0 | rest-site options; shop entries |
| 1 | `global_f` | float32 [4] — derived floats | `hp_frac`, `gold_log1p` | [0,1] / >= 0 | hp / max_hp; ln(1 + gold) |
| | | | `floor_frac` | [0,1] | total_floor / 48 (/ 49 at A10) |
| | | | `incoming_net_hp` | float | hp - max(0, incoming_damage - block) |
| 2 | `hand` | int32 [10, 22] — row i = hand card i (rows >= hand_n are 0) | `card_id`, `upgrade` | `cards` id 0..597, >= 0 | card identity and upgrade level |
| | | | `cost_current`, `cost_base` | -2/-1/>= 0 | energy cost now (modifiers applied) / printed; -1 = X cost, -2 = unplayable cost |
| | | | `type`, `target`, `rarity` | `CardType` 0..6, `TargetType` 0..9, `CardRarity` 0..10 | card enums |
| | | | `keyword_mask` | bit 1<<k, k = `CardKeyword` | keywords incl. those granted by other sources |
| | | | `enchant_id`, `enchant_amount`, `affliction_id` | `enchantments` id 0..22, >= 0, `afflictions` id 0..8 | enchantment and affliction |
| | | | `playable`, `playable_reason_id` | 0/1, bits HasUnplayableKeyword=2 BlockedByHook=4 BlockedByCardLogic=8 EnergyCostTooHigh=16 StarCostTooHigh=32 NoLivingAllies=64 | can be played now / why not (bitmask) |
| | | | `hits` | >= 0 | hit count of a damage card (X cost: 0) |
| | | | `var_id_0..3` | `card_vars` id 0..132 | the card's first 4 dynamic variables (names: `schema.card_vars`) |
| | | | `var_val_0..3` | int | their base values |
| 3 | `hand_f` | float32 [10, 2] — row i = hand card i | `block_eff` | >= 0 | block the card would give now (all modifiers) |
| | | | `damage_eff_self_or_all` | >= 0 | damage of a damage card not aimed at one enemy (AoE / random / self) |
| 4 | `hand_dmg` | float32 [10, 6] — row i = hand card i | `vs_enemy_0..5` | >= 0 | damage of the card against the live enemy in `enemies` row j (0 if none / no damage) |
| 5 | `deck` | int32 [96, 5] — master deck in pickup order (deck_n = true count) | `card_id`, `upgrade` | `cards` id 0..597, >= 0 | card identity |
| | | | `enchant_id`, `enchant_amount` | `enchantments` id 0..22, >= 0 | enchantment |
| | | | `affliction_id` | `afflictions` id 0..8 | affliction |
| 6 | `draw_counts` | int16 [1196] — combat only | - | >= 0 | multiset of the draw pile: index card_id * 2 + min(upgrade, 1); ORDER IS HIDDEN |
| 7 | `discard_counts` | int16 [1196] — combat only | - | >= 0 | multiset of the discard pile (same indexing) |
| 8 | `exhaust_counts` | int16 [1196] — combat only | - | >= 0 | multiset of the exhaust pile (same indexing) |
| 9 | `relics` | int32 [32, 5] — relics in pickup order | `relic_id` | `relics` id 0..298 | relic |
| | | | `counter` | -1/>= 0 | counter shown on the relic (-1 = none shown) |
| | | | `status`, `used_up`, `stack` | `RelicStatus` 0..2, 0/1, >= 0 | status; used up; stack count |
| 10 | `potions` | int32 [10, 3] — row = belt slot (empty slot = 0 row) | `potion_id` | `potions` id 0..65 | potion |
| | | | `usable_now` | 0/1 | a UsePotion candidate exists for this slot now |
| | | | `target_type` | `TargetType` 0..9 | what the potion targets |
| 11 | `powers_player` | int32 [24, 3] — visible player powers | `power_id`, `amount` | `powers` id 0..244, int | power and the amount the UI shows |
| | | | `is_debuff` | 0/1 | shown as a debuff at this amount |
| 12 | `orbs` | int32 [10, 3] — orb queue, front first | `orb_id` | `orbs` id 0..6 | orb |
| | | | `passive`, `evoke` | int | current passive / evoke values |
| 13 | `enemies` | int32 [6, 9] — row j = combat creature j (the PlayCard / UsePotion target index); dead = 0 row | `present` | 0/1 | live enemy in this slot |
| | | | `uid` | >= 0 | combat id, stable for the whole combat (= `cand.target_uid`) |
| | | | `monster_id` | `monsters` id 0..112 | monster |
| | | | `hp`, `max_hp`, `block` | >= 0 | current values |
| | | | `flank`, `behind` | 0/1/2, 0/1 | back-attack side (1 left, 2 right); attacks hit the surrounded player's back |
| | | | `intent_n` | 0..3 | intent rows written in `enemy_intents[j]` |
| 14 | `enemy_intents` | int32 [6, 3, 4] — [enemy j][intent k]: the next move's intents as the UI shows them | `type` | `IntentType` 0..14 | intent type (mask rows by intent_n: Attack = 0) |
| | | | `damage`, `times` | >= 0 | per-hit damage after modifiers; hit count |
| | | | `card_count` | >= 0 | cards a status intent adds |
| 15 | `enemy_powers` | int32 [6, 12, 2] — [enemy j][k]: visible powers | `power_id`, `amount` | `powers` id 0..244, int | power and displayed amount |
| 16 | `allies` | int32 [2, 5] — live pets (row a-1 = ally a; UsePotion ally target B = -2 - a) | `present`, `monster_id` | 0/1, `monsters` id 0..112 | live pet and its monster id |
| | | | `hp`, `max_hp`, `block` | >= 0 | current values |
| 17 | `ally_powers` | int32 [2, 8, 2] — [pet][k]: visible powers | `power_id`, `amount` | `powers` id 0..244, int | power and displayed amount |
| 18 | `map_nodes` | int32 [18, 7, 6] — [row][col] of the current act's map (start, rooms, boss rows) | `point_type` | `MapPointType` 0..8 | room type (0 = no node) |
| | | | `visited`, `current` | 0/1 | visited / the current node |
| | | | `legal_next` | 0/1 | a Travel candidate leads here (Map phase) |
| | | | `child_mask` | bitmask | bit c = edge to column c of the next row |
| | | | `quest` | >= 0 | quest marks on the node |
| 19 | `rewards` | int32 [8, 4] — row i = reward i (= ClaimReward A); claimed = 0 row | `reward_type` | `RewardType` 0..6 | reward kind |
| | | | `amount` | >= 0 | gold amount |
| | | | `item_id` | id | potion / relic / special-card id (vocabulary by reward_type) |
| | | | `is_card_reward` | 0/1 | a card reward (its offered cards: `reward_choices`) |
| 20 | `card_reward` | int32 [4, 22] — row i = offered card i (CardReward phase only) | 22 columns of `hand` | as hand | same columns as `hand`; playable, playable_reason_id, hits are 0 |
| 21 | `card_reward_alts` | int32 [2] — row i = alternative i (= RewardAlternative A) | - | `CardRewardAlt` 0..4 | e.g. Skip, REROLL, SACRIFICE |
| 22 | `shop` | int32 [14, 7] — row i = shop entry i (= BuyEntry A) | `category` | `ShopCategory` 0..6 | card, colorless_card, relic, potion, card_removal |
| | | | `item_id`, `upgrade` | id, >= 0 | card / relic / potion id (0 for removal); card upgrade |
| | | | `price` | >= 0 | price now |
| | | | `stocked`, `on_sale`, `affordable` | 0/1 | in stock; the sale card; stocked and price <= gold |
| 23 | `rest` | int32 [9, 2] — row i = rest option i (= ChooseRestOption A) | `option_id` | `RestOption` 0..10 | HEAL, SMITH, ... |
| | | | `enabled` | 0/1 | can be chosen |
| 24 | `event_options` | int32 [8, 6] — row i = event option i (= ChooseEventOption A) | `key_hash`, `ordinal` | int32, >= 0 | low 32 bits of XXH64("<eventId>:<TextKey>"); i |
| | | | `enabled`, `locked`, `proceed` | 0/1 | option flags |
| | | | `relic_id` | `relics` id 0..298 | relic the option offers (0 = none) |
| 25 | `prompt_options` | int32 [128, 5] — row i = prompt option i (= PromptSelect A) | `item_id`, `upgrade` | id, >= 0 | card / relic / potion id (else a hash of the item) |
| | | | `picked` | >= 0 | 1-based pick position (0 = not picked) |
| | | | `kind_of_item` | `PromptItemKind` 0..6 | card, relic, potion, bundle or index |
| | | | `ordinal` | >= 0 | i |
| 26 | `cand` | int32 [192, 9] — THE ACTION SPACE: row r < cand_n = legal action r; rows >= cand_n are 0 | `kind` | `RunActionKind` 0..22 | action kind (21, 22: the rewards-screen macros TakeRewardCard, TakeRewardAlternative) |
| | | | `A`, `B` | -1/>= 0 | the action's raw arguments (see the Action section; macros: `rewards` row, card / alternative index) |
| | | | `hand_slot` | -1/0..9 | PlayCard: `hand` row |
| | | | `target_slot` | -1/0..5 | PlayCard / UsePotion: `enemies` row (-1 = no enemy target) |
| | | | `potion_slot` | -1/0..9 | UsePotion / DiscardPotion: `potions` row |
| | | | `option_index` | -1/>= 0 | row in the phase's table: Travel rank, rewards, card_reward, shop, rest, event_options, prompt_options; reward_choices for the macros |
| | | | `card_id` | `cards` id 0..597 | card played / picked / taken / selected (0 = none) |
| | | | `target_uid` | >= 0 | `enemies.uid` of the target (0 = none) |
| 27 | `cand_n` | int32 [] — scalar | - | 0..192 | number of legal candidates (0 when finished) |
| 28 | `reward` | float32 [] — scalar | - | 0/1 | 1 exactly at a Victory terminal step |
| 29 | `terminated` | uint8 [] — scalar | - | 0/1 | the episode ended at Victory or Defeat this step |
| 30 | `truncated` | uint8 [] — scalar | - | 0/1 | ended by step_cap, the floor stall cap (FLOOR_STALL), CONTENT_GAP or SIM_ERROR this step |
| 31 | `status` | int32 [] — scalar | - | bit flags | per-slot outcome bits of the last call (table below) |
| 32 | `episode` | int64 [3] — episode bookkeeping | `episode_id` | >= 1 | unique per env: episodes_started_before_on_slot x N + slot + 1 |
| | | | `step_index` | >= 0 | decisions taken in this episode |
| | | | `start_entry` | >= -1 | pool entry the episode started from; -1 = none (a `branch` destination, or an empty pool) |
| 33 | `aux` | float32 [14] — logging / metric helpers (never part of the reward) | `total_floor`, `act_index`, `hp`, `max_hp`, `gold`, `deck_n`, `relic_n`, `is_combat` | float | copies of `global_i` |
| | | | `bosses_killed`, `combats_won` | >= 0 | boss rooms / combat rooms cleared so far |
| | | | `act_floor`, `map_row`, `map_col` | float | copies of `global_i` (protocol metric inputs) |
| | | | `act_rooms` | >= 0 | rooms in the current act (map's last row + 1; the protocol's L) |
| 34 | `last_episode` | int32 [8] — the episode that ended in this call (with auto-reset the slot already shows the next one) | `win` | 0/1 | victory |
| | | | `floor`, `act` | >= 0, 0..2 | final total floor; 0-based act |
| | | | `steps` | >= 0 | decisions taken (applied actions; illegal indices excluded) |
| | | | `final_hp`, `final_max_hp` | >= 0 | final hp values |
| | | | `end_kind` | `EndKind` 0..5 | Terminal, Truncated (step_cap), ContentGap, SimError, FloorStall (floor_step_cap) |
| | | | `last_encounter_id` | `encounters` id 0..90 | last combat encounter (the killer on a Defeat) |
| 35 | `global_x` | int32 [2] — run / combat scalars appended after the freeze | `ancient_id` | `events` id 0..67 | this act's Ancient (its portrait marks its map point; 0 = none) |
| | | | `draw_marked_n` | >= 0 | combat: draw-pile cards with an enchantment or affliction (true count) |
| 36 | `hand_x` | int32 [10, 2] — row i = hand card i (more `hand` columns) | `affliction_amount` | >= 0 | the card's affliction amount (0 = none) |
| | | | `dampened` | 0/1 | downgraded by DAMPEN (restored when its casters die) |
| 37 | `draw_marked` | int32 [32, 6] — combat only: draw-pile cards with an enchantment or affliction, rows sorted by content; ORDER IS HIDDEN | `card_id`, `upgrade` | `cards` id 0..597, >= 0 | card identity |
| | | | `enchant_id`, `enchant_amount` | `enchantments` id 0..22, >= 0 | enchantment (0 = none) |
| | | | `affliction_id`, `affliction_amount` | `afflictions` id 0..8, >= 0 | affliction (0 = none) |
| 38 | `discard_order` | int32 [64, 6] — combat only: row i = discard-pile card i, oldest first (`discard_n` = true count) | `card_id`, `upgrade` | `cards` id 0..597, >= 0 | card identity |
| | | | `enchant_id`, `enchant_amount` | `enchantments` id 0..22, >= 0 | enchantment (0 = none) |
| | | | `affliction_id`, `affliction_amount` | `afflictions` id 0..8, >= 0 | affliction (0 = none) |
| 39 | `exhaust_order` | int32 [64, 6] — combat only: row i = exhaust-pile card i, oldest first (`exhaust_n` = true count) | `card_id`, `upgrade` | `cards` id 0..597, >= 0 | card identity |
| | | | `enchant_id`, `enchant_amount` | `enchantments` id 0..22, >= 0 | enchantment (0 = none) |
| | | | `affliction_id`, `affliction_amount` | `afflictions` id 0..8, >= 0 | affliction (0 = none) |
| 40 | `event_option_values` | int32 [8, 9] — row i = event option i (= `event_options` row i): what the option's text names | `value_n` | >= 0 | numbers the text names: gold, HP, damage, costs ... (true count) |
| | | | `value_0..2` | int | those numbers in text order (first 3) |
| | | | `item_n` | >= 0 | cards / relics / potions / enchantments the text names (true count) |
| | | | `item_kind_0`, `item_kind_1` | `ItemKind` 0..6 | vocabulary of `item_id_k` (0 = none) |
| | | | `item_id_0`, `item_id_1` | id | the items in text order (first 2; RELIC_TRADER: owned, then new relic) |
| 41 | `reward_choices` | int32 [16, 25] — rewards screen: row k = choice k of the unopened card rewards (each reward's cards, then its REROLL / SACRIFICE), the `option_index` of the k-th TakeRewardCard / TakeRewardAlternative candidate | `reward_index` | >= 0 | `rewards` row of the card reward (= the macro's A) |
| | | | `option` | >= 0 | card index or alternative index (= the macro's B) |
| | | | `alt_id` | `CardRewardAlt` 0..4 | the alternative (0 for a card row) |
| | | | 22 columns of `hand` | as hand | the offered card, as `card_reward` shows it once opened (0 for an alternative) |
| 42 | `map_routes` | int32 [18, 7, 5] — [row][col] = the `map_nodes` cell: routing features of the drawn map graph (child edges); 0 where no node | `elites_min`, `elites_max` | -1/>= 0 | fewest / most Elite rooms on a path from this node to the act's boss (this node included; -1 = boss not reachable) |
| | | | `rests`, `shops` | >= 0 | rest sites / shops reachable from this node (distinct nodes, this node included) |
| | | | `boss_dist` | -1/>= 0 | moves from this node to the act's boss (0 at the boss; -1 = not reachable, e.g. the second boss) |

Static card table (`schema.card_static`, int32 [598, 30]: row = `cards` id; published once in the schema JSON as `card_static`, not per observation; rows 0 and 1 are zero; `schema.card_static_cols()` names the columns):

| column(s) | range | meaning |
| --- | --- | --- |
| `ported` | 0/1 | 1 = in the simulator's content; 0 = known only from the catalog (cost, type, rarity, target; rest 0) |
| `cost`, `cost_upgraded`, `star_cost`, `star_cost_upgraded` | -2/-1/>= 0 | energy / star cost of a fresh card and after one upgrade; energy -1 = X, -2 = unplayable; star -1 = none, -2 = X |
| `type`, `rarity`, `target`, `keyword_mask`, `keyword_mask_upgraded` | `CardType` 0..6, `CardRarity` 0..10, `TargetType` 0..9, bit 1<<k, k = `CardKeyword` | card enums (MAD_SCIENCE: type None until Tinker Time); canonical / upgraded keywords |
| `max_upgrade`, `upgrade_known` | >= 0, 0/1 | upgrade levels; 1 = the upgraded columns come from a probe upgrade |
| `damage`, `damage_upgraded`, `block`, `block_upgraded`, `hits`, `hits_upgraded` | -1/>= 0 | Damage (or Calculated / Osty damage) and Block vars, hits per play (-1 = X); base / upgraded |
| `var_id_0`, `var_id_1`, `var_id_2`, `var_id_3`, `var_val_0`, `var_val_1`, `var_val_2`, `var_val_3`, `var_val_upgraded_0`, `var_val_upgraded_1`, `var_val_upgraded_2`, `var_val_upgraded_3` | `card_vars` id 0..132, int | the first 4 dynamic vars as in `hand` (every "magic number" is one), base / upgraded values |

Enum values (`schema.vocab.enums[name][value]`):

- `RunPhase`: 0 Map, 1 Combat, 2 Rewards, 3 CardReward, 4 Shop, 5 RestSite, 6 Treasure, 7 Event, 8 Prompt, 9 Victory, 10 Defeat
- `RunActionKind`: 0 PlayCard, 1 EndTurn, 2 UsePotion, 3 DiscardPotion, 4 Travel, 5 ClaimReward, 6 ProceedRewards, 7 PickRewardCard, 8 RewardAlternative, 9 BuyEntry, 10 LeaveShop, 11 ChooseRestOption, 12 LeaveRestSite, 13 OpenChest, 14 TakeTreasureRelic, 15 SkipTreasureRelic, 16 ChooseEventOption, 17 LeaveEvent, 18 PromptSelect, 19 PromptConfirm, 20 PromptCancel, 21 TakeRewardCard, 22 TakeRewardAlternative
- `IntentType`: 0 Attack, 1 Buff, 2 Debuff, 3 DebuffStrong, 4 Defend, 5 Escape, 6 Heal, 7 Hidden, 8 Summon, 9 Sleep, 10 Stun, 11 StatusCard, 12 CardDebuff, 13 DeathBlow, 14 Unknown
- `MapPointType`: 0 Unassigned, 1 Unknown, 2 Shop, 3 Treasure, 4 RestSite, 5 Monster, 6 Elite, 7 Boss, 8 Ancient
- `RewardType`: 0 None, 1 Card, 2 Gold, 3 Potion, 4 Relic, 5 RemoveCard, 6 SpecialCard
- `ChoicePromptKind`: 0 HandSelect, 1 HandUpgrade, 2 PileSelect, 3 ChooseCard, 4 SimpleGrid, 5 DeckUpgrade, 6 DeckTransform, 7 DeckEnchant, 8 DeckRemove, 9 DeckGeneric, 10 RewardsGrid, 11 Bundle, 12 CardReward
- `CardType`: 0 None, 1 Attack, 2 Skill, 3 Power, 4 Status, 5 Curse, 6 Quest
- `TargetType`: 0 None, 1 Self, 2 AnyEnemy, 3 AllEnemies, 4 RandomEnemy, 5 AnyPlayer, 6 AnyAlly, 7 AllAllies, 8 TargetedNoCreature, 9 Osty
- `CardRarity`: 0 None, 1 Basic, 2 Common, 3 Uncommon, 4 Rare, 5 Ancient, 6 Event, 7 Token, 8 Status, 9 Curse, 10 Quest
- `RestOption`: 0 ∅, 1 ?, 2 CLONE, 3 COOK, 4 DIG, 5 HATCH, 6 HEAL, 7 KINDLE, 8 LIFT, 9 MEND, 10 SMITH
- `CardRewardAlt`: 0 ∅, 1 ?, 2 REROLL, 3 SACRIFICE, 4 Skip
- `ShopCategory`: 0 ∅, 1 ?, 2 card, 3 card_removal, 4 colorless_card, 5 potion, 6 relic
- `PileType`: 0 None, 1 Draw, 2 Hand, 3 Discard, 4 Exhaust, 5 Play, 6 Deck
- `CardKeyword`: 0 None, 1 Exhaust, 2 Ethereal, 3 Innate, 4 Unplayable, 5 Retain, 6 Sly, 7 Eternal
- `RelicStatus`: 0 Normal, 1 Active, 2 Disabled
- `PromptItemKind`: 0 ∅, 1 ?, 2 bundle, 3 card, 4 index, 5 potion, 6 relic
- `EndKind`: 0 None, 1 Terminal, 2 Truncated, 3 ContentGap, 4 SimError, 5 FloorStall
- `ItemKind`: 0 ∅, 1 ?, 2 affliction, 3 card, 4 enchantment, 5 potion, 6 relic

Status bits (`status`, `sts2vec.STATUS`): `OK` 0, `NOOP` 1, `ILLEGAL_ACTION` 2, `CONTENT_GAP` 4, `SIM_ERROR` 8, `CANDIDATE_OVERFLOW` 16, `TABLE_OVERFLOW` 32, `RESET` 64, `POOL_EMPTY` 128, `FLOOR_STALL` 256, `MACRO_FALLBACK` 512.

<!-- END GENERATED -->

## 6. Hidden information (never exposed)

The observation is what a human sees on screen (rules: CONTRACTS.md §D3.3 H1–H15; tests in `VecTests.Encoder.cs`):
draw-pile **order** (only the sorted multiset `draw_counts` and the content-sorted `draw_marked`; leak tests reshuffle
600 combat states, and marked piles, and require identical bytes), seeds and all RNG streams/counters, future rooms
beyond the current act's map, reward odds and grab bags, the treasure relic before the chest opens, enemy **move
identities** and AI state (only intents: type, damage, hits, card count), monster/event internals (an event option
shows only the numbers and items its text names: `event_option_values`), instance ids (except the stable enemy `uid`),
profile and history. Powers show the amount the UI shows (hidden powers are omitted). Visible on purpose: discard and
exhaust pile order (the pile screens keep it), the act's Ancient, and the cards of an unopened card reward on the
rewards screen (`reward_choices`; the game shows them one free click away: opening and closing it leaves it unclaimed).

## 7. Metric and evaluation

Protocol v0.2 progress per game: 1.0 for a win, else `((act - 1) + min(c, L - 1) / L) / 3` with `L` = rooms in the act
(`aux.act_rooms`) and `c` = rooms cleared in the act (rooms entered − 1). Headline = mean over a frozen seed set with a
95 % CI; secondary: Wilson win rate, floors, acts reached. Definition and leaderboard: protocol doc §4 and §8.

`sts2vec eval --policy P --seedset S [--out DIR] [--threads 8] [--seed 0]`: one slot per game (`auto_reset=False`),
all games batched, limits `step_cap` 1200 (`capped`) and the env's `floor_step_cap` 80 (`stuck`, status
`FLOOR_STALL`); a policy's no-op for a live game is sent as an illegal index. `P` =
`random` (seeded per game), `heuristic` (rules documented in `sts2vec/policies.py`), `first`, `pkg.module:callable` or
`file.py:callable`. `S` = `std-a0-v1`, `std-a10-v1` (20 games each, 4 per character), `smoke-a0-v1` or a JSON path.
Output = the LLM runner's layout (`config.json`, `run.json`, `games/<CHAR>-<SEED>.summary.json`, `report.json`,
`report.md`), so NN and LLM rows are directly comparable (`sts2eval report DIR` re-renders it). Each summary keeps
`history_actions` (candidate indices); the **replay audit** replays them on a fresh env and requires the same result,
act, floor and final `StateHash` (`replayable` must be n/n). Python: `sts2vec.evaluate.evaluate(seedset, policy, out)`.
Extra summary keys are ignored by the report and the audit (`sts2train eval --aux-heads`: `win_prob`, `combat_hp_pred`).

Reference rows (`std-a0-v1` / `std-a10-v1`, since the rewards-screen macros; earlier rows: protocol doc §8): random
0.082 / 0.044, first 0.194 / 0.103, heuristic 0.221 / 0.110; LLM track Sonnet 5.5 0.499 / 0.310. No policy has won yet.

## 8. Seeds and determinism

- A **fresh pool entry** is `(character, seed text, ascension, act1, content)`; the same entry always starts the same
  run (`PoolSpec(count=K)` creates entries with seed `"<seed_prefix>-<character>-<i>"`; `env.pool.add_fresh(...)`
  appends one). A **save entry** imports a real game save (`env.pool.add_save(path)`). `env.snapshot(slot)` appends
  a clone of a live slot; `env.restore(slot, entry)` restarts one slot from any entry; search copies live slots without
  a pool entry (`env.branch`, `env.pool.release`: §9, `sim/VEC.md` "Search").
- Start sequence of slot `i`: `slot_seed = XXH64(seed_text ‖ int32 i)`, entry `(slot_seed + episodes_started) mod
  pool_size` — independent of thread scheduling.
- **Guarantee**: same library build + same config + same pool entries in the same order + same action sequence ⇒
  byte-identical observation buffers and equal `state_hash` per slot, for any `threads` and across processes
  (`sts2vec check`, `sts2vec digest`, tests). Snapshots, exact branches and clones step exactly like their source; a
  determinized branch is a function of (state, seed, position in the call).
- The simulator RNG is internal; policies get randomness only from their own generators (`ctx.rngs` in `eval`).

## 9. API summary

Python (`sim/tools/vec`, package `sts2vec`; full reference `sim/tools/vec/README.md`):

| Call | Meaning |
| --- | --- |
| `VecEnv(N, threads=8, step_cap=3000, floor_step_cap=80, auto_reset=True, seed_text="VEC", content="ported_only", pool=PoolSpec(...), pool_cache=4096)` | create `N` slots |
| `env.reset(mask, start_ids)` / `env.step(actions)` | §3 |
| `env.pool.add_fresh(character, seed=, ascension=, act1=, content=)`, `env.pool.add_save(path)`, `len(env.pool)`, `env.pool.warm()` | pool entries (ids in insertion order); `warm` pre-creates fresh templates (≈ 16 ms each) |
| `env.snapshot(slot) -> entry`, `env.restore(slot, entry)` | clone a slot into the pool / restart a slot from an entry |
| `env.branch(src, dsts, determinized=True, seed=0)` | search (ABI 2): copy live slot(s) into `dsts` in one call, no pool entry; `src` = one slot or one per destination; destinations start a new episode (`RESET`, `start_entry` -1) with their observations written |
| `env.restore_many(slots, entries)`, `env.pool.release(ids)` | batched restore; free pool entries for good (ids never reused) |
| `env.state_hash(slots)` | 64-bit hash of the full hidden state (tests / debugging) |
| `env.candidates(i)`, `env.legal_mask()`, `env.last_episode(i)`, `env.slot_errors()` | decoded helpers |
| `env.schema` | buffers, fields, vocabularies, enums, caps, `encoder_version` |

C ABI (21 `sts2vec_*` exports, any language; the frozen 14 plus appended optional exports, the ABI 2 search set among
them, and `config_json` keys such as `floor_step_cap`): `sim/VEC.md` "C ABI"; Python calls the same exports via ctypes.

## 10. Versioning

- `encoder_version` = XXH64 of the schema JSON (buffers, shapes, fields, vocabularies, enums, caps). Any observation
  change changes it; store it with every checkpoint and refuse to mix (`sts2train` warns on mismatch). Columns are only
  appended; the ABI exports are frozen (new ones are additive and optional). `sts2vec_abi_version` (`lib.abi_version`:
  1 without it, 2 since the search exports; `lib.supports_branch` ...) versions the ABI apart from `encoder_version`.
- Runs record `server_version = sts2vec/<encoder_version>`, `repo_sha`, the seed set's `sha256`, `protocol_version`
  0.2 and a policy hash (`builtin:<name>`, the policy file's sha256, or `weights:<hash>` for checkpoints).
- Game updates: `docs/version-migration-playbook.md` (`scripts/version_delta.py` maps changed game files to sim files).

## 11. Machine requirements and throughput

- .NET 9 runtime (SDK to build), Python ≥ 3.11 with `uv` and numpy; macOS arm64 and Linux x86_64 validated; Docker
  image (`Dockerfile`, `docs/docker.md`, arm64 + amd64). Server GC is required (shipped runtimeconfig sets it).
- Memory: 35.1 KB of buffers per env plus the run state (≈ 15–35 KB per live game) and one template per pool entry.
- Throughput (uniform-random policy, A0, warmed pool): Mac M4 Pro 1,024 envs × 8 threads ≈ 116k env-steps/s (C#
  bench), ≈ 73k through Python; 4,096 × 8 ≈ 144k (C#) / 163k (Python). Linux 8 vCPU (Huginn) ≈ 50k (C#, after JIT warm-up; use
  `DOTNET_TieredCompilation=0` or the NativeAOT library for short runs). A 20-game protocol evaluation of a rule policy
  takes seconds. Details: `sim/BENCH.md`, `sim/VEC.md` "Bench".

## 12. File layout

| Path | Content |
| --- | --- |
| `sim/Sts2Sim.Core` | rules kernel (cards, relics, monsters, events, map, rewards) |
| `sim/Sts2Sim.Vec` | batched env, observation encoder (`ObservationEncoder*.cs`), schema (`Schema.cs`), C ABI (`Exports.cs`) |
| `sim/tools/vec/sts2vec` | Python package: `env.py`, `pool.py`, `schema.py`, `policies.py`, `evaluate.py`, `protocol.py`, `cli.py` |
| `sim/tools/vec/examples` | `random_policy.py`, `heuristic_policy.py`, `policy_template.py` |
| `sim/tools/vec/tools/gen_env_spec.py` | regenerates §5 of this page (`--check` in the tests) |
| `sim/tools/vec/tools/gen_event_option_vars.py` | regenerates `EventOptionVars.g.cs` (which numbers / items each event option's text names) from `knowledge/source` |
| `sim/tools/eval/seedsets` | frozen protocol seed sets (sha256 in each file) |
| `sim/tools/train` | PPO learner (example consumer; its `eval` wraps `sts2vec eval`; `--aux-heads`, `--deck-counts`, `--acq-shaping`: its README) |
| `tmp/eval/<run>/` | evaluation outputs (git-ignored) |

## 13. FAQ

- **Why candidate indices instead of a fixed action space?** Legal moves vary wildly (cards × targets, prompts, map
  nodes, shop rows). Score each candidate row (pointer networks / attention over `cand` joined to the tables its
  pointer columns name) and take a softmax over rows `< cand_n`.
- **Is there reward shaping?** No; the env reward is win = 1. `aux` (floor, act, bosses killed, combats won, act rooms)
  is for logging or your own shaping; the benchmark scores protocol progress only. (`sts2train --acq-shaping` shapes
  inside the trainer: potential-based on deck size / relics / upgrades, it telescopes and is logged apart.)
- **What if my policy outputs an illegal index?** The slot does not move and `status` has `ILLEGAL_ACTION`; at a
  non-combat point it still counts toward `floor_step_cap`, so the evaluator counts it (`illegal`) and the game ends
  `stuck` (in combat only `step_cap` bounds applied actions).
- **Do potions, prompts and events need special code?** No; they are candidates like any other action.
- **Can I see what the enemy will do?** Only its intents (type, damage, hits), as on screen; move names are hidden.
- **Card text?** Not in the observation; join ids to `knowledge/source/v0.111.0/cards.jsonl` (`id`, localized text).
- **Same score as the LLM leaderboard?** Yes: same seed sets, metric, limits and report format; mark rows "sim NN track".
