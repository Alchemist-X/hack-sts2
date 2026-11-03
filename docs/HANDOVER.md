# hack-sts2 接手文档（当前入口）

更新：2026-11-02 04:50（E3 合入后）。给新会话或新同事的唯一入口：先读本页，再按需打开引用的文档。历史交接记录在
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

评分口径：0–10，10 = 该条目标在环境层面已无缺口（评测/训练本身不算在内）。波次 J（2026-11-02，`sim/specs/integration.md`
"J integrate"）后重评；保真行为 E3 合入（"E3 integrate"）后新增：

| 目标（用户原话） | 评分 | 现状（证据） | 环境层面还差 |
| --- | --- | --- | --- |
| 1.1 NN 输入输出清晰、可做 auto-research 目标 | **8** | 一页环境说明 `sim/ENV_SPEC.md`（§5 列表/枚举/状态位由 schema 生成，测试防漂移）；与策略无关的评测器 `sts2vec eval --policy <random\|first\|heuristic\|file.py:act>`（与 LLM runner 同一计分代码，>10,000 例逐字比对；每个种子集 < 1 秒）；20 行策略模板 `sim/tools/vec/examples/`；基线 random 0.087 / first 0.194 / heuristic 0.221（A0，E3 合入后重测）；去克隆快路径（默认开，输出逐字节相同）；信息集克隆 `clone_determinized`（同视图、隐藏状态按种子重采样，供搜索） | 训练环境无停滞上限：Vec 只有 `step_cap`，评测器的"每层 80 个非战斗决策即 stuck"没有下沉到 VecEnv（训练冒烟因此学到商店移除循环）；信息集克隆不重采样预生成内容（遭遇队列、后续 Boss/Ancient、未开宝箱遗物、未开卡牌奖励），整局搜索会泄漏；heuristic 基线偏弱 |
| 1.2 LLM 与人类信息对等 | **8** | 逐 UI 元素对照审计 `docs/parity-audit-2026-11-02.md`（服务器 JSON/文本、紧凑文本、Vec 四通道）：修掉隐藏能力 AMBERGRIS_POWER 泄漏；39 个事件选项数值补全（事件遍历 0 个 `?`）；补上 Boss/Ancient、进阶、任务点、三堆牌（抽牌堆排序显示，不泄漏顺序）、充能球数值、休息点说明、升级预览、附魔/诅咒标记、游戏同款能力图标数字；1,500 状态服务器↔Vec 交叉检查、250 次抽牌顺序重排观测逐字节不变 | Vec（NN 通道）还缺几项人类可见信息：事件选项数值与引用物、本幕 Ancient、弃牌/消耗堆顺序、牌堆卡附魔/诅咒通道、手牌 affliction 数值（需一次 schema 追加 + encoder_version 升级）；3 处风味文本、休息点说明、附魔标题仍是 sim 措辞而非游戏原文（需导出对应本地化表）；升级预览用下一级基础数值（不含附魔加成）；2 个未移植事件（CRYSTAL_SPHERE、FAKE_MERCHANT）以单选项 PROCEED 占位 |
| 1.3 按平均通关进度评分（A0/A10） | **9** | 协议 v0.2 定版，官方种子集带 sha256；LLM 赛道（`sts2eval`）与 NN 赛道（`sts2vec eval`）同一计分、同一运行目录格式、逐局重放审计；榜单 12 行（`docs/sts2-bench-protocol-v0.2-2026-11-01.md` §8） | 环境层面无缺口。Sonnet 行（0.499 / 0.310）测于对等审计之前的观测，需注明口径；full/compact A/B 与对照模型属评测，不在本阶段 |
| 1.4 容器化、并行、高吞吐 | **8** | `scripts/sts2_cluster.sh up N [--host raven]` 一条命令起 N 个服务器并等健康，`sts2eval run --urls` 轮转；镜像内 NativeAOT `Sts2Sim.Vec.so` 默认启用；Huginn 实测：整机 26–28 局/秒随机对局、每服务器 110–150 MiB、容器内 Vec 6.2–6.3 万 env-steps/s（`docs/capacity-2026-11-02.md`）；Mac 1024 局×8 线程 Vec 10.8–12.1 万 env-steps/s（负载 5–14 下中位数；轻载单次 12.7 万，每步 61 CPU µs；`sim/BENCH.md` "J integrate"）；arm64/amd64 镜像均验证 | arm64 的 AOT 镜像未构建验证；`sts2eval` 单进程约 7 局/秒封顶，没有内置多进程模式（只影响快速脚本策略走 HTTP 的情形，NN 走 `sts2vec eval` 不受影响）；Huginn 的 containerd 快照与构建缓存在 2.7 GB 根盘上（需管理员迁到 `/data`）；客户端与服务器分机的纯服务器容量未测 |
| 1.5 版本更新能快速定位 | **8** | `scripts/version_update.sh` 一条命令：版本差分（10 秒）→ 逐模型工作包（`version_stubs.py`，新模型带可编译骨架，546 包 2 秒）→ 录像钩子漂移检查（`hook_drift.py`，合成版本上恰好抓到 1 处签名变化）→ 构建 → 受影响测试 → selftest → G0，末尾输出人工清单；内核映射 564 文件 0 unmapped；快路径提示点表 `gen_prompt_sites.py --check` 进门；Linux 容器门 `scripts/ci_linux.sh`（Huginn PASS） | 规则移植本身仍是人工（工作包只定位不改写）；GitHub workflow 只是骨架未启用；`ci_linux.sh` 改过的启动行未完整跑一次；STS2_MCP mod 的钩子目标不在 hook-map 里，漂移检查覆盖不到；默认只跑受影响测试（`--full-tests` 才全量） |
| 保真（E3，五条的共同前提） | **7** | 对账闭环已通并合入：REC recorder（真游戏存档锚点）→ 4 进程无头覆盖农场 39 局（`scripts/farm/`）→ `export-bundles-v2` 绑定存档锚点 → G1/G2/G3 → 分诊与 6 处 Core 修复 → 棘轮基线 `sim/specs/diff-baseline-e3-farm.json`。合并后的树（`sim/specs/integration.md` "E3 integrate"）：农场 G3 种子整局 3,320/3,324 决策（99.88%），39 局 30 局到录像终点；`--anchors` 3,578/3,582，7 处分歧；`--anchors-every`（每段从游戏存档起，即 G2）3,601/3,603（99.94%）；存档对照：可导出的 258 段除解锁档与地图历史外全等（另 179 段在战斗内或带未决提示，不可导出）；G1/G2 棘轮 787 段、2,664 决策 0 回退；人类语料 G3 1,532/1,537 → 1,921/1,924（99.84%），人类战斗闸 117 段 1,599/1,607 不变 | 7 处开放分歧（PUNCH_OFF 事件转战斗、GAMBLING_CHIP 阶段名、导出器把放弃的卡牌奖励当事件选项、STRATAGEM 选牌顺序、3 处只在链式重放出现的隐藏状态/回合计数）；人类战斗闸 G1 99.50%，未到 99.9%；农场覆盖薄：1 局到第 2 幕、0 局到第 3 幕，A10 最高 14 层；导出器未索引 R2 逐战斗回放；8 次真游戏 worker 崩溃（5 次 Defect）未分诊；2 个事件仍是占位 |

