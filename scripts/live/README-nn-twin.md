# 看训练好的网络打真游戏（twin lockstep）

`scripts/live/nn_twin_play.py` 让一个 `sts2train` checkpoint 在**真实的** Slay the Spire 2 里打一局，你在旁边看。
网络从不直接读游戏画面：

```
真实游戏 ──STS2_MCP──▶ 状态 + 合法动作 ──┐
                                          ├─ 逐项对比（层、幕、HP、金币、能量、格挡、手牌、敌人、药水、遗物、当前画面内容）
模拟器孪生（同一种子 / 游戏存档）─────────┘
      │ checkpoint 读孪生的观测，选一个候选行
      ▼
按“含义”映射到游戏的候选（牌 id + 升级 + 目标 combat_id、地图坐标、奖励身份、商品、休息选项……）
      ▼
在游戏里执行（等提交证据 + 静默帧），孪生走同一行
```

孪生和游戏一旦对不上（desync），runner 进入 **follow 模式**：继续用孪生的观测给网络打分，取排名最高、且能映射到
游戏里的动作；什么都映射不上时用一个确定性的兜底动作（日志里标 `fallback`，不算网络的决策）；到游戏下一个存档点
（进房间 / 战斗胜利后的奖励 / 先古之民事件结束）就用游戏自己的 `current_run.save` 重新播种孪生（resync），对上就回到
lockstep。

## 已验证到什么程度（2026-11-04 夜）

checkpoint：s3c `best.pt`（encoder `5dbfa09dd9dcb5ed`，15.8M 参数，静默猎手 A10 训练，sim 评测 progress 0.156、
平均 9 层、0 胜；没有 `--aux-heads`，所以状态行显示 critic 值 `V` 而不是通关把握）。种子 `TW1NA10S01`。

- **Dry run**（模拟器 play server 当替身，两个独立进程）：整局 256 个决策，第 17 层首领 LAGAVULIN_MATRIARCH 战败，
  0 desync；孪生自己行号推出的 token 与替身按含义匹配到的 token 256/256 相同，替身的 history 与 runner 发出的完全
  一致；3.2 秒。
- **真实游戏**（无头实例：`scripts/headless_provision.sh` + `headless_launch.sh`，锁定的带 eval API 的 MCP 构建，
  录像器关闭，与你的 Steam 安装、存档、录像目录完全隔离）：
  - 纯 lockstep：255 个决策，**0 desync，0 resync**，游戏结果与 dry run 完全相同（第 17 层被
    LAGAVULIN_MATRIARCH_BOSS 击败，score 340），逐个决策的动作、概率、HP 与 dry run 一致；UI glue 20 次（19 次多选
    确认，1 次离开宝箱房），孪生单边 1 次（模拟器的显式开箱，游戏里宝箱自己开）；约 505 秒，2 秒/决策（推理
    9 ms，执行中位 1.4 s）。两次独立运行（提交前、提交 `ca9cfbd` 后）结果相同。
  - `--anchor room --inject-desync 100`：每个存档点都从游戏存档重新播种孪生，并在第 100 个决策故意让孪生走另一步。
    23 次导入：22 次导入后与游戏完全一致（16 次进房间、5 次战后奖励、1 次涅奥结束），1 次被导入器拒绝（第 8 层
    战后存档带 `extra_rewards`——小偷偷走的金币——已知不支持），runner 保留原孪生继续；注入的 desync 在第 6 层战斗
    中出现（能量/敌人 HP/手牌），follow 模式打完这场战斗，战后奖励存档 resync 成功回到 lockstep。
  - 4 局随机策略（种子 `C0VER11/12/27/28`，共 429 个决策，用来覆盖 s3c 从不碰的画面：领卡、领金币/药水、商店
    购买、战斗内喝药水、牌组多选、涅奥卡包）：0 个状态 desync。暴露的两处接口差异已修：非战斗画面的丢药水
    （MCP 状态没带 `can_discard_out_of_combat`，runner 自己补上）、模拟器“空确认”= 游戏的跳过按钮；非战斗画面
    喝药水 MCP 不开放，按 `substitute` 处理。
  - 产物（git 忽略）：`output/nn-twin/20261104-s3c-silent-a10-TW1NA10S01-real`（提交 `ca9cfbd` 跑的最终真实局，
    255 个决策，0 desync，与同目录 `-dry` 逐决策相同）和 `output/nn-twin/20261104-validation/`（上面各次验证）。
