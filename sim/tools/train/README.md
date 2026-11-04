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
# resume: newest checkpoint of the run dir; the checkpoint's config is kept, flags given here override it
uv run --project sim/tools/train sts2train train --resume auto --out tmp/train/run1 --max-hours 6
uv run --project sim/tools/train sts2train train --resume tmp/train/run1/checkpoints/ckpt-0000500.pt

# protocol v0.2 NN entry point: greedy policy on a frozen seed set -> run dir + report.md (same format as sts2eval)
uv run --project sim/tools/train sts2train eval --seedset std-a0-v1 \
    --checkpoint tmp/train/run1/checkpoints/best.pt --out tmp/eval/nn-run1-a0
uv run --project sim/tools/eval sts2eval report tmp/eval/nn-run1-a0      # recomputes the same report
uv run --project sim/tools/train sts2train plot tmp/train/run1           # curve.png from metrics.jsonl / evals.jsonl
```

(`eval` also answers to `evaluate`; `sts2train train --help` lists every flag with its default.)

## Run directory (`--out`)

| File | Content |
| --- | --- |
| `status.json` | rewritten atomically after every update: `state` (running / finished / stopped / aborted / error), `stop_reason`, `update`, `env_steps`, `episodes`, `wins`, `wall_s`, `env_steps_per_s`, `recent` (progress / win rate / floor over the last 20 updates), `best_eval`, `last_eval`, `last_checkpoint`, `updated_at` |
| `metrics.jsonl` | one row per update: env steps, episodes, finished-episode `progress_mean` / `win_rate` / `floor_mean` / `return_mean` / `act_reached`, `policy_loss`, `value_loss`, `entropy`, `approx_kl`, `clipfrac`, `grad_norm`, `explained_variance`, `env_steps_per_s`, time split `t_env_s` / `t_infer_s` / `t_update_s` / `t_ckpt_s` / `t_eval_s` / `t_other_s`, error counts, `gpu_mem_*_mb` (CUDA / MPS) |
| `evals.jsonl`, `evals/u<update>/` | every `--eval-every` updates and once at the end of a budget-ended session: `sts2vec.evaluate` on `--eval-seedset` (default `std-a0-v1`) with the live model, greedy; each row has the headline (`progress_mean`, CI, wins, floor, results, replayable) and the checkpoint it was taken at |
| `checkpoints/ckpt-<update>.pt` | every `--ckpt-every` updates and every `--ckpt-every-min` minutes (tmp file + fsync + rename), the last `--keep-ckpts`; weights, Adam state, counters, RNG states, `spec`, config, `weights_hash`; `best.pt` = the checkpoint of the best eval so far |
| `config.json` | resolved config, architecture, parameter count, library path / version, `encoder_version`, one entry per session (host, device, torch, resumed from) |
| `summary.json` | written when a session ends (stop reason, throughput and time split, totals, best / last eval) |
| `train.log` | the CLI's log lines with timestamps, appended by every session |

Stops: `--max-hours` (this session's wall clock; the loop stops before an update that would overrun it),
`--max-updates` / `--max-env-steps` (totals across resumes), or SIGINT / SIGTERM (finish the update, checkpoint,
exit; a second SIGINT aborts). A resumed session restores weights, optimizer, counters and RNG states; the env restarts
from fresh episodes (new start sequence), `metrics.jsonl` / `evals.jsonl` drop rows past the checkpoint. A fresh run
into a directory that already holds checkpoints is refused.

## Model sizes (`--preset`, or `--d-model --state-dim --depth --heads --ff-mult`)

| Preset | d_model / state / depth / heads | Parameters | Use |
| --- | --- | --- | --- |
| `smoke` (= defaults) | 96 / 256 / 0 / – | 948,750 | the 2026-11-02 smoke model (pooled tables concatenated into the state MLP) |
| `small` | 128 / 256 / 2 / 4 | 1,175,150 | CPU / MPS smokes of the transformer path |
| `large` | 384 / 768 / 6 / 8 | 15,804,270 | `--device cuda` only (one A100) |

Activation memory (autograd's saved tensors, fp32, worst-case 64 candidate / prompt rows; measured by
`model.activation_bytes_per_sample` and printed at start-up for the configured minibatch): smoke 0.41 MiB/sample,
small 1.20, large 7.39 → one minibatch of 4096 saves ≈ 1.6 / 4.8 / **29.6 GiB**. For the large preset on a 40 GB
A100 use `--amp` (bf16 autocast, roughly half), `--grad-checkpoint` (recompute the transformer blocks in backward:
1.71 MiB/sample, ≈ 6.8 GiB at 4096, at the cost of one more trunk forward) and/or `--minibatch 2048`; on CUDA the
trainer warns when the estimate exceeds 70 % of the free GPU memory.

Local-size guard: on `cpu` / `mps` the trainer refuses (exit 2, with the activation estimate) a model above 3,000,000
parameters or the `large` preset unless `--allow-big-local` is given (never inherited by `--resume`). On `mps` it
defaults `PYTORCH_MPS_HIGH_WATERMARK_RATIO` to 0.3 (and the low watermark below it) unless they are set.

`depth > 0` adds a pre-LayerNorm transformer trunk (`F.scaled_dot_product_attention`, key padding mask) over 17
typed table tokens plus one token per hand card, enemy and potion row; the state is read from the global token and the
mean of the live tokens, and the contextualised hand / enemy / potion rows feed the candidate pointers. Counts are on
the `fc2eb34c07bc8513` schema; every size, vocabulary and column index is read from the library's schema at start-up,
so a library with extra observation buffers trains unchanged (unused buffers are ignored).

## Speed (`--compile`, `--static-rows`, `bench-update`)

The default path (`--compile off`) computes exactly what the trainer computed before (tests/test_perf.py replays the
old update loop bit for bit) but never waits for the device inside a minibatch or a rollout step: row cuts come from
host-side cand_n / prompt_option_n counts, multi-column reads use device index buffers (a Python-list index uploads
the list, a stream sync per call), every epoch's minibatch indices go up in one copy, the statistics come back once
per update, the sampling distribution skips its argument validation and CUDA uploads go through pinned memory.

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
| `features.py` | `ModelSpec` (vocab / enum sizes, column maps, shapes, encoder_version from the published schema; stored in checkpoints), `obs_to_device` / `obs_to_torch` (the 27 buffers the model reads; `cand` / `prompt_options` cut to the rows the batch needs), `RolloutStore` ([T, N, ...] tensors on the training device in source dtypes, 643 MiB at 32 × 1024 with 64 candidate / prompt rows; minibatches are gathered on the device) |
| `model.py` | `Policy`: id embeddings for cards, relics, potions, powers, monsters, encounters, events, orbs, enchantments, afflictions, card vars and every enum; per-table row MLPs (hand / card reward share one card encoder; deck and piles are embedding bags; map nodes, prompt options single layer) with masked mean pooling; optional transformer trunk; candidate scorer whose first layer is factored per source (kind, A/B, hand_slot → hand row, target_slot → enemy row, potion_slot → potion row, option_index → the option bank of its kind, card_id → card embedding, state) and scored bilinearly against the state; masked to `j < cand_n`; value head. `PRESETS` |
| `ppo.py` | `TrainConfig`, `plan_run` (fresh / resume config resolution), `train` (PPO: GAE, clipped policy and value loss, entropy bonus, Adam, optional bf16 autocast on CUDA, TF32 on CUDA), `ppo_loss` / `MinibatchUpdater` / `InferenceRunner` (the update step and rollout forward, eager or compiled) and `check_compiled`, checkpoints, status / metrics / evals, budget and signal handling, uniform-random baseline |
| `bench.py` | `sts2train bench-update`: random observations of the schema's shapes and value ranges (`random_obs`), timing of `ppo.MinibatchUpdater` / `ppo.InferenceRunner` per compile mode, eager-vs-compiled check, profiler table |
| `runlog.py` | atomic writes, `JsonlWriter`, `CheckpointManager` (rotation, `best.pt`) |
| `objective.py` | `EpisodeRewards`: per-slot reward of the `protocol` / `win` / `shaped` objective and finished-episode records (protocol progress fields + the episode's return) |
| `progress.py` | `ProgressTracker`: protocol v0.2 progress of finished auto-reset episodes from the previous call's `aux` (act_floor, act_rooms) + `last_episode`, via `sts2vec.protocol.compute` (the evaluator's copy of `sts2eval.progress.compute`, pinned to it by sts2vec's tests) |
| `evaluate.py` | thin wrapper over `sts2vec.evaluate`: greedy policy of a live model (`evaluate_model`, used during training) or a checkpoint (`evaluate`, `sts2train eval`); one pool entry and one slot per seed, protocol limits (step_cap 1200 → `capped`, floor_step_cap 80 non-combat steps → `stuck`), the LLM runner's run-dir layout + `report.md` |

Objective (`objective.py`, `--objective`): the default `protocol` rewards each step with the change of the protocol
v0.2 progress metric (§4.2 of `docs/sts2-bench-protocol-v0.2-2026-11-01.md`: per-act normalised, `sts2vec.protocol.compute`,
the function `sts2vec eval` scores with) and adds `--win-bonus` (default 1.0) at a Victory terminal. Rewards telescope,
so a fresh episode's return is exactly its protocol progress (+ bonus on a win); truncated episodes keep the progress
of their last state, as the protocol scores `capped` / `stuck` games (`tests/test_objective.py` replays synthetic and
real episodes against the evaluator's bookkeeping). `win` is the contract's reward (1 at a Victory terminal, else 0).
`--shaped` (= `--objective shaped`) is the LEGACY smoke objective, `win` + `delta(total_floor) / 48` per step.
`--floor-step-cap N` is passed to `VecEnv` (stall truncation) when the installed sts2vec supports it; otherwise it is
an error rather than a silent no-op.

Training restricts actions to the first 64 candidates (`--max-rows`; rare overflow is counted as `cand_over_max_rows`);
eval passes every candidate row.

## Colab bundle (a box without .NET: the Colab A100 runtime)

`scripts/train/make_colab_bundle.sh` writes `tmp/colab/sts2train-<gitsha>.tar.gz`, built from the committed HEAD:

| Path | Content |
| --- | --- |
| `lib/Sts2Sim.Vec.so` | linux-x64 NativeAOT build of `Sts2Sim.Vec` from the Dockerfile's `aot` stage (13 MB). It needs only `libc` / `libm` / `ld-linux` (newest symbol `GLIBC_2.34`), no .NET runtime and no data files: the rules and content are compiled in, fresh starts are generated from seeds (`knowledge/source` is only the play server's rules text). sts2vec loads it with `ctypes.CDLL` when `STS2VEC_AOT_LIB` is set (no hostfxr, no runtimeconfig) |
| `seedsets/*.json` | the protocol seed sets (`STS2VEC_SEEDSETS`), read by the periodic eval |
| `wheels/` | `sts2vec` and `sts2train-smoke` (pure-python `py3-none-any`, installed with `--no-deps`) |
| `requirements-colab.txt` | `numpy` (torch is preinstalled on Colab and deliberately not listed) |
| `run_train.sh RUN_DIR [THREADS=12] [ENVS] [train flags]` | `python3 -m pip install` of the above (no uv), exports `STS2VEC_AOT_LIB` / `STS2VEC_SEEDSETS`, `sts2vec check` (→ `RUN_DIR/sts2vec-check.txt`), then `sts2train train --out RUN_DIR --resume auto --threads THREADS [--envs ENVS] ...` |
| `smoke_bundle.sh [RUN_DIR] [MINUTES] [THREADS]` | validation on any linux x86_64 CPU box: CPU torch if missing, a short CPU run through `run_train.sh` (small preset, checkpoints, an eval on `std-a0-v1`), checks of the run directory, then a raw `sts2vec bench` |
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
```

Target box (Colab A100 runtime, 2026-11-03): Python 3.13, x86_64, glibc 2.39, torch 2.11.0+cu130 and numpy 2.4.6
preinstalled, 12 vCPU, no dotnet, no uv. The wheels are pure Python and the library's newest glibc symbol is 2.34.
