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
# fresh starts from a never-repeating seed stream (SP<env-seed>-<CHARACTER>-<n>), decoupled seeds; see "Start modes"
uv run --project sim/tools/train sts2train train --start-mode stream --characters SILENT --ascension 10 \
    --init-seed 1 --sample-seed 1 --env-seed 0 --preset large --device cuda --amp --envs 2048 --out tmp/train/w0

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
| `metrics.jsonl` | one row per update: env steps, episodes, finished-episode `progress_mean` / `win_rate` / `floor_mean` / `return_mean` / `act_reached`, `policy_loss`, `value_loss`, `entropy`, `approx_kl`, `clipfrac`, `grad_norm`, `explained_variance`, `env_steps_per_s`, time split `t_env_s` / `t_infer_s` / `t_update_s` / `t_ckpt_s` / `t_eval_s` / `t_other_s`, error counts, `floor_stall` (episodes truncated by the floor step cap this update), `gpu_mem_*_mb` (CUDA / MPS); with `--start-saves` also the `fresh_*` / `save_*` split (see "Start saves"), with `--harvest` the `harvest_*` keys and in stream mode `anchor_starts_update` / `anchor_start_failed` (see "Self-anchor starts"); with `--aux-heads` the `aux_*` keys, with `--acq-shaping` the shaping keys (see "Training-signal flags"), with `--rollout-loop-breaker` `loop_breaks` / `no_op_actions` / `cycle_revisits` (see "Loop breaker"); always the UI revisit cycle counts `cycles_total` / `cycles_rest` / `cycles_shop` / `cycle_steps` / `cycles_rejected` / `episodes_with_cycle` / `cycle_max_per_episode` / `cycle_cost_total` and `reward_sum_trained` (the reward PPO trained on, summed over the update's valid steps; see "UI revisit cycles") |
| `evals.jsonl`, `evals/u<update>/` | every `--eval-every` updates and once at the end of a budget-ended session: `sts2vec.evaluate` on `--eval-seedset` (default `std-a0-v1`) with the live model, greedy; each row has the headline (`progress_mean`, CI, wins, floor, results, replayable) and the checkpoint it was taken at; `best_skip` (why the row cannot be `best.pt`) when it is not clean; the greedy policy runs with the loop breaker (`--loop-breaker off` turns it off) and the row carries `loop_breaker`, `loop_breaks`, `no_op_actions`, `cycle_revisits`; a model with `--aux-heads` also writes per-decision `win_prob` / `combat_hp_pred` into each game summary |
| `cycles.jsonl` | one row per UI revisit cycle closed in a rollout (`update`, rollout step `t`, `slot`, `episode`, room `phase`, `prompt`, `act`, `floor`, `len`, `step` = the episode's decision index, `path` / `actions`); each finished episode's count is the `cycles` field of its `episodes.jsonl` row |
| `evals/u<update>/<mode>/` | `--eval-at` runs: one protocol run directory per mode (`greedy-on`, `greedy-off`, `sample-off`), one `evals.jsonl` row each with `mode`, `eval_at`, `stuck`, `cycles`, `games_with_cycle`, `loop_breaks`; the weights stay in `checkpoints/pinned-<update>.pt` |
| `checkpoints/ckpt-<update>.pt` | every `--ckpt-every` updates and every `--ckpt-every-min` minutes (tmp file + fsync + rename), the last `--keep-ckpts`; weights, Adam state, counters, RNG states, `spec`, config, `arch` (size knobs plus `aux_heads` / `deck_counts` when on), `heads` (output definitions, `--aux-heads` only), `weights_hash`; `best.pt` = the checkpoint of the best clean eval so far (highest `progress_mean` among evals that ran, replayed every game — `replayable == games` — and had no `crashed` / `error` game: `ppo.best_skip_reason`) |
| `config.json` | resolved config (incl. `start_saves` / `save_mix` / `save_copies`), architecture, parameter count, library path / version, `encoder_version` (this session's library) and `checkpoint_encoder_version` + `resume_compat` (the checkpoint it resumed from, `model_inputs` identical / widened and `widened_enums`; `null` for a fresh run), `env` (`step_cap`, the `floor_step_cap` in effect, 0 = off), one entry per session (host, device, torch, resumed from, both encoder versions, `model_inputs` / `widened_enums`, and since 2026-11-04 `given` (the command line's explicit flags), `config_diff` ({key: [checkpoint, this session]}), `seeds`, `rng_restored` ({numpy, torch, cuda: ok / failed / absent / reseeded}), `adam_step`, `init_weights_hash` (fresh runs), `start_mode`, `seed_stream_next`, `dropped_at_start`, `best_eval_source`); `start_pool` (with `--start-saves`, rebuilt every session: entry counts, effective mix, skipped files and reasons, start floor / act / phase / ascension histograms, one row per usable save); `harvest` (with `--harvest`: rings, probability, per-step cap, base pool size, start mode, the capture rule); `start_mode`, `seeds`, `isolation` (checked holdout sets, collisions), `seed_stream` (see "Start modes, seeds and isolation") |
| `episodes.jsonl` | one row per training episode: `update`, `session`, `slot`, `seed_text`, `n` (stream index), `result` (win / death / trunc / stall / error; `dropped` and `start_failed` rows too), `act`, `floor`, `steps`, `progress`, `return`, `end_kind`, `cycles` (UI revisit cycles the episode closed, `sts2train.cycles`; null on `dropped` / `start_failed` rows), `start_kind` (only with `--harvest` in stream mode: `stream` / `anchor:<kind>`), the run state at the end (since 2026-11-05, "Run-state fields" below: `hp`, `max_hp`, `deck`, `upgraded`, `relics`, `gold`, `potions`, `killer`, `boss_entry`, `room`; absent on older rows and on `dropped` / `start_failed` rows) + the fields of registered hooks (`sts2train.episodes`); a resume drops the rows past the checkpoint; `--no-episodes-log` turns it off |
| `blind-access.jsonl` | one row per allowed evaluation on a frozen blind seed set (`--allow-blind --purpose`): utc, weights hash, seed set, purpose |
| `summary.json` | written when a session ends (stop reason, throughput and time split, totals, best / last eval, `seeds`, `starts`, `dropped_total`, `inflight_at_end`, `seed_stream` with the stream's timing, `isolation`) |
| `train.log` | the CLI's log lines with timestamps, appended by every session |

Stops: `--max-hours` (this session's wall clock; the loop stops before an update that would overrun it),
`--max-updates` / `--max-env-steps` (totals across resumes), or SIGINT / SIGTERM (finish the update, checkpoint,
exit; a second SIGINT aborts). A resumed session restores weights, optimizer, counters and RNG states; the env restarts
from fresh episodes (pool mode: a new start sequence; stream mode: the stream continues at the checkpoint's counter),
`metrics.jsonl` / `evals.jsonl` / `episodes.jsonl` drop rows past the checkpoint, and the checkpoint's in-flight
episodes are logged as `dropped`. `best_eval` / `last_eval` are recomputed from the checkpoint's run directory's
`evals.jsonl` (rows up to the checkpoint's update, the session's eval seed set): a checkpoint is written before its
own update's eval, so the values stored in it lag by one eval (Q9); a fork into a new directory reads the source run's
file. A fresh run into a directory that already holds checkpoints is refused.

**Run-state fields** (`sts2vec.runstats.RunStats`, 2026-11-05; logging only: actions, RNG streams and weights are
unchanged, the 3-update pool / stream smokes keep their `weights_hash`). Every finished episode's `episodes.jsonl` row
and every `sts2train eval` / `sts2vec eval` game summary (`games/<KEY>.summary.json`) carry:

| Key | Meaning |
| --- | --- |
| `hp`, `max_hp` | at the end (`last_episode.final_hp` / `final_max_hp`): 0 at a death; meaningful for win / trunc / stall |
| `deck`, `upgraded`, `relics`, `gold`, `potions` | deck size, deck cards with an upgrade, relic count, gold, filled potion slots at the episode's last decision |
| `room` | the final room's kind: the `MapPointType` of the current map node (`Monster`, `Elite`, `Boss`, `RestSite`, `Shop`, `Treasure`, `Ancient`); an `Unknown` node resolved by what was seen on that floor (a fight -> `Monster`, else `Event` / `Shop` / `Treasure` / `RestSite`); null before the first map move |
| `killer` | the encounter id of the fight a death happened in (`last_episode.last_encounter_id`); null for other endings and for a death outside combat |
| `boss_entry` | `[{act, floor, hp, max_hp, boss}]`, one per act-boss room entered (Boss map node with a combat running), hp at the fight's first decision, `boss` = its encounter id (`boss_id`, or `second_boss_id` for a double-boss act's second boss) |

They are read from the observation the slot decided at before the step that ended the episode (`RunStats.observe`
next to the cycle detector's `commit`, in both rollout loops and per `--overlap double` lane), never from the
observation after it, which with auto-reset is already the successor's first one; the per-episode state is cleared at
the episode's end, so pool / stream / `--harvest` anchor starts and resumed sessions (whose in-flight episodes are
`dropped` rows without these fields) start empty. `--deck-counts` changes only the model: the `deck` buffer is always
observed, so the fields do not depend on it. Cost: one gather of a few global_i columns, a count over the deck's
upgrade column and the potion slots and a gather of the current map node per slot per rollout step (numpy over all
slots), plus a dict per boss entry: ~0.8 ms per step at 2,048 slots (about 0.5% of the env step, measured in the testbox), ~+155 bytes per row (~360 in all). Eval summaries also get `fights` (below) and a filled
`death.room_type` (it was always null). Readers keep working on rows / summaries without the keys (`report-card`
reads `deck` / `relics` at death when present).

Eval summaries' `fights`: one `{floor, act, room, enemies, encounter, hp_before, max_hp_before, result, hp_after,
max_hp}` per combat in order: `enemies` = monster ids present at the first decision, `encounter` = the encounter id
of a boss fight or of the lost fight (null otherwise: the observation carries no encounter id during a fight; the
monster set identifies it), `result` `won` / `lost` / `unfinished` (capped / stuck in it), `hp_after` / `max_hp` at
the first decision after the fight (the final values at a death). So "is encounter X killing more because more games
reach it or because it is beaten less often" is the count of its fights against its `lost` ones.

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
| `mid` | 192 / 384 / 3 / 6 | 2,827,406 | the self-play v2 line's size (2,904,208 with `--deck-counts --aux-heads`) |
| `mid-deep` | 192 / 384 / 6 / 6 | 4,161,998 | twice the trunk depth of `mid` (4,238,800 with `--deck-counts --aux-heads`); `--device cuda` |
| `large` | 384 / 768 / 6 / 8 | 15,805,070 | `--device cuda` only (one A100; 16,106,128 with `--deck-counts --aux-heads`) |

Activation memory (autograd's saved tensors, fp32, worst-case 64 candidate / prompt rows; measured by
`model.activation_bytes_per_sample` and printed at start-up for the configured minibatch): smoke 0.41 MiB/sample,
small 1.20, large 7.39 → one minibatch of 4096 saves ≈ 1.6 / 4.8 / **29.6 GiB**. For the large preset on a 40 GB
A100 use `--amp` (bf16 autocast, roughly half), `--grad-checkpoint` (recompute the transformer blocks in backward:
1.71 MiB/sample, ≈ 6.8 GiB at 4096, at the cost of one more trunk forward) and/or `--minibatch 2048`; on CUDA the
trainer warns when the estimate exceeds 70 % of the free GPU memory.

`--deck-counts` adds 14 × d_model parameters (smoke +1,344 = +0.14 %, small +1,792, large +5,376 = +0.03 %),
`--aux-heads` adds two heads of hidden width state / 4 (smoke / small +33,026, large +295,682 = +1.9 %).

A resume cannot change the architecture: `--resume` of a `mid` run with `--preset large` (or `mid-deep`, or any
model flag that differs from the checkpoint's `arch`) exits 2 naming the checkpoint, both architectures and the
differing knobs; start the bigger model as a fresh run with a new `--out`.

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

## Long-run flags (`--ent-coef-final`, `--eval-at-every`, `--pin-every`)

All off by default; off, the trainer computes and logs exactly what it did before (no `t_eval_at_s` column, a
constant `ent_coef`, no extra checkpoint files).

- **Entropy schedule** (`--ent-coef A --ent-coef-final X --ent-anneal-updates N [--ent-anneal-start U]`): update
  `u` (1-based, absolute) uses `A + (X - A) * clamp((u - 1 - U) / N, 0, 1)`, i.e. `A` at update `U + 1`, linear to
  `X` at update `U + N + 1`, then `X`. `U` defaults to the update count at the start of the session that first sets
  the schedule; the resolved `U` is stored in the config, so `--resume` continues the schedule where it was
  (`--ent-coef-final none` turns an inherited schedule off). Both `--ent-coef-final` and `--ent-anneal-updates >= 1`
  are needed (one alone exits 2). Every metrics row carries the value in effect as `ent_coef`. The coefficient is a
  0-d device tensor set in place before each update, so a compiled loss is not recompiled per value.
- **Periodic fixed endpoints** (`--eval-at-every N`): every N updates (absolute update numbers, so a resume keeps the
  grid) runs the `--eval-at` protocol: `--eval-at-seedset` in every `--eval-at-modes` mode, one `evals.jsonl` row per
  mode (`mode`, `eval_at`), the weights kept as `checkpoints/pinned-<N>.pt`, never selecting `best.pt`. It combines
  with explicit `--eval-at` updates. With either on, metrics rows carry `t_eval_at_s` (the share of `t_eval_s` spent
  in these evals).
- **Pinned checkpoints** (`--pin-every N`): every N updates the checkpoint of that update (written if the regular
  cadence did not) is copied to `checkpoints/pinned-<N>.pt`. `--keep-ckpts K` prunes only `ckpt-*.pt`; `best.pt`
  and `pinned-*.pt` are never pruned and never taken by `--resume auto`.

```bash
# a long cuda run: entropy 0.02 -> 0.005 over 400 updates, dev-128 greedy-on / greedy-off every 50, weights every 100
uv run --project sim/tools/train sts2train train ... --ent-coef 0.02 --ent-coef-final 0.005 --ent-anneal-updates 400 \
    --eval-at-every 50 --eval-at-seedset silent-a10-dev-v1 --eval-at-modes greedy-on,greedy-off --pin-every 100
```

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

## Rollout overlap (`--overlap off|double`)

`--overlap off` (default) is the serial rollout: per step, the forward of all N slots, then the env step of all N
slots. `--overlap double` splits the slots into two lanes `[0, N/2)` / `[N/2, N)`, each its own native `VecEnv`
(`sts2train/overlap.py`: `VecPair`, both lanes writing into one N-slot buffer set), and runs one lane's env step in a
worker thread while the main thread runs the other lane's bookkeeping + forward (the native step releases the GIL):

    worker:  step A(t)              | step B(t)              | step A(t+1) ...
    main:    post B(t-1), pre B(t)  | post A(t), pre A(t+1)  | post B(t), pre B(t+1) ...

The store receives the same [T, N] batch; GAE, the bootstrap forward (all N slots, one call), the update and every
metric are unchanged. The per-slot trackers (rewards, acquisition shaping, loop breaker, cycle detector) run per lane
with global slot numbers; the aux-head targets write through column views. Stream mode (`LaneStreamEnv`) hands seeds out
in the order one N-slot env would (A(t) then B(t) = ascending slots), and episode ids are mapped to the one-env
numbering, so under a deterministic policy `double` reproduces `off` (tests/test_overlap.py: same actions, metrics,
episodes.jsonl, cycles.jsonl, stream counter and weights to float rounding, also with `--cycle-cost --acq-shaping
--rollout-loop-breaker --aux-heads`). With sampling the actions differ only because the sampler draws per lane (same
distribution, different RNG consumption). Pool mode works (fresh pools; lane B's slot start offsets come from the seed
text `<seed>~L1`: same start distribution, other assignment); `--start-saves` is refused, and so is `--harvest`
(anchors are clones in one env's pool and anchor starts restore them into any slot; the lanes have separate pools, so
self-anchor runs use `--overlap off`). Both lanes' pools hold every
stream entry, so template creation (`pool.warm`, during the update) costs twice the CPU, run in parallel per lane.

Metrics (only with `double`): `t_env_s` = summed native step time of both lanes (overlapped, so `t_other_s` goes
negative), `t_infer_s` = the main thread's pre phases + bootstrap, `t_overlap_wait_s` = main thread blocked on the
worker, `t_rollout_s` = the rollout's wall time; summary.json `overlap`.

Measured (Huginn testbox, 6 CPUs shared with other jobs, no GPU; `bench/overlap_bench.py`, stream mode, medians of
updates 2-6, 3 interleaved repeats): 64 slots, rollout 16, d32 model, 2 env + 2 torch threads: off env 0.12 s, infer
0.51 s, update 0.97 s, total 1.95 s per update; double env 0.18 s, infer 0.85 s, wait 0.003 s, rollout 0.88 s, total
3.35 s. On a CPU box `double` loses: the forward is CPU work there and the env phase it can hide is small, while a
half-batch forward costs ~0.85 of a full one (infer ratio 1.7 for the two halves) and env threads compete with torch
threads. The gain needs a GPU forward and env ~ infer, as on the A100 (env 2.4 s, infer 2.7 s, update 3.2 s per
update = 8.3 s): double ~ max(2.4 x r_env, 2.7 x r_inf) + 3.2, i.e. 5.9 s (1.41x) at r_inf = 1, 6.8 s (1.22x) at
1.35, no gain at r_inf >= 1.9, where r_inf = (lane A + lane B forward time) / one full-batch forward on the GPU. Measure
r_inf before adopting it (`--device cuda` on the Colab bundle):

```bash
python sim/tools/train/bench/overlap_bench.py --device cuda --envs 1024 --rollout 32 --updates 6 --preset small \
    --threads 10 --torch-threads 2 --minibatch 4096 --epochs 2 --repeats 2 --out tmp/bench-overlap-a100
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
| `startpool.py` | `--start-saves`: `probe_saves` (import every save once, first observation, skip reasons), `plan_layout` (interleaved fresh / save entry layout for the requested mix), `build_start_pool` (fills the env's pool; determinized or exact save copies), `split_stats` (the `fresh_*` / `save_*` / `harvest_*` metrics) |
| `harvest.py` | `--harvest`: `parse_harvest` (ring spec), `Harvester` (captures determinized clones of combat entries of fresh- / stream-started episodes into per-kind FIFO rings of live pool entries, releases the overflow, per-update `harvest_*` counters); stream-mode anchor starts: `seedstream.StreamEnv.attach_anchors` |
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

## Start modes, seeds and isolation (`--start-mode`, `--init-seed` / `--sample-seed` / `--env-seed`, blind sets)

**`--start-mode stream`** (`sts2train.seedstream`; the 2026-11-04 mainline): every episode of every slot starts from a
seed that no episode of the run started from before, `<prefix>-<CHARACTER>-<n>` (`--seed-stream-prefix`, default
`SP<env-seed>`, `[A-Z][A-Z0-9_]{0,23}`, `TRAIN*` refused; character `characters[n % len]`; `--ascension`), `n` from a
counter that the checkpoint stores (`seed_stream`: `next`, `started`, `failed`, `skipped`) and a resume continues; the
episodes in flight at the checkpoint are logged as `dropped` rows by the resumed session and never restarted.
`--start-saves` must be none, `--baseline-steps` 0. The VecEnv runs with `auto_reset=False` and an empty pool, so it
never starts an episode by itself: after each step the ended slots are restarted with `restore_many` onto the next
stream entries, which are released at once (the step's reward / terminated / truncated / status are copied first, a
restore writes RESET signals; `last_episode` keeps describing the ended episode). Slot i's k-th episode therefore has
`episode_id = (k - 1) * N + i + 1` (checked by `tests/test_seed_stream.py`). Assignment is FIFO in slot order, no RNG.
Lookahead (`--stream-lookahead`, default auto = `max(64, 2 x envs)`, grown to 1.25 x an update's starts): entries
generated ahead whose `RunSim.NewRun` templates `pool.warm()` creates once per update, in a background thread during
the PPO update (`--stream-prefetch`, default) or synchronously (`--no-stream-prefetch`). CPU smoke (16 envs x 2
threads, rollout 16, Silent A10, 60 updates, random-init policy, ~80 steps per episode): pool mode 8.8-9.9 ms of env
time per update; stream without lookahead 57 ms (+478 %, 84 % of it NewRun, ~15 ms per start); with the auto
lookahead and the background prefetch 9.8-10.4 ms (restarts 3-4 % of env time) plus ~33 ms of NewRun per update in the
prefetch thread, hidden behind the ~150 ms update; synchronous prefetch costs those ~32 ms on the critical path.
Two forks of one checkpoint (e.g. A/B arms) continue the same stream (common random numbers); `--env-seed` /
`--seed-stream-prefix` give a fork its own family. Metrics: `starts_update`, `starts_total`, `seeds_used_total`,
`seed_stream_next`, `dropped_total`, `stream_skipped_total`, `stream_failed_total`, `stream_start_failed`,
`stream_lookahead`, `t_stream_s` (restarts inside the steps; the first update also holds the reset's),
`t_prefetch_s` (thread time), `t_prefetch_wait_s` (time the trainer waited for it).

**`--start-mode pool`** (default; legacy, bit for bit the earlier trainer): the fixed pool of `--pool` fresh seeds
`TRAIN<env-seed>-<CHARACTER>-<i>` (+ `--start-saves`), slot offsets from `PPO<start-seed>[R<update>]`. Metrics add
`starts_update` / `starts_total`, `seeds_used_total` (pool entries started at least once; the checkpoint keeps the
per-entry `start_counts` while the pool size is unchanged) and `dropped_total`.

**Seeds** (`--seed` derives the four when they are not given; the resolved values are in config.json `seeds`, the
checkpoint's config and `sessions[]`): `--init-seed` (model initialisation: its own CPU generator state, the global
state restored afterwards), `--sample-seed` (action sampling, torch on every device, and the minibatch shuffles,
numpy), `--env-seed` (the stream prefix `SP<env-seed>` / the pool family `TRAIN<env-seed>`), `--start-seed` (pool mode
only: slot offsets, save copies). Scheme `rng_scheme`: `legacy` for pool mode without `--init-seed` / `--sample-seed`
(one torch RNG seeded with `--seed` serves the init and then the sampling, as before), else `decoupled` (each use gets
a sha256-derived seed of its own: `ppo.purpose_seed`; changing `--sample-seed` leaves the initial weights unchanged,
changing `--init-seed` leaves the sampling streams unchanged; config.json `sessions[0].init_weights_hash` records the
initial weights). On a resume the checkpoint's seeds are kept and the RNG states restored; `--init-seed` must equal
the checkpoint's; a different `--sample-seed` re-seeds the sampling streams (`rng_restored` = `reseeded`); an old
checkpoint without these fields ran `legacy` with init / sample seed = its `--seed`.

**Isolation** (`sts2train.isolation`): every frozen seed set under `sim/tools/eval/seedsets` (+ `$STS2VEC_SEEDSETS`)
with a role in target / dev / test / dev2 / ab / blind / official / smoke, plus the run's eval seed sets
(`--eval-seedset` with `--eval-every`, `--eval-at-seedset` with `--eval-at`), are holdouts,
compared by the game's 64-bit run seed (`sts2vec.protocol.run_seed` = `RngSeeds.RunSeed`: XxHash64 of the UTF-8 text,
the legacy 32-bit hash for `old*` texts) and by (character, seed text, ascension). The stream generator skips a
colliding index (counted); pool mode refuses to start (exit 2, before the run directory exists) when the pool holds a
holdout game: `--seed 0 --pool 4608` would swallow `silent-a10-ab-v1` (TRAIN0-SILENT-4096..4607, committed here from
train/harvest a570373 with the `TRAIN<n>-<CHARACTER>-<i>` seed form). `silent-a10-test-v1` is now role `dev2` (exposed by
selection and analysis, Q3 audit; the checksum covers ascension / content / games only, so its sha256 is unchanged).

**Blind sets** (role `blind`; `silent-a10-blind-v1` = `silent-a10-blind-s256`, B1A10SI001..256, frozen 2026-11-04; no
text and no run seed in common with any other set, the TRAIN0 pools or the start of the SP stream family): every
evaluation (`sts2vec.evaluate.evaluate`, so `sts2train eval` in every mode, report-card / pack incl. their baselines
and `--modes` rows, the trainer's periodic and `--eval-at` evals, `sts2vec eval`, the lookahead) refuses a seed set
holding a blind game unless `--allow-blind --purpose
"<text>"` is given, and then appends {utc, weights_hash, seedset, purpose} to `<out>/blind-access.jsonl` (the trainer
and report-card also to their own directory). Not evaluated while a scheme is chosen.

**Episode hooks** (`sts2train.episodes.EPISODE_HOOKS`; `train(..., episode_hooks=[...])` for one call): a callable
`fn(episode) -> dict | None` runs once per finished episode in the rollout step it ended in (after
`EpisodeRewards.step`), gets the finished-episode dict (slot, win, floor, act, steps, end_kind, progress, return, start
fields, `seed_text`, `seed_n`, `update`, `t`) and returns fields for that episode's `episodes.jsonl` row (the UI
revisit cycle count is a base field, `cycles`, written at the end of the update: the detector closes the episode after
the hooks ran):

```python
from sts2train.episodes import EPISODE_HOOKS
EPISODE_HOOKS.register("ui_cycles", lambda e: {"ui_cycles": monitor.pop_slot(e["slot"])})   # e.g. a loop monitor
```

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

## Self-anchor starts (`--harvest`)

Training episodes can also start from determinized clones of the policy's own combat entries (`sts2train/harvest.py`;
off by default, and with it off the trainer computes exactly what it did before). The self-play v2 line uses it in
stream mode:

```bash
uv run --project sim/tools/train sts2train train --start-mode stream ... \
    --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25 --harvest-start-share 0.33
# pool mode (the legacy fixed pool, optionally with saves): the anchors join the auto-reset sequence
uv run --project sim/tools/train sts2train train ... --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25
```

- **Capture.** At a combat's first decision (`aux.is_combat` 0 -> 1 inside a running episode, never at an episode's
  first observation) of an episode that started from a fresh pool entry (pool mode) or a stream seed (stream mode) --
  never a save or another anchor -- the trainer keeps `env.clone_determinized(slot, seed)` as a pool entry: the same
  player view (byte for byte on the model's 27 input buffers) with the hidden state resampled (draw order, RNG
  streams, monster AI, relic bag, event order). Kind by the map cell flagged `current`: Boss -> `boss`, Elite ->
  `elite`, any other combat in act 2+ -> `act2`; `any` takes every combat no other ring takes, the act-1 hallway
  included (tests / smoke). A kind without its own ring falls back to `act2` (act 2+), then `any`. Slots with
  SIM_ERROR / CONTENT_GAP or no legal action are skipped. A candidate is kept with probability `--harvest-prob`, at
  most `--harvest-max-per-step` per env step and a ring's size per ring and step; the clone seed is derived from
  (seed, update, step, slot).
- **Rings.** Per-kind FIFO rings of live entries; a full ring releases its oldest entries (one `env.pool.release`
  call per step, sts2vec ABI 2; ids are never reused). A slot already running from a released anchor plays on (the
  slot holds its own state).
- **Starts, stream mode.** After a step, every slot whose episode ended draws u ~ U(0, 1) (one draw per ended slot,
  ascending slot order, from a generator seeded from the trainer seed and the stream counter at the session start);
  with u < `--harvest-start-share` (default 0.33 with `--harvest`) and at least one live anchor, the slot restarts
  from an anchor drawn uniformly from the live ones (`restore_many`), else from the next stream seed. An anchor start
  consumes no stream index: `seeds_used_total == starts_total == seed_stream_next` keeps holding, stream indices are
  never reused, and the isolation guard (which checks stream seed texts as they are generated) never sees an anchor
  -- an anchor derives from a stream seed the guard already passed. The anchor stays live after the start (only its
  ring's FIFO releases it). A failed anchor start (SIM_ERROR / CONTENT_GAP or no legal action) falls back to the
  stream (`anchor_start_failed`). The session's first reset takes stream seeds only (the rings are empty).
  `--harvest-start-share` is refused in pool mode.
- **Starts, pool mode.** Nothing schedules them: the env's auto-reset sequence (`(slot_seed + k) mod len(pool)` over
  the live entries in id order) runs over the anchors, which form one contiguous block at its end, so they take a
  share of the *slots* (about live anchors / live entries), not a fixed share of the episodes.
- **Metrics.** `fresh_*` counts only episodes started from fresh entries / stream seeds (comparable with a run
  without anchors), `save_*` the save starts and `harvest_*` the anchor starts: the same keys plus
  `harvest_start_share` (anchor-started / finished episodes), `harvest_progress_gain_mean`, `harvest_start_floor_mean`,
  `harvest_floors_gained_mean` and per ring kind `harvest_episodes_<kind>` / `harvest_clear_rate_<kind>` (the episode
  won or left its start floor). Per update: `harvest_candidates`, `harvest_captured` / `harvest_released` (also
  `_<kind>`), `harvest_live` / `harvest_live_<kind>`, `harvest_clone_s` (seconds in clone + release),
  `harvest_step_share` (decisions in anchor-started episodes / all decisions), `harvest_starts` /
  `harvest_starts_in_combat`, `harvest_captured_run` / `harvest_starts_run` (over every session); in stream mode also
  `anchor_starts_update` / `anchor_start_failed`. Anchor episodes are shorter than fresh ones, so
  `harvest_start_share` runs above `harvest_step_share`. `episodes.jsonl` (stream mode): `start_kind` `stream` or
  `anchor:<kind>`; an anchor row has `n` null and the `seed_text` of the stream episode it was cloned from.
  config.json `harvest` records the rings, probability, cap, start mode and the rule (`seed_stream.anchor_share` the
  share), summary.json `harvest` the session totals, status.json `harvest_live` the ring sizes; the log line shows
  captured / released / live, the anchor episodes' progress gain and both shares.
- **Resume.** The spec is part of the config: `--resume` keeps it, `--harvest off` turns it off, and a checkpoint from
  before the flag resumes with it off. Anchors live in the training process, so every session starts with empty
  rings; the checkpoint's `harvest` block carries the run counters (`sessions`, `captured`, `anchor_starts`, ...),
  and its `inflight` record lists anchor episodes too (their `dropped` rows carry `start_kind`).
- **Information parity.** Anchors are training-only start points; the clone is determinized and shows exactly the
  source's observation; evaluation (fresh seeds, single pass, greedy + loop breaker) is unchanged. Determinization
  keeps the pre-rolled encounter queues, later bosses and unopened rewards, and every start of one anchor replays its
  one sampled hidden state (which the policy never observes).

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

## UI revisit cycles, execution modes, fixed endpoints (`--cycle-cost`, `eval --policy / --breaker`, `compare`, `--eval-at`, `--no-best`)

`sts2train.cycles.CycleDetector` counts the zero-progress UI loop that the loop breaker papers over in evaluation
(2026-11-04: 176 of u2225's 177 dev-128 interventions were RestSite `SMITH` → `PromptSelect` → `PromptCancel` back
to the byte-identical rest site, one was Shop removal → select → cancel). A cycle is completed at a RestSite / Shop
decision whose 64-bit digest of the 27 model-read buffers (`loopbreak.ObsDigest`) equals an earlier RestSite / Shop
decision of the same episode and floor at most 64 decisions back, when every decision in between (at least one) was
a DeckUpgrade / DeckRemove prompt entered from that room that carried no new information: it equals the room
observation in every model-read buffer except the prompt UI (the prompt columns, `phase` and `decision_kind` of
`global_i`; `prompt_options`, `cand`, `cand_n`), its options are a sub-multiset of the deck's (card, upgrade) rows,
and its candidates only select a listed option, confirm or cancel. Counted once at the revisit; going round again
is another cycle. Not cycles: a confirmed upgrade or removal (the deck changes), leaving the room, a fight or any
other screen in between, an event prompt cancelled back to the event, a prompt that fails the check (`rejected_*`),
a no-op. Checked on the archived u2225 dev-128 games: 177 cycles at exactly the 177 intervention decisions
(breaker on), 2,469 in the 94 stuck games of the breaker-off run; on all 5,476 room prompt screens the only
differing global_i columns are prompt UI columns, the deck buffer is unchanged (no upgrade preview) and every option
is a deck card.

- **Training**: always on, counting only (the default path stays bit for bit the old one: `tests/test_perf.py`);
  metrics keys, `cycles.jsonl` and the `cycles` field of `episodes.jsonl` above. `--cycle-cost c` subtracts c from the trainer's
  reward of the transition that closes a cycle (the cancel back to the room), once per cycle; the env, gamma /
  lambda, the value targets' construction, the logged objective (`reward_mean`, returns, progress) and the
  acquisition shaping (`shaping_mean`) are unchanged; `cycle_cost_total` = c × cycles. Size of c = 0.01 (arm C1 of
  the 2026-11-04 plan) against the s8a reward scale: half a room of Act-1 progress (1/51 = 0.0196), 0.83 of one
  upgrade's shaping (0.012), one relic (0.01), 3.3 card acquisitions (0.003), a tenth of a fresh episode's death
  debt (~0.10), 3.4 % of a fresh episode's mean return (0.29).
- **Evaluation modes** (`sts2train eval`): `--policy greedy|sample` × `--breaker on|off` (`--breaker` =
  `--loop-breaker`), same protocol limits for all. Sample = one draw per decision from the softmax over the legal
  candidates (float64 inverse CDF), the play's RNG `cycles.sample_rng(--sample-seed, game key, repeat)` (batch
  independent); `--repeats R` writes `OUT/r<k>/` + `OUT/repeats.json`; `--chunk i/n` plays one slice (config.json
  `parent_seedset`). Every game summary gets `eval_mode`, `decisions`, `wall_s` (from the run's start to the game's end;
  games run batched), `stalled`, `cycles`, `cycles_rest` / `cycles_shop`, `cycle_events` (decision index, room,
  floor, length, path) next to `loop_breaks` and `stuck`; report.json the totals under `cycles`.
- **`sts2train compare ARM ARM ...`**: an arm = comma-separated run directories (slices, repeat parents) of one seed
  set; per arm progress [95 % t over per-seed means], clear rate, stalls (count, phase@floor), plays with a cycle,
  cycles per play, loop-breaker plays, decisions, wall seconds, the stage funnel (`reportcard.stages`); per pair the
  per-seed difference (t and 10,000-resample bootstrap 95 % intervals, better / worse / same, identical actions).
- **Fixed endpoints** (`train --eval-at u1,u2 [--eval-at-seedset silent-a10-dev-v1] [--eval-at-modes
  greedy-on,greedy-off,sample-off] [--eval-at-sample-seed 1]`): at those absolute updates the live model plays the
  seed set once per mode into `evals/u<N>/<mode>/`, rows with `mode` in `evals.jsonl`, weights kept as
  `checkpoints/pinned-<N>.pt`; these rows never select `best.pt`. `--no-best` turns `best.pt` off altogether (the
  periodic `--eval-every` eval stays a monitor; `--best` undoes it on `--resume`).

```bash
# the three execution modes of one checkpoint on the dev set, two 64-game slices each (a Mac: <= 64 env slots)
for c in 0 1; do
  uv run --project sim/tools/train sts2train eval --seedset silent-a10-dev-v1 --checkpoint CK --threads 3 \
      --chunk $c/2 --policy greedy --breaker off --out tmp/eval/modes/greedy-off-c$c
  uv run --project sim/tools/train sts2train eval --seedset silent-a10-dev-v1 --checkpoint CK --threads 3 \
      --chunk $c/2 --policy sample --breaker off --sample-seed 1 --repeats 3 --out tmp/eval/modes/sample-off-c$c
  uv run --project sim/tools/train sts2train eval --seedset silent-a10-dev-v1 --checkpoint CK --threads 3 \
      --chunk $c/2 --policy greedy --breaker on --out tmp/eval/modes/greedy-on-c$c
done
M=tmp/eval/modes
uv run --project sim/tools/train sts2train compare $M/greedy-off-c0,$M/greedy-off-c1 \
    $M/sample-off-c0,$M/sample-off-c1 $M/greedy-on-c0,$M/greedy-on-c1 --labels greedy-off,sample-off,greedy-on
# a training arm with the loop cost and fixed-endpoint evals, no best.pt
uv run --project sim/tools/train sts2train train ... --cycle-cost 0.01 --eval-at u381,u762 --no-best
```

| Checkpoint (silent-a10-dev-v1) | Mode | Plays | Progress [95 % t] | Stuck | Plays with a cycle | Cycles / play | Breaker plays |
| --- | --- | --- | --- | --- | --- | --- | --- |
| s8a u2225 | greedy, breaker off | 128 | 0.252 [0.237, 0.267] | 94 | 94 | 19.29 | 0 |
| | sample (seed 1, 3 repeats), off | 384 | 0.281 [0.266, 0.296] | 1 | 192 | 2.10 | 0 |
| | greedy, breaker on | 128 | 0.372 [0.348, 0.396] | 0 | 94 | 1.38 | 94 (177 breaks) |
| s8a u2050 | greedy, breaker off | 128 | 0.282 [0.262, 0.302] | 52 | 52 | 12.19 | 0 |
| | sample (seed 1, 3 repeats), off | 384 | 0.287 [0.271, 0.302] | 0 | 144 | 1.04 | 0 |
| | greedy, breaker on | 128 | 0.351 [0.326, 0.376] | 0 | 52 | 0.69 | 52 (88 breaks) |

(cpu, 3 threads, library `3b73317739c650bd`; paired per seed, u2225: greedy-on − greedy-off +0.120 [0.098, 0.143],
sample-off − greedy-off +0.029 [0.009, 0.050], greedy-on − sample-off +0.091 [0.070, 0.112]; sampled, u2225 and
u2050 are level: −0.005 [−0.018, 0.007]. Diagnosis memo: the 2026-11-04 q-modes memo.)

## Room macros (`--macros rest,shop`)

`--macros rest,shop` (TrainConfig `macros`, default `""` = off) turns on the env's room macro candidates (sim/VEC.md
"Room macros"): at a rest site SMITH becomes one candidate per upgradable deck card, in a shop the card removal one
candidate per removable card, each doing pick + confirm in the same step, so the SMITH → select → cancel loop
(docs/diag-2026-11-04/q4-loopbreaker.md) cannot happen and no rest / shop UI revisit cycle can close. The value is
passed to the training VecEnv (pool and stream modes), the periodic `--eval-every` and `--eval-at` evaluations, kept
in the checkpoint's config (a resume keeps it unless `--macros` is given again; `--macros none` turns it off) and in
`config.json` (`env.macros`, only when on). `sts2train eval` and `report-card` play a checkpoint with its own
`config.macros` (override: `eval --macros rest,shop|none`); the eval run's `config.json` has `limits.macros`.

