# Silent A10 training plan (2026-11-03): A10-direct PPO on one A100, unattended, search as a gated add-on

Status: **plan of record, not yet executed.** Written 2026-11-03 ≈23:20 EST by the synthesis step of the training
design workflow (three recipes, two judges).
- Repo: `claude/recorder-performance-lag-0f9d6d` @ `35424a8` (23:35 EST). It includes the `train/save-pools` merge
  `998fb89` and a Core fix `35424a8`.
- Phase 1 runs on the proven bundle `tmp/colab/sts2train-0b1bbd3a4e.tar.gz`.
- **T+0** = the moment the operator starts §9.3 (expected ≈23:30 EST).
- Roles:
  - Fable orchestrates and writes the milestone reports.
  - Opus owners execute: integration (seed sets + bundle), **B** (trainer `sim/tools/train`), **C** (combat
    lookahead).
- Related:
  - protocol `docs/sts2-bench-protocol-v0.2-2026-11-01.md`
  - trainer `sim/tools/train/README.md`
  - smoke `docs/train-smoke-2026-11-02.md`
  - review `docs/a10-first-principles-review-2026-10-27.md`
  - integration record `sim/specs/integration.md`

## 中文摘要

- **结论**：主线是 Silent A10 直接 PPO。训练目标就是 protocol 进度，所以训练回报等于评测分数；关键路径上不需要新代码。第一段训练 3.75 小时，给出第一份 A10 结果。之后按会话队列无人值守续训，花满 300 compute units 时由预算闸先存 checkpoint 再停机。
- **评委分歧的取舍**：评委 1 选 R2（A10 直接训练 + 前沿重启课程），评委 2 选 R1（先 A0 再升阶）。两人都认可 R2/R3 的 A10 第一阶段不需要写代码、能无人值守，所以第一阶段直接训 A10。执行纪律取自 R1：本地 RUN_DIR、100 局留出 test 集、人工检查 replayable、热启动前清 best_eval。A0 阶梯降为 M2 的兜底分支。人类 A9 存档桥两位评委都主张推迟。
- **前 5 小时的门**：
  - T+0:45 吞吐闸：中模型与 large 各跑 8 次 update，按规则选一个。
  - T+1:00 G0：检查吞吐，确认零错误。
  - T+1:45 M1：dev 比启发式高 0.03。
  - T+3:00 M2：dev ≥ 0.18。
  - T+4:40 M3：100 局留出集 ≥ 0.20，并且配对比较优于启发式。
- **搜索**：做诚实确定化的战斗内前瞻（只在战斗内搜索；克隆状态时重采样隐藏信息，不读真实的抽牌顺序和 RNG）。它作为单独申报的评测行，在 Huginn CPU 上跑，不和训练抢 VM。只有配对增益 ≥ +0.02 且置信区间不含 0，第二阶段才做 ExIt。
- **预期**：T+5 时，留出集上贪心约 0.18–0.28（启发式约 0.11–0.12）。约 40 GPU 小时时约 0.28–0.36，前瞻再加 0.02–0.05。目标 0.60 在这个预算内不可信，复盘时会如实说明。
- **预算**：现在有 3 个 Colab 会话（2 个 A100、1 个 CPU），每小时消耗 13.54 units。这个计划只用 sts2train，sts2b 和 sts2c 确认没用后应停掉。看门脚本每 5 分钟读一次余额，满 300 units 即停。
- **之前为什么卡顿并中断（回答你的问题）**：
  - 直接原因：12:21:32 在 Mac 上启动了 large 预设（15.8M 参数）的 MPS 训练探针。两分钟内它占用的 MPS 内存到了 30 GiB，Claude 进程树的内存足迹到了 39.8 GB，而这台 Mac 只有 24 GB。
  - 经过：主进程多次卡死 20–32 秒；12:23:18 探针因 MPS 内存不足退出；12:23:22 app 退出。21:50 重启时日志记录为上次非正常退出。
  - 叠加因素：Owner A/B、设计工作流、21 GB 的 rsync，可能还有一个 QEMU 构建，当时都在同时运行。
  - 后果：后台 agent 随 app 一起退出；之后没有客户端活动，Colab 会话被回收。
  - 现在的防护：trainer 在 cpu/mps 上拒绝训练超过 3M 参数的模型，MPS 内存水位上限设为 0.3。Mac 只做编排。

## 1. Decision

**Chosen recipe: A10-direct PPO on the protocol objective with no new code on the critical path. It runs unattended
in 4–6 h Colab sessions under a 300-unit budget guard. Search enters as a separately declared, honest
combat-lookahead eval row. It becomes a training signal (ExIt-lite) only after it passes a paired gate.**

How the judges' verdicts map onto it:

- **The judges split.** Judge 1 (criterion: A10 progress at ≈40 GPU-h plus a credible 5 h signal) ranked R2 > R3 >
  R1. Judge 2 (criterion: implementation risk) ranked R1 > R3 > R2. Both rated the A10-direct first stage of R2/R3
  zero-code and runnable unattended. Their disagreement was the A0-first stage and the code-heavy later stages of R2.
  - Stage 1 is A10-direct (Judge 1). It runs on flags that exist on HEAD, with R1's operational hygiene (Judge 2).
  - Everything that needs new code is moved behind gates.
