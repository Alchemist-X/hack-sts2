# 第三幕存档池（wave 20 item C，2026-11-06）

**结论先行**：从现有两个人类存档池（`silent-a9a10-late`、`silent-a10-late`，后者是前者的子集）筛出两个第三幕池，
都用 trainer 自己的 `startpool.probe_saves` 在 raven 上试导入过，**全部可用**，没有 holdout 碰撞，也没有停在 pending prompt 的存档：

| 池 | 筛选 | 文件 | probe 可用 | 不同状态 | A9 / A10 | 首观测阶段 Combat / Rewards / Event / RestSite / Shop / Treasure | floor |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `silent-a9a10-act3` | act 3 | 60 | 60 | 51 | 30 / 30 | 26 / 16 / 9 / 5 / 2 / 2 | 34–49 |
| `silent-a9a10-act3-late` | act 3，距第一个 boss ≤ 6 层（含 boss 层） | 30 | 30 | 25 | 15 / 15 | 14 / 8 / 3 / 4 / 1 / 0 | 42–49 |

多样性仍然很低：两池都只来自 2 局人类游戏（A43U5UZ5W8BS A9、WWVGBDEYNG19 A10），每局 30 / 15 份。
boss 战开局实际只有 3 个状态：A9 TEST_SUBJECT（floor 48）、A10 QUEEN（floor 48，5 份重存，其中 3 份 13 件遗物、2 份 16 件）、
A10 第二 boss AEONGLASS（floor 49）；另有 3 份 boss 战后奖励屏（不是战斗）。late 池按文件均匀抽样时 QUEEN 入口占 5 / 30。

## 池在哪里

- 本机（scratchpad，数据，不提交）：
  `/private/tmp/claude-501/-Users-Aincrad-dev-proj-hack-sts2--claude-worktrees-recorder-performance-lag-0f9d6d/adbf2344-809a-4167-9774-cd8087651b17/scratchpad/saves/silent-a9a10-act3/`
  和同目录下的 `silent-a9a10-act3-late/`。每个目录是 `collect_start_saves.py` 的标准输出：
  `SILENT_A<asc>_f<floor>_<seed>_<sha8>.save` 字节副本 + `manifest.json`（dict，`saves` 列表带 `sha256`，所以 trainer 的
  `_manifest_rows` 能读到来源；旧的 `*-late` 池是裸列表 manifest，会被忽略）。
- raven：`/data/raven/hack-sts2/testbox/w20c/tmp/w20c/silent-a9a10-act3{,-late}/`（容器内 `/work/tmp/w20c/...`）。
- 源池没有改动：构建时先在 `scratchpad/saves-staging/act3-src/<session>/native/saves/<file>` 建了指向两个源池文件的符号链接
  （按源池 manifest 的 `session` / `file` 还原录制器目录结构；a10-late 的 58 份与 a9a10-late 重合，按会话路径合并成 122 个链接），
  `collect_start_saves.py` 只读链接目标、复制。`silent-a10-all` 里的第三幕存档全部已在这两个池里（检查过，0 份额外）。

## 构建命令

```bash
STAGE=$SCRATCH/saves-staging/act3-src
python3 -I scripts/train/collect_start_saves.py --sessions $STAGE --out $SCRATCH/saves/silent-a9a10-act3      --character SILENT --act 3
python3 -I scripts/train/collect_start_saves.py --sessions $STAGE --out $SCRATCH/saves/silent-a9a10-act3-late --character SILENT --act 3 --max-floors-to-boss 6
```

输出：122 个源文件，0 重复，0 跳过；act3 池 filtered out `{act: 62}`，late 池 `{act: 62, max_floors_to_boss: 30}`。

## 筛选定义

`collect_start_saves.py` 原来只有 `--character / --ascension / --min-floor`，这次加了两个过滤器（输出格式不变，manifest
行多了几个字段）：

