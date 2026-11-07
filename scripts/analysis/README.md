# scripts/analysis

- Eval readouts and decisions (`eval_summary.py`, `evalstats.py`, `decide.py`, `prereg/`): see `README-eval.md`.
- `death_profile.py` — death profile of eval dirs (`games/*.summary.json`) and / or `episodes.jsonl` (`@LO-HI` update
  ranges), one label per input, Markdown tables side by side (`--json` for machines): end-location bands, conditional
  death rates, killer ranking (encounter-id suffix kind), and with the wave-6 run-state fields boss-entry HP, deck /
  relics at death, death room kind, per-encounter exposure from eval `fights`; a data-gaps line per label. Stdlib only:
  `python3 -I scripts/analysis/death_profile.py M=<eval dir> T=<episodes.jsonl>@2065-2264`. Method:
  `docs/diag-2026-11-05/death-analysis-m-u2264.md`; tests: `sim/tools/train/tests/test_death_profile.py`.
- `bench_report.py` — throughput bench results (THRU-MEASURE) in, a table and a recommendation out:
  `sts2train bench-update` / `bench-rollout` `--json` files (per mode: ms per minibatch and per rollout step projected
  to seconds per training update, speed-up vs off, peak GPU MB, kernel launches, compiled-vs-eager checks) and the
  overlap preflight's `bench.json` / `decision.txt` (r_inf <= 1.4 rule). Recommends the fastest passing compile mode
  (split into `--compile-update` / `--compile-infer` when the winners differ) only above `--min-gain` of the projected
  live update time, and whether `--overlap double` is worth it. Stdlib only:
  `python3 -I scripts/analysis/bench_report.py runs/bench-w21/evals`; tests: `sim/tools/train/tests/test_bench_report.py`.