- 单元测试 46 个：每种候选类型的映射（MCP 状态形状来自 E3 农场的 v0.111.0 记录）、视图对比、UI glue 规则、
  resync 规则（存档种类、层数、只导入一次、回退）、play-server token 解码；集成测试 2 个：带 aux heads 的小
  checkpoint dry run（验证 win_prob 记录），5 个随机策略整局（商店、休息、事件、奖励、药水、多选、卡包）。
- **合并进主线后**（Live twin integrate，库 encoder `3b73317739c650bd`：`RunActionKind` 多了奖励页宏动作 21
  `TakeRewardCard` / 22 `TakeRewardAlternative`）：单元测试 50 个（+ 按库的 `RunActionKind` 逐个枚举 23 种候选的解码与
  token、`TakeRewardAlternative` 的两步计划、多个卡牌奖励按名次配对、play server 上的宏动作 token）；集成测试 4 个
  （两个小 checkpoint 的 dry run——本库 spec 的和宏动作之前 spec 的（加宽加载）、加宽检查、5 个随机策略整局，宏动作
  必须出现）。dry run 全部 0 desync、token 全同。
- **合并后的库上的真实游戏（2026-11-04 早）**：checkpoint s8a `ckpt-0002050.pt`（encoder `3b73317739c650bd`，2.9M
  参数，`--aux-heads` + `--deck-counts`，状态行显示通关把握），贪心 + `--loop-breaker`（默认开，和 `sts2train eval`
  一样）。最初两局暴露了三个接口差异，已修（`c89a0bc`、`39c5b46`）：MCP 的遗物奖励不带 id（网络要的遗物被丢下）、
  事件里的奖励页领完后游戏自己关掉、升级预览上的取消在游戏里只回到选牌网格（要再取消一次）。修复后：
  `TW1NA10S02` 233 个决策、`TW1NA10S03` 228 个决策，**都是 0 desync、0 resync，与各自 dry run 逐决策相同**（各有
  1 次 loop breaker：休息处 HEAL 代替又一次 SMITH），都死在第一幕首领（THE_KIN_BOSS / WATERFALL_GIANT_BOSS），
  约 7.5 分钟一局。修复前的 S03 那局因为兜底动作走了别的路，反而打到第二幕第 22 层。产物：
  `output/nn-twin/20261104-s8a-silent-a10-*`。

## 你早上要做的（推荐：可见的隔离实例）

不碰你的 Steam 安装和存档，用今晚验证过的同一个 MCP 构建，只是把窗口打开。

```bash
cd /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-live-twin    # 或合并后的主检出
# 一次性：模拟器与训练环境
dotnet build sim/Sts2Sim.sln -c Release
uv sync --project sim/tools/train

# 1. 隔离实例（headless-instances/inst1：自己的 HOME、端口 15601；解锁从你的存档只读复制）
scripts/headless_provision.sh 1            # 已做过可跳过；会重建锁定的 MCP（sha256 校验）
scripts/headless_launch.sh 1 --visible     # 有窗口的那一个；不加 --visible 就是无头

# 2. 开跑：runner 用 eval API 自己开一局带种子的标准局，然后开始下
nice -n 10 uv run --project sim/tools/train python scripts/live/nn_twin_play.py \
    --checkpoint <你的 best.pt> --character SILENT --ascension 10 --seed TW1NA10S01 \
    --port 15601 --start-run \
    --save-root "$PWD/headless-instances/inst1/home/Library/Application Support/SlayTheSpire2" \
    --out output/nn-twin/$(date +%Y%m%d-%H%M)-silent-a10

# 3. 结束后停实例
scripts/headless_launch.sh 1 --stop
```

`--visible` 今晚没有跑过（屏幕关着），它和无头模式的唯一区别是不传 Godot 的 `--headless`，隔离方式完全一样。

合并后的树（PR #30，带奖励页宏动作的库）同样能跑第 2 步，而且在新库上训练的 checkpoint 只能在它上面跑。实例仍在
`train-live-twin` 里（`headless-instances/` 按树存放、git 忽略）：在那棵树里启停实例，在合并后的树里运行 runner，
`--save-root` 写实例的绝对路径。