规则保真是环境"对不对"的总前提，五条评分都以它为底（上表最后一行）。目标 G1/G2 ≥ 99.9%：G2（从存档起）99.94% 已达；G3 农场 99.88%、人类 99.84%；G1 人类战斗闸 99.50% 未达。

### 1.6 环境还差什么（按优先级；不含评测与训练）

1. **保真收尾（P0，闭环已通）**：农场 → 导出 → G1/G2/G3 → 分诊已能一条线跑完（§3 命令）。剩：7 处开放分歧
   （`sim/specs/diff-report-e3-farm-2026-11-02.md` §4：PUNCH_OFF 事件→战斗转换，人类 G0G2 同病；GAMBLING_CHIP 回合开始选牌的
   `turn.phase`；导出器把放弃的卡牌奖励（Index ≥ 选项数）当事件 `choose`；STRATAGEM 牌堆选择顺序；K4P3/545X/SDR1 三处只在
   链式重放出现的隐藏抽牌堆状态与回合计数）；导出器未索引 R2 逐战斗回放 `native/replays/*.mcr`（存档已覆盖每个战斗起点，
   不阻塞），ancient_exit 存档未绑定；农场第 2/3 幕覆盖薄（1 局到第 2 幕、0 局到第 3 幕、A10 最高 14 层，需要更强的驱动：
   sim 训练策略或 LLM）；8 次真游戏 worker 崩溃（5 次 Defect；有崩溃报告的 2 次是未处理托管异常，文本已丢；`run_farm.py` 现在重启前把 `game.log`
   复制到 `<farm>/crashlogs/`）待分诊；V2Runner 房间段缺 G3Schema 适配（房间级 G2 以 `g3 --anchors-every` 为准）；
   人类战斗闸 8 行（GM28 战斗开始时遗物运行时状态等）。目标不变：G1/G2 ≥ 99.9%、G3 整局一致。