- **From R1 (Judge 2's winner):**
  - Run directory on local VM disk plus pulled backups, never Drive FUSE.
  - A 100-game held-out test set that is never used for selection.
  - The warm-start strip recipe.
  - The throughput fallback order.
  - Checking `report.json` `headline.replayable` by hand.
  - Huginn for amd64 builds and deterministic evals.
  - The A0 ladder survives as the M2 fallback branch.
- **From R2 (Judge 1's winner):**
  - Silent-only A10 from step 0.
  - T0-calibrated sizing (N 4096, minibatch 8192) and the 8-update A/B launch gate with its decision rule.
  - A 128-game dev set.
  - FLOOR_STALL and steps-per-episode alarms.
  - The loop-order rules for any later non-fresh starts.
  - The frontier-restart curriculum, as gated phase-2 stage S3.
  - Declaring objective and start distribution in `config_extra`.
- **From R3:**
  - The verified crash diagnosis and the Mac rules.
  - The lookahead implementation and test plan.
  - The ExIt go/no-go gate (+0.02 paired, CI excluding 0) with an equal-time PPO control.
  - Search workers kept off the training VM.
  - Determinism settings for final rows.
- **Deferred by both judges:** human A9 save anchors. The capability has landed anyway as `--start-saves DIR
  --save-mix R` (merge `998fb89`). Phase 1 runs **fresh-only** (no `--start-saves`). Saves return only
  as a phase-2 ablation: clean act-1 states only, mix ≤5%, paired against fresh-only.
- **Corrections to the recipes (stale facts):**
  - HEAD already contains env-cap, the player-view append, the trainer and bundle, `silent-a{0,10}-v1`, and
    cross-encoder resume.
  - Training uses the evaluator's caps (step 1200, floor 80), so a fresh episode's training return equals the
    metric `sts2vec eval` reports.
  - Pool warm on Colab is ≈9 ms per template, so 32,768 templates take ≈5 min, not 45 s.
  - T0 already ended by `max_hours`.

**Expected outcome (honest).**
- T+5 h: greedy 0.18–0.28 on the held-out `silent-a10-test-v1` (Silent heuristic ≈0.11–0.12).
- ≈40 GPU-h: 0.28–0.36 greedy, +0.02–0.05 more with the declared lookahead.
- Wins are unlikely in phase 1.
- The user's 0.60 is not credible on this budget without search distillation and macro-level search. 0.60 means on
  average reaching about the act-2 boss, or about 1 win in 14. Say so at the phase-1 review instead of spending the
  reserve.

## 2. Facts this plan rests on (2026-11-03 23:15 EST)

**Code.**
- The proven bundle's commit `0b1bbd3` (followed by docs `79acb96` and the integration record `b8533a3`) contains:
  - `5fd17d6`: env `floor_step_cap`, status `FLOOR_STALL`.
  - `c20f882`: player-view append, `encoder_version` `5dbfa09dd9dcb5ed`. The 27 buffers the model reads are unchanged.
  - `19857d5` / `ef35697`: the long-run trainer and the Colab bundle.
  - `8eb7382`: `silent-a0-v1` / `silent-a10-v1` = `C1A{0,10}SI01–20`, frozen.
  - `5cf12ec`: resume across encoder versions.
  - `0b1bbd3`: smoke checks.
- Every flag S0–S2 need exists in `sim/tools/train/sts2train/cli.py`: `--characters --ascension --pool --floor-step-cap
  --objective --win-bonus`, arch knobs, `--amp --grad-checkpoint`, eval/ckpt/budget flags, `--torch-threads`.
- Absent:
  - `--ascension-mix`, potential shaping, a curriculum. Save starts landed later; see below.
  - `--compile`: in flight, uncommitted, in worktree `train-trainer-perf`.
  - `lookahead.py`, an ExIt actor.
  - Silent dev/test seed sets.
- **Bundle built and proven:** `tmp/colab/sts2train-0b1bbd3a4e.tar.gz`, recorded in `sim/specs/integration.md` @
  `b8533a3`.
  - 5,450,745 bytes, sha256 `c754754f…`, AOT key `64d964770dd0`, built on raven in 160 s.
  - The AOT check reports `5dbfa09dd9dcb5ed` (41 buffers, 30,942 bytes/env) and the floor cap is in effect.
  - All 41 buffers are byte-identical to the managed build.
  - `smoke_bundle: OK`; raw bench 74.7k env-steps/s on 8 Huginn threads.
  - An old-bundle checkpoint resumes and evaluates on it.
  - It lacks the new Silent dev/test sets. The only bundle ever uploaded, `ef356974fa`, predates the floor cap.
- **Landed after the proof (23:2x–23:35):** `998fb89` merges `train/save-pools`. It adds `--start-saves/--save-mix`,
  `startpool.py`, `collect_start_saves.py`, per-start metrics (`fresh_*`/`save_*`) and start saves in the bundle
  (`SAVES_DIR`).
- `35424a8` changes `sim/Sts2Sim.Core` so BYRDPIP / PAELS_LEGION saves import. A bundle from HEAD therefore has a new
  AOT key and a different `.so`.
- Phase 1 needs neither: it is fresh-only and stays on `0b1bbd3a4e`.

**Colab** (`/Users/Aincrad/.local/bin/colab`, always `--auth=adc`).
- Live sessions at 23:05:
  - `[sts2train]`: A100, created 22:19 EST, idle kernel.
  - `[sts2b]`: A100, created 22:49.
  - `[sts2c]`: CPU, created 23:00.
- Usage rate 13.54 units/h. Balance **881.38** of the original 890.85. An A100 costs ≈6.73 units/h.
- VM: 12 vCPU (6c/12t Xeon 2.2 GHz), 89.6 GB RAM, 253 GB disk, Python 3.13, torch 2.11 cu130, no dotnet.
- `colab drivemount` needs a human at a TTY, so it is not usable unattended.
- The first `sts2train` session (created 12:11 EST, last exec 12:20) was pruned after the Mac side died. About
  3 units burned, which suggests roughly 15–20 min without client activity (inferred, not measured).
- The current session has stayed up with ssh-only traffic.

**T0** (Colab A100, 22:2x EST; large preset, mixed-character A0, N 2048, minibatch 2048, rollout 32, 64 Adam
steps/update):
- Env-only throughput 91.5k env-steps/s; the raw bench gave 99–105k.
- End to end 4.6–5.0k env-steps/s.
- Per update 13.4 s: env 0.7 s, inference 1.8 s, update 10.6 s. The update is launch-bound, not FLOP-bound.
- GPU peak 10.4 GB.
- Pool warm: 2,048 templates in 18.6 s.
- `std-a0-v1` greedy 0.18–0.20, 20/20 replayable.

**Model sizes** (parameter count; fp32 activations per sample at 64 rows):

| Model | Parameters | Activations / sample |
| --- | --- | --- |
| smoke | 948,750 | 0.41 MiB |
| small | 1,175,150 | 1.20 MiB |
| **mid3** (`--d-model 192 --state-dim 512 --depth 3 --heads 4 --ff-mult 4`) | 3,097,710 | 2.25 MiB |
| large | 15,804,270 | 7.39 MiB; 1.71 MiB with `--grad-checkpoint` |

**Baselines.**

| Set | Library | heuristic | first | random | Notes |
| --- | --- | --- | --- | --- | --- |
| `std-a10-v1` | HEAD, `sts2vec/5dbfa09dd9dcb5ed`, repo `0b1bbd3` | 0.110 | 0.103 | 0.045 | 20/20 replayable; equal to the `fc2eb34c07bc8513` numbers |
| `std-a0-v1` | HEAD, as above | 0.221 | 0.194 | 0.087 | 20/20 replayable; equal to the `fc2eb34c07bc8513` numbers |
| `silent-a10-v1` | `fc2eb34c07bc8513` | 0.117 [0.102, 0.131] | 0.112 | 0.039 | |
| `silent-a0-v1` | `fc2eb34c07bc8513` | 0.183 | 0.141 | 0.058 | |

- Sonnet 5.5 on the 4 Silent games of `std-a10-v1`: 0.432, 0 wins.
- No policy has ever won a game in the sim from a fresh start.

**Search.** Honest determinized combat-lookahead prototype (heuristic base, 20 Silent A10 seeds `LKSI01–20`):

| Variant | Progress | Paired change vs heuristic [95% CI] |
| --- | --- | --- |
| Heuristic | 0.117 | — |
| Turn horizon | 0.150 | +0.033 [+0.016, +0.051] |
| Combat horizon | 0.163 | +0.046 [+0.024, +0.068] |

- 0 desyncs, 20/20 replayable.
- The Colab `.so` exports `snapshot`, `restore` and `clone_determinized`.

**Protocol.**
- Rows on the Silent sets are target/dev rows, not leaderboard rows (v0.2 §3).
- Search or best-of-n rows must be declared and listed separately (§2).
- `sts2train eval` exits 0 even when the replay audit fails.

## 3. Stages

**Phase 1** = S0–S2. It is unattended and capped by the **300-unit guard** (≈44.6 A100-h). **Phase 2** = S3–S5, which
needs the user's go at the phase-1 review. The phase-2 envelope is ≈58 GPU-h ≈390 units. After that, ≈100 units of
the 800-unit budget stay in reserve (9.47 units were already spent before this plan).

The trainer builds every VecEnv below from the CLI flags (`ppo.env_kwargs`).

| Stage | Env (VecEnv kwargs) | Objective | Model | Key hyper-parameters | Stop / gate | GPU-h |
| --- | --- | --- | --- | --- | --- | --- |
| **S0** T0 launch gate (§9.4) | `VecEnv(4096, threads=12, step_cap=1200, floor_step_cap=80, auto_reset=True, seed_text='PPO99', content='ported_only', pool=PoolSpec(('SILENT',), ascension=10, count=4096, seed_prefix='TRAIN99'), pool_cache=4096)` | protocol + win bonus 1.0 (throughput only) | A: mid3, both bf16 and fp32. B: large + `--grad-checkpoint`, bf16 | rollout 32, epochs 2, minibatch 8192 (32 Adam steps/update), 8 updates per config, `--torch-threads 2`, `OMP_WAIT_POLICY=PASSIVE` | §3.1 rule → `/content/gate/decision.json` | 0.3 |
| **S1** A10 bootstrap | `VecEnv(4096, threads=12, step_cap=1200, floor_step_cap=80, auto_reset=True, seed_text='PPO0', content='ported_only', fast_path=True, pool=PoolSpec(('SILENT',), ascension=10, count=32768, seed_prefix='TRAIN0'), pool_cache=32768)` | `--objective protocol --win-bonus 1.0`, γ 1, λ 0.95. A fresh episode's return is its v0.2 progress (+1 on a win). Step-cap and FLOOR_STALL truncations keep their progress, as the evaluator scores `capped` and `stuck` | gate winner; expected mid3 (3,097,710) | 131,072 samples/update, minibatch 8192, epochs 2 (1 if mid3 < 12k/s), lr 3e-4, clip 0.2, vf 0.5, ent 0.01, grad-norm 0.5, max-rows 64. Eval every 50 updates on `silent-a10-dev-v1`. Checkpoint every 50 updates or 10 min, keep 6 | `--max-hours 3.75`; G0, M1, M2, M3 (§5) | 3.9 |
| **S2** A10 continuation (queue of 6 sessions, §9.6) | as S1; session k = 1..6 uses `seed_text='PPO{k}R{update}'` and `seed_prefix='TRAIN{k}'`, i.e. 32,768 fresh seeds per session (`--seed k` on resume) | as S1 | resumed. Optional `--compile`, adopted at a session boundary (§6) | lr 3e-4 (s2a, s2b), then 2e-4 with ent 0.0075 (s2c, s2d), then 1e-4 with ent 0.005 (s2e, s2f) | `--max-hours 6` per session; plateau rule (§6); **300-unit guard** | ≤36, guard-capped |
| **S3** frontier restarts (phase 2) | `auto_reset=False` with manual `reset(mask, start_ids)`. Starts: fresh `TRAIN{k}` ×32,768, plus self-anchors (see below) | protocol + potential shaping Φ = 0.05·hp/max_hp + 0.03·act_index, Φ(terminal) = 0 (policy-invariant). Metrics split by start kind | resumed | start weights fresh 0.6 / self 0.4; at most 12 captures per update; copy step signals before `reset` | dev-128 gain < 0.01 over 8 GPU-h; cap | ≤25 |
| **S4** ExIt-lite, or scale-up (phase 2) | ExIt actor on Huginn CPU: `VecEnv(128*(1+8*2), threads=8, auto_reset=False, pool=None, step_cap=10**6, floor_step_cap=0, fast_path=False)`, games `add_fresh('SILENT', seed=f'LBL-A10-{i}', ascension=10)`. Learner = the S2 env | ExIt: PPO + 0.3·CE(π_search, π_θ) on searched combat states (side batch 1,024). Kickstart: KL(π_teacher‖π_student) + value distill, β 1→0 over 10 GPU-h | mid3 (ExIt), or a large student (kickstart) | π_search ∝ exp(logit + 1.5·clip((Q−Q_greedy)/σ, −3, 3)); turn horizon, K 8, R 2 | every 10 GPU-h: paired dev gain ≥ +0.02 vs an equal-time PPO control, else stop | ≤30 |
| **S5** final rows (phase 2) | evaluator default (one fresh entry per game, `auto_reset=False`) + the lookahead's private search env | — | best of S2–S4 by dev-128 | greedy row + declared lookahead row; deterministic CPU re-run from a clean bundle | n/n replayable, 0 desyncs | ≤3 |

S3 self-anchors are snapshot + `clone_determinized` of the policy's own fresh episodes at six points:
- act-1 room entry at floor ≥ 9
- act-1 boss entry
- act-2 start
- act-2 boss entry
- act-3 start
- act-3 boss entry

Each bucket is a 1,024-entry ring, and a session holds at most 400k pool entries.

### 3.1 T0 gate decision rule (implemented in `t0_gate_s1.sh`, §9.4)

Measure the median `env_steps_per_s` of updates 3–8 for each config. A config counts as clean only if it ran 8 updates
with `sim_error + content_gap + illegal == 0`.

1. Mid3 precision: take bf16 only if it is more than 5% faster than fp32; otherwise fp32 (cleaner PPO ratios).
2. **Large wins** if it is clean, ≥10k env-steps/s and ≥0.6× mid3. Otherwise mid3.
3. If mid3 is below 12k, add `--epochs 1`.
4. If mid3 is below 8k, or no mid3 run is clean, **abort**. A human relaunches with `--preset small` and checks
   `gate/*.log`.

Rough expectation, an estimate from T0: mid3 12–20k end to end. At minibatch 8192, large uses 4× fewer Adam steps
than in T0 and may reach ≈9–12k.

### 3.2 Stage-1 flags (exact; `run_train.sh` adds `--out RUN --resume auto --threads 12 --envs 4096`)

```
--device cuda --characters SILENT --ascension 10 --step-cap 1200 --floor-step-cap 80 --objective protocol
--win-bonus 1.0 --rollout 32 --epochs 2 --minibatch 8192 --torch-threads 2 <gate flags: model + --amp/--no-amp
[+ --epochs 1]> --pool 32768 --seed 0 --lr 3e-4 --gamma 1.0 --lam 0.95 --clip 0.2 --vf-coef 0.5 --ent-coef 0.01
--max-grad-norm 0.5 --max-rows 64 --eval-every 50 --eval-seedset silent-a10-dev-v1 --ckpt-every 50
--ckpt-every-min 10 --keep-ckpts 6 --log-every 5 --max-hours 3.75
```

With `OMP_WAIT_POLICY=PASSIVE`.
- Memory: the rollout store is ≈2.5 GiB on the GPU.
- Mid3 activations per minibatch of 8192 are ≈18 GiB in fp32 (≈10 GiB in bf16).
- Large with checkpointing is ≈7.5 GiB in bf16. All fit in 40 GB.
- Pool warm takes ≈5 min before the first update.

### 3.3 Why these choices

- **A10 from step 0.** The score is A10 progress. The progress reward is dense: +1/(3·L) per room cleared, about
  0.02 per room. Learning signal at A10 is clear: heuristic 0.117 vs random 0.039 on `silent-a10-v1`. Mixing in A0
  is the fallback, not the default.
- **Eval caps in training.** Step cap 1200 and floor cap 80 make training identical to the evaluator. The smoke's shop
  loop becomes a zero-reward truncation instead of a sink. Full A10 runs need ≈900–1,100 decisions, so the 1,200 cap
  only binds near a win, and the evaluator applies the same cap.
- **Large batches.** N 4096 and minibatch 8192 amortise the launch-bound update that T0 measured. 32 Adam steps per
  update keep PPO stable.
- **Seed pools of 32,768 per session, rotated by `--seed k`** (a new `TRAIN{k}` prefix). That is about 30–80 visits
  per seed per session, and ≈200k distinct training seeds over phase 1. Training seeds contain `-`, so they can never
  equal an eval seed.
- **One run directory** (`/content/runs/silent-a10-ppo`) for S1 and S2. The eval set stays `silent-a10-dev-v1`, so
  `best.pt` stays comparable across sessions. No warm-start traps arise (`best_eval` never crosses eval sets).

## 4. Evaluation plan

### 4.1 Seed sets

Three new sets are **data only**.
- S0 (§9.3) commits them on `train/silent-seedsets`.
- §9.4 copies them into the `seedsets/` of the proven bundle on the VM. That bundle predates them, and `STS2VEC_SEEDSETS`
  points at that directory.
- Any later bundle built from the integration branch carries them from HEAD.

| id (file) | Games | Seeds | Role | Use |
| --- | --- | --- | --- | --- |
| `silent-a10-dev-v1` (`silent-a10-dev-s128.json`, **new**) | 128 | `D1A10SI001`–`128` | dev | in-training greedy eval every 50 updates; `best.pt` selection; all A/B and lookahead tuning |
| `silent-a10-test-v1` (`silent-a10-test-s100.json`, **new**) | 100 | `H1A10SI001`–`100` | heldout | the A10 headline. Only at M3, the phase-1 review and final. **Never** used for selection or tuning |
| `silent-a10-v1` (`silent-a10-s20.json`, committed) | 20 | `C1A10SI01`–`20` | target | every session boundary; continuity with the existing Silent rows (heuristic 0.117) |
| `std-a10-v1` (`a10-s20.json`, committed, official) | 20, 4 of them Silent | `E1A10*` | official | M3 and final: the 5-character row (other characters will be weak) plus the Silent subset vs Sonnet 0.432 (4 games, continuity only) |
| `silent-a0-v1` (`silent-a0-s20.json`, committed) | 20 | `C1A0SI01`–`20` | target | A0 transfer sanity check at M3 and final |
| `silent-a0-dev-v1` (`silent-a0-dev-s100.json`, **new**) | 100 | `D1A0SI001`–`100` | dev | only if the A0-interlude fallback triggers (§5) |

All new sets use protocol `"0.2"`, content `ported_only`, `version` 1, the commit date as `frozen`, and `sha256` from
`sts2eval.seedsets.checksum`.
- The new prefixes are disjoint from `E1*`, `C1*`, the prototype's `LKSI*` and every `TRAIN*-…` seed.
- Add the ids to `FROZEN` (`sim/tools/eval/tests/test_seedsets.py`) and to the `test_committed_seedsets_load`
  parametrize list (`sim/tools/vec/tests/test_protocol.py`).
- Do **not** add them to the 5-character balance test or to `test_silent_target_seedsets`.

### 4.2 Cadence

- **In training**: greedy dev-128 every 50 updates (≈every 7–12 min, ≈15–30 s each on the VM), plus once at every
  session end. `best.pt` is the checkpoint with the best dev `progress_mean`.
- **Each session boundary** (the watcher, on the VM GPU, ≈1 min): `best.pt` on `silent-a10-v1`.
- **M3, the S1 boundary**: `best.pt` on `silent-a10-v1`, `silent-a10-test-v1`, `std-a10-v1` and `silent-a0-v1`.
  - The declared lookahead row on dev-128 runs on Huginn CPU (§7).
- **Phase-1 review**: the same four sets on the VM.
  - A deterministic CPU re-run on Huginn from the same bundle.
  - Lookahead rows (turn and combat) on dev-128 and test-100.
- **Baselines**: the T0 script runs `heuristic`, `first` and `random` on dev-128, test-100 and `silent-a10-v1` with
  the run's own `.so` and writes `RUN/baselines.txt`. H_dev (heuristic on dev-128, expected ≈0.11–0.12) anchors M1.

### 4.3 Statistics

- Compare checkpoints, baselines and search rows **paired per seed**: Δprogress per game, mean, t-interval,
  10k-resample bootstrap interval, and better/worse/same counts (§9.10 has a 15-line script). Never compare unpaired
  means.
- "Better" means the paired 95% CI excludes 0. Typical half-widths:

  | Set | Half-width |
  | --- | --- |
  | dev-128 | ≈±0.025–0.03 |
  | test-100 | ≈±0.03–0.04 |
  | 20-game sets | ≈±0.07 (continuity only) |

- Win rates use a Wilson interval. 0 wins in 100 games bounds the win rate below ≈3.7%.
- Training-episode `progress_mean` in `metrics.jsonl` comes from the stochastic policy and is biased toward short
  episodes. Use it for trend only.

### 4.4 Reporting rules

- Label every row on a Silent set **"Silent target/dev set, sim, not a std-\* leaderboard row"**.
- An NN row's identity is three fields:
  - `server_version` `sts2vec/5dbfa09dd9dcb5ed`.
  - `prompt_hash` `weights:<hash>`.
  - The bundle git sha, from `config.json` `sessions[].bundle`, because `repo_sha` is `None` in a wheel install.
- A row counts only when `headline.replayable == games`. `sts2train eval` exits 0 even when replays fail, so read
  `report.json` (the watcher's milestone hook prints `REPLAY-FAIL`).
  - In-training eval rows in `evals.jsonl` carry `replayable` as well.
  - `best.pt` is chosen on `progress_mean` alone, so a replay failure there is an alarm.
- Compare rows only within one library build (same `encoder_version` and bundle `.so` sha256 from `manifest.json`).
- Search rows are separate rows:
  - `policy_hash` `weights:<wh>+la:<cfg sha8>`.
  - `config_extra.search` holds the config; `information` is `"determinized, combat-only"`.
- A Sonnet 5.5 reference row on `silent-a10-v1` costs ≈$22. Run it only with the user's approval.

### 4.5 Training monitors (`metrics.jsonl`, one row per update) and healthy ranges

| Metric | Healthy range / alarm |
| --- | --- |
| `env_steps_per_s` | ≥12k for mid3, ≥9k for large |
| env-only throughput (`env_steps_update / t_env_s`) | ≥60k |
| `approx_kl` | 0.005–0.03; alarm above 0.05 for 5 updates |
| `clipfrac` | 0.05–0.2 |
| `entropy` | falls slowly; alarm below 0.2 in the first 2 h |
| `explained_variance` | > 0.3 by 1 h, > 0.5 by 3 h |
| `sim_error` = `content_gap` = `illegal` | 0 |
| `floor_stall` | < 2% of episodes |
| `cand_over_max_rows` | ≈0 |
| `gpu_mem_peak_mb` | < 30,000 |

## 5. First 5 hours: timeline and go/no-go

| T+ (≈EST) | Where | What |
| --- | --- | --- |
| 0:00–0:15 (23:30–23:45) | Mac (integration owner) | Generate, test and commit the three seed sets on `train/silent-seedsets` (§9.3). S1 needs nothing from the later HEAD commits |
| 0:15–0:25 | Mac → VM | Stop the idle extra sessions (with their owner's OK). Upload the proven `0b1bbd3a4e` bundle, the three seed-set files and two scripts. Pre-flight. Start `t0_gate_s1.sh` |
| 0:25–0:40 | VM | Gate: 3 × 8 updates (≈12 min); baselines (≈1 min); decision; S1 `exec` |
| 0:40–0:45 | VM; Mac | S1 pool warm (≈5 min). Start the watcher in `screen` once `RUN.s1.flags` exists |
| 1:00 | | **G0** |
| 1:45 | | **M1** |
| 2:30 | | M1 re-check (only after "adjust") |
| 3:00 | | **M2**. Owner C: first NN lookahead rows on a fixed `best.pt` copy (Huginn CPU) |
| 4:30–4:40 | VM (watcher) | S1 ends; boundary evals = **M3**; `s2a` starts automatically |
| 4:40–5:00 | Mac / Huginn (Fable) | M3 report: curves (`sts2train plot`), tables, paired compares, the lookahead row, decision for the night |

| Gate | Measure | Continue | Adjust | Stop / branch |
| --- | --- | --- | --- | --- |
| **G-S0** (T+0:25) | The bundle is proven (raven proof for `0b1bbd3a4e`; re-run the Huginn smoke for any new bundle); the three ids resolve on the VM (T0 checks this); `sts2vec check` OK on the VM | all pass | — | any failure: fix before using the GPU |
| **G-T0** (T+0:45) | `gate/decision.json` | a winner is set, 0 errors | mid3 < 12k: `--epochs 1` is added automatically | abort (no clean mid3, or < 8k): relaunch by hand with `--preset small` |
| **G0** (T+1:00, ≥10 updates) | §4.5 throughput, errors, GPU peak, `floor_stall` | sps ≥ 12k (mid3) / 9k (large); 0 errors | 8–12k: accept; plan `--compile` for a boundary | errors > 0: stop and debug. < 8k: stop, relaunch with the fallback |
| **M1** (T+1:45, ≥6 dev evals) | best dev-128 vs H_dev; KL, entropy, EV, `stuck`/`capped` | best ≥ H_dev + 0.03 and the last 3 evals not falling | within H_dev − 0.01 … + 0.03: keep running, check `stuck`/`capped`/KL, re-check at T+2:30 | still < H_dev − 0.01 at T+2:30, any replay failure, or `stuck` > 10%: stop; check the reward/obs pipeline against the heuristic |
| **M2** (T+3:00) | best dev-128, act distribution | ≥ 0.18 | 0.14–0.18: keep S1; give `s2a` `--ent-coef 0.015` (edit `queue.txt`) | < 0.14: A0 interlude (below) |
| **M3** (T+4:40) | `best.pt` on test-100 and `silent-a10-v1`; paired vs heuristic | test ≥ 0.20 (target 0.22, stretch 0.28); paired CI vs heuristic > 0; n/n replayable; `stuck` ≤ 2%; ≥ 5% of test games reach act 2 | test 0.16–0.20: continue S2; move S3 code to the front | test < 0.16: let `s2a` run, then review; consider the A0 ladder (R1) as a new run |

**A0 interlude (the M2 fallback; zero code).** Replace the first two lines of `tmp/colab/queue.txt` before the S1
boundary with:

```
s2a --seed 50 --max-hours 1.5 --ascension 0
s2b --seed 51 --max-hours 6 --ascension 10
```

The eval set stays `silent-a10-dev-v1`, so `best.pt` stays comparable, and resume overrides `ascension`. Judge it by
the paired dev change at the end of `s2b`. If it does not help, the R1 mixed pool (`--ascension-mix`, Owner B) is the
next step.

## 6. After T+5 h: phase-1 continuation and the 300-unit review

- **Overnight:** the watcher runs the queue `s2a`…`s2f` (6 h each). At every boundary it pulls artifacts, evaluates
  `best.pt` on `silent-a10-v1`, and launches the next line.
  - Fable (or the user) reads `tmp/colab-runs/watch/watch.log` at each boundary. Edits to `queue.txt` take effect at
    the next boundary.
- **Adopt `--compile`** at a boundary only if all of these hold:
  - Owner B has landed `train/trainer-perf`.
  - `sts2train bench-update` on the A100 shows the update ≥1.5× faster for the gate winner.
  - Losses match within 1e-3.
  - The checkpoint keys are unchanged.

  It needs a rebuilt bundle (same `.so`) and a watcher restart with the new `BN` (§9.9).
- **M4 (end of `s2b`, ≈T+17 h, ≈16 GPU-h):** best dev ≥ 0.26.
- **Plateau rule:** if best dev improves < 0.01 over a whole session, move the lr step-down one session earlier (edit
  `queue.txt`) and start the S3 code. If it also stays flat across the session after the step-down, stop the queue
  early and hold the review. That saves units for phase 2.
- **Rollback rule:** if dev falls > 0.03 below best on 2 consecutive evals, resume from `best.pt` with the lr halved
  (§9.9).
- **Phase-1 review** (the queue ends after ≈41 h, or the watcher stops at 300 units spent, ≈44.6 A100-h; whichever
  comes first, ≈T+41–45 h). Fable delivers:
  - `best.pt` rows on test-100, `silent-a10-v1`, `std-a10-v1` (+ Silent subset) and `silent-a0-v1`.
  - A deterministic CPU re-run on Huginn.
  - The lookahead rows (turn and combat, value+hp) paired against greedy.
  - The learning curve.
  - Throughput and units spent.
  - A phase-2 recommendation with numbers: S3 curriculum if fewer than 25% of fresh episodes reach the act-1 boss;
    S4 ExIt if the lookahead Δ ≥ +0.02 with a CI excluding 0; scale-up if capacity-bound (EV > 0.7, entropy < 0.2,
    flat after the lr step-down).
  - **Phase 2 needs the user's explicit go.**

## 7. Search component

**A. Eval-time honest combat lookahead (Owner C; brief in §11.2).**
- What it is: a plug-in policy for the **unmodified** `sts2vec.evaluate`, with no new C ABI.
- It owns a private search VecEnv: `M·(1+K·R)` slots, `auto_reset=False`, `pool=None`, `step_cap=10**6`,
  `floor_step_cap=0`, `fast_path=False`.
  - M shadow slots are rebuilt from `ctx.keys` with `add_fresh` and checked byte for byte against the evaluator's
    observations each call.
  - The rest are rollout slots.
- When it searches: decisions that are live, `global_i.is_combat == 1` and `cand_n ≥ 2`.
- Candidates: the top-K by logit with DiscardPotion pruned, always keeping the greedy row and EndTurn.
- Roots come only from `clone_determinized`, seeded by sha256 of (seed, key, decision index, r), with common random
  numbers across candidates.
- Greedy NN rollouts stop at the next player turn (turn horizon, H ≤ 12) or at the end of the combat (combat
  horizon, H ≤ 40). They **never continue past the end of a combat**: the clone still holds the pre-rolled next
  encounter, the bosses, the chest relic and the card rewards.
- Leaf scores:
  - Default `value+hp`: Q = V(leaf) + 0.10·hp/max_hp − 0.03·Σenemy_hp/Σenemy_max_hp.
  - `value` (pure value head) once explained variance is > 0.5.
  - Defeat = 0. Victory = 1 − P(root) + win bonus.
  - Take the argmax; ties go to greedy.
- **Where it runs:** Huginn CPU (python:3.13 container with the bundle), or the Mac at ≤4 threads with load < 8.
  Never on the training VM while training runs.
- **Schedule:**
  - Unit tests green by T+2:30.
  - First NN rows at T+3:00 on a fixed checkpoint.
  - Grid {turn, combat} × {value+hp, value}, K=8, R=1, on dev-128, T+3:15–4:15.
  - The winner with R=2 at M3.
- **Expected cost** (estimate from the prototype + CPU forward timings): ≈6 (turn) or ≈33 (combat) sequential
  forwards per round. That is ≈3–8 min (turn) or ≈15–30 min (combat) per 128 games on 8 Huginn threads.

**B. Reporting.** Search rows are declared and listed separately (protocol §2):
- `policy_name` `nn:<ckpt>+la(K8,R1,turn,honest)`.
- `policy_hash` `weights:<wh>+la:<cfg sha8>`.
- `config_extra` `{search: cfg, information: "determinized, combat-only", mirror_env: true}`.
- Exact-snapshot "oracle" mode reads the true draw order and RNG. It is a diagnostic only and is never reported.

**C. ExIt-lite (phase 2).**
- **Go/no-go:** combat or turn lookahead Δ ≥ +0.02 vs greedy, paired, CI excluding 0, on dev-128, for a checkpoint
  with ≥ 20 GPU-h of training.
- **Actor:** Huginn CPU with CPU torch (S4 env above). It plays `LBL-`-prefixed fresh seeds, refreshes weights hourly,
  and stores searched combat states with π_search as compressed npz shards. It is never on the training VM, where it
  would oversubscribe 6 physical cores.
- **Learner:** Owner B's distillation loss on a side minibatch.
- **Control:** equal-time PPO-only continuation from the same checkpoint.
- **Gate:** +0.02 paired per 10 GPU-h, else stop.
- **Limit:** it helps combat play only. Card picks and pathing need macro search, which first needs a Determinizer that
  resamples the encounter queue, the unopened chest and the unopened card rewards (a Core change + tests, not
  planned here).

## 8. Why the earlier session lagged and stopped (the user's question)

**Direct trigger:** a large-preset PPO probe launched on the Mac's GPU from this workflow.
- Run dir:
  `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/large-mps`
- 15,804,270 parameters, fp32, 1,024 envs × 12 threads, minibatch 4096, pid 31025, started 12:21:32.

Timeline, from `/Users/Aincrad/Library/Logs/Claude/main.log` lines 12805–12860 and the probe's `status.json`:

| Time | Event |
| --- | --- |
| 12:16–12:18 | The app's process tree already reports a 17–19 GB memory footprint while other agents' jobs run |
| 12:21:42 | Tree RSS 5.7 GB; the probe 1.3 GB and growing |
| 12:22:00–12:23:17 | The app's main process is blocked for 1.2 s, then 29 s, 32 s and 20 s |
| 12:23:18 | Tree footprint 39,848 MB **on a 24 GB Mac**. In the same second the probe dies with `MPS backend out of memory (MPS allocated: 30.08 GiB …)` |
| 12:23:22 | `beforeQuit: handler fired, going down` |
| 21:50:03 | Restart: `Previous run did not exit cleanly` |

**Contributing load:** Owner A's and B's jobs, the design workflow, a 21 GB rsync to the then-exFAT drive, and
probably a local QEMU `docker buildx` build. Load was 40–60 with heavy swapping. Afterwards Spotlight indexed the
exFAT drive. The memory note recorded only the contributing load, not the probe.

**Consequences:**
- Every background agent died with the app.
- The Colab session created at 12:11 was reclaimed after the client side went quiet (≈3 units).
- The work stalled until the 21:50 restart.

**Rules now in force:**
- The trainer refuses cpu/mps models above 3M parameters (and the large preset) unless `--allow-big-local` is given.
  Never use that flag on the Mac.
- MPS high watermark is 0.3.
- Mac = orchestration and light evals only. Check `uptime` load < 8 before any local heavy job, with at most 2 heavy
  jobs at once.
- linux-amd64 builds run on Huginn only.
- Spotlight stays off on `/Volumes/Extreme` (now APFS).
- Long-lived watchers run in `screen`, so an app crash cannot kill them.

If the question was about the in-game stutter instead: that was the recorder's main-thread snapshot cost. The fix
shipped as recorder 0.3.1 on 2026-11-02 (real play: avg 7.55 → 3.12 ms per snapshot).

## 9. Unattended Colab runbook

### 9.1 Where things live

| What | Where |
| --- | --- |
| Bundle | `$REPO/tmp/colab/sts2train-<sha>.tar.gz`, built by `scripts/train/make_colab_bundle.sh` from HEAD; the AOT `.so` is built on Huginn |
| Mac-side scripts (git-ignored `tmp/`, copied from this section) | `$REPO/tmp/colab/{ssh_config, t0_gate_s1.sh, sts2_remote.sh, watch_train.sh, queue.txt}` |
| VM bundle | `/content/<BN>/` (run_train.sh, wheels, `lib/Sts2Sim.Vec.so`, seedsets, `manifest.json`) |
| VM T0 | `/content/t0.log`, `/content/gate/{A-amp,A-fp32,B-amp}/`, `/content/gate/decision.json`, `/content/baselines/` |
| **VM run dir** `RUN=/content/runs/silent-a10-ppo` (local disk) | `status.json` (rewritten every update), `metrics.jsonl`, `evals.jsonl`, `evals/u*/`, `checkpoints/{ckpt-*.pt,best.pt}`, `config.json`, `summary.json`, `train.log`, `sts2vec-check.txt`, `baselines.txt`, `milestones/<tag>-<set>/` |
| VM launch logs and S1 flags | `RUN.launch.log`, `RUN.s1.flags` |
| Mac mirror (pulled every 30 min) | `$REPO/tmp/colab-runs/silent-a10-ppo/` (small files + `best.pt` + newest ckpt + milestones) |
| Mac best history | `$REPO/tmp/colab-runs/silent-a10-ppo.history/` |
| Huginn mirror | `raven:/data/raven/hack-sts2/train-runs/silent-a10-ppo/` |
| Watcher state | `$REPO/tmp/colab-runs/watch/` (`watch.log`, `budget_start`, `current`, `queue.done`, `crashes`, `recreated`, `s1.flags`) |

`REPO=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d`.

Credentials stay where they are:
- Never copy ADC, `~/.config/colab-cli/` or any key to Huginn or the VM.
- `~/.config/colab-cli/colab.log` contains bearer tokens: never paste or upload it.

### 9.2 One-time Mac setup

```bash
REPO=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d
mkdir -p "$REPO/tmp/colab" "$REPO/tmp/colab-runs/watch"
cat > "$REPO/tmp/colab/ssh_config" <<'EOF'
Host colab-sts2train
  User root
  ProxyCommand /Users/Aincrad/.local/bin/colab --auth=adc ssh --proxy-mode -s sts2train
  StrictHostKeyChecking no
  UserKnownHostsFile /dev/null
  LogLevel ERROR
  ServerAliveInterval 30
  ConnectTimeout 60
EOF
# save t0_gate_s1.sh (§9.4), sts2_remote.sh (§9.5), watch_train.sh and queue.txt (§9.6) into $REPO/tmp/colab/
/Users/Aincrad/.local/bin/colab --auth=adc usage      # balance + rate; the watcher stores the start balance
```

### 9.3 S0: seed sets and bundle (integration owner, Mac + Huginn; 0 GPU)

**Fast path (S1 does not wait for any merge).** The proven bundle `sts2train-0b1bbd3a4e` already has the floor cap,
the HEAD library and the trainer S1 needs. Only the three seed sets are missing. They are data files: commit them on
`train/silent-seedsets` (its worktree is clean at `8eb7382`), then copy them into the bundle's `seedsets/` on the VM
(§9.4). `STS2VEC_SEEDSETS` points there, and reports recognise them as frozen through their `sha256`.

```bash
uptime                                   # load < 8 before local heavy work; at most 2 heavy jobs at once
SS=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-silent-seedsets
cd "$SS" && git status --short           # must be clean (branch train/silent-seedsets @ 8eb7382)
# Adapt the C1 generator (renderer, checksum cross-check, overlap guard; keep the --write refusal to overwrite):
#   /private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/silent-seedsets/make_silent_seedsets.py
# -> silent-a10-dev-s128.json  id silent-a10-dev-v1   role dev      A10  D1A10SI001..D1A10SI128
#    silent-a10-test-s100.json id silent-a10-test-v1  role heldout  A10  H1A10SI001..H1A10SI100
#    silent-a0-dev-s100.json   id silent-a0-dev-v1    role dev      A0   D1A0SI001..D1A0SI100
# Add the three ids to FROZEN (sim/tools/eval/tests/test_seedsets.py) and to the test_committed_seedsets_load
# parametrize list (sim/tools/vec/tests/test_protocol.py); not to the 5-character or the C1 target-set tests.
uv run --project sim/tools/eval pytest sim/tools/eval/tests -q
uv run --project sim/tools/vec pytest sim/tools/vec/tests/test_protocol.py -q
git add sim/tools/eval/seedsets/silent-a10-dev-s128.json sim/tools/eval/seedsets/silent-a10-test-s100.json \
  sim/tools/eval/seedsets/silent-a0-dev-s100.json sim/tools/eval/tests/test_seedsets.py sim/tools/vec/tests/test_protocol.py
git commit -m "seedsets: Silent dev/held-out sets silent-a10-dev-v1 (128), silent-a10-test-v1 (100), silent-a0-dev-v1 (100)"
REPO=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d
BN=sts2train-0b1bbd3a4e
mkdir -p "$REPO/tmp/colab/seedsets-extra"
cp sim/tools/eval/seedsets/silent-a10-dev-s128.json sim/tools/eval/seedsets/silent-a10-test-s100.json \
   sim/tools/eval/seedsets/silent-a0-dev-s100.json "$REPO/tmp/colab/seedsets-extra/"
ssh raven "mkdir -p /data/raven/hack-sts2/train-runs /data/raven/hack-sts2/tmp/colab-smoke"   # mirror + eval dirs
```

**Clean path (later; for any new bundle):** after `train/silent-seedsets` is merged into the integration branch:

- `scripts/train/make_colab_bundle.sh`. HEAD `35424a8` changed `sim/Sts2Sim.Core`, so this is a fresh AOT build on
  raven (≈3 min), not a cache hit.
- Because the `.so` differs, adopt such a bundle only at a session boundary, and only after the same checkpoint's greedy
  dev-128 eval matches per game on both libraries.
- Then the Huginn smoke:
  `ssh raven "cd /data/raven/hack-sts2/tmp/colab-smoke && tar xzf $BN.tar.gz && docker run --rm -v \$PWD:/w -w /w python:3.13 bash $BN/smoke_bundle.sh /w/smoke-$BN 2 8"`
  after `scp`-ing the tarball there.
- Do not commit in that worktree while `make_colab_bundle.sh` runs: it reads HEAD at start and again after the AOT
  build, so a mid-run commit mislabels the bundle.
- A new bundle reaches the run only at a session boundary (§9.9 d).

### 9.4 T0 gate + Stage-1 launch on the VM

```bash
CFG="$REPO/tmp/colab/ssh_config"; CLI=/Users/Aincrad/.local/bin/colab
$CLI --auth=adc sessions                 # reuse [sts2train] (A100) if listed, otherwise:
# $CLI --auth=adc new -s sts2train --gpu A100
# $CLI --auth=adc stop -s sts2b; $CLI --auth=adc stop -s sts2c      # once their owner confirms they are idle
scp -F "$CFG" -q "$REPO/tmp/colab/$BN.tar.gz" "$REPO/tmp/colab/t0_gate_s1.sh" "$REPO/tmp/colab/sts2_remote.sh" \
  colab-sts2train:/content/
scp -F "$CFG" -q "$REPO"/tmp/colab/seedsets-extra/*.json colab-sts2train:/content/
ssh -F "$CFG" colab-sts2train "cd /content && rm -rf $BN && tar xzf $BN.tar.gz && cp /content/silent-a*-dev-s*.json \
  /content/silent-a10-test-s100.json $BN/seedsets/ && ls $BN/seedsets && grep -m1 git_sha $BN/manifest.json && \
  nvidia-smi --query-gpu=name,memory.total,memory.used --format=csv,noheader && nproc && free -g | sed -n 2p && \
  df -h /content | tail -n 1 && (pgrep -fa 'sts2train.cli' || echo 'no trainer running')"
ssh -F "$CFG" colab-sts2train "nohup setsid bash /content/t0_gate_s1.sh $BN > /content/t0.log 2>&1 < /dev/null & echo T0 started"
# follow it: gate (~12 min), baselines (~1 min), then Stage 1 (pool warm ~5 min before update 1)
ssh -F "$CFG" colab-sts2train "cat /content/gate/gate.rc 2>/dev/null; tail -n 2 /content/gate/*.log 2>/dev/null; cat /content/gate/decision.json 2>/dev/null"
ssh -F "$CFG" colab-sts2train "cat /content/runs/silent-a10-ppo/baselines.txt; tail -n 3 /content/runs/silent-a10-ppo/train.log"
# then start the watcher (§9.6)
```

`$REPO/tmp/colab/t0_gate_s1.sh`:

```bash
#!/usr/bin/env bash
# t0_gate_s1.sh — T0 launch gate + baselines + Stage-1 launch, run ON the Colab A100 VM
# (docs/train-plan-silent-a10-2026-11-03.md §3.1, §9.4):
#   nohup setsid bash /content/t0_gate_s1.sh sts2train-<sha> > /content/t0.log 2>&1 < /dev/null &
# Gate: mid3 bf16, mid3 fp32, large + grad-ckpt bf16; 8 PPO updates each (Silent A10, N 4096, rollout 32,
# minibatch 8192, pool 4096), one after another; rule -> /content/gate/decision.json. Then heuristic / first / random
# baselines on the dev, test and target sets (-> RUN/baselines.txt) and exec Stage 1 into RUN.
# Skip the gate: SKIP_GATE=1 FLAGS="--d-model 192 --state-dim 512 --depth 3 --heads 4 --ff-mult 4 --no-amp".
set -uo pipefail
BN="${1:?usage: t0_gate_s1.sh sts2train-<sha>}"
B="/content/$BN"; G=/content/gate; RUN=/content/runs/silent-a10-ppo
export OMP_WAIT_POLICY=PASSIVE PIP_DISABLE_PIP_VERSION_CHECK=1 PIP_ROOT_USER_ACTION=ignore
export STS2VEC_AOT_LIB="$B/lib/Sts2Sim.Vec.so" STS2VEC_SEEDSETS="$B/seedsets"
COMMON=(--device cuda --characters SILENT --ascension 10 --step-cap 1200 --floor-step-cap 80 --objective protocol
        --win-bonus 1.0 --rollout 32 --epochs 2 --minibatch 8192 --torch-threads 2)
MID=(--d-model 192 --state-dim 512 --depth 3 --heads 4 --ff-mult 4)
mkdir -p "$G" "$RUN" /content/baselines
python3 -m pip install -q -r "$B/requirements-colab.txt" &&
  python3 -m pip install -q --no-deps --force-reinstall "$B"/wheels/*.whl || { echo "T0 ABORT: pip install" >&2; exit 2; }
export SKIP_INSTALL=1
python3 -c "from sts2vec.protocol import load_seedset as L; [print(s, len(L(s)['games'])) for s in
  ('silent-a10-dev-v1', 'silent-a10-test-v1', 'silent-a10-v1')]" \
  || { echo "T0 ABORT: Silent dev/test sets missing from $B/seedsets (commit them, rebuild the bundle)" >&2; exit 4; }
gate() {   # gate NAME FLAGS... -> 8 updates into $G/NAME
  local name="$1"; shift
  rm -rf "${G:?}/$name"
  bash "$B/run_train.sh" "$G/$name" 12 4096 "${COMMON[@]}" "$@" --pool 4096 --seed 99 --max-updates 8 \
    --eval-every 0 --ckpt-every 0 --ckpt-every-min 0 --log-every 1 > "$G/$name.log" 2>&1
  echo "$(date -u +%FT%TZ) $name rc=$?" >> "$G/gate.rc"
  export SKIP_CHECK=1                        # sts2vec check ran with the first gate config
}
if [[ "${SKIP_GATE:-0}" != 1 ]]; then
  gate A-amp "${MID[@]}" --amp
  gate A-fp32 "${MID[@]}" --no-amp
  gate B-amp --preset large --grad-checkpoint --amp
  python3 - "$G" > "$G/decision.json" <<'PY'
import json, statistics, sys
from pathlib import Path
G = Path(sys.argv[1])
res = {}
for name in ("A-amp", "A-fp32", "B-amp"):
    p = G / name / "metrics.jsonl"
    rows = [json.loads(x) for x in p.read_text().splitlines() if x.strip()] if p.exists() else []
    tail = [r for r in rows if r.get("update", 0) >= 3] or rows
    med = lambda k: statistics.median([r[k] for r in tail]) if tail else None  # noqa: E731
    err = sum((r.get("sim_error") or 0) + (r.get("content_gap") or 0) + (r.get("illegal") or 0) for r in rows)
    res[name] = {"updates": len(rows), "errors": err, "sps": med("env_steps_per_s") or 0.0,
                 "t_env_s": med("t_env_s"), "t_infer_s": med("t_infer_s"), "t_update_s": med("t_update_s"),
                 "gpu_peak_mb": max([r.get("gpu_mem_peak_mb") or 0 for r in rows], default=None),
                 "ok": len(rows) >= 8 and err == 0}
mid = ["--d-model", "192", "--state-dim", "512", "--depth", "3", "--heads", "4", "--ff-mult", "4"]
out = {"gate": res, "rule": "large if clean, >= 10k and >= 0.6 x mid3; mid3 bf16 only if > 5% faster than fp32; "
                            "mid3 < 12k -> --epochs 1; mid3 < 8k or no clean mid3 -> abort"}
ok_a = [n for n in ("A-fp32", "A-amp") if res[n]["ok"]]
if not ok_a:
    out.update(winner=None, flags=None, abort="no clean mid3 gate run (see gate/*.log)")
else:
    a = ok_a[0]
    if a == "A-fp32" and res["A-amp"]["ok"] and res["A-amp"]["sps"] > 1.05 * res["A-fp32"]["sps"]:
        a = "A-amp"
    sa, b = res[a]["sps"], res["B-amp"]
    if b["ok"] and b["sps"] >= 10000 and b["sps"] >= 0.6 * sa:
        out.update(winner="B-amp", flags=["--preset", "large", "--grad-checkpoint", "--amp"])
    elif sa >= 8000:
        out.update(winner=a, flags=mid + (["--amp"] if a == "A-amp" else ["--no-amp"])
                   + (["--epochs", "1"] if sa < 12000 else []))
    else:
        out.update(winner=None, flags=None, abort=f"mid3 {sa:.0f} env-steps/s < 8k: relaunch by hand with --preset small")
print(json.dumps(out, indent=1))
PY
  FLAGS="$(python3 -c 'import json, sys; print(" ".join(json.load(open(sys.argv[1])).get("flags") or []))' "$G/decision.json")"
fi
cat "$G/decision.json" 2> /dev/null
[[ -n "${FLAGS:-}" ]] || { echo "T0 ABORT: no Stage-1 model flags (see $G/decision.json, $G/*.log)" >&2; exit 3; }
for P in heuristic first random; do
  for S in silent-a10-dev-v1 silent-a10-test-v1 silent-a10-v1; do
    python3 -m sts2vec.cli eval --policy "$P" --seedset "$S" --threads 12 --out "/content/baselines/$P-$S" \
      > "/content/baselines/$P-$S.log" 2>&1 || echo "baseline $P $S rc=$?"
  done
done
python3 - > "$RUN/baselines.txt" <<'PY'
import glob, json, os
for f in sorted(glob.glob("/content/baselines/*/report.json")):
    h = json.load(open(f))["headline"]
    print(f"{os.path.basename(os.path.dirname(f)):34s} progress {h['progress_mean']:.4f} "
          f"ci95 {[round(x, 4) for x in h['progress_ci95']]} floor {h['floor_mean']} replayable {h['replayable']}/{h['games']}")
PY
cat "$RUN/baselines.txt"
# shellcheck disable=SC2206
S1=("${COMMON[@]}" $FLAGS --pool 32768 --seed 0 --lr 3e-4 --gamma 1.0 --lam 0.95 --clip 0.2 --vf-coef 0.5
    --ent-coef 0.01 --max-grad-norm 0.5 --max-rows 64 --eval-every 50 --eval-seedset silent-a10-dev-v1
    --ckpt-every 50 --ckpt-every-min 10 --keep-ckpts 6 --log-every 5 --max-hours 3.75)
echo "${S1[*]}" > "$RUN.s1.flags"
unset SKIP_CHECK
echo "$(date -u +%FT%TZ) Stage 1: run_train.sh $RUN 12 4096 ${S1[*]}"
exec bash "$B/run_train.sh" "$RUN" 12 4096 "${S1[@]}" > "$RUN.launch.log" 2>&1
```

### 9.5 VM helper `$REPO/tmp/colab/sts2_remote.sh` (uploaded to `/content/`)

```bash
#!/usr/bin/env bash
# sts2_remote.sh — VM-side helper for the Mac watcher (docs/train-plan-silent-a10-2026-11-03.md §9.5)
#   status RUN                     one JSON line: state, update, sps, recent/best/last eval, age_s, proc, t0
#   pull RUN                       tar on stdout: small run files + best.pt + newest ckpt + milestones/
#   launch BN RUN [train flags]    nohup setsid run_train.sh RUN 12 4096 FLAGS (run_train.sh adds --resume auto)
#   stop | kill                    SIGTERM (finish the update, checkpoint, exit) | SIGTERM, 60 s, SIGKILL
#   milestone BN RUN TAG SET...    greedy eval of RUN/checkpoints/best.pt on each set -> RUN/milestones/TAG-SET
set -uo pipefail
cmd="${1:-}"; shift || true
case "$cmd" in
status)
  python3 - "$1" <<'PY'
import json, os, subprocess, sys, time
from datetime import datetime
run = sys.argv[1]
alive = lambda pat: subprocess.run(["pgrep", "-f", pat], capture_output=True).returncode == 0  # noqa: E731
try:
    st = json.load(open(os.path.join(run, "status.json")))
except Exception:
    st = {}
last = None
try:
    rows = [x for x in open(os.path.join(run, "evals.jsonl")).read().splitlines() if x.strip()]
    last = json.loads(rows[-1]) if rows else None
except Exception:
    pass
upd = st.get("updated_at")
ep = datetime.fromisoformat(upd).timestamp() if upd else None
print(json.dumps({
    "state": st.get("state"), "stop_reason": st.get("stop_reason"), "session": st.get("session"),
    "update": st.get("update"), "env_steps": st.get("env_steps"), "sps": st.get("env_steps_per_s"),
    "recent_progress": (st.get("recent") or {}).get("progress_mean"),
    "best": (st.get("best_eval") or {}).get("progress_mean"),
    "last_eval": None if not last else {k: last.get(k) for k in ("update", "progress_mean", "replayable", "games", "error")},
    "updated_epoch": ep, "age_s": None if ep is None else round(time.time() - ep),
    "proc": alive("sts2train.cli train"), "t0": alive("t0_gate_s1.sh")}))
PY
  ;;
pull)
  cd "$1" || exit 1
  f=(status.json metrics.jsonl evals.jsonl config.json train.log)
  for x in summary.json baselines.txt checkpoints/best.pt milestones; do [[ -e "$x" ]] && f+=("$x"); done
  c="$(ls checkpoints 2> /dev/null | grep -E '^ckpt-[0-9]+\.pt$' | sort | tail -n 1)"
  [[ -n "$c" ]] && f+=("checkpoints/$c")
  tar cf - "${f[@]}" 2> /dev/null
  ;;
launch)
  BN="$1"; RUN="$2"; shift 2
  mkdir -p "$RUN"; cd /content || exit 1
  OMP_WAIT_POLICY=PASSIVE nohup setsid bash "/content/$BN/run_train.sh" "$RUN" 12 4096 "$@" \
    >> "$RUN.launch.log" 2>&1 < /dev/null &
  echo "launched pid $! at $(date -u +%FT%TZ): $*"
  ;;
stop) if pkill -TERM -f "sts2train.cli train"; then echo "SIGTERM sent"; else echo "no trainer running"; fi ;;
kill) pkill -TERM -f "sts2train.cli train"; sleep 60; pkill -KILL -f "sts2train.cli train"; echo "killed" ;;
milestone)
  BN="$1"; RUN="$2"; TAG="$3"; shift 3
  export STS2VEC_AOT_LIB="/content/$BN/lib/Sts2Sim.Vec.so" STS2VEC_SEEDSETS="/content/$BN/seedsets"
  mkdir -p "$RUN/milestones"
  for S in "$@"; do
    out="$RUN/milestones/$TAG-$S"; rm -rf "$out"
    python3 -m sts2train.cli eval --seedset "$S" --checkpoint "$RUN/checkpoints/best.pt" --device cuda \
      --threads 12 --out "$out" > "$out.log" 2>&1
    python3 - "$out" "$S" "$TAG" <<'PY'
import json, sys
out, s, tag = sys.argv[1:4]
try:
    h = json.load(open(out + "/report.json"))["headline"]
except Exception as e:
    print(f"MILESTONE {tag} {s}: FAILED ({e})")
    sys.exit(0)
ok = h.get("replayable") == h.get("games")
print(f"MILESTONE {tag} {s}: progress {h['progress_mean']:.4f} ci95 {[round(x, 4) for x in h['progress_ci95']]} "
      f"wins {h['wins']}/{h['games']} floor {h.get('floor_mean')} acts {h.get('act_reached')} "
      f"results {h.get('results')} replayable {h.get('replayable')}/{h['games']} {'OK' if ok else 'REPLAY-FAIL'}")
PY
  done
  ;;
*) sed -n '2,8p' "$0"; exit 2 ;;
esac
```

### 9.6 Watcher `$REPO/tmp/colab/watch_train.sh` and the session queue `$REPO/tmp/colab/queue.txt`

How it polls. Every 5 min:
1. `colab usage`: the budget guard and the usage-rate alarm.
2. `ssh … sts2_remote.sh status`: one JSON line.
3. A one-line `colab exec` keepalive, so the kernel shows activity.
4. Its decision:
   - Trainer running: OK; kill and resume if `status.json` is > 30 min old.
   - Within 5 min of a launch: wait.
   - `finished` (budget end): pull, run boundary evals, launch the next queue line, or stop the session when the queue
     is empty.
   - Dead, `error`, `aborted` or unexpectedly `stopped`: resume with the same flags, at most 3 times per session. A
     non-finite loss adds `--no-amp`.
   - ssh failing 3 times and the session gone: recreate it and restore the run dir from the Mac mirror (at most 3 per
     run).

Every 30 min it pulls the small files, `best.pt` and the newest checkpoint, and mirrors them to Huginn.

```bash
#!/usr/bin/env bash
# watch_train.sh — unattended Mac-side watcher for the Colab Silent-A10 PPO run
# (docs/train-plan-silent-a10-2026-11-03.md §9.6). Start it detached from the Claude app (an app crash cannot kill a
# screen session) and keep the Mac awake while it runs:
#   screen -dmS sts2watch caffeinate -dimsu env BN=sts2train-<sha> bash "$REPO/tmp/colab/watch_train.sh"
# Env: BN (required), SESS=sts2train, RUN=/content/runs/silent-a10-ppo, GUARD=300, WARN=250 (units since the stored
# start balance), POLL=300, PULL_EVERY=1800, HANG_S=1800, MAX_CRASH=3, MAX_RECREATE=3, MAX_RATE=7.5, STOP_ALL=0.
set -uo pipefail
REPO=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d
BN="${BN:?set BN=sts2train-<sha>}"; SESS="${SESS:-sts2train}"; RUN="${RUN:-/content/runs/silent-a10-ppo}"
GUARD="${GUARD:-300}"; WARN="${WARN:-250}"; POLL="${POLL:-300}"; PULL_EVERY="${PULL_EVERY:-1800}"
HANG_S="${HANG_S:-1800}"; MAX_CRASH="${MAX_CRASH:-3}"; MAX_RECREATE="${MAX_RECREATE:-3}"; MAX_RATE="${MAX_RATE:-7.5}"
C="$REPO/tmp/colab"; W="$REPO/tmp/colab-runs/watch"; NAME="$(basename "$RUN")"; BK="$REPO/tmp/colab-runs/$NAME"
CLI=/Users/Aincrad/.local/bin/colab
mkdir -p "$W"
log()   { printf '%s %s\n' "$(date '+%F %T')" "$*" >> "$W/watch.log"; }
alert() { log "ALERT $*"; osascript -e "display notification \"${*//\"/}\" with title \"sts2 watcher\"" > /dev/null 2>&1 || true; }
cb()    { "$CLI" --auth=adc "$@"; }
vm()    { ssh -F "$C/ssh_config" -o ConnectTimeout=60 "colab-$SESS" "$@"; }
ge()    { python3 -c 'import sys; sys.exit(0 if float(sys.argv[1]) >= float(sys.argv[2]) else 1)' "$1" "$2"; }
jget()  { python3 -c 'import json, sys
try: v = json.loads(sys.argv[1])
except Exception: v = {}
for k in sys.argv[2].split("."): v = v.get(k) if isinstance(v, dict) else None
print("" if v is None else v)' "$1" "$2"; }
keepalive() { printf 'print("watcher keepalive")\n' | cb exec -s "$SESS" --timeout 60 > /dev/null 2>&1 || log "keepalive exec failed"; }

pull() {   # small files + best.pt + newest ckpt + milestones -> $BK (swapped in whole), then mirror to Huginn
  local tmp="$BK.part"; rm -rf "$tmp"; mkdir -p "$tmp"
  if vm bash /content/sts2_remote.sh pull "$RUN" 2>> "$W/watch.log" | tar xf - -C "$tmp" 2>> "$W/watch.log" \
      && [[ -s "$tmp/status.json" ]]; then
    if [[ -f "$BK/checkpoints/best.pt" && -f "$tmp/checkpoints/best.pt" ]] \
        && ! cmp -s "$BK/checkpoints/best.pt" "$tmp/checkpoints/best.pt"; then
      mkdir -p "$BK.history" && cp "$BK/checkpoints/best.pt" "$BK.history/best-$(date +%m%d-%H%M).pt"
    fi
    rm -rf "$BK.old"; [[ -d "$BK" ]] && mv "$BK" "$BK.old"; mv "$tmp" "$BK"; rm -rf "$BK.old"
    log "pulled $(du -sh "$BK" | cut -f1) into $BK"
    rsync -a --delete --timeout=120 "$BK/" "raven:/data/raven/hack-sts2/train-runs/$NAME/" > /dev/null 2>&1 \
      || log "Huginn mirror failed (non-fatal)"
    return 0
  fi
  rm -rf "$tmp"; log "pull failed"; return 1
}
launch() {   # launch NAME FLAGS...: record it, start it on the VM (run_train.sh adds --resume auto)
  local name="$1"; shift
  printf '%s %s\n' "$name" "$*" > "$W/current"; date +%s > "$W/launched_at"
  vm bash /content/sts2_remote.sh launch "$BN" "$RUN" "$@" >> "$W/watch.log" 2>&1 || alert "launch of $name failed (ssh)"
  log "launched $name: $*"
}
next_queue() {   # first queue line whose name is not in queue.done
  local name rest
  while read -r name rest; do
    [[ -z "$name" || "$name" == \#* ]] && continue
    grep -qx -- "$name" "$W/queue.done" 2> /dev/null && continue
    printf '%s %s\n' "$name" "$rest"; return 0
  done < "$C/queue.txt"
  return 1
}
milestone() {   # boundary evals of best.pt on the VM GPU (the S1 boundary is M3)
  local sets="silent-a10-v1"
  [[ "$1" == s1 ]] && sets="silent-a10-v1 silent-a10-test-v1 std-a10-v1 silent-a0-v1"
  # shellcheck disable=SC2086
  vm bash /content/sts2_remote.sh milestone "$BN" "$RUN" "$1" $sets >> "$W/watch.log" 2>&1 || alert "milestone evals failed after $1"
  grep -E "MILESTONE $1 .*(REPLAY-FAIL|FAILED)" "$W/watch.log" > /dev/null && alert "milestone $1: replay or eval failure (watch.log)"
  pull > /dev/null 2>&1 || true
}
session_done() {   # the session ended on its budget: pull, evaluate, launch the next queue line or stop
  local cur line nm fl
  read -r cur _ < "$W/current"
  pull; echo "$cur" >> "$W/queue.done"; echo 0 > "$W/crashes"
  milestone "$cur"
  if line="$(next_queue)"; then
    read -r nm fl <<< "$line"
    # shellcheck disable=SC2086
    launch "$nm" $fl
  else
    alert "queue finished after $cur: stopping [$SESS]"; cb stop -s "$SESS" >> "$W/watch.log" 2>&1; exit 0
  fi
}
crash_relaunch() {   # trainer died / hung / stopped unexpectedly: resume it with the session's flags
  local why="$1" n cur flags
  pull > /dev/null 2>&1 || true
  vm "tail -n 30 $RUN.launch.log" >> "$W/watch.log" 2>&1 || true
  n=$(( $(cat "$W/crashes" 2> /dev/null || echo 0) + 1 )); echo "$n" > "$W/crashes"
  if (( n > MAX_CRASH )); then
    alert "trainer failed $n times in this session ($why): stopping [$SESS] for a human"
    cb stop -s "$SESS" >> "$W/watch.log" 2>&1; exit 1
  fi
  if [[ ! -s "$W/current" ]]; then alert "trainer down ($why) and no session flags recorded: needs a human"; return; fi
  read -r cur flags < "$W/current"
  if grep -qi 'non-finite' <<< "$why"; then flags="$flags --no-amp"; fi
  alert "relaunching $cur after: $why (attempt $n of $MAX_CRASH)"
  # shellcheck disable=SC2086
  launch "$cur" $flags
}
recover_session() {   # ssh keeps failing: if the session is gone, recreate it and restore the run from the mirror
  local n cur flags
  if [[ ! -f "$W/restoring" ]]; then
    if cb sessions 2> /dev/null | grep -q "^\[$SESS\]"; then log "ssh fails but [$SESS] is still listed; retrying"; return; fi
    n=$(cat "$W/recreated" 2> /dev/null || echo 0)
    if (( n >= MAX_RECREATE )); then alert "[$SESS] gone again after $n recreations: watcher exits"; exit 1; fi
    echo $((n + 1)) > "$W/recreated"
    alert "[$SESS] is gone (pruned or preempted): recreating ($((n + 1)) of $MAX_RECREATE)"
    cb new -s "$SESS" --gpu A100 >> "$W/watch.log" 2>&1 || { alert "colab new failed; retrying at the next poll"; return; }
    touch "$W/restoring"; sleep 60
  fi
  if ! { scp -F "$C/ssh_config" -q "$C/$BN.tar.gz" "$C/sts2_remote.sh" "$C"/seedsets-extra/*.json "colab-$SESS:/content/" \
         && vm "cd /content && rm -rf $BN && tar xzf $BN.tar.gz && cp /content/silent-a*.json $BN/seedsets/ && mkdir -p $RUN"
       } >> "$W/watch.log" 2>&1; then
    alert "bundle upload to the new [$SESS] failed; retrying at the next poll"; return
  fi
  if ls "$BK"/checkpoints/ckpt-*.pt > /dev/null 2>&1; then
    if ! { tar czf "$W/restore.tgz" -C "$BK" . && scp -F "$C/ssh_config" -q "$W/restore.tgz" "colab-$SESS:/content/" \
           && vm "tar xzf /content/restore.tgz -C $RUN"; } >> "$W/watch.log" 2>&1; then
      alert "run-dir restore failed; retrying at the next poll"; return
    fi
    rm -f "$W/restoring"; read -r cur flags < "$W/current"
    # shellcheck disable=SC2086
    launch "$cur" $flags
  else
    rm -f "$W/restoring"; alert "no checkpoint in $BK: restarting Stage 1 from scratch on the new session"
    # shellcheck disable=SC2046
    launch s1 $(cat "$W/s1.flags")
  fi
}
guard_stop() {   # spent >= GUARD: SIGTERM (checkpoint within one update), pull, stop the session(s), exit
  local i st s
  alert "budget guard: $1 units spent since $START (guard $GUARD): checkpoint, pull, stop [$SESS]"
  vm bash /content/sts2_remote.sh stop >> "$W/watch.log" 2>&1
  for i in $(seq 1 20); do
    sleep 30; st="$(vm bash /content/sts2_remote.sh status "$RUN" 2> /dev/null)"
    [[ "$(jget "$st" proc)" == True ]] || break
  done
  pull; cb stop -s "$SESS" >> "$W/watch.log" 2>&1
  if [[ "${STOP_ALL:-0}" == 1 ]]; then
    for s in $(cb sessions 2> /dev/null | sed -n 's/^\[\([^]]*\)\].*/\1/p'); do cb stop -s "$s" >> "$W/watch.log" 2>&1; done
  fi
  alert "stopped at the guard; sessions still listed: $(cb sessions 2> /dev/null | cut -d' ' -f1 | tr '\n' ' ')"
  exit 0
}

[[ -s "$W/budget_start" ]] || cb usage 2> /dev/null | awk '/Current balance/ {print $3}' > "$W/budget_start"
START="$(cat "$W/budget_start")"
[[ -n "$START" ]] || { echo "cannot read the compute-unit balance (colab --auth=adc usage)" >&2; rm -f "$W/budget_start"; exit 2; }
log "watcher up: session [$SESS], run $RUN, bundle $BN, guard $GUARD units from balance $START"
fails=0; last_pull=0; warned=0; hot=0; quiet=0
while :; do
  now=$(date +%s); u="$(cb usage 2> /dev/null)"
  bal="$(awk '/Current balance/ {print $3}' <<< "$u")"; rate="$(awk '/Usage rate/ {sub("/hr", "", $3); print $3}' <<< "$u")"
  if [[ -n "$bal" ]]; then
    spent="$(python3 -c 'import sys; print(round(float(sys.argv[1]) - float(sys.argv[2]), 2))' "$START" "$bal")"
    ge "$spent" "$GUARD" && guard_stop "$spent"
    if (( ! warned )) && ge "$spent" "$WARN"; then alert "budget: $spent of $GUARD units spent"; warned=1; fi
  fi
  if [[ -n "$rate" ]] && ge "$rate" "$MAX_RATE"; then
    hot=$((hot + 1))
    (( hot == 2 )) && alert "usage rate $rate units/h: more than one A100 is up: $(cb sessions 2> /dev/null | cut -d' ' -f1 | tr '\n' ' ')"
  else
    hot=0
  fi
  if [[ -f "$W/restoring" ]]; then recover_session; sleep "$POLL"; continue; fi
  [[ -s "$W/s1.flags" ]] || { vm cat "$RUN.s1.flags" > "$W/s1.flags" 2> /dev/null || rm -f "$W/s1.flags"; }
  [[ -s "$W/current" || ! -s "$W/s1.flags" ]] || printf 's1 %s\n' "$(cat "$W/s1.flags")" > "$W/current"
  s="$(vm bash /content/sts2_remote.sh status "$RUN" 2>> "$W/watch.log")"
  if [[ -z "$s" ]]; then
    fails=$((fails + 1)); log "status poll failed ($fails)"
    if (( fails >= 3 )); then recover_session; fails=0; fi
    sleep "$POLL"; continue
  fi
  fails=0; keepalive
  state="$(jget "$s" state)"; proc="$(jget "$s" proc)"; t0="$(jget "$s" t0)"; age="$(jget "$s" age_s)"
  launched="$(cat "$W/launched_at" 2> /dev/null || echo 0)"
  log "state=$state proc=$proc t0=$t0 update=$(jget "$s" update) sps=$(jget "$s" sps) recent=$(jget "$s" recent_progress) best=$(jget "$s" best) last_eval=$(jget "$s" last_eval) balance=$bal rate=$rate"
  if [[ "$proc" == True || "$t0" == True ]]; then
    quiet=0
    if [[ "$state" == running && -n "$age" ]] && ge "$age" "$HANG_S" && ge "$((now - launched))" "$HANG_S"; then
      alert "trainer hung (status.json ${age}s old): kill and resume"
      vm bash /content/sts2_remote.sh kill >> "$W/watch.log" 2>&1
      crash_relaunch "hang"
    fi
  elif (( now - launched < 300 )); then
    log "launch in progress"
  elif [[ "$state" == finished ]] && ge "$(jget "$s" updated_epoch)" "$launched"; then
    session_done
  elif [[ -z "$state" ]]; then
    (( quiet++ == 0 )) && alert "no trainer and no status.json in $RUN: T0 aborted or the launch failed (see /content/t0.log, $RUN.launch.log)"
  else
    crash_relaunch "state=$state reason=$(jget "$s" stop_reason)"
  fi
  if (( now - last_pull >= PULL_EVERY )); then pull && last_pull=$now; fi
  sleep "$POLL"
done
```

`$REPO/tmp/colab/queue.txt` (Stage-2 sessions; the watcher reads it at every boundary, so edits take effect at the next
one; `run_train.sh` adds `--resume auto`, and the checkpoint's config supplies everything not given):

```
# NAME FLAGS...   (lines starting with # are ignored; names already in watch/queue.done are skipped)
s2a --seed 1 --max-hours 6
s2b --seed 2 --max-hours 6
s2c --seed 3 --max-hours 6 --lr 2e-4 --ent-coef 0.0075
s2d --seed 4 --max-hours 6 --lr 2e-4 --ent-coef 0.0075
s2e --seed 5 --max-hours 6 --lr 1e-4 --ent-coef 0.005
s2f --seed 6 --max-hours 6 --lr 1e-4 --ent-coef 0.005
```

Start, inspect, stop:

```bash
screen -dmS sts2watch caffeinate -dimsu env BN=$BN bash "$REPO/tmp/colab/watch_train.sh"
screen -ls                                                     # sts2watch must be listed
tail -n 30 "$REPO/tmp/colab-runs/watch/watch.log"; grep -E 'ALERT|MILESTONE' "$REPO/tmp/colab-runs/watch/watch.log" | tail -n 20
screen -S sts2watch -X quit                                    # stop the watcher; training continues on the VM
```

The VM may be reclaimed ≈15–20 min after client activity stops (inferred from the 11-03 prune). Do not leave a running
session without the watcher for longer than that. The watcher's state files persist, so restarting it continues where
it left off: the start balance, the current session, finished queue entries and counters.

### 9.7 Reading the run by hand

```bash
ssh -F "$CFG" colab-sts2train 'bash /content/sts2_remote.sh status /content/runs/silent-a10-ppo'
ssh -F "$CFG" colab-sts2train 'tail -n 2 /content/runs/silent-a10-ppo/metrics.jsonl'          # per-update row (§4.5)
ssh -F "$CFG" colab-sts2train 'tail -n 5 /content/runs/silent-a10-ppo/evals.jsonl | cut -c1-400'
ssh -F "$CFG" colab-sts2train 'tail -n 20 /content/runs/silent-a10-ppo/train.log; nvidia-smi; uptime'
ssh -F "$CFG" colab-sts2train 'cat /content/runs/silent-a10-ppo/baselines.txt /content/gate/decision.json'
cd "$REPO" && uv run --project sim/tools/train sts2train plot tmp/colab-runs/silent-a10-ppo   # curve.png from the mirror
```

`status.json` fields:
- `state`: running, finished, stopped, aborted or error.
- `stop_reason`, `update`, `env_steps`, `env_steps_per_s`.
- `recent`: progress, win rate and floor over the last 20 updates.
- `best_eval` and `last_eval`: dev rows with `replayable`.
- `last_checkpoint`, `updated_at`.

### 9.8 When to stop the session; the budget guard

The watcher stops `[sts2train]` by itself:
- when the queue is empty after a session ends;
- at the **300-unit guard**;
- after more than 3 trainer failures in one session;
- when a recreated session cannot be restored 3 times.

It always pulls first, because the VM disk is lost on stop.

Stop by hand (`bash /content/sts2_remote.sh stop`, wait for `state: stopped`, pull, then
`colab --auth=adc stop -s sts2train`):
- when a §5 gate says stop;
- before code work that takes longer than ≈30 min while the GPU would sit idle;
- never leave an A100 without a running trainer for more than ≈15 min (6.73 units/h).

Stop `[sts2b]` and `[sts2c]` as soon as their owner confirms they are idle. Phase 1 needs one A100.

**Budget guard.**
- The start balance is stored once in `tmp/colab-runs/watch/budget_start`, at the first watcher start.
- spent = start − the current balance from `colab usage`. This is **account-wide**: idle extra sessions eat the same
  budget.
- WARN at 250 units.
- GUARD at 300 units: SIGTERM (the trainer finishes its update and checkpoints), pull, stop `[sts2train]`. With
  `STOP_ALL=1` it also stops every other listed session. Then the watcher exits.
- The usage-rate alarm fires when two consecutive polls read > 7.5 units/h, meaning more than one A100 is up.
- Only the user raises the guard, at the phase-1 review. Restart the watcher with a new `GUARD`; keep `budget_start`.
- The user's overall budget is 800 units from 890.85. Never let the balance fall below ≈91.

### 9.9 Manual interventions

- **(a) Pause or resume the watcher.** Quit: `screen -S sts2watch -X quit`. Resume: start it again (§9.6).
- **(b) Re-run or override T0.** `ssh … "SKIP_GATE=1 FLAGS='<model + precision flags>' nohup setsid bash
  /content/t0_gate_s1.sh $BN > /content/t0.log 2>&1 < /dev/null &"`. Only into an empty `RUN`: a run dir holding
  checkpoints of another architecture is refused.
- **(c) A0 interlude.** Edit `queue.txt` (§5).
- **(d) New bundle** (e.g. `--compile` adopted, the save-pools merge, the clean-path rebuild):
  1. Build and smoke it (§9.3).
  2. `scp` and unpack it on the VM, and copy the extra seed sets into its `seedsets/`.
  3. `screen -S sts2watch -X quit`.
  4. Add the new flags (e.g. `--compile reduce-overhead`) to the next queue line.
  5. Restart the watcher with the new `BN` at a boundary.
  6. Same `RUN`: `--resume auto` continues the run under the new wheels with the same `.so`.
- **(e) Rollback to `best.pt`** (dev > 0.03 below best on 2 consecutive evals):

  ```bash
  screen -S sts2watch -X quit
  ssh -F "$CFG" colab-sts2train 'bash /content/sts2_remote.sh stop; sleep 90; cd /content/runs/silent-a10-ppo && \
    b=$(python3 -c "import torch; print(torch.load(\"checkpoints/best.pt\", map_location=\"cpu\", weights_only=False)[\"updates\"])") && \
    mkdir -p checkpoints/rolled-back && for f in checkpoints/ckpt-*.pt; do n=${f##*ckpt-}; n=$((10#${n%.pt})); \
    if [ "$n" -gt "$b" ]; then mv "$f" checkpoints/rolled-back/; fi; done; ls checkpoints'
  ssh -F "$CFG" colab-sts2train "bash /content/sts2_remote.sh launch $BN /content/runs/silent-a10-ppo \
    --resume /content/runs/silent-a10-ppo/checkpoints/best.pt --lr 1.5e-4 --max-hours 6"
  printf '%s --lr 1.5e-4 --max-hours 6\n' "$(cut -d' ' -f1 "$REPO/tmp/colab-runs/watch/current")" > "$REPO/tmp/colab-runs/watch/current.new" \
    && mv "$REPO/tmp/colab-runs/watch/current.new" "$REPO/tmp/colab-runs/watch/current"; date +%s > "$REPO/tmp/colab-runs/watch/launched_at"
  screen -dmS sts2watch caffeinate -dimsu env BN=$BN bash "$REPO/tmp/colab/watch_train.sh"
  ```

  The later `--resume` overrides `run_train.sh`'s `--resume auto`, and argparse keeps the last value. Moving the newer
  checkpoints aside stops a later `--resume auto` from jumping past the rollback. The watcher's `current` must not
  contain `--resume`.
- **(f) One-off eval of `best.pt` on the VM** between sessions:
  `ssh … "bash /content/sts2_remote.sh milestone $BN /content/runs/silent-a10-ppo manual silent-a10-test-v1"`.

### 9.10 Off-VM evals, paired comparison, the M3 report

Paired comparison (any two run dirs with `games/*.summary.json`; B − A):

```bash
python3 - A_DIR B_DIR <<'PY'
import glob, json, random, statistics, sys
def load(d):
    out = {}
    for f in glob.glob(f"{d}/games/*.summary.json"):
        g = json.load(open(f)); out[g["key"]] = g.get("progress") or 0.0
    return out
a, b = load(sys.argv[1]), load(sys.argv[2]); keys = sorted(set(a) & set(b))
d = [b[k] - a[k] for k in keys]; n = len(d); m = statistics.fmean(d)
se = statistics.stdev(d) / n ** 0.5 if n > 1 else float("nan"); t = 2.093 if n <= 20 else 1.98
rng = random.Random(0); bs = sorted(statistics.fmean(rng.choices(d, k=n)) for _ in range(10000))
print(f"n={n} mean delta={m:+.4f} t95=[{m - t * se:+.4f}, {m + t * se:+.4f}] boot95=[{bs[249]:+.4f}, {bs[9749]:+.4f}] "
      f"better={sum(x > 1e-9 for x in d)} worse={sum(x < -1e-9 for x in d)} same={sum(abs(x) <= 1e-9 for x in d)}")
PY
```

Baselines for pairing live on the VM. Pull them once:

```bash
ssh -F "$CFG" colab-sts2train 'tar cf - -C /content baselines' | tar xf - -C "$REPO/tmp/colab-runs/"
```

Deterministic CPU re-run on Huginn (final rows, optional at M3). It uses the same bundle, the mirrored `best.pt` and
CPU torch in a throwaway python:3.13 container. Place the bundle and the extra seed sets under
`/data/raven/hack-sts2/tmp/colab-smoke/$BN` first.

```bash
ssh raven "cd /data/raven/hack-sts2 && mkdir -p train-evals cache/pip && docker run --rm -v \$PWD:/w \
  -v \$PWD/cache/pip:/root/.cache/pip -w /w -e BN=$BN python:3.13 bash -c '
  pip install -q torch --index-url https://download.pytorch.org/whl/cpu && pip install -q numpy &&
  pip install -q --no-deps tmp/colab-smoke/\$BN/wheels/*.whl &&
  export STS2VEC_AOT_LIB=/w/tmp/colab-smoke/\$BN/lib/Sts2Sim.Vec.so STS2VEC_SEEDSETS=/w/tmp/colab-smoke/\$BN/seedsets &&
  for S in silent-a10-test-v1 silent-a10-v1; do python -m sts2train.cli eval --seedset \$S --device cpu --threads 8 \
    --checkpoint train-runs/silent-a10-ppo/checkpoints/best.pt --out train-evals/cpu-\$S; done'"
```

**M3 report checklist** (Fable, T+4:40–5:00):
1. `MILESTONE s1 …` lines for the four sets: all `OK` (n/n replayable).
2. Paired vs `heuristic-<set>` baselines on test-100 and `silent-a10-v1`.
3. Learning curve (`sts2train plot`) and dev evals over time.
4. Throughput and time split (median of the last 50 `metrics.jsonl` rows).
5. GPU peak, `floor_stall` rate, `stuck`/`capped` counts.
6. The lookahead row vs greedy, if Owner C is ready.
7. Units spent and the projected guard time (`colab usage`).
8. Decision for the night per the §5 M3 row, plus any `queue.txt` edits.

## 10. Risks

| # | Risk | Signal | Mitigation |
| --- | --- | --- | --- |
| 1 | The 11-03 Mac crash repeats (§8) | `uptime` load > 8, swap growth | The Mac only orchestrates. The trainer refuses > 3M params / large on cpu/mps; never pass `--allow-big-local` locally. At most 2 heavy local jobs. amd64 builds on Huginn. The watcher runs in `screen` + `caffeinate` |
| 2 | Colab VM reclaimed: idle ≈15–20 min without client activity (inferred), preemption, ≈24 h lifetime | 3 failed ssh polls; session missing from `colab sessions` | 5-min polls + kernel keepalive. Local-disk `RUN` + 30-min pulls, so ≤ 30 min of training is lost. Auto-recreate ≤ 3 times, restoring from the Mac mirror; `--resume auto` |
| 3 | Budget burnt by extra or idle sessions (13.54 units/h at 23:05) | rate > 7.5 units/h | Stop `sts2b`/`sts2c`. The rate alarm. The guard counts account-wide spend. Stop an A100 that has been idle ≥ 15 min |
| 4 | Throughput below plan (6 cores at 2.2 GHz, serial env→GPU loop) | G0 | Gate rule with the `--epochs 1` / `--preset small` fallbacks. `--compile` at a boundary. Async collector (phase 2) |
| 5 | A10 learning stalls in act 1 | M1/M2 | A0 interlude; entropy bump; S3 frontier restarts (phase 2); then the R1 mixed pool |
| 6 | Degenerate loops: the shop loop, in-combat prompt loops (combat decisions never count toward the floor cap) | `floor_stall` > 2%, eval `stuck`/`capped`, steps/episode rising | Train with the eval caps (1200/80). If greedy `stuck` > 5%, Owner B masks a PromptCancel repeated on the same prompt |
| 7 | Eval traps: `sts2train eval` exits 0 on replay failure; `best.pt` is chosen on progress only | `REPLAY-FAIL` in a milestone line; `replayable < games` in `evals.jsonl` | Watcher check + alert; Owner B fix (§11.1) |
| 8 | Library or bundle drift; a bundle mislabelled by a mid-build commit | `manifest.json` `git_sha` vs the commit; `encoder_version` | One `.so` per run. Rows compared within one `.so` sha256. Baselines re-run on the run's own `.so` (T0 script). No commits while `make_colab_bundle.sh` runs |
| 9 | Seed memorisation; train/eval gap | training progress ≫ dev | 32,768 fresh seeds per session, rotated (`TRAIN{k}`); dev/test seeds disjoint by construction |
| 10 | PPO instability, bf16 noise | `approx_kl` > 0.05 sustained, entropy < 0.2 early, EV < 0, non-finite loss | Rollback rule (§9.9 e); lr halving; the watcher adds `--no-amp` after a non-finite loss; fp32 unless bf16 is > 5% faster |
| 11 | Search leakage | — | Honest determinized roots only; stop at combat end; oracle never reported; search rows listed separately |
| 12 | Ownership conflicts: `train/trainer-perf` (uncommitted) and `train/save-pools` (merged `998fb89`) touch `sim/tools/train`; `sts2vec/evaluate.py` belongs to the env owner | merge conflicts | Owner C adds new files only, with its own `python -m` entry. Phase 1 needs neither in-flight branch. A new bundle is adopted only at a boundary |
| 13 | Selection bias: the best of ≈250 dev evals | dev ≫ test | Select on dev-128 only; test-100 at milestones only; paired comparisons |
| 14 | The 0.60 ambition gap | phase-1 review | Say it at the review. Phase 2 needs approval. Macro search is out of scope until the Determinizer resamples pre-rolled content |
| 15 | Credentials | — | Never copy ADC, `~/.config/colab-cli/` or keys to Huginn or the VM. `colab.log` holds bearer tokens |

## 11. Owner follow-ups

### 11.1 Owner B (trainer, `sim/tools/train`): what this plan still needs

| Priority | Item |
| --- | --- |
| Now | `train/save-pools` is merged (`998fb89`). Pin with a test that the fresh-only path (no `--start-saves`) is bit-identical to `0b1bbd3`'s trainer: same rewards, metrics keys and checkpoints. S1 runs on the proven `0b1bbd3a4e` bundle |
| Boundary | Land `train/trainer-perf` (`--compile`, `--static-rows`, `bench-update`): the compiled-vs-eager numerics check (≤ 1e-3); checkpoints loadable by `load_checkpoint`/resume with compile on or off (no `_orig_mod.` keys); an A100 `bench-update` for the gate winner at minibatch 8192 (modes off/default/reduce-overhead). Adopt only if the update is ≥ 1.5× faster |
| Soon | `sts2train eval`: exit 1 when `headline.replayable != games` and print `replayable`. Training: `best.pt` ignores eval rows with replay failures or errors |
| Soon | `--reset-best` (clear `best_eval`/`last_eval` on resume) for warm starts across eval sets or run dirs |
| Soon | Optional `--pool-prefix`. Document that `--seed k` on resume only re-prefixes the fresh pool (`TRAIN{k}`) and the env seed text (RNG states come from the checkpoint). Record the pool prefix in `config.json` |
| Soon | `make_colab_bundle.sh`: resolve HEAD once (`REV=$(git rev-parse HEAD)`) and archive `$REV` everywhere. README / `run_train.sh` examples: `/content/runs/<run>` on local disk + pulled backups (`colab drivemount` is interactive) |
| On demand (M2 fallback) | `--ascension-mix 0:1,10:3` (interleaved FreshEntry list, per-ascension progress in `metrics.jsonl`, a test of the reset histogram) |
| Phase 2, behind flags, with tests | S3 curriculum: manual `reset(mask, start_ids)` loop, capture rules, rings, scheduler, signals copied before `reset`, start-kind metrics split, potential shaping. ExIt: distillation loss + shard loader + `exit_kl`/top-1 metrics. Teacher-KL kickstart. Async collector if `t_update` ≥ 40% after compile |
| Final rows | `sts2train eval --deterministic` (`torch.use_deterministic_algorithms`, `CUBLAS_WORKSPACE_CONFIG=:4096:8`); record the bundle `.so` sha256 in `config.json` |
| Optional | Mask a PromptCancel repeated on the same prompt if greedy `stuck` > 5% |

### 11.2 Owner C brief: honest combat lookahead (eval-time search row)

Goal: a declared, search-augmented eval row for a trained checkpoint. At each combat decision it tries the top-K
candidates in determinized clones, rolls out greedily with the same network to the next player turn (or combat end),
and picks the best leaf. It is a plug-in policy for the UNMODIFIED `sts2vec.evaluate.evaluate`; no C ABI or
evaluator changes.

Start from the validated prototype
`/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/lookahead/proto_lookahead.py`
(+ `run_proto.py`). Heuristic base, 20 Silent A10 seeds: +0.033 (turn) / +0.046 (combat) paired, 0 desyncs, 20/20
replayable.

**Files** (own branch from HEAD, new files only):
- `sim/tools/train/sts2train/lookahead.py`
- `sim/tools/train/tests/test_lookahead.py`
- entry point `python -m sts2train.lookahead eval|paired`

Do not edit `cli.py`, `ppo.py`, `features.py`, `model.py` or `sts2vec/evaluate.py`. Use only `ppo.load_checkpoint`,
`features.bind_spec` / `obs_to_torch` and `model(x) -> (logits, value)`.

**Design.**
- `LookaheadConfig(K=8, R=1, H=12, horizon='turn'|'combat' (H<=40), score='value+hp'|'value'|'hp', threads=8,
  seed=0, pool_budget=50_000, device='cpu')`.
- `LookaheadPolicy(model, spec, cfg)(obs, ctx)`. On the first call it builds a private
  `VecEnv(M*(1+K*R), threads, auto_reset=False, pool=None, step_cap=10**6, floor_step_cap=0, fast_path=False,
  seed_text='SEARCH')`. For each `ctx.keys` entry 'CHAR-SEED' it calls
  `pool.add_fresh(CHAR, seed=SEED, ascension=ctx.ascension, content='ported_only')`, then
  `reset(mask, start_ids=arange(M))`.
- Per call:
  1. Byte-compare shadow vs evaluator obs (live slots); raise `Desync` on mismatch.
  2. One forward gives logits, V and the greedy action.
  3. Search where live & `global_i.is_combat==1` & `cand_n>=2`.
  4. Candidates: drop DiscardPotion; keep greedy + EndTurn (last row); fill to K by logit.
  5. Roots: `clone_determinized(shadow, seed=u64(sha256(f'{cfg.seed}:{key}:{n_g}:{r}')))`, shared by all candidates.
     Never `snapshot`.
  6. One batched `reset(mask, start_ids)`. Assert RESET and that the candidate rows equal the root's.
  7. Step 0 applies the candidate. Then greedy forwards on ACTIVE rows only, −1 elsewhere. Latch per slot:
     - error bits → invalid (log `slot_errors()`)
     - terminated → victory or defeat
     - `is_combat` 1→0 → won
     - turn horizon: `global_i.turn` > root turn, or `cand_n==0` → leaf
     - H steps → leaf

     Never continue past the end of the combat.
  8. Q:
     - defeat → 0
     - victory → 1 − P(root) + 1
     - otherwise ΔP + V(leaf), plus 0.10·hp/max_hp − 0.03·Σenemy_hp/Σenemy_max_hp for value+hp

     Mean over R; argmax, ties → greedy.
  9. Step the shadows with the returned actions.
  10. When `len(pool)` exceeds `pool_budget`, rebuild and replay the histories.
- `evaluate_lookahead(seedset, checkpoint, out, cfg)`:
  - policy_name `nn:<ckpt>+la(K,R,horizon,honest)`
  - policy_hash `weights:<wh>+la:<cfg sha8>`
  - config_extra `{search, information: 'determinized, combat-only', mirror_env: true}`
  - `stats.json`: rounds, searched, changed, forwards, env-steps, restores, pool size, ms/decision, rollout errors
- `paired(dir_a, dir_b)`: per-seed Δprogress, mean, t-CI, bootstrap CI, better/worse/same, wins.

**Tests** (CPU, < 2 min, random-init checkpoint):
- cross-slot exact restore: obs bytes + StateHash
- determinized clone: same obs, different hash; same seed → same hash
- restore counters: step_index 0, RESET
- 4-game shadow sync through `sts2vec.evaluate`, n/n replayable
- K=1 ≡ greedy
- a re-run is identical
- honest mode never calls `snapshot`
- a defeat is latched and the slot is reusable
- a forced rebuild ≡ no rebuild

**Acceptance** on `silent-a10-dev-v1` (128 games) with a fixed S1 checkpoint, on CPU:
- 128/128 replayable for greedy and for lookahead
- 0 desyncs; an identical re-run; rollout errors reported
- the paired Δ with both CIs
- turn ≤ 10 min and combat ≤ 30 min on 8 Huginn threads

ExIt go signal: Δ ≥ +0.02 with the CI excluding 0.

**Where and when.** Run on Huginn (python:3.13 + the bundle, CPU torch) or on the Mac at ≤ 4 threads with load < 8.
Never on the training VM.
- tests green by T+2:30
- first NN rows at T+3:00
- grid {turn, combat} × {value+hp, value} at T+3:15–4:15
- the best config with R=2 at M3

### 11.3 Owner A (env)

Nothing is required for phase 1. Possible speed-ups for the search env, none needed by this plan:
- batched snapshot / clone
- pool truncation
- skipping `Observe` for NOOP slots
- `DeterminizeOptions` over the ABI

The prerequisite for macro search is a Determinizer extension that resamples the encounter queue, unopened chests and
unopened card rewards (Core + `VecDeterminizeTests`).
