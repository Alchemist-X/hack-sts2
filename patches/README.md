# Patches

## Current isolated worker build

`scripts/build_selection_mcp.sh` materializes upstream
[STS2MCP](https://github.com/Gennadiyev/STS2MCP) at the `commit` in
`config/sts2mcp.lock.json` (`55e06485`, v0.4.0), applies the lock's `patches`
list in order (each sha256-checked, `patch --fuzz=0`), builds against the game
build the lock pins (`game`: v0.111.0 / `41cef1ea`, sts2.dll sha256 per
assembly dir) and finishes with the headless/port rewrite
(`scripts/McpHeadlessPatcher`). The output is reproducible (SourceLink off,
PathMap), so the sandbox DLL sha256 is checked against
`sandbox_build.dll_sha256`.

| order | patch | origin |
|---|---|---|
| 1 | `sts2mcp-selection-state.patch` | this repo |
| 2 | `sts2mcp-rule-evidence.patch` | this repo |
| 3 | `sts2mcp-action-receipts.patch` | hack-sts2-cloud `main@b8a85286` |
| 4 | `sts2mcp-seeded-standard-embark.patch` | hack-sts2-cloud `main@b8a85286` |
| 5 | `sts2mcp-eval-api.patch` | hack-sts2-cloud `main@b8a85286` (commit `3ef07d8c`) |
| 6 | `sts2mcp-v0111-compat.patch` | this repo, game v0.107.1 -> v0.111.0 |

Worker and benchmark pools only. Never install any of these into the human
game (`scripts/mcp_mod.sh` handles the human game's own MCP).

`sts2mcp-action-receipts.patch` adds request IDs, bounded duplicate-request
protection, public personal-turn numbers and action-completion receipts. It
tracks card action completion and direct end-turn advancement, gated by queue
and UI readiness. It never selects cards, targets, rewards or routes.

## sts2mcp-seeded-standard-embark.patch

Lets `menu_select embark` start a **seeded standard run** for the fixed-seed
benchmark (`scripts/bench/run_bench.py`). The stock game only takes seeds in
custom mode. With the patch, when the lobby is in `GameMode.Standard` and a
seed is given, the MCP sets the game's own
`NGame.Instance.DebugSeedOverride = SeedHelper.CanonicalizeSeed(seed)` (the
hook the game's AutoSlayer uses) and then clicks embark; an unseeded embark
clears the override. The reply carries `"seed"` and
`"seed_mechanism": "NGame.DebugSeedOverride"`. The runner verifies the seed the
game used from `Embarking on a singleplayer ... Seed: X` in the game log and
`rng.seed` in `current_run.save`.

## sts2mcp-eval-api.patch

Adds `/api/v1/eval` (new file `McpMod.Eval.cs` plus one route in
`McpMod.cs`) so the benchmark harness can configure, start, observe, stop and
checkpoint runs with verifiable calls instead of menu clicking. It drives menus
and reads state only; it never chooses cards, targets, rewards or routes.
Singleplayer only (409 during multiplayer). Long operations (start, abandon,
checkpoints, act/batch) are serialized; overlapping calls get an error.

**Snapshot** `GET /api/v1/eval?wait_settled_ms=0..120000&stable_ms=200&include_state=true`
(or POST action `snapshot` with the same fields): `schema` `sts2-eval-snapshot`,
`state_version` (monotonic; bumped when the sha256 state fingerprint changes),
`state_fingerprint`, `state_type`, `settled`, `busy`/`busy_reason`
(`scene_transition`, `run_save_in_progress`, `checkpoint_write_in_progress`,
`run_scene_loading`, `action_queue_not_empty`, `action_executing`,
`combat_ending_awaiting_rewards`, ...), `waited_ms`, `run` (in_progress,
character, seed, ascension, game_mode, modifiers, act/act_id/acts, floor,
act_floor, map_coord, room, is_game_over, run_time_s, hp, gold), `refs` (deck
`d<i>` with card id/upgrades, hand `card_uid`/`deck_index`, enemies
`combat_id`/`entity_id`), `pending_epoch_ids`, `run_result`, `eval_meta` (mod
version, settings, `rng` counters per stream) and `state` (the normal
`/api/v1/singleplayer` state with `card_uid`/`deck_index` on hand cards).

**POST actions** (`{"action": ...}`):

| action | parameters | result |
|---|---|---|
| `start_run` | `character` (IRONCLAD, SILENT, REGENT, NECROBINDER, DEFECT), `ascension` 0-10, `seed` (canonicalized like the game: upper case, O->0, I->1), `mode` `standard`\|`custom`, `modifiers` (custom only), `settings` (as `configure`), `abandon_existing`, `require_seed` (default true), `timeout_ms` | Validates everything before touching a run, then drives main menu -> singleplayer -> standard/custom -> character -> ascension -> embark (standard seeds via `NGame.DebugSeedOverride`, custom via the lobby seed), waits for a settled first frame and returns `verified`, `mismatches`, `requested`, `readback` (from live run state), `seed_mechanism`, `settings`, `steps`, `abandoned_previous`. |
| `configure` | `fast_mode` `normal`\|`fast`\|`instant` (instant needs `allow_experimental`), `screenshake` 0-4, `text_effects`, `skip_tutorials`, `persist` (default false: settings.save untouched) | `changed`, `settings`, `logic_impact`. Also applied at startup from env `STS2_MCP_EVAL_SETTINGS` (JSON). None is read by game logic or the gameplay RNG. |
| `abandon` | `return_to_menu` (default true), `timeout_ms` | Abandons the live run or the saved run from the menu, returns its `run_result` (`cause` `abandoned`). |
| `return_to_menu` | `timeout_ms` | Save-and-quit to the main menu (`has_saved_run`). |
| `run_result` | - | Summary of the finished run (`victory`, `abandoned`, `cause` (`victory`, `abandoned`, `encounter:<id>`, `event:<id>`), `score`, character/seed/ascension/mode/modifiers, act/floor, hp, gold, deck, relics, potions, `run_time_s`, `history_available`, `source`); `status: in_progress` while the run is not over. |
| `checkpoint_save` / `checkpoint_restore` / `checkpoint_list` | `name` `[A-Za-z0-9_-]{1,64}`, `timeout_ms` | Copies the game's own run autosave (written on room entry) to `mcp_checkpoints/<name>`; restore returns to the menu, puts it back and continues it, then checks seed/ascension and the resume floor. Granularity is the room start, exactly like the game's Continue. |
| `act` | `game_action` (a `/api/v1/singleplayer` POST body), `expect_state_version`, `expect_state_type`, `wait_settled_ms`, `stable_ms` | One action, then waits until settled; per-step record with versions before/after and `changed`. |
| `batch` | `steps` (<= 50 action bodies, each may carry `expect_state_version`/`expect_state_type`), `stop_on_no_change` (default true), `wait_settled_ms`, `stable_ms`, `include_state` | Step by step, verifying and settling after each; stops at the first error/stale version/no-change/unsettled step. |

Not automated: revealing pending timeline epochs (reported as
`pending_epoch_ids`), multiplayer.

## sts2mcp-v0111-compat.patch

Compile drift between game v0.107.1 and v0.111.0 for the stack above; each
change mirrors the engine's own v0.111.0 call or keeps the old output schema:

1. `SerializableRunRngSet.Counters` became `Rngs` (`RunRngType ->
   SerializableRng{counter, s0..s3}`); `eval_meta.rng.counters` stays name -> int.
2. `SaveManager.IncrementNumReloads(save, bool)` became `(save, NetGameType)`;
   `checkpoint_restore` passes `NetGameType.Singleplayer` like `RunManager`.
3. `Hook.ModifyDamage` gained `CardPlay? cardPlay`; the preview passes `null`
   like the engine's own `DamageVar` preview.
4. `StartRunLobby.MaxPlayers` became the private `_maxPlayers` (reflection read).
5. `LoadRunLobby.ConnectedPlayerIds` became `Players`/`PlayerIds`/`PlayerCount`.

Unpatched upstream does not compile on v0.111.0 (items 4 and 5), which is why
`scripts/build_upstream_mcp.sh` only accepts the game build of its historical
artifact (v0.107.1).

## sts2mcp-0.4.0-v0107.1-compat.patch (historical)

Makes STS2MCP v0.4.0 (commit `20eadeb`) compile against game v0.107.1
(upstream issue #114). Three drift families:

1. `CombatManager.IsPlayPhase` (removed) → per-player
   `player.PlayerCombatState?.Phase == PlayerTurnPhase.Play`
2. `Creature.CombatState` is now the `ICombatState` interface
3. `MerchantRoom.Inventory` (removed) → per-player `Inventories` +
   `GetLocalInventory()` (guarded by `Count > 0` to preserve null-until-entered
   semantics)

Upstream `55e06485` (the lock commit) already contains equivalent fixes. Used
for Level-2 recorder verification (`scripts/level2_run.sh`).
