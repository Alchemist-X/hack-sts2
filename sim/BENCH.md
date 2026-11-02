# Sts2Sim benchmark (P0, W1)

Command: `dotnet run -c Release --project sim/Sts2Sim.Bench [encounter] [seconds]` (defaults: `NIBBITS_WEAK`, 5 s).

Machine: Apple M4 Pro (Arm64), .NET 9.0.16, Release, single thread. Other build/agent processes were running on the machine at the same time, so throughput varied between runs (see range below).

Setup: real Ironclad starter deck (`StartingDecks.Ironclad`, 10 cards) against the registered `NIBBITS_WEAK` encounter. Each run starts with a warmup pass before measuring.

| Metric | Earlier runs (3, loaded machine) | Latest run (2026-10-30) |
|---|---|---|
| Clone() (mid-fight, 6 actions in; 100k iterations; clone+hash minus hash-only) | 3.89 / 2.89 / 4.20 us | 2.53 us/clone |
| StateHash() (200k calls) | 2.68 / 1.56 / 1.59 us | 0.87 us/call |
| Uniform-random legal playouts, 5 s window, cap 1000 steps | 158k / 271k / 450k decisions/s | 705,048 decisions/s |
| Episodes in window | 39,381 / 67,438 / 112,116 (all completed, 0 capped) | 175,664 (all completed, 0 capped), 20.1 decisions/episode, 97.9% wins |

Notes:
- Each clone's StateHash is added into a sink that gets printed, so the JIT cannot drop the work. The clone figure is clone+hash with the measured hash cost subtracted.
- The policy uses `System.Random(12345)`. Combat seeds are 1, 2, 3, and so on.

## Gates (P0, W1)

| Gate | Target | Measured | Status |
|---|---|---|---|
| Throughput | >=1000 decisions/s/core | 705k latest, >=158k worst | PASS (>=158x margin) |
| Clone cost | <=1 ms | 2.5 us latest, ~4 us worst | PASS (>=250x margin) |
| W1 throughput (Part B) | >=300 decisions/s/core | 136k median, 55k worst run (2026-11-01, round 3, all nine W1 tracks; 668k with the scaffold only) | PASS (~450x margin, ~185x at the worst run) |
| W1 clone cost (Part B) | <=1 ms | 2.2 us median, 10.9 us worst (2026-11-01, round 3; 2.65 us with the scaffold only) | PASS (~450x margin, ~90x at the worst run) |

## W1 (2026-11-01, integrate round 2)

Machine: Apple M4 Pro (Arm64, 12 cores), .NET SDK 9.0.117, runtime 9.0.16, Release, single thread. Other agent processes were running (load average 2.4 to 4.5), so the P0 tree (`819d7c6`, exported with `git archive`) was rebuilt and benched interleaved with the W1 tree in the same session.

Tree: `claude/sim-source-recreation-9392df` at the W1 phase-0 scaffold (`46d1918` plus `94424e5`). No W1 track content is merged yet, so these numbers measure the scaffold's state additions only (D3 player fields, the D4 `RunState`, the B2 combat fields in clone and canonical).

| Metric | P0 latest (2026-10-30) | W1, 3 standalone runs (median) | Same session, interleaved: P0 tree / W1 tree (medians) | W1 / P0 same session | W1 / P0 latest |
|---|---|---|---|---|---|
| Clone() (mid-fight, 6 actions in; 100k iterations; clone+hash minus hash-only) | 2.53 us | 2.652 / 2.739 / 2.623 us (2.652) | 1.841 / 2.634 us | 1.43x | 1.05x |
| StateHash() (200k calls) | 0.87 us | 1.107 / 1.133 / 1.192 us (1.133) | 1.006 / 1.156 us | 1.15x | 1.30x |
| Uniform-random legal playouts, 5 s window, cap 1000 steps | 705,048 decisions/s | 664,117 / 671,595 / 667,858 (667,858) | 670,718 / 677,960 decisions/s | 1.01x | 0.95x |
| Episodes in window | 175,664 | 165,446 / 167,315 / 166,377 (all completed, 0 capped), 20.1 decisions/episode, 97.9% wins | | | |