## 用你自己的游戏（Steam）

```bash
cd /Users/Aincrad/dev-proj/hack-sts2
scripts/mcp_mod.sh on        # 必须用主检出的脚本：.mcp-stashed/ 在主检出
```

1. 从 Steam 启动游戏（带 mod 加载）。
2. 开一局**标准模式**静默猎手进阶 10。标准模式在原版游戏里不能填种子——不用填：runner 从
   `current_run.save` 读出这一局的种子，孪生用同一个种子开局。（自定义模式能填种子，但它不是标准局，存档导入会拒绝，
   不要用。）在涅奥的第一个画面（还没点任何选项）启动 runner 最好；已经过了第 1 层也行，runner 会导入当前存档，但必须
   停在房间开头。
3. 运行：

   ```bash
   cd /Users/Aincrad/dev-proj/hack-sts2/.claude/worktrees/train-live-twin
   nice -n 10 uv run --project sim/tools/train python scripts/live/nn_twin_play.py \
       --checkpoint <你的 best.pt> --character SILENT --ascension 10 \
       --out output/nn-twin/$(date +%Y%m%d-%H%M)-mine
   ```

   默认端口 15526（`.mcp-stashed/STS2_MCP.conf`），默认存档根 `~/Library/Application Support/SlayTheSpire2`。
4. 打完（或中途停）以后**一定要**：

   ```bash
   cd /Users/Aincrad/dev-proj/hack-sts2 && scripts/mcp_mod.sh off && scripts/mcp_mod.sh status
   ```

   录像器一直装着，这一局会被录下来；它的 manifest 的 mods 列表里有 STS2_MCP，靠这个和人类局区分。

**风险（没验证）**：`.mcp-stashed/` 里的 `STS2_MCP.dll` 是 2026-07-14 的 v0.107.1 兼容构建（`0.4.0+v0107`，sha256
`fa24a144…`），没有 eval API，也没有 selection-state 补丁，现在的游戏是 v0.111.0。它在 v0.111.0 上能否加载、多选界面能否
确认都没测过。runner 连不上（`no STS2_MCP answer on port 15526`）或卡在多选确认时，改用上面的隔离实例。补丁版 MCP
（`scripts/build_selection_mcp.sh`）按项目规定不能装进你的游戏。

## 看什么

每个决策一行状态（终端和 `<out>/status.log`）：

```
#0027 A1 F03 combat      HP 56/81 E3 | play STRIKE_SILENT -> u1 TOADPOLE p=0.35 | V +0.12 | sync
      · glue confirm: confirm selection
```

序号、幕、层、画面、HP、能量 | 网络选的动作（孪生的说法）和它的概率 | `通关把握 xx%`（checkpoint 带 `--aux-heads`
的胜率头时）否则 `V`（critic 价值：期望协议回报，不是概率）| `sync` / `DESYNC 字段…` / `follow`。缩进的 `·` 行是
只在一边存在的 UI 步骤（事件对话翻页、游戏需要而模拟器不需要的“确认”按钮、宝箱房离开、模拟器的显式开箱）。

- `<out>/trajectory.jsonl`：每条记录一行。`type` = `start` / `decision` / `glue` / `twin_only` / `resync` / `error`。
  决策记录：`act` `floor` `screen` `hp` `mode`（lockstep/follow）`policy`（greedy / substitute / follow /
  follow_relaxed / fallback，见下）、
  `twin_action`（行号、语义、play-server token）、`real_action`（发给 MCP 的 JSON）、`p`、`win_prob`、`value`、
  `top5`（前五个候选及概率）、`desync`（字段、孪生值、游戏值）、`ms`（观察/推理/执行耗时）、`twin_hash`。
- `<out>/summary.json`：决策数、desync 决策数、前几次 desync 详情（层、画面、字段）、resync 次数和结果、各策略来源
  的决策数、计数器、最终画面、`result`（eval API 的 run_result）、耗时。
