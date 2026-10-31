# Sts2Sim benchmark (P0)

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

## P0 gates

| Gate | Target | Measured | Status |
|---|---|---|---|
| Throughput | >=1000 decisions/s/core | 705k latest, >=158k worst | PASS (>=158x margin) |
| Clone cost | <=1 ms | 2.5 us latest, ~4 us worst | PASS (>=250x margin) |

Caveat: these numbers come from a single small encounter with a 10-card deck. Costs will rise as more cards, powers and monsters are ported, so re-run the bench after each content batch.