2. **Vec 停滞上限（P0，小）**：VecEnv 加 `floor_step_cap`（与评测器同口径：同一层 80 个非战斗决策即截断），加状态位，
   重生成 `sim/ENV_SPEC.md`。否则任何训练都会先学会刷循环。
3. **Vec 信息补齐（P1，一次 schema 追加）**：事件选项数值与引用物、本幕 Ancient、弃牌/消耗堆顺序、牌堆卡附魔/诅咒、
   手牌 affliction 数值；升级 encoder_version，重生成 ENV_SPEC。
4. **游戏原文补齐（P1）**：`scripts/export_sts2_localization.py` 导出 characters / rest_site_ui / 附魔表，替换 sim 措辞；
   移植 CRYSTAL_SPHERE、FAKE_MERCHANT 两个事件。
5. **信息集克隆补全（P1，搜索要用时再做）**：重采样遭遇队列、后续 Boss/Ancient、未开宝箱与未开卡牌奖励（生成器有约束，不是简单洗牌）。
6. **运维收尾（P2）**：arm64 AOT 镜像、`sts2eval` 多进程模式、GitHub workflow 启用、Huginn containerd 迁盘（管理员）、
   STS2_MCP 钩子纳入 hook-map。

做完 1–3，环境就可以交给训练；4–6 不阻塞训练。

## 2. 仓库地图

