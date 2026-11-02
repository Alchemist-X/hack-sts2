# Linux x86_64 validation (Huginn, 2026-11-01)

Result: the simulator, the HTTP play server and the Vec training interface build and pass on Linux x86_64 with no
code changes to the C# tree. Build 0 warnings 0 errors; 1103/1103 tests pass (the full suite, 19.5 min); selftest
300/300; vec pytest 63 passed with `STS2VEC_REQUIRE_FULL=1`; eval pytest 43 passed; NativeAOT `.so` publishes and loads.
Every deterministic count matches the Mac exactly (same combat-bench hash sink, same episode and status counts in the
vec benches). Throughput per run is 2.6x to 4x lower than the M4 Pro. Huginn has 4 physical cores (8 vCPU) and the
M4 Pro has 12 cores.

The portability fixes are all on the tooling side: the hostfxr discovery in the Python loader, a gcc fallback for
NativeAOT, a fallback when the uv cache is a dangling symlink, and two fixes to the launcher script (see §4).

## 1. Machine

| | Huginn (`ssh raven`) | Mac reference (same day) |
|---|---|---|
| CPU | Intel Xeon Platinum (KVM guest, Alibaba ECS), 1 socket × 4 cores × 2 threads = 8 vCPU, AVX-512, 33 MiB L3 | Apple M4 Pro, 12 cores (Arm64) |
| RAM | 30 GB, no swap | — |
| OS | Ubuntu 24.04.4 LTS, kernel 6.8.0-136-generic, locale en_US.UTF-8 | macOS 26.5.2 |
| .NET | SDK 9.0.320, runtime 9.0.22, installed user-level by `dotnet-install.sh --channel 9.0` into `$HOME/.dotnet` (22.5 s) | SDK 9.0.117, runtime 9.0.16 (Homebrew `dotnet@9`) |
| Python / uv | CPython 3.12.3 (`/usr/bin/python3`), uv 0.12.10 (already in `~/.local/bin`) | CPython 3.13.9, uv 0.11.7 |
| C toolchain | gcc 13 (no clang; no root to install it), GNU ld, objcopy, zlib dev | Xcode clang |
| Load | idle before the run (load average 0.00), only this job | other agents running (load average 2 to 7) |

## 2. How to run

```bash
scripts/linux_validate.sh [host]                 # default host 'raven'; validates HEAD (committed changes only)
scripts/linux_validate.sh --attach <sha> [host]  # re-attach after a dropped connection: poll, then fetch the logs
# knobs: LINUX_VALIDATE_SKIP_FULL=1  LINUX_VALIDATE_SKIP_AOT=1  LINUX_VALIDATE_FULL_TIMEOUT=1200
#        LINUX_VALIDATE_AOT_TIMEOUT=900  LINUX_VALIDATE_PURGE=1 (also rm ~/.nuget/packages)  LINUX_VALIDATE_POLL=30
```

The script runs `git archive HEAD` to a tarball, scps it to `<host>:~/sts2sim-validation/<sha>/`, and starts the
remote part with `nohup`, so a dropped ssh session does not kill it. It then polls until `logs/DONE` exists, copies
`logs/` to `tmp/linux-validation/<sha>/logs/` and prints `SUMMARY.txt`. It exits nonzero if a required step failed;
the full suite and the AOT steps are informational. On the host it installs the .NET 9 SDK and uv user-level when
they are missing. Each step gets its own log and timing (`summary.tsv`). At the end it removes the extracted tree and
every build output, keeps the logs, and leaves the SDK in place.

Steps run on the host, in order, from the extracted tree:

