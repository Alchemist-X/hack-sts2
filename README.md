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

## 训练工作（Route C 模拟器 · 静默猎手 A10）— 分支状态 2026-11-06

本分支在录制器之外，还带着整套训练栈：`sim/` 是从反编译游戏源码移植的无头、可克隆 C# 规则内核（游戏作者书面许可，学术研究），
`sim/tools/vec`（sts2vec，批量 Vec ABI 的 Python 绑定）、`sim/tools/train`（sts2train：PPO、协议评测、配对比较）、`sim/tools/eval`
（协议 v0.2 计分），以及 Colab A100 上的线驱动与运维脚本（`scripts/colab/`）。目标：THE SILENT · Ascension 10，在 dev-128 上
贪心、关 loop breaker 的**平均通关进度 ≥ 0.60、通关率 ≥ 0.10**，信息对等（不读隐藏信息、不读档）。

**当前数字**（全部 0 通关；来源见括号；截至 2026-11-06 22:30 EST）：

| 读数 | 值 | 来源 |
| --- | --- | --- |
| 随机初始化、5,007 万步后（W0） | dev-128 0.251 | `sp2-w0` evals.jsonl，u382 |
| 主线 M，4 亿步 | dev-128 0.404 [0.378, 0.430] | `sp3-main` evals.jsonl，u3064 |
| 线 A 官方终点（宏动作 + 熵退火） | dev-128 0.444 [0.416, 0.473]；ab-512 0.464 [0.449, 0.479] | `w3m-a`，u7664 |
| C 系纯续训（`--macros all`）各段终 | ab-512 0.449 (u9164) → 0.475 (u10664) → 0.486 (u11164) → 0.489 (u11664) → 0.485 (u12164) | `w4-c` / `w4-c3` 端点 report.json |
| boss 学校 S（25% 人类存档起局 + act-2+ boss 击杀奖励 0.2）段终 | ab-512 0.492 (u11164)、**0.500 (u11664，所有线最高)**；配对 S − C3 +0.006 / +0.011（均不显著，单训练种子） | `w10-s` 端点 report.json；配对日志 |

dev-128 = `silent-a10-dev-v1`（训练从不使用的 128 个开局），ab-512 = `silent-a10-ab-v1`（确认集）。纯续训已到平台（5 点窗口斜率 +0.002/500 update，
`scripts/analysis/evalstats.py`）；Act 2 boss 通过率约 7%，第三幕到达 5–11/128；行为审计：策略不锻造、不删牌，最终 boss 战输在输出不足（30–66/回合对
535–636 血池）与格挡供给（见 [最终 boss 复盘](docs/diag-2026-11-06/final-boss-losses.md)）。

**现在在跑**（两台 Colab A100）：GPU A 线 C4（C3 u12164 → u13164，纯续训对照）；GPU B 线 S 收尾后，按 [wave 20 试点预注册](docs/wave20-pilot-plan-2026-11-06.md)
二选一：S2（boss 学校续训）或 W20a（`--boss-hp-bonus 0.1`：boss 战失败时按打掉的血池比例给终局奖励），都与 C4 同 fork 配对。

**实验监控平台（对所有人可见）**：<https://claude.ai/artifact/492zmT59ngRDmG3GqkmMC7>

- 总览：当前状态、各线最佳读数、新开局进度曲线（可勾选线）、评测表（可筛线 / 种子集 / 只看段终）、事件时间线；
- 实验台账：自 11-04 起的每条线一行（假设、fork、旗标差、对照、段终读数、配对、裁决、教训、GPU 小时与单位）与谱系图；
- 单线复盘：点任一条线看它的事件经历、与对照线的评测曲线（带置信区间）、段终与配对表、训练侧过程、裁决与教训；
- 吞吐与过程：每个 run 的 update/小时、每 update 的环境 / 推理 / 更新耗时拆分、GPU 峰值内存、每千次更新单位成本，以及熵 / KL / harvest / 存档起局等中间过程；
- 架构：A10 训练流水线图、Sts2Sim 全景与 run 层循环图（完整架构说明：<https://claude.ai/artifact/9roz2JxkbXzfW4d3tat1FQ>）、系统参数表。