- Compatibility: the schema, the model spec and `encoder_version` are identical with macros on or off (the macro rows
  reuse the `ChooseRestOption` / `BuyEntry` kinds with B = the deck row and `card_id` = the card), so every checkpoint,
  of any preset and mid-run, resumes with `--macros rest,shop` without widening (`check_spec_compat`: identical model
  inputs). Old behaviour is bit for bit unchanged without the flag (`tests/test_perf.py` default-path tests).
- Rows are appended after the screen's other candidates; with `max_rows` 64 a deck larger than about 45 cards can
  push the last macro rows past the training cut in a shop (counted in `cand_over_max_rows`).
- The real-game twin (`scripts/live/nn_twin_play.py`, `--macros checkpoint` by default) lists the checkpoint's macros
  and plays each macro row in the game as its clicks, checked step by step (`scripts/live/nn_twin/macros.py`,
  README-nn-twin.md "宏动作"); it does not map the `prompt` macros of `--macros all` yet (fails closed at the first
  auto-confirmed prompt). Not covered: the combat lookahead has no macro mapping (it plays raw candidates).

```bash
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR --macros rest,shop ...
uv run --project sim/tools/train sts2train eval --seedset silent-a10-dev-v1 --checkpoint RUN_DIR/checkpoints/last.pt
```

### Prompt macros v2 (`--macros all`)

`--macros all` (= `rest,shop,prompt`; sim/VEC.md "Prompt macros v2", docs/prompt-macros-v2-2026-11-04.md) adds to
the room macros: no `PromptCancel` row at any prompt, and a pick that completes the selection confirms in the same
step (exactly-k = k picks, up-to-N = picks + a DONE row, DECLINE only where the game's prompt allows 0). Same spec and
`encoder_version` as off, so any checkpoint resumes with it; the rest / shop rows fall back to the plain open row past
64 candidates (status `MACRO_FALLBACK`). Cycle counting over every screen and prompt kind (counting only):
`CycleDetector(spec, scope="all")` (`sts2train/cycles.py`; the trainer's own detector and `--cycle-cost` stay on the
rest / shop scope).

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
