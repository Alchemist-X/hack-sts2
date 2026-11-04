# Self-play v2 run plan (2026-11-04): pure fresh-start common start W0, then C0 / C1 (UI loop cost)

Status: **plan of record, not yet executed.** Pre-registered before any W0 / C0 / C1 number exists.

- Code: branch `train/selfplay-v2` (from `8c9455f`):
  - `a4489e2` merges `train/sp2-seeds`: seed stream, decoupled RNG seeds, holdout isolation guard, blind set,
    episodes.jsonl.
  - `542a1e4` merges `train/sp2-cycles`: the UI revisit cycle detector, `--cycle-cost`, the eval modes, `compare`,
    `--eval-at`, `--no-best`. It also carries the integration fixes listed in §5.
  - No C# change: same library and AOT key as `8c9455f`.
- Bundle: `tmp/colab/sts2train-542a1e4eb8.tar.gz` in the `train-selfplay-v2` worktree.
  - 7,127,214 bytes, sha256 `abefe0ad4999add0123c002fd281cfae3bdc888309f8ace7a41a8b93b94765f3`.
  - AOT key `5c80f4e6ba70` (linux-x64 NativeAOT, newest symbol GLIBC_2.34).
  - Wheels `sts2vec-0.1.0`, `sts2train_smoke-0.2.0`, with `isolation.py`, `cycles.py`, `seedstream.py`, `episodes.py`
    and `compare.py`.
  - All 10 seed sets, `silent-a10-blind-s256` and `silent-a10-ab-s512` included.
  - `run_train.sh` passes `--resume auto`.
- The directive is the user's 2026-11-04 message: pause harvest, then the diagnosis and fix experiment, one GPU, serial,
  first-batch cap 40 Colab units.

