# 游戏版本迁移手册（playbook）

目的：游戏大版本更新后，把整套栈（反编译真值 → sim → 差分闸 → 评测
harness）在 **≤1–2 天**内迁到新版本。由 v0.107.1→v0.111.0 搬迁沉淀
（2026-10-30）；每次迁移后回填教训。

## 触发与检测

- recorder 每局 manifest 记录游戏版本——新版本首次出现即触发本手册。
- CLAUDE.md 既有规则：游戏更新后重建 recorder 并对 fresh decompile 复核
  钩子（docs/hook-map.md 漂移流程）。

## 工具（2026-11-01）

```bash
# 一条命令看清迁移面：双树 diff → 变更文件 / 模型 id / sim 文件 / 规格条文 / 测试 / 重跑计划
python3 scripts/version_delta.py <旧树> <新树> --old-version A.B.C --new-version X.Y.Z \
    [--out tmp/version-delta/<名>] [--cite-tree <规格引用的树>]
# 清单执行器：默认 dry-run 只打印；--run 依次执行可自动的步骤，首个失败即停
scripts/version_update.sh <旧树> <新树> --new-version X.Y.Z [--old-version A.B.C] \
    [--bundles <G0 bundle 目录>] [--bundles-v2 <G3 目录>] [--run]
python3 -m unittest discover -s scripts -p 'test_*.py'      # version_delta, kernel_map, version_stubs, hook_drift
# 逐模型工作包（差分 manifest → packets/INDEX.md + 每模型一页；新模型带骨架，--check-skeletons 编译验证）
python3 scripts/version_stubs.py <out>/manifest.json --out <out> [--check-skeletons] [--trimmed-index F --top N]
# 录像钩子漂移：docs/hook-map.md + mod/src 的目标对一棵反编译树逐个核对（退出码 1 = 有漂移）
python3 scripts/hook_drift.py --tree <新树> [--json F] [--md F]
# 合成"新版本"树（无真实新版本时演练整条流程）
python3 scripts/version_synthetic.py <源树> <目标目录> [--force]
# 内核映射：反编译内核文件 → sim 文件（version_delta 读它）；新树上先重生成，--check 只校验不写
python3 scripts/kernel_map.py [--tree <新树> --tree-version X.Y.Z] [--unmapped] [--check]
```

- **内核映射 `sim/specs/kernel-map.json`**（`scripts/kernel_map.py` 生成，2026-11-02）：内核没有内容那样的
  `Defs/<Class>.cs` 一一对应，靠这张表回答"这个内核文件变了，要动哪些 sim 文件"。键是相对树根的反编译路径
  （`MegaCrit.Sts2.Core.Commands/PowerCmd.cs`），`key` 是规格引用形式。三类派生来源逐文件记在 `via`：
  `type`（sim Core 声明同名类型，或固定改名 `<T>State`、`<X>Model → <X>Def`；内容 Defs 不算）、`cite`（sim
  源码注释里的 `文件:行`）、`spec`（规格 / sim-port 文档里同一行既引用反编译文件又点名 sim 符号，至少 2 行；
  若该符号同时是游戏类型，只算它自己的文件）。其余靠手工：`curated`（单文件记录，`replace` 可覆盖派生结果）
  与 `patterns`（按命名空间 glob，如 `Saves/*` → `no_port`）；脚本重生成时保留这两块。状态：`mapped`
  （列出 sim 文件）/ `no_port`（有意不移植，必须写 reason）/ `unmapped`（待评审）。v0.111.0 Linux 树：564 个
  内核文件，mapped 374、no_port 190、unmapped 0（派生：type 201、spec 79、cite 23；手工：pattern 116、
  curated 66 个文件）。`version_delta.py` 对 kernel 文件把映射里的 sim 文件并入 `sim_files`，报告新增
  "Kernel map coverage" 一节（变更内核文件的 mapped / no_port / unmapped 计数、unmapped 文件及行数、
  no_port 文件及理由），unmapped 进入重跑计划的 notes。树里新增、表里没有的内核文件按 `patterns` 兜底，否则
  记 `unmapped`。