- `<out>/saves/`：重新播种孪生时用的存档副本（原存档只读）。
- `policy`：`greedy` = 网络的首选，原样执行；`substitute` = 两边状态一致，但游戏接口不提供网络的首选（例如在
  非战斗画面喝药水，STS2_MCP 不开放），改用网络排名最高、游戏提供的那个，孪生走同一步，仍是 lockstep（状态行
  标 `[instead of …]`）；`follow` / `follow_relaxed` = desync 之后网络排名最高的可映射动作（relaxed 忽略升级）；
  `fallback` = 什么都映射不上时的确定性兜底，不是网络的选择。
- 暂停：`touch <out>/PAUSE`（删掉继续），这期间可以手动操作游戏，但回来时要停在 runner 离开时的同一个决策点，
  否则下一步会 desync，等下一个存档点再对齐。停止：Ctrl-C（做完当前一步，写 summary）。

## desync 是什么意思

孪生（模拟器）和游戏在同一个决策点上看到的东西不一样。常见原因：

1. **模拟器移植误差**：E3 农场 39 局的结论是每决策 99.5–99.9% 一致；未解决的 7 个分歧见
   `sim/specs/diff-report-e3-farm-2026-11-02.md` §4（PUNCH_OFF 事件开战、GAMBLING_CHIP 回合开始选牌的阶段名、
   STRATAGEM 牌堆选择的顺序、链式重放里开局手牌哪张打击被升级、链式战斗后的回合计数等）。
2. **解锁差异**：模拟器默认全解锁（`UnlockProfile.All`）；你的档如果有没解锁的卡/遗物，奖励池可能不同。从游戏存档
   resync 之后孪生用的是存档里的真实解锁状态。
3. **执行没被确认**：动作发出去了但游戏没在超时内给出提交证据（`EXEC-FAIL`）；runner 会试着让孪生也走这一步，
   比对后决定保留还是撤回。

desync 之后网络仍然在玩（follow 模式），只是孪生可能已经和游戏不是同一局面。战斗中途没有存档，所以**战斗内的
desync 要到这场战斗结束（奖励画面的存档）才能对齐**；其余画面在下一个房间开头对齐。`--anchor room` 让 runner 在每个
存档点都从游戏存档重新播种（即使没 desync），换来解锁/隐藏状态与游戏一致，代价是不再检验模拟器的长链一致性。

## 已知限制

- 战斗内不能重新同步（游戏只在房间边界写存档）；follow 模式下的映射是“同一张牌 + 同一个目标”，手牌不同就可能退到
  兜底动作。
- `--lookahead`：`sts2train.lookahead` 已合并进这棵树，但它的镜像环境只会从新种子重放，跟不上存档 resync，所以本
  runner 只做贪心（`--lookahead` 打印原因后退出 2）。
- 奖励页宏动作（`TakeRewardCard` / `TakeRewardAlternative`，合并后的库才有）在游戏里是两步：先点开第 k 个卡牌奖励
  （按孪生未领卡牌奖励里的名次配对），再选那张牌 / REROLL（Driftwood）/ SACRIFICE（Pael's Wing）。dry run 覆盖
  `TakeRewardCard`，`TakeRewardAlternative` 只有单元测试，两者都没在真实游戏里跑过。点开后游戏里的卡和孪生的不一样
  （打开前看不到的隐藏信息）时，runner 记一次 `macro_step` desync、停用孪生，用兜底动作打到下一个存档点再重新播种。
- 宏动作之前训练的 checkpoint（encoder `5dbfa09dd9dcb5ed`：s3c / s4a / s5b）在合并后的库上加宽加载（`RunActionKind`
  21 → 23，新行从零开始，和 `sts2train eval` 一样；`config.json` 的 `spec_compat` 记 `widened`）。不加宽的话模型会把
  宏动作行夹到最后一种动作（PromptCancel）去打分。反过来，新库上训练的 checkpoint 在宏动作之前的树上会被拒绝。
- 原版 MCP 的事件对话翻页、确认按钮等纯 UI 步骤由 runner 自动点（日志里的 glue），不经过网络。
- 非战斗画面只能丢药水（`discard_potion`，协议文档写明可用），不能喝：网络在那里想喝药水时按 `substitute` 处理。
- `--inject-desync N`（只用于测试）：第 N 个决策让孪生故意走另一步，用来验证 follow 和存档 resync。
- 卡牌/遗物名字在游戏里是本地化的（中文界面），runner 只用 id 比对，不受影响。
- 速度约 2 秒/决策（游戏动画 + 提交证据 + 静默窗口，网络推理只占 10 ms）：s3c 这样死在第一幕首领的局约 8 分钟，
  打穿三幕（约 1500 个决策）约 50 分钟。
