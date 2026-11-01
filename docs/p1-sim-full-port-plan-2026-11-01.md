# P1 — Route C 全量源码移植：完整 1:1 环境实施方案

2026-11-01 · 分支 `claude/sim-source-recreation-9392df` · 承接
[P0 走通骨架 + 差分闸](p0-sim-port-plan-2026-10-28.md) · 依据
[A10 第一性原理评审](a10-first-principles-review-2026-10-27.md) §4/§6 ·
游戏作者已书面许可（学术研究项目）。

## 目标

把 `sim/` 从「第一幕 weak 战斗切片」建成**完整的、与真游戏逐决策一致的
无头可克隆环境**：

| 维度 | 范围 |
| --- | --- |
| 角色 | 5 个可玩角色：Ironclad / Silent / Defect / Necrobinder / Regent（`Deprived` 为 mock，不可玩） |
| 卡牌 | 596 个模型：五角色卡池 + 无色 / 诅咒 / 状态 / Token / 事件 / 任务卡（含升级） |
| 能力 | 268 个（含用于测试的 mocks 标记但不移植） |
| 遗物 / 药水 | 300 / 65，含战斗内与 run 层两类行为 |
| 怪物 / 遭遇 | 121 / 91：四幕（Overgrowth、Underdocks、Hive、Glory）weak / normal / elite / boss + 事件遭遇 |
| 事件 | 68 个事件 + Neow + 远古事件 |
| run 层 | 地图生成、房间（战斗 / 事件 / 商店 / 营地 / 宝箱）、奖励与赔率、幕切换、A0–A10 进阶效果（`AscensionManager.maxAscensionAllowed = 10`）、药水使用 / 丢弃 |
| 决策 API | 覆盖所有屏幕与**所有玩家选择提示**（手牌 / 牌堆 / 三选一 / 网格；牌组升级 / 转化 / 附魔 / 删除；奖励卡与替代项；捆绑包。v0.111.0 没有 Scry / 球体 / 保留提示） |

排除（按设计不算保真缺陷）：联机与多人缩放、每日 / 修饰符模式、成就 /
时间线 / 解锁 UI、存档迁移写路径（导入所需的读取除外）、VFX / SFX / 动画。

## 验收门

| 门 | 标准 |
| --- | --- |
| 差分一致 | 分级闸 G0（首战，现有）→ G1（每场带锚点的战斗）→ G2（每个带锚点的房间）→ G3（种子整局重放）；对真实录像（recorder 语料 + MCP 自走局）逐决策一致率 ≥99.9%，v0.111.0 锚点段 100%；分歧按差分闸设计 §6.4 的 11 类分诊，不许未分类 |
| 覆盖 | 每个内容批带覆盖计数器：被重放决策触达的卡 / 能力 / 遗物 / 怪 / 事件；未触达项用定向单测或 MCP 定向自走补齐，不许静默留白 |
| 自一致 | `Hash(x) == Hash(x.Clone())`；克隆互不影响；legal-action 闭包（任一合法动作都能 Step 且不抛）；随机策略 10⁵ 整局无异常 |
| 吞吐 | 整局随机策略 ≥300 决策 / 秒 / 核（评审假设区间 300–900）；`Clone()` ≤1 ms |
| 构建 | 0 警告 0 错误；C# 与 Python 测试全绿 |

## 波次（Fable 编排，Opus 执行）

```
W0 理解与设计 ──► W1 内核扩展 ──► W1.5 差分闸 G1 ──► W2 run 层 ──► W3 内容横扩 ──► W4 差分闸 G2/G3 与回归
                   (选择提示 API、     (RunSim、地图、    (powers → cards → monsters/   (exporter v2、全局重放、
                    全部 Hook、Cmd      房间、奖励、        encounters → relics →          覆盖计数、修复循环)
                    门面、遗物/药水     商店、营地、        potions → events；
                    运行时、球/宠物/    事件框架、Neow、    file-per-model 流水线)
                    星/附魔)            幕、进阶)
每一波：合同冻结 → 所有权分区并行实现 → 对抗校验 → 集成 → 修复 → 基准 → 提交
```