- `version_delta.py`（纯 stdlib）：两种目录布局（嵌套 `MegaCrit/sts2/Core/Models/Cards/X.cs` 与扁平
  `MegaCrit.Sts2.Core.Models.Cards/X.cs`）统一成规格引用的键 `Models.Cards/X.cs`；去注释、空白、换行、
  `using` 后再比，纯格式变化记 `cosmetic`；同名跨命名空间记 `moved`。分类：content（卡/遗物/药水/能力/
  怪物/遭遇/事件及其余模型种类）、kernel（Commands、Hooks、Combat/Entities、Random、Map、Rewards、Rooms、
  Runs、Saves 等）、out_of_scope（Nodes/UI/VFX/联机/平台/开发工具）。映射：`sim/specs/inventory/<kind>.json`
  的 `file → model_id`（缺省按 Slugify 推）、`Defs/<Class>.cs` 与同名 sim 文件、sim 源码里的反编译路径引用；
  规格条文：`sim/specs/**/*.md` 与 `docs/sim-port/*.md` 中每条 `文件:行` 引用在 `--cite-tree`（默认
  Linux 真值树）里落到成员（方法/属性；状态机与 lambda 归其源方法，重载按参数类型区分），命中级别
  `line` / `member` / `added` / `removed`；测试：`Sts2Sim.Tests` 里提到模型 id、类名或映射 sim 类的方法。
- **批量改动识别**：同一编辑模式（类名掩码后）出现在 ≥8 个文件即为 bulk（如 `FromCard(this)` →
  `FromCard(this, cardPlay)` 一处签名变更覆盖 178 个文件）；只含 bulk 的文件标 `bulk only`，其余才是逐文件
  移植清单。
- **反编译设置不一致检测**：一棵树把 async 方法输出为 `IAsyncStateMachine` 结构体、另一棵输出 `async
  Task` 时，报告顶部告警（这种对比几乎每个文件都"变"）——先用相同 ILSpy 设置重反编译。
- 输出：`<out>/report.md`（汇总表、重跑计划、bulk 表、逐文件表、需重读的规格条文、逐文件规范化 diff，行号
  为原文件行号）+ `<out>/manifest.json`（`sts2sim.version-delta/v1`）。全树对比约 10 s（设置不一致时
  约 25 s）。
- 重跑计划规则：任一 kernel 变更 ⇒ 全量测试 + run soak；内容按种类映射 content soak（卡/能力/附魔/苦难/
  充能球 → `EveryCard`，怪物/遭遇/能力 → `EveryEncounter`，遗物 → `EveryRelic`，药水 → `EveryPotion`）；
  战斗内核或战斗内容变更 ⇒ G0；任何范围内变更 ⇒ G3；rng/save 变更 ⇒ 先查 `Saves.Migrations`；版本号
  变化 ⇒ `GameVersions.Default` 与 `knowledge/source/v<ver>` 必须迁移，增删模型列为 `VersionExclusive`
  候选。
- 演示：[v0.107.1 → v0.111.0 报告摘要](version-delta-demo-2026-11-01.md)（content 546 / kernel 134，
  其中 228 个内容文件只含 bulk 改动；W0 人工清单的 171 个 `changed` 全部命中；变更内核文件有 sim 文件的
  由 45 升到 95 / 134，其余 39 个是 `no_port`）。
- **W0 清单补漏**：`version_delta.py` 找到、W0 两个来源都漏掉的"唯一改动"内容文件，逐个人工判定后写入
  `sim/tools/inventory/classify/version-deltas/triage.py` 的 `MANUAL_VD`，`build.py` 把它们并入
  `sim/specs/inventory/version-deltas.json`（`sources: version_delta`）。迁移到新版本时同样处理：报告里
  "有唯一改动但不在 W0 清单"的文件必须判定，否则 taint 过滤会把真改动的旧录像当成可比。

## 一键执行器：差分 → 工作包 → 钩子漂移 → 构建 → 测试 → 闸门（2026-11-02）

版本更新现在是一份基本自动跑完的清单：`scripts/version_update.sh` 默认 dry-run（只跑写入 `<out>` 的分析三步，
其余步骤打印命令），`--execute` 依次执行全部可自动步骤，首个失败即停；结尾打印并写出 `<out>/CHECKLIST.md`
（每步状态 + 剩余人工项）。

