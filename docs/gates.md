# Gates runner (`scripts/gates.sh`)

One command for the checks every wave integrator ran by hand (sim/specs/integration.md). Each step prints
`PASS` / `FAIL` / `SKIP` with its key numbers, a summary table closes the run, and the exit status is non-zero
when any step failed.

```bash
scripts/gates.sh                     # full set: full test suite (incl. run / content soaks); ~8-10 min on an M4 Pro
scripts/gates.sh --quick             # filtered tests instead of the full suite; ~2.5 min
scripts/gates.sh --bench "E3 integrate"   # also bench (combat + vec) and append a row to sim/BENCH.md
scripts/gates.sh --skip g0,sts2rec   # skip steps (SKIP, not FAIL), e.g. on a host without the G0 bundles
scripts/gates.sh --list              # step names
```

Logs go to `tmp/gates/<UTC timestamp>/`: one `<step>.log` per step, `summary.tsv` (step, status, seconds, key
numbers), `SUMMARY.txt` (the closing table), `header.txt` (commit, host, load average, tool versions) and the G0
report `g0-report.json`. Exit status: 0 all passed or skipped, 1 a step failed, 2 usage error.

## Steps

| step | command | PASS when |
| --- | --- | --- |
| `build-release` | `dotnet build sim/Sts2Sim.sln -c Release --no-incremental` | rc 0, 0 warnings, 0 errors |
| `build-debug` | same, `-c Debug` | rc 0, 0 warnings, 0 errors |
| `tests` | `dotnet run -c Release --no-build --project sim/Sts2Sim.Tests` (full), or with `--quick` the filters `Server Vec Parity EndToEnd SelfConsistency W1Gate` | rc 0, `Summary:` passed = total, 0 failed, `PENDING total: 0` |
| `selftest` | `Sts2Sim.Server -- --selftest 100 --content ported_only` | every run completed, 0 crashed, 0 server errors |
| `g0` | `Sts2Sim.Diff -- <bundles> --report <logs>/g0-report.json` | `overall:` line: bundles 4, agree 4, decisions agreed 69/69 |
| `pytest-eval` | `uv run --project sim/tools/eval pytest -q sim/tools/eval` | rc 0, no failed / error count |
| `pytest-vec` | `STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q` | same |
| `sts2rec` | `cd sts2rec && uv run pytest -q`; one rerun on failure | same; a pass on the rerun is reported with the first run's failures |
| `py-scripts` | `python3 -m unittest discover -s scripts -p 'test_*.py'`, then `python3 scripts/kernel_map.py --check` when the rule-truth decompile is present | unittest OK; map up to date, no stale curated record, no unmapped kernel file of >= 50 lines |
| `bench-combat` | `--bench` only: `Sts2Sim.Bench` (NIBBITS_WEAK, 5 s) | rc 0 and a decisions/s line |
| `bench-vec` | `--bench` only: `DOTNET_gcServer=1 Sts2Sim.Bench -- vec 1024 8 500 0 1024` | rc 0, env-steps/s line, `sim_error=0 content_gap=0 illegal=0` |

Notes:
- The MSBuild summary is parsed in English (`0 Warning(s)`, `0 Error(s)`) and Chinese (`0 个警告`, `0 个错误`);
  when the summary block is missing, distinct `warning CSxxxx` / `error CSxxxx` lines are counted instead.
- `--no-incremental` is the default so a project that is already up to date still reports its warnings;
  `GATES_INCREMENTAL=1` builds incrementally (faster, may hide warnings of unchanged projects).
- The known `sts2rec` flake is `test_text_env.py::test_vector_steps_workers_concurrently` (a wall-clock assertion
  that fails under machine load); it is rerun once, never skipped.
- Every dotnet step after `build-release` reports `FAIL (not run)` when the Release build failed.
- `--bench` appends a section `## <label> (<date>, scripts/gates.sh --bench, commit <sha>)` with a combat row
  (decisions/s, Clone, StateHash) and a vec row (env-steps/s, ms/batch avg / min / max, resets/s, episodes) plus the
  load average sampled before each bench. Benches are noisy on a loaded machine: compare against a row taken in
  the same session, as the integration records do.
- Portable: macOS `/bin/bash` 3.2 and Linux, no GNU-only tools. A per-step timeout (`GATES_TIMEOUT`, default 3600 s)
  applies only when `timeout` or `gtimeout` is on PATH.

## Environment knobs

| variable | default | meaning |
| --- | --- | --- |
| `STS2SIM_G0_BUNDLES` | `/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/sim-source-recreation-9392df/tmp/handoff/diff-bundles/bundles` | G0 bundle directory (P0 set, v0.107.1) |
| `GATES_G0_BUNDLES` / `GATES_G0_DECISIONS` | 4 / 69 | expected G0 counts |
| `GATES_SELFTEST_RUNS` | 100 | selftest games |
| `GATES_INCREMENTAL` | 0 | 1 = incremental builds |
| `GATES_BENCH_FILE` | `sim/BENCH.md` | where `--bench` appends |
| `GATES_OUT` | `tmp/gates/<timestamp>` | log directory |
| `GATES_TIMEOUT` | 3600 | per-step seconds (needs `timeout` / `gtimeout`) |
| `STS2_DECOMPILE_ROOT` | `output/server-bootstrap/linux-decompiled` (main checkout) | tree for `kernel_map.py --check` |

On Huginn (no G0 bundles, no decompile tree): `scripts/gates.sh --quick --skip g0` (the kernel-map check skips
itself without the tree). `scripts/linux_validate.sh` remains the full Linux validation (toolchain install, AOT,
benches); this runner is the per-integration gate.

## Reference run (2026-11-02, macOS M4 Pro, commit `ebc055f` + this change)

`scripts/gates.sh --quick --bench "gates.sh trial"` (bench row written to a scratch file, not to BENCH.md):
build Release / Debug 0 warnings 0 errors; tests (quick) 107/107, PENDING 0 (31 s); selftest 100/100, 0 crashed,
mean 67.6 steps; G0 4/4 bundles, 69/69 decisions; pytest-eval 67 passed; pytest-vec 66 passed; sts2rec 701 passed,
4 skipped; combat 135,441 decisions/s, Clone 2.477 us, StateHash 2.727 us; vec 1024x8x500 78,141 env-steps/s
(load average 6.7: another wave's game instances were running). Total 151 s.

`scripts/gates.sh` (full, same tree plus the kernel-map work): build 0 / 0 both configurations; full suite
1114/1114, PENDING 0 (465 s in the runner); selftest 100/100; G0 4/4, 69/69; pytest-eval 67; pytest-vec 66;
sts2rec 701 passed, 4 skipped (no flake rerun needed); py-scripts 30 unittests OK, `kernel_map.py --check` OK.
Total 570 s.