- `--act N`：存档状态所在的幕（`current_act_index + 1`）。
- `--max-floors-to-boss N`：`floors_to_boss ≤ N`。
  - `act_floor` = 存档当前幕的 map-point 数（幕首的 Ancient 起点算第 1 层），"进入地图点"的存档再 + 1（sim 会进入那个房间）。
    它等于当前地图格的 row + 1；60 份全部与 `visited_map_coords` 最后一格核对一致。
  - 第一个 boss 在 `acts[i].saved_map.boss.coord.row + 1` 层（两局第三幕地图 height 14，boss row 14 → act floor 15，即总 floor 48）。
  - `floors_to_boss` = boss act floor − act_floor：第一个 boss 的入口和它的战后奖励屏是 0，A10 第二个 boss（floor 49）是 −1。
  - 所以 `--max-floors-to-boss 6` = boss 前的最后 6 层（act floor 9–14，总 floor 42–47，20 份）**加上** boss 层本身
    （floor 48 / 49，10 份：6 份第一 boss 入口、1 份第二 boss 入口、3 份 boss 战后奖励屏）。只要严格的"boss 之前"可按 manifest 的
    `floors_to_boss ≥ 1` 再筛（20 份）。
- manifest 每行新增字段：`act_floor`、`floors_to_boss`、`boss_id`、`second_boss_id`、`upgraded_n`（`current_upgrade_level > 0` 的牌数）；
  直方图新增 `floors_to_boss`；`filters` 记录 `act` / `max_floors_to_boss`。

距 boss 分布（act3 池）：−1:2，0:8，1:2，2:3，3:5，4:3，5:3，6:4，7:2，8:2，9:3，10:7，11:4，12:3，13:5，14:4。

## probe（可用性）

- 方法：与 boss 学校计划（`docs/boss-school-plan-2026-11-06.md`，wave 17 `scratchpad/w17/scripts/probe.py`）相同：
  `sts2train.startpool.probe_saves(pool, characters=("SILENT",))`（1 槽 env，`ported_only` 内容，导入 + 首观测 + 合法动作检查），
  加 `IsolationGuard.from_seedsets()` 的 holdout 种子检查。另外用改过的 `collect_start_saves.py --check`（sts2vec 导入）在容器里对两池再跑一次。
- 命令（raven，HEAD 063a9d1 现编译的库，镜像 `sts2-testbox:c025f32e8cbd`）：
  `TESTBOX_NAME=w20c scripts/test_remote.sh --suites none -- bash -c '... uv run python $T/probe_act3.py $T $T/probe.json silent-a9a10-act3 silent-a9a10-act3-late; ... collect_start_saves.py --sessions $T/stage-$p --out $T/check-$p --character SILENT --act 3 --check'`
  （脚本 `scratchpad/w20c/up/probe_act3.py`、`stage.py`，日志 `scratchpad/w20c/probe.log`，结果 `scratchpad/w20c/probe.json`），exit 0。
- 结果：act3 60 / 60 可用，late 30 / 30 可用，0 跳过，0 holdout 命中，0 pending prompt，两池重合 30；`--check` 也是 60 / 60、30 / 30。
  （wave 17 时 a9a10-late 唯一被跳过的 `extra_rewards` 存档在第二幕，不在这里。）
- sim 首观测与存档字段：floor / act / deck / relic 全部一致；HP 有 3 份不同（sim 是进房后的值）：两份进入第三幕 Ancient 的存档
  （49 → 71、3 → 62），以及 A9 的 TEST_SUBJECT boss 入口（37 → 62）。A10 的 boss 入口 HP 不变（53）。表里写成 `存档值（sim 值）`。
- `start_progress`：floor 48 boss 入口 A9 0.9778 / A10 0.9583，floor 49 第二 boss 0.9792（A10 第三幕 16 个房间，A9 15 个）。

## 逐份表

列：save id = 源池文件名（`<start_time>-<seed>-partNN-<seq>`，即录制器会话 + 存档序号）；局 = 种子 + ascension；floor = 总楼层；
act floor / 距 boss 见上；存档点：进入 = `entering_map_point`，战后奖励 = `post_combat_rewards`，Ancient 后 = `ancient_finished`；
房间是存档里的地图点类型（`unknown` = 问号格）；sim 首观测阶段取自 probe；升级 = 已升级的牌数；sha256[:8] 也是池内文件名的后缀。

#### `silent-a9a10-act3`（60 份）