| # | Command |
|---|---|
| 01 | `dotnet build sim/Sts2Sim.sln -c Release` |
| 02 | `dotnet run -c Release --no-build --project sim/Sts2Sim.Tests -- Server Vec EndToEnd SelfConsistency W1Gate` |
| 03 | `dotnet run -c Release --no-build --project sim/Sts2Sim.Tests` (full suite, `timeout 1200`) |
| 04 | `dotnet run -c Release --no-build --project sim/Sts2Sim.Server -- --selftest 300 --content ported_only` |
| 05 | `dotnet run -c Release --no-build --project sim/Sts2Sim.Bench` (combat row, NIBBITS_WEAK, 5 s) |
| 06 to 09 | `DOTNET_gcServer=1 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec <N> <T> <steps> 0 1024` for 1024×8 (500 and 3000 steps), 1024×1, 4096×8, 64×8 |
| 10 | `STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q` |
| 11 | `uv run --project sim/tools/vec sts2vec check` |
| 11b | `env -u DOTNET_ROOT PATH=~/.local/bin:/usr/bin:/bin uv run --project sim/tools/vec python -c "from sts2vec import load; ..."` (discovery with no `DOTNET_ROOT` and no `dotnet` on PATH) |
| 12 | `uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500` |
| 13 | `uv run --project sim/tools/eval pytest -q sim/tools/eval` |
| 14 | `scripts/vec_publish_aot.sh` (linux-x64) |
| 15, 16 | `STS2VEC_AOT_LIB=.../Sts2Sim.Vec.so` + `sts2vec check` / `sts2vec bench --envs 1024 --threads 8 --steps 500` |

## 3. Results

There were three runs: `a20ed62` (all steps; the Python steps failed on the uv cache, see §4), `8530e4a`, and
`7527635` (the final script, run with `LINUX_VALIDATE_SKIP_FULL=1`). The full suite was only run in `a20ed62`; that run
had no other changes to `sim/` code. The tables show the `7527635` figures unless a cell says otherwise. Runs agree
within ±3 %.

### 3.1 Build and tests

| Step | Huginn | Mac (same day) | Huginn / Mac time |
|---|---|---|---|
| Release build (clean tree, NuGet restore cached) | 0 warnings, 0 errors, 20.4 s | 0 warnings, 0 errors | — |
| Filtered tests (`Server Vec EndToEnd SelfConsistency W1Gate`) | 100 passed, 0 failed, 79.6 s | 100 passed, 30.4 s | 2.6x |
| Full suite (runner is single-threaded) | **1103 passed, 0 failed, 1171.3 s (19.5 min)** (`a20ed62`) | 1103 passed, 420.1 s (7.0 min) | 2.8x |
| `--selftest 300 --content ported_only` | 300 completed, 0 crashed, 0 capped, 0 server errors, mean 68.9 steps, 23.8 s | — | — |
| vec pytest (`STS2VEC_REQUIRE_FULL=1`) | 63 passed, 32.2 s | 63 passed, 12.2 s | 2.6x |
| eval pytest (`sim/tools/eval`) | 43 passed, 3.7 s | — | — |
| `sts2vec check` (hostfxr and AOT) | OK / OK | — | — |
| Loader discovery with no `DOTNET_ROOT` and no `dotnet` on PATH | found `~/.dotnet/host/fxr/9.0.22/libhostfxr.so`, Server GC | — | — |

The full suite finished just inside the 20 min limit, so it was run and reported in full. On an idle 8-vCPU host the
script's 1200 s default timeout is tight. Raise `LINUX_VALIDATE_FULL_TIMEOUT` if the suite grows.

The play server's rules text is found in the tree (`knowledge/source`, versions v0.107.1 and v0.111.0: 625 cards,
306 relics, 66 potions, 296 powers, 70 events, 126 enemies). Test fixtures are found by `CallerFilePath`. Both work
from a `git archive` tree on Linux, with no case-sensitivity or line-ending problems.

### 3.2 Throughput, x86_64 compared with the Mac

The Mac column was measured in the same session on the same commit, under load from other agents (load average 2 to
7). `sim/BENCH.md` has earlier Mac rows: the E2 integrate row is 116,350 at 1024×8 and 142,439 decisions/s combat.

