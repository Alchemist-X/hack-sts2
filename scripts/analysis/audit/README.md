# scripts/analysis/audit — 逐决策回放的行为审计脚本

对一个 checkpoint 在某个种子集上的**逐决策回放落盘**做行为统计。全部只用标准库，`python3 -I` 直接跑，不需要 torch / 模拟器。
首次使用对象：线 A `runs/w3m-a/checkpoints/pinned-0005064.pt`，`silent-a10-dev-v1` 128 局，greedy、loop breaker 关、
room macros `rest,shop`（2026-11-05）。结论文档：`docs/diag-2026-11-05/behaviour-audit-a-u5064.md`、`sly-check-a-u5064.md`。

## 输入：回放落盘 `<audit_dir>/{decisions,games}.jsonl`

由 `audit_dump.py` 在 Huginn（raven）的 python:3.13 容器里生成（CPU torch，8 线程，128 局约 20 分钟）。该驱动不改仓库代码：
它把 `sts2train.evaluate.greedy_policy` 包一层，动作仍由原贪心策略选出，包装层只额外解码"选这一步时的观测"并记录模型输出，
然后原样调用 `sts2train.evaluate.evaluate`（同样的步数上限 1200 / 楼层步数上限 80、checkpoint 自带的 macros、breaker 关、replay audit）。
驱动目前只在 raven 上：`/data/raven/hack-sts2/audit/src/audit_dump.py`（入口 `audit/run.sh`，用法
`./run.sh src/audit_dump.py --checkpoint ckpt/<pinned>.pt --seedset silent-a10-dev-v1 --out <dir> --threads 8`）；
线 A u5064 的落盘在 `/data/raven/hack-sts2/audit/a-u5064-dev128/`（与官方评测逐种子 result/act/floor 128/128 一致）。
落盘是数据，不进 git。

**`decisions.jsonl`**：每个决策一行，记录的是**动作之前**的状态。

- 定位：`g`（局 `SILENT-<seed>`）、`t`（局内决策序号，= 官方 `history_actions` 下标）、`act`、`floor`、`act_floor`、
  `phase`（Map / Combat / Rewards / CardReward / Shop / RestSite / Treasure / Event）、`dk`（= phase，有选择提示挂起时为 `Prompt`）、
  `node`（当前地图格 MapPointType）。
- 玩家：`hp`、`max_hp`、`gold`、`deck`（拾取顺序）、`relics`、`potions`。
- 决策：`cands`（候选行的紧凑字符串，按环境顺序）、`pr`（模型对每行的 softmax 概率，4 位小数）、`a`（所选行下标）、
  `chosen`（= `cands[a]`）、`p`（= `pr[a]`）、`kind`（所选行的 RunActionKind）、`card`（PlayCard 打出的牌）、
  `discard_enabler` / `discard_payoff` / `sly`（弃牌出口、弃牌收益牌、奇巧标记）、`win_prob`、`combat_hp_pred`（aux 头）、`event`。
- `combat`（战斗中）：`turn`、`energy`、`block`、`incoming`、`draw_n` / `discard_n` / `exhaust_n`、`hand`、`powers`、`enemies`
  （`slot` 即 `>eN` 的目标号、`id`、`hp`、`intent`、`powers`）。
- `prompt`（`dk` = Prompt）：`kind`（HandSelect / PileSelect / DeckUpgrade …）、`min` / `max` / `picked`、`options`、`site`
  （`prompt_site_hash` 的 hex）、`after`（战斗中此提示之前最后打出的牌或药水，`P:` 前缀为药水，通常就是提示来源）。
- 牌字符串：卡牌词表 id，每级升级一个 `+`；`*` = 此刻带奇巧（Sly），`!` = 手牌中当前不可打出；提示选项后缀 `#k` = 已作为第 k 个选中。
- 候选字符串：`Play:<card>[*][>eN]`、`EndTurn`、`Potion:<id>[>eN]`、`Travel:<类型>@r<行>c<列>[e<min>-<max>,rest<R>,shop<S>]`、
  `TakeCard:<card>` / `TakeAlt:<…>`（奖励界面宏）、`Claim:<…>`、`ProceedRewards`（离开奖励界面 = 跳卡）、`Buy:<…>$<价>`、
  `Remove:<card>$<价>`（商店删牌宏）、`LeaveShop`、`Rest:HEAL` / `Rest:SMITH:<card>`（锻造宏）、`LeaveRestSite`、`Event:<i>…`、
  `OpenChest` / `TakeRelic:<id>` / `SkipTreasureRelic`、`Select:<option>` / `PromptConfirm` / `PromptCancel`。
- 注意：Travel 方括号里的路线统计（`map_routes`）和提示选项上的静态奇巧 `*` 模型都**读不到**，只供分析。

**`games.jsonl`**：每局一行：`g`、`seed`、`result`（win / loss / capped / stuck / crashed）、`act`、`floor`、`progress`（协议 v0.2
进度，头号指标）、`decisions`、`turns`、`final_hp` / `max_hp`、`death`、`killer`、`final_room`、`final_deck` / `final_relics` /
`final_potions` / `final_gold` / `deck_n`、`upgraded`、`boss_entry`、`boss_fights` / `elite_fights` / `fights` / `n_fights`、
`history_actions_sha`、`replayable`、`win_prob_first` / `win_prob_last`。

