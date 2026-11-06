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
| `objective.py` | `EpisodeRewards`: per-slot reward of the `protocol` / `win` / `shaped` objective and finished-episode records (protocol progress fields + the episode's return); `AcqShaping` (`--acq-shaping`), `HpShaping` (`--hp-shaping-coef`), `ShapingSet` |
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
- **`--hp-shaping-coef KAPPA`** (default 0 = off; wave 8): potential-based HP shaping on top of the objective
  (`objective.HpShaping`), built exactly like `--acq-shaping` and summed with it when both are on
  (`objective.ShapingSet`; potential-based terms compose). phi(s) = KAPPA × hp / max_hp of a live observation (`aux.hp`
  / `aux.max_hp`; 0 when max_hp is 0), phi = 0 after any episode end (death, win, step cap, floor stall), F = gamma ×
  phi(s') − phi(s) per step. With gamma 1: losing 20% of max HP on a step pays −0.2 KAPPA (KAPPA 0.1: −0.02, about one
  room's +0.0196), healing pays it back, a max-HP change (relic, event) moves only the ratio. An episode's HP shaping
  telescopes to −KAPPA × hp0 / max_hp0 of its first observation, whatever happens in between: at a death the last step
  pays −phi(last live state), at a win the terminal pays back −KAPPA × hp_T / max_hp_T (the conventional terminal-0
  form, so the optimal policy is unchanged). The potential is (re)initialised at the first observation of every
  episode: the session's first observations (`reset`, a resume included: no shaping step crosses a session boundary),
  and at every auto-reset the next episode's first observation, which for a harvest anchor / save start is the
  restored state's hp / max_hp; under `--overlap double` every lane keeps its own. Logged apart like the acquisition
  shaping: `reward_mean_shaped`, `shaping_mean`, `shaped_return_mean`, `shaping_return_mean` (all parts) plus
  `hp_shaping_mean` (per slot-step of the window), `hp_shaping_sum` (window total), `hp_shaping_return_mean` (finished
  episodes) and `hp_potential_mean`; episode records carry `hp_shaping`; episodes.jsonl `return`, `reward_mean`,
  `return_mean` and every progress number stay the objective's. Coef 0 builds no shaping part, so a run is
  bit-identical to one without the flag. The coefficient is recorded in the checkpoint config, config.json and the
  report card's objective group; `--resume` of a checkpoint trained without it (no key) resumes at 0 unless
  `--hp-shaping-coef` is given, a checkpoint trained with it keeps its value unless overridden.

`--aux-heads` and `--deck-counts` are part of the architecture: a run resumes only with the setting its checkpoint
was trained with (other values are refused before the env starts, like the size flags), and their modules are created
after all others, so a model with a flag on starts every other parameter from the same initial values as without it.
`--acq-shaping`, `--hp-shaping-coef` and the coefficients are objective settings and may change on resume. `bench-update --deck-counts
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
- **Stream mode** (wave 17): `--start-mode stream --start-saves DIR --save-mix M` (M < 1) puts the save entries
  (same copy count as above) into the empty pool and starts a starting slot from a uniformly drawn one with
  probability M; see "Boss 学校（wave 17）" below.

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

## Boss 学校（wave 17）

为什么：dev-128 greedy 读数里 act-2 boss 只有 ≈ 20% 的局能到、到了也只有 ≈ 7% 能杀（act-1 boss 79%）；协议 progress 对一次
boss 击杀只付 ≈ 1/(3L) ≈ 0.02，所以 value head 在 act-2 boss 附近的斜率只有 0.5（`docs/diag-2026-11-06/value-calibration.md`）；
重放策略自己的 boss 入口（harvest ring `boss`，B 线 vs A 线）没有区别；wave 16 显示"总是强制升级 / 删牌 / 精英"更差。所以
直接的杠杆是：让策略在**强牌组**的后期局面里积累经验（人类存档开局），并给 act-2+ boss 击杀一个更强的信号。三件东西，
默认全关，关着时训练逐位不变（raven 上的确定性对照：stream + harvest + macros 与 pool + acq/hp shaping 两种配置，
旧 / 新代码 3 个 update 的权重、metrics、episodes.jsonl 完全一致；新增的只有下面列出的 metrics 键）。

### `--boss-bonus B`（`objective.EpisodeRewards`）

- **何时付**：每次 act-2 及以后的 boss 击杀付一次 B：(1) 观测里 `aux.act_index` 从 k 变成 k + 1 且 k ≥ 1 的那一步
  （act-2 boss 在 act 2 → 3 的过渡之后；过渡发生在 boss 的 Rewards 屏之后，所以奖励记在过渡那一步）；(2) Victory 终局
  （act-3 / 最终 boss），B 叠加在 `--win-bonus` 之上（决定：最终 boss 也付 B，"每个 act-2+ boss 都付"）。act 1 → 2
  （k = 0）不付；一局的第一个观测（reset / auto-reset / 存档 / anchor 开局）不付（没有观测到过渡）。
- **只付挣来的击杀**：aux 块里有 `is_combat`（训练器的 aux 块有）时，过渡只有在本局在 act k 里观测到过战斗时才付；
  存档 / anchor 从人类已经打完的 boss 的 Rewards 屏（或其后）开局，走进下一 act 不付，计为 `boss2_transitions_unearned`。
- **只进 return，不进 progress**：B 是 reward 的一部分，所以进 `return` / GAE / value 目标；`progress`、`start_progress`、
  `progress_gain`（协议的）不变，eval 读数不受影响。`return = progress_gain + win_bonus·win + B·boss2_kills`。
- **记录**：每局 `boss_bonus_total`（本局付的 B 总和）和 `boss2_kills`（B ≠ 0 时进 episodes.jsonl）；每个 update 的
  `boss2_kills` = `boss2_kills_transition` + `boss2_kills_win`、`boss2_transitions_unearned`、`boss_bonus_sum`
  （B = 0 时也计数，作诊断；不付钱）。存档开局在 act 2 的局，杀了 act-2 boss 照常付。
- **配置**：`TrainConfig.boss_bonus`（默认 0.0），在 `--win-bonus` 旁边；checkpoint 的 config 记录它；从旧 checkpoint
  resume 得 0；resume 继承，`--boss-bonus 0` 关掉。非有限值报错。对任意 objective 都加（与 shaping 相同：加在
  objective 的 reward 上）。

### 存档学校：stream 模式下的 `--start-saves`（`startpool` + `seedstream.StreamEnv.attach_saves`）

wave 17 之前 `--start-mode stream` 拒绝 `--start-saves`（只是设计上没接，不涉及冻结 eval 种子集：eval 从不用存档）。
现在两者可以组合，训练线的其他 flag（`--seed-stream-prefix`、`--harvest ...`、`--macros all`、`--overlap off`、
explorer off）都不受影响；没有存档时 stream 的开局序列逐位不变（不抽随机数）。

- **池**：存档（和它们的副本）在 stream 发任何种子之前先放进空池，占 pool id 0 .. K-1，永不释放、不消耗 stream index。
  副本数与 pool 模式相同：`c = max(1, round(pool · M / (1 − M) / saves))`（`--pool` 默认 2048、M = 0.25、121 个存档 →
  每个 6 份、726 个条目）；`--save-copies determinized`（别名 `--copies-mode`）= copy 0 是导入的存档本身，其余是
  `env.clone_determinized`（同一玩家视图，隐藏状态重采样）；`exact` = 重新导入（每次都是人类的 RNG）。
- **开局规则**：每个要开新局的槽（每步结束的槽，以及会话开始时的 reset）先抽 u ~ U(0,1)（生成器种子 = sha256(训练 seed,
  会话开始时的 stream 计数器)）；u < M → 均匀抽一个存档条目（`restore_many`），否则进入 anchor 份额（`--harvest`；
  所以 anchor 份额是"非存档开局"的份额），再否则下一个 stream 种子。M 就是所有开局里存档开局的比例（smoke：首个
  reset 64 槽里 20 个）。失败的存档开局退回 anchor / stream（`save_start_failed`）。`--save-mix` 必须 < 1；
  `--overlap double` 仍拒绝存档。
- **隔离**：每个可用存档的 `rng.seed`（人类局的 12 字符种子）+ 角色 + ascension 与冻结 holdout 集比对（run seed 与三元组），
  撞上就拒绝训练（pool 模式同样）；config.json `isolation.save_seeds_checked` / `save_run_seeds`。
- **记录**：`fresh_*` = stream 开局（与无存档的线可比），`save_*` = 存档开局（`save_progress_gain_mean`、
  `save_start_progress_mean`、`save_episodes_by_start_act` …），`harvest_*` = anchor；`save_starts_update` /
  `save_start_failed` / `save_starts_session`；config.json `start_pool`（`start_mode: stream`、`save_mix`、
  `save_entries`、`copies_per_save`、跳过原因、直方图）与 `seed_stream.save_share`。episodes.jsonl：`start_kind`
  `stream` / `save` / `anchor:<kind>`，存档行 `seed_text` = `<file>#copy<c>`、`n` = null，非 stream 开局的行带
  `start_act` / `start_floor` / `start_progress` / `progress_gain`。存档开局的 progress 从存档状态的协议 progress
  （`start_progress`，该局的绝对进度）起算，return 只算从那里得到的增量；eval 从不用存档，所以 eval 读数不受影响。
  存档保留自己的 ascension：A9 存档在 `--ascension 10` 下按 A9 玩（日志有 note）。
- **Resume**：存档目录在 config 里（绝对路径），resume 保留；`--start-saves none` 关掉。

**做一个池**：录制器在每次游戏存档时复制 `current_run.save`（`<sessions>/<session>/native/saves/*.save`，schema 20）。
`scripts/train/collect_start_saves.py --out DIR --character SILENT [--ascension 10] [--min-floor 18] --check`
只读地复制它们（按 sha256 去重，`--check` 用 sts2vec 试导入并跳过失败的），并写 `DIR/manifest.json`（会话、种子、
floor / act / 房间直方图）；A9 + A10 混合池就别给 `--ascension`。trainer 的 probe 还会再按内容去重，并跳过导入器
不支持的存档（原因写进 config.json 的 `start_pool.skipped_files`）。manifest 只作来源记录，可选。

现有三个池的 probe（raven，2026-11-06，`probe_saves(..., characters=("SILENT",))`）：

| 池 | 文件 | 可用 | 跳过（原因） | act 2 / 3 | 首个观测阶段 Combat / Rewards / Event / RestSite / Shop / Treasure | start_progress 均值 | 人类局 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| silent-a9a10-late | 122 | 121 | 1（`pre_finished_room extra_rewards are not supported`） | 61 / 60（A9 64，A10 57） | 54 / 32 / 15 / 11 / 5 / 4 | 0.650 | 2（A43U5UZ5W8BS A9，WWVGBDEYNG19 A10） |
| silent-a10-late | 58 | 57 | 1（同上） | 27 / 30 | 24 / 13 / 8 / 7 / 3 / 2 | 0.668 | 1 |
| silent-a10-all | 88 | 87 | 1（同上） | act 1/2/3 = 30 / 27 / 30 | 36 / 21 / 12 / 12 / 3 / 3 | 0.488 | 1 |

a10-late 是 a9a10-late 的子集（57 个 sha 重合），三池并集 151 个；没有存档停在地图屏（存档点只有进房间 / 战后 Rewards /
Ancient 之后），≈ 45% 从战斗第一回合开局；没有 holdout 碰撞；没有 pending prompt。注意多样性很低：121 个存档只来自
2 局人类游戏（每局约 60 个存档点），determinized 副本只重采样隐藏状态，牌组 / 地图只有两种。

### harvest ring `boss2`（`harvest.KINDS`）

`boss2` = 当前地图格是 Boss 且 `aux.act_index ≥ 1`（act-2 / act-3 boss）的战斗入口，机制与其他 ring 相同（概率、
每步上限、FIFO 释放、stream 模式下按 `--harvest-start-share` 开局）。有 `boss2` ring 时 act 2+ 的 boss 进 `boss2`、
`boss` 只剩 act-1 boss；没有时 `boss` 照旧接收所有 boss（逐位同前）。`boss2` 追加在 `KINDS` 末尾，旧 kind 的编码与
规范 spec 顺序不变（`format_harvest`：`boss:…,elite:…,act2:…,any:…,boss2:…`）。注意 anchor 只从 stream 开局的局里
捕获（不从存档 / anchor 局），所以 `boss2` 的捕获量取决于策略自己到达 act-2 boss 的频率（≈ 20% 的局）。

### S 线的额外 flag

在训练线（A / C 的 resume 命令）原有的 flag 之上加：

```bash
--start-saves /content/sts2/saves-silent-a9a10-late --save-mix 0.25 --copies-mode determinized --boss-bonus 0.2
# 可选（若采用 boss2 ring；B 线显示 boss ring 本身无效，所以默认不加）：
--harvest boss:1536,elite:512,act2:1024,boss2:512
```

`--copies-mode` 是 `--save-copies` 的别名。Colab 上 `/content/sts2/saves-silent-a9a10-late` 需要先把 122 个存档
（含 manifest.json）放进去；`--save-mix 0.25` 时每个 update 约 1/4 的新局从存档开（由于存档局比 fresh 局短，
episode 份额会更高）。

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

## Exploration for rare decision kinds (`--explore-kinds`, `--explore-eps`, `--explore-logp`)

Motivation (docs/diag-2026-11-05/behaviour-audit-a-u5064.md): line A u5064 chose HEAL at 453/453 rest sites with the
SMITH:<card> macro rows at probability 0.000 and never bought the card removal (0/203). Rows at ~0 probability are
never sampled, so PPO never learns their value. `--explore-kinds K1,K2 --explore-eps E` (`sts2train/explore.py`)
mixes a uniform draw into the **rollout** sampling at those decisions only:

- At a rollout decision whose candidate table has >= 1 row of an explore item, with probability E the action is drawn
  uniformly from all explore rows present (e.g. HEAL and every SMITH:<card> row for `ChooseRestOption`), otherwise from
  the policy. Rows the sampling distribution cannot take (past `cand_n` / `max_rows`, banned by
  `--rollout-loop-breaker`) are never explored. Evaluation (`sts2train eval`, `--eval-every`, `--eval-at`,
  report cards) and the loop breaker's greedy path never explore.
- Items: a RunActionKind name as the candidate table reports it (`ChooseRestOption`, `BuyEntry`, `ProceedRewards`,
  `TakeRewardCard`, ...), optionally `Kind:selector`: `macro` (room-macro rows, B >= 0) / `plain` (B = -1), a
  RestOption name on `ChooseRestOption` (`ChooseRestOption:SMITH` = the SMITH rows without HEAL), a ShopCategory name
  or `remove` (= `card_removal`) on `BuyEntry` (`BuyEntry:remove` = the removal rows only: a removal is a `BuyEntry`
  like any purchase, the kind alone cannot separate it). Unknown names fail at start-up with the library's list.
- Wave 10 (behaviour audit: 0/257 avoidable elites taken, card rewards skipped 6.3% of the time):
  - `Travel:<MapPointType>` (`Travel:Elite`, `Travel:RestSite`, `Travel:Unknown`, `Travel:Shop`, `Travel:Monster`,
    `Travel:Treasure`, ...): the Travel rows whose destination node has that type. A Travel row's A (`option_index`) is
    the rank of the destination among the legal points (`map_nodes.legal_next`) sorted by (col, row) (sim/VEC.md, the
    model's map pointer), so the type is `map_nodes.point_type` of the A-th legal cell in column-major order; checked
    against the real env (the node the slot lands on). `--explore-kinds Travel:Elite --explore-eps E`: at a Travel
    decision offering >= 1 Elite, with probability E the action is uniform among the Elite destinations (P(an elite)
    >= E in the rollouts); decisions without an Elite destination are untouched. Unknown point types (or `Unassigned`)
    fail at start-up.
  - `ProceedRewards` (every ProceedRewards row: also the decisions where nothing, or only gold / potions / relics, is
    left to claim, where proceeding skips no card) and `ProceedRewards:skipcard` (the row only at decisions that also
    list a card row, `TakeRewardCard` or `PickRewardCard`: there proceeding skips the unclaimed card). Use `:skipcard`
    to explore true skips. Proceeding forfeits every unclaimed reward on the screen (`OnSkipped`), so a skipcard draw
    with gold still unclaimed skips the gold too. The CardReward phase (after a REROLL) lists the card screen's own Skip
    (`RewardAlternative`), not ProceedRewards, so it is not explored by this item.
- Behaviour policy b = (1 - E) pi + E u at those decisions (u uniform over the explore rows), b = pi elsewhere.
  `--explore-logp` picks what PPO's ratio uses. Every mode corrects only the one-step policy ratio: GAE advantages and
  value targets come from the b-trajectories with no off-policy correction (no V-trace / Retrace), so the critic
  learns V^b and the advantages are b's (a SMITH forced at E of the rest sites lowers V upstream of rest sites relative
  to V^pi; small, because these decisions are rare). The entropy bonus stays on pi.
  - `decoupled` (default): stores log pi_old and multiplies the normalised advantage by pi_old / b (decoupled PPO,
    Hilton et al. 2021): the ratio starts at 1 everywhere, so the clip window is centred on pi_old, and pi_old / b is
    the importance weight of the drawn sample.
  - `mixture`: the stored log-prob is log b, ratio pi_new / b. At explore decisions the ratio starts at pi / b:
    1 / (1 - E) for the rows the policy drew (outside the clip for E > clip / (1 + clip) = 0.167 at clip 0.2, so they
    only receive negative-advantage gradient) and ~0 for explored rows the policy gives ~0 (below 1 - clip, so they only
    receive positive-advantage gradient): an advantage-independent drift away from the policy's own choices.
    Refused at E >= clip / (1 + clip); `explore_mix_clip0` is the share of explore-decision samples whose mixture start
    ratio is outside the window (logged in every mode).
  - `policy`: stores log pi_old (ratio pi_new / pi_old, as if pi had drawn every action): biased off-policy, but the
    explored rows get the full advantage x grad log pi push, the fastest way to move P(SMITH) off 0.
- Randomness: the coin and the pick of slot s at step t of update u come from
  `default_rng([purpose_seed(sample_seed, "explore"), u, t])`: no state, so resumes continue the same stream and
  `--overlap double` draws each slot exactly as `--overlap off`; the torch sampling RNG is not touched. Works with
  `--macros` on or off, `--harvest`, `--overlap double`, resume (inherited from the checkpoint; `--explore-kinds none`
  or `--explore-eps 0` turns it off). E = 0 (default) builds no explorer: bit-identical to a build without it.
  `--explore-kinds` without `--explore-eps` (eps 0) is refused rather than silently running the control arm.
- Cost: a step with no explore row in any slot's candidate table (every combat step for `ChooseRestOption,BuyEntry`)
  costs a host-side row scan only; a step with one copies the [n, w] log-probs and the actions to the host (two syncs
  on CUDA). `t_explore_s` per update is the explorer's wall time; not yet measured on CUDA, so compare `infer_s` with
  the explorer on and off in the GPU A/B.
- Metrics per update (metrics.jsonl): `explore_eps`, `explore_decisions` (decisions with an explore row),
  `explore_share` (of valid decisions), `explore_steps` (mixture draws), `explore_policy_mass` (pi's summed
  probability on the explore rows there), `explore_mix_clip0`, `t_explore_s`, and per item `explore_present.<item>`, `explore_pmass.<item>`,
  `explore_taken.<item>`, `explore_drawn.<item>` plus `_macro` variants over the item's macro rows:
  `explore_pmass_macro.ChooseRestOption` is P(SMITH:<card>) at rest sites (watch it recover from 0),
  `explore_taken_macro.BuyEntry` the removals taken. summary.json `explore` holds the run totals.

```bash
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR --macros rest,shop \
    --explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 [--explore-logp decoupled|mixture|policy]
# wave 10: elite routes and card-reward skips
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR \
    --explore-kinds Travel:Elite,ProceedRewards:skipcard --explore-eps 0.3
```

### Dual-clip 与 log-ratio 钳制（`--dual-clip C`，wave 11）

事故：run w9-e（2026-11-06 06:35-06:42 EST，bundle sts2train-57b85d8f0d）用 `--explore-logp policy` 时，被探索到、
而策略给 ~0 概率的行（SMITH、card removal）存下的 log pi_old 极负（甚至 float 下溢成 -inf），ratio
r = exp(logp_new - logp_old) 无界；PPO 的 min(r A, clip(r) A) 在 A < 0 时就是 r A，梯度巨大（A == 0 时 inf x 0 = nan），
approx_kl 先到 2.9e12 再成 nan，trainer 连续三次挂掉。

- log-ratio 钳制（始终开启）：`ppo.ppo_surrogate` 先算 logr = (logp_new - logp_old).clamp(max=LOG_RATIO_MAX = 20)
  再 exp，approx_kl / clipfrac 也用钳制后的 ratio。logr <= 20 时 clamp 是精确的 no-op（数值与梯度逐位不变），
  只有超过上界的行不同（以前是 inf / nan 或 > 4.9e8 的 ratio，现在 ratio = exp(20)，且这些行无梯度）。
- `--dual-clip C`（C > 1，常用 3；Ye et al. 2020）：A < 0 的样本 surrogate 变为 max(min(r A, clip(r) A), C A)，
  A >= 0 不变。默认 0 = 关闭，与改动前逐位相同（`tests/test_dual_clip.py`、`test_perf` 的 default-path 门）。
  checkpoint 的 config 记录 `dual_clip`；没有该字段的旧 checkpoint resume 时为 0，除非显式给出。
- 指标：metrics.jsonl 每次 update 的 `dual_clipped` = 落在 dual-clip 分支的样本比例（关闭时 0）；开启时
  train.log 的 update 行打印 `dual_clipped=`。
- 监控注意：钳制只保证有限，不保证小。每个被钳制的行对 approx_kl 的贡献约 exp(20) ≈ 4.9e8，所以在
  `--explore-logp policy` 下 approx_kl（以及关闭 dual clip 时的 policy_loss / loss，约 -4.9e8 x A）仍可能是 1e7-1e8
  量级，但这些行没有梯度。这时判断稳定性看 `grad_norm`、`entropy`、`dual_clipped` 和评测曲线，不要看 approx_kl。

```bash
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR --macros rest,shop \
    --explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3
```

### Explore push（有界探索推力，`--explore-push DELTA`，wave 12）

为什么：run w9-e2（2026-11-06 10:18 EST，Colab A100，bundle sts2train-69463a9f85 = wave 11；`--explore-kinds
ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3.0`，bf16，1 epoch x 32 个 4096 的
minibatch）的每次 update 的 approx_kl 在 0.4-0.5 与 6.8e4-1.5e6 之间交替，dual_clipped 1e-5-1.7e-3，explore_steps ≈ 487 /
update；落在 log-ratio 钳制上的样本数（kl / (e^20 / 131072) ≈ kl / 3700）从 ~18 涨到 ~400 ≈ 每个被探索的样本。解读：
`policy` 模式下，被探索的行（SMITH:<card>、card removal，策略给 log pi ≈ -80）在一次 update 内就被前几个 minibatch
的 A·∇log pi 推动 > 20 nats（行打分器已饱和），随后钳制把它们的梯度清零，下一次 update 自己的负优势样本又把它们推回：
振荡；连"安静"的 update 也有 kl 0.4（正常 0.002）。dual-clip 让目标有界，但没有让动力学有界。PPO 的 ratio 机制救不了
硬零行：`decoupled` / `mixture` 给这些行的梯度 ∝ pi ≈ 0，`policy` 给的是无界且有偏的推力。

做法（`ppo.ppo_loss` / `ppo.explore_push_term`）：explorer（`explore.KindExplorer.last_explored`）按样本记录 explored
掩码（True = 本步是 mixture 抽样，硬币命中），trainer 像 act / logp 一样存进 [T, N]，传给 `MinibatchUpdater`。DELTA > 0 时：

- explored 样本退出 PPO surrogate（其 surrogate 项为 0，minibatch 均值的分母不变，所以其余样本的贡献逐位不变），
  但照常参与 value loss、entropy 与 aux heads；
- 改为推力项 `L_push = -mean_{explored} A · clamp(logp_new - logp_old, -DELTA, +DELTA)`，乘 `--explore-push-coef`
  （默认 1.0）加进 loss。A 是 surrogate 用的同一个归一化优势，logp_old 是存下的 log pi_old；均值只在 explored 样本上取。
  梯度：|logp_new - logp_old| < DELTA 时为 A·∇log pi，超出后为 0（clamp 的反传）：每个 explored 行每次 update 至多移动
  DELTA nats 的信任域，与 ratio 无关，并且对极端 logit 上的 bf16 噪声稳健（DELTA ≈ 1 ≫ 幅度 80 的 logit 约 0.25 nat 的
  bf16 量化）；
- 非 explored 样本不受影响：wave 11 的 dual-clip 与 log-ratio 钳制照旧作用于它们。

与其他 flag 的关系：必须 `--explore-logp policy`（`resolve_config` 拒绝 decoupled / mixture：前者给优势乘 pi_old / b，后者
存 log b，都不是推力项要的 log pi_old）；必须有 explorer（`--explore-kinds` + `--explore-eps` > 0）；resume 时若关掉了
explorer，继承来的 push 一并关闭。可与 `--dual-clip` 同开（建议保留 3：它只管非 explored 样本）。DELTA 必须 >= 0，
0 = 关闭，与改动前逐位相同（`tests/test_explore_push.py`；4 步 Adam、smoke / trunk x aux on/off，与 HEAD 的 ppo.py
比较 stats / 权重 / Adam 状态逐位相同）。checkpoint 的 config 记录 `explore_push` / `explore_push_coef`，没有这两个字段
的旧 checkpoint resume 时为 0 / 1.0；report card 的 explore 组显示它们。

指标（开启时：metrics.jsonl 每次 update 与 train.log 的 update 行；是 minibatch 统计量的 update 内平均）：

- `push_share`：explored 样本占 minibatch 的比例；
- `push_clamped`：explored 样本中 loss 计算时 |logp_new - logp_old| >= DELTA 的比例（已达本次 update 的信任域边界，无梯度）；
- `push_dlogp`：explored 样本的 logp_new - logp_old 均值（未钳制）：正 = 被推上，负 = 被推下；
- `push_loss`：L_push（乘 coef 之前）。
- 同时 `approx_kl` / `clipfrac` 改为只在非 explored 样本上统计（explored 样本不走 ratio 路径），所以 kl 重新有意义：
  开启后应回到 ~1e-3 量级；若仍大，问题不在探索行。minibatch 内没有 explored 样本时 push_clamped / push_dlogp /
  push_loss 记 0（会把 update 平均拉低；4096 的 minibatch 下 ~400 个 explored / update 时几乎不发生）。

```bash
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR --macros rest,shop \
    --explore-kinds ChooseRestOption,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3 \
    --explore-push 1
```

### Explore push step（独立归一步长，wave 14；推力自身的预算，wave 14b；host 端资格判定，wave 14c）

为什么（wave 13 诊断，`docs/diag-2026-11-06/explore-sensitivity.md`，A 的 u7664）：explored 的 SMITH 行比 HEAL 低 ~92 nat
（removal 行 ~73，p5 -654），而且 ‖∇θ log π‖ ≈ 238（removal ≈ 754，combat 行 ≈ 1），各决策之间几乎平行。wave 12 把推力放在
共享 minibatch loss 里：推力梯度主导整个梯度，全局 clip 0.5 把一切缩小（PPO 部分缩 ~470 倍），Adam 的逐参数归一化又把它
放大回每个 minibatch ~12 nat 的跳变（新 Adam 状态 9 nat；dormant 二阶矩只解释 ~30%）。于是 wave 12 的信任域对 96% 的
explored 样本处于钳制状态，这些行每次 update 随机游走 ±47 nat（已复现）。

做法（`--explore-push-step ETA`，需要 `--explore-push DELTA` > 0；`ppo.MinibatchUpdater.push_step`）：

1. 每个 minibatch 的主 loss 是**不含推力项**的 PPO：explored 样本照 wave 12 退出 surrogate 和 kl / clipfrac，value /
   entropy / aux 不变；backward → clip → Adam step，与推力系数为 0 时逐位相同（Adam 状态从不看到推力梯度）。
2. 然后对 minibatch 里的每个推力组（`ppo.push_groups`：explore item × 稀有 / 常见，见下）：在 Adam step **之后**，
   U = 该组在本 minibatch 的全部 explored 样本（只排除存的 log π_old 非有限的，如 -inf）；在 fp32 下算
   `g = ∇θ L_push`（L_push = −mean_U A·log π(a)）和 `h = ∇θ mean_U log π(a)`，然后普通一步
   `θ ← θ − ETA / max(‖h‖², 1) · g`：不进全局 clip，不进 Adam，无动量；只有 autograd 能从 U 走到的参数会动。
   （wave 14 用 rollout 基准的 |log π_new − log π_old| < DELTA 选 U；14b 起不再用它做门控，见下面"预算"。）
   一阶下子集的 mean log π 变化 = ETA·⟨mean A∇log π, h⟩/‖h‖²，行梯度平行时 = ETA·mean A（ETA 的单位是 nat）。
   `max(‖h‖², 1)` 的下限防止 ∇log π ≈ 0 的行（策略本来就选的 p ≈ 1 行）得到无界步长：‖h‖² < 1 时就是 lr = ETA 的
   普通 SGD，子集只动 ETA·‖h‖² < ETA。U 为空时 g = 0，参数逐位不变。
3. 分组：explorer 记录每个 mixture draw 的 explore item（`KindExplorer.last_explored_item`，int8，-1 = 非 draw，顺序同
   `--explore-kinds`），trainer 存成 [T, N]（overlap 两条 lane 同样）。`push_groups` = 2·item + [log π_old <
   `PUSH_STEP_RARE_LOGP` = -3]：同一个 item 里稀有行与常见行分开归一。原因（实测）：`--explore-kinds ChooseRestOption`
   不带 selector 时，mixture draw 里有一半是 HEAL（p ≈ 1，梯度 ≈ 0），与 SMITH 同组时把 ‖h‖² 稀释成 (k/n)²，SMITH 行一步
   动 ETA·n/k；32 个 minibatch 后 SMITH 漂移 +13.8 nat（p50），分组后 −1.0。SMITH 与 removal 是不同 item，各自一步
   （梯度尺度差 ~3 倍）。
4. 编译：推力步总是 eager（单独的小前向 + 两次 `autograd.grad`），主 minibatch 的编译路径不变；启动时的
   compiled-vs-eager gate（`check_compiled`）只测主 loss，不含推力步。代价：每个 minibatch 每个推力组多 2 次
   ~16 行的前向和一次小 backward（预算用完的组照样算，只是系数为 0——这样决定留在 device 上）；trainer 侧分组在 host 上
   做，不加 device sync。
5. **预算（wave 14b，推力自己的信任域）**。wave 14 的复核（raven 回放，A u7664）：不加推力时主 PPO / Adam 本身在一个
   update 的 32 个 minibatch 里就把 SMITH 行移动 p50 −8 nat（p5 −19，p95 +14），以 rollout 的 log π_old 为基准的
   |dl| < DELTA 被这部分漂移占满：每个 minibatch 只剩 3–4 / 16 个样本可推，无推力 arm 的 push_clamped 也是 0.72–0.79；
   推力自身每个 update 只贡献 ~0..+2 nat，而主漂移 −8，P(SMITH) 继续下降。所以 14b 把信任域改成**推力自身的累计移动量**：
   每个预算窗口、每个推力组 g 累计 `used_g = Σ |该组每一步前后（fp32 前向）mean_U log π 的变化|`，以及步数；组 g 在
   `used_g < DELTA − min(ETA, DELTA)/2` 且步数 < `ceil(DELTA/ETA) + 1` 时才走下一步。于是一个窗口里每组走
   round(DELTA/ETA) 步（至少 1 步）、共 ≈ DELTA nat，与主 loss 的漂移无关（DELTA 1、ETA 0.5 → 正好 2 步；DELTA 2 → 4 步）。
   半步余量让实测 0.4999 的两步不会在 DELTA 1 时买到第三步；取绝对值让向下推（A < 0）也消耗预算；步数上限是 A 正负
   混合时的保护（那时每步 |mean 变化| ≈ 0，nat 预算填不满，上限 3 步 @ DELTA 1 / ETA 0.5）。
   **窗口 = 一个 update 的一个 epoch**：trainer 在每个 epoch 的第一个 minibatch 前调 `MinibatchUpdater.reset_push_budget()`，
   所以 `--epochs E` 时一个 update 内推力最多把一组行移动 ≈ E·DELTA（默认 epochs 2 → ≈ 2·DELTA）。没有新 flag：DELTA 仍是
   `--explore-push`，ETA 仍是 `--explore-push-step`；ETA = 0 时整条路径与 wave 14 / 12 逐位相同。没有新的 checkpoint 状态
   （累加器每个窗口从 0 开始）。

与 wave 12 的关系：`--explore-push DELTA` 仍是信任域参数（14b 起是推力自身每窗口的预算），存的 log π_old 语义不变；开了 `--explore-push-step` 时 wave 12
的系数路径关闭（主 loss 里推力系数强制为 0；同时显式传 `--explore-push-coef` 会被 `resolve_config` 拒绝）。ETA 必须 >= 0，
0 = 关闭，与改动前逐位相同（`tests/test_explore_push_step.py` (a)；另在 raven 上与 HEAD 的 ppo.py 比较：4 步 updater ×
smoke / trunk × {plain, aux, push 1, push 1 + dual 3 + aux}，以及 3 个完整 CPU train() 跑（默认、explorer + push 1 +
dual 3、explorer + push 1 + overlap double），权重 hash 与 metrics 逐位相同）。没有 explorer / push 时传 ETA > 0 报错；
resume 关掉 explorer 时继承的 push 与 step 一并关闭。checkpoint 的 config 记录 `explore_push_step`，旧 checkpoint resume 为 0；
report card 的 explore 组显示它。

指标（开启时，metrics.jsonl 与 train.log 的 update 行；位于 push_* 之后）：

- `push_step_n`：本次 update 被推的 explored 样本数（实际走了的那些步的 |U| 之和；不是平均）；
- `push_step_dlogp`：这些样本在各自组那一步前后（fp32 前向）log π 的平均变化，按 n 加权——直接检验 ETA：A 全为 +1 时
  应 ≈ ETA；A 正负混合时 ≈ ETA·mean A（所以通常比 ETA 小）；
- `push_step_hnorm`：各组 ‖h‖ 的 n 加权平均（行灵敏度；u7664 上 SMITH ≈ 220，随训练变化时看它）；
- `push_budget_used`（14b）：窗口结束时各组 used_g 的平均（nat），各 epoch 取平均；A 同号时 ≈ DELTA；
- `push_budget_hit`（14b）：窗口结束时已不能再走的组（预算或步数上限）所占比例，各 epoch 取平均；
- wave 12 的 `push_share` / `push_clamped` / `push_dlogp` / `push_loss` 照旧（主 loss 前向时测，即 Adam step 之前）。
  14b 起 `push_clamped` 不再决定推力步，只是漂移指示：explored 样本相对 rollout 的 log π_old 已漂出 ±DELTA 的比例
  （主要来自主 PPO 的漂移）。

验收实测（raven CPU fp32，A 的 pinned-0007664 + wave 13 采集的决策，committed `MinibatchUpdater`，checkpoint 的 Adam 状态与
lr 3e-4 / clip 0.5；minibatch = 385 个 combat 决策（合成优势）+ 16 个 explored SMITH 样本，log π_old = 起点；推力效果 =
与同一 minibatch 序列的无推力 arm 的差）：

| 路径 | 每步 push_step_dlogp | 4 mb 后 SMITH 推力效果 p50 | 32 mb 后 SMITH 推力效果 p5 / p50 / p95 | combat KL vs 无推力，4 mb 后 p95 / max | HEAL |
|---|---|---|---|---|---|
| wave 12（coef 1，A ±1） | — | −40.9 | −142 / −125 / −113 | 2.1e-2 / 0.44 | 0 |
| wave 14（ETA 0.5，A ±1） | \|·\| 均值 0.20 | +0.02 | −1.86 / −1.48 / −1.10 | 4.2e-7 / 1.7e-5 | 0 |
| wave 14（ETA 0.5，A 全 +1） | 0.500（0.499..0.509） | +2.03（4 步 × 0.5） | +7.7 / +8.7 / +10.3 | 1.5e-4 / 2.8e-3 | 0 |
| wave 12（A 全 +1） | — | +38.4 | +69 / +80 / +86 | 8.9e-3 / 0.60 | ≤ 6e-4 |

- `push_clamped` 在这个复现里 wave 14 也有 0.72（wave 12 0.97），但无推力 arm 同样是 0.74：钳制来自主 PPO（合成优势 +
  checkpoint Adam 状态）本身在 32 个 minibatch 里把 SMITH 行整体移动 −8 nat（p5 −19，p95 +14），不是推力；wave 13 的
  "push_clamped 0.0" 是只放推力项的复现。真实 minibatch 的主 PPO 漂移幅度未测。
- 32 个 minibatch 之后两条轨迹的 combat KL（与无推力 arm 比）p95 3e-3、max 0.13（wave 12：p95 5e-2、max 6.4）：单步副作用
  < 1e-4，但后续 Adam 步会放大小差异。

14b 验收（同一回放框架，committed `MinibatchUpdater`；A 的 checkpoint 是 epochs 1，所以 32 个 minibatch = 一个预算窗口；
"推力效果" = 与无推力 arm 的 SMITH 行 log π 差的 p50）：

| arm（ETA 0.5） | 走了几步（第几个 mb） | 每步 push_step_dlogp | 窗口末 push_budget_used / hit | 4 mb 后推力效果 | 32 mb 后推力效果 | combat KL vs 无推力，4 mb 后 p95 / max | HEAL |
|---|---|---|---|---|---|---|---|
| DELTA 1，A 全 +1（rest64 主批） | 2（mb 1–2） | 0.4996 / 0.4994 | 0.999 / 1.0 | +0.92 | +2.48 | 8.6e-5 / 3.4e-2 | 0 |
| DELTA 1，A 全 +1（combat 主批） | 2 | 0.5009 / 0.4994 | 1.000 / 1.0 | +0.94 | +1.15 | 4.0e-5 / 9.4e-4 | 0 |
| DELTA 2，A 全 +1（combat 主批） | 4（mb 1–4） | 0.499..0.501 | 1.999 / 1.0 | +1.94 | +2.19 | 1.4e-4 / 3.4e-3 | 0 |
| DELTA 1，A ±1（rest64） | 3（步数上限） | −0.17 / +0.08 / +0.12 | 0.37 / 1.0 | +0.07 | +0.43 | 1.6e-6 / 3.1e-5 | 0 |
| DELTA 2，A ±1（combat） | 5（步数上限） | \|·\| ≤ 0.17 | 0.42 / 1.0 | +0.03 | +0.21 | 3.6e-7 / 1.3e-5 | 0 |

- 推力步不再被主漂移吃掉：无推力时 push_clamped 仍是 0.62–0.79（主 PPO 漂移），但每组照样在窗口开头走满预算。
- 4 mb 后的推力效果 ≈ DELTA（A 同号）；之后两条轨迹经 Adam 分叉（32 mb 后 combat KL max 0.5–7），32 mb 的数字含这部分
  混沌放大，不全是推力本身。

**14c：推力步的开销（结果逐位不变）**。line G（Colab A100，DELTA 3 / ETA 0.5，minibatch 4096 × 32，8 个推力组）每个 update
的更新阶段 22–23 s（不开推力 3.2 s）：14b 对每个 minibatch 的每个出现的组都跑子集前向 + 两次 autograd.grad + 步后前向，
预算用完的组只是系数 0，约 6 步后预算就用完，剩下的是 ~1,300 个受 kernel 启动延迟限制的空转小 pass。14c 在 host 上先判
资格：每次调用一次 device→host 读回（各组的 `used < DELTA − min(ETA, DELTA)/2 且 steps < cap`，与 14b 相同的 float32 设备端比较，
外加 minibatch 的 finite log π_old 掩码），不合格的组（预算/步数用完，或没有有限样本）完全跳过前向与反向。跳过路径与 14b 的系数 0
路径逐位相同：参数减 0、预算加 0、统计加 0（push_step_n / dlogp / hnorm 的 n 加权和里贡献 0），该组的预算条目照样建立
（push_budget_used / hit 的分母不变）；判定也相同，因为每组每次调用最多走一步、各组预算互不影响。每步仍是 1 次带梯度前向 +
2 次 autograd.grad + 1 次步后前向（"步前" log π 已复用带梯度前向；不同组子集不同、跨 minibatch 之间有 Adam 步，没有可共享的
pass）。验证：raven CPU（A u7664 回放，DELTA 3 / ETA 0.5 / dual 3，32 minibatch，4 线程），与 f6b591c 的 ppo.py 比较 stats /
参数 / Adam 状态 torch.equal 全部相同；推力步耗时 1 组 3.78 → 0.85 s，2 组 6.77 → 1.47 s，8 组 51.7 → 10.2 s（32 个 minibatch
总耗时 92.8 → 51.2 s；只有前 7 个 minibatch 有组在走步）。`tests/test_explore_push_step.py` (h)：8 组 × 32 minibatch 与 14b
内联副本逐位相同（DELTA 1 / 3），推力步内的模型前向与 autograd.grad 次数 = 2 × 实际步数（用完的组 0 次）。

```bash
uv run --project sim/tools/train sts2train train --resume auto --out RUN_DIR --macros rest,shop \
    --explore-kinds ChooseRestOption:SMITH,BuyEntry:remove --explore-eps 0.3 --explore-logp policy --dual-clip 3 \
    --explore-push 1 --explore-push-step 0.5
```

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
