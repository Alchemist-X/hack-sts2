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
5. **harness 搬迁**（v0.111.0 实测清单；版本相关的东西只在三处：
   `config/sts2mcp.lock.json`、`knowledge/source/<ver>/`、`config/bench/*.json`）：
   1. `scripts/build_selection_mcp.sh` 会因游戏版本/sts2.dll sha256 与锁文件
      不符而拒绝构建——这就是触发信号。`STS2_MCP_ALLOW_UNPINNED_GAME=1` 再跑
      一次，拿到编译错误清单。
   2. 编译漂移写成新补丁 `patches/sts2mcp-v<ver>-compat.patch`（参照
      `sts2mcp-v0111-compat.patch`：每处都照抄引擎自己的新调用、保持输出
      schema 不变），追加进锁文件 `patches`；更新锁文件 `game` 段（version、
      commit、各程序集目录 sts2.dll sha256）。构建通过后把沙盒 DLL sha256 记入
      `sandbox_build.dll_sha256`（构建可复现，换目录/换 commit 结果不变）。
   3. 规则证据重导到 `knowledge/source/<ver>/`（`export_sts2_knowledge.py`、
      `export_sts2_rule_evidence.py`，反编译树需带引用程序集）。
   4. `headless_provision.sh` 建新池（默认新档，不读人类存档）→ 启动一次让
      游戏写出 progress.save → `make_unlocked_save.py` → `baseline.py freeze`
      冻结 v2 / v2-a10 到 `$STS2_BENCH_BASELINE_ROOT/<ver>/`（命令见
      docs/benchmark.md）。新档 `unique_id` 随机，基线无法逐字节重做，**文件
      必须保留**。
   5. `config/bench/*.json`：版本号 minor bump、`game_version`/`game_commit`、
      新基线名与 sha256。`test_bench_configs_match_the_pinned_game_and_knowledge`
      会检查锁文件、配置、knowledge 三者一致。
   6. 验收：stub `run_bench`（`--start-method eval`）+ `--determinism SEED
      --starts 2`；看 seed_evidence 三源一致、每步 `authority_verified`。
   TODO：把 1–6 串成 `scripts/migrate_game_version.sh`。

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
- （v0.111 harness）评测 API、seeded-embark、action-receipts 三个补丁原先只
  在私有 hack-sts2-cloud 仓库，本仓库脚本只打两个补丁；现已全部纳入
  `patches/` 并由锁文件列出。补丁不在本仓库 = 迁移时找不到。
- （v0.111 harness）未打补丁的 upstream 在新版不编译；`headless_provision.sh`
  已不再构建它。
- （v0.111 harness）macOS 工作进程存档在 `home/Library/Application Support`，
  不是 Linux 的 `home/.local/share`；基线还原、current_run.save、history 查找
  都按实例自动选路径。
- （v0.111 harness）`run_bench` 必须把配置的 `game_version` 传给
  `sts2_live start`，否则悄悄用 v0.107.1 的规则知识。
- （v0.111 harness）`make_unlocked_save.py` 依赖的源码标记会搬家（v0.111
  epoch 列表移进 `EpochModel` 静态构造与 `AgnosticUnlocks`，progress schema
  21→24）：先跑无 `--saves-dir` 的计数核对。
- （v0.111 harness）合成的 settings.save 要写 `language: eng`，否则工作进程
  跟随主机语言（中文系统会得到中文卡面文本）。
- （v0.111 harness）.NET SDK 默认 SourceLink 把外层仓库 HEAD 写进 MCP DLL，
  sha256 随提交变化；构建已关闭（`EnableSourceLink=false` 等），可固定 pin。
- Steam 当前在 public-beta 分支且 AutoUpdateBehavior=1（启动即更新）：
  大版本可能在任何一次启动后到来，锁文件的版本闸会第一时间拦下。

## 状态

- 2026-10-30：手册首版。
- 2026-10-31：第 5 步（评测 runner v0.107.1→v0.111.0）完成：锁文件驱动的
  MCP 构建（沙盒 DLL `83fc6828…`，与冒烟版 IL 完全一致）、`knowledge/source/
  v0.111.0`、基线 v2 `c45ec7a9…` / v2-a10 `27c009af…`、配置 v1.1；A0/A10 stub
  冒烟、双实例并行与确定性检查通过。
