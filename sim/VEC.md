# Sts2Sim.Vec — batched training interface

Status: **scaffold** (wave E2). Shapes, names, buffer order, capacities and export signatures are final
(CONTRACTS.md Part D). The player-view encoder (`ObservationEncoder*.cs`, V1) fills every observation table (see the
field reference below); the candidate table (`CandidateTable`, owner V2) is a stub until V2 lands, so `cand_n` is 0.

## Build and load

```bash
dotnet build sim/Sts2Sim.sln -c Release          # → sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll
uv run --project sim/tools/vec pytest -q sim/tools/vec
uv run --project sim/tools/vec sts2vec check
uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500
```

(`pytest -q` without the path, run from the repo root, also collects `sim/tools/eval` tests; pass the path as the
repo's other uv projects do, or run from `sim/tools/vec`.)

- `Sts2Sim.Vec` is a `net9.0` class library referencing only `Sts2Sim.Core` (`EnableDynamicLoading`,
  `ServerGarbageCollection`, `ConcurrentGarbageCollection=false`, `TieredPGO`, `InvariantGlobalization`).
- Python hosts the CLR in-process through hostfxr with the shipped `sts2vec.runtimeconfig.json`
  (`System.GC.Server = true`; the generated `Sts2Sim.Vec.runtimeconfig.json` has the same settings and is the fallback),
  resolves every export with `load_assembly_and_get_function_pointer(…, UNMANAGEDCALLERSONLY_METHOD)` and calls it with
  ctypes (`CFUNCTYPE` releases the GIL). Type `Sts2Sim.Vec.Exports, Sts2Sim.Vec`; managed method = PascalCase of the
  export name without `sts2vec_` (`sts2vec_pool_add_fresh` → `PoolAddFresh`).
- Discovery: `STS2VEC_DLL` (file or directory) else the Release build dir; libhostfxr from `DOTNET_ROOT`,
  `/opt/homebrew/opt/dotnet@9/libexec`, standard roots, then `dotnet --list-runtimes`. A `.dylib`/`.so` path loads a
  NativeAOT build with the same exports via `ctypes.CDLL`.
- `sts2vec_create` fails with a "Server GC is off" error when `GCSettings.IsServerGC` is false (Workstation GC caps
  batched stepping ~4.8× lower). Escape hatch for debugging only: `STS2VEC_ALLOW_WORKSTATION_GC=1`
  (C# tests set `Exports.RequireServerGC = false`).
- One CLR per process: the first load is cached.

## Environment model (§D2)

- Slot `i`: `RunSim? Sim`, `episode_id`, `step_index`, `start_entry`, `slot_seed = XXH64(UTF-8(seed_text) ‖ int32 LE i)`.
- `episode_id = episodes_started_before_on_slot × N + i + 1` (unique per env, increasing per slot, independent of
  thread scheduling).
- Start sequence: `entry = (slot_seed + episodes_started_before) mod pool_count` (pool in insertion order).
- `reset(mask, start_ids)`: masked slots restart (explicit entry, or the sequence when `< 0`); their observation and
  signals (reward 0, terminated 0, truncated 0, status `RESET`, episode) are written; other slots are untouched.
  A start id `>= pool_count` fails the whole call (-1) before anything changes. NULL mask = all slots; NULL start_ids =
  all from the sequence.
- `step(actions)`: `a < 0` → `NOOP`; `a ∉ [0, cand_n)` (or no run / finished) → `ILLEGAL_ACTION`, slot unchanged;
  else `RunSim.Step(LegalActions()[a])`. Terminal → `terminated`, reward 1 iff Victory; `step_index >= step_cap` →
  `truncated`; NotImplemented/NotSupported → `CONTENT_GAP`, other exceptions → `SIM_ERROR` (episode ends with
  `truncated = 1`, reward 0, run discarded, text in `sts2vec_last_error(h, slot)`). An encoder exception on a live
  state ends the episode the same way. Then every slot's observation, candidates and signals are written.
- Auto-reset (default): a finished episode writes `last_episode`, then the slot restarts from its sequence in the same
  call (`RESET` bit, new `episode_id`, the new episode's first observation). A slot whose start failed retries the
  next sequence entry on the following step. With `auto_reset = false` the slot stays terminal (`cand_n = 0`).
- Pool: fresh entries create their template with `RunSim.NewRun` on first use and cache it (up to `pool_cache`
  templates); resets `Clone()` the template under a per-entry lock. Save entries use `RunImporter.FromSave` (checked)
  with the env's content filter. Snapshots hold `Clone()` of a slot. `config.pool.fresh` with `count = K` adds K
  entries: character `characters[i % len]`, seed `"<seed_prefix>-<character>-<i>"` (interleaved, so any prefix of the
  pool covers every character). Beyond `pool_cache` templates a fresh entry runs `NewRun` on every start (≈ 16 ms).
- Save entries whose import stops at a prompt (a choice during the load) are re-imported from the parsed save on every
  start instead of cloned, because `RunSim.Clone` shares the pending load room (`Pending.Load`) between clones. The
  fixture save and every save point verified so far import without a pending prompt, so they clone normally.
- Threads: `Parallel.For(0, N, MaxDegreeOfParallelism = threads)`; a slot is touched by one worker per call; the pool's
  entry list is lock-protected and each entry's template creation + `Clone()` runs under its own lock. Verified: 256
  envs × 300 steps on 1 vs 8 threads and two instances give byte-identical buffers after every step and equal
  `StateHash` per slot; 1,024 envs × 200 steps on 1 vs 12 threads likewise.
- Per-slot scratch: `VecSlot.Legal` (`List<RunAction>`, capacity 64) is refilled by `RunSim.LegalActions(list)` after
  every change and is the action table `step` indexes; no per-step allocation in VecEnv beyond the sim's own.
- Test hook: `VecEnv.FaultInjector = (slot, action) => Exception?` throws the returned exception in place of
  `RunSim.Step` (classification and auto-reset apply). Null in production.

### Candidate table (`cand`, `CandidateTable.Fill`)

Row `r < cand_n` is `RunSim.LegalActions()[r]` (same order); rows `>= cand_n` are zero. `cand_n = min(legal, 192)`;
more legal actions set `CANDIDATE_OVERFLOW` and only the first 192 are selectable (constructible: a PileSelect over a
250-card draw pile). `CandidateTable.ToAction(row)` inverts a row to its `RunAction` (kind, A, B).

| Column | Value (−1 = does not apply unless stated) |
| --- | --- |
| `kind`, `A`, `B` | the `RunAction` fields verbatim (`RunActionKind` value; A/B as in CONTRACTS.md §C1, −1 = none) |
| `hand_slot` | A for `PlayCard` (index into `hand`) |
| `target_slot` | B for `PlayCard` / `UsePotion` when B ≥ 0 (index into `enemies`); −1 for self/none (B = −1) and for pets (B ≤ −3 = `Allies[-2 - B]`: read B) |
| `potion_slot` | A for `UsePotion` / `DiscardPotion` (index into `potions`) |
| `option_index` | A for `Travel` (rank of the destination among legal points sorted by (col, row)), `ClaimReward`, `PickRewardCard`, `RewardAlternative`, `BuyEntry`, `ChooseRestOption`, `ChooseEventOption`, `PromptSelect` (index into `prompt_options`) |
| `card_id` | cards vocab id (0 = none) of the played hand card, of the picked card-reward card, or of a prompt option that is a card (not for Bundle prompts; on grid prompts only ids that are not also relic/potion ids) |
| `target_uid` | `CombatId` of the targeted monster (= `enemies.uid`), 0 = none |

## Bench

```bash
DOTNET_gcServer=1 dotnet run -c Release --project sim/Sts2Sim.Bench -- vec [envs=1024] [threads=8] [steps=500] [ascension=0] [pool=1024]
```

In-process through the real exports (`delegate* unmanaged` → `[UnmanagedCallersOnly]`), NativeMemory buffers, seeded
uniform-random candidate policy, all five characters. Pool templates are warmed before the timed window. Prints
env-steps/s (slot steps with an action ≥ 0), ms per batch (avg/min/max), resets/s, episode ends, error counts, GC mode
and collections, bytes per env. `DOTNET_gcServer=1` matches the shipped runtimeconfig (the Bench exe itself defaults to
Workstation GC, ~3× slower here). Rows go to `sim/BENCH.md` ("Vec (E2)").

## C ABI (§D5, frozen)

```c
int32_t sts2vec_version(uint8_t* buf, int32_t cap);                       // "sts2vec/<encoder_version>"; returns needed length
int32_t sts2vec_schema(uint8_t* buf, int32_t cap);                        // schema JSON; returns needed length (call with cap 0 first)
int64_t sts2vec_create(const uint8_t* config_json, int32_t len);          // > 0 handle, or -1 (sts2vec_last_error(0, -1, …))
int32_t sts2vec_destroy(int64_t h);
int32_t sts2vec_set_threads(int64_t h, int32_t threads);
int32_t sts2vec_pool_add_fresh(int64_t h, const uint8_t* json, int32_t len);   // {"character","ascension","seed","act1","content"} → entry id or -1
int32_t sts2vec_pool_add_save(int64_t h, const uint8_t* path_utf8, int32_t len); // entry id or -1 (reasons in last_error)
int32_t sts2vec_pool_count(int64_t h);
int32_t sts2vec_snapshot(int64_t h, int32_t slot);                        // entry id or -1
int32_t sts2vec_restore(int64_t h, int32_t slot, int32_t entry, void** buffers);
int32_t sts2vec_reset(int64_t h, const uint8_t* mask, const int32_t* start_ids, void** buffers);
int32_t sts2vec_step(int64_t h, const int32_t* actions, void** buffers);
int32_t sts2vec_state_hash(int64_t h, const int32_t* slots, int32_t n, uint64_t* out);   // test/debug only
int32_t sts2vec_last_error(int64_t h, int32_t slot, uint8_t* buf, int32_t cap);         // slot -1 = env-level; h 0 = global
```

- String outputs write `min(cap, len)` bytes and return the full UTF-8 length.
- Return 0 ok / −1 error. Every failing export stores `"<ExceptionType>: <message>"` globally (`h = 0`) and, when the
  handle is valid, on the env (`slot = -1`). Per-slot problems go to `status` and the slot's last error (full
  exception text). No exception crosses the ABI. Each env serialises its own calls with a lock.
- Validation: an invalid handle, a slot outside `0..N-1` (`last_error` also accepts −1), a pool entry outside
  `0..count-1`, a null or incomplete buffer table, a null actions pointer, `n < 0` or null pointers with `n > 0` in
  `state_hash`, a start id `>= pool_count` (masked-in slots only), malformed or unknown-key JSON, an unknown character,
  ascension outside 0..10 → −1 with the text, the env unchanged and usable.
- `sts2vec_state_hash` writes 0 for a slot with no run; `sts2vec_snapshot` of an empty slot is an error.
- `config_json`: `{"num_envs": N, "threads": 8, "step_cap": 3000, "auto_reset": true, "seed_text": "VEC",
  "content": "ported_only" | "all", "pool": {"fresh": {"characters": [...], "ascension": 0, "count": 4096,
  "seed_prefix": "TRAIN", "act1": "random"}}, "pool_cache": 4096}`; `num_envs` required, unknown keys rejected.

## Schema (§D3; published by `sts2vec_schema`)

- Ids: index into the schema's vocabulary (`0` none/padding, `1` unknown, real ids from 2, sorted ordinal). Cards =
  registry ids ∪ `CardCatalog` ids (unported ids stable); events also list the two stub events (CRYSTAL_SPHERE,
  FAKE_MERCHANT). `card_vars` follows the same convention (2 + 131 names).
- Enum columns hold the CLR enum value; the `vocab.enums` lists are indexed by value (RunPhase, RunActionKind,
  IntentType, MapPointType, RewardType, ChoicePromptKind, CardType, TargetType, CardRarity, PileType, CardKeyword,
  RelicStatus, EndKind). String-keyed enums (RestOption, CardRewardAlt, ShopCategory, PromptItemKind) use the vocabulary
  convention (`""` 0, `"?"` 1). Rows beyond a table's count are zero; mask by the matching `*_n` (or id ≠ 0).
- `playable_reason_id` holds the raw `UnplayableReason` bitmask; `playable_reason_bits` maps names to bits.
- Count vectors: index `card_id * 2 + min(upgrade, 1)`, length `2 × V_cards`.
- `encoder_version` = XXH64 (seed 0, lower-case hex) of the UTF-8 JSON text with the leading
  `"encoder_version":"…",` member removed (Python: `Schema.verify_version()`).

| # | Buffer | dtype | shape | Fields / content |
| --- | --- | --- | --- | --- |
| 0 | `global_i` | int32 | [58] | phase, character, ascension, act_index, act_id, total_floor, act_floor, map_row, map_col, hp, max_hp, gold, energy, max_energy, stars, block, turn, round, potion_slots, deck_n, draw_n, discard_n, exhaust_n, orb_slots, relic_n, hand_n, enemy_n, ally_n, boss_id, second_boss_id, card_removals_used, removal_price, incoming_damage, incoming_hits, prompt_kind, prompt_min, prompt_max, prompt_picked, prompt_needs_confirm, prompt_can_cancel, prompt_can_skip, prompt_ordered, prompt_option_n, prompt_pile, prompt_site_hash, reward_n, card_reward_can_skip, card_reward_skip_consumes, treasure_opened, treasure_relic_id, treasure_claimed, event_id, event_finished, event_option_n, rest_option_n, shop_entry_n, decision_kind, is_combat. See the field reference |
| 1 | `global_f` | float32 | [4] | hp_frac, gold_log1p, floor_frac, incoming_net_hp |
| 2 | `hand` | int32 | [10, 22] | card_id, upgrade, cost_current (−1 X, −2 unplayable), cost_base, type, target, rarity, keyword_mask (bit = 1 << CardKeyword), enchant_id, enchant_amount, affliction_id, playable, playable_reason_id, hits, var_id_0..3, var_val_0..3 |
| 3 | `hand_f` | float32 | [10, 2] | block_eff, damage_eff_self_or_all |
| 4 | `hand_dmg` | float32 | [10, 6] | vs_enemy_0..5 |
| 5 | `deck` | int32 | [96, 5] | card_id, upgrade, enchant_id, enchant_amount, affliction_id |
| 6–8 | `draw_counts`, `discard_counts`, `exhaust_counts` | int16 | [V_cards × 2] | multiset counts |
| 9 | `relics` | int32 | [32, 5] | relic_id, counter, status, used_up, stack |
| 10 | `potions` | int32 | [10, 3] | potion_id, usable_now, target_type |
| 11 | `powers_player` | int32 | [24, 3] | power_id, amount, is_debuff |
| 12 | `orbs` | int32 | [10, 3] | orb_id, passive, evoke |
| 13 | `enemies` | int32 | [6, 9] | present, uid, monster_id, hp, max_hp, block, flank, behind, intent_n |
| 14 | `enemy_intents` | int32 | [6, 3, 4] | type, damage, times, card_count |
| 15 | `enemy_powers` | int32 | [6, 12, 2] | power_id, amount |
| 16 | `allies` | int32 | [2, 5] | present, monster_id, hp, max_hp, block |
| 17 | `ally_powers` | int32 | [2, 8, 2] | power_id, amount |
| 18 | `map_nodes` | int32 | [18, 7, 6] | point_type, visited, current, legal_next, child_mask, quest |
| 19 | `rewards` | int32 | [8, 4] | reward_type, amount, item_id, is_card_reward |
| 20 | `card_reward` | int32 | [4, 22] | hand columns (playable, playable_reason_id, hits = 0) |
| 21 | `card_reward_alts` | int32 | [2] | alt_id (CardRewardAlt) |
| 22 | `shop` | int32 | [14, 7] | category, item_id, upgrade, price, stocked, on_sale, affordable |
| 23 | `rest` | int32 | [9, 2] | option_id, enabled |
| 24 | `event_options` | int32 | [8, 6] | key_hash, ordinal, enabled, locked, proceed, relic_id |
| 25 | `prompt_options` | int32 | [128, 5] | item_id, upgrade, picked, kind_of_item (PromptItemKind), ordinal |
| 26 | `cand` | int32 | [192, 9] | kind, A, B, hand_slot, target_slot, potion_slot, option_index, card_id, target_uid (see "Candidate table") |
| 27 | `cand_n` | int32 | [] | candidates = min(len(LegalActions()), 192); 0 when terminal, finished or empty |
| 28 | `reward` | float32 | [] | 1 at a Victory terminal, else 0 |
| 29 | `terminated` | uint8 | [] | episode ended at Victory/Defeat |
| 30 | `truncated` | uint8 | [] | step cap, content gap or sim error |
| 31 | `status` | int32 | [] | OK 0, NOOP 1, ILLEGAL_ACTION 2, CONTENT_GAP 4, SIM_ERROR 8, CANDIDATE_OVERFLOW 16, TABLE_OVERFLOW 32, RESET 64, POOL_EMPTY 128 |
| 32 | `episode` | int64 | [3] | episode_id, step_index, start_entry |
| 33 | `aux` | float32 | [10] | total_floor, act_index, hp, max_hp, gold, bosses_killed, combats_won, deck_n, relic_n, is_combat |
| 34 | `last_episode` | int32 | [8] | win, floor, act, steps, final_hp, final_max_hp, end_kind (EndKind), last_encounter_id |

35 buffers (D3.1 numbers its rows 0–33 and lists `card_reward_alts` inside row 20). ≈ 26.7 KB per env.

## Field reference (observation encoder, `ObservationEncoder*.cs`)

The encoder reads the run through public Core API only. It is pure: it writes no state, draws no RNG, and leaves the
state hash unchanged (`Test_VecEncoder_IsPure` checks 1,000 sampled states, and also checks that encoding twice gives
identical bytes). Apart from what Core calls allocate, it does not allocate: it uses per-thread scratch and no LINQ.
Measured over 1,000 states it averages ≈ 36 µs and ≈ 440 B per call, and the bytes come from Core hooks. Each call
clears every observation buffer first. A table that exceeds its capacity keeps its first CAP rows, sets
`TABLE_OVERFLOW`, and still reports the true count in its `*_n` column. Fractional numbers are truncated to the
integer the game UI shows.

### Player-view rules (CONTRACTS.md §D3.3, hidden-information list H1–H15)

| # | Rule | How the encoder applies it |
| --- | --- | --- |
| H1 | The draw pile order is hidden. | Draw, discard and exhaust piles are encoded only as count vectors. `Test_VecEncoder_LeakTest_DrawOrder` takes ≥ 500 combat decision points, encodes each next to a clone whose draw pile was reshuffled, and requires identical bytes; each clone's hash differs, which proves the shuffle happened. |
| H2 | No seed, RNG stream or counter. | Nothing under `Rng`, `SeedText` or `Odds` is read. |
| H3 | No future rooms. | Only the current act's `Boss` and `SecondBoss` appear (`boss_id`, `second_boss_id`); the game shows both on the map and the top bar. The map is the current act's map only. |
| H4/H5 | No odds and no grab bags. | Neither is read. |
| H6 | The treasure relic is hidden until the chest opens. | `treasure_relic_id` is 0 until `ChestOpened`. |
| H7 | Card-reward cards are hidden on the rewards screen. | A card reward appears in `rewards` only as `is_card_reward = 1`. `card_reward` and `card_reward_alts` are written only in the `CardReward` loop phase. |
| H8 | No move ids. | Intents come from `NextMove.Intents` through `PlayerView.Intent` (type, damage, times, card_count). The move's identity and the AI state are never read; `Test_VecEncoder_NoMoveIds` greps the encoder source and checks the rows. |
| H9/H11 | No monster, encounter or event internals. | `Extra`, `EncounterExtra`, event fields and event RNG are not read. An event option is identified by `key_hash` and `ordinal` only. |
| H10 | Powers are shown as the UI shows them. | Visibility uses `PlayerView.IsVisibleToPlayer` and amounts use `PlayerView.DisplayAmountToPlayer` (see below). |
| H13 | No instance ids. | The only exception is the enemy `uid` (= `CombatId`, stable for the whole combat; the play server uses it as well). Prompt options are identified by (item_id, upgrade, ordinal). |
| H12/H14/H15 | No profile, history or visited-event set. | None of these is read. |

`PlayerView` (`sim/Sts2Sim.Core/Combat/Pipeline/PlayerView.cs`, decision D9) holds the helpers that were moved from
`SimProjector`, with their semantics unchanged:

- `IsVisible` and `DisplayAmount` are strict. They throw `PlayerViewGapException` for AMBERGRIS and for the
  display-amount overrides that have no rule; the diff gate relies on this.
- `Intent`, `CostOf`, `CanPlay` and `HitCount` are shared by the diff projection, the play server and the encoder.
- `IsVisibleToPlayer` and `DisplayAmountToPlayer` never throw, and only the encoder uses them. AMBERGRIS is hidden, as
  the game sets `IsVisibleInternal => false`. AUTOMATION, FERAL, MONOLOGUE, ORBIT, PALE_BLUE_DOT, PANACHE, SLOTH and
  WITHERING_PRESENCE apply the game's DisplayAmount from the Core port's state; the decompile citations are inline in
  the code. CACOPHONY and TAG_TEAM are not ported and fall back to `Amount`.

### Columns

Combat-only fields are 0 unless `RunState.Combat.IsInProgress`. Room tables are written from the loop phase
(`RunSim.Loop.Phase`), so a pending prompt keeps its screen as context. For example, a merchant card-removal prompt
still shows the shop.

**global_i**

| Column | Meaning |
| --- | --- |
| `phase` | `RunSim.Phase` (RunPhase). |
| `decision_kind` | `Prompt` while `HasPendingChoice`, else `phase`. |
| `is_combat` | 1 while a combat is in progress. |
| `character` | Vocab id of the character. |
| `ascension` | Ascension level. |
| `act_index` | Act index, 0–2. |
| `act_id` | Act vocab id. |
| `total_floor` | `MapPointHistory.Count`. |
| `act_floor` | Floor within the act. |
| `map_row`, `map_col` | The current map coordinate; −1 before the first move. |
| `hp`, `max_hp`, `gold`, `stars` | Read from the shared `PlayerState`. |
| `energy` | Combat only. |
| `max_energy` | `EnergyCmd.GetMaxEnergy` in combat, else `BaseMaxEnergy`. |
| `block`, `turn`, `round` | Combat only (`PlayerState.TurnNumber`, `CombatState.RoundNumber`). |
| `potion_slots` | Number of potion slots. |
| `deck_n`, `relic_n` | Counts of the deck and the relics. |
| `draw_n`, `discard_n`, `exhaust_n`, `hand_n` | Combat only. |
| `orb_slots` | `OrbQueue.Capacity` in combat, else `BaseOrbSlotCount`. |
| `enemy_n` | Number of live enemies. |
| `ally_n` | Number of live pets. |
| `boss_id`, `second_boss_id` | Encounter vocab ids of the current act's bosses. |
| `card_removals_used` | Card removals bought so far. |
| `removal_price` | Next removal price: base 75 / 100 at A6, plus 25 / 50 per removal used. In a shop it is the removal entry's `GetCost` (relic modifiers included), or −1 once sold. |
| `incoming_damage`, `incoming_hits` | Sums over live enemies and their attack intents of damage × times, and of times. This is the play server's `danger`. |
| `prompt_kind` | ChoicePromptKind. HandSelect = 0, so mask by `decision_kind`. |
| `prompt_min`, `prompt_max` | Selection bounds. |
| `prompt_picked` | Number of picks so far. |
| `prompt_needs_confirm`, `prompt_can_cancel`, `prompt_can_skip`, `prompt_ordered` | Prompt flags. |
| `prompt_option_n` | Number of options (the true count). |
| `prompt_pile` | PileType; 0 when there is none. |
| `prompt_site_hash` | Low 32 bits of XXH64(UTF-8 `SiteId`) (`ObservationEncoder.HashText`). |
| `reward_n` | Number of unclaimed rewards. |
| `card_reward_can_skip` | 1 when a `Skip` alternative exists (CardReward phase). |
| `card_reward_skip_consumes` | 1 when skipping completes the reward. |
| `treasure_opened` | 1 once the chest is open. |
| `treasure_relic_id` | Relic vocab id, only once the chest is open (H6). |
| `treasure_claimed` | 1 = taken, 2 = skipped. |
| `event_id` | Event vocab id. |
| `event_finished` | 1 when the event is finished. |
| `event_option_n` | Number of options while the event is not finished, else 0. |
| `rest_option_n` | `RestSiteRoom.Options.Count`. |
| `shop_entry_n` | `MerchantInventory.AllEntries.Count`. |

**global_f**

| Column | Meaning |
| --- | --- |
| `hp_frac` | hp / max_hp. |
| `gold_log1p` | ln(1 + gold). |
| `floor_frac` | total_floor / 48, or / 49 at A10 (double boss). |
| `incoming_net_hp` | hp − max(0, incoming_damage − block). |

**hand / card_reward** (row i = `Hand[i]` / `CardReward.Cards[i]`)

| Column | Meaning |
| --- | --- |
| `card_id`, `upgrade` | `UpgradeLevel`. |
| `cost_current` | −1 for X cost; −2 when the cost is unplayable (`Base < 0`); otherwise `PlayerView.CostOf` (combat modifiers) in combat and `GetAmountToSpend()` out of combat. |
| `cost_base` | `EnergyCost.Base`, with the same sentinels. |
| `type`, `target`, `rarity` | Per-instance enum values. |
| `keyword_mask` | `GetKeywordMaskWithSources(cs, All)`; bit = 1 << CardKeyword. |
| `enchant_id`, `enchant_amount` | The card's enchantment. |
| `affliction_id` | The card's affliction. |
| `playable` | Hand only: `CanPlay` with reason None. |
| `playable_reason_id` | Hand only: the raw UnplayableReason bitmask. |
| `hits` | Hand only, for cards with a damage var: `PlayerView.HitCount` (Repeat var, else the def's `HitCount` constant, else 1; X cost 0). |
| `var_id_k`, `var_val_k` | The first 4 dynamic vars in the card's declaration order (`card_vars` vocabulary), with their base `IntValue`. |

**hand_f**

| Column | Meaning |
| --- | --- |
| `block_eff` | `CardPreview.Block(cs, card, null)`. |
| `damage_eff_self_or_all` | For cards with a damage var whose target is not AnyEnemy, the untargeted `CardPreview.Damage(cs, card, null)`. |

**hand_dmg**

| Column | Meaning |
| --- | --- |
| `vs_enemy_j` | `CardPreview.Damage(cs, card, Monsters[j])` for a live enemy in slot j and a card with a damage var; 0 otherwise. This equals the play server's `effective.damage[uid]`. |

**deck** (row = pickup order; `deck_n` holds the true count)

| Column | Meaning |
| --- | --- |
| `card_id`, `upgrade` | Card identity. |
| `enchant_id`, `enchant_amount` | The card's enchantment. |
| `affliction_id` | The card's affliction. |

**draw_counts / discard_counts / exhaust_counts**

Each is the count of the pile's cards at index `card_id * 2 + min(upgrade, 1)`, saturating at 32,767. These vectors
are combat only.

**relics** (pickup order)

| Column | Meaning |
| --- | --- |
| `relic_id` | Relic vocab id. |
| `counter` | `DisplayAmount` when `ShowCounter`, else −1. |
| `status` | RelicStatus. |
| `used_up` | `IsUsedUp`. |
| `stack` | `StackCount`. |

**potions** (row = slot; an empty slot is a zero row)

| Column | Meaning |
| --- | --- |
| `potion_id` | Potion vocab id. |
| `usable_now` | 1 exactly when a UsePotion action exists for the slot now: the combat rule of `LegalActionGenerator.FillPotions` at a combat decision point, or `CanUsePotionOutOfCombat` outside combat (Victory/Defeat excluded); 0 while a prompt is pending. |
| `target_type` | TargetType. |

**powers_player** (visible powers only, in list order)

| Column | Meaning |
| --- | --- |
| `power_id` | Power vocab id. |
| `amount` | `DisplayAmountToPlayer`. |
| `is_debuff` | `TypeForCurrentAmount == Debuff`. |

**orbs** (queue order)

| Column | Meaning |
| --- | --- |
| `orb_id` | Orb vocab id. |
| `passive` | `PassiveVal(cs)`. |
| `evoke` | `EvokeVal(cs)`. |

**enemies** (row j = `CombatState.Monsters[j]`, the PlayCard and UsePotion target index; a dead or non-enemy slot is a zero row)

| Column | Meaning |
| --- | --- |
| `present` | 1 for a live enemy. |
| `uid` | `CombatId`. |
| `monster_id` | Monster vocab id. |
| `hp`, `max_hp`, `block` | Current values. |
| `flank` | 1 with BACK_ATTACK_LEFT, 2 with BACK_ATTACK_RIGHT. |
| `behind` | 1 when this enemy's attacks hit the SURROUNDED player's back now. |
| `intent_n` | Number of intent rows written. |

**enemy_intents** [j][k] (the next move's intents k < 3)

| Column | Meaning |
| --- | --- |
| `type` | IntentType. Attack = 0, so mask by `intent_n`. |
| `damage` | Per-hit damage after modifiers (`AttackIntent.GetSingleDamage`). |
| `times` | Hit count (`GetRepeats`). |
| `card_count` | `StatusIntent.CardCount`. |

**enemy_powers** [j][k] and **ally_powers** [a][k]

| Column | Meaning |
| --- | --- |
| `power_id` | Power vocab id; visible powers only. |
| `amount` | `DisplayAmountToPlayer`. |

**allies** (row a−1 = `CombatState.Allies[a]`, the live pets; the UsePotion ally target is −2 − a)

| Column | Meaning |
| --- | --- |
| `present` | 1 for a live pet. |
| `monster_id` | Monster vocab id. |
| `hp`, `max_hp`, `block` | Current values. |

**map_nodes** [row][col] (the current act: grid points, start, boss, second boss)

| Column | Meaning |
| --- | --- |
| `point_type` | MapPointType; 0 means no node. |
| `visited` | The node is in `VisitedMapCoords`. |
| `current` | The node is `CurrentMapCoord`. |
| `legal_next` | `MapTravel.GetLegalMapPoints`; Map phase only and not at a prompt. Travel A is the rank of the point by (col, row). |
| `child_mask` | Bit c = an edge to column c of the next row. |
| `quest` | Number of quest marks at the node (FUR_COAT, SPOILS_MAP). |

**rewards** (row i = `RewardsSet.Rewards[i]` = ClaimReward A; a claimed reward is a zero row with reward_type None; written in the Rewards and CardReward phases)

| Column | Meaning |
| --- | --- |
| `reward_type` | RewardType. |
| `amount` | Gold amount. |
| `item_id` | Potion, relic or special-card vocab id (by reward type). |
| `is_card_reward` | 1 for a card reward (its cards stay hidden, H7). |

**card_reward_alts**

Row i holds the CardRewardAlt vocab id of `Alternatives[i]` (= RewardAlternative A).

**shop** (row i = `AllEntries[i]` = BuyEntry A)

| Column | Meaning |
| --- | --- |
| `category` | ShopCategory: card, colorless_card, relic, potion or card_removal. |
| `item_id` | Card, relic or potion vocab id; 0 for removal. |
| `upgrade` | The card's upgrade level. |
| `price` | `GetCost(rs)`. |
| `stocked` | 1 while the entry is in stock. |
| `on_sale` | 1 for the sale card. |
| `affordable` | stocked and price ≤ gold, which is BuyEntry's own rule. |

**rest** (row i = `Options[i]` = ChooseRestOption A)

| Column | Meaning |
| --- | --- |
| `option_id` | RestOption vocab id. |
| `enabled` | 1 when the option can be chosen. |

**event_options** (row i = `CurrentOptions[i]` = ChooseEventOption A, while not finished)

| Column | Meaning |
| --- | --- |
| `key_hash` | Low 32 bits of XXH64(UTF-8 `"<eventId>:<TextKey>"`). |
| `ordinal` | i. |
| `enabled`, `locked`, `proceed` | Option flags. |
| `relic_id` | Relic vocab id of the option's relic. |

**prompt_options** (row i = `Options[i]` = PromptSelect A)

| Column | Meaning |
| --- | --- |
| `item_id` | Card, relic or potion vocab id when the option's ModelId is one, else the `HashText` of the ModelId. |
| `kind_of_item` | PromptItemKind: card, relic, potion, bundle (the Bundle prompt, whose item_id is the hash of the comma-joined card list) or index. |
| `upgrade` | `UpgradeLevel`. |
| `picked` | The 1-based position of i in `PendingPicks`; 0 = not picked. |
| `ordinal` | i. Draw-pile prompts list their options as the game does: sorted by (rarity, id, pile index). |

**aux** (float32, for logging and shaping; never part of the reward)

| Column | Meaning |
| --- | --- |
| `total_floor`, `act_index`, `hp`, `max_hp`, `gold`, `deck_n`, `relic_n`, `is_combat` | The same values as in `global_i`. |
| `bosses_killed`, `combats_won` | Monster, Elite and Boss rooms in the floor history. The current floor's last room is excluded while its combat runs, or after a Defeat when that room is a combat room. |

**last_episode**

`ObservationEncoder.FillEpisodeSummary(RunSim, Span<int>)` fills floor, act, final_hp, final_max_hp and
last_encounter_id (the last Monster, Elite or Boss room in the history). VecEnv writes win, steps and end_kind.

### Encoder tests (`sim/Sts2Sim.Tests/VecTests.Encoder.cs`, class `VecEncoderTests`)

| Test | What it checks |
| --- | --- |
| `IsPure` | 1,000 states from 60 random boosted runs across all characters and A0/A10. Each must keep its hash after encoding, encode to identical bytes twice, and hold no `?` id in any id column. |
| `LeakTest_DrawOrder` | 600 combat points whose draw pile was reshuffled in a clone. The encoded bytes must be identical. |
| `ParityWithServer` | 300 states (252 in combat). hp, max_hp, gold, energy, max_energy, block, floor and act must match the play server's `ObservationBuilder` JSON. So must the hand's ids, upgrades, costs, playability and hits, and the enemies' slot, uid, id, hp, max_hp, block and intents (type, damage, times, card_count). Also compared: incoming danger and every effective damage against every live enemy (751 values). |
| `NoMoveIds` | Greps the encoder source for move, AI and RNG identifiers, and checks that the intent rows match the intents' types. |
| `TreasureAndCardRewardHidden` | 5 closed and then opened chests, and 74 card rewards that are hidden on the Rewards screen and shown in the CardReward phase. |
| `TablesRespectCaps` | A 110-card deck sets `TABLE_OVERFLOW`, writes exactly 96 rows in pickup order and reports `deck_n = 110`. An ordinary state sets no overflow, and its draw counts sum to the pile size. |

## Python API (§D6)

```python
from sts2vec import VecEnv, PoolSpec
with VecEnv(1024, threads=8, pool=PoolSpec(characters=("IRONCLAD",), count=256)) as env:
    obs = env.reset()                                   # dict of env-owned (N, *shape) arrays (views)
    obs, reward, terminated, truncated, status, info = env.step(actions)   # info: episode_id, step_index, start_entry, last_episode
    env.pool.add_fresh("SILENT", seed="S1"); env.pool.add_save(path); len(env.pool)
    e = env.snapshot(0); env.restore(3, e); env.state_hash([0, 3]); env.candidates(0)
```

`env.schema` is the parsed JSON (`schema["hand"].fields`, `schema.vocab.cards[i]`, `schema.vocab.cards.index_of(id)`,
`schema.vocab.enums["RunActionKind"]`). Buffers: `env.buffers[name]`.

## Known gaps / notes for owners

- `Clone()` shares the pre-finished room object (`Pending.Load`) of a save import between clones (inventory §1.1).
  Handled in `StartPool`: a save that imports to a pending prompt is re-imported per start. A *snapshot* of a slot that
  started from such a save and is still at that load prompt would still share it (not observed).
- **Clone fidelity (Core, not fixed here):** `RunSim.Clone()` does not preserve the aliasing between a claimed
  `PotionReward.Potion` and the same potion in the belt (`PotionReward` clones its potion with `CloneFor`). After a
  clone, discarding that potion while the rewards screen is still open flags only the belt copy
  (`HasBeenRemovedFromState`), so `clone.Step(a)` and `original.Step(a)` hash differently. No gameplay effect found (the
  reward copy is not a listener), but snapshot/restore and template clones are hash-equivalent to the original only up
  to this flag. Tests compare clones with clones (or with a second materialisation of the start entry).
- `hand` has no star-cost or affliction-amount column (not in §D3.1).
- The encoder fails closed: an exception from a Core call (intent damage, card preview, potion rules) ends the
  episode as `SIM_ERROR`/`CONTENT_GAP` instead of encoding a fallback (the play server falls back to the intent type).
- `rewards` rows are indexed by `RewardsSet.Rewards` index (ClaimReward A), so a claimed reward leaves a zero row inside
  the table; `reward_n` counts the unclaimed ones.
