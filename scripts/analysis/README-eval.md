# scripts/analysis: eval readouts and decisions

Three stdlib-only tools (run them as `python3 -I`). They implement the 2026-11-06 audit items EVAL-A (EV1),
EVAL-B (EV4) and DECIDE (EV5 + EV6, ops F4). `README.md` covers the older tools in this directory.

| tool | what it does |
| --- | --- |
| `eval_summary.py` | Writes a flat `summary.json` (`sts2.eval.summary/1`) into an eval dir (config.json + games/). The summary holds identity, headline, stage funnel, `per_seed` progress and `per_seed_stages` bitmasks. `--backfill ROOT` fills every eval dir under ROOT. `--print-gcloud` prints the bucket backfill loop and does not run it. |
| `evalstats.py` | `window` gives the per-seed mean over the K = 5 latest pinned evals at or before `--at`, after `--after` (the fork). `pair` gives B − A per seed: mean, t CI and a 10k seed bootstrap, as in `sts2train/compare.py`, plus leading indicators. `slope` gives the OLS slope per 500 updates, and several SRCs make one lineage. `index` lists what it found. |
| `decide.py` | Takes a pre-registration JSON (`prereg/*.json`) and the data, and prints `ADOPT / REJECT / CONTINUE / REPLICATE / KILL` with the rule that fired. It writes `pair.json` and logs each confirm-set read to `ab-access.jsonl`. |

## Sources

A SRC is `[RUN=]PATH[,PATH...]`. A PATH can be:

- an eval dir
- a tree of eval dirs (`runs/<run>/evals/...`, a pulled copy)
- a run mirror with `evals.jsonl` (per-checkpoint means only)
- a `summary.json` file

When per-seed data is missing, the window uses the means. Its interval then comes from the EV1 variance model, half-width `1.96*sqrt((J/K_a + J/K_b)/n)` with J = 0.0156, and the output says `ci_method: variance-model`. `decide.py --root DIR` uses `DIR/<run>` for every run in the prereg.

```bash
M=<scratchpad>/mirror
python3 -I scripts/analysis/evalstats.py pair w4-d=$M/w4-d w4-c=$M/w4-c --at 9164 --after 7664
python3 -I scripts/analysis/evalstats.py slope w4-c=$M/w4-c w4-c3=$M/w4-c3 --from 10664 --to 11900
python3 -I scripts/analysis/decide.py scripts/analysis/prereg/s2-vs-c4.json --at 12664 --root $M --no-write
python3 -I scripts/analysis/eval_summary.py --print-gcloud > /tmp/backfill.sh   # review, then run on Tokyo
```

## Decision rules (decide.py)

The first rule that matches wins.

1. **KILL**: at least `kill.after_updates` updates since the fork, window delta < `kill.delta`, and CI upper < 0. It can fire on any read.
2. **CONTINUE**: a monitoring read, meaning a read point that is not pre-registered. Only KILL can fire on such a read, and the confirm set is not read.
3. **CONTINUE**: `pairing_wait`. The data of some arm has not reached the read point yet.
4. **ADOPT**: all of the following hold:
   - at least 2 pairs
   - pooled CI lower bound > 0
   - every pair's point estimate > 0
   - confirm point estimate > 0, with confirm CI lower bound > −margin
5. **REPLICATE**: a single pair whose CI lower bound is > 0. One pair never gives ADOPT.
6. **REJECT** when any of these holds:
   - harm: CI upper < 0
   - futility: CI upper < mde/2, from the `futility.from_read`-th read on (default: the 2nd)
   - the read is at `max_at`
7. **CONTINUE**: otherwise.

The futility default deviates from the audit plan. At the first pre-registered read, most of the window comes from checkpoints taken before the effect has had time to build. For example, S − C3 at u11164 had a window of −0.008 [−0.022, +0.006], which would have read REJECT. The same contrast read +0.000 at u11664.

## Tests

The tests use unittest, so either runner works:

```bash
python3 -I scripts/analysis/tests/test_eval_summary.py
python3 -I scripts/analysis/tests/test_evalstats.py
python3 -I scripts/analysis/tests/test_decide.py
pytest scripts/analysis/tests
```

Synthetic fixtures live in `tests/synth.py`. Regression checks on mirrored data run when it is present (set `STS2_ANALYSIS_DATA` to the scratchpad copy); otherwise they are skipped. The regression checks are:

- C − D @u9164 reads REPLICATE, not ADOPT.
- C and C-rep vs D reads REJECT.
- S − C3 @u11164 reads CONTINUE.
- G is KILLed at u10700.
- The S u11000 − C3 u11300 pair matches `sts2train compare`.
- The EV1 numbers hold: sd_diff 0.168 and corr 0.400; C window +0.0195; C slopes +0.0096 and +0.0017 per 500.
