# W3M overnight plan (2026-11-04 night): two A100 lines forked from the main line M at u3064

Status: **plan, pre-registered before any W3M number exists.** Code: branch `train/w3m` = `00fc102` (train/w3) +
merge of `train/w3-macro` (owner D's room macros, merge `71ef93e`) + the drivers in `scripts/colab/w3m_*`. Bundle
`sts2train-413f510188.tar.gz` (§7). Owner goal served: THE SILENT A10, dev-128 greedy-off mean progress ≥ 0.60 and
clear rate ≥ 0.10. Today's best ≈ 0.31 (self-play v2 C1 at u764: 0.310 vs C0 0.287, paired +0.023 [+0.002, +0.043],
0 vs 9 dev-128 games with a UI cycle); M continues C1 from u764 to u3064.

## 中文摘要

- **分叉点**：主线 M（GPU A，`/content/sts2/runs/sp3-main`，v2 配方 + `--cycle-cost 0.01`，无 overlap）约 00:15 EST
  到达 u3064，写出 `checkpoints/pinned-0003064.pt`。两条夜间线都从**这一个** checkpoint 分叉（复制成新 run 目录的
  `checkpoints/ckpt-0003064.pt`，`--resume auto` 接着训；`config_diff` 记录所有改动）。
- **A 线（GPU A，sts2train）**：M 的配方 + 房间宏动作 `--macros rest,shop` + 熵退火 0.02→0.005（4,600 次更新，从
  u3064 起算）+ `--eval-at-every 100 --pin-every 100 --keep-ckpts 60`，`--overlap off`（只有可选预检
  `w3m_preflight_overlap.sh` 三条全过——r_inf ≤ 1.4、double 总时长更短、compile 检查 OK——才可用 double，开跑前定死）。
  每段 500 次更新（u3564, 4064, …, 7564，最后一段到 7664；共 +4,600 次 ≈ 6.03 亿环境步），每段后对该段 pinned
  checkpoint 跑 ab-512 greedy-off。
- **B 线（GPU B，sts2b）**：A 的配方 + harvest（boss:1536, elite:512, act2:1024，prob 0.25，share 0.33），
  `--overlap off`（必须）。同一 u3064 checkpoint（从 GCS 取），同样的分段和评测。复验 R 约 22:30 结束，但分叉点
  00:15 才出现：B 的驱动可以提前启动，会自己等 checkpoint；GPU B 的空档（22:30–00:15）正好用来跑 overlap 预检。
- **比较**：每个 500 点做 B−A 配对比较：dev-128 greedy-off 为主，ab-512 greedy-off 确认；blind-256 不碰。
  宏动作是否保留：看 A 自己的固定评测与 M 最后两个点（u2764, u3064）的比较；**若 A 的头两个点（u3564, u4064）
  dev-128 greedy-off 都比 M u3064 低超过配对 CI 半宽（即配对 95% t CI 上界 < 0），停 A 和 B，改跑不带宏的 A0/B0**
  （同一 u3064 分叉，`MACROS=none`）。B 的伤害停止：从 u4064 起连续两个比较点 B−A 的 CI 全在 0 以下则停 B。
  正式结论只看 u7664。
- **目标检查**：每条线 u7664 dev-128 greedy-off progress ≥ 0.60 且 clear ≥ 0.10——这是目标，不是承诺；达标再用
  ab-512 确认。
- **时间与费用**：每条线约 11.6–12.6 h（训练 4,600 × 7.5 s ≈ 9.6 h + 评测与会话开销），A 约 11:45 EST 结束，B 约
  12:00–13:00 EST；两卡合计约 165–180 units（6.73/h），在 ~300 的预估和 400 的上限之内。
- **未在 GPU 上验证**：宏动作在 CUDA/compile 下的整条路径、harvest 在 2048 环境下的成本、double 在 A100 上的收益、
  v2 代码的 u3064 checkpoint 在本包上的 CUDA 续训。CPU 门禁全部通过（§6）。

## 1. Fork point

