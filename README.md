# hack-sts2

Full-trajectory recorder and official-engine-backed text environment for
**Slay the Spire 2** — the STS2 counterpart of
[hack-balatro](https://github.com/Alchemist-X/hack-balatro)'s real-client pipeline.

The project has two complementary modes:

1. **Passive recording** — while a human plays normally, a first-party mod-loader
   mod records every observable state and committed action to local, versioned,
   append-only artifacts.
2. **Evaluation and training** — isolated, text-only workers run the official game
   engine with rendering and Steam disabled, exposing structured observations,
   enumerated action candidates, action masks, rewards, episode boundaries, and
   action-space audit metadata to RL policies, search procedures, or LLM
   controllers.

The text environment does not approximate or reimplement STS2 rules: the official
game process remains the state-transition engine.

> Research project. Data-cooperation agreement with the developer is in place;
> the game copy is legally owned. Human recording is local-only — nothing is
> uploaded. Sandbox workers use isolated save directories and never touch human
> saves.

## How it works

- **`mod/`** — a C# mod (`Sts2Recorder.dll`) loaded by STS2's *official* first-party
  mod loader. Harmony postfix patches on the game's human-input funnels capture the
  exact action stream; the game's native `CombatHistory` provides fine-grained combat
  events (true draw order, damage, energy); a throttled snapshotter writes full
  hash-deduped state JSON at every decision point. At run end it archives the game's
  own `.run` summary and `latest.mcr` deterministic replay alongside our streams.
  This recorder is observation-only and declares `affects_gameplay: false`.
- **`sts2rec/`** — Python recording, evaluation, and training APIs: list/validate
  sessions, canonicalize human trajectories, enumerate candidate actions, filter
  privileged information, drive one or many text workers, and stream compact
  transition JSONL with coverage audits.
- **`scripts/headless_provision.sh` / `scripts/headless_launch.sh`** — create one
  shared official-engine runtime plus lightweight per-worker homes, ports, logs,
  saves, and trajectory directories. Workers run with Godot `--headless` and
  `--force-steam=off`.

State serialization is adapted from [STS2MCP](https://github.com/Gennadiyev/STS2MCP)
(MIT). The human recorder keeps its action-injection and HTTP surface removed; the
isolated training runtime uses a separate controller build and is never installed
into the human-play environment.

Full architecture and format spec: [docs/design.md](docs/design.md). Text-environment
commands, API examples, and current limitations: [docs/text-environment.md](docs/text-environment.md).
Dataset, training, and benchmark protocol:
[docs/training-and-evaluation.md](docs/training-and-evaluation.md).

## Session layout

```
<output_root>/sessions/<start_unix>-<seed>/
  manifest.json    # schema/game/mod versions, seed, character, result
  states.jsonl     # full state snapshots at stable decision points
  actions.jsonl    # exact human actions (Harmony-hooked, not diff-inferred)
  events.jsonl     # combat history: draws, damage, energy, … (privileged channel)
  native/          # the game's own .run summary + .mcr replay for cross-validation
```

Default `<output_root>`: `~/Library/Application Support/Sts2Recorder` (macOS).

## Passive recorder (macOS)

```bash
brew install dotnet@9
./scripts/install_mod.sh   # builds mod against your game install, copies into mods/
```

Then launch STS2 with "Load with Mods" and accept the mod-consent dialog once.
Play normally; sessions appear under the output root.

```bash
cd sts2rec && uv run sts2rec sessions      # list recorded runs
uv run sts2rec validate <session-dir>      # schema + consistency checks
uv run sts2rec canonical <session-dir>     # emit canonical trajectory JSON
```

## Evaluation and training environment

From `hack-sts2/sts2rec`:

```bash
# Build/update one shared runtime and start four isolated text workers.
uv run sts2text pool start 4 --base ../headless-instances

# Inspect a structured state and the currently enumerated action candidates.
uv run sts2text observe --port 15601
uv run sts2text observe --port 15601 --json

# Play through the same interface manually from a terminal.
uv run sts2text play --port 15601 --character NECROBINDER

# Inspect resource usage, then stop workers without touching human saves.
uv run sts2text pool status --base ../headless-instances
uv run sts2text pool stop --base ../headless-instances
```

The Python interface supports arbitrary policy callables and concurrent episodes:

```python
from sts2rec.env import Sts2Env
from sts2rec.information import InformationMode
from sts2rec.text_env import Sts2TextEnv, TextEnvConfig, VectorSts2TextEnv

workers = [
    Sts2TextEnv(
        Sts2Env(15601 + i),
        config=TextEnvConfig(information_mode=InformationMode.LIMITED),
        worker_id=i + 1,
    )
    for i in range(4)
]

with VectorSts2TextEnv(workers) as env:
    observations = env.reset(character="NECROBINDER")
    next_observations = env.step([policy(obs) for obs in observations])
```

Every time step contains a leakage-filtered observation, indexed `legal_actions`
(the API name for the current candidate set), an `action_mask`, reward,
`terminated` / `truncated`, and action-space audit data. The audit can flag known
incompleteness; the current implementation does not certify that its candidates
equal the engine's complete action space in every game screen.
Two information contracts are available:

- **`limited`** exposes only information available to a human at that decision.
- **`omniscient`** adds a physically separate privileged channel for oracle
  evaluation and must not be mixed into limited-policy training data.

Typical consumers include behavior cloning, offline/online RL, learned value
functions, tree search, and LLM-controlled agents. Controlled online evaluation
is possible after the reset, seed, and action-space qualification gates described
in the training protocol are satisfied.
Training trajectories contain structured `(s, A(s), a, r, s', done)` transitions;
screenshots and frame extraction are not part of the loop.

The portable dataset and baseline commands are pure Python and do not start the
game:

```bash
cd sts2rec
uv run sts2train dataset build transitions.jsonl -o ../datasets/limited.jsonl
# Or strictly align confirmed actions from passive recorder sessions.
uv run sts2train dataset build-human <session-dir> -o ../datasets/human.jsonl
uv run sts2train dataset audit ../datasets/limited.jsonl
uv run sts2train train bc ../datasets/limited.jsonl -o ../checkpoints/bc.json
uv run sts2train evaluate bc ../datasets/limited.jsonl --model ../checkpoints/bc.json
```

See the [training and evaluation protocol](docs/training-and-evaluation.md) for
run-level splitting, value training, NoSL/SL aggregation, and promotion gates.
The human adapter rejects ambiguous, automatic, cancelled, estimated-state, and
incomplete-action-space decisions instead of guessing labels. Incomplete runs can
contribute behavior-cloning examples but never terminal value targets.
Formal benchmark aggregation is spec-bound: the spec fingerprints the game,
controller, policy, ordered seed cases, timeout/step limits, mod/unlock state, and
SL budget. NoSL qualification additionally requires explicit zero-reload and
complete trajectory-hash evidence, while strict SL requires one complete hashed
trajectory per attempt. The formal CLI re-hashes every referenced trajectory
artifact and enforces the frozen timeout and step limit; exploratory reports
without a spec are never presented as qualified results.

### Current boundaries

- The worker pool is currently verified on macOS arm64. Linux packaging remains a
  separate deployment task because the official Linux game build is x86_64.
- Exact arbitrary **mid-combat clone/restore is not yet supported**. Independent
  reset-driven episodes are supported, but benchmark-controlled seed injection and
  verification are not yet implemented. Precise counterfactual branching requires
  an in-engine checkpoint serializer or deterministic action-prefix replay.
- The environment requires a legally owned game installation and pins every
  trajectory to its exact game version/build. Game assets are not included here.
- STS2 is in Early Access. Legal-action coverage, hooks, and model compatibility
  must be regression-tested whenever the game version changes.

## Version compatibility

STS2 is Early Access and moves fast. Every hook installs independently and degrades
gracefully: after a game update, a missing method disables that one hook (recorded in
`manifest.degraded_hooks`) while polling snapshots keep the session usable. Every
manifest stamps the exact game version/commit/build. See
[docs/design.md](docs/design.md#version-drift-policy-early-access-reality).

## License

MIT — see [LICENSE](LICENSE). Contains code adapted from STS2MCP (MIT, © 2026
Yikun Ji).
