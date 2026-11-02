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
| `GATES_COMMIT` | `git rev-parse --short HEAD` | commit label for a tree without `.git` (the CI export) |

On Huginn (no G0 bundles, no decompile tree): `scripts/gates.sh --quick --skip g0` (the kernel-map check skips
itself without the tree). `scripts/linux_validate.sh` remains the full Linux validation (toolchain install, AOT,
benches); this runner is the per-integration gate.

## Linux CI (`scripts/ci_linux.sh`, `docker/ci.Dockerfile`)

The same gates on Linux x86_64 inside a container, on a clean `git archive` export of HEAD (committed changes
only):

```bash
scripts/ci_linux.sh                    # gates.sh --quick --skip g0; local docker if 'docker info' works, else ssh raven
scripts/ci_linux.sh -- --skip g0       # replace the gate arguments (here: the full suite)
scripts/ci_linux.sh --host raven       # force the ssh path; --local forces local docker
scripts/ci_linux.sh --attach <sha>     # re-attach to a started remote run after a dropped link
```

- **Image** `docker/ci.Dockerfile`: `mcr.microsoft.com/dotnet/sdk:9.0` (Debian 12, python3 3.11 added) plus `uv`
  0.11.7, the same tags as the product `Dockerfile`; nothing from the repository is baked in. The checkout is
  bind-mounted at `/work` and the container runs as the caller's uid with `HOME=/work/.ci-home`, so one image
  serves every commit and the outputs are owned by the user.
- **Flow**: export -> (scp to `/data/raven/hack-sts2/tmp/ci-linux/<sha>/` on Huginn) -> extract -> build the image
  -> `docker run ... bash scripts/gates.sh <args>` with `GATES_COMMIT=<sha>`, NuGet / uv caches kept in
  `<work root>/cache/` between runs and the container's `/tmp` on the work volume -> `logs/` (gate logs,
  `image.log`, `timing.tsv`, `SUMMARY.txt`) -> the extracted tree is removed. The remote part runs under `nohup`
  and the local part polls for `logs/DONE`, then copies `logs/` to `tmp/ci-linux/<sha>/`. Exit status = gates.sh's.
- **Huginn root fs**: Docker's data root is `/data/raven/docker`, but the containerd snapshots and the build
  cache live on the root fs, which has about 2.7 GB free; the SDK image needs about 2 GB there. The first run
  filled it (the container's `/tmp` was on it too, and every step failed with ENOSPC); since then the runner
  refuses to pull with less than `CI_LINUX_MIN_FREE_MB` (2500) free, keeps `/tmp` on `/data`, and over ssh
  removes the image and the build-cache records it created after the run (`CI_LINUX_CLEANUP=1`, the default
  there). Moving containerd's root to `/data` needs the admin (see the open items in docs/HANDOVER.md).
- **GitHub**: `.github/workflows/gates.yml` runs the same container step and uploads the gate logs. It is a
  skeleton, **not enabled**: only `workflow_dispatch` is declared and it has never run on GitHub; the file header
  says how to turn it on.

Reference run: see "Linux CI run" below.

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

## Linux CI run (2026-11-02, Huginn, `scripts/ci_linux.sh --host raven`)

Commit `feab6d5` (the code of this change; docs edited after), Huginn x86_64, 8 vCPU, Docker 29.9.2, load average
8-12 from other users at the start. Toolchain image built cold (the SDK image pulled and later removed):

| step | seconds | result |
| --- | ---: | --- |
| image (pull `dotnet/sdk:9.0` + `uv`, apt python3) | 63 | built |
| `gates.sh --quick --skip g0` | 263 | **PASS**: build Release / Debug 0 warnings 0 errors (26 / 20 s); tests quick 108/108, PENDING 0 (85 s); selftest 100/100, 0 crashed; pytest-eval 76 passed; pytest-vec 66 passed; sts2rec 701 passed, 4 skipped; py-scripts 53 unittests OK (kernel-map check skipped: no tree); g0 skipped |
| total on Huginn | 326 | cleanup: image and this run's build cache removed, root fs back to 2.8 GB free |

An earlier run of the same flow (`32798c4`) gave the same results in 305 s (image 48 s, gates 257 s). The very first
attempt (`5d8b339`) failed every step with ENOSPC: the container's `/tmp` sat on the full root fs; that run is why
the runner now mounts `/tmp` from the work volume, checks free space first and cleans up. Logs:
`raven:/data/raven/hack-sts2/tmp/ci-linux/<sha>/logs/` (readable by the `raven` group) and `tmp/ci-linux/<sha>/logs/`
locally.
