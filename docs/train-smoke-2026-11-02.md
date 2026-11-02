# Train smoke on sts2vec — entity-encoder PPO + the NN protocol-eval entry point (E3, 2026-11-02)

Status: **smoke passed**. Learning code drives the batched interface end to end (schema → numpy → torch → candidate
logits → `step` → auto-reset → protocol metric) for 52 minutes with 0 SIM_ERROR / 0 CONTENT_GAP / 0 illegal actions,
and protocol progress rises from the random level to about 3.3×. The protocol's missing NN entry point exists and
writes the same `report.md` as the LLM runner. Code: `sim/tools/train` (uv project `sts2train-smoke`, README there).
Outputs (`tmp/train-smoke/`, not committed): `run1/curve.csv`, `run1/updates.csv`, `run1/curve.png`, `run1/summary.json`,
`run1/checkpoint.pt`, `nn-run1-a0-s20/` (eval run dir + `report.md`).

## 1. Setup

| | |
| --- | --- |
| Machine | Apple M4 Pro, 24 GB; load average 4–8 from other agents during the run |
| Library | `Sts2Sim.Vec` Release (hostfxr), `encoder_version` `fc2eb34c07bc8513` (this change: 4 aux columns, §4) |
| Env | `VecEnv(1024, threads=8, step_cap=3000, auto_reset=True)`, pool 2,048 fresh A0 entries (all five characters, seeds `TRAIN0-<char>-<i>`), warmed in 5.3 s |
| Model | entity encoder + pointer candidate scorer + value head, **948,750 parameters** (d = 96, s = 256), on MPS |
| PPO | 1,024 envs × rollout 32 = 32,768 samples / update, 2 epochs × minibatch 4,096, Adam 3e-4, clip 0.2, vf 0.5, entropy 0.01, γ = 1, λ = 0.95, advantage normalisation, grad-norm 0.5; actions limited to the first 64 candidates (never exceeded: `cand_over_max_rows` = 0) |
| Objective | `--shaped` (**smoke-only**): win/loss reward + Δ`total_floor` / 48 per step. The default config is pure win/loss; progress is logged only |
| Wall | 52.05 min of training (+ 5.3 s warm, 6.8 s random baseline); 620 updates |

## 2. Throughput (learner in the loop)

| metric | value |
| --- | --- |
| env-steps total | 20,316,160 |
| **env-steps/s with the learner in the loop** | **6,505** (5-min windows 6,304–6,701) |
| env-steps/s inside `env.step` only | 134,475 |
| time split of 3,123 s | env 151.1 s (4.8 %), policy inference 682.1 s (21.8 %), PPO update 2,286.3 s (73.2 %), other < 0.1 % |
| per update | rollout ≈ 1.35 s (env ≈ 0.25 s + inference ≈ 1.1 s) + update ≈ 3.6 s |
| model fwd at N = 1024 | MPS ≈ 26 ms, CPU (4 threads) ≈ 63 ms; fwd + bwd MPS ≈ 55 ms, CPU ≈ 139 ms |
| rollout store | 643 MiB (32 × 1024 × used buffers, 64 candidate / prompt rows) |

The simulator is not the bottleneck: the learner is ≈ 95 % of wall. Next steps there are learner-side (smaller first
layers on the wide tables, bf16 on MPS/CUDA, one epoch, or env/learner pipelining with two half-size envs; a CUDA box
is the obvious move for real training).

## 3. Learning curve (5-minute windows, finished episodes)

Random baselines: uniform-random policy on the same training env, 600 steps × 1,024: **progress 0.0769**, floor 4.92
(8,517 episodes). The protocol's uniform-random row on `std-a0-v1` is 0.065.