| 波 | 交付 | 晋级门 |
| --- | --- | --- |
| W0 | `sim/specs/inventory/*.json`（全量机器可读清单，含 tier / 内核需求 / 提示类型 / 版本差异）、`docs/sim-port/design-*.md`（内核缺口、run 层、差分闸、子系统）、批评报告 | 每个内容文件恰被登记一次；设计覆盖全部 Cmd / Hook / RoomType / RewardType / GameAction |
| W1 | 选择提示决策 API（答案带重放，`sim/CONTRACTS.md` Part B）；182 个 Hook 虚方法 + 148 个分发器；全部 Cmd 门面（199 重载，45 个 VFX 剔除）；遗物 / 药水为钩子监听者，`PlayerState` 为 run / 战斗共享；Orb / Osty / Stars / Forge / 附魔 / 苦痛 / 战斗历史；版本缝（播种方案 + 注册表过滤，无规则分支） | 0 警告全绿；现有 4 个差分 bundle 仍 69/69；≥300 决策/秒/核且报告相对 P0 的回归比；F1/F4/F5 自一致 |
| W1.5 | 差分闸 G1：存档导入（战斗子集）+ 锚点开战、bundle v2（每局一容器、按房间分段、`choose` / 药水决策）、exporter v2、`events.jsonl` 逐条历史比对（G1h） | 现有 bundle 仍全绿；每场带锚点的 v0.111.0 战斗可重放；在第一批 W3 内容前可用 |
| W2 | `RunSim`（地图 → 房间 → 奖励 → …）、存档导入、全部屏幕的 legal actions | 从种子整局随机自走无异常；地图与 Neow 选项对语料逐点一致 |
| W3 | 全部内容，按依赖序分批；每批：移植 + spec 引用 + 单测 + verify 代理抽查 + 差分回归 | 每批全绿并保持累计差分库全绿 |
| W4 | `Sts2Sim.Diff` run 层比对（G2 锚点局层、G3 种子整局）、覆盖计数与 ratchet 基线、分歧分诊循环 | 验收门全部达标 |

### 并行轨道（与波次并行，各有所有者）

| 轨道 | 内容 | 时段与约束 |
| --- | --- | --- |
| REC | recorder 被动补充（差分闸设计 §4.3）：R0 v0.111.0 钩子漂移复核；R1 `SaveManager.Saved` 之后拷贝 `current_run.save` 到 `native/saves/<seq>.save` + `run_saved` 事件；R2 拷贝 `replays/latest.mcr`；R3 主牌组；R4 奖励 offer；R5 事件选项 key；R7 遗物 props；R11 解锁档案字段；R6 / R8 / R9 / R10 随后 | W1 期间并行实现并通过 mod 测试；**只构建不安装**——人类轨迹是基准核心资产，新 recorder 的安装由用户确认后按 `scripts/install_mod.sh` 执行 |
| INV | 清单归一化（批评 B6 / G1 / G5–G7 / G10 / §5.4）：修跨类同名 `model_id`、补 71 个未分类文件、统一 `tier` + `tier_with_deps` / 卡牌批次编号 / 需求目录（键指向 `kernel-gaps.json`）/ 提示记录 schema（键为 `ChoicePromptKind`）、版本差异对账清单、分类生成器从 `tmp/` 入库 | W1 期间并行；产出是 W3 批次 DAG 的输入 |
| FARM | MCP 覆盖农场：无头实例、隔离 `STS2_RECORDER_OUTPUT_ROOT`、覆盖导向策略、检查点分支，产 v0.111.0 锚点语料 | W3 时段；全程 `scripts/mcp_mod.sh on` / `off` 包裹，农场轨迹不与人类语料混放 |
| IL | 反编译丢失的 9 处静态数组已由 `output/server-bootstrap/linux-assembly/sts2.dll` 的 IL 读出（批评 §7），接受为真值，引用写作 `IL:<Type>.<member>` | 已完成 |

## 编排规则（承 CLAUDE.md）

- 主会话（Fable）只做：合同、简报、综合、终审；所有 Workflow `agent()` 与
  Agent 调用一律 `model: 'opus'`。
- 规则真值只有反编译树（v0.111.0；v0.107.1 树仅作版本差异对照）。禁用
  StS1 记忆数值。每条常量 / 规则在 `sim/specs/<area>.md` 带 `文件:行`。
- 所有权分区：共享文件（`Hook.cs`、`AbstractModel.cs`、`CombatState.cs`、
  `CombatLoop.cs`）每波只有一个所有者；内容文件 file-per-model，各自新增。
- 每个并行实现阶段之后必有对抗校验阶段（独立代理重读反编译 vs 移植码），
  再进入修复阶段；修复后差分回归必须全绿才提交。
- 决策点语义：每个玩家选择提示都是搜索节点；W0 冲突的裁决（D1–D12：统一选择运行时与答案带重放、
  per-listener 提示内联 + 保真标记、run / 战斗共享 `PlayerState`、`RunState` 外壳、`UnlockProfile`、
  工厂 partial 拆分、仅 v0.111.0 规则、IL 真值、182 / 148 / A0–A10）见 `sim/CONTRACTS.md` Part B §B0。

## 状态日志

- 2026-11-01：P1 开工。基线复核：构建 0 警告 0 错误，367/367 测试通过；
  反编译树 v0.111.0 共 3,537 文件 / 85.7 万行。W0 workflow（13 个 Opus
  代理：1 机械抽取 + 4 设计研究 → 7 语义分类 → 1 完整性批评）已派发；
  结果与各波进展随后补记。
