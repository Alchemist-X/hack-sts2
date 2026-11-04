# sts2train — entity-encoder / transformer PPO on sts2vec + the NN protocol-eval entry point

PPO trainer for the batched simulator interface (`sim/VEC.md`, `sim/tools/vec`), built for multi-hour runs on one GPU
box: protocol objective, a 1–20 M parameter policy/value net, checkpoints and `--resume`, a JSONL metrics log, a
`status.json` for external watchers, periodic protocol evaluation, a wall-clock budget. The uv project is still named
`sts2train-smoke` (its first job was the 2026-11-02 smoke, `docs/train-smoke-2026-11-02.md`); the CLI answers to
`sts2train` and `sts2train-smoke`.

```bash
dotnet build sim/Sts2Sim.sln -c Release                      # Sts2Sim.Vec.dll (needs the E3 aux columns)
uv sync --project sim/tools/train                            # numpy, torch, sts2vec (+ dev: pytest, matplotlib, sts2eval)
uv run --project sim/tools/train pytest -q                   # gate (from the repo root or sim/tools/train)

# train (outputs under tmp/, never committed); flags not given keep their defaults
uv run --project sim/tools/train sts2train train --preset large --device cuda --amp --envs 2048 \
    --max-hours 6 --ckpt-every 50 --ckpt-every-min 15 --eval-every 50 --out tmp/train/run1     # GPU box only
# the local smoke (a Mac is a smoke box: smoke/small presets, <= 256 envs, minibatch <= 512, <= 6 threads)
PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.3 uv run --project sim/tools/train sts2train train --device mps --preset small \
    --envs 256 --minibatch 512 --threads 6 --torch-threads 6 --max-hours 0.05 --eval-every 10 --out tmp/train/mps-smoke
# the training-signal flags (all off by default; see "Training-signal flags")
uv run --project sim/tools/train sts2train train --preset large --device cuda --amp --envs 2048 --max-hours 6 \
    --aux-heads --deck-counts --acq-shaping --eval-every 50 --out tmp/train/run2                # GPU box only
# resume: newest checkpoint of the run dir; the checkpoint's config is kept, flags given here override it
uv run --project sim/tools/train sts2train train --resume auto --out tmp/train/run1 --max-hours 6
uv run --project sim/tools/train sts2train train --resume tmp/train/run1/checkpoints/ckpt-0000500.pt

# protocol v0.2 NN entry point: greedy policy on a frozen seed set -> run dir + report.md (same format as sts2eval);
# exit 1 when a game's history does not replay (the replay audit), like sts2vec eval
uv run --project sim/tools/train sts2train eval --seedset std-a0-v1 \
    --checkpoint tmp/train/run1/checkpoints/best.pt --out tmp/eval/nn-run1-a0
# the greedy policy runs with the loop breaker (see "Loop breaker"); --loop-breaker off = the plain argmax
uv run --project sim/tools/train sts2train eval --seedset std-a0-v1 --loop-breaker off \
    --checkpoint tmp/train/run1/checkpoints/best.pt --out tmp/eval/nn-run1-a0-plain
uv run --project sim/tools/eval sts2eval report tmp/eval/nn-run1-a0      # recomputes the same report
uv run --project sim/tools/train sts2train plot tmp/train/run1           # curve.png from metrics.jsonl / evals.jsonl
# model card (staged results with Wilson intervals, baselines) and the weights bundle; see "Model card and weights bundle"
uv run --project sim/tools/train sts2train report-card --checkpoint tmp/train/run1/checkpoints/best.pt --out tmp/card/run1
uv run --project sim/tools/train sts2train pack --checkpoint tmp/train/run1/checkpoints/best.pt \
    --out tmp/release/run1.tar.gz --card tmp/card/run1
# decision-time combat lookahead (a declared search row) and its paired greedy baseline; see "Combat lookahead"
uv run --project sim/tools/train python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 \
    --checkpoint tmp/train/run1/checkpoints/best.pt --out tmp/eval/la-run1 --threads 4
```

(`eval` also answers to `evaluate`; `sts2train train --help` lists every flag with its default.)

## Run directory (`--out`)