| Row | Huginn x86_64 | Mac M4 Pro | Mac / Huginn |
|---|---|---|---|
| Combat playouts (NIBBITS_WEAK, 5 s) | 41,400 decisions/s | 147,223 | 3.6x |
| Clone() | 8.595 us | 2.325 us | 3.7x |
| StateHash() | 4.532 us | 2.554 us | 1.8x |
| vec 1024×8, 500 steps | 28,072 env-steps/s (36.5 ms/batch) | 114,818 (8.9 ms) | 4.1x |
| vec 1024×8, 3000 steps (steady state) | **50,452** (20.3 ms/batch) | 179,715 (5.7 ms) | 3.6x |
| vec 1024×1, 500 steps | 10,431 (98.2 ms/batch) | 26,436 (38.7 ms) | 2.5x |
| vec 4096×8, 500 steps | 44,965 (91.1 ms/batch) | 125,397 (32.7 ms) | 2.8x |
| vec 64×8, 500 steps | 21,796 (2.9 ms/batch) | 65,356 (1.0 ms) | 3.0x |
| Python `sts2vec bench` 1024×8 (pool 64, hostfxr JIT) | 21,556 (47.5 ms/batch) | 47,739 (21.5 ms) | 2.2x |
| Python `sts2vec bench` 1024×8 (pool 64, **NativeAOT .so**) | **61,425** (16.7 ms/batch) | — | — |
| Pool warm, 1024 templates (C# vec bench) | 11.5 to 12.1 s | 2.45 s | 4.8x |

All vec rows: 0 sim_error, 0 content_gap, 0 illegal, 0 truncated, 0 wins.

**Determinism across platforms.** The x86_64 runs reproduce the Arm64 runs exactly: the combat bench clone-hash sink
is `40788C706BFA21E0` on both, the episode counts match (vec 1024×8: 7,286 at 500 steps and 44,417 at 3000; 4096×8:
29,235; 64×8: 463), and so do the Python status counts (OK 504,778, RESET 7,222, 7,222 episodes).

**Thread scaling.** Going from 1 to 8 threads at N=1024 gives 2.7x on Huginn and 4.3x on the Mac. Huginn has 4
physical cores, and SMT is worth roughly 1.3x on top of them.

### 3.3 JIT warm-up dominates the 500-step window on Huginn

Probe on Huginn (`tmp/linux-validation/probe/probe.log`; C# `vec 1024 8 500 0 1024` unless stated):

| Variant | env-steps/s |
|---|---|
| default (TieredCompilation on, TieredPGO on) | 27,829 |
| `DOTNET_TieredPGO=0` | 36,306 |
| `DOTNET_TieredCompilation=0` (full JIT up front) | 51,804 |
| `DOTNET_ReadyToRun=0` / `GCHeapCount=8` / `gcConcurrent=0` | 29,384 / 29,641 / 28,557 (no effect) |
| 3000 steps: default / `TieredCompilation=0` / `TieredPGO=0` | 50,937 / 53,253 / 51,988 |
| Python pool 64: hostfxr / AOT / hostfxr + `TieredCompilation=0` | 18,874 / 62,341 / 53,178 |
| Python pool 1024: hostfxr / AOT / hostfxr + `TieredCompilation=0` | 26,301 / 27,537 / 22,563 |

On this 4-core VM the tiered JIT needs about 20 s of batches to reach steady state, because tier-up compilation
competes with the 8 workers for the cores. That is why the 500-step rows read about 45 % below steady state. At steady
state the default JIT reaches the §D7 target, ≥ 50k env-steps/s at 1024×8: measured 50.5k to 50.9k. For short jobs on
Linux there are two options. NativeAOT starts at full speed, measured at 2.9x the hostfxr JIT on the Python 500-step
row. `DOTNET_TieredCompilation=0` gets close to that, but it makes pool-1024 runs slower, because full JIT on every
first `NewRun` path is expensive. With pool 1024 the cost of the first `NewRun` per entry dominates, and AOT and JIT
are equal. The Mac's 500-step window is also below its own steady state (115k against 180k).

### 3.4 NativeAOT (linux-x64)

**PASS.** `scripts/vec_publish_aot.sh` produced `sim/Sts2Sim.Vec/bin/aot/Sts2Sim.Vec.so`: 13 MB, 0 warnings, 36.2 s
(49.5 s on the first run, which included the ILCompiler package restore). Huginn has no clang and no root to install
it, so the script now links with gcc through `-p:CppCompilerAndLinker=gcc`. `sts2vec check` passed against the `.so`,
and the `.so` reports Server GC, fixed at publish time. Python bench: 61,425 env-steps/s at 1024×8 (pool 64). On Linux
the ILCompiler does not link the Homebrew openssl/brotli that the macOS build needs.

## 4. Portability fixes made

| Problem found | Fix |
|---|---|
| `sts2vec/loader.py` probed Homebrew and macOS roots first. The error and hint text named only Homebrew, and the hint was wrong on Linux. | The probe order is now `DOTNET_ROOT`, then `DOTNET_INSTALL_DIR`, then per-platform roots: macOS Homebrew and `/usr/local/share/dotnet`; Linux `$HOME/.dotnet`, `/usr/lib/dotnet`, `/usr/share/dotnet`, `/usr/lib64/dotnet`, `/opt/dotnet`, the snap path. After those come the real directory of `dotnet` on PATH (this resolves `/usr/bin/dotnet` to `/usr/lib/dotnet`) and `dotnet --list-runtimes`. The error hints are now platform-aware. The old code already reached `~/.dotnet` as the last candidate, so this change is about robustness and the error text. Step 11b proves that discovery works with no environment set. |
| NativeAOT needs clang, and Huginn has only gcc. | `scripts/vec_publish_aot.sh`: on Linux with no clang, add `-p:CppCompilerAndLinker=gcc`; `AOT_LINKER=clang|gcc` overrides this. |
| `~/.cache/uv` on Huginn is a root-owned symlink to `/data/raven/cache/users/yishu/uv`, which does not exist. Every `uv` command failed with `Failed to initialize cache ... File exists`. | The validation script detects a dangling `~/.cache/uv` and sets `UV_CACHE_DIR` to a run-local directory, which is removed at cleanup. Nothing outside `$HOME/sts2sim-validation` was touched. |
| `uv run --project sim/tools/eval pytest -q`, run from the repository root, collects `sim/tools/vec/tests` and fails on `numpy`. This happens on the Mac too. | The script passes the path, `pytest -q sim/tools/eval`, as `sim/tools/eval/README.md` already says. |
| Launcher: `ssh host "cd X && tar ... && nohup cmd > log &"` backgrounds the whole `&&` list. Its stdout stays the ssh channel, so ssh never returned, and the connection was reset after about 15 min. The remote run survived. | Only the `nohup` command is backgrounded. `--attach <sha>` re-attaches to a running job. |

No C# changes were needed: no path, casing, locale or line-ending issues turned up.

## 5. Cleanup on Huginn

Removed: every extracted tree and build output (`~/sts2sim-validation/<sha>/src`, about 400 MB each), the tarballs,
the run-local uv caches, the probe tree, and `~/.nuget/packages` (340 MB, created by this job). Kept: the logs under
`~/sts2sim-validation/{a20ed62,8530e4a,7527635}/logs` and `~/sts2sim-validation/probe/*.log` (484 KB in all), and the
.NET SDK in `~/.dotnet` (579 MB) so later runs skip the install. `~/.dotnet/corefx` existed before this job and was
not touched. The root disk was at 89 % (4.4 GB free) before and at 91 % (3.7 GB free) after. Running `rm -rf
~/.dotnet/{dotnet,host,packs,sdk,sdk-advertising,sdk-manifests,shared,templates,metadata}` frees the remaining 579 MB.
Local copies of the logs are in `tmp/linux-validation/` (git-ignored).

## 6. Open issues

- Huginn's dangling `~/.cache/uv` symlink, owned by root, breaks plain `uv` for this user everywhere on the host. An
  admin should recreate `/data/raven/cache/users/yishu/uv` or remove the link.
- The root filesystem on Huginn is at 91 %. Future Linux runs should consider building under `/data/raven` (155 GB
  free), but the brief requires `$HOME`.
- The Vec bench's 500-step window mostly measures JIT warm-up on small x86 hosts. For Linux rows, prefer ≥ 3000 steps,
  or publish with NativeAOT. Consider documenting `DOTNET_TieredPGO=0` / `TieredCompilation=0` for short jobs, or
  setting `TieredCompilation` in `sts2vec.runtimeconfig.json` after a cross-platform measurement. No runtime config was
  changed here.
- The single-threaded test runner takes 19.5 min on Huginn. A parallel runner, or sharding the filter list, would make
  the full suite practical on Linux CI.
- `sim/VEC.md` says a NativeAOT round-trip pytest runs when `bin/aot/Sts2Sim.Vec.<dylib|so>` exists. No such test is
  in `sim/tools/vec/tests` (the only AOT-related test is a GC-mode skip). AOT is covered here by `sts2vec check` and
  `bench` against the `.so`.
- Containerisation was not attempted: Huginn has no docker and no root.