- 2026-11-01（续）：W0 完成——13 个 Opus 代理、9.1M token、2.6 小时。机械覆盖达标
  （13 类 1,622 个 .cs 各登记恰一次；1,621 个 id 经真 .NET Slugify 复核 0 误差；186 个
  AbstractModel 虚成员 / 148 个分发器 / 199 个 Cmd 重载全覆盖）。批评报告指出 3 个阻断性
  设计冲突（选择 API、per-listener 提示、多选顺序）+ 4 处状态归属分歧 + 清单缺陷，
  已由编排者裁决为 D1–D12 并写入 `sim/CONTRACTS.md` Part B；计划文本按 C9–C12 修正
  （A0–A10、182/148、无 Scry 提示、版本缝替代 GameVersion 开关、G1 提前到 W1.5）。
  差分基线复核：4/4 bundle、69/69（bundle 已拷贝到会话 scratchpad）。下一步：派发 W1
  workflow（phase 0 脚手架 → 9 路所有权并行 → 集成 → 对抗校验 → 修复 → 门）。
- 2026-11-01（续 2）：W1 第 1–2 轮——43 个 Opus 代理、11.0M token、4.7 小时。落地：phase-0 脚手架
  （182 虚成员 / 148 分发器 / B1–B2–B5 成员，`46d1918` + 对抗校验修复 `94424e5`）、INV 轨道
  （`687077a`）、REC 轨道（`4094c1e`，recorder 只构建未安装）、门测试套件 `W1GateTests.cs`（F1/F4/F5
  通过，B2 链与试点覆盖故意红）、`integration.md` §7–8、BENCH W1 行（脚手架单独代价：吞吐 0.95×、
  Clone 2.65 µs）。**9 个所有权轨道未运行**：Workflow 的隔离 worktree 从 `origin/main` 切出、缺脚手架，
  所有者按简报在基线检查处停下；两轮集成均为空合并。对抗校验因此审的是脚手架 + 合同 + 简报，产出
  ~210 条发现，其中合同级错误（PotionFactory 成员名、FairyInABottle 是药水、Upgrade 不写牌组、
  run 监听顺序重复访问 modifiers、owner 死亡只杀 Osty、UsePotionAction 行号、CardPlay 缺 ResourceInfo
  与 PlayId、牌 id 分配器归属等）裁决为 Part B §B12 A1–A23。下一步：第 3 轮 workflow——phase-0b
  脚手架修正 → 9 路所有者（worktree 先 reset 到基线）→ 依赖序集成 → 双视角对抗校验 → 修复 → 门。
- 2026-11-01（续 3）：W1 第 3 轮——阶段 0b 脚手架修正（`a48adf5`，对抗校验后修复 `263810d`）→ 9 路所有者在
  隔离 worktree 上先 `git reset --hard` 到基线再实现（10:12–11:48，全部提交）→ 依赖序集成（9 个合并提交 +
  接缝修复 `9f4db55`）。集成门全部通过：构建 0 警告，721/721 测试，PENDING 0，差分 4/4 · 69/69，F1/F4/F5、
  B2 链、试点覆盖、基准均 pass。**用户要求降低对抗校验、尽快推进**：本轮的双视角校验、修复、第 4 轮集成与
  独立审计全部跳过；此后每波只保留 简报 → 并行所有者 → 单一集成者跑自动门（构建 / 测试 / 差分 / 基准）→ 提交，
  不再设对抗校验与审计阶段，保真误差留给差分闸发现。W1.5 的存档导入并入 W2 的 R7，bundle v2 / exporter v2
  延后到 W4。下一步：W2（run 层 R1–R8）与 W3 战斗内容批次并行启动。

- 2026-11-01（续 4）：W2 + W3 精简流程一次跑完（run `wf_91ccc186-a2b`，62 个 Opus 代理，14.8M token，2.1 小时，0 失败）。
  阶段：W2 脚手架 `72edde8`（CONTRACTS Part C、RunAction/RunSim 骨架、ContentSoakTests）∥ 32 个战斗内容所有者 →
  集成 A `0a01a90` → W2 七个所有者（R1–R7）∥ 怪物 / 配套能力 / 遭遇 → 集成 B `8eda7fb` → R8 oracle ∥ 事件 / run 侧遗物 /
  角色 / 幕 → 集成 C `a1c0e4b`。终态：构建 0 警告，965/965 测试，PENDING 3，内容 soak 0 失败（卡 1,108、遭遇 252、
  药水 128、遗物 586），差分 4/4 · 69/69，基准 148k 决策/秒、Clone 2.5 µs。W3 共移植 1,402 个模型，27 个按原因跳过。
  未闭合：`EventRoom.EnterInternal`（R4 房间入口桩）挡住 RunSim 新局测试与整局随机 soak；存档导入缺 R2/R3 三个成员；
  powers:8（run 耦合 7 个能力）漏分配，连带 GremlinMerc / ThievingHopper / FORBIDDEN_GRIMOIRE / ROYALTIES；
  `PlayerCmd.EndTurn`、`CombatState.EscapedCreatures`、战斗外 `CardCmd.Transform` 等内核缝使 9 个定义被 `[SimExclude]`。
  下一步：W3.5 补缝（3 个所有者 + 集成）与 W4（exporter v2、harness v2、.mcr 解码、F1–F8 自洽、G3 整局重放与分诊）合为一个工作流。