| window | t (min) | episodes | floor mean | **progress mean** | median | act reached | steps/episode | entropy | SIM_ERROR / CONTENT_GAP |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0 | 5.05 | 14,925 | 11.02 | **0.197** | 0.196 | 1: 14,818, 2: 107 | 130.3 | 0.731 | 0 / 0 |
| 1 | 10.07 | 11,908 | 13.38 | **0.243** | 0.275 | 1: 11,695, 2: 213 | 160.8 | 0.497 | 0 / 0 |
| 2 | 15.15 | 11,807 | 13.57 | **0.246** | 0.275 | 1: 11,682, 2: 125 | 169.5 | 0.454 | 0 / 0 |
| 3 | 20.23 | 10,628 | 13.81 | **0.251** | 0.275 | 1: 10,481, 2: 147 | 186.2 | 0.438 | 0 / 0 |
| 4 | 25.28 | 10,136 | 14.14 | **0.258** | 0.275 | 1: 9,977, 2: 159 | 193.6 | 0.463 | 0 / 0 |
| 5 | 30.31 | 9,585 | 14.31 | **0.261** | 0.314 | 1: 9,405, 2: 180 | 195.4 | 0.388 | 0 / 0 |
| 6 | 35.34 | 5,429 | 14.13 | **0.258** | 0.275 | 1: 5,326, 2: 103 | 217.6 | 0.403 | 0 / 0 |
| 7 | 40.38 | 1,649 | 13.29 | **0.241** | 0.275 | 1: 1,621, 2: 28 | 1,039.2 | 0.553 | 0 / 0 |
| 8 | 45.42 | 2,729 | 12.92 | **0.234** | 0.255 | 1: 2,707, 2: 22 | 857.5 | 0.703 | 0 / 0 |
| 9 | 50.49 | 3,334 | 13.72 | **0.249** | 0.275 | 1: 3,276, 2: 57, 3: 1 | 528.4 | 0.692 | 0 / 0 |
| 10 (1.6 min) | 52.05 | 1,060 | 14.94 | **0.274** | 0.314 | 1: 999, 2: 61 | 892.0 | 0.684 | 0 / 0 |

(Per-update rows: `run1/updates.csv`.) Totals: 83,190 finished
episodes, 0 wins, 0 SIM_ERROR, 0 CONTENT_GAP, 0 ILLEGAL_ACTION.

