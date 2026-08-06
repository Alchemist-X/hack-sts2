# No-SL Ascension 10 training and evaluation protocol

Status: proposed benchmark contract (2026-08-07)

## Executive summary

`hack-sts2` currently provides the environment needed to train and evaluate an
agent, but it does **not** yet contain a trained policy checkpoint or a measured
agent win rate. The official game engine can run headlessly, expose structured
observations and legal actions, execute arbitrary policy callables, and record
compact transitions. Two concurrent workers have been verified on macOS arm64.

The next credible milestone is not “claim A10” from a few selected runs. It is a
version-locked, leakage-free, no-save/load ladder in which one frozen policy wins
all 11 difficulty levels from A0 through A10 while every decision and terminal
result is recorded.

## Current capability and missing pieces

| Capability | Current state |
|---|---|
| Official-engine text environment | Implemented and verified on macOS arm64 |
| Complete indexed legal actions and action masks | Implemented for the supported observation surfaces, with runtime audit metadata |
| Limited and omniscient information contracts | Implemented and physically separated |
| Concurrent independent episodes | Implemented; two live workers verified |
| Compact `(s, A(s), a, r, s', done)` JSONL | Implemented |
| Full passive human trajectory recorder | Implemented |
| Learned policy/value checkpoint | **Not implemented** |
| Measured agent win rate by ascension | **No result yet** |
| `reset(character, ascension)` | **Not implemented**; reset currently selects only a character |
| Certified no-save/load enforcement | **Not implemented** |
| Exact arbitrary mid-combat clone/restore | **Not implemented** and not required for the ladder itself |

Therefore the environment is ready for policy development, but no statement such
as “the current model can beat A5/A10” is currently evidence-based.

## Define the target before training

STS2 uses A10 for its highest ascension difficulty. The complete difficulty range
is A0 through A10. Accordingly, the strict ladder in this document means winning
A0, A1, ..., A10 in order: 11 sequential wins ending with an A10 victory.

Qualification conditions:

- one fixed character;
- one fixed game version, build, mod set, observation schema, and policy checkpoint;
- `limited` information mode unless an explicitly separate oracle track is reported;
- a fresh seed committed to the manifest before each run;
- no seed rejection, manual intervention, run restart, or alternate branch;
- ascension increases by exactly one only after a verified victory;
- death, abandon, illegal action, policy timeout, or process crash ends the streak;
- all 11 runs are sequential for qualification, even if training rollouts were parallel;
- autosaving is allowed, but loading an earlier state or restarting a run is not.

For a strict public result, infrastructure failures count as failures. A separate
engineering report may mark a run void only if it proves that no action was
committed and no game state advanced; voided runs cannot be included in the
headline streak.

## Why the goal requires a very strong per-run policy

Let `p_a` be the policy's true win probability at ascension `a`. The probability
of completing one A0-to-A10 attempt is

\[
P(\text{11-win streak})=\prod_{a=0}^{10}p_a.
\]

If all ascensions had the same win probability `p`, then:

| Per-run win rate | Chance of 11 consecutive wins |
|---:|---:|
| 80% | 8.6% |
| 90% | 31.4% |
| 95% | 56.9% |
| 98% | 80.1% |

A 50% chance of completing an 11-run attempt requires approximately a 93.9%
per-run win rate under the equal-rate simplification. In reality later ascensions
will be harder, so an average win rate hides the exact weaknesses that destroy a
streak. Report `p_a` and uncertainty separately for every ascension, then compute
the product.

## Required benchmark harness

Before training a large model, add a small, auditable ladder runner:

1. Extend reset to accept and verify an ascension level before embark.
2. Add a ladder manifest containing `streak_id`, rung, ascension, character,
   seed, game/build/schema hashes, policy hash, information mode, and previous
   trajectory hash.
3. Hash-chain every run so a removed, reordered, or replaced trajectory is
   detectable.