**W0, C0 and C1 use no human saves and none of their resampled copies, no harvest, and no old weights, labels or
snapshots.** W0 starts from a random init (`--init-seed 1`) on 100 % fresh starts from the seed stream. The old u2225 /
u2050 checkpoints served only the read-only diagnosis (owner B's q-modes runs) and the local CPU gates in §5.

## 中文摘要

- **做什么**：
  - 从随机初始化开始，100% 正常新开局（持续生成的种子流 `SP1-SILENT-<n>`）。
  - 训练 382 次更新（约 5,007 万环境步），得到共同起点 W0。W0 阶段记录 UI 循环，但不罚。
  - 从 W0 终点完整复制训练状态，分出 C0（原奖励）和 C1（只加循环成本 0.01）。两支各再训 382 次更新，到 u764，串行执行。
  - 正式比较在固定终点 u764，不用 best.pt，也不用 20 局集选权重。
- **不用什么**：
  - 不用任何人类存档及其重采样副本，不用 harvest，不用旧模型的权重、标签或快照。
  - `--start-mode stream` 拒绝 `--start-saves`。包里的 `saves/` 是打包命令带上的惰性数据，本计划不引用。
  - 不开 `--rollout-loop-breaker`。不改 gamma、lambda、网络和其他奖励。不做搜索训练，不扩大模型。
- **时间与预算**：按 s8a 实测约 10.2 s/次更新，W0、C0、C1 各约 65 分钟训练（各约 7.3 units，按 6.73 units/h）。
  加上评测和启动，合计约 26 units（区间约 22–31）。首批上限 40 units；到上限时在两支共同达到的更新数上报告，不自动追加。
- **判定（预先登记）**：
  - 主要指标是 dev-128 无评测端 breaker（greedy-off）的进度，以及出现 UI 循环的局数比例。
  - 若 C0 在 u764 的 greedy-off 中只有 ≤6/128 局出现循环，标记"实验触发不足"，不下有效或无效的结论。
  - 若循环显著减少（McNemar 精确检验 p<0.05，ab-512 同向），且进度没有明确退化（逐种子配对差 95% CI 不全在 0 以下），
    再做独立训练种子复验。复验预计再需约 26 units，超出首批 40，需要用户另行批准。
  - 若只少了取消次数，却损害了升级、休息决策或整体进度：停止扩大，回查惩罚定义。
  - 下一阶段工程目标是评测端 breaker 介入局率 <5%、开关进度差 <0.01。这是目标，不是已有保证。

## 1. What the directive fixes, and where it lives in the code

| Directive item | Implementation | Evidence |
| --- | --- | --- |
| Human saves and resampled copies leave the mainline; old models are read-only | `--start-mode stream` refuses `--start-saves` (`ppo.resolve_config`, exit 2). W0 is a random init: no `--resume` of an old checkpoint, `init_weights_hash` recorded in `config.json` `sessions[0]` | `tests/test_seed_stream.py` config rules; gate g1 |
| Decoupled RNGs: model init, action sampling, env seed generation, start sampling | `--init-seed` (own generator, `init_rng`), `--sample-seed` (torch sampling + numpy shuffles, `seed_rngs`), `--env-seed` (stream prefix `SP<n>`), slot assignment FIFO without RNG. The stream counter `seed_stream.next` is in every checkpoint | `test_rng_decoupling_init_vs_sample`; gate g1 |
| Fresh starts from a continuously generated training seed stream; holdout conflicts checked by run seed (XxHash64), not text | `seedstream.SeedStream` skips any index whose `(character, text, ascension)` or run seed is a holdout game (`isolation.IsolationGuard`: every frozen set with a holdout role, 10 sets / 1,176 run seeds) | gate g2 |
| test-100 becomes a dev set; ab-512 is a dev comparison set, with start and generation guards | `silent-a10-test-v1` role `dev2`; `silent-a10-ab-v1` (TRAIN0-SILENT-4096..4607) is in the guard. Pool mode refuses `--seed 0 --pool 4608` | gate g2 |
| ≥256 new blind seeds, not looked at this round | `silent-a10-blind-v1` (B1A10SI001..256, content sha256 `59974078…`). Every evaluation path refuses it without `--allow-blind --purpose` | gate g3 |
| Three execution modes diagnosed on the old model | Done by owner B on u2225 / u2050 (dev-128, R=3). Memo: `scratchpad/diag/q-modes/memo.md` | Q-modes memo |
| Only verified zero-progress rest-site / shop UI revisit cycles; charge once per completed cycle; log phase + full path; no in-game change | `sts2train.cycles.CycleDetector` (always on, counting only). `--cycle-cost c` subtracts c from the trainer's reward of the closing transition | `tests/test_cycles.py`; gate g4 |
| Fixed endpoints instead of `best.pt` | `--eval-at` (rows with `mode`, weights pinned as `checkpoints/pinned-<N>.pt`), `--no-best` | gate g6 |

**The loop cost c = 0.01** is fixed, with no sweep. The Q5 reward magnitudes of the s8a objective (protocol progress +
acquisition shaping, γ 1.0, λ 0.99) are:

| Reward item | Magnitude | c = 0.01 equals |
| --- | --- | --- |
| One Act-1 room of progress | 1/51 = 0.0196 | 0.51 room |
| One Act-2 room of progress | 0.0208 | 0.48 room |
| One upgrade, shaping coefficient | 0.012 | 0.83 upgrade |
| One relic | 0.01 | 1 relic |
| One card acquisition | 0.003 | 3.3 cards |
| Death debt of a fresh episode, mean | about −0.099 | 1/10 |
| Mean objective return of a fresh episode | 0.290 | 3.4 % |

- A single exploratory SMITH → cancel costs half a room, so the cost never outweighs real progress.
- γ = 1, so the cost is not discounted, and the value head learns it like any other reward.
- Only repeated cycling adds up. The u2225 greedy-off stalls had 25–39 cycles each: 0.25–0.39, about one fresh
  episode's whole return.

## 2. Colab commands

### 2.1 Setup

Use the A100 runtime with 12 vCPU. The transport is the 2026-11-03 `ssh_config` and `colab` CLI
(`docs/train-plan-silent-a10-2026-11-03.md` §9.2). Run directories stay on local VM disk, never Drive FUSE; the Mac pulls
backups.

```bash
# Mac
REPO=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/recorder-performance-lag-0f9d6d; CFG=$REPO/tmp/colab/ssh_config
WT=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-selfplay-v2
scp -F "$CFG" -q $WT/tmp/colab/sts2train-542a1e4eb8.tar.gz colab-sts2train:/content/
ssh -F "$CFG" colab-sts2train "mkdir -p /content/sts2/runs \
  && tar xzf /content/sts2train-542a1e4eb8.tar.gz -C /content/sts2 \
  && grep -m1 aot_key /content/sts2/sts2train-542a1e4eb8/manifest.json \
  && nvidia-smi --query-gpu=name --format=csv,noheader"
```

### 2.2 The three training runs (exact flags)

The flags shared by all three runs are below. They are the s8a model / PPO / reward configuration (u2225's
`config.json`, Q5 fact 1), except:

- starts: stream instead of pool + saves;
- seeds: decoupled, all 1;
- evaluation: `--no-best`, `--eval-at`.

```bash
B=/content/sts2/sts2train-542a1e4eb8; R=/content/sts2/runs
FLAGS=(--start-mode stream --seed-stream-prefix SP1 --env-seed 1 --init-seed 1 --sample-seed 1
       --characters SILENT --ascension 10 --start-saves none
       --objective protocol --win-bonus 1.0 --acq-shaping --acq-deck-coef 0.003 --acq-relic-coef 0.01
       --acq-upgrade-coef 0.012
       --d-model 192 --state-dim 384 --depth 3 --heads 6 --ff-mult 4 --deck-counts --aux-heads --aux-coef 0.1
       --rollout 64 --epochs 1 --minibatch 4096 --lr 3e-4 --gamma 1.0 --lam 0.99 --clip 0.2 --vf-coef 0.5
       --ent-coef 0.02 --max-grad-norm 0.5 --max-rows 64 --step-cap 3000 --floor-step-cap 80
       --device cuda --amp --compile default
       --no-best --eval-at-seedset silent-a10-dev-v1 --eval-at-modes greedy-on,greedy-off
       --eval-every 25 --eval-seedset silent-a10-v1 --loop-breaker on
       --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 40 --max-hours 2 --log-every 1)
```

**W0** runs from a random init; `run_train.sh` adds `--resume auto`, which starts fresh in an empty directory:

```bash
bash $B/run_train.sh $R/sp2-w0 12 2048 "${FLAGS[@]}" --max-updates 382 --eval-at 191,382 --cycle-cost 0
```

- 382 updates × 2,048 envs × 64 steps = 50,069,504 env steps.
- Cycles are counted and logged; the cost is 0.
- Fixed-endpoint evaluations at u191 and u382 run on dev-128, greedy with the breaker on and off.
- The periodic silent-a10-v1 eval (every 25 updates) is a monitor only.

**C0 and C1** each fork W0's endpoint. Copy W0's u382 weights, Adam state, counters, RNG states and stream counter
(`pinned-0000382.pt`) into the branch's own `checkpoints/`. `--resume auto` then starts from it, and a re-run after a
disconnect continues the branch, never W0.

```bash
for arm in c0 c1; do mkdir -p $R/sp2-$arm/checkpoints
  cp $R/sp2-w0/checkpoints/pinned-0000382.pt $R/sp2-$arm/checkpoints/ckpt-0000382.pt; done
bash $B/run_train.sh $R/sp2-c0 12 2048 "${FLAGS[@]}" --max-updates 764 --eval-at 573,764 --cycle-cost 0
bash $B/run_train.sh $R/sp2-c1 12 2048 "${FLAGS[@]}" --max-updates 764 --eval-at 573,764 --cycle-cost 0.01
```

The two branches have the same model, data-generation rule, optimizer state, budget and eval settings.

- Both continue W0's seed stream, so the k-th episode started after the fork gets the same seed in both arms (common
  random numbers).