- 导入器不支持带 `extra_rewards` 的战后存档（例如小偷偷了金币的战斗）：这类存档点上 resync 失败，孪生保持原状，
  等下一个存档点。

## 宏动作（macro）：一个决策，游戏里几次点击（W4 2026-11-04）

模拟器的 Vec 层可以把“打开一个画面 → 选牌 → 确认”这样的多步选择列成**一个候选**（宏动作），网络一步选完，不再在
逐步选择的画面之间来回（docs/diag-2026-11-04/q4-loopbreaker.md 的循环）。真实游戏没有这种一步动作，runner 把宏动作
按它的点击顺序在游戏里执行，每一步都检查游戏到了预期画面：

| 宏 | 孪生的行 | 游戏里的点击（每步检查） |
| --- | --- | --- |
| `TakeRewardCard` / `TakeRewardAlternative` | 奖励页（库自带） | 点开第 k 个卡牌奖励（按名次配对）→ 选那张牌 / REROLL / SACRIFICE |
| `SMITH` | 休息处 `ChooseRestOption(A, B = 牌组第 d 张)`（`--macros rest`） | 选 SMITH → 升级网格（网格必须与孪生的可升级牌完全相同）→ 选第 d 张牌 → 确认预览 |
| `REMOVE` | 商店 `BuyEntry(A, B = d)`（`--macros shop`） | 买删牌服务 → 删牌网格（同上检查，诅咒排在最前）→ 选第 d 张牌 → 确认 |

- 孪生按 checkpoint 训练时的宏开 env：`--macros checkpoint`（默认，读 checkpoint 的 `config.macros`，和
  `sts2train eval` 一样），`--macros none` 关，或显式 `--macros rest,shop`。不开宏的 checkpoint 的路径与之前完全一样
  （`Twin()` 不传 `macros`）。`config.json` 记 `twin_macros`。
- 执行规则在 `scripts/live/nn_twin/macros.py` 的表 `SPECS` 里（宏名 → 每个点击的 `Step`：游戏必须显示的画面、是否
  可选、是否在这一步检查网格；`open` = 第一步怎么配对；`abort` = 检查失败时是否用游戏的取消键退回房间）。孪生把宏行
  解码成 `Act("macro", (名字, …), plan=(点击…), expect=((牌, 孪生位置), …))`（`twin.py`），runner 在
  `decide()` 里调 `macros.play_plan`。新的宏（owner G 的 `--macros all`：战斗内手牌选择、选一张牌、事件的多张变形 /
  删除 / 升级）只要在 `SPECS` 里登记名字；没登记的名字用 `generic_spec`（每步必需、第一个选牌帧检查网格、末尾的确认
  可选——游戏可能在最后一张牌选中时自己关掉选择）。
- **选牌只解析一次**：在第一个选牌帧（in-prompt 宏是点击之前的那一帧）按“同一张牌（id + 升级）的第 j 个副本”把计划里
  的每个选择配到游戏网格的 `index`，之后每步只检查那个 `index` 还在提供。不能每步重新匹配：STS2_MCP 的多选网格里已选
  的牌还在，再选一次就是取消选择。牌的身份 = 网格位置；STS2_MCP 不给实例 id，所以同一张牌的副本按顺序配对——两边的
  顺序都来自牌组（`RoomPrompts.FromDeckForUpgrade` 牌组顺序；`FromDeckForRemoval` 诅咒在前，其余牌组顺序）。只在附魔上
  不同的两张副本，视图和接口都分不出来（已知限制）。
- 检查失败（打开后不是选牌画面、网格与孪生不同、该点的不在）：记一次 `macro_step` / `macro_grid` desync（记录里的
  `macro.failure`），runner 不让孪生走这一步；SMITH / REMOVE 会先按游戏的取消键退回休息处 / 商店（`macro.aborted`，
  此时游戏和孪生都停在房间里，什么都没选，进 follow 模式），奖励宏和没退回的情况停用孪生、兜底动作打到下一个存档点
  再重新播种。follow 模式下不比网格，选牌按 id 配对（副本不够时取第一张）。
