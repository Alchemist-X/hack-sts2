# P0 — Route C 战斗核心源码移植：实施方案（walking skeleton 轮）

2026-10-28 · 分支 `claude/sts2-a10-model-review-26f93e` · 依据
[a10-first-principles-review-2026-10-27.md](a10-first-principles-review-2026-10-27.md) §4/§6 ·
游戏作者已书面许可（学术研究项目）。

## P0 目标与验收门（2–3 周）

| 门 | 标准 |
| --- | --- |
| 差分一致 | 对托管 oracle 自走局 + 人类 replay 的逐决策状态一致率 ≥99.9%（本轮不验收，先立骨架与单测） |
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
- P0.5 待办：带引用程序集重新反编译；v0.107.1 树对齐人类语料。

## 卡牌横扩流水线（本轮之后）

file-per-card：每卡一个 opus 代理（读模型文件 → 移植 → spec 引用 → 单测），
批量并行；verify 代理抽查；差分 oracle 回放把关。随后：战斗子集 30 卡 →
差分接入 → P1 搜索教师。

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