- Each arm logs W0's 2,048 in-flight episodes as `dropped` rows.
- The only flag that differs is `--cycle-cost`.
- After the first branch update, check `config.json` `sessions[-1]`:
  - `config_diff` for C0 is `{"out": [<W0 dir>, <C0 dir>], "max_updates": [382, 764], "eval_at": [[191, 382], [573,
    764]]}`; C1 has the same plus `"cycle_cost": [0.0, 0.01]`;
  - `rng_restored` is ok for numpy, torch and cuda;
  - `seed_stream_next` equals W0's final counter;
  - `dropped_at_start` is 2048.

### 2.3 Unattended serial driver (recommended)

The driver runs W0, C0, C1, the endpoint evals and the paired comparisons, one run at a time. Re-running it after a
disconnect continues where it stopped. Save the block below on the Mac as `$WT/tmp/colab/sp2_serial.sh`.

<!-- sp2_serial.sh begin -->
```bash
#!/bin/bash
# sp2_serial.sh BUNDLE RUNS: W0 -> C0 -> C1 -> endpoint evals -> paired comparisons, one GPU, one run at a time
# (docs/selfplay-v2-run-2026-11-04.md). Every stage is run_train.sh (--resume auto): a stage at its update count is
# skipped, an interrupted one continues. TRAIN / EV / W0_UPD / W0_MID / END / MID / DEV / AB only change for tests.
set -u
B=$1; R=$2
TRAIN=${TRAIN:-"bash $B/run_train.sh"}; EV=${EV:-"python3 -m sts2train.cli"}
W0_UPD=${W0_UPD:-382}; W0_MID=${W0_MID:-191}; END=${END:-764}; MID=${MID:-573}
DEV=${DEV:-silent-a10-dev-v1}; AB=${AB:-silent-a10-ab-v1}
FLAGS=(--start-mode stream --seed-stream-prefix SP1 --env-seed 1 --init-seed 1 --sample-seed 1
       --characters SILENT --ascension 10 --start-saves none
       --objective protocol --win-bonus 1.0 --acq-shaping --acq-deck-coef 0.003 --acq-relic-coef 0.01
       --acq-upgrade-coef 0.012
       --d-model 192 --state-dim 384 --depth 3 --heads 6 --ff-mult 4 --deck-counts --aux-heads --aux-coef 0.1
       --rollout 64 --epochs 1 --minibatch 4096 --lr 3e-4 --gamma 1.0 --lam 0.99 --clip 0.2 --vf-coef 0.5
       --ent-coef 0.02 --max-grad-norm 0.5 --max-rows 64 --step-cap 3000 --floor-step-cap 80
       --device cuda --amp --compile default
       --no-best --eval-at-seedset "$DEV" --eval-at-modes greedy-on,greedy-off
       --eval-every 25 --eval-seedset silent-a10-v1 --loop-breaker on
       --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 40 --max-hours 2 --log-every 1)
log() { echo "$(date -u '+%F %T') sp2: $*"; }
reached() { python3 - "$1" "$2" <<'PY'
import json, sys
from pathlib import Path
p = Path(sys.argv[1]) / "status.json"
sys.exit(0 if p.exists() and int(json.loads(p.read_text()).get("update") or 0) >= int(sys.argv[2]) else 1)
PY
}
stage() {   # RUN_DIR MAX_UPDATES EVAL_AT CYCLE_COST
  local tries=0
  until reached "$1" "$2"; do
    tries=$((tries + 1))
    if [ $tries -gt 3 ]; then log "$1 is not at update $2 after 3 sessions: stop"; return 1; fi
    log "train $1 to update $2 (session $tries, cycle cost $4)"
    $TRAIN "$1" 12 2048 "${FLAGS[@]}" --max-updates "$2" --eval-at "$3" --cycle-cost "$4" || return 1
  done
  log "$1 at update $2"
}
pin() { printf '%s/checkpoints/pinned-%07d.pt' "$1" "$2"; }
stage "$R/sp2-w0" "$W0_UPD" "$W0_MID,$W0_UPD" 0 || exit 1
for arm in c0 c1; do
  cost=0; [ $arm = c1 ] && cost=0.01
  if [ ! -d "$R/sp2-$arm/checkpoints" ]; then         # the fork: a full copy of W0's training state at its endpoint
    mkdir -p "$R/sp2-$arm/checkpoints"
    cp "$(pin "$R/sp2-w0" "$W0_UPD")" "$(printf '%s/checkpoints/ckpt-%07d.pt' "$R/sp2-$arm" "$W0_UPD")" || exit 1
    log "forked $R/sp2-$arm from W0 u$W0_UPD"
  fi
  stage "$R/sp2-$arm" "$END" "$MID,$END" $cost || exit 1
done
export STS2VEC_AOT_LIB="$B/lib/Sts2Sim.Vec.so" STS2VEC_SEEDSETS="$B/seedsets"
run_eval() {   # OUT DONE_FILE eval-args...: skip a finished evaluation, redo an interrupted one from scratch
  local out=$1 done=$2; shift 2
  [ -f "$out/$done" ] && return 0
  rm -rf "$out"; log "eval -> $out"; $EV eval --out "$out" --threads 12 --device cuda "$@"
}
for arm in c0 c1; do
  CK=$(pin "$R/sp2-$arm" "$END"); E="$R/sp2-$arm/endpoint"
  run_eval "$E/dev128-sample-off" repeats.json --seedset "$DEV" --checkpoint "$CK" --policy sample --breaker off \
    --sample-seed 1 --repeats 3
  for br in off on; do
    run_eval "$E/ab512-greedy-$br" report.json --seedset "$AB" --checkpoint "$CK" --policy greedy --breaker $br
  done
done
mkdir -p "$R/cmp"
pair() {   # NAME ARM_DIR ARM_DIR --labels A,B: sts2train compare -> $R/cmp/NAME.{txt,json}
  local name=$1; shift
  $EV compare "$@" --json "$R/cmp/$name.json" > "$R/cmp/$name.txt" 2>&1; log "compare $name rc=$?"
}
u=$(printf 'u%07d' "$END"); um=$(printf 'u%07d' "$MID"); uw=$(printf 'u%07d' "$W0_UPD")
for m in greedy-off greedy-on; do            # C1 - C0 at the fixed endpoint (official) and at the midpoint
  pair "dev128-$m-$u" "$R/sp2-c0/evals/$u/$m" "$R/sp2-c1/evals/$u/$m" --labels C0,C1
  pair "dev128-$m-$um" "$R/sp2-c0/evals/$um/$m" "$R/sp2-c1/evals/$um/$m" --labels C0,C1
  pair "ab512-$m-$u" "$R/sp2-c0/endpoint/ab512-$m" "$R/sp2-c1/endpoint/ab512-$m" --labels C0,C1
done
pair "dev128-sample-off-$u" "$R/sp2-c0/endpoint/dev128-sample-off" "$R/sp2-c1/endpoint/dev128-sample-off" \
  --labels C0,C1
for arm in c0 c1; do                         # the eval-side breaker's effect (engineering target), per arm
  pair "$arm-dev128-onoff-$u" "$R/sp2-$arm/evals/$u/greedy-off" "$R/sp2-$arm/evals/$u/greedy-on" --labels off,on
  pair "$arm-ab512-onoff-$u" "$R/sp2-$arm/endpoint/ab512-greedy-off" "$R/sp2-$arm/endpoint/ab512-greedy-on" \
    --labels off,on
done
pair "w0-dev128-onoff-$uw" "$R/sp2-w0/evals/$uw/greedy-off" "$R/sp2-w0/evals/$uw/greedy-on" --labels off,on
log "done"
```
<!-- sp2_serial.sh end -->