| 步 | 内容 | 自动化 | 产物 |
| --- | --- | --- | --- |
| 1 | 反编译新版本 | 人工 | 新树 + 指纹 |
| 2 | `version_delta.py`（内核映射未覆盖、反编译设置不一致会生成 action 项） | dry-run 也跑 | `manifest.json`、`report.md` |
| 3 | `version_stubs.py`：每个变更/新增/删除内容模型一个工作包；`--check-skeletons` 把新模型骨架放进 sim Core 编译一遍（仅 execute） | dry-run 也跑 | `packets/INDEX.md`、`packets/<kind>/<ModelId>.md`、`index.json` |
| 4 | `hook_drift.py`：`docs/hook-map.md` 最新附录的 Patched members 表、Events、FieldRefs 段，加 `mod/src` 里每个 `patch:`/`event:`/`fieldref:` 目标，逐个对新树查文件、类型、成员、返回类型、参数类型与参数名（Harmony 按名绑定） | dry-run 也跑；漂移不致命，生成 action 项 | `hook-drift.md/json` |
| 5 | inventory 重生成 | 人工（会改 `sim/specs/inventory`） | |
| 6 | `GameVersions.Default` / `[VersionExclusive]` | 人工 | |
| 7 | 知识导出 | 提醒（`--export-knowledge` 且装有新版游戏才自动） | `knowledge/source/v<ver>` |
| 8 | 构建 `--no-incremental`，0 警告 0 错误 | execute | `logs/build.log` |
| 9 | 受影响测试（delta 的测试过滤 + content soak + run soak）；`--full-tests` 跑全量 | execute | `logs/tests.log` |
| 10 | selftest 300 局 | execute | `logs/selftest.log` |
| 11 | G0（`--bundles` 或环境变量 `STS2SIM_G0_BUNDLES`） | execute | `g0.json` |
| 12 | G3（`--bundles-v2`） | execute | |
| 13 | harness 搬迁（recorder/MCP 重建、规则证据、存档基线、benchmark minor bump；逐步清单见附录 A） | 人工 | |

工作包内容见 [`docs/version-stubs-demo-2026-11-02.md`](version-stubs-demo-2026-11-02.md)（v0.107.1 → v0.111.0：
546 个包，新增 36 / 删除 3 / 唯一改动 279 / 纯批量 228，23 个骨架全部编译通过）。钩子检查在当前 v0.111.0 树上
0 漂移（59 个 hook-map 目标 + 57 个 mod 目标全部 ok，0.3 s），在 v0.107.1 树上也是 0 漂移（26 个行号移动，仅提示）。

### 合成版本对的实跑记录

没有第二个真实新版本时，用 `scripts/version_synthetic.py` 从规则真值树造一棵"新版本"树：演示文档的五处规则改动
（`Bash` 伤害 8 → 9、`LeafSlimeM` ClumpDamage 9 → 10、`Anchor` 格挡 10 → 12、`PowerCmd.Apply<T>` 单目标
`amount == 0` 提前返回、`StandardActMap.maxElites` 15 → 16）+ 一处钩子签名改动（`RunManager.OnEnded(bool)` →
`OnEnded(bool, bool)`）+ 一个新增模型（`SyntheticBash`，`Bash` 改类名）。每处改动必须在源树里恰好命中一次。

```bash
T=/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled
S=<scratch>/version-update        # 本次：会话 scratchpad 下的 version-update/
python3 scripts/version_synthetic.py "$T" "$S/tree-v0.111.1-synthetic" --force          # 7 处改动，约 2 s
scripts/version_update.sh "$T" "$S/tree-v0.111.1-synthetic" --old-version 0.111.0 --new-version 0.111.1 \
    --out "$S/dry-run"                                                                    # dry-run
STS2SIM_G0_BUNDLES=/Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/sim-source-recreation-9392df/tmp/handoff/diff-bundles/bundles \
scripts/version_update.sh "$T" "$S/tree-v0.111.1-synthetic" --old-version 0.111.0 --new-version 0.111.1 \
    --out "$S/execute" --execute --check-skeletons                                        # execute
```

