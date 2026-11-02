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
python3 -m unittest discover -s scripts -p 'test_version_delta.py'
```

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
  其中 228 个内容文件只含 bulk 改动；W0 人工清单的 171 个 `changed` 全部命中）。

## 五步流水线

1. **反编译新版本**（带引用程序集）→ `output/.../<ver>` 树 + manifest
   指纹（版本、commit、sts2.dll SHA-256）。旧树保留，永不覆盖。
2. **双树 diff → 任务清单**（最大提速器；`scripts/version_delta.py`）：对"已移植文件集合"做新旧树
   diff——sim/specs/*.md 里每条规则都带反编译 `文件:行` 引用，已移植文件
   清单可机器提取。命中的文件 = 本次移植任务列表；未命中 = 免检。种子
   算法/存档 schema 类变更会在 `Saves.Migrations` 命中（v18→v19 即先例）。
3. **sim 对齐**：file-per-change 的 opus 批量代理只改命中文件；全测试绿。
4. **差分闸回归 = 迁移验收器**：旧版本语料按旧 seeding 继续重放（历史
   资产永不作废）；新版本攒 1–2 局录像（正常游玩即可）→ 新 seeding 路径
   重放全绿 = 迁移通过。
5. **harness 搬迁**：MCP 重建（`scripts/build_selection_mcp.sh`，源码仓库
   与游戏目录**显式传参**）→ 规则证据重导（`export_sts2_rule_evidence.py`
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
- `build_selection_mcp.sh` 的默认 STS2MCP 源码路径按脚本所在仓库根的兄弟
  目录解析——**在 worktree 里必须显式传 `STS2_MCP_SOURCE_REPO`**。
- 静态字段按进阶一次性初始化（CardRarityOdds 等）的进程内复用陷阱，迁移
  后重验。
- recorder 的 `context_model` 标签粘滞（PushModel 不出栈）：升级 recorder
  时考虑补 PopModel 钩子。

## 状态

- 2026-11-01：`scripts/version_delta.py` + `scripts/version_update.sh` 落地（19 个单元测试）；
  v0.107.1 → v0.111.0 演示见 `docs/version-delta-demo-2026-11-01.md`。
- 2026-10-30：手册首版。第 5 步（评测 runner v0.107.1→v0.111 搬迁）首跑
  进行中；结果与教训回填于此。