```bash
scp -F "$CFG" -q $WT/tmp/colab/sp2_serial.sh colab-sts2train:/content/
ssh -F "$CFG" colab-sts2train "nohup setsid bash /content/sp2_serial.sh /content/sts2/sts2train-542a1e4eb8 \
  /content/sts2/runs > /content/sts2/runs/sp2_serial.log 2>&1 < /dev/null & echo started"
```

The dev-128 greedy on / off endpoint rows are the `--eval-at` rows at u764 (`evals/u0000764/greedy-{on,off}/`). The
driver adds the endpoint dev-128 sample-off with R=3 (sample seed 1) and ab-512 greedy on / off, all with the protocol
limits (step cap 1200, floor step cap 80). The blind set is never evaluated.

**Loop share, paired across arms.** `compare` gives the per-arm share of games with a cycle and paired CIs for
progress. The paired test of the loop indicator is McNemar's exact test on the discordant games (C0 dir, then C1 dir;
the same for ab-512 with `endpoint/ab512-greedy-off`):

```bash
python3 -I - $R/sp2-c0/evals/u0000764/greedy-off $R/sp2-c1/evals/u0000764/greedy-off <<'PY'
import json, math, sys
from pathlib import Path
def ind(d): return {p.name.split(".")[0]: int(json.loads(p.read_text())["cycles"] > 0)
                    for p in Path(d, "games").glob("*.summary.json")}
a, b = ind(sys.argv[1]), ind(sys.argv[2]); assert a.keys() == b.keys()
n10 = sum(a[k] and not b[k] for k in a); n01 = sum(b[k] and not a[k] for k in a); n = n10 + n01
p = min(1.0, 2 * sum(math.comb(n, i) for i in range(min(n10, n01) + 1)) / 2 ** n) if n else 1.0
print(f"C0 {sum(a.values())}/{len(a)} C1 {sum(b.values())}/{len(b)} C0-only {n10} C1-only {n01} McNemar p {p:.4g}")
PY
```

