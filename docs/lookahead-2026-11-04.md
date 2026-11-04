# Decision-time combat lookahead for the Silent policy (2026-11-04)

Status: **built and measured; the ExIt go signal did not fire.** `sts2train.lookahead` gives a trained checkpoint an
honest, declared search-augmented evaluation row: an ordinary `sts2vec` policy, played, scored and replay-audited by
the unmodified protocol evaluator, that searches combat decisions on determinized clones (the player's view, with the
draw order and every RNG stream resampled; never the true hidden state). On the A10 dev set (`silent-a10-dev-v1`, 128
games) the best configuration (turn horizon, `value+hp` leaf score, K = 8, R = 1) adds **+0.0152 progress** to the
greedy A10 checkpoint: t 95 % CI [+0.0080, +0.0223], bootstrap [+0.0084, +0.0227], 33 better / 8 worse / 87 same. The
bar was Δ ≥ +0.02 with the CI excluding 0: the CI excludes 0, the effect is below the bar. On the held-out
`silent-a10-test-v1` (100) the same configuration gives +0.0057 with the CI including 0 ([−0.0011, +0.0124]).
Acceptance holds: 128/128 replayable for greedy and every lookahead run, 0 desyncs, 0 rollout errors, identical
re-run, K = 1 identical to greedy, 7.1 min (turn) and 23.0 min (combat) for 128 games on 8 raven threads. On the
current integration head (ABI 2 `env.branch` path, rewards-screen macros) the dev result is the same: +0.0159
[+0.0087, +0.0232].

Checkpoints (copied from the Colab runs, encoder `fc2eb34c07bc8513`, large preset, 15.8M parameters): A10 = `s1b` best
(update 100, weights `923921aa008db238`), A0 = `s1a` best (update 75, weights `5870d8bc9174fca0`). Both greedy rows
reproduce their Colab evals exactly on the current library (0.1578431 on `silent-a10-v1`, 0.25784295 on `silent-a0-v1`).
Library: `b8533a3` (encoder `5dbfa09dd9dcb5ed`; raven uses the bundle `sts2train-0b1bbd3a4e`, sha256 `c754754f…`).

## 1. Results

All rows: protocol v0.2 progress, paired per seed against the greedy row of the same machine and code (`python -m
sts2train.lookahead paired`); t interval = Student t on the per-seed differences, bootstrap = 10,000 resamples of the
mean (seed 0). "changed" = share of searched decisions where the search overrode the greedy choice. Raven: 8 env and
torch threads, python:3.13 container, torch 2.15.0+cpu.

### 1.1 A10 checkpoint, `silent-a10-dev-v1` (128 games), raven

Greedy: 0.1556 [0.145, 0.167], 123 loss / 5 stuck, floor 8.94, 128/128 replayable, 125 s.

| config (K = 8) | progress | Δ | t 95 % CI | bootstrap 95 % CI | better / worse / same | changed | ms / searched decision | wall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| **turn, value+hp** | **0.1708** | **+0.0152** | **[+0.0080, +0.0223]** | **[+0.0084, +0.0227]** | 33 / 8 / 87 | 16.2 % | 25.0 | 426 s |
| turn, value | 0.1575 | +0.0018 | [−0.0068, +0.0104] | [−0.0066, +0.0106] | 23 / 24 / 81 | 19.2 % | 25.4 | 417 s |
| combat, value+hp | 0.1679 | +0.0123 | [+0.0046, +0.0199] | [+0.0049, +0.0201] | 33 / 7 / 88 | 11.9 % | 100.3 | 1,380 s |
| combat, value | 0.1633 | +0.0077 | [+0.0005, +0.0148] | [+0.0006, +0.0149] | 27 / 11 / 90 | 12.3 % | 153.7 | 2,023 s * |
| turn, value+hp, **R = 2** | 0.1705 | +0.0149 | [+0.0082, +0.0215] | [+0.0086, +0.0219] | 37 / 8 / 83 | 16.7 % | 44.3 | 686 s |

