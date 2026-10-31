# 游戏版本迁移手册（playbook）

目的：游戏大版本更新后，把整套栈（反编译真值 → sim → 差分闸 → 评测
harness）在 **≤1–2 天**内迁到新版本。由 v0.107.1→v0.111.0 搬迁沉淀
（2026-10-30）；每次迁移后回填教训。

## 触发与检测

- recorder 每局 manifest 记录游戏版本——新版本首次出现即触发本手册。
- CLAUDE.md 既有规则：游戏更新后重建 recorder 并对 fresh decompile 复核
  钩子（docs/hook-map.md 漂移流程）。

## 五步流水线

1. **反编译新版本**（带引用程序集）→ `output/.../<ver>` 树 + manifest
   指纹（版本、commit、sts2.dll SHA-256）。旧树保留，永不覆盖。
2. **双树 diff → 任务清单**（最大提速器）：对"已移植文件集合"做新旧树
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
   TODO（本次搬迁落地后）：把这三步串成 `scripts/migrate_game_version.sh`，
   下次一条命令。

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

- 2026-10-30：手册首版。第 5 步（评测 runner v0.107.1→v0.111 搬迁）首跑
  进行中；结果与教训回填于此。