### 2.4 Expected wall time and units

These numbers use the s8a rate of about 10.2 s per update at 2,048 envs × 64 and 6.73 units per A100 hour.

| Item | Updates | Env steps | Wall | Units |
| --- | --- | --- | --- | --- |
| W0 training | 1–382 | 50.07 M | ≈65 min | ≈7.3 |
| C0 training | 383–764 | +50.07 M | ≈65 min | ≈7.3 |
| C1 training | 383–764 | +50.07 M | ≈65 min | ≈7.3 |
| Session start ×3 (pip, `sts2vec check`, warm of 2,048 + 4,096 lookahead templates, first `torch.compile` update) | | | ≈9 min | ≈1.0 |
| `--eval-at` ×6 points (dev-128 × 2 modes) | | | ≈6 min | ≈0.7 |
| Periodic monitor (silent-a10-v1 every 25 updates, ≈46 evals) | | | ≈11 min | ≈1.3 |
| Endpoint evals ×2 arms (dev-128 sample R=3, ab-512 × 2 modes) | | | ≈10 min | ≈1.1 |
| **Total** | | 150.2 M | **≈3.9 h** | **≈26 (range 22–31)** |

- The training rate is the planning number. Not measured yet:
  - the stream mode at 2,048 envs (the CPU smoke showed +0.6 % to +5 % per update over pool mode);
  - random-init episode lengths.