- 决策记录多一个 `macro` 字段：`name`、每步的 `steps`（动作、当时的画面、`ok` / `skipped` / `failed`）、`failure`、
  `aborted`；`real_action` 是全部点击。summary 的计数器：`macros`、`macro_failures`、`macro_aborts`。
- 信息对等：宏只用游戏在这个选择前给玩家看的东西（牌组、休息选项、删牌价格）；网格检查比的正是游戏网格上的牌。
- 验证到什么程度：单元测试（假的 MCP 传输：按 v0.111.0 的状态形状写的脚本化状态 + 以 wire 为键的转移表，发出意外的
  wire 就报错）覆盖 SMITH（副本按名次、网格不同 → 取消退回、打开后没到网格、预览不能确认 → 两次取消）、REMOVE（诅咒
  在前的顺序、删牌不可用时不映射）、两张牌的变形（选择一次解析，不会对同一 index 点两次）、末尾可选的确认、runner 的
  lockstep 和 abort 路径、孪生的宏行解码与 token；集成测试：`--macros rest,shop` 的孪生 + 随机策略对 play server 替身
  （替身没有宏，所以宏 = 它的三个 token）整局 dry run，0 desync、token 全同。**没有在真实游戏里跑过**（今晚不能开游戏）：
  真实游戏里升级 / 删牌网格的顺序是否与牌组顺序一致、删牌确认是否总有预览，都还要第一次真机运行来确认（看
  `macro.steps` 和 `macro_grid` desync）。

### prompt 宏（`--macros all` = `rest,shop,prompt`，W5 2026-11-05）

`all` 训练的模型在孪生里看到的是“无取消、选满即提交”的提示（sim/VEC.md “Prompt macros v2”）。这些行沿用
`PromptSelect` / `PromptConfirm` 的种类，所以不是 `macro` Act；runner 按孪生 env **自己走完这一步后的状态**决定游戏里的点击
（`macros.py` “Prompt macros”，`Runner.prompt_commit`）：

| 孪生的行 | 游戏里的点击 |
| --- | --- |
| `PromptSelect(A)`，孪生这一步**没**提交（exactly-k 的前 k−1 张、up-to-N 未满） | 只选这张牌（按 key + 名次配对，同其他选牌） |
| `PromptSelect(A)`，孪生这一步提交了（离开了提示，或站在另一个提示里：`global_i.prompt_picked` 不是原来 +1 / `prompt_site_hash` 变了）——exactly-1、exactly-k 的第 k 张、up-to-N 的第 N 张、无序提示选到末尾 | 选这张牌，然后：游戏仍显示已选的牌且有确认键 → 点确认；游戏已自己关掉选择 → 不点（`closed_by_game`）；两边都进了新提示 → 不点（`next_prompt`）；游戏显示的已选张数（`View.picked`）不等于孪生的 picked+1 → 不点，**fail closed**（`prompt_picked`）；其余（已选、无确认键；孪生已离开而游戏是新的未选提示；确认被拒）→ **fail closed** |
| `PromptConfirm`，已有选牌（DONE） | 游戏的确认 |
| `PromptConfirm`，0 张（DECLINE，只在游戏允许时列出） | 游戏的确认 / 跳过键（可跳过的 choose 画面同时给 `cancel_selection` 和 `skip_card_selection`：取跳过）；**绝不**映射到可取消站点的“取消”（那会回到打开提示的画面）——游戏只给取消时这行不映射（同步时换成游戏提供的次优行，否则 follow）。**宏关时（`--macros rest,shop` / none）同一规则**：DECLINE 有跳过就取跳过、绝不取取消；宏关时另有的 `PromptCancel` 行仍映射到游戏的取消 |
| `PromptCancel` | 宏开时不该出现；候选表里一出现就 fail closed（`prompt_cancel_row`，孪生停用、兜底到下一个存档点） |
| 休息点 / 商店 `MACRO_FALLBACK`（status 512，宏行会超过 64 行） | 原始 `SMITH` / 删牌行（普通 `rest` / `buy` 映射）+ 上面的无取消提示行；计数器 `twin_macro_fallback` |

