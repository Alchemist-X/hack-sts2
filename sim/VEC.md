# Sts2Sim.Vec — batched training interface

Status: **scaffold** (wave E2). Shapes, names, buffer order, capacities and export signatures are final
(CONTRACTS.md Part D); the encoder (`ObservationEncoder`, owner V1) and candidate table (`CandidateTable`, owner V2)
are zero-filling stubs, so every observation table except `global_i.phase/decision_kind/is_combat` reads 0 and
`cand_n` is 0. Fields marked **TODO(V1)** below are written by V1.

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
  entries: character `characters[i % len]`, seed `"<seed_prefix>-<character>-<i>"`.

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
| 0 | `global_i` | int32 | [58] | phase, character, ascension, act_index, act_id, total_floor, act_floor, map_row, map_col, hp, max_hp, gold, energy, max_energy, stars, block, turn, round, potion_slots, deck_n, draw_n, discard_n, exhaust_n, orb_slots, relic_n, hand_n, enemy_n, ally_n, boss_id, second_boss_id, card_removals_used, removal_price, incoming_damage, incoming_hits, prompt_kind, prompt_min, prompt_max, prompt_picked, prompt_needs_confirm, prompt_can_cancel, prompt_can_skip, prompt_ordered, prompt_option_n, prompt_pile, prompt_site_hash, reward_n, card_reward_can_skip, card_reward_skip_consumes, treasure_opened, treasure_relic_id, treasure_claimed, event_id, event_finished, event_option_n, rest_option_n, shop_entry_n, decision_kind, is_combat. Stub writes phase, decision_kind, is_combat; **TODO(V1)** the rest |
| 1 | `global_f` | float32 | [4] | hp_frac, gold_log1p, floor_frac, incoming_net_hp — **TODO(V1)** |
| 2 | `hand` | int32 | [10, 22] | card_id, upgrade, cost_current (−1 X, −2 unplayable), cost_base, type, target, rarity, keyword_mask (bit = 1 << CardKeyword), enchant_id, enchant_amount, affliction_id, playable, playable_reason_id, hits, var_id_0..3, var_val_0..3 — **TODO(V1)** |
| 3 | `hand_f` | float32 | [10, 2] | block_eff, damage_eff_self_or_all — **TODO(V1)** |
| 4 | `hand_dmg` | float32 | [10, 6] | vs_enemy_0..5 — **TODO(V1)** |
| 5 | `deck` | int32 | [96, 5] | card_id, upgrade, enchant_id, enchant_amount, affliction_id — **TODO(V1)** |
| 6–8 | `draw_counts`, `discard_counts`, `exhaust_counts` | int16 | [V_cards × 2] | multiset counts — **TODO(V1)** |
| 9 | `relics` | int32 | [32, 5] | relic_id, counter, status, used_up, stack — **TODO(V1)** |
| 10 | `potions` | int32 | [10, 3] | potion_id, usable_now, target_type — **TODO(V1)** |
| 11 | `powers_player` | int32 | [24, 3] | power_id, amount, is_debuff — **TODO(V1)** |
| 12 | `orbs` | int32 | [10, 3] | orb_id, passive, evoke — **TODO(V1)** |
| 13 | `enemies` | int32 | [6, 9] | present, uid, monster_id, hp, max_hp, block, flank, behind, intent_n — **TODO(V1)** |
| 14 | `enemy_intents` | int32 | [6, 3, 4] | type, damage, times, card_count — **TODO(V1)** |
| 15 | `enemy_powers` | int32 | [6, 12, 2] | power_id, amount — **TODO(V1)** |
| 16 | `allies` | int32 | [2, 5] | present, monster_id, hp, max_hp, block — **TODO(V1)** |
| 17 | `ally_powers` | int32 | [2, 8, 2] | power_id, amount — **TODO(V1)** |
| 18 | `map_nodes` | int32 | [18, 7, 6] | point_type, visited, current, legal_next, child_mask, quest — **TODO(V1)** |
| 19 | `rewards` | int32 | [8, 4] | reward_type, amount, item_id, is_card_reward — **TODO(V1)** |
| 20 | `card_reward` | int32 | [4, 22] | hand columns (playable, playable_reason_id, hits = 0) — **TODO(V1)** |
| 21 | `card_reward_alts` | int32 | [2] | alt_id (CardRewardAlt) — **TODO(V1)** |
| 22 | `shop` | int32 | [14, 7] | category, item_id, upgrade, price, stocked, on_sale, affordable — **TODO(V1)** |
| 23 | `rest` | int32 | [9, 2] | option_id, enabled — **TODO(V1)** |
| 24 | `event_options` | int32 | [8, 6] | key_hash, ordinal, enabled, locked, proceed, relic_id — **TODO(V1)** |
| 25 | `prompt_options` | int32 | [128, 5] | item_id, upgrade, picked, kind_of_item (PromptItemKind), ordinal — **TODO(V1)** |
| 26 | `cand` | int32 | [192, 9] | kind, A, B, hand_slot, target_slot, potion_slot, option_index, card_id, target_uid — **TODO(V2)** |
| 27 | `cand_n` | int32 | [] | candidates (0 when terminal) — stub 0, **TODO(V2)** |
| 28 | `reward` | float32 | [] | 1 at a Victory terminal, else 0 |
| 29 | `terminated` | uint8 | [] | episode ended at Victory/Defeat |
| 30 | `truncated` | uint8 | [] | step cap, content gap or sim error |
| 31 | `status` | int32 | [] | OK 0, NOOP 1, ILLEGAL_ACTION 2, CONTENT_GAP 4, SIM_ERROR 8, CANDIDATE_OVERFLOW 16, TABLE_OVERFLOW 32, RESET 64, POOL_EMPTY 128 |
| 32 | `episode` | int64 | [3] | episode_id, step_index, start_entry |
| 33 | `aux` | float32 | [10] | total_floor, act_index, hp, max_hp, gold, bosses_killed, combats_won, deck_n, relic_n, is_combat — **TODO(V1)** |
| 34 | `last_episode` | int32 | [8] | win, floor, act, steps, final_hp, final_max_hp, end_kind (EndKind), last_encounter_id |

35 buffers (D3.1 numbers its rows 0–33 and lists `card_reward_alts` inside row 20). ≈ 26.7 KB per env.

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

- `Clone()` shares the pre-finished room object (`Pending.Load`) of a save import between clones (inventory §1.1);
  V2 should confirm it is never mutated before relying on save entries under parallel resets.
- `hand` has no star-cost or affliction-amount column (not in §D3.1).