- In W0's first 10 updates, watch `t_prefetch_wait_s` (should stay ≪ 1 s), `t_stream_s`, `stream_failed_total` (0) and
  `env_steps_per_s` in `metrics.jsonl`.
- Budget guard: record the balance before the upload. Keep the 2026-11-03 keepalive and the unit guard (§9.6) with
  GUARD=40. Stage launches come only from `sp2_serial.sh`.
- At the cap, stop and report at the common update count of C0 and C1. `--keep-ckpts 40` keeps every 25-update
  checkpoint of C0 for that.
- Nothing extends a run automatically.

## 3. Pre-registered decision rules (directive items 5–6)

1. **Official comparison** is C1 vs C0 at the fixed endpoint u764 (`pinned-0000764.pt`).
   - u573 and W0's u191 / u382 are trajectory points only.
   - No `best.pt` exists, and the 20-game set no longer picks the delivered weights. Each arm's deliverable is its u764
     checkpoint, fixed now.
2. **Primary metrics** (dev-128 at u764, greedy, breaker off):
   - (a) fresh-start progress, the per-seed paired difference C1 − C0 with 95 % t and bootstrap CIs (`compare`);
   - (b) the share of games with ≥1 defined UI cycle.
   - ab-512 at u764 (greedy-off) is the confirmatory set.
3. **Also reported**:
   - breaker-on scores; per-seed paired differences in both modes; the sample-off R=3 row;
   - stage funnel, clear rate, cycles per game, stall locations (`compare`);
   - training-side cycle rates: `cycles_total` / valid decisions per update, `cycles.jsonl` paths, and the share of
     episodes with a cycle.
4. **Under-triggered.** If C0 at u764 has ≤6 of 128 dev games with a UI cycle in greedy-off (<5 %), the report says
   **实验触发不足 / under-triggered**. It then claims neither that the cost works nor that it fails. C0's training-side
   cycle rate is reported next to it.
5. **The loop cost "clearly reduces loops"** when all of these hold:
   - C1 has fewer dev-128 greedy-off games with a cycle than C0;
   - McNemar exact two-sided p < 0.05 on the discordant games;
   - ab-512 greedy-off points the same way.
6. **"No clear degradation"** means the 95 % t CI of the per-seed paired progress difference (C1 − C0) is not entirely
   below 0. This must hold for dev-128 greedy-off and greedy-on, and for ab-512 greedy-off.
7. **Decision.**
   - If 5 and 6 hold, next comes a replication with independent training seeds, before any harvest. For example
     `--init-seed 2 --sample-seed 2 --env-seed 2 --seed-stream-prefix SP2`, a new W0' plus C0' / C1'.
     It costs about 26 units more, which exceeds the first-batch cap of 40, so it needs the user's go.
   - If loops fall only by fewer cancels, while upgrades, rest-site decisions or overall progress get worse, stop
     scaling and review the penalty definition. "Worse" means one of these is clearly worse in the paired sense of 6:
     - upgrades per game;
     - the share of rest-site visits that end in a confirmed upgrade;
     - progress.
   - Upgrades and the rest-site choice mix come from replaying the eval games' `history_actions`, as the q-modes scripts
     do.
