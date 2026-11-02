# hack-sts2 接手文档（当前入口）

更新：2026-11-02 01:30。给新会话或新同事的唯一入口：先读本页，再按需打开引用的文档。历史交接记录在
`docs/handoff-2026-11-01.md`（E1/E2 期间逐段追加），波次级状态日志在 `docs/p1-sim-full-port-plan-2026-11-01.md` 末尾。

## 1. 项目是什么，现在到哪了

用户目标原话（2026-11-02 补充，作为正式口径）：

> 1. 基于 STS2，构建一个长程决策任务环境，能使用神经网络、LLM 并发高效评测，并支持后续的训练
>    1. 对于神经网络来讲，输入、输出要设定得比较清楚，适合转化为一个 auto-research 的环境交给 LLM 来不断优化
>    2. 对于 LLM 来说，我们确保它接收到的信息和人类是一样的（攻击意图、可见、不可见信息，卡牌和遗物效果）
>    3. 评测分数目前主要根据平均通关进度，比如在 A0 或者 A10 下平均能到多少进度
>    4. 容器化设计，每个环境可以并行运行，吞吐量要足够高
>    5. 可扩展性要好，之后版本更新了，能快速定位到不同的部分，进行更新


一个可快速评测神经网络与 LLM 的长程决策环境：Slay the Spire 2（v0.111.0 规则）的源码级无头模拟器 `sim/`，
带两种接口（文本 HTTP 给 LLM，数字批量通道给网络）、一套评测协议、与真游戏的逐决策对账闸，以及容器化与版本更新工具。
真实游戏只作对账 oracle 与最终考场。

| 目标（用户原话） | 现状 | 证据 |
| --- | --- | --- |
| NN 输入输出清晰、可做 auto-research 目标 | 接口已落地（`Sts2Sim.Vec` + `sts2vec`），训练冒烟在波次 G 中 | `sim/VEC.md`、`sim/CONTRACTS.md` Part D、`sim/BENCH.md` |
| LLM 与人类信息对等 | 已做：规则文案、有效数值、意图只给类型与数值、招式名默认隐藏、能力走显示数值、泄漏测试 | `sim/PLAY.md` "Hidden information"、`ServerParityTests.cs` |
| 按平均通关进度评分（A0/A10） | 协议 v0.2 定版，官方种子集带校验和，榜单有 Sonnet 行 | `docs/sts2-bench-protocol-v0.2-2026-11-01.md` §3/§4/§8 |
| 容器化、并行、高吞吐 | Dockerfile（arm64、amd64 实测；镜像内 NativeAOT `Sts2Sim.Vec.so` 默认启用）；`scripts/sts2_cluster.sh up N [--host raven]` 一条命令起 N 个服务器，`sts2eval --urls` 轮转；Huginn 容量：整机约 26-28 局/秒随机对局、每服务器 110-150 MiB、容器内 AOT 6.2-6.3 万 env-steps/s；Linux x86_64 全门通过；1024 局×8 线程 11.6 万 env-steps/s（Mac） | `docs/docker.md`、`docs/capacity-2026-11-02.md`、`docs/linux-validation-2026-11-01.md`、`sim/BENCH.md` |
| 版本更新能快速定位 | `scripts/version_delta.py` 10 秒出报告（内核映射补全中） | `docs/version-migration-playbook.md`、`docs/version-delta-demo-2026-11-01.md` |

保真（E3）：战斗级 117 段 1,599/1,607 决策一致，整局 G3 1,532/1,537；录像农场放量在波次 F 中（见 §7）。

## 2. 仓库地图

