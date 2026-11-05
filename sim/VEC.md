# Sts2Sim.Vec — batched training interface

> Policy authors: read `sim/ENV_SPEC.md` first (one page, generated column tables, metric, `sts2vec eval`). This page
> documents the implementation.

Status: **integrated** (wave E2, 2026-11-01; `sim/specs/integration.md` "E2 integrate"). Shapes, names, buffer
order, capacities and export signatures are final (CONTRACTS.md Part D). The player-view encoder
(`ObservationEncoder*.cs`, V1) fills every observation table (see the field reference below); `VecEnv`, `StartPool`,
`CandidateTable` and the exports (V2) step, reset, snapshot and restore slots; the Python package `sts2vec` (V3) loads
the library through hostfxr (or a NativeAOT build) and wraps it. `encoder_version` `3b73317739c650bd`, 35,062
bytes/env, 43 buffers (E3 train-smoke appended 4 `aux` columns for the protocol progress metric; the floor stall cap
appended `EndKind.FloorStall`; the player-view schema append of 2026-11-03 appended 6 buffers, "Appended buffers"
below; the rewards-screen macros of 2026-11-04 appended `reward_choices` and two candidate kinds, "Rewards-screen
macros"; the map-routing features of 2026-11-04 appended `map_routes`, and the static card table `card_static` joined
the schema JSON (not a buffer); integration figures below were measured at
`dff07a6bc984b119`, 26,710 bytes/env). Measured at integration: 116k env-steps/s (C# bench) and 73k (Python `sts2vec bench`) at
N = 1024 / 8 threads, A0, random policy (§D7 target ≥ 50k); full grid in `sim/BENCH.md` "E2 integrate".

ABI 2 (2026-11-04, decision-time search): `sts2vec_abi_version`, `sts2vec_branch`, `sts2vec_branch_many`,
`sts2vec_restore_many`, `sts2vec_pool_release` (21 exports; the observation schema and `encoder_version` are unchanged).
See "Search".

## Build and load

```bash
dotnet build sim/Sts2Sim.sln -c Release          # → sim/Sts2Sim.Vec/bin/Release/net9.0/Sts2Sim.Vec.dll
uv run --project sim/tools/vec pytest -q
uv run --project sim/tools/vec sts2vec check
uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500
scripts/vec_publish_aot.sh                       # optional NativeAOT .dylib/.so (STS2VEC_AOT_LIB)
```

(`pytest -q` from the repo root is scoped to `sim/tools/vec` by the package's pytest plugin; see Python API.)

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
- Start sequence: the live pool entry at position `(slot_seed + episodes_started_before) mod pool_count` in id
  (= insertion) order; `pool_count` counts live entries, so while nothing was released (ABI 2 `pool_release`) this is
  entry `(slot_seed + episodes_started_before) mod pool_count`.
- `reset(mask, start_ids)`: masked slots restart (explicit entry, or the sequence when `< 0`); their observation and
  signals (reward 0, terminated 0, truncated 0, status `RESET`, episode) are written; other slots are untouched.
  A start id that is not a live entry (never issued, or released) fails the whole call (-1) before anything changes.
  NULL mask = all slots; NULL start_ids = all from the sequence.
- `step(actions)`: `a < 0` → `NOOP`; `a ∉ [0, cand_n)` (or no run / finished) → `ILLEGAL_ACTION`, slot unchanged;
  else candidate `a`: `RunSim.StepFast(action)` (`RunSim.Step` with `fast_path: false`; see "Fast path"), or for a
  rewards-screen macro its two actions in turn ("Rewards-screen macros"; one decision for `step_index` and the caps). Terminal → `terminated`, reward 1 iff Victory; `step_index >= step_cap` →
  `truncated`; floor stall (below) → `truncated` + `FLOOR_STALL`; NotImplemented/NotSupported → `CONTENT_GAP`, other
  exceptions → `SIM_ERROR` (episode ends with `truncated = 1`, reward 0, run discarded, text in
  `sts2vec_last_error(h, slot)`). An encoder exception on a live state ends the episode the same way. Then every
  slot's observation, candidates and signals are written.
- Floor stall cap (`config.floor_step_cap`, default 80 = `VecConfig.DefaultFloorStepCap` = Python
  `sts2vec.FLOOR_STEP_CAP`; 0 = off): `VecSlot.FloorSteps` counts the decisions (`a >= 0` on a live slot, legal or
  illegal; no-ops are not decisions) taken at non-combat points (`RunState.Combat.IsInProgress` false, the encoder's
  `is_combat`) since `RunState.TotalFloor` last changed (`VecSlot.FloorSeen`, set at every start/restore and after
  every applied action). After a step whose resulting state is a non-combat point with `FloorSteps >= cap`, the
  episode ends: `truncated = 1`, reward 0, status `FLOOR_STALL` (256), `last_episode.end_kind` = FloorStall (5), run
  kept (like `step_cap`), auto-reset as for `step_cap`. Precedence: terminal, `step_cap`, stall. This is exactly the
  protocol evaluator's `stuck` rule moved to the step that produces the state it would refuse to decide on (the
  evaluator used to count per observation in Python; `sts2vec eval` now passes its cap to the env and maps
  `FLOOR_STALL` to `stuck`). An illegal index is the one change to the "slot unchanged" rule: the run and the
  observation are unchanged, but the decision counts toward the cap (as it did in the evaluator), so a policy stuck on
  illegal indices at a non-combat point is truncated too. Tests: `VecTests.Test_VecEnv_FloorStepCapTruncatesShopStall`
  (shop BuyEntry(removal) → PromptCancel loop cut at exactly 80 and 7, auto-reset on/off, cap 0, illegal/no-op
  counting), `Test_VecEnv_FloorStepCapIgnoresCombat`; Python `test_floor_cap.py` (the env against the evaluator rule
  recomputed from observations over 64 games × 900 steps with a shop-stall policy: 248 stalls, every one at the same
  step; the C# default through the raw ABI).
- Auto-reset (default): a finished episode writes `last_episode`, then the slot restarts from its sequence in the same
  call (`RESET` bit, new `episode_id`, the new episode's first observation). A slot whose start failed retries the
  next sequence entry on the following step. With `auto_reset = false` the slot stays terminal (`cand_n = 0`).
- Pool: fresh entries create their template with `RunSim.NewRun` on first use and cache it (up to `pool_cache`
  templates); `sts2vec_pool_warm` (`env.pool.warm()`) creates them ahead of time, in id order, on the env's worker
  threads (the same templates, so no start changes). Template creation runs under a per-entry lock; the `Clone()` a
  start takes runs outside it (a template is never stepped, so starts from one entry clone it concurrently;
  `VecTests.Test_VecBranch_ParallelClonesOfOneSourceAreIdentical`). `sts2vec_pool_release` removes entries ("Search"). Save
  entries use `RunImporter.FromSave` (checked) with the env's content filter. Snapshots hold `Clone()` of a slot
  (hash-faithful: a clone steps exactly like its source). `config.pool.fresh` with `count = K` adds K
  entries: character `characters[i % len]`, seed `"<seed_prefix>-<character>-<i>"` (interleaved, so any prefix of the
  pool covers every character). Beyond `pool_cache` templates a fresh entry runs `NewRun` on every start (≈ 16 ms).
- Save entries whose import stops at a prompt (a choice during the load) are re-imported from the parsed save on every
  start instead of cloned, because `RunSim.Clone` shares the pending load room (`Pending.Load`) between clones. The
  fixture save and every save point verified so far import without a pending prompt, so they clone normally.
- Threads: `Parallel.For(0, N, MaxDegreeOfParallelism = threads)`; a slot is touched by one worker per call; the pool's
  entry table is lock-protected and each entry's template creation runs under its own lock (clones of one template, and
  the clones of one branch source, run concurrently: `RunSim.Clone` only reads its source). Verified: 256
  envs × 300 steps on 1 vs 8 threads and two instances give byte-identical buffers after every step and equal
  `StateHash` per slot; 1,024 envs × 200 steps on 1 vs 12 threads likewise.
- Per-slot scratch: `VecSlot.Raw` (`List<RunAction>`, capacity 64) is refilled by `RunSim.LegalActions(list)` after
  every change and `VecSlot.Legal` by `RewardMacros.Fill` (the candidate list, = `Raw` off the rewards screen); `Legal`
  is the action table `step` indexes; no per-step allocation in VecEnv beyond the sim's own (rewards screens add the
  Core allocations of `CardRewardAlternative.Generate`).
- Test hook: `VecEnv.FaultInjector = (slot, action) => Exception?` throws the returned exception in place of
  `RunSim.Step` (classification and auto-reset apply). Null in production. `VecEnv.ForceFastPath = (slot, action) =>
  bool` forces the clone-free step without the predicate (exercises the fail-closed path). Null in production.

### Fast path (clone-free stepping, `config.fast_path`, default true)

`RunSim.Step(a)` deep-clones the run before every action so that a choice prompt can be replayed from that basis with
the answer tape. Most actions cannot prompt, so `RunSim.StepFast(a)` asks `RunSim.MayPrompt(a)` first and, when it is
false, runs `Step(a, FastPathAnswerer.Instance)` without the clone. Outputs are byte-identical to `fast_path: false`.

- **Table** (`sim/Sts2Sim.Core/Choice/PromptSites.g.cs`, generated by `python3 sim/tools/inventory/gen_prompt_sites.py`;
  `--check` fails when stale, `--list` prints every site): scans every model def in Core for calls that can reach
  `ChoiceRuntime.Ask` (`CardSelectCmd.From*`, `RelicDeckPromptsR91.From*`, `RoomPrompts.From*`, `EventKit3456` prompts,
  `Choice.Ask`, `new ChoicePrompt`), for kernel entry points that can (relic obtain, merchant purchase, reward claim,
  rest/event options, room and act transitions), for auto-play (`CardCmd.AutoPlay`, `AutoPlayFromDrawPile`) and for
  Sly grants (a discarded Sly card is auto-played by the kernel). A closure adds models that reference a hazardous
  power, monster, relic, enchantment, affliction, orb or modifier (by class name, id literal or id const), at the member
  holding the reference (e.g. `TOOLS_OF_THE_TRADE` applies `TOOLS_OF_THE_TRADE_POWER` in OnPlay). Each model gets a
  trigger class: card on play / anywhere, potion on use, relic on obtain / while owned, power, monster, enchantment,
  affliction, orb, modifier (anywhere), event. Any hazard call outside a model def must be a reviewed kernel file
  (`KERNEL_FILES`), and every decompile prompt record in `sim/specs/inventory/*.classified.json` whose opener is ported
  must be in the table, else generation fails. Today: 153 hazardous models of 1,371 (60 cards, 33 relics, 10 potions,
  10 powers, 2 monsters, 1 enchantment, 37 events) + 4 room sources (SMITH, COOK, MERCHANT_CARD_REMOVAL,
  COMBAT_REWARD_CARD_REMOVAL); 185 inventory records cross-checked.
- **Predicate** (`sim/Sts2Sim.Core/Run/Loop/RunSim.FastPath.cs`; `FastPathBlocker(a)` returns the reason): may prompt
  when a prompt is pending; out of combat, when an event room is on the room stack; when any "anywhere" hazard is alive
  (a run modifier, an owned relic/potion, a card in the deck or a combat pile or its enchantment/affliction, a power on
  any creature, a monster, an orb); in combat when the played card or the used potion is hazardous, or a hazardous card
  is Sly this turn; out of combat for travel to anything but a shop/rest site/treasure, claiming any reward but gold, a
  potion or opening a card reward, a reward alternative, closing a terminal rewards screen after a boss or with a
  continuation relic, buying, choosing a rest option, taking a treasure relic, using a hazardous potion. A combat step
  never moves the room stack (a combat room's victory always pushes a rewards set), so events and act transitions are
  handled on the out-of-combat steps.
- **Fail-closed**: a prompt on the clone-free path reaches `FastPathAnswerer`, which throws `FastPathPromptException`
  (an `InvalidOperationException` naming the action, site and source); the run is poisoned (`IsPoisoned`; `Step` and
  `Clone` throw) and Vec ends the episode as `SIM_ERROR` with the message in the slot's last error. Nothing is ever
  answered on that path. `VecSlot.FastSteps` / `SlowSteps` and `VecEnv.FastSteps` / `SlowSteps` count the two paths.
- **Tests**: `ChoiceFastPathTests` (2,000-run soak, 5 characters x A0/A10 x uniform / play-biased / 999-HP play-biased:
  every prompt's `SourceId` is in `PromptSites.PromptSourceIds` and no prompt follows `MayPrompt == false`; 20,000+
  sampled clone-free steps give the same `StateHash` as the tape engine on a clone; a forced clone-free SURVIVOR play
  throws and poisons), `VecTests.Test_Vec_FastPathMatchesCloningPath` (on/off byte-identical buffers and hashes, A0 and
  A10), `VecTests.Test_Vec_FastPathPromptIsSimErrorNotSilent`.
- **Measured** (`sim/BENCH.md` "Vec fast path"): 82–83 % of random-policy Vec steps take the fast path; a RunSim step
  drops from 13–15 µs to ≈ 7.8 µs single-threaded (1.6–1.9x), but a Vec env-step is ≈ 80 µs of CPU dominated by
  observation encoding and candidates, so 1024 x 8 gains +7–8 % env-steps/s, −9–10 % CPU per env-step and ≈ 3x fewer
  gen0/gen1 collections (loaded host; medians of 3).
- **After porting content**: rerun the generator (the gates' `--check` catches a stale table); a new prompt path the
  scan cannot see shows up as a soak failure or, in production, as a `FastPathPromptException` SIM_ERROR, never as a
  wrong answer.

### Candidate table (`cand`, `CandidateTable.Fill`)

Row `r < cand_n` is candidate `r` of the Vec candidate list: `RunSim.LegalActions()[r]` (same order) on every screen
but the rewards screen, where `RewardMacros.Fill` substitutes the macro rows (next section); rows `>= cand_n` are zero.
`cand_n = min(candidates, 192)`; more candidates set `CANDIDATE_OVERFLOW` and only the first 192 are selectable
(constructible: a PileSelect over a 250-card draw pile, or the merchant card removal with a 200-card deck).
`CandidateTable.ToAction(row)` inverts a row to its (kind, A, B); `RewardMacros.ExpandAll` gives the RunActions a macro
row runs.

Overflow consequences (tested by `VecTests.Test_VecEnv_PromptTableOverflow` and `Test_VecEnv_RemovalPromptOverflow`):

- A step index at or after `cand_n` is `ILLEGAL_ACTION` and leaves the slot unchanged, even when `LegalActions()` lists
  an action there. On a prompt the picks come first and `PromptConfirm` / `PromptCancel` last, so past the cap the
  control actions are not selectable (a 200-card DeckRemove cannot be cancelled); a pick still leads to a state whose
  confirm is listed (after the pick: `[confirm, cancel]`).
- `prompt_options` holds the first 128 options (`TABLE_OVERFLOW`; `prompt_option_n` keeps the true count). Candidates
  for options 128 and up stay selectable: their `option_index` points past the table, so read the option's identity
  from the candidate row's `card_id`. A pick of such an option shows only in `prompt_picked`, not in a `picked` column.

| Column | Value (−1 = does not apply unless stated) |
| --- | --- |
| `kind`, `A`, `B` | the `RunAction` fields verbatim (`RunActionKind` value; A/B as in CONTRACTS.md §C1, −1 = none) |
| `hand_slot` | A for `PlayCard` (index into `hand`) |
| `target_slot` | B for `PlayCard` / `UsePotion` when B ≥ 0 (index into `enemies`); −1 for self/none (B = −1) and for pets (B ≤ −3 = `Allies[-2 - B]`: read B) |
| `potion_slot` | A for `UsePotion` / `DiscardPotion` (index into `potions`) |
| `option_index` | A for `Travel` (rank of the destination among legal points sorted by (col, row)), `ClaimReward`, `PickRewardCard`, `RewardAlternative`, `BuyEntry`, `ChooseRestOption`, `ChooseEventOption`, `PromptSelect` (index into `prompt_options`); for the macros `TakeRewardCard` / `TakeRewardAlternative` the `reward_choices` row (the k-th macro row of the list points at row k) |
| `card_id` | cards vocab id (0 = none) of the played hand card, of the picked card-reward card (`PickRewardCard`, `TakeRewardCard`), or of a prompt option that is a card (not for Bundle prompts; on grid prompts only ids that are not also relic/potion ids) |
| `target_uid` | `CombatId` of the targeted monster (= `enemies.uid`), 0 = none |

### Rewards-screen macros (deck-building channel, 2026-11-04; `RewardMacros.cs`)

Motivation: the PPO policy of 2026-11-03 never opened a card reward (995 rewards screens, 0 picks): on the old list a
card needed two decisions (open, then pick) and the cards were invisible until the first one. On the rewards screen
(loop phase Rewards, no pending prompt) the candidate list is now `RunSim.LegalActions()` with two substitutions:

- `ClaimReward(r)` of an unclaimed card reward becomes one **`TakeRewardCard`** row (kind 21, A = r, B = card index)
  per offered card, then one **`TakeRewardAlternative`** row (kind 22, A = r, B = alternative index) per alternative
  the opened screen would offer other than Skip: REROLL (Driftwood) and SACRIFICE (Pael's Wing), predicted with the
  game's own `CardRewardAlternative.Generate` (pure: its hooks only append to a fresh list). Executing a row runs the
  game's two clicks with `StepFast` each — `ClaimReward(r)` (opens the screen, game `Rewards/CardReward.cs:592-603`),
  then `PickRewardCard(B)` / `RewardAlternative(B)` (`:604-700`) — so states, hashes and replays are the game's; after
  the first click `RewardMacros.RequireOpened` checks the screen of reward r is open with option B on it and otherwise
  throws, which ends the episode as SIM_ERROR (fail closed; never seen). A macro is one decision: `step_index`,
  `step_cap` and the floor stall cap count it once; `FastSteps` / `SlowSteps` count its two RunSim steps.
- `ClaimReward(r)` of a potion reward the game would refuse is dropped: with a full belt `TryToProcure` fails TooFull
  and `OnSelect` returns false, the reward stays unclaimed (`Rewards/PotionReward.cs:180-191`;
  `PotionReward.ClaimWouldBeRefused`). Under Sozu the game consumes the reward without a potion (`:192-194`), so that
  claim stays. Gold, relic, special-card, removal and allowed potion claims keep their one-click `ClaimReward` rows.
- **No Skip row.** The card screen's Skip is `EndSelectionAndDoNotCompleteReward`
  (`Entities.CardRewardAlternatives/CardRewardAlternative.cs:67-73`; `OnSelect` returns `rewardComplete = false`,
  `Rewards/CardReward.cs:660`, `:738`): it closes the screen and the reward stays on the rewards screen, so as a macro it
  would be a no-op loop. Declining a card is `ProceedRewards` (every unclaimed reward is skipped there, `OnSkipped`).
- Kept granular: the CardReward phase (reached after a REROLL, when a reward allows more picks, or from a save) lists
  `PickRewardCard` / `RewardAlternative` (Skip included) as before; every other screen is unchanged. `RunSim`'s own
  legal actions (and so the play server / LLM channel) are unchanged.
- Observation: `reward_choices` (appended buffer 41, CAP_REWARD_CHOICES 16) row k = the k-th macro row's target:
  `reward_index` (= A), `option` (= B), `alt_id` (CardRewardAlt of an alternative row, 0 for a card), then the 22 `hand`
  columns of the offered card exactly as `card_reward` shows it once opened (`playable`, `playable_reason_id`, `hits`
  0). Written in the Rewards loop phase; empty while a card screen is open (then `card_reward` holds the cards).
  More than 16 choices set `TABLE_OVERFLOW`; their macro rows stay selectable with `option_index` past the table (read
  `card_id`).
- Hidden information: this makes H7 weaker on purpose (table below): the cards of an unopened card reward are shown on
  the rewards screen. A human sees them one free click away (open, look, close: the reward stays unclaimed); the one
  side effect of that peek in the game, the reward no longer listening for relics obtained later (`Player.RelicObtained
  -=` after the selection ends, `Rewards/CardReward.cs:703`; sim `_listensForRelics`), does not happen here.
- Candidate-set change: indices recorded before 2026-11-04 (eval `history_actions`, training logs) do not replay on
  this library once a rewards screen is reached; `encoder_version` changed (`5dbfa09dd9dcb5ed` → `e9baaefc39811631`)
  and `RunActionKind` gained two members (21 `TakeRewardCard`, 22 `TakeRewardAlternative`), which is a model input of
  `sim/tools/train`: checkpoints of earlier libraries do not resume on this one.
- Tests (`VecTests.RewardMacros.cs`): `Test_VecCand_RewardMacroTakesAnOfferedCard` (rows, `reward_choices` = the opened
  `card_reward` rows column for column, one decision = ClaimReward + PickRewardCard by hash, deck +1),
  `Test_VecCand_RewardMacroAlternatives` (REROLL leaves the card screen open with new cards; SACRIFICE completes the
  reward; Skip never listed), `Test_VecCand_RewardPotionClaimNeedsAFreeSlot` (full belt: not listed, the game refuses;
  free slot or Sozu: listed), `Test_VecCand_RewardMacroFailsClosed`; T4 `Test_VecCand_MatchesLegalActions` checks every
  macro row against `reward_choices` and steps a shadow run by its expansion (10,000 decision points);
  `VecEncoderTests.Test_VecEncoder_TreasureAndCardRewardHidden` (74 rewards screens: the cards in `reward_choices`).

### Room macros (opt-in, W3 2026-11-04; `RoomMacros.cs`, config `macros`)

Motivation (docs/diag-2026-11-04/q4-loopbreaker.md): at a rest site the policy picked SMITH, then cancelled on the
DeckUpgrade prompt and came back to a byte-identical rest site (173 of u2225's 177 dev-128 loop-breaker interventions;
13 % of rest visits upgraded); the shop's card removal looped the same way. With `config_json` `"macros": ["rest",
"shop"]` (or `"rest,shop"`; Python `VecEnv(macros="rest,shop")`, trainer `--macros rest,shop`) those screens take the
whole choice in one decision. **Off is the default** and lists exactly the earlier candidates (C# test: every buffer
of a `Macros = None` env equals the default env's; Python `make_config` leaves the key out).

- `rest`: at a rest site (loop phase RestSite, no pending prompt) the `ChooseRestOption(A)` row of a
  `SmithRestSiteOption` with `SmithCount == 1` is replaced by one row **`ChooseRestOption(A, B = d)`** per upgradable
  master-deck card d (`CardInstance.IsUpgradable`, the filter of `RoomPrompts.FromDeckForUpgrade`). HEAL and every other
  option stay; a SMITH with `SmithCount != 1` stays raw.
- `shop`: in a shop (loop phase Shop, no pending prompt) the legal `BuyEntry(A)` row of the `MerchantCardRemovalEntry`
  is replaced by one row **`BuyEntry(A, B = d)`** per removable deck card d (`IsRemovable`, the filter of
  `RoomPrompts.FromDeckForRemoval`). The rows exist only when the plain purchase is legal (stocked, enough gold); the
  game charges the entry's price as before.
- Execution (`RoomMacros.Execute`): the game's own three clicks, each through the env's step (`StepFast` / tape path):
  `Kind(A)` opens the DeckUpgrade / DeckRemove prompt (checked: kind and site `SmithRestSiteOption.OnSelect` /
  `OneOffSynchronizer.DoMerchantCardRemoval`), `PromptSelect(j)` of the option whose `InstanceId` is deck card d's,
  `PromptConfirm`. Any divergence throws → SIM_ERROR (fail closed). No cancel path exists; a prompt a hook opens
  afterwards is left to the policy. One decision for `step_index`, `step_cap` and the floor stall cap. States and
  hashes equal the manual three clicks (`Test_VecCand_RoomMacro*`).
- Layout: the macro rows are **appended after the screen's other candidates**, in deck order, so HEAL / purchases /
  leave keep their low indices under the trainer's 64-row candidate cut (`max_rows`; rows past it are not selectable
  in training, counted in `cand_over_max_rows`). Columns: kind = `ChooseRestOption` / `BuyEntry` (unchanged ids),
  A = the rest option / shop entry, **B = the deck row** (`deck` buffer row d; a game action of these kinds always has
  B = -1, so B >= 0 identifies a macro row: `RoomMacros.IsMacro`), `option_index` = A (the SMITH `rest` row / the
  removal `shop` row with its `price`), **`card_id` = the deck card's cards-vocab id** (what the model's candidate
  scorer embeds for every card-bearing row: `c_card(card_emb[card_id])`, plus the deck-copies feature with
  `--deck-counts`; the same path as `TakeRewardCard`). The upgrade level of the card is not a candidate column (B points
  at the deck row, which the model pools but does not gather).
- Schema / spec: **unchanged** — no buffer, enum member or vocab is added, so `encoder_version` is the same with macros
  on or off (3b73317739c650bd for this build) and `features.check_spec_compat` reports identical model inputs, no
  widening (`sim/tools/train/tests/test_train_macros.py`). Any checkpoint (any preset, mid-run) resumes with
  `--macros rest,shop`: the candidate rows have the same shape; its learned `ChooseRestOption` / `BuyEntry` kind and
  rest / shop option-bank features carry over and the card embedding now separates the rows. The meaning shift is
  behavioural only: the old "SMITH" row stood for "open the prompt", the new rows stand for "upgrade this card".
- Replays: `history_actions` index the candidate list, so a game recorded with macros replays only with the same
  macros (`sts2vec.evaluate` records `limits.macros` and its replay audit uses them; `sts2train eval` defaults to the
  checkpoint's `config.macros`). Not covered: the real-game twin (`scripts/live/nn_twin`) and the combat lookahead's
  search env have no macro mapping (a macro-trained model plays them on the raw candidates); the play server / LLM
  channel uses `RunSim.LegalActions()` and is unaffected.
- A config of an older library without the key: `sts2vec_create` fails with `unknown config key "macros"`.
- Tests: C# `VecTests.RoomMacros.cs` (`Test_VecConfig_MacrosParse`, `Test_VecCand_RoomMacroSmithUpgradesOneCard`,
  `Test_VecCand_RoomMacroShopRemovalPaysThePrice`, `Test_VecEnv_RoomMacrosSoak`: 32 play-biased slots x 4000 steps,
  no SIM_ERROR, macros taken in both rooms, never at the SMITH / removal prompt); Python
  `sim/tools/vec/tests/test_room_macros.py`, `sim/tools/train/tests/test_train_macros.py` (no rest / shop cycle in a
  300-step 64-slot random run with macros; the same run without them closes one).

## Bench

```bash
DOTNET_gcServer=1 dotnet run -c Release --project sim/Sts2Sim.Bench -- vec [envs=1024] [threads=8] [steps=500] [ascension=0] [pool=1024]
```

In-process through the real exports (`delegate* unmanaged` → `[UnmanagedCallersOnly]`), NativeMemory buffers, seeded
uniform-random candidate policy, all five characters. Pool templates are warmed before the timed window. Prints
env-steps/s (slot steps with an action ≥ 0), ms per batch (avg/min/max), resets/s, episode ends, error counts, GC mode
and collections, bytes per env. `DOTNET_gcServer=1` matches the shipped runtimeconfig (the Bench exe itself defaults to
Workstation GC, ~3× slower here). Rows go to `sim/BENCH.md` ("Vec (E2)").

Python: `uv run --project sim/tools/vec sts2vec bench --envs N --threads T --steps 500 --pool P [--no-warm] [--json]`
(the same exports through ctypes; reports the whole loop and the native step alone, which differ by < 1 %). Like the
C# bench, it warms the pool before the timed window (`--warm`, the default): `env.pool.warm()` (`sts2vec_pool_warm`)
creates every fresh template (`NewRun`, ≈ 16 ms each, on the env's threads) and the time is printed on its own line
(`pool warm: 1,024 templates in 2929 ms`; JSON `warm`, `warm_ms`, `warmed_templates`). `--no-warm` measures cold starts:
each fresh entry then pays its `NewRun` at the first reset that uses it, inside the timed window, which dominates
small N with a large pool. Measured at E2c (8 threads, load average ≈ 4.5; same episodes either way):

| N | pool | steps | warm (default) | `--no-warm` |
| --- | --- | --- | --- | --- |
| 64 | 64 | 200 | 55,168 (warm 613 ms) | 15,859 |
| 64 | 1024 | 500 | 82,852 (warm 2,929 ms) | 5,664 |

**Short JIT runs are warm-up dominated on small Linux hosts.** On Huginn (x86_64, 8 vCPU on 4 cores) tier-up compilation
competes with the 8 workers for the cores, and the default tiered JIT needs ≈ 20 s of batches to reach steady state, so
a 500-step window reads ≈ 45 % low (C# `vec` 1024×8: 28.1k env-steps/s at 500 steps, 50.5k at 3,000). Two ways reach
full speed from the first batch: `DOTNET_TieredCompilation=0` (full JIT up front: 51.8k at 500 steps; Python pool 64:
53.2k against 18.9k) or the NativeAOT library (`scripts/vec_publish_aot.sh`, `STS2VEC_AOT_LIB`; Python 1024×8 pool 64:
61.4k against 21.6k through hostfxr). `TieredCompilation=0` slows a cold large pool (Python pool 1024: 22.6k against
26.3k) because every method on the first `NewRun` paths is fully optimised on its first call, so warm the pool first.
For Linux rows use ≥ 3,000 steps, `TieredCompilation=0` or AOT; the shipped runtime configs keep their defaults.
Details: `docs/linux-validation-2026-11-01.md` §3.3.

Measured at integration (E2, before `--warm` existed: the "Python (pool 1024, cold)" column is today's `--no-warm`;
load average 3–7, other agents running; numbers vary ±30 % between runs):

| N | threads | C# `vec` (pool 1024, warmed) | Python (pool 1024, cold) | Python (pool 64) |
| --- | --- | --- | --- | --- |
| 64 | 1 | 18,551 | 4,389 | 12,118 |
| 64 | 8 | 89,125 | 5,682 | 20,740 |
| 1024 | 1 | 27,418 | 23,798 | 29,561 |
| 1024 | 8 | 116,350 | 73,194 | 54,137 |
| 4096 | 1 | 41,549 | 39,685 | 39,899 |
| 4096 | 8 | 144,421 | 162,928 | 86,682 |

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
// additive (E2c), appended after the frozen set:
int32_t sts2vec_pool_warm(int64_t h, int32_t max_entries);                // fresh templates created now → count, or -1
// additive (determinized clones), appended:
int32_t sts2vec_clone_determinized(int64_t h, int32_t slot, uint64_t seed); // pool entry id of a determinized clone, or -1
// additive (ABI 2, search), appended:
int32_t sts2vec_abi_version(void);                                          // 2; a build without this export is ABI 1
int32_t sts2vec_branch(int64_t h, int32_t src_slot, const int32_t* dst_slots, int32_t n, int32_t mode, uint64_t seed,
                       void** buffers);                                     // mode 0 exact, 1 determinized; 0 or -1
int32_t sts2vec_branch_many(int64_t h, const int32_t* src_slots, const int32_t* dst_slots, int32_t n, int32_t mode,
                            uint64_t seed, void** buffers);                 // pairs (src_slots[j] -> dst_slots[j]); 0 or -1
int32_t sts2vec_restore_many(int64_t h, const int32_t* slots, const int32_t* entries, int32_t n, void** buffers); // 0 or -1
int32_t sts2vec_pool_release(int64_t h, const int32_t* entries, int32_t n); // n released, or -1 (nothing released)
```

- ABI 2 exports (`branch`, `branch_many`, `restore_many`, `pool_release`, `abi_version`): semantics, costs and the search
  recipe in "Search". Like every export added after the freeze they are optional for the Python loader; `lib.abi_version`
  (1 without `sts2vec_abi_version`) and `lib.supports_branch` / `supports_restore_many` / `supports_pool_release` report
  them, and the wrappers raise `Sts2VecError` ("not exported ... not supported; rebuild it") on an older build. The ABI
  version is separate from `encoder_version`: ABI 2 changes no buffer, so checkpoints and evaluation outputs are
  unaffected.

- `sts2vec_clone_determinized`: appends a pool entry holding `RunSim.CloneDeterminized(seed)` of the slot's current
  state (prompts included) and returns its id; `sts2vec_restore(h, other_slot, entry, buffers)` starts a slot from it.
  The restored slot's observation equals the source slot's byte for byte; its hidden state is resampled (see
  "Determinized clones" below). An empty slot is an error. Python: `env.clone_determinized(slot, seed)`.

- `sts2vec_pool_warm`: creates the templates of up to `max_entries` fresh entries without one (`< 0`: all), in id
  order, while the template cache (`pool_cache`) has room, on the env's threads; returns the number created by this
  call (0 when all are warm or the cache is full). A template is the `NewRun` the entry's first start would create, so
  warming changes no start (`VecTests.Test_VecPool_WarmCreatesTemplatesAhead`: warm and cold envs give byte-identical
  buffers and hashes). Save and snapshot entries hold their template already. The Python loader treats exports added
  after the freeze as optional: a library built before them still loads, and `env.pool.warm()` then raises
  `Sts2VecError` ("rebuild").
- String outputs write `min(cap, len)` bytes and return the full UTF-8 length.
- Return 0 ok / −1 error. Every failing export stores `"<ExceptionType>: <message>"` globally (`h = 0`) and, when the
  handle is valid, on the env (`slot = -1`). Per-slot problems go to `status` and the slot's last error (full
  exception text). No exception crosses the ABI. Each env serialises its own calls with a lock: every export that
  touches an env (all but `pool_count` and `last_error`, which only read) holds that env's lock for the whole call, so
  calls on one env from several threads run one after another (a branch issued while another thread steps waits for
  the step, and vice versa); different envs never contend.
- Validation: an invalid handle, a slot outside `0..N-1` (`last_error` also accepts −1), a pool entry outside
  `0..count-1`, a null or incomplete buffer table, a null actions pointer, `n < 0` or null pointers with `n > 0` in
  `state_hash`, a start id that is not a live pool entry (masked-in slots only), malformed or unknown-key JSON, an unknown
  character, ascension outside 0..10, and for the ABI 2 calls a slot out of range or listed twice, a branch destination
  that is a source of the same call, a branch source without a run, `mode` not 0/1, `n < 0` or `n > N`, null arrays with
  `n > 0`, a released or never-issued entry → −1 with the text, the env unchanged and usable.
- `sts2vec_state_hash` writes 0 for a slot with no run; `sts2vec_snapshot` of an empty slot is an error.
- `config_json`: `{"num_envs": N, "threads": 8, "step_cap": 3000, "auto_reset": true, "seed_text": "VEC",
  "content": "ported_only" | "all", "pool": {"fresh": {"characters": [...], "ascension": 0, "count": 4096,
  "seed_prefix": "TRAIN", "act1": "random"}}, "pool_cache": 4096, "fast_path": true, "floor_step_cap": 80}`; `num_envs`
  required, unknown keys rejected. `fast_path` (default true): clone-free stepping behind the static prompt-site
  predicate (section "Fast path"). `floor_step_cap` (appended, default 80, 0 = off, < 0 rejected): the floor stall cap
  ("Environment model"). Appended config keys are the ABI's additive options: a library built before a key rejects it
  as unknown, so the Python side sends `floor_step_cap` only when the schema's `EndKind` lists `FloorStall`
  (`Schema.supports_floor_step_cap`; `loader.CONFIG_ADDITIONS`) and otherwise refuses a non-zero cap with a rebuild hint.
  The version string `sts2vec/<encoder_version>` changed with it (`fc2eb34c07bc8513` → `0f57a3a225333e34`).

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
- `card_static` (appended 2026-11-04; `CardStatic.cs`): a static table in the schema JSON, `{"fields": [...], "rows":
  [[...], ...]}`, one int row per `cards` vocab id (rows 0 / 1 zero), published once rather than per observation so a
  model can embed a card from its numbers: `ported`, energy / star cost and after one upgrade (−1 X, −2 unplayable /
  star X), type, rarity, target, keyword mask (and upgraded), max upgrade level, `upgrade_known`, damage / block / hits
  (base and upgraded), and the first four dynamic vars (`card_vars` ids, base and upgraded values; every "magic number"
  is one of them). Ported cards are read from a probe `CardInstance` (canonical values, then one `Upgrade()` = the
  def's `OnUpgrade`); catalog-only cards (`ported` 0) carry their catalog cost / type / rarity / target. Per-instance
  values as in `hand` (MAD_SCIENCE's type is its Tinker Time choice, None on a fresh card). Python:
  `schema.card_static` (int32 [V_cards, F]), `schema.card_static_fields`, `schema.card_static_cols()`. Test
  `VecTests.Test_Vec_CardStaticTable`: 595 card constructors (cost, type, rarity, target) and the damage (152), block
  (65) and cost (55) changes of the game's `OnUpgrade` bodies agree with `knowledge/source/v0.111.0/cards.jsonl`; the
  table adds ≈ 40 KB to the schema JSON (74 KB in all) and changes `encoder_version` whenever a card's numbers change.
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
| 30 | `truncated` | uint8 | [] | step cap, floor stall cap, content gap or sim error |
| 31 | `status` | int32 | [] | OK 0, NOOP 1, ILLEGAL_ACTION 2, CONTENT_GAP 4, SIM_ERROR 8, CANDIDATE_OVERFLOW 16, TABLE_OVERFLOW 32, RESET 64, POOL_EMPTY 128, FLOOR_STALL 256 |
| 32 | `episode` | int64 | [3] | episode_id, step_index, start_entry |
| 33 | `aux` | float32 | [14] | total_floor, act_index, hp, max_hp, gold, bosses_killed, combats_won, deck_n, relic_n, is_combat, act_floor, map_row, map_col, act_rooms |
| 34 | `last_episode` | int32 | [8] | win, floor, act, steps, final_hp, final_max_hp, end_kind (EndKind), last_encounter_id |
| 35 | `global_x` | int32 | [2] | ancient_id, draw_marked_n (appended) |
| 36 | `hand_x` | int32 | [10, 2] | affliction_amount, dampened (appended; row i = `hand` row i) |
| 37 | `draw_marked` | int32 | [32, 6] | card_id, upgrade, enchant_id, enchant_amount, affliction_id, affliction_amount (appended; sorted by content) |
| 38–39 | `discard_order`, `exhaust_order` | int32 | [64, 6] | same columns as `draw_marked`, pile order (appended) |
| 40 | `event_option_values` | int32 | [8, 9] | value_n, value_0..2, item_n, item_kind_0, item_id_0, item_kind_1, item_id_1 (appended; row i = `event_options` row i) |
| 41 | `reward_choices` | int32 | [16, 25] | reward_index, option, alt_id, then the 22 `hand` columns (appended 2026-11-04; row k = the k-th rewards-screen macro's target, "Rewards-screen macros") |
| 42 | `map_routes` | int32 | [18, 7, 5] | elites_min, elites_max, rests, shops, boss_dist (appended 2026-11-04; cell [row][col] = `map_nodes` cell, "map_routes" below) |

43 buffers (D3.1 numbers its rows 0–33 and lists `card_reward_alts` inside row 20; rows 35–42 are appended after the
signal buffers so every earlier buffer keeps its index, shape and bytes). ≈ 35.1 KB per env (`reward_choices` +1,600 B,
`map_routes` +2,520 B). Caps appended with `reward_choices`: REWARD_CHOICES 16. New caps (`schema.caps`):
DRAW_MARKED 32, PILE_ORDER 64, EVENT_VALUES 3, EVENT_ITEMS 2; new enum `ItemKind` (vocabulary convention: affliction,
card, enchantment, potion, relic).

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
| H1 | The draw pile order is hidden. | The draw pile is encoded only as a count vector and as `draw_marked`, its enchanted / afflicted cards sorted by their whole row (so the table is a function of the pile's multiset). Discard and exhaust order are not hidden (the pile screens show them in order) and are encoded in `discard_order` / `exhaust_order`. `Test_VecEncoder_LeakTest_DrawOrder` takes ≥ 500 combat decision points, encodes each next to a clone whose draw pile was reshuffled, and requires identical bytes; each clone's hash differs, which proves the shuffle happened. It repeats the check with two draw cards enchanted / afflicted first (585 of 600 states). |
| H2 | No seed, RNG stream or counter. | Nothing under `Rng`, `SeedText` or `Odds` is read. |
| H3 | No future rooms. | Only the current act's `Boss` and `SecondBoss` appear (`boss_id`, `second_boss_id`); the game shows both on the map and the top bar. The map is the current act's map only. |
| H4/H5 | No odds and no grab bags. | Neither is read. |
| H6 | The treasure relic is hidden until the chest opens. | `treasure_relic_id` is 0 until `ChestOpened`. |
| H7 | Card-reward cards: hidden on the rewards screen until 2026-11-04; since the rewards-screen macros they are shown there (one free click away in the game: opening a card reward and closing it leaves it unclaimed). | `rewards` still names no card (`is_card_reward = 1`); `reward_choices` lists the unopened rewards' cards (and REROLL / SACRIFICE) in the Rewards loop phase; `card_reward` and `card_reward_alts` are written only in the `CardReward` loop phase (the open screen). |
| H8 | No move ids. | Intents come from `NextMove.Intents` through `PlayerView.Intent` (type, damage, times, card_count). The move's identity and the AI state are never read; `Test_VecEncoder_NoMoveIds` greps the encoder source and checks the rows. |
| H9/H11 | No monster, encounter or event internals. | `Extra`, `EncounterExtra` and event RNG are not read. An event option is identified by `key_hash` and `ordinal`; `event_option_values` reads only the event members its option text names (the localisation placeholders, `EventOptionVars`), i.e. what the button shows. |
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
| `affordable` | stocked and price ≤ gold (the price label's colour, `Nodes.Screens.Shops/NMerchantPotion.cs:178`). BuyEntry additionally needs a potion to fit: no BuyEntry is listed for a potion while the belt is full or Sozu forbids it (`MerchantRoom.CanBuy`, 2026-11-04), so read the candidates for what is buyable. |

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

**map_routes** [row][col] (appended 2026-11-04; the `map_nodes` cell; a cell without a node is a zero row)

Routing features of the drawn map graph, computed from the published `map_nodes` columns themselves (`point_type`,
`child_mask`; an edge always leads to the next row), so they cannot disagree with them. The act's boss is
`ActMap.BossMapPoint` (the first Boss row; the A10 second boss follows it). Motivation: the PPO policy of 2026-11-03
avoided elites when it had a choice but walked into nodes where every option was an elite (0.65 forced elites per Act 1
on maps where 19 of 20 had an elite-free path): it saw only the next row.

| Column | Meaning |
| --- | --- |
| `elites_min`, `elites_max` | Fewest / most Elite rooms on any path from this node to the boss, this node included (0 at the boss; −1 when the boss is not reachable from the node, i.e. the second boss). For a `legal_next` node: the elites the route through it must face / can collect. |
| `rests`, `shops` | Distinct RestSite / Shop nodes reachable from this node, this node included. |
| `boss_dist` | Moves from this node to the boss (0 at the boss, −1 when not reachable); every path has this length. |

One pass over the 126 cells from the last row up (children first) with 128-bit reachability sets, allocation-free; the
purity test's mean encode time stayed at ≈ 44 µs per call (within its run-to-run noise). Not modelled: free travel (`Hook.ShouldAllowFreeTravel`, any node of the next row) — the features
describe the drawn edges; Unknown (?) rooms are not counted as possible elites. Test:
`VecEncoderTests.Test_VecEncoder_MapRoutesMatchTheDrawnGraph` (80 distinct maps of all characters at A0 / A10, 14 in act 3:
every node against a memoised recursion over `map_nodes`, the start node's min / max against an enumeration of all
6,899 start-to-boss paths); Python `tests/test_map_routes.py` recomputes the table from `map_nodes` over random rollouts.

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
| `act_floor`, `map_row`, `map_col` | The same values as in `global_i` (E3: appended for the protocol metric). |
| `act_rooms` | Rooms in the current act = the act map's last row + 1 (start row 0 through the last boss row, second boss included; a GOLDEN_COMPASS path counts its own rows); 0 without a map. Equals the play server's `map.nodes` max row + 1 and `map_nodes`' last non-empty row + 1. |

Protocol v0.2 progress (`docs/sts2-bench-protocol-v0.2-2026-11-01.md` §4.2) from `aux`: act `a = act_index + 1`, the
act's start floor `total_floor - act_floor` (act 1: 0; equals the total floor at the act's first observation, checked
by `VecEncoderTests.Test_VecEncoder_AuxProgressColumns` over 10,229 states / 24 act starts), `L = act_rooms`. With
auto-reset, score a finished episode from the slot's aux of the previous call plus `last_episode` (win, floor, act):
`sts2train.progress.ProgressTracker` (`sim/tools/train`) does exactly that and calls `sts2eval.progress.compute`.

**last_episode**

`ObservationEncoder.FillEpisodeSummary(RunSim, Span<int>)` fills floor, act, final_hp, final_max_hp and
last_encounter_id (the last Monster, Elite or Boss room in the history). VecEnv writes win, steps and end_kind.

### Appended buffers (player-view schema append, 2026-11-03)

The human-visible details `docs/parity-audit-2026-11-02.md` §5.2 listed as missing from the Vec channel, appended as
new buffers after `last_episode` (no existing buffer changes index, shape or meaning). Cost: +4,216 bytes/env (26,726 →
30,942, +15.8 %); +2.5 % instructions retired per env-step (Python `sts2vec bench`, 512 envs, 1 thread, 300 steps,
random policy, interleaved with the previous build; wall-clock throughput on the loaded host was within noise).

**global_x**

| Column | Meaning |
| --- | --- |
| `ancient_id` | Events vocab id of the current act's Ancient (`RoomSet.AncientId`; its portrait marks its map point, `NAncientMapPoint`, like the bosses in `boss_id`, H3). Seen: act 1 NEOW; act 2 DARV, OROBAS, PAEL, TEZCATARA; act 3 NONUPEIPE, TANX, VAKUU. |
| `draw_marked_n` | Combat only: draw-pile cards with an enchantment or an affliction (the true count; `draw_marked` holds the first 32). |

**hand_x** (row i = `hand` row i)

| Column | Meaning |
| --- | --- |
| `affliction_amount` | `Affliction.Amount` of the card (0 without one); the play server's hand `affliction.amount`. |
| `dampened` | 1 when a DAMPEN power downgraded the card (`DampenPower.DowngradedCards`; restored when its casters die); the play server's hand `dampened`. The instance-id comparison lives in `CardMarks.cs`, outside the grepped encoder sources (H13: nothing id-like is written). |

**draw_marked / discard_order / exhaust_order** (combat only; columns card_id, upgrade, enchant_id, enchant_amount,
affliction_id, affliction_amount)

- `draw_marked`: the draw-pile cards that carry an enchantment or an affliction (generated cards, BOUND in draw, ...),
  sorted by the whole row; the first 32 rows, `global_x.draw_marked_n` = true count, more set `TABLE_OVERFLOW`. Order
  free by construction (H1); together with `draw_counts` it is the game's sorted draw-pile screen.
- `discard_order` / `exhaust_order`: row i = `DiscardPile.Cards[i]` / `ExhaustPile.Cards[i]`, the order the pile
  screens show (oldest first); the first 64 rows (`discard_n` / `exhaust_n` keep the true counts), more set
  `TABLE_OVERFLOW` (1 of the purity test's 1,000 sampled deep healed-run states: a 74-card discard pile, a 13-card
  deck plus generated status cards).

**event_option_values** (row i = `event_options` row i, written while the event is not finished)

The numbers and items option i's button text names. Which placeholders an option shows lives only in the game's
localisation templates, so `sim/tools/vec/tools/gen_event_option_vars.py` extracts them from
`knowledge/source/<version>/events.jsonl` into `EventOptionVars.g.cs` (131 option keys; `--check` in the vec tests);
`EventOptionVars.Resolve` reads each name on the live event exactly as the play server's rules text does (`{Relic}` = the
option's relic; else `<Name>For(RunState)`, a property or a field named `Name`, `NameAmount` or `NameGain`; else the
event's single const id of that kind), through reflection cached per (event type, name). Event screens are a few % of
steps; boxing there is the only new allocation.

| Column | Meaning |
| --- | --- |
| `value_n` | Numbers the text names (gold, HP loss, damage, costs, counts), true count. |
| `value_0..2` | The first three, in text order (title, then description), truncated to int. |
| `item_n` | Cards, relics, potions, enchantments (or afflictions) the text names, true count. |
| `item_kind_k`, `item_id_k` | The first two items: `ItemKind` and the id in that kind's vocabulary (RELIC_TRADER's `.TOP`: the owned relic, then the new one). |

More than 3 numbers or 2 items set `TABLE_OVERFLOW` (none in the event walk). Flags (`IsMultiplayer`), free texts and
names that resolve to nothing are skipped, so `value_n + item_n` ≤ the option's placeholders. An accessor that throws
is skipped too (the server's text shows `?` then) rather than failing the episode: these are display values, and the
parity test below requires every value the walk reaches to resolve.

### Encoder tests (`sim/Sts2Sim.Tests/VecTests.Encoder.cs`, class `VecEncoderTests`)

| Test | What it checks |
| --- | --- |
| `IsPure` | 1,000 states from 60 random boosted runs across all characters and A0/A10. Each must keep its hash after encoding, encode to identical bytes twice, and hold no `?` id in any id column. |
| `LeakTest_DrawOrder` | 600 combat points whose draw pile was reshuffled in a clone. The encoded bytes must be identical. |
| `ParityWithServer` | 300 states (252 in combat). hp, max_hp, gold, energy, max_energy, block, floor and act must match the play server's `ObservationBuilder` JSON. So must the hand's ids, upgrades, costs, playability and hits, and the enemies' slot, uid, id, hp, max_hp, block and intents (type, damage, times, card_count). Also compared: incoming danger and every effective damage against every live enemy (751 values). |
| `NoMoveIds` | Greps the encoder source for move, AI and RNG identifiers, and checks that the intent rows match the intents' types. |
| `TreasureAndCardRewardHidden` | 5 closed and then opened chests (relic hidden until opened), and 74 card rewards: on the Rewards screen `rewards` names no card, `card_reward` is empty and `reward_choices` lists exactly the reward's cards; once opened, `card_reward` shows them and `reward_choices` is empty. |
| `TablesRespectCaps` | A 110-card deck sets `TABLE_OVERFLOW`, writes exactly 96 rows in pickup order and reports `deck_n = 110`. An ordinary state sets no overflow, and its draw counts sum to the pile size. |
| `AuxProgressColumns` | 10,229 states from 10 healed random runs (24 act starts): aux `act_floor` / `map_row` / `map_col` equal the `global_i` columns, `act_rooms` = `map_nodes`' last row + 1 (values seen 15–18), and `total_floor - act_floor` = the protocol's act start floor at every state. |
| `MapRoutesMatchTheDrawnGraph` | `map_routes` on 80 distinct maps (all characters, A0 / A10, acts 1–3) equals a memoised recursion over `map_nodes`' point types and child masks at every node; the start node's elite min / max equal those of an explicit enumeration of every start-to-boss path. |

Appended-buffer tests (`sim/Sts2Sim.Tests/VecTests.Append.cs`, class `VecTests`; the purity, leak and server-parity
tests above also cover the appended columns: no `?` id, marked-draw reshuffles, hand `affliction.amount` /
`dampened` and the `ancient` id equal the server's JSON):

| Test | What it checks |
| --- | --- |
| `Test_VecAppend_PileTablesAndHandMarks` | A first combat with marked cards in every pile and a DAMPEN debuff: `hand_x`, sorted `draw_marked` (+ true count), `discard_order` / `exhaust_order` rows equal the kernel's piles; a reshuffled marked draw pile gives identical bytes; 70 exhausted and 43 marked draw cards keep the first CAP rows, the true counts and set TABLE_OVERFLOW. |
| `Test_VecAppend_AncientId` | `ancient_id` = `RoomSet.AncientId` on sampled states of 12 healed random runs (Ancients of all three acts seen). |
| `Test_VecAppend_EventOptionValuesMatchServerText` | The event walk of `ParityAuditTests` (every event, pages within three choices, three seeds) plus RELIC_TRADER / STONE_OF_ALL_TIME / WELCOME_TO_WONGOS with setup: every number in `value_k` and every item's title appears in the play server's rendered option text (758 option rows, 103 keys, 343 numbers, 122 items), no overflow, `value_n + item_n` ≤ placeholders. |

## Python API (§D6)

Package `sim/tools/vec` (`sts2vec`, uv project; deps numpy, dev pytest). Full reference: `sim/tools/vec/README.md`.

| Module | Content |
| --- | --- |
| `loader.py` | `load(dll=None) -> Library` (one per process, cached): library discovery (`dll` arg, `STS2VEC_AOT_LIB`, `STS2VEC_DLL`, Release build dir), libhostfxr discovery (`DOTNET_ROOT`, `DOTNET_INSTALL_DIR`, macOS Homebrew `dotnet@9/libexec`, Linux `$HOME/.dotnet` and distro roots, the `dotnet` on PATH, `dotnet --list-runtimes`; first root with a 9.x runtime), `hostfxr_initialize_for_runtime_config(sts2vec.runtimeconfig.json)` with `dotnet_root` set, hostfxr error writer captured into `LoaderError`, `System.GC.Server` read back (`lib.gc_server`), `load_assembly_and_get_function_pointer(..., UNMANAGEDCALLERSONLY_METHOD = -1)` for each of the 21 exports with ctypes `CFUNCTYPE` signatures (`EXPORTS`, §D5 order); NativeAOT `.dylib`/`.so` through `ctypes.CDLL` with the same symbols. Exports added after the freeze (`OPTIONAL_EXPORTS`: `sts2vec_pool_warm`, `sts2vec_clone_determinized`, and ABI 2's `sts2vec_abi_version`, `sts2vec_branch`, `sts2vec_branch_many`, `sts2vec_restore_many`, `sts2vec_pool_release`) may be missing from an older build: the attribute is then None, `lib.missing_exports` names it and `lib.require(name)` raises `Sts2VecError` ("not supported; rebuild"). `lib.abi_version` (1 without the export), `lib.supports(feature)` / `supports_branch` / `supports_restore_many` / `supports_pool_release` (`ABI_FEATURES`). `lib.version()`, `lib.schema_json()`, `lib.last_error(h, slot)`. |
| `schema.py` | `Schema.from_json` (buffers, dtypes, shapes, fields, vocab lists with reverse maps `index_of`, enum lists, caps, card_vars, playable_reason_bits), `schema.col(buf, field)` / `schema.cols(buf).field`, `observation_names`, `verify_version()` (pure-Python XXH64), `Schema.count_index`; `Buffers` (one C-contiguous `(N, *shape)` array per buffer + the `void*` table in schema order, `check()`); `Status` IntFlag / `STATUS`, `status_names`, `status_counts`; `slot_seed(seed_text, slot)`. |
| `pool.py` | `PoolSpec(characters, ascension, count, seed_prefix, act1, entries)` → `config.pool.fresh` (+ explicit `FreshEntry` / `SaveEntry` added after create, ids `count..`), `make_config(...)` (config_json), `env.pool` (`add_fresh`, `add_save`, `warm(max_entries=None)` → templates created, `release(ids)` → number released (ABI 2), `len` = live entries). |
| `env.py` | `VecEnv` (§D6; kwargs mirror `config_json`, incl. `floor_step_cap=FLOOR_STEP_CAP`): `reset(mask, start_ids, copy)`, `step(actions, copy) -> (obs, reward, terminated, truncated, status, info)`, `restore`, `snapshot`, `clone_determinized(slot, seed)`, ABI 2 `branch(src, dsts, determinized=True, seed=0)` (`src` an int or one source per destination), `restore_many(slots, entries)`, `abi_version` / `supports_*`, `branch_seed(seed, j)`, `state_hash`, `set_threads`, `candidates(i)`, `legal_mask()`, `last_episode(i)`, `slot_errors()`, `status_counts()`, `close()` (idempotent) / context manager; `random_actions(rng, cand_n)`. Non-zero returns raise `Sts2VecError(<last_error>)`; a non-Server-GC CLR raises `ServerGCError`; per-slot outcomes are only status bits. |
| `cli.py` | every env subcommand takes `--floor-step-cap` (default 80; `eval` passes it to the env); `sts2vec bench-branch` (search primitives, "Search"; `--old-only` times the pre-ABI-2 path, also on an ABI 1 library); `sts2vec bench` (pool warm time with `--warm`, the default, or cold starts with `--no-warm`; env-steps/s, native-only env-steps/s, ms/batch, obs MB/batch, GC mode, reset ms, resets/s, episodes/wins, status counts; `--json`), `sts2vec check` (schema/buffer consistency, version, determinism: two envs with the same config, threads 1 vs 8, seeded random policy, byte-identical buffers every step + equal state hashes; exit 1 on failure), `sts2vec digest` (JSON digest of a seeded rollout, for cross-process determinism). |

```python
from sts2vec import VecEnv, PoolSpec, STATUS, random_actions
with VecEnv(1024, threads=8, pool=PoolSpec(characters=("IRONCLAD",), count=256)) as env:
    obs = env.reset()                                   # dict of env-owned (N, *shape) arrays (views)
    obs, reward, terminated, truncated, status, info = env.step(random_actions(rng, obs["cand_n"]))
    env.pool.add_fresh("SILENT", seed="S1"); env.pool.add_save(path); len(env.pool); env.pool.warm()
    e = env.snapshot(0); env.restore(3, e); env.state_hash([0, 3]); env.candidates(0)
    d = env.clone_determinized(0, seed=17); env.restore(4, d)   # same view as slot 0, hidden state resampled
    env.branch(0, [5, 6, 7], seed=3)                            # ABI 2: three determinized copies, no pool entry
    env.branch([0, 0, 1], [5, 6, 7], determinized=False)        # exact copies of two roots in one call
    env.restore_many([1, 2], [e, e]); env.pool.release([e, d])  # batched restore; free the entries
```

- `obs` = every buffer except `reward/terminated/truncated/status/episode/last_episode` (those come back as the step
  signals and `info`); arrays are views valid until the next call (`copy=True` copies).
- Inputs are validated in Python before the call: `actions`/`mask`/`start_ids` must have shape `(N,)` (no broadcasting)
  and integer dtype.
- Tests (`uv run --project sim/tools/vec pytest -q`): the tests that need V1/V2 (`test_full.py`, the fixture-save
  3-candidate test) skip with reason `needs V1/V2` while `cand_n`/`hp` are 0 after a reset and run automatically
  afterwards; `STS2VEC_REQUIRE_FULL=1` makes them fail instead (integrator gate). The NativeAOT round-trip test exists
  only when `sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.<dylib|so>` exists.
- The package registers a pytest plugin (`sts2vec._pytest_plugin`, entry point `pytest11`): when pytest runs without
  paths and without a config file (e.g. the repo root), collection is limited to `sim/tools/vec`.
- NativeAOT: `scripts/vec_publish_aot.sh` publishes with `PublishAot`, `NativeLib=Shared`, `ServerGarbageCollection`,
  `TrimmerRootAssembly Sts2Sim.Core` (injected through `CustomAfterMicrosoftCommonTargets`; no csproj change),
  `LIBRARY_PATH=/opt/homebrew/lib`, into `bin/aot-artifacts` (separate obj) and copies the library to `bin/aot/`.

## Determinized clones

`RunSim.CloneDeterminized(ulong seed, DeterminizeOptions? options = null)` (and `CombatSim.CloneDeterminized`) return a
deep clone whose **public state is identical** and whose **hidden state is resampled** from `seed`: one sample of the
information set, for information-set search (ISMCTS, PIMC) later. Implementation: `Sts2Sim.Core/Random/Determinizer.cs`
(`Determinizer.Apply` works on a `RunState` / `CombatState` in place). Through the vec layer:
`sts2vec_clone_determinized(h, slot, seed)` → pool entry, then `sts2vec_restore`; Python `env.clone_determinized(slot, seed)`.

Contract (tests in `sim/Sts2Sim.Tests/VecTests.Determinize.cs`, class `VecDeterminizeTests`; Python
`test_full.py::test_clone_determinized_same_view_resampled_hidden_state`):

- the player view is unchanged: `ObservationEncoder` bytes and status equal the original's on 900 sampled decision
  points (717 combat, 136 map / shop / event / reward / rest screens, 47 pending prompts);
- the hidden state changed: the clone's `StateHash` differs; a reflection walk over the whole object graph
  (`RngGraphWalker`) finds every `Rng` object of the clone distinct from, and in a different state than, every `Rng` of
  the original (4,506 RNG objects over 300 states; the act map's construction RNG, shared by `Clone()`, is excluded);
- two seeds give different draw orders over the same multiset (299 / 300 states) and different future monster moves
  under the same end-turn policy (11 / 150 combats; most monster AIs are fixed cycles), while the current intents stay;
- deterministic: same state + same seed + same options ⇒ same `StateHash`; restoring one entry twice gives the same
  state; `CombatSim.CloneDeterminized` behaves the same on standalone combats.

**What is resampled.**

| Hidden item | Sim location | Game source (decompile) | How |
| --- | --- | --- | --- |
| Draw-pile order (H1) | `PlayerState.DrawPile` of every combat player | `Entities.Cards/CardPile.cs:18` (`_cards`, a plain list; top = index 0) | uniform permutation of the pile (multiset kept), except `KeepDrawTop` / `KeepDrawBottom` cards at the ends |
| 12 run streams (UpFront, Shuffle, UnknownMapPoint, CombatCardGeneration, CombatPotionGeneration, CombatCardSelection, CombatEnergyCosts, CombatTargets, MonsterAi, Niche, CombatOrbs, TreasureRoomRelics) | `RunState.Rng` (`RunRngSet`) | `Runs/RunRngSet.cs:17` | every distinct stream object restarted in place from a seed-derived value (aliases stay aliases; the call counter is kept) |
| Hashed run seed | `RunRngSet.Seed` | `Runs/RunRngSet.cs:27` | replaced: it is the root of every RNG derived later (act maps `StandardActMap` / `SpoilsActMap`, `EventModel.Rng` at event start, monster private and encounter RNGs at spawn, `Rng.ForPlayerModel` users such as Fur Coat), so those are resampled too. `StringSeed` / `RunConfig.SeedText` stay as run identity (read only at `NewRun`) |
| 3 player streams (Rewards, Shops, Transformations) + set seed | `PlayerState.PlayerRng` (`PlayerRngSet`) | `Random/PlayerRngSet.cs:12,21` | restarted in place |
| Current event RNG | `EventRoom.CanonicalEvent.Rng`, `EventRoom.LocalMutableEvent.Rng` | `Models/EventModel.cs:399` | restarted in place |
| Reward RNG overrides | `Reward.RngOverride` (rewards stack, `CombatRoom.ExtraRewards`), `CardReward.Options` / `RerollOptions.RngOverride` | `Rewards/Reward.cs:159` (`_rngOverride`) | restarted in place (an override that aliases a player stream is restarted once) |
| Monster private RNGs | `PrivateRngExtraState.PrivateRng` of monsters, allies and escaped creatures | `Models/MonsterModel.cs:336` (`_rng`) | restarted in place |
| Future monster moves | rolled from `MonsterAi` / private RNGs when the next intent is chosen | `Models/MonsterModel.cs:721` (`RollMove(..., RunRng.MonsterAi)`) | follow from the reseeded streams; the **current** `NextMove` (the shown intent, H8) is untouched |
| Relic grab-bag order (H5) | `RunState.SharedRelicGrabBag`, `PlayerState.RelicGrabBag` | `Runs/RelicGrabBag.cs:101,147` (each rarity deque `UnstableShuffle`d once) | each rarity deque reshuffled uniformly (pulls and removals never reorder the rest, so the remaining order is uniform given what was seen) |
| Unvisited event order (H3) | `ActInstance.Rooms.events[eventsVisited..]` of the current and later acts | `Models/ActModel.cs:257` (one `UnstableShuffle`) | tail reshuffled uniformly; an already pulled current event keeps its slot |

The last two rows are switched off with `DeterminizeOptions { ResampleRunQueues = false }`.

**What stays** (public, or pre-rolled content that is not a plain shuffle):

- everything the player sees: hand, discard and exhaust order, the piles' multisets, intents, HP / block / powers,
  orbs, potions, relics, gold, the map, shop stock, generated card choices, prompt options, the current act's boss;
- pre-rolled hidden content made by constrained generators, kept as rolled (a known leak for run-level search,
  irrelevant inside one combat): normal / elite encounter queues (`ActInstance.GenerateRooms`, tag-aware
  `AddWithoutRepeatingTags`), later acts' boss / second boss / ancient and the shared-ancient split, the relic inside an
  unopened chest (H6), the cards of an unopened card reward (H7), odds values (`RunOddsSet`, `PlayerOddsSet`);
- the act map's own construction RNG (shared between clones by `Clone()`, used only while generating the map).

**Draw-pile placements the player may remember.** Cards put on top (`CardPilePosition.Top`: Headbutt, Thinking Ahead,
Cosmic Indifference, Glimmer, Photon Cut, Shining Strike, Rebound, Nostalgia, Perfect Fit after a shuffle; game:
`Commands/CardPileCmd.cs:309-311`, `Models.Cards/Headbutt.cs:112`), Innate cards moved to the top at combat start
(`Combat/CombatManager.cs:4266-4273`) and cards sent to the bottom are positions a human can remember. Neither the game
nor the kernel tracks this knowledge: `CardPile` is a bare list with no placement metadata
(`Entities.Cards/CardPile.cs`, `AddInternal` / `MoveToTopInternal` :248-314; sim `State/CardPile.cs`), the draw-pile
screen sorts the cards by (rarity, id) (`Nodes.Screens/NCardPileScreen.cs:185-187`) and so does the draw-pile selection
grid (`Nodes.Screens.CardSelection/NCombatPileCardSelectScreen.cs:412-428`). The observation encoder does not carry it
either (H1). Decision: the default resamples the whole draw pile, i.e. it conditions on the observation, not on the
player's memory of the action log; a caller that tracks placements itself passes
`DeterminizeOptions { KeepDrawTop = k, KeepDrawBottom = m }` (C# only; the C ABI uses the default).

**Pending prompts.** A clone taken at a pending prompt keeps the prompt, the picks so far and the answer tape. The
pending action is re-executed from its pre-action basis on every answer (tape engine), so resampling the basis would
change what the re-run reveals before the prompt. Instead the clone records a hook (answer index k, seed, options): the
re-run replays the recorded prefix unchanged (every taped prompt still passes `ChoiceTape.SameShape`) and resamples the
work state when the tape hands out answer k, i.e. right after the current prompt's options were shown. The tape
therefore always matches; the clone's current (paused) state is resampled as well. Hooks are cloned, hashed (tag
`Determinize`, written only when present, so ordinary hashes and the F2 pins are unchanged) and accumulate when a
clone is determinized again. `VecDeterminizeTests` answers 47 prompts on clones, checks that each resolved clone differs
from the resolved original (the hook fired) and that no tape divergence occurs.

**Not save-faithful.** `RunExporter.ToSave` of a determinized clone writes the original `StringSeed` next to resampled
streams; a determinized state is for search, not for export.

## Search (ABI 2: branch, restore_many, pool release)

Decision-time search needs many short-lived copies of live slots. Before ABI 2 every copy went through the pool:
`snapshot` / `clone_determinized` appended an entry (one RunSim, never freed; auto-resetting slots could even start from
it), then `reset(mask, start_ids)` / `restore` cloned it again into the rollout slot and encoded it, and the prototype
lookahead had to rebuild its private env when the pool grew too large. ABI 2 copies slot to slot and frees entries.

| What | C ABI | Python |
| --- | --- | --- |
| copy one source into n slots | `sts2vec_branch(h, src, dsts, n, mode, seed, buffers)` | `env.branch(src, dsts, determinized=True, seed=0)` |
| copy many sources in one call | `sts2vec_branch_many(h, srcs, dsts, n, mode, seed, buffers)` | `env.branch(srcs, dsts, ...)` (one source per destination) |
| restore many slots in one call | `sts2vec_restore_many(h, slots, entries, n, buffers)` | `env.restore_many(slots, entries)` |
| free pool entries | `sts2vec_pool_release(h, entries, n)` | `env.pool.release(ids)` |
| capability | `sts2vec_abi_version()` = 2 | `lib.abi_version`, `lib.supports_branch` / `_restore_many` / `_pool_release`, `env.supports_*` |

**Branch** (`VecEnv.Branch` / `BranchMany`; tests in `sim/Sts2Sim.Tests/VecTests.Branch.cs`, Python `test_branch.py`):

- `mode` 0, exact: `RunSim.Clone()`: the same `StateHash`; the copy steps exactly like the source
  (`Test_VecBranch_ExactCopiesStateViewAndFuture`: 25 source states × 4 copies, 282 replayed steps hash- and
  byte-identical; also a pending 250-option WISH prompt with `CANDIDATE_OVERFLOW`, answered identically).
- `mode` 1, determinized: `RunSim.CloneDeterminized(BranchSeed(seed, j))` with j = the destination's position in the
  call; `BranchSeed(seed, j)` = SplitMix64 finaliser of `seed + (j + 1) · 0x9E3779B97F4A7C15` (mod 2^64; Python
  `sts2vec.branch_seed`). Same player view, hidden state resampled ("Determinized clones"). The result is a function of
  (state, seed, j) only: the same call into other slots, and `clone_determinized(src, branch_seed(seed, j))` + `restore`,
  give the same state; `branch(src, dsts)` equals `branch([src] * n, dsts)`.
- Each destination starts a new episode: status `RESET` (plus the view's `TABLE_OVERFLOW` / `CANDIDATE_OVERFLOW`), a
  new `episode_id` (its episode counter +1), `step_index` 0, `start_entry` -1 (no pool entry holds the start), floor stall
  count 0; `last_episode` is untouched. Its observation, candidates and signals are written **in the call**, as `reset`
  writes the slots it restarts. The sources and every other slot are untouched (state and buffer rows). Pending prompts,
  picks and answer tapes are copied. A finished source (auto-reset off) is allowed; its copies are live again.
- Validated before anything changes (-1 + text): every source in range and holding a run; destinations in range, listed
  once and never a source of the same call; `mode` 0 or 1; `0 <= n <= N`. A clone that throws (not expected) is a
  per-slot `SIM_ERROR` / `CONTENT_GAP`, like a failed start.
- How: phase 1 clones every destination in parallel on the env's threads (`RunSim.Clone` only reads its source, so the
  clones of one source run concurrently; `Test_VecBranch_ParallelClonesOfOneSourceAreIdentical` takes 64 clones on 4
  threads at 274 states and compares them with serial clones) and encodes the first destination of each source on its
  own clone; phase 2 copies that view (every buffer but reward / terminated / truncated / status / episode /
  last_episode, so appended buffers are included, plus the legal-action list) to the source's other destinations. On
  one thread a clone costs ≈ 10 µs, a copy ≈ 5-10 µs and an encode ≈ 40 µs. Exact copies have the source's view by construction;
  determinized copies by their player-view contract, which `Test_VecBranch_DeterminizedSameViewResampledHiddenState`
  checks on every destination by encoding it on its own (240 source states × 4 destinations at A0 and A10, plus the
  WISH prompt) and `Test_VecBranch_ManySourcesInOneCall` for interleaved sources.

**restore_many**: `restore(slots[j], entries[j])` for every j under one lock acquisition, in parallel; the same
per-slot result as `restore` (`RESET`, new episode id, step index 0, `start_entry` = the entry, observation written).
Slots are distinct, so it equals the restores one by one (`Test_VecRestoreMany_EqualsSequentialRestores`: buffers and
hashes, then 20 steps). Several slots may start from one entry; their clones run concurrently.

**Pool release**: `release(ids)` is all or nothing. A released entry leaves `pool_count` (live entries) and the start
sequence (live entries in id order), drops its template (a snapshot's RunSim becomes garbage: the test checks it is
collected; a cached fresh template frees its `pool_cache` slot), and its id is never issued again: `reset` / `restore` /
`restore_many` / `release` with it fail with "pool entry X was released". Slots started from it keep running. With no
release nothing changes (ids 0..pool_count-1, the same start sequence). Ids are int32 and never reused: an env that
issues 2^31 - 1 ids refuses further entries (≈ 18 h of continuous snapshots at 30 µs each; branch issues none).

**Locks and threads**: every call on an env holds the env's lock for the whole call ("C ABI"), so a branch from one
thread and a step from another serialise; neither fails or sees the other half done
(`Test_VecBranch_ConcurrentWithStepSerialises`: one thread steps slots 0-7 250 times while another branches slot 8 into
9-15 250 times, 249 of them while the stepper ran; the stepped slots end byte- and hash-identical to an env without
branches, the source never changes). Python: `branch` / `restore_many` / `pool.release` keep no shared scratch and may
run on another thread (`test_branch_from_another_thread_while_stepping`); `reset` / `step` / `restore` share per-env
scratch, so drive those from one thread.

**Memory per branched slot**: one RunSim (`Clone()` allocates a median 31 KB, p90 47 KB, max 89 KB over 90 states of six
random runs, `Test_VecBranch_MemoryPerBranchedSlot`; a clone keeps nearly all of it and shares the act map and model
definitions) plus the slot's 35.1 KB of buffer rows. The next branch, reset or restore of the slot replaces it, so a search
env's memory stays ≈ N × (60-120 KB) however long the search runs. The old path also kept one pool entry (the same
RunSim size) per snapshot or determinization, forever.

**K-candidate × R-rollout lookahead over M games** (one private search env: `auto_reset=False`, `pool=None` plus the
evaluator's start entries, N = M × (1 + K·R)):

1. Slots 0..M-1 are the roots and track the real games: start them with `restore_many` from the same entries the
   evaluator uses and step them with the evaluator's actions (the prototype checked the bytes stay equal), or branch
   them from the acting env when both live in one env.
2. Each decision round, one call: `env.branch(np.repeat(roots, K * R), scratch, seed=round)` where scratch = the K·R slots
   of each root. Destination j of root g will take candidate `cand[g][j // R]`; determinized for honest search,
   `determinized=False` for an oracle bound. Per-root `branch(g, ...)` calls also work but each call has a serial
   clone + encode + copy critical path (≈ 60-80 µs at K·R = 8 on 4 threads); batch them.
3. `env.step(first)` with `first[scratch] = candidate indices` and -1 for the roots, then roll out with the base policy
   (H steps, the end of the turn or of the combat), batched over every scratch slot; score the leaves (`aux`,
   `global_i.hp`, terminal reward).
4. Pick the best candidate per root and step the roots with it. Nothing to release: the next round branches over the
   same scratch slots. Use snapshots + `restore_many` + `release` only when one state must seed many rounds.
5. Branch in a training env only into slots that are not training slots: a branch starts a new episode on the
   destination (`RESET`, `start_entry` -1), and its observation is a copy of the source's.

**Cost** (`sts2vec bench-branch --envs 1024 --threads 4 --k 8`: 113 roots × 8 destinations, roots mid-run after 60
random steps, 81 of them in combat; µs of wall time per branched slot, median of 7 rounds after 3 warm-up rounds; 2026-11-04
on an M4 Pro shared with a running game and other agents at nice 10, load average 6-9, so absolute numbers are ≈ 2x
pessimistic and vary ±30 % between runs; six runs, the range is shown; ABI 1 = the baseline library e80ea22,
`--old-only`; full rows in `sim/BENCH.md` "Vec search"):

| Path | exact | determinized |
| --- | --- | --- |
| `branch`, one call for all roots (`branch_many`) | 2.8-4.0 | 4.0-4.9 |
| `branch`, one call per root (8 destinations) | 7.5-10.2 | 9.1-10.9 |
| old: `snapshot` per root / `clone_determinized` per slot, then one `reset(mask, start_ids)`, ABI 2 library | 4.7-10.2 | 14.0-20.8 |
| old path on the ABI 1 library | 9.6-9.9 | 16.3-24.0 |
| `restore_many` (per element) | 4.1-8.5 | |
| one `restore` call per slot / `reset(mask, start_ids)` | 18-34 / 3.8-8.5 | |

`branch_many` is 1.7-2.8x faster than the old path for exact copies and 3.5-4.6x for determinized ones (paired rows of
one run), and leaves no pool entries; the old path leaves 113 + 904 entries per round (10,347 after the 10 rounds of an
ABI 1 run). `restore_many` matches `reset(mask, start_ids)` (which needs an N-slot mask and start-id array) and is ≈ 4x
faster than one `restore` call per slot. Plain stepping is unchanged (`sts2vec bench --envs 1024 --threads 4 --steps
500`, three interleaved runs each: 35,253 env-steps/s on the ABI 1 library, 35,537 on ABI 2, load ≈ 10).

## Known gaps / notes for owners

- No leak test through the C ABI (none in §D5); `sts2vec check` says so. The draw-order leak test is C#
  (`VecEncoderTests.Test_VecEncoder_LeakTest_DrawOrder`, 600 reshuffled combat points).
- The clone-free stepping path (§D0 D7) is implemented behind a static predicate ("Fast path"); steps it cannot clear
  (≈ 17 % under a random policy) still deep-clone. The next throughput lever is the observation encoder, not the clone.
- Random-policy soaks reach no victories (0 wins in ~60k episodes); reward 1 at Victory is covered by
  `VecTests.Test_VecEnv_RewardOnlyOnVictory` with a boosted-HP snapshot.
- `Clone()` shares the pre-finished room object (`Pending.Load`) of a save import between clones (inventory §1.1).
  Handled in `StartPool`: a save that imports to a pending prompt is re-imported per start. A *snapshot* (or a *branch*)
  of a slot that started from such a save and is still at that load prompt would still share it (not observed).
- Clone fidelity of claimed rewards (fixed in E2c): `RunSim.Clone()` keeps the objects a claimed reward shares with
  the run — a taken `PotionReward.Potion` is the belt's potion (the game's `PotionCmd.TryToProcure` stores the reward's
  own instance), a picked card-reward card and a taken special card are the deck's cards, and a combat room's extra
  rewards are the very objects its rewards set lists. Before the fix a clone copied them twice, so e.g. `DiscardPotion`
  on a still-open Rewards screen after a clone flagged only the belt copy and `clone.Step(a)` hashed differently from
  `original.Step(a)`. Snapshot/restore and template clones now step hash-identically to the original
  (`SelfConsistencyTests.Test_SelfConsistency_F7_CloneThenStepMatchesStep`, `RewardsTests.Test_Rewards_Claimed*` /
  `Test_Rewards_Clone*`).
- `hand` has no star-cost column (not in §D3.1); the affliction amount is `hand_x.affliction_amount`.
- `sim/tools/train`'s model does not read `reward_choices` yet: a `TakeRewardCard` row reaches it through its kind, A/B
  and `card_id` (card embedding), not through an `option_index` pointer into a table, so upgrades and other per-instance
  columns of an offered card are invisible to it until the model embeds that buffer.
- Not appended: the master deck's affliction amounts (deck cards carry `affliction_id` only; afflictions are combat
  marks) and a separate (enchant, affliction) channel for `card_reward` cards.
- The encoder fails closed: an exception from a Core call (intent damage, card preview, potion rules) ends the
  episode as `SIM_ERROR`/`CONTENT_GAP` instead of encoding a fallback (the play server falls back to the intent type).
- `rewards` rows are indexed by `RewardsSet.Rewards` index (ClaimReward A), so a claimed reward leaves a zero row inside
  the table; `reward_n` counts the unclaimed ones.
