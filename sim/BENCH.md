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
