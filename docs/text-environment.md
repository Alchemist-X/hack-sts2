# STS2 text environment

## Architecture

The environment does **not** reimplement Slay the Spire 2 rules. The official
v0.107.1 process remains the state-transition engine and runs with Godot
`--headless --force-steam=off`. Agents see only structured observations and
legal actions through the local MCP endpoint; no frame capture, image model, or
screen buffer participates in the loop.

The upstream 2.2 GiB logical bundle consists mostly of the 1.8 GiB Godot PCK
and the 171 MiB executable. Headless mode avoids rendering but the engine still
uses the PCK for scenes, models, localization, and game data. The pool uses a
small overlay: it clonefiles the executable and symlinks immutable Resources
and Frameworks from `STS2_GAME_APP` (or the normal installation).

```text
headless-instances/
  runtime/SlayTheSpire2.app/     # one shared overlay (~172 MiB logical)
  inst1/                         # HOME, port, pid, logs, trajectories
  inst2/                         # HOME, port, pid, logs, trajectories
  ...
```

On APFS the clonefile adds almost no new physical blocks. `STS2_MCP_PORT` and
`STS2_RECORDER_*` are process-local. Every worker can load
the same mod DLLs while binding a different port and writing to a different
HOME. On the development M4 Pro, two simultaneous menu workers used about
383 MiB RSS each; a loaded run can use more. The measured private directories were 1.6 MiB for the old
test worker (including old logs) and 224 KiB for a clean worker.

## Commands

From `hack-sts2/sts2rec`:

```bash
# Build/update one runtime and launch four concurrent text workers.
uv run sts2text pool start 4 --base ../headless-instances

# Inspect processes and private on-disk bytes.
uv run sts2text pool status --base ../headless-instances

# Print a structured observation without starting a renderer.
uv run sts2text observe --port 15601
uv run sts2text observe --port 15601 --json

# Human terminal client: choose actions by integer index.
uv run sts2text play --port 15601 --character NECROBINDER

# Graceful PID-file-based shutdown.
uv run sts2text pool stop --base ../headless-instances

# Recover a timed-out policy; optionally discard only this sandbox run.
uv run sts2text pool recycle 2 --base ../headless-instances --clear-run
```

Full recorder hooks are disabled for pool workers by default. Add `--record`
only for recorder diagnostics; training code should write compact transition
JSONL with `JsonlTrajectoryWriter`.

## Python training interface

```python
from sts2rec.env import Sts2Env
from sts2rec.text_env import (
    JsonlTrajectoryWriter,
    Sts2TextEnv,
    TextEnvConfig,
    VectorSts2TextEnv,
)
from sts2rec.information import InformationMode

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
    # One action index per worker; HTTP transitions execute concurrently.
    next_observations = env.step([policy(obs) for obs in observations])
```

Each `TextTimeStep` contains:

- `observation`: leakage-filtered public state;
- `legal_actions`: complete indexed candidates with card/item/relic effects;
- `action_mask`: one entry per legal action;
- `reward`: sparse win reward by default, replaceable with a callback;
- `terminated` / `truncated`: Gym-style episode flags;
- `info`: worker ID, action-space audit, selected wire action, information mode,
  settle status, and an isolated privileged channel for omniscient evaluation.

`run_episode` and `run_episodes_concurrently` accept arbitrary policy callables.
`JsonlTrajectoryWriter` is thread-safe and streams `(s, A(s), a, r, s', done)`
records without screenshots.

`recycle_worker`/`sts2text pool recycle` provides crash recovery and a clean-run
reset while preserving the worker's unlock/preferences seed. `--clear-run`
targets only `instN/home/**/current_run.save`; it never touches human saves.

## Information modes and evaluation boundary

- `limited`: strips seed/RNG/true draw order/future shop and any
  `privileged_*` field before policy access.
- `omniscient`: uses the same public observation plus a separate privileged
  provider. Privileged data cannot flow into limited training records by
  construction.

The environment is ready for independent text rollouts and learned policy/value
callbacks, subject to one terminal-label boundary: the current controller may
return only a `game_over` message without a verified win/loss field. That outcome
is recorded as unknown and is not eligible for terminal-value training. Passive
human manifests already carry an explicit outcome. Valid text-worker action
labels remain behavior-cloning eligible, but headless online value/RL
qualification must wait for the controller to expose the equivalent signal.

Exact **mid-combat clone/restore** is not claimed: the public game
API does not serialize every in-memory combat object, and `current_run.save`
is only a room-level recovery artifact. A future exact counterfactual runner
must add an in-engine checkpoint serializer or replay a deterministic action
prefix; copying the save file alone would give misleading Q-loss estimates.