**Does progress improve?** Yes: 0.077 (random) → 0.197 in the first 5 minutes → 0.24–0.27 from 10 minutes on (floor
4.9 → 13–15, i.e. the policy reaches the act-1 boss region and ≈ 1–2 % of episodes reach act 2). It plateaus after
≈ 10 minutes, and from ≈ 33 minutes finished episodes per window drop 9,585 → 1,649 while steps/episode climb to
500–1,040: the policy learned a **non-progress loop** that the shaped objective does not punish (γ = 1, no per-step cost,
step_cap 3000). The loop seen in eval: at a merchant, `BuyEntry` (card removal) → `PromptSelect` → `PromptCancel` →
`BuyEntry` … forever. Fixes for real training, not done here: a floor_step_cap / stall truncation in the training env
(the protocol's 80 non-combat steps per floor), a tiny per-step cost, or masking `PromptCancel` after a repeated
cancel.

Default (pure win/loss) config sanity check, 5 updates (163,840 steps): runs, reward sum 0 (no wins), 0 errors —
nothing to learn from inside a smoke budget, as expected; its progress drift (0.02 → 0.12 over 5 updates) is the
early-episode selection bias of a fresh auto-reset batch plus the entropy term, not reward signal.

## 4. NN protocol entry point (`sts2train-smoke eval`)

```bash
uv run --project sim/tools/train sts2train-smoke eval --seedset sim/tools/eval/seedsets/a0-s20.json \
    --checkpoint tmp/train-smoke/run1/checkpoint.pt --out tmp/train-smoke/nn-run1-a0-s20
```

One fresh pool entry and one env slot per game (`pool.add_fresh(character, seed, ascension, content)`),
`auto_reset=false`, greedy argmax over all candidate rows, protocol limits (`step_cap` 1200 → `capped`,
`floor_step_cap` 80 non-combat decisions on one floor → `stuck`). Each game's summary carries the protocol fields
(`progress`, `rooms_reached`, `rooms_cleared`, `act_rooms_current`, `act_rooms_source`, `act_start_floor`, `act_rooms`,
`progress_pct`, …) computed by `sts2eval.progress.compute`; `config.json` records `server_version`
`sts2vec/<encoder_version>` and `prompt_hash` `weights:<sha256[:16] of the weights>` (the protocol's NN identity, §5).
`report.md` / `report.json` are written by `sts2eval.report`; `sts2eval report DIR` on a copy of the run dir gives a
byte-identical report body (checked). Audit: every game's candidate-index history is replayed on a fresh env and must
reproduce result / act / floor (`replayable`: 20/20).

`std-a0-v1` (A0, 20 games), final checkpoint (`weights` `94148fbbef0bcb1a`, 620 updates):

| metric | value |
| --- | --- |
| **progress v0.2** (mean [95% CI], median) | **0.277** [0.225, 0.329], 0.255 |
| **win rate** (Wilson 95%) | **0.0%** [0.0%, 16.1%] |
| results | loss 14, stuck 6 (all six at a merchant removal prompt loop) |
| floor mean / median / max | 15.1 / 14.0 / 29 |
| act reached | act 1: 17, act 2: 3 |
| steps mean (total) | 226.8 (4,535) |
| illegal / crashes / capped | 0 / 0 / 0 |
| replayable histories | 20/20 |
| wall | 0.04 min for all 20 games (CPU) |

Per character: NECROBINDER 0.331, DEFECT 0.315, SILENT 0.307, IRONCLAD 0.230, REGENT 0.201. For scale on the same
seed set: uniform random 0.065, the untrained network (greedy, random init) 0.170, Claude Sonnet 5.5 0.499.
Not a leaderboard row: the training objective is the smoke-only shaping and `repo_sha` is `-dirty` at eval time.

### aux fields the protocol metric needed (added)

The protocol scores a game from its final (act, floor) **plus** the floor at which the act was first observed and the
act's room count L (the act map's last row + 1). The aux block had `total_floor` and `act_index` only, and with
auto-reset the finished episode's map is gone by the time a learner sees `last_episode`. Appended to `aux` (order
published in the schema; earlier columns unchanged; nothing else bumped): `act_floor`, `map_row`, `map_col`,
`act_rooms`. The act start floor is `total_floor − act_floor` (act 1: 0), verified against the first observation of
every act over 10,229 states (`VecEncoderTests.Test_VecEncoder_AuxProgressColumns`). `encoder_version`
`dff07a6bc984b119` → `fc2eb34c07bc8513`, 26,710 → 26,726 bytes/env. `ProgressTracker` keeps each slot's aux from the
previous call and scores the finished episode from it plus `last_episode`.

## 5. Gates

- `dotnet build sim/Sts2Sim.sln -c Release`: 0 warnings, 0 errors.
- `dotnet run -c Release --project sim/Sts2Sim.Tests -- Vec`: 38 passed (new `Test_VecEncoder_AuxProgressColumns`);
  full suite 1,115 passed, 0 failed (444 s).
- `uv run --project sim/tools/eval pytest -q sim/tools/eval`: 67 passed (unchanged; that project has no scoping plugin,
  so without the path it also collects the vec/train tests and fails on their imports, as before this change).
- `uv run --project sim/tools/vec pytest -q`: 66 passed (`bytes_per_env` 26,726).
- `uv run --project sim/tools/train pytest -q`: 8 passed — model forward on a real env step (greedy actions legal),
  candidate masking (rows ≥ cand_n get −1e9 and zero probability; training row cap), pointer wiring changes logits,
  stored log-probs reproduce and the PPO loss is finite with finite gradients, progress tracker = `sts2eval.progress`,
  parameter budget < 2M, eval entry point writes a protocol run dir + report with 2/2 replayable games.
- Both packages' pytest plugins now step aside when the interpreter belongs to another uv project's environment, so
  `uv run --project sim/tools/train pytest -q` from the repo root collects `sim/tools/train` (not `sim/tools/vec`).

## 6. Caveats

- Shaped objective is smoke-only; it rewards floor count, not winning, and it allows stall loops (§3).
- One seed, one run; no hyper-parameter search; ± numbers not estimated.
- Training pool seeds (`TRAIN0-*`) are disjoint from the protocol seeds (`E1A0*`), but there are only 2,048 fixed
  starts.
- Timing shares were measured with other agents' load on the machine (load average 4–8).
