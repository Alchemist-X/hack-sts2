# P0 — Route C 战斗核心源码移植：实施方案（walking skeleton 轮）

2026-10-28 · 分支 `claude/sts2-a10-model-review-26f93e` · 依据
[a10-first-principles-review-2026-10-27.md](a10-first-principles-review-2026-10-27.md) §4/§6 ·
游戏作者已书面许可（学术研究项目）。

## P0 目标与验收门（2–3 周）

| 门 | 标准 |
| --- | --- |
| 差分一致 | 对真实引擎 oracle 轨迹（既有版本匹配录像 + MCP 范围约束自走局）的逐决策状态一致率 ≥99.9%，且分歧全部归类（设计见「差分闸设计」节；人类轨迹非必需） |
| 吞吐 | ≥1,000 决策/秒/核（随机策略整场战斗，单线程） |
| 克隆 | `CombatSim.Clone()` ≤1ms |
| 覆盖 | Ironclad 战斗子集 ~30 卡 + 第一幕敌人若干 |

## 本轮范围（走通骨架）

RNG 全量移植 + 战斗状态模型 + 伤害/格挡/能力管线（子集所需）+ Ironclad
起始卡组 + 一个第一幕简单遭遇 + 回合循环与决策 API + Clone/StateHash +
轻量测试 runner + 微基准。

**不含**：遗物、药水、事件、地图/run 层、卡牌升级的应用逻辑（数值随模型带上）、
联机、存档迁移。缺项按计划排除，不算保真缺陷。

## 规则真值（反编译树，只读）

根：`/Users/Aincrad/dev-proj/hack-sts2/output/server-bootstrap/linux-decompiled/`
（按命名空间分目录；对应游戏 v0.111.0）。

| 区域 | 路径（相对根） |
| --- | --- |
| RNG 核心 | `MegaCrit.Sts2.Core.Random/`（`Rng.cs` 为带 `_counter` 的包装，内核 `MegaRandom`） |
| 命名流注册 | `MegaCrit.Sts2.Core.Entities.Rngs/` |
| 战斗主控 | `MegaCrit.Sts2.Core.Combat/CombatManager.cs`（~7.6k 行） |
| 动作执行 | `MegaCrit.Sts2.Core.GameActions/`（`ActionExecutor.cs` 等） |
| 数值内核 | `MegaCrit.Sts2.Core.Hooks/Hook.cs`（~11.8k 行） |
| 静态数据 | `Models.Cards / Models.Monsters / Models.Powers / Models.Characters / Models.Encounters / Models.Acts / Models.CardPools` |
| 运行时实体 | `Entities.Cards / Entities.Creatures / Entities.Players / Entities.Powers / Entities.Intents / Entities.Ascension / Entities.Encounters` |
| 怪物 AI | `MegaCrit.Sts2.Core.MonsterMoves/`（含 `Intents`、`MonsterMoveStateMachine`） |

## 架构

- `sim/Sts2Sim.sln`：`Sts2Sim.Core`（classlib，net9.0，纯 BCL——零 NuGet、零
  Godot 引用）、`Sts2Sim.Tests`（console，自研断言 runner，零测试框架依赖，
  反射发现 `Test_*` 静态方法）、`Sts2Sim.Bench`（console，Stopwatch）。
- 镜像游戏自身的 Models（静态定义）/ Entities（运行时状态）分层；能力钩子
  沿用 `Hook.cs` 的钩子名目，便于逐条对照。
- 决策 API：`CombatSim.Create(seed, deckIds, encounterId, ascension=0)` /
  `LegalActions()` / `Step(action)` / `Clone()` / `StateHash()` / `Outcome`。
- 同步化改写：协程 / await / VFX 等待 → 顺序直接执行（对应官方
  TestMode/NonInteractiveMode 跳等待语义）；所有剥离点记录在 spec。
- 保真方法：每个常量/规则在 `sim/specs/<area>.md` 标注反编译 `文件:行`；
  对抗校验（独立代理重读反编译 vs 移植码互证）；后续接差分（oracle 自走 +
  人类 replay 向量）。
- 溢出语义：C# 默认 unchecked——播种/哈希里的加法溢出必须按原样保留。
- 克隆契约：所有带状态类型实现 `DeepClone()` 与 `WriteCanonical(...)`（规范
  字节序列 → 64 位哈希），RNG 流状态（含 counter）入哈希。