| save id（原池文件名） | 局（种子 / A） | act | floor | act floor | 距 boss | 存档点 / 房间 | sim 首观测阶段 | hp / max_hp | 牌组 | 升级 | 遗物 | sha256[:8] |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1793671962-A43U5UZ5W8BS-part18-001836 | A43U5UZ5W8BS A9 | 3 | 34 | 1 | 14 | 进入 ancient | Event | 49 / 77（sim 71） | 29 | 27 | 13 | 39c48ee8 |
| 1793671962-A43U5UZ5W8BS-part18-001841 | A43U5UZ5W8BS A9 | 3 | 34 | 1 | 14 | Ancient 后 event | Event | 71 / 77 | 30 | 27 | 14 | cb02c085 |
| 1793671962-A43U5UZ5W8BS-part18-001850 | A43U5UZ5W8BS A9 | 3 | 35 | 2 | 13 | 进入 monster | Combat | 71 / 77 | 30 | 27 | 14 | 25daab00 |
| 1793671962-A43U5UZ5W8BS-part18-002139 | A43U5UZ5W8BS A9 | 3 | 35 | 2 | 13 | 战后奖励 monster | Rewards | 71 / 77 | 30 | 27 | 14 | c0136860 |
| 1793671962-A43U5UZ5W8BS-part18-002152 | A43U5UZ5W8BS A9 | 3 | 36 | 3 | 12 | 进入 monster | Combat | 71 / 77 | 30 | 27 | 14 | 7ddf66e7 |
| 1793671962-A43U5UZ5W8BS-part18-002307 | A43U5UZ5W8BS A9 | 3 | 36 | 3 | 12 | 战后奖励 monster | Rewards | 71 / 77 | 30 | 27 | 14 | 8975b85f |
| 1793671962-A43U5UZ5W8BS-part18-002320 | A43U5UZ5W8BS A9 | 3 | 37 | 4 | 11 | 进入 monster | Combat | 71 / 77 | 30 | 27 | 14 | 4d6f3a65 |
| 1793671962-A43U5UZ5W8BS-part18-002992 | A43U5UZ5W8BS A9 | 3 | 37 | 4 | 11 | 战后奖励 monster | Rewards | 46 / 77 | 30 | 27 | 14 | 28c07422 |
| 1793671962-A43U5UZ5W8BS-part18-003009 | A43U5UZ5W8BS A9 | 3 | 38 | 5 | 10 | 进入 monster | Combat | 46 / 77 | 31 | 27 | 14 | f2c10cd2 |
| 1793671962-A43U5UZ5W8BS-part19-000003 | A43U5UZ5W8BS A9 | 3 | 38 | 5 | 10 | 进入 monster | Combat | 46 / 77 | 31 | 27 | 14 | edebe44e |
| 1793671962-A43U5UZ5W8BS-part19-000462 | A43U5UZ5W8BS A9 | 3 | 38 | 5 | 10 | 战后奖励 monster | Rewards | 33 / 77 | 31 | 27 | 14 | 2d1c8792 |
| 1793671962-A43U5UZ5W8BS-part19-000473 | A43U5UZ5W8BS A9 | 3 | 39 | 6 | 9 | 进入 monster | Combat | 33 / 77 | 31 | 27 | 14 | 0996a5b6 |
| 1793671962-A43U5UZ5W8BS-part19-000657 | A43U5UZ5W8BS A9 | 3 | 39 | 6 | 9 | 战后奖励 monster | Rewards | 33 / 77 | 31 | 27 | 14 | aaa16861 |
| 1793671962-A43U5UZ5W8BS-part19-000670 | A43U5UZ5W8BS A9 | 3 | 40 | 7 | 8 | 进入 shop | Shop | 33 / 77 | 32 | 27 | 14 | 78b7f5d4 |
| 1793671962-A43U5UZ5W8BS-part19-000680 | A43U5UZ5W8BS A9 | 3 | 41 | 8 | 7 | 进入 treasure | Treasure | 33 / 77 | 33 | 27 | 14 | 7a7e2e9f |
| 1793671962-A43U5UZ5W8BS-part19-000692 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 进入 elite | Combat | 33 / 77 | 33 | 27 | 15 | a6dfb1a2 |
| 1793671962-A43U5UZ5W8BS-part20-000003 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 进入 elite | Combat | 33 / 77 | 33 | 27 | 16 | baeb4ec5 |
| 1793671962-A43U5UZ5W8BS-part20-000667 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 战后奖励 elite | Rewards | 33 / 77 | 33 | 27 | 16 | a3a8365c |
| 1793671962-A43U5UZ5W8BS-part20-000682 | A43U5UZ5W8BS A9 | 3 | 43 | 10 | 5 | 进入 monster | Combat | 33 / 77 | 33 | 27 | 17 | 82787559 |
| 1793671962-A43U5UZ5W8BS-part20-001105 | A43U5UZ5W8BS A9 | 3 | 43 | 10 | 5 | 战后奖励 monster | Rewards | 21 / 77 | 33 | 27 | 17 | fd46069a |
| 1793671962-A43U5UZ5W8BS-part20-001116 | A43U5UZ5W8BS A9 | 3 | 44 | 11 | 4 | 进入 unknown | Event | 21 / 77 | 33 | 27 | 17 | ccb17796 |
| 1793671962-A43U5UZ5W8BS-part21-000003 | A43U5UZ5W8BS A9 | 3 | 44 | 11 | 4 | 进入 unknown | Event | 21 / 77 | 33 | 27 | 17 | a1622596 |
| 1793671962-A43U5UZ5W8BS-part21-000017 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 进入 elite | Combat | 21 / 77 | 33 | 27 | 18 | 85f764d8 |
| 1793671962-A43U5UZ5W8BS-part22-000003 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 进入 elite | Combat | 21 / 77 | 33 | 27 | 18 | bff831d3 |
| 1793671962-A43U5UZ5W8BS-part22-000398 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 战后奖励 elite | Rewards | 14 / 77 | 33 | 27 | 18 | 313cf869 |
| 1793671962-A43U5UZ5W8BS-part22-000411 | A43U5UZ5W8BS A9 | 3 | 46 | 13 | 2 | 进入 monster | Combat | 14 / 77 | 33 | 27 | 19 | a09eef34 |
| 1793671962-A43U5UZ5W8BS-part22-000668 | A43U5UZ5W8BS A9 | 3 | 46 | 13 | 2 | 战后奖励 monster | Rewards | 14 / 77 | 33 | 27 | 19 | 21108c9e |
| 1793671962-A43U5UZ5W8BS-part22-000679 | A43U5UZ5W8BS A9 | 3 | 47 | 14 | 1 | 进入 rest_site | RestSite | 14 / 77 | 33 | 27 | 19 | 7c8d036f |
| 1793671962-A43U5UZ5W8BS-part22-000690 | A43U5UZ5W8BS A9 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 37 / 77（sim 62） | 33 | 27 | 19 | c9318177 |
| 1793671962-A43U5UZ5W8BS-part22-001931 | A43U5UZ5W8BS A9 | 3 | 48 | 15 | 0 | 战后奖励 boss | Rewards | 22 / 77 | 33 | 27 | 19 | d74da963 |
| 1793767417-WWVGBDEYNG19-part8-001189 | WWVGBDEYNG19 A10 | 3 | 34 | 1 | 14 | 进入 ancient | Event | 3 / 77（sim 62） | 24 | 6 | 11 | adaec7cc |
| 1793767417-WWVGBDEYNG19-part8-001194 | WWVGBDEYNG19 A10 | 3 | 34 | 1 | 14 | Ancient 后 event | Event | 62 / 77 | 24 | 6 | 12 | 6edf43de |
| 1793767417-WWVGBDEYNG19-part8-001203 | WWVGBDEYNG19 A10 | 3 | 35 | 2 | 13 | 进入 monster | Combat | 62 / 77 | 24 | 6 | 12 | c6d67835 |
| 1793767417-WWVGBDEYNG19-part9-000003 | WWVGBDEYNG19 A10 | 3 | 35 | 2 | 13 | 进入 monster | Combat | 62 / 77 | 24 | 6 | 12 | e50e240b |
| 1793767417-WWVGBDEYNG19-part9-000220 | WWVGBDEYNG19 A10 | 3 | 35 | 2 | 13 | 战后奖励 monster | Rewards | 53 / 77 | 24 | 6 | 12 | e274dc03 |
| 1793767417-WWVGBDEYNG19-part9-000242 | WWVGBDEYNG19 A10 | 3 | 36 | 3 | 12 | 进入 unknown | Event | 53 / 77 | 25 | 7 | 12 | 836348a9 |
| 1793767417-WWVGBDEYNG19-part9-000258 | WWVGBDEYNG19 A10 | 3 | 37 | 4 | 11 | 进入 monster | Combat | 50 / 77 | 24 | 7 | 12 | 201ccd45 |
| 1793767417-WWVGBDEYNG19-part9-000493 | WWVGBDEYNG19 A10 | 3 | 37 | 4 | 11 | 战后奖励 monster | Rewards | 50 / 77 | 24 | 7 | 12 | e11aae3b |
| 1793767417-WWVGBDEYNG19-part10-000003 | WWVGBDEYNG19 A10 | 3 | 38 | 5 | 10 | 进入 monster | Combat | 50 / 77 | 24 | 7 | 12 | f2d0f2e7 |
| 1793767417-WWVGBDEYNG19-part11-000003 | WWVGBDEYNG19 A10 | 3 | 38 | 5 | 10 | 进入 monster | Combat | 50 / 77 | 24 | 7 | 12 | 51f1e5cb |
| 1793767417-WWVGBDEYNG19-part11-000371 | WWVGBDEYNG19 A10 | 3 | 38 | 5 | 10 | 战后奖励 monster | Rewards | 30 / 77 | 24 | 7 | 12 | 6871f476 |
| 1793767417-WWVGBDEYNG19-part9-000513 | WWVGBDEYNG19 A10 | 3 | 38 | 5 | 10 | 进入 monster | Combat | 50 / 77 | 24 | 7 | 12 | ceb35eda |
| 1793767417-WWVGBDEYNG19-part11-000390 | WWVGBDEYNG19 A10 | 3 | 39 | 6 | 9 | 进入 unknown | Event | 30 / 77 | 24 | 7 | 12 | 382e0180 |
| 1793767417-WWVGBDEYNG19-part11-000404 | WWVGBDEYNG19 A10 | 3 | 40 | 7 | 8 | 进入 rest_site | RestSite | 30 / 77 | 24 | 7 | 12 | 07c35617 |
| 1793767417-WWVGBDEYNG19-part11-000424 | WWVGBDEYNG19 A10 | 3 | 41 | 8 | 7 | 进入 treasure | Treasure | 30 / 77 | 24 | 8 | 12 | e9ed54b6 |
| 1793767417-WWVGBDEYNG19-part11-000437 | WWVGBDEYNG19 A10 | 3 | 42 | 9 | 6 | 进入 rest_site | RestSite | 30 / 77 | 24 | 8 | 13 | 7c38ca95 |
| 1793767417-WWVGBDEYNG19-part11-000454 | WWVGBDEYNG19 A10 | 3 | 43 | 10 | 5 | 进入 shop | Shop | 30 / 77 | 24 | 9 | 13 | ae1c83bc |
| 1793767417-WWVGBDEYNG19-part11-000470 | WWVGBDEYNG19 A10 | 3 | 44 | 11 | 4 | 进入 rest_site | RestSite | 30 / 77 | 25 | 9 | 13 | a840120e |
| 1793767417-WWVGBDEYNG19-part11-000485 | WWVGBDEYNG19 A10 | 3 | 45 | 12 | 3 | 进入 monster | Combat | 30 / 77 | 25 | 10 | 13 | ac3ecf57 |
| 1793767417-WWVGBDEYNG19-part11-000828 | WWVGBDEYNG19 A10 | 3 | 45 | 12 | 3 | 战后奖励 monster | Rewards | 30 / 77 | 25 | 10 | 13 | 5954cc05 |
| 1793767417-WWVGBDEYNG19-part11-000844 | WWVGBDEYNG19 A10 | 3 | 46 | 13 | 2 | 进入 unknown | Event | 30 / 77 | 25 | 10 | 13 | 98a41f01 |
| 1793767417-WWVGBDEYNG19-part11-000863 | WWVGBDEYNG19 A10 | 3 | 47 | 14 | 1 | 进入 rest_site | RestSite | 30 / 77 | 25 | 10 | 13 | 9422b60d |
| 1793767417-WWVGBDEYNG19-part11-000878 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | 58072fc3 |
| 1793767417-WWVGBDEYNG19-part12-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | 9b3663ad |
| 1793767417-WWVGBDEYNG19-part13-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | e8a46b81 |
| 1793767417-WWVGBDEYNG19-part14-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 16 | 06bec170 |
| 1793767417-WWVGBDEYNG19-part15-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 16 | 91c80b83 |
| 1793767417-WWVGBDEYNG19-part15-000624 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 战后奖励 boss | Rewards | 40 / 77 | 25 | 10 | 16 | 9b978f48 |
| 1793767417-WWVGBDEYNG19-part15-000632 | WWVGBDEYNG19 A10 | 3 | 49 | 16 | -1 | 进入 boss | Combat | 40 / 77 | 25 | 10 | 16 | 2816ffa5 |
| 1793767417-WWVGBDEYNG19-part15-001295 | WWVGBDEYNG19 A10 | 3 | 49 | 16 | -1 | 战后奖励 boss | Rewards | 25 / 77 | 25 | 10 | 16 | 4d0e1a9f |