Notes:
- Same-session runs (P0 / W1 per round): clone 1.841 / 2.634, 1.791 / 2.627, 1.889 / 2.711 us; hash 1.006 / 1.184, 1.010 / 1.156, 0.992 / 1.098 us; playouts 673,354 / 674,732, 662,310 / 680,741, 670,718 / 677,960 decisions/s.
- The P0 tree's own clone time moves between sessions (1.79 to 2.78 us across the W1 round-1 and round-2 sessions) while the W1 tree stays at 2.6 to 2.9 us, so the same-load clone ratio is 1.10x to 1.43x. The added cost is 18 more objects per clone plus the larger B2 card copies (+1,256 B in all, +23 %); the hash covers 326 more canonical bytes (+16 %). Throughput is flat at equal load because the step path gained only constant-cost checks. Breakdown: `sim/specs/integration.md` sections 7.4 and 8.4.

## W1 (2026-11-01, integrate round 3)

Machine: Apple M4 Pro (Arm64, 12 cores), .NET SDK 9.0.117, runtime 9.0.16, Release, single thread. Other agents were running (load average 3.4 to 9). The machine alternated between two speed states for every tree (the same binary gave about 150k and 60k to 80k decisions/s in consecutive runs, the P0 tree 667k and 213k), so only same-session ratios rank the trees.

Tree: all nine W1 tracks merged (`674ee65`) plus the round-3 integration fixes.

| Metric | P0 latest (2026-10-30) | W1 round 3, 5 runs (median) | Same session, interleaved medians (11 runs each): P0 / base `263810d` / W1 | W1 / P0 same session (side by side) | W1 / P0 latest |
|---|---|---|---|---|---|
| Clone() | 2.53 us | 2.201 / 2.138 / 2.273 / 2.162 / 5.476 us (2.201) | 3.275 / 4.544 / 5.848 us | 1.79x (1.34x) | 0.87x |
| StateHash() | 0.87 us | 2.479 / 2.498 / 2.578 / 6.955 / 4.057 us (2.578) | 2.076 / 2.452 / 4.016 us | 1.93x (2.10x) | 2.96x |
| Uniform-random legal playouts, 5 s window, cap 1000 steps | 705,048 decisions/s | 147,982 / 135,696 / 143,311 / 75,419 / 68,788 (135,696) | 314,391 / 328,960 / 66,592 | 0.21x (0.25x) | 0.19x |

Notes:
- Every tree plays the same decisions (40,299 in 2,000 episodes, equal trajectory digest), 20.1 decisions per episode, 97.8 % wins.
- Allocated bytes per decision: P0 935, base 1,008, W1 17,165 (3,373 on the fast path `Step(a, answerer)`); per `Clone()`: 5,384 / 6,648 / 12,752; canonical bytes of the mid-fight state: 2,042 / 2,373 / 3,792.
- The decision API `Step(a)` takes one `DeepClone` per action (B1 tape engine); the fast path is 1.32x to 1.59x faster in the same process. The rest of the regression is the full listener walk (B3), the B6 turn flow and history, and the B7 keyword hook. Per-track breakdown: `sim/specs/integration.md` section 9.7.

Caveat: these numbers come from a single small encounter with a 10-card deck. Costs will rise as more cards, powers and monsters are ported, so re-run the bench after each content batch.

## W2/W3 stage A (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W2/W3 stage A | 136,592 | 21.229 us | 5.928 us |

## W2/W3 stage B (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W2/W3 stage B | 148,206 | 2.534 us | 2.501 us |

## W2/W3 stage C (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W2/W3 stage C | 147,758 | 2.537 us | 2.428 us |

## W3.5/W4 integrate 1 (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W3.5/W4 integrate 1 | 145,573 | 2.121 us | 2.570 us |

