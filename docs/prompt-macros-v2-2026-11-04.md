# Prompt macros v2 / 选择提示宏 v2（2026-11-04，W4，owner G）

## 结论 / Summary

- 新增 `macros` 名 `prompt` 与别名 `all`（= `rest,shop,prompt`）。开启后**任何**待决选择提示都不再列出
  `PromptCancel`；一次选牌若使 Confirm 成为唯一出路，则同一步内自动确认；`PromptConfirm` 行只在游戏本身允许时出现，
  0 张时即“放弃（DECLINE）”，有选牌时即“完成（DONE）”。只列出游戏选择过滤器放行的选项（不新增、不泄露隐藏信息）。
- 循环来源只有 4 个可取消站点（`PromptSites.RoomSources`：SMITH、COOK、MERCHANT_CARD_REMOVAL、
  COMBAT_REWARD_CARD_REMOVAL），取消都回到打开它的同一画面。其余全部提示（战斗卡牌效果、事件、遗物、药水）都不可取消，
  选择单调递增（游戏没有“取消选中”），本身不会成环。
- ModelSpec / encoder_version / RunActionKind **不变**：宏行沿用 PromptSelect / PromptConfirm 的种类与列，
  执行语义在 Vec 层改变。默认（宏关）路径逐字节不变；`rest,shop` 保持 W3 行为（不加上限）。
- 64 行上限：开 `prompt` 时，休息点 / 商店宏行若会使候选超过 64，退回原始 SMITH / 删卡行（随后是无取消的顺序提示），
  并在该观测的 status 置 `MACRO_FALLBACK`（512，追加位）。提示本身选项 > 64 时只能顺序（训练端照旧计入
  `cand_over_max_rows`）。

English: `macros` gains `prompt` and the alias `all`. With it no prompt lists a cancel row, a pick that leaves
Confirm as the only way on confirms in the same step, the Confirm row exists only where the game allows declining /
finishing, and only the game's selectable options are listed. Spec unchanged; default path unchanged.

## 设计选择 / Design decisions

1. **exactly-k 用“顺序选牌 + 第 k 张自动提交、无取消”，不用组合行。** 候选表一行只有一个 `card_id` 列，组合行要么加列
   （ModelSpec 变宽），要么只编码一张卡（信息不全）。顺序方案的每一步都可观测：`prompt_options.picked`、
   `global_i.prompt_picked` 已显示已选；选择单调，不可能回到同一观测。故保持 ModelSpec 不变。
   / exactly-k = k sequential picks committing at the k-th, no cancel, no combination rows: the candidate table has
   one card_id column per row, so combination rows would need a wider spec; the sequential state is already in the
   observation (`picked`, `prompt_picked`).
2. **“打开”步骤只在选项事先可见且确定时合并**（W3 的 SMITH 与商店删卡：选项 = 牌组中可升级 / 可删除的牌）。事件选项、
   打出卡牌、使用药水在执行前选项未知（如先抽牌再选、随机生成的牌），预先展开会泄露执行后的信息，因此保持“先打开、
   再在提示中选”，但提示内无法回退。COOK（选 2 张）与 RemoveCard 奖励也走无取消的提示（2 步 / 3 步，均不可成环）。
   / The open click is merged only where the options are known beforehand (W3 rest / shop). Event options, card
   plays and potions keep their open click: their options exist only after it (look-ahead would leak).
3. **取消行一律去掉**；只有当提示除取消外无任何行时保留（移植中未出现）。放弃只走游戏自己的 0 张确认 / CanSkip。
4. **自动确认的判据**：同一提示仍待决，且其合法动作去掉取消后只剩 `PromptConfirm`。覆盖 exactly-1 + 手动确认
   （牌组提示）、exactly-k + 手动确认（COOK）、up-to-N 的第 N 张（PURITY 第 3 张）以及无序提示选到末尾的情形。
5. **观测不改**：`prompt_can_cancel` 仍描述游戏提示本身（宏开时可能为 1 但无取消行）；只改候选列表与执行。

## G1 清单 / Inventory of prompt kinds

站点数为 `sim/Sts2Sim.Core` 中调用点的 grep 计数（证据：本文件 §证据）。决策量为 dev-128 回放中的提示决策
（docs/diag-2026-11-04 数据：HandSelect 8,773 / ChooseCard 870 / PileSelect 163 / HandUpgrade 4）。
可产生提示的模型共 153 个（`PromptSites.ModelCount`：卡 57+3、药水 10、遗物 24+9、能力 10、怪物 2、附魔 1、事件 37、
房间来源 4）。