Every row: 128/128 replayable, 0 desyncs, 0 rollout errors, no rebuild. Re-run of turn value+hp: identical histories
and identical search counters (424 s). K = 1: identical histories to greedy. * this run overlapped external load on
the shared host (2.14 → 3.41 ms per forwarded row at the same work); see §3.

### 1.2 Held-out: A10 checkpoint, `silent-a10-test-v1` (100 games), raven

Greedy: 0.1620 [0.148, 0.176], 96 loss / 4 stuck, floor 9.26, 100/100 replayable, 120 s. Only the configuration
selected on the dev set was run.

| config (K = 8) | progress | Δ | t 95 % CI | bootstrap 95 % CI | better / worse / same | changed | ms / searched decision | wall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| turn, value+hp | 0.1676 | +0.0057 | [−0.0011, +0.0124] | [−0.0008, +0.0125] | 23 / 9 / 68 | 17.3 % | 24.4 | 332 s |

100/100 replayable, 0 desyncs, 0 rollout errors.

### 1.3 A10 checkpoint, `silent-a10-v1` (20 games)

Raven and the Mac (M4 Pro, 4 threads, before measurements moved to raven) produced **identical game histories** for
greedy and all four grid points. Greedy 0.1578, 17 loss / 3 stuck.

| config (K = 8) | progress | Δ | t 95 % CI | bootstrap 95 % CI | better / worse / same | changed |
| --- | --- | --- | --- | --- | --- | --- |
| turn, value+hp | 0.1667 | +0.0088 | [−0.0050, +0.0226] | [−0.0039, +0.0216] | 7 / 1 / 12 | 18.1 % |
| turn, value | 0.1549 | −0.0029 | [−0.0288, +0.0229] | [−0.0284, +0.0176] | 7 / 3 / 10 | 18.0 % |
| combat, value+hp | 0.1735 | +0.0157 | [+0.0043, +0.0271] | [+0.0059, +0.0265] | 7 / 0 / 13 | 12.0 % |
| combat, value | 0.1618 | +0.0039 | [−0.0207, +0.0286] | [−0.0216, +0.0225] | 6 / 1 / 13 | 12.7 % |
| turn, value+hp, R = 2 | 0.1647 | +0.0069 | [−0.0069, +0.0206] | [−0.0059, +0.0196] | 5 / 1 / 14 | 18.3 % |
| turn, value+hp, pool budget 300 | 0.1667 | +0.0088 | (identical histories to turn value+hp, 6 rebuilds) | | | |

20/20 replayable and 0 desyncs in every run. The 20-game set ranks combat above turn; the 128-game set does not.

### 1.4 A0 checkpoint

| set | greedy | turn, value+hp | Δ | t 95 % CI | bootstrap 95 % CI | better / worse / same |
| --- | --- | --- | --- | --- | --- | --- |
| `silent-a0-dev-v1` (100), raven | 0.2320 | 0.2384 | +0.0064 | [−0.0083, +0.0212] | [−0.0080, +0.0213] | 23 / 18 / 59 |
| `silent-a0-v1` (20), Mac | 0.2578 | 0.2343 | −0.0235 | [−0.0630, +0.0159] | [−0.0618, +0.0088] | 6 / 5 / 9 |

On 20 A0 games all four grid points had negative means (turn value −0.0127, combat value+hp −0.0147, combat value
−0.0186; all CIs include 0), driven by three games that died ~10 floors earlier; on 100 games the A0 effect is
+0.0064 and not significant. 100/100 and 20/20 replayable, 0 desyncs. A0 combat on 100 games was not run (raven time).

### 1.5 Current integration head (ABI 2 branch path, rewards-screen macros)

`164073f` (this branch merged with the integration head `b26a383`) on the bundle `sts2train-e877036880` (encoder
`3b73317739c650bd`, ABI 2; the env now has rewards-screen macros, so the A10 checkpoint runs widened: RunActionKind
21 → 23, new rows zero). The search takes the ABI 2 path: one determinized `env.branch` per root, seeded so it equals
`clone_determinized` with the same seed, then one exact fan-out `branch` from the roots; no pool entry (pool max = the
128 fresh entries). Greedy: 0.1619 [0.151, 0.173], 128 loss, floor 9.26, 128/128 replayable, 132 s.

