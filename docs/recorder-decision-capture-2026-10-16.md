# Recorder 0.4.0：候选完整性与选择前状态

这份说明来自 2026-10-16 的归档分支 `archive/oct-2026-recorder-decision-capture-and-training`
（7665421，当时标为 "0.3"）。2026-11 把它移植到 recorder 0.3.1（7 月主线程卡顿修复）之上，
发布为 **recorder 0.4.0**；归档里的 "0.3" / "0.3.1" 版本号已被占用，不再使用。
JSONL 外层仍为 `schema_version` 1，只新增可选字段。

移植时与归档不同的地方：

- **战斗输入的采样默认关闭。** 归档在每次手动出牌、结束回合、使用药水时都同步构建一次完整快照
  （约 8 ms 主线程工作），这会把 0.3.1 刚去掉的卡顿放回每一次战斗输入。0.4.0 用
  `Sts2Recorder.conf` 的 `decision_capture_combat_inputs`（默认 `false`）控制这一类采样；
  关闭时这些动作保持 0.3.1 行为（`state_seq` = 动作前最近的快照，无 `_decision`）。
  生效值写在 `recorder_config` 事件里。非战斗决策采样（奖励、地图、事件、商店、篝火、
  选牌/网格界面、战斗外的药水和宝箱遗物）始终开启；战斗内的手牌/牌堆选择属于选择界面，也始终开启。
- 决策采样绕过 0.3.1 的突发合并（burst coalescing）和轮询节流，同步返回状态 seq，
  动作引用的就是这次选择前快照；它同样会重置两个节流窗口。
- 主牌组沿用 0.3.0 的 R3 格式（`{id, upgrade_level, enchantment}`，按牌组顺序），没有采用归档的牌组块。
- 奖励卡牌沿用 0.3.0 的 `BuildOfferedCard`（带 `upgrade_level`、`enchantment`），再加 `choice_index`；
  `reward_taken` 同时保留 0.3.0 的 `offer` / `picked_cards` 和归档的领取前快照引用。
- 事件选项点击：0.3.0 的点击索引与归档的选择前快照合并为同一张表（`EventInput`）。
- 归档的哈希/去重重写、`capture_main_thread_ms`、`record_state_ms` 已被 0.3.1 取代，未移植，
  见 [性能说明](recorder-performance-2026-10-16.md)。

## 旧实现为什么漏录

旧实现优先保证"动作和结算写进日志"，没有把"选择前完整候选"作为独立验收条件。

1. **触发点不覆盖决策界面的生命周期。** 房间进入时 UI 可能尚未构建完；奖励按钮打开三选一不会立即
   产生已提交动作，战斗状态变化也不会替这个界面触发快照。地图打开、事件页面准备完成、升级预览与
   取消同样缺少专门触发。
2. **最近快照与选择前快照混为一谈。** `SyncLocalChoice` 发生在等待 UI 返回结果之后，部分界面已经关闭。
   仅在这个函数加一次采样仍然太晚。异步奖励、商店和篝火回调也可能引用子流程或后续页面的快照。
3. **候选来源不完整。** UI 网格只实例化当前可见的一部分卡牌；按控件遍历会遗漏需要滚动才能看到的候选。
   战斗牌堆选择更特殊：界面继承的 `_cards` 始终初始化为空，动态完整候选由 `_grid._cards` 持有，
   后者已按公开显示规则排序，不应改读原始抽牌顺序。
4. **事件读错对象。** `CanonicalEvent` 是模板，当前玩家的页面、描述、条件和候选属于 `LocalMutableEvent`
   （0.3.0 已改为从它读正文与页面键，0.4.0 起选项也从它的 `CurrentOptions` 读）。
5. **版本适配只覆盖了结果。** 奖励替代操作已统一为 Skip、Reroll 和遗物附加操作；原实现把"存在任何替代按钮"
   简化为 `can_skip=true`，丢失身份与同步索引。
6. **测试与审计的判据偏窄。** 序号单调、引用存在、JSON 可解析、hook 未报错，都不能证明引用的是正确界面，
   更不能证明候选齐全。

归档当时对一局前 23 个录制分段的基线检查：426 条出牌记录均引用战斗快照；32 条选路记录只有 9 条引用地图快照；
21 条事件选择中 17 条引用的选项为空；只有 1 条 `card_reward` 快照，但有 20 条楼层摘要包含 `card_choices`。
这些是分段记录计数，包含续玩/回滚，不是独立决策的丢失率。楼层摘要能补救部分历史候选，但不能反向伪造当时的界面快照。
战斗轮询节流不是这些问题的主要原因：没有触发采样的非战斗界面，调低节流也不会被可靠记录。

## 修复方式

- `DecisionHooks` 在奖励选择等待、候选刷新、网格选择等待、点击/确认/取消、手牌选择、地图打开/刷新、
  事件 `SetOptions` 时采样。所有入口都是被动读取，不执行选择、不重新生成随机候选。