完整字段表在 raven 落盘目录的 README.md 里。

## 脚本

| 脚本 | 用法 | 做什么 |
| --- | --- | --- |
| `behaviour_audit.py` | `python3 -I behaviour_audit.py <audit_dir> <repo_root> <out.json>` | 主审计：按结局分组（A1 死亡 / A2 途中死亡 / 到 A2 Boss）统计构筑方向（毒 / 小刀 / 弃牌奇巧 / 防御 / 混合 / 无方向）、关键牌选取率、卡牌奖励跳过率；弃牌出口与弃牌选择（对均匀随机基线）；路线（精英 / 火堆 / 商店 / 问号房偏好、每幕精英数）；火堆休息 / 锻造、商店购买与删牌、遗物与金币。打印表格，全部聚合写入 `out.json`。卡牌元数据读 `<repo_root>/knowledge/source/v0.111.0/cards.jsonl` 与 `sim/specs/inventory/cards-silent-defect.classified.json` |
| `behaviour_audit_extra.py` | `python3 -I behaviour_audit_extra.py <audit_dir> <repo_root> <out.json>` | 追加核对：满血休息、锻造行概率质量上限、Hand Trick 赋予奇巧后的去向、Act 2 开局时的构筑与是否到 A2 Boss（控制牌组增长的幸存者偏差）、Act 1 Boss 结果、走向各类格子的偏好与"被迫进精英"、商店删牌可用性、Flick Flack 手动打出、A2 Boss 逐回合细节。复用同目录 `behaviour_audit.py` 的分组与工具函数（读取其源码 `# --- load` 之前的部分），两文件须放在一起 |
| `sly_check.py` | `python3 -I sly_check.py <decisions.jsonl> <games.jsonl>` | 奇巧（Sly）专项：最终牌组里的自带奇巧牌、弃牌触发遗物、手里有奇巧牌的战斗决策占比、弃牌出口打出次数、出口之后的弃牌提示里选奇巧 / 诅咒状态 / 其他的次数、最常弃的牌。已知缺陷：Hand Trick / Master Planner 按 `discard_payoff` 计为 0，有误，以 `behaviour_audit.py` 的出牌计数为准 |
| `argmax_split.py` | `python3 -I argmax_split.py <decisions.jsonl>` | 核对贪心 argmax 是否掩盖了宏动作行之间的概率分摊：火堆 P(HEAL) 对 ΣP(SMITH:*)、商店 ΣP(Remove:*)、奖励界面 ΣP(TakeCard:*) 对 P(ProceedRewards) |
| `pick_rates.py` | `python3 -I pick_rates.py <decisions.jsonl> CARD [CARD ...]` | 指定卡牌在卡牌奖励中的"被选 / 出现"次数 |
| `pick_context.py` | `python3 -I pick_context.py <decisions.jsonl>` | 已有构筑是否影响选牌：牌组里已有 0 / 1–2 / 3+ 张毒（小刀）牌时，出现该族牌时选它的比例 |
| `discard_ethereal.py` | `python3 -I discard_ethereal.py <decisions.jsonl>` | 弃牌提示（7 个固定 `site`）中，可选项含虚无类（Ascender's Bane、Clumsy、Dazed，回合末本就消耗）或有害类（Beckon、Infection，留手回合末掉血）时选它们的次数与均匀基线 |

## 首次结果（线 A pinned-0005064，dev-128 贪心，47,375 个决策，128 局全负）

- 构筑：只有毒系有可辨认倾向（最终牌组 40/128 判为毒，小刀 8，弃牌奇巧 1，防御 0）；选牌像固定优先级表，不看已有构筑
  （Noxious Fumes 38/39；毒牌选取率在已有 0 / 1–2 / 3+ 张时为 61.8% / 58.4% / 55.8%）；Footwork、Accuracy、Tactician、Reflex 从不选。
- 奇巧：战术上会用（有奇巧可弃时 76/83 弃奇巧牌，Flick Flack 70/70），构筑上不追；Flick Flack 被花费用手动打出 157 次，
  Hand Trick 赋予奇巧后 41/56 仍被手动打掉。有害状态牌 38 次弃 37 次。
- 路线：能避开精英时 0/257 选精英，194 场精英战全是被迫进入；有火堆可选时 213/223 选火堆。
- 火堆 453/453 全部休息、从不锻造（含 35 次满血休息）；商店 203 个有删牌行的决策从不删牌；`argmax_split.py` 核实
  P(HEAL)=1.000、SMITH / Remove 行概率 0.000，不是贪心摊薄，训练期采样也碰不到 → 第三轮 E′ 的探索修复（`docs/round3-plan-2026-11-05.md`）。
- 跳卡率 6.3%，牌组膨胀到 28–32 张；A2 Boss 21 局只过 2 局，死于输出起量慢。
- 环境侧：弃牌提示的选项行不带关键词位（模型只能靠卡牌嵌入识别奇巧）；模型不读 `card_static`、`map_routes`、事件数值
  （见 `docs/diag-2026-11-05/env-information-check.md`）。