| config (K = 8) | progress | Δ | t 95 % CI | bootstrap 95 % CI | better / worse / same | changed | ms / searched decision | wall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| turn, value+hp | 0.1778 | +0.0159 | [+0.0087, +0.0232] | [+0.0090, +0.0234] | 34 / 8 / 86 | 16.2 % | 25.4 | 440 s |

128/128 replayable, 0 desyncs, 0 rollout errors, 0 rebuilds. Same effect as on the measured base (§1.1).

## 2. What the row is (declared search)

`python -m sts2train.lookahead eval` = the checkpoint's greedy policy plus a decision-time search at combat decisions.
The declaration is in every run's `config.json`: `search` (algo `la-v1`, K, R, H, horizon, score, seed, threads, …),
`information: "determinized, combat-only"`, `mirror_env: true`; policy name `nn:<ckpt>+la(K,R,horizon,honest)`, hash
`weights:<wh>+la:<sha8 of the search fields>`; `stats.json` next to it.

- **Mirror env.** The policy only sees `(obs, ctx)`. It rebuilds every game in a private `VecEnv` from `ctx.keys` (one
  `pool.add_fresh` per key; auto-reset off, no step / floor cap) and steps those shadows with exactly the actions it
  returns. Every call byte-compares all observation buffers of every live game with the evaluator's; a difference
  raises `Desync`.
- **Where.** Live games in combat (`is_combat`) with `cand_n ≥ 2`, in-combat prompts included (66–67 % of decisions).
- **Candidates.** Greedy, EndTurn, then the rest by descending logit, at most K; DiscardPotion never. K = 8 is not
  binding (5.0 candidates per search on average).
- **Information.** R roots per decision, determinization seed `u64(sha256("{seed}:{key}:{n}:{r}"))`, n = the game's
  decision index, shared by every candidate (common random numbers). A root shows the player's view byte for byte and
  resamples the draw order and every RNG stream (future monster moves, generated cards, rewards). An exact copy of a
  game's state is never taken (tests replace `snapshot` with an exception and check that exact `branch` copies only
  ever start from a rollout root). Pre-rolled content that is not resampled (encounter queues, later bosses, an
  unopened chest) is out of reach of a combat-only rollout; a won combat's rewards are generated at combat end from
  the root's resampled streams (`RewardsCmd` → `RewardsSet.WithRewardsFromRoom` → `Populate`).
- **Rollouts.** All rollout slots of a round start together; step 0 plays the candidate, then the model's greedy action
  on the rows still running (one batched forward per step across all games; byte-identical inputs evaluated once;
  unoccupied trailing hand / enemy / potion rows cut, which the model masks anyway). Latch per rollout:
  error / truncation → invalid (logged), terminal → victory / defeat, `is_combat` 1 → 0 → won, turn horizon: the next
  player turn or `cand_n = 0`, H steps (12 turn, 40 combat). A rollout never continues past its combat.
- **Score.** defeat 0; victory 1 − P(root) + win_bonus; otherwise ΔP + V(leaf), plus 0.10·hp/max_hp − 0.03·Σenemy
  hp/Σenemy max hp for `value+hp`. P = protocol progress, V = the value head (protocol objective, γ = 1, so ΔP + V
  estimates the remaining protocol return). Mean over valid determinizations, argmax, ties (1e-6) → greedy.
- **Memory.** On ABI 1 libraries every root is a pool entry (~22–33 KB) that only closing the env frees; past
  `pool_budget` (50,000) the env is rebuilt from the action histories (state hash + observation checked). R = 1 peaked
  at 12.6k entries for 128 games, R = 2 at 24.4k, so no measured run rebuilt; a 20-game run forced to budget 300
  rebuilt 6 times with identical histories. On ABI 2 libraries the roots are `env.branch` copies and nothing
  accumulates (§1.5).