| File | Content |
| --- | --- |
| `status.json` | rewritten atomically after every update: `state` (running / finished / stopped / aborted / error), `stop_reason`, `update`, `env_steps`, `episodes`, `wins`, `wall_s`, `env_steps_per_s`, `recent` (progress / win rate / floor over the last 20 updates), `best_eval`, `last_eval`, `last_checkpoint`, `updated_at` |
| `metrics.jsonl` | one row per update: env steps, episodes, finished-episode `progress_mean` / `win_rate` / `floor_mean` / `return_mean` / `act_reached`, `policy_loss`, `value_loss`, `entropy`, `approx_kl`, `clipfrac`, `grad_norm`, `explained_variance`, `env_steps_per_s`, time split `t_env_s` / `t_infer_s` / `t_update_s` / `t_ckpt_s` / `t_eval_s` / `t_other_s`, error counts, `floor_stall` (episodes truncated by the floor step cap this update), `gpu_mem_*_mb` (CUDA / MPS); with `--start-saves` also the `fresh_*` / `save_*` split (see "Start saves"); with `--aux-heads` the `aux_*` keys, with `--acq-shaping` the shaping keys (see "Training-signal flags"), with `--rollout-loop-breaker` `loop_breaks` / `no_op_actions` / `cycle_revisits` (see "Loop breaker") |
| `evals.jsonl`, `evals/u<update>/` | every `--eval-every` updates and once at the end of a budget-ended session: `sts2vec.evaluate` on `--eval-seedset` (default `std-a0-v1`) with the live model, greedy; each row has the headline (`progress_mean`, CI, wins, floor, results, replayable) and the checkpoint it was taken at; `best_skip` (why the row cannot be `best.pt`) when it is not clean; the greedy policy runs with the loop breaker (`--loop-breaker off` turns it off) and the row carries `loop_breaker`, `loop_breaks`, `no_op_actions`, `cycle_revisits`; a model with `--aux-heads` also writes per-decision `win_prob` / `combat_hp_pred` into each game summary |
| `checkpoints/ckpt-<update>.pt` | every `--ckpt-every` updates and every `--ckpt-every-min` minutes (tmp file + fsync + rename), the last `--keep-ckpts`; weights, Adam state, counters, RNG states, `spec`, config, `arch` (size knobs plus `aux_heads` / `deck_counts` when on), `heads` (output definitions, `--aux-heads` only), `weights_hash`; `best.pt` = the checkpoint of the best clean eval so far (highest `progress_mean` among evals that ran, replayed every game — `replayable == games` — and had no `crashed` / `error` game: `ppo.best_skip_reason`) |
| `config.json` | resolved config (incl. `start_saves` / `save_mix` / `save_copies`), architecture, parameter count, library path / version, `encoder_version` (this session's library) and `checkpoint_encoder_version` + `resume_compat` (the checkpoint it resumed from, `model_inputs` identical / widened and `widened_enums`; `null` for a fresh run), `env` (`step_cap`, the `floor_step_cap` in effect, 0 = off), one entry per session (host, device, torch, resumed from, both encoder versions, `model_inputs` / `widened_enums`); `start_pool` (with `--start-saves`, rebuilt every session: entry counts, effective mix, skipped files and reasons, start floor / act / phase / ascension histograms, one row per usable save) |
| `summary.json` | written when a session ends (stop reason, throughput and time split, totals, best / last eval) |
| `train.log` | the CLI's log lines with timestamps, appended by every session |

Stops: `--max-hours` (this session's wall clock; the loop stops before an update that would overrun it),
`--max-updates` / `--max-env-steps` (totals across resumes), or SIGINT / SIGTERM (finish the update, checkpoint,
exit; a second SIGINT aborts). A resumed session restores weights, optimizer, counters and RNG states; the env restarts
from fresh episodes (new start sequence), `metrics.jsonl` / `evals.jsonl` drop rows past the checkpoint. A fresh run
into a directory that already holds checkpoints is refused.

Resume across library versions: a checkpoint resumes on a library with another `encoder_version` when the model's
inputs are identical (`features.check_spec_compat`): shape and column map of each of the 27 buffers it reads, the sizes
of the vocabularies and enums it embeds, those enums' members (recorded by name), `card_vars`. Appended buffers, enums
the model does not embed (EndKind, ItemKind) and the aux / last_episode columns (read by name from the live schema) may
differ; the differences are logged and kept in `config.json` (`resume_compat`), and the next checkpoint carries the
new spec. One change of the inputs is accepted as well: an embedded enum that gained members appended at its end,
verified by member names (`resume_compat.model_inputs` = `widened`, `widened_enums` = {enum: from, to, added}). The
model is built with the library's sizes; that enum's embedding tables (`model.ENUM_EMBEDDINGS`) keep the checkpoint's
rows, the new members' rows and their Adam moments start at zero, so every candidate of a kind the checkpoint knew is
scored exactly as before. Anything else is refused before the env is built (exit 2, every difference listed, the run
directory untouched): a vocabulary of another size, an enum that shrank, was reordered or had a member inserted, or
grew without member names to check (specs written before 2026-11-04 name only the RunActionKind / PromptItemKind /
RewardType members). So the Stage-1 checkpoints of `fc2eb34c07bc8513` resumed on `5dbfa09dd9dcb5ed` (that append only
added buffers and enum members the model does not embed), and the checkpoints of both resume on `3b73317739c650bd` by
widening `RunActionKind` 21 -> 23: the rewards-screen macros (`e9baaefc39811631` onward, `sim/VEC.md` "Rewards-screen
macros") appended `TakeRewardCard` / `TakeRewardAlternative`. `tests/test_resume_compat.py` pins both real specs.
The model does not read the appended `reward_choices` yet: a macro row reaches it through its kind (a zero embedding
in a widened checkpoint until trained), A/B and `card_id`. `sts2train eval` applies the same rule to a checkpoint
(the eval's `config.json` records `spec_compat` when the specs differ).

## Model sizes (`--preset`, or `--d-model --state-dim --depth --heads --ff-mult`)

| Preset | d_model / state / depth / heads | Parameters | Use |
| --- | --- | --- | --- |
| `smoke` (= defaults) | 96 / 256 / 0 / – | 948,974 | the 2026-11-02 smoke model (pooled tables concatenated into the state MLP) |
| `small` | 128 / 256 / 2 / 4 | 1,175,438 | CPU / MPS smokes of the transformer path |
| `large` | 384 / 768 / 6 / 8 | 15,805,070 | `--device cuda` only (one A100) |

Activation memory (autograd's saved tensors, fp32, worst-case 64 candidate / prompt rows; measured by
`model.activation_bytes_per_sample` and printed at start-up for the configured minibatch): smoke 0.41 MiB/sample,
small 1.20, large 7.39 → one minibatch of 4096 saves ≈ 1.6 / 4.8 / **29.6 GiB**. For the large preset on a 40 GB
A100 use `--amp` (bf16 autocast, roughly half), `--grad-checkpoint` (recompute the transformer blocks in backward:
1.71 MiB/sample, ≈ 6.8 GiB at 4096, at the cost of one more trunk forward) and/or `--minibatch 2048`; on CUDA the
trainer warns when the estimate exceeds 70 % of the free GPU memory.

`--deck-counts` adds 14 × d_model parameters (smoke +1,344 = +0.14 %, small +1,792, large +5,376 = +0.03 %),
`--aux-heads` adds two heads of hidden width state / 4 (smoke / small +33,026, large +295,682 = +1.9 %).

Local-size guard: on `cpu` / `mps` the trainer refuses (exit 2, with the activation estimate) a model above 3,000,000
parameters or the `large` preset unless `--allow-big-local` is given (never inherited by `--resume`). On `mps` it
defaults `PYTORCH_MPS_HIGH_WATERMARK_RATIO` to 0.3 (and the low watermark below it) unless they are set.

`depth > 0` adds a pre-LayerNorm transformer trunk (`F.scaled_dot_product_attention`, key padding mask) over 17
typed table tokens plus one token per hand card, enemy and potion row; the state is read from the global token and the
mean of the live tokens, and the contextualised hand / enemy / potion rows feed the candidate pointers. Counts are on
the `3b73317739c650bd` schema (since `e9baaefc39811631` two more `RunActionKind` members than `fc2eb34c07bc8513` /
`5dbfa09dd9dcb5ed`, whose counts were 948,750 / 1,175,150 / 15,804,270); every size, vocabulary and column index is
read from the library's schema at start-up, so a library with extra observation buffers trains unchanged (unused
buffers are ignored).

## Speed (`--compile`, `--static-rows`, `bench-update`)

The default path (`--compile off`) computes exactly what the trainer computed before (tests/test_perf.py replays the
old update loop, and the whole pre-merge `train()` loop of e80ea22 on a real env with fresh starts: rollout, start
bookkeeping, GAE, update; same weights, Adam state and statistics bit for bit) but never waits for the device inside a
minibatch or a rollout step: row cuts come from host-side cand_n / prompt_option_n counts, multi-column reads use
device index buffers (a Python-list index uploads the list, a stream sync per call), every epoch's minibatch indices
go up in one copy, the statistics come back once per update, the sampling distribution skips its argument validation
and CUDA uploads go through pinned memory.

The candidate scorer's card read (`card_t[cid]`, a [V_cards, d] table projected from the card embeddings) and the
prompt options' card read (`card32[iid]`) are `F.embedding` lookups. As advanced-indexing reads their backward was an
`index_put_` with accumulate, which CUDA runs as a sort plus one serial pass over each run of equal indices
(`indexing_backward_kernel`); most rows carry id 0 (non-card candidates, padding, samples without a prompt), so on the
A100 profile of 2026-11-03 (large preset, minibatch 2048, bf16) those two calls took 114 of the update's 244 ms. The
embedding backward (`embedding_dense_backward`: sorted segments reduced in parallel, float accumulation) computes the
same gradient: on the cpu with one thread the loss and every gradient are bit-identical to the indexing reads
(`tests/test_perf.py`, which also asserts that the backward runs no `_index_put_impl_`); on several cpu threads only
the summation order of the old atomic accumulation differs (< 2e-9).

`--compile default` wraps the minibatch forward + PPO loss (backward through AOTAutograd) and the rollout forward in
`torch.compile` (inductor: fused elementwise / reduction kernels); `--compile reduce-overhead` adds CUDA graphs (no
per-kernel launch cost). Both imply `--static-rows`: `cand` / `prompt_options` are always `--max-rows` wide (masked
padding; same logits and loss up to float rounding), so every full minibatch and every rollout step has one shape; a
short last minibatch runs eager. The first update compiles (minutes for the large preset) and first checks the
compiled update and rollout forward against eager on the live batch (loss, every parameter's gradient norm, logits;
bf16 / TF32 tolerances): a mismatch stops the run (`CompiledMismatchError`) instead of training on wrong gradients.
Checkpoints are unchanged (the compiled functions wrap the same module).

```bash
# the PPO minibatch update (gather, forward + loss, backward, clip, Adam) and the rollout step on random inputs of the
# schema's shapes: ms per minibatch, samples/s, numerics check, --profile = top ops by device time
sts2train bench-update --preset large --minibatch 2048 --device cuda --amp --modes off,default,reduce-overhead --profile
# local validation only (cpu / mps; refuses an estimated > 3 GiB of activations)
PYTORCH_MPS_HIGH_WATERMARK_RATIO=0.3 sts2train bench-update --preset small --minibatch 512 --envs 256 --device mps \
    --modes off,default
```

## Pieces

| Module | Content |
| --- | --- |
| `features.py` | `ModelSpec` (vocab / enum sizes, column maps, shapes, encoder_version from the published schema; stored in checkpoints), `check_spec_compat` (may a checkpoint's spec drive this library: model inputs identical), `obs_to_device` / `obs_to_torch` (the 27 buffers the model reads; `cand` / `prompt_options` cut to the rows the batch needs), `RolloutStore` ([T, N, ...] tensors on the training device in source dtypes, 643 MiB at 32 × 1024 with 64 candidate / prompt rows; minibatches are gathered on the device) |
| `heads.py` | `--aux-heads`: `AuxTargets` (per-window targets and masks of the win-probability / combat-hp heads), `aux_losses`, `HEAD_OUTPUTS` (the definitions checkpoints record) |
| `model.py` | `Policy`: id embeddings for cards, relics, potions, powers, monsters, encounters, events, orbs, enchantments, afflictions, card vars and every enum; per-table row MLPs (hand / card reward share one card encoder; deck and piles are embedding bags; map nodes, prompt options single layer) with masked mean pooling; optional transformer trunk; candidate scorer whose first layer is factored per source (kind, A/B, hand_slot → hand row, target_slot → enemy row, potion_slot → potion row, option_index → the option bank of its kind, card_id → card embedding, state) and scored bilinearly against the state; masked to `j < cand_n`; value head. `PRESETS`; `MODEL_FLAGS` (`aux_heads`, `deck_counts`: optional parts, created last) |
| `ppo.py` | `TrainConfig`, `plan_run` (fresh / resume config resolution), `train` (PPO: GAE, clipped policy and value loss, entropy bonus, Adam, optional bf16 autocast on CUDA, TF32 on CUDA), `ppo_loss` / `MinibatchUpdater` / `InferenceRunner` (the update step and rollout forward, eager or compiled) and `check_compiled`, checkpoints, status / metrics / evals, budget and signal handling, uniform-random baseline |
| `bench.py` | `sts2train bench-update`: random observations of the schema's shapes and value ranges (`random_obs`), timing of `ppo.MinibatchUpdater` / `ppo.InferenceRunner` per compile mode, eager-vs-compiled check, profiler table |
| `runlog.py` | atomic writes, `JsonlWriter`, `CheckpointManager` (rotation, `best.pt`) |
| `objective.py` | `EpisodeRewards`: per-slot reward of the `protocol` / `win` / `shaped` objective and finished-episode records (protocol progress fields + the episode's return); `AcqShaping` (`--acq-shaping`) |
| `startpool.py` | `--start-saves`: `probe_saves` (import every save once, first observation, skip reasons), `plan_layout` (interleaved fresh / save entry layout for the requested mix), `build_start_pool` (fills the env's pool; determinized or exact save copies), `split_stats` (the `fresh_*` / `save_*` metrics) |
| `progress.py` | `ProgressTracker`: protocol v0.2 progress of finished auto-reset episodes from the previous call's `aux` (act_floor, act_rooms) + `last_episode`, via `sts2vec.protocol.compute` (the evaluator's copy of `sts2eval.progress.compute`, pinned to it by sts2vec's tests) |
| `evaluate.py` | thin wrapper over `sts2vec.evaluate`: greedy policy of a live model (`evaluate_model`, used during training) or a checkpoint (`evaluate`, `sts2train eval`, which exits 1 when the replay audit fails, as `sts2vec eval`); one pool entry and one slot per seed, protocol limits (step_cap 1200 → `capped`, floor_step_cap 80 non-combat steps → `stuck`), the LLM runner's run-dir layout + `report.md`; the loop breaker's counts in the game summaries / report.json (`write_loop_stats`); with aux heads the per-decision readouts in the game summaries (`HeadLog`) |
| `loopbreak.py` | `LoopBreaker` (the greedy eval policy's no-op / cycle breaker, optionally the rollout sampling's) and `ObsDigest` (vectorised 64-bit digest of the model-read buffers per slot) |
| `reportcard.py` | `sts2train report-card` / `pack` / `probe`: the model card of a checkpoint (evals run or reused, baselines cached, `stages` / `endings_table` / Wilson rows, `--lookahead-dir` rows paired per seed), the weights bundle (`pack`, `verify_bundle`), the per-decision readout of an eval's games (`probe`) |
| `lookahead.py` | `python -m sts2train.lookahead eval / paired`: decision-time combat lookahead on determinized clones as an `sts2vec` policy (`LookaheadPolicy`: private mirror env, K candidates × R roots, turn / combat horizon, `value+hp` leaf; ABI 2 `env.branch` path, pool + rebuild path before it), `--record` ExIt rows, `evaluate_greedy` (the paired baseline, loop breaker off), `paired` (per-seed differences, t and bootstrap intervals); `docs/lookahead-2026-11-04.md` |

Objective (`objective.py`, `--objective`): the default `protocol` rewards each step with the change of the protocol
v0.2 progress metric (§4.2 of `docs/sts2-bench-protocol-v0.2-2026-11-01.md`: per-act normalised, `sts2vec.protocol.compute`,
the function `sts2vec eval` scores with) and adds `--win-bonus` (default 1.0) at a Victory terminal. Rewards telescope,
so a fresh episode's return is exactly its protocol progress (+ bonus on a win); truncated episodes keep the progress
of their last state, as the protocol scores `capped` / `stuck` games (`tests/test_objective.py` replays synthetic and
real episodes against the evaluator's bookkeeping). `win` is the contract's reward (1 at a Victory terminal, else 0).
`--shaped` (= `--objective shaped`) is the LEGACY smoke objective, `win` + `delta(total_floor) / 48` per step.
`--floor-step-cap N` is passed to `VecEnv` (stall truncation: an episode with N non-combat decisions on one floor is
truncated, status `FLOOR_STALL`, scored like the protocol's `stuck`) when the installed sts2vec supports it; otherwise
it is an error rather than a silent no-op. Unset, the env's default applies: 80 (`sts2vec.FLOOR_STEP_CAP`, the
evaluator's `stuck` rule) on a library that implements the cap. The cap in effect is in `config.json` `env`.

## Training-signal flags (`--aux-heads`, `--deck-counts`, `--acq-shaping`)

Motivated by the 2026-11-03 trajectory diagnosis of the Silent plateau (the policy never takes card rewards; the critic
valued "take a card" at +0.00004 against a measured +0.0185 progress; lambda 0.95 over 32-step windows truncates the
credit; the deck was a mean of card-id embeddings). All three are off by default; off, the trainer computes exactly
what it computed before (`tests/test_perf.py` runs the e80ea22 loop with them given explicitly off). Whether they
help is an experiment, not something the tests show.

- **`--aux-heads`** (architecture; `--aux-coef` 0.1, `--aux-bootstrap` on): two heads on the state embedding next to the
  unchanged value head (`heads.py`). `win_prob` = sigmoid(win logit), binary cross-entropy to the episode outcome
  (Victory 1, Defeat 0); `combat_hp_pred` = sigmoid(combat logit), squared error to hp / max_hp when the current combat
  ends (a combat ends when `aux.is_combat` drops, `aux.combats_won` grows or the episode ends; a death in it = 0), for
  states in combat only. Targets per rollout window (`heads.AuxTargets`): outcomes seen inside the window are exact;
  truncated episodes (step cap, floor cap, sim errors) and combats a truncation cut are masked; episodes / combats still
  running at the window end get the head's own prediction at the state after the window (the n-step bootstrap the
  value head uses; `--no-aux-bootstrap` masks them, leaving only the last ≤ `--rollout` steps before an outcome).
  Losses are masked minibatch means, added to the PPO loss times `--aux-coef`; they train the shared encoder.
  Metrics: `aux_win_loss`, `aux_combat_loss`, `aux_win_frac` / `aux_combat_frac` (minibatch share with a target),
  `aux_win_targets` / `aux_win_exact` / `aux_win_target_mean`, the same for combat, `aux_win_prob_mean` /
  `aux_combat_pred_mean` (the heads at the state after the window). Checkpoints record `arch.aux_heads` and `heads`;
  `sts2train eval` (and the periodic eval) writes `win_prob` (one value per `history_actions` entry: the "通关把握"
  readout), `combat_hp_pred` (null outside combat), `win_prob_first` / `win_prob_last` into
  `games/<KEY>.summary.json`, lists the heads in `config.json` and prints `head_readout`.
- **`--deck-counts`** (architecture): the deck becomes a count-aware embedding bag: rows weighted log(1 + k) / k (k =
  copies of the row's card id), so each distinct card adds log(1 + copies) × the mean of its rows (identity, upgrade,
  enchantment), plus 7 count features (log(1 + n), n / 32, log(1 + distinct), log(1 + upgraded), upgraded share,
  log(1 + enchanted), log(1 + afflicted)); each combat pile's bag is sum_id log(1 + copies) × embedding (plus distinct
  ids and upgraded copies); the candidate scorer gets log(1 + copies of the candidate's card in the deck). The schema
  has no static per-card numbers for deck rows (id, upgrade, enchantment, affliction only); hand and card-reward rows
  keep their full numeric features.
- **`--acq-shaping`** (`--acq-deck-coef` 0.003, `--acq-relic-coef` 0.01, `--acq-upgrade-coef` 0.004): potential-based
  shaping on top of the objective (`objective.AcqShaping`): phi(s) = deck_coef × min(deck_n − 10, 12) + relic_coef ×
  relic_n + upgrade_coef × (summed upgrade levels), phi = 0 after a terminal or truncation, F = gamma × phi(s') −
  phi(s) per step. An episode's shaping telescopes to −phi(first observation), so the optimal policy is unchanged;
  acquisitions are paid when taken. PPO's returns / advantages use the shaped reward; `reward_mean`, `return_mean`,
  `fresh_*` / `save_*` and every progress number stay the objective's, and the shaping is logged apart:
  `reward_mean_shaped`, `shaping_mean` (per step), `shaped_return_mean`, `shaping_return_mean` (finished episodes),
  `acq_potential_mean`; episode records carry `shaping` / `shaped_return`. Evaluations never see it.

`--aux-heads` and `--deck-counts` are part of the architecture: a run resumes only with the setting its checkpoint
was trained with (other values are refused before the env starts, like the size flags), and their modules are created
after all others, so a model with a flag on starts every other parameter from the same initial values as without it.
`--acq-shaping` and the coefficients are objective settings and may change on resume. `bench-update --deck-counts
--aux-heads` times the flagged model / loss.

## Start saves (`--start-saves DIR --save-mix R`)

Episodes can start from real game saves (mid-run states: Act 2/3 rooms, human-built decks) mixed with fresh starts,
deterministically given `--seed` and the saves' contents (`sts2train/startpool.py`):

```bash
# collect the recorder's save copies (read only) -> DIR/*.save + DIR/manifest.json (session, floor, act, sha256, ...)
uv run --project sim/tools/vec python scripts/train/collect_start_saves.py --out tmp/train/saves-silent-a9 \
    --character SILENT --ascension 9 --check
uv run --project sim/tools/train sts2train train --characters SILENT --ascension 10 \
    --start-saves tmp/train/saves-silent-a9 --save-mix 0.5 ...
```

- **What a save is.** The game's `current_run.save` (schema 20, v0.111.0), written when entering a map point, after a
  won combat and after the act's Ancient. `env.pool.add_save` imports it with `RunImporter.FromSave` (checked) and the
  episode starts where the game would resume: the entered room from its start (a combat's first turn, an event, a
  shop, ...; the sim's floor is the save's history + 1), the won combat's Rewards screen, or the Ancient's last page.
  There are no mid-combat saves. A save keeps its own character and ascension: `--ascension` applies to fresh entries
  only (the trainer logs a note when they differ), and the protocol metric uses the act's map length from `aux`.
- **Probe.** Every `*.save` under DIR (recursive, sorted) is imported once in a 1-slot env; unreadable or unsupported
  saves (the importer's reasons, e.g. an unmapped relic property), a first observation with SIM_ERROR / CONTENT_GAP or
  no legal action, characters outside `--characters` and byte-identical duplicates are skipped and reported. No usable
  save is an error.
- **Mixing** (entry weights in the start sequence, sim/VEC.md: a slot's k-th episode starts from entry
  `(slot_seed + k) mod len(pool)`). The pool holds the `--pool` fresh entries (the same characters and seed texts as
  without saves) and `c = max(1, round(pool * R / (1 - R) / saves))` copies of every usable save, interleaved evenly
  (save at position j iff `floor((j+1) S / P) > floor(j S / P)`), so every slot alternates in the ratio from its first
  episode on. The effective fraction `S / P` is printed and stored (2048 fresh + 146 saves x 14 at R = 0.5:
  0.4995). `R = 1` drops the fresh entries (`--pool` then sets the number of save entries). Copy 0 of a save is the
  import itself; with `--save-copies determinized` (default) the other copies are `env.clone_determinized` of it: the
  same player view, hidden state (draw order, RNG streams, relic bag, unvisited event order) resampled per copy from
  (seed, save sha256, copy), so the copies do not all replay the human's RNG; `exact` re-imports the save (every start
  of it is the same state, RNG included).
- **Bookkeeping.** Rewards telescope from an episode's first observation, so a save-started episode's return is the
  protocol progress gained from the save (+ the win bonus), never the progress it started with. Finished-episode
  records carry `start_entry`, `start_floor`, `start_act`, `start_progress` and `progress_gain`; `metrics.jsonl` adds
  `fresh_episodes` / `fresh_wins` / `fresh_win_rate` / `fresh_progress_mean` / `fresh_floor_mean` /
  `fresh_return_mean` (directly comparable with a run without saves) and the same `save_*` keys plus
  `save_start_progress_mean`, `save_progress_gain_mean`, `save_start_floor_mean`, `save_floors_gained_mean`,
  `save_act_reached`, `save_episodes_by_start_act`, `save_gain_by_start_act`. `save_progress_mean` is the run's
  absolute progress at the end (what the evaluator would score from that save), so the all-episode `progress_mean`
  is inflated by save starts: watch `fresh_progress_mean` and the protocol evals. `status.json` `recent` and the log
  line carry the split too; `sts2train plot` draws both curves.
- **Resume** keeps the saves directory of the checkpoint's config (an absolute path); `--start-saves NEWDIR` points it
  elsewhere, `--start-saves none` turns save starts off.

Training restricts actions to the first 64 candidates (`--max-rows`; rare overflow is counted as `cand_over_max_rows`);
eval passes every candidate row.

## Loop breaker (`--loop-breaker`, `--rollout-loop-breaker`)

A greedy policy that takes an action which leaves the observation unchanged (a no-op: `BuyEntry` of a potion with a
full belt, an event option that changes nothing visible) or that leads back to it through other screens (a cycle:
Rewards → claim the card reward → CardReward → skip → the same Rewards view) sees the same input again and repeats
itself until the floor step cap ends the game `stuck`. On 2026-11-04 that was 2/20 games for the A10 checkpoint s3c
(152 no-op potion buys) and 12/20 for the A0 mid-size checkpoint s5b (232 no-ops, 681 cycle revisits).

`sts2train.loopbreak.LoopBreaker` keeps, per env slot, a 64-bit digest of the 27 model-read buffers (`ObsDigest`: sum
of 4-byte words times fixed odd keys mod 2^64; one changed word always changes it) for each of its last 64 decisions
and the action taken there. At an observation already in the window it masks (`NEG`) every candidate row it took there
and the policy takes the best remaining one; from the 3rd return (`--loop-breaker-k`) only the rows that leave the
screen (ProceedRewards, LeaveShop, LeaveRestSite, LeaveEvent, SkipTreasureRelic, EndTurn, PromptConfirm,
PromptCancel) stay unmasked when one is legal and not yet taken, else the best remaining row. If every legal row was
already taken nothing is masked (the env's caps apply as before). The leave kinds are resolved by name against the
library's `RunActionKind` (the evaluator and the trainer build the breaker from the library's spec, never from a
checkpoint's), so appended kinds shift nothing; the rewards-screen macros `TakeRewardCard` / `TakeRewardAlternative`
(21 / 22 since `3b73317739c650bd`) are real actions, never leave rows. That library also removes the potion no-op and
the card-reward cycle above at the source (a potion purchase or claim the game would refuse is not listed; a card is
taken in one `TakeRewardCard` decision); illegal indices and the shop removal → prompt → cancel cycle remain.

- **Eval** (`sts2train eval`, the periodic eval): on by default; `--loop-breaker off` is the plain argmax. The
  evaluator records and replays the actions actually sent (the replay audit is unchanged). Each game summary gets
  `loop_breaker`, `loop_breaks` (decisions where the chosen row is not the model's argmax), `no_op_actions` (actions
  that left the observation unchanged) and `cycle_revisits` (returns through other observations; both counted with
  the breaker off too); `config.json` records `loop_breaker` (mode, k, window, leave kinds), `report.json` and the
  CLI's JSON the totals.
- **Training** (`--rollout-loop-breaker`, off by default): the same mask on the rollout's sampling distribution; the
  stored log-probs are the masked distribution's, so PPO's ratio is taken against the policy that actually sampled.
  `metrics.jsonl` gets `loop_breaks` / `no_op_actions` / `cycle_revisits` per update, `summary.json`
  `rollout_loop_breaker`. Off, the rollout is unchanged (`tests/test_perf.py` pins the default path bit for bit).

| Checkpoint (seed set) | Breaker | Progress | Stuck | No-op actions | Cycle revisits | Replay |
| --- | --- | --- | --- | --- | --- | --- |
| s3c A10 u160 (silent-a10-v1) | off | 0.156 | 2 | 152 | 0 | 20/20 |
| | on | 0.160 | 0 | 3 | 0 | 20/20 |
| s5b A0 u160 (silent-a0-v1) | off | 0.330 | 12 | 232 | 681 | 20/20 |
| | on | 0.500 | 0 | 9 | 12 | 20/20 |

(4 cpu threads, library `5dbfa09dd9dcb5ed`; the remaining s5b-on non-loss is one game `capped` by the 1200-step limit
in act 3, floor 48.)

## Model card and weights bundle (`report-card`, `pack`, `probe`)

```bash
# per seed set: the greedy eval (run, or reused when CARD/evals/<seed set> already holds one of these weights on this
# library), the random / first / heuristic baselines (sts2vec eval, cached by policy, seed set and library), then
# CARD/model-card.md + model-card.json
uv run --project sim/tools/train sts2train report-card --checkpoint best.pt --out tmp/card/s3c \
    [--seedsets silent-a10-v1,silent-a10-dev-v1,silent-a10-test-v1] [--threads 4] [--device cpu] \
    [--lookahead-dir LA_RUN ...] [--baselines random,first,heuristic | none] [--baseline-cache tmp/eval/baselines]
# FILE.tar.gz (top directory FILE), re-read and checked after writing; the card comes from --card (default FILE.card
# next to the archive) when it is this checkpoint's (sha256), else it is made there with the report-card options
uv run --project sim/tools/train sts2train pack --checkpoint best.pt --out tmp/release/s3c-u160.tar.gz --card tmp/card/s3c
# replay an eval's games and read the checkpoint at every decision: action taken, top candidates with probabilities,
# value head (+ win_prob / combat_hp_pred with aux heads); JSON lines with --out
uv run --project sim/tools/train sts2train probe --checkpoint best.pt --eval-dir tmp/card/s3c/evals/silent-a10-v1 \
    --game SILENT-C1A10SI01 --top 3 --out tmp/card/s3c/probe.jsonl
```

- **Card** (`sts2train/reportcard.py`): checkpoint identity (file sha256, weights_hash recomputed from the stored
  weights, update, env steps, both encoder versions and whether the model's inputs are identical or widened,
  architecture and preset, aux heads, parameter count, the training flags of the checkpoint's config, `*` marking a
  flag the config predates) and per seed set and policy (NN greedy, lookahead rows, baselines): the protocol headline
  (progress mean [95% CI], win rate [Wilson 95%], floor, results, replay audit), the staged table (reached / beat the
  Act-1 boss, reached Act 2, reached / beat the Act-2 boss, reached Act 3, cleared; each count / N [Wilson 95%]), the
  floor histogram, how the games ended (deaths by the encounter id's type: boss / elite / normal / event fight; stuck,
  capped, crashed; the most common encounters), deck size and relics at death when the summaries carry them
  (`sts2vec eval` records neither today, so the section is omitted), and the aux win-probability head at each game's
  first and last decision (a calibration hint). "Beat the Act-N boss" = left act N, or ended `stuck` in its boss room
  (the floor step cap counts only non-combat decisions, which a boss room offers only after the fight is won; `capped`
  there does not count). Exit 1 when a greedy eval fails the replay audit, 2 when the checkpoint cannot drive the
  library (`features.check_spec_compat`).
- **Reuse.** `CARD/evals/<seed set>` is reused when its `config.json` matches the weights, seed set content, library
  (`server_version`), protocol limits and loop breaker (on, k 3) and every game has a summary; otherwise it is moved
  to `*.stale-<time>` and played again. Baselines are cached the same way (policy, seed set content, library; policy
  seed 0). `--lookahead-dir` run directories (`python -m sts2train.lookahead eval`) are read only: a search run is a
  "lookahead, determinized: la(K,R,horizon,honest) <score>" row with its search declaration and `stats.json`
  counters, paired per seed (`lookahead.paired`) against a `--greedy` run of the same weights (loop breaker off, as
  the lookahead plays outside its search), else against the card's greedy row (loop breaker on: flagged, Δ then
  mixes the search with the loop breaker); a `--greedy` run is an "NN greedy, loop breaker off" row; any other run
  directory is refused. A differing weights hash or library is noted on the row.
- **Bundle**: `checkpoint/<file>` (as trained, optimizer state included when it has one), `checkpoint/spec.json`,
  `checkpoint/config.json` (config, arch, heads, counters, training-time evals), `model-card.md` / `.json`,
  `library.json` (encoder_version, ABI version, spec compatibility), `README-weights.md` (verify, evaluate, re-make
  the card, probe, and the live twin runner `scripts/live/nn_twin_play.py` between `scripts/mcp_mod.sh on` / `off`),
  `manifest.json` and `SHA256SUMS` (`shasum -a 256 -c SHA256SUMS`).
- **Seed sets**: `silent-a10-dev-v1` (`silent-a10-dev-s128`, D1A10SI001..128) and `silent-a10-test-v1`
  (`silent-a10-test-s100`, H1A10SI001..100, held out), THE SILENT A10, next to the 20-game `silent-a10-v1`; and
  `silent-a0-dev-v1` (`silent-a0-dev-s100`, D1A0SI001..100) for A0.

## Combat lookahead (`python -m sts2train.lookahead`)

```bash
# the checkpoint's greedy policy plus a search at combat decisions on determinized clones (never the true hidden
# state): K 8 candidates, R 1 root, turn horizon (H 12; --horizon combat: H 40), value+hp leaf score
uv run --project sim/tools/train python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 --checkpoint best.pt \
    --out tmp/eval/la --threads 4 [--K 8 --R 1 --horizon turn --score value+hp --record]
# the paired baseline: the sts2train eval path with the loop breaker off (the lookahead's own choices outside the
# search are the plain argmax), so the two runs differ only by the search; K = 1 is identical to it
uv run --project sim/tools/train python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 --checkpoint best.pt \
    --out tmp/eval/la-greedy --threads 4 --greedy
uv run --project sim/tools/train python -m sts2train.lookahead paired tmp/eval/la-greedy tmp/eval/la
# both as rows of the model card
uv run --project sim/tools/train sts2train report-card --checkpoint best.pt --out tmp/card/x \
    --seedsets silent-a10-dev-v1 --lookahead-dir tmp/eval/la tmp/eval/la-greedy
```

The run directory is a normal protocol run (`sts2vec.evaluate` plays, scores and replay-audits it) with the search
declared in `config.json` (`search`, `information: "determinized, combat-only"`, `mirror_env`; policy
`nn:<ckpt>+la(K,R,horizon,honest)`) and the search counters in `stats.json` (searched / changed decisions, ms per
searched decision, rollout errors, desyncs, the code's sha256); `--record` adds `searches.jsonl` (per searched
decision: candidates, mean Q, choice; ExIt targets). The lookahead has no loop breaker. Measurements and the ExIt
go / no-go: `docs/lookahead-2026-11-04.md` (A10 dev +0.0152 [+0.0080, +0.0223], below the +0.02 bar).

## Colab bundle (a box without .NET: the Colab A100 runtime)

`scripts/train/make_colab_bundle.sh` writes `tmp/colab/sts2train-<gitsha>.tar.gz`, built from the committed HEAD:

| Path | Content |
| --- | --- |
| `lib/Sts2Sim.Vec.so` | linux-x64 NativeAOT build of `Sts2Sim.Vec` from the Dockerfile's `aot` stage (13 MB). It needs only `libc` / `libm` / `ld-linux` (newest symbol `GLIBC_2.34`), no .NET runtime and no data files: the rules and content are compiled in, fresh starts are generated from seeds (`knowledge/source` is only the play server's rules text). sts2vec loads it with `ctypes.CDLL` when `STS2VEC_AOT_LIB` is set (no hostfxr, no runtimeconfig) |
| `seedsets/*.json` | the protocol seed sets (`STS2VEC_SEEDSETS`), read by the periodic eval |
| `wheels/` | `sts2vec` and `sts2train-smoke` (pure-python `py3-none-any`, installed with `--no-deps`) |
| `requirements-colab.txt` | `numpy` (torch is preinstalled on Colab and deliberately not listed) |
| `run_train.sh RUN_DIR [THREADS=12] [ENVS] [train flags]` | `python3 -m pip install` of the above (no uv), exports `STS2VEC_AOT_LIB` / `STS2VEC_SEEDSETS`, `sts2vec check` (→ `RUN_DIR/sts2vec-check.txt`), then `sts2train train --out RUN_DIR --resume auto --threads THREADS [--envs ENVS] ...` |
| `smoke_bundle.sh [RUN_DIR] [MINUTES] [THREADS]` | validation on any linux x86_64 CPU box: CPU torch if missing, a short CPU run through `run_train.sh` (small preset, `--floor-step-cap 80`, checkpoints, an eval on `std-a0-v1`; `--start-saves --save-mix 0.5` when the bundle has `saves/`), checks of the run directory (the cap in effect in `config.json`, the encoder version; with saves: `config.start_saves` set, finished fresh- and save-started episodes, no SIM_ERROR / CONTENT_GAP, the saves' start-floor histogram), a check that the bundled library fills the six appended observation buffers (documented shapes; `event_option_values` is resolved by reflection, which a NativeAOT publish could trim), then a raw `sts2vec bench` |
| `saves/` | only with `SAVES_DIR=DIR scripts/train/make_colab_bundle.sh`: DIR's `*.save` files and `manifest.json` (`collect_start_saves.py` output; data, never committed). `run_train.sh` prints the path for `--start-saves` |
| `manifest.json` | git sha, AOT cache key, newest GLIBC symbol, size and sha256 of every file |

The `.so` is built on an amd64 Docker host over ssh (`AOT_HOST`, default `raven`: `git archive` → `docker build
--platform linux/amd64 --target aot` → `docker cp` → `scp`; build dirs under `/data/raven/hack-sts2/tmp/colab-aot/`),
never with emulation on a Mac, and cached under `tmp/colab/aot/<key>/` keyed by the git trees of `sim/Sts2Sim.Core`,
`sim/Sts2Sim.Vec`, the Dockerfile and `scripts/vec_publish_aot.sh` (`AOT_SO=path` reuses a prebuilt library).

```bash
scripts/train/make_colab_bundle.sh                          # -> tmp/colab/sts2train-<gitsha>.tar.gz
# validate on Huginn in a plain python container (no dotnet)
docker run --rm -v "$PWD:/w" -w /w python:3.13 bash sts2train-<gitsha>/smoke_bundle.sh /w/smoke-run 2 8
# Colab (A100 runtime, 12 vCPU): unpack on local disk, keep RUN_DIR on Drive; re-running the same line resumes
tar xzf sts2train-<gitsha>.tar.gz -C /content
bash /content/sts2train-<gitsha>/run_train.sh /content/drive/MyDrive/sts2/run1 12 2048 \
    --preset large --device cuda --amp --max-hours 6 --ckpt-every 50 --ckpt-every-min 15 --eval-every 50
# with start saves: SAVES_DIR=tmp/train/saves-silent-a9 scripts/train/make_colab_bundle.sh, then add
#   --characters SILENT --start-saves /content/sts2train-<gitsha>/saves --save-mix 0.5
```

Target box (Colab A100 runtime, 2026-11-03): Python 3.13, x86_64, glibc 2.39, torch 2.11.0+cu130 and numpy 2.4.6
preinstalled, 12 vCPU, no dotnet, no uv. The wheels are pure Python and the library's newest glibc symbol is 2.34.