- `ReserveChoiceId` → UI await 绑定屏幕实例 → 提交前捕获 → `SyncLocalChoice(player, choiceId)` 消费。
  嵌套选择和同屏重掷按 ID 隔离，后来的快照不能覆盖已固定的选择前状态。同一绑定内的同步嵌套输入只采样一次
  （`DecisionChoices.InputScope`）。
- 篝火父选择（`BeforePlayerOptionChosen`）与升级子选择分别固定快照。地图在改变投票/可走状态前捕获；事件在按钮
  被清空前捕获。奖励领取和商店异步购买也保留开始前的观察、价格与会话身份。
- 每个延迟提交携带原会话引用，旧局结束后的回调不能写进新局。
- 奖励候选来自 `_options`，替代选项来自 `_extraOptions`；网格来自完整候选模型列表，战斗牌堆使用网格内已公开
  排序的完整列表。控件销毁延迟、焦点改变顺序、滚动和升级预览不会改变候选集合。
- 记录选择数量上下限、确认约束、已选牌。卡牌的 recorder `instance_id` 与动作中的 `card_instances`
  区分同名的不同实例，仅在同一录制进程内关联，不是跨分段持久 ID。
- 事件保存当前页面的选项、锁定条件、`text_key` 与实际索引；`options_source` 说明选项来源。
  奖励保存每个替代选项的 ID、标题、`choice_index` 和后续行为；`can_skip` 只表示确有 Skip。
- 地图状态增加 `is_open`、`travel_enabled`。

新增字段示意：

```json
{
  "action": {
    "kind": "player_choice",
    "params": {
      "choice_id": 5,
      "cards": ["CARD.GUARDS"],
      "card_instances": [{"id": "CARD.GUARDS", "instance_id": 12}],
      "_decision": {"status": "captured", "stage": "before_input"}
    }
  },
  "state_seq": 379
}
```

`state_seq` 指向该选择固定的快照，可能早于多条无关新快照。`choice_index` 使用游戏同步选择的候选索引；
不要把它当成像素位置或旧版 MCP 的可见控件索引。采样失败或选择未绑定时显式写 `state_seq:null`、
`_decision.status:"missing"`；canonical 不用未来快照替代这种明确失败。旧数据的历史兼容行为保留，
并有 `state_before_estimated` 标记。

`sts2rec canonical` 增加 `info.decision_audit`；`sts2rec validate` 增加语义覆盖警告，包括错误界面、空候选、
候选读取失败、所选卡牌/索引/路线不在候选中，以及只捕获打开界面却没有观察到提交前输入。结构校验通过与
决策覆盖通过是两回事；覆盖问题是 warning，调用方必须检查。

## 离线验证

```bash
dotnet build mod/Sts2Recorder.csproj -c Release
dotnet test mod/tests/Sts2Recorder.Core.Tests.csproj
scripts/verify_core.sh
cd sts2rec && uv run pytest -q
```

只读程序集兼容检查（`scripts/hook_drift.py` 不检查 `decision:` 前缀的钩子，由它覆盖）：

```bash
dotnet run --project scripts/RecorderContractCheck -c Release -- \
  mod/bin/Release/net9.0/Sts2Recorder.dll \
  "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"
```

它在实际游戏程序集上解析 55 个钩子目标/注入签名（`DecisionHooks.GetPatchPlan()` 的 52 个，加上商店购买、
卡牌移除、奖励领取 3 个 `__state` 前后缀对）和 26 个候选字段，不初始化游戏、不打 Harmony 补丁、不访问游戏存档。
2026-11-02 在 v0.111.0（`41cef1ea`）上通过。

## 仍需隔离实机验收

编译、核心测试和程序集元数据匹配不能证明所有 Godot UI 时序都已运行验证。安装到隔离实例（不是用户正在玩的那份）后，至少覆盖：

1. 战后选牌、跳过、重掷两次，以及遗物提供的替代按钮；候选与实际显示逐项核对。
2. 长牌组滚动升级、多选、预览后取消再选另一张、关闭选择，以及升级与篝火父操作不同的 `state_seq`。
3. 事件换页、锁定选项、古代对话结束、事件中的选牌子流程。
4. 普通分支与允许特殊移动的遗物；实际目的地必须在对应快照候选中。
5. 战斗牌堆动态变化、手牌多选、同名不同附魔卡牌，以及读档后不串用上一段 ID。
6. 主线程 p95/p99（manifest `perf.capture_by_trigger_ms` 的 `decision:*` 项）、每次操作实际采样次数和写盘积压；
   如要打开 `decision_capture_combat_inputs`，先在这一步量出战斗输入的代价。

每项对照界面和动作记录检查 `decision_audit`，不能只看文件生成或 `degraded_hooks: []`。
旧轨迹不会自动补齐；未走分支的结果和隐藏 RNG 仍不属于可记录候选。