- **ExIt hook.** `--record` writes `searches.jsonl`: per searched decision the game key, decision index, candidates,
  mean Q per candidate, valid rollout counts, choice and greedy choice (the state is the game replayed through
  `history_actions[:n]`). Off in every measured run; it changes no decision (test).

## 3. Costs

Raven: Xeon Platinum, 4 cores × 2 hyperthreads, AVX-512 (no bf16 / AMX), shared by ~18 users; the model is the large
preset (6-block d = 384 trunk over 43 tokens).

| 128 games, A10 checkpoint, 8 threads | turn, value+hp | combat, value+hp |
| --- | --- | --- |
| wall (acceptance ≤ 10 / ≤ 30 min) | 426 s = 7.1 min | 1,380 s = 23.0 min |
| searched decisions (share of live) | 12,447 (66 %) | 12,649 (67 %) |
| per searched decision | 25.0 ms; 5.0 candidates, 16.5 env steps, 10.3 forwarded rows | 100.3 ms; 5.0 candidates, 75.4 env steps, 42.8 forwarded rows |
| time split | root greedy forward 106 s, rollout forwards 290 s, env 18 s | root greedy forward 103 s, rollout forwards 1,201 s, env 62 s |
| model time per forwarded row | 2.19 ms | 2.14 ms |
| forward rows saved by input dedup | 37 % | 42 % |
| pool entries (max) / rebuilds | 12,575 / 0 | 12,777 / 0 |

- The network is the cost: rollout forwards are 93–95 % of the search time; restores, clones, rollout env steps, the
  shadow step and the sync check together stay under 6 %. The plain greedy row takes 125 s for the same 128 games, ~100
  s of it the same root forward. R = 2 costs 1.6× (686 s): the input dedup absorbs 59 % of the doubled rows, because
  two determinizations look the same until the next draw.
- Per-row forward cost of this model: 1.05 ms on the M4 Pro (4 threads), 3.3 ms on raven at 4 or 8 threads (GEMMs at
  ~70 % of AVX-512 peak; GELU, copies and attention take the rest). Cutting the unoccupied hand / enemy / potion rows
  (43 → ~30 tokens; max |Δlogit| 5.7e-6, 0 argmax flips in 3,360 rows) brought raven to ~2.2 ms (0.71–0.81×). Without
  it the turn run would take ~10 min and the combat run ~31 min.
- Memory: ~22–33 KB per pool root, so R = 1 holds ~0.4 GB for 128 games, R = 2 ~0.8 GB; the ABI 2 path holds none.

## 4. Checks

| check | result |
| --- | --- |
| replay audit (128 / 100 / 20 games, greedy and every lookahead run) | n/n everywhere |
| shadow sync (every call, every live game, every buffer) | 0 desyncs in 18,744 game-decisions (128-game turn run) and every other run |
| rollout errors | 0 in every run |
| identical re-run (128 games, turn value+hp) | identical histories and search counters |
| K = 1 vs greedy (128 games) | identical histories |
| Mac vs raven (20 games, 5 runs) | identical histories |
| forced rebuild (20 games, budget 300) | 6 rebuilds, identical histories |
| turn ≤ 10 min / combat ≤ 30 min, 128 games, 8 raven threads | 7.1 min / 23.0 min |
| `uv run --project sim/tools/train pytest -q` | green (56 tests at `b4a7cb8`; 113 after the merge, `164073f`) |

`tests/test_lookahead.py` (16 tests, ~18 s, random-init model): cross-slot exact restore, determinized clones, restore
counters after a defeat, inverse branch seed = `clone_determinized`, shadow sync through `sts2vec.evaluate` (n/n
replayable), K = 1 = greedy, identical re-run, honest mode (no exact copy of a shadow), defeat latched with slots
reused, branch path = pool path = forced rebuild, padding cut keeps outputs, paired statistics, ExIt record hook, the
Silent dev / test seed sets.

## 5. Reading the numbers