结果（2026-11-02，Mac M4 Pro，机器同时在跑 E3 农场，负载 10–50；代码即本提交）：

| 步 | dry-run（3 s） | execute（476 s，退出码 0） |
| --- | --- | --- |
| 2 差分 | content 4、kernel 3（`PowerCmd`、`StandardActMap` 改动；`RunManager` 只动签名），内核映射 unmapped 0；计划：全量测试、content soak（卡/遭遇/遗物）、run soak、G0、G3 | 同左（2 s） |
| 3 工作包 | 4 个：`CARD.SYNTHETIC_BASH` 新增（骨架）、`CARD.BASH` "DamageVar 8 -> 9"、`RELIC.ANCHOR` "BlockVar 10 -> 12"、`MONSTER.LEAF_SLIME_M` "DeadlyEnemies 9 -> 10"（A9+ 值） | 同左 + 骨架在 sim Core 内编译通过（0 诊断，3 s） |
| 4 钩子 | DRIFT 1：`patch:RunManager.OnEnded` signature_changed（文档 `SerializableRun OnEnded(bool isVictory)`，树上 `OnEnded(bool isVictory, bool isAbandoned)`）；其余 115 个目标 ok | 同左 |
| 8 构建 | 打印命令 | 0 警告 0 错误 |
| 9 测试 | 打印过滤串（93 个测试类 + 3 个 content soak + run soak；`PowerCmd` 牵动大半测试） | 1057/1057 通过，PENDING 0（451 s，含 `SelfConsistencyTests` 的 F2/F7） |
| 10 selftest | 打印命令 | 300/300 完成，0 崩溃，0 封顶，0 服务端错误 |
| 11 G0 | 无 bundle 记为 skipped | 4/4 bundle，69/69 决策一致 |
| 12 G3 | skipped（无 v2 bundle） | skipped |

两次都以清单收尾，剩余人工项：反编译指纹；移植 4 个工作包；修 `OnEnded` 钩子（mod + hook-map 新附录 + 重建
recorder）；inventory 重生成；`GameVersions.Default` → 0.111.1；导出 `knowledge/source/v0.111.1`；按计划跑全量
测试（或 `--full-tests`）；录 1–2 局新版本对局补 G0/G3；harness 搬迁。合成树上 sim 没有移植改动，所以测试与 G0
全绿只说明执行器本身走通，不说明 sim 已跟上 0.111.1——那正是工作包要做的事。

## 五步流水线

1. **反编译新版本**（带引用程序集）→ `output/.../<ver>` 树 + manifest
   指纹（版本、commit、sts2.dll SHA-256）。旧树保留，永不覆盖。
