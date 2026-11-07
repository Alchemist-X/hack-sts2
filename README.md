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

## Silent A10 自主训练监控与复盘

打开 [STS2 研究监控台](https://sts2-modal-research-console.crazy-kfc.chatgpt.site) 查看本分支的 Modal 实验。站点为只读私有页面，仅站点所有者登录后可访问。运行卡片分别显示各实验的采样时刻、检查点进度与训练器状态。

- [实验档案](https://sts2-modal-research-console.crazy-kfc.chatgpt.site#experiments)记录每次实验的假设、配方改动、权重父模型、实际与目标剂量、正式评测结果、故障恢复和证据引用。
- [架构演变](https://sts2-modal-research-console.crazy-kfc.chatgpt.site#architecture)按型号和版本展示共享编码、动作评分与价值估计的演变，保留早期资源探针的结果。
- [训练吞吐](https://sts2-modal-research-console.crazy-kfc.chatgpt.site#performance)展示 PPO 更新、检查点里程碑和分阶段耗时。训练奖励与进度用于诊断，正式能力对照使用固定开发集的平均进度、胜率与样本数。
- [费用](https://sts2-modal-research-console.crazy-kfc.chatgpt.site#budget)分别列出工作区毛计量、账单净额与抵扣、App 归属和共享用量、估算及预留。缺少逐调用账单时标为未知。

本轮收尾范围是完成当前 S、M 训练和正式开发集对照，随后暂停训练。

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
  saves, and trajectory directories. Workers normally run with Godot `--headless`
  and `--force-steam=off`; `--visible` shows one isolated worker without changing
  its HOME, save tree, port, or recording directory. By default worker profiles
  copy the human settings and seed unlocks from the human profile (the E3
  coverage farm relies on this); `STS2_HEADLESS_FRESH_PROFILE=1` provisions
  fresh profiles that never read the human save tree (benchmark pools, whose
  save tree is replaced by a frozen baseline anyway). The worker MCP is built from `config/sts2mcp.lock.json` (upstream commit, ordered
  patches, pinned game build) by `scripts/build_selection_mcp.sh`.

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

To supervise a single isolated game window, provision normally and launch it
directly with recording enabled:

```bash
./scripts/headless_provision.sh 1
./scripts/headless_launch.sh 1 --visible --record
./scripts/headless_launch.sh 1 --stop
```

Only one visible worker is allowed. `scripts/hash_human_saves.sh` can be run
before and after a smoke test to verify that the real Steam save tree did not
change.

For supervised agent play, `scripts/sts2_action_executor.py` is deliberately a
single-action transport, not a combat policy. The external reasoner must inspect
the full public state, choose every card/target/end-turn decision, and provide a
reason plus the observed state hash. The executor rejects stale decisions,
waits for semantic card commitment and a quiet settlement window, and writes a
durable intent before injection followed by a linked outcome:

```bash
python3 scripts/sts2_action_executor.py --port 15601 observe --full
python3 scripts/sts2_action_executor.py --port 15601 act \
  --expected-hash STATE_SHA256 \
  --json '{"action":"play_card","card_index":3,"target":"ENEMY_0"}' \
  --reason 'Externally chosen after comparing the full turn state.' \
  --controller llm --log output/decisions.jsonl
```

There is intentionally no generic heuristic combat driver. Training and
benchmark policies remain separate interfaces and must identify their own
controller provenance.

For per-game live narration, version-checked rule retrieval, pending-action
recovery, and reward/potion/token audits, use `scripts/sts2_live.py`. Read the
[live-play protocol](docs/sts2-live-protocol.md) for setup and commands, and the
[live harness architecture](docs/live-harness-architecture.md) for its decision
and improvement workflow. The live controller does not load the offline learned
policy or value models. The [A10 case-study release](https://github.com/Alchemist-X/hack-sts2/releases/tag/sts2-a10-gpt6-2026-09-09)
contains separately archived gameplay evidence and saves; these are not Git
source files or a qualified independent benchmark.

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
The benchmark vocabulary follows the game: A10 is the maximum ascension and
A0-A10 denotes the complete difficulty range. The strict ladder definition is in
the [NoSL A0-to-A10 protocol](docs/no-sl-ascension-10.md).
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

- The current text controller's native `game_over` snapshot does not expose an
  authoritative win/loss field. Such terminal outcomes are recorded as `null`,
  never guessed as defeats, and are rejected by the value-data quality gate;
  otherwise valid demonstrated actions remain usable for behavior cloning.
  Passive human sessions can still use their recorder manifest outcome. Online
  headless value/RL qualification requires adding a verified engine outcome to
  the controller contract.
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