- The search helps the A10 policy a little and reliably (+0.012 to +0.015 with the CI above 0 for both value+hp rows),
  about 0.8 floor per game; almost every game diverges from greedy (median first difference at decision 5–8), so this
  is a whole-trajectory effect.
- The leaf evaluator is the limit, not the search budget: value-only scoring is worth nothing at the turn horizon
  (+0.0018) and it overrides toward EndTurn twice as often (475 vs 254); R = 2 equals R = 1; the combat horizon (4×
  the forwards) is not better than the turn horizon on 128 games. More rollouts on this value head will not clear the
  bar.
- **ExIt go signal: not fired** (best Δ = +0.0152 < +0.02; CI excludes 0). On the held-out set the same configuration
  shrinks to +0.0057 with the CI including 0. If ExIt is still tried, the turn / value+hp search is the expert to
  imitate (25 ms per searched decision on raven; the `--record` rows carry its targets), but a better leaf evaluator
  (value head) is the lever this measurement points at.

## 6. Not verified / caveats

- Raven is a shared VM: the same work ran at 2.1–3.4 ms per forwarded row at different times (and up to 14.8 ms in the
  20-game runs at 06:59–07:10 UTC, whose identical forced-rebuild twin took 98 s instead of 401 s). The timing
  acceptance rests on the 128-game turn run (426 s, re-run 424 s) and combat value+hp run (1,380 s); the combat value run
  (2,023 s) overlapped external load.
- Torch results depend in the low bits on batch composition on raven's MKL (8.6 % of rows not bit-identical across
  batches, 0 argmax flips in 7,463), so a game's rollouts can differ between seed sets of different sizes; re-runs are
  identical, and the root greedy forward uses the evaluator's full batch so K = 1 stays identical to greedy.
- The 20-game sets are too small for this effect size (the A0 20-game grid was negative, the 100-game set is not; the
  20-game A10 grid ranks combat first, the 128-game set does not).
- Not run: combat horizon with R = 2; A0 combat on 100 games; the A0 checkpoint on A10 seeds.
- Draw-pile positions a human could remember (cards put on top) are resampled too (`clone_determinized` default): the
  search knows slightly less than a careful human, never more.
- Rollouts use the model's greedy action, so the row inherits the model's blind spots inside the horizon.

## 7. Reproduce

```bash
# raven: python:3.13 container, CPU torch, the bundle's library, the committed sources (no git needed)
python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 --checkpoint s1b.pt --threads 8 --out G --greedy
python -m sts2train.lookahead eval --seedset silent-a10-dev-v1 --checkpoint s1b.pt --threads 8 --out L \
    --horizon turn --score value+hp            # --K 8 --R 1 --H 12 (40 for combat) --pool-budget 50000 --record
python -m sts2train.lookahead paired G L --out paired.json
STS2LA_CHECKPOINT=s1b.pt STS2LA_CONFIG='{"K": 8}' sts2vec eval --policy sts2train.lookahead:EnvLookahead --seedset ...
```

After the integration with the loop breaker (`sts2train.loopbreak`, on by default in `sts2train eval`), `--greedy`
runs that code path with the loop breaker **off** (`loop_breaker` in its config.json / stats.json): the lookahead's own
choices outside the search are the plain argmax, so the paired comparison still differs only by the search and K = 1
stays identical to `--greedy`. The measurements above predate the loop breaker.

Commits (branch `train/lookahead`): `8c702e3` seed sets, `65fad7b` lookahead + tests, `1dfb0c7` padding cut (the
128-game grid, re-run and K = 1 ran this), `b4a7cb8` ExIt record hook + provenance (R = 2, the raven 20-game grid,
A0-100 and test-100 ran this; same decisions), `6914040` merge of the integration head `b26a383`, `164073f` ABI 2
branch path + widened-checkpoint loading (§1.5 ran this). Run directories: raven `/data/raven/hack-sts2/lookahead/runs/`
(`a10-dev128`, `a10-test100`, `a10-s20`, `a0-dev100`, `v2-a10-dev128`).