## W3.5/W4 integrate 2 (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W3.5/W4 integrate 2 | 141,555 | 2.449 us | 2.682 us |

## W4.1 integrate (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

Machine heavily loaded (the game client running, load average 4.5 to 7.5); the integrate-2 tree (`efb0102`, exported with `git archive`) was benched interleaved in the same session and runs at the same speed, so the drop from the integrate-2 row is the machine, not the code.

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| W4.1 integrate (5 runs: median, range) | 28,384 (26,229–30,838) | 12.963 us (2.565–14.388) | 10.289 us (9.833–11.488, 3 runs) |
| integrate-2 tree, same session (4 runs) | 27,110 (23,022–135,714) | 10.137 us (8.713–12.630) | 9.439–12.043 us (2 runs) |

## E1 integrate (2026-11-01, NIBBITS_WEAK, 1 run, 5 s)

Machine moderately loaded (load average 3.3 to 4.4). Back at the integrate-2 level; the W4.1 drop was machine load. E1 touched the kernel only with read-only queries (`CardPreview`, a `DampenPower` accessor), so no kernel speed change is expected.

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| E1 integrate | 143,509 | 2.179 us | 2.566 us |

## Vec (E2): batched VecEnv through the C ABI

`DOTNET_gcServer=1 dotnet run -c Release --project sim/Sts2Sim.Bench -- vec <envs> <threads> <steps> <ascension> [pool]`
(in-process exports, NativeMemory buffers, uniform-random candidate policy, all five characters, pool templates warmed
before the window; env-steps/s counts slot steps with an action ≥ 0). Target (CONTRACTS.md §D7): ≥ 50k env-steps/s at
N = 1024, 8 threads, A0. The integrator appends rows (C# `vec` and Python `sts2vec bench`, N ∈ {64, 1024, 4096} ×
threads {1, 8}).

| Row | N | threads | GC | env-steps/s | ms/batch (avg / min / max) | resets/s | bytes/env | load avg |
|---|---|---|---|---|---|---|---|---|
| V2 env-core, stub encoder (2026-11-01, 500 steps, pool 1024) | 1024 | 8 | server | 164,521 | 6.22 / 5.25 / 20.5 | 2,341 | 26,710 | 10.3 |
| V2 env-core, stub encoder (same session) | 1024 | 8 | workstation | 57,575 | 17.8 / 11.8 / 30.7 | 819 | 26,710 | 10.5 |
| V2 env-core, stub encoder (same session, 200 steps) | 1024 | 1 | server | 25,387 | 40.3 / 35.6 / 56.5 | 348 | 26,710 | 10.6 |

## E2 integrate (2026-11-01)

