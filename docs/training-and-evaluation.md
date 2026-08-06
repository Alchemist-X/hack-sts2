# Training and evaluation

This document defines how `hack-sts2` turns recorded decisions into models and
how those models must be evaluated. The target is not short-term damage or one
fight's survival probability: at every legal decision, the policy should choose
the action that maximizes the probability of eventually completing the declared
run objective.

## Safety and execution boundary

The human-play installation and the research environment are separate systems.

- **Do not launch, attach to, restart, or stop the local graphical game for
  development or CI.** A human may be playing while the Python pipeline runs.
- Local and GitHub CI run only pure-Python dataset, model, and unit tests. They do
  not build .NET code and do not start the official game or a headless worker.
- An integration rollout may use only an isolated text/headless worker. On the
  local Mac, defer even a headless integration run until human play has stopped.
- Never read from or write to the human save directory from a training worker.
  Each worker gets its own home, saves, ports, logs, and trajectory directory.
- No screenshots or keyframes are inputs to this pipeline. Observations, legal
  actions, masks, rewards, and episode boundaries are structured records.

The existing Google Cloud node is **ARM64**. It can run the portable Python
dataset, training, and offline-evaluation stages, but it cannot run the official
Linux STS2 engine because the available game binary is built for `x86_64`, which
is incompatible with the node's architecture and executable format. Online
rollouts on Google Cloud therefore remain blocked until an `x86_64` worker is
provisioned and the Linux headless packaging is verified. Architecture emulation
is not an accepted benchmark configuration.

| Machine | Allowed now | Not allowed / blocked |
|---|---|---|
| Local macOS arm64 | Pure-Python development and offline training | Automation must not touch the active human game; no GUI tests |
| Existing GCP ARM64 node | Dataset build/audit/split, BC/value training, offline evaluation | Official Linux game and online rollouts (`x86_64` blocker) |
| Future GCP x86_64 node | Isolated headless workers after verification | GUI, Steam dependency, or access to human saves |
| GitHub Actions | Pure-Python tests on clean Linux runners | .NET build, official game, headless integration tests |

## Reproducibility contract

Every dataset, checkpoint, and benchmark report must retain or reference an
immutable manifest containing:

- exact game version/build, observation schema, recorder/controller version, and
  repository commit;
- character, ascension, victory condition, unlock state, mod set, and reward
  definition;
- information mode (`limited` or a separately reported privileged/oracle track);
- SL/NoSL rules and, for SL, the retry/search budget;
- seed-suite hash and the train/validation/test split manifest;
- model/checkpoint hash, model configuration, and any LLM provider/model/decoding
  settings;
- terminal reason and trajectory hash for every episode.

Split data by **seed/run**, never by individual transition. All transitions from
one run belong to one split. Freeze the test seeds before model selection, keep
human-recorded test runs out of training, and do not mix game versions unless the
report explicitly measures cross-version transfer. The limited track must reject
RNG state, true future draw order, future rewards/shops, and all `privileged_*`
fields before serialization, rather than relying on the model to ignore them.

## Portable CLI

Run commands from `hack-sts2/sts2rec`. Outputs below live in ignored directories
so trajectories and checkpoints are not accidentally committed.