8. **Engineering target for the next phase** (a target, not a guarantee): the eval-side breaker intervenes in < 5 % of
   games and the on / off progress gap is < 0.01.
   - Intervention rate = `games_with_loop_breaks / games` of the greedy-on row.
   - Gap = greedy-on − greedy-off progress, same checkpoint and set: the `*-onoff-*` comparisons.
   - Only once both hold, compare normal fresh starts against an autonomous harvest curriculum.
9. **Not attributable to the cost alone.** GPU training with bf16 and `torch.compile` is not bitwise reproducible, so
   C1 − C0 mixes the cost's effect with run-to-run noise. That is why rule 7 asks for a replication before any claim
   scales.

## 4. What is logged where (each run directory)

| File | Content |
| --- | --- |
| `metrics.jsonl` | Per update. Cycle counts: `cycles_total`, `cycles_rest`, `cycles_shop`, `cycle_steps`, `cycles_rejected`, `episodes_with_cycle`, `cycle_max_per_episode`, `cycle_cost_total` (= c × cycles), `reward_sum_trained`. Stream counters: `seed_stream_next`, `seeds_used_total`, `stream_skipped_total`, `stream_failed_total`, `starts_update`, `dropped_total`. Timing: `t_stream_s`, `t_prefetch_s`, `t_prefetch_wait_s`. Plus PPO statistics, progress and the shaping keys |
| `cycles.jsonl` | One row per closed UI cycle: `update`, rollout step `t`, `slot`, `episode`, `phase` (RestSite / Shop), `prompt` (DeckUpgrade / DeckRemove), `act`, `floor`, `len`, the episode's decision index `step`, `path` (e.g. `rest:SMITH > select:STRIKE_SILENT > cancel`), `actions` |
| `episodes.jsonl` | One row per episode: `seed_text` (`SP1-SILENT-<n>`), `n`, `result`, `act`, `floor`, `steps`, `progress`, `return`, `end_kind`, `cycles` (the episode's UI cycles). A resume adds `dropped` rows for the checkpoint's in-flight episodes; failed starts get `start_failed` rows |
| `evals.jsonl` | Periodic monitor rows (silent-a10-v1, greedy, breaker on, no `mode` field). Fixed-endpoint rows: `eval_at` true, `mode` greedy-on / greedy-off, `policy`, `breaker`, `progress_mean`, `stuck`, `cycles`, `games_with_cycle`, `loop_breaks`, `games_with_loop_breaks`, `checkpoint` (`pinned-<N>.pt`) |
| `evals/u<N>/<mode>/` | Protocol run directories. Every `games/*.summary.json` has `eval_mode`, `cycles`, `cycle_events`, `loop_breaks`, `stalled` and `history_actions` |
| `checkpoints/` | `ckpt-<N>.pt` every 25 updates / 15 min (the last 40 kept) and `pinned-<N>.pt` at each `--eval-at` point (never pruned). No `best.pt` |
| `config.json` | `seeds` (init / sample / env 1, `rng_scheme` decoupled, prefix SP1). `seed_stream`. `isolation`: 10 checked sets; `collisions_skipped` is the count at session start, the live count is in `summary.json` / `stream_skipped_total`. `blind_eval` / `blind_eval_at` null. `sessions[]`: `given`, `config_diff`, `rng_restored`, `seed_stream_next`, `dropped_at_start`, `init_weights_hash` |
| `summary.json` | Per session: `cycles` totals, `seed_stream` (+ timing), `isolation`, `best_eval` null |

## 5. Gates passed before the plan (CPU, local, 2026-11-04)

Evidence lives under the session scratchpad `sp2int/gates/` (scripts `g1.sh` … `g7.sh`, results `g*/check.json`).

- Test suites:
  - `sim/tools/train`: 169 passed (137 at `8c9455f` + 14 A + 13 B + 5 integration).
  - `sim/tools/vec`: 125 passed, 1 failed. `test_find_library_discovery` needs a local Release build at the
    worktree's default path; it fails identically on the `8c9455f` sources.
  - `sim/tools/eval`: 80 passed, 4 skipped (no server build).
- **g1** stream run, 3 updates then `--resume auto` to 5:
  - `seed_stream_next` 16 → 18 → 28 | 44 → 50;
  - `sessions[1].seed_stream_next` 28;
  - 16 dropped rows equal ckpt-3's in-flight episodes;
  - consumed indices = range(50), no duplicate; texts `SP1-SILENT-*`.
- **g2** the guard and the stream skip:
  - `--start-mode pool --seed 0 --pool 4608` exits 2 with no run directory: 512 seeds of silent-a10-ab-v1, refused by
    triple at A10 and by run seed at A0;
  - `--pool 4096` passes, with 4,096 seeds checked against 10 sets;
  - in stream mode, injected collisions at n = 2 (triple) and n = 5 (run seed) are skipped: next 21 = started 19 +
    skipped 2, neither started.
- **g3** silent-a10-blind-v1 is refused before any game or file. Exit 2, no output directory:
  - `sts2train eval` (greedy on / off, sample R=3, a slice, `--allow-blind` without `--purpose`);
  - `train --eval-every` and `train --eval-at` (two mode lists);
  - `sts2vec eval`.

  report-card / pack also exit 2; they leave an empty card directory and no `evals/`. The lookahead exits 1 with
  `BlindSetError`.
- **g4** on a copy of u2225 (pool mode as it ran, `--start-saves none`, 64 envs, rollout 64, 4 updates, lr 0):
  - `cycles_total` 0 / 22 / 51 / 58 in both runs (129 rest, 2 shop);
  - the `reward_sum_trained` difference = 0.01 × cycles, residual ≤ 1.3e-8;
  - `cycles.jsonl` identical in both runs;
  - every number bit-identical to owner B's pre-merge run.
- **g5** `sts2train eval` in greedy-off / sample-off R=3 / greedy-on on 8 dev games, then `compare`:
  - per game, `history_actions`, progress and cycles are identical to owner B's dev-128 runs: 40 / 40 plays;
  - progress 0.245 / 0.274 / 0.386; greedy-off stuck 7 of 8.
- **g6** `--eval-at 2,3 --no-best` on a stream smoke, then a fork rehearsal (C0 / C1, `--eval-at 4,5`, cost 0 vs
  0.01):
  - rows carry `mode`; `pinned-<N>.pt` written; no `best.pt`; `best_eval` null;
  - both arms started the same seeds with the same results, and each logged 16 dropped rows.
- **g7** pool mode without any new flag reproduces `8c9455f` bit for bit (3-update smoke, against a `git archive
  8c9455f` run):
  - `weights_hash` `552a16b0b3f09454` in both;
  - 81 shared metric values equal (timing keys excluded).
- **Driver dry run.** `sp2_serial.sh` was extracted from this file and run at toy scale (local stand-ins for
  `run_train.sh` / `python3 -m sts2train.cli`, CPU, tiny model). W0 ran to 3 updates, then the copy fork, then C0 / C1
  to 6 updates, endpoint evals on 4- / 8-game sets and 13 comparisons, in 34 s.
  - The forks' `config_diff` was `out` + `max_updates` + `eval_at`, plus `cycle_cost` for C1.
  - Both arms had the same `seed_stream_next` and logged 16 dropped rows each.
  - A re-run skipped every finished stage and evaluation.
  - The McNemar snippet ran on the outputs.
- Integration fixes in `542a1e4`, beyond the union of the two branches:
  - `--eval-at`'s seed set goes through the blind gate and the isolation guard;
  - on a resume, fixed-endpoint rows are never `best_eval` / `last_eval`;
  - `--no-best` survives the resume recompute;
  - the per-episode cycle count is `episodes.jsonl` `cycles`; the `cycle_episodes.jsonl` shim is dropped;
  - an unknown eval seed set exits 2.

## 6. Not verified before launch

- CUDA paths of the new code:
  - the decoupled CUDA sampling seed and `rng_restored` cuda;
  - the stream prefetch thread next to a GPU update;
  - `--eval-at` wall time on the A100.
- The NativeAOT `.so` under the stream mode (`restore_many`, `pool.release`, `pool.warm` from a thread). The library
  exports these symbols (checked in its symbol strings), but only the managed DLL ran them locally.
- Lookahead sizing at 2,048 envs: auto 4,096 live templates, adaptive growth. `episodes.jsonl` / `cycles.jsonl` growth
  at scale: about 0.1–0.3 MB per update.
- The cycle rate of a random-init policy over the first 50 M steps is unknown. It decides whether C0 is under-triggered
  (rule 4).
- Bundle note: built with the given command, so it carries `saves/` (148 human A9 saves, 11.2 MB). They are inert here:
  the stream mode refuses `--start-saves`, and nothing in this plan names them. A bundle without them is the same
  command minus `SAVES_DIR`.