4. Reject any run that invokes `SetUpSavedSinglePlayer`, increments the game's
   reload counter, reuses the same run ID after process restart, or contains an
   unrecorded action.
5. Run the compact transition writer for training and the full recorder/native
   replay for qualification. Cross-check their action counts and terminal result.
6. Persist a terminal reason: victory, death, abandon, illegal action, timeout,
   process exit, recorder mismatch, or reload detected.
7. Stop the ladder immediately on the first non-victory result.

The game may continue writing its normal crash-recovery save. “No SL” means the
runner never consumes that save to retry or change a result.

## Training path

### 1. Establish baselines

Run the same versioned seed suites with:

- random legal actions as an environment sanity check;
- the existing deterministic driver as an engineering baseline, not a strong
  gameplay claim;
- a constrained LLM policy that can choose only enumerated legal actions;
- behavior cloning from recorded human decisions.

Measure complete-run win rate, illegal-action rate, timeout rate, run length, and
trajectory completeness. Do not begin expensive RL until complete A0/A1 episodes
can run unattended with zero illegal actions and zero human-save contamination.

### 2. Train a structured recurrent policy and value model

Use separate encoders for cards, relics, potions, enemies, map state, and scalar
resources. Encode actions using their structured parameters rather than a global
fixed action index, then apply the engine-provided action mask. Because the public
state does not reveal all RNG and draw-order information, include recent decision
history with a GRU or Transformer rather than assuming a fully observed MDP.

Recommended order:

1. behavior-cloning pretraining on human trajectories;
2. terminal win-value and auxiliary survival/resource prediction;
3. offline policy improvement on recorded data with conservative out-of-distribution
   handling;
4. online actor-learner fine-tuning from parallel independent official-engine runs;
5. distillation of expensive LLM/search decisions into the fast structured policy.

The LLM is most useful as a high-level planner, teacher, and unfamiliar-content
fallback. The learned policy/value model should execute routine combat decisions
quickly and provide calibrated probabilities. All LLM output remains constrained
to the legal-action set.

### 3. Use an ascension curriculum without forgetting earlier levels

Start at A0/A1 and unlock the next training band only after the current policy
passes a predefined win-rate and reliability gate. Continue sampling earlier
ascensions after promotion; otherwise optimizing A10 can destroy the consistency
needed for the full ladder.

For each ascension, maintain a fixed held-out seed suite and a separate calibration
set. Promotion should use a confidence bound, not the point estimate alone. The
final streak probability estimate is the product of the per-ascension posterior
estimates and should be reported with uncertainty.

### 4. Freeze before qualification

Once the estimated ladder probability is credible:

- freeze the policy/value/LLM versions and decoding settings;
- freeze the game build and all environment code;
- run A0 through A10 sequentially with recording always on;
- publish the complete attempt, including failed attempts, not only the first
  successful streak;
- report attempts-to-first-streak and total successful streaks over total attempts.

One certified 11-win streak demonstrates feasibility. A capability claim should
also report a denominator; for example, multiple successful ladders from a fixed
number of predeclared attempts.

## Milestone gates

1. **Harness gate:** selectable ascension, no-SL detector, hash-chained manifests,
   and full/compact recorder cross-checks.
2. **Reliability gate:** at least 100 unattended complete runs with zero illegal
   actions, zero human-save writes, and an explicitly reported infrastructure
   failure rate.
3. **Baseline gate:** measured per-ascension win rates for deterministic, LLM, and
   behavior-cloned policies.
4. **Curriculum gate:** a frozen policy clears held-out evaluation suites through
   progressively higher ascensions without losing earlier-level performance.
5. **Qualification gate:** one frozen limited-information policy completes the
   strict A0-to-A10 ladder with all 11 trajectories and no load/retry event.
6. **Replication gate:** repeat predeclared ladder attempts and publish both
   successes and failures to estimate the true streak probability.

The immediate engineering priority is gate 1. Training before ascension selection,
no-SL enforcement, and complete-run recording are reliable would produce a model
whose headline result cannot be audited.