```bash
mkdir -p ../datasets ../artifacts ../checkpoints

# Convert one or more compact transition streams into a normalized dataset.
uv run sts2train dataset build \
  ../headless-instances/inst1/trajectories/transitions.jsonl \
  ../headless-instances/inst2/trajectories/transitions.jsonl \
  -o ../datasets/limited-all.jsonl

# Convert passive human-recorder sessions. Only uniquely aligned, confirmed
# decisions pass the BC quality gate; unfinished runs have no value labels.
uv run sts2train dataset build-human <session-dir> \
  -o ../datasets/human-all.jsonl

# Fail fast on malformed transitions, leakage, illegal chosen actions, or broken
# episode boundaries, then produce run-level splits.
uv run sts2train dataset audit ../datasets/limited-all.jsonl
uv run sts2train dataset audit ../datasets/limited-all.jsonl --task value
uv run sts2train dataset split ../datasets/limited-all.jsonl \
  --output-dir ../datasets/splits

# Portable baseline models. These commands do not start the game.
uv run sts2train train bc ../datasets/splits/train.jsonl \
  -o ../checkpoints/bc.json
uv run sts2train train value ../datasets/splits/train.jsonl \
  -o ../checkpoints/value.json

uv run sts2train evaluate bc ../datasets/splits/test.jsonl \
  --model ../checkpoints/bc.json
uv run sts2train evaluate value ../datasets/splits/test.jsonl \
  --model ../checkpoints/value.json

# Aggregate already-recorded episode outcomes. This is offline aggregation; it
# does not launch a worker.
uv run sts2train benchmark nosl ../artifacts/nosl-outcomes.jsonl
uv run sts2train benchmark sl ../artifacts/sl-outcomes.jsonl --budget 8

# Formal reports additionally bind every outcome to an exact, frozen case
# manifest. Missing/extra cases or a fingerprint mismatch fail the command.
uv run sts2train benchmark nosl ../artifacts/nosl-outcomes.jsonl \
  --spec ../artifacts/nosl-spec.json
```

These commands form the stable portable boundary. More expensive neural or RL
trainers may consume the same split files and emit richer checkpoints without
changing the dataset and benchmark contracts.

Without `--spec`, benchmark commands are exploratory aggregations only. A formal
NoSL/SL result requires one `spec_fingerprint` on every outcome and an exact match
to the supplied manifest's game build, environment, policy, ordered seeds,
ascensions, information mode, victory condition, timeout, maximum step count,
mod/unlock state, controller version, and retry budget. A spec-bound NoSL outcome
must also declare `reload_count: 0`, `trajectory_complete: true`, and a non-empty
`trajectory_hash`; missing evidence fails the command. A spec-bound SL report
requires a complete trajectory hash for every attempt, rejects attempts beyond
the declared maximum budget, and rejects any attempt recorded after that case's
first victory.

The formal CLI also requires each outcome to name a `trajectory_path`. It hashes
the referenced file and compares it with `trajectory_hash`; a self-reported hash
without an artifact is insufficient. Outcomes must declare `steps` and
`elapsed_s`, both within the spec's frozen `max_steps` and `timeout_s` limits.

`dataset audit` and `dataset split` default to the behavior-cloning eligibility
gate. Pass `--task value` when preparing terminal-value data; this rejects
unfinished human sessions and every row without a terminal win/loss target.
Omniscient baselines are opt-in via `train ... --use-privileged` and reject mixed
limited rows. The privileged observation remains physically separate in both the
trajectory and checkpoint contracts.

### Storage discipline

- Keep only compact transition JSONL for routine training; enable the full
  recorder/native replay only for diagnostics and qualification runs.
- Stream records to disk and audit before copying them. Do not duplicate the game
  bundle or `headless-instances/runtime` inside a dataset directory.
- Use one shared immutable engine runtime plus small per-worker writable homes.
- Record dataset/checkpoint hashes in reports. Move cold raw sessions to external
  or cloud storage only after the normalized dataset passes audit.
- Check `du -sh datasets artifacts checkpoints headless-instances` before a large
  run. Set a retention policy for failed experiments; never delete the sole copy
  of a qualification trajectory.

## Training pipeline

The stages are ordered so that an expensive online learner is not used to debug
data integrity or action masking.

### Stage 0: data and environment gate

Canonicalize human and worker trajectories into transitions of the form
`(observation, legal_actions, chosen_action, reward, next_observation, done)`.
Audit every transition for schema validity, leakage, a chosen action present in
the legal set, a mask aligned one-to-one with that set, monotonic episode order,
one stable split group per seed/episode, and exactly one final terminal transition
per completed text-worker episode.
For passive human data whose final canonical action is filtered out, value
eligibility instead requires consistent session-level completion and terminal
outcome metadata; incomplete human runs remain BC-only.