- 孪生侧：`Twin(macros="all")`（或 `"rest,shop,prompt"`；`--macros checkpoint` 读 checkpoint 的 `config.macros`）→
  `twin.prompt_macros`；`Twin.prompt_marker()` 读 `prompt_picked / prompt_site_hash / prompt_needs_confirm / min / max`。
  dry run 的 token：提交且孪生提示需要确认时是 `pick j; confirm`，否则 `pick j`（与 play server 的历史逐条相等）。
- 游戏侧：STS2_MCP 的网格里已选的牌还在，再选一次就是取消选择；`mcp.acts_of` 不再把已选的牌（`is_selected` /
  `selected_indices`）列为可选，k 张选择的后几张只在未选的牌里配对（模拟器没有取消选择）：模拟器只列出上一张之后的选项，所以按孪生这张在**全部未选的同 key 选项**里的名次配对（`Twin.prompt_copy_rank` / `Runner.later_pick`；游戏列出的同 key 张数与之不同时退回普通名次配对）。
- 选牌执行报失败（确认超时）时：读游戏的已选张数——没变 → 孪生不走；+1 → 孪生走这一步并照常提交 / 点确认；离开了选择 → 普通的试走；其余 → fail closed（`prompt_picked`，计数器 `prompt_select_failures`）。
- 决策记录多一个 `prompt_macro` 字段：`committed`、`picked`、`needs_confirm`、`bounds`，提交时还有 `status`、`steps`、
  `failure`；计数器 `prompt_selects`、`prompt_commits`、`prompt_commit_confirmed / closed_by_game / next_prompt / failed`、
  `prompt_commit_failures`、`prompt_cancel_rows`。
- 测试：`scripts/live/tests/test_prompt_macros.py`（假 MCP：exactly-1 手牌选择 HAND_TRICK 型、exactly-2 事件变形、
  up-to-N 的 DONE 与第 N 张自动确认、DECLINE = 跳过、DECLINE 不变成取消、取消行 fail closed、MACRO_FALLBACK 的 SMITH
  路径、三种 desync fail closed）；`test_dry_run.py` 的 `--macros all` 随机策略 dry run 与 `nn_twin_play --macros all`
  checkpoint dry run（0 desync、token 全同）。随机策略 dry run（SILENT 20 局 + IRONCLAD 11 局）里提交的选择 114 次：
  113 次替身在选中时自己关掉提示（`closed_by_game`），1 次（IRONCLAD C0VER35 第 4 层的“至多 2 张”手牌选择，需手动确认）
  按 `pick j; confirm` 执行——需确认的提示在随机打法里很少见，这条路径主要靠单元测试。
- **还没覆盖 / 没验证**：没在真实游戏里跑过；真实游戏的手牌选择在选中后是否把牌移出 `cards`、哪些提示游戏自己关闭、
  哪些要确认键，都只按 v0.111.0 的状态形状推断（规则对两种情况都成立，但要看第一次真机运行的 `prompt_macro.steps`）；
  同 id + 升级的副本按（未选副本中的）名次配对（手牌里费用被临时改过的副本分不出来）；一次升级多张的休息点（SmithCount ≠ 1）照旧走
  原始提示；combat lookahead 没有宏映射。

## 开发者：dry run 与测试

```bash
dotnet run -c Release --project sim/Sts2Sim.Server -- --port 8765 --no-crash-log &   # 或 scripts/sim_server.sh
nice -n 10 uv run --project sim/tools/train python scripts/live/nn_twin_play.py --checkpoint best.pt \
    --seed TW1NA10S01 --character SILENT --ascension 10 --out tmp/nn-twin/dry --dry-run-server http://127.0.0.1:8765
nice -n 10 uv run --project sim/tools/train pytest -q scripts/live/tests
```

代码：`scripts/live/nn_twin/`——`semantics.py`（语义动作、视图、匹配、对比）、`twin.py`（模拟器槽位 + checkpoint
策略 + 候选解码）、`mcp.py`（真实游戏：复用 `scripts/sts2_action_executor.py` 的 `request` / `wait_settled` /
`settled_state` 和 `sts2rec/legal_actions.py` 的 `derive_legal_actions`）、`standin.py`（play server 替身）、
`runner.py`（lockstep 主循环）。