| 路径 | 内容 |
| --- | --- |
| `sim/Sts2Sim.Core` | 规则内核 + 1,400+ 内容模型（逐模型单文件，规则引用反编译 `文件:行`，见 `sim/specs/<area>.md`） |
| `sim/Sts2Sim.Server` | HTTP 对局服务器（LLM 用）：观测 JSON + 文本、token 动作、步骤日志、规则文案、`--reveal-moves`、`--bind` |
| `sim/Sts2Sim.Vec` + `sim/tools/vec` | 批量训练接口：C ABI（16 个 `sts2vec_*`）、实体表观测、候选动作、快照起点池、去克隆快路径、信息集克隆；Python `sts2vec`（AOT 或 hostfxr + ctypes + numpy）、`sts2vec eval` 协议评测器、示例策略 |
| `sim/Sts2Sim.Diff` | 差分闸：G0 首战 bundle、v2 bundle（G1/G2，`--baseline` 棘轮）、G3 种子整局重放（`--anchors` / `--anchors-every` / `--save-check` 用游戏存档锚点）、`.mcr` 解码 |
| `sim/Sts2Sim.Tests` / `Sts2Sim.Bench` | 自定义测试运行器（`-- <过滤子串>`），基准（combat 行、`vec` 子命令、run-soak） |
| `sim/tools/eval` | LLM 评测器 `sts2eval`（Anthropic SDK、并发、转录、计费、循环守卫、协议 v0.2 指标） |
| `sim/tools/play` | `sim_play.py` 命令行客户端 + 代理手册 |
| `sim/tools/train` | 训练冒烟（波次 G 新增；评测入口已改为 `sts2vec eval` 的薄包装） |
| `sim/ENV_SPEC.md` | NN / 程序化策略的一页环境说明（§5 由 schema 生成） |
| `sim/CONTRACTS.md` | Part A 内核 / B 决策 API / C run 层 / D 训练接口 的合同 |
| `sim/specs/` | 规格索引、集成日志 `integration.md`、差分报告 |
| `sts2rec/` | 录像工具（uv）：`validate`、`export-bundles-v2`（绑定存档锚点、R4 奖励、R5 选项键）、`export-run-oracles`、存档读取 |
| `mod/` | Sts2Recorder 被动录像 mod（REC 版，带存档锚点） |
| `scripts/` | `sim_server.sh`、`gates.sh`、`ci_linux.sh`、`linux_validate.sh`、`sts2_cluster.sh`、`cluster_capacity.sh`、`version_delta.py`、`version_stubs.py`、`hook_drift.py`、`version_update.sh`、`install_mod.sh`、`mcp_mod.sh`、`headless_*.sh`（`--recorder-root`）、`farm/`（E3 覆盖农场：`run_farm.py`、`validate_farm.py`、`summarize_farm.py`）、`mcp_build_env.sh` + `build_selection_mcp.sh`（按 `config/sts2mcp.lock.json` 应用带 sha256 的补丁链，含 `sts2mcp-v0111-compat.patch`；只认锁定的游戏版本，DLL 可复现并校验） |
| `knowledge/source/<版本>/` | 游戏文案（卡/遗物/药水/能力/事件/敌人/关键词）jsonl；`scripts/export_sts2_localization.py` 从 PCK 导出 |
| `docs/` | 本页、协议、设计、评审、状态日志、试玩报告 |

## 3. 怎么跑