2. **双树 diff → 任务清单**（最大提速器；先 `scripts/kernel_map.py --tree <新树>` 重生成内核映射并处理
   unmapped，再 `scripts/version_delta.py`）：对"已移植文件集合"做新旧树
   diff——sim/specs/*.md 里每条规则都带反编译 `文件:行` 引用，已移植文件
   清单可机器提取。命中的文件 = 本次移植任务列表；未命中 = 免检。种子
   算法/存档 schema 类变更会在 `Saves.Migrations` 命中（v18→v19 即先例）。
3. **sim 对齐**：file-per-change 的 opus 批量代理只改命中文件；全测试绿。
4. **差分闸回归 = 迁移验收器**：旧版本语料按旧 seeding 继续重放（历史
   资产永不作废）；新版本攒 1–2 局录像（正常游玩即可）→ 新 seeding 路径
   重放全绿 = 迁移通过。
5. **harness 搬迁**（逐步清单见附录 A）：MCP 重建（`scripts/build_selection_mcp.sh`，
   按 `config/sts2mcp.lock.json` 构建）→ 规则证据重导（`export_sts2_rule_evidence.py`
   → `knowledge/source/<ver>/`）→ 冻结存档基线重切（make_unlocked_save +
   baseline manifest SHA-256）→ stub 策略 `run_bench` 冒烟。
   清单执行器 `scripts/version_update.sh` 已把第 2–4 步的可自动部分（delta、知识导出、构建、受影响测试 /
   全量测试、selftest、G0、G3）串起来，第 1、5 步与 inventory 重生成、注册表版本改动打印为人工步骤。

## 版本治理

- 成绩按游戏版本 pin，永不跨版本混排；`knowledge/source/` 与反编译树按
  版本并存。
- benchmark semver：游戏版本变更 ⇒ minor bump + 规则漂移说明（来自第 2
  步 diff 报告）。
- **完成定义**：新版本上 stub 基线跑通 + replay 认证通过 + 差分闸全绿。

## 已知陷阱（历史教训）

- **RNG 种子算法可整体更换**（v0.107.1 HashOld 相加 → v0.110+ XXH64）：
  `Saves.Migrations` 是第一检查点；sim 保留双实现按版本选择。
- 反编译 async/局部函数体可能缺失（Neow OnChosen 先例）：关键规则读不到
  时查 sts2.dll IL，禁止臆测补全。
- `build_selection_mcp.sh` 的默认 STS2MCP 源码路径曾按脚本所在仓库根的兄弟
  目录解析，worktree 里必须显式传 `STS2_MCP_SOURCE_REPO`。现由
  `scripts/mcp_build_env.sh` 经 `.git` → `commondir` 找到主检出旁的 STS2MCP，
  worktree 内无需再传（传了仍然优先）。
- 静态字段按进阶一次性初始化（CardRarityOdds 等）的进程内复用陷阱，迁移
  后重验。
- recorder 的 `context_model` 标签粘滞（PushModel 不出栈）：升级 recorder
  时考虑补 PopModel 钩子。

## 状态

- 2026-11-02（波次 J）：`version_stubs.py`（工作包 + 可编译骨架）、`hook_drift.py`（录像钩子漂移）、
  `version_synthetic.py`（合成版本对）；`version_update.sh` 改为 dry-run 也跑分析三步、`--execute` 跑到 G0、结尾
  输出清单；Linux 容器门 `scripts/ci_linux.sh`（`docs/gates.md` "Linux CI"）。单元测试 53 个。
- 2026-11-02：内核映射 `sim/specs/kernel-map.json` + `scripts/kernel_map.py`（564 个内核文件全部判定：
  mapped 374、no_port 190）；`version_delta.py` 读映射并报告内核覆盖（变更内核文件有 sim 文件 45 → 95 / 134）；
  W0 清单补入 28 个漏判内容文件（27 `refactor_only`，`MusicBox` 为 `changed`）；单元测试 30 个
  （`test_version_delta.py` 21、`test_kernel_map.py` 9）；闸门脚本 `scripts/gates.sh`（`docs/gates.md`）。
- 2026-11-01：`scripts/version_delta.py` + `scripts/version_update.sh` 落地（19 个单元测试）；
  v0.107.1 → v0.111.0 演示见 `docs/version-delta-demo-2026-11-01.md`。
- 2026-10-31（a10 分支 `f713da2`，2026-11-02 合入）：第 5 步（评测 runner
  v0.107.1→v0.111.0）完成：锁文件驱动的 MCP 构建（沙盒 DLL `83fc6828…`）、
  `knowledge/source/v0.111.0` 的规则证据与 source 块、基线 v2 `c45ec7a9…` /
  v2-a10 `27c009af…`、bench 配置 v1.1；A0/A10 stub 冒烟、双实例并行与确定性
  检查通过。清单与实测笔记见附录。
- 2026-10-30：手册首版。

## 附录 A：harness 搬迁清单（第 5 步 / 执行器第 13 步）

来自 v0.107.1 → v0.111.0 的实测（a10 分支 `f713da2`，2026-10-31）。版本相关的东西只在三处：
`config/sts2mcp.lock.json`、`knowledge/source/<ver>/`、`config/bench/*.json`；
`sts2rec/tests/test_bench_runner.py::test_bench_configs_match_the_pinned_game_and_knowledge`
检查三者一致。

1. **触发**：`scripts/build_selection_mcp.sh` 因游戏版本或 sts2.dll sha256 与锁文件 `game` 段不符而拒绝
   构建——这就是信号。`STS2_MCP_ALLOW_UNPINNED_GAME=1` 再跑一次，拿到编译错误清单（只用于迁移）。
2. **MCP 补丁与锁文件**：编译漂移写成新补丁 `patches/sts2mcp-v<ver>-compat.patch`（参照
   `sts2mcp-v0111-compat.patch`：每处照抄引擎自己的新调用、保持输出 schema 不变），追加进锁文件
   `patches`（带 sha256）；更新 `game` 段（version、commit、各程序集目录 sts2.dll sha256、
   `knowledge_source`）。构建通过后把沙盒 DLL sha256 记入 `sandbox_build.dll_sha256`（构建可复现，换目录或
   换提交结果不变）。不要再往构建脚本里加内联替换。
3. **知识与规则证据**：表文件由执行器第 7 步的 `export_sts2_localization.py` 写入 `knowledge/source/<ver>/`
   （sim 的 RulesText 读它）。harness 另需 `export_sts2_knowledge.py --offline` 的 source 事实：它拒绝写入
   localisation 导出目录（`--overwrite-localization-export` 才放行），所以导出到草稿目录，再把
   `model_id`/`source` 按 `id` 并入表行、`mechanics.jsonl` 原样拷入、manifest 补 `sources`/`coverage`
   （做法见 `knowledge/README.md`）。然后 `export_sts2_rule_evidence.py --output knowledge/source/<ver>`
   写 `rule-evidence.json`；新版本须先列入它的 `REVIEWED_VERSIONS`（重读每个被引文件，含义变了就写
   覆盖）。反编译树需带引用程序集。
4. **冻结存档基线**：`STS2_HEADLESS_FRESH_PROFILE=1 scripts/headless_provision.sh` 建新池（新档，不读
   人类存档；默认模式会从人类存档复制设置并播种解锁）→ 启动一次让游戏写出 progress.save →
   `make_unlocked_save.py` → `baseline.py freeze` 冻结 v2 / v2-a10 到 `$STS2_BENCH_BASELINE_ROOT/<ver>/`
   （命令见 `docs/benchmark.md`）。新档 `unique_id` 随机，基线无法逐字节重做，**文件必须保留**。
5. **bench 配置**：`config/bench/*.json` minor bump、`game_version` / `game_commit`、新基线名与 sha256。
6. **验收**：stub `run_bench`（`--start-method eval`）+ `--determinism SEED --starts 2`；seed_evidence
   三源（game.log、current_run.save、MCP readback）一致，每步 `authority_verified`。

TODO：把 1–6 的可自动部分接进 `scripts/version_update.sh` 第 13 步。

## 附录 B：v0.111 harness 实测笔记

- 评测 API、seeded-embark、action-receipts 三个补丁原先只在私有 hack-sts2-cloud 仓库，本仓库脚本只打
  两个补丁；现已全部纳入 `patches/` 并由锁文件列出。补丁不在本仓库 = 迁移时找不到。
- 未打补丁的 upstream 在新版不编译（v0.111.0 删了 `StartRunLobby.MaxPlayers` 和
  `LoadRunLobby.ConnectedPlayerIds`）；`headless_provision.sh` 已不再构建它，`build_upstream_mcp.sh`
  只认它锁定的旧版本。
- macOS 工作进程存档在 `home/Library/Application Support`，不是 Linux 的 `home/.local/share`；基线还原、
  current_run.save、history 查找都按实例自动选路径。
- `run_bench` 必须把配置的 `game_version` 传给 `sts2_live start`，否则悄悄用 v0.107.1 的规则知识
  （`sts2_live` 的 `--game-version` 默认仍是 v0.107.1）。
- `make_unlocked_save.py` 依赖的源码标记会搬家（v0.111 的 epoch 列表移进 `EpochModel` 静态构造与
  `AgnosticUnlocks`，progress schema 21 → 24）：先跑不带 `--saves-dir` 的计数核对。
- 合成的 settings.save 要写 `language: eng`，否则工作进程跟随主机语言（中文系统会得到中文卡面文本）。
- .NET SDK 默认 SourceLink 把外层仓库 HEAD 写进 MCP DLL，sha256 随提交变化；构建已关闭
  （`EnableSourceLink=false` 等）并加 PathMap，可以固定 pin。
- Steam 当前在 public-beta 分支且 AutoUpdateBehavior=1（启动即更新）：大版本可能在任何一次启动后到来，
  锁文件的版本闸会第一时间拦下。