| 路径 | 内容 |
| --- | --- |
| `sim/Sts2Sim.Core` | 规则内核 + 1,400+ 内容模型（逐模型单文件，规则引用反编译 `文件:行`，见 `sim/specs/<area>.md`） |
| `sim/Sts2Sim.Server` | HTTP 对局服务器（LLM 用）：观测 JSON + 文本、token 动作、步骤日志、规则文案、`--reveal-moves`、`--bind` |
| `sim/Sts2Sim.Vec` + `sim/tools/vec` | 批量训练接口：C ABI（`sts2vec_*`）、实体表观测、候选动作、快照起点池；Python `sts2vec`（hostfxr + ctypes + numpy） |
| `sim/Sts2Sim.Diff` | 差分闸：G0 首战 bundle、v2 bundle（G1/G2）、G3 种子整局重放、`.mcr` 解码 |
| `sim/Sts2Sim.Tests` / `Sts2Sim.Bench` | 自定义测试运行器（`-- <过滤子串>`），基准（combat 行、`vec` 子命令、run-soak） |
| `sim/tools/eval` | LLM 评测器 `sts2eval`（Anthropic SDK、并发、转录、计费、循环守卫、协议 v0.2 指标） |
| `sim/tools/play` | `sim_play.py` 命令行客户端 + 代理手册 |
| `sim/tools/train` | 训练冒烟（波次 G 新增） |
| `sim/CONTRACTS.md` | Part A 内核 / B 决策 API / C run 层 / D 训练接口 的合同 |
| `sim/specs/` | 规格索引、集成日志 `integration.md`、差分报告 |
| `sts2rec/` | 录像工具（uv）：`validate`、`export-bundles-v2`、`export-run-oracles`、存档读取 |
| `mod/` | Sts2Recorder 被动录像 mod（REC 版，带存档锚点） |
| `scripts/` | `sim_server.sh`、`gates.sh`（波次 G）、`linux_validate.sh`、`version_delta.py`、`version_update.sh`、`install_mod.sh`、`mcp_mod.sh`、`headless_*.sh`、`farm/`（波次 F） |
| `knowledge/source/<版本>/` | 游戏文案（卡/遗物/药水/能力/事件/敌人/关键词）jsonl；`scripts/export_sts2_localization.py` 从 PCK 导出 |
| `docs/` | 本页、协议、设计、评审、状态日志、试玩报告 |

## 3. 怎么跑

```bash
dotnet build sim/Sts2Sim.sln -c Release                          # 0 警告
dotnet run -c Release --project sim/Sts2Sim.Tests -- Server Vec   # 过滤子串；无参数 = 全量约 7 分钟
scripts/gates.sh [--quick] [--bench]                              # 集成门一键（波次 G 落地后）
scripts/sim_server.sh                                             # 对局服务器 8765；stop/status
python3 sim/tools/play/sim_play.py new --seed PLAY1 --character IRONCLAD
uv run --project sim/tools/eval sts2eval run --seedset std-a0-v1 --policy claude --model claude-sonnet-5-5 \
  --effort low --max-tokens 2048 --concurrency 10 --spawn --api-key-file ~/.config/sts2eval/anthropic_api_key
uv run --project sim/tools/eval sts2eval report tmp/eval/<run>    # 重算报表
STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q   # 训练接口测试
uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500 ; sts2vec check
dotnet run -c Release --project sim/Sts2Sim.Bench -- vec 1024 8 500 0 1024
dotnet run -c Release --project sim/Sts2Sim.Diff -- <bundle dir> [--v2] [--report out.json]   # 差分闸
scripts/version_delta.py <旧反编译树> <新反编译树> --out tmp/version-delta/x                  # 版本定位
docker build -t sts2sim . && docker compose up                   # 见 docs/docker.md
scripts/linux_validate.sh                                         # Huginn 全门
```

评测输出在 `tmp/eval/<run>/`（git-ignored）：`config.json`、`games/*.jsonl` 每步转录、`report.md/json`。

## 4. 协议与榜单（v0.2）

- 种子集：`std-a0-v1`（A0，20 局）、`std-a10-v1`（A10，20 局）、`smoke-a0-v1`（冒烟 10 局），文件在 `sim/tools/eval/seedsets/`，带 sha256。
- 头牌指标：按幕归一化的进度均值 [95% CI] 与 Wilson 胜率；副指标：层数、步数、非法/卡死/封顶、token、费用、耗时。
- 当前榜单（Sonnet 5.5，effort low，招式隐藏）：A0 正式 0.499 [0.410, 0.588]、A10 正式 0.310 [0.243, 0.377]、随机 0.065 / 0.051；0 胜。
- 费用口径：A0 约 $1.8–2.5/局，A10 约 $1.1/局；缓存命中率约 58–61%。

## 5. 流程与约定

- Fable 编排、工程子代理一律 Opus；试玩/评测代理可用 Sonnet。精简流程：简报 → 并行所有者（隔离 worktree，先 `git checkout -B <分支> <基线 sha>`）→
  单一集成者跑门 → 提交。不设对抗校验。