```bash
dotnet build sim/Sts2Sim.sln -c Release                          # 0 警告
dotnet run -c Release --project sim/Sts2Sim.Tests -- Server Vec   # 过滤子串；无参数 = 全量约 7 分钟
scripts/gates.sh [--quick] [--bench]                              # 集成门一键（docs/gates.md）
scripts/ci_linux.sh --host raven                                  # 同一套门在 Huginn 的 Linux 容器里跑
scripts/sim_server.sh                                             # 对局服务器 8765；stop/status
python3 sim/tools/play/sim_play.py new --seed PLAY1 --character IRONCLAD
uv run --project sim/tools/eval sts2eval run --seedset std-a0-v1 --policy claude --model claude-sonnet-5-5 \
  --effort low --max-tokens 2048 --concurrency 10 --spawn --api-key-file ~/.config/sts2eval/anthropic_api_key
uv run --project sim/tools/eval sts2eval report tmp/eval/<run>    # 重算报表
STS2VEC_REQUIRE_FULL=1 uv run --project sim/tools/vec pytest -q   # 训练接口测试
uv run --project sim/tools/vec sts2vec bench --envs 1024 --threads 8 --steps 500 ; sts2vec check
dotnet run -c Release --project sim/Sts2Sim.Bench -- vec 1024 8 500 0 1024
uv run --project sim/tools/vec sts2vec eval --policy heuristic --seedset std-a0-v1      # NN/脚本策略按协议打分
uv run --project sim/tools/vec sts2vec eval --policy my_policy.py:act --seedset std-a10-v1 --out tmp/eval/mine
#   （`evaluate` 是 `eval` 的别名；策略合同 act(obs, ctx) -> (N,) 见 sim/ENV_SPEC.md、sim/tools/vec/examples/）
dotnet run -c Release --project sim/Sts2Sim.Bench -- vec 1024 8 500 0 1024 0                  # 末位 0 = 关快路径做 A/B
dotnet run -c Release --project sim/Sts2Sim.Bench -- fastpath 1000 0                           # 单线程 Step vs StepFast
dotnet run -c Release --project sim/Sts2Sim.Diff -- <bundle dir> [--v2] [--report out.json]   # 差分闸
scripts/version_update.sh <旧树> <新树> --new-version X.Y.Z --out tmp/vu/x [--execute]      # 版本更新一条龙（默认 dry-run）
python3 scripts/hook_drift.py --tree <新树>                                                  # 录像钩子漂移
python3 sim/tools/inventory/gen_prompt_sites.py [--check]                                     # 内容移植后重生成快路径提示点表
docker build -t sts2sim . && docker compose up                   # 见 docs/docker.md
scripts/sts2_cluster.sh up 8 --host raven                        # Huginn 起 8 个服务器并等 /health（本机去掉 --host）
scripts/sts2_cluster.sh tunnel --host raven                      # 前台 ssh -L，另开终端用
uv run --project sim/tools/eval sts2eval run --urls $(scripts/sts2_cluster.sh urls --host raven) ...   # 种子集分摊到多服务器
scripts/sts2_cluster.sh down [--host raven]
scripts/linux_validate.sh                                         # Huginn 全门
# 保真闭环（E3）：农场 → 导出 → G1/G2/G3；细节（沙盒升阶、fast_mode、MCP 构建）见 docs/sim-port/e3-farm-2026-11-02.md 与 diff-report-e3-farm
/Users/Aincrad/dev-proj/hack-sts2/scripts/mcp_mod.sh on        # 只在农场期间；用主检出的脚本（stash 在主检出，worktree 副本找不到）
STS2_MCP_SOURCE_REPO=/Users/Aincrad/dev-proj/STS2MCP STS2_HEADLESS_PORT_BASE=15770 scripts/headless_provision.sh 4 <farm>/pool
python3 scripts/farm/run_farm.py --pool <farm>/pool --farm-root <farm> --workers 1,2,3,4 --duration-min 120 --hard-stop-min 150
python3 scripts/farm/validate_farm.py <farm> <farm>/validate.json && python3 scripts/farm/summarize_farm.py <farm> --validate <farm>/validate.json
/Users/Aincrad/dev-proj/hack-sts2/scripts/mcp_mod.sh off       # 必做；status 复核
(cd sts2rec && uv run sts2rec export-bundles-v2 <bundles> --root <sessions> --sim-core ../sim/Sts2Sim.Core)   # 必须带 --root（默认是人类语料）
dotnet run -c Release --project sim/Sts2Sim.Diff -- <bundles> --v2 --baseline sim/specs/diff-baseline-e3-farm.json --report <r>.json   # G1/G2 + 棘轮
dotnet run -c Release --project sim/Sts2Sim.Diff -- g3 <bundles> --report <r>.json      # G3 种子整局
#   加 --anchors：分歧后从下一个存档续跑；--anchors-every：每段从游戏存档起（G2 存档导入）；--save-check：链式 sim 与存档逐行对照
```

评测输出在 `tmp/eval/<run>/`（git-ignored）：`config.json`、`games/*.jsonl` 每步转录、`report.md/json`。

## 4. 协议与榜单（v0.2）

- 种子集：`std-a0-v1`（A0，20 局）、`std-a10-v1`（A10，20 局）、`smoke-a0-v1`（冒烟 10 局），文件在 `sim/tools/eval/seedsets/`，带 sha256。
- 头牌指标：按幕归一化的进度均值 [95% CI] 与 Wilson 胜率；副指标：层数、步数、非法/卡死/封顶、token、费用、耗时。
- 当前榜单（v0.3 正式行，服务器 `c3df586`，紧凑观测、招式隐藏，Sonnet 5.5 effort low）：A0 0.601 [0.512, 0.691]、A10 0.316 [0.227, 0.404]，0 胜；sim NN 赛道：随机 0.087 / 0.045，启发式 0.221 / 0.110。历史行见协议 §8。
  这两行测于波次 J 对等审计之前的观测（服务器文本/紧凑 compact-v1）；之后的观测多了 Boss/Ancient、三堆牌等行（compact-v2），新跑的行需注明口径。
- NN 赛道（`sts2vec eval`，同一计分）：random 0.087 / 0.045、first 0.193 / 0.102、heuristic 0.221 / 0.104（A0 / A10；协议 §8，测于 E3 规则修复之前）。
  E3 合入后重测：random 0.087 / 0.045、first 0.194 / 0.103、heuristic 0.221 / 0.110（协议行未改）。