- Source: `/content/sts2/runs/sp3-main/checkpoints/pinned-0003064.pt` (M, written by its `--eval-at 3064` at about
  00:15 EST; M's session ends there because of `--max-updates 3064`). M's dev-128 eval rows at u2764 and u3064
  (`evals/u0002764/greedy-off`, `evals/u0003064/greedy-off`) are the macro-adoption baseline.
- Each line copies it once into `RUN_DIR/checkpoints/ckpt-0003064.pt` (`w3m_line.sh`; tmp + rename, never
  overwritten; `RUN_DIR/fork.sha256` records both hashes, which must be equal across the two lines). The driver checks
  that the checkpoint's `updates` is 3064 before copying.
- Line A (VM A) waits until the file exists **and** no process has `--out /content/sts2/runs/sp3-main` on its command
  line (M's trainer has exited; same GPU). Line B (VM B) first downloads the file from the bucket (`w3m_fetch.py`
  waits until `runs/sp3-main/checkpoints/pinned-0003064.pt` exists with a stable size).
- First-session checks (`config.json` `sessions[0]`): `model_inputs` identical (no widening; macros change neither the
  spec nor `encoder_version` 3b73317739c650bd), `rng_restored` ok for numpy / torch / cuda, `seed_stream_next` = M's
  final counter (both lines: common random numbers), `dropped_at_start` 2048. `config_diff` lists `macros ["",
  "rest,shop"]`, `ent_coef_final`, `ent_anneal_updates`, `ent_anneal_start`, `eval_at_every`, `pin_every`, `eval_at`,
  `eval_every` (25 → 100), `out`, `max_updates`, `max_hours`; B also `harvest`, `harvest_start_share` (`harvest_prob`
  0.25 is the default). The u3064 checkpoint comes from the v2 code (542a1e4eb8), so keys added in W3 also show up at
  their defaults (expected; seen in the w3 fork gate). Later sessions differ only in `max_updates`.
- Behaviour shift after the switch: the learned SMITH row meant "open the prompt"; with macros the rest-site rows mean
  "upgrade this card" (one row per upgradable card, card embedding attached), the shop removal rows "remove this
  card". A transient is expected; the revert rule (§5.1) bounds its cost.

## 2. Recipes (exactly what `scripts/colab/w3m_line.sh` passes)

```bash
FLAGS=(--start-mode stream --seed-stream-prefix SP1 --env-seed 1 --init-seed 1 --sample-seed 1
       --characters SILENT --ascension 10 --start-saves none
       --objective protocol --win-bonus 1.0 --acq-shaping --acq-deck-coef 0.003 --acq-relic-coef 0.01
       --acq-upgrade-coef 0.012
       --d-model 192 --state-dim 384 --depth 3 --heads 6 --ff-mult 4 --deck-counts --aux-heads --aux-coef 0.1
       --rollout 64 --epochs 1 --minibatch 4096 --lr 3e-4 --gamma 1.0 --lam 0.99 --clip 0.2 --vf-coef 0.5
       --ent-coef 0.02 --max-grad-norm 0.5 --max-rows 64 --step-cap 3000 --floor-step-cap 80
       --device cuda --amp --compile default
       --no-best --eval-at-seedset silent-a10-dev-v1 --eval-at-modes greedy-on,greedy-off
       --eval-every 100 --eval-seedset silent-a10-v1 --loop-breaker on
       --ckpt-every 25 --ckpt-every-min 15 --keep-ckpts 60 --max-hours 3 --log-every 1
       --cycle-cost 0.01                                        # as M (C1)
       --macros rest,shop                                       # NEW: room macros (MACROS=none for A0 / B0)
       --ent-coef-final 0.005 --ent-anneal-updates 4600 --ent-anneal-start 3064    # NEW
       --eval-at-every 100 --pin-every 100                      # NEW
       --eval-at 3564,4064,4564,5064,5564,6064,6564,7064,7564,7664)
A: "${FLAGS[@]}" --overlap off            # or double, only after the preflight passed (§3)
B: "${FLAGS[@]}" --overlap off --harvest boss:1536,elite:512,act2:1024 --harvest-prob 0.25 --harvest-start-share 0.33
```

- `run_train.sh RUN 12 2048 ...` adds `--out RUN --resume auto --threads 12 --envs 2048`.
- Entropy at update u: 0.02 + (0.005 − 0.02) · clamp((u − 1 − 3064) / 4600, 0, 1): 0.02 at u3065, ≈ 0.005 at u7664.
  The start is explicit, so every segment and reconnect computes the same schedule.
- Segments: `--max-updates` 3564, 4064, …, 7564, 7664 (9 × 500 + 1 × 100 = 4,600 updates; × 2048 × 64 =
  602,931,200 env steps). Each end is an `--eval-at` point (dev-128 greedy-on / greedy-off rows + `pinned-<N>.pt`);
  `--eval-at-every 100` adds rows and pins at u3100, u3200, …, u7600. After each segment the driver runs
  `sts2train eval --seedset silent-a10-ab-v1 --checkpoint pinned-<END>.pt --policy greedy --breaker off --macros
  rest,shop` into `RUN/evals/endpoint/u<END>-ab512-greedy-off` (under `evals/`, so `gcs_sync.py` mirrors it). At u7664
  also ab-512 greedy-on and dev-128 sample-off R=3.
- Idempotent: a segment already reached (`status.json` update) is skipped, at most 4 sessions per segment; a finished
  eval (`report.json` / `repeats.json`) is skipped, an interrupted one redone. Re-running the same command after a
  disconnect / VM restart continues (after `gcs_pull.py --newest-ckpt-only` restored RUN onto a new VM).
- `eval` passes `--macros rest,shop` explicitly (the default would be the checkpoint's `config.macros`, the same).
  Progress on dev / ab seeds stays comparable with M's macro-free rows (same game, seeds, protocol limits); action
  sequences do not (replays need the same macros).

## 3. Optional: overlap preflight (decides A's `--overlap`)

Run on an idle A100 before line A's first session: GPU B in its 22:30–00:15 gap is the natural slot (it needs any
checkpoint of the architecture, e.g. M's `pinned-0002764.pt` fetched with `w3m_fetch.py --blob
runs/sp3-main/checkpoints/pinned-0002764.pt --dest /content/sts2/fork/pinned-0002764.pt`).

```bash
bash $SRC/scripts/colab/w3m_preflight_overlap.sh /content/sts2/sts2train-413f510188 $SRC \
    /content/sts2/fork/pinned-0002764.pt /content/sts2/preflight-overlap
# = python3 $SRC/sim/tools/train/bench/overlap_bench.py --device cuda --envs 2048 --rollout 64 --updates 6 \
#       --arch 192,384,3,6,4 --threads 10 --torch-threads 2 --minibatch 4096 --epochs 1 --repeats 2 --out OUT/bench
#   then 3 updates of the real line-A recipe with --overlap double from a copy of the checkpoint (EVALS=0)
```

Pass rule (fixed now): **OVERLAP=double iff** bench `measured.infer_ratio` r_inf ≤ 1.4, **and** `total_speedup` > 1
(double's `t_total_s` below off's), **and** `train.log` shows `compiled rollout forward vs eager: … OK` with
`t_overlap_wait_s` in `metrics.jsonl`. Otherwise off. Written to `OUT/decision.txt`; decided once before line A's
first session, never changed within the line. The bench uses the line's architecture (`--arch 192,384,3,6,4`; the w3
plan's quick form was `--preset small`; `ARCH=` overrides). The wheels must be installed first (any `run_train.sh`
call does it). B always runs off (harvest + double is refused); in stream mode off and double sample the same policy
distribution and comparisons are at matched updates (= matched env steps), so A double / B off stays a fair pair (w3
plan §3.3). If the preflight is not run, A runs off.

## 4. Launch commands

Shared (each VM): bundle unpacked at `/content/sts2/sts2train-413f510188`; `SRC=/content/sts2/w3m-src` = `git
archive` of `train/w3m` (`scripts/colab`, `sim/tools/train/bench`; `tmp/colab/w3m-src-<sha>.tar.gz` in the worktree);
`gcs_sync.py`, `gcs-sts2-ckpt.json` in `/content/sts2` as today.

```bash
# VM A (GPU A, sts2train): start any time before 00:15; it waits for M's u3064 pin and for M's trainer to exit
setsid nohup bash $SRC/scripts/colab/w3m_line.sh /content/sts2/sts2train-413f510188 /content/sts2/runs/w3m-a a \
    /content/sts2/runs/sp3-main/checkpoints/pinned-0003064.pt off > /content/sts2/runs/w3m-a.driver.log 2>&1 < /dev/null &

# VM B (GPU B, sts2b): after the replication R has finished (~22:30); waits for the fork checkpoint in the bucket
setsid nohup bash -c 'python3 $0/scripts/colab/w3m_fetch.py --key /content/sts2/gcs-sts2-ckpt.json \
      --bucket gomoku-zero-20260816-ai-sts2train --blob runs/sp3-main/checkpoints/pinned-0003064.pt \
      --dest /content/sts2/fork/pinned-0003064.pt \
    && bash $0/scripts/colab/w3m_line.sh /content/sts2/sts2train-413f510188 /content/sts2/runs/w3m-b b \
      /content/sts2/fork/pinned-0003064.pt' $SRC > /content/sts2/runs/w3m-b.driver.log 2>&1 < /dev/null &
```

- Each driver starts one `gcs_sync.py --run RUN --interval 60 --min-age 60` (unless one runs for that RUN). Mac side
  unchanged: `ping_loop.sh SESSION 300` per runtime, `mirror_loop.sh` (no checkpoints).
- After the first session started: both `RUN/fork.sha256` show the same hash; the §1 first-session checks hold.

## 5. Comparisons and rules (pre-registered)

At each point N ∈ {3564, 4064, …, 7564, 7664} once both lines have it, on VM B (never on the Mac):

```bash
bash $SRC/scripts/colab/w3m_compare.sh N      # pulls M / A / B eval dirs (no .pt) from GCS, writes runs/cmp/*
# = sts2train compare A/evals/uN/greedy-off B/evals/uN/greedy-off --labels A,B   (PRIMARY, dev-128)
#   sts2train compare A/evals/uN/greedy-on  B/evals/uN/greedy-on  --labels A,B
#   sts2train compare A/evals/endpoint/uN-ab512-greedy-off B/... --labels A,B     (CONFIRMATORY, ab-512)
#   sts2train compare M/evals/u0003064/greedy-off A/evals/uN/greedy-off --labels M3064,A   (also M2764, and B vs M)
```

1. **Macro adoption / stop-and-revert (A vs M).** Macros stay unless at **both** u3564 and u4064 A's dev-128
   greedy-off is below M's u3064 greedy-off by more than the paired CI half-width, i.e. the paired 95 % t CI of
   A(uN) − M(u3064) lies entirely below 0 at both points. Then stop both drivers and trainers and start **A0** /
   **B0**: the same commands with `MACROS=none`, run dirs `w3m-a0` / `w3m-b0`, forked again from M's u3064 (so B0 − A0
   keeps the harvest question clean). A(uN) − M(u2764) is reported as context. UI-cycle plays (`cyc-plays`) of A / B
   should be ≈ 0.
2. **Harm stop (B only).** From u4064 on, two consecutive points with the dev-128 greedy-off B − A paired 95 % t CI
   entirely below 0: stop B, report "harvest hurts (interim)"; A continues.
3. **Official B − A at u7664** (`pinned-0007664.pt` of each line): "harvest helps" iff the dev-128 greedy-off CI is
   entirely above 0 and the ab-512 greedy-off difference has the same sign (CI not entirely below 0); "hurts" iff the
   dev-128 CI is entirely below 0 and ab-512 points the same way; otherwise "no detectable difference at +600 M
   steps". Earlier points are trajectory only (they can trigger rules 1–2, never a "helps" claim). Harvest stays
   exploratory (v2 rule 7: a positive claim needs an independent-seed replication).
4. **Goal check (target, not a promise).** Per line at u7664, dev-128 greedy-off: progress ≥ 0.60 and clear rate ≥
   0.10; if met, confirm on ab-512 greedy-off. **blind-256 (`silent-a10-blind-v1`) is not evaluated**; a goal claim on
   it needs a separate, user-approved `--allow-blind --purpose` run of the one delivered checkpoint.
5. **Engineering stop (immediate, either line):** NaN / inf loss; `sim_error` or `content_gap` > 0 on 3 consecutive
   updates (a macro divergence fails closed as SIM_ERROR); `stream_failed_total` growing; B `anchor_start_failed` > 1 %
   of `anchor_starts_update` over 10 updates; an isolation error; an eval-at row with `error`. Resume after a fix with
   unchanged flags.
6. No other early stop; a plateau does not stop a line. Training-side numbers (`harvest_*`, training progress) are
   diagnostics; only eval rows compare.

## 6. Gates passed (raven testbox, CPU; commits 71ef93e, 413f510, c70531c)

- Full suites on the merge: train 212, vec 129, eval 84 passed; C# `Macro` filter 7/7 (incl. the 32 × 4000-step
  soak); gates.sh quick filters (`Server Vec Parity EndToEnd SelfConsistency W1Gate`) 146/146.
- Default path bit for bit vs `00fc102` (3 updates, macros off): pool `weights_hash` 6fde2adf17381229, stream
  ffe9ba603d0b52a5 in both; every metric equal except the wall-clock `env_steps_per_s_session`.
- Macro smoke (stream, smoke preset, 6 updates, `--macros rest,shop --cycle-cost 0.01`): `cycles_rest` = `cycles_shop`
  = 0 (macro-free control: 1 shop cycle); chosen macro rows counted by a step probe: 2 × `SMITH:<card>`
  (STRIKE_SILENT, DEFEND_SILENT), 6 × `REMOVE:<card>`; `env.macros` recorded.
- Resume of a macro-free stream checkpoint with `--macros rest,shop`: `model_inputs` identical, no widening,
  `config_diff` = macros + out + max_updates, rng ok, trains to u6 (macro rows chosen after the switch).
- Harvest + macros (stream, tiny rings): 75 anchor starts, 0 failed, 0 rest / shop cycles, 0 sim errors. Entropy
  schedule + `--eval-at-every 2 --pin-every 2` + macros: ent 0.02 → 0.00875 linear, pinned u2 / u4, eval rows with
  `limits.macros [rest, shop]`. `--overlap double` + macros: `env.macros` recorded (integration fix in `VecPair`).
- Driver dry run (CPU stand-ins for `run_train.sh` / CUDA, the real architecture, fork at u3, segments 5 / 7): both
  lines fork, resume, pin, run the endpoint evals; a re-run skips everything; `w3m_compare.sh` runs on the outputs and
  prints the rule lines; `MACROS=none` gives a macro-free line.

## 7. Bundle

`tmp/colab/sts2train-413f510188.tar.gz` in the `train-w3m` worktree: 7,228,467 bytes, sha256
`be79e6bff4e11e613dfc36a53b51351c4c122d017adad32824924eb1ce2378ef`; built from `413f510` with owner D's cached
NativeAOT library (key `7aaa846a6a35`, no rebuild; the key computed from this tree is the same). Manifest `aot_key`
`7aaa846a6a35`, GLIBC_2.34, 148 saves (inert in stream mode), all 10 seed sets; wheels `sts2vec-0.1.0` (with
`parse_macros`) and `sts2train_smoke-0.2.0` (with `harvest.py`, `overlap.py`, `seedstream.py`, `cycles.py` incl. the
`:card` label fix). `run_train.sh` passes `--resume auto`. Later commits on `train/w3m` change only `scripts/colab/`
and `docs/` (not in the bundle): copy `scripts/colab/` and `sim/tools/train/bench/` as `SRC`.

## 8. Wall time and units (6.73 units per A100 hour)

| Item (per line) | A (off) | B (off + harvest) |
| --- | --- | --- |
| Training, 4,600 updates at ~7.5 s | 9.6 h | 9.6–10.6 h (harvest cost 0–10 %, unmeasured at 2048 envs) |
| `--eval-at(-every)`, 46 points × 2 modes on dev-128 | 0.8 h | 0.8 h |
| Monitor every 100 (46 × silent-a10-v1) | 0.2 h | 0.2 h |
| ab-512 greedy-off × 10 + final ab-512 greedy-on + dev-128 sample R=3 | 0.5 h | 0.5 h |
| Session starts (10 segments, ~3 min each) | 0.5 h | 0.5 h |
| **Total** | **≈11.6 h ≈ 78 units** | **≈11.6–12.6 h ≈ 78–85 units** |

Plus the preflight (0.2 h) and GPU B's wait 22:30–00:15 if the VM is kept (≈12 units). Night total ≈ 165–180 units,
inside the ~300-unit envelope and the 400-unit ceiling. A ends ≈ 11:45 EST, B ≈ 12:00–13:00 EST (a double A saves
≈ 1.5–2 h if the preflight passes). A revert at u4064 costs ≈ 2 × 2.6 h of the night.

## 9. Not verified

- Anything on CUDA with the new code: macros under `--compile default` / bf16 at 2048 envs, macro throughput, harvest
  cost at 2048 envs with ≈3k live anchors, the double gain on the A100 (preflight).
- The fork itself: M's u3064 checkpoint (v2 code, bundle 542a1e4eb8) resuming into this bundle on CUDA (the CPU gate
  resumed a w3m checkpoint without macros; the w3 fork gate resumed a 073a32b checkpoint into w3 code).
- `w3m_fetch.py` and the GCS paths (no bucket access from the testbox); the `pgrep` wait on M's trainer on a real VM;
  the preflight script end to end (needs CUDA).
- The real-game twin and the combat lookahead have no macro mapping (a macro-trained model plays them on raw rows).
