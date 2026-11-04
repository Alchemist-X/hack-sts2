# Silent A10 training cycle — results (2026-11-03/04, overnight)

Status: FINAL (09:25 EST). **Final pick: s8a u2225** (dev-128 0.372, the pre-registered selection rule), packed as
`silent-a10-s8a-u2225.tar.gz` (31,106,383 bytes, sha256 `4686d6ae74769188…`), model card in
`docs/model-card-silent-a10-u2225-2026-11-04.md`. Runner-up u2536 (test-100 0.373) and the 07:30 fallback u2050 are
packed too (scratchpad only). Compute: 147.6 Colab A100 units (890.85 → 743.21) for the whole cycle, two A100s.

## What was trained

- Policy: `sts2train` PPO, mid preset (d=192, state 384, depth 3, 6 heads; ~2.8M parameters) with
  `--aux-heads` (win probability + end-of-combat HP), `--deck-counts`, `--acq-shaping`
  (upgrade coefficient 0.012 = 3× default; variant 2 also relic 0.02), human-save start points
  (148 Silent A9 saves, mix 0.35), floor-stall cap 80, λ 0.99, entropy 0.02, rollout 64,
  minibatch 4096, 1 epoch, bf16 + `torch.compile default`, loop-breaker in evaluation.
- Environment: PR #30 line (encoder `3b73317739c650bd`, ABI 2): one-step reward macros
  (`TakeRewardCard`/`TakeRewardAlternative`), shop potion rule, `map_routes`, `card_static`,
  stall truncation, fidelity fixes (G3 farm 100 %, human combat gate 99.70 %).
- Information parity: no exact snapshots, no save-reload, single-pass No-SL evaluation; the
  determinized lookahead row re-samples hidden state per clone.
- Compute: two Colab A100s, ~14–19k env-steps/s per GPU after the `F.embedding` hot-spot fix.

## Candidate checkpoints (greedy + loop-breaker, Silent, Ascension 10)

| checkpoint | silent-a10-v1 (20) | silent-a10-dev-v1 (128) | silent-a10-test-v1 (100) | Act-1 boss beaten (dev) | Act 2 reached (dev) |
| --- | --- | --- | --- | --- | --- |
| s8a u925 (best on the 20-game set) | 0.391 [0.332, 0.450] | 0.283 [0.263, 0.304] | 0.295 [0.273, 0.316] | 19/128 | 19/128 |
| s8a u1275 | 0.352 [0.270, 0.433] | 0.325 [0.302, 0.347] | 0.313 [0.288, 0.338] | 38/128 | 38/128 |
| s8a u1300 | 0.353 [0.297, 0.410] | 0.329 [0.309, 0.349] | **0.327 [0.299, 0.355]** | 37/128 | 37/128 |
| s8a u1525 | 0.408 [0.347, 0.468] | **0.335 [0.310, 0.360]** | 0.326 [0.299, 0.353] | 42/128 | 42/128 |
| s8a u1900 (best on the 20-game set, segment 2) | 0.413 [0.340, 0.487] | 0.342 [0.319, 0.365] | **0.365 [0.332, 0.399]** | 47/128 | 47/128 |
| **s8a u2050 (segment-2 end; packed deliverable)** | 0.389 [0.311, 0.467] | **0.351 [0.326, 0.376]** | 0.353 [0.324, 0.383] | 52/128 | 52/128 |
| s8b u2100 (variant 2: + relic 0.02; best on the 20-game set) | 0.412 [0.321, 0.503] | 0.335 [0.311, 0.360] | 0.350 [0.319, 0.380] | 49/128 | 49/128 |
| s8b u2181 (variant-2 segment end) | 0.366 [0.279, 0.452] | 0.343 [0.319, 0.368] | 0.341 [0.309, 0.373] | 48/128 | 48/128 |
| s8a u2225 (segment 3; best on the 20-game set) | 0.427 [0.351, 0.503] | **0.372 [0.348, 0.396]** | 0.346 [0.316, 0.377] | 57/128 | 57/128 |
| s8a u2536 (segment-3 end) | 0.385 [0.301, 0.468] | 0.363 [0.338, 0.388] | **0.373 [0.340, 0.407]** | 52/128 | 52/128 |
| s8b u2475 (segment 3; best on the 20-game set) | 0.423 [0.350, 0.497] | 0.350 [0.327, 0.373] | 0.344 [0.314, 0.373] | 52/128 | 52/128 |
| s8b u2625 (segment-3 end) | 0.357 [0.286, 0.429] | 0.365 [0.340, 0.390] | 0.369 [0.336, 0.403] | 55/128 | 55/128 |