- 费用口径：A0 约 $1.8–2.5/局，A10 约 $1.1/局；缓存命中率约 58–61%。

## 5. 流程与约定

- Fable 编排、工程子代理一律 Opus；试玩/评测代理可用 Sonnet。精简流程：简报 → 并行所有者（隔离 worktree，先 `git checkout -B <分支> <基线 sha>`）→
  单一集成者跑门 → 提交。不设对抗校验。
- 规则真值只有反编译树 `output/server-bootstrap/linux-decompiled/`（只读，不入库）；禁用 StS1 记忆数值。
- 不提交轨迹/存档/bundle/`tmp/`；不改游戏存档；`STS2_MCP` 只在代理运行期间安装，结束必须 `scripts/mcp_mod.sh off`
  （用主检出的 `/Users/Aincrad/dev-proj/hack-sts2/scripts/mcp_mod.sh`：stash 在主检出）。
- 提交信息祈使句，末行 `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`。
- 分支：会话分支 `claude/sts2-eval-pipeline-a3aee7` 含波次 G、J、E3；`claude/sim-source-recreation-9392df` 停在 `ebc055f`；都未推送。

## 6. 凭证与机器

- Anthropic API key：`~/.config/sts2eval/anthropic_api_key`（chmod 600，仓库外；用 `--api-key-file`）。本机 SDK 需 `Accept-Encoding: gzip`（评测器已处理）。
- Huginn（`ssh raven`，管理 `ssh raven-labs`）：8 vCPU x86_64，Ubuntu 24.04；用户级 .NET 9 在 `~/.dotnet`；Docker 29.9（数据目录 `/data/raven/docker`）；根盘仅 3 GB 余量，大文件放 `/data/raven`。
- 东京 GCP VM：生产机，只用于一次性验证镜像（见 `~/.claude/rules/tokyo-vm.md`）。
- 游戏：本机 Steam 安装，mods 目录见 `scripts/mcp_mod.sh`；人类录像 `~/Library/Application Support/Sts2Recorder/sessions/`（只读，核心资产）。
  mods 目录现为 REC recorder v0.3.0（R1 存档副本、R2 回放副本、R4/R5/R7/R11；`Sts2Recorder.conf` 节流保留），旧版备份在会话 worktree 的
  `tmp/recorder-backup/20261102-012808/`；STS2_MCP 已关（E3 集成时 `mcp_mod.sh status` 复核）。
- 周额度：2026-11-01 深夜为 82%，11-07 重置；一个 4 所有者波次约 2%。

## 7. 进行中与开放问题

- 波次 J（已合入 2026-11-02，记录见 `sim/specs/integration.md` "J integrate"，只做环境）：去克隆快路径（`sim/VEC.md` "Fast path"；
  RunSim 单步 1.6–1.9×，Vec 端到端 +7–8%、每步 CPU −9–10%，见 `sim/BENCH.md`——旧票里的"2.6×"只是 `Step` 调用本身，
  Vec 每步约 80 µs CPU 的大头是观测编码、`LegalActions` 与候选表）；信息集克隆（`sim/VEC.md` "Determinized clones"，
  第 16 个导出 `sts2vec_clone_determinized`）；UI 对等审计（`docs/parity-audit-2026-11-02.md`，事件选项 `?` 票关闭：
  实际是 17 个事件 39 个键，已全部补上）；`sim/ENV_SPEC.md` + `sts2vec eval` + NN 赛道基线；多服务器集群脚本与 AOT 镜像
  （`docs/capacity-2026-11-02.md`）；版本更新执行器、钩子漂移、Linux CI（`docs/version-migration-playbook.md`、`docs/gates.md`）。
- 波次 G（已合入）：紧凑观测（每步成本 −19.5%，full/compact A/B 未跑）、`scripts/gates.sh`、内核映射、训练冒烟
  （学到商店移除循环，`docs/train-smoke-2026-11-02.md`）、Docker amd64。