Merged tree (scaffold + V1 encoder + V2 env-core + V3 python + integration fixes), real encoder and candidate table,
`encoder_version` `dff07a6bc984b119`, 26,710 bytes/env, A0, uniform-random candidate policy, 500 batched steps, all
five characters, Server GC. Machine loaded by other agents (load average 2.6 to 7.0); repeated runs vary by up to
±30 %. §D7 target (≥ 50k env-steps/s at N = 1024, 8 threads): **met** (C# 116k, Python 73k / 54k).

C# — `DOTNET_gcServer=1 dotnet run -c Release --no-build --project sim/Sts2Sim.Bench -- vec <N> <T> 500 0 1024`
(pool of 1024 fresh entries, templates warmed before the window; 0 sim_error, 0 content_gap, 0 illegal, 0 truncated, 0 wins in every row):

| N | threads | env-steps/s | ms/batch (avg / min / max) | resets/s | episodes ended | obs MB/batch | gen0/1/2 | load avg |
|---|---|---|---|---|---|---|---|---|
| 64 | 1 | 18,551 | 3.450 / 2.680 / 8.175 | 268 | 463 | 1.7 | 7/3/0 | 2.6 |
| 64 | 8 | 89,125 | 0.718 / 0.483 / 4.483 | 1,290 | 463 | 1.7 | 5/3/0 | 2.7 |
| 1024 | 1 | 27,418 | 37.348 / 20.193 / 69.930 | 390 | 7,286 | 27.4 | 16/7/1 | 3.6 |
| 1024 | 8 | 116,350 | 8.801 / 7.896 / 40.474 | 1,656 | 7,286 | 27.4 | 17/8/2 | 3.1 |
| 4096 | 1 | 41,549 | 98.582 / 77.192 / 263.095 | 593 | 29,235 | 109.4 | 31/13/5 | 5.1 |
| 4096 | 8 | 144,421 | 28.362 / 11.614 / 71.979 | 2,062 | 29,235 | 109.4 | 44/17/8 | 3.0 |

Python — `uv run --project sim/tools/vec sts2vec bench --envs <N> --threads <T> --steps 500 --pool <P> --seed 0`
(hostfxr, ctypes; whole-loop figure, the native step alone is < 1 % faster; the Python bench did not pre-warm the pool
then, so with pool 1024 each first start of an entry pays `NewRun`, ~16 ms; status counts OK/RESET only — 0 SIM_ERROR,
0 CONTENT_GAP, 0 ILLEGAL_ACTION). Since E2c `sts2vec bench` warms the pool by default (`sts2vec_pool_warm`, warm time
printed separately); these rows are today's `--no-warm`. Warm against cold at N = 64 / 8 threads: pool 64, 200 steps
55,168 against 15,859; pool 1024, 500 steps 82,852 (warm 1,024 templates in 2.9 s) against 5,664 (`sim/VEC.md`, Bench):

| N | threads | pool | env-steps/s | ms/batch | obs MB/batch | resets/s | first reset ms | load avg |
|---|---|---|---|---|---|---|---|---|
| 64 | 1 | 1024 | 4,389 | 14.58 | 1.7 | 61 | 1,549 | 4.3 |
| 64 | 8 | 1024 | 5,682 | 11.26 | 1.7 | 79 | 521 | 4.1 |
| 1024 | 1 | 1024 | 23,798 | 43.03 | 27.3 | 331 | 9,900 | 3.9 |
| 1024 | 8 | 1024 | 73,194 | 13.99 | 27.3 | 1,017 | 1,859 | 3.1 |
| 4096 | 1 | 1024 | 39,685 | 103.21 | 109.1 | 553 | 14,825 | 4.3 |
| 4096 | 8 | 1024 | 162,928 | 25.14 | 109.1 | 2,272 | 2,548 | 4.1 |
| 64 | 1 | 64 | 12,118 | 5.28 | 1.7 | 170 | 1,109 | 5.4 |
| 64 | 8 | 64 | 20,740 | 3.09 | 1.7 | 290 | 441 | 5.4 |
| 1024 | 1 | 64 | 29,561 | 34.64 | 27.3 | 417 | 1,430 | 5.5 |
| 1024 | 8 | 64 | 54,137 | 18.91 | 27.3 | 764 | 551 | 5.6 |
| 4096 | 1 | 64 | 39,899 | 102.66 | 109.1 | 565 | 1,486 | 7.0 |
| 4096 | 8 | 64 | 86,682 | 47.25 | 109.1 | 1,227 | 557 | 4.7 |

Random soak — `sts2vec bench --envs 1024 --steps 2000` (threads 8): seed 0 pool 64: 96,869 env-steps/s, 29,864
episodes, 2,048,000 actions, status OK 2,018,136 / RESET 29,864, **0 SIM_ERROR, 0 CONTENT_GAP, 0 ILLEGAL_ACTION**;
seed 1 pool 1024 prefix SOAK: 127,325 env-steps/s, 29,449 episodes, OK 2,018,551 / RESET 29,449, 0 errors. 0 wins in both.

Combat bench (`dotnet run -c Release --project sim/Sts2Sim.Bench`, NIBBITS_WEAK, 5 s, load average 7.1):

| Row | decisions/s | Clone() | StateHash() |
|---|---|---|---|
| E2 integrate | 142,439 | 2.172 us | 2.625 us |

## Linux x86_64 (Huginn)

2026-11-01, commit `7527635` (full test suite: `a20ed62`, same `sim/` code), `scripts/linux_validate.sh`; details in
`docs/linux-validation-2026-11-01.md`. Machine: Intel Xeon Platinum KVM guest, 4 cores x 2 threads = 8 vCPU, 30 GB,
Ubuntu 24.04.4, .NET SDK 9.0.320 / runtime 9.0.22 (user-level `$HOME/.dotnet`), Release, Server GC for vec rows, idle
host (load average 0.00 before the run). Mac column: Apple M4 Pro, same commit, same session (load average 2 to 7).
All vec rows: A0, pool 1024 fresh entries (Python: default pool 64), 0 sim_error / content_gap / illegal / truncated,
0 wins; episode counts identical on both platforms (cross-platform determinism; combat hash sink `40788C706BFA21E0`
on both).

| Row | Huginn x86_64 | Mac M4 Pro | Mac / Huginn |
|---|---|---|---|
| Combat playouts, NIBBITS_WEAK, 5 s (decisions/s) | 41,400 | 147,223 | 3.6x |
| Clone() | 8.595 us | 2.325 us | 3.7x |
| StateHash() | 4.532 us | 2.554 us | 1.8x |
| C# vec 1024 x 8, 500 steps (env-steps/s; ms/batch) | 28,072; 36.5 | 114,818; 8.9 | 4.1x |
| C# vec 1024 x 8, 3000 steps (steady state) | 50,452; 20.3 | 179,715; 5.7 | 3.6x |
| C# vec 1024 x 1, 500 steps | 10,431; 98.2 | 26,436; 38.7 | 2.5x |
| C# vec 4096 x 8, 500 steps | 44,965; 91.1 | 125,397; 32.7 | 2.8x |
| C# vec 64 x 8, 500 steps | 21,796; 2.9 | 65,356; 1.0 | 3.0x |
| Python `sts2vec bench --envs 1024 --threads 8 --steps 500` (hostfxr JIT) | 21,556; 47.5 | 47,739; 21.5 | 2.2x |
| Same, NativeAOT `Sts2Sim.Vec.so` (`STS2VEC_AOT_LIB`) | 61,425; 16.7 | — | — |
| Full test suite (1103 tests, single-threaded runner) | 1171 s | 420 s | 2.8x |

§D7 (≥ 50k env-steps/s at 1024 x 8): met on Huginn at steady state (50.5k over 3000 batches) and with NativeAOT
(61k at 500); the 500-batch JIT row (28k) is mostly tiered-JIT warm-up on 4 cores (`DOTNET_TieredCompilation=0`:
51.8k at 500; probe in the doc §3.3).

### Container (Docker image, linux/amd64, Huginn)

2026-11-02, image built from `ebc055f` (`Dockerfile`, runtime .NET 9.0.22, Server GC), same machine as the native
rows above, idle host; details in `docs/docker.md` (Validation, amd64). Tokyo arm64 container (2 vCPU) for reference.

| Row | Huginn container | Huginn native | Tokyo arm64 container |
|---|---|---|---|
| Combat playouts, NIBBITS_WEAK, 5 s (decisions/s) | 41,203 | 41,400 | - |
| Clone() / StateHash() | 8.551 / 4.481 us | 8.595 / 4.532 us | - |
| C# vec 1024 x 8, 500 steps (env-steps/s; ms/batch) | 27,956; 36.6 (rerun 28,278; 36.2) | 28,072; 36.5 | 19,178 at 1024 x 4 |
| C# vec 1024 x 8, 3000 steps | 50,900; 20.1 | 50,452; 20.3 | - |
| Python `sts2vec bench --envs 1024 --threads 8 --steps 500` (pool 64) | 19,140; 53.5 (rerun 21,065; 48.6) | 21,556; 47.5 | 15,664 at 1024 x 4 |
| `selftest 200` (random full runs, play-service path) | 18.6 s, 0 crashes | - | 13.3 s |
| 20 random games over HTTP (`sim_play.py`, host -> published port) | 4.19 s, 1,379 steps | - | 4.2 s, 1,379 steps |

Episode counts (7,286 at 500, 44,417 at 3000), Python status counts (OK 504,778, RESET 7,222) and the combat hash sink
`40788C706BFA21E0` match the native and Mac rows: no container overhead and no cross-architecture drift.

## Vec fast path: clone-free stepping (2026-11-02, branch `j/fast-path`)

`RunSim.StepFast` skips the per-step deep clone when the static prompt-site predicate `RunSim.MayPrompt` proves the
action cannot reach a choice prompt (`sim/VEC.md` "Fast path"); `config.fast_path` (default true) turns it on in Vec.
Mac M4 Pro, **heavily loaded host** (load average 14–30: an E3 farm of game instances plus other agents' test runs), so
the rows are medians of 3 interleaved on/off runs; treat ±15 % as noise. Uniform-random candidate policy, five
characters, pool 1024 warmed, `DOTNET_gcServer=1`, 1024 envs x 8 threads x 500 batches.

| Row | fast path steps | env-steps/s (median of 3; runs) | process CPU µs / env-step (median) | min ms/batch | GC gen0/1/2 in window |
| --- | --- | --- | --- | --- | --- |
| A0, `fast_path: false` | 0 % | 77,007 (64,756 / 77,007 / 81,982) | 88.3 | 9.49 | 16 / 8 / 1 |
| A0, `fast_path: true` | 83.2 % | 83,209 (83,209 / 79,306 / 92,820) | 79.3 | 8.69 | 4 / 2 / 0 |
| A10, `fast_path: false` | 0 % | 81,435 (80,399 / 81,435 / 86,919) | 86.3 | 8.91 | 15 / 7 / 1 |
| A10, `fast_path: true` | 82.0 % | 87,292 (87,292 / 85,644 / 105,752) | 79.1 | 8.13 | 5 / 3 / 1 |

End to end the gain is modest: +7–8 % env-steps/s, −9–10 % CPU per env-step, about 3x fewer gen0/gen1 collections.
The clone is a small share of a Vec step at this size: a single-threaded RunSim step costs 13–15 µs with the clone and
≈ 7.8 µs on the fast path (below), while a Vec env-step costs ≈ 80 µs of CPU, most of it outside `Step`
(observation encoding, `LegalActions`, candidate table, resets). The 1.7–2.6x quoted earlier for `Step(a, answerer)` is
the `Step` call alone, not the env. Buffers and `StateHash` are byte-identical with the fast path on and off
(`VecTests.Test_Vec_FastPathMatchesCloningPath`; the bench's episode counts match too: 7,286 ended at A0, 11,345 at A10).

`dotnet run -c Release --project sim/Sts2Sim.Bench -- fastpath [runs] [ascension]` (single thread; same uniform-random
trajectories replayed through `Step` and `StepFast`; only the step call is timed; final-state hash XOR compared):

| Row | runs / steps | fast fraction | µs/step, cloning `Step` | µs/step, `StepFast` (incl. `MayPrompt`) | `MayPrompt` alone | speedup |
| --- | --- | --- | --- | --- | --- | --- |
| A0 | 1,000 / 68,089 | 0.832 | 13.02, 12.68 | 7.80, 7.56 | 0.19 | 1.67x, 1.68x |
| A10 | 1,000 / 43,992 | 0.824 | 14.68, 12.65 | 7.89, 7.83 | 0.20 | 1.86x, 1.62x |

Soak fast fraction (`ChoiceFastPathTests`, 2,000 runs incl. play-biased and 999-HP deep runs that reach events, shops
and bosses): 72.1 % of 577,867 steps; blockers in order: presence relic 31k, pending prompt 26k, event room 23k,
presence card 22k, creature (power/monster) 19k, played prompt card 14k, travel 13k, room action 8k.

### Docker host capacity (Huginn container, 2026-11-02, `docs/capacity-2026-11-02.md`)

8 vCPU (4 cores x 2 threads); image `sts2sim:latest` with the NativeAOT `Sts2Sim.Vec.so`; rerun with
`bash scripts/cluster_capacity.sh OUT all` on the docker host.

| Row | Result |
| --- | --- |
| Play server, 1 server x 1 `sim_play.py random --runs 200` client | 7.43 games/s, 513 steps/s; server 105 % CPU (2.0 ms CPU per step, 0.14 core-s per game) |
| Play server, 4 servers x 1 client / 8 x 1 / 1 x 8 / 8 x 2 | 21.6 / 26.2 / 26.5 / 28.1 games/s (host saturated at ~26-28 with clients on the host; 3.3-3.5 games/s per vCPU) |
| Play server memory | 110-150 MiB per container under load, peak <= 193 MiB |
| `sts2eval run --policy random --concurrency 32 --urls` (1 / 4 / 8 servers, 200 games) | 6.81 / 7.17 / 6.93 games/s (one runner process is CPU-bound at ~1 core); 4 runner processes x 4 servers 11.5 |
| `sts2vec bench` AOT, 1024 envs x 4 / x 8 threads (500 steps) | 51,160 / **62,109** env-steps/s (12.8k per physical core); peak 0.46 GiB |
| `sts2vec bench` AOT, 4096 envs x 4 / x 8 threads (300 steps) | 50,968 / **63,141** env-steps/s; peak 0.67 GiB |
| `sts2vec bench` hostfxr (`-e STS2VEC_AOT_LIB=`), 1024 x 8 / 4096 x 8 | 21,827 / 26,655 env-steps/s (AOT is 2.8x / 2.4x) |

## J integrate (2026-11-02)

Integrated tree (`6df05f0` + regenerated prompt-site table / kernel map), Mac M4 Pro, `DOTNET_gcServer=1`,
`dotnet run -c Release --project sim/Sts2Sim.Bench -- vec 1024 8 500 <asc> 1024 <fast_path>`: uniform-random candidate
policy, five characters, pool 1024 warmed. Three interleaved off/on pairs per ascension; the host load average rose from
5 to 14 during the series (other agents and the E3 farm), so treat ±15 % as noise. A single unpaired A0 fast-path run at
load ≈ 2 just before the series gave **126,522 env-steps/s, 61.0 CPU µs/env-step** (min 7.37 ms/batch).

| Row | fast path steps | env-steps/s median (runs) | CPU µs / env-step median (runs) | min ms/batch (best) | GC gen0/1/2 | episodes ended |
| --- | --- | --- | --- | --- | --- | --- |
| A0, `fast_path` 0 | 0 % | 104,858 (104,858 / 94,918 / 109,913) | 73.6 (73.6 / 75.0 / 70.4) | 8.17 | 15-17 / 6-7 / 1 | 7,286 |
| A0, `fast_path` 1 | 83.2 % | 107,522 (110,583 / 107,522 / 93,488) | 69.6 (68.7 / 69.6 / 73.8) | 7.57 | 4 / 2 / 0 | 7,286 |
| A10, `fast_path` 0 | 0 % | 111,391 (103,125 / 111,391 / 112,107) | 69.6 (75.0 / 69.6 / 69.0) | 7.94 | 15-16 / 7-8 / 1 | 11,345 |
| A10, `fast_path` 1 | 82.0 % | 121,284 (121,284 / 123,420 / 120,325) | 63.5 (63.5 / 62.2 / 63.7) | 7.52 | 4-6 / 2-4 / 0-1 | 11,345 |

Reading: the fast path saves 5-9 % CPU per env-step and about 3-4x of the gen0/gen1 collections; wall-clock gain is
within noise at A0 and +9 % at A10. Episode counts are identical on and off (outputs are byte-identical by test). The
remaining ≈ 60-70 CPU µs per env-step is mostly observation encoding, `LegalActions` and the candidate table.