#### `silent-a9a10-act3-late`（30 份）

| save id（原池文件名） | 局（种子 / A） | act | floor | act floor | 距 boss | 存档点 / 房间 | sim 首观测阶段 | hp / max_hp | 牌组 | 升级 | 遗物 | sha256[:8] |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1793671962-A43U5UZ5W8BS-part19-000692 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 进入 elite | Combat | 33 / 77 | 33 | 27 | 15 | a6dfb1a2 |
| 1793671962-A43U5UZ5W8BS-part20-000003 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 进入 elite | Combat | 33 / 77 | 33 | 27 | 16 | baeb4ec5 |
| 1793671962-A43U5UZ5W8BS-part20-000667 | A43U5UZ5W8BS A9 | 3 | 42 | 9 | 6 | 战后奖励 elite | Rewards | 33 / 77 | 33 | 27 | 16 | a3a8365c |
| 1793671962-A43U5UZ5W8BS-part20-000682 | A43U5UZ5W8BS A9 | 3 | 43 | 10 | 5 | 进入 monster | Combat | 33 / 77 | 33 | 27 | 17 | 82787559 |
| 1793671962-A43U5UZ5W8BS-part20-001105 | A43U5UZ5W8BS A9 | 3 | 43 | 10 | 5 | 战后奖励 monster | Rewards | 21 / 77 | 33 | 27 | 17 | fd46069a |
| 1793671962-A43U5UZ5W8BS-part20-001116 | A43U5UZ5W8BS A9 | 3 | 44 | 11 | 4 | 进入 unknown | Event | 21 / 77 | 33 | 27 | 17 | ccb17796 |
| 1793671962-A43U5UZ5W8BS-part21-000003 | A43U5UZ5W8BS A9 | 3 | 44 | 11 | 4 | 进入 unknown | Event | 21 / 77 | 33 | 27 | 17 | a1622596 |
| 1793671962-A43U5UZ5W8BS-part21-000017 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 进入 elite | Combat | 21 / 77 | 33 | 27 | 18 | 85f764d8 |
| 1793671962-A43U5UZ5W8BS-part22-000003 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 进入 elite | Combat | 21 / 77 | 33 | 27 | 18 | bff831d3 |
| 1793671962-A43U5UZ5W8BS-part22-000398 | A43U5UZ5W8BS A9 | 3 | 45 | 12 | 3 | 战后奖励 elite | Rewards | 14 / 77 | 33 | 27 | 18 | 313cf869 |
| 1793671962-A43U5UZ5W8BS-part22-000411 | A43U5UZ5W8BS A9 | 3 | 46 | 13 | 2 | 进入 monster | Combat | 14 / 77 | 33 | 27 | 19 | a09eef34 |
| 1793671962-A43U5UZ5W8BS-part22-000668 | A43U5UZ5W8BS A9 | 3 | 46 | 13 | 2 | 战后奖励 monster | Rewards | 14 / 77 | 33 | 27 | 19 | 21108c9e |
| 1793671962-A43U5UZ5W8BS-part22-000679 | A43U5UZ5W8BS A9 | 3 | 47 | 14 | 1 | 进入 rest_site | RestSite | 14 / 77 | 33 | 27 | 19 | 7c8d036f |
| 1793671962-A43U5UZ5W8BS-part22-000690 | A43U5UZ5W8BS A9 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 37 / 77（sim 62） | 33 | 27 | 19 | c9318177 |
| 1793671962-A43U5UZ5W8BS-part22-001931 | A43U5UZ5W8BS A9 | 3 | 48 | 15 | 0 | 战后奖励 boss | Rewards | 22 / 77 | 33 | 27 | 19 | d74da963 |
| 1793767417-WWVGBDEYNG19-part11-000437 | WWVGBDEYNG19 A10 | 3 | 42 | 9 | 6 | 进入 rest_site | RestSite | 30 / 77 | 24 | 8 | 13 | 7c38ca95 |
| 1793767417-WWVGBDEYNG19-part11-000454 | WWVGBDEYNG19 A10 | 3 | 43 | 10 | 5 | 进入 shop | Shop | 30 / 77 | 24 | 9 | 13 | ae1c83bc |
| 1793767417-WWVGBDEYNG19-part11-000470 | WWVGBDEYNG19 A10 | 3 | 44 | 11 | 4 | 进入 rest_site | RestSite | 30 / 77 | 25 | 9 | 13 | a840120e |
| 1793767417-WWVGBDEYNG19-part11-000485 | WWVGBDEYNG19 A10 | 3 | 45 | 12 | 3 | 进入 monster | Combat | 30 / 77 | 25 | 10 | 13 | ac3ecf57 |
| 1793767417-WWVGBDEYNG19-part11-000828 | WWVGBDEYNG19 A10 | 3 | 45 | 12 | 3 | 战后奖励 monster | Rewards | 30 / 77 | 25 | 10 | 13 | 5954cc05 |
| 1793767417-WWVGBDEYNG19-part11-000844 | WWVGBDEYNG19 A10 | 3 | 46 | 13 | 2 | 进入 unknown | Event | 30 / 77 | 25 | 10 | 13 | 98a41f01 |
| 1793767417-WWVGBDEYNG19-part11-000863 | WWVGBDEYNG19 A10 | 3 | 47 | 14 | 1 | 进入 rest_site | RestSite | 30 / 77 | 25 | 10 | 13 | 9422b60d |
| 1793767417-WWVGBDEYNG19-part11-000878 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | 58072fc3 |
| 1793767417-WWVGBDEYNG19-part12-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | 9b3663ad |
| 1793767417-WWVGBDEYNG19-part13-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 13 | e8a46b81 |
| 1793767417-WWVGBDEYNG19-part14-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 16 | 06bec170 |
| 1793767417-WWVGBDEYNG19-part15-000003 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 进入 boss | Combat | 53 / 77 | 25 | 10 | 16 | 91c80b83 |
| 1793767417-WWVGBDEYNG19-part15-000624 | WWVGBDEYNG19 A10 | 3 | 48 | 15 | 0 | 战后奖励 boss | Rewards | 40 / 77 | 25 | 10 | 16 | 9b978f48 |
| 1793767417-WWVGBDEYNG19-part15-000632 | WWVGBDEYNG19 A10 | 3 | 49 | 16 | -1 | 进入 boss | Combat | 40 / 77 | 25 | 10 | 16 | 2816ffa5 |
| 1793767417-WWVGBDEYNG19-part15-001295 | WWVGBDEYNG19 A10 | 3 | 49 | 16 | -1 | 战后奖励 boss | Rewards | 25 / 77 | 25 | 10 | 16 | 4d0e1a9f |

## 风险与备注

- 只有两局人类游戏，第三幕 boss 战开局只有 3 个不同状态，且同一 QUEEN 入口有 5 份内容不同（`num_reloads`、遗物数 13 / 16、
  grab bag 顺序）的重存；probe 只按字节去重，所以都保留，均匀抽样会偏向 QUEEN。需要更强 boss 覆盖就得再录对局。
- A9 那局（A43U5UZ5W8BS）27 张牌已升级，A10 那局（WWVGBDEYNG19）只有 6–10 张：两池里的两套牌组强度差别大。
- 3 份 boss 战后奖励屏不是战斗；对 item A 的 boss-HP 分数来说它们不付钱（不是 fight），对 boss 学校只是 boss 后的 Rewards 起点。
- 这两池都没接进任何 launch 脚本，也没进 Colab bundle；用的时候 `SAVES_DIR=<池> scripts/train/make_colab_bundle.sh` 或另传
  `--start-saves`。