台账事实在仓库 [`docs/results/lines-silent-a10.json`](docs/results/lines-silent-a10.json) 与 [`line-events-silent-a10.jsonl`](docs/results/line-events-silent-a10.jsonl)
（新线起跑时追加一行与事件），数字由训练镜像自动填充；刷新一条命令 `scripts/dashboard/dash_refresh.sh`，数据契约与发布步骤见
[`scripts/dashboard/README.md`](scripts/dashboard/README.md)。

**科普动画《教 AI 爬塔》（约 12 分钟，分 14 幕）**：<https://claude.ai/artifact/4nRxgWB7WL1fau6fk6xJAo> — 问题为什么难、为什么自己造模拟器、PPO 一图看懂、探索历程谱系、探索推进为什么失败、奖励塑形与 boss 血量信号、测量的噪声与配对、五组取舍、工程教训、参考文献（已核实）；页面源码在 `scripts/explainer/`。

**入口文档**：

- [交接：给新人的一页](docs/handoff-2026-11-06.md)（目标、架构、各波工程、主要发现、复现、测试、数据、预算、文档索引）
- [两天复盘与重构清单](docs/retro-2026-11-06.md)（2026-11-05/06：成果、浪费与原因、基础设施代价、20 项重构的排序与状态）
- [结果表](docs/results-silent-a10-2026-11-06.md)（+ [JSON](docs/results/evals-silent-a10-2026-11-06.json)）
- [研究日志](docs/research-log-2026-11-05.md)（每半小时的观察 → 判断 → 行动，决策以此为准）
- 计划与预注册：[第二轮](docs/round2-plan-2026-11-05.md)、[第三轮](docs/round3-plan-2026-11-05.md)、[第四轮](docs/round4-plan-2026-11-06.md)、
  [boss 学校](docs/boss-school-plan-2026-11-06.md)、[wave 20 试点](docs/wave20-pilot-plan-2026-11-06.md)、`scripts/analysis/prereg/*.json`
- 评测统计与判决工具：[`scripts/analysis/README-eval.md`](scripts/analysis/README-eval.md)（5 点窗口逐 seed 配对、斜率、扁平 summary.json、
  预注册 → ADOPT / REJECT / CONTINUE / REPLICATE / KILL）
- 线规格运行器 v2、kill 规则守卫、保活 v3：[`scripts/colab/lab/README.md`](scripts/colab/lab/README.md)（影子模式上线计划）；
  旧版线驱动与运维：[`scripts/colab/ops/README.md`](scripts/colab/ops/README.md)
- [sts2train README](sim/tools/train/README.md)（所有训练 / 评测参数，含 wave 20 的 boss 血量比例与 DropPotion 门控）
- 录制人类对局前先跑 `scripts/recording_preflight.sh`（游戏必须在 v0.111.0；mods 只装 Sts2Recorder）

**co-author 上手五步**：

1. `git clone https://github.com/Alchemist-X/hack-sts2.git && cd hack-sts2 && git checkout claude/recorder-performance-lag-0f9d6d`
2. 读 [`docs/handoff-2026-11-06.md`](docs/handoff-2026-11-06.md)，再看结果表与研究日志最后几轮。
3. `uv sync --project sim/tools/train` 与 `uv sync --project sts2rec`（模拟器构建需要 .NET 9：`dotnet build sim/Sts2Sim.sln -c Release`）。
4. 测试一律走 raven 上的远端测试箱：`scripts/test_remote.sh`（或 `--suites none -- uv run --project sim/tools/train pytest -q <files>`）；
   不要在笔记本上跑重的 pytest / 训练冒烟。raven 的 SSH 访问向 owner 申请。
5. 训练数据（checkpoint、评测目录）在私有 GCS 桶里：向 owner 申请读权限；密钥只在 owner 本机与训练 VM 上，不进仓库。
   录制器会话与人类轨迹永远不入库。

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