- 规则真值只有反编译树 `output/server-bootstrap/linux-decompiled/`（只读，不入库）；禁用 StS1 记忆数值。
- 不提交轨迹/存档/bundle/`tmp/`；不改游戏存档；`STS2_MCP` 只在代理运行期间安装，结束必须 `scripts/mcp_mod.sh off`。
- 提交信息祈使句，末行 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`。
- 分支：`claude/sts2-eval-pipeline-a3aee7` 与 `claude/sim-source-recreation-9392df` 已同步到同一提交；都未推送。

## 6. 凭证与机器

- Anthropic API key：`~/.config/sts2eval/anthropic_api_key`（chmod 600，仓库外；用 `--api-key-file`）。本机 SDK 需 `Accept-Encoding: gzip`（评测器已处理）。
- Huginn（`ssh raven`，管理 `ssh raven-labs`）：8 vCPU x86_64，Ubuntu 24.04；用户级 .NET 9 在 `~/.dotnet`；Docker 29.9（数据目录 `/data/raven/docker`）；根盘仅 3 GB 余量，大文件放 `/data/raven`。
- 东京 GCP VM：生产机，只用于一次性验证镜像（见 `~/.claude/rules/tokyo-vm.md`）。
- 游戏：本机 Steam 安装，mods 目录见 `scripts/mcp_mod.sh`；人类录像 `~/Library/Application Support/Sts2Recorder/sessions/`（只读，核心资产）。
- 周额度：2026-11-01 深夜为 82%，11-07 重置；一个 4 所有者波次约 2%。

## 7. 进行中与开放问题

- 波次 G（已合入 2026-11-02，记录见 `sim/specs/integration.md` "G integrate"）：紧凑观测（`--obs-mode compact` 为 CLI 默认；每步成本 −19.5%，未达 30–40% 目标，std-a0-v1 full/compact A/B 未跑）；`scripts/gates.sh`（`docs/gates.md`）；内核映射 `sim/specs/kernel-map.json`（564 文件 0 unmapped，版本差分内核覆盖 45 → 95/134）；训练冒烟 `sim/tools/train` + NN 评测入口（std-a0-v1 进度 0.277，学到商店移除循环，`docs/train-smoke-2026-11-02.md`；vec aux 14 列，encoder `fc2eb34c07bc8513`）；Docker amd64 在 Huginn 验证通过（`docs/docker.md`）。
- 波次 F / E3（进行中）：安装 REC recorder → 4 个无头实例 MCP 农场约 2 小时 → v2 bundle → G1/G2/G3 → 分诊与修复 → 关 MCP。报告落在 `docs/sim-port/e3-*.md`、`sim/specs/diff-report-e3-farm-2026-11-02.md`。
- 待做：对照基线（Opus/Haiku/effort high/thinking off，约 $200，等紧凑观测落地后统一口径）；E4 搜索自举（等额度）；Huginn 根盘清理（管理员）。
- 已知小票：Vec 去克隆快路径（吞吐 2.6×）、Python bench 已预热；事件选项 7/81 文案 `?`；商店 base_price 推算值；版本差分内核映射已补全（95/134 变更内核文件有 sim 文件，其余为 no_port）。

## 8. 优先级

| 级别 | 内容 |
| --- | --- |
| P00 | 波次 G 与 F 落地并提交；基线统一到紧凑观测口径 |
| P0 | E3 分诊循环持续到 G1/G2 ≥ 99.9%；并行环境在 Huginn 跑通；对照基线入榜 |
| P1 | 去克隆快路径；观测稀疏化；LLM 记忆/工具赛道；版本更新全自动 |
| P2 | 论文材料；跨游戏迁移实验（hack-balatro）；人类语料统计并入报表 |

## 9. 文档索引

`docs/handoff-2026-11-01.md`（历史交接）· `docs/p1-sim-full-port-plan-2026-11-01.md`（状态日志）· `sim/CONTRACTS.md` · `sim/VEC.md` · `sim/PLAY.md` ·
`docs/sts2-bench-protocol-v0.2-2026-11-01.md` · `docs/docker.md` · `docs/linux-validation-2026-11-01.md` · `docs/version-migration-playbook.md` ·
`sim/specs/integration.md`（每波门的数字）· `docs/gates.md` · `docs/train-smoke-2026-11-02.md` · `sim/specs/kernel-map.json`· `sim/specs/diff-report-w4-*.md` · `docs/sim-port/playtest-2026-11-01.md` · `docs/a10-first-principles-review-2026-10-27.md` ·
`docs/sim-port/design-diff-gate.md`。会话记忆：`~/.claude/projects/-Users-Aincrad-dev-proj-hack-sts2/memory/`。
