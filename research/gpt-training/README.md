# GPT training research: co-author guide

Read the [branch README](../../README.md) and [progress snapshot](PROGRESS.md).
Evidence cutoff: **2026-11-06T05:09:53Z**. This is not a live status feed.

## Source identity

The original worker trees contain 43 Python files each and are preserved byte for
byte under `releases/task099/code/` and `releases/task104/code/`.
The [manifest](source-manifest.json) maps published files to original paths, commits
and SHA-256s. Original commit identifiers refer to worker-only snapshots, not full
host releases:

- Task099: `c93781778b2e71b29110079c0f9ec3881829e7f6`.
- Task104: `53dd1d94616a6c1f32c177d8c45e6686571c4188`, directly descended from Task099.
- Full repository context: `ef377742fa61c66ab246b4bedff536e745751d0b`.

Start with [model](releases/task104/code/sts2train/model.py),
[PPO](releases/task104/code/sts2train/ppo.py),
[reward](releases/task104/code/sts2train/objective.py),
[GAE](releases/task104/code/sts2train/gae.py),
[public input mapping](releases/task104/code/sts2train/features.py), and
[worker entry](releases/task104/code/modal_research/native_worker.py).
Compare Task099 at the same relative paths. Top-level train/vector packages mirror
the manifested Task104 files for navigation; inherited CLI helpers outside this
manifest were not part of the exact production worker closure.

`modal-function/` preserves the Task104 Function transport-source package. Its
`modal_research/native_worker.py` overlays the base worker for 32-core execution.
Resume inputs refer to validated accepted checkpoint generations and are separate
from mutable invocation outputs. Credentials, production image/Volume IDs and signed
activation packets are absent.

**Do not deploy worker controller modules onto the production host.** They are
worker dependencies, not the current control plane. Top-level
`sim/tools/modal_research/` and monitor tooling retain historical context;
production has accepted later patches. This publication does not retrieve, replace
or restart live host services.

## Recipe and data

[Task099 recipe](releases/task099/recipe.json) and
[Task104 recipe](releases/task104/recipe.json) are scientific excerpts with hashes
of the original operational specifications, not executable signed job packets.
Both use Silent A10, autonomous fresh streams, strict FP32, PPO clip 0.2,
learning rate 0.0003, gamma 1.0, lambda 0.99, entropy coefficient 0.02,
value coefficient 0.5, one epoch and minibatch 4096. Acquisition shaping is **off**
despite inactive coefficients in the configuration. Auxiliary combat/win and public
transition prediction are separate signals; scalar V is not win probability.

Task104 changes publicly unambiguous HandSelect options to reference current-hand
features. Ambiguous matches remain unknown. Legal candidate order and parameter
count are unchanged. It starts fresh; its steps do not continue Task099's 200M counter.

## Verification

From the repository root, with only the Python standard library:

```bash
python3 research/gpt-training/verify_snapshot.py
```

For a machine with Python >=3.11, NumPy, PyTorch and pytest already available, run
the archived CPU tests in separate processes to isolate package versions:

```bash
PYTEST_DISABLE_PLUGIN_AUTOLOAD=1 PYTHONDONTWRITEBYTECODE=1 python3 -m pytest -q -p no:cacheprovider research/gpt-training/releases/task099/tests
PYTEST_DISABLE_PLUGIN_AUTOLOAD=1 PYTHONDONTWRITEBYTECODE=1 python3 -m pytest -q -p no:cacheprovider research/gpt-training/releases/task104/tests
```

Fixtures use a public native schema and synthetic observations, not human traces or
saves. Fixture paths are adapted for this layout; substantive assertions are retained.
[VALIDATION.md](VALIDATION.md) separates archived results from this publication's checks.

## Reproduction boundaries

Actual training additionally needs a compatible native simulator/vocabulary bundle,
the approved schema, GPU runtime, dependencies and independent resource configuration.
The archived Function runtime observed Torch 2.7.1+cu128 on H100; generic dependency
ranges are not a claim that all newer combinations reproduce that run.

Use existing simulator build documentation, then inspect the pinned worker's
`Installation`, job specification and artifact validators. Production calls worker
APIs, not the old generic CLI recipe. No verification/test command above starts a
cloud run. Checkpoints and game assets require separate private provisioning.

The inherited repository includes other agents' work and optional save-based tools;
their presence as code does not mean this lineage used their data or weights.
Raw trajectories, checkpoints, ledgers and logs remain outside Git. The published
aggregates support inspection; reproducing every statistical analysis requires
separately provisioned original evidence.

## Research backlog

The [issue index](issues/README.md) links hypotheses, controls, acceptance criteria
and primary papers. Keep a matched PPO control, change one factor at a time,
pre-register observation points and compare both new environment steps and actual
compute costs. Validate promising results with multiple training seeds. 25M is
an observation point, not a universal minimum-score cutoff.