| 提示种类 Kind | 出现位置 Where | 选择数 Picks | 游戏可取消 Cancel in game | 原流程能否成环 Loop (old) | 宏 v2 行为 Macro v2 |
|---|---|---|---|---|---|
| HandSelect（手牌选择） | 战斗卡牌效果：`FromHand` 23 处（卡 18、药水 2、能力 2、遗物 1），`FromHandForDiscard` 8 处；例 HAND_TRICK（仅技能且本回合非 sly）、DECISIONS_DECISIONS（仅可打出的技能）、HIDDEN_DAGGERS（弃 2）、PURITY（消耗 0..3） | exactly 1 / exactly k（无确认）；up-to-N（Min≠Max，手动确认） | 否 No | 否（选择单调）No | 每张合法牌一行；k 张时顺序选、第 k 张提交；up-to-N 加 DONE 行；无取消 |
| HandUpgrade（手牌升级） | 战斗：`FromHandForUpgrade` 1 处（仅可升级牌；≤1 张时不弹出） | exactly 1 | 否 | 否 | 每张可升级牌一行，一步完成 |
| PileSelect（牌堆选择） | 战斗：`FromCombatPile` 17 处（卡 13、药水 2、能力 2）；例 SECRET_TECHNIQUE（抽牌堆中的技能） | exactly k / up-to-N | 否 | 否 | 同 HandSelect |
| ChooseCard（三选一） | 战斗：`FromChooseACardScreen` 10 处（卡 4、怪物 1、药水 4、遗物 1）；遗物版 2 处 | exactly 1，或 0..1（CanSkip） | 否（Skip = 0 张确认） | 否 | 每张一行；CanSkip 时一行 DECLINE（PromptConfirm） |
| SimpleGrid（网格选牌） | 遗物 `FromSimpleGrid` 1 处；事件网格（ROOM_FULL_OF_CHEESE 等，EventKit） | exactly k / up-to-N | 否 | 否 | 顺序选，第 k 张提交 / DONE |
| DeckUpgrade（牌组升级） | 休息点 SMITH（`RoomPrompts`，**可取消**，手动确认）；事件（AROMA_OF_CHAOS、SAPPHIRE_SEED、SPIRIT_GRAFTER、TRIAL k 张）；遗物 2 处 | exactly SmithCount / 1 / k | SMITH 是；其余否 | **SMITH 是**（SMITH→选→取消→同一休息点；u2225 dev-128 176 次） | rest 宏：每张可升级牌一行（>64 行时退回 SMITH 行 + 无取消提示）；其余：一行一张，自动确认 |
| DeckTransform（牌组变形） | 事件（AROMA_OF_CHAOS、MORPHIC_GROVE k、TRIAL k、WHISPERING_HOLLOW、SYMBIOTE k、ENDLESS_CONVEYOR）；遗物 3 处 | exactly 1 / k | 否 | 否 | 顺序选，第 k 张提交 |
| DeckEnchant（牌组附魔） | 事件（GRAVE_OF_THE_FORGOTTEN、WATERLOGGED_SCRIPTORIUM、SELF_HELP_BOOK 仅限某类型、SAPPHIRE_SEED、SPIRALING_WHIRLPOOL、STONE_OF_ALL_TIME、FIELD_OF_MAN_SIZED_HOLES、SYMBIOTE、WOOD_CARVINGS）；遗物 7 处 | exactly 1 / k | 否 | 否 | 只列 `CanEnchant` 放行的牌；同上 |
| DeckRemove（牌组删除） | 商店删卡（**可取消**）；休息点 COOK（选 2，**可取消**）；RemoveCard 奖励（**可取消**）；事件（LUMINOUS_CHOIR k、FIELD_OF_MAN_SIZED_HOLES k、DOORS_OF_LIGHT_AND_DARK、AMALGAMATOR 按标签过滤、ZEN_WEAVER k、WELLSPRING）；遗物 6 处 | exactly 1 / 2 / k（可取消站点均手动确认） | 3 个房间站点是；其余否 | **是**（商店删卡 / COOK / 奖励删卡 → 取消 → 同一画面） | shop 宏：每张可删牌一行；COOK / 奖励删卡：无取消，顺序选，末张自动确认 |
| DeckGeneric（牌组通用） | 事件 WOOD_CARVINGS 2 处（仅基础且可变形）；遗物 1 处 | exactly 1 | 否 | 否 | 一行一张，一步 |
| RewardsGrid（奖励网格） | 遗物 1 处（`FromSimpleGridForRewards`） | per prefs | 否 | 否 | 顺序 / DONE |
| Bundle（卡包三选一） | 遗物 1 处（`FromChooseABundleScreen`） | exactly 1 | 否 | 否 | 每包一行（card_id = 0） |
| CardReward（提示种类） | 移植中无构造点（0） | — | — | — | — |
| （非提示）CardReward 阶段 | 奖励卡界面打开后（REROLL 后 / 多选奖励）；Skip 回到奖励界面但奖励未完成 | 1 + Skip | Skip 相当于返回 | 理论上可（需再次打开同一奖励）；见 G3 环检查 | 不改（W3 行为）；`scope="all"` 检测器把它当 UI 画面计数 |