The present text controller may emit a terminal snapshot containing only
`{"state_type":"game_over","game_over":{"message":"Run ended."}}`. This proves
that the episode ended, but not whether it was a victory. The environment writes
`info.win: null` and zero sparse reward for this unknown case; the dataset gate
then rejects the episode from terminal-value training while retaining otherwise
sound chosen-action labels for behavior cloning. Generic experiment fields such
as `metadata.result`, and self-reported top-level transition labels, are never
accepted as substitutes. A text-worker run becomes value-eligible only after the
controller supplies a verified explicit outcome. Human-manifest proof requires
both the canonical human schema and source marker. Do not reinterpret an unknown
outcome as a loss.

Legacy text-worker files may contain an inferred `info.win: false` for the same
message-only terminal shape. The converter intentionally ignores that field
unless `info.terminal_outcome_known: true` is present or the engine snapshot
itself carries an explicit result. Consequently, pre-fix terminal value labels
fail closed instead of silently preserving contaminated losses.

Passive recorder actions use a different vocabulary from text-worker commands.
The human adapter currently aligns only confirmed, unique mappings (including
end turn, card play by card model and target, map movement, event choices, and
rest-site choices). Unsupported or ambiguous actions remain visible as rejected
rows with an explicit reason; they are never silently coerced into a label.

Do not proceed to online RL until the controller exposes authoritative terminal
outcomes and at least 100 unattended headless episodes can be completed with zero
illegal actions, zero human-save writes, and a reported timeout/process-failure
rate. Unknown outcomes, environment failures, and gameplay losses are three
different cases and must remain distinguishable.

### Stage 1: behavior cloning

Train a structured, masked policy from human decisions. Encode the public
observation and recent history, encode each currently legal action with its
parameters (card, target, shop item, map node, and so on), score each candidate,
then normalize only across that state's legal set. A GRU or causal Transformer is
appropriate because the limited observation is not fully Markov.

For demonstrated action `a_t`, minimize masked negative log likelihood:

```text
L_BC = -mean_t log pi(a_t | h_t, A_t)
```

Start with equal transition weights and report results by decision type. Later,
run-level or outcome weighting can be an ablation, but it must not silently turn
survivorship bias into a claim of optimal play.

### Stage 2: terminal value and calibration

Train `V(h)` to estimate eventual run success from the limited history and train
`Q(h,a)` when action-conditioned targets are available. The basic terminal target
is the final run outcome `Y` propagated to decisions from that same run:

```text
L_value = -mean_t [Y log V(h_t) + (1-Y) log(1-V(h_t))]
```

Auxiliary predictions such as next-floor survival, HP/resource deltas, and act
completion may improve representation learning, but the published value remains
a calibrated probability of the declared terminal victory. Fit calibration only
on the validation split and freeze it before test evaluation.

### Stage 3: conservative offline policy improvement

Improve over cloning without trusting arbitrary legal-but-unseen actions. A legal
action mask proves executability, not that the offline dataset contains enough
evidence to estimate its long-term value. Use a conservative method such as IQL
with advantage-weighted policy regression, or a discrete CQL penalty evaluated
over the per-state legal candidates. Compare every checkpoint against BC on the
same held-out runs and reject regressions in calibration, rare decision types, or
illegal-action reliability.

Do not label observational action-value differences as causal per-step loss. A
reported decision loss,

```text
loss_t = max_a Q(h_t, a) - Q(h_t, a_t),  a in A(h_t),
```

must name the estimator, its data support, calibration error, and uncertainty.
Exact counterfactual labels require valid engine rollouts or deterministic prefix
replay; copying a room-level recovery save is not an exact mid-combat branch.

### Stage 4: online NoSL reinforcement learning

Fine-tune only in isolated official-engine headless workers. Use independent
episodes, action masking, recurrent state, and an actor-learner algorithm suitable
for variable-length episodes (for example PPO or V-trace). The optimization target
is terminal victory with `gamma = 1`; shaped rewards may be training auxiliaries
but must not redefine benchmark success.

Use an ascension curriculum starting at A0/A1. Promote only after a frozen
checkpoint clears a predeclared lower confidence-bound gate on held-out seeds, and
continue sampling earlier levels to prevent forgetting. NoSL rollouts may not
reload, retry, reject a seed, or use information learned from a failed branch.

This stage can run locally only through isolated headless workers after human play
has stopped. The present ARM64 GCP node cannot run it. Cloud scaling requires a
verified `x86_64` Linux worker image first.