## 已知陷阱（评审 §4，移植时逐条核对）

- `CardRarityOdds` 等静态字段按进阶一次性初始化——进程内多局需重置缝。
- TheArchitect 的 VFX 列表长度参与规则 RNG 消耗（移植该敌人时专门处理）。
- A8 HP / A9 伤害散落在各怪物模型文件内的条件分支——逐怪照搬，A0 为默认路径。
- 反编译质量：多数 async 状态机未还原、存在 missing-reference 行——读不通先查
  邻近重载/基类，仍歧义则记 open question，禁止臆测补全。
- P0.5（按需，2026-10-30 移出 P0 必做）：带引用程序集重新反编译——移植撞上
  读不通的文件再做；v0.107.1 树对齐——等 P1 需要 BC／人类基线、或需要动用
  92% 旧版语料做差分时再做。

## 差分闸设计（2026-10-30 定稿；骨架完成后的第一优先级）

人类轨迹不是 P0 必需（BC／人类基线属 P1）；差分闸只服务「环境+评测」。
oracle 不托管 DLL：**真实游戏 + Sts2Recorder 的录像本身就是 oracle trace**——
manifest 有种子、`actions.jsonl` 有 Harmony 钩子捕获的精确动作流（实例解析
走 PR #27 机器）、`states.jsonl` 有每个稳定决策点的全状态快照（StateBuilder
的 screen→JSON）、特权通道有真实抽牌序（`CardDrawnEntry`）；
`native/replay.mcr` 留作后手。托管 DLL（TestMode 无头加载）降级为 P1 吞吐
升级项。

1. **轨迹来源（优先级序）**：
   a) 既有录像中游戏版本与反编译树（v0.111.x）一致、且战斗引用内容全部
      已移植的切片——零成本立刻可用；
   b) MCP 驱动的范围约束自走局（随机 + 启发式）：全程跳卡牌奖励、不买不
      喝药、优先普通怪房 ⇒ 整局保持起始卡组，每场战斗都在已移植范围内；
      装卸遵守 `scripts/mcp_mod.sh on/off`；
   c) v0.107.1 大头语料——按需（P0.5），非 P0。
2. **状态对齐**（sim `Create(RunRngSet,…)` 需要战斗开始时各流 counter）：
   候选甲：命名流隔离 ⇒ 首战斗前运行层只消耗自己的流，战斗相关流 counter
   为 0／确定性偏移（待反编译核实）；候选乙：recorder 被动扩展——战斗
   开始时把自动存档拷进会话 `native/`（只拷贝，不碰原档）；候选丙（兼容
   旧录像）：用真实抽牌序／意图序做 counter 对齐搜索——洗牌序即指纹，
   后续消耗持续自验证。
3. **重放比对**：`sts2rec` 导出 replay bundle（开局状态 + 动作列表 + 逐决策
   期望投影）→ sim 侧 Diff harness 逐动作执行、逐决策点规范投影比对
   （HP/块/能量/牌堆内容与顺序/能力栈/意图/RNG counter），首分歧即停，
   输出种子+层数+动作下标+字段 diff+双侧全状态的最小复现（可固化为单测）。
4. **门禁（常驻 ratchet）**：每个分歧三分类——移植 bug（修）／recorder
   缺口（补）／P0 范围外（带排除理由跳过）；门 = 逐决策一致率 ≥99.9% 且
   分歧全部归类（当前内容量下按 100% 或可解释执行）。sim 加 Hook 路径
   覆盖计数，未被轨迹触达的路径用定向策略或单测补，不许静默留白。此后
   每批横扩必须在累计轨迹库上保持全绿。

开工调查项（先证伪）：① Ironclad 起始遗物在 STS2 的触发时机（战斗内 or
战斗后）与 RNG 消耗；② 首战斗首个决策点时各流 counter（候选甲成立与否）；
③ 既有语料盘点——版本分布、v0.111.x Ironclad 局数、floor-1 遭遇分布、
当前内容下可用决策数、快照字段是否足以做投影。

## 卡牌横扩流水线（差分闸就绪后批量跑）

file-per-card：每卡一个 opus 代理（读模型文件 → 移植 → spec 引用 → 单测），
批量并行；verify 代理抽查；每批过差分回归闸。选卡标准（2026-10-30 修订）：
不按人类语料频次，按**机制覆盖**选首批——攻击/技能/能力、消耗、抽牌、
AoE、叠甲、力量缩放各类先进流水线，让各类 Hook 的移植方法尽早暴露在差分
闸下；随后按卡池批量。敌人同法扩第一幕遭遇（A8/A9 条件逐怪核对）。随后：
战斗子集 30 卡 → 差分验收 → P1 搜索教师。