“relic / potion picks” 均以上表种类出现（遗物获得时的牌组提示、药水的 ChooseCard / HandSelect / PileSelect）；
宝箱遗物（TakeTreasureRelic）与卡牌奖励（TakeRewardCard 宏）不是选择提示。

## Overflow / 上限

- `PromptMacros.MaxRows = 64`（训练端 `--max-rows` 默认值）。仅 `prompt` 开时生效；`rest,shop` 不受影响（W3 测试仍比较
  W3 列表）。
- 退回时该观测 status 置 `MACRO_FALLBACK`；Python `STATUS["MACRO_FALLBACK"] == 512`。训练端已有的
  `cand_over_max_rows`（cand_n > max_rows 的决策数）照常统计无法避免的情形（提示选项本身 > 64）。

## 测试 / Tests

- C#（`sim/Sts2Sim.Tests/VecTests.PromptMacros.cs`）：`Test_VecConfig_PromptMacrosParse`；
  `Test_VecCand_PromptMacroHandSelectSkillsOnly`（HAND_TRICK：选项 = 手中技能，攻击牌不在列，单步 = 手动点击，StateHash
  相等）；`Test_VecCand_PromptMacroUpToNHasDoneNoCancel`（PURITY：DONE / 第 3 张自动确认，均 = 手动序列；宏关列表 = 游戏动作）；
  `Test_VecCand_PromptMacroExactlyKCommitsAtK`（HIDDEN_DAGGERS 弃 2）；`Test_VecCand_PromptMacroCookNoCancel`（COOK：宏关有取消，
  宏开无取消，= COOK、选、选、确认）；`Test_VecCand_PromptMacroRestFallbackPastMaxRows`（90 张可升级：rest,shop 照旧展开，all
  退回 + MACRO_FALLBACK，= SMITH、选、确认）；`Test_VecEnv_PromptMacrosSoak`（32 槽 × 3000 步，无 SIM_ERROR，永不出现取消行）。
- Python：`sim/tools/vec/tests/test_prompt_macros.py`（配置；64 槽随机 400 步：宏关有取消行，宏开 0）；
  `sim/tools/train/tests/test_prompt_macros.py`（ModelSpec 宏开关一致；`CycleDetector(scope="all")` 在 64 槽 × 2000 步
  随机策略下宏开 0 环、宏关 > 0；结果写入 testbox `tmp/prompt-macros/cycle_check.json`）。

## 证据 / Evidence pointers

- 可取消站点：`sim/Sts2Sim.Core/Run/Rooms/RestSiteOption.cs:89,139`、`Run/Rooms/MerchantEntry.cs:500`、
  `Run/Rewards/BasicRewards.cs:340`；其余 `CardSelectorPrefs.Cancelable` 默认 false（`Choice/CardSelectorPrefs.cs`）。
- 选择合法动作：`Combat/Loop/LegalActionGenerator.cs` `FillPrompt`（无序提示只允许升序选，保证每个组合唯一）；
  提示推进：`Run/Loop/RunSim.cs` `StepPrompt`（未完成的选牌只追加到 Picks，提示对象不变）。
- 宏实现：`sim/Sts2Sim.Vec/PromptMacros.cs`、`RoomMacros.cs`（`Apply(..., maxRows, out fellBack)`）、`VecEnv.cs`
  （Observe / StepSlot）、`Status.cs`（`MacroFallback = 512`）；检测器：`sim/tools/train/sts2train/cycles.py`（`scope`）。