- E3（已合入 2026-11-02，记录见 `sim/specs/integration.md` "E3 integrate"）：REC recorder v0.3.0 装进人类 mods 目录并验证锚点
  （`docs/sim-port/e3-recorder-install-2026-11-02.md`；当时 `build_selection_mcp.sh` 内联替换适配 v0.111；现已换成锁文件驱动的补丁链，见该文 §3）；4 进程无头覆盖农场 39 局、
  0 胜、MCP 已关（`docs/sim-port/e3-farm-2026-11-02.md`）；导出器存档锚点绑定 + R4/R5、G3 `--anchors` / `--anchors-every` /
  `--save-check`、6 处 Core 修复（存档导入的玩家战斗 id、6 个弱遭遇 Tags、战斗结束钩子拿到房间、`RelicCmd.Obtain` 移出遗物袋 +
  FloorAddedToDeck、起始遗物第 1 层、AUTOMATION 显示数值）与棘轮基线（`sim/specs/diff-report-e3-farm-2026-11-02.md`）。
  数字见 §1 保真行；合并没有改变任何保真数字。
- 环境剩余项见 §1.6。评测侧（不在本阶段）：对照基线（Opus/Haiku/effort high/thinking off，约 $200，统一到审计后观测口径）、
  full/compact A/B；E4 搜索自举。
- 已知小票：商店 base_price 推算值；快路径下若一步中途抛内容缺口/内核错误，`last_episode` 摘要按部分推进的状态计算（只影响错误路径）；
  去战斗/未知节点的移动总走克隆路径（约 2% 步数，可按本幕遭遇池是否含 KNOWLEDGE_DEMON/THIEVING_HOPPER 细化）；
  信息集克隆导出存档不忠实（`RunExporter.ToSave` 写原 seed）；Huginn 根盘：containerd 快照与构建缓存在根盘，并发构建曾把根盘写满几分钟（需管理员迁盘）。

## 8. 优先级

| 级别 | 内容 |
| --- | --- |
| P00 | 保真收尾：7 处开放分歧、Defect 崩溃分诊、第 2/3 幕覆盖（更强驱动）（§1.6 第 1 条）；Vec 停滞上限（§1.6 第 2 条） |
| P0 | Vec 信息补齐 schema 追加；游戏原文补齐与两个未移植事件；然后把环境交给训练 |
| P1 | 信息集克隆补全预生成内容；`sts2eval` 多进程；arm64 AOT 镜像；观测编码提速（Vec 每步 CPU 的大头） |
| P2 | 论文材料；跨游戏迁移实验（hack-balatro）；人类语料统计并入报表；GitHub workflow 启用 |

## 9. 文档索引

`docs/handoff-2026-11-01.md`（历史交接）· `docs/p1-sim-full-port-plan-2026-11-01.md`（状态日志）· `sim/CONTRACTS.md` · `sim/VEC.md` · `sim/PLAY.md` ·
`docs/sts2-bench-protocol-v0.2-2026-11-01.md` · `docs/docker.md` · `docs/linux-validation-2026-11-01.md` · `docs/version-migration-playbook.md` ·
`sim/specs/integration.md`（每波门的数字）· `docs/gates.md` · `docs/train-smoke-2026-11-02.md` · `sim/specs/kernel-map.json`· `sim/specs/diff-report-w4-*.md` · `docs/sim-port/playtest-2026-11-01.md` · `docs/a10-first-principles-review-2026-10-27.md` ·
`docs/sim-port/design-diff-gate.md` · `sim/ENV_SPEC.md` · `docs/parity-audit-2026-11-02.md` · `docs/capacity-2026-11-02.md` ·
`docs/version-stubs-demo-2026-11-02.md` · `docs/sim-port/e3-recorder-install-2026-11-02.md` · `docs/sim-port/e3-farm-2026-11-02.md` ·
`sim/specs/diff-report-e3-farm-2026-11-02.md` · `sim/specs/diff-baseline-e3-farm.json`（G1/G2 棘轮）。会话记忆：`~/.claude/projects/-Users-Aincrad-dev-proj-hack-sts2/memory/`。