## 状态日志

- 2026-10-28：方案定稿，走通骨架开工（脚手架 → 六路并行移植 → 集成 →
  对抗校验 → 基准）。结果随进展补记。
- 2026-10-30：**走通骨架完成**。构建 0 警告 0 错误，205/205 测试通过，含
  两类强验证：RNG 对真实存档（种子 Q3X6PF8S37GA，只读）15 条命名流两个
  时刻逐位一致；E2E 与一个不共享 Core 代码的独立参考模型逐决策 lockstep
  对比。四路对抗校验 + 三个 major 修复（A5 诅咒入起始卡组、Create 支持
  HP/进阶/RunRngSet 中途开局、setup 窗口 IsStarting 语义）+ 复查：无
  critical/major 残留，~40 项 minor 均为 P0 不可达或记入 spec 的 deferral
  （清单见 `sim/specs/*.md`）。基准（M4 Pro 单线程 Release）：**705k
  决策/秒**（门槛 1k）、**Clone 2.53µs**（门槛 1ms）、StateHash 0.87µs，
  随机策略 5 秒窗口完赛 17.6 万局。覆盖：Ironclad 起始卡组（+ASCENDERS_BANE
  A5 变体）、NIBBITS_WEAK、Vulnerable/Weak/Strength/Dexterity/Frail、
  同步化回合循环与决策 API、克隆/规范哈希。下一步：卡牌横扩流水线
  （file-per-card）+ 托管 oracle 差分接入 + v0.107.1 对齐语料。
- 2026-10-30（续）：P0 范围修订 + 差分闸设计定稿（见上两节）：人类轨迹
  相关工作移出 P0（v0.107.1 对齐降为按需 P0.5）；oracle 改为「真实游戏 +
  recorder 录像」（既有版本匹配切片优先），托管 DLL 降为 P1 吞吐升级；
  横扩选卡改机制覆盖。三项开工调查（起始遗物时机／首战前流消耗／语料
  盘点）已派发 opus 代理。
- 2026-10-30（晚）：三路调查返回（3 Opus，~118 万 token，386 次工具调用）。
  ① **Neow 事件强制且无跳过**——floor 1 恒为 Neow，首战在 floor 2，每个
  选项至少 +1 遗物：真实首战永不"纯起始卡组"。Burning Blood 战斗内零
  效果（仅胜利后治疗 6，无 RNG）；Neow 遗物多数战斗内惰性（BOOMING_CONCH
  除外），白名单入 `sim/specs/diff-bundle.md`。
  ② **首战 RNG 对齐配方实证成立**：sim 相关流开战 counter 全 0，Niche
  偏移 = 6·[Kaleidoscope]+[NewLeaf]+[NeowsBones]（110 局实证：洗牌 97/99、
  LeafSlime 意图 16/16、Niche 88/89，差异均可解释）；**v0.107.1 旧语料用
  LegacyV18 旧种子核逐位可复现**（sim 已有实现，79/80+85 手牌精确）；
  后续战斗需真实 counter——`native/replay.mcr` 已含 SerializableRunRngSet
  （PacketReader 解码列 P0.5 后手），或 recorder 被动拷 `current_run.save`。
  设置顺序 sim↔游戏核对一致，4 处记录在案的偏差均不影响第一幕 weak。
  ③ **语料盘点**（133 lineage/37 胜/8.95 万动作；v0.107.1 占 87%）：严格
  口径可用战斗 = **0**——卡在遭遇覆盖（当前仅 NIBBITS_WEAK，且只在
  Overgrowth 变体出现）与升级卡表达（Neow 升级）。近失案例三个（v0.107.1
  NIBBITS/SEAPUNK/SLIMES 首战，deck=起始+bane±升级）。
  → 结论与行动：差分点亮需最小内容批。已启动实现 workflow：sts2rec
  exporter + Sts2Sim.Diff 比对器（契约 `sim/specs/diff-bundle.md`）+
  内容批（第一幕全部 8 个 weak 遭遇、升级版 Strike/Defend/Bash、SLIMED/
  GREED、遭遇私有 Rng 与槽位排序基建）+ 集成 + 对抗校验 + 修复。