### Stage 5: LLM teacher and bounded search

An LLM is useful as a high-level teacher for sparse/novel decisions, not as an
unconstrained actuator. Give it the same limited observation and enumerated legal
actions, require it to return one action index, validate that index, and record
model/version, prompt hash, latency, tokens, and cost. Distill teacher action
preferences into the fast structured policy.

Search-assisted labels must declare their information and retry budget. SL search
results may train a student, but an SL solve rate is never reported as a NoSL win
rate. A limited-information NoSL evaluation must not receive oracle rollouts,
future outcomes, hidden RNG state, or teacher feedback generated from the held-out
test trajectory.

## Evaluation protocol

### Offline model selection

Use one frozen run-level split and report counts overall and by character,
ascension, act, and decision type.

| Component | Primary metrics | Required diagnostics |
|---|---|---|
| Dataset | completed episodes, transitions, terminal coverage | schema/version counts, leakage findings, legal/mask failures, duplicate seeds |
| Behavior cloning | top-1 legal-action accuracy, negative log likelihood | top-k accuracy, decision-type breakdown, illegal-action rate (must be 0 after masking) |
| Win value | log loss and Brier score | ROC-AUC when both classes exist, calibration error/reliability bins, predicted-vs-observed win rate |
| Action value | held-out log loss/Brier where labels are valid | coverage, uncertainty, per-decision loss distribution, estimator provenance |

Offline action accuracy is not a win-rate claim. It measures agreement with the
recorded player and can reward imitation of a bad move.

### NoSL online benchmark

Before evaluation, freeze the game build, environment commit, policy/checkpoint,
seed list, character, ascension, information mode, timeout, and victory condition.
Commit each seed before its episode starts. Each seed receives exactly one attempt;
death, abandon, illegal action, timeout, or process crash is a failed attempt.
A detected reload or missing trajectory proof invalidates formal NoSL
qualification instead of being silently omitted or replaced with another run.
Preserve every trajectory, including failures and invalid attempts.

Report:

- wins / attempts and pass rate for every ascension;
- a 95% Wilson confidence interval for each binomial pass rate;
- observed maximum streak and the full distribution of streak lengths;
- the strict A1-to-A13 qualification outcome and all attempts-to-first-streak;
- illegal-action, timeout, reload-detection, process-failure, and incomplete-
  trajectory rates;
- decisions per second, wall time per episode, and bytes per transition.

For per-ascension win probabilities `p_a`, the estimated probability of an
A1-to-A13 streak is `product(p_a, a=1..13)`. Report the per-ascension estimates and
uncertainty, not only this product. One successful streak demonstrates feasibility;
a capability claim also requires the predeclared number of attempts and failures.

The current portable aggregator implements frozen-case validation, pass rate,
Wilson intervals, per-ascension slices, terminal reasons, and generic consecutive
win streaks. A dedicated A1-to-A13 ladder runner, ladder-boundary statistics,
throughput/resource accounting, and attempts-to-first-qualification remain
promotion-gate work; the generic streak field must not be presented as an A1-to-A13
qualification result.

### SL search benchmark

Group attempts by seed and permit at most the declared budget `B`. Report
`SL@B solve rate` as the fraction of seeds with at least one win within `B`, plus
attempts-to-solve among solved seeds, unsolved seeds, environment steps, wall time,
LLM tokens/cost if applicable, and peak workers/storage. Never pool extra retries
after seeing which test seeds are difficult.

### Baselines and promotion gates

Evaluate at least random-legal, deterministic/rule-based, human-BC, and frozen
candidate policies under the same protocol. Add an LLM policy only when its exact
model and budget are reproducible. A checkpoint may advance only if:

1. dataset audit passes with no limited-channel leakage;
2. masked illegal-action rate is zero in offline evaluation;
3. offline primary metrics improve or the intended trade-off is documented;
4. unattended headless reliability meets the environment gate;
5. the held-out NoSL lower confidence bound clears the predeclared curriculum
   threshold without materially regressing earlier ascensions.

The final qualification policy is frozen. Training, calibration, prompt tuning,
and seed selection stop before qualification begins.