Selection rule (pre-registered): highest dev-128 progress; test-100 is reported, not used for picking.
Order on dev-128: u2225 0.372 > u2625 0.365 > u2536 0.363 > u2050 0.351 > u2475 0.350; all CIs overlap, so the
pick is a rule, not a measured difference. Training curve on the 20-game set oscillated 0.33–0.43 over the last
~800 updates on both lineages: the recipe is at its plateau.
Act-2 boss reached on dev-128: u1900 6/128, u2050 8/128; Act-2 boss beaten 0/128 (u1900 beat it 2/100 on test-100).

Baselines on the same library: random 0.037 / first 0.112 / heuristic 0.117 (silent-a10-v1).
Reference points: Sonnet 5.5 LLM 0.316 on the mixed std-a10-v1 (different seed set); the earlier
end-to-end PPO baseline plateaued at 0.16 (no deck-building).

## Behaviour (probe, silent-a10-v1)

u750: 143 reward screens → 115 cards, relics 14/14 + chest 15/0, boss fights 13 → 2 won,
boss HP left median 30 %, SMITH 0/43 (heal only), 0 stuck. The acquisition channel opened
with the one-step macros + shaping; combat strength and rest-site upgrades remain the gap.

## Lookahead (honest, determinized)

- Pre-signal A10 checkpoint: +0.015 on dev-128 (CI excludes 0), +0.006 on test-100 (n.s.).
- Final candidate u2050 on dev-128 (turn horizon, value+hp, K=8): lookahead 0.274 [0.255, 0.292]
  vs greedy-without-loop-breaker 0.282 [0.262, 0.302]; paired Δ +0.008 [−0.011, +0.027]
  (40 better / 32 worse / 56 same) — not significant. The greedy policy WITH the loop-breaker
  (0.351) is the deliverable; the search needs the loop-breaker and a better leaf score before
  it pays (docs/lookahead-2026-11-04.md).

## Real game (live twin, isolated headless instance, STS2_MCP eval API)

u2050 (the 07:30 fallback; u2225 was not replayed before the 10:00 cut), greedy + loop-breaker, Silent A10 standard runs seeded through the eval API; the sim twin
runs in lockstep and every decision is checked against the game's state (floor, act, HP, gold,
energy, block, hand, enemies, potions, relics, screen contents). After three runner fixes found by
these runs (event reward-screen exit, id-less relic rewards, double cancel on the upgrade preview;
PR #30 merge 0fa1fa3), both runs were desync-free and identical to their dry runs decision by decision:

| seed | decisions | desync / resync | result | wall time |
| --- | --- | --- | --- | --- |
| TW1NA10S02 | 233 | 0 / 0 | floor 17, killed by THE_KIN_BOSS (Act-1 boss), score 442 | 453 s |
| TW1NA10S03 | 228 | 0 / 0 | floor 17, killed by WATERFALL_GIANT_BOSS (Act-1 boss), score 348 | 433 s |

Each run had one loop-breaker intervention (rest site: HEAL instead of a repeated SMITH), the same
as in the dry run; the win-probability head stayed ≈ 0 throughout. Before the fixes, S03 reached
Act 2 floor 22 only because a fallback action took a different path (that run is kept under
`output/nn-twin/…-S03-real-c89a0bc`, not counted). The user's Steam install, saves and recorder
sessions were not touched (control-channel mod stays stashed). How to watch a run yourself:
`scripts/live/README-nn-twin.md`.

## Known limits / next cycle

- 0 clears at A10 on any set; the competitor's NoSL single model reports ~75 % with billions
  of decisions — this cycle used ~0.2 B. Levers next: combat-focused curriculum from combat
  snapshots, a leaf score from the new heads, ExIt once the lookahead gain clears +0.02,
  async collection to use the idle CPU.
